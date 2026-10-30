using System;
using System.Collections.Generic;

namespace HalfGripOpenWorld
{
	internal enum ParkKind
	{
		/// <summary>A whole town block between streets, given over to lawn.</summary>
		Block = 0,
		/// <summary>A river's floodplain where it runs through a town.</summary>
		River = 1,
	}

	/// <summary>
	/// The towns' parks and the walking trails round the lakes, laid out once with the roads (after them, since a park is found
	/// between them):
	///
	///   - block parks: the land between a town's streets is split into blocks (what is left once every road and its verge or
	///     sidewalk is taken out); a few closed, compact blocks of a park's size near the middle are given over to lawn, their houses
	///     not built. The big ones get a pond in their middle with a path round it; paths come in from the streets on up to four
	///     sides and meet in the middle (or on the pond's path);
	///   - a riverside park: where a river runs through a town, its floodplain inside the town is lawn, with a walk beside the water;
	///   - trails: a footpath round each lake, a little way up from its shore, broken where a river runs in or out and stopping on any
	///     road it crosses, joined to the road network;
	///   - benches along the park paths and, now and then, on the lake side of a trail.
	///
	/// A park is a raster of 8-unit cells (each wholly off the roads and out of the water); its lawn reaches from those cells out to
	/// the back of the sidewalk. Its trees stand in clumps (<see cref="Foliage"/>). A pond is a bowl dug into the ground under a flat
	/// water surface (<see cref="Ground"/>, <see cref="WaterMesh"/>).
	/// </summary>
	internal static class Parks
	{
		internal sealed class Park
		{
			internal ParkKind Kind;
			internal string Name;
			internal string Place;
			/// <summary>Its middle: the cell furthest from its edge (a block park's), or its first cell along the river.</summary>
			internal double X, Z;
			/// <summary>The raster: <see cref="W"/> by <see cref="H"/> cells of <see cref="CellSize"/> from (Ox, Oz).</summary>
			internal double Ox, Oz;
			internal int W, H;
			internal bool[] Mask;
			internal int Cells;
			/// <summary>How far its middle is from its edge.</summary>
			internal double Room;
			/// <summary>The pond: its middle, radius at the water's edge and level (from sea level); NaN level for none.</summary>
			internal double PondX, PondZ, PondR, PondLevel = double.NaN;

			internal bool HasPond => !double.IsNaN(PondLevel);

			internal bool In(double x, double z)
			{
				int i = (int)Math.Floor((x - Ox) / CellSize), j = (int)Math.Floor((z - Oz) / CellSize);
				return i >= 0 && j >= 0 && i < W && j < H && Mask[j * W + i];
			}

			/// <summary>Whether (x, z) is in a cell of the park or in one next to it.</summary>
			internal bool Near(double x, double z)
			{
				int i = (int)Math.Floor((x - Ox) / CellSize), j = (int)Math.Floor((z - Oz) / CellSize);
				for (int dj = -1; dj <= 1; dj++)
				{
					for (int di = -1; di <= 1; di++)
					{
						int a = i + di, b = j + dj;
						if (a >= 0 && b >= 0 && a < W && b < H && Mask[b * W + a])
						{
							return true;
						}
					}
				}
				return false;
			}
		}

		/// <summary>A bench at (x, z), its seat facing (dx, dz).</summary>
		internal struct Bench
		{
			internal double X, Z, Dx, Dz;
		}

		internal static readonly List<Park> All = new List<Park>();
		internal static readonly List<Bench> Benches = new List<Bench>();

		/// <summary>The paths of the parks and the lake trails, with where their benches go (see <see cref="Furnish"/>).</summary>
		private static readonly List<(Network.Road road, Park park)> walks = new List<(Network.Road, Park)>();

		/// <summary>Lake trails built, and their length, for the log.</summary>
		internal static int TrailCount;
		internal static double TrailLength;

		internal const double CellSize = 8.0;

		/// <summary>A park's cells keep this far off every road's edge (a sidewalk or verge is 4 wide).</summary>
		private const double RoadMargin = 5.0;

		/// <summary>The lawn reaches this close to a road's edge: the back of its sidewalk or the foot of its verge.</summary>
		private const double LawnMargin = 4.0;

		/// <summary>A riverside park reaches this far from the town's streets (a house plot and its garden, and some).</summary>
		private const double RiverReach = 200.0;

		/// <summary>A riverside park reaches at least this far from the river's middle (a small stream's floodplain is narrow).</summary>
		private const double RiverBank = 70.0;

		/// <summary>A block park's size, in square units (about 0.15 to 1.4 hectares at real scale), and a pond's least.</summary>
		private const double LeastBlock = 16000.0;
		private const double MostBlock = 150000.0;
		private const double LeastPond = 40000.0;

