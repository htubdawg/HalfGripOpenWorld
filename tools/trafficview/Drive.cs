using System;
using System.Collections.Generic;
using System.Diagnostics;
using HalfGripOpenWorld;

// Stage 3's physics cars without the game: every driving car within 300 of a pretend player made a "physics car" (here a point on its
// lane with the game car's acceleration and braking, driven by the real TrafficDrive.Target: the profile, the car in front, the
// junctions, the trip's end, never early), the rest drawn from the day through the ledger, and each physics car put back on its
// timetable through the ledger as it leaves 400 or reaches its trip's end, as TrafficPhysics does. Checks that the line and the
// projection agree, that no physics car runs into another, a drawn car or the player, that no two physics cars cross in a junction's
// box, that nobody waits for ever, and that going back on the timetable doesn't make a car jump.
internal static partial class Program
{
	private const double DriveStep = 0.05, DriveRun = 600.0, PhysReach = 300.0, PhysLeave = 400.0, BubbleEvery = 0.4;
	private const int MostPhysical = 24;
	/// <summary>
	/// What a run may have: cars that both committed to a junction at once (one turning, one merging, both past stopping) and meet in its
	/// box. Physics cars there bump; the day itself has about a dozen such meetings a day. 16 seeds give 0 to 5 a run at the busiest place
	/// at the rush (2026-11-23).
	/// </summary>
	private const int MostMeetings = 6, MostCrossings = 3;
	/// <summary>The game car's pull and braking as the driver uses them (units/s²): a level-0 car pulls about 5 to 8 off the line; brakes up to 40.</summary>
	private const double SimAccel = 6.0, SimBrake = 30.0;

	private sealed class Sim
	{
		public int Car, Leg;
		public double P, V, Stopped, MostStopped;
		public float[] Profile;
		public TrafficDrive.State St;
		public TrafficDrive.Mover Me;
		/// <summary>What held it when it had first stood 90 s.</summary>
		public string StopNote;
	}

	/// <summary>A light's states over the physics run, every 10 s (G, A, R; - no light).</summary>
	private static string LightsTrace(TrafficDay day, int node, int arm, double w0)
	{
		if (!TrafficSignals.Has(node))
		{
			return "-";
		}
		char[] c = new char[(int)(DriveRun / 10.0)];
		for (int i = 0; i < c.Length; i++)
		{
			int st = day.Lights.State(node, arm, w0 + i * 10.0);
			c[i] = st == TrafficSignals.Green ? 'G' : st == TrafficSignals.Amber ? 'A' : 'R';
		}
		return new string(c);
	}

	private static bool Drive(TrafficDay day)
	{
		bool ok = DriveLine(day);
		double w0 = TrafficDay.SimOfHour(17.0);
		// Where the most cars drive by at the evening rush, as the ledger's check stands.
		List<(double x, double z, int c)> moving = new List<(double, double, int)>();
		for (int c = 0; c < day.Cars.Count; c++)
		{
			TrafficDay.Place at = day.At(c, w0);
			if (at.State == TrafficDay.State.Driving && !at.InTunnel)
			{
				moving.Add((at.X, at.Z, c));
			}
		}
		int most = -1;
		double bx = 0.0, bz = 0.0;
		foreach ((double x, double z, int c) a in moving)
		{
			int n = 0;
			foreach ((double x, double z, int c) b in moving)
			{
				n += (a.x - b.x) * (a.x - b.x) + (a.z - b.z) * (a.z - b.z) < 300.0 * 300.0 ? 1 : 0;
			}
			if (n > most)
			{
				most = n;
				bx = a.x;
				bz = a.z;
			}
		}
		ok &= DriveRun1(day, w0, bx, bz, false, "standing at " + bx.ToString("0") + ", " + bz.ToString("0") + " (" + most + " cars driving within 300)");
		ok &= DriveRun1(day, w0, bx, bz, true, "the same, the player's car stopped across a lane");
		return ok;
	}

