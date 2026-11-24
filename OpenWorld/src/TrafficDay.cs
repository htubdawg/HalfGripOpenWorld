using System;
using System.Collections.Generic;

namespace HalfGripOpenWorld
{
	/// <summary>
	/// One race's day of traffic: every trip the households make that day and every car's place at any moment, worked out once from the
	/// race's seed over the traffic's map (<see cref="Traffic"/>). The day runs from 03:00 (the quietest hour) round to 03:00 again in
	/// <see cref="Traffic.DaySeconds"/> and repeats; a race starting at any hour joins it there (<see cref="SimTime"/>).
	///
	/// How a day is made (docs/TRAFFIC_HANDOFF.md):
	/// - The day: a weekend day with probability 2/7, else a weekday.
	/// - Tours: each household gets that day's candidate tours (work, shopping, errands, visits) from the survey's rates, a leaving time
	///   from the purpose's hour table and a stay; a place by gravity (nearer and bigger draws more). Then every tour is kept with one
	///   probability, so the time households spend driving is the survey's share of the day: the roads as busy as Pennsylvania's counters
	///   say, with the hours' shape and the purposes' mix kept. Through traffic comes in and goes out at the roads out.
	/// - The simulation: every car driven on a fixed step in index order, with a speed profile per leg (limits, bends, turns, a stop at
	///   the end), the Intelligent Driver Model behind the car in front, and every junction uncontrolled: first come first served by
	///   reservation, the car on the right first at a tie, nobody into a junction they cannot clear.
	/// - Recorded: each leg's distance along its path every half second; <see cref="At"/> reads any car's place from it.
	///
	/// A car that drives into one of the trunk's tunnels always comes out of the other at the same moment (<see cref="Wrap"/>): a trip
	/// to or from the trunk's ends carries on through the wrap to the road north, whose tunnel is shut to players, so no car is ever seen
	/// to vanish into a tunnel or appear from one without its pair. Local routes never use the wrap as a short cut (it is not in the
	/// graph).
	///
	/// Deterministic like <see cref="Herds"/>: integer hashing, doubles with + - * / and <c>Math.Sqrt</c>, ties broken by index, so every
	/// machine in a room gets the same day. No Unity types: <c>tools/trafficview</c> builds and checks it. Heights are from sea level
	/// (add <see cref="Network.Lift"/> for the scene).
	/// </summary>
	internal sealed class TrafficDay
	{
		// ---- the driving -------------------------------------------------------------------------------------------------------

		/// <summary>The simulation's step (seconds) and how often each leg's place is recorded.</summary>
		internal const double Step = 0.25;
		internal const double RecordStep = 0.5;
		/// <summary>The Intelligent Driver Model's figures: comfortable acceleration and braking (2.5 m/s²), time gap, standstill gap.</summary>
		internal const double Accel = 5.0;
		internal const double Brake = 8.25;
		internal const double Headway = 1.5;
		internal const double MinGap = 7.0;
		/// <summary>How far ahead along its own path a car looks for the car in front.</summary>
		private const double Ahead = 200.0;
		/// <summary>The speed profile's spacing along a path.</summary>
		private const double Sample = 10.0;
		/// <summary>A sharp turn at a junction (about 9 mph).</summary>
		private const double TurnSpeed = 13.0;
		/// <summary>The sideways acceleration a driver takes a bend at: 0.25 g in units.</summary>
		private const double Lateral = 8.1;
		/// <summary>A car kept waiting at a junction this long is let through (a guard against deadlock: there should be none).</summary>
		private const double ForceAfter = 45.0;
		/// <summary>A car waiting to pull out this long pulls out anyway (it should be let in long before).</summary>
		private const double PullOutForce = 300.0;
		/// <summary>
		/// No tour leaves after 21:00 (sim time 1800): a drive here spans an hour or more of the world's clock (it runs 36 times fast,
		/// driving does not), so a later one would put the evening's traffic into the small hours the counters see empty.
		/// </summary>
		private const double LastOut = 1800.0;

		/// <summary>
		/// A car waiting this long to pull out is let in: the car behind it on the lane leaves it a gap. Only on a road no faster than
		/// <see cref="SlowRoad"/> (a town street): elsewhere it waits until the road is clear, as the manual says.
		/// </summary>
		private const double LetInAfter = 20.0;
		/// <summary>The fastest road (a 25 mph street) where a car pulling out may be let in, or take a short gap (units a second).</summary>
		private const double SlowRoad = 26.0 * Traffic.Mph;
		/// <summary>
		/// The shortest stay at a place. A stay is counted from leaving home, not from arriving: the world's clock runs 36 times fast and
		/// driving does not, so a drive spans an hour or two of it, and a stay counted from arriving would push every way home late into
		/// the night. Counted from leaving, a tour keeps its place in the day (out in the morning, home in the late afternoon).
		/// </summary>
		internal const double MinStay = 20.0;

		/// <summary>A car is not taken out again until it has been home this long.</summary>
		private const double Rest = 60.0;
		/// <summary>How long past the day's end the last cars are driven home.</summary>
		private const double Tail = 2400.0;

		// ---- the day's figures (docs/PLAN.md, "Traffic: what the studies say") -------------------------------------------------

		/// <summary>Tours a household makes in a day, by purpose: work per worker, the rest per household (weekday, weekend).</summary>
		private static readonly double[] RateWeekday = { 0.86, 0.45, 0.6, 0.5 };
		private static readonly double[] RateWeekend = { 0.12, 0.75, 0.45, 0.85 };

		/// <summary>When tours leave, by world hour from midnight: weights (work, shop, errand, social), weekday then weekend.</summary>
		private static readonly double[][] HoursWeekday =
		{
			new double[] { 0, 0, 0, 1, 5, 14, 17, 10, 5, 2, 1.5, 1.5, 1.5, 1.5, 1.5, 1, 1, 0.5, 0.3, 0.2, 0, 0, 0, 0 },
			new double[] { 0, 0, 0, 0, 0, 0.2, 1, 3, 6, 8, 9, 9, 9, 9, 9.5, 9.5, 8, 5, 2, 0.5, 0, 0, 0, 0 },
			new double[] { 0, 0, 0, 0, 0.5, 2, 5, 7, 8, 8, 8, 8, 8, 8, 8.5, 8.5, 6, 3, 1.5, 0.5, 0, 0, 0, 0 },
			new double[] { 0, 0, 0, 0, 0, 0, 0.5, 1, 2, 3, 4, 4, 4, 4.5, 5.5, 8, 10, 10, 7, 3, 0, 0, 0, 0 },
		};
		private static readonly double[][] HoursWeekend =
		{
			new double[] { 0, 0, 0, 1, 4, 8, 10, 8, 6, 4, 3, 3, 2, 2, 2, 1.5, 1, 0.5, 0, 0, 0, 0, 0, 0 },
			new double[] { 0, 0, 0, 0, 0.5, 2, 5, 8, 9.5, 10, 10, 10, 9.5, 8, 6, 4, 2, 0.5, 0, 0, 0, 0, 0, 0 },
			new double[] { 0, 0, 0, 0, 0.3, 1, 3, 6, 8, 9, 9, 9, 9, 8.5, 7, 5, 3, 1.5, 0.5, 0, 0, 0, 0, 0 },
			new double[] { 0, 0, 0, 0, 0.3, 1, 3, 5, 6, 6, 6.5, 7, 7.5, 8, 8, 7, 5, 2, 0.5, 0, 0, 0, 0, 0 },
		};


		/// <summary>What the counters see on Pennsylvania's central rural collectors (PennDOT TPG 9), percent of the day from midnight: the shape to match.</summary>
		internal static readonly double[] HoursCounted =
		{
			0.45, 0.28, 0.28, 0.48, 1.08, 2.79, 4.66, 5.99, 5.45, 5.33, 5.56, 6.09, 6.42, 6.38, 7.06, 8.21, 8.77, 7.68, 5.50, 4.02, 3.14, 2.12, 1.39, 0.87,
		};

		/// <summary>Vehicle trips by hour on a Saturday (FHWA NT-3, 2001 NHTS), percent of the day from midnight: a weekend day's shape.</summary>
		internal static readonly double[] HoursSaturday =
		{
			0.8, 0.3, 0.2, 0.1, 0.3, 0.5, 1.1, 2.2, 3.9, 5.6, 7.7, 8.3, 8.6, 7.7, 8.0, 7.6, 7.2, 6.7, 6.8, 5.0, 4.2, 3.6, 2.0, 1.5,
		};

		/// <summary>
		/// Cars circling the trunk all day (through traffic: the wrap makes the trunk a loop). A lap takes about 500 s, so each passes a
		/// point about five times a day. 10 of them made the trunk's count about the 5,400 a day Pennsylvania's principal arterials carry,
		/// which the owner found too thin to drive in (2026-11-24): 100 (about 18,000 a day more) keep a car in sight along the highway
		/// most of the time. Their one flaw: the day repeats at 03:00, and there each jumps from where the day ends it to where the day
		/// began it.
		/// </summary>
		private const int Circling = 100;
		/// <summary>trafficview only: another number of circling cars to measure (0: <see cref="Circling"/>).</summary>
		internal static int CirclingTry { get; set; }

		/// <summary>Straight-line distance at which a place draws a quarter as strongly as one next door... half: weight / (1 + (d / Reach)²).</summary>
		private const double Reach = 3000.0;

		// ---- what a day holds ----------------------------------------------------------------------------------------------------

		/// <summary>
		/// A stretch of a leg's path: along a graph edge (<see cref="Edge"/> ≥ 0) from road distance <see cref="S0"/> the way
		/// <see cref="Dir"/>, or inside a tunnel (<see cref="Edge"/> -1), straight from (X0, Z0) along (Dx, Dz). <see cref="P0"/> is the
		/// path distance where it starts.
		/// </summary>
		internal struct Seg
		{
			public int Edge;
			public int Dir;
			public double S0;
			public double Len;
			public double P0;
			/// <summary>Its free speed: the edge's limit (in a tunnel, the road's).</summary>
			public double Speed;
			/// <summary>Tunnel: which portal, and whether the car is coming out of it into the world.</summary>
			public int Portal;
			public bool Inbound;
			public double X0, Z0, Y0, Dx, Dz;
		}

		/// <summary>One drive from one place to another: its path, when it ran and where the car was every half second.</summary>
		internal sealed class Leg
		{
			public int Car;
			public Traffic.Purpose Purpose;
			/// <summary>True for the drive home (or a through car's journey).</summary>
			public bool Return;
			public Seg[] Path;
			public double Length;
			/// <summary>Starts out of a tunnel at speed (not from a parking spot), ends into one (not parked).</summary>
			public bool Appears, Vanishes;
			/// <summary>Where the car parks at each end (meaningless at a tunnel end).</summary>
			public Traffic.Anchor From, To;
			/// <summary>The place (index in <see cref="Traffic.Destinations"/>) at the far end, -1 for a home.</summary>
			public int Dest = -1;
			/// <summary>When it could leave, when it did and when it ended (sim time); -1 if it never ran.</summary>
			public double Ready, Start = -1.0, End = -1.0;
			/// <summary>Distance along the path every <see cref="RecordStep"/> from <see cref="Start"/>; at <see cref="End"/> it is <see cref="Length"/>.</summary>
			public float[] Rec;
			/// <summary>
			/// Its lane changes on the trunk, by path distance, three numbers each: where it starts moving over, where it is in the new lane,
			/// and that lane (1 the inner, 0 the outer); null if it kept to the outer lane. Read by <see cref="InnerAt"/>.
			/// </summary>
			public float[] Lanes;
			/// <summary>
			/// The lane its car keeps to as a physics car (-1: none): the one it was in when it became one, whatever the day did after (it
			/// drives among cars the day doesn't know of). From <see cref="HoldEnd"/> on (given back), over <see cref="HoldGlide"/>, back to
			/// the day's lane. Set and cleared on the main thread only.
			/// </summary>
			internal float HoldLane = -1f;
			internal double HoldEnd = double.MaxValue;
			internal double Dwell;
			internal int Host = -1;
			internal float[] Profile;
			/// <summary>Its start has been moved alongside the space the car stands in (once, as it first waits to set off).</summary>
			internal bool Placed;
			/// <summary>The space it sets off from (ends in) is across the road, left of its way: it crosses the oncoming lane pulling out (in).</summary>
			internal bool FarSide, FarEnd;

			/// <summary>Seconds it stands on the lane beside its space before it sets off (pulling out) and after it stops (pulling in).</summary>
			internal double PullOut, PullIn;
		}

		/// <summary>A car: a household's (parked at its home between trips) or a through car's.</summary>
		internal sealed class Car
		{
			public int Household = -1;
			public Traffic.Anchor Home;
			/// <summary>Its legs (indices in <see cref="Legs"/>) in the order they ran.</summary>
			public readonly List<int> Legs = new List<int>();
			internal bool Out;
			internal double FreeAt;
		}

		internal enum State
		{
			/// <summary>Parked at a spot (X, Z).</summary>
			Parked,
			/// <summary>On the road (or in a tunnel's mouth): place, heading and speed.</summary>
			Driving,
			/// <summary>Off the map (gone down a road out).</summary>
			Away,
		}

		/// <summary>Where a car is at a moment.</summary>
		internal struct Place
		{
			public State State;
			public double X, Z, Y;
			/// <summary>The way it faces (unit) and its speed, when driving.</summary>
			public double Hx, Hz, Speed;
			/// <summary>When driving: the edge (-1 in a tunnel), the way along it and the road distance.</summary>
			public int Edge, Dir;
			public double S;
			public bool InTunnel;
			/// <summary>When driving, the leg; when parked, the leg that brought it there (-1: at home before its first trip).</summary>
			public int Leg;
			/// <summary>When driving: the distance along the leg's path (0 to its <see cref="Leg.Length"/>).</summary>
			public double P;
			/// <summary>Driving: seconds since its leg set off.</summary>
			public double T;
		}

		internal readonly int Seed;
		/// <summary>Where every parked car stands: chosen as the day runs, each trip starting and ending on the lane beside its space.</summary>
		internal readonly TrafficParking Parking;
		internal readonly bool Weekend;
		internal readonly List<Car> Cars = new List<Car>();
		internal readonly List<Leg> Legs = new List<Leg>();

		// What it took, for the checks and the log.
		internal double BuildMs, PlanMs, ParkingMs;
		internal int Candidates, Kept, Dropped, Through, Unrouted;
		internal double Keep, TargetSeconds, CandidateSeconds;
		internal int Forced, Overlaps, BoxConflicts, Speeding, StoppedAtMouth, PullOutsForced, Unfinished;
		internal double MinGapSeen = double.MaxValue, MaxOver;
		internal int[] TourCount = new int[4];
		internal readonly List<string> Stuck = new List<string>(), OverlapNotes = new List<string>(), ConflictNotes = new List<string>();
		/// <summary>Diagnostics: seconds cars stood waiting at each node, and why requests were refused (behind a refused one, crossing a grant, no room).</summary>
		internal readonly Dictionary<int, double> WaitAt = new Dictionary<int, double>();
		/// <summary>Seconds waited per node and kind (node * 4 + kind: 0 from a side arm, 1 on along the main line, 2 turning off it, 3 no main line). For trafficview.</summary>
		internal readonly Dictionary<int, double> WaitKind = new Dictionary<int, double>();
		internal int RefusedOrder, RefusedCross, RefusedRoom;
		/// <summary>Requests refused for a gap: a car giving way to a main-line car coming.</summary>
		internal int RefusedGap;
		internal readonly List<string> MainHeldNotes = new List<string>();
		/// <summary>Main-line cars (or others with the right of way) refused at a junction for a car that gives way to them, once per car and junction: should be none.</summary>
		internal int MainHeld => mainHeld.Count;
		private readonly HashSet<long> mainHeld = new HashSet<long>();
		internal double ProfileSeconds;
		internal readonly Dictionary<int, double> StoppedOn = new Dictionary<int, double>();
		internal readonly int[] ActiveBy = new int[48], StartsBy = new int[48];

		private readonly ulong salt;

		/// <summary>Builds race <paramref name="raceSeed"/>'s day (seconds of work: do it on a worker).</summary>
		internal TrafficDay(int raceSeed)
		{
			Traffic.Ensure();
			Prepare();
			Seed = raceSeed;
			salt = Noise.Mix((ulong)(uint)raceSeed ^ 0x7AF_F1C0_D4E5UL);
			Weekend = Plots.Unit(salt, 1) < 2.0 / 7.0;
			System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
			routeCost = new double[Traffic.Nodes.Count];
			routeVia = new int[Traffic.Nodes.Count];
			for (int i = 0; i < routeCost.Length; i++)
			{
				routeCost[i] = double.MaxValue;
				routeVia[i] = -1;
			}
			Plan();
			PlanMs = watch.Elapsed.TotalMilliseconds;
			Parking = new TrafficParking(this);
			ParkingMs = watch.Elapsed.TotalMilliseconds - PlanMs;
			Simulate();
			BuildMs = watch.Elapsed.TotalMilliseconds;
		}

		/// <summary>Sim time (0 at 03:00) of race time <paramref name="raceTime"/> in a race that started at world hour <paramref name="startHour"/>.</summary>
		internal static double SimTime(double startHour, double raceTime)
		{
			double w = (startHour - 3.0) * 100.0 + raceTime;
			return w - Math.Floor(w / Traffic.DaySeconds) * Traffic.DaySeconds;
		}

		/// <summary>Sim time of world hour <paramref name="hour"/> (0 to 24).</summary>
		internal static double SimOfHour(double hour)
		{
			double w = (hour - 3.0) * 100.0;
			return w - Math.Floor(w / Traffic.DaySeconds) * Traffic.DaySeconds;
		}

		// ---- fixed with the world: bends and junction arms ---------------------------------------------------------------------

		private static readonly object gate = new object();
		private static bool prepared;
		/// <summary>Per road, per sample: the speed its bend allows there.</summary>
		private static double[][] bend;
		/// <summary>Per edge, whether it is the trunk's (two lanes each way); per portal, whether its road is.</summary>
		private static bool[] twoLaneEdge, twoLanePortal;
		/// <summary>Per node, per link (as <see cref="Traffic.Links"/>): its place round the junction (0 the first by angle) and the way out along it (unit).</summary>
		private static int[][] armRank;
		private static double[][] armX, armZ;
		/// <summary>Per portal, the place at it (index in <see cref="Traffic.Destinations"/>), and the road north's portal (-1 if none).</summary>
		private static int[] portalDest;
		private static int north = -1;

		private static void Prepare()
		{
			lock (gate)
			{
				if (prepared)
				{
					return;
				}
				bend = new double[Network.Roads.Count][];
				for (int r = 0; r < Network.Roads.Count; r++)
				{
					Network.Road road = Network.Roads[r];
					if (Traffic.RoadEdges[r].Count == 0)
					{
						continue;
					}
					double[] b = new double[road.Count];
					double end = road.S[road.Count - 1];
					for (int i = 0; i < road.Count; i++)
					{
						double s = road.S[i];
						Traffic.At(road, Math.Max(0.0, s - 40.0), out double x1, out double z1, out double _);
						Traffic.At(road, s, out double x2, out double z2, out double _);
						Traffic.At(road, Math.Min(end, s + 40.0), out double x3, out double z3, out double _);
						double a = Dist(x1, z1, x2, z2), c = Dist(x2, z2, x3, z3), d = Dist(x1, z1, x3, z3);
						double area2 = Math.Abs((x2 - x1) * (z3 - z1) - (z2 - z1) * (x3 - x1));
						b[i] = area2 < 1e-6 ? 1e9 : Math.Sqrt(Lateral * a * c * d / (2.0 * area2));
					}
					bend[r] = b;
				}
				int n = Traffic.Nodes.Count;
				armRank = new int[n][];
				armX = new double[n][];
				armZ = new double[n][];
				for (int node = 0; node < n; node++)
				{
					List<int> links = Traffic.Links[node];
					int m = links.Count;
					double[] ax = new double[m], az = new double[m], key = new double[m];
					for (int k = 0; k < m; k++)
					{
						Traffic.Edge e = Traffic.Edges[links[k]];
						Network.Road road = Network.Roads[e.Road];
						double into = Math.Min(15.0, e.Length * 0.5);
						Traffic.At(road, e.A == node ? e.Sa + into : e.Sb - into, out double x, out double z, out double _);
						double dx = x - Traffic.Nodes[node].X, dz = z - Traffic.Nodes[node].Z, len = Math.Sqrt(dx * dx + dz * dz);
						if (len < 1e-9)
						{
							Traffic.Heading(road, e.A == node ? e.Sa : e.Sb, out dx, out dz);
							if (e.A != node)
							{
								dx = -dx;
								dz = -dz;
							}
							len = 1.0;
						}
						ax[k] = dx / len;
						az[k] = dz / len;
						key[k] = Pseudo(ax[k], az[k]);
					}
					int[] rank = new int[m];
					for (int k = 0; k < m; k++)
					{
						for (int j = 0; j < m; j++)
						{
							if (key[j] < key[k] || (key[j] == key[k] && j < k))
							{
								rank[k]++;
							}
						}
					}
					armRank[node] = rank;
					armX[node] = ax;
					armZ[node] = az;
				}
				twoLaneEdge = new bool[Traffic.Edges.Count];
				for (int e = 0; e < Traffic.Edges.Count; e++)
				{
					twoLaneEdge[e] = Network.Roads[Traffic.Edges[e].Road].Kind == RoadKind.Trunk;
				}
				twoLanePortal = new bool[Network.Portals.Count];
				for (int q = 0; q < Network.Portals.Count; q++)
				{
					twoLanePortal[q] = Network.Roads[Network.Portals[q].Road].Kind == RoadKind.Trunk;
				}
				mainA = new int[n];
				mainB = new int[n];
				twoLaneMain = new bool[n];
				for (int node = 0; node < n; node++)
				{
					MainLine(node);
					twoLaneMain[node] = mainA[node] >= 0 && twoLaneEdge[Traffic.Links[node][mainA[node]]] && twoLaneEdge[Traffic.Links[node][mainB[node]]];
				}
				portalDest = new int[Network.Portals.Count];
				north = -1;
				for (int p = 0; p < Network.Portals.Count; p++)
				{
					portalDest[p] = -1;
					for (int d = 0; d < Traffic.Destinations.Count; d++)
					{
						Traffic.Destination dest = Traffic.Destinations[d];
						if (dest.Kind == Traffic.DestinationKind.Edge && dest.At.X == Network.Portals[p].X && dest.At.Z == Network.Portals[p].Z)
						{
							portalDest[p] = d;
						}
					}
					if (!Wrap.Open(p) && portalDest[p] >= 0 && north < 0)
					{
						north = p;
					}
				}
				prepared = true;
			}
		}