		/// <summary>
		/// Pond: radius at most, at least; how far its water is drawn past its edge, under the ground (past where the ground rises over
		/// the water, 0.4 out, by a 5-unit triangle's diagonal, so no triangle of the ground dips under the water's edge); its path round.
		/// </summary>
		private const double PondMost = 36.0;
		private const double PondLeast = 14.0;
		internal const double PondDrawn = 7.6;
		private const double PondPath = 13.0;

		private static readonly ulong Salt = Noise.Mix(WorldGen.Seed + 801);
		private static readonly ulong SaltClump = Noise.Mix(WorldGen.Seed + 802);

		// ---- layout ------------------------------------------------------------------------------------------------

		/// <summary>Finds the parks and lays out their paths and the lake trails. Called by <see cref="Network"/> after its index.</summary>
		internal static void Build()
		{
			All.Clear();
			Benches.Clear();
			walks.Clear();
			TrailCount = 0;
			TrailLength = 0.0;
			int towns = 0;
			foreach (Places.Place p in Places.All)
			{
				if (p.Kind != PlaceKind.Town)
				{
					continue;
				}
				Blocks(p, towns == 0 ? 3 : 2);
				Riverside(p);
				towns++;
			}
			foreach (Park park in All)
			{
				if (park.Kind == ParkKind.Block)
				{
					BlockPaths(park);
				}
				else
				{
					RiverWalk(park);
				}
			}
			for (int k = 0; k < Rivers.Lakes.Count; k++)
			{
				Trail(k);
			}
		}

		/// <summary>The town's blocks: connected cells off the roads and out of the water, inside 0.9 of its radius.</summary>
		private static void Blocks(Places.Place town, int count)
		{
			Raster(town, false, out double ox, out double oz, out int n, out bool[] open);
			List<(List<int> cells, bool edge)> parts = Parts(open, n);
			List<(double score, Park park)> found = new List<(double, Park)>();
			foreach ((List<int> cells, bool edge) in parts)
			{
				double area = cells.Count * CellSize * CellSize;
				if (edge || area < LeastBlock || area > MostBlock)
				{
					continue;
				}
				Park park = Make(ParkKind.Block, town, ox, oz, n, cells);
				// Compact: not a sliver between two streets.
				if (cells.Count < 0.4 * park.W * park.H)
				{
					continue;
				}
				double from = Math.Sqrt((park.X - town.X) * (park.X - town.X) + (park.Z - town.Z) * (park.Z - town.Z)) / town.Radius;
				double sx = park.X - Network.SpawnX, sz = park.Z - Network.SpawnZ;
				if (from > 0.75 || sx * sx + sz * sz < 300.0 * 300.0)
				{
					continue;
				}
				found.Add((Math.Sqrt(area) * (1.2 - from), park));
			}
			// Best first (ties by position); spread out over the towns.
			found.Sort((a, b) => a.score != b.score ? b.score.CompareTo(a.score) : a.park.X != b.park.X ? a.park.X.CompareTo(b.park.X) : a.park.Z.CompareTo(b.park.Z));
			int taken = 0;
			foreach ((double _, Park park) in found)
			{
				if (taken >= count)
				{
					break;
				}
				bool clear = true;
				foreach (Park other in All)
				{
					double dx = other.X - park.X, dz = other.Z - park.Z;
					clear &= dx * dx + dz * dz > 500.0 * 500.0;
				}
				if (!clear)
				{
					continue;
				}
				taken++;
				park.Name = town.Name + (taken == 1 ? " Park" : taken == 2 ? " Gardens" : " Green");
				Pond(park);
				All.Add(park);
			}
		}

		/// <summary>The biggest stretches of a river's floodplain in the built-up town (the two biggest: one each side of the water), as a park.</summary>
		private static void Riverside(Places.Place town)
		{
			Raster(town, true, out double ox, out double oz, out int n, out bool[] open);
			List<List<int>> parts = new List<List<int>>();
			foreach ((List<int> cells, bool _) in Parts(open, n))
			{
				if (cells.Count * CellSize * CellSize >= 10000.0)
				{
					parts.Add(cells);
				}
			}
			if (parts.Count == 0)
			{
				return;
			}
			// Biggest first; ties by place in the raster.
			parts.Sort((a, b) => a.Count != b.Count ? b.Count.CompareTo(a.Count) : a[0].CompareTo(b[0]));
			List<int> both = new List<int>(parts[0]);
			if (parts.Count > 1)
			{
				both.AddRange(parts[1]);
			}
			if (both.Count * CellSize * CellSize < 20000.0)
			{
				return;
			}
			Park park = Make(ParkKind.River, town, ox, oz, n, both);
			park.Name = town.Name + " Riverside";
			All.Add(park);
		}

