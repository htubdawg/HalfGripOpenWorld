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
	/// late, so a late car can come up on a car the day put just behind it: it is held behind what is in its way (<see cref="Yield"/>),
	/// late by that too. **No car is ever hidden** (the owner, 2026-11-26: "The cars cannot get hidden"; until then a car that would
	/// overlap another was hidden until out of sight). **A late car catches up in the background** once neither where it is drawn nor
	/// where its timetable has it can be seen (its tile unloaded or out of reach): its lateness is dropped and it is back on the day,
	/// which keeps the traffic consistent with no car used up.
	///
	/// What "seen" is belongs to the machine (which tiles it has loaded), so <see cref="Settle"/> is told; the records themselves are
	/// the same on every machine that applies the same calls. In a room (stage 4, <see cref="TrafficRoom"/>) the entries are the room's:
	/// made by one machine, settled by the host and applied everywhere (<see cref="Apply"/>), and only the host catches cars up. No Unity
	/// types (trafficview checks it).
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
			/// <summary>The leg it is late on (-1: none).</summary>
			public int Leg;
			/// <summary>Seconds behind.</summary>
			public double D;
			/// <summary>The sim time it was made, and the same moment on the leg's clock (a leg past the day's end runs on w + a day).</summary>
			public double FromW, FromT;
			/// <summary>Wrecked: gone for the rest of the race.</summary>
			public bool Gone;
		}

		private readonly TrafficDay day;
		private readonly StrictMap<int, Entry> ledger = new StrictMap<int, Entry>();
		private readonly List<int> keys = new List<int>();
		/// <summary>The ledger's cars for <see cref="SpaceTaken"/> (its own list: it may be asked while <see cref="Settle"/> walks <see cref="keys"/>).</summary>
		private readonly List<int> spaceKeys = new List<int>();
		private readonly List<int> caught = new List<int>();

		/// <summary>Counts for the log and the overlay: made late, caught up in the background, retired.</summary>
		internal int MadeLate, CaughtUp, Retired;

		/// <summary>Late cars let off a stand the day had them make (<see cref="SkipStand"/>).</summary>
		internal int Unstuck;

		internal TrafficLate(TrafficDay day)
		{
			this.day = day;
		}

		/// <summary>How many cars have an entry now.</summary>
		internal int Count => ledger.Count;

		internal bool Has(int car) => ledger.ContainsKey(car);

		/// <summary>Car <paramref name="car"/>'s entry (none: leg -1).</summary>
		internal bool TryGet(int car, out Entry e)
		{
			bool had = ledger.TryGetValue(car, out e);
			if (!had)
			{
				e = new Entry { Leg = -1 };
			}
			return had;
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

		/// <summary>
		/// A late car standing where the day had it stand (queued, or waiting at a junction) that long ago, with nothing there now to stand
		/// for (<see cref="TrafficDrive.NothingToStandFor"/>): its lateness cut by the rest of that stand, so it goes on now. It stands
		/// still all the while, so its place does not change: no jump. The cars it queued behind in the day are drawn elsewhere now
		/// (on time), so it stood for minutes for no reason the player could see, and the physics cars behind it with it (seed 8: one
		/// stood 557 s behind a car drawn 129 s late). The same for an early car: one back early 45 s from its trip's end was drawn where
		/// the day would have it, standing in a queue the day had there only later, and six physics cars stood behind it (seed 9). Not
		/// while pulling out or in. False if there was nothing to cut; else the new
		/// entry's leg and lateness (a room sends them), and a lateness cut below <see cref="Least"/> puts it back on time.
		/// </summary>
		internal bool SkipStand(int car, double w, out int leg, out double d)
		{
			leg = -1;
			d = 0.0;
			if (!ledger.TryGetValue(car, out Entry e) || e.Gone || e.Leg < 0)
			{
				return false;
			}
			TrafficDay.Leg l = day.Legs[e.Leg];
			double now = e.FromT + Since(e.FromW, w);
			double t = now - e.D;
			if (t - l.Start < l.PullOut + 1.0 || t > l.End - l.PullIn - 1.0)
			{
				return false;
			}
			TrafficDay.Place p0 = day.Driving(e.Leg, t);
			if (p0.Speed >= 0.5)
			{
				return false;
			}
			// The day's last moment of this stand: for a late car, it still stands now at the latest (then it is back on time where it
			// stands); an early one (a physics car back early near its trip's end, drawn where the day will have it) only gets earlier,
			// and from its trip's end it is drawn parked there until the day gets there (<see cref="At"/>).
			double end = t, limit = l.End - l.PullIn - 1.0;
			if (e.D > 0.0)
			{
				limit = Math.Min(now, limit);
			}
			while (end + StandStep <= limit && Math.Abs(day.Driving(e.Leg, end + StandStep).P - p0.P) < 0.3)
			{
				end += StandStep;
			}
			if (end - t < StandStep)
			{
				return false;
			}
			bool had = ledger.ContainsKey(car);
			leg = e.Leg;
			d = now - end;
			Late(car, e.Leg, w, d);
			if (had && ledger.ContainsKey(car))
			{
				// The same lateness made smaller, not a car made late again.
				MadeLate--;
			}
			Unstuck++;
			return true;
		}

		/// <summary>How finely <see cref="SkipStand"/> looks for the end of a stand (seconds).</summary>
		private const double StandStep = 0.5;

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

		/// <summary>
		/// Where car <paramref name="car"/> is drawn at sim time <paramref name="w"/>: the day's place, or for a late car the day's place
		/// that long ago; <paramref name="gone"/> if it is not to be drawn (a wreck). An entry that has run out (its car parked with
		/// time to spare) is dropped here.
		/// </summary>
		internal TrafficDay.Place At(int car, double w, out bool gone)
		{
			gone = false;
			if (!ledger.TryGetValue(car, out Entry e))
			{
				return day.At(car, w);
			}
			if (e.Gone)
			{
				gone = true;
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
				if (e.D < 0.0)
				{
					// Early (a physics car that got to its trip's end ahead of the day): parked there until the day has it there too.
					if (now < leg.End)
					{
						return leg.Vanishes ? new TrafficDay.Place { State = TrafficDay.State.Away, Leg = e.Leg } : new TrafficDay.Place { State = TrafficDay.State.Parked, X = leg.To.X, Z = leg.To.Z, Leg = e.Leg };
					}
					ledger.Remove(car);
					return day.At(car, w);
				}
				// Its late trip is over: parked at its end from leg.End + D. Its stay there is cut short to make the time up, never below
				// the shortest stay; what is left over makes its next trip late.
				double arrive = leg.End + e.D;
				int next = Next(car, e.Leg);
				if (next < 0 || day.Legs[next].Start >= arrive + TrafficDay.MinStay)
				{
					// Made up in full: back on the day.
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
		/// Catching up in the background: every late car neither of whose places (where it is drawn now, and where the day has it) can be
		/// seen is put back on the day. <paramref name="seen"/> says whether a car at a place could be seen (from this machine: its tile is
		/// loaded and it is within reach; on a room's host, by any player), and is given the car so a parked car's space can be found. With
		/// <paramref name="catchUp"/> false (in a room, not the host) late cars are left to the host. Returns the cars caught up (the host
		/// sends them), in order.
		/// </summary>
		internal List<int> Settle(double w, Func<int, TrafficDay.Place, bool> seen, bool catchUp = true)
		{
			caught.Clear();
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
					caught.Add(car);
					CaughtUp++;
				}
			}
			return caught;
		}

		/// <summary>A drawn car near the player (<see cref="Overlaps"/>, and what a drawn car is held behind).</summary>
		internal struct Near
		{
			public int Car;
			public double X, Z, Hx, Hz;
			public bool Moving;
			/// <summary>Its distance from the player.</summary>
			public double Distance;
			/// <summary>Late (in the ledger): an on-time car the day keeps apart from the others, a late one it doesn't.</summary>
			public bool Late;
			/// <summary>On its last few seconds, pulling into its space (a car pulling out of that space doesn't wait for it).</summary>
			public bool PullingIn;
			/// <summary>The speed it is drawn going (0 parked).</summary>
			public double Speed;
		}

		/// <summary>
		/// How many pairs in <paramref name="near"/> (the cars about to be drawn) with at least one late car overlap (two moving cars one
		/// behind the other on a lane closer than <see cref="Gap"/>, or a car in a parked car's space). Nothing is hidden for it any more
		/// (the owner, 2026-11-26: "The cars cannot get hidden"): a drawn car is held behind what is in its way (<see cref="Yield"/>), and
		/// trafficview counts what is left. Pairs of on-time cars are the day's own business (it keeps them apart).
		/// </summary>
		internal static int Overlaps(List<Near> near, Func<int, bool> isLate)
		{
			int count = 0;
			for (int i = 0; i < near.Count; i++)
			{
				for (int j = i + 1; j < near.Count; j++)
				{
					if ((isLate(near[i].Car) || isLate(near[j].Car)) && Overlap(near[i], near[j]))
					{
						count++;
					}
				}
			}
			return count;
		}

		/// <summary>
		/// Whether two drawn cars' bodies overlap, whichever way each faces (each box half a unit smaller than a car, as the physics cars'
		/// check). Until 2026-11-26 two moving cars overlapped only going the same way, and a moving car and a parked one within 6 of each
		/// other: a car driving through a crossing one passed, and one passing a kerb car half a unit off failed.
		/// </summary>
		internal static bool Overlap(in Near a, in Near b)
		{
			return TrafficDrive.Overlap(a.X, a.Z, a.Hx, a.Hz, b.X, b.Z, b.Hx, b.Hz, -0.5);
		}

		private static bool Visible(int car, TrafficDay.Place p, Func<int, TrafficDay.Place, bool> seen)
		{
			if (p.State == TrafficDay.State.Away || (p.State == TrafficDay.State.Driving && p.InTunnel))
			{
				return false;
			}
			return seen(car, p);
		}

		/// <summary>
		/// Sim time gone from <paramref name="from"/> to <paramref name="w"/>, across the day's wrap. An entry is read after it was made (a
		/// room's clocks put that a few seconds either way), and may be read for most of a day: a car early by 1,193 s (the day's own car
		/// stood 15 game hours in a jam on Kingsbridge street, seed 9) was read at its trip's end 1,207 s on, past half a day, which
		/// wrapped round to before the entry was made and drew it driving again. So up to three quarters of a day on, a quarter back.
		/// </summary>
		private static double Since(double from, double w)
		{
			double d = w - from;
			if (d < -Traffic.DaySeconds * 0.25)
			{
				d += Traffic.DaySeconds;
			}
			else if (d > Traffic.DaySeconds * 0.75)
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
		/// Whether drawn car <paramref name="car"/>, driving at <paramref name="at"/>, may be held back in its space (<see cref="HoldBack"/>):
		/// only while it is still in it (it hasn't moved yet). A car already pulling out, or out and waiting on its lane to set off, would jump
		/// back into its space: it waits where it is drawn instead (<see cref="Yield"/>). A car in a space out of sight isn't drawn until it
		/// sets off (<see cref="RoundTheCorner"/>), so holding it there hides nothing.
		/// </summary>
		internal bool CanHoldBack(int car, TrafficDay.Place at)
		{
			return at.State == TrafficDay.State.Driving && at.Leg >= 0 && at.T < 0.5 && !day.Legs[at.Leg].Appears;
		}

		/// <summary>
		/// Whether car <paramref name="car"/>, driving at <paramref name="at"/>, is still in a space out of sight (round the corner, or gone
		/// from the map) before it sets off: not drawn. It was drawn on its lane at its trip's start from the first moment, and a car held
		/// back there went back to its unseen space and vanished, or stood in the lane where others drove (trafficview's ledger, 2026-11-26).
		/// </summary>
		internal bool RoundTheCorner(int car, TrafficDay.Place at)
		{
			if (!CanHoldBack(car, at))
			{
				return false;
			}
			return SpaceUnseen(car, at.Leg);
		}

		/// <summary>
		/// Whether car <paramref name="car"/>, driving at <paramref name="at"/>, has just come out of a space out of sight onto its lane (its
		/// first second on the road): where <see cref="StartBlocked"/> is asked, the first frame it would be drawn.
		/// </summary>
		internal bool Appearing(int car, TrafficDay.Place at)
		{
			return at.State == TrafficDay.State.Driving && at.Leg >= 0 && at.T >= 0.5 && at.T < 1.5 && SpaceUnseen(car, at.Leg);
		}

		/// <summary>Whether car <paramref name="car"/> sets off on leg <paramref name="leg"/> from a space that isn't drawn (out of sight, or gone from the map).</summary>
		private bool SpaceUnseen(int car, int leg)
		{
			if (day.Legs[leg].Appears)
			{
				return false;
			}
			TrafficDay.Place before = Before(car, leg);
			return before.State != TrafficDay.State.Parked || day.Parking.Parked(car, before.Leg).Away;
		}

		/// <summary>
		/// Whether car <paramref name="car"/>, still round the corner at <paramref name="at"/> (<see cref="RoundTheCorner"/>), would appear
		/// on its lane in a drawn car or right in front of a moving one that couldn't stop: one of <paramref name="drawn"/> (last frame's) or
		/// <paramref name="appearing"/> (cars let appear this frame: two held back by the same car were let go together at the same spot),
		/// where it or the other is late (on-time cars the day keeps apart itself). It waits round the corner instead (<see cref="HoldBack"/>):
		/// late cars appeared in the queues standing at their trip's start (trafficview's ledger, 2026-11-26).
		/// </summary>
		internal bool StartBlocked(int car, TrafficDay.Place at, List<Near> drawn, List<Near> appearing)
		{
			bool meLate = Has(car);
			Near me = new Near { Car = car, X = at.X, Z = at.Z, Hx = at.Hx, Hz = at.Hz, Moving = true };
			return Blocks(car, meLate, me, drawn) || Blocks(car, meLate, me, appearing);
		}

		private static bool Blocks(int car, bool meLate, in Near me, List<Near> others)
		{
			foreach (Near o in others)
			{
				if (o.Car == car || (!meLate && !o.Late))
				{
					continue;
				}
				if (Overlap(me, o))
				{
					return true;
				}
				if (o.Moving && o.Speed > 0.5)
				{
					double stop = o.Speed * o.Speed / (2.0 * TrafficDrive.YieldBrake) + TrafficDrive.YieldGap;
					if (TrafficDrive.FreeAhead(o.X, o.Z, o.Hx, o.Hz, me.X, me.Z, me.Hx, me.Hz, TrafficDrive.YieldLook(o.Speed), 0.0) < stop)
					{
						return true;
					}
				}
			}
			return false;
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

		/// <summary>
		/// The speed each car held back here was last drawn at, while it is held or picking up again (this machine's own, never sent): it
		/// pulls away at the day's acceleration, not at once at the day's speed.
		/// </summary>
		private readonly Dictionary<int, double> shown = new Dictionary<int, double>();

		/// <summary>Cars slowed or stopped behind something the day didn't know was there (counted once each time one starts to be held).</summary>
		internal int Yielded;

		/// <summary>
		/// A drawn driving car at <paramref name="at"/> with <paramref name="free"/> units it can still go before it must stand
		/// (<see cref="TrafficDrive.FreeAhead"/>; MaxValue: nothing in its way), over <paramref name="dt"/> seconds: drawn no faster than it
		/// could stop in that (braking at <see cref="TrafficDrive.YieldBrake"/>), and pulling away again no quicker than the day's cars do.
		/// Its timetable goes on without it, so it is made late by what it lost: a car that would have run into a physics car or a late
		/// car waits behind it, as a driver would. The owner (2026-11-26): "The cars cannot get hidden". Call it every frame for every drawn
		/// driving car. True if its lateness changed (<paramref name="d"/>: the new lateness, which a room sends).
		/// </summary>
		internal bool Yield(int car, TrafficDay.Place at, double w, double dt, double free, out double d)
		{
			d = 0.0;
			if (at.State != TrafficDay.State.Driving || at.Leg < 0 || at.InTunnel || dt <= 0.0)
			{
				shown.Remove(car);
				return false;
			}
			double v = Math.Max(0.0, at.Speed);
			// (Never more than it has room for in the step: a long step would carry it past where it must stand.)
			double allowed = free <= 0.0 ? 0.0 : free >= 1e9 ? double.MaxValue : Math.Min(Math.Sqrt(2.0 * TrafficDrive.YieldBrake * free), free / dt);
			bool held = shown.TryGetValue(car, out double last);
			if (held)
			{
				allowed = Math.Min(allowed, last + TrafficDay.Accel * dt);
			}
			if (allowed >= v)
			{
				shown.Remove(car);
				return false;
			}
			if (!held)
			{
				Yielded++;
			}
			shown[car] = allowed;
			// It is drawn going allowed instead of v: it loses the rest of this step's progress on the day.
			// (Never back past its trip's start: it would be drawn in its space again, a jump.)
			double extra = Math.Min(at.T, dt * (1.0 - allowed / Math.Max(v, 1e-3)));
			TrafficDay.Leg leg = day.Legs[at.Leg];
			double drawnAt = leg.Start + at.T - extra;
			// One that came out of a space out of sight is never drawn back into it (it would vanish: RoundTheCorner).
			double floor = at.T >= 0.5 && SpaceUnseen(car, at.Leg) ? leg.Start + 0.5 : leg.Start;
			drawnAt = Math.Max(floor, drawnAt);
			// And never further on than going allowed from where it was a step ago: the day's speed over the step it is moved back to can
			// be more than v, and a car held at the yield's room crept into the car in front (trafficview's ledger, 2026-11-26).
			double was = TrafficDay.DistanceAt(leg, Math.Max(leg.Start, leg.Start + at.T - dt));
			double most = was + allowed * dt;
			if (TrafficDay.DistanceAt(leg, drawnAt) > most + 1e-6)
			{
				drawnAt = Math.Max(floor, Math.Min(drawnAt, TrafficDrive.TimeAt(leg, most)));
			}
			d = TrafficDrive.LegClock(leg, w) - drawnAt;
			bool had = ledger.ContainsKey(car);
			Late(car, at.Leg, w, d, 0.0);
			if (had)
			{
				MadeLate--;
			}
			return true;
		}

		/// <summary>
		/// Whether a late car other than <paramref name="car"/> is still in parking space <paramref name="spot"/> at sim time <paramref name="w"/>
		/// (parked, or pulling out of it): the day gave the space to <paramref name="car"/> once that car had left, and it hasn't. Read from
		/// the ledger, not from the drawn cars: parked cars are drawn nearer the player than moving ones, and a car arriving out of their
		/// reach parked in one still there (trafficview's ledger, 2026-11-26).
		/// </summary>
		internal bool SpaceTaken(int car, TrafficParking.Spot spot, double w)
		{
			if (spot.Away)
			{
				return false;
			}
			// Over a copy of the keys: At drops an entry whose car has caught up. .NET 8 (trafficview) lets a Dictionary lose entries while
			// it is walked; the game's Mono throws, and the throw stopped all the traffic at once (the owner's log, 2026-11-26: a car
			// vanished as the owner hit it).
			spaceKeys.Clear();
			spaceKeys.AddRange(ledger.Keys);
			foreach (int key in spaceKeys)
			{
				if (key == car || !ledger.TryGetValue(key, out Entry e) || e.Gone)
				{
					continue;
				}
				TrafficDay.Place p = At(key, w, out bool gone);
				if (gone || p.Leg < 0)
				{
					continue;
				}
				TrafficParking.Spot there;
				if (p.State == TrafficDay.State.Parked)
				{
					there = day.Parking.Parked(key, p.Leg);
				}
				else if (p.State == TrafficDay.State.Driving && !day.Legs[p.Leg].Appears && p.T < day.Legs[p.Leg].PullOut + 0.5)
				{
					there = day.Parking.Parked(key, LegBefore(key, p.Leg));
				}
				else
				{
					continue;
				}
				if (!there.Away && (there.X - spot.X) * (there.X - spot.X) + (there.Z - spot.Z) * (there.Z - spot.Z) < InSpace * InSpace)
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>The car's leg that ran before <paramref name="leg"/> (-1: none: it set off from home).</summary>
		private int LegBefore(int car, int leg)
		{
			double start = day.Legs[leg].Start;
			int best = -1;
			foreach (int li in day.Cars[car].Legs)
			{
				TrafficDay.Leg l = day.Legs[li];
				if (li != leg && l.Start < start && (best < 0 || l.Start > day.Legs[best].Start))
				{
					best = li;
				}
			}
			return best;
		}

		/// <summary>
		/// Forgets that car <paramref name="car"/> was held back (<see cref="Yield"/>): it became a physics car. Kept, the speed it was held
		/// to would be where it picks up from once it is drawn again, however fast its physics car was going.
		/// </summary>
		internal void Unheld(int car) => shown.Remove(car);

		/// <summary>Whether car <paramref name="car"/> is being held back or picking up again (<see cref="Yield"/>).</summary>
		internal bool Yielding(int car) => shown.ContainsKey(car);

		/// <summary>
		/// The speed car <paramref name="car"/> at <paramref name="at"/> is drawn going: the day's, or while it is held back (<see cref="Yield"/>)
		/// what it is slowed to. What the physics cars' drivers see: a held car going the day's speed was run into from behind.
		/// </summary>
		internal double DrawnSpeed(int car, TrafficDay.Place at) => shown.TryGetValue(car, out double v) ? Math.Min(v, at.Speed) : at.Speed;

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

	/// <summary>
	/// A Dictionary that throws when it is changed (an entry removed, added or set) while it is being walked, as the game's Mono does and
	/// .NET 8 doesn't (it lets an entry go mid-walk): trafficview runs on .NET 8, so a walk that changes it would pass there and stop all
	/// the traffic in the game (the owner's log, 2026-11-26: the ledger walked while <see cref="TrafficLate.At"/> dropped a caught-up car).
	/// </summary>
	internal sealed class StrictMap<TKey, TValue> : IEnumerable<KeyValuePair<TKey, TValue>>
	{
		private readonly Dictionary<TKey, TValue> map = new Dictionary<TKey, TValue>();
		private int version;

		internal int Count => map.Count;

		internal Dictionary<TKey, TValue>.KeyCollection Keys => map.Keys;

		internal bool ContainsKey(TKey key) => map.ContainsKey(key);

		internal bool TryGetValue(TKey key, out TValue value) => map.TryGetValue(key, out value);

		internal TValue this[TKey key]
		{
			get => map[key];
			set
			{
				map[key] = value;
				version++;
			}
		}

		internal bool Remove(TKey key)
		{
			version++;
			return map.Remove(key);
		}

		internal void Clear()
		{
			version++;
			map.Clear();
		}

		public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
		{
			int v = version;
			foreach (KeyValuePair<TKey, TValue> kv in map)
			{
				if (v != version)
				{
					throw new InvalidOperationException("Collection was modified; enumeration operation may not execute.");
				}
				yield return kv;
				if (v != version)
				{
					throw new InvalidOperationException("Collection was modified; enumeration operation may not execute.");
				}
			}
		}

		System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
	}
}
