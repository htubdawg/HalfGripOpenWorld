using System;
using System.Collections.Generic;

namespace HalfGripOpenWorld
{
	/// <summary>
	/// The traffic rework's mesoscopic simulation (phase 3, <c>docs/TRAFFIC_REWORK.md</c> §4): every car in the world moved live along
	/// the lane graph (<see cref="Lanes"/>) all race long, from its plans (<see cref="Schedules"/>). Each lane is a queue: a car reaches
	/// its end no sooner than its free-flow time, leaves it no sooner than a headway behind the car before (the saturation flow), and only
	/// onto a lane with room (storage, so queues spill back up the roads) through a junction that admits it: the light green for its
	/// move (<see cref="TrafficSignals"/>) and clear of the conflict areas it shares with the cars already in the junction and the ones it
	/// gives way to that are coming (<see cref="Conflicts"/>, the same areas and rules the micro level will drive by, which is what keeps a
	/// journey the same time watched or not). Routes are the quickest on the lane graph at the live times (through the wrap's tunnels,
	/// which the traffic's map can't route), worked out as a car sets off and again if it is held up a long time. Parking has capacity:
	/// a car finding its lot full goes on to the nearest other place for the same purpose with room.
	///
	/// Nothing is recorded ahead: a car held up is late, and its next stops shift (<see cref="Schedules.Leave"/>). It starts at any hour
	/// from where the plans put every car (<see cref="Schedules.At"/>). It need not be deterministic (the room's host runs it and sends
	/// what happens), but it is: everything in index order, no randomness. Time is day seconds (<see cref="Schedules"/>), the lights' clock
	/// the race's (<see cref="RaceStart"/> subtracted). No Unity types: <c>tools/trafficsim</c> runs it headless over the whole day.
	/// </summary>
	internal sealed class Meso
	{
		// ---- the model's figures ---------------------------------------------------------------------------------------------

		/// <summary>The simulation's step (seconds). A lane lets a car go at most once a headway, so a quarter of a second loses nothing.</summary>
		internal const double Step = 0.25;
		/// <summary>
		/// The least time between two cars leaving a lane over its stop line (seconds): 1,800 cars an hour, the Highway Capacity Manual's
		/// base saturation flow for a lane at a light (HCM 7th ed., 1,900 less the usual heavy-vehicle and grade adjustments).
		/// </summary>
		internal const double Headway = 2.0;
		/// <summary>The room a car takes standing in a queue (units): its length and the gap a driver leaves behind the car ahead (about 0.75 m).</summary>
		internal const double Jam = Traffic.CarLength + 2.5;
		/// <summary>How close behind or before a car already in a conflict area another may pass through it (seconds).</summary>
		internal const double Margin = 1.0;
		/// <summary>A car moving at the stop line when its light turns amber goes on if it reached it this recently (it can't stop); one standing doesn't start.</summary>
		internal const double AmberGo = 2.0 * Step;
		/// <summary>A car held at a lane's end this long by anything but a red light looks for another way.</summary>
		internal const double Reroute = 45.0;
		/// <summary>A car held at a lane's end this long by anything but a red light is stuck: trafficsim reports it as a deadlock.</summary>
		internal const double Stuck = 300.0;
		/// <summary>The most often a car changes its route for a queue.</summary>
		private const int MaxReroutes = 4;
		/// <summary>How quickly a lane's measured delay follows each car leaving it.</summary>
		private const double Smooth = 0.2;

		// ---- the cars --------------------------------------------------------------------------------------------------------

		internal enum Mode
		{
			/// <summary>Parked at stop <see cref="Vehicle.Stop"/> of its plans.</summary>
			Parked,
			/// <summary>Beyond the map at an Away stop: off the roads until it comes back in by the road out it left by.</summary>
			Away,
			/// <summary>On the roads, in lane <see cref="Vehicle.Lane"/>'s queue, heading for stop <see cref="Vehicle.Stop"/>.</summary>
			Driving,
		}

		/// <summary>A car's live state.</summary>
		internal sealed class Vehicle
		{
			/// <summary>Its index in the plans' <see cref="Schedules.Cars"/> (and so its identity all day).</summary>
			public int Car;
			public Mode Mode;
			public bool Through;
			/// <summary>Parked: the stop it is at. Driving: the stop it is going to.</summary>
			public int Stop;
			/// <summary>Where it parked (a full lot sends it on, so not always its stop's) and the place it parked at (-1: home, a kerb).</summary>
			public Traffic.Anchor ParkedAt;
			public int Place = -1;
			/// <summary>Parked: when it came and when it leaves.</summary>
			public double Arrived, LeaveAt;
			/// <summary>The lane whose queue it is in (-1 parked), and the connector it last came through to it (-1: pulled out onto it).</summary>
			public int Lane = -1, Conn = -1;
			/// <summary>When it went onto that connector and when it came onto the lane after it.</summary>
			public double ConnAt, Entered;
			/// <summary>Where along the lane it came on (0 off a connector; part way pulling out) and when its front reaches the lane's end, or its stop, on a free road.</summary>
			public double From, Ready;
			/// <summary>The connectors still to take (from <see cref="Next"/>); a through car's is one ahead at a time.</summary>
			public readonly List<int> Path = new List<int>();
			public int Next;
			/// <summary>Its stop is on this lane, <see cref="DestAlong"/> along it: it leaves the queue there and doesn't wait at the lane's end.</summary>
			public bool Ends;
			public double DestAlong;
			/// <summary>Where it is going (the stop's, or the lot it was sent on to).</summary>
			public Traffic.Anchor Target;
			public int TargetPlace = -1;
			/// <summary>When it came to the head of its lane ready to go and was held (-1: not held); whether by a red light.</summary>
			public double HeldSince = -1.0;
			public bool HeldByLight;
			public int Reroutes;
			/// <summary>This trip: when it set off and its time on free roads; the places it found full.</summary>
			public double TripStart, TripFree;
			public List<int> Full;
		}

