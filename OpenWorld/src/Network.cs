using System;
using System.Collections.Generic;

namespace HalfGripOpenWorld
{
	/// <summary>Road classes, biggest first. A road may follow a road of its own class or bigger, never a smaller one.</summary>
	internal enum RoadKind
	{
		Trunk = 0,
		Primary = 1,
		Secondary = 2,
		Street = 3,
		Lane = 4,
		Track = 5,
	}

	/// <summary>
	/// The roads, laid out the way real road networks come about, in order of importance:
	///
	///   1. a dual-carriageway trunk road crossing the region from the west edge to the east, bypassing the towns, with each town
	///      joined to it;
	///   2. main roads between towns and villages: the Gabriel graph of the places (two places are neighbours if no other place is in
	///      the circle between them, which matches real road links far better than a tree), taken in order of "gravity" (population
	///      times population over distance squared), the strongest links as primary roads, the rest secondary, and a road over the
	///      mountains to the north edge;
	///   3. streets grown out from the roads in each town and village (<see cref="Grow"/>);
	///   4. a lane from every hamlet and farm to the nearest road, and some between neighbouring hamlets;
	///   5. farm tracks branching off the lanes and minor roads.
	///
	/// Every road is routed with A* over the land on a 100-unit grid (<see cref="Router"/>), costed by grade (each class has its own
	/// limit), with bridges dear and water closed. Following a road already built (of the same class or bigger) is cheap, so a new
	/// road shares the way with the network and only the new part is built: it leaves and joins the network at junctions. Then it is
	/// smoothed into curves, given a height profile held to its class's grade that meets the roads it joins and crosses at their height,
	/// and lifted onto bridges over rivers. The ground is then cut and filled to every road's bed (<see cref="Ground"/>).
	///
	/// Built once, with <see cref="Rng"/> and plain arithmetic only, so every player gets the same roads, and never changed afterwards,
	/// so the worker threads that build tiles can read it freely.
	/// </summary>
	internal static class Network
	{
		internal sealed class Road
		{
			public RoadKind Kind;
			public string Name;
			/// <summary>Half the paved width.</summary>
			public double Half;
			public double[] X;
			public double[] Z;
			/// <summary>Height of the road line, from sea level (add <see cref="Lift"/> for the scene).</summary>
			public double[] Y;
			/// <summary>Distance along the road at each sample, for the texture.</summary>
			public double[] S;
			/// <summary>True where the road is on a bridge: the ground under it is left as it is.</summary>
			public bool[] Bridge;
			public int Count => X.Length;
		}

		/// <summary>What each class is like: width, steepest grade, how much it smooths the land, how it curves.</summary>
		private struct Class
		{
			public double Half;
			public double Grade;
			public int Window;
			public int Keep;
			public int Rounds;
			public double Soft;
			public double Steep;
			public double Bank;
		}

		private static readonly Class[] Classes =
		{
			// Half widths: a lane is 11 units (a car is about 5.4 wide). Trunk: two carriageways of two lanes and a median.
			new Class { Half = 33.0, Grade = 0.05, Window = 40, Keep = 6, Rounds = 4, Soft = 0.035, Steep = 8.0, Bank = 160.0 },
			new Class { Half = 14.0, Grade = 0.07, Window = 22, Keep = 4, Rounds = 3, Soft = 0.05, Steep = 5.0, Bank = 120.0 },
			new Class { Half = 12.0, Grade = 0.09, Window = 14, Keep = 3, Rounds = 3, Soft = 0.07, Steep = 3.0, Bank = 90.0 },
			new Class { Half = 10.0, Grade = 0.11, Window = 3, Keep = 1, Rounds = 2, Soft = 0.09, Steep = 2.0, Bank = 30.0 },
			new Class { Half = 7.5, Grade = 0.11, Window = 6, Keep = 2, Rounds = 3, Soft = 0.09, Steep = 2.0, Bank = 40.0 },
			new Class { Half = 4.5, Grade = 0.14, Window = 3, Keep = 1, Rounds = 2, Soft = 0.12, Steep = 1.5, Bank = 25.0 },
		};

		internal static double HalfOf(RoadKind kind)
		{
			return Classes[(int)kind].Half;
		}

		/// <summary>Distance between a road's samples.</summary>
		internal const double Step = 8.0;

		/// <summary>Flat ground either side of the paving before the bank starts.</summary>
		internal const double Shoulder = 4.0;

		/// <summary>The road bed is this far under the road surface, so the ground never shows through the paving.</summary>
		internal const double BedDepth = 0.3;

		internal static readonly List<Road> Roads = new List<Road>();

		/// <summary>Where roads leave the world: the mouth of a tunnel into the edge wall, facing out along (DirX, DirZ).</summary>
		internal struct Portal
		{
			public double X;
			public double Z;
			public double Y;
			public double DirX;
			public double DirZ;
			public double Half;
		}

		internal static readonly List<Portal> Portals = new List<Portal>();

		/// <summary>Where the player starts: on the trunk road near the main town, in the inner lane, heading east.</summary>
		internal static double SpawnX;
		internal static double SpawnZ;
		internal static double SpawnY;
		internal static double SpawnDirX = 1.0;
		internal static double SpawnDirZ;

		/// <summary>
		/// Added to every height for the scene, so the road at the spawn is at 0.5: the Multiplayer mod puts every car at height 1 on
		/// the grid (its own grid rule), which is then just resting on the road.
		/// </summary>
		internal static double Lift;

		/// <summary>Milliseconds the layout took, for the log.</summary>
		internal static double BuildMs;

		private static readonly object gate = new object();
		private static volatile bool built;

		/// <summary>Builds the world's layout if it has not been built yet. Safe from any thread; the first call does the work.</summary>
		internal static void Ensure()
		{
			if (built)
			{
				return;
			}
			lock (gate)
			{
				if (!built)
				{
					System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
					Build();
					BuildMs = watch.Elapsed.TotalMilliseconds;
					built = true;
				}
			}
		}

		private static Router router;
		private static Segments segments;

