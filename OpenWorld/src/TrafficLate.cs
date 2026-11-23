using System;
using System.Collections.Generic;

namespace HalfGripOpenWorld
{
	/// <summary>
	/// The lateness ledger (stage 3 of the traffic plan): which cars of the race's day are running behind their timetable, and which are
	/// gone. A car held up near the player (stage 3's physics cars: queued behind a car the player stopped, bumped, waiting at a junction
	/// longer than the day had it) goes back on its timetable when it leaves the player's reach, late by what held it up: "car c is D
	/// seconds late from w". A wrecked car is "gone from w". Small records, so a room (stage 4) only has to send them.
	///
	/// A late car is drawn where the day had it D seconds ago, until it next parks: there its stay is cut short (not below the day's
	/// <see cref="TrafficDay.MinStay"/>) and whatever is left over makes its next trip late. The day was built without knowing it was
	/// late, so a late car can come up on a car the day put just behind it. The owner's rule (2026-11-23): a late car that would overlap
	/// other traffic is hidden (the farther of the two from the player, so the one in front of them stays), and **a late car catches up
	/// in the background** once neither where it is drawn nor where its timetable has it can be seen (its tile unloaded or out of reach):
	/// its lateness is dropped and it is back on the day, which keeps the traffic consistent with no car used up. A hidden car comes back
	/// the same way, so it never pops back in where the player is looking.
	///
	/// What "seen" is belongs to the machine (which tiles it has loaded), so <see cref="Settle"/> is told; the records themselves are
	/// the same on every machine that applies the same calls. No Unity types (trafficview checks it).
	/// </summary>
	internal sealed class TrafficLate
	{
		/// <summary>Two moving cars on one lane closer than this (centre to centre, units: a car and a little) would be drawn overlapping.</summary>
		internal const double Gap = 18.0;
		/// <summary>A car whose middle is this close to a parked car's is in it (neighbouring spaces are 9 apart side by side at least).</summary>
		internal const double InSpace = 6.0;
		/// <summary>Lateness under this (seconds) is not worth an entry: the car is put back on time.</summary>
		internal const double Least = 1.5;

		/// <summary>One car's entry.</summary>
		internal struct Entry
		{
			/// <summary>The leg it is late on (-1: on time, only hidden).</summary>
			public int Leg;
			/// <summary>Seconds behind.</summary>
			public double D;
			/// <summary>The sim time it was made, and the same moment on the leg's clock (a leg past the day's end runs on w + a day).</summary>
			public double FromW, FromT;
			/// <summary>Not drawn until it catches up.</summary>
			public bool Hidden;
			/// <summary>Wrecked: gone for the rest of the race.</summary>
			public bool Gone;
		}

		private readonly TrafficDay day;
		private readonly Dictionary<int, Entry> ledger = new Dictionary<int, Entry>();
		private readonly List<int> keys = new List<int>();

		/// <summary>Counts for the log and the overlay: made late, caught up in the background, hidden for an overlap, retired.</summary>
		internal int MadeLate, CaughtUp, HiddenCount, Retired;

		internal TrafficLate(TrafficDay day)
		{
			this.day = day;
		}

		/// <summary>How many cars have an entry now.</summary>
		internal int Count => ledger.Count;

		internal bool Has(int car) => ledger.ContainsKey(car);

		internal bool TryGet(int car, out Entry e) => ledger.TryGetValue(car, out e);

		/// <summary>
		/// Car <paramref name="car"/>, driving leg <paramref name="leg"/>, is <paramref name="d"/> seconds behind its timetable from sim time
		/// <paramref name="w"/> (stage 3: a physics car going back on its timetable). Replaces any entry it had (a car is late once).
		/// </summary>
		internal void Late(int car, int leg, double w, double d)
		{
			if (ledger.TryGetValue(car, out Entry old) && old.Gone)
			{
				return;
			}
			if (d < Least || leg < 0)
			{
				ledger.Remove(car);
				return;
			}
			TrafficDay.Leg l = day.Legs[leg];
			// The leg's clock: w, or w + a day for a leg that ran past the day's end.
			double t = w;
			if (t < l.Start - 1.0 && t + Traffic.DaySeconds <= l.End + 1.0)
			{
				t += Traffic.DaySeconds;
			}
			ledger[car] = new Entry { Leg = leg, D = d, FromW = w, FromT = t };
			MadeLate++;
		}