		/// <summary>A lane's queue: its cars in the order they reach its end.</summary>
		internal sealed class Queue
		{
			public readonly List<int> Cars = new List<int>();
			/// <summary>How many cars it holds standing (at least 1).</summary>
			public int Storage;
			/// <summary>When the last car left it over its end.</summary>
			public double LastExit = double.MinValue;
			/// <summary>Seconds lost on it, smoothed over the cars leaving it: what routes are worked out on.</summary>
			public double Delay;
			/// <summary>For trafficsim: the most cars it held, and the longest a car stood at its head.</summary>
			public int Most;
			public double LongestHeld;
		}

		/// <summary>A car's way through a connector, for its conflict areas: when it went on and its speed through.</summary>
		private struct Pass
		{
			public double T, V;
			public int Car;
		}

		internal readonly Schedules Plans;
		internal readonly Vehicle[] Cars;
		internal readonly Queue[] Queues;
		/// <summary>The day second now, and the day second the race (the lights' clock) started at.</summary>
		internal double Now, RaceStart;

		/// <summary>Per place, the cars parked there now.</summary>
		internal readonly int[] Parked;

		// ---- what trafficsim reads -----------------------------------------------------------------------------------------

		internal int Departures, Arrivals, Redirects, Reroutes, Unrouted, Overfull;
		/// <summary>Seconds lost per trip over its free time, summed, and the trips.</summary>
		internal double DelaySum;
		internal double WorstDelay;
		/// <summary>Per edge, the cars that came onto it (either way): the day's counts.</summary>
		internal readonly int[] EdgeCount;
		/// <summary>Per hour of the clock from midnight: trips started; cars on the roads sampled each second (through cars apart).</summary>
		internal readonly double[] StartsByHour = new double[24], MovingByHour = new double[24];
		internal int PeakMoving;
		internal double PeakAt;
		internal double RouteMs, StepMs;
		internal int Routes;
		/// <summary>Cars held longer than <see cref="Stuck"/> (each once), and where.</summary>
		internal readonly List<(int car, int lane, double since)> StuckCars = new List<(int, int, double)>();
		private readonly bool[] reportedStuck;

		// ---- the lane graph as carriageways --------------------------------------------------------------------------------

		/// <summary>Per lane: its carriageway (edge * 2, or edge * 2 + 1 going -1, as a leg's route) and how far its end is from the carriageway's start.</summary>
		private static int[] cwOf;
		private static double[] reach;
		/// <summary>Per carriageway: its lanes (through lanes outer first, then a bay) and where along its road it starts.</summary>
		private static List<int>[] cwLanes;
		private static double[] cwStart;
		private static readonly object gate = new object();
		private static int builtFor = -1;

		private readonly List<Pass>[] passes;
		private readonly Heap departures = new Heap();
		private readonly Heap heap = new Heap();
		private readonly double[] cost;
		private readonly int[] via;
		private readonly bool[] seeded;
		private readonly List<int> touched = new List<int>();
		private double nextSample;

		/// <summary>The carriageways worked out once from the lane graph (built if it isn't).</summary>
		private static void Prepare()
		{
			Lanes.Ensure();
			lock (gate)
			{
				if (builtFor == Lanes.All.Count && cwOf != null)
				{
					return;
				}
				int lanes = Lanes.All.Count, cws = Traffic.Edges.Count * 2;
				cwOf = new int[lanes];
				reach = new double[lanes];
				cwLanes = new List<int>[cws];
				cwStart = new double[cws];
				for (int e = 0; e < Traffic.Edges.Count; e++)
				{
					cwLanes[2 * e] = Lanes.Forward[e];
					cwLanes[2 * e + 1] = Lanes.Backward[e];
					for (int k = 0; k < 2; k++)
					{
						List<int> list = cwLanes[2 * e + k];
						if (list.Count == 0)
						{
							continue;
						}
						Lanes.Lane first = Lanes.All[list[0]];
						cwStart[2 * e + k] = first.Start;
						foreach (int l in list)
						{
							Lanes.Lane lane = Lanes.All[l];
							cwOf[l] = 2 * e + k;
							reach[l] = Math.Max(0.0, (lane.End - first.Start) * lane.Dir);
						}
					}
				}
				builtFor = lanes;
			}
		}

		/// <summary>How far a lane is driven to its end from where its carriageway starts (a bay's cars drive its through lane first).</summary>
		internal static double Reach(int lane)
		{
			return reach[lane];
		}

		/// <summary>The carriageway lane <paramref name="lane"/> is on.</summary>
		internal static int CarriagewayOf(int lane)
		{
			return cwOf[lane];
		}

		/// <summary>Distance <paramref name="s"/> along carriageway <paramref name="cw"/>'s road as distance from its start (0 to its length).</summary>
		private static double Along(int cw, double s)
		{
			int dir = (cw & 1) == 0 ? 1 : -1;
			return Math.Max(0.0, (s - cwStart[cw]) * dir);
		}

		/// <summary>The time through connector <paramref name="c"/> and its speed: slower than its limit, as a car through a junction is (often from a stop).</summary>
		private static double PassSpeed(Lanes.Connector c)
		{
			return Math.Max(Lanes.Crawl, c.Limit * 0.75);
		}

		private static double PassTime(Lanes.Connector c)
		{
			return c.Length / PassSpeed(c);
		}

		// ---- building ------------------------------------------------------------------------------------------------------