		private static void Build()
		{
			Rivers.Build();
			Rng rng = new Rng(Noise.Mix(WorldGen.Seed ^ 0x5157_E5UL));
			Places.Build(rng);
			segments = new Segments();
			router = new Router();

			List<Places.Place> towns = Places.All.FindAll(p => p.Kind == PlaceKind.Town);
			List<Places.Place> villages = Places.All.FindAll(p => p.Kind == PlaceKind.Village);
			List<Places.Place> hamlets = Places.All.FindAll(p => p.Kind == PlaceKind.Hamlet);

			// 1. The trunk road, west edge to east edge, then each town joined to it.
			Gate(true, out double wx, out double wz);
			Gate(false, out double ex, out double ez);
			Road trunk = null;
			foreach (Road r in Route(RoadKind.Trunk, "Trunk road", wx, wz, ex, ez, false))
			{
				trunk = trunk == null || r.Count > trunk.Count ? r : trunk;
			}
			if (trunk != null)
			{
				trunk = Extend(trunk, true);
				trunk = Extend(trunk, false);
				Spawn(trunk, towns.Count > 0 ? towns[0] : null);
			}
			foreach (Places.Place town in towns)
			{
				RouteToNetwork(RoadKind.Primary, town.Name + " link", town.X, town.Z, RoadKind.Trunk);
			}

			// 2. Main roads: the Gabriel graph of towns and villages (and the north gate), strongest links first.
			List<(double x, double z, double pop, string name)> nodes = new List<(double, double, double, string)>();
			foreach (Places.Place p in towns)
			{
				nodes.Add((p.X, p.Z, p.Population, p.Name));
			}
			foreach (Places.Place p in villages)
			{
				nodes.Add((p.X, p.Z, p.Population, p.Name));
			}
			NorthGate(out double nx, out double nz);
			nodes.Add((nx, nz, 3000.0, "north"));
			List<(int a, int b, double g)> links = Gabriel(nodes);
			links.Sort((p, q) => q.g != p.g ? q.g.CompareTo(p.g) : (p.a * 1000 + p.b).CompareTo(q.a * 1000 + q.b));
			for (int k = 0; k < links.Count; k++)
			{
				(int a, int b, double _) = links[k];
				bool main = k < links.Count / 3 || nodes[a].name == "north" || nodes[b].name == "north";
				foreach (Road r in Route(main ? RoadKind.Primary : RoadKind.Secondary, nodes[a].name + " - " + nodes[b].name, nodes[a].x, nodes[a].z, nodes[b].x, nodes[b].z, false))
				{
					if (nodes[b].name == "north" && r.X[r.Count - 1] == nodes[b].x && r.Z[r.Count - 1] == nodes[b].z)
					{
						Extend(r, false);
					}
				}
			}

			// 3. Streets, grown out from the roads in each town and village.
			for (int i = 0; i < Places.All.Count; i++)
			{
				Places.Place p = Places.All[i];
				if (p.Kind != PlaceKind.Hamlet)
				{
					Grow(new Rng(Noise.Mix(WorldGen.Seed + 7000UL + (ulong)i)), p);
				}
			}

			// 4. A lane from each hamlet to the nearest road, and to some neighbouring hamlets.
			foreach (Places.Place h in hamlets)
			{
				RouteToNetwork(RoadKind.Lane, h.Name + " lane", h.X, h.Z, RoadKind.Lane);
			}
			for (int i = 0; i < hamlets.Count; i++)
			{
				int nearest = -1;
				double best = 3200.0 * 3200.0;
				for (int j = 0; j < hamlets.Count; j++)
				{
					double dx = hamlets[i].X - hamlets[j].X, dz = hamlets[i].Z - hamlets[j].Z;
					if (j != i && dx * dx + dz * dz < best)
					{
						best = dx * dx + dz * dz;
						nearest = j;
					}
				}
				if (nearest > i && rng.Next() < 0.45)
				{
					Route(RoadKind.Lane, hamlets[i].Name + " - " + hamlets[nearest].Name, hamlets[i].X, hamlets[i].Z, hamlets[nearest].X, hamlets[nearest].Z, false);
				}
			}

			// 5. Farm tracks off the lanes and minor roads.
			Tracks(new Rng(Noise.Mix(WorldGen.Seed + 9000UL)));

			Index();
			Lift = 0.5 - SpawnY;
		}

		// ---- the edges ---------------------------------------------------------------------------------------------

		/// <summary>Where the trunk road meets the west (or east) edge: the lowest open ground on the edge line, between 1 and 8 km from the coast.</summary>
		private static void Gate(bool west, out double x, out double z)
		{
			x = (west ? -1.0 : 1.0) * (WorldGen.Half - WorldGen.WallDepth - 400.0);
			z = 0.0;
			double lowest = double.MaxValue;
			for (double t = -WorldGen.Half + 3000.0; t < 8000.0; t += 100.0)
			{
				double inland = WorldGen.Inland(x, t);
				if (inland < 1000.0 || inland > 8000.0 || !double.IsNaN(Rivers.SurfaceAt(x, t)))
				{
					continue;
				}
				double h = WorldGen.Base(x, t) + (Rivers.Distance(x, t, out double plain) < plain + 100.0 ? 1000.0 : 0.0);
				if (h < lowest)
				{
					lowest = h;
					z = t;
				}
			}
		}

		/// <summary>Where the road over the mountains meets the north edge: the lowest point on the edge line (a pass).</summary>
		private static void NorthGate(out double x, out double z)
		{
			z = WorldGen.Half - WorldGen.WallDepth - 400.0;
			x = 0.0;
			double lowest = double.MaxValue;
			for (double t = -12000.0; t <= 12000.0; t += 100.0)
			{
				if (!double.IsNaN(Rivers.SurfaceAt(t, z)))
				{
					continue;
				}
				double h = WorldGen.Base(t, z);
				if (h < lowest)
				{
					lowest = h;
					x = t;
				}
			}
		}

		/// <summary>
		/// Carries a road that ends at a gate on, level, into the edge wall, ending at a tunnel mouth: the road goes on to somewhere
		/// outside the world. Returns the longer road, which takes the old one's place in the network.
		/// </summary>
		private static Road Extend(Road road, bool atStart)
		{
			int end = atStart ? 0 : road.Count - 1;
			int prev = atStart ? 1 : road.Count - 2;
			double dx = road.X[end] - road.X[prev], dz = road.Z[end] - road.Z[prev];
			// Straight out through the edge: snap the way to the nearest edge's outward direction.
			if (Math.Abs(road.X[end]) > Math.Abs(road.Z[end]))
			{
				dx = Math.Sign(road.X[end]);
				dz = 0.0;
			}
			else
			{
				dx = 0.0;
				dz = 1.0;
			}
			int add = (int)(1100.0 / Step);
			int n = road.Count + add;
			Road longer = new Road { Kind = road.Kind, Name = road.Name, Half = road.Half, X = new double[n], Z = new double[n], Y = new double[n], S = new double[n], Bridge = new bool[n] };
			int offset = atStart ? add : 0;
			Array.Copy(road.X, 0, longer.X, offset, road.Count);
			Array.Copy(road.Z, 0, longer.Z, offset, road.Count);
			Array.Copy(road.Y, 0, longer.Y, offset, road.Count);
			Array.Copy(road.Bridge, 0, longer.Bridge, offset, road.Count);
			for (int k = 1; k <= add; k++)
			{
				int i = atStart ? add - k : road.Count - 1 + k;
				longer.X[i] = road.X[end] + dx * Step * k;
				longer.Z[i] = road.Z[end] + dz * Step * k;
				longer.Y[i] = road.Y[end];
			}
			Lengths(longer);
			int at = Roads.IndexOf(road);
			Roads[at] = longer;
			segments.Replace(at, road, longer);
			int tip = atStart ? 0 : n - 1;
			Portals.Add(new Portal { X = longer.X[tip], Z = longer.Z[tip], Y = longer.Y[tip], DirX = dx, DirZ = dz, Half = longer.Half });
			return longer;
		}

		// ---- links -------------------------------------------------------------------------------------------------

		/// <summary>The Gabriel graph of the nodes, each edge with its gravity (population times population over distance squared).</summary>
		private static List<(int a, int b, double g)> Gabriel(List<(double x, double z, double pop, string name)> nodes)
		{
			List<(int, int, double)> edges = new List<(int, int, double)>();
			for (int a = 0; a < nodes.Count; a++)
			{
				for (int b = a + 1; b < nodes.Count; b++)
				{
					double mx = (nodes[a].x + nodes[b].x) * 0.5, mz = (nodes[a].z + nodes[b].z) * 0.5;
					double dx = nodes[a].x - nodes[b].x, dz = nodes[a].z - nodes[b].z;
					double r2 = (dx * dx + dz * dz) * 0.25;
					bool empty = true;
					for (int k = 0; k < nodes.Count && empty; k++)
					{
						if (k != a && k != b)
						{
							double kx = nodes[k].x - mx, kz = nodes[k].z - mz;
							empty = kx * kx + kz * kz >= r2;
						}
					}
					if (empty)
					{
						edges.Add((a, b, nodes[a].pop * nodes[b].pop / (dx * dx + dz * dz)));
					}
				}
			}
			return edges;
		}

		// ---- routing and building ----------------------------------------------------------------------------------

