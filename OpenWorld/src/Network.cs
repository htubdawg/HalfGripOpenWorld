using System;
using System.Collections.Generic;

namespace HalfGripOpenWorld
{
	internal enum RoadKind
	{
		Highway = 0,
		Rural = 1,
		Street = 2,
	}

	/// <summary>
	/// The towns, villages and roads of the world, laid out once from the seed: two towns at opposite ends joined by a highway, villages in
	/// the open country between the mountain ranges, rural roads linking every place to its neighbours, and a grid of streets in each
	/// town. Built with <see cref="Rng"/> and plain arithmetic only, so every player gets the same layout, and never changed afterwards,
	/// so the worker threads that build tiles can read it freely.
	///
	/// Roads are routed with A* over a 200-unit grid of the land (<see cref="Route"/>), costed by grade, so they go round mountains and
	/// up valleys; then smoothed into curves and given a height profile that follows the land loosely, never steeper than the road's
	/// limit, and meets every road it crosses at that road's height (an at-grade junction). The ground is then cut and filled to each
	/// road's bed in <see cref="Ground"/>.
	/// </summary>
	internal static class Network
	{
		internal sealed class Site
		{
			public string Name;
			public bool Town;
			public double X;
			public double Z;
			public double Y;
			public double Radius;
			public double Blend;
		}

		internal sealed class Road
		{
			public RoadKind Kind;
			public string Name;
			/// <summary>Half the paved width.</summary>
			public double Half;
			public double[] X;
			public double[] Z;
			public double[] Y;
			/// <summary>Distance along the road at each sample, for the texture.</summary>
			public double[] S;
			public int Count => X.Length;
		}

		/// <summary>Distance between a road's samples.</summary>
		internal const double Step = 8.0;

		/// <summary>Half widths: a lane is 11 units (a car is about 5.4 wide), as on the Highway mod's roads.</summary>
		internal const double HighwayHalf = 33.0;
		internal const double RuralHalf = 12.0;
		internal const double StreetHalf = 12.0;

		/// <summary>Flat ground either side of the paving before the bank starts.</summary>
		internal const double Shoulder = 4.0;

		/// <summary>The widest bank (cut or fill) beside a road.</summary>
		private const double MaxBank = 150.0;

		/// <summary>The road bed is this far under the road surface, so the ground never shows through the paving.</summary>
		internal const double BedDepth = 0.3;

		internal const double TownRadius = 1500.0;
		private const double TownBlend = 900.0;
		private const double VillageRadius = 180.0;
		private const double VillageBlend = 500.0;
		private const double StreetSpacing = 200.0;

		private static readonly string[] TownNames = { "Westhaven", "Eastport" };
		private static readonly string[] VillageNames =
		{
			"Ashby", "Brookfield", "Carrow", "Dunmore", "Elmsted", "Fenwick", "Glenford", "Harrow", "Ivybridge", "Kestrel", "Lowmoor", "Millbrook",
			"Northcote", "Oakhurst", "Pinewick", "Redford", "Stonebury", "Thornton",
		};

		/// <summary>Every town and village. Empty until <see cref="Ensure"/> has run.</summary>
		internal static Site[] Sites = new Site[0];

		internal static readonly List<Road> Roads = new List<Road>();

		/// <summary>Where the player starts: on the highway just outside the first town, heading for the second.</summary>
		internal static double SpawnX;
		internal static double SpawnZ;
		internal static double SpawnY;
		internal static double SpawnDirX = 1.0;
		internal static double SpawnDirZ;

		/// <summary>
		/// Added to every height in the world once the layout is built, so the road at the spawn is at 0.5: the Multiplayer mod puts every
		/// car at height 1 on the grid (its own grid rule), which is then just resting on the road. 0 while the layout is being built.
		/// </summary>
		internal static double Lift;

		/// <summary>Milliseconds the layout took, for the log.</summary>
		internal static double BuildMs;

		private static readonly object gate = new object();
		private static volatile bool built;

		/// <summary>Builds the layout if it has not been built yet. Safe from any thread; the first call does the work.</summary>
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

