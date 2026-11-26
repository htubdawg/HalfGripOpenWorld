using System;
using System.Collections.Generic;
using System.Diagnostics;
using HalfGripOpenWorld;

// The lateness ledger (TrafficLate): cars made late as stage 3's physics cars will make them (going back on their timetable as they
// leave the player's reach), drawn as the game draws them round a pretend player with its square of loaded tiles, every 0.4 s as the
// game chooses them. Checks that no late car is ever drawn overlapping another, that a drawn late car never jumps, and that late cars
// catch up once out of sight (the owner's rule, 2026-11-23).
internal static partial class Program
{
	private const double LateStep = 0.4, LateRun = 600.0;
	private const double MovingReach = 1500.0, ParkedReach = 750.0, TileSize = 250.0;
	private const int TileRadius = 5;

	private static bool Lateness(TrafficDay day)
	{
		bool ok = true;
		double w0 = TrafficDay.SimOfHour(17.0);
		// Two pretend players: one driving with the traffic (following the car with the longest drive left at 17:00), one standing where
		// the most cars drive by.
		int follow = -1;
		double longest = 0.0, bestX = 0.0, bestZ = 0.0;
		int most = -1;
		List<(double x, double z)> moving = new List<(double, double)>();
		for (int c = 0; c < day.Cars.Count; c++)
		{
			TrafficDay.Place at = day.At(c, w0);
			if (at.State != TrafficDay.State.Driving || at.InTunnel)
			{
				continue;
			}
			moving.Add((at.X, at.Z));
			double left = day.Legs[at.Leg].End - (day.Legs[at.Leg].Start + at.T);
			if (left > longest && day.Cars[c].Household >= 0)
			{
				longest = left;
				follow = c;
			}
		}
		foreach ((double x, double z) a in moving)
		{
			int n = 0;
			foreach ((double x, double z) b in moving)
			{
				n += (a.x - b.x) * (a.x - b.x) + (a.z - b.z) * (a.z - b.z) < 450.0 * 450.0 ? 1 : 0;
			}
			if (n > most)
			{
				most = n;
				bestX = a.x;
				bestZ = a.z;
			}
		}
		ok &= EarlyCheck(day, w0);
		ok &= LateRun1(day, w0, "driving with car " + follow, c => follow, 0.0, 0.0);
		ok &= LateRun1(day, w0, "standing at " + bestX.ToString("0") + ", " + bestZ.ToString("0") + " (" + most + " cars driving within 450)", c => -1, bestX, bestZ);
		return ok;
	}