		/// <summary>
		/// The cells (8 units) of a square round the town, inside 0.9 of its radius, each open if its middle is clear of every road's edge
		/// by <see cref="RoadMargin"/>, out of the water and out of the parks already found; with <paramref name="plain"/> also on a
		/// river's floodplain or bank, near the streets.
		/// </summary>
		private static void Raster(Places.Place town, bool plain, out double ox, out double oz, out int n, out bool[] open)
		{
			double reach = town.Radius * 0.9;
			n = (int)Math.Ceiling(2.0 * reach / CellSize);
			ox = town.X - reach;
			oz = town.Z - reach;
			open = new bool[n * n];
			for (int j = 0; j < n; j++)
			{
				for (int i = 0; i < n; i++)
				{
					double x = ox + (i + 0.5) * CellSize, z = oz + (j + 0.5) * CellSize;
					double dx = x - town.X, dz = z - town.Z;
					if (dx * dx + dz * dz > reach * reach)
					{
						continue;
					}
					// By the river where the town is built up: its floodplain within reach of the streets, not out in the fields.
					if (plain && (Rivers.Distance(x, z, out double width) >= Math.Max(width, RiverBank) || Network.Nearest(x, z, RiverReach, RoadKind.Trunk, RoadKind.Street) >= RiverReach))
					{
						continue;
					}
					if (Rivers.Water(x, z, out double _) || !Network.Claims(x, z, -1, 0.0, RoadMargin) || InPark(x, z))
					{
						continue;
					}
					open[j * n + i] = true;
				}
			}
		}

		/// <summary>The connected parts of the open cells (4-neighbours), in raster order; each with whether it reaches the raster's edge or a closed cell outside the town's circle.</summary>
		private static List<(List<int> cells, bool edge)> Parts(bool[] open, int n)
		{
			List<(List<int>, bool)> parts = new List<(List<int>, bool)>();
			bool[] seen = new bool[n * n];
			Queue<int> queue = new Queue<int>();
			for (int start = 0; start < n * n; start++)
			{
				if (!open[start] || seen[start])
				{
					continue;
				}
				List<int> cells = new List<int>();
				bool edge = false;
				seen[start] = true;
				queue.Enqueue(start);
				while (queue.Count > 0)
				{
					int c = queue.Dequeue();
					cells.Add(c);
					int i = c % n, j = c / n;
					for (int k = 0; k < 4; k++)
					{
						int a = i + (k == 0 ? 1 : k == 1 ? -1 : 0), b = j + (k == 2 ? 1 : k == 3 ? -1 : 0);
						if (a < 0 || b < 0 || a >= n || b >= n)
						{
							edge = true;
							continue;
						}
						int m = b * n + a;
						if (open[m] && !seen[m])
						{
							seen[m] = true;
							queue.Enqueue(m);
						}
					}
				}
				// A part running out to the town's circle is open country, not a block.
				foreach (int c in cells)
				{
					int i = c % n, j = c / n;
					double cx = (i + 0.5) - n * 0.5, cz = (j + 0.5) - n * 0.5;
					if (cx * cx + cz * cz > (n * 0.5 - 1.5) * (n * 0.5 - 1.5))
					{
						edge = true;
						break;
					}
				}
				parts.Add((cells, edge));
			}
			return parts;
		}

		/// <summary>A park from raster cells: its own raster just round them, and its middle (the cell furthest from its edge).</summary>
		private static Park Make(ParkKind kind, Places.Place town, double ox, double oz, int n, List<int> cells)
		{
			int i0 = int.MaxValue, j0 = int.MaxValue, i1 = int.MinValue, j1 = int.MinValue;
			foreach (int c in cells)
			{
				i0 = Math.Min(i0, c % n);
				i1 = Math.Max(i1, c % n);
				j0 = Math.Min(j0, c / n);
				j1 = Math.Max(j1, c / n);
			}
			Park park = new Park { Kind = kind, Place = town.Name, Ox = ox + i0 * CellSize, Oz = oz + j0 * CellSize, W = i1 - i0 + 1, H = j1 - j0 + 1, Cells = cells.Count };
			park.Mask = new bool[park.W * park.H];
			foreach (int c in cells)
			{
				park.Mask[(c / n - j0) * park.W + (c % n - i0)] = true;
			}
			// Distance in cells from the edge, by rings inwards.
			int[] depth = new int[park.W * park.H];
			Queue<int> queue = new Queue<int>();
			for (int c = 0; c < depth.Length; c++)
			{
				depth[c] = -1;
				if (park.Mask[c] && Edge(park, c))
				{
					depth[c] = 0;
					queue.Enqueue(c);
				}
			}
			int deepest = cells[0] % n - i0 + (cells[0] / n - j0) * park.W;
			while (queue.Count > 0)
			{
				int c = queue.Dequeue();
				if (depth[c] > depth[deepest])
				{
					deepest = c;
				}
				int i = c % park.W, j = c / park.W;
				for (int k = 0; k < 4; k++)
				{
					int a = i + (k == 0 ? 1 : k == 1 ? -1 : 0), b = j + (k == 2 ? 1 : k == 3 ? -1 : 0);
					if (a < 0 || b < 0 || a >= park.W || b >= park.H)
					{
						continue;
					}
					int m = b * park.W + a;
					if (park.Mask[m] && depth[m] < 0)
					{
						depth[m] = depth[c] + 1;
						queue.Enqueue(m);
					}
				}
			}
			park.X = park.Ox + (deepest % park.W + 0.5) * CellSize;
			park.Z = park.Oz + (deepest / park.W + 0.5) * CellSize;
			park.Room = Math.Max(0, depth[deepest]) * CellSize;
			return park;
		}

