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
	private const double MovingReach = 900.0, ParkedReach = 450.0, TileSize = 250.0;
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
		ok &= LateRun1(day, w0, "driving with car " + follow, c => follow, 0.0, 0.0);
		ok &= LateRun1(day, w0, "standing at " + bestX.ToString("0") + ", " + bestZ.ToString("0") + " (" + most + " cars driving within 450)", c => -1, bestX, bestZ);
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
		HashSet<int> wasHidden = new HashSet<int>();
		int hideNear = 0, hideMid = 0, hideFar = 0, hideParked = 0;
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
				TrafficDay.Place at = late.At(c, w, out bool hidden);
				if (hidden || !seen(c, at))
				{
					continue;
				}
				(bool ok, double x, double z) p = where(c, at);
				double dx = p.x - px, dz = p.z - pz;
				near.Add(new TrafficLate.Near { Car = c, X = p.x, Z = p.z, Hx = at.Hx, Hz = at.Hz, Moving = at.State == TrafficDay.State.Driving, Distance = Math.Sqrt(dx * dx + dz * dz) });
			}
			watch.Restart();
			int hiddenBefore = late.HiddenCount;
			late.Guard(near, w);
			if (late.HiddenCount > hiddenBefore)
			{
				foreach (TrafficLate.Near a in near)
				{
					if (late.TryGet(a.Car, out TrafficLate.Entry he) && he.Hidden && !wasHidden.Contains(a.Car))
					{
						wasHidden.Add(a.Car);
						hideNear += a.Distance < 300.0 ? 1 : 0;
						hideMid += a.Distance >= 300.0 && a.Distance < 600.0 ? 1 : 0;
						hideFar += a.Distance >= 600.0 ? 1 : 0;
						hideParked += a.Moving ? 0 : 1;
					}
				}
			}
			late.Settle(w, seen);
			guardMs += watch.Elapsed.TotalMilliseconds;
			// What is drawn now: none of it overlapping because one is late, no late car jumping.
			drawnNow.Clear();
			List<TrafficLate.Near> drawn = new List<TrafficLate.Near>();
			foreach (TrafficLate.Near a in near)
			{
				if (late.TryGet(a.Car, out TrafficLate.Entry e) && (e.Hidden || e.Gone))
				{
					continue;
				}
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
		Console.WriteLine("  lateness, " + name + ", 10 minutes from 17:00: made late " + late.MadeLate + " (at most " + mostLate + " at once), hidden for an overlap " + late.HiddenCount
			+ " (" + hideNear + " within 300 of the player (physics cars in stage 3), " + hideMid + " 300 to 600, " + hideFar + " farther; " + hideParked + " parked)" + ", caught up out of sight " + late.CaughtUp + ", still late at the end " + left + "; drawn overlapping because late " + overlaps + ", late cars jumping " + jumps + jumpNote
			+ " (worst step " + worstJump.ToString("0.0") + "); guard and catch-up " + (guardMs / steps).ToString("0.00") + " ms a choice" + (good ? "  ok" : "  FAIL"));
		return good;
	}
}