		private static void Build()
		{
			Rng rng = new Rng(Noise.Mix(WorldGen.Seed ^ 0x5157_E5UL));
			List<Site> sites = new List<Site>();
			Site west = PlaceTown(rng, TownNames[0], -11500.0, -8500.0);
			Site east = PlaceTown(rng, TownNames[1], 8500.0, 11500.0);
			sites.Add(west);
			sites.Add(east);
			PlaceVillages(rng, sites);
			foreach (Site site in sites)
			{
				site.Y = SiteHeight(site);
			}
			Sites = sites.ToArray();

			Router router = new Router();
			Road highway = Highway(router, west, east);
			Roads.Add(highway);
			Spawn(highway);
			foreach ((Site a, Site b) in Links(sites))
			{
				Road road = Rural(router, a, b);
				if (road != null)
				{
					Roads.Add(road);
				}
			}
			foreach (Site town in sites)
			{
				if (town.Town)
				{
					Streets(rng, town);
				}
			}
			Index();

			double lift = 0.5 - SpawnY;
			foreach (Site site in Sites)
			{
				site.Y += lift;
			}
			foreach (Road road in Roads)
			{
				for (int i = 0; i < road.Count; i++)
				{
					road.Y[i] += lift;
				}
			}
			SpawnY += lift;
			Lift = lift;
		}

		// ---- places ------------------------------------------------------------------------------------------------

		/// <summary>The flattest open ground (least mountain within the town and a ring round it) of 60 tries in the given band of x.</summary>
		private static Site PlaceTown(Rng rng, string name, double x0, double x1)
		{
			Site best = null;
			double bestScore = double.MaxValue;
			for (int i = 0; i < 60; i++)
			{
				double x = rng.Range(x0, x1);
				double z = rng.Range(-6000.0, 6000.0);
				double score = MaskAround(x, z, TownRadius + 1500.0) + Roughness(x, z, TownRadius) / 200.0;
				if (score < bestScore)
				{
					bestScore = score;
					best = new Site { Name = name, Town = true, X = x, Z = z, Radius = TownRadius, Blend = TownBlend };
				}
			}
			return best;
		}

		/// <summary>Up to eighteen villages in open country, well apart from each other and from the towns.</summary>
		private static void PlaceVillages(Rng rng, List<Site> sites)
		{
			int named = 0;
			for (int i = 0; i < 600 && named < VillageNames.Length; i++)
			{
				double x = rng.Range(-13000.0, 13000.0);
				double z = rng.Range(-13000.0, 13000.0);
				if (WorldGen.MountainMask(x, z) > 0.02 || MaskAround(x, z, 700.0) > 0.15)
				{
					continue;
				}
				bool clear = true;
				foreach (Site s in sites)
				{
					double need = s.Town ? s.Radius + 2500.0 : 3500.0;
					if ((s.X - x) * (s.X - x) + (s.Z - z) * (s.Z - z) < need * need)
					{
						clear = false;
						break;
					}
				}
				if (clear)
				{
					sites.Add(new Site { Name = VillageNames[named++], X = x, Z = z, Radius = VillageRadius, Blend = VillageBlend });
				}
			}
		}

		/// <summary>The most mountain-mask at the centre and at sixteen points on two rings out to <paramref name="r"/>.</summary>
		private static double MaskAround(double x, double z, double r)
		{
			double worst = WorldGen.MountainMask(x, z);
			for (int k = 0; k < 8; k++)
			{
				double a = k * DetMath.Pi / 4.0;
				double c = DetMath.Cos(a);
				double s = DetMath.Sin(a);
				worst = Math.Max(worst, WorldGen.MountainMask(x + c * r, z + s * r));
				worst = Math.Max(worst, WorldGen.MountainMask(x + c * r * 0.5, z + s * r * 0.5));
			}
			return worst;
		}

		/// <summary>The spread of the land's height over a disc (highest minus lowest of 17 samples).</summary>
		private static double Roughness(double x, double z, double r)
		{
			double lo = double.MaxValue;
			double hi = double.MinValue;
			for (int k = 0; k < 17; k++)
			{
				double rr = k == 0 ? 0.0 : (k <= 8 ? r * 0.5 : r);
				double a = k * DetMath.Pi / 4.0;
				double h = WorldGen.Natural(x + DetMath.Cos(a) * rr, z + DetMath.Sin(a) * rr);
				lo = Math.Min(lo, h);
				hi = Math.Max(hi, h);
			}
			return hi - lo;
		}