		private Meso(Schedules plans)
		{
			Prepare();
			Plans = plans;
			Cars = new Vehicle[plans.Cars.Count];
			Queues = new Queue[Lanes.All.Count];
			for (int l = 0; l < Queues.Length; l++)
			{
				Lanes.Lane lane = Lanes.All[l];
				Queues[l] = new Queue { Storage = Math.Max(1, (int)Math.Floor(lane.Length / Jam)) };
			}
			passes = new List<Pass>[Lanes.Connectors.Count];
			for (int c = 0; c < passes.Length; c++)
			{
				passes[c] = new List<Pass>();
			}
			Parked = new int[Traffic.Destinations.Count];
			EdgeCount = new int[Traffic.Edges.Count];
			int cws = Traffic.Edges.Count * 2;
			cost = new double[cws];
			via = new int[cws];
			seeded = new bool[cws];
			for (int i = 0; i < cws; i++)
			{
				cost[i] = double.MaxValue;
				via[i] = -1;
			}
			reportedStuck = new bool[plans.Cars.Count];
		}

		/// <summary>
		/// The simulation of a race's day from its plans, at day second <paramref name="start"/>, every car where the plans have it then
		/// (<see cref="Schedules.At"/>): at home, parked at work or the shops, beyond the map, or part way along its drive (placed along
		/// its route as far as it would have got on free roads; through cars where they start on the trunk). <paramref name="raceStart"/>
		/// is the day second the lights' clock counts from.
		/// </summary>
		internal static Meso Start(Schedules plans, double start, double raceStart)
		{
			Meso m = new Meso(plans);
			m.Now = start;
			m.RaceStart = raceStart;
			m.nextSample = Math.Ceiling(start);
			for (int i = 0; i < plans.Cars.Count; i++)
			{
				m.Place(i);
			}
			return m;
		}

		private void Place(int i)
		{
			Schedules.Car c = Plans.Cars[i];
			Vehicle v = new Vehicle { Car = i, Through = c.Through };
			Cars[i] = v;
			if (c.Through)
			{
				int cw = 2 * c.Start.Edge + (c.Way > 0 ? 0 : 1);
				int prefer = i % 3 == 2 ? 1 : 0;
				v.Mode = Mode.Driving;
				int lane = cwLanes[cw][Math.Min(prefer, Throughs(cw) - 1)];
				v.Path.Clear();
				v.Next = 0;
				int on = ThroughOn(lane, prefer);
				if (on >= 0)
				{
					v.Path.Add(on);
					lane = Lanes.Connectors[on].From;
				}
				Enter(v, lane, -1, Along(cw, c.Start.S), Now);
				return;
			}
			Schedules.Where w = Schedules.At(c, Now);
			if (w.State == Schedules.State.Parked)
			{
				Park(v, w.Index, c.Stops[w.Index].At, PlaceOf(c.Stops[w.Index]), w.From, true);
				v.LeaveAt = w.Until;
				return;
			}
			// Part way along leg w.Index: its route now, and as far along it as the plans' time on it would have got.
			v.Stop = w.Index + 1;
			Schedules.Stop from = c.Stops[w.Index], to = c.Stops[w.Index + 1];
			v.Mode = Mode.Driving;
			v.Target = to.At;
			v.TargetPlace = PlaceOf(to);
			v.TripStart = w.From;
			if (!Route(v, from.At.Edge, from.At.S, to.At, -1))
			{
				Unrouted++;
				Park(v, w.Index + 1, to.At, -1, Now, true);
				return;
			}
			v.TripFree = FreeTime(v, from.At.S);
			// Walk the route by free time.
			double left = Now - w.From;
			int cw0 = v.Path.Count > 0 ? cwOf[Lanes.Connectors[v.Path[0]].From] : FirstCw;
			double along = Along(cw0, from.At.S);
			int lane0 = ExitLane(v, cw0);
			while (true)
			{
				Lanes.Lane lane = Lanes.All[lane0];
				double end = v.Next >= v.Path.Count ? Math.Max(along, Math.Min(reach[lane0], Along(cwOf[lane0], to.At.S))) : reach[lane0];
				double t = Math.Max(0.0, end - along) / lane.Limit;
				if (left < t || v.Next >= v.Path.Count)
				{
					Enter(v, lane0, v.Conn, Math.Min(end, along + left * lane.Limit), Now);
					return;
				}
				left -= t;
				Lanes.Connector cn = Lanes.Connectors[v.Path[v.Next]];
				double pt = PassTime(cn);
				v.Next++;
				v.Conn = v.Path[v.Next - 1];
				int cw = cwOf[cn.To];
				lane0 = ExitLane(v, cw);
				if (left < pt)
				{
					// On the connector: on the lane after it with its entry still to come.
					Enter(v, lane0, v.Conn, 0.0, Now + (pt - left));
					v.ConnAt = Now - left;
					return;
				}
				left -= pt;
				along = 0.0;
			}
		}

		private static int Throughs(int cw)
		{
			int n = 0;
			foreach (int l in cwLanes[cw])
			{
				n += Lanes.All[l].Kind == Lanes.Through ? 1 : 0;
			}
			return n;
		}

		/// <summary>The place a stop parks at (-1: home, a kerb, beyond the map).</summary>
		private static int PlaceOf(Schedules.Stop st)
		{
			return st.Kind == Schedules.StopKind.Place ? st.Dest : -1;
		}

		// ---- running -------------------------------------------------------------------------------------------------------

		/// <summary>Runs the simulation on to day second <paramref name="until"/>.</summary>
		internal void Run(double until)
		{
			System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
			while (Now + Step <= until + 1e-9)
			{
				Now += Step;
				Tick();
			}
			StepMs += watch.Elapsed.TotalMilliseconds;
		}

