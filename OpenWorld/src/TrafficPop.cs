using System;
using System.Collections.Generic;
using UnityEngine;

namespace HalfGripOpenWorld
{
	/// <summary>
	/// Part of <see cref="TrafficCars"/>: a watch for cars that start being drawn close to the player. It found the owner's "pop-in at 50 m"
	/// on the highway (2026-11-26): no car was first drawn close, so the far cars were drawn but unseen (on the Enemy layer, which the main
	/// camera doesn't draw; see <see cref="PartsOf"/>). Kept for the next such report: each car drawn this frame that wasn't the frame before,
	/// nearer than <see cref="PopMoving"/> (moving) or <see cref="PopParked"/> (parked), is counted in the timings line and logged (verbose)
	/// with what it was the frame before (not chosen, chosen but not drawn and why), where it is and how fast both it and the player go.
	/// Not counted: a physics car going back on its timetable (out of reach, about 120 m off and unseen behind) and the race's first choice.
	/// </summary>
	internal sealed partial class TrafficCars
	{
		/// <summary>A car first drawn nearer than this (units, about 270 m moving and 135 m parked) is logged.</summary>
		private const double PopMoving = 900.0, PopParked = 450.0;
		/// <summary>At most this many lines a race (the count goes on in the timings line).</summary>
		private const int PopMostLogged = 20;

		private Dictionary<int, string> popPrev = new Dictionary<int, string>(), popNow = new Dictionary<int, string>();
		private HashSet<int> popPhysPrev = new HashSet<int>();
		private int popLogged, popCount;
		private double popLastX = double.NaN, popLastZ, popVx, popVz;
		private bool choseThisFrame;
		/// <summary>True until the first frame with cars drawn is over (the race's first choice draws every car at once).</summary>
		private bool popFirst = true;
		/// <summary>Why <see cref="Draw"/> last said no (for the watch).</summary>
		private string drawFail;

		/// <summary>Chosen car <paramref name="c"/> not drawn this frame, for <paramref name="why"/>.</summary>
		private void PopFrom(int c, string why)
		{
			popNow[c] = why;
		}

		/// <summary>Chosen car <paramref name="c"/> drawn this frame: logged if it wasn't drawn the frame before and is close.</summary>
		private void PopCheck(int c, View v, double w, double cx, double cz, bool chosenNow)
		{
			popNow[c] = null;
			if (popPrev.TryGetValue(c, out string before) && before == null)
			{
				return;
			}
			TrafficDay.Place at = late.At(c, w, out bool _);
			bool moving = at.State == TrafficDay.State.Driving;
			double x = moving ? v.LastX : v.ParkX, z = moving ? v.LastZ : v.ParkZ;
			if (double.IsNaN(x))
			{
				return;
			}
			if (popFirst || (before == null && popPhysPrev.Contains(c)))
			{
				return;
			}
			double dx = x - cx, dz = z - cz, d = Math.Sqrt(dx * dx + dz * dz);
			if (d >= (moving ? PopMoving : PopParked))
			{
				return;
			}
			popCount++;
			if (popLogged >= PopMostLogged)
			{
				return;
			}
			popLogged++;
			string was = before ?? "not chosen" + (chosenNow ? " (chosen this frame)" : " (no choice this frame)");
			double speed = Math.Sqrt(popVx * popVx + popVz * popVz);
			double ahead = speed > 1.0 ? (dx * popVx + dz * popVz) / speed : 0.0;
			bool inLedger = late.TryGet(c, out TrafficLate.Entry le);
			Mod.Info("Traffic pop-in: car " + c + " " + (moving ? "moving at " + (at.Speed / 3.3 * 3.6).ToString("0") + " km/h, leg " + at.Leg + " (" + at.T.ToString("0") + " s in)" : "parked")
				+ " first drawn " + d.ToString("0") + " units (" + (d / 3.3).ToString("0") + " m) off, " + (ahead >= 0.0 ? "ahead" : "behind") + " (" + Math.Abs(ahead).ToString("0") + " along), "
				+ (IsFar(v, x, z) ? "far" : "near") + ", at (" + x.ToString("0") + ", " + z.ToString("0") + "); the frame before: " + was
				+ (inLedger ? "; in the ledger (late " + le.D.ToString("0.0") + " s)" : "")
				+ "; the player at (" + cx.ToString("0") + ", " + cz.ToString("0") + ") doing " + (speed / 3.3 * 3.6).ToString("0") + " km/h; physics cars " + physical.Count
				+ "; tile loaded there " + (Streamer.Instance != null && Streamer.Instance.IsLoadedAt(x, z)));
		}

		/// <summary>End of the frame's drawing: this frame's statuses become the last, the player's speed is measured.</summary>
		private void PopForget(double cx, double cz, float dt)
		{
			Dictionary<int, string> t = popPrev;
			popPrev = popNow;
			popNow = t;
			popNow.Clear();
			if (popPrev.Count > 0)
			{
				popFirst = false;
			}
			popPhysPrev.Clear();
			foreach (int c in physical.Keys)
			{
				popPhysPrev.Add(c);
			}
			if (!double.IsNaN(popLastX) && dt > 1e-4f)
			{
				double vx = (cx - popLastX) / dt, vz = (cz - popLastZ) / dt;
				// An origin shift doesn't move world coordinates; the wrap does: a jump is not a speed.
				if (vx * vx + vz * vz < 1000.0 * 1000.0)
				{
					popVx = vx;
					popVz = vz;
				}
			}
			popLastX = cx;
			popLastZ = cz;
		}

		/// <summary>For the timings line: how many cars were first drawn close this race.</summary>
		private string PopText()
		{
			return "; first drawn close this race " + popCount;
		}
	}
}