		/// <summary>A road from (ax, az) to (bx, bz): routed, and its new parts (where it does not follow a road already built) built.</summary>
		private static List<Road> Route(RoadKind kind, string name, double ax, double az, double bx, double bz, bool toNetwork)
		{
			List<int> path = router.Find(kind, ax, az, bx, bz, toNetwork ? (int)kind : -1);
			return path == null ? new List<Road>() : Build(kind, name, path, ax, az, toNetwork ? double.NaN : bx, bz);
		}

		/// <summary>A road from (x, z) to the nearest road of class <paramref name="joins"/> or bigger.</summary>
		private static List<Road> RouteToNetwork(RoadKind kind, string name, double x, double z, RoadKind joins)
		{
			List<int> path = router.Find(kind, x, z, 0.0, 0.0, (int)joins);
			return path == null ? new List<Road>() : Build(kind, name, path, x, z, double.NaN, 0.0);
		}

		/// <summary>
		/// Turns a routed path into roads: every stretch of the path not on a road already built becomes a road, from where it leaves the
		/// network (or the start) to where it joins it again (or the end). Ends that join the network are moved onto the nearest point of
		/// the road there, and pinned to its height.
		/// </summary>
		private static List<Road> Build(RoadKind kind, string name, List<int> path, double ax, double az, double bx, double bz)
		{
			List<Road> made = new List<Road>();
			int n = path.Count;
			int k = 0;
			while (k < n)
			{
				if (router.OnRoad(path[k]))
				{
					k++;
					continue;
				}
				int s = k;
				while (k < n && !router.OnRoad(path[k]))
				{
					k++;
				}
				int e = k - 1;
				List<(double x, double z)> pts = new List<(double x, double z)>();
				List<(int idx, double y)> pins = new List<(int, double)>();
				double pinStart = double.NaN, pinEnd = double.NaN;
				if (s == 0)
				{
					pts.Add((ax, az));
				}
				else if (segments.Snap(router.X(path[s - 1]), router.Z(path[s - 1]), 140.0, -1, out double px, out double pz, out double py))
				{
					pts.Add((px, pz));
					pinStart = py;
				}
				for (int c = s; c <= e; c++)
				{
					pts.Add((router.X(path[c]), router.Z(path[c])));
				}
				if (e == n - 1)
				{
					if (!double.IsNaN(bx))
					{
						pts.Add((bx, bz));
					}
					else if (segments.Snap(router.X(path[e]), router.Z(path[e]), 160.0, -1, out double px2, out double pz2, out double py2))
					{
						pts.Add((px2, pz2));
						pinEnd = py2;
					}
				}
				else if (segments.Snap(router.X(path[e + 1]), router.Z(path[e + 1]), 140.0, -1, out double px3, out double pz3, out double py3))
				{
					pts.Add((px3, pz3));
					pinEnd = py3;
				}
				if (pts.Count < 2)
				{
					continue;
				}
				Class cl = Classes[(int)kind];
				List<(double x, double z)> line = Wobble(Smooth(pts, cl.Keep, cl.Rounds), kind);
				Road road = Make(kind, name, line);
				if (road.Count < 6)
				{
					continue;
				}
				pins.Add((0, double.IsNaN(pinStart) ? WorldGen.Base(road.X[0], road.Z[0]) : pinStart));
				pins.Add((road.Count - 1, double.IsNaN(pinEnd) ? WorldGen.Base(road.X[road.Count - 1], road.Z[road.Count - 1]) : pinEnd));
				Add(road, pins);
				made.Add(road);
			}
			return made;
		}

		/// <summary>Gives a new road its height (pins at its ends and where it crosses roads), its bridges, and adds it to the network.</summary>
		private static void Add(Road road, List<(int idx, double y)> pins)
		{
			Class cl = Classes[(int)road.Kind];
			segments.Crossings(road, pins);
			Profile(road, cl.Grade, cl.Window, pins);
			Bridges(road, cl.Grade);
			Roads.Add(road);
			segments.Add(Roads.Count - 1, road);
			router.Mark(road);
		}

		private static Road Make(RoadKind kind, string name, List<(double x, double z)> points)
		{
			int n = points.Count;
			Road road = new Road { Kind = kind, Name = name, Half = HalfOf(kind), X = new double[n], Z = new double[n], Y = new double[n], S = new double[n], Bridge = new bool[n] };
			for (int i = 0; i < n; i++)
			{
				road.X[i] = points[i].x;
				road.Z[i] = points[i].z;
			}
			Lengths(road);
			return road;
		}

		private static void Lengths(Road road)
		{
			road.S[0] = 0.0;
			for (int i = 1; i < road.Count; i++)
			{
				double dx = road.X[i] - road.X[i - 1];
				double dz = road.Z[i] - road.Z[i - 1];
				road.S[i] = road.S[i - 1] + Math.Sqrt(dx * dx + dz * dz);
			}
		}

		/// <summary>
		/// Grid path to curve: every <paramref name="keep"/>th point (and the ends), rounded off by Chaikin's corner cutting
		/// <paramref name="rounds"/> times, then resampled every <see cref="Step"/> units.
		/// </summary>
		private static List<(double x, double z)> Smooth(List<(double x, double z)> path, int keep, int rounds)
		{
			List<(double x, double z)> pts = new List<(double x, double z)> { path[0] };
			for (int i = keep; i < path.Count - 1; i += keep)
			{
				pts.Add(path[i]);
			}
			pts.Add(path[path.Count - 1]);
			for (int r = 0; r < rounds; r++)
			{
				List<(double x, double z)> next = new List<(double x, double z)> { pts[0] };
				for (int i = 0; i + 1 < pts.Count; i++)
				{
					(double x0, double z0) = pts[i];
					(double x1, double z1) = pts[i + 1];
					next.Add((x0 * 0.75 + x1 * 0.25, z0 * 0.75 + z1 * 0.25));
					next.Add((x0 * 0.25 + x1 * 0.75, z0 * 0.25 + z1 * 0.75));
				}
				next.Add(pts[pts.Count - 1]);
				pts = next;
			}
			return Lines.Resample(pts, Step);
		}

		private static readonly ulong SaltWobble = Noise.Mix(WorldGen.Seed + 303);

		/// <summary>
		/// Small roads wander: each point pushed sideways by slow noise along the road (country lanes follow old field edges and kinks;
		/// a main road barely, the trunk not at all), fading to nothing at the ends so junctions stay exactly where they are.
		/// </summary>
		private static List<(double x, double z)> Wobble(List<(double x, double z)> line, RoadKind kind)
		{
			double amount = kind == RoadKind.Lane ? 22.0 : kind == RoadKind.Secondary ? 12.0 : kind == RoadKind.Primary ? 5.0 : 0.0;
			int n = line.Count;
			if (amount <= 0.0 || n < 8)
			{
				return line;
			}
			List<(double x, double z)> bent = new List<(double x, double z)>(n);
			double key = line[0].x * 0.37 + line[0].z * 0.61;
			for (int i = 0; i < n; i++)
			{
				int a = Math.Max(0, i - 1), b = Math.Min(n - 1, i + 1);
				double tx = line[b].x - line[a].x, tz = line[b].z - line[a].z;
				double len = Math.Sqrt(tx * tx + tz * tz);
				double fade = Math.Min(1.0, Math.Min(i, n - 1 - i) / 12.0);
				double push = Noise.Fbm(SaltWobble, i * Step, key, 180.0, 2, 0.5) * amount * fade;
				bent.Add(len > 0.0 ? (line[i].x - tz / len * push, line[i].z + tx / len * push) : line[i]);
			}
			return Lines.Resample(bent, Step);
		}