		/// <summary>A site's plateau height: the land's average over the plateau, so as much is filled as is cut.</summary>
		private static double SiteHeight(Site site)
		{
			double sum = WorldGen.Natural(site.X, site.Z);
			int n = 1;
			for (int ring = 1; ring <= 2; ring++)
			{
				double r = site.Radius * ring / 2.0;
				for (int k = 0; k < 8; k++)
				{
					double a = k * DetMath.Pi / 4.0;
					sum += WorldGen.Natural(site.X + DetMath.Cos(a) * r, site.Z + DetMath.Sin(a) * r);
					n++;
				}
			}
			return sum / n;
		}

		/// <summary>
		/// Which places a rural road joins: a minimum spanning tree over all of them (so everything is reachable), plus each place's nearest
		/// neighbour it is not yet joined to when that is under 7 km (so there are loops, not only dead ends). Never town to town: the
		/// highway does that.
		/// </summary>
		private static List<(Site, Site)> Links(List<Site> sites)
		{
			int n = sites.Count;
			bool[,] joined = new bool[n, n];
			joined[0, 1] = joined[1, 0] = true;
			List<(Site, Site)> links = new List<(Site, Site)>();
			bool[] inTree = new bool[n];
			inTree[0] = true;
			for (int added = 1; added < n; added++)
			{
				int bi = -1, bj = -1;
				double best = double.MaxValue;
				for (int i = 0; i < n; i++)
				{
					if (!inTree[i])
					{
						continue;
					}
					for (int j = 0; j < n; j++)
					{
						if (inTree[j] || (sites[i].Town && sites[j].Town))
						{
							continue;
						}
						double d = Dist(sites[i], sites[j]);
						if (d < best)
						{
							best = d;
							bi = i;
							bj = j;
						}
					}
				}
				if (bj < 0)
				{
					break;
				}
				inTree[bj] = true;
				joined[bi, bj] = joined[bj, bi] = true;
				links.Add((sites[bi], sites[bj]));
			}
			for (int i = 0; i < n; i++)
			{
				int nearest = -1;
				double best = 7000.0;
				for (int j = 0; j < n; j++)
				{
					if (j != i && !joined[i, j] && !(sites[i].Town && sites[j].Town) && Dist(sites[i], sites[j]) < best)
					{
						best = Dist(sites[i], sites[j]);
						nearest = j;
					}
				}
				if (nearest >= 0)
				{
					joined[i, nearest] = joined[nearest, i] = true;
					links.Add((sites[i], sites[nearest]));
				}
			}
			return links;
		}