		private static double Dist(double ax, double az, double bx, double bz)
		{
			return Math.Sqrt((bx - ax) * (bx - ax) + (bz - az) * (bz - az));
		}

		/// <summary>A number that grows with the angle of (x, z) from +x towards +z, 0 to 4: orders directions with + - * / only.</summary>
		private static double Pseudo(double x, double z)
		{
			double a = Math.Abs(x) + Math.Abs(z);
			if (a < 1e-12)
			{
				return 0.0;
			}
			double r = z / a;
			if (x >= 0.0)
			{
				return z >= 0.0 ? r : 4.0 + r;
			}
			return 2.0 - r;
		}

		/// <summary>The speed road <paramref name="r"/>'s bend allows at distance <paramref name="s"/>.</summary>
		internal static double BendSpeed(int r, double s)
		{
			Network.Locate(Network.Roads[r], s, out int j, out double _);
			return Math.Min(bend[r][j], bend[r][j + 1]);
		}

		private static double EdgeSpeed(int e)
		{
			Traffic.Edge edge = Traffic.Edges[e];
			return edge.Length / Math.Max(1e-6, edge.Time);
		}

		// ---- paths ---------------------------------------------------------------------------------------------------------------

		private readonly Heap routeHeap = new Heap();
		private readonly double[] routeCost;
		private readonly int[] routeVia;
		private readonly List<int> routeTouched = new List<int>();
		private readonly List<(int edge, int dir)> route = new List<(int edge, int dir)>();

		/// <summary>The road point of an anchor (its line, not its parking spot).</summary>
		private static void Point(Traffic.Anchor a, out double x, out double z, out double y)
		{
			Traffic.At(Network.Roads[Traffic.Edges[a.Edge].Road], a.S, out x, out z, out y);
		}

		/// <summary>
		/// Appends the way from anchor <paramref name="a"/> to anchor <paramref name="b"/> to <paramref name="segs"/>: out of portal
		/// <paramref name="pa"/>'s tunnel first if it is one (from <see cref="Wrap.Depth"/> inside it), into portal <paramref name="pb"/>'s
		/// after if it is one (to <see cref="Wrap.Depth"/> inside). False if there is no route.
		/// </summary>
		private bool Way(Traffic.Anchor a, int pa, Traffic.Anchor b, int pb, List<Seg> segs)
		{
			a = OffJunctions(a);
			b = OffJunctions(b);
			if (!Traffic.Route(a, b, route, routeHeap, routeCost, routeVia, routeTouched))
			{
				return false;
			}
			if (pa >= 0)
			{
				segs.Add(Tunnel(a, pa, true));
			}
			for (int k = 0; k < route.Count; k++)
			{
				(int e, int dir) = route[k];
				Traffic.Edge edge = Traffic.Edges[e];
				double s0 = k == 0 ? a.S : dir > 0 ? edge.Sa : edge.Sb;
				double s1 = k == route.Count - 1 ? b.S : dir > 0 ? edge.Sb : edge.Sa;
				double len = (s1 - s0) * dir;
				if (len < 0.0)
				{
					len = 0.0;
				}
				segs.Add(new Seg { Edge = e, Dir = dir, S0 = s0, Len = len, Speed = EdgeSpeed(e), Portal = -1 });
			}
			if (pb >= 0)
			{
				segs.Add(Tunnel(b, pb, false));
			}
			return true;
		}

		/// <summary>
		/// An anchor moved along its stretch of road out of the junction boxes at its ends (a box and a car's length clear of each, or the
		/// middle of a short stretch): a car pulling out or stopping inside a junction would block it. Only the spot on the road moves;
		/// the parking spot beside it stays.
		/// </summary>
		private static Traffic.Anchor OffJunctions(Traffic.Anchor a)
		{
			Traffic.Edge e = Traffic.Edges[a.Edge];
			double lo = e.Sa + (Traffic.Links[e.A].Count > 1 ? Traffic.Nodes[e.A].Box + Traffic.CarLength : 0.0);
			double hi = e.Sb - (Traffic.Links[e.B].Count > 1 ? Traffic.Nodes[e.B].Box + Traffic.CarLength : 0.0);
			if (lo > hi)
			{
				a.S = (e.Sa + e.Sb) * 0.5;
			}
			else
			{
				a.S = Math.Max(lo, Math.Min(hi, a.S));
			}
			return a;
		}

		/// <summary>
		/// Moves a leg's start (<paramref name="start"/>) or end along its stretch of road to alongside parking spot
		/// <paramref name="spot"/>, kept out of the junction boxes: the car sets off from (or stops at) the lane beside its space, so it is
		/// drawn pulling out (or in) sideways where the day has it, never on lane behind (or ahead of) it that the day thinks free. Left as
		/// it is if that would turn the stretch round (a trip along one stretch whose space lies past its other end).
		/// </summary>
		private static void MoveEnd(Leg leg, bool start, TrafficParking.Spot spot)
		{
			int k = start ? 0 : leg.Path.Length - 1;
			Seg seg = leg.Path[k];
			if (seg.Edge < 0)
			{
				return;
			}
			Traffic.Edge edge = Traffic.Edges[seg.Edge];
			Network.Road road = Network.Roads[edge.Road];
			double s = seg.Dir > 0 ? seg.S0 + seg.Len : seg.S0 - seg.Len;
			if (start)
			{
				s = seg.S0;
			}
			// Two steps of Newton's: the spot is beside the road, a little way along it.
			for (int it = 0; it < 2; it++)
			{
				Traffic.At(road, s, out double x, out double z, out double _);
				Traffic.Heading(road, s, out double tx, out double tz);
				s += (spot.X - x) * tx + (spot.Z - z) * tz;
				s = OffJunctions(new Traffic.Anchor { Edge = seg.Edge, S = s }).S;
			}
			double end = seg.Dir > 0 ? seg.S0 + seg.Len : seg.S0 - seg.Len;
			double len = start ? (end - s) * seg.Dir : (s - seg.S0) * seg.Dir;
			if (len < 0.0)
			{
				return;
			}
			if (start)
			{
				seg.S0 = s;
			}
			seg.Len = len;
			leg.Path[k] = seg;
			double p = 0.0;
			for (int j = 0; j < leg.Path.Length; j++)
			{
				leg.Path[j].P0 = p;
				p += leg.Path[j].Len;
			}
			leg.Length = p;
		}

		/// <summary>The stretch inside portal <paramref name="p"/>'s tunnel between the glue plane and anchor <paramref name="a"/>, coming out or going in.</summary>
		private static Seg Tunnel(Traffic.Anchor a, int p, bool inbound)
		{
			Network.Portal portal = Network.Portals[p];
			Point(a, out double x, out double z, out double y);
			double along = (x - portal.X) * portal.DirX + (z - portal.Z) * portal.DirZ;
			double len = Math.Max(1.0, Wrap.Depth - along);
			Seg seg = new Seg { Edge = -1, Dir = 1, Len = len, Speed = EdgeSpeed(a.Edge), Portal = p, Inbound = inbound, Y0 = y };
			if (inbound)
			{
				seg.X0 = x + portal.DirX * len;
				seg.Z0 = z + portal.DirZ * len;
				seg.Dx = -portal.DirX;
				seg.Dz = -portal.DirZ;
			}
			else
			{
				seg.X0 = x;
				seg.Z0 = z;
				seg.Dx = portal.DirX;
				seg.Dz = portal.DirZ;
			}
			return seg;
		}

		private static Seg[] Finish(List<Seg> segs, out double length)
		{
			Seg[] path = segs.ToArray();
			double p = 0.0;
			for (int k = 0; k < path.Length; k++)
			{
				path[k].P0 = p;
				p += path[k].Len;
			}
			length = p;
			return path;
		}

		/// <summary>
		/// What driving a path takes as a driver would: at its speed profile (limits, bends, turns, the stop), and a quarter more for pulling
		/// away, the cars in front and the junctions (measured in trafficview). The survey's 59 minutes a driver are real driving, so the
		/// day is thinned on this, not on free-flow time.
		/// </summary>
		private static double Expected(Seg[] path, double length, bool vanishes)
		{
			float[] v = Profile(new Leg { Path = path, Length = length, Vanishes = vanishes });
			double t = 0.0;
			for (int i = 0; i + 1 < v.Length; i++)
			{
				double step = Math.Min(Sample, Math.Max(0.0, length - i * Sample));
				t += step / Math.Max(1.0, Math.Min(v[i], v[i + 1]));
			}
			return t * Overhead;
		}

		/// <summary>Driving time over the profile's: pulling away, following and waiting at junctions.</summary>
		private const double Overhead = 1.25;

		/// <summary>Free-flow seconds to drive a path.</summary>
		private static double FreeTime(Seg[] path)
		{
			double t = 0.0;
			foreach (Seg s in path)
			{
				t += s.Len / Math.Max(1.0, s.Speed);
			}
			return t;
		}

		private static Traffic.Anchor PortalAnchor(int p)
		{
			return Traffic.Destinations[portalDest[p]].At;
		}

		/// <summary>
		/// The path to or from place <paramref name="dest"/> at a road out (<paramref name="portal"/>) from or to anchor
		/// <paramref name="home"/>. Through one of the trunk's tunnels it carries on through the wrap to the road north (out) or comes from
		/// it (back), so a car into one tunnel always comes out of the other.
		/// </summary>
		private bool EdgeWay(Traffic.Anchor home, int portal, bool back, List<Seg> segs)
		{
			Traffic.Anchor at = PortalAnchor(portal);
			if (Wrap.Open(portal) && north >= 0)
			{
				int other = Wrap.Other(portal);
				Traffic.Anchor oa = PortalAnchor(other), na = PortalAnchor(north);
				if (!back)
				{
					return Way(home, -1, at, portal, segs) && Way(oa, other, na, north, segs);
				}
				return Way(na, north, oa, other, segs) && Way(at, portal, home, -1, segs);
			}
			return back ? Way(at, portal, home, -1, segs) : Way(home, -1, at, portal, segs);
		}

		// ---- the plan: tours, thinned ----------------------------------------------------------------------------------------------

		private sealed class Tour
		{
			public int Household;
			public Traffic.Purpose Purpose;
			public double Depart, Dwell;
			public int Dest = -1, Host = -1, Portal = -1;
			public Traffic.Anchor To;
			public Seg[] Out;
			public double OutLength;
		}

		/// <summary>Per household, its kept tours in the order they leave.</summary>
		private List<Tour>[] tours;
		/// <summary>Legs waiting to be driven (made, not yet started), and through journeys by when they leave.</summary>
		private readonly List<int> waiting = new List<int>();
		private readonly List<int> throughs = new List<int>();

		private double U(ulong h, int k)
		{
			return Plots.Unit(h, k);
		}

		/// <summary>Sim time of a leaving time drawn from an hour table: the hour by its weight, the minute uniform.</summary>
		private static double DrawHour(double[] weights, double u, double v)
		{
			double sum = 0.0;
			foreach (double w in weights)
			{
				sum += w;
			}
			double pick = u * sum, acc = 0.0;
			int hour = 23;
			for (int h = 0; h < 24; h++)
			{
				acc += weights[h];
				if (pick < acc)
				{
					hour = h;
					break;
				}
			}
			return ((hour - 3 + 24) % 24) * 100.0 + v * 100.0;
		}

		private static double Gravity(double weight, double x0, double z0, double x1, double z1)
		{
			double d2 = ((x1 - x0) * (x1 - x0) + (z1 - z0) * (z1 - z0)) / (Reach * Reach);
			return weight / (1.0 + d2);
		}

		private static double Draw(Traffic.Destination d, Traffic.Purpose p)
		{
			switch (p)
			{
				case Traffic.Purpose.Work: return d.Work;
				case Traffic.Purpose.Shop: return d.Shop;
				case Traffic.Purpose.Errand: return d.Errand;
				default: return d.Social;
			}
		}

		/// <summary>
		/// Whether place <paramref name="d"/> can be driven to: every place but the trunk's ends when the trunk wraps round and the road
		/// north leaves the map. Driving out of one of the trunk's tunnels brings a car back in at the other, so the road north is the
		/// only way off the map, and work, shops and friends beyond it are reached that way.
		/// </summary>
		private static bool Beyond(int d)
		{
			if (Traffic.Destinations[d].Kind != Traffic.DestinationKind.Edge || north < 0)
			{
				return true;
			}
			int p = Array.IndexOf(portalDest, d);
			return !Wrap.Open(p);
		}

		/// <summary>
		/// How much more place <paramref name="d"/> draws than its own figures say: the road north takes the draw of the trunk's ends too
		/// when they lead back into the map (<see cref="Beyond"/>), so the share of work, shops and friends beyond the map stays the same.
		/// </summary>
		private static double PullOf(int d)
		{
			if (north < 0 || d != portalDest[north])
			{
				return 1.0;
			}
			double own = 0.0, all = 0.0;
			for (int p = 0; p < portalDest.Length; p++)
			{
				if (portalDest[p] < 0)
				{
					continue;
				}
				Traffic.Destination dest = Traffic.Destinations[portalDest[p]];
				double w = dest.Work + dest.Shop + dest.Errand + dest.Social;
				all += w;
				own += p == north ? w : 0.0;
			}
			return own > 0.0 ? all / own : 1.0;
		}

		/// <summary>The day's tours: candidates from the rates, each kept with the one probability that gives the survey's driving time.</summary>
		private void Plan()
		{
			List<Traffic.Household> hh = Traffic.Households;
			List<Tour> candidates = new List<Tour>();
			double[] rates = Weekend ? RateWeekend : RateWeekday;
			double[][] hours = Weekend ? HoursWeekend : HoursWeekday;
			int withCars = 0;
			double[] weight = new double[Traffic.Destinations.Count + 8];
			List<Seg> segs = new List<Seg>();
			for (int h = 0; h < hh.Count; h++)
			{
				Traffic.Household home = hh[h];
				if (home.Cars == 0)
				{
					continue;
				}
				withCars++;
				ulong hs = Noise.Mix(salt ^ home.Hash);
				int k = 0;
				for (int purpose = 0; purpose < 4; purpose++)
				{
					int n = purpose == 0 ? home.Workers : 1;
					for (int i = 0; i < n; i++, k += 16)
					{
						if (U(hs, k) >= rates[purpose])
						{
							continue;
						}
						Tour t = new Tour { Household = h, Purpose = (Traffic.Purpose)purpose };
						t.Depart = DrawHour(hours[purpose], U(hs, k + 1), U(hs, k + 2));
						if (t.Depart >= LastOut)
						{
							continue;
						}
						double a = U(hs, k + 3), b = U(hs, k + 4), c = U(hs, k + 5);
						switch (t.Purpose)
						{
							case Traffic.Purpose.Work: t.Dwell = (8.5 + 2.0 * (a + b + c - 1.5)) * 100.0; break;
							case Traffic.Purpose.Shop: t.Dwell = (0.3 + 0.7 * a) * 100.0; break;
							case Traffic.Purpose.Errand: t.Dwell = (0.15 + 0.35 * a) * 100.0; break;
							default: t.Dwell = (0.75 + 1.25 * a) * 100.0; break;
						}
						// Where: every place by its draw for the purpose over distance; a visit also among eight households drawn at random.
						double sum = 0.0;
						for (int d = 0; d < Traffic.Destinations.Count; d++)
						{
							Traffic.Destination dest = Traffic.Destinations[d];
							if (!Beyond(d))
							{
								weight[d] = 0.0;
								continue;
							}
							weight[d] = Gravity(Draw(dest, t.Purpose) * PullOf(d), home.Home.X, home.Home.Z, dest.At.X, dest.At.Z);
							sum += weight[d];
						}
						int[] hosts = new int[8];
						for (int j = 0; j < 8; j++)
						{
							hosts[j] = -1;
							weight[Traffic.Destinations.Count + j] = 0.0;
							if (t.Purpose != Traffic.Purpose.Social)
							{
								continue;
							}
							int other = (int)(U(hs, k + 6 + j) * hh.Count);
							if (other == h || other >= hh.Count)
							{
								continue;
							}
							hosts[j] = other;
							weight[Traffic.Destinations.Count + j] = Gravity(3.0, home.Home.X, home.Home.Z, hh[other].Home.X, hh[other].Home.Z);
							sum += weight[Traffic.Destinations.Count + j];
						}
						if (sum <= 0.0)
						{
							continue;
						}
						double pick = U(hs, k + 15) * sum, acc = 0.0;
						int chosen = -1;
						for (int d = 0; d < weight.Length; d++)
						{
							acc += weight[d];
							if (weight[d] > 0.0 && pick < acc)
							{
								chosen = d;
								break;
							}
						}
						if (chosen < 0)
						{
							continue;
						}
						if (chosen >= Traffic.Destinations.Count)
						{
							t.Host = hosts[chosen - Traffic.Destinations.Count];
							t.To = hh[t.Host].Home;
						}
						else
						{
							t.Dest = chosen;
							t.To = Traffic.Destinations[chosen].At;
							if (Traffic.Destinations[chosen].Kind == Traffic.DestinationKind.Edge)
							{
								t.Portal = Array.IndexOf(portalDest, chosen);
							}
						}
						segs.Clear();
						bool ok = t.Portal >= 0 ? EdgeWay(home.Home, t.Portal, false, segs) : Way(home.Home, -1, t.To, -1, segs);
						if (!ok)
						{
							Unrouted++;
							continue;
						}
						t.Out = Finish(segs, out t.OutLength);
						if (t.OutLength < 30.0)
						{
							continue;
						}
						candidates.Add(t);
					}
				}
			}
			// Thinning: the share of the day households spend driving (NHTS), counted free-flow and both ways.
			Candidates = candidates.Count;
			CandidateSeconds = 0.0;
			foreach (Tour t in candidates)
			{
				CandidateSeconds += 2.0 * Expected(t.Out, t.OutLength, t.Portal >= 0);
			}
			TargetSeconds = Traffic.DrivingShare * Traffic.DaySeconds * withCars;
			Keep = Math.Min(1.0, TargetSeconds / Math.Max(1.0, CandidateSeconds));
			tours = new List<Tour>[hh.Count];
			for (int h = 0; h < hh.Count; h++)
			{
				tours[h] = new List<Tour>();
			}
			for (int i = 0; i < candidates.Count; i++)
			{
				Tour t = candidates[i];
				if (U(Noise.Mix(salt ^ hh[t.Household].Hash ^ 0x5EED_0000UL), i) < Keep)
				{
					tours[t.Household].Add(t);
					Kept++;
					TourCount[(int)t.Purpose]++;
				}
			}
			for (int h = 0; h < hh.Count; h++)
			{
				tours[h].Sort((a, b) => a.Depart != b.Depart ? a.Depart.CompareTo(b.Depart) : ((int)a.Purpose).CompareTo((int)b.Purpose));
			}
			// The cars: each household's, then the through cars.
			householdCars = new int[hh.Count];
			for (int h = 0; h < hh.Count; h++)
			{
				householdCars[h] = Cars.Count;
				for (int c = 0; c < hh[h].Cars; c++)
				{
					Cars.Add(new Car { Household = h, Home = hh[h].Home });
				}
			}
			// Through traffic: cars circling the trunk all day, into one tunnel and out of the other (the wrap joins the trunk into a
			// loop, so traffic passing through it never leaves). Half each way, spread along it at 03:00, driven past the day's end.
			if (Wrap.A >= 0 && portalDest[Wrap.A] >= 0 && portalDest[Wrap.B] >= 0)
			{
				List<int> trunk = new List<int>();
				for (int e = 0; e < Traffic.Edges.Count; e++)
				{
					if (Network.Roads[Traffic.Edges[e].Road].Kind == RoadKind.Trunk && Traffic.OnMain(e) && Traffic.Edges[e].Length > 200.0)
					{
						trunk.Add(e);
					}
				}
				// Spread evenly by distance along the trunk's long stretches (so many cars never start on one spot), each way in turn.
				double trunkLength = 0.0;
				foreach (int te in trunk)
				{
					trunkLength += Traffic.Edges[te].Length;
				}
				int ti = 0;
				double before = 0.0;
				int circling = CirclingTry > 0 ? CirclingTry : Circling;
				for (int i = 0; i < circling && trunk.Count > 0; i++)
				{
					int into = i % 2 == 0 ? Wrap.A : Wrap.B, other = Wrap.Other(into);
					double want = (i + 0.5) * trunkLength / circling;
					while (ti + 1 < trunk.Count && before + Traffic.Edges[trunk[ti]].Length <= want)
					{
						before += Traffic.Edges[trunk[ti]].Length;
						ti++;
					}
					Traffic.Edge e = Traffic.Edges[trunk[ti]];
					double u = (want - before) / Math.Max(1.0, e.Length);
					u = Math.Max(0.0, Math.Min(1.0, u));
					Traffic.Anchor start = OffJunctions(new Traffic.Anchor { Edge = trunk[ti], S = e.Sa + (e.Sb - e.Sa) * u, Side = 1 });
					segs.Clear();
					bool ok = Way(start, -1, PortalAnchor(into), into, segs);
					double lap = 0.0;
					List<Seg> loop = new List<Seg>();
					ok = ok && Way(PortalAnchor(other), other, PortalAnchor(into), into, loop);
					foreach (Seg sg in loop)
					{
						lap += sg.Len / Math.Max(1.0, sg.Speed);
					}
					if (!ok || lap <= 0.0)
					{
						Unrouted++;
						continue;
					}
					// Enough laps to outlast the day at free-flow speed, and one more.
					for (int k = (int)Math.Ceiling((Traffic.DaySeconds + 100.0) / lap) + 1; k > 0; k--)
					{
						segs.AddRange(loop);
					}
					Point(start, out double sx, out double sz, out double _);
					Car car = new Car { Household = -1, Home = new Traffic.Anchor { Edge = start.Edge, S = start.S, X = sx, Z = sz }, Out = true };
					Cars.Add(car);
					Leg leg = new Leg { Car = Cars.Count - 1, Purpose = Traffic.Purpose.Through, Return = true, Appears = true, Vanishes = true, Ready = 0.0 };
					leg.Path = Finish(segs, out leg.Length);
					leg.From = car.Home;
					leg.To = PortalAnchor(into);
					Legs.Add(leg);
					throughs.Add(Legs.Count - 1);
					Through++;
				}
			}
		}