		private static bool Edge(Park park, int c)
		{
			int i = c % park.W, j = c / park.W;
			return i == 0 || j == 0 || i == park.W - 1 || j == park.H - 1 || !park.Mask[c - 1] || !park.Mask[c + 1] || !park.Mask[c - park.W] || !park.Mask[c + park.W];
		}

		/// <summary>
		/// A pond in the middle of a big block park, if there is room: its radius under half the way to the park's edge; its level under
		/// the lowest land round its edge, so the ground rising from the water hides the edge of the water's surface.
		/// </summary>
		private static void Pond(Park park)
		{
			if (park.Cells * CellSize * CellSize < LeastPond)
			{
				return;
			}
			double r = Math.Min(PondMost, park.Room * 0.45);
			if (r < PondLeast)
			{
				return;
			}
			double lowest = double.MaxValue;
			for (int k = 0; k < 24; k++)
			{
				double a = k * DetMath.Pi / 12.0;
				foreach (double d in new[] { r + 0.5, r + 2.0, r + 4.0, r + 6.0, r + PondDrawn })
				{
					lowest = Math.Min(lowest, WorldGen.Base(park.X + DetMath.Cos(a) * d, park.Z + DetMath.Sin(a) * d));
				}
			}
			park.PondX = park.X;
			park.PondZ = park.Z;
			park.PondR = r;
			park.PondLevel = lowest - 0.6;
		}

		// ---- paths -------------------------------------------------------------------------------------------------

		/// <summary>
		/// A block park's paths: round its pond, if it has one; and in from the streets on up to four sides (turned by the park's hash),
		/// each from the nearest street to the middle (or to the pond's path), so long as it stays in the park all the way.
		/// </summary>
		private static void BlockPaths(Park park)
		{
			ulong h = Noise.Mix(Salt ^ (ulong)(long)Math.Floor(park.X) * 0x9E3779B97F4A7C15UL ^ (ulong)(long)Math.Floor(park.Z));
			double turn = Unit(h, 0) * DetMath.Pi * 0.5;
			double inner = 0.0;
			if (park.HasPond)
			{
				inner = park.PondR + PondPath;
				List<(double x, double z)> loop = new List<(double x, double z)>();
				for (int k = 0; k <= 36; k++)
				{
					double a = turn + k * DetMath.Pi / 18.0;
					loop.Add((park.X + DetMath.Cos(a) * inner, park.Z + DetMath.Sin(a) * inner));
				}
				Network.Road round = Network.AddPath(loop, park.Name + " pond path", double.NaN, double.NaN, 0.0);
				if (round != null)
				{
					// The path's bed and its bank down to the land can dip under the land by the pond: the water stays under them too.
					double low = double.MaxValue;
					for (int i = 0; i < round.Count; i++)
					{
						low = Math.Min(low, round.Y[i] - Network.TrackDepth);
					}
					park.PondLevel = Math.Min(park.PondLevel, low - 0.6);
				}
				Walk(round, park);
			}
			// Each way in: from the middle (or the pond's path) out to the park's edge, then on to the nearest road.
			List<(double x, double z)>[] ways = new List<(double x, double z)>[4];
			for (int k = 0; k < 4; k++)
			{
				double a = turn + k * DetMath.Pi * 0.5 + (Unit(h, k + 1) - 0.5) * 0.5;
				double dx = DetMath.Cos(a), dz = DetMath.Sin(a);
				double d = inner;
				while (park.In(park.X + dx * (d + CellSize), park.Z + dz * (d + CellSize)))
				{
					d += CellSize;
				}
				if (d - inner < 30.0)
				{
					continue;
				}
				double ex = park.X + dx * d, ez = park.Z + dz * d;
				if (!Network.SnapRoad(ex + dx * 10.0, ez + dz * 10.0, 30.0, out double rx, out double rz, out double _))
				{
					continue;
				}
				ways[k] = new List<(double x, double z)> { (park.X + dx * inner, park.Z + dz * inner), (ex, ez), (rx, rz) };
			}
			if (!park.HasPond && ways[0] != null && ways[2] != null)
			{
				// Straight through the middle, one path from street to street.
				List<(double x, double z)> through = new List<(double x, double z)>();
				for (int k = ways[0].Count - 1; k >= 1; k--)
				{
					through.Add(ways[0][k]);
				}
				through.AddRange(ways[2].GetRange(1, ways[2].Count - 1));
				Road(through, park, true);
				ways[0] = ways[2] = null;
			}
			for (int k = 0; k < 4; k++)
			{
				if (ways[k] != null)
				{
					List<(double x, double z)> way = ways[k];
					// From the street in, ending on the path already there (the pond's, or the one through the middle).
					way.Reverse();
					Road(way, park, false);
				}
			}
		}