		/// <summary>Car <paramref name="car"/> is wrecked: away for the rest of the race.</summary>
		internal void Retire(int car, double w)
		{
			if (ledger.TryGetValue(car, out Entry e) && e.Gone)
			{
				return;
			}
			ledger[car] = new Entry { Leg = -1, FromW = w, Gone = true };
			Retired++;
		}

		/// <summary>Car <paramref name="car"/> is not to be drawn until it catches up (it would overlap another car).</summary>
		internal void Hide(int car, double w)
		{
			if (ledger.TryGetValue(car, out Entry e))
			{
				if (e.Gone || e.Hidden)
				{
					return;
				}
				e.Hidden = true;
				ledger[car] = e;
			}
			else
			{
				ledger[car] = new Entry { Leg = -1, FromW = w, Hidden = true };
			}
			HiddenCount++;
		}

		/// <summary>
		/// Where car <paramref name="car"/> is drawn at sim time <paramref name="w"/>: the day's place, or for a late car the day's place
		/// that long ago; <paramref name="hidden"/> if it is not to be drawn. An entry that has run out (its car parked with time to spare)
		/// is dropped here.
		/// </summary>
		internal TrafficDay.Place At(int car, double w, out bool hidden)
		{
			hidden = false;
			if (!ledger.TryGetValue(car, out Entry e))
			{
				return day.At(car, w);
			}
			if (e.Gone)
			{
				hidden = true;
				return new TrafficDay.Place { State = TrafficDay.State.Away, Leg = -1 };
			}
			hidden = e.Hidden;
			// At most a few legs on (each pass moves the entry to the next leg).
			for (int guard = 0; guard < 8 && e.Leg >= 0; guard++)
			{
				TrafficDay.Leg leg = day.Legs[e.Leg];
				double now = e.FromT + Since(e.FromW, w);
				double t = now - e.D;
				if (t < leg.End)
				{
					return day.Driving(e.Leg, t);
				}
				// Its late trip is over: parked at its end from leg.End + D. Its stay there is cut short to make the time up, never below
				// the shortest stay; what is left over makes its next trip late.
				double arrive = leg.End + e.D;
				int next = Next(car, e.Leg);
				if (next < 0 || day.Legs[next].Start >= arrive + TrafficDay.MinStay)
				{
					// Made up in full: back on the day (still hidden if it was, until it is out of sight).
					if (e.Hidden)
					{
						e.Leg = -1;
						e.D = 0.0;
						ledger[car] = e;
						break;
					}
					ledger.Remove(car);
					return day.At(car, w);
				}
				if (now < arrive + TrafficDay.MinStay)
				{
					if (leg.Vanishes)
					{
						return new TrafficDay.Place { State = TrafficDay.State.Away, Leg = e.Leg };
					}
					return new TrafficDay.Place { State = TrafficDay.State.Parked, X = leg.To.X, Z = leg.To.Z, Leg = e.Leg };
				}
				e.D = arrive + TrafficDay.MinStay - day.Legs[next].Start;
				e.Leg = next;
				ledger[car] = e;
			}
			return day.At(car, w);
		}

		/// <summary>
		/// Catching up in the background: every late or hidden car neither of whose places (where it is drawn now, and where the day has it)
		/// can be seen is put back on the day. <paramref name="seen"/> says whether a car at a place could be seen from this machine (its
		/// tile is loaded and it is within reach), and is given the car so a parked car's space can be found.
		/// </summary>
		internal void Settle(double w, Func<int, TrafficDay.Place, bool> seen)
		{
			keys.Clear();
			keys.AddRange(ledger.Keys);
			keys.Sort();
			foreach (int car in keys)
			{
				if (!ledger.TryGetValue(car, out Entry e) || e.Gone)
				{
					continue;
				}
				TrafficDay.Place drawn = At(car, w, out bool _);
				if (!ledger.ContainsKey(car))
				{
					continue;
				}
				TrafficDay.Place onTime = day.At(car, w);
				if (!Visible(car, drawn, seen) && !Visible(car, onTime, seen))
				{
					ledger.Remove(car);
					CaughtUp++;
				}
			}
		}