		private void Tick()
		{
			while (departures.Count > 0)
			{
				int i = departures.Pop(out double at);
				if (at > Now)
				{
					departures.Push(at, i);
					break;
				}
				Vehicle v = Cars[i];
				if ((v.Mode == Mode.Parked || v.Mode == Mode.Away) && v.LeaveAt == at)
				{
					Depart(v);
				}
			}
			for (int l = 0; l < Queues.Length; l++)
			{
				if (Queues[l].Cars.Count > 0)
				{
					Advance(l);
				}
			}
			if (Now >= nextSample)
			{
				nextSample += 1.0;
				int moving = 0;
				foreach (Vehicle v in Cars)
				{
					moving += v.Mode == Mode.Driving && !v.Through ? 1 : 0;
				}
				MovingByHour[(int)Schedules.HourOf(Now) % 24] += moving;
				if (moving > PeakMoving)
				{
					PeakMoving = moving;
					PeakAt = Now;
				}
			}
		}

		/// <summary>A parked car sets off for its next stop (if it can't get onto its road yet, it tries again next step).</summary>
		private void Depart(Vehicle v)
		{
			Schedules.Car c = Plans.Cars[v.Car];
			Schedules.Stop to = c.Stops[v.Stop + 1];
			v.Target = to.At;
			v.TargetPlace = PlaceOf(to);
			v.Full = null;
			v.Reroutes = 0;
			if (!Route(v, v.ParkedAt.Edge, v.ParkedAt.S, to.At, -1))
			{
				Unrouted++;
				Leave(v);
				v.Stop++;
				Park(v, v.Stop, to.At, -1, Now, false);
				return;
			}
			int cw = v.Path.Count > 0 ? cwOf[Lanes.Connectors[v.Path[0]].From] : FirstCw;
			int lane = ExitLane(v, cw);
			if (Queues[lane].Cars.Count >= Queues[lane].Storage && v.Mode == Mode.Parked)
			{
				// The road is queued past the drive: wait for a gap.
				v.LeaveAt = Now + 1.0;
				departures.Push(v.LeaveAt, v.Car);
				return;
			}
			Leave(v);
			v.Stop++;
			v.Mode = Mode.Driving;
			v.TripStart = Now;
			v.TripFree = FreeTime(v, v.ParkedAt.S);
			Departures++;
			StartsByHour[(int)Schedules.HourOf(Now) % 24]++;
			Enter(v, lane, -1, Along(cw, v.ParkedAt.S), Now);
		}

		/// <summary>The car leaves where it was parked.</summary>
		private void Leave(Vehicle v)
		{
			if (v.Place >= 0)
			{
				Parked[v.Place]--;
				v.Place = -1;
			}
		}

		/// <summary>Car <paramref name="v"/> parks at stop <paramref name="stop"/>, at <paramref name="at"/> (place <paramref name="place"/>), having come at <paramref name="arrived"/>.</summary>
		private void Park(Vehicle v, int stop, Traffic.Anchor at, int place, double arrived, bool initial)
		{
			Schedules.Car c = Plans.Cars[v.Car];
			v.Stop = stop;
			v.Mode = c.Stops[stop].Kind == Schedules.StopKind.Away ? Mode.Away : Mode.Parked;
			v.ParkedAt = at;
			v.Place = place;
			v.Lane = -1;
			v.Conn = -1;
			v.Path.Clear();
			v.Next = 0;
			v.Ends = false;
			v.HeldSince = -1.0;
			if (place >= 0)
			{
				Parked[place]++;
				if (Traffic.Destinations[place].Spaces > 0 && Parked[place] > Traffic.Destinations[place].Spaces)
				{
					Overfull++;
				}
			}
			v.Arrived = arrived;
			v.LeaveAt = stop + 1 < c.Stops.Length ? Schedules.Leave(c.Stops[stop], arrived) : double.MaxValue;
			if (v.LeaveAt < double.MaxValue)
			{
				departures.Push(v.LeaveAt, v.Car);
			}
		}

		/// <summary>
		/// Puts car <paramref name="v"/> into lane <paramref name="lane"/>'s queue, coming on <paramref name="from"/> along it at
		/// <paramref name="at"/> (after connector <paramref name="conn"/>, or pulling out onto it).
		/// </summary>
		private void Enter(Vehicle v, int lane, int conn, double from, double at)
		{
			Lanes.Lane l = Lanes.All[lane];
			v.Lane = lane;
			v.Conn = conn;
			v.Entered = at;
			v.From = from;
			int cw = cwOf[lane];
			v.Ends = !v.Through && v.Next >= v.Path.Count;
			if (v.Ends)
			{
				v.DestAlong = Math.Max(from, Math.Min(reach[lane], Along(cw, v.Target.S)));
				v.Ready = at + (v.DestAlong - from) / l.Limit;
			}
			else
			{
				v.Ready = at + Math.Max(0.0, reach[lane] - from) / l.Limit;
			}
			v.HeldSince = -1.0;
			v.HeldByLight = false;
			if (from <= 0.0)
			{
				EdgeCount[cw >> 1]++;
			}
			List<int> q = Queues[lane].Cars;
			int k = q.Count;
			while (k > 0 && Cars[q[k - 1]].Ready > v.Ready)
			{
				k--;
			}
			q.Insert(k, v.Car);
			Queues[lane].Most = Math.Max(Queues[lane].Most, q.Count);
		}

		/// <summary>The lane car <paramref name="v"/> drives carriageway <paramref name="cw"/> in: the one its next connector leaves from, else (its stop on it, or a through car) the outer.</summary>
		private int ExitLane(Vehicle v, int cw)
		{
			if (v.Next < v.Path.Count)
			{
				return Lanes.Connectors[v.Path[v.Next]].From;
			}
			return cwLanes[cw][0];
		}