		/// <summary>A park path along <paramref name="points"/>, its ends on whatever roads or paths they meet.</summary>
		private static void Road(List<(double x, double z)> points, Park park, bool through)
		{
			(double x0, double z0) = points[0];
			(double x1, double z1) = points[points.Count - 1];
			double y0 = Network.SnapRoad(x0, z0, 3.0, out double _, out double _, out double sy0) ? sy0 : double.NaN;
			double y1 = Network.SnapRoad(x1, z1, through ? 3.0 : 6.0, out double px, out double pz, out double sy1) ? sy1 : double.NaN;
			if (!double.IsNaN(y1))
			{
				points[points.Count - 1] = (px, pz);
			}
			Walk(Network.AddPath(points, park.Name + " path", y0, y1, 30.0), park);
		}

		/// <summary>
		/// A riverside park's walk: beside the river, past where its water is drawn, on the side (and stretch) of it that runs longest
		/// through the park; on to a road at either end if one is near.
		/// </summary>
		private static void RiverWalk(Park park)
		{
			List<(double x, double z)> best = null;
			foreach (Rivers.River river in Rivers.All)
			{
				for (int side = -1; side <= 1; side += 2)
				{
					List<(double x, double z)> run = new List<(double x, double z)>();
					for (int i = 0; i <= river.Count; i++)
					{
						bool inside = false;
						double x = 0.0, z = 0.0;
						if (i < river.Count)
						{
							int a = Math.Max(0, i - 1), b = Math.Min(river.Count - 1, i + 1);
							double tx = river.X[b] - river.X[a], tz = river.Z[b] - river.Z[a], len = Math.Sqrt(tx * tx + tz * tz);
							if (len > 0.0)
							{
								double off = (Rivers.Edge(river.Half[i]) + 9.0) * side;
								x = river.X[i] - tz / len * off;
								z = river.Z[i] + tx / len * off;
								inside = park.In(x, z);
							}
						}
						if (inside)
						{
							run.Add((x, z));
						}
						else
						{
							if (run.Count > 1 && (best == null || run.Count > best.Count))
							{
								best = run;
							}
							run = new List<(double x, double z)>();
						}
					}
				}
			}
			if (best == null || best.Count < 7)
			{
				return;
			}
			park.X = best[best.Count / 2].x;
			park.Z = best[best.Count / 2].z;
			double y0 = double.NaN, y1 = double.NaN;
			(double sx, double sz) = best[0];
			if (Network.SnapRoad(sx, sz, 40.0, out double px, out double pz, out double py))
			{
				best.Insert(0, (px, pz));
				y0 = py;
			}
			(double ex, double ez) = best[best.Count - 1];
			if (Network.SnapRoad(ex, ez, 40.0, out double qx, out double qz, out double qy))
			{
				best.Add((qx, qz));
				y1 = qy;
			}
			Walk(Network.AddPath(best, park.Name + " walk", y0, y1, 100.0), park);
		}

		/// <summary>The 8 neighbours of a cell, turning anticlockwise from east.</summary>
		private static readonly int[] RoundI = { 1, 1, 0, -1, -1, -1, 0, 1 };
		private static readonly int[] RoundJ = { 0, 1, 1, 1, 0, -1, -1, -1 };

		/// <summary>On a lake's trail grid: how far round its shore the trail keeps (in cells), and the grid's cell and margin.</summary>
		private const int TrailOff = 2;
		private const double TrailCell = 20.0;
		private const double TrailMargin = 400.0;