	/// <summary>
	/// Early entries (a physics car at its trip's end ahead of the day goes back on it early): drawn where it was and on from there with
	/// no jump, parked at its trip's end until the day has it there, then back on the day (parked, or on its next trip) with no jump.
	/// </summary>
	private static bool EarlyCheck(TrafficDay day, double w0)
	{
		TrafficLate late = new TrafficLate(day);
		int tried = 0, jumps = 0, notParked = 0, back = 0;
		string note = "", backNote = "";
		for (int c = 0; c < day.Cars.Count && tried < 200; c++)
		{
			TrafficDay.Place at = day.At(c, w0);
			if (at.State != TrafficDay.State.Driving || at.InTunnel)
			{
				continue;
			}
			TrafficDay.Leg leg = day.Legs[at.Leg];
			if (leg.Vanishes || leg.End - (leg.Start + at.T) < 30.0)
			{
				continue;
			}
			tried++;
			// As if a physics car got to 40 before its end now: as early as the day is from there.
			double p = Math.Max(at.P, leg.Length - 40.0);
			double d = TrafficDrive.Lateness(leg, p, w0);
			late.Late(c, at.Leg, w0, d, 0.05);
			TrafficDay.Place first = late.At(c, w0, out bool _);
			// (Along its path: the day's place there is drawn where the day draws it, which the physics car's line follows.)
			if (first.State != TrafficDay.State.Driving || first.Leg != at.Leg || Math.Abs(first.P - p) > 1.0)
			{
				jumps++;
				note = note.Length == 0 ? " (first: car " + c + " drawn " + (first.P - p).ToString("0.0") + " on)" : note;
			}
			// Between its early arrival and the day's: parked at its end.
			// (Drawn on |d| ahead of the day, it gets there at leg.End + d.)
			TrafficDay.Place m = late.At(c, leg.End + 0.5 * d, out bool _);
			if (d < -2.0 && m.State != TrafficDay.State.Parked)
			{
				notParked++;
			}
			// After the day's arrival: back on the day.
			double after = leg.End + 1.0;
			TrafficDay.Place a = late.At(c, after, out bool _), dd = day.At(c, after);
			if (a.State == dd.State && Math.Abs(a.X - dd.X) < 0.01 && Math.Abs(a.Z - dd.Z) < 0.01)
			{
				back++;
			}
			else if (backNote.Length == 0)
			{
				backNote = " (first not: car " + c + " leg " + at.Leg + " d " + d.ToString("0.00") + ", drawn " + a.State + " leg " + a.Leg + " at " + a.X.ToString("0.0") + ", " + a.Z.ToString("0.0") + ", the day " + dd.State + " leg " + dd.Leg + " at " + dd.X.ToString("0.0") + ", " + dd.Z.ToString("0.0") + "; leg " + leg.Start.ToString("0") + " to " + leg.End.ToString("0") + " length " + leg.Length.ToString("0") + ", now at " + at.P.ToString("0") + " T " + at.T.ToString("0") + ")";
			}
		}
		bool ok = jumps == 0 && notParked == 0 && back == tried;
		Console.WriteLine("  early entries (a physics car back early at its trip's end): " + tried + " tried, " + jumps + " jumps" + note + ", " + notParked + " not parked at its end before the day gets there, " + back + " back on the day after" + backNote + (ok ? "  ok" : "  FAIL"));
		return ok;
	}

