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
	/// <summary>How many more physics cars may be made, over <see cref="MostPhysical"/>, of cars that have to slow for something the day doesn't know of (TrafficCars.MostYieldPhysics).</summary>
	private const int MostYieldPhysical = 16;
	/// <summary>TV_NOYIELDPHYS=1: held cars stay played back (as before 2026-11-26's plan), to compare.</summary>
	private static readonly int YieldCar = int.TryParse(Environment.GetEnvironmentVariable("TV_YIELD_CAR"), out int yc) ? yc : -1;
	private static readonly bool NoYieldPhysics = Environment.GetEnvironmentVariable("TV_NOYIELDPHYS") != null;
	/// <summary>
	/// What a run may have: none. Two cars in each other, or two crossing in a junction's box, is a crash a player sees. (Until 2026-11-26
	/// a run might have 6 and 3: those were crashes counted as passes.)
	/// </summary>
	private const int MostMeetings = 0, MostCrossings = 0;
	/// <summary>A physics car may stand this long (s) at most; longer is a gridlock. With the player's car stopped across a lane, the queue behind it (each car standing behind the next, the first at the player's car: <see cref="BehindPlayer"/>) may stand for ever: the drivers don't go round it.</summary>
	private const double LongestStand = 90.0;
	/// <summary>The game car's pull and braking as the driver uses them (units/s²): a level-0 car pulls about 5 to 8 off the line; brakes up to 40.</summary>
	private const double SimAccel = 6.0, SimBrake = 30.0;

	private sealed class Sim
	{
		public int Car, Leg;
		public double P, V, Stopped, MostStopped;
		/// <summary>The longest it stood over <see cref="LongestStand"/> not in the queue behind the player's car, and whether its stand now is in it.</summary>
		public double Unexcused;
		public bool StandExcused;
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
			int st = day.Lights.State(node, arm, -1, w0 + i * 10.0);
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
		HashSet<long> crashed = new HashSet<long>(), crossed = new HashSet<long>(), drawnCrashed = new HashSet<long>();
		List<Sim> wentBack = new List<Sim>();
		string drawnNote = "";
		// What last held each drawn car (a car, -2 the player's car): for the chains.
		Dictionary<int, int> heldBy = new Dictionary<int, int>();
		Func<int, int> waitsOn = x => physical.TryGetValue(x, out Sim ws) ? TrafficDrive.Waiting(ws.St) : late.Yielding(x) && heldBy.TryGetValue(x, out int h) ? h : -1;
		List<TrafficLate.Near> drawnWas = new List<TrafficLate.Near>(), drawnIs = new List<TrafficLate.Near>(), appearing = new List<TrafficLate.Near>();
		int made = 0, madeYield = 0, mostAtOnce = 0, unstuck = 0, backOnTime = 0, backLate = 0, waitedEnd = 0, yielded = 0, hiddenByHold = 0, heldBack = 0, hitPlayer = 0, jumps = 0, targets = 0;
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
					wentBack.Add(s);
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
				// Making: every drawn driving car within reach, nearest first, up to the cap; not one on a physics car (it stays drawn, held
				// behind it). Before that, as TrafficCars does for every drawn car: one about to pull out onto a physics car held back in its
				// space. (One in or about to run into a physics car is held behind it every step, below: never hidden.)
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
					if (physical.Count > 0 && late.CanHoldBack(c, at) && OnPhysical(day, physical, at.X, at.Z, at.Hx, at.Hz, 25.0, 45.0, al.FarSide))
					{
						late.HoldBack(c, at.Leg, w, 1.0);
						heldBack++;
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
						continue;
					}
					physical[e.c] = new Sim { Car = e.c, Leg = e.at.Leg, P = e.at.P, V = Math.Max(0.0, late.DrawnSpeed(e.c, e.at)), Profile = TrafficDay.Profile(leg), St = new TrafficDrive.State { WaitNode = -1 } };
					late.Unheld(e.c);
					TrafficDay.HoldLaneAt(leg, e.at.P);
					made++;
					mostAtOnce = Math.Max(mostAtOnce, physical.Count);
				}
			}

			// What the drivers see: the drawn driving cars near the player and the physics cars, prepared; the player's car if stopped.
			movers.Clear();
			(drawnWas, drawnIs) = (drawnIs, drawnWas);
			appearing.Clear();
			drawnIs.Clear();
			for (int c = 0; c < day.Cars.Count; c++)
			{
				if (physical.ContainsKey(c))
				{
					continue;
				}
				TrafficDay.Place at = late.At(c, w, out bool hid);
				if (!hid && at.State == TrafficDay.State.Parked && at.Leg >= 0)
				{
					// Parked cars near the player, as TrafficCars draws them: what a late car pulling in stops for, its space taken.
					TrafficParking.Spot here = parking.Parked(c, at.Leg);
					if (!here.Away && (here.X - px) * (here.X - px) + (here.Z - pz) * (here.Z - pz) < 600.0 * 600.0)
					{
						drawnIs.Add(new TrafficLate.Near { Car = c, X = here.X, Z = here.Z, Hx = here.Hx, Hz = here.Hz, Moving = false, Late = late.Has(c) });
					}
					continue;
				}
				if (hid || at.State != TrafficDay.State.Driving || at.InTunnel || late.RoundTheCorner(c, at))
				{
					continue;
				}
				// Come out of a space out of sight onto the road: it waits round the corner while that would put it in a car (TrafficCars.Draw).
				if (late.Appearing(c, at) && !drawnWas.Exists(o => o.Car == c))
				{
					if (late.StartBlocked(c, at, drawnWas, appearing))
					{
						late.HoldBack(c, at.Leg, w, 1.0);
						continue;
					}
					appearing.Add(new TrafficLate.Near { Car = c, X = at.X, Z = at.Z, Hx = at.Hx, Hz = at.Hz, Moving = true, Late = late.Has(c), Speed = late.DrawnSpeed(c, at) });
				}
				if ((at.X - px) * (at.X - px) + (at.Z - pz) * (at.Z - pz) > 600.0 * 600.0)
				{
					continue;
				}
				// The game checks this every frame it draws a car, so here every step.
				TrafficDay.Leg ml = day.Legs[at.Leg];
				if (physical.Count > 0 && late.CanHoldBack(c, at) && OnPhysical(day, physical, at.X, at.Z, at.Hx, at.Hz, 25.0, 45.0, ml.FarSide))
				{
					late.HoldBack(c, at.Leg, w, 1.0);
					heldBack++;
					continue;
				}
				// Held behind a physics car, the player's stopped car or a late drawn car in its way (TrafficPhysics.YieldDrawn), never hidden.
				{
					parking.Drawn(c, at, out double yx, out double yz, out double yhx, out double yhz);
					double look = TrafficDrive.YieldLook(Math.Max(0.0, at.Speed));
					double free = double.MaxValue;
					int by = -1;
					bool pullInNow = !ml.Vanishes && ml.End - ml.Start - at.T < ml.PullIn;
					bool pullingNow = (!ml.Appears && at.T < ml.PullOut) || pullInNow;
					foreach (Sim ps in physical.Values)
					{
						TrafficDrive.Line(day.Legs[ps.Leg], ps.P, out double bx, out double bz, out double bhx, out double bhz);
						if (TrafficDrive.PullsPast(pullingNow, pullInNow, yx, yz, yhx, yhz, bx, bz, bhx, bhz, ps.V) || TrafficDrive.Ring(c, ps.Car, waitsOn))
						{
							continue;
						}
						double fp = TrafficDrive.FreeAhead(yx, yz, yhx, yhz, bx, bz, bhx, bhz, look, ps.V);
						if (fp < free)
						{
							free = fp;
							by = ps.Car;
						}
					}
					if (blocked)
					{
						double fo = TrafficDrive.FreeAhead(yx, yz, yhx, yhz, ox, oz, -yhz, yhx, look, 0.0);
						if (fo < free)
						{
							free = fo;
							by = -2;
						}
					}
					bool meLate = late.Has(c);
					bool pullingIn = !ml.Vanishes && ml.End - ml.Start - at.T < ml.PullIn + 2.0, pullingOut = !ml.Appears && at.T < ml.PullOut + 1.0;
					bool keptIn = false;
					if (late.CanHoldBack(c, at))
					{
						foreach (TrafficLate.Near o in drawnWas)
						{
							if (o.Moving && o.Car != c && !o.PullingIn && (meLate || o.Late) && TrafficDrive.PullOutBlocked(at.X, at.Z, at.Hx, at.Hz, o.X, o.Z, o.Hx, o.Hz))
							{
								late.HoldBack(c, at.Leg, w, 1.0);
								heldBack++;
								keptIn = true;
								break;
							}
						}
					}
					if (keptIn)
					{
						continue;
					}
					TrafficParking.Spot mine = pullingIn ? parking.Parked(c, at.Leg) : default;
					if (pullingIn && late.SpaceTaken(c, mine, w))
					{
						free = 0.0;
					}
					foreach (TrafficLate.Near o in drawnWas)
					{
						if ((pullingOut && o.PullingIn) || o.Car == c || (!meLate && !o.Late))
						{
							continue;
						}
						if (pullingIn && !mine.Away && (o.X - mine.X) * (o.X - mine.X) + (o.Z - mine.Z) * (o.Z - mine.Z) < TrafficLate.InSpace * TrafficLate.InSpace)
						{
							// Its space still taken, or being left: it waits on its lane by it (TrafficCars.YieldDrawn).
							free = 0.0;
							by = o.Car;
							continue;
						}
						if (o.Moving && !TrafficDrive.Ring(c, o.Car, waitsOn))
						{
							double fd = TrafficDrive.FreeAhead(yx, yz, yhx, yhz, o.X, o.Z, o.Hx, o.Hz, look, o.Speed);
							if (fd < free)
							{
								free = fd;
								by = o.Car;
							}
						}
					}
					if (YieldCar == c && Math.Abs(t - Math.Round(t)) < DriveStep * 0.5 && (int)Math.Round(t) % 5 == 0)
					{
						string who = "";
						foreach (Sim ps in physical.Values)
						{
							TrafficDrive.Line(day.Legs[ps.Leg], ps.P, out double bx, out double bz, out double bhx, out double bhz);
							double f1 = TrafficDrive.FreeAhead(yx, yz, yhx, yhz, bx, bz, bhx, bhz, look, ps.V);
							if (f1 < 1e9)
							{
								who += " p" + ps.Car + " free " + f1.ToString("0.0") + " v " + ps.V.ToString("0.0") + " pullsPast " + TrafficDrive.PullsPast(pullingNow, pullInNow, yx, yz, yhx, yhz, bx, bz, bhx, bhz, ps.V);
							}
						}
						foreach (TrafficLate.Near o in drawnWas)
						{
							double f1 = TrafficDrive.FreeAhead(yx, yz, yhx, yhz, o.X, o.Z, o.Hx, o.Hz, look, o.Speed);
							if (f1 < 1e9 && o.Car != c)
							{
								who += " d" + o.Car + " free " + f1.ToString("0.0") + " moving " + o.Moving + " late " + o.Late;
							}
						}
						Console.WriteLine("    yield car " + c + " t " + t.ToString("0") + " T " + at.T.ToString("0.0") + " pulling " + pullingNow + " free " + free.ToString("0.0") + " at (" + yx.ToString("0") + ", " + yz.ToString("0") + ") h (" + yhx.ToString("0.00") + ", " + yhz.ToString("0.00") + "):" + who);
					}
					bool wasHeld = late.Yielding(c);
					if (late.Yield(c, at, w, DriveStep, free, out double _))
					{
						heldBy[c] = by;
						yielded += wasHeld ? 0 : 1;
						at = late.At(c, w, out bool _);
						if (at.State != TrafficDay.State.Driving || at.InTunnel)
						{
							// Held off its road: the game would stop drawing it (TrafficCars.Draw, "held back off the road").
							hiddenByHold += at.InTunnel ? 0 : 1;
							continue;
						}
					}
					if (!late.Yielding(c))
					{
						heldBy.Remove(c);
					}
					// A car played back from the day that has to slow for something the day doesn't know of becomes a physics car at once,
					// where it can (TrafficCars.YieldDrawn): a held car can't negotiate a junction, and physics cars waiting on held cars
					// waiting on them gridlocked (2026-11-26).
					if (late.Yielding(c) && MakeForYield(day, late, physical, c, at, px, pz, blocked, ox, oz))
					{
						madeYield++;
						mostAtOnce = Math.Max(mostAtOnce, physical.Count);
						continue;
					}
				}
				parking.Drawn(c, at, out double x, out double z, out double hx, out double hz);
				drawnIs.Add(new TrafficLate.Near { Car = c, X = x, Z = z, Hx = hx, Hz = hz, Moving = true, Late = late.Has(c), PullingIn = !ml.Vanishes && ml.End - ml.Start - at.T < ml.PullIn + 2.0, Speed = late.DrawnSpeed(c, at) });
				bool pulling = (!ml.Appears && at.T < ml.PullOut) || (!ml.Vanishes && ml.End - ml.Start - at.T < ml.PullIn);
				TrafficDrive.Mover m = new TrafficDrive.Mover { Car = c, Leg = at.Leg, P = at.P, V = late.DrawnSpeed(c, at), X = x, Z = z, Hx = hx, Hz = hz, Pulling = pulling };
				TrafficDrive.Prepare(day, ref m);
				movers.Add(m);
			}
			int drawnCount = movers.Count;
			// No two cars played back from the day drawn in each other because one is late (on-time ones the day keeps apart itself).
			for (int i = 0; i < drawnIs.Count; i++)
			{
				for (int j = i + 1; j < drawnIs.Count; j++)
				{
					if ((drawnIs[i].Late || drawnIs[j].Late) && TrafficLate.Overlap(drawnIs[i], drawnIs[j]))
					{
						long key = Math.Min(drawnIs[i].Car, drawnIs[j].Car) * 100000L + Math.Max(drawnIs[i].Car, drawnIs[j].Car);
						if (drawnCrashed.Add(key) && drawnNote.Length == 0)
						{
							drawnNote = " (first cars " + drawnIs[i].Car + " and " + drawnIs[j].Car + " at (" + drawnIs[i].X.ToString("0") + ", " + drawnIs[i].Z.ToString("0") + "), " + (3.0 + w / 100.0).ToString("0.00") + " h)";
						}
					}
				}
			}
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
				if (s.Stopped == 0.0)
				{
					s.StandExcused = false;
				}
				else if (s.Stopped > LongestStand)
				{
					// Standing long: in the queue behind the player's stopped car (the drivers don't go round it), or not.
					if (!s.StandExcused && blocked && BehindPlayer(day, physical, heldBy, waitsOn, s, ox, oz))
					{
						s.StandExcused = true;
					}
					if (!s.StandExcused)
					{
						s.Unexcused = Math.Max(s.Unexcused, s.Stopped);
					}
				}
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
		// A physics car stopped over LongestStand fails: there is no queue so long at any light here. Only the queue behind the player's
		// stopped car may stand for ever. (Until 2026-11-26 a car stopped as long as the day had it stand passed: the day's own gridlock
		// counted as fine.)
		int longWaits = 0, behindPlayer = 0, dayAlso = 0;
		double longest = 0.0;
		// Every car that was a physics car in the run: those gone back on their timetable too (one that stood minutes and went back out of
		// reach went uncounted until 2026-11-26).
		List<Sim> all = new List<Sim>(wentBack);
		all.AddRange(physical.Values);
		foreach (Sim s in all)
		{
			longest = Math.Max(longest, s.MostStopped);
			if (s.Unexcused > LongestStand)
			{
				longWaits++;
				dayAlso += DayStop(day, s.Car, w0) > LongestStand ? 1 : 0;
			}
			else if (s.MostStopped > LongestStand)
			{
				behindPlayer++;
			}
			if (s.MostStopped > LongestStand && Environment.GetEnvironmentVariable("TV_DRIVE_NOTES") != null)
			{
				TrafficDrive.Mover lm = new TrafficDrive.Mover { Car = s.Car, Leg = s.Leg, P = s.P };
				TrafficDrive.Prepare(day, ref lm);
				Console.WriteLine("    long stop: car " + s.Car + " leg " + s.Leg + " at " + s.P.ToString("0") + " of " + day.Legs[s.Leg].Length.ToString("0") + " why " + s.St.Why + " held " + s.St.Held + " node " + lm.Node + " arms " + lm.InArm + ">" + lm.OutArm + " toBox " + lm.ToBox.ToString("0") + " stopped " + s.MostStopped.ToString("0") + " s (the day's own car: " + DayStop(day, s.Car, w0).ToString("0") + " s) lights " + LightsTrace(day, lm.Node, lm.InArm, TrafficDay.SimOfHour(17.0)) + " || " + s.StopNote);
			}
		}
		if (Environment.GetEnvironmentVariable("TV_DRIVE_CHAIN") != null)
		{
			// What each long-stopped physics car waits on, and what that waits on: the chains' heads are the gridlock's causes.
			double wEnd = w0 + DriveRun;
			if (wEnd >= Traffic.DaySeconds)
			{
				wEnd -= Traffic.DaySeconds;
			}
			Dictionary<string, int> heads = new Dictionary<string, int>();
			foreach (Sim s0 in physical.Values)
			{
				if (s0.Stopped < LongestStand)
				{
					continue;
				}
				string chain = "";
				HashSet<int> seen = new HashSet<int>();
				int c = s0.Car;
				string head = "";
				for (int hop = 0; hop < 12; hop++)
				{
					if (!seen.Add(c))
					{
						chain += " LOOP";
						head = "loop";
						break;
					}
					if (physical.TryGetValue(c, out Sim s))
					{
						TrafficDrive.Mover lm = new TrafficDrive.Mover { Car = s.Car, Leg = s.Leg, P = s.P };
						TrafficDrive.Prepare(day, ref lm);
						chain += " > p" + c + "(why " + s.St.Why + " held " + s.St.Held + " n" + lm.Node + " " + lm.InArm + ">" + lm.OutArm + " toBox " + lm.ToBox.ToString("0") + " back " + lm.Back + " v " + s.V.ToString("0.0") + ")";
						int next = s.St.Why == 2 && s.St.Blocker >= 0 ? s.St.Blocker : s.St.Front >= 0 ? s.St.Front : s.St.Blocker;
						if (next < 0)
						{
							head = "physics why " + s.St.Why + " held " + s.St.Held + " node " + lm.Node + " " + lm.InArm + ">" + lm.OutArm;
							break;
						}
						c = next;
					}
					else
					{
						TrafficDay.Place at = late.At(c, wEnd, out bool _);
						bool held = late.Yielding(c);
						TrafficDrive.Mover lm = new TrafficDrive.Mover { Car = c, Leg = at.Leg, P = at.P };
						if (at.Leg >= 0)
						{
							TrafficDrive.Prepare(day, ref lm);
						}
						string legInfo = at.Leg >= 0 ? " leg " + at.Leg + " T " + at.T.ToString("0.0") + " P " + at.P.ToString("0") + "/" + day.Legs[at.Leg].Length.ToString("0") + " appears " + day.Legs[at.Leg].Appears + " vanishes " + day.Legs[at.Leg].Vanishes + " pullOut " + day.Legs[at.Leg].PullOut.ToString("0.0") + " pullIn " + day.Legs[at.Leg].PullIn.ToString("0.0") + " dur " + (day.Legs[at.Leg].End - day.Legs[at.Leg].Start).ToString("0.0") : " no leg";
						chain += " > d" + c + legInfo + "(" + at.State + " v " + late.DrawnSpeed(c, at).ToString("0.0") + " day v " + at.Speed.ToString("0.0") + (held ? " HELD" : "") + (late.Has(c) ? " late" : "") + " n" + lm.Node + " toBox " + lm.ToBox.ToString("0") + " back " + lm.Back + " drivable " + (at.Leg >= 0 && TrafficDrive.Drivable(day, at)) + ")";
						if (at.Leg >= 0 && !day.Legs[at.Leg].Vanishes && day.Legs[at.Leg].End - day.Legs[at.Leg].Start - at.T < day.Legs[at.Leg].PullIn + 2.0)
						{
							TrafficParking.Spot sp = day.Parking.Parked(c, at.Leg);
							chain += " space (" + sp.X.ToString("0") + ", " + sp.Z.ToString("0") + ") taken by:";
							foreach (TrafficLate.Near o in drawnIs)
							{
								if (o.Car != c && (o.X - sp.X) * (o.X - sp.X) + (o.Z - sp.Z) * (o.Z - sp.Z) < TrafficLate.InSpace * TrafficLate.InSpace)
								{
									TrafficDay.Place oa = late.At(o.Car, wEnd, out bool _);
									chain += " d" + o.Car + " " + oa.State + " T " + oa.T.ToString("0.0") + " late " + late.Has(o.Car) + " yielding " + late.Yielding(o.Car) + " pullingIn " + o.PullingIn;
								}
							}
							foreach (Sim ps in physical.Values)
							{
								TrafficDrive.Line(day.Legs[ps.Leg], ps.P, out double bx, out double bz, out double _, out double _);
								if ((bx - sp.X) * (bx - sp.X) + (bz - sp.Z) * (bz - sp.Z) < TrafficLate.InSpace * TrafficLate.InSpace)
								{
									chain += " p" + ps.Car;
								}
							}
						}
						head = "drawn" + (held ? " held" : late.Has(c) ? " late" : " on time") + (at.Speed < 0.5 ? " standing in the day" : " moving in the day");
						if (held && heldBy.TryGetValue(c, out int hb))
						{
							if (hb == -2)
							{
								chain += " > the player's car";
								head = "player";
								break;
							}
							head += " (held by " + hb + ")";
							c = hb;
							continue;
						}
						break;
					}
				}
				heads[head] = heads.TryGetValue(head, out int n) ? n + 1 : 1;
				Console.WriteLine("    chain: " + chain);
			}
			foreach (KeyValuePair<string, int> kv in heads)
			{
				Console.WriteLine("    chain heads: " + kv.Key + " " + kv.Value);
			}
		}
		bool ok = crashed.Count <= MostMeetings && crossed.Count <= MostCrossings && drawnCrashed.Count == 0 && hitPlayer == 0 && jumps == 0 && longWaits == 0 && hiddenByHold == 0;
		Console.WriteLine("  physics cars, " + name + ", 10 minutes from 17:00: " + made + " made physical by the nearest and " + madeYield + " for having to slow (at most " + mostAtOnce + " at once), back " + backOnTime + " on time and " + backLate + " late (most " + mostLate.ToString("0") + " s, mean "
			+ (backLate > 0 ? sumLate / backLate : 0.0).ToString("0.0") + " s), " + waitedEnd + " back early at a trip's end, " + yielded + " drawn cars held behind another (" + hiddenByHold + " hidden by it), " + heldBack + " held back from pulling out onto one, " + unstuck + " late drawn cars let off a stand; " + crashed.Count + " pairs in each other (at most " + MostMeetings + ")"
			+ (crashNote.Length > 0 ? " (first " + crashNote + ")" : "") + ", " + crossed.Count + " pairs crossing in a box (at most " + MostCrossings + ")" + (crossNote.Length > 0 ? " (first " + crossNote + ")" : "") + (blocked ? ", " + hitPlayer + " steps in the player's car" + hitNote : "")
			+ "; jumps going back: worst " + worstJump.ToString("0.0") + (jumpNote.Length > 0 ? " (" + jumpNote + ")" : "") + ", " + jumps + " over 3; longest stop of any physics car " + longest.ToString("0") + " s"
			+ " (" + longWaits + " over " + LongestStand.ToString("0") + " s, " + dayAlso + " of them where the day has the same car stand that long too" + (blocked ? ", " + behindPlayer + " in the queue behind the player's car" : "") + "); " + drawnCrashed.Count + " pairs of played-back cars in each other because late" + drawnNote + "; " + (targets > 0 ? targetMs * 1000.0 / targets : 0.0).ToString("0.0") + " us a target" + (ok ? "" : " FAIL"));
		return ok;
	}

	/// <summary>
	/// Whether physics car <paramref name="s"/> stands in the queue behind the player's stopped car at (ox, oz): what it waits for, and
	/// what that waits for (<paramref name="waitsOn"/>: physics cars' drivers, drawn cars held back), ends at the player's car: a physics
	/// car standing within two cars of it with nothing it waits for, or a drawn car held back by it (<paramref name="heldBy"/> -2).
	/// </summary>
	private static bool BehindPlayer(TrafficDay day, Dictionary<int, Sim> physical, Dictionary<int, int> heldBy, Func<int, int> waitsOn, Sim s, double ox, double oz)
	{
		HashSet<int> seen = new HashSet<int>();
		int c = s.Car;
		while (seen.Add(c))
		{
			if (physical.TryGetValue(c, out Sim p))
			{
				int n = TrafficDrive.Waiting(p.St);
				if (n < 0)
				{
					TrafficDrive.Line(day.Legs[p.Leg], p.P, out double x, out double z, out double _, out double _);
					return p.V < 0.5 && (x - ox) * (x - ox) + (z - oz) * (z - oz) < (2.0 * TrafficDrive.CarLength + 8.0) * (2.0 * TrafficDrive.CarLength + 8.0);
				}
				c = n;
				continue;
			}
			if (heldBy.TryGetValue(c, out int h) && h == -2)
			{
				return true;
			}
			c = waitsOn(c);
			if (c < 0)
			{
				return false;
			}
		}
		return false;
	}

	/// <summary>
	/// Makes car <paramref name="c"/>, played back at <paramref name="at"/> and held up, a physics car if it may be one (as
	/// TrafficCars.ClaimForYield does): drivable, within reach, under the cap with <see cref="MostYieldPhysical"/> more, and not in a
	/// physics car or the player's car.
	/// </summary>
	private static bool MakeForYield(TrafficDay day, TrafficLate late, Dictionary<int, Sim> physical, int c, TrafficDay.Place at, double px, double pz, bool blocked, double ox, double oz)
	{
		if (NoYieldPhysics || physical.Count >= MostPhysical + MostYieldPhysical || !TrafficDrive.Drivable(day, at, true))
		{
			return false;
		}
		day.Parking.Drawn(c, at, out double x, out double z, out double hx, out double hz);
		if ((x - px) * (x - px) + (z - pz) * (z - pz) > PhysReach * PhysReach)
		{
			return false;
		}
		foreach (Sim s in physical.Values)
		{
			TrafficDrive.Line(day.Legs[s.Leg], s.P, out double bx, out double bz, out double bhx, out double bhz);
			if (TrafficDrive.Overlap(x, z, hx, hz, bx, bz, bhx, bhz, 0.5))
			{
				return false;
			}
		}
		if (blocked && TrafficDrive.Overlap(x, z, hx, hz, ox, oz, -hz, hx, 0.5))
		{
			return false;
		}
		TrafficDay.Leg leg = day.Legs[at.Leg];
		physical[c] = new Sim { Car = c, Leg = at.Leg, P = at.P, V = Math.Max(0.0, late.DrawnSpeed(c, at)), Profile = TrafficDay.Profile(leg), St = new TrafficDrive.State { WaitNode = -1 } };
		late.Unheld(c);
		TrafficDay.HoldLaneAt(leg, at.P);
		return true;
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
			bool letsOut = TrafficDrive.LetsOut(along, s.V);
			if ((!letsOut && side < 4.0 && along > -TrafficLate.Gap - before && along < TrafficLate.Gap + behind) || TrafficDrive.Overlap(x, z, hx, hz, bx, bz, bhx, bhz, 0.5) || (farSide && !letsOut && TrafficDrive.AcrossItsWay(along, side)))
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