	/// <summary>The line and the projection: a point on a leg's line projects back to where it came from; the time the day was at a place inverts its place at a time.</summary>
	private static bool DriveLine(TrafficDay day)
	{
		Rng rng = new Rng(0xD21BEUL ^ (ulong)day.Seed);
		int tried = 0, wrong = 0, timeWrong = 0;
		double worst = 0.0, worstT = 0.0;
		Stopwatch watch = Stopwatch.StartNew();
		int projections = 0;
		for (int i = 0; i < 4000; i++)
		{
			TrafficDay.Leg leg = day.Legs[(int)Math.Min(day.Legs.Count - 1, Math.Floor(rng.Next() * day.Legs.Count))];
			if (leg.Start < 0.0 || leg.Rec == null || leg.Length < 60.0)
			{
				continue;
			}
			double p = 20.0 + rng.Next() * (leg.Length - 40.0);
			TrafficDrive.Line(leg, p, out double x, out double z, out double _, out double _);
			double from = p - 3.0 + rng.Next() * 6.0;
			double q = TrafficDrive.Project(leg, from, x, z, out double side, 6.0, 9.0);
			projections++;
			tried++;
			// The line can come back near itself (a U-turn at a junction's curve): a point found as near is fine.
			TrafficDrive.Line(leg, q, out double qx, out double qz, out double _, out double _);
			double miss = Math.Sqrt((qx - x) * (qx - x) + (qz - z) * (qz - z));
			if (Math.Abs(q - p) > 1.0 && miss > 0.5)
			{
				wrong++;
			}
			worst = Math.Max(worst, Math.Min(Math.Abs(q - p), miss));
			// TimeAt inverts DistanceAt where the car was moving.
			double t = leg.Start + rng.Next() * (leg.End - leg.Start);
			double pt = TrafficDay.DistanceAt(leg, t);
			double back = TrafficDrive.TimeAt(leg, pt);
			double pb = TrafficDay.DistanceAt(leg, back);
			if (Math.Abs(pb - pt) > 0.5)
			{
				timeWrong++;
			}
			worstT = Math.Max(worstT, Math.Abs(pb - pt));
		}
		double us = watch.Elapsed.TotalMilliseconds * 1000.0 / Math.Max(1, projections);
		Console.WriteLine("  the drivers' line: " + tried + " points projected back, " + wrong + " wrong (worst " + worst.ToString("0.00") + "), " + timeWrong + " times not inverting (worst " + worstT.ToString("0.00") + " units), "
			+ us.ToString("0.0") + " us each with the time check" + (wrong == 0 && timeWrong == 0 ? "" : " FAIL"));
		return wrong == 0 && timeWrong == 0;
	}