		private int[] householdCars;

		// ---- the simulation --------------------------------------------------------------------------------------------------------

		private sealed class Mover
		{
			public int Index;
			public Leg L;
			public double P, V;
			public int Seg;
			public int StartStep;
			public double Wait;
			/// <summary>When it began waiting at its stop to cross the oncoming lane into its space (MaxValue: not waiting).</summary>
			public double HeldSince = double.MaxValue;
			/// <summary>It stands still on its lane until then, drawn pulling out of its space; and parks at <see cref="ParkAt"/>, drawn pulling in.</summary>
			public double HoldUntil = -1.0, ParkAt = double.MaxValue;
			public readonly List<float> Rec = new List<float>();
			public readonly List<Grant> Grants = new List<Grant>();
			public long Lane;
			public double LaneU;
			/// <summary>The car just ahead on the same lane this step (null if none).</summary>
			public Mover Ahead;
			/// <summary>
			/// Which of the trunk's lanes it is in (0 the outer, and the one lane of every other road; 1 the inner; <see cref="AuxIx"/> a
			/// junction's deceleration or acceleration lane; <see cref="MedianIx"/> a median lane); when it last changed lanes; its changes
			/// (as <see cref="Leg.Lanes"/>).
			/// </summary>
			public int Ix;
			public bool Inner { get => Ix == 1; set => Ix = value ? 1 : 0; }
			/// <summary>In a junction lane it merges from (not the one it turns off from): where along its path that lane ends (MaxValue: none).</summary>
			public double LaneEnd = double.MaxValue;
			public double Changed = -1e9;
			public List<float> LaneRec;
		}

		private sealed class Grant
		{
			public Mover M;
			public int Node, SegIndex;
			public long InLane, OutLane;
			public int InKey, OutKey;
			public double NodeP;
			public bool Forced;
			/// <summary>The speed it will be down to through the box (its turn's): what its clearing is reckoned at.</summary>
			public double Through;
		}

		private struct Request
		{
			public Mover M;
			public int Node, SegIndex, InArm, OutArm;
			public long InLane, OutLane;
			public double Eta, NodeP;
		}

		private readonly List<Mover> movers = new List<Mover>();
		/// <summary>This step's cars waiting to be let in: lane and spot along it.</summary>
		private readonly List<(long lane, double u)> letIn = new List<(long lane, double u)>();
		private readonly Dictionary<long, List<Mover>> lanes = new Dictionary<long, List<Mover>>();
		private readonly List<long> laneKeys = new List<long>();
		private readonly Dictionary<int, List<Grant>> nodeGrants = new Dictionary<int, List<Grant>>();
		private readonly List<int> grantNodes = new List<int>();
		private readonly HashSet<long> overlapPairs = new HashSet<long>(), conflictPairs = new HashSet<long>();

		/// <summary>Added to a lane's key for the trunk's inner lane beside it.</summary>
		private const long InnerKey = 1L << 40;

		/// <summary>The lane mover <paramref name="m"/> is in (or keeps to) on seg <paramref name="s"/>: its trunk lane there if <paramref name="s"/> is on the trunk.</summary>
		private static long LaneOf(Mover m, Seg s)
		{
			return LaneKey(s, m.Ix);
		}

		/// <summary>Lane <paramref name="ix"/> of seg <paramref name="s"/> (0 on a road with one lane each way).</summary>
		private static long LaneKey(Seg s, int ix)
		{
			return LaneOf(s) + (TwoLane(s) ? ix * InnerKey : 0L);
		}

		/// <summary>Lane indexes of the trunk's junction lanes (<see cref="TrunkLanes"/>): its deceleration and acceleration lanes, its median lanes.</summary>
		internal const int AuxIx = 2, MedianIx = 3;

		/// <summary>
		/// Whether two movements through a node, each with the lanes it comes in and leaves by, cross: as <see cref="Crosses"/>, but two
		/// that leave by the same arm in different lanes (one joining the trunk's outer lane, one going on in its inner lane) do not, and
		/// two from the trunk's two lanes of one arm do if they leave by the same lane (both turning off into one road).
		/// </summary>
		private static bool Conflict(int node, int inA, int outA, long inLaneA, long outLaneA, int inB, int outB, long inLaneB, long outLaneB)
		{
			if (inA == inB)
			{
				return inLaneA != inLaneB && outLaneA == outLaneB;
			}
			if (outA == outB && outLaneA != outLaneB)
			{
				return false;
			}
			return Crosses(node, inA, outA, inB, outB);
		}

		private static long LaneOf(Seg s)
		{
			return s.Edge >= 0 ? s.Edge * 2L + (s.Dir > 0 ? 1 : 0) : -1L - (s.Portal * 2L + (s.Inbound ? 1 : 0));
		}

		/// <summary>Distance along a lane (the way it is driven) at path distance p within seg s.</summary>
		private static double LaneU(Seg s, double p)
		{
			if (s.Edge < 0)
			{
				return p - s.P0;
			}
			Traffic.Edge e = Traffic.Edges[s.Edge];
			double road = s.S0 + s.Dir * (p - s.P0);
			return s.Dir > 0 ? road - e.Sa : e.Sb - road;
		}

		/// <summary>The node a path's seg k ends at, or -1 (its last seg, or a tunnel at either side).</summary>
		internal static int NodeAfter(Seg[] path, int k)
		{
			if (k + 1 >= path.Length || path[k].Edge < 0 || path[k + 1].Edge < 0)
			{
				return -1;
			}
			Traffic.Edge e = Traffic.Edges[path[k].Edge];
			return path[k].Dir > 0 ? e.B : e.A;
		}

		internal static int LinkIndex(int node, int edge)
		{
			return Traffic.Links[node].IndexOf(edge);
		}

		/// <summary>
		/// Whether two movements through a node cross: the same lane in never does (the second follows the first), the same lane out always
		/// does (they merge), otherwise their chords round the junction cross. Round a junction each arm has its lane out before its lane
		/// in (right-hand traffic), so the four points' order says it with integers only.
		/// </summary>
		internal static bool Crosses(int node, int inA, int outA, int inB, int outB)
		{
			if (inA == inB)
			{
				return false;
			}
			if (outA == outB)
			{
				return true;
			}
			int[] rank = armRank[node];
			int m = 2 * rank.Length;
			int a1 = 2 * rank[inA] + 1, b1 = 2 * rank[outA], a2 = 2 * rank[inB] + 1, b2 = 2 * rank[outB];
			int span = (b1 - a1 + m) % m;
			bool x = (a2 - a1 + m) % m < span, y = (b2 - a1 + m) % m < span;
			return x != y;
		}

		/// <summary>Per node, the two arms (indices in <see cref="Traffic.Links"/>) of its main line, or -1 for both where it has none.</summary>
		private static int[] mainA, mainB;

		/// <summary>
		/// The main line of a junction, worked out once with the map: drivers know which road is the through road without a sign. Among
		/// the arms of the highest class (if only one arm is of it, that arm and those of the next class): the two arms of one road that
		/// carries on through the node; else the pair nearest a straight line, if it is clearly straighter than the next pair (a T has
		/// one, a fork of two equal branches or a crossroads of equal roads does not). None at a dead end, a bend (two arms) or a hub of
		/// five arms or more: there every junction stays first come first served.
		/// </summary>
		private static void MainLine(int node)
		{
			mainA[node] = mainB[node] = -1;
			List<int> links = Traffic.Links[node];
			int m = links.Count;
			if (m < 3 || m > 4)
			{
				return;
			}
			int[] kind = new int[m];
			int best = int.MaxValue;
			for (int k = 0; k < m; k++)
			{
				kind[k] = (int)Network.Roads[Traffic.Edges[links[k]].Road].Kind;
				best = Math.Min(best, kind[k]);
			}
			int top = 0, only = -1, next = int.MaxValue;
			for (int k = 0; k < m; k++)
			{
				if (kind[k] == best)
				{
					top++;
					only = k;
				}
				else
				{
					next = Math.Min(next, kind[k]);
				}
			}
			if (top == 2)
			{
				// Two arms of a higher class than the rest: they are the main line, however they bend.
				for (int k = 0; k < m; k++)
				{
					if (kind[k] == best)
					{
						if (mainA[node] < 0)
						{
							mainA[node] = k;
						}
						else
						{
							mainB[node] = k;
						}
					}
				}
				return;
			}
			int pa = -1, pb = -1, qa = -1, qb = -1;
			double pd = double.MaxValue, qd = double.MaxValue;
			bool same = false;
			for (int i = 0; i < m; i++)
			{
				for (int j = i + 1; j < m; j++)
				{
					bool ok = top >= 3 ? kind[i] == best && kind[j] == best : (i == only && kind[j] == next) || (j == only && kind[i] == next);
					if (!ok)
					{
						continue;
					}
					bool carries = Traffic.Edges[links[i]].Road == Traffic.Edges[links[j]].Road;
					double d = armX[node][i] * armX[node][j] + armZ[node][i] * armZ[node][j];
					if (carries && !same)
					{
						// A road carrying on through beats any pair that does not.
						same = true;
						pa = pb = qa = qb = -1;
						pd = qd = double.MaxValue;
					}
					else if (same && !carries)
					{
						continue;
					}
					if (d < pd)
					{
						qa = pa;
						qb = pb;
						qd = pd;
						pa = i;
						pb = j;
						pd = d;
					}
					else if (d < qd)
					{
						qa = i;
						qb = j;
						qd = d;
					}
				}
			}
			if (pa < 0)
			{
				return;
			}
			if (!same && (pd > -0.7 || (qa >= 0 && qd - pd < 0.15)))
			{
				// No pair near enough straight, or two about as straight as each other: no clear main line.
				return;
			}
			mainA[node] = pa;
			mainB[node] = pb;
		}

		/// <summary>The arms of node <paramref name="node"/>'s main line (-1, -1: none). For trafficview's picture.</summary>
		internal static void MainArms(int node, out int a, out int b)
		{
			Prepare();
			a = mainA[node];
			b = mainB[node];
		}

		/// <summary>Whether a movement through node <paramref name="node"/> crosses a carriageway of the trunk: a turn left there, or straight across it from side to side.</summary>
		private static bool CrossesCarriageway(int node, int inA, int outA)
		{
			if (!twoLaneMain[node])
			{
				return false;
			}
			bool inMain = inA == mainA[node] || inA == mainB[node], outMain = outA == mainA[node] || outA == mainB[node];
			return LeftTurn(node, inA, outA) || (!inMain && !outMain);
		}

		/// <summary>Whether a movement through node <paramref name="node"/> crosses another stream: a turn left anywhere, or across a main road from side to side, or across a carriageway of the trunk.</summary>
		private static bool CrossesTraffic(int node, int inA, int outA)
		{
			if (CrossesCarriageway(node, inA, outA) || LeftTurn(node, inA, outA))
			{
				return true;
			}
			int a0 = mainA[node], a1 = mainB[node];
			return a0 >= 0 && inA != a0 && inA != a1 && outA != a0 && outA != a1;
		}

		/// <summary>Whether the way in along arm <paramref name="inA"/> and out along <paramref name="outA"/> turns left (across the oncoming lane: traffic keeps right).</summary>
		private static bool LeftTurn(int node, int inA, int outA)
		{
			double tx = -armX[node][inA], tz = -armZ[node][inA];
			return tx * armZ[node][outA] - tz * armX[node][outA] > 0.3;
		}

		/// <summary>
		/// Whether movement A (in along arm <paramref name="inA"/>, out along <paramref name="outA"/>) gives way to movement B at node
		/// <paramref name="node"/>, their ways crossing: a car from a side arm gives way to every car from the main line; on the main
		/// line, a car turning off gives way to the oncoming car going on along it, and of two turning off from opposite ways into the
		/// same arm the one turning left gives way. False both ways where the node has no main line or both come from side arms: first
		/// come first served there, as before.
		/// </summary>
		internal static bool Yields(int node, int inA, int outA, int inB, int outB)
		{
			return !NoRightOfWay && MainYields(node, inA, outA, inB, outB);
		}

		/// <summary>trafficview only: every junction first come first served, as before 0.33.0 (to compare with).</summary>
		internal static bool NoRightOfWay { get; set; }

		private static bool MainYields(int node, int inA, int outA, int inB, int outB)
		{
			int a0 = mainA[node], a1 = mainB[node];
			if (a0 < 0 || inA == inB)
			{
				return false;
			}
			bool aMain = inA == a0 || inA == a1, bMain = inB == a0 || inB == a1;
			if (!aMain)
			{
				return bMain;
			}
			if (!bMain)
			{
				return false;
			}
			return MainTurnYields(node, inA, outA, inB, outB, a0, a1);
		}

		/// <summary>
		/// Unused since 0.34.0, kept so trafficview's switch still compiles: a car turning left off the main line always gives way to the
		/// oncoming cars going on ("Drivers turning left must yield to oncoming vehicles going straight ahead", PA Driver's Manual p. 48).
		/// 0.33.0 had it first come first served, and left-turners cut across oncoming traffic (the owner's report).
		/// </summary>
		internal static bool MainTurnsGiveWay { get; set; }

		/// <summary>
		/// The gap a car from a side arm leaves before a car with the right of way gets to the junction (seconds), from the PA Driver's
		/// Manual (p. 49): "about a 6-second gap in both directions to cross", "a gap of about eight (8) seconds ... approaching from the
		/// left" to turn right, "a 9-second gap" turning left from a minor onto a major street. Not shortened by waiting: the manual has no
		/// such thing. 0 for a car on the main line (a left turn off it needs only to be clear of the oncoming car).
		/// </summary>
		internal static double SideGap(int node, int inA, int outA, double waited)
		{
			double gap = SideGap(node, inA, outA);
			if (mainA[node] >= 0 && !twoLaneMain[node])
			{
				// Off the trunk a driver kept waiting takes a shorter gap, down to half after 10 s (as studies of gap
				// acceptance find); the car it goes in front of still never has to brake hard (TooSoon).
				gap *= 1.0 - (1.0 - TownPatience) * Math.Min(1.0, waited / TownPatienceAfter);
			}
			return gap;
		}

		private const double TownPatience = 0.5, TownPatienceAfter = 10.0;

		internal static double SideGap(int node, int inA, int outA)
		{
			if (mainA[node] < 0 || inA == mainA[node] || inA == mainB[node])
			{
				return 0.0;
			}
			int entry = EntryIx(node, inA, outA);
			if (entry == MedianIx)
			{
				// Turning left onto the trunk in two stages: across the near carriageway into the median lane, as across a road.
				return AcrossGap;
			}
			bool trunk = twoLaneMain[node];
			if (LeftTurn(node, inA, outA))
			{
				return trunk ? LeftGap : TownLeftGap;
			}
			return RightTurn(node, inA, outA) ? (trunk ? RightGap : TownRightGap) : (trunk ? AcrossGap : TownAcrossGap);
		}

		/// <summary>The manual's gaps (p. 49), kept onto the trunk, where a misjudged one meets 55 mph.</summary>
		internal const double LeftGap = 9.0, RightGap = 8.0, AcrossGap = 6.0;
		/// <summary>
		/// Onto every other road, the gaps drivers are measured to take (the Highway Capacity Manual's critical headways for a two-way
		/// stop onto a two-lane road: 7.1 s turning left, 6.2 s right, 6.5 s across). The manual's 8 and 9 s, everywhere, held the towns'
		/// side streets up for twice as long as before (seed 1: 2,084 minutes waiting at junctions against 1,231) with nothing gained.
		/// </summary>
		internal const double TownLeftGap = 7.1, TownRightGap = 6.2, TownAcrossGap = 6.5;

		/// <summary>Whether the way in along arm <paramref name="inA"/> and out along <paramref name="outA"/> turns right (traffic keeps right: no oncoming lane crossed).</summary>
		private static bool RightTurn(int node, int inA, int outA)
		{
			double tx = -armX[node][inA], tz = -armZ[node][inA];
			return tx * armZ[node][outA] - tz * armX[node][outA] < -0.3;
		}

		/// <summary>
		/// Per node, whether its main line is the trunk's (two lanes each way): there a car turning off across the oncoming carriageway
		/// always gives way to the oncoming cars, waiting in the inner lane, which the cars going on pass in the outer one. Never cutting
		/// across the whole highway in front of them (the owner's screenshot, 0.33.0).
		/// </summary>
		private static bool[] twoLaneMain;

		private static bool MainTurnYields(int node, int inA, int outA, int inB, int outB, int a0, int a1)
		{
			bool aOff = outA != a0 && outA != a1, bOff = outB != a0 && outB != a1;
			if (aOff && !bOff)
			{
				return true;
			}
			return aOff && bOff && LeftTurn(node, inA, outA) && !LeftTurn(node, inB, outB);
		}

		/// <summary>
		/// Whether movement A's way and movement B's cross only in the box's near half for A: B comes from A's left (in the lane or the
		/// carriageway nearer A) and they leave by different arms. A is then clear of B once its back is past the box's middle.
		/// </summary>
		internal static bool NearHalf(int node, int inA, int outA, int inB, int outB)
		{
			return outA != outB && inA != inB && RightOf(node, inA, inB);
		}

		/// <summary>Whether a car coming in along arm <paramref name="a"/> is on the right of one coming in along arm <paramref name="b"/>.</summary>
		internal static bool RightOf(int node, int a, int b)
		{
			// b travels in along -u_b; its right is (-u_b.z, u_b.x).
			return armX[node][a] * -armZ[node][b] + armZ[node][a] * armX[node][b] > 0.3;
		}

