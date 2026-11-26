using System;
using System.Collections.Generic;
using System.Diagnostics;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.LowLevel;

namespace HalfGripOpenWorld
{
	/// <summary>
	/// Where the whole frame goes, with ShowCoords on (the performance handoff, 0.32.4): the owner's 0.32.2 drive showed the traffic's own
	/// code at about 0.1 ms a frame while the frame rate on the highway stayed at 100 to 110 against 135 before, so the rest had to be
	/// timed. Three views, each averaged over a second, on the overlay and logged every 10 s as <c>Frame timings: ...</c>:
	///
	/// - Unity's own frame, step by step: a marker is put after every step of Unity's player loop (physics, scripts' updates, animation,
	///   rendering, waiting for the GPU or the screen...), so each step's time is the time since the marker before. The slowest steps are
	///   shown by name. That tells the game's physics and rendering from our scripts, and the CPU from waiting on the GPU, which no
	///   stopwatch round our own code can.
	/// - Our own systems: the highway's wrap, the origin shift, streaming (and inside it the tiles made: their meshes and colliders), the
	///   far view, foliage, the sky, the deer, the traffic.
	/// - What the renderer counts (draw calls, batches, triangles, shadow casters) and the GPU's and render thread's own frame times, where
	///   the game's build gives them (otherwise "n/a").
	///
	/// The markers go into the player loop only while ShowCoords is on (<see cref="Want"/>, from <c>Streamer</c> each frame) and come out
	/// with the world: off, nothing here costs anything.
	/// </summary>
	internal static class FramePerf
	{
		/// <summary>Our own systems' parts.</summary>
		internal enum Part { Wrap, Origin, Stream, Tiles, FarView, Foliage, Sky, Deer, Traffic, Count }

		private static readonly string[] partNames = { "wrap", "origin", "streaming", "tiles made", "far view", "foliage", "sky", "deer", "traffic" };

		/// <summary>The type every marker carries, so they can all be found and taken out again.</summary>
		private struct Mark { }

		private static bool installed;
		private static readonly List<string> stepNames = new List<string>();
		private static long[] stepSum = new long[0];
		private static float[] stepMs = new float[0];
		private static long last;
		/// <summary>The marker just before the time being measured; -1: the time between the end of one frame and the start of the next.</summary>
		private const int Between = -1;
		private static long betweenSum;
		private static float betweenMs;

		private static readonly long[] partSum = new long[(int)Part.Count];
		private static readonly float[] partMs = new float[(int)Part.Count];
		private static int tilesMade, tilesShown;

		private static long frameStart;
		private static int frames, shownFrames, slowFrames, shownSlow;
		private static long seconds, worst;
		private static float shownFrame, shownWorst, sinceLog;

		private static ProfilerRecorder drawCalls, batches, setPass, triangles, shadowCasters, gcAlloc;
		private static double drawSum, batchSum, setPassSum, triSum, shadowSum, gcSum;
		private static float drawShown, batchShown, setPassShown, triShown, shadowShown, gcShown;
		private static readonly FrameTiming[] timing = new FrameTiming[1];
		private static double gpuSum, renderSum, mainSum;
		private static int timedFrames;
		private static float gpuShown = -1f, renderShown = -1f, mainShown = -1f;

		internal static long Now => Stopwatch.GetTimestamp();

		internal static bool On => installed;

		internal static void Add(Part p, long since)
		{
			if (installed)
			{
				partSum[(int)p] += Stopwatch.GetTimestamp() - since;
			}
		}

		internal static void TileMade()
		{
			if (installed)
			{
				tilesMade++;
			}
		}

		/// <summary>Called every frame by <c>Streamer</c>: puts the markers in or takes them out as ShowCoords is turned on or off.</summary>
		internal static void Want(bool on)
		{
			if (on == installed)
			{
				return;
			}
			if (on)
			{
				Install();
			}
			else
			{
				Remove();
			}
		}