		/// <summary>Lane <paramref name="l"/>'s cars: those reaching their stop on it leave, and its head goes on if it can.</summary>
		private void Advance(int l)
		{
			Queue q = Queues[l];
			List<int> cars = q.Cars;
			double length = reach[l];
			// Cars stopping along it: past the standing queue, they turn off into their stop.
			int standing = 0;
			for (int k = 0; k < cars.Count; k++)
			{
				Vehicle v = Cars[cars[k]];
				if (v.Ready > Now)
				{
					break;
				}
				if (!v.Ends)
				{
					standing++;
					continue;
				}
				if (v.DestAlong <= length - (standing - 0.5) * Jam || standing == 0)
				{
					cars.RemoveAt(k);
					k--;
					Arrive(v);
				}
			}
			// The head: the first car not stopping on the lane.
			int head = -1;
			foreach (int i in cars)
			{
				if (!Cars[i].Ends)
				{
					head = i;
					break;
				}
			}
			if (head < 0)
			{
				return;
			}
			Vehicle h = Cars[head];
			if (h.Ready > Now || Now < q.LastExit + Headway)
			{
				return;
			}
			if (h.Next >= h.Path.Count && h.Through)
			{
				int on = ThroughOn(l, Lanes.All[l].Index);
				if (on < 0)
				{
					return;
				}
				h.Path.Clear();
				h.Next = 0;
				h.Path.Add(on);
			}
			int c = h.Path[h.Next];
			if (Go(h, c, out bool light, out int target))
			{
				cars.Remove(head);
				Lanes.Connector cn = Lanes.Connectors[c];
				double held = h.HeldSince >= 0.0 ? Now - h.HeldSince : 0.0;
				q.LongestHeld = Math.Max(q.LongestHeld, held);
				q.LastExit = Now;
				double lost = Now - h.Entered - Math.Max(0.0, length - h.From) / Lanes.All[l].Limit;
				q.Delay += (Math.Max(0.0, lost) - q.Delay) * Smooth;
				List<Pass> ps = passes[c];
				int gone = 0;
				while (gone < ps.Count && ps[gone].T < Now - 60.0)
				{
					// Long through the junction: forgotten, so the list stays short.
					gone++;
				}
				ps.RemoveRange(0, gone);
				ps.Add(new Pass { T = Now, V = PassSpeed(cn), Car = head });
				h.Next++;
				h.ConnAt = Now;
				if (h.Through)
				{
					int on = ThroughOn(cn.To, Lanes.All[cn.To].Index);
					h.Path.Clear();
					h.Next = 0;
					if (on >= 0)
					{
						h.Path.Add(on);
					}
				}
				Enter(h, target, c, 0.0, Now + PassTime(cn));
				h.ConnAt = Now;
				return;
			}
			if (h.HeldSince < 0.0)
			{
				h.HeldSince = Now;
			}
			h.HeldByLight = light;
			if (light)
			{
				h.HeldSince = Now;
				return;
			}
			double waited = Now - h.HeldSince;
			if (waited > Stuck && !reportedStuck[head])
			{
				reportedStuck[head] = true;
				StuckCars.Add((head, l, h.HeldSince));
			}
			if (waited > Reroute && !h.Through && h.Reroutes < MaxReroutes)
			{
				// Another way from this lane's end, if there is a quicker one now.
				h.Reroutes++;
				int cw = cwOf[l];
				List<int> old = new List<int>(h.Path);
				int oldNext = h.Next;
				if (Route(h, -1, 0.0, h.Target, l) && h.Path.Count > 0 && h.Path[0] != old[oldNext])
				{
					Reroutes++;
					h.HeldSince = Now;
				}
				else
				{
					h.Path.Clear();
					h.Path.AddRange(old);
					h.Next = oldNext;
				}
			}
		}

		/// <summary>
		/// Whether car <paramref name="h"/>, at the head of its lane, can take connector <paramref name="c"/> now: its light (<paramref
		/// name="light"/> set if that is what holds it), room on the lane it goes on to (<paramref name="target"/>), and the conflict areas.
		/// </summary>
		private bool Go(Vehicle h, int c, out bool light, out int target)
		{
			Lanes.Connector cn = Lanes.Connectors[c];
			light = false;
			target = Target(h, c, h.Next + 1);
			if (!LightLets(cn, h.Ready))
			{
				light = true;
				return false;
			}
			if (Queues[target].Cars.Count >= Queues[target].Storage)
			{
				return false;
			}
			return Clear(h, c);
		}

		/// <summary>The lane a car taking connector <paramref name="c"/> drives next: the one its connector after leaves from (lanes are changed along the stretch), else the outer or a through car's own.</summary>
		private int Target(Vehicle v, int c, int after)
		{
			Lanes.Connector cn = Lanes.Connectors[c];
			if (v.Through)
			{
				int on = ThroughOn(cn.To, Lanes.All[cn.To].Index);
				return on >= 0 ? Lanes.Connectors[on].From : cn.To;
			}
			if (after < v.Path.Count)
			{
				return Lanes.Connectors[v.Path[after]].From;
			}
			return cwLanes[cwOf[cn.To]][0];
		}

		/// <summary>Whether connector <paramref name="cn"/>'s light lets a car go that reached the stop line at <paramref name="ready"/>.</summary>
		private bool LightLets(Lanes.Connector cn, double ready)
		{
			if (cn.Turn == Lanes.Jump || !TrafficSignals.Has(cn.Node))
			{
				return true;
			}
			int state = TrafficSignals.State(cn.Node, cn.InArm, cn.OutArm, Now - RaceStart);
			return state == TrafficSignals.Green || (state == TrafficSignals.Amber && ready >= Now - AmberGo);
		}