		/// <summary>
		/// The road's heights: the land under it averaged over <paramref name="window"/> samples each way (so it rides over bumps and
		/// through small dips), moved to meet each pin exactly (a straight-line correction between pins), then held to
		/// <paramref name="grade"/> by passes along the road in both directions.
		/// </summary>
		private static void Profile(Road road, double grade, int window, List<(int idx, double y)> pins)
		{
			int n = road.Count;
			double[] raw = new double[n + 1];
			for (int i = 0; i < n; i++)
			{
				raw[i + 1] = raw[i] + WorldGen.Base(road.X[i], road.Z[i]);
			}
			double[] y = road.Y;
			for (int i = 0; i < n; i++)
			{
				int a = Math.Max(0, i - window);
				int b = Math.Min(n - 1, i + window);
				y[i] = (raw[b + 1] - raw[a]) / (b - a + 1);
			}
			for (int round = 0; round < 3; round++)
			{
				Pin(y, pins);
				Hold(road, grade, 0, n - 1);
			}
			Pin(y, pins);
		}

		/// <summary>
		/// Holds the road's heights from sample <paramref name="a"/> to <paramref name="b"/> to <paramref name="grade"/>, both ways, by
		/// each segment's real length (the last one of a road can be much shorter than the rest).
		/// </summary>
		private static void Hold(Road road, double grade, int a, int b)
		{
			double[] y = road.Y;
			for (int i = a + 1; i <= b; i++)
			{
				double rise = grade * (road.S[i] - road.S[i - 1]);
				y[i] = Math.Max(y[i - 1] - rise, Math.Min(y[i - 1] + rise, y[i]));
			}
			for (int i = b - 1; i >= a; i--)
			{
				double rise = grade * (road.S[i + 1] - road.S[i]);
				y[i] = Math.Max(y[i + 1] - rise, Math.Min(y[i + 1] + rise, y[i]));
			}
		}

		/// <summary>Moves the heights by a straight-line correction between pins so each pinned sample is exactly at its pin.</summary>
		private static void Pin(double[] y, List<(int idx, double y)> pins)
		{
			for (int p = 0; p + 1 < pins.Count; p++)
			{
				(int i0, double y0) = pins[p];
				(int i1, double y1) = pins[p + 1];
				double d0 = y0 - y[i0];
				double d1 = y1 - y[i1];
				for (int i = i0; i <= i1; i++)
				{
					double t = i1 > i0 ? (double)(i - i0) / (i1 - i0) : 0.0;
					y[i] += d0 + (d1 - d0) * t;
				}
			}
		}

		/// <summary>
		/// Bridges: where the road crosses a river's channel it is carried on a deck clear of the water (5 units, more for a wide river),
		/// with ramps up to it at the road's grade, and the ground under the deck is left alone so the river runs under it.
		/// </summary>
		private static void Bridges(Road road, double grade)
		{
			int n = road.Count;
			bool[] wet = new bool[n];
			double[] water = new double[n];
			for (int i = 0; i < n; i++)
			{
				if (Rivers.Water(road.X[i], road.Z[i], out double level) && double.IsNaN(Rivers.SurfaceAt(road.X[i], road.Z[i])))
				{
					wet[i] = true;
					water[i] = level;
				}
			}
			for (int i = 0; i < n; i++)
			{
				if (!wet[i])
				{
					continue;
				}
				int s = i;
				double top = 0.0;
				while (i < n && wet[i])
				{
					top = Math.Max(top, water[i]);
					i++;
				}
				int e = i - 1;
				// The deck runs a little past the banks.
				int a = Math.Max(0, s - 3), b = Math.Min(n - 1, e + 3);
				double deck = Math.Max(top + 5.0 + road.Half * 0.05, Math.Max(road.Y[a], road.Y[b]));
				for (int k = a; k <= b; k++)
				{
					road.Y[k] = deck;
					road.Bridge[k] = true;
				}
				Hold(road, grade, 0, n - 1);
			}
		}

		// ---- growth ------------------------------------------------------------------------------------------------

		/// <summary>
		/// Streets grown out from the roads of a town or village, the way Parish and Müller grow city streets: along each road in the place,
		/// side streets branch off every 60 to 120 m (more often near the centre), each a short random walk; each step obeys local rules: a
		/// street that would cross another stops there (a junction), one that comes near another joins it, one near an existing junction
		/// snaps to it, and one that leaves the place, reaches water or gets too steep stops. Streets then branch again, less often. The
		/// result is the denser-in-the-middle, T-junction-rich network real towns have, never a grid.
		/// </summary>
		private static void Grow(Rng rng, Places.Place place)
		{
			bool town = place.Kind == PlaceKind.Town;
			int depthMost = town ? 3 : 2;
			Queue<(int road, int depth)> queue = new Queue<(int, int)>();

			// The old roads out of the centre to the place's fields, spread round the compass between the roads already arriving.
			int radials = town ? 5 : 2;
			double turn = rng.Next() * 2.0 * DetMath.Pi;
			double start = WorldGen.Base(place.X, place.Z);
			bool hub = segments.Snap(place.X, place.Z, place.Radius * 0.25, -1, out double cx, out double cz, out double cy);
			for (int k = 0; k < radials; k++)
			{
				double angle = turn + k * 2.0 * DetMath.Pi / radials + rng.Range(-0.3, 0.3);
				Road radial = Walk(rng, hub ? cx : place.X, hub ? cz : place.Z, hub ? cy : start, angle, place.Radius * rng.Range(0.8, 1.05), place, RoadKind.Street, -1, 0.08);
				if (radial != null)
				{
					queue.Enqueue((Roads.Count - 1, 1));
				}
			}
			foreach (int r in segments.RoadsNear(place.X, place.Z, place.Radius))
			{
				if (Roads[r].Kind <= RoadKind.Lane)
				{
					queue.Enqueue((r, 0));
				}
			}
			int made = 0;
			while (queue.Count > 0 && made < (town ? 700 : 60))
			{
				(int r, int depth) = queue.Dequeue();
				Road parent = Roads[r];
				// Side streets 60 to 120 m apart in a town (200 to 400 units), so the blocks between them hold houses with gardens.
				double gap = rng.Range(0.7, 1.3) * (town ? 260.0 : 300.0);
				for (int i = 2; i < parent.Count - 2; i++)
				{
					if (parent.S[i] < gap)
					{
						continue;
					}
					double dx = parent.X[i] - place.X, dz = parent.Z[i] - place.Z;
					double from = Math.Sqrt(dx * dx + dz * dz) / place.Radius;
					if (from > 1.0)
					{
						continue;
					}
					gap = parent.S[i] + rng.Range(0.7, 1.3) * (town ? 260.0 : 300.0) * (1.0 + 0.6 * from);
					for (int side = -1; side <= 1; side += 2)
					{
						double chance = (town ? 0.92 : 0.7) * (1.0 - from * from) * (depth <= 1 ? 1.0 : 0.85 / depth);
						if (rng.Next() >= chance)
						{
							continue;
						}
						double length = rng.Range(250.0, town ? 900.0 : 500.0) * (1.0 - 0.4 * from);
						Road street = Branch(rng, parent, r, i, side, length, place, RoadKind.Street);
						if (street != null)
						{
							made++;
							if (depth + 1 <= depthMost)
							{
								queue.Enqueue((Roads.Count - 1, depth + 1));
							}
						}
					}
				}
			}
		}

		/// <summary>Farm tracks: dead ends off the lanes and minor roads in the country, into the fields.</summary>
		private static void Tracks(Rng rng)
		{
			int count = Roads.Count;
			for (int r = 0; r < count; r++)
			{
				Road parent = Roads[r];
				if (parent.Kind != RoadKind.Lane && parent.Kind != RoadKind.Secondary)
				{
					continue;
				}
				double next = rng.Range(300.0, 900.0);
				for (int i = 2; i < parent.Count - 2; i++)
				{
					if (parent.S[i] < next || parent.Bridge[i])
					{
						continue;
					}
					next = parent.S[i] + rng.Range(400.0, 1100.0);
					if (InPlace(parent.X[i], parent.Z[i]) || rng.Next() > 0.45)
					{
						continue;
					}
					Branch(rng, parent, r, i, rng.Next() < 0.5 ? -1 : 1, rng.Range(150.0, 450.0), null, RoadKind.Track);
				}
			}
		}