		private static double Dist(Site a, Site b)
		{
			return Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Z - b.Z) * (a.Z - b.Z));
		}

		/// <summary>Where a road from <paramref name="site"/> towards (tx, tz) starts: a town's ring road, or a village's centre.</summary>
		private static void Gate(Site site, double tx, double tz, out double x, out double z)
		{
			if (!site.Town)
			{
				x = site.X;
				z = site.Z;
				return;
			}
			double dx = tx - site.X;
			double dz = tz - site.Z;
			double d = Math.Sqrt(dx * dx + dz * dz);
			x = site.X + dx / d * site.Radius;
			z = site.Z + dz / d * site.Radius;
		}

		// ---- roads -------------------------------------------------------------------------------------------------

		private static Road Highway(Router router, Site a, Site b)
		{
			Gate(a, b.X, b.Z, out double ax, out double az);
			Gate(b, a.X, a.Z, out double bx, out double bz);
			List<(double x, double z)> path = router.Find(ax, az, bx, bz, 0.04, 6.0, false);
			Road road = Make(RoadKind.Highway, a.Name + " - " + b.Name, HighwayHalf, Smooth(path, 5, 4));
			Profile(road, 0.05, 45, new List<(int, double)> { (0, a.Y), (road.Count - 1, b.Y) });
			// The first stretch (the grid, the spawn and the start lights) is dead level at the town's height: the Multiplayer mod puts
			// every car at one fixed height on the grid, which must not be under the road anywhere along it.
			double rise = 0.05 * Step;
			int level = Math.Min(road.Count - 2, SpawnSample + 10);
			for (int i = 0; i <= level; i++)
			{
				road.Y[i] = a.Y;
			}
			for (int i = level + 1; i < road.Count - 1; i++)
			{
				double held = Math.Max(road.Y[i - 1] - rise, Math.Min(road.Y[i - 1] + rise, road.Y[i]));
				if (held == road.Y[i])
				{
					break;
				}
				road.Y[i] = held;
			}
			router.Mark(road);
			return road;
		}

		private static Road Rural(Router router, Site a, Site b)
		{
			Gate(a, b.X, b.Z, out double ax, out double az);
			Gate(b, a.X, a.Z, out double bx, out double bz);
			List<(double x, double z)> path = router.Find(ax, az, bx, bz, 0.07, 2.0, true);
			if (path == null)
			{
				return null;
			}
			Road road = Make(RoadKind.Rural, a.Name + " - " + b.Name, RuralHalf, Smooth(path, 2, 3));
			List<(int, double)> pins = new List<(int, double)> { (0, a.Y), (road.Count - 1, b.Y) };
			Crossings(road, pins);
			Profile(road, 0.09, 20, pins);
			router.Mark(road);
			return road;
		}

		/// <summary>A town's streets: a ring road at its edge and a grid across it, turned to a seeded angle, all at the town's height.</summary>
		private static void Streets(Rng rng, Site town)
		{
			int ringCount = (int)Math.Ceiling(2.0 * DetMath.Pi * town.Radius / Step);
			List<(double, double)> ring = new List<(double, double)>();
			for (int i = 0; i <= ringCount; i++)
			{
				double a = 2.0 * DetMath.Pi * i / ringCount;
				ring.Add((town.X + DetMath.Cos(a) * town.Radius, town.Z + DetMath.Sin(a) * town.Radius));
			}
			Flat(Make(RoadKind.Street, town.Name + " ring", StreetHalf, ring), town.Y);

			double angle = rng.Next() * DetMath.Pi / 2.0;
			double ux = DetMath.Cos(angle);
			double uz = DetMath.Sin(angle);
			int lines = (int)(town.Radius / StreetSpacing);
			for (int dir = 0; dir < 2; dir++)
			{
				double ax = dir == 0 ? ux : -uz;
				double az = dir == 0 ? uz : ux;
				for (int k = -lines; k <= lines; k++)
				{
					double o = k * StreetSpacing;
					double reach = town.Radius * town.Radius - o * o;
					if (reach <= 0.0)
					{
						continue;
					}
					// Each street runs right up to the ring road.
					double l = Math.Sqrt(reach);
					if (l < 150.0)
					{
						continue;
					}
					double cx = town.X - az * o;
					double cz = town.Z + ax * o;
					List<(double, double)> line = new List<(double, double)> { (cx - ax * l, cz - az * l), (cx + ax * l, cz + az * l) };
					Flat(Make(RoadKind.Street, town.Name + " street", StreetHalf, Resample(line)), town.Y);
				}
			}
		}

		private static void Flat(Road road, double y)
		{
			for (int i = 0; i < road.Count; i++)
			{
				road.Y[i] = y;
			}
			Roads.Add(road);
		}

		private static Road Make(RoadKind kind, string name, double half, List<(double x, double z)> points)
		{
			int n = points.Count;
			Road road = new Road { Kind = kind, Name = name, Half = half, X = new double[n], Z = new double[n], Y = new double[n], S = new double[n] };
			for (int i = 0; i < n; i++)
			{
				road.X[i] = points[i].x;
				road.Z[i] = points[i].z;
				if (i > 0)
				{
					double dx = road.X[i] - road.X[i - 1];
					double dz = road.Z[i] - road.Z[i - 1];
					road.S[i] = road.S[i - 1] + Math.Sqrt(dx * dx + dz * dz);
				}
			}
			return road;
		}

		/// <summary>
		/// Grid path to curve: every <paramref name="keep"/>th cell centre (and the ends), rounded off by Chaikin's corner cutting
		/// <paramref name="rounds"/> times, then resampled every <see cref="Step"/> units.
		/// </summary>
		private static List<(double x, double z)> Smooth(List<(double x, double z)> path, int keep, int rounds)
		{
			List<(double x, double z)> pts = new List<(double x, double z)>();
			for (int i = 0; i < path.Count; i += keep)
			{
				pts.Add(path[i]);
			}
			if (pts[pts.Count - 1] != path[path.Count - 1])
			{
				pts.Add(path[path.Count - 1]);
			}
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
			return Resample(pts);
		}

		/// <summary>The polyline resampled every <see cref="Step"/> units (ends kept).</summary>
		private static List<(double x, double z)> Resample(List<(double x, double z)> pts)
		{
			List<(double x, double z)> output = new List<(double x, double z)> { pts[0] };
			double carry = 0.0;
			for (int i = 0; i + 1 < pts.Count; i++)
			{
				(double x0, double z0) = pts[i];
				(double x1, double z1) = pts[i + 1];
				double len = Math.Sqrt((x1 - x0) * (x1 - x0) + (z1 - z0) * (z1 - z0));
				double at = Step - carry;
				while (at <= len)
				{
					double t = at / len;
					output.Add((x0 + (x1 - x0) * t, z0 + (z1 - z0) * t));
					at += Step;
				}
				carry = len - (at - Step);
			}
			(double ex, double ez) = pts[pts.Count - 1];
			(double lx, double lz) = output[output.Count - 1];
			if ((ex - lx) * (ex - lx) + (ez - lz) * (ez - lz) > Step * Step * 0.25)
			{
				output.Add((ex, ez));
			}
			else
			{
				output[output.Count - 1] = (ex, ez);
			}
			return output;
		}

		/// <summary>
		/// Where this road crosses one already built, it is pinned to that road's height there, so the two meet level. Pins closer than
		/// 25 samples to one already pinned are skipped.
		/// </summary>
		private static void Crossings(Road road, List<(int idx, double y)> pins)
		{
			// The built roads' segments by 64-unit cell (a segment is listed under both its ends' cells; segments are 8 long).
			const double cell = 64.0;
			Dictionary<long, List<(int r, int j)>> grid = new Dictionary<long, List<(int r, int j)>>();
			for (int r = 0; r < Roads.Count; r++)
			{
				Road other = Roads[r];
				for (int j = 0; j + 1 < other.Count; j++)
				{
					long k0 = Key(other.X[j], other.Z[j], cell);
					long k1 = Key(other.X[j + 1], other.Z[j + 1], cell);
					Add(grid, k0, (r, j));
					if (k1 != k0)
					{
						Add(grid, k1, (r, j));
					}
				}
			}
			for (int i = 0; i + 1 < road.Count; i++)
			{
				double ax = road.X[i], az = road.Z[i], bx = road.X[i + 1], bz = road.Z[i + 1];
				long ka = Key(ax, az, cell);
				long kb = Key(bx, bz, cell);
				for (int pass = 0; pass < 2; pass++)
				{
					if (pass == 1 && kb == ka)
					{
						break;
					}
					if (!grid.TryGetValue(pass == 0 ? ka : kb, out List<(int r, int j)> near))
					{
						continue;
					}
					foreach ((int r, int j) in near)
					{
						Road other = Roads[r];
						double cx = other.X[j], cz = other.Z[j], dx = other.X[j + 1], dz = other.Z[j + 1];
						double den = (bx - ax) * (dz - cz) - (bz - az) * (dx - cx);
						if (den == 0.0)
						{
							continue;
						}
						double t = ((cx - ax) * (dz - cz) - (cz - az) * (dx - cx)) / den;
						double u = ((cx - ax) * (bz - az) - (cz - az) * (bx - ax)) / den;
						if (t < 0.0 || t > 1.0 || u < 0.0 || u > 1.0)
						{
							continue;
						}
						bool close = false;
						foreach ((int idx, double _) in pins)
						{
							if (Math.Abs(idx - i) < 25)
							{
								close = true;
								break;
							}
						}
						if (!close)
						{
							pins.Add((i, other.Y[j] + (other.Y[j + 1] - other.Y[j]) * u));
						}
					}
				}
			}
			pins.Sort((p, q) => p.idx.CompareTo(q.idx));
		}

		private static long Key(double x, double z, double cell)
		{
			return ((long)Math.Floor(x / cell) << 32) | (uint)(int)Math.Floor(z / cell);
		}

		private static void Add(Dictionary<long, List<(int r, int j)>> grid, long key, (int r, int j) item)
		{
			if (!grid.TryGetValue(key, out List<(int r, int j)> list))
			{
				list = new List<(int r, int j)>();
				grid[key] = list;
			}
			list.Add(item);
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
			double rise = grade * Step;
			for (int round = 0; round < 3; round++)
			{
				Pin(y, pins);
				for (int i = 1; i < n; i++)
				{
					y[i] = Math.Max(y[i - 1] - rise, Math.Min(y[i - 1] + rise, y[i]));
				}
				for (int i = n - 2; i >= 0; i--)
				{
					y[i] = Math.Max(y[i + 1] - rise, Math.Min(y[i + 1] + rise, y[i]));
				}
			}
			Pin(y, pins);
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

		/// <summary>The highway sample the spawn is at: 400 units from the first town.</summary>
		private const int SpawnSample = (int)(400.0 / Step);

		/// <summary>The spawn: 400 units along the highway from the first town, in the right-hand lanes, facing the second town.</summary>
		private static void Spawn(Road highway)
		{
			int i = Math.Min(highway.Count - 2, SpawnSample);
			double dx = highway.X[i + 1] - highway.X[i];
			double dz = highway.Z[i + 1] - highway.Z[i];
			double d = Math.Sqrt(dx * dx + dz * dz);
			SpawnDirX = dx / d;
			SpawnDirZ = dz / d;
			// Right of the direction of travel: (dz, -dx). The inner lane's centre is the median (5) plus its shoulder (2) plus half a lane.
			const double lane = 5.0 + 2.0 + 5.5;
			SpawnX = highway.X[i] + SpawnDirZ * lane;
			SpawnZ = highway.Z[i] - SpawnDirX * lane;
			SpawnY = highway.Y[i];
		}

		// ---- the ground --------------------------------------------------------------------------------------------

		private const double Cell = 128.0;
		private const int Cells = 260;
		private const double Origin0 = -Cells * Cell / 2.0;
		private static int[] cellStart;
		private static long[] cellItems;

		/// <summary>
		/// Each road segment is listed in every 128-unit cell its bank can reach, so <see cref="Ground"/> only looks at the few segments
		/// near a point.
		/// </summary>
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
					// A street lies on its town's flat plateau, so it has no bank to speak of.
					double reach = road.Half + Shoulder + (road.Kind == RoadKind.Street ? 16.0 : MaxBank);
					for (int i = 0; i + 1 < road.Count; i++)
					{
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
		/// The ground at (x, z) given the land there (<paramref name="land"/>): the bed of the nearest road where a road is, and a bank
		/// blending back to the land beside it. The bank is wider where the road is far above or below the land (up to 150 units), so cuts
		/// and embankments are about 1 in 1.6. Where two roads' banks overlap, the one that claims the point most wins.
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
				double ax = road.X[i], az = road.Z[i];
				double sx = road.X[i + 1] - ax, sz = road.Z[i + 1] - az;
				double len2 = sx * sx + sz * sz;
				double t = len2 > 0.0 ? ((x - ax) * sx + (z - az) * sz) / len2 : 0.0;
				t = t < 0.0 ? 0.0 : (t > 1.0 ? 1.0 : t);
				double px = ax + sx * t - x;
				double pz = az + sz * t - z;
				double d = Math.Sqrt(px * px + pz * pz);
				double inner = road.Half + Shoulder;
				if (d >= inner + (road.Kind == RoadKind.Street ? 16.0 : MaxBank))
				{
					continue;
				}
				double ry = road.Y[i] + (road.Y[i + 1] - road.Y[i]) * t - BedDepth;
				double bank = Math.Max(16.0, Math.Min(MaxBank, Math.Abs(land - ry) * 1.6 + 12.0));
				double w = 1.0 - Noise.Step(inner, inner + bank, d);
				if (w > bestW)
				{
					bestW = w;
					bestY = ry;
				}
			}
			return land + (bestY - land) * bestW;
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

		// ---- routing -----------------------------------------------------------------------------------------------

		/// <summary>
		/// A* over a 200-unit grid of the land. A step costs its length times (1 + 3 (grade / soft)^2), so gentle ground is cheap and
		/// steep ground dear; the edge wall is closed. Rural roads also pay to run on or beside a road already there (so they cross rather
		/// than run alongside) and to cut through a town (they go round to its ring road). Ties are broken by cell number, so the result is the same
		/// on every machine.
		/// </summary>
		private sealed class Router
		{
			private const double Size = 200.0;
			private const int N = 150;
			private const double Min = -N * Size / 2.0;
			private readonly double[] h = new double[N * N];
			private readonly bool[] taken = new bool[N * N];
			private readonly bool[] town = new bool[N * N];

			internal Router()
			{
				for (int j = 0; j < N; j++)
				{
					for (int i = 0; i < N; i++)
					{
						double x = Min + (i + 0.5) * Size;
						double z = Min + (j + 0.5) * Size;
						h[j * N + i] = WorldGen.Base(x, z);
						foreach (Site s in Sites)
						{
							if (s.Town && (s.X - x) * (s.X - x) + (s.Z - z) * (s.Z - z) < (s.Radius - 150.0) * (s.Radius - 150.0))
							{
								town[j * N + i] = true;
							}
						}
					}
				}
			}

			/// <summary>Marks the cells a built road runs through, and the cells beside them, so a new road does not run alongside it.</summary>
			internal void Mark(Road road)
			{
				for (int i = 0; i < road.Count; i++)
				{
					int c = Index(road.X[i], road.Z[i]);
					if (c < 0)
					{
						continue;
					}
					int ci = c % N, cj = c / N;
					for (int dj = -1; dj <= 1; dj++)
					{
						for (int di = -1; di <= 1; di++)
						{
							int ni = ci + di, nj = cj + dj;
							if (ni >= 0 && nj >= 0 && ni < N && nj < N)
							{
								taken[nj * N + ni] = true;
							}
						}
					}
				}
			}

			private static int Index(double x, double z)
			{
				int i = (int)Math.Floor((x - Min) / Size);
				int j = (int)Math.Floor((z - Min) / Size);
				return i < 0 || j < 0 || i >= N || j >= N ? -1 : j * N + i;
			}

			private static bool Open(int i, int j)
			{
				double x = Min + (i + 0.5) * Size;
				double z = Min + (j + 0.5) * Size;
				double limit = WorldGen.Half - WorldGen.WallDepth - 500.0;
				return Math.Abs(x) < limit && Math.Abs(z) < limit;
			}

			internal List<(double x, double z)> Find(double ax, double az, double bx, double bz, double soft, double steepness, bool rural)
			{
				int start = Index(ax, az);
				int goal = Index(bx, bz);
				if (start < 0 || goal < 0)
				{
					return null;
				}
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
				while (open.Count > 0)
				{
					int cur = open.Pop(out double f);
					if (cur == goal)
					{
						break;
					}
					if (f > g[cur] + Guess(cur, goal) + 1e-9)
					{
						continue;
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
							if (ni < 0 || nj < 0 || ni >= N || nj >= N || !Open(ni, nj))
							{
								continue;
							}
							int next = nj * N + ni;
							double len = (di != 0 && dj != 0) ? Size * 1.4142135623730951 : Size;
							double grade = Math.Abs(h[next] - h[cur]) / len / soft;
							double cost = len * (1.0 + steepness * grade * grade);
							if (rural && taken[next] && next != goal)
							{
								cost += len * 4.0;
							}
							if (rural && town[next] && next != goal)
							{
								cost += len * 20.0;
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
				if (from[goal] < 0 && goal != start)
				{
					return null;
				}
				List<(double x, double z)> path = new List<(double x, double z)>();
				for (int c = goal; c >= 0; c = from[c])
				{
					path.Add((Min + (c % N + 0.5) * Size, Min + (c / N + 0.5) * Size));
					if (c == start)
					{
						break;
					}
				}
				path.Reverse();
				path[0] = (ax, az);
				path[path.Count - 1] = (bx, bz);
				return path;
			}

			private static double Guess(int a, int b)
			{
				double dx = (a % N - b % N) * Size;
				double dz = (a / N - b / N) * Size;
				return Math.Sqrt(dx * dx + dz * dz);
			}
		}

		/// <summary>A binary min-heap of (priority, cell); equal priorities come out lowest cell first.</summary>
		private sealed class Heap
		{
			private readonly List<(double p, int c)> items = new List<(double p, int c)>();

			internal int Count => items.Count;

			private static bool Less((double p, int c) a, (double p, int c) b)
			{
				return a.p < b.p || (a.p == b.p && a.c < b.c);
			}

			internal void Push(double p, int c)
			{
				items.Add((p, c));
				int i = items.Count - 1;
				while (i > 0)
				{
					int up = (i - 1) / 2;
					if (!Less(items[i], items[up]))
					{
						break;
					}
					(items[i], items[up]) = (items[up], items[i]);
					i = up;
				}
			}

			internal int Pop(out double p)
			{
				(double p, int c) top = items[0];
				int last = items.Count - 1;
				items[0] = items[last];
				items.RemoveAt(last);
				int i = 0;
				while (true)
				{
					int l = i * 2 + 1, r = l + 1, m = i;
					if (l < items.Count && Less(items[l], items[m]))
					{
						m = l;
					}
					if (r < items.Count && Less(items[r], items[m]))
					{
						m = r;
					}
					if (m == i)
					{
						break;
					}
					(items[i], items[m]) = (items[m], items[i]);
					i = m;
				}
				p = top.p;
				return top.c;
			}
		}
	}
}