		/// <summary>
		/// Whether connector <paramref name="c"/>'s conflict areas are clear for car <paramref name="h"/> going now: no car already through
		/// one of them within <see cref="Margin"/> of when this one would be, and, where it gives way (or the area is first come first served
		/// and the other came first), no car coming to it on the other connector before this one would be through, unless that car can't
		/// go itself (its light red, no room past the junction) or has been held at its line less long than this one.
		/// </summary>
		private bool Clear(Vehicle h, int c)
		{
			Lanes.Connector cn = Lanes.Connectors[c];
			double vc = PassSpeed(cn);
			foreach (int a in Conflicts.Of[c])
			{
				Conflicts.Area area = Conflicts.All[a];
				if (area.Yield == Conflicts.Apart || area.Yield == Conflicts.Follow)
				{
					continue;
				}
				bool meA = area.A == c;
				int o = meA ? area.B : area.A;
				double my0 = Math.Max(0.0, meA ? area.A0 : area.B0), my1 = Math.Max(0.0, meA ? area.A1 : area.B1);
				double o0 = Math.Max(0.0, meA ? area.B0 : area.A0), o1 = Math.Max(0.0, meA ? area.B1 : area.A1);
				double myIn = Now + my0 / vc, myOut = Now + my1 / vc;
				List<Pass> ps = passes[o];
				for (int k = ps.Count - 1; k >= 0; k--)
				{
					Pass p = ps[k];
					double oOut = p.T + o1 / p.V;
					double oIn = p.T + o0 / p.V;
					if (oIn < myOut + Margin && myIn < oOut + Margin)
					{
						return false;
					}
				}
				int mine = meA ? Conflicts.YieldA : Conflicts.YieldB;
				bool gives = area.Yield == mine || area.Yield == Conflicts.FirstCome;
				if (!gives)
				{
					continue;
				}
				// The car coming to the other connector: its lane's head, if that is its way.
				Lanes.Connector oc = Lanes.Connectors[o];
				int g = HeadOf(oc.From);
				if (g < 0)
				{
					continue;
				}
				Vehicle gv = Cars[g];
				if (gv.Next >= gv.Path.Count || gv.Path[gv.Next] != o)
				{
					continue;
				}
				if (area.Yield == Conflicts.FirstCome && (gv.Ready > h.Ready || (gv.Ready == h.Ready && area.Tie != mine)))
				{
					continue;
				}
				if (!LightLets(oc, gv.Ready))
				{
					continue;
				}
				if (gv.HeldSince >= 0.0 && !gv.HeldByLight && h.HeldSince >= 0.0 && (h.HeldSince < gv.HeldSince || (h.HeldSince == gv.HeldSince && h.Car < gv.Car)))
				{
					// It is held too (giving way to someone else, as at two junctions whose boxes touch, where each car can give way to
					// another round a loop): the one waiting longer goes, as drivers sort out a standoff. The check above keeps them apart.
					continue;
				}
				int gt = Target(gv, o, gv.Next + 1);
				if (Queues[gt].Cars.Count >= Queues[gt].Storage)
				{
					// It can't go itself (no room past the junction): no need to wait for it.
					continue;
				}
				double go = Math.Max(Math.Max(gv.Ready, Now), Queues[oc.From].LastExit + Headway);
				double gIn = go + o0 / PassSpeed(oc);
				if (gIn < myOut + Margin)
				{
					return false;
				}
			}
			return true;
		}

		/// <summary>The car at the head of lane <paramref name="l"/> (the first not stopping on it), or -1.</summary>
		private int HeadOf(int l)
		{
			foreach (int i in Queues[l].Cars)
			{
				if (!Cars[i].Ends)
				{
					return i;
				}
			}
			return -1;
		}

		/// <summary>A car reaches its stop (or the lot it was going to): parks if there is room, else goes on to the next place with some.</summary>
		private void Arrive(Vehicle v)
		{
			if (v.TargetPlace >= 0)
			{
				Traffic.Destination d = Traffic.Destinations[v.TargetPlace];
				if (d.Spaces > 0 && Parked[v.TargetPlace] >= d.Spaces)
				{
					int next = NextPlace(v);
					if (next >= 0)
					{
						Redirects++;
						(v.Full ??= new List<int>()).Add(v.TargetPlace);
						Traffic.Anchor here = v.Target;
						v.Target = Traffic.Destinations[next].At;
						v.TargetPlace = next;
						if (Route(v, here.Edge, here.S, v.Target, -1))
						{
							int cw = v.Path.Count > 0 ? cwOf[Lanes.Connectors[v.Path[0]].From] : FirstCw;
							Enter(v, ExitLane(v, cw), -1, Along(cw, here.S), Now);
							return;
						}
					}
				}
			}
			Arrivals++;
			double lost = Now - v.TripStart - v.TripFree;
			DelaySum += Math.Max(0.0, lost);
			WorstDelay = Math.Max(WorstDelay, lost);
			Park(v, v.Stop, v.Target, v.TargetPlace, Now, false);
		}

		/// <summary>The nearest place (straight line) drawing the stop's purpose with room, not one already found full; -1: none, park where it is.</summary>
		private int NextPlace(Vehicle v)
		{
			Schedules.Stop st = Plans.Cars[v.Car].Stops[v.Stop];
			Traffic.Destination here = Traffic.Destinations[v.TargetPlace];
			int best = -1;
			double bestD = double.MaxValue;
			for (int d = 0; d < Traffic.Destinations.Count; d++)
			{
				Traffic.Destination o = Traffic.Destinations[d];
				if (d == v.TargetPlace || o.Kind == Traffic.DestinationKind.Edge || (v.Full != null && v.Full.Contains(d)))
				{
					continue;
				}
				double w = st.Purpose == Traffic.Purpose.Work ? o.Work : st.Purpose == Traffic.Purpose.Shop ? o.Shop : st.Purpose == Traffic.Purpose.Errand ? o.Errand : o.Social;
				if (w <= 0.0 || (o.Spaces > 0 && Parked[d] >= o.Spaces) || !Traffic.OnMain(o.At.Edge))
				{
					continue;
				}
				double dx = o.At.X - here.At.X, dz = o.At.Z - here.At.Z, dd = dx * dx + dz * dz;
				if (dd < bestD)
				{
					bestD = dd;
					best = d;
				}
			}
			return best;
		}

