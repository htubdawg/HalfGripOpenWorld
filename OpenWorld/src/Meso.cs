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
			/// <summary>Out of the day: a physics car knocked off its line or wrecked (<see cref="Micro.LoseCar"/>). Its household goes without it.</summary>
			Gone,
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
			/// <summary>Its speed coming onto the lane, and at the lane's end (or its stop) on a free road (<see cref="Driver.Run"/>); its speed coming onto its connector.</summary>
			public double V, VOut, VConn;
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
			/// <summary>When it first tried to set off on this trip (-1: not yet): a car waiting for a gap to pull out is delayed from then.</summary>
			public double TriedOut = -1.0;
			public List<int> Full;
			/// <summary>Driven by the micro level (<see cref="Near"/>) near a player rather than by its queue: it is still in the queue of the
			/// lane it is on (or, on a connector, the lane after it), so storage and conservation count it, but the meso leaves it alone.</summary>
			public bool Micro;
		}

		/// <summary>A lane's queue: its cars in the order they reach its end.</summary>
		internal sealed class Queue
		{
			public readonly List<int> Cars = new List<int>();
			/// <summary>How many cars it holds standing (at least 1).</summary>
			public int Storage;
			/// <summary>When the last car left it over its end.</summary>
			public double LastExit = double.MinValue;
			/// <summary>How many cars have left it one behind another since the queue started moving (its first 0): the n-th has had n cars' room behind the line to pull away in.</summary>
			public int Run;
			/// <summary>Seconds lost on it, smoothed over the cars leaving it: what routes are worked out on.</summary>
			public double Delay;
			/// <summary>For trafficsim: the most cars it held, and the longest a car stood at its head.</summary>
			public int Most;
			public double LongestHeld;
		}

		/// <summary>A car's way through a connector, for its conflict areas: when it went on and its speed through.</summary>
		private struct Pass
		{
			public double T, V0;
			public int Car;
		}

		internal readonly Schedules Plans;
		internal readonly Vehicle[] Cars;
		internal readonly Queue[] Queues;
		/// <summary>The day second now, and the day second the race (the lights' clock) started at.</summary>
		internal double Now, RaceStart;

		/// <summary>Per place, the cars parked there now.</summary>
		internal readonly int[] Parked;

		/// <summary>The micro level round the players (null: the meso alone, as headless phase 3). It drives the lanes into its junctions; the
		/// meso asks it for room on them and for a gap to pull out into.</summary>
		internal Micro Near;

		// ---- what trafficsim reads -----------------------------------------------------------------------------------------

		internal int Departures, Arrivals, Redirects, Reroutes, Unrouted, Overfull;
		/// <summary>Seconds lost per trip over its free time, summed, and the trips.</summary>
		internal double DelaySum;
		/// <summary>For trafficsim's consistency check, when set: each trip's car, the stop it reached, how long it took (from first trying to set off), when it got there, whether it was sent on from a full lot.</summary>
		internal List<(int Car, int Stop, double Took, double At, bool SentOn)> Trips = null;
		internal double WorstDelay;
		/// <summary>Per edge, the cars that came onto it (either way): the day's counts.</summary>
		internal readonly int[] EdgeCount;
		/// <summary>Per lane, the cars that went over its end and the seconds they lost on it over its free time (micro's too): trafficsim compares the levels by them.</summary>
		internal readonly int[] LaneExits;
		internal readonly double[] LaneLost;
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

		/// <summary>The carriageways worked out (once) from the lane graph, for readers of the static figures before any simulation.</summary>
		internal static void Ensure()
		{
			Prepare();
		}

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
		internal static double Along(int cw, double s)
		{
			int dir = (cw & 1) == 0 ? 1 : -1;
			return Math.Max(0.0, (s - cwStart[cw]) * dir);
		}

		/// <summary>The time through connector <paramref name="c"/> and its speed: slower than its limit, as a car through a junction is (often from a stop).</summary>
		internal static double PassSpeed(Lanes.Connector c)
		{
			return Math.Max(Lanes.Crawl, c.Limit * 0.75);
		}

		internal static double PassTime(Lanes.Connector c)
		{
			return c.Length / PassSpeed(c);
		}

		/// <summary>The time through connector <paramref name="c"/> coming on at <paramref name="v0"/> (as a micro car drives it: at the turn's limit, slowing for the lane after if that is slower) and the speed coming off.</summary>
		internal static double ConnTime(Lanes.Connector c, double v0, out double vEnd)
		{
			return Driver.Run(c.Length, v0, c.Limit, Lanes.All[c.To].Limit, out vEnd);
		}

		/// <summary>When a car coming onto connector <paramref name="c"/> at <paramref name="v0"/> is <paramref name="d"/> through it (past its end, on at the turn's limit).</summary>
		internal static double PassAt(Lanes.Connector c, double v0, double d)
		{
			return d <= 0.0 ? 0.0 : Driver.Run(d, v0, c.Limit, c.Limit, out double _);
		}

		/// <summary>The speed a car wants at the end of its stretch of lane <paramref name="lane"/> before connector <paramref name="c"/>: the turn's limit (the lane's with none).</summary>
		internal static double EndSpeed(int lane, int c)
		{
			double lim = Lanes.All[lane].Limit;
			return c >= 0 ? Math.Min(lim, Lanes.Connectors[c].Limit) : lim;
		}

		/// <summary>The speed car <paramref name="v"/> wants at the end of its stretch of lane <paramref name="lane"/>: 0 at its stop, else its next connector's (<see cref="EndSpeed(int, int)"/>).</summary>
		internal static double EndSpeed(Vehicle v, int lane)
		{
			if (v.Ends)
			{
				return 0.0;
			}
			return EndSpeed(lane, v.Next < v.Path.Count ? v.Path[v.Next] : -1);
		}

		/// <summary>Car <paramref name="v"/>'s time on a free road over lane <paramref name="lane"/> from where it came on to the end, before connector <paramref name="c"/>: what a car is counted as losing on a lane is over this.</summary>
		internal static double LaneFree(Vehicle v, int lane, int c)
		{
			Lanes.Lane l = Lanes.All[lane];
			return Driver.Run(Math.Max(0.0, reach[lane] - v.From), v.V, l.Limit, EndSpeed(lane, c), out double _);
		}

		/// <summary>Where along its lane car <paramref name="v"/> (on it, not on a connector) would be at <paramref name="t"/> on a free road, and its
		/// speed: standing once it has come to the end (still in the queue there, so held: at a red, giving way, or its stop).</summary>
		internal static double FreeAlong(Vehicle v, double t, out double speed)
		{
			Lanes.Lane l = Lanes.All[v.Lane];
			double end = v.Ends ? v.DestAlong : reach[v.Lane], dist = Math.Max(0.0, end - v.From);
			double x = Driver.RunAt(dist, v.V, l.Limit, EndSpeed(v, v.Lane), t - v.Entered, out speed);
			if (x >= dist)
			{
				speed = 0.0;
			}
			return v.From + x;
		}

		/// <summary>Distance <paramref name="along"/> from carriageway <paramref name="cw"/>'s start as distance along its road (the inverse of <see cref="Along"/>).</summary>
		internal static double RoadS(int cw, double along)
		{
			return cwStart[cw] + ((cw & 1) == 0 ? along : -along);
		}

		/// <summary>The lanes of carriageway <paramref name="cw"/>: through lanes outer first, then a bay.</summary>
		internal static List<int> LanesOf(int cw)
		{
			return cwLanes[cw];
		}

		/// <summary>Where along its carriageway (from its start) lane <paramref name="lane"/> starts: 0, or part way for a bay.</summary>
		internal static double StartOf(int lane)
		{
			return Along(cwOf[lane], Lanes.All[lane].Start);
		}

		/// <summary>
		/// Where along the carriageway after connector <paramref name="c"/> a car's front is as it comes off it: its middle at the connector's
		/// end (where it joins the lane, <see cref="Lanes.Connector.Join"/>), its front half a car on; the jump through the wrap leaves the front
		/// at the lane's start.
		/// </summary>
		internal static double JoinFrom(int c)
		{
			return Math.Min(JoinAt(c), reach[Lanes.Connectors[c].To]);
		}

		/// <summary>
		/// <see cref="JoinFrom"/> not held to the lane's end: on a lane shorter than half a car (two junctions' boxes meeting) the front is
		/// already past it, on the connector after (the micro level carries a car straight on through).
		/// </summary>
		internal static double JoinAt(int c)
		{
			Lanes.Connector cn = Lanes.Connectors[c];
			if (cn.Turn == Lanes.Jump)
			{
				// Through the wrap: out at the lane's start.
				return Along(cwOf[cn.To], Lanes.All[cn.To].Start);
			}
			return Along(cwOf[cn.To], cn.Join) + Lanes.Axle;
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
			LaneExits = new int[Lanes.All.Count];
			LaneLost = new double[Lanes.All.Count];
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
			double speed = 0.0;
			while (true)
			{
				Lanes.Lane lane = Lanes.All[lane0];
				bool ends = v.Next >= v.Path.Count;
				double end = ends ? Math.Max(along, Math.Min(reach[lane0], Along(cwOf[lane0], to.At.S))) : reach[lane0];
				double vEnd = ends ? 0.0 : EndSpeed(lane0, v.Path[v.Next]);
				double t = Driver.Run(Math.Max(0.0, end - along), speed, lane.Limit, vEnd, out double vOff);
				if (left < t || ends)
				{
					double x = along + Driver.RunAt(Math.Max(0.0, end - along), speed, lane.Limit, vEnd, left, out double vNow);
					Enter(v, lane0, v.Conn, Math.Min(end, x), Now, true, vNow);
					return;
				}
				left -= t;
				Lanes.Connector cn = Lanes.Connectors[v.Path[v.Next]];
				double pt = ConnTime(cn, vOff, out speed);
				v.Next++;
				v.Conn = v.Path[v.Next - 1];
				int cw = cwOf[cn.To];
				lane0 = ExitLane(v, cw);
				if (left < pt)
				{
					// On the connector: on the lane after it with its entry still to come.
					Enter(v, lane0, v.Conn, JoinFrom(v.Conn), Now + (pt - left), true, speed);
					v.ConnAt = Now - left;
					v.VConn = vOff;
					return;
				}
				left -= pt;
				along = JoinFrom(v.Conn);
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
				if (Queues[l].Cars.Count > 0 && (Near == null || !Near.LaneMicro[l]))
				{
					Advance(l);
				}
			}
			Near?.Step();
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
			bool full = Queues[lane].Cars.Count >= Queues[lane].Storage
				|| (Near != null && Near.LaneMicro[lane] && !Near.CanPullOut(lane, Along(cw, v.ParkedAt.S)));
			if (v.TriedOut < 0.0)
			{
				v.TriedOut = Now;
			}
			if (full && v.Mode == Mode.Parked)
			{
				// The road is queued past the drive: wait for a gap.
				v.LeaveAt = Now + 1.0;
				departures.Push(v.LeaveAt, v.Car);
				return;
			}
			Leave(v);
			v.Stop++;
			v.Mode = Mode.Driving;
			v.TripStart = v.TriedOut;
			v.TriedOut = -1.0;
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
			v.TriedOut = -1.0;
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
		/// <paramref name="at"/> (after connector <paramref name="conn"/>, or pulling out onto it), going <paramref name="vIn"/> (NaN: as off
		/// the connector on a free road, or from standing pulling out).
		/// </summary>
		internal void Enter(Vehicle v, int lane, int conn, double from, double at, bool count = true, double vIn = double.NaN)
		{
			Lanes.Lane l = Lanes.All[lane];
			v.Lane = lane;
			v.Conn = conn;
			v.Entered = at;
			v.From = from;
			int cw = cwOf[lane];
			v.Ends = !v.Through && v.Next >= v.Path.Count;
			if (double.IsNaN(vIn))
			{
				vIn = conn >= 0 ? Math.Min(Lanes.Connectors[conn].Limit, l.Limit) : 0.0;
			}
			v.V = vIn;
			double end = reach[lane];
			if (v.Ends)
			{
				v.DestAlong = Math.Max(from, Math.Min(reach[lane], Along(cw, v.Target.S)));
				end = v.DestAlong;
			}
			v.Ready = at + Driver.Run(Math.Max(0.0, end - from), vIn, l.Limit, EndSpeed(v, lane), out v.VOut);
			if (conn >= 0 && Throughs(cw) == 1)
			{
				// No passing on a road one lane each way: at its end no sooner than the cars already on it (a slower car ahead holds it up,
				// as it does a micro car). Its lane's queue keeps the order they came on in.
				foreach (int ahead in Queues[lane].Cars)
				{
					Vehicle o = Cars[ahead];
					if (!o.Ends && !o.Micro && ahead != v.Car)
					{
						v.Ready = Math.Max(v.Ready, o.Ready);
					}
				}
			}
			v.HeldSince = -1.0;
			v.HeldByLight = false;
			v.Micro = false;
			if (count && (conn >= 0 || from <= 0.0))
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

		/// <summary>Whether car <paramref name="v"/>, coming onto lane <paramref name="lane"/> <paramref name="from"/> along its carriageway, stops on it (its route done) and where.</summary>
		internal void Aim(Vehicle v, int lane, double from)
		{
			v.Ends = !v.Through && v.Next >= v.Path.Count;
			if (v.Ends)
			{
				v.DestAlong = Math.Max(from, Math.Min(reach[lane], Along(cwOf[lane], v.Target.S)));
			}
		}

		/// <summary>For the micro level: a car came onto lane <paramref name="lane"/> through a connector (the day's counts).</summary>
		internal void Counted(int lane)
		{
			EdgeCount[cwOf[lane] >> 1]++;
		}

		/// <summary>For the micro level: car <paramref name="v"/> went over lane <paramref name="lane"/>'s stop line now (what routes are worked out on).</summary>
		internal void Crossed(int lane, Vehicle v, int c)
		{
			Queue q = Queues[lane];
			q.LastExit = Now;
			double lost = Now - v.Entered - LaneFree(v, lane, c);
			q.Delay += (Math.Max(0.0, lost) - q.Delay) * Smooth;
			LaneExits[lane]++;
			LaneLost[lane] += Math.Max(0.0, lost);
		}

		/// <summary>For the micro level handing a junction back: car <paramref name="car"/> went onto connector <paramref name="c"/> at <paramref name="t"/>, for the conflict areas.</summary>
		internal void Passed(int c, double t, int car)
		{
			List<Pass> ps = passes[c];
			int k = ps.Count;
			while (k > 0 && ps[k - 1].T > t)
			{
				k--;
			}
			ps.Insert(k, new Pass { T = t, V0 = Cars[car].VConn, Car = car });
		}

		/// <summary>The lane car <paramref name="v"/> drives carriageway <paramref name="cw"/> in: the one its next connector leaves from, else (its stop on it, or a through car) the outer.</summary>
		internal int ExitLane(Vehicle v, int cw)
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
				if (v.Micro)
				{
					continue;
				}
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
			int head = HeadOf(l);
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
			if (Go(h, c, out bool light, out int target, out double v0, out int run))
			{
				cars.Remove(head);
				Lanes.Connector cn = Lanes.Connectors[c];
				double held = h.HeldSince >= 0.0 ? Now - h.HeldSince : 0.0;
				q.LongestHeld = Math.Max(q.LongestHeld, held);
				q.LastExit = Now;
				double lost = Now - h.Entered - LaneFree(h, l, c);
				q.Delay += (Math.Max(0.0, lost) - q.Delay) * Smooth;
				LaneExits[l]++;
				LaneLost[l] += Math.Max(0.0, lost);
				q.Run = run;
				h.VConn = v0;
				List<Pass> ps = passes[c];
				int gone = 0;
				while (gone < ps.Count && ps[gone].T < Now - 60.0)
				{
					// Long through the junction: forgotten, so the list stays short.
					gone++;
				}
				ps.RemoveRange(0, gone);
				ps.Add(new Pass { T = Now, V0 = v0, Car = head });
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
				double through = ConnTime(cn, v0, out double vOff);
				Enter(h, target, c, JoinFrom(c), Now + through, true, vOff);
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
		/// name="light"/> set if that is what holds it), room on the lane it goes on to (<paramref name="target"/>), the conflict areas, and the car gone before it from the same lane (its speed onto the connector, <paramref name="v0"/>, and how many cars of a moving queue went before it, <paramref name="run"/>).
		/// </summary>
		private bool Go(Vehicle h, int c, out bool light, out int target, out double v0, out int run)
		{
			Lanes.Connector cn = Lanes.Connectors[c];
			light = false;
			v0 = 0.0;
			run = 0;
			target = Target(h, c, h.Next + 1);
			double start = StartSpeed(h, cn.From, out run);
			if (!LightLets(cn, h.Ready) && !AmberGoes(cn, start, run))
			{
				light = true;
				return false;
			}
			if (Queues[target].Cars.Count >= Queues[target].Storage || (Near != null && Near.LaneMicro[target] && !Near.Room(target, c)))
			{
				return false;
			}
			v0 = Behind(c, start);
			return v0 >= 0.0 && Clear(h, c, v0);
		}

		/// <summary>
		/// The speed a car going onto connector <paramref name="c"/> at <paramref name="v0"/> can go on at without catching the car gone
		/// before it from the same lane: onto the same connector (the head of a queue pulling away from standing, the car behind already
		/// moving), or onto another while the two ways still share the road (their diverging area: a car turning off slowly holds up the
		/// one behind it going on, as in the micro, where it is its leader, <see cref="Micro"/>'s Siblings). The micro, taking the junction
		/// over, finds them where the meso has them. -1: not even from standing (it waits).
		/// </summary>
		private double Behind(int c, double v0)
		{
			Lanes.Connector cn = Lanes.Connectors[c];
			double cap = Limit(c, cn.Length, c, cn.Length, v0);
			foreach (int a in Conflicts.Of[c])
			{
				Conflicts.Area area = Conflicts.All[a];
				if (cap < 0.0)
				{
					break;
				}
				if (area.Yield == Conflicts.Follow)
				{
					bool meA = area.A == c;
					cap = Limit(c, meA ? area.A1 : area.B1, meA ? area.B : area.A, meA ? area.B1 : area.A1, cap);
				}
			}
			return cap;
		}

		/// <summary>Scratch for <see cref="Limit"/>: when the car ahead is at each point looked at.</summary>
		private readonly double[] dueAt = new double[7];

		/// <summary>
		/// <paramref name="v0"/>, or less, so that a car going onto connector <paramref name="c"/> now is no nearer than a standing queue's
		/// spacing behind the last car onto connector <paramref name="o"/> anywhere from the stop line to <paramref name="my1"/> along (where
		/// the shared road ends, <paramref name="o1"/> along that one). -1: not even from standing (the car ahead not a spacing on yet).
		/// </summary>
		private double Limit(int c, double my1, int o, double o1, double v0)
		{
			List<Pass> ps = passes[o];
			if (ps.Count == 0 || my1 <= 0.0 || o1 <= 0.0)
			{
				return v0;
			}
			// The last one onto it is the nearest ahead (they came off the one lane in order).
			Pass p = ps[ps.Count - 1];
			Lanes.Connector cn = Lanes.Connectors[c], oc = Lanes.Connectors[o];
			// Looked at from the stop line (the one ahead a spacing on before this one goes: a car pulling away from standing takes
			// longer than the headway) to the end of the shared road, along which the two lines run together.
			const int Points = 6; // dueAt's length less one
			double[] due = dueAt;
			for (int k = 0; k <= Points; k++)
			{
				due[k] = p.T + PassAt(oc, p.V0, o1 * k / Points + Jam) - Now;
			}
			bool Ok(double v)
			{
				for (int k = 0; k <= Points; k++)
				{
					if (PassAt(cn, v, my1 * k / Points) < due[k])
					{
						return false;
					}
				}
				return true;
			}
			if (Ok(v0))
			{
				return v0;
			}
			if (!Ok(0.0))
			{
				return -1.0;
			}
			double lo = 0.0, hi = v0;
			for (int it = 0; it < 16; it++)
			{
				double mid = 0.5 * (lo + hi);
				if (Ok(mid))
				{
					lo = mid;
				}
				else
				{
					hi = mid;
				}
			}
			return lo;
		}

		/// <summary>The lane a car taking connector <paramref name="c"/> drives next: the one its connector after leaves from (lanes are changed along the stretch), else the outer or a through car's own.</summary>
		internal int Target(Vehicle v, int c, int after)
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
		internal bool LightLets(Lanes.Connector cn, double ready)
		{
			if (cn.Turn == Lanes.Jump || !TrafficSignals.Has(cn.Node))
			{
				return true;
			}
			int state = TrafficSignals.State(cn.Node, cn.InArm, cn.OutArm, Now - RaceStart);
			return state == TrafficSignals.Green || (state == TrafficSignals.Amber && ready >= Now - AmberGo);
		}

		/// <summary>
		/// Whether the head of a queue pulling away (<paramref name="run"/> cars gone one behind another before it, reaching the line at
		/// <paramref name="v0"/>) goes on at connector <paramref name="cn"/>'s amber, as a micro driver judges it: on if, when the amber
		/// came on, it couldn't stop before the line braking comfortably. It has gathered speed from standing at <see cref="Driver.Accel"/>,
		/// so <c>τ</c> seconds before the line it was <c>v0 τ - a τ² / 2</c> away. A queue still going at the green's end lets one or two
		/// more cars through, as the micro's does.
		/// </summary>
		private bool AmberGoes(Lanes.Connector cn, double v0, int run)
		{
			if (run <= 0 || v0 <= 0.0 || cn.Turn == Lanes.Jump || !TrafficSignals.Has(cn.Node))
			{
				return false;
			}
			double tau = TrafficSignals.AmberFor(cn.Node, cn.InArm, cn.OutArm, Now - RaceStart);
			if (tau < 0.0)
			{
				return false;
			}
			double away = tau < v0 / Driver.Accel ? v0 * tau - 0.5 * Driver.Accel * tau * tau : 0.5 * v0 * v0 / Driver.Accel;
			return Driver.Stopping(v0, Driver.Brake) > away;
		}

		/// <summary>
		/// Whether connector <paramref name="c"/>'s conflict areas are clear for car <paramref name="h"/> going now: no car already through
		/// one of them within <see cref="Margin"/> of when this one would be, and, where it gives way (or the area is first come first served
		/// and the other came first), no car coming to it on the other connector before this one would be through, unless that car can't
		/// go itself (its light red, no room past the junction) or has been held at its line less long than this one.
		/// </summary>
		private bool Clear(Vehicle h, int c, double v0)
		{
			Lanes.Connector cn = Lanes.Connectors[c];
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
				double myIn = Now + PassAt(cn, v0, my0), myOut = Now + PassAt(cn, v0, my1);
				List<Pass> ps = passes[o];
				for (int k = ps.Count - 1; k >= 0; k--)
				{
					Pass p = ps[k];
					Lanes.Connector pc = Lanes.Connectors[o];
					double oOut = p.T + PassAt(pc, p.V0, o1);
					double oIn = p.T + PassAt(pc, p.V0, o0);
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
				// The cars coming to the other connector: its lane's head, if that is its way, and those still on their way along the lane
				// behind it (a car on the road it gives way to coming fast is as much in the way as one at the line: micro's drivers look
				// as far back as Micro.Approach).
				Lanes.Connector oc = Lanes.Connectors[o];
				int g = HeadOf(oc.From);
				double lastGo = Queues[oc.From].LastExit;
				foreach (int k in g < 0 ? none : Queues[oc.From].Cars)
				{
					Vehicle gv = Cars[k];
					if (gv.Ends || gv.Micro)
					{
						continue;
					}
					bool head = k == g;
					if (!head && gv.Ready <= Now)
					{
						// Standing behind the head: not before it goes (the head is looked at already).
						continue;
					}
					if (gv.Ready > myOut + Margin)
					{
						break;
					}
					double go = Math.Max(Math.Max(gv.Ready, Now), lastGo + Headway);
					lastGo = go;
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
					if (head && gv.HeldSince >= 0.0 && !gv.HeldByLight && h.HeldSince >= 0.0 && (h.HeldSince < gv.HeldSince || (h.HeldSince == gv.HeldSince && h.Car < gv.Car)))
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
					double gv0 = gv.Ready >= Now ? gv.VOut : 0.0;
					double gIn = go + PassAt(oc, gv0, o0);
					double behind = Margin;
					if (area.Kind == Conflicts.Merging)
					{
						// Merging ahead of it: room for it to come down to this car's speed comfortably and keep its time gap after (as a
						// micro driver judges it, Micro.Clear).
						double vj = Math.Max(gv0, Math.Min(oc.Limit, Math.Sqrt(gv0 * gv0 + 2.0 * Driver.Accel * Math.Max(0.0, o0))));
						double vMe = Math.Min(cn.Limit, Math.Sqrt(v0 * v0 + 2.0 * Driver.Accel * Math.Max(0.0, my1)));
						double need = Driver.Gap0 + vMe * Driver.TimeGap + Math.Max(0.0, vj - vMe) * Math.Max(0.0, vj - vMe) / (2.0 * Driver.Brake);
						behind = Math.Max(behind, need / Math.Max(vj, 1.0));
					}
					if (gIn < myOut + 0.5 + behind)
					{
						return false;
					}
				}
				if (reach[oc.From] < Lookback && Upstream(h, cn, v0, oc, o, o0, my1, myOut))
				{
					return false;
				}
			}
			return true;
		}

		/// <summary>
		/// The speed car <paramref name="h"/>, at the head of lane <paramref name="l"/>, goes over its stop line at going now: on a free road
		/// its run's (<see cref="Vehicle.VOut"/>); held (at a red, giving way, or slowed behind the car before), from standing, with as many
		/// cars' room behind the line to pull away in as have left one behind another before it (<paramref name="run"/>): the head of a queue
		/// at a green starts on the line, the fifth car behind it is already moving.
		/// </summary>
		private double StartSpeed(Vehicle h, int l, out int run)
		{
			if (Now - h.Ready < Step * 1.01)
			{
				run = 0;
				return h.VOut;
			}
			Queue q = Queues[l];
			run = Now - q.LastExit <= Headway + Step * 1.5 ? q.Run + 1 : 0;
			return Math.Min(h.VOut, Math.Sqrt(2.0 * Driver.Accel * run * Jam));
		}

		/// <summary>How far back along the roads a driver giving way looks for cars coming (Micro's Approach): past the junction before where
		/// the lane in is shorter.</summary>
		private const double Lookback = 600.0;

		/// <summary>No cars (a lane in with none on it: those on the lanes before are still looked for).</summary>
		private static readonly List<int> none = new List<int>();

		/// <summary>Scratch for <see cref="Upstream"/>: the connectors from the lane looked along to the one given way to.</summary>
		private readonly List<int> chain = new List<int>();

		/// <summary>
		/// Whether a car on a lane into the junction before connector <paramref name="o"/>'s (its lane in, <paramref name="oc"/>.From, short),
		/// or into the junction before that while the lanes between are short (to <see cref="Lookback"/> in all, as micro's drivers look back
		/// along each car's route through the junctions: at three junctions in a row a fast car two junctions back is as near as one on the
		/// lane in), comes on through to <paramref name="o"/> too soon for car <paramref name="h"/> to go now (coming onto <paramref
		/// name="cn"/> at <paramref name="v0"/>, clear of the area <paramref name="my1"/> along it at <paramref name="myOut"/>): not one held
		/// at a red.
		/// </summary>
		private bool Upstream(Vehicle h, Lanes.Connector cn, double v0, Lanes.Connector oc, int o, double o0, double my1, double myOut)
		{
			chain.Clear();
			chain.Add(o);
			return Back(oc.From, reach[oc.From], cn, v0, oc, o0, my1, myOut, 0);
		}

		/// <summary><see cref="Upstream"/> for the lanes into lane <paramref name="lane"/>'s start (<see cref="chain"/>: the connectors from
		/// <paramref name="lane"/>'s end on), <paramref name="dist"/> from there to the stop line of the connector given way to.</summary>
		private bool Back(int lane, double dist, Lanes.Connector cn, double v0, Lanes.Connector oc, double o0, double my1, double myOut, int depth)
		{
			foreach (int kc in Lanes.All[lane].In)
			{
				Lanes.Connector kn = Lanes.Connectors[kc];
				chain.Insert(0, kc);
				foreach (int k in Queues[kn.From].Cars)
				{
					Vehicle gv = Cars[k];
					if (gv.Ends || gv.Micro || gv.Ready <= Now || gv.Next + chain.Count > gv.Path.Count)
					{
						continue;
					}
					bool way = true;
					for (int j = 0; j < chain.Count && way; j++)
					{
						way = gv.Path[gv.Next + j] == chain[j];
					}
					if (!way)
					{
						continue;
					}
					if (gv.Ready > myOut + 4.0)
					{
						break;
					}
					if (!LightLets(kn, gv.Ready))
					{
						continue;
					}
					// On at its free run's speed through the junctions before and along the short lanes.
					double v = Math.Min(gv.VOut, kn.Limit), t = gv.Ready;
					for (int j = 0; j + 1 < chain.Count; j++)
					{
						Lanes.Connector cj = Lanes.Connectors[chain[j]];
						t += ConnTime(cj, j == 0 ? v : Math.Min(v, cj.Limit), out double vOff);
						int next = chain[j + 1], on = Lanes.Connectors[next].From;
						t += Driver.Run(Math.Max(0.0, reach[on] - JoinFrom(chain[j])), vOff, Lanes.All[on].Limit, EndSpeed(on, next), out v);
					}
					double vLine = v;
					double gIn = t + PassAt(oc, vLine, o0);
					double vj = Math.Max(vLine, Math.Min(oc.Limit, Math.Sqrt(vLine * vLine + 2.0 * Driver.Accel * Math.Max(0.0, o0))));
					double vMe = Math.Min(cn.Limit, Math.Sqrt(v0 * v0 + 2.0 * Driver.Accel * Math.Max(0.0, my1)));
					double need = Driver.Gap0 + vMe * Driver.TimeGap + Math.Max(0.0, vj - vMe) * Math.Max(0.0, vj - vMe) / (2.0 * Driver.Brake);
					if (gIn < myOut + 0.5 + Math.Max(Margin, need / Math.Max(vj, 1.0)))
					{
						chain.RemoveAt(0);
						return true;
					}
				}
				double further = dist + kn.Length + reach[kn.From];
				if (depth < 4 && further < Lookback && Back(kn.From, further, cn, v0, oc, o0, my1, myOut, depth + 1))
				{
					chain.RemoveAt(0);
					return true;
				}
				chain.RemoveAt(0);
			}
			return false;
		}

		/// <summary>The car at the head of lane <paramref name="l"/> (the first not stopping on it), or -1.</summary>
		private int HeadOf(int l)
		{
			foreach (int i in Queues[l].Cars)
			{
				if (!Cars[i].Ends && !Cars[i].Micro)
				{
					return i;
				}
			}
			return -1;
		}

		/// <summary>A car reaches its stop (or the lot it was going to): parks if there is room, else goes on to the next place with some.</summary>
		/// <summary>For the overlay: cars gone from the day (knocked off the road or wrecked).</summary>
		internal int Gone;

		/// <summary>Car <paramref name="v"/>, already out of its queue, leaves the day: never parked, never setting off again.</summary>
		internal void Lose(Vehicle v)
		{
			v.Mode = Mode.Gone;
			v.Lane = -1;
			v.Conn = -1;
			v.Micro = false;
			v.LeaveAt = double.MaxValue;
			v.HeldSince = -1.0;
			v.Path.Clear();
			v.Next = 0;
			Gone++;
		}

		internal void Arrive(Vehicle v)
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
			Trips?.Add((v.Car, v.Stop, Now - v.TripStart, Now, v.Full != null));
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
		internal static int ThroughOn(int lane, int prefer)
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
						Reach(cwOf[cn.To], PassCost(c) + TurnLoss(c), c, true);
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
								Reach(cwOf[cn.To], t + PassCost(c) + TurnLoss(c), c, true);
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
							Reach(cwOf[cn.To], c + t + PassCost(cc) + TurnLoss(cc), cc, false);
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
				Settle(v.Path, fromLane);
				return true;
			}
			finally
			{
				RouteMs += watch.Elapsed.TotalMilliseconds;
			}
		}

		/// <summary>
		/// Route <paramref name="path"/>'s connectors chosen, from its end back, so a car can drive it lane by lane: each one through its
		/// junction (the same turn onto the same carriageway) that lands where the next leaves from in the fewest lane changes, a change
		/// counted only with <see cref="ChangeRoom"/> for it on the stretch between. The carriageways' search takes the cheapest connector
		/// into a carriageway, but a micro car on a connector comes out in the lane it leads to, and where junctions follow close on each
		/// other (four on the trunk within 300, a left-turn bay after them) it must be in the lane for the last of them from the stretch
		/// before the first, or it missed its turn (and a car the meso handed the micro mid-junction came out in the wrong lane). The same
		/// lane in kept on a tie; the first from <paramref name="fromLane"/> if the car is in it (-1: pulling out).
		/// </summary>
		private static void Settle(List<int> path, int fromLane)
		{
			for (int k = path.Count - 2; k >= 0; k--)
			{
				Lanes.Lane want = Lanes.All[Lanes.Connectors[path[k + 1]].From];
				Lanes.Connector cur = Lanes.Connectors[path[k]];
				int toCw = cwOf[cur.To];
				int best = -1;
				double bestScore = double.MaxValue;
				foreach (int l in cwLanes[cwOf[cur.From]])
				{
					if (k == 0 && fromLane >= 0 && l != fromLane)
					{
						continue;
					}
					foreach (int o in Lanes.All[l].Out)
					{
						Lanes.Connector on = Lanes.Connectors[o];
						if (on.Node != cur.Node || on.Turn != cur.Turn || cwOf[on.To] != toCw)
						{
							continue;
						}
						Lanes.Lane land = Lanes.All[on.To];
						int changes = Math.Abs(land.Index - want.Index);
						double room = Math.Min(reach[on.To], reach[Lanes.Connectors[path[k + 1]].From]) - Math.Max(StartOf(on.To), StartOf(Lanes.Connectors[path[k + 1]].From)) - want.Hold;
						double score = changes + (room < changes * ChangeRoom(land) ? 1000.0 : 0.0) + (on.From == cur.From ? 0.0 : 0.5);
						if (score < bestScore)
						{
							bestScore = score;
							best = o;
						}
					}
				}
				if (best >= 0)
				{
					path[k] = best;
				}
			}
		}

		/// <summary>
		/// The room a car is counted on to need on lane <paramref name="l"/> for each lane it moves over: twice what a change at the lane's
		/// limit takes (on the trunk 500, its left-turn bays a little more), as with traffic alongside a gap may not come at once (stretches
		/// of 320 and 475 on the trunk left cars standing at their ends, one needing two changes in 570 another). The micro's drivers choose
		/// their lanes ahead by it too.
		/// </summary>
		internal static double ChangeRoom(Lanes.Lane l)
		{
			return 2.0 * l.Limit * Driver.ChangeTime + Traffic.CarLength;
		}

		/// <summary>For the micro level: car <paramref name="v"/>'s quickest way on from the end of lane <paramref name="lane"/> (its <see cref="Vehicle.Path"/> from there). False if none.</summary>
		internal bool RouteFrom(Vehicle v, int lane)
		{
			return Route(v, -1, 0.0, v.Target, lane);
		}

		private static double[] passCost;

		/// <summary>
		/// A route's cost through connector <paramref name="c"/> on free roads: its time and what slowing into it from the lane in's limit
		/// and pulling away to the lane out's costs over driving those stretches at their limits (<see cref="Driver.Run"/>), as a car
		/// is timed through it; the turns' flat losses had routes taking many slow turns to save a little distance.
		/// </summary>
		private static double PassCost(int c)
		{
			if (passCost == null || passCost.Length != Lanes.Connectors.Count)
			{
				double[] pc = new double[Lanes.Connectors.Count];
				for (int k = 0; k < pc.Length; k++)
				{
					Lanes.Connector cn = Lanes.Connectors[k];
					double vin = Lanes.All[cn.From].Limit, vout = Lanes.All[cn.To].Limit, vc = Math.Min(cn.Limit, vin);
					double b = Driver.Brake * 0.5, brakeD = (vin * vin - vc * vc) / (2.0 * b);
					double slow = brakeD > 0.0 ? 2.0 * brakeD / (vin + vc) - brakeD / vin : 0.0;
					double through = ConnTime(cn, vc, out double vOff);
					const double Away = 1500.0;
					double away = Driver.Run(Away, vOff, vout, vout, out double _) - Away / vout;
					pc[k] = slow + through + Math.Max(0.0, away);
				}
				passCost = pc;
			}
			return passCost[c];
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

		/// <summary>Scratch for <see cref="PlanRoute"/>, one per plans' builder (the plans may be built on a worker).</summary>
		internal sealed class PlanScratch
		{
			internal double[] Cost;
			internal int[] Via;
			internal bool[] Seeded;
			internal readonly List<int> Touched = new List<int>();
			internal readonly Heap Heap = new Heap();
		}

		/// <summary>
		/// For the plans (<see cref="Schedules"/>): the quickest way from <paramref name="from"/> to <paramref name="to"/> on empty roads by
		/// the lane graph, as the meso routes a car setting off on them (<see cref="Route"/>'s costs with no queues: each stretch at its
		/// limit, each junction's <see cref="PassCost"/> and <see cref="TurnLoss"/>), into <paramref name="route"/> as (edge, +1 along it
		/// or -1). So a plan's leg is one a car can drive, timed by <see cref="LegTime"/> (the traffic's map takes turns the lane graph
		/// has none for, as back up an acute fork, and its legs' times were for roads no car could take) and through the wrap's tunnels as
		/// the meso goes. False if either end has no lanes or there is no way. Deterministic under <see cref="Network"/>'s rules.
		/// </summary>
		internal static bool PlanRoute(Traffic.Anchor from, Traffic.Anchor to, List<(int edge, int dir)> route, PlanScratch x)
		{
			Prepare();
			route.Clear();
			if (from.Edge == to.Edge)
			{
				int cw0 = 2 * from.Edge + (to.S >= from.S ? 0 : 1);
				if (cwLanes[cw0].Count == 0)
				{
					return false;
				}
				route.Add((from.Edge, to.S >= from.S ? 1 : -1));
				return true;
			}
			int cws = cwLanes.Length;
			if (x.Cost == null || x.Cost.Length != cws)
			{
				x.Cost = new double[cws];
				x.Via = new int[cws];
				x.Seeded = new bool[cws];
				for (int k = 0; k < cws; k++)
				{
					x.Cost[k] = double.MaxValue;
					x.Via[k] = -1;
				}
				x.Touched.Clear();
			}
			double[] cost = x.Cost;
			int[] via = x.Via;
			bool[] seeded = x.Seeded;
			foreach (int st in x.Touched)
			{
				cost[st] = double.MaxValue;
				via[st] = -1;
				seeded[st] = false;
			}
			x.Touched.Clear();
			Heap heap = x.Heap;
			heap.Clear();
			void Reach(int cw, double c, int conn, bool seed)
			{
				if (c < cost[cw])
				{
					if (cost[cw] == double.MaxValue)
					{
						x.Touched.Add(cw);
					}
					cost[cw] = c;
					via[cw] = conn;
					seeded[cw] = seed;
					heap.Push(c, cw);
				}
			}
			for (int k = 0; k < 2; k++)
			{
				int cw = 2 * from.Edge + k;
				if (cwLanes[cw].Count == 0)
				{
					continue;
				}
				if (cost[cw] == double.MaxValue)
				{
					x.Touched.Add(cw);
				}
				cost[cw] = 0.0;
				via[cw] = -1;
				double at = Along(cw, from.S);
				foreach (int l in cwLanes[cw])
				{
					double t = Math.Max(0.0, reach[l] - at) / Lanes.All[l].Limit;
					foreach (int c in Lanes.All[l].Out)
					{
						Reach(cwOf[Lanes.Connectors[c].To], t + PassCost(c) + TurnLoss(c), c, true);
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
					double t = reach[l] / Lanes.All[l].Limit;
					foreach (int cc in Lanes.All[l].Out)
					{
						Reach(cwOf[Lanes.Connectors[cc].To], c + t + PassCost(cc) + TurnLoss(cc), cc, false);
					}
				}
			}
			if (bestCw < 0)
			{
				return false;
			}
			for (int u = bestCw; ;)
			{
				route.Add((u >> 1, (u & 1) == 0 ? 1 : -1));
				if (via[u] < 0)
				{
					break;
				}
				bool first = seeded[u];
				u = cwOf[Lanes.Connectors[via[u]].From];
				if (first)
				{
					route.Add((u >> 1, (u & 1) == 0 ? 1 : -1));
					break;
				}
			}
			route.Reverse();
			return true;
		}

		/// <summary>
		/// For the plans (<see cref="Schedules"/>): the time on free roads, driven as a micro car drives (<see cref="Driver.Run"/>), along
		/// carriageways <paramref name="route"/> (2 × edge, + 1 against its direction) from <paramref name="fromS"/> on the first to
		/// <paramref name="toS"/> on the last, through the junctions by the fastest connector from one to the next; from standing and to a stop.
		/// -1 where two carriageways have no connector between them. Deterministic under <see cref="Network"/>'s rules (the plans are).
		/// </summary>
		internal static double LegTime(int[] route, double fromS, double toS)
		{
			Prepare();
			int cw = route[0];
			if (cwLanes[cw].Count == 0)
			{
				return -1.0;
			}
			double at = Along(cw, fromS), t = 0.0, speed = 0.0;
			for (int k = 0; k + 1 < route.Length; k++)
			{
				int next = route[k + 1], best = -1;
				foreach (int l in cwLanes[cw])
				{
					foreach (int c in Lanes.All[l].Out)
					{
						Lanes.Connector cn = Lanes.Connectors[c];
						if (cwOf[cn.To] == next && (best < 0 || (Lanes.Connectors[best].Turn == Lanes.Jump && cn.Turn != Lanes.Jump)
							|| ((cn.Turn == Lanes.Jump) == (Lanes.Connectors[best].Turn == Lanes.Jump) && cn.Limit > Lanes.Connectors[best].Limit)))
						{
							// (Through the wrap's tunnels by its jump only where that is the way: a route the meso takes round the highway.)
							best = c;
						}
					}
				}
				if (best < 0)
				{
					return -1.0;
				}
				Lanes.Connector bc = Lanes.Connectors[best];
				t += Driver.Run(Math.Max(0.0, reach[bc.From] - at), speed, Lanes.All[bc.From].Limit, EndSpeed(bc.From, best), out speed);
				t += ConnTime(bc, speed, out speed);
				at = JoinFrom(best);
				cw = next;
			}
			if (cwLanes[cw].Count == 0)
			{
				return -1.0;
			}
			t += Driver.Run(Math.Max(0.0, Along(cw, toS) - at), speed, Lanes.All[cwLanes[cw][0]].Limit, 0.0, out double _);
			return t;
		}

		/// <summary>Car <paramref name="v"/>'s route's time on free roads from <paramref name="s"/> on its first stretch (for the delay a trip lost).</summary>
		private double FreeTime(Vehicle v, double s)
		{
			int cw = v.Path.Count > 0 ? cwOf[Lanes.Connectors[v.Path[0]].From] : FirstCw;
			double at = Along(cw, s), t = 0.0, speed = 0.0;
			for (int k = 0; k < v.Path.Count; k++)
			{
				Lanes.Connector cn = Lanes.Connectors[v.Path[k]];
				t += Driver.Run(Math.Max(0.0, reach[cn.From] - at), speed, Lanes.All[cn.From].Limit, EndSpeed(cn.From, v.Path[k]), out speed);
				t += ConnTime(cn, speed, out speed);
				at = JoinFrom(v.Path[k]);
				cw = cwOf[cn.To];
			}
			t += Driver.Run(Math.Max(0.0, Along(cw, v.Target.S) - at), speed, Lanes.All[cwLanes[cw][0]].Limit, 0.0, out double _);
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
			if (v.Micro)
			{
				return Near.Where(i, out lane, out conn, out along);
			}
			if (Now < v.Entered && v.Conn >= 0)
			{
				conn = v.Conn;
				Lanes.Connector cn = Lanes.Connectors[conn];
				along = Math.Min(cn.Length, Driver.RunAt(cn.Length, v.VConn, cn.Limit, Lanes.All[cn.To].Limit, Now - v.ConnAt, out double _));
				return true;
			}
			double free = FreeAlong(v, Now, out double _);
			int ahead = 0;
			foreach (int k in Queues[lane].Cars)
			{
				if (k == i)
				{
					break;
				}
				ahead += Cars[k].Ready <= Now && !Cars[k].Ends && !Cars[k].Micro ? 1 : 0;
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
			double v0 = Behind(c, StartSpeed(v, v.Lane, out int _));
			return why + (v0 < 0.0 ? ": behind a slow car turning off" : Clear(v, c, v0) ? ": free" : ": a conflict");
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