		internal static float[] Profile(Leg leg)
		{
			Seg[] path = leg.Path;
			int n = (int)Math.Ceiling(leg.Length / Sample) + 2;
			float[] v = new float[n];
			int k = 0;
			for (int i = 0; i < n; i++)
			{
				double p = Math.Min(i * Sample, leg.Length);
				while (k + 1 < path.Length && path[k + 1].P0 <= p)
				{
					k++;
				}
				Seg s = path[k];
				double limit = s.Speed;
				if (s.Edge >= 0)
				{
					int r = Traffic.Edges[s.Edge].Road;
					Network.Locate(Network.Roads[r], s.S0 + s.Dir * (p - s.P0), out int j, out double _);
					limit = Math.Min(limit, Math.Min(bend[r][j], bend[r][j + 1]));
				}
				v[i] = (float)limit;
			}
			// Turns at the junctions: none under 20 degrees, a sharp one at walking pace and a bit.
			for (int j = 0; j + 1 < path.Length; j++)
			{
				int node = NodeAfter(path, j);
				if (node < 0)
				{
					continue;
				}
				Seg a = path[j], b = path[j + 1];
				Network.Road ra = Network.Roads[Traffic.Edges[a.Edge].Road], rb = Network.Roads[Traffic.Edges[b.Edge].Road];
				Traffic.Heading(ra, a.S0 + a.Dir * (a.Len - Math.Min(a.Len, 2.0)), out double ax, out double az);
				Traffic.Heading(rb, b.S0 + b.Dir * Math.Min(b.Len, 2.0), out double bx, out double bz);
				double cos = (ax * a.Dir) * (bx * b.Dir) + (az * a.Dir) * (bz * b.Dir);
				const double cos20 = 0.9397, cos45 = 0.7071;
				if (cos >= cos20)
				{
					continue;
				}
				double nodeP = a.P0 + a.Len, box = Traffic.Nodes[node].Box;
				for (int i = 0; i < n; i++)
				{
					double p = i * Sample;
					if (p < nodeP - box || p > nodeP + box)
					{
						continue;
					}
					double sharp = TurnAt(box);
					double turn = cos <= cos45 ? sharp : sharp + (v[i] - sharp) * (cos - cos45) / (cos20 - cos45);
					v[i] = (float)Math.Min(v[i], turn);
				}
			}
			if (!leg.Vanishes)
			{
				// Creep at the end: the stop itself comes from the end as an obstacle.
				int last = (int)Math.Floor(leg.Length / Sample);
				for (int i = Math.Min(last, n - 1); i < n; i++)
				{
					v[i] = (float)Math.Min(v[i], 2.0);
				}
			}
			for (int i = n - 2; i >= 0; i--)
			{
				v[i] = (float)Math.Min(v[i], Math.Sqrt(v[i + 1] * (double)v[i + 1] + 2.0 * Brake * Sample));
			}
			return v;
		}

		/// <summary>
		/// The speed of a sharp turn through a junction whose box is <paramref name="box"/> (half) across: round a curve about that
		/// radius at the bends' sideways pull (<see cref="Lateral"/>), from <see cref="TurnSpeed"/> at a town street's corner to about
		/// 15 mph across the trunk, 80 wide (a left off it at walking pace took 9 s to clear, longer than any gap its traffic left).
		/// </summary>
		internal static double TurnAt(double box)
		{
			return Math.Max(TurnSpeed, Math.Min(MostTurn, Math.Sqrt(Lateral * box)));
		}

		private const double MostTurn = 22.0;

		internal static double DesiredAt(float[] profile, double p)
		{
			int i = (int)Math.Floor(p / Sample);
			i = Math.Max(0, Math.Min(profile.Length - 2, i));
			return Math.Min(profile[i], profile[i + 1]);
		}

		/// <summary>The IDM's interaction with an obstacle <paramref name="gap"/> ahead moving at <paramref name="lead"/>: the braking it asks for.</summary>
		private static double Interaction(double v, double gap, double lead)
		{
			double star = MinGap + Math.Max(0.0, v * Headway + v * (v - lead) / (2.0 * Math.Sqrt(Accel * Brake)));
			double g = Math.Max(gap, 0.25);
			return -Accel * (star / g) * (star / g);
		}

		private void Simulate()
		{
			List<Traffic.Household> hh = Traffic.Households;
			int[] next = new int[hh.Count];
			int throughNext = 0;
			int steps = (int)Math.Round((Traffic.DaySeconds + Tail) / Step);
			List<Request> requests = new List<Request>();
			List<Request> refused = new List<Request>();
			double[] acc = new double[0];
			for (int step = 0; step < steps; step++)
			{
				double w = step * Step;
				bool inDay = w < Traffic.DaySeconds;
				// Tours whose time has come, once a car is home and rested (none after 23:00).
				if (inDay)
				{
					for (int h = 0; h < hh.Count; h++)
					{
						List<Tour> list = tours[h];
						while (next[h] < list.Count && list[next[h]].Depart <= w)
						{
							Tour t = list[next[h]];
							int car = -1;
							for (int c = householdCars[h]; c < householdCars[h] + hh[h].Cars; c++)
							{
								if (!Cars[c].Out && Cars[c].FreeAt <= w)
								{
									car = c;
									break;
								}
							}
							if (car < 0)
							{
								if (w >= LastOut)
								{
									next[h]++;
									Dropped++;
									continue;
								}
								break;
							}
							next[h]++;
							Cars[car].Out = true;
							Leg leg = new Leg
							{
								Car = car, Purpose = t.Purpose, Path = t.Out, Length = t.OutLength, Vanishes = t.Portal >= 0, From = hh[h].Home, To = t.To,
								Dest = t.Dest, Ready = w, Dwell = t.Dwell, Host = t.Host,
							};
							Legs.Add(leg);
							Cars[car].Legs.Add(Legs.Count - 1);
							waiting.Add(Legs.Count - 1);
						}
					}
					while (throughNext < throughs.Count && Legs[throughs[throughNext]].Ready <= w)
					{
						int li = throughs[throughNext++];
						Cars[Legs[li].Car].Legs.Add(li);
						waiting.Add(li);
					}
				}
				if (step % 400 == 0 && step / 400 < 48)
				{
					ActiveBy[step / 400] = movers.Count;
				}
				BuildLanes(step);
				if (LaneChanges(w))
				{
					BuildLanes(step);
				}
				StartWaiting(step, w);
				// Record (state at w).
				foreach (Mover m in movers)
				{
					if (((step - m.StartStep) & 1) == 0)
					{
						m.Rec.Add((float)m.P);
					}
				}
				Junctions(requests, refused);
				// Accelerations, all from the same state, then the move.
				if (acc.Length < movers.Count)
				{
					acc = new double[movers.Count * 2];
				}
				for (int i = 0; i < movers.Count; i++)
				{
					acc[i] = Acceleration(movers[i]);
				}
				for (int i = 0; i < movers.Count; i++)
				{
					Mover m = movers[i];
					if (w < m.HoldUntil)
					{
						// Still pulling out of its space, standing on the lane beside it.
						m.V = 0.0;
						continue;
					}
					double a = acc[i], v1 = m.V + a * Step;
					if (v1 < 0.0)
					{
						m.P += a < 0.0 ? -m.V * m.V / (2.0 * a) : 0.0;
						m.V = 0.0;
					}
					else
					{
						m.P += (m.V + v1) * 0.5 * Step;
						m.V = v1;
					}
					Seg[] path = m.L.Path;
					while (m.Seg + 1 < path.Length && m.P >= path[m.Seg].P0 + path[m.Seg].Len)
					{
						m.Seg++;
						if (TwoLane(path[m.Seg]) && !TwoLane(path[m.Seg - 1]))
						{
							EnterTrunk(m, w);
						}
					}
					double limit = path[m.Seg].Speed;
					if (m.V > limit * 1.05)
					{
						Speeding++;
						MaxOver = Math.Max(MaxOver, m.V / limit);
					}
					if (m.V < 0.5)
					{
						StoppedOn.TryGetValue(path[m.Seg].Edge, out double sec);
						StoppedOn[path[m.Seg].Edge] = sec + Step;
						if (TwoLane(path[m.Seg]))
						{
							// On the trunk: going on along it at the next junction, or turning off.
							int ahead = m.Seg + 1 < path.Length ? m.Seg + 1 : m.Seg;
							if (TwoLane(path[ahead]))
							{
								TrunkStoppedOn += Step;
							}
							else
							{
								TrunkStoppedTurning += Step;
							}
						}
					}
					if (m.V < 0.5 && NearMouth(m))
					{
						StoppedAtMouth++;
					}
				}
				Release();
				CheckBoxes();
				// Arrivals.
				for (int i = movers.Count - 1; i >= 0; i--)
				{
					Mover m = movers[i];
					Leg leg = m.L;
					bool done = leg.Vanishes ? m.P >= leg.Length : (m.P >= leg.Length - 1.0 && m.V < 1.5) || m.P >= leg.Length;
					if (!done)
					{
						continue;
					}
					// Into a space across the road it waits at its stop, as a driver turning across would, until the oncoming lane is clear.
					if (leg.FarEnd && m.ParkAt == double.MaxValue && m.HeldSince > w)
					{
						m.HeldSince = w;
					}
					Seg last = leg.Path[leg.Path.Length - 1];
					if (leg.FarEnd && m.ParkAt == double.MaxValue && w - m.HeldSince < FarHoldMost && !OppositeClear(last, leg.Length, m))
					{
						m.P = Math.Min(m.P, leg.Length);
						m.V = 0.0;
						continue;
					}
					// Stopped beside its space, it stands on the lane while it is drawn pulling in, then parks.
					if (!leg.Vanishes && leg.PullIn > 0.0)
					{
						if (m.ParkAt == double.MaxValue)
						{
							m.ParkAt = w + leg.PullIn;
						}
						if (w + Step < m.ParkAt)
						{
							m.P = Math.Min(m.P, leg.Length);
							m.V = 0.0;
							continue;
						}
					}
					Arrive(m, w + Step);
					movers.RemoveAt(i);
				}
				if (!inDay && movers.Count == 0 && waiting.Count == 0 && returns.Count == 0)
				{
					break;
				}
			}
			Unfinished = movers.Count + waiting.Count;
			for (int i = 0; i < movers.Count && Stuck.Count < 40; i++)
			{
				Mover m = movers[i];
				if (m.Ahead != null)
				{
					continue;
				}
				Seg[] path = m.L.Path;
				int k = Asking(m);
				string why = k >= 0 ? "asks node " + NodeAfter(path, k) + " seg " + k : "no ask";
				if (k >= 0 && nodeGrants.TryGetValue(NodeAfter(path, k), out List<Grant> held))
				{
					why += " {held:";
					foreach (Grant g in held)
					{
						why += " leg " + g.M.Index + " " + g.InKey + ">" + g.OutKey + " at " + (g.M.P - g.NodeP).ToString("0") + " v " + g.M.V.ToString("0") + " outlane " + g.OutLane;
					}
					why += "; mine " + LinkIndex(NodeAfter(path, k), path[k].Edge) + ">" + LinkIndex(NodeAfter(path, k), path[k + 1].Edge) + " outlane " + LaneOf(path[k + 1]) + "}";
				}
				string grants = "";
				foreach (Grant g in m.Grants)
				{
					grants += " g" + g.Node;
				}
				string look = "";
				for (int k2 = m.Seg + 1; k2 < path.Length && path[k2].P0 - m.P < Ahead; k2++)
				{
					if (lanes.TryGetValue(LaneOf(path[k2]), out List<Mover> list2) && list2.Count > 0)
					{
						look += " [seg " + k2 + " lane " + LaneOf(path[k2]) + " u0 " + LaneU(path[k2], path[k2].P0).ToString("0") + ":";
						foreach (Mover o in list2)
						{
							look += " " + o.Index + "@" + o.LaneU.ToString("0") + "v" + o.V.ToString("0");
						}
						look += "]";
					}
				}
				Stuck.Add(look + " :: leg " + m.Index + " p " + m.P.ToString("0") + "/" + m.L.Length.ToString("0") + " v " + m.V.ToString("0.0") + " seg " + m.Seg + "/" + path.Length + " lane " + m.Lane
					+ " u " + m.LaneU.ToString("0") + " " + why + " ahead " + (m.Ahead != null ? m.Ahead.Index + " at " + m.Ahead.LaneU.ToString("0") : "-") + " grants" + grants + " wait " + m.Wait.ToString("0"));
			}
			foreach (Mover m in movers)
			{
				m.L.End = steps * Step;
				m.Rec.Add((float)m.P);
				m.L.Rec = m.Rec.ToArray();
				m.L.Lanes = m.LaneRec?.ToArray();
			}
			movers.Clear();
		}

		private readonly List<(double ready, int leg)> returns = new List<(double ready, int leg)>();

		/// <summary>A leg ends: record it, park the car or send it off the map, and line up the way home after its stay.</summary>
		private void Arrive(Mover m, double t)
		{
			Leg leg = m.L;
			m.Rec.Add((float)leg.Length);
			leg.Rec = m.Rec.ToArray();
			leg.Lanes = m.LaneRec?.ToArray();
			leg.End = t;
			leg.Profile = null;
			foreach (Grant g in m.Grants)
			{
				nodeGrants[g.Node].Remove(g);
			}
			m.Grants.Clear();
			Car car = Cars[leg.Car];
			if (leg.Return || car.Household < 0)
			{
				car.Out = false;
				car.FreeAt = t + Rest;
				return;
			}
			// The way back, after the stay (cut short so it leaves before the day ends).
			Traffic.Household home = Traffic.Households[car.Household];
			List<Seg> segs = new List<Seg>();
			bool ok;
			if (leg.Vanishes)
			{
				int portal = Array.IndexOf(portalDest, leg.Dest);
				ok = EdgeWay(home.Home, portal, true, segs);
			}
			else
			{
				ok = Way(leg.To, -1, home.Home, -1, segs);
			}
			if (!ok)
			{
				Unrouted++;
				car.Out = false;
				car.FreeAt = t + Rest;
				return;
			}
			Leg back = new Leg
			{
				Car = leg.Car, Purpose = leg.Purpose, Return = true, Appears = leg.Vanishes, From = leg.To, To = home.Home, Dest = leg.Dest, Host = leg.Host,
				Ready = Math.Max(t + MinStay, leg.Ready + leg.Dwell),
			};
			back.Path = Finish(segs, out back.Length);
			Legs.Add(back);
			car.Legs.Add(Legs.Count - 1);
			returns.Add((back.Ready, Legs.Count - 1));
		}

		/// <summary>Per lane, the cars on it in order along it; each car's lane, place on it and the car just ahead.</summary>
		private void BuildLanes(int step)
		{
			foreach (long key in laneKeys)
			{
				lanes[key].Clear();
			}
			laneKeys.Clear();
			foreach (Mover m in movers)
			{
				AddToLane(m);
			}
			foreach (long key in laneKeys)
			{
				List<Mover> list = lanes[key];
				list.Sort((a, b) => a.LaneU != b.LaneU ? a.LaneU.CompareTo(b.LaneU) : a.Index.CompareTo(b.Index));
				for (int i = 0; i < list.Count; i++)
				{
					list[i].Ahead = i + 1 < list.Count ? list[i + 1] : null;
					if (i + 1 < list.Count)
					{
						double gap = list[i + 1].LaneU - list[i].LaneU;
						MinGapSeen = Math.Min(MinGapSeen, gap);
						if (gap < Traffic.CarLength)
						{
							long pair = ((long)Math.Min(list[i].Index, list[i + 1].Index) << 32) | (uint)Math.Max(list[i].Index, list[i + 1].Index);
							if (!overlapPairs.Contains(pair) && OverlapNotes.Count < 14)
							{
								Mover a = list[i], b = list[i + 1];
								OverlapNotes.Add("w " + (step * Step).ToString("0") + " lane " + key + " gap " + gap.ToString("0.0") + ": behind leg " + a.Index + " seg " + a.Seg + " in " + (a.P - a.L.Path[a.Seg].P0).ToString("0") + " v " + a.V.ToString("0.0") + " age " + (step - a.StartStep) + " forced " + (a.Grants.Exists(g => g.Forced)) + " / ahead leg " + b.Index + " seg " + b.Seg + " in " + (b.P - b.L.Path[b.Seg].P0).ToString("0") + " v " + b.V.ToString("0.0") + " age " + (step - b.StartStep));
							}
							Overlaps++;
							overlapPairs.Add(((long)Math.Min(list[i].Index, list[i + 1].Index) << 32) | (uint)Math.Max(list[i].Index, list[i + 1].Index));
						}
					}
				}
			}
		}

		private void AddToLane(Mover m)
		{
			Seg s = m.L.Path[m.Seg];
			if (m.Ix != 0 && !TwoLane(s))
			{
				// Off the trunk: its one lane (the move over is drawn through the junction's curve).
				m.Ix = 0;
				m.LaneEnd = double.MaxValue;
			}
			m.Lane = LaneOf(m, s);
			m.LaneU = LaneU(s, m.P);
			m.Ahead = null;
			if (!lanes.TryGetValue(m.Lane, out List<Mover> list))
			{
				list = new List<Mover>();
				lanes[m.Lane] = list;
			}
			if (list.Count == 0)
			{
				laneKeys.Add(m.Lane);
			}
			list.Add(m);
		}

		/// <summary>Legs whose time has come pull out once their lane is clear (nothing within 40, nothing arriving within 3 s), or after a minute anyway.</summary>
		private void StartWaiting(int step, double w)
		{
			// The ways home whose stay is over join the queue, in order.
			if (returns.Count > 0)
			{
				for (int i = 0; i < returns.Count; i++)
				{
					if (returns[i].ready <= w)
					{
						waiting.Add(returns[i].leg);
						returns.RemoveAt(i);
						i--;
					}
				}
			}
			letIn.Clear();
			for (int i = 0; i < waiting.Count; i++)
			{
				Leg leg = Legs[waiting[i]];
				if (!leg.Placed)
				{
					leg.Placed = true;
					if (!leg.Appears)
					{
						TrafficParking.Spot here = Parking.Here(leg.Car);
						if (!here.Away)
						{
							MoveEnd(leg, true, here);
							leg.FarSide = LeftOf(leg.Path[0], 0.0, here) || TrafficParking.Backwards(here, leg, 0.0);
							leg.PullOut = TrafficParking.PullTime(here, leg, 0.0);
						}
					}
				}
				int k0 = 0;
				while (k0 + 1 < leg.Path.Length && leg.Path[k0].Len <= 0.0)
				{
					k0++;
				}
				Seg first = leg.Path[k0];
				long lane = LaneOf(first);
				double u = LaneU(first, first.P0);
				bool clear = !Coming(first, u) && (k0 == 0 || !Coming(leg.Path[0], LaneU(leg.Path[0], 0.0)));
				// A short first stretch (a tunnel's): what is just past it on the next lane counts too.
				if (clear && first.Len < 40.0 && k0 + 1 < leg.Path.Length && lanes.TryGetValue(LaneOf(leg.Path[k0 + 1]), out List<Mover> nextList))
				{
					Seg nx = leg.Path[k0 + 1];
					double u0 = LaneU(nx, nx.P0);
					foreach (Mover o in nextList)
					{
						if (o.LaneU >= u0 && o.LaneU - u0 < 40.0 - first.Len + Traffic.CarLength)
						{
							clear = false;
							break;
						}
					}
				}
				// "A vehicle entering a public highway from an alley, private road or driveway must wait until the main road is clear"
				// (PA Driver's Manual p. 48): on a road faster than a 25 mph street, the gap a car turning right leaves (8 s).
				bool slowRoad = first.Speed <= SlowRoad;
				double pullGap = slowRoad ? 3.0 : RightGap;
				List<Mover> list = null;
				if (clear && lanes.TryGetValue(lane, out list))
				{
					foreach (Mover o in list)
					{
						double d = o.LaneU - u;
						if (Math.Abs(d) < 40.0 || (d < 0.0 && -d < pullGap * o.V))
						{
							clear = false;
							break;
						}
					}
				}
				// From a space across the road the car crosses the oncoming lane (turning round into its own): that lane must be clear
				// too, for as long as that takes.
				if (clear && leg.FarSide && first.Edge >= 0)
				{
					clear = OppositeClear(first, first.P0);
				}
				else if (clear && !leg.Appears && first.Edge >= 0)
				{
					// Pulling out at an angle (out of a drive, a bay, or a kerb space on a bend), its corner swings into the far lane on a
					// narrow road: nothing alongside there.
					clear = !OppositeNear(first, first.P0, 25.0);
				}
				// Nor into the path of a car crossing this lane from the far one, into or out of a space this side.
				if (clear && first.Edge >= 0)
				{
					clear = !CrossingNear(first, first.P0);
				}
				// Let in: after a while the car just behind leaves a gap (below), and the car pulls out once the gap is there and
				// nothing is coming fast.
				if (!clear && slowRoad && w - leg.Ready >= LetInAfter && !Coming(first, u))
				{
					bool gap = true;
					if (list != null || lanes.TryGetValue(lane, out list))
					{
						foreach (Mover o in list)
						{
							double d = o.LaneU - u;
							if ((d > -Traffic.CarLength - 2.0 && d < Traffic.CarLength + 2.0) || (d <= -Traffic.CarLength - 2.0 && o.V > 2.0 && -d < 3.0 * o.V + 40.0))
							{
								gap = false;
								break;
							}
						}
					}
					clear = gap;
					if (!gap)
					{
						letIn.Add((lane, u));
					}
				}
				if (!clear && w - leg.Ready < PullOutForce)
				{
					continue;
				}
				if (list == null)
				{
					lanes.TryGetValue(lane, out list);
				}
				if (!clear && list != null && list.Exists(o => Math.Abs(o.LaneU - u) < Traffic.CarLength + 2.0))
				{
					// Even forced, never on top of another car.
					continue;
				}
				if (!clear)
				{
					PullOutsForced++;
				}
				leg.Start = w;
				TrafficParking.Spot there = Parking.Take(waiting[i], w);
				if (!leg.Vanishes && !there.Away)
				{
					MoveEnd(leg, false, there);
					leg.FarEnd = LeftOf(leg.Path[leg.Path.Length - 1], leg.Length, there) || TrafficParking.Backwards(there, leg, leg.Length);
					leg.PullIn = TrafficParking.PullTime(there, leg, leg.Length);
				}
				StartsBy[Math.Min(47, (int)(w / 100.0))]++;
				leg.Profile = Profile(leg);
				for (int q = 0; q + 1 < leg.Profile.Length; q++)
				{
					ProfileSeconds += Sample / Math.Max(1.0, Math.Min(leg.Profile[q], leg.Profile[q + 1]));
				}
				Mover m = new Mover { Index = waiting[i], L = leg, StartStep = step, P = 0.0, Seg = k0, HoldUntil = leg.Appears ? -1.0 : w + leg.PullOut };
				m.V = leg.Appears ? Math.Min(leg.Profile[0], leg.Path[0].Speed) : 0.0;
				movers.Add(m);
				AddToLane(m);
				if (list == null)
				{
					list = lanes[lane];
				}
				list.Sort((a, b) => a.LaneU != b.LaneU ? a.LaneU.CompareTo(b.LaneU) : a.Index.CompareTo(b.Index));
				for (int j = 0; j < list.Count; j++)
				{
					list[j].Ahead = j + 1 < list.Count ? list[j + 1] : null;
				}
				waiting.RemoveAt(i);
				i--;
			}
			movers.Sort((a, b) => a.Index.CompareTo(b.Index));
		}