		/// <summary>A through car's way on from trunk lane <paramref name="lane"/>: straight on (or through the wrap) onto the trunk, in lane <paramref name="prefer"/> if it can.</summary>
		private static int ThroughOn(int lane, int prefer)
		{
			int best = -1, bestScore = int.MaxValue;
			foreach (int l in cwLanes[cwOf[lane]])
			{
				Lanes.Lane from = Lanes.All[l];
				if (from.Kind != Lanes.Through)
				{
					continue;
				}
				foreach (int c in from.Out)
				{
					Lanes.Connector cn = Lanes.Connectors[c];
					Lanes.Lane to = Lanes.All[cn.To];
					if ((cn.Turn != Lanes.Straight && cn.Turn != Lanes.Jump) || Network.Roads[Traffic.Edges[to.Edge].Road].Kind != RoadKind.Trunk)
					{
						continue;
					}
					int score = (from.Index == prefer ? 0 : 2) + (to.Index == prefer ? 0 : 1);
					if (score < bestScore)
					{
						bestScore = score;
						best = c;
					}
				}
			}
			return best;
		}

		// ---- routes on the lane graph ----------------------------------------------------------------------------------------

		/// <summary>The carriageway a route with no connectors is on (its start and end on one stretch).</summary>
		private int FirstCw;

		/// <summary>A lane's time at the live delays: its free time over <paramref name="dist"/> and what is being lost on it.</summary>
		private double LaneTime(int l, double dist)
		{
			Queue q = Queues[l];
			double standing = 0.0;
			int h = HeadOf(l);
			if (h >= 0 && Cars[h].HeldSince >= 0.0)
			{
				standing = Now - Cars[h].HeldSince;
			}
			return dist / Lanes.All[l].Limit + Math.Max(q.Delay, Math.Max(standing, q.Cars.Count * Headway * 0.5));
		}

		/// <summary>
		/// Car <paramref name="v"/>'s quickest way at the live times from road distance <paramref name="s"/> along edge <paramref name="edge"/>
		/// (either way: a car pulling out may turn either way) to <paramref name="to"/>, or (<paramref name="fromLane"/> &gt;= 0) from the end
		/// of that lane: the connectors in order into its <see cref="Vehicle.Path"/>. Dijkstra over the carriageways, entered at their start;
		/// the time on each lane to its end and through each connector. False if there is no way.
		/// </summary>
		private bool Route(Vehicle v, int edge, double s, Traffic.Anchor to, int fromLane)
		{
			System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
			Routes++;
			v.Path.Clear();
			v.Next = 0;
			try
			{
				if (fromLane < 0 && edge == to.Edge)
				{
					FirstCw = 2 * edge + (to.S >= s ? 0 : 1);
					return cwLanes[FirstCw].Count > 0;
				}
				foreach (int st in touched)
				{
					cost[st] = double.MaxValue;
					via[st] = -1;
					seeded[st] = false;
				}
				touched.Clear();
				heap.Clear();
				void Reach(int cw, double c, int conn, bool seed)
				{
					if (c < cost[cw])
					{
						if (cost[cw] == double.MaxValue)
						{
							touched.Add(cw);
						}
						cost[cw] = c;
						via[cw] = conn;
						seeded[cw] = seed;
						heap.Push(c, cw);
					}
				}
				void Mark(int cw)
				{
					if (cost[cw] == double.MaxValue)
					{
						touched.Add(cw);
					}
					cost[cw] = 0.0;
					via[cw] = -1;
				}
				if (fromLane >= 0)
				{
					Mark(cwOf[fromLane]);
					foreach (int c in Lanes.All[fromLane].Out)
					{
						Lanes.Connector cn = Lanes.Connectors[c];
						Reach(cwOf[cn.To], PassTime(cn) + TurnLoss(c), c, true);
					}
				}
				else
				{
					for (int k = 0; k < 2; k++)
					{
						int cw = 2 * edge + k;
						if (cwLanes[cw].Count == 0)
						{
							continue;
						}
						Mark(cw);
						double at = Along(cw, s);
						foreach (int l in cwLanes[cw])
						{
							double t = LaneTime(l, Math.Max(0.0, reach[l] - at));
							foreach (int c in Lanes.All[l].Out)
							{
								Lanes.Connector cn = Lanes.Connectors[c];
								Reach(cwOf[cn.To], t + PassTime(cn) + TurnLoss(c), c, true);
							}
						}
					}
				}
				double best = double.MaxValue;
				int bestCw = -1;
				while (heap.Count > 0)
				{
					int u = heap.Pop(out double c);
					if (c > cost[u])
					{
						continue;
					}
					if (c >= best)
					{
						break;
					}
					if (u >> 1 == to.Edge)
					{
						double inner = Along(u, to.S) / Lanes.All[cwLanes[u][0]].Limit;
						if (c + inner < best)
						{
							best = c + inner;
							bestCw = u;
						}
					}
					foreach (int l in cwLanes[u])
					{
						double t = LaneTime(l, reach[l]);
						foreach (int cc in Lanes.All[l].Out)
						{
							Lanes.Connector cn = Lanes.Connectors[cc];
							Reach(cwOf[cn.To], c + t + PassTime(cn) + TurnLoss(cc), cc, false);
						}
					}
				}
				if (bestCw < 0)
				{
					return false;
				}
				for (int u = bestCw; ;)
				{
					int cc = via[u];
					v.Path.Add(cc);
					if (seeded[u])
					{
						break;
					}
					u = cwOf[Lanes.Connectors[cc].From];
				}
				v.Path.Reverse();
				return true;
			}
			finally
			{
				RouteMs += watch.Elapsed.TotalMilliseconds;
			}
		}