	private static bool DriveRun1(TrafficDay day, double w0, double px, double pz, bool blocked, string name)
	{
		TrafficParking parking = day.Parking;
		TrafficLate late = new TrafficLate(day);
		Dictionary<int, Sim> physical = new Dictionary<int, Sim>();
		List<TrafficDrive.Mover> movers = new List<TrafficDrive.Mover>();
		List<TrafficDrive.Obstacle> obstacles = new List<TrafficDrive.Obstacle>();
		List<int> keys = new List<int>();
		HashSet<long> crashed = new HashSet<long>(), crossed = new HashSet<long>();
		int made = 0, unstuck = 0, backOnTime = 0, backLate = 0, waitedEnd = 0, hidden = 0, heldBack = 0, hitPlayer = 0, jumps = 0, targets = 0;
		double worstJump = 0.0, mostLate = 0.0, sumLate = 0.0, targetMs = 0.0;
		string crashNote = "", crossNote = "", jumpNote = "", hitNote = "";
		Stopwatch watch = new Stopwatch();
		foreach (TrafficDay.Leg l in day.Legs)
		{
			l.HoldLane = -1f;
			l.HoldEnd = double.MaxValue;
		}

		// The player's car stopped across a lane: on the path of the driving car nearest the player, 60 on.
		double ox = 0.0, oz = 0.0;
		if (blocked)
		{
			double best = double.MaxValue;
			for (int c = 0; c < day.Cars.Count; c++)
			{
				TrafficDay.Place at = day.At(c, w0);
				if (at.State != TrafficDay.State.Driving || at.InTunnel || at.P + 80.0 > day.Legs[at.Leg].Length)
				{
					continue;
				}
				double d = (at.X - px) * (at.X - px) + (at.Z - pz) * (at.Z - pz);
				if (d < best)
				{
					best = d;
					TrafficDrive.Line(day.Legs[at.Leg], at.P + 60.0, out ox, out oz, out double _, out double _);
				}
			}
			px = ox;
			pz = oz;
		}

		double nextBubble = 0.0;
		for (double t = 0.0; t < DriveRun; t += DriveStep)
		{
			double w = w0 + t;
			if (w >= Traffic.DaySeconds)
			{
				w -= Traffic.DaySeconds;
			}
			bool bubble = t >= nextBubble;
			{
				// Going back: out of reach, the trip's end (each bubble), in a tunnel's dark (every step, as TrafficCars does each frame:
				// driven right in, it is through the dark within a bubble's interval).
				keys.Clear();
				keys.AddRange(physical.Keys);
				keys.Sort();
				foreach (int c in keys)
				{
					Sim s = physical[c];
					TrafficDay.Leg leg = day.Legs[s.Leg];
					TrafficDrive.Line(leg, s.P, out double x, out double z, out double _, out double _);
					bool far = (x - px) * (x - px) + (z - pz) * (z - pz) > PhysLeave * PhysLeave;
					bool end = !leg.Vanishes && s.P > leg.Length - TrafficDrive.EndZone;
					bool dark = TrafficDrive.InDark(leg, s.P);
					if (!bubble && !dark)
					{
						continue;
					}
					if (!far && !end && !dark)
					{
						continue;
					}
					double d = TrafficDrive.Lateness(leg, s.P, w);
					if (end && d < -1.0)
					{
						// Early at its trip's end: back early (TrafficPhysics), parked there until the day catches up.
						waitedEnd++;
					}
					late.Late(c, s.Leg, w, d, 0.05);
					if (d >= TrafficLate.Least)
					{
						backLate++;
						mostLate = Math.Max(mostLate, d);
						sumLate += d;
					}
					else
					{
						backOnTime++;
					}
					TrafficDay.ReleaseLaneAt(day.Legs[s.Leg], s.P);
					physical.Remove(c);
					// Where it is drawn now against where it was as a physics car: no jump.
					TrafficDay.Place drawn = late.At(c, w, out bool _);
					if (drawn.State == TrafficDay.State.Driving)
					{
						parking.Drawn(c, drawn, out double dx, out double dz, out double _, out double _);
						double jump = Math.Sqrt((dx - x) * (dx - x) + (dz - z) * (dz - z));
						if (jump > worstJump)
						{
							worstJump = jump;
							jumpNote = "car " + c + " leg " + s.Leg + " at " + s.P.ToString("0") + " of " + leg.Length.ToString("0") + ", " + d.ToString("0.0") + " s late";
						}
						jumps += jump > 3.0 ? 1 : 0;
					}
				}
			}
			if (bubble)
			{
				nextBubble += BubbleEvery;
				// Making: every drawn driving car within reach, nearest first, up to the cap; one on a physics car hidden instead. Before
				// that, as TrafficCars does for every drawn car: one about to pull out onto a physics car held back in its space, one in or
				// about to run into a physics car hidden.
				List<(double d, int c, TrafficDay.Place at)> cand = new List<(double, int, TrafficDay.Place)>();
				for (int c = 0; c < day.Cars.Count; c++)
				{
					if (physical.ContainsKey(c))
					{
						continue;
					}
					TrafficDay.Place at = late.At(c, w, out bool hid);
					if (hid || at.State != TrafficDay.State.Driving || at.InTunnel)
					{
						continue;
					}
					if ((at.X - px) * (at.X - px) + (at.Z - pz) * (at.Z - pz) > 900.0 * 900.0)
					{
						continue;
					}
					TrafficDay.Leg al = day.Legs[at.Leg];
					if (physical.Count > 0 && !al.Appears && (at.T < al.PullOut + 1.0 || at.P <= 1.0) && OnPhysical(day, physical, at.X, at.Z, at.Hx, at.Hz, 25.0, 45.0, al.FarSide))
					{
						late.HoldBack(c, at.Leg, w, 1.0);
						heldBack++;
						continue;
					}
					parking.Drawn(c, at, out double hx0, out double hz0, out double _, out double _);
					if (OnPhysical(day, physical, hx0, hz0, at.Hx, at.Hz, Math.Min(40.0, Math.Max(0.0, at.Speed) * 1.5))
						|| (blocked && Math.Abs((ox - hx0) * at.Hx + (oz - hz0) * at.Hz) < TrafficDrive.CarLength + 4.0 && Math.Abs((ox - hx0) * at.Hz - (oz - hz0) * at.Hx) < 4.0))
					{
						// (A drawn car can't be in the player's car either: in the game it is a wall that pushes it.)
						late.Hide(c, w);
						hidden++;
						continue;
					}
					double d = (at.X - px) * (at.X - px) + (at.Z - pz) * (at.Z - pz);
					if (d < PhysReach * PhysReach)
					{
						cand.Add((d, c, at));
					}
				}
				cand.Sort((a, b) => a.d != b.d ? a.d.CompareTo(b.d) : a.c.CompareTo(b.c));
				foreach ((double d, int c, TrafficDay.Place at) e in cand)
				{
					if (physical.Count >= MostPhysical)
					{
						break;
					}
					TrafficDay.Leg leg = day.Legs[e.at.Leg];
					if ((!leg.Appears && e.at.T < leg.PullOut + 0.5) || (!leg.Vanishes && (leg.End - leg.Start - e.at.T < leg.PullIn + 3.0 || e.at.P > leg.Length - TrafficDrive.EndZone - 25.0)) || TrafficDrive.NearTunnel(leg, e.at.P))
					{
						continue;
					}
					TrafficDrive.Mover bm = new TrafficDrive.Mover { Car = e.c, Leg = e.at.Leg, P = e.at.P };
					TrafficDrive.Prepare(day, ref bm);
					if ((bm.Node >= 0 && bm.ToBox < TrafficDrive.CarLength * 0.5) || bm.Back >= 0)
					{
						continue;
					}
					parking.Drawn(e.c, e.at, out double ex, out double ez, out double _, out double _);
					if (blocked && (ex - ox) * (ex - ox) + (ez - oz) * (ez - oz) < 36.0 * 36.0)
					{
						// Drawn into or just short of the player's stopped car (in the game it would have hit it, a wall, or touched it, a loose car): not made physical there, too close to stop.
						continue;
					}
					if (OnPhysical(day, physical, ex, ez, e.at.Hx, e.at.Hz, 20.0))
					{
						late.Hide(e.c, w);
						hidden++;
						continue;
					}
					physical[e.c] = new Sim { Car = e.c, Leg = e.at.Leg, P = e.at.P, V = Math.Max(0.0, e.at.Speed), Profile = TrafficDay.Profile(leg), St = new TrafficDrive.State { WaitNode = -1 } };
					TrafficDay.HoldLaneAt(leg, e.at.P);
					made++;
				}
			}

			// What the drivers see: the drawn driving cars near the player and the physics cars, prepared; the player's car if stopped.
			movers.Clear();
			for (int c = 0; c < day.Cars.Count; c++)
			{
				if (physical.ContainsKey(c))
				{
					continue;
				}
				TrafficDay.Place at = late.At(c, w, out bool hid);
				if (hid || at.State != TrafficDay.State.Driving || at.InTunnel)
				{
					continue;
				}
				if ((at.X - px) * (at.X - px) + (at.Z - pz) * (at.Z - pz) > 600.0 * 600.0)
				{
					continue;
				}
				// The game checks this every frame it draws a car, so here every step.
				TrafficDay.Leg ml = day.Legs[at.Leg];
				if (physical.Count > 0 && !ml.Appears && (at.T < ml.PullOut + 1.0 || at.P <= 1.0) && OnPhysical(day, physical, at.X, at.Z, at.Hx, at.Hz, 25.0, 45.0, ml.FarSide))
				{
					late.HoldBack(c, at.Leg, w, 1.0);
					heldBack++;
					continue;
				}
				parking.Drawn(c, at, out double x, out double z, out double hx, out double hz);
				bool pulling = (!ml.Appears && at.T < ml.PullOut) || (!ml.Vanishes && ml.End - ml.Start - at.T < ml.PullIn);
				TrafficDrive.Mover m = new TrafficDrive.Mover { Car = c, Leg = at.Leg, P = at.P, V = at.Speed, X = x, Z = z, Hx = hx, Hz = hz, Pulling = pulling };
				TrafficDrive.Prepare(day, ref m);
				movers.Add(m);
			}
			int drawnCount = movers.Count;
			keys.Clear();
			keys.AddRange(physical.Keys);
			keys.Sort();
			foreach (int c in keys)
			{
				Sim s = physical[c];
				TrafficDrive.Line(day.Legs[s.Leg], s.P, out double x, out double z, out double hx, out double hz);
				s.Me = new TrafficDrive.Mover { Car = c, Leg = s.Leg, P = s.P, V = s.V, X = x, Z = z, Hx = hx, Hz = hz, Physical = true };
				TrafficDrive.Prepare(day, ref s.Me);
				movers.Add(s.Me);
			}
			obstacles.Clear();
			if (blocked)
			{
				obstacles.Add(new TrafficDrive.Obstacle { X = ox, Z = oz });
			}
			// A late drawn car standing as the day had it, with nothing here to stand for, goes on (TrafficPhysics.LetStandsGo).
			if (physical.Count > 0)
			{
				for (int i = 0; i < drawnCount; i++)
				{
					if (movers[i].V < 0.5 && late.Has(movers[i].Car) && TrafficDrive.NothingToStandFor(day, movers[i], movers, obstacles, w) && late.SkipStand(movers[i].Car, w, out int _, out double _))
					{
						unstuck++;
					}
				}
			}

			// Each physics car drives a step.
			foreach (int c in keys)
			{
				Sim s = physical[c];
				TrafficDay.Leg leg = day.Legs[s.Leg];
				double onP = TrafficDrive.OnTime(leg, w, out double onV);
				watch.Restart();
				double target = TrafficDrive.Target(day, s.Me, s.Profile, movers, obstacles, onP, onV, w, DriveStep, ref s.St);
				targetMs += watch.Elapsed.TotalMilliseconds;
				targets++;
				if (Environment.GetEnvironmentVariable("TV_DRIVE_CAR") == c.ToString() && s.Me.Node >= 0 && s.Me.ToBox < 80.0)
				{
					string others = "";
					foreach (TrafficDrive.Mover o in movers)
					{
						if (o.Car != c && o.Node == s.Me.Node)
						{
							others += " [" + o.Car + (o.Physical ? "p" : "d") + " arms " + o.InArm + ">" + o.OutArm + " toBox " + o.ToBox.ToString("0.0") + " v " + o.V.ToString("0.0") + " x " + (o.InArm != s.Me.InArm && TrafficDay.Crosses(s.Me.Node, s.Me.InArm, s.Me.OutArm, o.InArm, o.OutArm)) + "]";
						}
					}
					Console.WriteLine("    car " + c + " " + (3.0 + w / 100.0).ToString("0.000") + " h node " + s.Me.Node + " arms " + s.Me.InArm + ">" + s.Me.OutArm + " toBox " + s.Me.ToBox.ToString("0.0") + " v " + s.V.ToString("0.0") + " target " + target.ToString("0.0") + " why " + s.St.Why + others);
				}
				s.V = target > s.V ? Math.Min(target, s.V + SimAccel * DriveStep) : Math.Max(target, s.V - SimBrake * DriveStep);
				s.P = Math.Min(leg.Length, s.P + s.V * DriveStep);
				s.Stopped = s.V < 0.5 ? s.Stopped + DriveStep : 0.0;
				s.MostStopped = Math.Max(s.MostStopped, s.Stopped);
				if (s.Stopped > 90.0 && s.StopNote == null)
				{
					s.StopNote = "at 90 s: " + (3.0 + w / 100.0).ToString("0.00") + " h why " + s.St.Why + " held " + s.St.Held + " node " + s.Me.Node + " arms " + s.Me.InArm + ">" + s.Me.OutArm + " toBox " + s.Me.ToBox.ToString("0") + " target " + target.ToString("0.0") + " | front " + Who(day, late, physical, s.St.Front, w) + " | blocker " + Who(day, late, physical, s.St.Blocker, w);
				}
			}

			// Checks: no physics car in another car (physics or drawn) or the player's; no two physics cars crossing in a box.
			for (int i = drawnCount; i < movers.Count; i++)
			{
				TrafficDrive.Mover a = movers[i];
				Sim sa = physical[a.Car];
				TrafficDrive.Line(day.Legs[sa.Leg], sa.P, out double ax, out double az, out double ahx, out double ahz);
				for (int j = 0; j < movers.Count; j++)
				{
					if (j == i)
					{
						continue;
					}
					TrafficDrive.Mover b = movers[j];
					double bxp = b.X, bzp = b.Z, bhx = b.Hx, bhz = b.Hz;
					if (j >= drawnCount)
					{
						if (j < i)
						{
							continue;
						}
						TrafficDrive.Line(day.Legs[physical[b.Car].Leg], physical[b.Car].P, out bxp, out bzp, out bhx, out bhz);
					}
					if (InEachOther(ax, az, ahx, ahz, bxp, bzp, bhx, bhz))
					{
						long key = Math.Min(a.Car, b.Car) * 100000L + Math.Max(a.Car, b.Car);
						if (crashed.Add(key) && Environment.GetEnvironmentVariable("TV_DRIVE_NOTES") != null && crashed.Count <= 12)
						{
							TrafficDay.Leg lb = day.Legs[b.Leg];
							Console.WriteLine("    in each other: physics " + a.Car + " leg " + a.Leg + " P " + sa.P.ToString("0.0") + " v " + sa.V.ToString("0.0") + " why " + sa.St.Why + " edge " + a.Edge + "/" + a.Dir + " s " + a.S.ToString("0") + " | " + (j >= drawnCount ? "physics " : "drawn ") + b.Car + " leg " + b.Leg + " P " + b.P.ToString("0.0") + " v " + b.V.ToString("0.0")
								+ " edge " + b.Edge + "/" + b.Dir + " s " + b.S.ToString("0") + " T " + (j >= drawnCount ? 0.0 : TrafficDrive.LegClock(lb, w) - lb.Start).ToString("0.0") + " pullOut " + lb.PullOut.ToString("0.0") + " pullIn " + lb.PullIn.ToString("0.0") + " len " + lb.Length.ToString("0") + " farSide " + lb.FarSide + " farEnd " + lb.FarEnd
								+ " | d " + Math.Sqrt((bxp - ax) * (bxp - ax) + (bzp - az) * (bzp - az)).ToString("0.0") + " cos " + (ahx * bhx + ahz * bhz).ToString("0.00") + " at " + (3.0 + w / 100.0).ToString("0.00") + " h");
						}
						if (crashNote.Length == 0)
						{
							crashNote = "car " + a.Car + " (physics, leg " + a.Leg + " at " + sa.P.ToString("0") + ") and car " + b.Car + (j >= drawnCount ? " (physics" : " (drawn") + ", leg " + b.Leg + " at " + b.P.ToString("0") + ") at (" + ax.ToString("0") + ", " + az.ToString("0") + "), " + (3.0 + w / 100.0).ToString("0.00") + " h";
						}
					}
					if (j >= drawnCount && InBox(a, out int an, out int ai, out int ao) && InBox(b, out int bn, out int bi, out int bo) && an == bn && ai != bi && TrafficDay.Crosses(an, ai, ao, bi, bo))
					{
						long key = Math.Min(a.Car, b.Car) * 100000L + Math.Max(a.Car, b.Car);
						if (crossed.Add(key) && Environment.GetEnvironmentVariable("TV_DRIVE_NOTES") != null)
						{
							Sim sb = physical[b.Car];
							Console.WriteLine("    crossing in box " + an + " (box " + Traffic.Nodes[an].Box.ToString("0") + "): " + a.Car + " arms " + a.InArm + ">" + a.OutArm + " toBox " + a.ToBox.ToString("0.0") + " v " + sa.V.ToString("0.0") + " why " + sa.St.Why + " waited " + sa.St.Waited.ToString("0.0")
								+ " | " + b.Car + " arms " + b.InArm + ">" + b.OutArm + " toBox " + b.ToBox.ToString("0.0") + " v " + sb.V.ToString("0.0") + " why " + sb.St.Why + " waited " + sb.St.Waited.ToString("0.0") + " at " + (3.0 + w / 100.0).ToString("0.00") + " h");
						}
						if (crossNote.Length == 0)
						{
							crossNote = "cars " + a.Car + " and " + b.Car + " at node " + an + ", " + (3.0 + w / 100.0).ToString("0.00") + " h";
						}
					}
				}
				if (blocked)
				{
					double dx = ox - ax, dz = oz - az;
					if (Math.Abs(dx * ahx + dz * ahz) < TrafficDrive.CarLength - 1.0 && Math.Abs(dx * ahz - dz * ahx) < 4.0)
					{
						if (hitPlayer == 0)
						{
							hitNote = " (first car " + sa.Car + ", leg " + sa.Leg + " at " + sa.P.ToString("0") + " v " + sa.V.ToString("0.0") + ", " + (3.0 + w / 100.0).ToString("0.00") + " h, along " + (dx * ahx + dz * ahz).ToString("0.0") + " across " + (dx * ahz - dz * ahx).ToString("0.0") + ")";
						}
						hitPlayer++;
					}
				}
			}
		}
		// A physics car stopped over 90 s fails only if it stood well longer than the day had the same car stand over the run: one in
		// the day's own queue (Kingsbridge's junctions at the 5 pm peak, minutes on some seeds) stands where the player sees why, the
		// cars in front standing too, and that queue is the day's (its junction waiting, above).
		int longWaits = 0, dayQueue = 0;
		double longest = 0.0;
		foreach (Sim s in physical.Values)
		{
			longest = Math.Max(longest, s.MostStopped);
			if (s.MostStopped > 90.0)
			{
				if (s.MostStopped > DayStop(day, s.Car, w0) + 30.0)
				{
					longWaits++;
				}
				else
				{
					dayQueue++;
				}
			}
			if (s.MostStopped > 90.0 && Environment.GetEnvironmentVariable("TV_DRIVE_NOTES") != null)
			{
				TrafficDrive.Mover lm = new TrafficDrive.Mover { Car = s.Car, Leg = s.Leg, P = s.P };
				TrafficDrive.Prepare(day, ref lm);
				Console.WriteLine("    long stop: car " + s.Car + " leg " + s.Leg + " at " + s.P.ToString("0") + " of " + day.Legs[s.Leg].Length.ToString("0") + " why " + s.St.Why + " held " + s.St.Held + " node " + lm.Node + " arms " + lm.InArm + ">" + lm.OutArm + " toBox " + lm.ToBox.ToString("0") + " stopped " + s.MostStopped.ToString("0") + " s (the day's own car: " + DayStop(day, s.Car, w0).ToString("0") + " s) lights " + LightsTrace(day, lm.Node, lm.InArm, TrafficDay.SimOfHour(17.0)) + " || " + s.StopNote);
			}
		}
		bool ok = crashed.Count <= MostMeetings && crossed.Count <= MostCrossings && hitPlayer == 0 && jumps == 0 && (blocked || longWaits == 0);
		Console.WriteLine("  physics cars, " + name + ", 10 minutes from 17:00: " + made + " made physical, back " + backOnTime + " on time and " + backLate + " late (most " + mostLate.ToString("0") + " s, mean "
			+ (backLate > 0 ? sumLate / backLate : 0.0).ToString("0.0") + " s), " + waitedEnd + " back early at a trip's end, " + hidden + " drawn cars hidden on a physics car, " + heldBack + " held back from pulling out onto one, " + unstuck + " late drawn cars let off a stand; " + crashed.Count + " pairs in each other (at most " + MostMeetings + ")"
			+ (crashNote.Length > 0 ? " (first " + crashNote + ")" : "") + ", " + crossed.Count + " pairs crossing in a box (at most " + MostCrossings + ")" + (crossNote.Length > 0 ? " (first " + crossNote + ")" : "") + (blocked ? ", " + hitPlayer + " steps in the player's car" + hitNote : "")
			+ "; jumps going back: worst " + worstJump.ToString("0.0") + (jumpNote.Length > 0 ? " (" + jumpNote + ")" : "") + ", " + jumps + " over 3; longest stop of the cars still physical " + longest.ToString("0") + " s"
			+ (blocked ? "" : " (" + longWaits + " over 90 s and 30 s longer than the day's car, " + dayQueue + " over 90 s in the day's own queue)") + "; " + (targets > 0 ? targetMs * 1000.0 / targets : 0.0).ToString("0.0") + " us a target" + (ok ? "" : " FAIL"));
		return ok;
	}