		/// <summary>
		/// A trail round lake <paramref name="k"/>, following its shore whatever its shape: on a 20-unit grid round the lake, the lake
		/// and the low ground joined to it (under 1.5 above the water) are its shore; the cells within 2 of that are taken, and the
		/// outline of what is taken is traced (Moore's neighbour tracing), rounded off and resampled. Then it is broken into pieces
		/// where it comes near a river, the ground dips towards the water or turns too steep, stopping on any road it crosses and
		/// starting again past it. Pieces 150 or longer are built, and the longest is joined to the road network if no piece meets a road.
		/// </summary>
		private static void Trail(int k)
		{
			(double level, List<int> cells) = Rivers.Lakes[k];
			HashSet<int> lake = new HashSet<int>(cells);
			double x0 = double.MaxValue, z0 = double.MaxValue, x1 = double.MinValue, z1 = double.MinValue;
			foreach (int c in cells)
			{
				double cx = Rivers.GridMin + (c % Rivers.GridN) * Rivers.GridSize, cz = Rivers.GridMin + (c / Rivers.GridN) * Rivers.GridSize;
				x0 = Math.Min(x0, cx);
				z0 = Math.Min(z0, cz);
				x1 = Math.Max(x1, cx + Rivers.GridSize);
				z1 = Math.Max(z1, cz + Rivers.GridSize);
			}
			x0 -= TrailMargin;
			z0 -= TrailMargin;
			int nx = (int)Math.Ceiling((x1 - x0 + TrailMargin) / TrailCell), nz = (int)Math.Ceiling((z1 - z0 + TrailMargin) / TrailCell);
			// The shore: the lake's cells, then the low ground joined to them.
			int[] dist = new int[nx * nz];
			Queue<int> queue = new Queue<int>();
			for (int c = 0; c < dist.Length; c++)
			{
				dist[c] = int.MaxValue;
				double x = x0 + (c % nx + 0.5) * TrailCell, z = z0 + (c / nx + 0.5) * TrailCell;
				int i = (int)Math.Floor((x - Rivers.GridMin) / Rivers.GridSize), j = (int)Math.Floor((z - Rivers.GridMin) / Rivers.GridSize);
				if (lake.Contains(j * Rivers.GridN + i))
				{
					dist[c] = -1;
					queue.Enqueue(c);
				}
			}
			while (queue.Count > 0)
			{
				int c = queue.Dequeue();
				int i = c % nx, j = c / nx;
				for (int d = 0; d < 8; d += 2)
				{
					int a = i + RoundI[d], b = j + RoundJ[d];
					if (a < 0 || b < 0 || a >= nx || b >= nz || dist[b * nx + a] != int.MaxValue)
					{
						continue;
					}
					double x = x0 + (a + 0.5) * TrailCell, z = z0 + (b + 0.5) * TrailCell;
					if (double.IsNaN(Rivers.SurfaceAt(x, z)) && WorldGen.Base(x, z) < level + 1.5)
					{
						dist[b * nx + a] = -1;
						queue.Enqueue(b * nx + a);
					}
				}
			}
			// Then the cells within TrailOff of the shore (8-neighbour steps).
			for (int c = 0; c < dist.Length; c++)
			{
				if (dist[c] == -1)
				{
					dist[c] = 0;
					queue.Enqueue(c);
				}
			}
			while (queue.Count > 0)
			{
				int c = queue.Dequeue();
				if (dist[c] >= TrailOff)
				{
					continue;
				}
				int i = c % nx, j = c / nx;
				for (int d = 0; d < 8; d++)
				{
					int a = i + RoundI[d], b = j + RoundJ[d];
					if (a >= 0 && b >= 0 && a < nx && b < nz && dist[b * nx + a] == int.MaxValue)
					{
						dist[b * nx + a] = dist[c] + 1;
						queue.Enqueue(b * nx + a);
					}
				}
			}
			// The outline, from the first taken cell in raster order (its west neighbour is not taken).
			int start = -1;
			for (int c = 0; c < dist.Length && start < 0; c++)
			{
				if (dist[c] != int.MaxValue)
				{
					start = c;
				}
			}
			if (start < 0 || start % nx == 0)
			{
				return;
			}
			List<(double x, double z)> ring = new List<(double x, double z)>();
			int cur = start, back = 4;
			for (int steps = 0; steps < 4 * dist.Length; steps++)
			{
				int ci = cur % nx, cj = cur / nx;
				if (ci == 0 || cj == 0 || ci == nx - 1 || cj == nz - 1)
				{
					// The shore runs off the grid (low ground running far from the lake): no trail.
					return;
				}
				ring.Add((x0 + (ci + 0.5) * TrailCell, z0 + (cj + 0.5) * TrailCell));
				int found = -1, before = back;
				for (int r = 1; r <= 8; r++)
				{
					int d = (back + r) % 8;
					if (dist[(cj + RoundJ[d]) * nx + ci + RoundI[d]] != int.MaxValue)
					{
						found = d;
						break;
					}
					before = d;
				}
				if (found < 0)
				{
					return;
				}
				int ni = ci + RoundI[found], nj = cj + RoundJ[found];
				// The last cell looked at before the one found (not taken) is where the next look round starts.
				int bi = ci + RoundI[before] - ni, bj = cj + RoundJ[before] - nj;
				for (int d = 0; d < 8; d++)
				{
					if (RoundI[d] == bi && RoundJ[d] == bj)
					{
						back = d;
					}
				}
				cur = nj * nx + ni;
				if (cur == start && back == 4)
				{
					break;
				}
			}
			if (ring.Count < 12)
			{
				return;
			}
			ring.Add(ring[0]);
			// Rounded three times (the grid's steps), then 30 apart.
			for (int round = 0; round < 3; round++)
			{
				List<(double x, double z)> next = new List<(double x, double z)> { ring[0] };
				for (int i = 0; i + 1 < ring.Count; i++)
				{
					(double ax, double az) = ring[i];
					(double bx, double bz) = ring[i + 1];
					next.Add((ax * 0.75 + bx * 0.25, az * 0.75 + bz * 0.25));
					next.Add((ax * 0.25 + bx * 0.75, az * 0.25 + bz * 0.75));
				}
				next.Add(ring[ring.Count - 1]);
				ring = next;
			}
			ring = Lines.Resample(ring, 30.0);

			string name = "Lake trail " + (k + 1);
			List<Network.Road> built = new List<Network.Road>();
			bool meets = false;
			List<(double x, double z)> piece = new List<(double x, double z)>();
			double startY = double.NaN;
			for (int i = 0; i < ring.Count; i++)
			{
				(double x, double z) = ring[i];
				if (!Good(x, z, level, piece.Count > 0 ? piece[piece.Count - 1] : ((double, double)?)null))
				{
					Flush(piece, startY, double.NaN, name, built, ref meets);
					piece = new List<(double x, double z)>();
					startY = double.NaN;
					continue;
				}
				if (piece.Count > 0)
				{
					(double px, double pz) = piece[piece.Count - 1];
					if (Network.CrossRoad(px, pz, x, z, out double ix, out double iz, out double iy))
					{
						piece.Add((ix, iz));
						Flush(piece, startY, iy, name, built, ref meets);
						piece = new List<(double x, double z)> { (ix, iz) };
						startY = iy;
						meets = true;
					}
				}
				piece.Add((x, z));
			}
			Flush(piece, startY, double.NaN, name, built, ref meets);
			if (built.Count == 0)
			{
				return;
			}
			TrailCount++;
			if (!meets)
			{
				Network.Road longest = built[0];
				foreach (Network.Road r in built)
				{
					longest = r.S[r.Count - 1] > longest.S[longest.Count - 1] ? r : longest;
				}
				int mid = longest.Count / 2;
				Network.PathToNetwork(name + " way", longest.X[mid], longest.Z[mid]);
			}
		}