		private static bool InPlace(double x, double z)
		{
			foreach (Places.Place p in Places.All)
			{
				double dx = x - p.X, dz = z - p.Z;
				if (dx * dx + dz * dz < (p.Radius + 100.0) * (p.Radius + 100.0))
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>One branch off road <paramref name="r"/> at sample <paramref name="i"/>, to the given side, at a right angle (give or take 12 degrees).</summary>
		private static Road Branch(Rng rng, Road parent, int r, int i, int side, double length, Places.Place place, RoadKind kind)
		{
			double tx = parent.X[i + 1] - parent.X[i - 1], tz = parent.Z[i + 1] - parent.Z[i - 1];
			if (tx * tx + tz * tz <= 0.0)
			{
				return null;
			}
			double angle = Atan2(-tx * side, tz * side) + rng.Range(-0.2, 0.2);
			return Walk(rng, parent.X[i], parent.Z[i], parent.Y[i], angle, length, place, kind, r, 0.18);
		}

		/// <summary>
		/// A new road walked out from (x, z) (on a road at height <paramref name="y"/>, or a place's centre): 30-unit steps, turning by up
		/// to <paramref name="wander"/> radians each step, stopped by the local rules (see <see cref="Grow"/>): it ends on a road it would
		/// cross, joins a road it comes near (not <paramref name="from"/>, the one it left, for its first steps), and stops before leaving
		/// the place, reaching water, or getting too steep. Added to the network if it came to 50 units or more.
		/// </summary>
		private static Road Walk(Rng rng, double x, double z, double y, double angle, double length, Places.Place place, RoadKind kind, int from, double wander)
		{
			const double seg = 30.0;
			List<(double x, double z)> pts = new List<(double x, double z)> { (x, z) };
			double endPin = double.NaN;
			double walked = 0.0;
			double h = WorldGen.Base(x, z);
			while (walked < length)
			{
				angle += rng.Range(-wander, wander);
				double nx = x + DetMath.Cos(angle) * seg, nz = z + DetMath.Sin(angle) * seg;
				if (place != null)
				{
					double dx = nx - place.X, dz = nz - place.Z;
					if (dx * dx + dz * dz > place.Radius * place.Radius * 1.1)
					{
						break;
					}
				}
				if (!double.IsNaN(Rivers.SurfaceAt(nx, nz)) || Rivers.Distance(nx, nz, out double plain) < plain * 0.5 + 25.0)
				{
					break;
				}
				double nh = WorldGen.Base(nx, nz);
				if (Math.Abs(nh - h) / seg > Classes[(int)kind].Grade * 1.3)
				{
					break;
				}
				bool first = pts.Count < 3;
				// Crossing a road: stop on it.
				if (segments.Intersect(x, z, nx, nz, first ? from : -1, out double ix, out double iz, out double iy))
				{
					if (!first || from >= 0)
					{
						pts.Add((ix, iz));
						endPin = iy;
						break;
					}
				}
				// Near a road (not the one it left, at first): join it.
				if (!first && segments.Snap(nx, nz, 40.0, -1, out double sx, out double sz, out double sy))
				{
					pts.Add((sx, sz));
					endPin = sy;
					break;
				}
				pts.Add((nx, nz));
				x = nx;
				z = nz;
				h = nh;
				walked += seg;
			}
			if (pts.Count < 3 && double.IsNaN(endPin))
			{
				return null;
			}
			Road road = Make(kind, kind == RoadKind.Track ? "Track" : (place != null ? place.Name + " street" : "Street"), Lines.Resample(Rounded(pts), Step));
			if (road.S[road.Count - 1] < 50.0)
			{
				return null;
			}
			List<(int idx, double y)> pins = new List<(int, double)> { (0, y) };
			pins.Add((road.Count - 1, double.IsNaN(endPin) ? WorldGen.Base(road.X[road.Count - 1], road.Z[road.Count - 1]) : endPin));
			Add(road, pins);
			return road;
		}

		/// <summary>One round of corner cutting, ends kept: softens a walk's 30-unit steps.</summary>
		private static List<(double x, double z)> Rounded(List<(double x, double z)> pts)
		{
			List<(double x, double z)> next = new List<(double x, double z)> { pts[0] };
			for (int i = 0; i + 1 < pts.Count; i++)
			{
				(double x0, double z0) = pts[i];
				(double x1, double z1) = pts[i + 1];
				if (i > 0)
				{
					next.Add((x0 * 0.75 + x1 * 0.25, z0 * 0.75 + z1 * 0.25));
				}
				if (i + 2 < pts.Count)
				{
					next.Add((x0 * 0.25 + x1 * 0.75, z0 * 0.25 + z1 * 0.75));
				}
			}
			next.Add(pts[pts.Count - 1]);
			return next;
		}

		/// <summary>atan2 from plain arithmetic (a polynomial good to about 1e-5 radians), the same on every machine.</summary>
		internal static double Atan2(double y, double x)
		{
			double ax = Math.Abs(x), ay = Math.Abs(y);
			if (ax == 0.0 && ay == 0.0)
			{
				return 0.0;
			}
			double a = Math.Min(ax, ay) / Math.Max(ax, ay);
			double s = a * a;
			double r = ((-0.0464964749 * s + 0.15931422) * s - 0.327622764) * s * a + a;
			if (ay > ax)
			{
				r = DetMath.Pi / 2.0 - r;
			}
			if (x < 0.0)
			{
				r = DetMath.Pi - r;
			}
			return y < 0.0 ? -r : r;
		}

		/// <summary>The spawn: on the trunk road where it passes closest to the main town, in the inner eastbound lane, on a level stretch.</summary>
		private static void Spawn(Road trunk, Places.Place town)
		{
			int i = trunk.Count / 2;
			if (town != null)
			{
				double best = double.MaxValue;
				for (int k = 60; k < trunk.Count - 60; k++)
				{
					double dx = trunk.X[k] - town.X, dz = trunk.Z[k] - town.Z;
					if (dx * dx + dz * dz < best && !trunk.Bridge[k])
					{
						best = dx * dx + dz * dz;
						i = k;
					}
				}
				// A little before the closest point, so the junction to the town is just ahead.
				i = Math.Max(30, i - 40);
			}
			double dirX = trunk.X[i + 1] - trunk.X[i - 1], dirZ = trunk.Z[i + 1] - trunk.Z[i - 1];
			double d = Math.Sqrt(dirX * dirX + dirZ * dirZ);
			SpawnDirX = dirX / d;
			SpawnDirZ = dirZ / d;
			// Dead level from 20 samples behind to 12 ahead: the grid, the spawn and the start lights. The Multiplayer mod puts every car
			// at one fixed height on the grid, which must not be under the road anywhere along it.
			int a = Math.Max(0, i - 20), b = Math.Min(trunk.Count - 1, i + 12);
			double level = trunk.Y[i];
			for (int k = a; k <= b; k++)
			{
				trunk.Y[k] = level;
				trunk.Bridge[k] = false;
			}
			Hold(trunk, Classes[0].Grade, 0, trunk.Count - 1);
			// Right of the direction of travel: (dz, -dx). The inner lane's centre is the median (5) plus its shoulder (2) plus half a lane.
			const double lane = 5.0 + 2.0 + 5.5;
			SpawnX = trunk.X[i] + SpawnDirZ * lane;
			SpawnZ = trunk.Z[i] - SpawnDirX * lane;
			SpawnY = level;
		}

		// ---- the ground --------------------------------------------------------------------------------------------

		private const double Cell = 128.0;
		private const int Cells = 260;
		private const double Origin0 = -Cells * Cell / 2.0;
		private static int[] cellStart;
		private static long[] cellItems;

		/// <summary>The widest bank (cut or fill) any road has, plus its shoulder: how far a road's bed can reach.</summary>
		private static double Reach(Road road)
		{
			return road.Half + Shoulder + Classes[(int)road.Kind].Bank;
		}

		/// <summary>Each road segment is listed in every 128-unit cell its bank can reach, so <see cref="Ground"/> only looks at the few near a point.</summary>
		private static void Index()
		{
			int[] counts = new int[Cells * Cells + 1];
			for (int pass = 0; pass < 2; pass++)
			{
				if (pass == 1)
				{
					cellStart = new int[Cells * Cells + 1];
					for (int c = 0; c < Cells * Cells; c++)
					{
						cellStart[c + 1] = cellStart[c] + counts[c];
					}
					cellItems = new long[cellStart[Cells * Cells]];
					Array.Clear(counts, 0, counts.Length);
				}
				for (int r = 0; r < Roads.Count; r++)
				{
					Road road = Roads[r];
					double reach = Reach(road);
					for (int i = 0; i + 1 < road.Count; i++)
					{
						if (road.Bridge[i] && road.Bridge[i + 1])
						{
							continue;
						}
						int c0 = CellOf(Math.Min(road.X[i], road.X[i + 1]) - reach);
						int c1 = CellOf(Math.Max(road.X[i], road.X[i + 1]) + reach);
						int r0 = CellOf(Math.Min(road.Z[i], road.Z[i + 1]) - reach);
						int r1 = CellOf(Math.Max(road.Z[i], road.Z[i + 1]) + reach);
						for (int cz = r0; cz <= r1; cz++)
						{
							for (int cx = c0; cx <= c1; cx++)
							{
								int c = cz * Cells + cx;
								if (pass == 1)
								{
									cellItems[cellStart[c] + counts[c]] = ((long)r << 32) | (uint)i;
								}
								counts[c]++;
							}
						}
					}
				}
			}
		}

		private static int CellOf(double v)
		{
			return Math.Max(0, Math.Min(Cells - 1, (int)Math.Floor((v - Origin0) / Cell)));
		}

		/// <summary>
		/// The ground at (x, z) given the land there (<paramref name="land"/>), from sea level: the bed of the nearest road where a road
		/// is, and a bank blending back to the land beside it, wider where the road is far above or below the land (up to the class's
		/// limit), so cuts and embankments are about 1 in 1.6. Where two roads' banks overlap, the one that claims the point most wins.
		/// Bridges leave the ground alone.
		/// </summary>
		internal static double Ground(double x, double z, double land)
		{
			if (cellStart == null)
			{
				return land;
			}
			int c = CellOf(z) * Cells + CellOf(x);
			double bestW = 0.0;
			double bestY = land;
			for (int k = cellStart[c]; k < cellStart[c + 1]; k++)
			{
				long item = cellItems[k];
				Road road = Roads[(int)(item >> 32)];
				int i = (int)(uint)item;
				Lines.Project(road.X, road.Z, i, x, z, out double t, out double d);
				double inner = road.Half + Shoulder;
				double most = Classes[(int)road.Kind].Bank;
				if (d >= inner + most)
				{
					continue;
				}
				double ry = road.Y[i] + (road.Y[i + 1] - road.Y[i]) * t - BedDepth;
				double bank = Math.Max(12.0, Math.Min(most, Math.Abs(land - ry) * 1.6 + 8.0));
				double w = 1.0 - Noise.Step(inner, inner + bank, d);
				if (w > bestW)
				{
					bestW = w;
					bestY = ry;
				}
			}
			return land + (bestY - land) * bestW;
		}

		/// <summary>Distance from (x, z) to the nearest road of class <paramref name="kind"/> or smaller, up to <paramref name="most"/>.</summary>
		internal static double Nearest(double x, double z, double most, RoadKind kind)
		{
			if (cellStart == null)
			{
				return most;
			}
			int c = CellOf(z) * Cells + CellOf(x);
			double best = most;
			for (int k = cellStart[c]; k < cellStart[c + 1]; k++)
			{
				long item = cellItems[k];
				Road road = Roads[(int)(item >> 32)];
				if (road.Kind < kind)
				{
					continue;
				}
				int i = (int)(uint)item;
				if (Math.Abs(road.X[i] - x) > best + 10.0 || Math.Abs(road.Z[i] - z) > best + 10.0)
				{
					continue;
				}
				Lines.Project(road.X, road.Z, i, x, z, out double _, out double d);
				best = Math.Min(best, d);
			}
			return best;
		}

		/// <summary>
		/// For each of a tile's <paramref name="pixels"/> x <paramref name="pixels"/> pixels, how far its centre is from the nearest road's
		/// edge (up to 20; negative on a road), from the road pieces of the tile and its neighbours. For keeping trees and grass off the
		/// roads without asking every pixel.
		/// </summary>
		internal static float[] Clearance(int tx, int tz, float tileSize, int pixels)
		{
			float[] clear = new float[pixels * pixels];
			for (int k = 0; k < clear.Length; k++)
			{
				clear[k] = 20f;
			}
			double ox = tx * (double)tileSize, oz = tz * (double)tileSize;
			double size = tileSize / (double)pixels;
			for (int j = tz - 1; j <= tz + 1; j++)
			{
				for (int i = tx - 1; i <= tx + 1; i++)
				{
					List<long> list = PiecesIn(i, j, tileSize);
					if (list == null)
					{
						continue;
					}
					foreach (long piece in list)
					{
						Road road = Roads[(int)(piece >> 32)];
						int k = (int)(uint)piece;
						double reach = road.Half + 20.0;
						int p0 = Math.Max(0, (int)Math.Floor((Math.Min(road.X[k], road.X[k + 1]) - reach - ox) / size));
						int p1 = Math.Min(pixels - 1, (int)Math.Floor((Math.Max(road.X[k], road.X[k + 1]) + reach - ox) / size));
						int q0 = Math.Max(0, (int)Math.Floor((Math.Min(road.Z[k], road.Z[k + 1]) - reach - oz) / size));
						int q1 = Math.Min(pixels - 1, (int)Math.Floor((Math.Max(road.Z[k], road.Z[k + 1]) + reach - oz) / size));
						for (int q = q0; q <= q1; q++)
						{
							for (int p = p0; p <= p1; p++)
							{
								Lines.Project(road.X, road.Z, k, ox + (p + 0.5) * size, oz + (q + 0.5) * size, out double _, out double d);
								float edge = (float)(d - road.Half);
								if (edge < clear[q * pixels + p])
								{
									clear[q * pixels + p] = edge;
								}
							}
						}
					}
				}
			}
			return clear;
		}

		/// <summary>
		/// Whether (x, z) may be built on for road <paramref name="own"/> (-1: for no road): at least <paramref name="margin"/> off every
		/// road's edge, and no other road's edge nearer than <paramref name="ownEdge"/> (its distance from its own road's edge), so each
		/// road builds only on its own side of a block.
		/// </summary>
		internal static bool Claims(double x, double z, int own, double ownEdge, double margin)
		{
			if (cellStart == null)
			{
				return true;
			}
			int c = CellOf(z) * Cells + CellOf(x);
			for (int k = cellStart[c]; k < cellStart[c + 1]; k++)
			{
				long item = cellItems[k];
				int r = (int)(item >> 32);
				Road road = Roads[r];
				int i = (int)(uint)item;
				Lines.Project(road.X, road.Z, i, x, z, out double _, out double d);
				double edge = d - road.Half;
				if (edge < margin || (own >= 0 && r != own && edge < ownEdge - 0.5))
				{
					return false;
				}
			}
			return true;
		}

		/// <summary>
		/// The nearest point on a road (not on a bridge) to (x, z), looked for in the tiles round it, and the road's direction there. For
		/// putting a car back on dry land.
		/// </summary>
		internal static bool NearestRoad(double x, double z, float tileSize, out double rx, out double rz, out double dx, out double dz)
		{
			rx = rz = dx = dz = 0.0;
			int tx = (int)Math.Floor(x / tileSize), tz = (int)Math.Floor(z / tileSize);
			for (int reach = 1; reach <= 8; reach *= 2)
			{
				double best = double.MaxValue;
				for (int j = tz - reach; j <= tz + reach; j++)
				{
					for (int i = tx - reach; i <= tx + reach; i++)
					{
						List<long> list = PiecesIn(i, j, tileSize);
						if (list == null)
						{
							continue;
						}
						foreach (long piece in list)
						{
							Road road = Roads[(int)(piece >> 32)];
							int k = (int)(uint)piece;
							if (road.Bridge[k] || road.Kind == RoadKind.Track)
							{
								continue;
							}
							Lines.Project(road.X, road.Z, k, x, z, out double t, out double d);
							if (d < best)
							{
								best = d;
								rx = road.X[k] + (road.X[k + 1] - road.X[k]) * t;
								rz = road.Z[k] + (road.Z[k + 1] - road.Z[k]) * t;
								double len = Math.Max(1e-9, road.S[k + 1] - road.S[k]);
								dx = (road.X[k + 1] - road.X[k]) / len;
								dz = (road.Z[k + 1] - road.Z[k]) / len;
							}
						}
					}
				}
				if (best < double.MaxValue)
				{
					return true;
				}
			}
			return false;
		}

		// ---- tiles -------------------------------------------------------------------------------------------------

		private static Dictionary<long, List<long>> pieces;
		private static readonly object piecesGate = new object();

		/// <summary>
		/// The road pieces (road, sample i to i + 1) whose middle is in tile (tx, tz) of side <paramref name="tileSize"/>. Each piece is
		/// drawn by exactly one tile, and neighbouring pieces share their end points, so the road is seamless across tile edges.
		/// </summary>
		internal static List<long> PiecesIn(int tx, int tz, float tileSize)
		{
			Ensure();
			lock (piecesGate)
			{
				if (pieces == null)
				{
					pieces = new Dictionary<long, List<long>>();
					for (int r = 0; r < Roads.Count; r++)
					{
						Road road = Roads[r];
						for (int i = 0; i + 1 < road.Count; i++)
						{
							int px = (int)Math.Floor((road.X[i] + road.X[i + 1]) * 0.5 / tileSize);
							int pz = (int)Math.Floor((road.Z[i] + road.Z[i + 1]) * 0.5 / tileSize);
							long key = ((long)px << 32) | (uint)pz;
							if (!pieces.TryGetValue(key, out List<long> list))
							{
								list = new List<long>();
								pieces[key] = list;
							}
							list.Add(((long)r << 32) | (uint)i);
						}
					}
				}
				return pieces.TryGetValue(((long)tx << 32) | (uint)tz, out List<long> found) ? found : null;
			}
		}

		// ---- the segments, while building --------------------------------------------------------------------------

		/// <summary>
		/// The segments of every road built so far, by 64-unit cell (a segment is listed under both its ends' cells; segments are 8
		/// long), for snapping a new road onto the network and finding where it crosses.
		/// </summary>
		private sealed class Segments
		{
			private const double Size = 64.0;
			private readonly Dictionary<long, List<long>> grid = new Dictionary<long, List<long>>();

			private static long Key(int i, int j)
			{
				return ((long)i << 32) | (uint)j;
			}

			private static int Cell(double v)
			{
				return (int)Math.Floor(v / Size);
			}

			internal void Add(int r, Road road)
			{
				for (int i = 0; i + 1 < road.Count; i++)
				{
					long k0 = Key(Cell(road.X[i]), Cell(road.Z[i]));
					long k1 = Key(Cell(road.X[i + 1]), Cell(road.Z[i + 1]));
					Put(k0, ((long)r << 32) | (uint)i);
					if (k1 != k0)
					{
						Put(k1, ((long)r << 32) | (uint)i);
					}
				}
			}

			/// <summary>Road r was replaced by a longer copy (an extension at an edge): its entries are rebuilt.</summary>
			internal void Replace(int r, Road old, Road longer)
			{
				foreach (List<long> list in grid.Values)
				{
					list.RemoveAll(item => (int)(item >> 32) == r);
				}
				Add(r, longer);
			}

			private void Put(long key, long item)
			{
				if (!grid.TryGetValue(key, out List<long> list))
				{
					list = new List<long>();
					grid[key] = list;
				}
				list.Add(item);
			}

			/// <summary>Every road with a segment within <paramref name="radius"/> of (x, z) (by cells), each once, lowest index first.</summary>
			internal List<int> RoadsNear(double x, double z, double radius)
			{
				SortedSet<int> found = new SortedSet<int>();
				for (int j = Cell(z - radius); j <= Cell(z + radius); j++)
				{
					for (int i = Cell(x - radius); i <= Cell(x + radius); i++)
					{
						if (grid.TryGetValue(Key(i, j), out List<long> list))
						{
							foreach (long item in list)
							{
								found.Add((int)(item >> 32));
							}
						}
					}
				}
				return new List<int>(found);
			}

			/// <summary>The nearest point within <paramref name="radius"/> on any road (except road <paramref name="skip"/>), with the road's height there.</summary>
			internal bool Snap(double x, double z, double radius, int skip, out double px, out double pz, out double py)
			{
				px = pz = py = 0.0;
				double best = radius;
				bool found = false;
				for (int j = Cell(z - radius); j <= Cell(z + radius); j++)
				{
					for (int i = Cell(x - radius); i <= Cell(x + radius); i++)
					{
						if (!grid.TryGetValue(Key(i, j), out List<long> list))
						{
							continue;
						}
						foreach (long item in list)
						{
							int r = (int)(item >> 32);
							if (r == skip)
							{
								continue;
							}
							Road road = Roads[r];
							int k = (int)(uint)item;
							Lines.Project(road.X, road.Z, k, x, z, out double t, out double d);
							if (d < best)
							{
								best = d;
								px = road.X[k] + (road.X[k + 1] - road.X[k]) * t;
								pz = road.Z[k] + (road.Z[k + 1] - road.Z[k]) * t;
								py = road.Y[k] + (road.Y[k + 1] - road.Y[k]) * t;
								found = true;
							}
						}
					}
				}
				return found;
			}

			/// <summary>Where segment (ax, az)-(bx, bz) first crosses a road (except road <paramref name="skip"/>), with that road's height there.</summary>
			internal bool Intersect(double ax, double az, double bx, double bz, int skip, out double ix, out double iz, out double iy)
			{
				ix = iz = iy = 0.0;
				double bestT = 2.0;
				for (int j = Math.Min(Cell(az), Cell(bz)); j <= Math.Max(Cell(az), Cell(bz)); j++)
				{
					for (int i = Math.Min(Cell(ax), Cell(bx)); i <= Math.Max(Cell(ax), Cell(bx)); i++)
					{
						if (!grid.TryGetValue(Key(i, j), out List<long> list))
						{
							continue;
						}
						foreach (long item in list)
						{
							int r = (int)(item >> 32);
							if (r == skip)
							{
								continue;
							}
							Road road = Roads[r];
							int k = (int)(uint)item;
							if (Cross(ax, az, bx, bz, road.X[k], road.Z[k], road.X[k + 1], road.Z[k + 1], out double t, out double u) && t > 1e-6 && t < bestT)
							{
								bestT = t;
								ix = ax + (bx - ax) * t;
								iz = az + (bz - az) * t;
								iy = road.Y[k] + (road.Y[k + 1] - road.Y[k]) * u;
							}
						}
					}
				}
				return bestT <= 1.0;
			}

			/// <summary>
			/// Where the new road crosses roads already built, it is pinned to their height there, so the two meet level. Pins closer than
			/// 12 samples to one already pinned are skipped.
			/// </summary>
			internal void Crossings(Road road, List<(int idx, double y)> pins)
			{
				for (int i = 0; i + 1 < road.Count; i++)
				{
					if (Intersect(road.X[i], road.Z[i], road.X[i + 1], road.Z[i + 1], -1, out double _, out double _, out double iy))
					{
						bool close = false;
						foreach ((int idx, double _) in pins)
						{
							if (Math.Abs(idx - i) < 12)
							{
								close = true;
								break;
							}
						}
						if (!close)
						{
							pins.Add((i, iy));
						}
					}
				}
				pins.Sort((p, q) => p.idx.CompareTo(q.idx));
			}

			private static bool Cross(double ax, double az, double bx, double bz, double cx, double cz, double dx, double dz, out double t, out double u)
			{
				t = u = 0.0;
				double den = (bx - ax) * (dz - cz) - (bz - az) * (dx - cx);
				if (den == 0.0)
				{
					return false;
				}
				t = ((cx - ax) * (dz - cz) - (cz - az) * (dx - cx)) / den;
				u = ((cx - ax) * (bz - az) - (cz - az) * (bx - ax)) / den;
				return t >= 0.0 && t <= 1.0 && u >= 0.0 && u <= 1.0;
			}
		}

		// ---- routing -----------------------------------------------------------------------------------------------

		/// <summary>
		/// A* (or Dijkstra, to reach the nearest road) over a 100-unit grid of <see cref="WorldGen.Base"/>. A step costs its length times
		/// (1 + steepness (grade / soft)^2) for the road's class; water is closed, and so is the edge wall; crossing a river costs a
		/// bridge (dearer for small roads); stepping along a road of the same class or bigger costs a third, so roads share the way; and
		/// the trunk road pays to go through a town (it bypasses them). Ties are broken by cell number, so the result is the same on
		/// every machine.
		/// </summary>
		private sealed class Router
		{
			private const double Size = 100.0;
			private const int N = 320;
			private const double Min = -N * Size / 2.0;
			private readonly double[] h = new double[N * N];
			private readonly bool[] closed = new bool[N * N];
			private readonly double[] river = new double[N * N];
			private readonly bool[] core = new bool[N * N];
			/// <summary>The biggest road class through each cell (smallest number), or 255.</summary>
			private readonly byte[] road = new byte[N * N];

			internal Router()
			{
				double limit = WorldGen.Half - WorldGen.WallDepth - 300.0;
				for (int c = 0; c < N * N; c++)
				{
					double x = X(c), z = Z(c);
					h[c] = WorldGen.Base(x, z);
					closed[c] = Math.Abs(x) > limit || z > limit || !double.IsNaN(Rivers.SurfaceAt(x, z));
					road[c] = 255;
					foreach (Places.Place p in Places.All)
					{
						if (p.Kind == PlaceKind.Town && (p.X - x) * (p.X - x) + (p.Z - z) * (p.Z - z) < p.Radius * p.Radius * 0.6)
						{
							core[c] = true;
						}
					}
				}
				foreach (Rivers.River r in Rivers.All)
				{
					for (int i = 0; i < r.Count; i++)
					{
						int c = Index(r.X[i], r.Z[i]);
						if (c >= 0)
						{
							river[c] = Math.Max(river[c], r.Half[i]);
						}
					}
				}
			}

			internal double X(int c)
			{
				return Min + (c % N + 0.5) * Size;
			}

			internal double Z(int c)
			{
				return Min + (c / N + 0.5) * Size;
			}

			internal bool OnRoad(int c)
			{
				return road[c] != 255;
			}

			private static int Index(double x, double z)
			{
				int i = (int)Math.Floor((x - Min) / Size);
				int j = (int)Math.Floor((z - Min) / Size);
				return i < 0 || j < 0 || i >= N || j >= N ? -1 : j * N + i;
			}

			/// <summary>Marks the cells a built road runs through with its class.</summary>
			internal void Mark(Road built)
			{
				for (int i = 0; i < built.Count; i++)
				{
					int c = Index(built.X[i], built.Z[i]);
					if (c >= 0)
					{
						road[c] = (byte)Math.Min(road[c], (int)built.Kind);
					}
				}
			}

			/// <summary>
			/// The cells from (ax, az) to (bx, bz); or, with <paramref name="joins"/> 0 or more, to the nearest cell with a road of that
			/// class or bigger. Null if there is no way.
			/// </summary>
			internal List<int> Find(RoadKind kind, double ax, double az, double bx, double bz, int joins)
			{
				int start = Index(ax, az);
				int goal = joins >= 0 ? -1 : Index(bx, bz);
				if (start < 0 || (joins < 0 && goal < 0))
				{
					return null;
				}
				Class cl = Classes[(int)kind];
				double[] g = new double[N * N];
				int[] from = new int[N * N];
				for (int k = 0; k < g.Length; k++)
				{
					g[k] = double.MaxValue;
					from[k] = -1;
				}
				Heap open = new Heap();
				g[start] = 0.0;
				open.Push(Guess(start, goal), start);
				int reached = -1;
				while (open.Count > 0)
				{
					int cur = open.Pop(out double f);
					if (f > g[cur] + Guess(cur, goal) + 1e-9)
					{
						continue;
					}
					if (cur == goal || (joins >= 0 && cur != start && road[cur] <= joins))
					{
						reached = cur;
						break;
					}
					int ci = cur % N, cj = cur / N;
					for (int dj = -1; dj <= 1; dj++)
					{
						for (int di = -1; di <= 1; di++)
						{
							if (di == 0 && dj == 0)
							{
								continue;
							}
							int ni = ci + di, nj = cj + dj;
							if (ni < 0 || nj < 0 || ni >= N || nj >= N)
							{
								continue;
							}
							int next = nj * N + ni;
							if (closed[next] && next != goal)
							{
								continue;
							}
							double len = (di != 0 && dj != 0) ? Size * 1.4142135623730951 : Size;
							double cost;
							if (road[next] <= (int)kind && road[cur] <= (int)kind)
							{
								cost = len * 0.33;
							}
							else
							{
								double grade = Math.Abs(h[next] - h[cur]) / len / cl.Soft;
								cost = len * (1.0 + cl.Steep * grade * grade);
								if (river[next] > 0.0 && road[next] == 255)
								{
									cost += len * (3.0 + river[next] / 4.0) * (kind >= RoadKind.Lane ? 3.0 : 1.0);
								}
								if (kind == RoadKind.Trunk && core[next])
								{
									cost += len * 3.0;
								}
							}
							double total = g[cur] + cost;
							if (total < g[next])
							{
								g[next] = total;
								from[next] = cur;
								open.Push(total + Guess(next, goal), next);
							}
						}
					}
				}
				if (reached < 0)
				{
					return null;
				}
				List<int> path = new List<int>();
				for (int c = reached; c >= 0; c = from[c])
				{
					path.Add(c);
					if (c == start)
					{
						break;
					}
				}
				path.Reverse();
				return path;
			}

			private double Guess(int a, int b)
			{
				if (b < 0)
				{
					return 0.0;
				}
				double dx = (a % N - b % N) * Size;
				double dz = (a / N - b / N) * Size;
				return Math.Sqrt(dx * dx + dz * dz) * 0.33;
			}
		}
	}
}
