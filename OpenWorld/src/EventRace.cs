using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace HalfGripOpenWorld
{
	/// <summary>
	/// Solo events at the courses (<see cref="Courses"/>): a glowing box behind each start line; stopping in it in neutral starts a
	/// countdown, then the clock runs through the checkpoint gates in order to the finish. The next gate stands across the road, lit,
	/// only while an event runs. A missed gate is called out and the clock runs on, but that run sets no best time. A wreck puts the car
	/// back at the last gate passed (<see cref="Respawn"/>), not the spawn. The best time of each course is kept in the mod's own config
	/// file, never the game's save.
	///
	/// Nothing here needs the Multiplayer mod: in a room an event is the player's own time trial, which the others do not see. Events
	/// that a room runs together are the next step (docs/PLAN.md).
	/// </summary>
	internal sealed class EventRace : MonoBehaviour
	{
		internal static EventRace Instance;

		private enum State
		{
			Idle,
			Countdown,
			Running,
			Finished,
		}

		/// <summary>As the garage's: slower than this counts as stopped, and the car must stand this long in neutral before the countdown.</summary>
		private const float Stopped = 1.2f;
		private const float Settle = 0.6f;

		/// <summary>Seconds from the countdown starting to the green light: the course's name, then 3, 2, 1.</summary>
		private const float CountdownSeconds = 4f;

		/// <summary>The event is given up when the car is further than this from the course for <see cref="OffCourseSeconds"/> (or jumps there).</summary>
		private const double OffCourse = 500.0;
		private const float OffCourseSeconds = 10f;

		/// <summary>A move further than this in one frame, not made by <see cref="Respawn"/>, is a teleport: the event is given up.</summary>
		private const double Jump = 150.0;

		/// <summary>How many gates past the next one a crossing is looked for: crossing one of them misses the gates before it.</summary>
		private const int LookAhead = 3;

		/// <summary>How long the result stays on the screen after the finish.</summary>
		private const float ResultSeconds = 10f;

		/// <summary>Gate posts: this far outside the road's edge, this tall over the road.</summary>
		private const float PostOut = 3f;
		private const float PostHeight = 16f;

		private TrackManager tm;
		private Material boxMaterial;
		private Material gateMaterial;
		private Material finishMaterial;
		private readonly List<GameObject> boxes = new List<GameObject>();
		private GameObject[] gates;

		private State state = State.Idle;
		private Courses.Course course;
		private Courses.Course here;
		private bool armed = true;
		private float readySince = -1f;
		private float countdownAt;
		private int countdownShown;
		private float startedAt;
		private int next;
		private int missed;
		private int lap;
		private float lapStartedAt;
		private float lastLap;
		private float finishTime;
		private float finishedAt;
		private bool newBest;
		private float previousBest;
		private string flash;
		private float flashAt = -1f;
		private float offSince = -1f;
		private float offCheckAt;
		private bool havePrev;
		private double prevX, prevZ;
		private int placedFrame = -1;
		private int shownNext = -1;
		private Courses.Course shownCourse;

		private static readonly Dictionary<string, ConfigEntry<float>> bests = new Dictionary<string, ConfigEntry<float>>();

		/// <summary>True while an event's clock runs: the reset after a wreck goes to its last gate.</summary>
		internal static bool Running => Instance != null && Instance.state == State.Running;

		internal void Setup(TrackManager manager, Shader lit)
		{
			Instance = this;
			tm = manager;
			try
			{
				boxMaterial = Garage.RingMaterial(lit, new Color(1f, 0.55f, 0.1f), "OpenWorld Event box");
				gateMaterial = Garage.RingMaterial(lit, new Color(1f, 0.55f, 0.1f), "OpenWorld Event gate");
				finishMaterial = Garage.RingMaterial(lit, new Color(0.95f, 0.95f, 0.95f), "OpenWorld Event finish");
				foreach (Courses.Course c in Courses.All)
				{
					boxes.Add(Box(c));
				}
				gates = new[] { GateMarker("Event gate (next)"), GateMarker("Event gate (after)") };
				List<string> names = new List<string>();
				foreach (Courses.Course c in Courses.All)
				{
					names.Add(c.Name + " " + (c.Distance / 3300.0).ToString("0.0") + " km, " + c.Gates.Count + " gates" + (c.Laps > 1 ? ", " + c.Laps + " laps" : ""));
				}
				Mod.Log.LogInfo("Events: " + string.Join("; ", names.ToArray()));
			}
			catch (Exception e)
			{
				Mod.Log.LogWarning("The events' start boxes and gates could not be made: " + e);
			}
		}

		// ---- the marks -----------------------------------------------------------------------------------------------

		/// <summary>
		/// The start box: a ring on the road, following its grade, a little over it so it never flickers into it, and a chevron inside
		/// pointing the way the event goes. A child of the world's root, so the floating origin moves it with everything else.
		/// </summary>
		private GameObject Box(Courses.Course c)
		{
			const int segments = 48;
			const float over = 0.2f, width = 0.9f;
			List<Vector3> v = new List<Vector3>();
			List<int> t = new List<int>();
			Vector3 Point(double ax, double az)
			{
				double x = c.BoxX + ax, z = c.BoxZ + az;
				return new Vector3((float)ax, (float)(c.BoxSurface(x, z) - c.BoxY) + over, (float)az);
			}
			void Quad(Vector3 a, Vector3 b, Vector3 p, Vector3 d)
			{
				int k = v.Count;
				v.Add(a);
				v.Add(b);
				v.Add(p);
				v.Add(d);
				t.AddRange(new[] { k, k + 2, k + 1, k + 1, k + 2, k + 3 });
			}
			double outer = c.BoxRadius, inner = c.BoxRadius - width;
			for (int i = 0; i < segments; i++)
			{
				double a0 = 2.0 * Math.PI * i / segments, a1 = 2.0 * Math.PI * (i + 1) / segments;
				Quad(Point(Math.Cos(a0) * outer, Math.Sin(a0) * outer), Point(Math.Cos(a1) * outer, Math.Sin(a1) * outer), Point(Math.Cos(a0) * inner, Math.Sin(a0) * inner), Point(Math.Cos(a1) * inner, Math.Sin(a1) * inner));
			}
			// The chevron: two bars meeting at a point ahead of the middle. Forward is (Dx, Dz), right (Dz, -Dx).
			double fx = c.BoxDx, fz = c.BoxDz, rx = c.BoxDz, rz = -c.BoxDx;
			double tip = inner * 0.55, back = -inner * 0.15, side = inner * 0.5, bar = 0.8;
			foreach (int s in new[] { -1, 1 })
			{
				double bx = fx * back + rx * side * s, bz = fz * back + rz * side * s;
				double px = fx * tip, pz = fz * tip;
				Quad(Point(bx, bz), Point(px, pz), Point(bx - fx * bar, bz - fz * bar), Point(px - fx * bar, pz - fz * bar));
			}
			Mesh mesh = new Mesh { name = "Event box" };
			mesh.SetVertices(v);
			mesh.SetTriangles(t, 0);
			mesh.RecalculateNormals();
			mesh.RecalculateBounds();
			GameObject go = new GameObject("Event start: " + c.Name);
			go.transform.SetParent(transform, false);
			go.transform.position = Origin.Local(c.BoxX, (float)(c.BoxY + Network.Lift), c.BoxZ);
			go.AddComponent<MeshFilter>().sharedMesh = mesh;
			MeshRenderer renderer = go.AddComponent<MeshRenderer>();
			renderer.sharedMaterial = boxMaterial;
			renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
			return go;
		}

		/// <summary>A gate: two posts and a bar across, no collider (a car drives through it), hidden until an event shows it.</summary>
		private GameObject GateMarker(string name)
		{
			GameObject go = new GameObject(name);
			go.transform.SetParent(transform, false);
			for (int i = 0; i < 3; i++)
			{
				GameObject part = GameObject.CreatePrimitive(PrimitiveType.Cube);
				DestroyImmediate(part.GetComponent<Collider>());
				part.name = i == 2 ? "Bar" : "Post";
				part.transform.SetParent(go.transform, false);
				MeshRenderer renderer = part.GetComponent<MeshRenderer>();
				renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
			}
			go.SetActive(false);
			return go;
		}

		/// <summary>Puts a gate marker across the road at gate g: its posts stand from the ground beside the road to over the road.</summary>
		private void ShowGate(GameObject marker, Courses.Gate g, bool finish)
		{
			double half = g.Half - Courses.GateMargin + PostOut;
			double rx = g.Dz, rz = -g.Dx;
			float road = (float)(g.Y + Network.Lift), top = road + PostHeight;
			Quaternion facing = Quaternion.LookRotation(new Vector3((float)g.Dx, 0f, (float)g.Dz), Vector3.up);
			Material material = finish ? finishMaterial : gateMaterial;
			for (int i = 0; i < 2; i++)
			{
				int s = i == 0 ? -1 : 1;
				double px = g.X + rx * half * s, pz = g.Z + rz * half * s;
				float bottom = Mathf.Min((float)WorldGen.Height(px, pz), road) - 1f;
				Transform post = marker.transform.GetChild(i);
				post.SetPositionAndRotation(Origin.Local(px, (bottom + top) * 0.5f, pz), facing);
				post.localScale = new Vector3(1.2f, top - bottom, 1.2f);
				post.GetComponent<MeshRenderer>().sharedMaterial = material;
			}
			Transform bar = marker.transform.GetChild(2);
			bar.SetPositionAndRotation(Origin.Local(g.X, top - 1f, g.Z), facing);
			bar.localScale = new Vector3((float)(half * 2.0) + 1.2f, 2f, 0.6f);
			bar.GetComponent<MeshRenderer>().sharedMaterial = material;
			marker.SetActive(true);
		}

		/// <summary>The next gate and the one after it lit while an event runs (the finish line white); none otherwise.</summary>
		private void ShowGates()
		{
			if (gates == null)
			{
				return;
			}
			bool on = (state == State.Countdown || state == State.Running) && course != null;
			// Placed once each time the next gate changes: the floating origin moves them with the world's root after that.
			Courses.Course showing = on ? course : null;
			if (showing == shownCourse && next == shownNext)
			{
				return;
			}
			shownCourse = showing;
			shownNext = next;
			for (int k = 0; k < gates.Length; k++)
			{
				int n = next + k;
				if (on && n < course.Gates.Count)
				{
					ShowGate(gates[k], course.Gates[n], n == course.Gates.Count - 1);
				}
				else if (gates[k].activeSelf)
				{
					gates[k].SetActive(false);
				}
			}
		}

		// ---- the event -------------------------------------------------------------------------------------------------

		private void Update()
		{
			CarControllerNew car = tm != null ? tm.playerCarController : null;
			CarLink link = tm != null ? tm.playerCarlink : null;
			if (car == null || link == null || GameManager.raceOver)
			{
				return;
			}
			Vector3 p = car.transform.position;
			Origin.World(p, out double x, out double z);
			float speed = car.rb != null ? car.rb.linearVelocity.magnitude : 0f;
			here = InBox(p, x, z);
			try
			{
				switch (state)
				{
					case State.Idle:
					case State.Finished:
						if (state == State.Finished && Time.time - finishedAt > ResultSeconds)
						{
							state = State.Idle;
						}
						Waiting(link, speed);
						break;
					case State.Countdown:
						Counting(link, speed);
						break;
					case State.Running:
						Timing(link, x, z);
						break;
				}
			}
			catch (Exception e)
			{
				Mod.Log.LogWarning("Event: " + e);
				Stop(null);
			}
			prevX = x;
			prevZ = z;
			havePrev = true;
		}

		/// <summary>The course whose start box the car stands in, at about the box's height (not on a bridge over it).</summary>
		private static Courses.Course InBox(Vector3 p, double x, double z)
		{
			Courses.Course c = Courses.BoxAt(x, z);
			if (c == null || Math.Abs(p.y - (c.BoxSurface(x, z) + Network.Lift)) > 6.0)
			{
				return null;
			}
			return c;
		}

		/// <summary>Stopped in a start box in neutral, and held there a moment: the countdown starts.</summary>
		private void Waiting(CarLink link, float speed)
		{
			bool neutral = link.sa != null && (link.sa.gear == 0 || !link.sa.inGear);
			bool garage = Garage.Instance != null && Garage.Instance.OpenAt != null;
			bool ready = here != null && speed < Stopped && neutral && !garage && link.hp > 0f;
			if (!ready)
			{
				readySince = -1f;
				// Out of the box or moving again: the next stop in it starts an event.
				if (here == null || speed > Stopped * 3f)
				{
					armed = true;
				}
				return;
			}
			if (!armed)
			{
				return;
			}
			if (readySince < 0f)
			{
				readySince = Time.time;
			}
			else if (Time.time - readySince >= Settle)
			{
				course = here;
				state = State.Countdown;
				countdownAt = Time.time;
				countdownShown = -1;
				next = 0;
				armed = false;
				readySince = -1f;
				TextController.ClearAll();
				Mod.Log.LogInfo("Event: countdown at the " + course.Name);
			}
		}

		/// <summary>The countdown: the course's name, 3, 2, 1, then the clock starts. Leaving the box or rolling away before it is a jump start.</summary>
		private void Counting(CarLink link, float speed)
		{
			if (here != course || speed > Stopped * 3f || link.hp <= 0f)
			{
				TextController.ClearAll();
				TextController.EnqueueRandomText(1.5f, link.hp <= 0f ? "EVENT OVER" : "JUMP START");
				Mod.Log.LogInfo("Event: stopped before the start at the " + course.Name);
				state = State.Idle;
				course = null;
				return;
			}
			float since = Time.time - countdownAt;
			int shown = (int)Math.Floor(since);
			if (shown != countdownShown && shown < (int)CountdownSeconds)
			{
				countdownShown = shown;
				TextController.EnqueueRandomText(0.9f, shown == 0 ? Title(course) : (CountdownSeconds - shown).ToString("0"));
			}
			if (since >= CountdownSeconds)
			{
				TextController.ClearAll();
				TextController.EnqueueRandomText(1f, "GO!");
				state = State.Running;
				startedAt = Time.time;
				lapStartedAt = startedAt;
				lap = 1;
				missed = 0;
				next = 0;
				offSince = -1f;
				havePrev = false;
			}
		}

		/// <summary>The clock running: gates crossed in order, a missed one called out, the event given up off the course.</summary>
		private void Timing(CarLink link, double x, double z)
		{
			float now = Time.time - startedAt;
			if (havePrev && placedFrame != Time.frameCount && placedFrame != Time.frameCount - 1)
			{
				double dx = x - prevX, dz = z - prevZ;
				if (dx * dx + dz * dz > Jump * Jump)
				{
					Stop("EVENT OVER");
					return;
				}
				for (int j = next; j < Math.Min(course.Gates.Count, next + LookAhead + 1); j++)
				{
					if (Courses.Crossed(course.Gates[j], prevX, prevZ, x, z))
					{
						Passed(j, now);
						break;
					}
				}
				if (state != State.Running)
				{
					return;
				}
			}
			// Off the course: checked twice a second (the whole centre line is looked at).
			if (Time.time >= offCheckAt)
			{
				offCheckAt = Time.time + 0.5f;
				if (course.Distance2(x, z) > OffCourse * OffCourse)
				{
					if (offSince < 0f)
					{
						offSince = Time.time;
						TextController.EnqueueRandomText(2f, "BACK TO THE COURSE");
					}
					else if (Time.time - offSince > OffCourseSeconds)
					{
						Stop("EVENT OVER");
					}
				}
				else
				{
					offSince = -1f;
				}
			}
		}

		private void Passed(int j, float now)
		{
			Courses.Gate g = course.Gates[j];
			if (j > next)
			{
				int n = j - next;
				missed += n;
				TextController.EnqueueRandomText(1.5f, n == 1 ? "MISSED A CHECKPOINT" : "MISSED " + n + " CHECKPOINTS");
			}
			next = j + 1;
			if (next >= course.Gates.Count)
			{
				Finish(now);
				return;
			}
			if (g.LapEnd > 0)
			{
				lastLap = Time.time - lapStartedAt;
				lapStartedAt = Time.time;
				lap = g.LapEnd + 1;
				TextController.EnqueueRandomText(1.5f, (lap == course.Laps ? "LAST LAP  " : "LAP " + lap + "  ") + Clock(lastLap));
				Flash("Lap " + g.LapEnd + ": " + Clock(lastLap));
			}
			else
			{
				Flash("Checkpoint " + next + " of " + course.Gates.Count + ": " + Clock(now));
			}
		}

		private void Finish(float now)
		{
			finishTime = now;
			finishedAt = Time.time;
			state = State.Finished;
			ConfigEntry<float> best = Best(course);
			previousBest = best != null ? best.Value : 0f;
			newBest = missed == 0 && best != null && (best.Value <= 0f || now < best.Value);
			if (newBest)
			{
				best.Value = now;
			}
			string text = missed > 0 ? "FINISH  " + Clock(now) + "  (MISSED " + missed + ")" : newBest ? "NEW BEST  " + Clock(now) : "FINISH  " + Clock(now);
			TextController.ClearAll();
			TextController.EnqueueRandomText(3f, text);
			Mod.Log.LogInfo("Event: finished the " + course.Name + " in " + Clock(now) + (missed > 0 ? ", " + missed + " checkpoints missed (no best time)" : newBest ? ", a new best" : ", best " + Clock(previousBest)));
		}

		/// <summary>Gives the event up, with a call-out if <paramref name="text"/> is given.</summary>
		private void Stop(string text)
		{
			if (text != null)
			{
				TextController.EnqueueRandomText(1.5f, text);
			}
			if (course != null)
			{
				Mod.Log.LogInfo("Event: given up at the " + course.Name);
			}
			state = State.Idle;
			course = null;
			next = 0;
		}

		/// <summary>
		/// The reset after a wreck in an event: the car goes back on the course just past the last gate passed (the start box if none
		/// yet), facing along it; the clock runs on. False when no event is running (the reset goes to the spawn).
		/// </summary>
		internal static bool Respawn(Streamer streamer)
		{
			EventRace e = Instance;
			if (e == null || e.state != State.Running || e.course == null)
			{
				return false;
			}
			Courses.Course c = e.course;
			double x, z, dx, dz;
			if (e.next == 0)
			{
				x = c.BoxX;
				z = c.BoxZ;
				dx = c.BoxDx;
				dz = c.BoxDz;
			}
			else
			{
				c.At(c.StartS + c.Gates[e.next - 1].Along + 6.0, out x, out z, out double _, out dx, out dz);
			}
			streamer.BuildAround(x, z, 1);
			streamer.Place(x, z, Quaternion.LookRotation(new Vector3((float)dx, 0f, (float)dz), Vector3.up));
			e.placedFrame = Time.frameCount;
			e.havePrev = false;
			return true;
		}

		// ---- best times ----------------------------------------------------------------------------------------------

		/// <summary>The course's best time, in seconds (0 for none), in the mod's config file under [Best times].</summary>
		private static ConfigEntry<float> Best(Courses.Course c)
		{
			if (bests.TryGetValue(c.Key, out ConfigEntry<float> entry))
			{
				return entry;
			}
			try
			{
				entry = Mod.Ctx.Config.Bind("Best times", c.Key, 0f, "Your best time in seconds at the " + c.Name + " event (" + (c.Distance / 3300.0).ToString("0.0") + " km" + (c.Laps > 1 ? ", " + c.Laps + " laps" : "") + "). 0 for none; set it to 0 to start again.");
			}
			catch (Exception e)
			{
				Mod.Log.LogWarning("Event: the best time could not be read: " + e.Message);
				entry = null;
			}
			bests[c.Key] = entry;
			return entry;
		}

		// ---- on the screen -------------------------------------------------------------------------------------------

		private static string Title(Courses.Course c)
		{
			return c.Name.ToUpperInvariant() + (c.Laps > 1 ? "  " + c.Laps + " LAPS" : "");
		}

		/// <summary>A time as m:ss.cc.</summary>
		internal static string Clock(float seconds)
		{
			if (seconds < 0f)
			{
				seconds = 0f;
			}
			int cs = (int)Math.Floor(seconds * 100f);
			return (cs / 6000) + ":" + (cs / 100 % 60).ToString("00") + "." + (cs % 100).ToString("00");
		}

		private void Flash(string text)
		{
			flash = text;
			flashAt = Time.time;
		}

		private GUIStyle big;
		private GUIStyle small;

		private void OnGUI()
		{
			// Not over the garage's window.
			if (tm == null || GameManager.raceOver || (Garage.Instance != null && Garage.Instance.OpenAt != null))
			{
				return;
			}
			big ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 28, fontStyle = FontStyle.Bold };
			small ??= new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 18, richText = true };
			float w = 640f, x0 = Screen.width * 0.5f - w * 0.5f, y0 = 72f;
			switch (state)
			{
				case State.Idle:
					if (here != null && armed)
					{
						ConfigEntry<float> best = Best(here);
						Panel(x0, y0, w, 2);
						GUI.Label(new Rect(x0, y0 + 4f, w, 34f), here.Name.ToUpperInvariant(), big);
						GUI.Label(new Rect(x0, y0 + 38f, w, 26f), Info(here) + (best != null && best.Value > 0f ? "   best " + Clock(best.Value) : "") + "   ·   stop in neutral to start", small);
					}
					break;
				case State.Countdown:
					Panel(x0, y0, w, 2);
					GUI.Label(new Rect(x0, y0 + 4f, w, 34f), course.Name.ToUpperInvariant(), big);
					GUI.Label(new Rect(x0, y0 + 38f, w, 26f), Info(course) + "   ·   go on green, not before", small);
					break;
				case State.Running:
				{
					float now = Time.time - startedAt;
					Panel(x0, y0, w, 3);
					GUI.Label(new Rect(x0, y0 + 4f, w, 34f), Clock(now), big);
					ConfigEntry<float> best = Best(course);
					string line = (course.Laps > 1 ? "Lap " + Math.Min(lap, course.Laps) + " of " + course.Laps + "   ·   " : "") + "Checkpoint " + next + " of " + course.Gates.Count
						+ (best != null && best.Value > 0f ? "   ·   best " + Clock(best.Value) : "") + (missed > 0 ? "   ·   <color=#ff8060>" + missed + " missed</color>" : "");
					GUI.Label(new Rect(x0, y0 + 38f, w, 26f), line, small);
					if (flash != null && Time.time - flashAt < 3f)
					{
						GUI.Label(new Rect(x0, y0 + 62f, w, 26f), flash, small);
					}
					break;
				}
				case State.Finished:
				{
					Panel(x0, y0, w, 2);
					GUI.Label(new Rect(x0, y0 + 4f, w, 34f), (newBest ? "NEW BEST  " : "FINISH  ") + Clock(finishTime), big);
					string line = course.Name + "   ·   " + (missed > 0 ? "<color=#ff8060>" + missed + " checkpoints missed: no best time</color>" : previousBest > 0f ? (newBest ? "was " : "best ") + Clock(previousBest) + (newBest ? "" : "   (+" + (finishTime - previousBest).ToString("0.00") + ")") : "first time set");
					GUI.Label(new Rect(x0, y0 + 38f, w, 26f), line, small);
					break;
				}
			}
		}

		/// <summary>The course's length (in real km) and laps.</summary>
		private static string Info(Courses.Course c)
		{
			return (c.Distance / 3300.0).ToString("0.0") + " km" + (c.Laps > 1 ? ", " + c.Laps + " laps" : "") + ", " + c.Gates.Count + " checkpoints";
		}

		/// <summary>A dark band behind the text, so it reads over sky and snow alike.</summary>
		private static void Panel(float x, float y, float w, int lines)
		{
			Color was = GUI.color;
			GUI.color = new Color(0f, 0f, 0f, 0.55f);
			GUI.DrawTexture(new Rect(x, y, w, 10f + 28f * lines), Texture2D.whiteTexture);
			GUI.color = was;
		}

		private void LateUpdate()
		{
			ShowGates();
		}

		private void OnDestroy()
		{
			foreach (Material m in new[] { boxMaterial, gateMaterial, finishMaterial })
			{
				if (m != null)
				{
					Destroy(m);
				}
			}
			foreach (GameObject box in boxes)
			{
				if (box != null)
				{
					Destroy(box.GetComponent<MeshFilter>().sharedMesh);
				}
			}
			if (Instance == this)
			{
				Instance = null;
			}
		}
	}
}