		/// <summary>
		/// Whether a car let through the junction at the start of seg <paramref name="s"/>'s lane is coming into it near a spot
		/// <paramref name="u"/> along it: a car pulling out there would pull out in front of it.
		/// </summary>
		/// <summary>
		/// Whether the oncoming lane beside path distance <paramref name="p"/> of seg <paramref name="seg"/> is clear for a car to cross it
		/// to a space (or from one) across the road: nothing within 40, nothing coming within the crossing's time and 3 s more.
		/// </summary>
		private bool OppositeClear(Seg seg, double p, Mover self = null)
		{
			if (seg.Edge < 0)
			{
				return true;
			}
			Seg opp = seg;
			opp.Dir = -seg.Dir;
			opp.S0 = seg.S0 + seg.Dir * (p - seg.P0);
			opp.P0 = p;
			double uo = LaneU(opp, p);
			// A car let into that lane at the junction it starts from may reach here before the crossing is done.
			if (Coming(opp, uo, 300.0))
			{
				return false;
			}
			if (lanes.TryGetValue(LaneOf(opp), out List<Mover> list))
			{
				foreach (Mover o in list)
				{
					double d = o.LaneU - uo;
					if (self != null && o.L.FarEnd && o.ParkAt == double.MaxValue && (o.HeldSince > self.HeldSince || (o.HeldSince == self.HeldSince && o.Index > self.Index)))
					{
						// Also waiting to cross to this side, but stopped after this car: this one goes first.
						continue;
					}
					if ((d > -80.0 && d < 40.0) || (d < 0.0 && -d < (3.0 + TrafficParking.PullMost) * Math.Max(5.0, o.V)))
					{
						return false;
					}
				}
			}
			return true;
		}

		/// <summary>Whether <paramref name="spot"/> is left of the way seg <paramref name="seg"/> is driven, beside path distance <paramref name="p"/> (across the road, driving on the right).</summary>
		private static bool LeftOf(Seg seg, double p, TrafficParking.Spot spot)
		{
			if (seg.Edge < 0)
			{
				return false;
			}
			Network.Road road = Network.Roads[Traffic.Edges[seg.Edge].Road];
			double rs = seg.S0 + seg.Dir * (p - seg.P0);
			Traffic.At(road, rs, out double rx, out double rz, out double _);
			Traffic.Heading(road, rs, out double tx, out double tz);
			return ((spot.X - rx) * -tz + (spot.Z - rz) * tx) * seg.Dir > 0.0;
		}

		/// <summary>Whether any car in the oncoming lane is within <paramref name="reach"/> of path distance <paramref name="p"/> of <paramref name="seg"/>.</summary>
		private bool OppositeNear(Seg seg, double p, double reach)
		{
			Seg opp = seg;
			opp.Dir = -seg.Dir;
			opp.S0 = seg.S0 + seg.Dir * (p - seg.P0);
			opp.P0 = p;
			double uo = LaneU(opp, p);
			if (lanes.TryGetValue(LaneOf(opp), out List<Mover> list))
			{
				foreach (Mover o in list)
				{
					if (Math.Abs(o.LaneU - uo) < reach)
					{
						return true;
					}
				}
			}
			return false;
		}