		/// <summary>What a driver expects to lose taking connector <paramref name="c"/>: an eighth of a light's cycle where there is one (red about half the time, half a red's wait), a little giving way at a turn.</summary>
		private static double TurnLoss(int c)
		{
			Lanes.Connector cn = Lanes.Connectors[c];
			double loss = cn.Turn == Lanes.Left ? 2.0 : cn.Turn == Lanes.Right ? 1.0 : cn.Turn == Lanes.Round ? 8.0 : 0.0;
			if (cn.Turn != Lanes.Jump && TrafficSignals.Has(cn.Node))
			{
				loss += TrafficSignals.Cycle(cn.Node) / 8.0;
			}
			return loss;
		}

		/// <summary>Car <paramref name="v"/>'s route's time on free roads from <paramref name="s"/> on its first stretch (for the delay a trip lost).</summary>
		private double FreeTime(Vehicle v, double s)
		{
			int cw = v.Path.Count > 0 ? cwOf[Lanes.Connectors[v.Path[0]].From] : FirstCw;
			double at = Along(cw, s), t = 0.0;
			for (int k = 0; k < v.Path.Count; k++)
			{
				Lanes.Connector cn = Lanes.Connectors[v.Path[k]];
				t += Math.Max(0.0, reach[cn.From] - at) / Lanes.All[cn.From].Limit + PassTime(cn);
				at = 0.0;
				cw = cwOf[cn.To];
			}
			t += Math.Max(0.0, Along(cw, v.Target.S) - at) / Lanes.All[cwLanes[cw][0]].Limit;
			return t;
		}

		// ---- reading the state -------------------------------------------------------------------------------------------------

		/// <summary>
		/// Where car <paramref name="i"/> is now, for drawing and the map: on connector <paramref name="conn"/> (&gt;= 0) <paramref
		/// name="along"/> through it, else on lane <paramref name="lane"/> <paramref name="along"/> from its carriageway's start (moving
		/// at free speed, stopped behind the cars standing before it). False if it is parked or beyond the map.
		/// </summary>
		internal bool Where(int i, out int lane, out int conn, out double along)
		{
			Vehicle v = Cars[i];
			lane = v.Lane;
			conn = -1;
			along = 0.0;
			if (v.Mode != Mode.Driving)
			{
				return false;
			}
			if (Now < v.Entered && v.Conn >= 0)
			{
				conn = v.Conn;
				Lanes.Connector cn = Lanes.Connectors[conn];
				along = Math.Min(cn.Length, (Now - v.ConnAt) * PassSpeed(cn));
				return true;
			}
			Lanes.Lane l = Lanes.All[lane];
			double free = v.From + Math.Max(0.0, Now - v.Entered) * l.Limit;
			int ahead = 0;
			foreach (int k in Queues[lane].Cars)
			{
				if (k == i)
				{
					break;
				}
				ahead += Cars[k].Ready <= Now && !Cars[k].Ends ? 1 : 0;
			}
			along = Math.Min(free, Math.Min(v.Ends ? v.DestAlong : reach[lane], reach[lane] - ahead * Jam));
			return true;
		}

		/// <summary>What holds car <paramref name="i"/> at its lane's end, for trafficsim's reports.</summary>
		internal string Why(int i)
		{
			Vehicle v = Cars[i];
			if (v.Mode != Mode.Driving || v.Next >= v.Path.Count)
			{
				return v.Mode.ToString();
			}
			int c = v.Path[v.Next];
			Lanes.Connector cn = Lanes.Connectors[c];
			int target = Target(v, c, v.Next + 1);
			string why = "lane " + v.Lane + " (" + Queues[v.Lane].Cars.Count + "/" + Queues[v.Lane].Storage + ", head " + (HeadOf(v.Lane) == i) + ") by connector " + c
				+ " (turn " + cn.Turn + ") to lane " + target + " (" + Queues[target].Cars.Count + "/" + Queues[target].Storage + ", length "
				+ Lanes.All[target].Length.ToString("0") + ")";
			if (!LightLets(cn, v.Ready))
			{
				return why + ": red";
			}
			if (Queues[target].Cars.Count >= Queues[target].Storage)
			{
				return why + ": no room";
			}
			return why + (Clear(v, c) ? ": free" : ": a conflict");
		}

		/// <summary>
		/// Whether every car is in exactly one place: parked or beyond the map and in no queue, or driving and in its lane's queue once.
		/// Trafficsim calls it through the day (conservation).
		/// </summary>
		internal bool Conserved(out string why)
		{
			why = "";
			int[] seen = new int[Cars.Length];
			for (int l = 0; l < Queues.Length; l++)
			{
				foreach (int i in Queues[l].Cars)
				{
					seen[i]++;
					if (Cars[i].Lane != l || Cars[i].Mode != Mode.Driving)
					{
						why = "car " + i + " is in lane " + l + "'s queue but " + Cars[i].Mode + " in lane " + Cars[i].Lane;
						return false;
					}
				}
			}
			for (int i = 0; i < Cars.Length; i++)
			{
				bool driving = Cars[i].Mode == Mode.Driving;
				if (seen[i] != (driving ? 1 : 0))
				{
					why = "car " + i + " (" + Cars[i].Mode + ") is in " + seen[i] + " queues";
					return false;
				}
			}
			return true;
		}
	}
}
