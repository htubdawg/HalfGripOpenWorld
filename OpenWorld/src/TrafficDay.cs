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
		private const double Accel = 5.0;
		internal const double Brake = 8.25;
		private const double Headway = 1.5;
		private const double MinGap = 7.0;
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

		/// <summary>A car waiting this long to pull out is let in: the car behind it on the lane leaves it a gap.</summary>
		private const double LetInAfter = 20.0;
		/// <summary>
		/// The shortest stay at a place. A stay is counted from leaving home, not from arriving: the world's clock runs 36 times fast and
		/// driving does not, so a drive spans an hour or two of it, and a stay counted from arriving would push every way home late into
		/// the night. Counted from leaving, a tour keeps its place in the day (out in the morning, home in the late afternoon).
		/// </summary>
		private const double MinStay = 20.0;

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
		/// point about five times a day: 10 of them add about 1,800 to the trunk's count, which the local traffic (about 3,000 to 4,500)
		/// brings to about the 5,400 a day Pennsylvania's principal arterials carry. Their one flaw: the day repeats at 03:00, and there
		/// each jumps from where the day ends it to where the day began it.
		/// </summary>
		private const int Circling = 10;

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
			internal double Dwell;
			internal int Host = -1;
			internal float[] Profile;
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
			public int Leg;
		}

		internal readonly int Seed;
		internal readonly bool Weekend;
		internal readonly List<Car> Cars = new List<Car>();
		internal readonly List<Leg> Legs = new List<Leg>();

		// What it took, for the checks and the log.
		internal double BuildMs, PlanMs;
		internal int Candidates, Kept, Dropped, Through, Unrouted;
		internal double Keep, TargetSeconds, CandidateSeconds;
		internal int Forced, Overlaps, BoxConflicts, Speeding, StoppedAtMouth, PullOutsForced, Unfinished;
		internal double MinGapSeen = double.MaxValue, MaxOver;
		internal int[] TourCount = new int[4];
		internal readonly List<string> Stuck = new List<string>(), OverlapNotes = new List<string>(), ConflictNotes = new List<string>();
		/// <summary>Diagnostics: seconds cars stood waiting at each node, and why requests were refused (behind a refused one, crossing a grant, no room).</summary>
		internal readonly Dictionary<int, double> WaitAt = new Dictionary<int, double>();
		internal int RefusedOrder, RefusedCross, RefusedRoom;
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
				for (int i = 0; i < Circling && trunk.Count > 0; i++)
				{
					int into = i % 2 == 0 ? Wrap.A : Wrap.B, other = Wrap.Other(into);
					Traffic.Edge e = Traffic.Edges[trunk[(int)((long)i * trunk.Count / Circling)]];
					Traffic.Anchor start = new Traffic.Anchor { Edge = trunk[(int)((long)i * trunk.Count / Circling)], S = (e.Sa + e.Sb) * 0.5, Side = 1 };
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
			public readonly List<float> Rec = new List<float>();
			public readonly List<Grant> Grants = new List<Grant>();
			public long Lane;
			public double LaneU;
			/// <summary>The car just ahead on the same lane this step (null if none).</summary>
			public Mover Ahead;
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
		private static int NodeAfter(Seg[] path, int k)
		{
			if (k + 1 >= path.Length || path[k].Edge < 0 || path[k + 1].Edge < 0)
			{
				return -1;
			}
			Traffic.Edge e = Traffic.Edges[path[k].Edge];
			return path[k].Dir > 0 ? e.B : e.A;
		}

		private static int LinkIndex(int node, int edge)
		{
			return Traffic.Links[node].IndexOf(edge);
		}

		/// <summary>
		/// Whether two movements through a node cross: the same lane in never does (the second follows the first), the same lane out always
		/// does (they merge), otherwise their chords round the junction cross. Round a junction each arm has its lane out before its lane
		/// in (right-hand traffic), so the four points' order says it with integers only.
		/// </summary>
		private static bool Crosses(int node, int inA, int outA, int inB, int outB)
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

		/// <summary>Whether a car coming in along arm <paramref name="a"/> is on the right of one coming in along arm <paramref name="b"/>.</summary>
		private static bool RightOf(int node, int a, int b)
		{
			// b travels in along -u_b; its right is (-u_b.z, u_b.x).
			return armX[node][a] * -armZ[node][b] + armZ[node][a] * armX[node][b] > 0.3;
		}

		private static float[] Profile(Leg leg)
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
					double turn = cos <= cos45 ? TurnSpeed : TurnSpeed + (v[i] - TurnSpeed) * (cos - cos45) / (cos20 - cos45);
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

		private static double DesiredAt(float[] profile, double p)
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
			m.Lane = LaneOf(s);
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
				List<Mover> list = null;
				if (clear && lanes.TryGetValue(lane, out list))
				{
					foreach (Mover o in list)
					{
						double d = o.LaneU - u;
						if (Math.Abs(d) < 40.0 || (d < 0.0 && -d < 3.0 * o.V))
						{
							clear = false;
							break;
						}
					}
				}
				// Let in: after a while the car just behind leaves a gap (below), and the car pulls out once the gap is there and
				// nothing is coming fast.
				if (!clear && w - leg.Ready >= LetInAfter && !Coming(first, u))
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
				StartsBy[Math.Min(47, (int)(w / 100.0))]++;
				leg.Profile = Profile(leg);
				for (int q = 0; q + 1 < leg.Profile.Length; q++)
				{
					ProfileSeconds += Sample / Math.Max(1.0, Math.Min(leg.Profile[q], leg.Profile[q + 1]));
				}
				Mover m = new Mover { Index = waiting[i], L = leg, StartStep = step, P = 0.0, Seg = k0 };
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
		private bool Coming(Seg s, double u)
		{
			if (s.Edge < 0)
			{
				return false;
			}
			Traffic.Edge e = Traffic.Edges[s.Edge];
			int node = s.Dir > 0 ? e.A : e.B;
			if (u > Traffic.Nodes[node].Box + 80.0 || !nodeGrants.TryGetValue(node, out List<Grant> list))
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
					InLane = LaneOf(path[k]), OutLane = LaneOf(path[k + 1]),
					Eta = ArriveTime(m.V, nodeP - box - m.P, Math.Max(m.V, DesiredAt(m.L.Profile, m.P + Math.Max(0.0, nodeP - box - m.P)))),
				});
			}
			requests.Sort((a, b) => a.Node != b.Node ? a.Node.CompareTo(b.Node) : a.Eta != b.Eta ? a.Eta.CompareTo(b.Eta) : a.M.Index.CompareTo(b.M.Index));
			for (int i = 0; i + 1 < requests.Count; i++)
			{
				Request a = requests[i], b = requests[i + 1];
				if (a.Node == b.Node && b.Eta - a.Eta < 1.0 && a.InArm != b.InArm && RightOf(a.Node, b.InArm, a.InArm))
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
						if (q.InArm == r.InArm || Crosses(node, q.InArm, q.OutArm, r.InArm, r.OutArm))
						{
							ok = false;
							RefusedOrder++;
							break;
						}
					}
					if (ok || force)
					{
						foreach (Grant g in granted)
						{
							if (g.M == r.M || !Crosses(node, g.InKey, g.OutKey, r.InArm, r.OutArm))
							{
								continue;
							}
							// Compatible if the other is through before this one gets there, or this one is through before the other gets
							// there (a gap taken, as a driver waiting at the line takes one): reckoned from a standstill for this one, and
							// as if the other speeds up a fifth, with a second to spare.
							double clear = (g.NodeP + box + Traffic.CarLength - g.M.P) / Math.Max(1.0, Math.Min(g.M.V, g.Through));
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
								break;
							}
						}
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
		private static double ClearTime(double v, double d)
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
			return first.LaneU - Traffic.CarLength > need;
		}

		/// <summary>Grants end once the car is a car's length past the box.</summary>
		private void Release()
		{
			foreach (Mover m in movers)
			{
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
						if (Math.Abs(gb.M.P - gb.NodeP) >= box || !Crosses(node, ga.InKey, ga.OutKey, gb.InKey, gb.OutKey))
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
				if (!lanes.TryGetValue(LaneOf(path[k]), out List<Mover> list) || list.Count == 0)
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
			// Right of the way of travel is (hz, -hx).
			at.X += at.Hz * off;
			at.Z -= at.Hx * off;
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
				foreach (double t in new[] { w, w + Traffic.DaySeconds })
				{
					if (t >= leg.Start && t < leg.End)
					{
						at.State = State.Driving;
						at.Leg = li;
						double p = DistanceAt(leg, t);
						OnPath(leg, p, ref at);
						double p1 = DistanceAt(leg, Math.Min(leg.End, t + 0.25));
						at.Speed = (p1 - p) / 0.25;
						return at;
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
				}
			}
			return h;
		}
	}
}
