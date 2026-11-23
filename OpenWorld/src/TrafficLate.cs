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
	/// the same on every machine that applies the same calls. In a room (stage 4, <see cref="TrafficRoom"/>) the entries are the room's:
	/// made by one machine, settled by the host and applied everywhere (<see cref="Apply"/>), and only the host catches cars up. Hiding is
	/// what one machine draws, kept apart from the entries (<see cref="hidden"/>) and never sent. No Unity types (trafficview checks it).
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
			/// <summary>Not drawn until it is out of sight (this machine's own: filled in by <see cref="TryGet"/>, not kept in the entry).</summary>
			public bool Hidden;
			/// <summary>Wrecked: gone for the rest of the race.</summary>
			public bool Gone;
		}

		private readonly TrafficDay day;
		private readonly Dictionary<int, Entry> ledger = new Dictionary<int, Entry>();
		/// <summary>Cars this machine doesn't draw until neither of their places can be seen (an overlap, a physics car where they are).</summary>
		private readonly HashSet<int> hidden = new HashSet<int>();
		private readonly List<int> keys = new List<int>();
		private readonly List<int> caught = new List<int>();

		/// <summary>Counts for the log and the overlay: made late, caught up in the background, hidden for an overlap, retired.</summary>
		internal int MadeLate, CaughtUp, HiddenCount, Retired;

		internal TrafficLate(TrafficDay day)
		{
			this.day = day;
		}

		/// <summary>How many cars have an entry or are hidden now.</summary>
		internal int Count
		{
			get
			{
				int n = ledger.Count;
				foreach (int car in hidden)
				{
					n += ledger.ContainsKey(car) ? 0 : 1;
				}
				return n;
			}
		}

		internal bool Has(int car) => ledger.ContainsKey(car) || hidden.Contains(car);

		internal bool IsHidden(int car) => hidden.Contains(car);

		/// <summary>Car <paramref name="car"/>'s entry (a hidden car with none: leg -1), with whether it is hidden here.</summary>
		internal bool TryGet(int car, out Entry e)
		{
			bool had = ledger.TryGetValue(car, out e);
			if (!had)
			{
				e = new Entry { Leg = -1 };
			}
			e.Hidden = hidden.Contains(car);
			return had || e.Hidden;
		}

		/// <summary>
		/// A change to car <paramref name="car"/>'s entry from the room (<see cref="TrafficRoom.Op"/>): late (taken as it is, however small:
		/// the sender kept it), retired, or back on time. A retired car stays retired.
		/// </summary>
		internal void Apply(int car, TrafficRoom.Op op)
		{
			switch (op.Kind)
			{
				case TrafficRoom.OpKind.Late:
					Late(car, op.Leg, op.W, op.D, 0.0);
					break;
				case TrafficRoom.OpKind.Retire:
					Retire(car, op.W);
					break;
				case TrafficRoom.OpKind.OnTime:
					if (ledger.TryGetValue(car, out Entry e) && !e.Gone)
					{
						ledger.Remove(car);
					}
					break;
			}
		}

		/// <summary>
		/// Car <paramref name="car"/>, driving leg <paramref name="leg"/>, is <paramref name="d"/> seconds behind its timetable from sim time
		/// <paramref name="w"/> (stage 3: a physics car going back on its timetable). Replaces any entry it had (a car is late once).
		/// Lateness under <paramref name="least"/> is not kept (the car is put back on time). A physics car going back passes a small one:
		/// at speed even half a second is a jump to see. It may be negative: a car a little ahead of its timetable, drawn that far on.
		/// </summary>
		internal void Late(int car, int leg, double w, double d, double least = Least)
		{
			if (ledger.TryGetValue(car, out Entry old) && old.Gone)
			{
				return;
			}
			// A new entry shows the car again (as a physics car going back is: it was where it is drawn).
			hidden.Remove(car);
			if (Math.Abs(d) < least || leg < 0 || (d == 0.0 && least == 0.0))
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

		/// <summary>Car <paramref name="car"/> is not to be drawn here until neither of its places can be seen (it would overlap another car).</summary>
		internal void Hide(int car, double w)
		{
			if ((ledger.TryGetValue(car, out Entry e) && e.Gone) || !hidden.Add(car))
			{
				return;
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
			hidden = this.hidden.Contains(car);
			if (!ledger.TryGetValue(car, out Entry e))
			{
				return day.At(car, w);
			}
			if (e.Gone)
			{
				hidden = true;
				return new TrafficDay.Place { State = TrafficDay.State.Away, Leg = -1 };
			}
			// At most a few legs on (each pass moves the entry to the next leg).
			for (int guard = 0; guard < 8 && e.Leg >= 0; guard++)
			{
				TrafficDay.Leg leg = day.Legs[e.Leg];
				double now = e.FromT + Since(e.FromW, w);
				double t = now - e.D;
				if (t < leg.Start)
				{
					// Held back before it set off (TrafficCars: a physics car where it would pull out): still parked where it was.
					return Before(car, e.Leg);
				}
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
		/// can be seen is put back on the day (a hidden one shown again). <paramref name="seen"/> says whether a car at a place could be seen
		/// (from this machine: its tile is loaded and it is within reach; on a room's host, by any player), and is given the car so a parked
		/// car's space can be found. With <paramref name="catchUp"/> false (in a room, not the host) late cars are left to the host; hidden
		/// ones are always this machine's. Returns the cars caught up (the host sends them), in order.
		/// </summary>
		internal List<int> Settle(double w, Func<int, TrafficDay.Place, bool> seen, bool catchUp = true)
		{
			caught.Clear();
			keys.Clear();
			keys.AddRange(hidden);
			keys.Sort();
			foreach (int car in keys)
			{
				if (!ledger.ContainsKey(car) || !catchUp)
				{
					TrafficDay.Place drawn = At(car, w, out bool _);
					if (!Visible(car, drawn, seen) && !Visible(car, day.At(car, w), seen))
					{
						hidden.Remove(car);
						if (catchUp)
						{
							CaughtUp++;
						}
					}
				}
			}
			if (!catchUp)
			{
				return caught;
			}
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
					hidden.Remove(car);
					caught.Add(car);
					CaughtUp++;
				}
			}
			return caught;
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
				if (!ledger.TryGetValue(a.Car, out Entry ea) || hidden.Contains(a.Car) || ea.Gone || ea.Leg < 0)
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
					if (hidden.Contains(b.Car) || (ledger.TryGetValue(b.Car, out Entry eb) && eb.Gone))
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

		/// <summary>Car <paramref name="car"/> parked before it set off on leg <paramref name="leg"/>: where its last trip left it (at home before its first).</summary>
		private TrafficDay.Place Before(int car, int leg)
		{
			TrafficDay.Leg l = day.Legs[leg];
			if (l.Appears)
			{
				return new TrafficDay.Place { State = TrafficDay.State.Away, Leg = -1 };
			}
			int prev = -1;
			foreach (int li in day.Cars[car].Legs)
			{
				TrafficDay.Leg o = day.Legs[li];
				if (li != leg && o.Start >= 0.0 && o.Start < l.Start && (prev < 0 || o.Start > day.Legs[prev].Start))
				{
					prev = li;
				}
			}
			TrafficDay.Car c = day.Cars[car];
			if (prev < 0)
			{
				return new TrafficDay.Place { State = TrafficDay.State.Parked, X = c.Home.X, Z = c.Home.Z, Leg = -1 };
			}
			TrafficDay.Leg p = day.Legs[prev];
			return new TrafficDay.Place { State = p.Vanishes ? TrafficDay.State.Away : TrafficDay.State.Parked, X = p.To.X, Z = p.To.Z, Leg = prev };
		}

		/// <summary>
		/// Holds car <paramref name="car"/> back in its space before it sets off on leg <paramref name="leg"/> (it would pull out where a physics
		/// car stands): late from sim time <paramref name="w"/> by however long until the leg's start, and <paramref name="more"/>. Returns the
		/// lateness (a room sends it).
		/// </summary>
		internal double HoldBack(int car, int leg, double w, double more)
		{
			TrafficDay.Leg l = day.Legs[leg];
			double t = w;
			if (l.Start - t > Traffic.DaySeconds * 0.5)
			{
				t += Traffic.DaySeconds;
			}
			bool had = ledger.ContainsKey(car);
			double d = t - l.Start + more;
			Late(car, leg, w, d, 0.0);
			if (had)
			{
				// One car held back counts once, however long it is kept waiting.
				MadeLate--;
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