	/// <summary>The junction box a mover is in (its near half, or the far half of the one it passed) and its arms.</summary>
	private static bool InBox(in TrafficDrive.Mover m, out int node, out int inArm, out int outArm)
	{
		if (m.Node >= 0 && m.ToBox < -1.0)
		{
			node = m.Node;
			inArm = m.InArm;
			outArm = m.OutArm;
			return true;
		}
		node = m.Back;
		inArm = m.BackIn;
		outArm = m.BackOut;
		return m.Back >= 0;
	}

	/// <summary>Whether a car drawn at (x, z) facing (hx, hz) would be on, or within <paramref name="behind"/> behind, a physics car (as TrafficPhysics tests).</summary>
	private static bool OnPhysical(TrafficDay day, Dictionary<int, Sim> physical, double x, double z, double hx, double hz, double behind, double before = 0.0, bool farSide = false)
	{
		foreach (Sim s in physical.Values)
		{
			TrafficDrive.Line(day.Legs[s.Leg], s.P, out double bx, out double bz, out double bhx, out double bhz);
			double dx = bx - x, dz = bz - z;
			double along = dx * hx + dz * hz, side = Math.Abs(dx * hz - dz * hx);
			if ((side < 4.0 && along > -TrafficLate.Gap - before && along < TrafficLate.Gap + behind) || TrafficDrive.Overlap(x, z, hx, hz, bx, bz, bhx, bhz, 0.5) || (farSide && TrafficDrive.AcrossItsWay(along, side)))
			{
				return true;
			}
		}
		return false;
	}