		/// <summary>A drawn car near the player, for <see cref="Guard"/>.</summary>
		internal struct Near
		{
			public int Car;
			public double X, Z, Hx, Hz;
			public bool Moving;
			/// <summary>Its distance from the player (the farther of two is hidden).</summary>
			public double Distance;
		}

		private readonly List<int> toHide = new List<int>();

		/// <summary>
		/// Hides cars in danger of overlapping because one is late: for every pair in <paramref name="near"/> (the cars about to be drawn)
		/// with at least one late car that would overlap (two moving cars one behind the other on a lane closer than <see cref="Gap"/>, or a
		/// car in a parked car's space), the farther from the player is hidden until it catches up. Pairs of on-time cars are the day's own
		/// business (it keeps them apart). Returns how many were hidden.
		/// </summary>
		internal int Guard(List<Near> near, double w)
		{
			toHide.Clear();
			int n = near.Count;
			for (int i = 0; i < n; i++)
			{
				Near a = near[i];
				if (!ledger.TryGetValue(a.Car, out Entry ea) || ea.Hidden || ea.Gone || ea.Leg < 0)
				{
					continue;
				}
				for (int j = 0; j < n; j++)
				{
					if (j == i)
					{
						continue;
					}
					Near b = near[j];
					if (ledger.TryGetValue(b.Car, out Entry eb) && (eb.Hidden || eb.Gone))
					{
						continue;
					}
					if (!Overlap(a, b))
					{
						continue;
					}
					int hide = a.Distance > b.Distance || (a.Distance == b.Distance && a.Car > b.Car) ? a.Car : b.Car;
					if (!toHide.Contains(hide))
					{
						toHide.Add(hide);
					}
				}
			}
			toHide.Sort();
			foreach (int car in toHide)
			{
				Hide(car, w);
			}
			return toHide.Count;
		}

		/// <summary>Whether two drawn cars would overlap: on one lane too close, or one in the other's parking space.</summary>
		internal static bool Overlap(in Near a, in Near b)
		{
			double dx = b.X - a.X, dz = b.Z - a.Z;
			double d2 = dx * dx + dz * dz;
			if (a.Moving && b.Moving)
			{
				// Same way (not a car in the other lane), close along it and not beside it.
				if (a.Hx * b.Hx + a.Hz * b.Hz < 0.5 || d2 >= Gap * Gap)
				{
					return false;
				}
				// Beside by either car's heading (the same answer whichever is a and which b).
				double side = Math.Min(Math.Abs(dx * a.Hz - dz * a.Hx), Math.Abs(dx * b.Hz - dz * b.Hx));
				return side < 3.0;
			}
			return d2 < InSpace * InSpace;
		}

		private static bool Visible(int car, TrafficDay.Place p, Func<int, TrafficDay.Place, bool> seen)
		{
			if (p.State == TrafficDay.State.Away || (p.State == TrafficDay.State.Driving && p.InTunnel))
			{
				return false;
			}
			return seen(car, p);
		}

		/// <summary>Sim time gone from <paramref name="from"/> to <paramref name="w"/>, across the day's wrap (lateness lasts far less than a day).</summary>
		private static double Since(double from, double w)
		{
			double d = w - from;
			if (d < -Traffic.DaySeconds * 0.5)
			{
				d += Traffic.DaySeconds;
			}
			else if (d > Traffic.DaySeconds * 0.5)
			{
				d -= Traffic.DaySeconds;
			}
			return d;
		}

		/// <summary>The car's next leg that ran after <paramref name="leg"/> (-1: none).</summary>
		private int Next(int car, int leg)
		{
			double start = day.Legs[leg].Start;
			int best = -1;
			foreach (int li in day.Cars[car].Legs)
			{
				TrafficDay.Leg l = day.Legs[li];
				if (li != leg && l.Start > start && (best < 0 || l.Start < day.Legs[best].Start))
				{
					best = li;
				}
			}
			return best;
		}
	}
}