		/// <summary>Whether a trail may pass (x, z): away from rivers, well above the lake, out of the towns' parks, and not too steep from the last point.</summary>
		private static bool Good(double x, double z, double level, (double x, double z)? last)
		{
			if (!WorldGen.Inside(x, z) || Rivers.Channel(x, z, 14.0, out double _) || !double.IsNaN(Rivers.SurfaceAt(x, z)))
			{
				return false;
			}
			double h = WorldGen.Base(x, z);
			if (h < level + 1.0)
			{
				return false;
			}
			double water = Rivers.DrawnWater(x, z);
			if (!double.IsNaN(water) && water > h - 0.8)
			{
				return false;
			}
			if (last != null)
			{
				(double lx, double lz) = last.Value;
				double run = Math.Sqrt((x - lx) * (x - lx) + (z - lz) * (z - lz));
				if (run > 0.0 && Math.Abs(h - WorldGen.Base(lx, lz)) / run > 0.18 * 1.3)
				{
					return false;
				}
			}
			return true;
		}

		private static void Flush(List<(double x, double z)> piece, double startY, double endY, string name, List<Network.Road> built, ref bool meets)
		{
			if (piece.Count < 2)
			{
				return;
			}
			Network.Road road = Network.AddPath(piece, name, startY, endY, 150.0);
			if (road == null)
			{
				return;
			}
			built.Add(road);
			meets |= !double.IsNaN(startY) || !double.IsNaN(endY);
			TrailLength += road.S[road.Count - 1];
			walks.Add((road, null));
		}

		private static void Walk(Network.Road road, Park park)
		{
			if (road != null)
			{
				walks.Add((road, park));
			}
		}

		// ---- benches -----------------------------------------------------------------------------------------------

		/// <summary>
		/// The benches, once the lots are laid out too: beside the park paths every 45 or so, sides taking turns, facing the path; on the
		/// lake side of a trail (its lower side) every 160 or so, facing the water. Each on ground near level, clear of roads, water, lots and each other.
		/// </summary>
		internal static void Furnish()
		{
			Benches.Clear();
			foreach ((Network.Road road, Park park) in walks)
			{
				double length = road.S[road.Count - 1];
				double spacing = park != null ? 45.0 : 160.0;
				int n = 0;
				for (double s = 22.0; s < length - 22.0; s += spacing, n++)
				{
					Network.Locate(road, s, out int i, out double t);
					double x = road.X[i] + (road.X[i + 1] - road.X[i]) * t, z = road.Z[i] + (road.Z[i + 1] - road.Z[i]) * t;
					Network.Side(road, i, out double sx, out double sz);
					int side;
					if (park != null)
					{
						side = n % 2 == 0 ? 1 : -1;
					}
					else
					{
						// On the lake's side (the lower ground), facing the water beyond the path.
						side = WorldGen.Base(x + sx * 40.0, z + sz * 40.0) < WorldGen.Base(x - sx * 40.0, z - sz * 40.0) ? 1 : -1;
					}
					double off = road.Half + 5.0;
					double bx = x + sx * side * off, bz = z + sz * side * off;
					double fx = -sx * side, fz = -sz * side;
					if (park == null)
					{
						fx = -fx;
						fz = -fz;
					}
					if (Fits(bx, bz, park))
					{
						Benches.Add(new Bench { X = bx, Z = bz, Dx = fx, Dz = fz });
					}
				}
			}
		}