		/// <summary>Whether a car in the oncoming lane within 40 of path distance <paramref name="p"/> of <paramref name="seg"/> is crossing this lane, pulling out of or into a space this side.</summary>
		private bool CrossingNear(Seg seg, double p)
		{
			Seg opp = seg;
			opp.Dir = -seg.Dir;
			opp.S0 = seg.S0 + seg.Dir * (p - seg.P0);
			opp.P0 = p;
			double uo = LaneU(opp, p);
			if (!lanes.TryGetValue(LaneOf(opp), out List<Mover> list))
			{
				return false;
			}
			foreach (Mover o in list)
			{
				bool crossing = (o.L.FarSide && o.P <= 0.0) || (o.L.FarEnd && o.ParkAt != double.MaxValue);
				if (crossing && Math.Abs(o.LaneU - uo) < 40.0)
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>
		/// How long a car waits at its stop for the oncoming lane to clear before crossing it into its space, then it goes anyway: it
		/// holds up its own lane meanwhile, and a longer wait on a busy main road backs the queue up through the junctions behind it.
		/// </summary>
		private const double FarHoldMost = 15.0;

		private bool Coming(Seg s, double u, double reach = 80.0)
		{
			if (s.Edge < 0)
			{
				return false;
			}
			Traffic.Edge e = Traffic.Edges[s.Edge];
			int node = s.Dir > 0 ? e.A : e.B;
			if (u > Traffic.Nodes[node].Box + reach || !nodeGrants.TryGetValue(node, out List<Grant> list))
			{
				return false;
			}
			long lane = LaneOf(s);
			foreach (Grant g in list)
			{
				if (g.OutLane == lane)
				{
					return true;
				}
			}
			return false;
		}

		private bool HasGrant(Mover m, int segIndex)
		{
			foreach (Grant g in m.Grants)
			{
				if (g.SegIndex == segIndex)
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>How far ahead a car asks for a junction: its stopping distance, a second and a half, the box; at least 40.</summary>
		private static double ReachOf(double v, double box)
		{
			return Math.Max(40.0, v * v / (2.0 * Brake) + 1.5 * v + box);
		}

		/// <summary>The first junction ahead the car has not been granted, if it is within reach: seg index, or -1.</summary>
		private int Asking(Mover m)
		{
			Seg[] path = m.L.Path;
			for (int k = m.Seg; k + 1 < path.Length; k++)
			{
				int node = NodeAfter(path, k);
				if (node < 0)
				{
					if (path[k].P0 + path[k].Len - m.P > Ahead)
					{
						break;
					}
					continue;
				}
				double nodeP = path[k].P0 + path[k].Len;
				if (nodeP - m.P > ReachOf(m.V, Traffic.Nodes[node].Box))
				{
					return -1;
				}
				if (!HasGrant(m, k))
				{
					return k;
				}
			}
			return -1;
		}

		/// <summary>
		/// Every junction's requests this step, granted first come first served: in order of arrival (the car on the right first when two
		/// arrive within a second), each one granted if it crosses nobody granted who will still be in the box when it gets there, its way
		/// out has room past the box, and nobody before it in the order who was refused crosses it.
		/// </summary>
		private void Junctions(List<Request> requests, List<Request> refused)
		{
			requests.Clear();
			joined.Clear();
			joined.UnionWith(joining);
			joining.Clear();
			foreach (Mover m in movers)
			{
				int k = Asking(m);
				if (k < 0)
				{
					continue;
				}
				Seg[] path = m.L.Path;
				int node = NodeAfter(path, k);
				// Behind a car on the same lane bound through the same junction and not yet let in: wait behind it, in order.
				if (k == m.Seg && m.Ahead != null && m.Ahead.Seg + 1 < m.Ahead.L.Path.Length && NodeAfter(m.Ahead.L.Path, m.Ahead.Seg) == node && !HasGrant(m.Ahead, m.Ahead.Seg))
				{
					continue;
				}
				double nodeP = path[k].P0 + path[k].Len, box = Traffic.Nodes[node].Box;
				requests.Add(new Request
				{
					M = m, Node = node, SegIndex = k, NodeP = nodeP,
					InArm = LinkIndex(node, path[k].Edge), OutArm = LinkIndex(node, path[k + 1].Edge),
					InLane = LaneOf(m, path[k]), OutLane = TwoLane(path[k]) ? LaneOf(m, path[k + 1]) : LaneKey(path[k + 1], EntryIx(path, k)),
					Eta = ArriveTime(m.V, nodeP - box - m.P, Math.Max(m.V, DesiredAt(m.L.Profile, m.P + Math.Max(0.0, nodeP - box - m.P)))),
				});
			}
			requests.Sort((a, b) => a.Node != b.Node ? a.Node.CompareTo(b.Node) : a.Eta != b.Eta ? a.Eta.CompareTo(b.Eta) : a.M.Index.CompareTo(b.M.Index));
			for (int i = 0; i + 1 < requests.Count; i++)
			{
				Request a = requests[i], b = requests[i + 1];
				if (a.Node == b.Node && b.Eta - a.Eta < 1.0 && a.InArm != b.InArm && RightOf(a.Node, b.InArm, a.InArm)
					&& !Yields(a.Node, b.InArm, b.OutArm, a.InArm, a.OutArm) && !Yields(a.Node, a.InArm, a.OutArm, b.InArm, b.OutArm))
				{
					requests[i] = b;
					requests[i + 1] = a;
					i++;
				}
			}
			int start = 0;
			while (start < requests.Count)
			{
				int node = requests[start].Node, end = start;
				while (end < requests.Count && requests[end].Node == node)
				{
					end++;
				}
				refused.Clear();
				double box = Traffic.Nodes[node].Box;
				if (!nodeGrants.TryGetValue(node, out List<Grant> granted))
				{
					granted = new List<Grant>();
					nodeGrants[node] = granted;
				}
				for (int i = start; i < end; i++)
				{
					Request r = requests[i];
					bool ok = true, crossing = false;
					bool force = r.M.Wait > ForceAfter;
					foreach (Request q in refused)
					{
						// A car that gives way to this one holds it up neither by being refused first nor by being first in the order.
						if (q.InArm != r.InArm && Yields(node, q.InArm, q.OutArm, r.InArm, r.OutArm))
						{
							continue;
						}
						if ((q.InArm == r.InArm && q.InLane == r.InLane) || Conflict(node, q.InArm, q.OutArm, q.InLane, q.OutLane, r.InArm, r.OutArm, r.InLane, r.OutLane))
						{
							ok = false;
							RefusedOrder++;
							break;
						}
					}
					bool pulled = false;
					double rNear = 0.0, rFar = 0.0, rVFar = 0.0;
					if (ok || force)
					{
						foreach (Grant g in granted)
						{
							if (g.M == r.M || !Conflict(node, g.InKey, g.OutKey, g.InLane, g.OutLane, r.InArm, r.OutArm, r.InLane, r.OutLane))
							{
								continue;
							}
							if (Yields(node, r.InArm, r.OutArm, g.InKey, g.OutKey))
							{
								// Giving way to it: the same reckoning as for a car still coming.
								if (!pulled)
								{
									PullAway(r.M, r.NodeP + Traffic.CarLength, r.NodeP + box + Traffic.CarLength, out rNear, out rFar, out rVFar);
									pulled = true;
								}
								if (!TooSoon(r, box, g.M, g.SegIndex, g.InKey, g.OutKey, rNear, rFar, rVFar, GiveWayAfter(r.M.Wait)))
								{
									continue;
								}
								ok = false;
								crossing = true;
								RefusedCross++;
								break;
							}
							// Compatible if the other is through before this one gets there, or this one is through before the other gets
							// there (a gap taken, as a driver waiting at the line takes one): reckoned from a standstill for this one, and
							// as if the other speeds up a fifth, with a second to spare. A car that gives way to this one was let in only
							// with time to clear before it: reckoned pulling away, as it does.
							bool gGives = Yields(node, g.InKey, g.OutKey, r.InArm, r.OutArm);
							double clear;
							if (gGives)
							{
								PullAway(g.M, g.NodeP + Traffic.CarLength, g.NodeP + box + Traffic.CarLength, out double gNear, out clear, out double _);
								if (NearHalf(node, g.InKey, g.OutKey, r.InArm, r.OutArm))
								{
									clear = gNear;
								}
								if (clear + 0.5 < r.Eta)
								{
									continue;
								}
							}
							else
							{
								clear = (g.NodeP + box + Traffic.CarLength - g.M.P) / Math.Max(1.0, Math.Min(g.M.V, g.Through));
							}
							if (clear + 0.5 < r.Eta)
							{
								continue;
							}
							double gArrive = ArriveTime(g.M.V, g.NodeP - box - g.M.P, Math.Max(g.M.V, DesiredAt(g.M.L.Profile, g.NodeP - box)));
							if (gArrive > 0.0 && ClearTime(r.M.V, r.NodeP + box + Traffic.CarLength - r.M.P) + 1.0 < gArrive)
							{
								continue;
							}
							{
								ok = false;
								crossing = true;
								RefusedCross++;
								if (MainYields(node, g.InKey, g.OutKey, r.InArm, r.OutArm))
								{
									if (mainHeld.Add(((long)r.M.Index << 32) | (uint)node) && MainHeldNotes.Count < 12)
									{
										MainHeldNotes.Add("node " + node + " arms " + Traffic.Links[node].Count + " box " + box.ToString("0") + ": main " + r.InArm + ">" + r.OutArm + " eta " + r.Eta.ToString("0.0") + " v " + r.M.V.ToString("0.0") + " / giving way " + g.InKey + ">" + g.OutKey + " at " + (g.M.P - g.NodeP).ToString("0") + " v " + g.M.V.ToString("0.0") + " clear " + clear.ToString("0.0") + " forced " + g.Forced + " wait " + g.M.Wait.ToString("0"));
									}
								}
								break;
							}
						}
					}
					// Giving way: a car that gives way to someone goes only if every car it gives way to, coming to this junction and
					// not yet let in, gets there after it is through and a margin. Never let go of by the guard below.
					if ((ok || force) && !crossing && GivesWayToComing(r, box))
					{
						ok = false;
						crossing = true;
						RefusedGap++;
						// Cars in the lane it joins move over for it if they can (the trunk's inner lane).
						joining.Add(r.OutLane);
					}
					bool roomless = false;
					if (ok || (force && !crossing))
					{
						roomless = !Room(r, granted, box);
						ok = ok && !roomless;
						RefusedRoom += roomless ? 1 : 0;
					}
					// The guard against a car kept waiting for ever by the order: after a long wait the order is let go, never a car
					// in the box and never the room on its way out.
					bool forced = false;
					if (!ok && force && !crossing && !roomless)
					{
						ok = true;
						forced = true;
						Forced++;
					}
					if (ok)
					{
						Grant g = new Grant { M = r.M, Node = node, SegIndex = r.SegIndex, InLane = r.InLane, OutLane = r.OutLane, InKey = r.InArm, OutKey = r.OutArm, NodeP = r.NodeP, Forced = forced, Through = Math.Min(DesiredAt(r.M.L.Profile, r.NodeP), DesiredAt(r.M.L.Profile, r.NodeP + box)) };
						granted.Add(g);
						if (granted.Count == 1)
						{
							grantNodes.Add(node);
						}
						r.M.Grants.Add(g);
						r.M.Wait = 0.0;
					}
					else if (!roomless)
					{
						// Refused for crossing someone: it keeps its place, and nobody after it who crosses it goes first. A car only
						// waiting for room on its way out holds nobody else up (it would hold the road it is waiting to join).
						refused.Add(r);
					}
				}
				start = end;
			}
		}

		/// <summary>
		/// The margin a car giving way keeps: it is out of the box this long before a car with the right of way gets to it, and never
		/// sooner than the manual's gap (<see cref="SideGap"/>). Waiting no longer shrinks it (0.34.0: <see cref="GiveWayLeast"/> is the
		/// margin itself): the PA Driver's Manual has no shorter gap for an impatient driver, and a car taking one made the trunk brake.
		/// </summary>
		internal const double GiveWayMargin = 1.0, GiveWayLeast = 1.0, GiveWayPatience = 20.0;

		/// <summary>The margin of a car giving way after waiting <paramref name="waited"/> seconds.</summary>
		internal static double GiveWayAfter(double waited)
		{
			return GiveWayLeast + (GiveWayMargin - GiveWayLeast) * Math.Max(0.0, 1.0 - waited / GiveWayPatience);
		}
		/// <summary>How far along the roads into a junction a car giving way looks for cars coming, at most (and how many junctions back).</summary>
		private const double GiveWayLook = 1500.0;
		/// <summary>The fastest a car coming to a junction is reckoned to go when working out how far back to look (the trunk's limit and a bit).</summary>
		private const double GiveWayFastest = 95.0;
		private const int GiveWayDepth = 6;
		private readonly List<(int edge, int node, double before, int depth)> lookBack = new List<(int, int, double, int)>();

		/// <summary>
		/// Whether request <paramref name="r"/> must wait for a car it gives way to: one coming to the node, not yet let in, whose way
		/// crosses its own and who gets there (at its speed, speeding up to its profile) before this one is through the box and
		/// <see cref="GiveWayMargin"/>. Looks back along the lanes into the node, and past the junctions behind them a few deep.
		/// </summary>
		private bool GivesWayToComing(Request r, double box)
		{
			int node = r.Node;
			if (mainA[node] < 0)
			{
				return false;
			}
			double margin = GiveWayAfter(r.M.Wait);

			// Its own pull-away through the box, as it will drive it: past the near half, and out of the box at what speed.
			double farP = r.NodeP + box + Traffic.CarLength;
			PullAway(r.M, r.NodeP + Traffic.CarLength, farP, out double tNear, out double tFar, out double vFar);
			double gap = SideGap(node, r.InArm, r.OutArm, r.M.Wait);
			double look = Math.Min(GiveWayLook, GiveWayFastest * Math.Max(tFar + margin, gap) + box + 300.0);
			List<int> links = Traffic.Links[node];
			lookBack.Clear();
			for (int a = 0; a < links.Count; a++)
			{
				if (a != r.InArm && (a == mainA[node] || a == mainB[node]))
				{
					lookBack.Add((links[a], node, 0.0, 0));
				}
			}
			for (int i = 0; i < lookBack.Count; i++)
			{
				(int edge, int toward, double before, int depth) = lookBack[i];
				Traffic.Edge e = Traffic.Edges[edge];
				long lane0 = edge * 2L + (e.B == toward ? 1 : 0);
				for (int sub = 0; sub < (twoLaneEdge[edge] ? 4 : 1); sub++)
				{
				if (lanes.TryGetValue(lane0 + sub * InnerKey, out List<Mover> list))
				{
					foreach (Mover o in list)
					{
						if (o == r.M || o.V < 1.0)
						{
							// Standing (queued, waiting to be let in, pulling out): not coming. Once it moves off it asks for the junction
							// itself, and whoever has the box first then decides.
							continue;
						}
						Seg[] path = o.L.Path;
						for (int k = o.Seg; k <= o.Seg + depth && k + 1 < path.Length; k++)
						{
							if (NodeAfter(path, k) != node)
							{
								continue;
							}
							int inA = LinkIndex(node, path[k].Edge), outA = LinkIndex(node, path[k + 1].Edge);
							long oOut = TwoLane(path[k]) ? LaneOf(o, path[k + 1]) : LaneOf(path[k + 1]);
							if (inA == r.InArm || HasGrant(o, k) || !Conflict(node, r.InArm, r.OutArm, r.InLane, r.OutLane, inA, outA, LaneOf(o, path[k]), oOut) || !Yields(node, r.InArm, r.OutArm, inA, outA))
							{
								break;
							}
							if (TooSoon(r, box, o, k, inA, outA, tNear, tFar, vFar, margin, depth == 0))
							{
								return true;
							}
							break;
						}
					}
				}
				}
				double reach = before + e.Length;
				if (depth + 1 < GiveWayDepth && reach < look)
				{
					int back = e.A == toward ? e.B : e.A;
					foreach (int f in Traffic.Links[back])
					{
						if (f != edge)
						{
							lookBack.Add((f, back, reach, depth + 1));
						}
					}
				}
			}
			return false;
		}

		/// <summary>
		/// Whether car <paramref name="o"/>, coming to request <paramref name="r"/>'s node along its path's seg <paramref name="k"/> (in along
		/// arm <paramref name="inA"/>, out along <paramref name="outA"/>), with the right of way over it, gets there too soon for it to go:
		/// before it is past where their ways cross (<paramref name="tNear"/>, the box's near half, for a car from its left; else
		/// <paramref name="tFar"/>, the whole box) and the margin; or, joining its lane, without room for it to slow to this car's speed.
		/// </summary>
		private bool TooSoon(Request r, double box, Mover o, int k, int inA, int outA, double tNear, double tFar, double vFar, double margin, bool block = true)
		{
			int node = r.Node;
			Seg[] path = o.L.Path;
			double d = path[k].P0 + path[k].Len - box - o.P;
			if (d < -2.0 * box - Traffic.CarLength)
			{
				// Out of the box already, its back and all.
				return false;
			}
			double top = Math.Max(o.V, DesiredAt(o.L.Profile, o.P + Math.Max(0.0, d)));
			if (o.Ahead != null && o.Ahead.LaneU - o.LaneU < Traffic.CarLength + MinGap + 2.0 * Headway * top)
			{
				// In a queue: it goes no faster than the car in front of it lets it (a little more, as the queue moves off).
				top = Math.Max(o.V, Math.Min(top, o.Ahead.V + Accel));
			}
			double arrive = ArriveTime(o.V, d, top);
			// The manual's gap counts the cars within a block of the junction ("about a 6-second gap ... or about a block of clear space
			// to the right and left", p. 49): off the trunk, those on the roads straight into it; any further off need only find it clear.
			double need = (NearHalf(node, r.InArm, r.OutArm, inA, outA) ? tNear : tFar) + margin;
			if (block || twoLaneMain[node])
			{
				need = Math.Max(need, SideGap(node, r.InArm, r.OutArm, r.M.Wait));
			}
			if (arrive < need)
			{
				return true;
			}
			if (outA == r.OutArm)
			{
				// Joining its lane: out of the box ahead of it, and far enough ahead that it slows to this car's speed braking firmly
				// at most, never to a stop (more room while this car has not waited long).
				double behind = d + 2.0 * box - top * tFar;
				double dv = Math.Max(0.0, top - vFar);
				double want = MinGap + top * margin + dv * dv / (2.0 * MergeBrake) * (0.6 + margin);
				if (behind < want)
				{
					return true;
				}
			}
			return false;
		}

		// ---- the trunk's two lanes -----------------------------------------------------------------------------------------------

		/// <summary>The lanes cars were refused joining for a gap last step (the outer lanes past a junction): cars on them move over if they can.</summary>
		private readonly HashSet<long> joining = new HashSet<long>(), joined = new HashSet<long>();
		/// <summary>Seconds a car keeps to a lane after changing, and how long it takes to move over (as drawn).</summary>
		private const double KeepLane = 4.0, MoveOverTime = 2.5;
		/// <summary>How much more a car must gain to overtake, and how much it may lose to keep right (the IDM's acceleration).</summary>
		private const double OvertakeGain = 0.6, KeepRightLoss = 0.3;
		/// <summary>The hardest braking a lane change may ask of the car it moves in front of, or of itself behind its new leader.</summary>
		private const double ChangeBrake = 0.4 * Brake;
		/// <summary>A car back in the outer lane this far before it leaves the trunk; looks for a car waiting to join this far ahead.</summary>
		private const double ExitReady = 1200.0, JoinLook = 500.0;

		/// <summary>Lane changes counted for trafficview: overtaking, keeping right (and before an exit), moving over for a car joining.</summary>
		internal int Overtakes, KeptRight, MovedOver;

		/// <summary>
		/// The trunk's cars change lanes (two each way): into the inner lane to overtake a slower car, or to make room for a car waiting to
		/// join the outer lane at a junction ahead; back into the outer lane when that costs little (keep right), and always well before
		/// their exit. Never near a junction (not while asking for one or let into one), never twice within <see cref="KeepLane"/>, and
		/// only with room in the new lane: neither it nor the car it moves in front of brakes harder than <see cref="ChangeBrake"/>.
		/// In the cars' order, each seeing the changes before it. Returns whether any car changed.
		/// </summary>
		private bool LaneChanges(double w)
		{
			zipper.Clear();
			bool any = JunctionLanes(w);
			foreach (Mover m in movers)
			{
				Seg[] path = m.L.Path;
				Seg s = path[m.Seg];
				if (!TwoLane(s) || w < m.HoldUntil || m.Ix >= AuxIx || w - m.Changed < JunctionLaneKeep || !GrantsGoOn(m))
				{
					continue;
				}
				double into = m.P - s.P0;
				int ask = Asking(m);
				if (into < 30.0 || s.P0 + s.Len - m.P < 60.0 + m.V * MoveOverTime || (ask >= 0 && !(TwoLane(path[ask]) && TwoLane(path[ask + 1]))))
				{
					// Near the junction it turns off at: it keeps the lane it is in (it got there before).
					continue;
				}
				// Leaving the trunk (or its trip ends) soon: the outer lane for a turn right, the inner one for a turn left (across the
				// oncoming carriageway: it waits there for a gap, out of the way of the cars going on).
				int exitLane = !m.L.Vanishes && m.L.Length - m.P < ExitReady ? 0 : -1;
				for (int k = m.Seg + 1; exitLane < 0 && k < path.Length && path[k].P0 - m.P < ExitReady; k++)
				{
					if (!TwoLane(path[k]))
					{
						int node = NodeAfter(path, k - 1);
						exitLane = node >= 0 && twoLaneMain[node] && LeftTurn(node, LinkIndex(node, path[k - 1].Edge), LinkIndex(node, path[k].Edge)) ? 1 : 0;
					}
				}
				bool exit = exitLane >= 0;
				if (!exit && w - m.Changed < KeepLane)
				{
					// Overtaking and keeping right wait a while between changes; getting into the lane for its turn does not.
					continue;
				}
				double v0 = Math.Max(0.5, DesiredAt(m.L.Profile, m.P));
				double here = Follow(m, m.Ahead, v0);
				bool toInner;
				if (exit)
				{
					if ((m.Inner ? 1 : 0) == exitLane)
					{
						continue;
					}
					toInner = exitLane == 1;
				}
				else if (m.Inner)
				{
					toInner = false;
					long outer = LaneOf(s);
					Neighbours(outer, m, out Mover ahead, out Mover _);
					double there = Follow(m, ahead, v0);
					bool keepRight = there >= here - KeepRightLoss && !joined.Contains(NextOuter(m));
					if (!exit && !keepRight)
					{
						continue;
					}
				}
				else
				{
					toInner = true;
					long inner = LaneOf(s) + InnerKey;
					Neighbours(inner, m, out Mover ahead, out Mover _);
					double there = Follow(m, ahead, v0);
					bool overtake = there > here + OvertakeGain;
					bool courtesy = !overtake && joined.Contains(NextOuter(m));
					if (!overtake && !courtesy)
					{
						continue;
					}
				}
				long target = LaneOf(s) + (toInner ? InnerKey : 0L);
				if (!RoomIn(target, m) || !GrantsAllow(m, toInner))
				{
					if (exit)
					{
						Zip(m, target);
					}
					continue;
				}
				MoveGrants(m, toInner);
				// Moving over: out of the old lane's list, into the new one's in order (later cars this step see it there).
				if (lanes.TryGetValue(m.Lane, out List<Mover> old))
				{
					old.Remove(m);
				}
				if (!lanes.TryGetValue(target, out List<Mover> list))
				{
					list = new List<Mover>();
					lanes[target] = list;
				}
				if (list.Count == 0)
				{
					laneKeys.Add(target);
				}
				int at = 0;
				while (at < list.Count && (list[at].LaneU < m.LaneU || (list[at].LaneU == m.LaneU && list[at].Index < m.Index)))
				{
					at++;
				}
				list.Insert(at, m);
				if (toInner)
				{
					if (joined.Contains(NextOuter(m)) && Follow(m, null, v0) <= here + OvertakeGain)
					{
						MovedOver++;
					}
					else
					{
						Overtakes++;
					}
				}
				else
				{
					KeptRight++;
				}
				m.Inner = toInner;
				m.Lane = target;
				m.Changed = w;
				if (m.LaneRec == null)
				{
					m.LaneRec = new List<float>();
				}
				m.LaneRec.Add((float)m.P);
				m.LaneRec.Add((float)Math.Min(s.P0 + s.Len, m.P + Math.Max(30.0, m.V * MoveOverTime)));
				m.LaneRec.Add(toInner ? 1f : 0f);
				any = true;
			}
			return any;
		}

		// ---- the trunk's junction lanes (TrunkLanes) -----------------------------------------------------------------------------

		/// <summary>Seconds a car keeps to a lane before moving into a junction lane or merging out of one (shorter than between overtakes: it is getting where it must be).</summary>
		private const double JunctionLaneKeep = 1.5;
		/// <summary>How far ahead a car on the trunk looks for the junction it turns off at.</summary>
		private const double ExitLook = 2000.0;

		/// <summary>The lane a car going from path seg <paramref name="k"/> onto the trunk at k + 1 comes into (see the node overload).</summary>
		private static int EntryIx(Seg[] path, int k)
		{
			if (k + 1 >= path.Length || !TwoLane(path[k + 1]) || TwoLane(path[k]))
			{
				return 0;
			}
			int node = NodeAfter(path, k);
			return node < 0 ? 0 : EntryIx(node, LinkIndex(node, path[k].Edge), LinkIndex(node, path[k + 1].Edge));
		}

		/// <summary>
		/// The lane a car from a side arm comes into on the trunk at node <paramref name="node"/>: a turn right into the acceleration lane
		/// beside the near carriageway, a turn left into the median lane beside the far one (two stages), where those lanes are there;
		/// else the outer lane (0).
		/// </summary>
		internal static int EntryIx(int node, int inA, int outA)
		{
			if (node < 0 || inA < 0 || outA < 0 || mainA[node] < 0 || inA == mainA[node] || inA == mainB[node] || (outA != mainA[node] && outA != mainB[node]))
			{
				return 0;
			}
			int edge = Traffic.Links[node][outA];
			if (!twoLaneEdge[edge])
			{
				return 0;
			}
			Traffic.Edge e = Traffic.Edges[edge];
			int dir = e.A == node ? 1 : -1;
			double s = (e.A == node ? e.Sa : e.Sb) + dir * (TrunkLanes.Mouth + 5.0);
			bool left = LeftTurn(node, inA, outA);
			int kind = left ? TrunkLanes.Median : TrunkLanes.Aux;
			return TrunkLanes.Find(e.Road, kind, dir, s) >= 0 ? (left ? MedianIx : AuxIx) : 0;
		}

		/// <summary>How far left of the outer lane lane index <paramref name="ix"/> is drawn, in lane widths (as <see cref="Leg.Lanes"/> records it).</summary>
		private static float LaneValue(int ix)
		{
			return ix == 1 ? 1f : ix == AuxIx ? (float)TrunkLanes.AuxLane : ix == MedianIx ? (float)TrunkLanes.MedianLane : 0f;
		}

		/// <summary>Distance along the trunk road at path distance <paramref name="p"/> on seg <paramref name="s"/>.</summary>
		private static double RoadS(Seg s, double p)
		{
			return s.S0 + s.Dir * (p - s.P0);
		}

		/// <summary>Mover <paramref name="m"/> has just come onto the trunk from a side road: into the junction lane its turn leads to, if any.</summary>
		private void EnterTrunk(Mover m, double w)
		{
			int ix = EntryIx(m.L.Path, m.Seg - 1);
			m.Ix = ix;
			m.LaneEnd = double.MaxValue;
			// Drawn in that lane from the junction (a car that left the trunk from another lane earlier is drawn back in the outer one).
			List<float> rec = m.LaneRec;
			if (ix == 0 && (rec == null || rec[rec.Count - 1] == 0f))
			{
				return;
			}
			Seg s = m.L.Path[m.Seg];
			if (m.LaneRec == null)
			{
				m.LaneRec = new List<float>();
			}
			m.LaneRec.Add((float)s.P0);
			m.LaneRec.Add((float)(s.P0 + 1.0));
			m.LaneRec.Add(LaneValue(ix));
			m.Changed = w;
		}

		/// <summary>
		/// The junction mover <paramref name="m"/> turns off the trunk at next, within <see cref="ExitLook"/>: the path distance and road
		/// distance of its node, and whether it turns left there (across the oncoming carriageway). False if none (it goes on, or ends).
		/// </summary>
		private static bool ExitAhead(Mover m, out double exitS, out bool left)
		{
			exitS = 0.0;
			left = false;
			Seg[] path = m.L.Path;
			for (int k = m.Seg + 1; k < path.Length && path[k].P0 - m.P < ExitLook; k++)
			{
				if (TwoLane(path[k]))
				{
					continue;
				}
				int node = NodeAfter(path, k - 1);
				if (node < 0)
				{
					return false;
				}
				Seg before = path[k - 1];
				exitS = RoadS(before, before.P0 + before.Len);
				left = LeftTurn(node, LinkIndex(node, before.Edge), LinkIndex(node, path[k].Edge));
				return true;
			}
			return false;
		}

		/// <summary>Whether junction lane <paramref name="zone"/> leads to a junction at road distance <paramref name="exitS"/> (it ends at that junction's mouth, or runs on through it).</summary>
		private static bool Leads(int zone, double exitS)
		{
			TrunkLanes.Zone z = TrunkLanes.Zones[zone];
			double reach = TrunkLanes.Mouth + 10.0;
			return z.Dir > 0 ? exitS >= z.S0 && exitS <= z.S1 + reach : exitS <= z.S1 && exitS >= z.S0 - reach;
		}

		/// <summary>Path distance where junction lane <paramref name="zone"/> ends ahead of mover <paramref name="m"/> (MaxValue if past its trunk path).</summary>
		private static double LaneEndOf(Mover m, int zone)
		{
			double end = TrunkLanes.EndOf(zone);
			int road = TrunkLanes.Zones[zone].Road;
			Seg[] path = m.L.Path;
			for (int k = m.Seg; k < path.Length && TwoLane(path[k]); k++)
			{
				Seg s = path[k];
				if (s.Edge < 0 || Traffic.Edges[s.Edge].Road != road)
				{
					continue;
				}
				double a = RoadS(s, s.P0), b = RoadS(s, s.P0 + s.Len);
				if ((end - a) * (end - b) <= 0.0)
				{
					return s.P0 + (end - s.S0) * s.Dir;
				}
			}
			return double.MaxValue;
		}

		/// <summary>
		/// Cars into and out of the trunk's junction lanes: a car turning right off it moves into the deceleration lane once in it (it is
		/// in the outer lane by then), a car turning left into the median's turn lane (from the inner lane); a car in an acceleration or
		/// median lane it does not turn off from merges once up to speed and given room (the cars on the trunk brake gently at most), and
		/// stops at the lane's end if none comes. Before the other lane changes each step.
		/// </summary>
		private bool JunctionLanes(double w)
		{
			bool any = false;
			foreach (Mover m in movers)
			{
				Seg[] path = m.L.Path;
				Seg s = path[m.Seg];
				if (!TwoLane(s) || s.Edge < 0 || w < m.HoldUntil || w - m.Changed < JunctionLaneKeep)
				{
					continue;
				}
				int road = Traffic.Edges[s.Edge].Road, dir = s.Dir > 0 ? 1 : -1;
				double rs = RoadS(s, m.P);
				bool exit = ExitAhead(m, out double exitS, out bool left);
				if (m.Ix < AuxIx)
				{
					if (exit && m.Ix != (left ? 1 : 0) && GrantsGoOn(m) && ExitNear(m) < MissAt)
					{
						// Still in the wrong lane for its turn this close to it: it goes on, and takes the next way round.
						if (Miss(m))
						{
							Missed++;
							continue;
						}
						// No way round but back (a turn left off the trunk): slowed in the outer lane, the inner one clear beside it, it
						// goes over into the median's turn lane rather than stand in a through lane.
						if (left && m.Ix == 0 && m.V < 5.0)
						{
							int mz = TrunkLanes.Find(road, TrunkLanes.Median, dir, rs);
							long median = LaneKey(s, MedianIx);
							if (mz >= 0 && Leads(mz, exitS) && RoomIn(LaneKey(s, 1), m) && RoomIn(median, m) && GrantsAllowIx(m, MedianIx))
							{
								IntoJunctionLanes++;
								Switch(m, median, MedianIx, w);
								any = true;
							}
						}
						continue;
					}
					if (!exit || m.Ix != (left ? 1 : 0) || !GrantsGoOn(m))
					{
						continue;
					}
					int zone = TrunkLanes.Find(road, left ? TrunkLanes.Median : TrunkLanes.Aux, dir, rs);
					if (zone < 0 || !Leads(zone, exitS))
					{
						continue;
					}
					int ix = left ? MedianIx : AuxIx;
					long target = LaneKey(s, ix);
					if (!RoomIn(target, m) || !GrantsAllowIx(m, ix))
					{
						Zip(m, target);
						continue;
					}
					IntoJunctionLanes++;
					Switch(m, target, ix, w);
					any = true;
					continue;
				}
				int kind = m.Ix == AuxIx ? TrunkLanes.Aux : TrunkLanes.Median;
				int here = TrunkLanes.Find(road, kind, dir, rs);
				if (here < 0)
				{
					// In a junction's mouth: the lane it is turning off from ends just behind, the one it has come into begins just ahead.
					here = TrunkLanes.Find(road, kind, dir, rs - dir * (TrunkLanes.Mouth + 12.0));
					if (here < 0 || !(exit && left == (m.Ix == MedianIx) && Leads(here, exitS)))
					{
						int ahead = TrunkLanes.Find(road, kind, dir, rs + dir * (TrunkLanes.Mouth + 12.0));
						here = ahead >= 0 ? ahead : here;
					}
				}
				if (here >= 0 && exit && left == (m.Ix == MedianIx) && Leads(here, exitS))
				{
					// It turns off from this lane.
					m.LaneEnd = double.MaxValue;
					continue;
				}
				m.LaneEnd = here >= 0 ? LaneEndOf(m, here) : m.P;
				double v0 = Math.Max(0.5, DesiredAt(m.L.Profile, m.P));
				// Up to speed first; but a car turning off soon across the other lanes goes over at once (given room).
				bool hurry = exit && (left != (m.Ix == MedianIx));
				if (!hurry && m.V < 0.85 * v0 && m.LaneEnd - m.P > 150.0)
				{
					continue;
				}
				int back = m.Ix == AuxIx ? 0 : 1;
				long into = LaneKey(s, back);
				if (!GrantsGoOn(m) || !RoomIn(into, m) || !GrantsAllowIx(m, back))
				{
					continue;
				}
				Merged++;
				m.LaneEnd = double.MaxValue;
				Switch(m, into, back, w);
				any = true;
			}
			return any;
		}

		/// <summary>
		/// For mover <paramref name="m"/>, in the lane next to the junction lane it turns off from (the outer beside a deceleration lane,
		/// the inner beside the median's): the lane distance of the last car standing in that lane's queue ahead of it, within reach
		/// of its junction, if it could not move in beside it now (MaxValue: no such queue, or it has room).
		/// </summary>
		private double TurnQueueTail(Mover m)
		{
			if (!ExitAhead(m, out double exitS, out bool left) || m.Ix != (left ? 1 : 0))
			{
				return double.MaxValue;
			}
			Seg s = m.L.Path[m.Seg];
			int road = Traffic.Edges[s.Edge].Road, dir = s.Dir > 0 ? 1 : -1;
			int zone = TrunkLanes.Find(road, left ? TrunkLanes.Median : TrunkLanes.Aux, dir, RoadS(s, m.P));
			if (zone < 0 || !Leads(zone, exitS) || !lanes.TryGetValue(LaneKey(s, left ? MedianIx : AuxIx), out List<Mover> list))
			{
				return double.MaxValue;
			}
			double tail = double.MaxValue;
			foreach (Mover o in list)
			{
				// Ahead of it (or drawing level), moving or not: the lane's last car is the nearest such. It keeps behind it until it can
				// move in, never level with it, where it could not.
				if (o.LaneU > m.LaneU - Traffic.CarLength && o.LaneU < tail)
				{
					tail = o.LaneU;
				}
			}
			// Only behind it: one already alongside or let into the junction goes on as it is.
			if (tail == double.MaxValue || tail - m.LaneU > 400.0 || m.Grants.Count > 0)
			{
				return double.MaxValue;
			}
			// Its place is behind that car: once there it moves in (JunctionLanes).
			return tail;
		}

		/// <summary>This step's cars slowed in a queue beside the lane they must get into for their turn: the lane and their spot along it.</summary>
		private readonly List<(long lane, double u)> zipper = new List<(long lane, double u)>();

		/// <summary>
		/// Mover <paramref name="m"/> could not move into lane <paramref name="target"/> for its turn: if it is slowed in a queue there,
		/// the car coming behind it in that lane leaves it a gap (as drivers let one another in, in turn, at a slow queue).
		/// </summary>
		private void Zip(Mover m, long target)
		{
			if (m.V < 5.0)
			{
				zipper.Add((target, m.LaneU));
			}
		}

		/// <summary>How near the junction it turns off at a car still in the wrong lane for its turn gives it up and goes on.</summary>
		private const double MissAt = 220.0;

		/// <summary>Turns given up in the day (the car went on and took another way).</summary>
		internal int Missed;

		/// <summary>Path distance from mover <paramref name="m"/> to the node it turns off the trunk at (MaxValue if none ahead).</summary>
		private static double ExitNear(Mover m)
		{
			Seg[] path = m.L.Path;
			for (int k = m.Seg + 1; k < path.Length && path[k].P0 - m.P < ExitLook; k++)
			{
				if (!TwoLane(path[k]))
				{
					return path[k].P0 - m.P;
				}
			}
			return double.MaxValue;
		}

		/// <summary>
		/// "If you miss your exit, take the next one" (PA Driver's Manual p. 52): mover <paramref name="m"/>, unable to get into the lane
		/// for its turn off the trunk, goes straight on through that junction instead and its path from there is routed afresh to the
		/// same end (never turning back along the stretch past the junction). False if no such way (it keeps its path).
		/// </summary>
		private bool Miss(Mover m)
		{
			Leg leg = m.L;
			Seg[] path = leg.Path;
			int k = m.Seg;
			while (k + 1 < path.Length && TwoLane(path[k + 1]))
			{
				k++;
			}
			int node = NodeAfter(path, k);
			if (node < 0 || mainA[node] < 0 || k + 1 >= path.Length)
			{
				return false;
			}
			int inA = LinkIndex(node, path[k].Edge);
			if (inA != mainA[node] && inA != mainB[node])
			{
				return false;
			}
			int straight = inA == mainA[node] ? mainB[node] : mainA[node];
			int e2 = Traffic.Links[node][straight];
			if (!twoLaneEdge[e2])
			{
				return false;
			}
			Traffic.Edge edge = Traffic.Edges[e2];
			int dir2 = edge.A == node ? 1 : -1;
			double nearS = dir2 > 0 ? edge.Sa : edge.Sb;
			Traffic.Anchor a = OffJunctions(new Traffic.Anchor { Edge = e2, S = nearS });
			// Where it was going: the end of its path (before the tunnel, if it ends in one).
			Seg last = path[path.Length - 1];
			int pb = -1;
			if (last.Edge < 0)
			{
				if (path.Length < 2 || last.Inbound)
				{
					return false;
				}
				pb = last.Portal;
				last = path[path.Length - 2];
			}
			if (last.Edge < 0)
			{
				return false;
			}
			Traffic.Anchor b = new Traffic.Anchor { Edge = last.Edge, S = last.S0 + last.Dir * last.Len };
			List<Seg> segs = new List<Seg>();
			for (int j = 0; j <= k; j++)
			{
				segs.Add(path[j]);
			}
			segs.Add(new Seg { Edge = e2, Dir = dir2, S0 = nearS, Len = Math.Abs(a.S - nearS), Speed = EdgeSpeed(e2), Portal = -1 });
			int before = segs.Count;
			if (!Way(a, -1, b, pb, segs) || segs.Count <= before || segs[before].Edge != e2 || segs[before].Dir != dir2)
			{
				// The quickest way from there turns back: on along the trunk to its next junction, and the best way from each road
				// leaving that (never back along this stretch).
				segs.RemoveRange(before - 1, segs.Count - (before - 1));
				int n2 = dir2 > 0 ? edge.B : edge.A;
				List<Seg> best = null;
				double bestLen = double.MaxValue;
				for (int f = 0; f < Traffic.Links[n2].Count; f++)
				{
					int ef = Traffic.Links[n2][f];
					if (ef == e2)
					{
						continue;
					}
					Traffic.Edge fe = Traffic.Edges[ef];
					int fdir = fe.A == n2 ? 1 : -1;
					double fs = fdir > 0 ? fe.Sa : fe.Sb;
					Traffic.Anchor fa = OffJunctions(new Traffic.Anchor { Edge = ef, S = fs });
					List<Seg> trial = new List<Seg>
					{
						new Seg { Edge = e2, Dir = dir2, S0 = nearS, Len = edge.Length, Speed = EdgeSpeed(e2), Portal = -1 },
						new Seg { Edge = ef, Dir = fdir, S0 = fs, Len = Math.Abs(fa.S - fs), Speed = EdgeSpeed(ef), Portal = -1 },
					};
					if (!Way(fa, -1, b, pb, trial) || trial.Count <= 2 || trial[2].Edge != ef || trial[2].Dir != fdir)
					{
						continue;
					}
					double len = 0.0;
					foreach (Seg t in trial)
					{
						len += t.Len;
					}
					if (len < bestLen)
					{
						bestLen = len;
						best = trial;
					}
				}
				if (best == null)
				{
					return false;
				}
				segs.AddRange(best);
			}
			leg.Path = Finish(segs, out double length);
			leg.Length = length;
			leg.Profile = Profile(leg);
			return true;
		}

		/// <summary>Cars moved into a junction lane to turn off, and merged out of one, in the day.</summary>
		internal int IntoJunctionLanes, Merged;

		/// <summary>Mover <paramref name="m"/> moved into lane <paramref name="target"/> (its index <paramref name="ix"/>): out of the old lane's list, into the new one's in order, its grants with it, the change recorded.</summary>
		private void Switch(Mover m, long target, int ix, double w)
		{
			foreach (Grant g in m.Grants)
			{
				Seg[] path = m.L.Path;
				g.InLane = LaneKey(path[g.SegIndex], ix);
				g.OutLane = LaneKey(path[g.SegIndex + 1], ix);
			}
			if (lanes.TryGetValue(m.Lane, out List<Mover> old))
			{
				old.Remove(m);
			}
			if (!lanes.TryGetValue(target, out List<Mover> list))
			{
				list = new List<Mover>();
				lanes[target] = list;
			}
			if (list.Count == 0)
			{
				laneKeys.Add(target);
			}
			int at = 0;
			while (at < list.Count && (list[at].LaneU < m.LaneU || (list[at].LaneU == m.LaneU && list[at].Index < m.Index)))
			{
				at++;
			}
			list.Insert(at, m);
			m.Ix = ix;
			m.Lane = target;
			m.Changed = w;
			Seg s = m.L.Path[m.Seg];
			if (m.LaneRec == null)
			{
				m.LaneRec = new List<float>();
			}
			m.LaneRec.Add((float)m.P);
			m.LaneRec.Add((float)Math.Min(s.P0 + s.Len, m.P + Math.Max(30.0, m.V * MoveOverTime)));
			m.LaneRec.Add(LaneValue(ix));
		}

		/// <summary>As <see cref="GrantsAllow"/>, into lane index <paramref name="ix"/>.</summary>
		private bool GrantsAllowIx(Mover m, int ix)
		{
			foreach (Grant g in m.Grants)
			{
				long outLane = LaneKey(m.L.Path[g.SegIndex + 1], ix);
				foreach (Grant o in nodeGrants[g.Node])
				{
					if (o.M != m && o.OutLane == outLane && o.InKey != g.InKey)
					{
						return false;
					}
				}
			}
			return true;
		}

		/// <summary>How far before a trunk junction the cars going on keep to the outer lane (the inner one is the turning lane there).</summary>
		private const double PocketLength = 400.0;

		/// <summary>Whether mover <paramref name="m"/> comes to a junction on the trunk's main line within <see cref="PocketLength"/>, going on through it.</summary>
		private static bool PocketAhead(Mover m)
		{
			Seg[] path = m.L.Path;
			for (int k = m.Seg; k + 1 < path.Length && path[k].P0 + path[k].Len - m.P < PocketLength; k++)
			{
				int node = NodeAfter(path, k);
				if (node >= 0 && twoLaneMain[node])
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>Whether every junction mover <paramref name="m"/> has been let into is one it goes straight on through on the trunk (it may still change lanes).</summary>
		private static bool GrantsGoOn(Mover m)
		{
			foreach (Grant g in m.Grants)
			{
				Seg[] path = m.L.Path;
				if (!TwoLane(path[g.SegIndex]) || !TwoLane(path[g.SegIndex + 1]))
				{
					return false;
				}
			}
			return true;
		}

		/// <summary>Whether mover <paramref name="m"/>, let into junctions ahead, can take its grants to the other lane: nobody else let in leaves by that lane there.</summary>
		private bool GrantsAllow(Mover m, bool toInner)
		{
			foreach (Grant g in m.Grants)
			{
				long outLane = LaneKey(m.L.Path[g.SegIndex + 1], toInner ? 1 : 0);
				foreach (Grant o in nodeGrants[g.Node])
				{
					if (o.M != m && o.OutLane == outLane && o.InKey != g.InKey)
					{
						return false;
					}
				}
			}
			return true;
		}

		/// <summary>Mover <paramref name="m"/>'s grants moved with it to the other lane.</summary>
		private static void MoveGrants(Mover m, bool toInner)
		{
			foreach (Grant g in m.Grants)
			{
				Seg[] path = m.L.Path;
				g.InLane = LaneKey(path[g.SegIndex], toInner ? 1 : 0);
				g.OutLane = LaneKey(path[g.SegIndex + 1], toInner ? 1 : 0);
			}
		}

		/// <summary>The outer lane mover <paramref name="m"/> leaves the next junction by, if that is near and it stays on the trunk (else -1).</summary>
		private static long NextOuter(Mover m)
		{
			Seg[] path = m.L.Path;
			for (int k = m.Seg; k + 1 < path.Length && path[k].P0 + path[k].Len - m.P < JoinLook; k++)
			{
				if (NodeAfter(path, k) >= 0)
				{
					return TwoLane(path[k + 1]) ? LaneOf(path[k + 1]) : -1L;
				}
			}
			return -1L;
		}

		/// <summary>The IDM's acceleration of mover <paramref name="m"/> on a free road to <paramref name="v0"/> behind <paramref name="ahead"/> (null: none).</summary>
		private static double Follow(Mover m, Mover ahead, double v0)
		{
			double q = m.V / v0;
			double a = Accel * (1.0 - q * q * q * q);
			if (ahead != null)
			{
				a += Interaction(m.V, ahead.LaneU - Traffic.CarLength - m.LaneU, ahead.V);
			}
			return a;
		}

		/// <summary>The cars just ahead of and behind mover <paramref name="m"/>'s place in lane <paramref name="lane"/> (null where none).</summary>
		private void Neighbours(long lane, Mover m, out Mover ahead, out Mover behind)
		{
			ahead = behind = null;
			if (!lanes.TryGetValue(lane, out List<Mover> list))
			{
				return;
			}
			foreach (Mover o in list)
			{
				if (o == m)
				{
					continue;
				}
				if (o.LaneU >= m.LaneU)
				{
					if (ahead == null || o.LaneU < ahead.LaneU)
					{
						ahead = o;
					}
				}
				else if (behind == null || o.LaneU > behind.LaneU)
				{
					behind = o;
				}
			}
		}

		/// <summary>
		/// Whether mover <paramref name="m"/> has room to move into lane <paramref name="lane"/> beside it: for the cars there, and for the
		/// time it is drawn moving over (<see cref="MoveOverTime"/>), when it is in both lanes: neither car closes on it in that time, in
		/// the new lane or the one it leaves.
		/// </summary>
		private bool RoomIn(long lane, Mover m)
		{
			Neighbours(lane, m, out Mover ahead, out Mover behind);
			if (ahead != null)
			{
				double gap = ahead.LaneU - Traffic.CarLength - m.LaneU;
				if (gap < MinGap + Math.Max(0.0, m.V - ahead.V) * MoveOverTime || Interaction(m.V, gap, ahead.V) < -ChangeBrake)
				{
					return false;
				}
			}
			if (behind != null)
			{
				double gap = m.LaneU - Traffic.CarLength - behind.LaneU;
				if (gap < MinGap + Math.Max(0.0, behind.V - m.V) * MoveOverTime || Interaction(behind.V, gap, m.V) < -ChangeBrake)
				{
					return false;
				}
			}
			Mover before = m.Ahead;
			if (before != null && before.LaneU - Traffic.CarLength - m.LaneU < MinGap + Math.Max(0.0, m.V - before.V) * MoveOverTime)
			{
				return false;
			}
			Neighbours(m.Lane, m, out Mover _, out Mover after);
			if (after != null && m.LaneU - Traffic.CarLength - after.LaneU < MinGap + Math.Max(0.0, after.V - m.V) * MoveOverTime)
			{
				return false;
			}
			return true;
		}

		/// <summary>The braking a car joining a main line may ask of the car coming behind it on it (firm, about 5 m/s2), never a stop.</summary>
		private const double MergeBrake = 2.0 * Brake;

		/// <summary>
		/// When car <paramref name="m"/>, pulling away now on a free road (the IDM's free term on its profile, braking for a slower
		/// stretch ahead), gets to path distances <paramref name="nearP"/> and <paramref name="farP"/>, and its speed at the far one.
		/// Its real pull-away, not a guess at it: it eases off to the turn's speed, and stands a little short of the box.
		/// </summary>
		private static void PullAway(Mover m, double nearP, double farP, out double tNear, out double tFar, out double vFar)
		{
			float[] profile = m.L.Profile;
			double p = m.P, v = m.V, t = 0.0;
			tNear = -1.0;
			while (p < farP && t < 40.0)
			{
				double v0 = Math.Max(0.5, DesiredAt(profile, p));
				double q = v / v0;
				double a = Accel * (1.0 - q * q * q * q);
				double reach = v * v / (2.0 * Brake) + 2.0 * Sample;
				for (int i = (int)Math.Floor(p / Sample) + 1; i < profile.Length && i * Sample - p <= reach; i++)
				{
					double vs = profile[i];
					if (vs < v)
					{
						double d = i * Sample - p;
						a = Math.Min(a, d > 0.5 ? (vs * vs - v * v) / (2.0 * d) : -Brake);
					}
				}
				v = Math.Max(0.0, v + Math.Min(Accel, a) * Step);
				p += v * Step;
				t += Step;
				if (tNear < 0.0 && p >= nearP)
				{
					tNear = t;
				}
			}
			tFar = t;
			vFar = v;
			if (tNear < 0.0)
			{
				tNear = t;
			}
		}

		/// <summary>The soonest a car at speed <paramref name="v"/> could cover <paramref name="d"/>: speeding up at the IDM's acceleration to <paramref name="top"/>.</summary>
		private static double ArriveTime(double v, double d, double top)
		{
			if (d <= 0.0)
			{
				return 0.0;
			}
			top = Math.Max(top, Math.Max(v, 1.0));
			double t1 = (top - v) / Accel, d1 = (v + top) * 0.5 * t1;
			if (d1 >= d)
			{
				return (-v + Math.Sqrt(v * v + 2.0 * Accel * d)) / Accel;
			}
			return t1 + (d - d1) / top;
		}

		/// <summary>Seconds to cover <paramref name="d"/> from speed <paramref name="v"/>, pulling away at the IDM's acceleration up to a turn's speed.</summary>
		internal static double ClearTime(double v, double d)
		{
			if (d <= 0.0)
			{
				return 0.0;
			}
			if (v >= TurnSpeed)
			{
				return d / v;
			}
			double t1 = (TurnSpeed - v) / Accel, d1 = (v + TurnSpeed) * 0.5 * t1;
			if (d1 >= d)
			{
				return (-v + Math.Sqrt(v * v + 2.0 * Accel * d)) / Accel;
			}
			return t1 + (d - d1) / TurnSpeed;
		}

		/// <summary>Whether the way out has room past the box for this car and those already let in ahead of it: never block a junction.</summary>
		private bool Room(Request r, List<Grant> granted, double box)
		{
			int pending = 0;
			foreach (Grant g in granted)
			{
				// Let in and not yet through, unless it is behind this car in the same lane (a car let in or pulled out in front of
				// it): then this one goes first, and counting it would leave both waiting for ever.
				if (g.OutLane == r.OutLane && g.M != r.M && g.M.P < g.NodeP && !(g.InLane == r.InLane && g.M.Lane == r.M.Lane && g.M.LaneU < r.M.LaneU))
				{
					pending++;
				}
			}
			double need = box + Traffic.CarLength + 4.0 + pending * (Traffic.CarLength + 4.0);
			Seg outSeg = r.M.L.Path[r.SegIndex + 1];
			double outLength = outSeg.Edge >= 0 ? Traffic.Edges[outSeg.Edge].Length : outSeg.Len;
			if (!lanes.TryGetValue(r.OutLane, out List<Mover> list) || list.Count == 0)
			{
				return pending == 0 || outLength >= need;
			}
			Mover first = null;
			foreach (Mover o in list)
			{
				if (o != r.M && (first == null || o.LaneU < first.LaneU))
				{
					first = o;
				}
			}
			if (first == null)
			{
				return pending == 0 || outLength >= need;
			}
			// By distance only: a car let in behind one that is moving, which then stops, would stand in the box and hold it.
			if (CrossesTraffic(r.Node, r.InArm, r.OutArm))
			{
				// Across another stream of traffic (a carriageway of the trunk, an oncoming lane, a main road from side to side): stopping
				// halfway would hold it up, so the way out must be moving, or clear well past the box. "Do not enter an intersection if
				// there are vehicles blocking your way. Wait until you are sure you can clear the intersection" (PA Driver's Manual p. 46).
				return first.LaneU - Traffic.CarLength > need + (first.V > TurnSpeed * 0.5 ? 0.0 : 3.0 * (Traffic.CarLength + MinGap));
			}
			return first.LaneU - Traffic.CarLength > need;
		}

		/// <summary>Steps a car stood still in a trunk junction's box crossing a carriageway (holding up all its lanes), and how many cars did.</summary>
		internal int StoppedAcross;
		/// <summary>Seconds cars stood still on the trunk going on along it, and waiting to turn off it.</summary>
		internal double TrunkStoppedOn, TrunkStoppedTurning;
		internal int StoppedAcrossCars => stoppedAcross.Count;
		private readonly HashSet<int> stoppedAcross = new HashSet<int>();
		internal readonly List<string> StoppedAcrossNotes = new List<string>();

		/// <summary>Grants end once the car is a car's length past the box.</summary>
		private void Release()
		{
			foreach (Mover m in movers)
			{
				if (m.V < 0.5)
				{
					foreach (Grant g in m.Grants)
					{
						if (Math.Abs(m.P - g.NodeP) < Traffic.Nodes[g.Node].Box && CrossesCarriageway(g.Node, g.InKey, g.OutKey))
						{
							StoppedAcross++;
							if (stoppedAcross.Add(m.Index) && StoppedAcrossNotes.Count < 8)
							{
								StoppedAcrossNotes.Add("leg " + m.Index + " node " + g.Node + " " + g.InKey + ">" + g.OutKey + " at " + (m.P - g.NodeP).ToString("0") + " box " + Traffic.Nodes[g.Node].Box.ToString("0") + " ahead " + (m.Ahead != null ? (m.Ahead.LaneU - m.LaneU).ToString("0") + " v " + m.Ahead.V.ToString("0.0") : "-") + " lane " + m.Lane);
							}
						}
					}
				}
				for (int i = m.Grants.Count - 1; i >= 0; i--)
				{
					Grant g = m.Grants[i];
					double box = Traffic.Nodes[g.Node].Box;
					if (m.P > g.NodeP + box + Traffic.CarLength)
					{
						nodeGrants[g.Node].Remove(g);
						m.Grants.RemoveAt(i);
					}
				}
			}
		}

		/// <summary>Counts pairs of cars whose movements cross inside one junction box at once.</summary>
		private void CheckBoxes()
		{
			for (int i = grantNodes.Count - 1; i >= 0; i--)
			{
				int node = grantNodes[i];
				List<Grant> list = nodeGrants[node];
				if (list.Count == 0)
				{
					grantNodes.RemoveAt(i);
					continue;
				}
				double box = Traffic.Nodes[node].Box;
				for (int a = 0; a < list.Count; a++)
				{
					Grant ga = list[a];
					if (Math.Abs(ga.M.P - ga.NodeP) >= box)
					{
						continue;
					}
					for (int b = a + 1; b < list.Count; b++)
					{
						Grant gb = list[b];
						if (Math.Abs(gb.M.P - gb.NodeP) >= box || !Conflict(node, ga.InKey, ga.OutKey, ga.InLane, ga.OutLane, gb.InKey, gb.OutKey, gb.InLane, gb.OutLane))
						{
							continue;
						}
						// One from the other's left crosses it in that one's near half only: past the middle (its length and all), it is clear.
						if ((NearHalf(node, ga.InKey, ga.OutKey, gb.InKey, gb.OutKey) && ga.M.P - ga.NodeP >= Traffic.CarLength)
							|| (NearHalf(node, gb.InKey, gb.OutKey, ga.InKey, ga.OutKey) && gb.M.P - gb.NodeP >= Traffic.CarLength))
						{
							continue;
						}
						if (ConflictNotes.Count < 14 && !conflictPairs.Contains(((long)Math.Min(ga.M.Index, gb.M.Index) << 32) | (uint)Math.Max(ga.M.Index, gb.M.Index)))
						{
							ConflictNotes.Add("node " + node + " arms " + Traffic.Links[node].Count + " box " + box.ToString("0") + ": leg " + ga.M.Index + " " + ga.InKey + ">" + ga.OutKey + " at " + (ga.M.P - ga.NodeP).ToString("0") + " v " + ga.M.V.ToString("0.0") + " forced " + ga.Forced
								+ " / leg " + gb.M.Index + " " + gb.InKey + ">" + gb.OutKey + " at " + (gb.M.P - gb.NodeP).ToString("0") + " v " + gb.M.V.ToString("0.0") + " forced " + gb.Forced);
						}
						BoxConflicts++;
						conflictPairs.Add(((long)Math.Min(ga.M.Index, gb.M.Index) << 32) | (uint)Math.Max(ga.M.Index, gb.M.Index));
					}
				}
			}
		}

		internal int OverlapPairs => overlapPairs.Count;
		internal int ConflictPairs => conflictPairs.Count;

		/// <summary>Whether a car is in a tunnel's mouth or within 60 of it.</summary>
		private static bool NearMouth(Mover m)
		{
			Seg[] path = m.L.Path;
			Seg s = path[m.Seg];
			if (s.Edge < 0)
			{
				return true;
			}
			if (m.Seg + 1 < path.Length && path[m.Seg + 1].Edge < 0 && s.P0 + s.Len - m.P < 60.0)
			{
				return true;
			}
			return m.Seg > 0 && path[m.Seg - 1].Edge < 0 && m.P - s.P0 < 60.0;
		}

		private double Acceleration(Mover m)
		{
			Leg leg = m.L;
			Seg[] path = leg.Path;
			double v = m.V;
			double v0 = Math.Max(0.5, DesiredAt(leg.Profile, m.P));
			double r = v / v0;
			double a = Accel * (1.0 - r * r * r * r);
			// A slower stretch ahead (a bend, a turn, a lower limit): brake for it in time, as the profile was built to allow, rather
			// than trail it as the IDM's free term would.
			float[] profile = leg.Profile;
			double reach = v * v / (2.0 * Brake) + 2.0 * Sample;
			for (int i = (int)Math.Floor(m.P / Sample) + 1; i < profile.Length && i * Sample - m.P <= reach; i++)
			{
				double vs = profile[i];
				if (vs >= v)
				{
					continue;
				}
				double d = i * Sample - m.P;
				a = Math.Min(a, d > 0.5 ? (vs * vs - v * v) / (2.0 * d) : -Brake);
			}
			double worst = 0.0;
			// The car in front along this car's own path.
			bool found = false;
			if (m.Ahead != null)
			{
				double gap = m.Ahead.LaneU - Traffic.CarLength - m.LaneU;
				worst = Math.Min(worst, Interaction(v, gap, m.Ahead.V));
				found = true;
			}
			for (int k = m.Seg + 1; !found && k < path.Length && path[k].P0 - m.P < Ahead; k++)
			{
				if (!lanes.TryGetValue(LaneOf(m, path[k]), out List<Mover> list) || list.Count == 0)
				{
					continue;
				}
				double u0 = LaneU(path[k], path[k].P0), uEnd = u0 + path[k].Len;
				foreach (Mover o in list)
				{
					if (o == m || o.LaneU < u0)
					{
						continue;
					}
					if (o.LaneU - Traffic.CarLength > uEnd + 1.0)
					{
						break;
					}
					double gap = path[k].P0 - m.P + (o.LaneU - u0) - Traffic.CarLength;
					worst = Math.Min(worst, Interaction(v, gap, o.V));
					found = true;
					break;
				}
			}
			// A car waiting to pull out just ahead: leave it a car's length and a little, if it can stop for it comfortably.
			foreach ((long lane, double u) in letIn)
			{
				if (lane != m.Lane)
				{
					continue;
				}
				double room = u - Traffic.CarLength - 2.0 - m.LaneU;
				if (room > 0.0 && room < 80.0 && v * v / (2.0 * Brake) < room && (m.Ahead == null || m.Ahead.LaneU > u))
				{
					worst = Math.Min(worst, Interaction(v, room + MinGap - 1.0, 0.0));
				}
			}
			// A car beside it waiting to come into this lane for its turn, in a slow queue: leave it room, if it can stop for it gently.
			foreach ((long lane, double u) in zipper)
			{
				if (lane != m.Lane || u <= m.LaneU)
				{
					continue;
				}
				double room = u - Traffic.CarLength - 2.0 - m.LaneU;
				if (room > 0.0 && room < 60.0 && v * v / (2.0 * 0.5 * Brake) < room && (m.Ahead == null || m.Ahead.LaneU > u))
				{
					worst = Math.Min(worst, Interaction(v, room + MinGap - 1.0, 0.0));
				}
			}
			// A junction it has not been let into: stop at the box's edge.
			int ask = Asking(m);
			if (ask >= 0)
			{
				int node = NodeAfter(path, ask);
				double stop = path[ask].P0 + path[ask].Len - Traffic.Nodes[node].Box;
				worst = Math.Min(worst, Interaction(v, stop + MinGap - 1.0 - m.P, 0.0));
				if (v < 0.5)
				{
					m.Wait += Step;
					WaitAt.TryGetValue(node, out double sec);
					WaitAt[node] = sec + Step;
					{
						int ia = LinkIndex(node, path[ask].Edge), oa = LinkIndex(node, path[ask + 1].Edge);
						int cat = mainA[node] < 0 ? 3 : (ia != mainA[node] && ia != mainB[node]) ? 0 : (oa == mainA[node] || oa == mainB[node]) ? 1 : 2;
						WaitKind.TryGetValue(node * 4 + cat, out double s2);
						WaitKind[node * 4 + cat] = s2 + Step;
					}
				}
			}
			// Turning off soon from a junction lane it is not yet in: it does not pull up beside that lane's queue (where it could not
			// get in, and would stand in a through lane), but holds back behind its last car and slips in there.
			if (m.Ix < AuxIx && TwoLane(path[m.Seg]) && path[m.Seg].Edge >= 0)
			{
				double tail = TurnQueueTail(m);
				if (tail < double.MaxValue)
				{
					// Standing a little further back than behind a car in its own lane, so the move in asks nothing hard of it.
					worst = Math.Min(worst, Interaction(v, tail - Traffic.CarLength - MinGap - 6.0 - m.LaneU, 0.0));
				}
			}
			// The end of the junction lane it is to merge from: no further (it waits there for a gap, out of everyone's way).
			if (m.Ix >= AuxIx && m.LaneEnd < double.MaxValue)
			{
				double left = m.LaneEnd - m.P;
				if (left < v * v / (2.0 * 0.6 * Brake) + 30.0)
				{
					worst = Math.Min(worst, Interaction(v, left + MinGap - 1.0, 0.0));
				}
			}
			// The end: stop at the spot.
			if (!leg.Vanishes)
			{
				if (leg.Length - m.P < 200.0)
				{
					worst = Math.Min(worst, Interaction(v, leg.Length + MinGap - 0.5 - m.P, 0.0));
				}
			}
			return Math.Max(-30.0, Math.Min(Accel, a + worst));
		}

		// ---- reading the day -----------------------------------------------------------------------------------------------------

		/// <summary>Path distance of leg <paramref name="leg"/> at sim time <paramref name="t"/> (within its run).</summary>
		internal static double DistanceAt(Leg leg, double t)
		{
			double f = (t - leg.Start) / RecordStep;
			int n = leg.Rec.Length - 1;
			// The last value is at End, the others every half second from Start.
			double lastT = leg.Start + (n - 1) * RecordStep;
			if (n <= 0)
			{
				return leg.Rec[0];
			}
			if (t >= lastT)
			{
				double span = leg.End - lastT;
				double k = span > 1e-9 ? Math.Min(1.0, (t - lastT) / span) : 1.0;
				return leg.Rec[n - 1] + (leg.Rec[n] - leg.Rec[n - 1]) * k;
			}
			int i = (int)Math.Floor(f);
			i = Math.Max(0, Math.Min(n - 2, i));
			double fr = Math.Max(0.0, Math.Min(1.0, f - i));
			return leg.Rec[i] + (leg.Rec[i + 1] - leg.Rec[i]) * fr;
		}

		/// <summary>The point, heading and place of a leg's path at distance <paramref name="p"/>, the lane offset to the right of travel applied.</summary>
		internal static void OnPath(Leg leg, double p, ref Place at)
		{
			Seg[] path = leg.Path;
			int lo = 0, hi = path.Length - 1;
			while (lo < hi)
			{
				int mid = (lo + hi + 1) / 2;
				if (path[mid].P0 <= p)
				{
					lo = mid;
				}
				else
				{
					hi = mid - 1;
				}
			}
			Seg s = path[lo];
			double off;
			if (s.Edge < 0)
			{
				double d = Math.Max(0.0, Math.Min(s.Len, p - s.P0));
				at.X = s.X0 + s.Dx * d;
				at.Z = s.Z0 + s.Dz * d;
				at.Y = s.Y0;
				at.Hx = s.Dx;
				at.Hz = s.Dz;
				at.Edge = -1;
				at.InTunnel = true;
				off = Traffic.LaneOffset(Network.Roads[Network.Portals[s.Portal].Road]);
			}
			else
			{
				Network.Road road = Network.Roads[Traffic.Edges[s.Edge].Road];
				double sr = s.S0 + s.Dir * Math.Max(0.0, Math.Min(s.Len, p - s.P0));
				Traffic.At(road, sr, out at.X, out at.Z, out at.Y);
				Traffic.Heading(road, sr, out double hx, out double hz);
				at.Hx = hx * s.Dir;
				at.Hz = hz * s.Dir;
				at.Edge = s.Edge;
				at.Dir = s.Dir;
				at.S = sr;
				at.InTunnel = false;
				off = Traffic.LaneOffset(road);
			}
			if (TwoLane(s))
			{
				off -= LaneWidth * LaneAt(leg, p);
			}
			// Right of the way of travel is (hz, -hx).
			at.X += at.Hz * off;
			at.Z -= at.Hx * off;
		}

		/// <summary>How far a physics car given back moves over from the lane it held to the day's.</summary>
		internal const double HoldGlide = 200.0;

		/// <summary>Leg <paramref name="leg"/>'s car becomes a physics car at <paramref name="p"/>: it keeps the lane it is in (or nearest).</summary>
		internal static void HoldLaneAt(Leg leg, double p)
		{
			double lane = InnerAt(leg, p);
			// Held in the through lane it is nearest; in a junction lane it follows the day (it turns off from it, or merges).
			leg.HoldLane = lane < -0.5 || lane > 1.5 ? -1f : lane >= 0.5 ? 1f : 0f;
			leg.HoldEnd = double.MaxValue;
		}

		/// <summary>Leg <paramref name="leg"/>'s physics car goes back on its timetable at <paramref name="p"/>: from there it moves over to the day's lane.</summary>
		internal static void ReleaseLaneAt(Leg leg, double p)
		{
			if (leg.HoldLane >= 0f && leg.HoldEnd == double.MaxValue)
			{
				leg.HoldEnd = p;
			}
		}

		/// <summary>The lane leg <paramref name="leg"/>'s car is drawn in at <paramref name="p"/> (0 outer, 1 inner): the day's, or the one it holds as a physics car.</summary>
		internal static double LaneAt(Leg leg, double p)
		{
			double day = InnerAt(leg, p);
			if (leg.HoldLane < 0f || p >= leg.HoldEnd + HoldGlide)
			{
				return day;
			}
			if (day < -0.01 || day > 1.01)
			{
				// The day has it moving into a junction lane (to turn off, or out of one): it glides there from the lane it held, over
				// the day's own move.
				double start = ChangeStart(leg, p);
				if (p <= start)
				{
					return leg.HoldLane;
				}
				double g = Math.Min(1.0, (p - start) / HoldGlide);
				g = g * g * (3.0 - 2.0 * g);
				return leg.HoldLane + (day - leg.HoldLane) * g;
			}
			if (p <= leg.HoldEnd)
			{
				return leg.HoldLane;
			}
			double f = (p - leg.HoldEnd) / HoldGlide;
			f = f * f * (3.0 - 2.0 * f);
			return leg.HoldLane + (day - leg.HoldLane) * f;
		}

		/// <summary>Where the leg's latest lane change at or before path distance <paramref name="p"/> began (0 if none).</summary>
		private static double ChangeStart(Leg leg, double p)
		{
			float[] l = leg.Lanes;
			double start = 0.0;
			for (int k = 0; l != null && k + 2 < l.Length && l[k] <= p; k += 3)
			{
				start = l[k];
			}
			return start;
		}

		/// <summary>How far the trunk's inner lane is left of its outer lane (a lane's width).</summary>
		internal const double LaneWidth = 11.0;

		/// <summary>Whether seg <paramref name="s"/> is on the trunk, two lanes each way.</summary>
		internal static bool TwoLane(Seg s)
		{
			return s.Edge >= 0 ? twoLaneEdge[s.Edge] : s.Portal >= 0 && twoLanePortal[s.Portal];
		}

		/// <summary>How far over to the trunk's inner lane leg <paramref name="leg"/>'s car is at path distance <paramref name="p"/>: 0 in the outer lane, 1 in the inner, between while moving over.</summary>
		internal static double InnerAt(Leg leg, double p)
		{
			float[] l = leg.Lanes;
			if (l == null)
			{
				return 0.0;
			}
			// The last change started at or before p.
			int lo = 0, hi = l.Length / 3 - 1, k = -1;
			while (lo <= hi)
			{
				int mid = (lo + hi) / 2;
				if (l[mid * 3] <= p)
				{
					k = mid;
					lo = mid + 1;
				}
				else
				{
					hi = mid - 1;
				}
			}
			if (k < 0)
			{
				return 0.0;
			}
			double to = l[k * 3 + 2], from = k > 0 ? l[k * 3 - 1] : 0.0;
			double a = l[k * 3], b = l[k * 3 + 1];
			if (p >= b || b <= a)
			{
				return to;
			}
			double f = (p - a) / (b - a);
			// Eased in and out, as a driver steers over.
			f = f * f * (3.0 - 2.0 * f);
			return from + (to - from) * f;
		}

		/// <summary>
		/// Where leg <paramref name="li"/>'s car is at time <paramref name="t"/> on the leg's own clock (clamped to the leg's start and
		/// end): driving on its path. <see cref="TrafficLate"/> reads a late car's place this way, at the time it is behind by.
		/// </summary>
		internal Place Driving(int li, double t)
		{
			Leg leg = Legs[li];
			t = Math.Max(leg.Start, Math.Min(leg.End, t));
			Place at = new Place { State = State.Driving, Leg = li };
			double p = DistanceAt(leg, t);
			OnPath(leg, p, ref at);
			at.P = p;
			at.T = t - leg.Start;
			double p1 = DistanceAt(leg, Math.Min(leg.End, t + 0.25));
			at.Speed = (p1 - p) / 0.25;
			return at;
		}

		/// <summary>Where car <paramref name="car"/> is at sim time <paramref name="w"/> (0 to <see cref="Traffic.DaySeconds"/>; the day repeats).</summary>
		internal Place At(int car, double w)
		{
			Car c = Cars[car];
			Place at = new Place { State = State.Parked, X = c.Home.X, Z = c.Home.Z, Leg = -1 };
			int before = -1;
			foreach (int li in c.Legs)
			{
				Leg leg = Legs[li];
				if (leg.Start < 0.0)
				{
					continue;
				}
				// A leg that ran past the day's end is read again the next morning (w + a day). No array for the two: this is called for
				// every car near the player.
				for (int k = 0; k < 2; k++)
				{
					double t = k == 0 ? w : w + Traffic.DaySeconds;
					if (t >= leg.Start && t < leg.End)
					{
						return Driving(li, t);
					}
				}
				if (leg.End <= w)
				{
					before = li;
				}
			}
			if (before < 0)
			{
				// Before its first trip of the day: where the day's last left it (the day repeats).
				for (int k = c.Legs.Count - 1; k >= 0; k--)
				{
					if (Legs[c.Legs[k]].Start >= 0.0)
					{
						before = c.Legs[k];
						break;
					}
				}
				if (before < 0)
				{
					if (c.Household < 0)
					{
						at.State = State.Away;
					}
					return at;
				}
			}
			Leg last = Legs[before];
			if (last.Vanishes)
			{
				at.State = State.Away;
				return at;
			}
			at.X = last.To.X;
			at.Z = last.To.Z;
			at.Leg = before;
			return at;
		}

		/// <summary>
		/// Every time a car passed the middle of an edge (to count it, as a road's counter would): edge, way, sim time. Read from the
		/// recordings.
		/// </summary>
		internal void Passes(Action<int, int, double, Leg> pass)
		{
			foreach (Leg leg in Legs)
			{
				if (leg.Start < 0.0 || leg.Rec == null)
				{
					continue;
				}
				foreach (Seg s in leg.Path)
				{
					if (s.Edge < 0)
					{
						continue;
					}
					Traffic.Edge e = Traffic.Edges[s.Edge];
					double mid = (e.Sa + e.Sb) * 0.5;
					double into = (mid - s.S0) * s.Dir;
					if (into <= 0.0 || into > s.Len)
					{
						continue;
					}
					double pm = s.P0 + into;
					// The first record at or past it.
					int n = leg.Rec.Length;
					for (int i = 0; i < n; i++)
					{
						if (leg.Rec[i] >= pm)
						{
							double t = i == n - 1 ? leg.End : leg.Start + i * RecordStep;
							pass(s.Edge, s.Dir, t, leg);
							break;
						}
					}
				}
			}
		}

		/// <summary>A checksum of the whole day: every leg's times and recordings.</summary>
		internal ulong Checksum()
		{
			ulong h = 1469598103934665603UL;
			unchecked
			{
				foreach (Leg leg in Legs)
				{
					h = Noise.Mix(h ^ (ulong)BitConverter.DoubleToInt64Bits(leg.Start));
					h = Noise.Mix(h ^ (ulong)BitConverter.DoubleToInt64Bits(leg.End));
					h = Noise.Mix(h ^ (ulong)leg.Car);
					if (leg.Rec != null)
					{
						foreach (float f in leg.Rec)
						{
							h = Noise.Mix(h ^ (uint)BitConverter.SingleToInt32Bits(f));
						}
					}
					if (leg.Lanes != null)
					{
						foreach (float f in leg.Lanes)
						{
							h = Noise.Mix(h ^ (uint)BitConverter.SingleToInt32Bits(f) ^ 0x1A4EUL);
						}
					}
				}
			}
			return h;
		}
	}
}