	/// <summary>The day's own car's longest stop while driving over the run (for the notes: a physics car standing as long is in the day's queue).</summary>
	private static double DayStop(TrafficDay day, int c, double w0)
	{
		double most = 0.0, run = 0.0;
		for (double t = 0.0; t < DriveRun; t += 1.0)
		{
			double w = w0 + t;
			if (w >= Traffic.DaySeconds)
			{
				w -= Traffic.DaySeconds;
			}
			TrafficDay.Place at = day.At(c, w);
			run = at.State == TrafficDay.State.Driving && at.Speed < 0.5 ? run + 1.0 : 0.0;
			most = Math.Max(most, run);
		}
		return most;
	}

	private static string Who(TrafficDay day, TrafficLate late, Dictionary<int, Sim> physical, int c, double w)
	{
		if (c < 0)
		{
			return "-";
		}
		if (physical.TryGetValue(c, out Sim s))
		{
			return "physics " + c + " leg " + s.Leg + " at " + s.P.ToString("0") + " v " + s.V.ToString("0.0") + " why " + s.St.Why + " held " + s.St.Held + " stopped " + s.MostStopped.ToString("0");
		}
		TrafficDay.Place at = late.At(c, w, out bool hid);
		string t = "drawn " + c + " leg " + at.Leg + " state " + at.State + " hid " + hid + " at " + at.P.ToString("0") + " v " + at.Speed.ToString("0.0");
		if (at.Leg >= 0)
		{
			TrafficDay.Leg l = day.Legs[at.Leg];
			t += " T " + at.T.ToString("0.0") + " of " + (l.End - l.Start).ToString("0.0") + " pullOut " + l.PullOut.ToString("0.0") + " pullIn " + l.PullIn.ToString("0.0") + " ledger " + (late.TryGet(c, out TrafficLate.Entry e) ? e.D.ToString("0.0") : "-") + " late " + TrafficDrive.Lateness(l, at.P, w).ToString("0") + " len " + l.Length.ToString("0");
		}
		return t;
	}

	/// <summary>Two cars' bodies overlapping by more than a touch (the shared test, each box half a unit smaller).</summary>
	private static bool InEachOther(double ax, double az, double ahx, double ahz, double bx, double bz, double bhx, double bhz)
	{
		return TrafficDrive.Overlap(ax, az, ahx, ahz, bx, bz, bhx, bhz, -0.5);
	}

}