	private static bool LateRun1(TrafficDay day, double w0, string name, Func<int, int> followOf, double sx, double sz)
	{
		TrafficParking parking = day.Parking;
		TrafficLate late = new TrafficLate(day);
		Rng rng = new Rng(0x1A7E5UL ^ (ulong)day.Seed);
		int follow = followOf(0);
		double px = sx, pz = sz;
		Func<int, TrafficDay.Place, (bool ok, double x, double z)> where = (c, at) =>
		{
			if (at.State == TrafficDay.State.Driving)
			{
				// Where the game draws it: on its lane, or pulling out of its space and into one.
				parking.Drawn(c, at, out double dx, out double dz, out double _, out double _);
				return (!at.InTunnel, dx, dz);
			}
			if (at.State == TrafficDay.State.Parked)
			{
				TrafficParking.Spot spot = parking.Parked(c, at.Leg);
				return (!spot.Away, spot.X, spot.Z);
			}
			return (false, 0.0, 0.0);
		};
		Func<int, TrafficDay.Place, bool> seen = (c, at) =>
		{
			(bool ok, double x, double z) p = where(c, at);
			if (!p.ok)
			{
				return false;
			}
			double reach = at.State == TrafficDay.State.Driving ? MovingReach : ParkedReach;
			double dx = p.x - px, dz = p.z - pz;
			if (dx * dx + dz * dz > reach * reach)
			{
				return false;
			}
			return Math.Abs(Math.Floor(p.x / TileSize) - Math.Floor(px / TileSize)) <= TileRadius && Math.Abs(Math.Floor(p.z / TileSize) - Math.Floor(pz / TileSize)) <= TileRadius;
		};
		List<TrafficLate.Near> near = new List<TrafficLate.Near>();
		Dictionary<int, (double x, double z)> lastDrawn = new Dictionary<int, (double, double)>(), drawnNow = new Dictionary<int, (double, double)>();
		int held = 0, skippedInto = 0;
		int left = 0, overlaps = 0, jumps = 0, steps = 0;
		double worstJump = 0.0, guardMs = 0.0, mostLate = 0;
		string jumpNote = "";
		Stopwatch watch = new Stopwatch();
		double nextLate = 0.0;
		for (double t = 0.0; t < LateRun; t += LateStep, steps++)
		{
			double w = w0 + t;
			if (w >= Traffic.DaySeconds)
			{
				w -= Traffic.DaySeconds;
			}
			if (follow >= 0)
			{
				TrafficDay.Place me = day.At(follow, w);
				if (me.State != TrafficDay.State.Away)
				{
					(bool ok, double x, double z) p = where(follow, me);
					if (p.ok || me.State == TrafficDay.State.Driving)
					{
						px = me.State == TrafficDay.State.Driving ? me.X : p.x;
						pz = me.State == TrafficDay.State.Driving ? me.Z : p.z;
					}
				}
			}
			// Stage 3's physics cars going back on their timetable: at the start, every driving car between 300 and 900 of the player; then
			// every 10 s, those leaving the bubble (380 to 420 out), each late by up to two minutes.
			if (t >= nextLate)
			{
				double inner = t == 0.0 ? 300.0 : 380.0, outer = t == 0.0 ? 900.0 : 420.0;
				// Where every car is drawn now: a physics car going back is where it was driving, never in another car (TrafficDrive keeps it
				// out, and the drawn cars are held behind it), so one whose late place is in another car isn't made late here. (Until
				// 2026-11-26 the overlap was hidden; nothing is now, and a car dropped into another stayed in it.)
				List<TrafficLate.Near> around = new List<TrafficLate.Near>();
				for (int c = 0; c < day.Cars.Count; c++)
				{
					TrafficDay.Place ap = late.At(c, w, out bool apGone);
					if (apGone || ap.State == TrafficDay.State.Away || (ap.State == TrafficDay.State.Driving && ap.InTunnel))
					{
						continue;
					}
					(bool ok, double x, double z) q = where(c, ap);
					double ax = q.x - px, az = q.z - pz;
					if (!q.ok || ax * ax + az * az > (outer + 100.0) * (outer + 100.0))
					{
						continue;
					}
					double ahx = ap.Hx, ahz = ap.Hz;
					if (ap.State == TrafficDay.State.Driving)
					{
						parking.Drawn(c, ap, out double _, out double _, out ahx, out ahz);
					}
					around.Add(new TrafficLate.Near { Car = c, X = q.x, Z = q.z, Hx = ahx, Hz = ahz, Moving = ap.State == TrafficDay.State.Driving });
				}
				for (int c = 0; c < day.Cars.Count; c++)
				{
					if (c == follow || late.Has(c))
					{
						continue;
					}
					TrafficDay.Place at = day.At(c, w);
					if (at.State != TrafficDay.State.Driving || at.InTunnel)
					{
						continue;
					}
					double dx = at.X - px, dz = at.Z - pz, d = Math.Sqrt(dx * dx + dz * dz);
					if (d >= inner && d < outer)
					{
						late.Late(c, at.Leg, w, rng.Range(2.0, 120.0));
						TrafficDay.Place lp = late.At(c, w, out bool _);
						(bool ok, double x, double z) q = where(c, lp);
						if (q.ok && lp.State == TrafficDay.State.Driving)
						{
							parking.Drawn(c, lp, out double _, out double _, out double lhx, out double lhz);
							TrafficLate.Near me = new TrafficLate.Near { Car = c, X = q.x, Z = q.z, Hx = lhx, Hz = lhz, Moving = true };
							bool into = false;
							foreach (TrafficLate.Near o in around)
							{
								if (o.Car != c && TrafficLate.Overlap(me, o))
								{
									into = true;
									break;
								}
							}
							if (into)
							{
								late.Late(c, at.Leg, w, 0.0);
								skippedInto++;
							}
							else
							{
								// Where it is drawn now, for the cars made late after it.
								around.RemoveAll(o => o.Car == c);
								around.Add(me);
							}
						}
					}
				}
				nextLate = t + 10.0;
			}
			mostLate = Math.Max(mostLate, late.Count);
			// The cars the game would draw, as TrafficCars.Choose finds them.
			near.Clear();
			for (int c = 0; c < day.Cars.Count; c++)
			{
				if (c == follow)
				{
					continue;
				}
				TrafficDay.Car car = day.Cars[c];
				double hx0 = car.Home.X - px, hz0 = car.Home.Z - pz;
				if (car.Legs.Count == 0 && hx0 * hx0 + hz0 * hz0 > (ParkedReach + 60.0) * (ParkedReach + 60.0))
				{
					continue;
				}
				TrafficDay.Place at = late.At(c, w, out bool gone);
				if (gone || !seen(c, at))
				{
					continue;
				}
				(bool ok, double x, double z) p = where(c, at);
				double dx = p.x - px, dz = p.z - pz;
				double hx = at.Hx, hz = at.Hz;
				if (at.State == TrafficDay.State.Driving)
				{
					parking.Drawn(c, at, out double _, out double _, out hx, out hz);
				}
				bool pin = at.State == TrafficDay.State.Driving && !day.Legs[at.Leg].Vanishes && day.Legs[at.Leg].End - day.Legs[at.Leg].Start - at.T < day.Legs[at.Leg].PullIn + 2.0;
				near.Add(new TrafficLate.Near { Car = c, X = p.x, Z = p.z, Hx = hx, Hz = hz, Moving = at.State == TrafficDay.State.Driving, Distance = Math.Sqrt(dx * dx + dz * dz), Late = late.Has(c), PullingIn = pin, Speed = at.State == TrafficDay.State.Driving ? late.DrawnSpeed(c, at) : 0.0 });
			}
			watch.Restart();
			// A late car (or one coming up on a late car) with another in its way is held behind it, as TrafficPhysics.YieldDrawn does
			// every frame: never hidden.
			for (int i = 0; i < near.Count; i++)
			{
				TrafficLate.Near a = near[i];
				if (!a.Moving)
				{
					continue;
				}
				TrafficDay.Place at = late.At(a.Car, w, out bool _);
				if (at.State != TrafficDay.State.Driving)
				{
					continue;
				}
				TrafficDay.Leg leg = day.Legs[at.Leg];
				bool pullingIn = !leg.Vanishes && leg.End - leg.Start - at.T < leg.PullIn + 2.0;
				bool pullingOut = !leg.Appears && at.T < leg.PullOut + 1.0;
				if (!leg.Appears && at.T < 0.5)
				{
					bool keptIn = false;
					foreach (TrafficLate.Near o in near)
					{
						if (o.Moving && o.Car != a.Car && !o.PullingIn && (a.Late || o.Late) && TrafficDrive.PullOutBlocked(at.X, at.Z, at.Hx, at.Hz, o.X, o.Z, o.Hx, o.Hz))
						{
							late.HoldBack(a.Car, at.Leg, w, 1.0);
							keptIn = true;
							break;
						}
					}
					if (keptIn)
					{
						TrafficDay.Place inSpace = late.At(a.Car, w, out bool _);
						(bool ok, double x, double z) q0 = where(a.Car, inSpace);
						a.X = q0.x;
						a.Z = q0.z;
						a.Moving = inSpace.State == TrafficDay.State.Driving;
						a.Late = true;
						near[i] = a;
						continue;
					}
				}
				double look = TrafficDrive.YieldLook(Math.Max(0.0, at.Speed));
				double free = double.MaxValue;
				TrafficParking.Spot mine = pullingIn ? parking.Parked(a.Car, at.Leg) : default;
				foreach (TrafficLate.Near o in near)
				{
					if (pullingIn && o.Car != a.Car && (a.Late || o.Late) && !mine.Away && (o.X - mine.X) * (o.X - mine.X) + (o.Z - mine.Z) * (o.Z - mine.Z) < TrafficLate.InSpace * TrafficLate.InSpace)
					{
						// Its space still taken: it waits on its lane by it (YieldDrawn).
						free = 0.0;
						continue;
					}
					if (!(pullingOut && o.PullingIn) && o.Car != a.Car && (a.Late || o.Late) && (o.Moving || pullingIn))
					{
						double f1 = TrafficDrive.FreeAhead(a.X, a.Z, a.Hx, a.Hz, o.X, o.Z, o.Hx, o.Hz, look, o.Speed);
						free = Math.Min(free, f1);
					}
				}
				bool wasHeld = late.Yielding(a.Car);
				if (late.Yield(a.Car, at, w, LateStep, free, out double _))
				{
					held += wasHeld ? 0 : 1;
					TrafficDay.Place now = late.At(a.Car, w, out bool _);
					(bool ok, double x, double z) q = where(a.Car, now);
					a.X = q.x;
					a.Z = q.z;
					a.Late = true;
					near[i] = a;
				}
			}
			late.Settle(w, seen);
			guardMs += watch.Elapsed.TotalMilliseconds;
			// What is drawn now: none of it overlapping because one is late, no late car jumping.
			drawnNow.Clear();
			List<TrafficLate.Near> drawn = new List<TrafficLate.Near>();
			foreach (TrafficLate.Near a in near)
			{
				drawn.Add(a);
				drawnNow[a.Car] = (a.X, a.Z);
			}
			for (int i = 0; i < drawn.Count; i++)
			{
				bool lateI = late.TryGet(drawn[i].Car, out TrafficLate.Entry ei) && ei.Leg >= 0;
				for (int j = i + 1; j < drawn.Count; j++)
				{
					bool lateJ = late.TryGet(drawn[j].Car, out TrafficLate.Entry ej) && ej.Leg >= 0;
					if ((lateI || lateJ) && TrafficLate.Overlap(drawn[i], drawn[j]))
					{
						overlaps++;
					}
				}
				// Not on the step it was made late: the test puts it back on the road at once, where stage 3 measures the lateness from where its
				// physics car is.
				if (lateI && ei.FromW != w && lastDrawn.TryGetValue(drawn[i].Car, out (double x, double z) was))
				{
					double jx = drawn[i].X - was.x, jz = drawn[i].Z - was.z, jump = Math.Sqrt(jx * jx + jz * jz);
					// A car moves at most about 0.4 s at 40 units/s; between its space and its lane a little more.
					if (jump > 16.0 + 30.0)
					{
						jumps++;
						if (jumpNote.Length == 0)
						{
							jumpNote = " (first: car " + drawn[i].Car + " " + jump.ToString("0") + " units at " + w.ToString("0.0") + ")";
						}
					}
					worstJump = Math.Max(worstJump, jump);
				}
			}
			(lastDrawn, drawnNow) = (drawnNow, lastDrawn);
		}
		left = late.Count;
		bool good = overlaps == 0 && jumps == 0;
		Console.WriteLine("  lateness, " + name + ", 10 minutes from 17:00: made late " + late.MadeLate + " (at most " + mostLate + " at once), held behind another car " + held + " (none hidden; " + skippedInto + " not made late into another car), caught up out of sight " + late.CaughtUp + ", still late at the end " + left + "; drawn overlapping because late " + overlaps + ", late cars jumping " + jumps + jumpNote
			+ " (worst step " + worstJump.ToString("0.0") + "); guard and catch-up " + (guardMs / steps).ToString("0.00") + " ms a choice" + (good ? "  ok" : "  FAIL"));
		return good;
	}
}