		private static void Install()
		{
			try
			{
				PlayerLoopSystem loop = PlayerLoop.GetCurrentPlayerLoop();
				Strip(ref loop);
				stepNames.Clear();
				if (loop.subSystemList == null)
				{
					return;
				}
				PlayerLoopSystem[] phases = loop.subSystemList;
				for (int p = 0; p < phases.Length; p++)
				{
					PlayerLoopSystem phase = phases[p];
					string phaseName = phase.type != null ? phase.type.Name : "Phase" + p;
					List<PlayerLoopSystem> steps = new List<PlayerLoopSystem>();
					// The very first marker of the frame starts it (the time before it is between frames: the wait for the screen or a
					// frame cap, and whatever Unity does outside its loop); every phase's first marker takes what came between phases.
					steps.Add(Marker(p == 0 ? -2 : Between));
					if (phase.subSystemList != null)
					{
						foreach (PlayerLoopSystem step in phase.subSystemList)
						{
							steps.Add(step);
							stepNames.Add(phaseName + "." + (step.type != null ? step.type.Name : "?"));
							steps.Add(Marker(stepNames.Count - 1));
						}
					}
					phase.subSystemList = steps.ToArray();
					phases[p] = phase;
				}
				loop.subSystemList = phases;
				stepSum = new long[stepNames.Count];
				stepMs = new float[stepNames.Count];
				PlayerLoop.SetPlayerLoop(loop);
				installed = true;
				last = frameStart = 0;
				sinceLog = 0f;
				shownFrames = 0;
				text = null;
				ResetCounts();
				drawCalls = Recorder(ProfilerCategory.Render, "Draw Calls Count");
				batches = Recorder(ProfilerCategory.Render, "Batches Count");
				setPass = Recorder(ProfilerCategory.Render, "SetPass Calls Count");
				triangles = Recorder(ProfilerCategory.Render, "Triangles Count");
				shadowCasters = Recorder(ProfilerCategory.Render, "Shadow Casters Count");
				gcAlloc = Recorder(ProfilerCategory.Memory, "GC Allocated In Frame");
				Mod.Info("Frame timings on: " + stepNames.Count + " steps of Unity's frame marked; the GPU's frame time " + (FrameTimingManager.IsFeatureEnabled() ? "available" : "not available in this build")
					+ "; draw calls " + (drawCalls.Valid ? "counted" : "not counted in this build") + ", GC allocations " + (gcAlloc.Valid ? "counted" : "not counted in this build"));
			}
			catch (Exception e)
			{
				Mod.Log.LogWarning("Frame timings could not be put into Unity's frame: " + e.Message);
				installed = false;
			}
		}

		private static ProfilerRecorder Recorder(ProfilerCategory category, string name)
		{
			try
			{
				return ProfilerRecorder.StartNew(category, name);
			}
			catch (Exception)
			{
				return default;
			}
		}

		/// <summary>Takes the markers out of Unity's frame (ShowCoords turned off, or the world gone).</summary>
		internal static void Remove()
		{
			if (!installed)
			{
				return;
			}
			installed = false;
			try
			{
				PlayerLoopSystem loop = PlayerLoop.GetCurrentPlayerLoop();
				Strip(ref loop);
				PlayerLoop.SetPlayerLoop(loop);
			}
			catch (Exception e)
			{
				Mod.Log.LogWarning("Frame timings could not be taken out of Unity's frame: " + e.Message);
			}
			Dispose(ref drawCalls);
			Dispose(ref batches);
			Dispose(ref setPass);
			Dispose(ref triangles);
			Dispose(ref shadowCasters);
			Dispose(ref gcAlloc);
			text = null;
		}

		private static void Dispose(ref ProfilerRecorder r)
		{
			try
			{
				if (r.Valid)
				{
					r.Dispose();
				}
			}
			catch (Exception)
			{
			}
			r = default;
		}