		private static bool Fits(double x, double z, Park park)
		{
			if ((park != null && !park.In(x, z)) || !Network.Claims(x, z, -1, 0.0, 1.5) || Town.Taken(x, z, 2.0) || Wet(x, z, 2.0))
			{
				return false;
			}
			double lo = double.MaxValue, hi = double.MinValue;
			foreach ((double u, double v) in new[] { (-3.5, -1.5), (3.5, -1.5), (-3.5, 1.5), (3.5, 1.5) })
			{
				double g = Town.Ground(x + u, z + v, Network.Ground(x + u, z + v, WorldGen.Base(x + u, z + v)));
				lo = Math.Min(lo, g);
				hi = Math.Max(hi, g);
			}
			if (hi - lo > 1.2)
			{
				return false;
			}
			foreach (Bench b in Benches)
			{
				if ((b.X - x) * (b.X - x) + (b.Z - z) * (b.Z - z) < 12.0 * 12.0)
				{
					return false;
				}
			}
			return true;
		}

		private static bool Wet(double x, double z, double margin)
		{
			if (Rivers.Water(x, z, out double _))
			{
				return true;
			}
			double w = Rivers.DrawnWater(x, z);
			return !double.IsNaN(w) && w + margin > WorldGen.Base(x, z);
		}

		// ---- queries -----------------------------------------------------------------------------------------------

		/// <summary>Whether (x, z) is in or next to a cell of a park already found.</summary>
		private static bool InPark(double x, double z)
		{
			foreach (Park park in All)
			{
				if (park.Near(x, z))
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>
		/// Whether (x, z) is on a park's lawn: in one of its cells, or by a road in a cell next to one and off the roads but for their
		/// sidewalk or verge, so the lawn runs right up to the back of the sidewalk and not in 8-unit steps.
		/// </summary>
		internal static bool At(double x, double z)
		{
			foreach (Park park in All)
			{
				if (x < park.Ox - CellSize || z < park.Oz - CellSize || x > park.Ox + (park.W + 1) * CellSize || z > park.Oz + (park.H + 1) * CellSize)
				{
					continue;
				}
				if (park.In(x, z))
				{
					return true;
				}
				if (park.Near(x, z) && Network.Nearest(x, z, 40.0, RoadKind.Trunk) < 40.0 && Network.Claims(x, z, -1, 0.0, LawnMargin) && !Rivers.Water(x, z, out double _))
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>The water level at (x, z) if it is in a pond (within <paramref name="margin"/> past its edge).</summary>
		internal static bool Pond(double x, double z, double margin, out double level)
		{
			level = 0.0;
			foreach (Park park in All)
			{
				if (!park.HasPond)
				{
					continue;
				}
				double dx = x - park.PondX, dz = z - park.PondZ, r = park.PondR + margin;
				if (dx * dx + dz * dz < r * r)
				{
					level = park.PondLevel;
					return true;
				}
			}
			return false;
		}

		/// <summary>The highest pond surface drawn over (x, z) (it is drawn a little past the water's edge, under the ground), or NaN.</summary>
		internal static double DrawnPond(double x, double z)
		{
			return Pond(x, z, PondDrawn, out double level) ? level : double.NaN;
		}

		/// <summary>
		/// The ground given the ground from the roads and lots (<paramref name="ground"/>, from sea level), with the ponds dug in: a bowl
		/// 1.8 deep under the water, its edge just under the water's level, rising 1 in 2 past it until it meets the ground.
		/// </summary>
		internal static double Ground(double x, double z, double ground)
		{
			foreach (Park park in All)
			{
				if (!park.HasPond)
				{
					continue;
				}
				double dx = x - park.PondX, dz = z - park.PondZ;
				double d2 = dx * dx + dz * dz, r = park.PondR;
				if (d2 > (r + 12.0) * (r + 12.0))
				{
					continue;
				}
				double d = Math.Sqrt(d2);
				double target = d < r ? park.PondLevel - 0.2 - 1.6 * (1.0 - d2 / (r * r)) : park.PondLevel - 0.2 + (d - r) * 0.5;
				ground = Math.Min(ground, target);
			}
			return ground;
		}

		/// <summary>The chance of a tree in a pixel of park lawn: in clumps here and there, a lone one now and then; none by a pond.</summary>
		internal static double TreeChance(double x, double z)
		{
			if (Pond(x, z, 8.0, out double _))
			{
				return 0.0;
			}
			return Noise.Gradient(SaltClump, x, z, 70.0) > 0.3 ? 0.14 : 0.004;
		}

		private static double Unit(ulong h, int k)
		{
			return (Noise.Mix(h + (ulong)k) >> 11) * (1.0 / 9007199254740992.0);
		}
	}
}