		private static void Strip(ref PlayerLoopSystem system)
		{
			if (system.subSystemList == null)
			{
				return;
			}
			List<PlayerLoopSystem> kept = new List<PlayerLoopSystem>(system.subSystemList.Length);
			foreach (PlayerLoopSystem s in system.subSystemList)
			{
				if (s.type == typeof(Mark))
				{
					continue;
				}
				PlayerLoopSystem child = s;
				Strip(ref child);
				kept.Add(child);
			}
			system.subSystemList = kept.ToArray();
		}

		private static PlayerLoopSystem Marker(int after)
		{
			PlayerLoopSystem.UpdateFunction tick;
			if (after == -2)
			{
				tick = Start;
			}
			else
			{
				tick = () => Tick(after);
			}
			return new PlayerLoopSystem { type = typeof(Mark), updateDelegate = tick };
		}

		private static void Tick(int after)
		{
			long now = Stopwatch.GetTimestamp();
			if (last != 0)
			{
				if (after == Between)
				{
					betweenSum += now - last;
				}
				else if (after < stepSum.Length)
				{
					stepSum[after] += now - last;
				}
			}
			last = now;
		}

		/// <summary>The frame's first marker: the time since the last frame's end is between frames; a whole frame done, and once a second the averages.</summary>
		private static void Start()
		{
			long now = Stopwatch.GetTimestamp();
			if (last != 0)
			{
				betweenSum += now - last;
			}
			last = now;
			if (frameStart != 0)
			{
				long frame = now - frameStart;
				frames++;
				seconds += frame;
				worst = Math.Max(worst, frame);
				if (frames > 1 && frame * frames > 2 * seconds)
				{
					// Twice the second's average so far: a hitch.
					slowFrames++;
				}
				Count();
				if (seconds >= Stopwatch.Frequency)
				{
					Average();
				}
			}
			frameStart = now;
		}

		/// <summary>Once a frame: the renderer's counts and the GPU's time for the frame before.</summary>
		private static void Count()
		{
			drawSum += Value(drawCalls);
			batchSum += Value(batches);
			setPassSum += Value(setPass);
			triSum += Value(triangles);
			shadowSum += Value(shadowCasters);
			gcSum += Value(gcAlloc);
			try
			{
				FrameTimingManager.CaptureFrameTimings();
				if (FrameTimingManager.GetLatestTimings(1, timing) > 0 && timing[0].gpuFrameTime > 0.0)
				{
					gpuSum += timing[0].gpuFrameTime;
					renderSum += timing[0].cpuRenderThreadFrameTime;
					mainSum += timing[0].cpuMainThreadFrameTime;
					timedFrames++;
				}
			}
			catch (Exception)
			{
				// Not in this build: shown as n/a.
			}
		}

		private static double Value(ProfilerRecorder r)
		{
			return r.Valid ? r.LastValue : double.NaN;
		}

		private static void Average()
		{
			double toMs = 1000.0 / Stopwatch.Frequency / frames;
			for (int i = 0; i < stepSum.Length; i++)
			{
				stepMs[i] = (float)(stepSum[i] * toMs);
				stepSum[i] = 0;
			}
			betweenMs = (float)(betweenSum * toMs);
			betweenSum = 0;
			for (int i = 0; i < partSum.Length; i++)
			{
				partMs[i] = (float)(partSum[i] * toMs);
				partSum[i] = 0;
			}
			shownFrame = (float)(seconds * toMs);
			shownWorst = (float)(worst * 1000.0 / Stopwatch.Frequency);
			shownFrames = frames;
			shownSlow = slowFrames;
			tilesShown = tilesMade;
			drawShown = (float)(drawSum / frames);
			batchShown = (float)(batchSum / frames);
			setPassShown = (float)(setPassSum / frames);
			triShown = (float)(triSum / frames);
			shadowShown = (float)(shadowSum / frames);
			gcShown = (float)(gcSum / frames);
			gpuShown = timedFrames > 0 ? (float)(gpuSum / timedFrames) : -1f;
			renderShown = timedFrames > 0 ? (float)(renderSum / timedFrames) : -1f;
			mainShown = timedFrames > 0 ? (float)(mainSum / timedFrames) : -1f;
			sinceLog += (float)(seconds / (double)Stopwatch.Frequency);
			ResetCounts();
			text = Compose();
		}

		private static void ResetCounts()
		{
			frames = slowFrames = tilesMade = timedFrames = 0;
			seconds = worst = 0;
			drawSum = batchSum = setPassSum = triSum = shadowSum = gcSum = 0.0;
			gpuSum = renderSum = mainSum = 0.0;
		}

		/// <summary>True every 10 s (the overlay's lines are then logged too).</summary>
		internal static bool LogDue()
		{
			if (!installed || sinceLog < 10f)
			{
				return false;
			}
			sinceLog = 0f;
			return true;
		}

		private static string text;

		/// <summary>The overlay's lines (ms per frame, averaged over the last second), or null before the first second. Made once a second, not every OnGUI.</summary>
		internal static string Text()
		{
			return installed ? text : null;
		}

		private static string Compose()
		{
			System.Text.StringBuilder s = new System.Text.StringBuilder(600);
			s.Append("frame ").Append(shownFrame.ToString("0.0")).Append(" ms (").Append(shownFrames).Append(" fps), worst ").Append(shownWorst.ToString("0.0"))
				.Append(" ms, ").Append(shownSlow).Append(" over twice the average");
			s.Append("; GPU ").Append(Ms(gpuShown)).Append(", render thread ").Append(Ms(renderShown)).Append(", main thread ").Append(Ms(mainShown));
			s.Append("\nUnity's slowest steps:");
			// The six slowest steps and the time between frames, by name.
			int[] order = new int[stepMs.Length];
			for (int i = 0; i < order.Length; i++)
			{
				order[i] = i;
			}
			Array.Sort(order, (a, b) => stepMs[b] != stepMs[a] ? stepMs[b].CompareTo(stepMs[a]) : a.CompareTo(b));
			for (int k = 0; k < Math.Min(6, order.Length); k++)
			{
				s.Append(k == 0 ? " " : ", ").Append(stepNames[order[k]]).Append(' ').Append(stepMs[order[k]].ToString("0.00"));
			}
			s.Append(", between frames ").Append(betweenMs.ToString("0.00"));
			s.Append("\nours:");
			for (int i = 0; i < partMs.Length; i++)
			{
				s.Append(i == 0 ? " " : ", ").Append(partNames[i]).Append(' ').Append(partMs[i].ToString("0.00"));
			}
			s.Append(" (").Append(tilesShown).Append(" tiles made in the second)");
			s.Append("\ndrawn: ").Append(Count(drawShown)).Append(" draw calls, ").Append(Count(batchShown)).Append(" batches, ").Append(Count(setPassShown)).Append(" set-pass calls, ")
				.Append(Count(triShown / 1000f)).Append("k triangles, ").Append(Count(shadowShown)).Append(" shadow casters; GC ").Append(Count(gcShown / 1024f)).Append(" KB a frame");
			return s.ToString();
		}

		private static string Ms(float v)
		{
			return v < 0f ? "n/a" : v.ToString("0.0") + " ms";
		}

		private static string Count(float v)
		{
			return float.IsNaN(v) ? "n/a" : v.ToString("0");
		}

		/// <summary>Every step's average, for the log every 10 s (the overlay shows only the slowest).</summary>
		internal static string AllSteps()
		{
			System.Text.StringBuilder s = new System.Text.StringBuilder(4000);
			for (int i = 0; i < stepMs.Length; i++)
			{
				if (stepMs[i] >= 0.01f)
				{
					s.Append(s.Length == 0 ? "" : ", ").Append(stepNames[i]).Append(' ').Append(stepMs[i].ToString("0.00"));
				}
			}
			return s.ToString();
		}
	}
}
