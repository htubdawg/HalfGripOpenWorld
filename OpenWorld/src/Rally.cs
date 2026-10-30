using System;
using System.Collections.Generic;

namespace HalfGripOpenWorld
{
	/// <summary>
	/// The rally stage: a closed loop of gravel road, about 5 km real, through the upland woods, laid out once from the world's seed after
	/// the towns and villages and before the farms (which keep out of it) and every road (which go round it). Its own track to the
	/// network leaves from its service area.
	///
	///   1. **Site.** On a 400-unit grid, the centres with a disc round them big enough for the loop that holds no water, river or
	///      floodplain, no mountains, no town, village or circuit, and is not near the edge wall or the coast; of those, the one whose land
	///      rolls most like a forest stage (a relief nearest <see cref="Relief"/>). A loop can cross no river (it has no bridges), so
	///      none may run through it.
	///   2. **Loop.** A straight (the start and finish, with the service area beside it) on a ring round the site's middle, then waypoints
	///      round the ring, some pulled in towards the middle, routed in turn by A* on a 50-unit grid, a step costing its length times
	///      (1 + steepness (grade / soft)²) and a slow noise (so the road winds between the cheap patches as a forest road does), steps
	///      steeper than <see cref="SteepestStep"/> closed, and every cell near a leg already routed closed to the legs after it, so the
	///      loop never comes near itself. The cells are thinned and rounded (Chaikin) into a curve. Loops that come out too short or long,
	///      turn tighter than <see cref="MinRadius"/> (once the kinks are eased) or come near themselves are thrown away; of up to 30 that
	///      pass, the one with the most corners and a length nearest 5 km is kept. The loop runs anticlockwise, so its right is the outside.
	///   3. **Road.** <see cref="Network"/> makes it a <see cref="RoadKind.Rally"/> road before any other: held to 12%, a ribbon on a bed
	///      pressed into the ground like a farm track's, with dirt grip, and its cells closed to the router (<see cref="Fenced"/>).
	///
	/// Built with <see cref="Rng"/>, <see cref="DetMath"/> and plain arithmetic, ties broken by index, so it is the same on every machine;
	/// read-only once built, so tiles can read it from any thread.
	/// </summary>
	internal static class Rally
	{
		/// <summary>Half the stage's width: 16 across, about 5 m, a forest road's.</summary>
		internal const double Half = 8.0;

		/// <summary>The straight the loop starts and ends on: the finish line near its start, the start line near its end, the service area between.</summary>
		internal const double StraightLength = 400.0;
		internal const double FinishS = 56.0;
		internal const double StartS = StraightLength - 56.0;
		internal const double ServiceS = StraightLength * 0.5;

		/// <summary>The service area: a gravel yard beside the straight, outside the loop (a <see cref="LotKind.Service"/> lot).</summary>
		internal const double ServiceW = 150.0;
		internal const double ServiceD = 90.0;

		/// <summary>Distance between the loop's samples (as the roads').</summary>
		internal const double Step = 8.0;

		/// <summary>About 5 km real; loops between the two limits are kept.</summary>
		private const double TargetLength = 16500.0;
		private const double MinLength = 13000.0;
		private const double MaxLength = 21000.0;

		/// <summary>The ring the waypoints are on, and how far round it the site must be clear.</summary>
		private const double Ring = 2300.0;
		private const double SiteClear = Ring + 500.0;

		/// <summary>How much the site's land should rise and fall (its standard deviation): a stage over rolling ground, not a plain or a mountain.</summary>
		private const double Relief = 30.0;

		/// <summary>The routing grid, and the steepest step it takes (the road is held to 12%, so a steeper step is cut down).</summary>
		private const double Cell = 50.0;
		private const double SteepestStep = 0.24;
		private const double Soft = 0.10;
		private const double Steep = 3.0;

		/// <summary>How strongly the slow noise sends the road winding.</summary>
		private const double Wind = 2.5;

		/// <summary>Each leg keeps this far from the legs routed before it.</summary>
		private const double Apart = 150.0;

		/// <summary>The tightest corner a loop may have (a hairpin a rally car takes in first gear), on the curve through samples 3 either side.</summary>
		private const double MinRadius = 24.0;

		/// <summary>Two stretches more than <see cref="SeparationAlong"/> apart along the loop keep this far apart: both roads, their beds and banks.</summary>
		private const double Separation = 100.0;
		private const double SeparationAlong = 500.0;

		/// <summary>Nearer along than that (a hairpin's two legs, past its turn), they keep this far apart: both stages and their soft edges, and a gap.</summary>
		private const double HairpinApart = 40.0;

		/// <summary>Kept clear of towns' and villages' edges.</summary>
		private const double PlaceClearance = 1000.0;

		/// <summary>How far past the stage's edge the forest is kept round it (and in the loop).</summary>
		private const double WoodsReach = 400.0;

		internal static bool Exists;
		/// <summary>The loop's samples, anticlockwise from the start of the straight; the last joins back to the first.</summary>
		internal static double[] X;
		internal static double[] Z;
		/// <summary>Distance along the loop at each sample.</summary>
		internal static double[] S;
		internal static int Count;
		internal static double Length;
		/// <summary>The site's middle.</summary>
		internal static double Cx;
		internal static double Cz;

		/// <summary>The straight's direction, its middle (beside which the service area is) and the way out of the loop from it.</summary>
		internal static double DirX;
		internal static double DirZ;
		internal static double MidX;
		internal static double MidZ;
		internal static double OutX;
		internal static double OutZ;

		/// <summary>Where the access track leaves the service area's back, and a point out beyond it the track is routed from.</summary>
		internal static double AccessX;
		internal static double AccessZ;
		internal static double AccessOutX;
		internal static double AccessOutZ;

		/// <summary>The stage's road, once <see cref="Network"/> has made it.</summary>
		internal static Network.Road Road;

		/// <summary>A row of straw bales on the outside of a tight corner: from <c>S0</c> to <c>S1</c> along the loop, on side <c>Side</c> (+1 left, -1 right).</summary>
		internal struct Bales
		{
			public double S0;
			public double S1;
			public int Side;
			public double Radius;
		}

		internal static readonly List<Bales> Corners = new List<Bales>();

		/// <summary>The bales' size: a big square bale, 2.4 by 1.2 by 1.2 m.</summary>
		internal const double BaleLength = 8.0;
		internal const double BaleDepth = 4.0;
		internal const float BaleHeight = 4f;

		/// <summary>How far out from the stage's centre line the bales stand (their inner face), past its soft edge.</summary>
		internal const double BaleOut = Half + 3.0;

		/// <summary>For the log and the preview.</summary>
		internal static double TightestRadius;
		internal static int CornerCount;
		internal static int Attempts;
		internal static string Report = "";

		private static readonly ulong Salt = Noise.Mix(WorldGen.Seed + 0x7A11UL);

		// ---- building --------------------------------------------------------------------------------------------

		/// <summary>
		/// Lays the loop out. Called by <see cref="Places.Build"/> once the towns and villages (and the circuit) are placed and before the
		/// farms, so the farms keep out of it; the rivers are built. If no site or loop is found, there is no stage and nothing else changes.
		/// </summary>
		internal static void Build()
		{
			Exists = false;
			Corners.Clear();
			Road = null;
			List<(double x, double z, double score)> sites = Sites();
			for (int k = 0; k < sites.Count && k < 4; k++)
			{
				(double sx, double sz, double _) = sites[k];
				Grid grid = new Grid(sx, sz);
				int best = -1, fitted = 0;
				double bestScore = double.MinValue;
				for (int attempt = 0; attempt < 240 && fitted < 30; attempt++)
				{
					Attempts++;
					if (Try(new Rng(Noise.Mix(Salt + (ulong)(k * 1000 + attempt))), grid, out double score))
					{
						fitted++;
						if (score > bestScore)
						{
							bestScore = score;
							best = attempt;
						}
					}
				}
				if (best >= 0)
				{
					Try(new Rng(Noise.Mix(Salt + (ulong)(k * 1000 + best))), grid, out double _);
					Cx = sx;
					Cz = sz;
					Index();
					Place();
					Exists = true;
					Report = "rally stage at (" + sx.ToString("0") + ", " + sz.ToString("0") + "), site " + (k + 1) + " of " + sites.Count + ", best of " + fitted + " loops that fit (" + Attempts + " drawn), " + CornerCount + " corners, " + Corners.Count + " with bales";
					return;
				}
			}
			Report = "no rally stage: " + sites.Count + " sites, no loop fitted";
		}

		// ---- the site --------------------------------------------------------------------------------------------

		/// <summary>The sites the loop fits on, best first (land relief nearest <see cref="Relief"/>), each at least the ring's width from every better one.</summary>
		private static List<(double x, double z, double score)> Sites()
		{
			List<(double x, double z, double score)> found = new List<(double, double, double)>();
			double wall = WorldGen.Half - WorldGen.WallDepth - 500.0;
			for (double cz = -WorldGen.Half + SiteClear; cz <= WorldGen.Half - SiteClear; cz += 400.0)
			{
				for (double cx = -WorldGen.Half + SiteClear; cx <= WorldGen.Half - SiteClear; cx += 400.0)
				{
					bool ok = true;
					foreach (Places.Place p in Places.All)
					{
						double reach = SiteClear + p.Radius + PlaceClearance;
						if ((p.X - cx) * (p.X - cx) + (p.Z - cz) * (p.Z - cz) < reach * reach)
						{
							ok = false;
							break;
						}
					}
					double n = 0.0, sum = 0.0, sq = 0.0;
					for (double v = -SiteClear; v <= SiteClear && ok; v += 200.0)
					{
						for (double u = -SiteClear; u <= SiteClear; u += 200.0)
						{
							if (u * u + v * v > SiteClear * SiteClear)
							{
								continue;
							}
							double x = cx + u, z = cz + v;
							if (!Open(x, z, wall))
							{
								ok = false;
								break;
							}
							double h = WorldGen.Land(x, z);
							n += 1.0;
							sum += h;
							sq += h * h;
						}
					}
					if (!ok || n == 0.0)
					{
						continue;
					}
					double mean = sum / n;
					double sd = Math.Sqrt(Math.Max(0.0, sq / n - mean * mean));
					found.Add((cx, cz, -Math.Abs(sd - Relief)));
				}
			}
			found.Sort((p, q) => p.score != q.score ? q.score.CompareTo(p.score) : (p.x != q.x ? p.x.CompareTo(q.x) : p.z.CompareTo(q.z)));
			List<(double x, double z, double score)> kept = new List<(double, double, double)>();
			foreach (var s in found)
			{
				bool apart = true;
				foreach (var k in kept)
				{
					if ((k.x - s.x) * (k.x - s.x) + (k.z - s.z) * (k.z - s.z) < 4.0 * Ring * Ring)
					{
						apart = false;
						break;
					}
				}
				if (apart)
				{
					kept.Add(s);
				}
			}
			return kept;
		}

		/// <summary>Whether the stage may run at (x, z): inside the walls, away from the coast, out of the water and floodplains, the mountains and the circuit.</summary>
		private static bool Open(double x, double z, double wall)
		{
			if (Math.Abs(x) > wall || z > wall || WorldGen.Inland(x, z) < 2000.0 || WorldGen.MountainMask(x, z) > 0.6)
			{
				return false;
			}
			if (!double.IsNaN(Rivers.SurfaceAt(x, z)) || Rivers.Distance(x, z, out double plain) < plain + 60.0 || WorldGen.Land(x, z) > 600.0)
			{
				return false;
			}
			return Circuit.Clear(x, z, 300.0);
		}

		// ---- the loop --------------------------------------------------------------------------------------------

		/// <summary>The routing grid round a site: its land and slow noise, by cell.</summary>
		private sealed class Grid
		{
			internal readonly int N;
			internal readonly double X0;
			internal readonly double Z0;
			internal readonly double[] H;
			internal readonly bool[] Closed;
			internal readonly double Cx;
			internal readonly double Cz;

			internal Grid(double cx, double cz)
			{
				Cx = cx;
				Cz = cz;
				N = (int)Math.Ceiling(2.0 * SiteClear / Cell);
				X0 = cx - N * Cell * 0.5;
				Z0 = cz - N * Cell * 0.5;
				H = new double[N * N];
				Closed = new bool[N * N];
				double reach = SiteClear - 100.0;
				for (int c = 0; c < N * N; c++)
				{
					double x = X(c), z = Z(c);
					H[c] = WorldGen.Land(x, z);
					Closed[c] = (x - cx) * (x - cx) + (z - cz) * (z - cz) > reach * reach;
				}
			}

			internal double X(int c)
			{
				return X0 + (c % N + 0.5) * Cell;
			}

			internal double Z(int c)
			{
				return Z0 + (c / N + 0.5) * Cell;
			}

			internal int Index(double x, double z)
			{
				int i = (int)Math.Floor((x - X0) / Cell), j = (int)Math.Floor((z - Z0) / Cell);
				return i < 0 || j < 0 || i >= N || j >= N ? -1 : j * N + i;
			}
		}

		/// <summary>
		/// Draws a loop and, if it passes, keeps it, with <paramref name="score"/> its character (more corners, a length nearer the target).
		/// </summary>
		private static bool Try(Rng rng, Grid grid, out double score)
		{
			score = 0.0;
			int n = grid.N;
			// The straight on the ring, heading anticlockwise round it; the legs leave from a point a little past its end and come back to
			// one a little before its start, so the loop runs on and off it in line.
			// The service area beside it needs about flat ground (its yard is level across and follows the straight's grade): of
			// sixteen places round the ring from a drawn one, the first that has it.
			double a0 = rng.Next() * 2.0 * DetMath.Pi;
			double tx = 0.0, tz = 0.0, px = 0.0, pz = 0.0;
			bool flat = false;
			for (int m = 0; m < 16 && !flat; m++)
			{
				double a = a0 + m * DetMath.Pi / 8.0;
				tx = -DetMath.Sin(a);
				tz = DetMath.Cos(a);
				px = grid.Cx + DetMath.Cos(a) * Ring * 0.95;
				pz = grid.Cz + DetMath.Sin(a) * Ring * 0.95;
				flat = Flat(px, pz, tx, tz);
				a0 = flat ? a : a0;
			}
			if (!flat)
			{
				return false;
			}
			const double lead = 100.0;
			double inX = px - tx * StraightLength * 0.5, inZ = pz - tz * StraightLength * 0.5;
			double outX = px + tx * StraightLength * 0.5, outZ = pz + tz * StraightLength * 0.5;
			double ax = inX - tx * lead, az = inZ - tz * lead, bx = outX + tx * lead, bz = outZ + tz * lead;
			int start = grid.Index(bx, bz), goal = grid.Index(ax, az);
			if (start < 0 || goal < 0 || grid.Closed[start] || grid.Closed[goal])
			{
				return false;
			}
			bool[] blocked = new bool[n * n];
			// Beside the straight and its leads, closed to every leg.
			for (int c = 0; c < n * n; c++)
			{
				double dx = grid.X(c) - ax, dz = grid.Z(c) - az;
				double along = dx * tx + dz * tz, across = Math.Abs(-dx * tz + dz * tx);
				if (along >= 0.0 && along <= StraightLength + 2.0 * lead && across < Apart && c != start && c != goal)
				{
					blocked[c] = true;
				}
			}
			// The waypoints round the ring, a third of them pulled in towards the middle (so the loop turns both ways).
			int k = 14 + (int)(rng.Next() * 5.0);
			List<int> targets = new List<int>();
			for (int i = 1; i < k; i++)
			{
				double a = a0 + 2.0 * DetMath.Pi * (i + 0.4 * (rng.Next() - 0.5)) / k;
				double f = rng.Next() < 0.4 ? rng.Range(0.35, 0.6) : rng.Range(0.75, 1.0);
				int c = grid.Index(grid.Cx + DetMath.Cos(a) * Ring * f, grid.Cz + DetMath.Sin(a) * Ring * f);
				if (c < 0 || grid.Closed[c] || blocked[c])
				{
					return false;
				}
				targets.Add(c);
			}
			targets.Add(goal);
			double[] wind = new double[n * n];
			ulong salt = Noise.Mix(Salt + 77UL + (ulong)(rng.Next() * 1e9));
			for (int c = 0; c < n * n; c++)
			{
				wind[c] = 0.5 + 0.5 * Noise.Fbm(salt, grid.X(c), grid.Z(c), 260.0, 2, 0.5);
			}
			List<int> cells = new List<int> { start };
			List<int> tail = null;
			int from = start;
			foreach (int to in targets)
			{
				List<int> leg = Find(grid, blocked, wind, from, to);
				if (leg == null)
				{
					return false;
				}
				// Close the cells near this leg to the legs after it, all but its last few (the next leg leaves from there); the last
				// leg's tail is closed now, the next leg being routed.
				int keep = Math.Max(0, leg.Count - 6);
				Block(grid, blocked, leg, 0, keep);
				if (tail != null)
				{
					Block(grid, blocked, tail, 0, tail.Count);
				}
				tail = leg.GetRange(keep, leg.Count - keep);
				for (int m = 1; m < leg.Count; m++)
				{
					cells.Add(leg[m]);
				}
				from = to;
			}
			// The line: the straight's end, its lead, the cells (every other one), the lead in, the straight's start; rounded, then the
			// straight itself in front of it.
			List<(double x, double z)> pts = new List<(double x, double z)> { (outX, outZ), (bx, bz) };
			for (int m = 2; m + 2 < cells.Count; m += 2)
			{
				pts.Add((grid.X(cells[m]), grid.Z(cells[m])));
			}
			pts.Add((ax, az));
			pts.Add((inX, inZ));
			for (int r = 0; r < 3; r++)
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
			List<(double x, double z)> loop = new List<(double x, double z)>();
			int straight = (int)Math.Round(StraightLength / Step);
			for (int i = 0; i < straight; i++)
			{
				loop.Add((inX + tx * StraightLength * i / straight, inZ + tz * StraightLength * i / straight));
			}
			List<(double x, double z)> rest = Lines.Resample(pts, Step);
			// The resampled line ends on the straight's start, where the loop began.
			for (int i = 0; i + 1 < rest.Count; i++)
			{
				loop.Add(rest[i]);
			}
			Ease(loop, straight + 4);
			if (!Keep(loop, tx, tz, px, pz))
			{
				return false;
			}
			score = CornerCount + 20.0 * (1.0 - Math.Abs(Length - TargetLength) / TargetLength);
			return true;
		}

		/// <summary>
		/// Eases the corners tighter than <see cref="MinRadius"/> (where two routed steps turned sharply): each such sample, and its
		/// neighbours, moved part way to the middle of the samples either side, a few times over, so a kink opens into a curve and the
		/// rest of the loop stays where it was routed. The first <paramref name="fixedCount"/> samples (the straight and the start of the
		/// lead off it) and the last few are left.
		/// </summary>
		private static void Ease(List<(double x, double z)> loop, int fixedCount)
		{
			int n = loop.Count;
			double[] x = new double[n], z = new double[n];
			for (int i = 0; i < n; i++)
			{
				x[i] = loop[i].x;
				z[i] = loop[i].z;
			}
			for (int pass = 0; pass < 60; pass++)
			{
				double[] curve = Curvature(x, z);
				bool any = false;
				for (int i = fixedCount; i < n - 4; i++)
				{
					if (Math.Abs(curve[i]) * MinRadius * 1.15 < 1.0)
					{
						continue;
					}
					any = true;
					for (int k = Math.Max(fixedCount, i - 2); k <= Math.Min(n - 5, i + 2); k++)
					{
						x[k] = x[k] * 0.5 + (x[k - 1] + x[k + 1]) * 0.25;
						z[k] = z[k] * 0.5 + (z[k - 1] + z[k + 1]) * 0.25;
					}
				}
				if (!any)
				{
					break;
				}
			}
			for (int i = 0; i < n; i++)
			{
				loop[i] = (x[i], z[i]);
			}
		}

		/// <summary>
		/// Whether the ground beside the straight (middle (px, pz), along (tx, tz)), where the service area goes, lies within 5 of the
		/// straight grade the stage is held to along it (between the land at the straight's ends, under 7%).
		/// </summary>
		private static bool Flat(double px, double pz, double tx, double tz)
		{
			double ox = tz, oz = -tx;
			double h0 = WorldGen.Land(px - tx * StraightLength * 0.5, pz - tz * StraightLength * 0.5), h1 = WorldGen.Land(px + tx * StraightLength * 0.5, pz + tz * StraightLength * 0.5);
			if (Math.Abs(h1 - h0) / StraightLength > 0.07)
			{
				return false;
			}
			for (double v = 0.0; v <= ServiceD + 40.0; v += 20.0)
			{
				for (double u = -ServiceW * 0.5 - 10.0; u <= ServiceW * 0.5 + 10.0; u += 25.0)
				{
					double line = (h0 + h1) * 0.5 + (h1 - h0) * u / StraightLength;
					double x = px + tx * u + ox * (Half + 4.0 + v), z = pz + tz * u + oz * (Half + 4.0 + v);
					if (Math.Abs(WorldGen.Land(x, z) - line) > 5.0)
					{
						return false;
					}
				}
			}
			return true;
		}

		/// <summary>Closes the cells within <see cref="Apart"/> of cells a (inclusive) to b (exclusive) of a leg.</summary>
		private static void Block(Grid grid, bool[] blocked, List<int> leg, int a, int b)
		{
			int n = grid.N;
			int reach = (int)Math.Ceiling(Apart / Cell);
			for (int m = a; m < b; m++)
			{
				int ci = leg[m] % n, cj = leg[m] / n;
				for (int dj = -reach; dj <= reach; dj++)
				{
					for (int di = -reach; di <= reach; di++)
					{
						int i = ci + di, j = cj + dj;
						if (i < 0 || j < 0 || i >= n || j >= n || (di * di + dj * dj) * Cell * Cell > Apart * Apart)
						{
							continue;
						}
						blocked[j * n + i] = true;
					}
				}
			}
		}

		/// <summary>A* from cell <paramref name="a"/> to <paramref name="b"/> over the open cells; null if there is no way. Ties broken by cell number.</summary>
		private static List<int> Find(Grid grid, bool[] blocked, double[] wind, int a, int b)
		{
			int n = grid.N;
			double[] g = new double[n * n];
			int[] from = new int[n * n];
			for (int c = 0; c < g.Length; c++)
			{
				g[c] = double.MaxValue;
				from[c] = -1;
			}
			Heap open = new Heap();
			g[a] = 0.0;
			open.Push(Guess(grid, a, b), a);
			bool reached = false;
			while (open.Count > 0)
			{
				int cur = open.Pop(out double f);
				if (f > g[cur] + Guess(grid, cur, b) + 1e-9)
				{
					continue;
				}
				if (cur == b)
				{
					reached = true;
					break;
				}
				int ci = cur % n, cj = cur / n;
				for (int dj = -1; dj <= 1; dj++)
				{
					for (int di = -1; di <= 1; di++)
					{
						if (di == 0 && dj == 0)
						{
							continue;
						}
						int ni = ci + di, nj = cj + dj;
						if (ni < 0 || nj < 0 || ni >= n || nj >= n)
						{
							continue;
						}
						int next = nj * n + ni;
						if ((grid.Closed[next] || blocked[next]) && next != b)
						{
							continue;
						}
						double len = di != 0 && dj != 0 ? Cell * 1.4142135623730951 : Cell;
						double grade = Math.Abs(grid.H[next] - grid.H[cur]) / len;
						if (grade > SteepestStep)
						{
							continue;
						}
						double r = grade / Soft;
						double total = g[cur] + len * (1.0 + Steep * r * r) * (1.0 + Wind * wind[next]);
						if (total < g[next])
						{
							g[next] = total;
							from[next] = cur;
							open.Push(total + Guess(grid, next, b), next);
						}
					}
				}
			}
			if (!reached)
			{
				return null;
			}
			List<int> path = new List<int>();
			for (int c = b; c >= 0; c = from[c])
			{
				path.Add(c);
				if (c == a)
				{
					break;
				}
			}
			path.Reverse();
			return path;
		}

		private static double Guess(Grid grid, int a, int b)
		{
			int n = grid.N;
			double dx = (a % n - b % n) * Cell, dz = (a / n - b / n) * Cell;
			return Math.Sqrt(dx * dx + dz * dz);
		}

		/// <summary>
		/// Checks a drawn loop (its length, corners, how near it comes to itself, that it runs anticlockwise) and keeps it if it passes,
		/// with its straight along (tx, tz) from sample 0, its middle at (px, pz).
		/// </summary>
		private static bool Keep(List<(double x, double z)> loop, double tx, double tz, double px, double pz)
		{
			int n = loop.Count;
			double[] x = new double[n], z = new double[n], s = new double[n];
			double area = 0.0;
			for (int i = 0; i < n; i++)
			{
				x[i] = loop[i].x;
				z[i] = loop[i].z;
				if (i > 0)
				{
					s[i] = s[i - 1] + Math.Sqrt((x[i] - x[i - 1]) * (x[i] - x[i - 1]) + (z[i] - z[i - 1]) * (z[i] - z[i - 1]));
				}
				int q = (i + 1) % n;
				area += loop[i].x * loop[q].z - loop[q].x * loop[i].z;
			}
			double length = s[n - 1] + Math.Sqrt((x[0] - x[n - 1]) * (x[0] - x[n - 1]) + (z[0] - z[n - 1]) * (z[0] - z[n - 1]));
			if (area <= 0.0 || length < MinLength || length > MaxLength)
			{
				return false;
			}
			double[] curve = Curvature(x, z);
			double tightest = double.MaxValue;
			for (int i = 0; i < n; i++)
			{
				tightest = Math.Min(tightest, 1.0 / Math.Max(1e-9, Math.Abs(curve[i])));
			}
			if (tightest < MinRadius)
			{
				return false;
			}
			for (int i = 0; i < n; i += 2)
			{
				for (int j = i + 2; j < n; j += 2)
				{
					double along = Math.Min(s[j] - s[i], length - (s[j] - s[i]));
					double need = along >= SeparationAlong ? Separation : along >= 100.0 ? HairpinApart : 0.0;
					double dx = x[j] - x[i], dz = z[j] - z[i];
					if (dx * dx + dz * dz < need * need)
					{
						return false;
					}
				}
			}
			// The service area and its track's way out lie outside the loop, clear of it but for the straight they are beside.
			double back = Half + 4.0 + ServiceD + Town.Pad + 8.0;
			double ox = tz, oz = -tx;
			foreach ((double u, double v) in new[] { (-ServiceW * 0.5, 0.0), (ServiceW * 0.5, 0.0), (-ServiceW * 0.5, back), (ServiceW * 0.5, back), (0.0, back + 260.0) })
			{
				double qx = px + tx * u + ox * (v + Half), qz = pz + tz * u + oz * (v + Half);
				if (Within(x, z, qx, qz))
				{
					return false;
				}
				for (int i = 0; i < n; i++)
				{
					if (s[i] > StraightLength + 100.0 && s[i] < length - 100.0 && (x[i] - qx) * (x[i] - qx) + (z[i] - qz) * (z[i] - qz) < Separation * Separation)
					{
						return false;
					}
				}
			}
			X = x;
			Z = z;
			S = s;
			Count = n;
			Length = length;
			DirX = tx;
			DirZ = tz;
			MidX = px;
			MidZ = pz;
			// Anticlockwise, the outside is to the right.
			OutX = tz;
			OutZ = -tx;
			TightestRadius = tightest;
			CornerCount = Turns(curve, 1.0 / 120.0, 0.5);
			return true;
		}

		/// <summary>Whether (qx, qz) is inside the closed line (x, z) (an even number of crossings to its east is outside).</summary>
		private static bool Within(double[] x, double[] z, double qx, double qz)
		{
			bool inside = false;
			int n = x.Length;
			for (int i = 0; i < n; i++)
			{
				int q = (i + 1) % n;
				if ((z[i] <= qz) != (z[q] <= qz) && x[i] + (qz - z[i]) / (z[q] - z[i]) * (x[q] - x[i]) > qx)
				{
					inside = !inside;
				}
			}
			return inside;
		}

		/// <summary>Each sample's signed curvature (positive turning left), on the circle through the samples 3 either side, smoothed over 5.</summary>
		private static double[] Curvature(double[] x, double[] z)
		{
			int n = x.Length;
			double[] raw = new double[n];
			for (int i = 0; i < n; i++)
			{
				int a = (i - 3 + n) % n, b = (i + 3) % n;
				double ax = x[i] - x[a], az = z[i] - z[a], bx = x[b] - x[i], bz = z[b] - z[i], cx = x[b] - x[a], cz = z[b] - z[a];
				double cross = ax * bz - az * bx;
				double den = Math.Sqrt(ax * ax + az * az) * Math.Sqrt(bx * bx + bz * bz) * Math.Sqrt(cx * cx + cz * cz);
				raw[i] = den > 1e-9 ? 2.0 * cross / den : 0.0;
			}
			double[] curve = new double[n];
			for (int i = 0; i < n; i++)
			{
				double sum = 0.0;
				for (int k = -2; k <= 2; k++)
				{
					sum += raw[(i + k + n) % n];
				}
				curve[i] = sum / 5.0;
			}
			return curve;
		}

		/// <summary>The corners: runs of samples one way tighter than <paramref name="threshold"/> turning through more than <paramref name="least"/> radians.</summary>
		private static int Turns(double[] curve, double threshold, double least)
		{
			int n = curve.Length, corners = 0;
			double angle = 0.0;
			for (int i = 0; i <= n; i++)
			{
				double c = curve[i % n];
				if (i < n && Math.Abs(c) > threshold && (angle == 0.0 || Math.Sign(c) == Math.Sign(angle)))
				{
					angle += c * Step;
					continue;
				}
				corners += Math.Abs(angle) > least ? 1 : 0;
				angle = i < n && Math.Abs(c) > threshold ? c * Step : 0.0;
			}
			return corners;
		}

		/// <summary>
		/// What is placed along the kept loop: the service area's access, and straw bales on the outside of the tightest corners (up to
		/// ten, under a 70 radius, 250 apart along the loop, none on the straight), where a car that runs wide would leave the road.
		/// </summary>
		private static void Place()
		{
			double back = Half + 4.0 + ServiceD + Town.Pad + 8.0;
			AccessX = MidX + OutX * back;
			AccessZ = MidZ + OutZ * back;
			AccessOutX = MidX + OutX * (back + 260.0);
			AccessOutZ = MidZ + OutZ * (back + 260.0);
			double[] curve = Curvature(X, Z);
			List<(double radius, int i)> apexes = new List<(double, int)>();
			for (int i = 0; i < Count; i++)
			{
				double c = Math.Abs(curve[i]);
				if (c > 1.0 / 70.0 && c >= Math.Abs(curve[(i - 1 + Count) % Count]) && c > Math.Abs(curve[(i + 1) % Count]) && S[i] > StraightLength + 150.0 && S[i] < Length - 150.0)
				{
					apexes.Add((1.0 / c, i));
				}
			}
			apexes.Sort((p, q) => p.radius != q.radius ? p.radius.CompareTo(q.radius) : p.i.CompareTo(q.i));
			foreach ((double radius, int i) in apexes)
			{
				if (Corners.Count >= 10)
				{
					break;
				}
				bool apart = true;
				foreach (Bales b in Corners)
				{
					double mid = (b.S0 + b.S1) * 0.5;
					if (Math.Min(Math.Abs(mid - S[i]), Length - Math.Abs(mid - S[i])) < 250.0)
					{
						apart = false;
						break;
					}
				}
				if (!apart)
				{
					continue;
				}
				// The outside of a left-hander is on the right.
				int side = curve[i] > 0.0 ? -1 : 1;
				Bales row = new Bales { S0 = S[i] - 5.0 * BaleLength, S1 = S[i] + 5.0 * BaleLength, Side = side, Radius = radius };
				if (Clear(row))
				{
					Corners.Add(row);
				}
			}
			Corners.Sort((p, q) => p.S0.CompareTo(q.S0));
		}

		/// <summary>Whether a row of bales keeps off the rest of the loop (another stretch's edge and soft edge).</summary>
		private static bool Clear(Bales row)
		{
			for (double s = row.S0; s <= row.S1; s += BaleLength)
			{
				Bale(row, s, out double x, out double z, out double _, out double _);
				for (int i = 0; i < Count; i++)
				{
					double along = Math.Abs(S[i] - s);
					if (Math.Min(along, Length - along) < 80.0)
					{
						continue;
					}
					double dx = X[i] - x, dz = Z[i] - z;
					if (dx * dx + dz * dz < (Half + 12.0) * (Half + 12.0))
					{
						return false;
					}
				}
			}
			return true;
		}

		/// <summary>The middle of the bale at distance s along the loop in a row, and the loop's direction there.</summary>
		internal static void Bale(Bales row, double s, out double x, out double z, out double dx, out double dz)
		{
			At(s, out double px, out double pz, out dx, out dz);
			// Left of the way the loop runs is (-dz, dx).
			double off = (BaleOut + BaleDepth * 0.5) * row.Side;
			x = px - dz * off;
			z = pz + dx * off;
		}

		/// <summary>The loop's point at distance s along it (wrapping round), and its direction there.</summary>
		internal static void At(double s, out double x, out double z, out double dx, out double dz)
		{
			s = ((s % Length) + Length) % Length;
			int lo = 0, hi = Count - 1;
			while (hi - lo > 1)
			{
				int mid = (lo + hi) / 2;
				if (S[mid] <= s)
				{
					lo = mid;
				}
				else
				{
					hi = mid;
				}
			}
			int i = S[hi] <= s ? hi : lo, q = (i + 1) % Count;
			double len = (q == 0 ? Length : S[q]) - S[i];
			double t = len > 0.0 ? (s - S[i]) / len : 0.0;
			x = X[i] + (X[q] - X[i]) * t;
			z = Z[i] + (Z[q] - Z[i]) * t;
			dx = (X[q] - X[i]) / Math.Max(1e-9, len);
			dz = (Z[q] - Z[i]) / Math.Max(1e-9, len);
		}

		// ---- the index ---------------------------------------------------------------------------------------------

		/// <summary>How far round the loop the index reaches: the woods round it.</summary>
		private const double Reach = Half + WoodsReach + 20.0;

		private const double Bucket = 100.0;
		private static double bx0, bz0;
		private static int bw, bh;
		private static int[][] buckets;

		/// <summary>The inside of the loop, as a raster.</summary>
		private const double InCell = 10.0;
		private static double ix0, iz0;
		private static int iw, ih;
		private static bool[] inside;

		private static void Index()
		{
			double minX = double.MaxValue, maxX = double.MinValue, minZ = double.MaxValue, maxZ = double.MinValue;
			for (int i = 0; i < Count; i++)
			{
				minX = Math.Min(minX, X[i]);
				maxX = Math.Max(maxX, X[i]);
				minZ = Math.Min(minZ, Z[i]);
				maxZ = Math.Max(maxZ, Z[i]);
			}
			bx0 = minX - Reach - Bucket;
			bz0 = minZ - Reach - Bucket;
			bw = (int)Math.Ceiling((maxX - minX + 2.0 * (Reach + Bucket)) / Bucket);
			bh = (int)Math.Ceiling((maxZ - minZ + 2.0 * (Reach + Bucket)) / Bucket);
			List<int>[] lists = new List<int>[bw * bh];
			for (int i = 0; i < Count; i++)
			{
				int q = (i + 1) % Count;
				int a0 = (int)Math.Floor((Math.Min(X[i], X[q]) - Reach - bx0) / Bucket), a1 = (int)Math.Floor((Math.Max(X[i], X[q]) + Reach - bx0) / Bucket);
				int c0 = (int)Math.Floor((Math.Min(Z[i], Z[q]) - Reach - bz0) / Bucket), c1 = (int)Math.Floor((Math.Max(Z[i], Z[q]) + Reach - bz0) / Bucket);
				for (int c = Math.Max(0, c0); c <= Math.Min(bh - 1, c1); c++)
				{
					for (int a = Math.Max(0, a0); a <= Math.Min(bw - 1, a1); a++)
					{
						(lists[c * bw + a] ??= new List<int>()).Add(i);
					}
				}
			}
			buckets = new int[bw * bh][];
			for (int k = 0; k < lists.Length; k++)
			{
				buckets[k] = lists[k]?.ToArray();
			}

			ix0 = minX - InCell;
			iz0 = minZ - InCell;
			iw = (int)Math.Ceiling((maxX - minX) / InCell) + 2;
			ih = (int)Math.Ceiling((maxZ - minZ) / InCell) + 2;
			inside = new bool[iw * ih];
			List<double> cross = new List<double>();
			for (int j = 0; j < ih; j++)
			{
				double z = iz0 + (j + 0.5) * InCell;
				cross.Clear();
				for (int i = 0; i < Count; i++)
				{
					int q = (i + 1) % Count;
					if ((Z[i] <= z) != (Z[q] <= z))
					{
						cross.Add(X[i] + (z - Z[i]) / (Z[q] - Z[i]) * (X[q] - X[i]));
					}
				}
				cross.Sort();
				for (int k = 0; k + 1 < cross.Count; k += 2)
				{
					int a = (int)Math.Ceiling((cross[k] - ix0) / InCell - 0.5), b = (int)Math.Floor((cross[k + 1] - ix0) / InCell - 0.5);
					for (int i = Math.Max(0, a); i <= Math.Min(iw - 1, b); i++)
					{
						inside[j * iw + i] = true;
					}
				}
			}
		}

		/// <summary>Distance from (x, z) to the loop's centre line, if within <see cref="Reach"/>; otherwise <see cref="Reach"/> or more.</summary>
		internal static double Distance(double x, double z)
		{
			if (!Exists)
			{
				return double.MaxValue;
			}
			int a = (int)Math.Floor((x - bx0) / Bucket), c = (int)Math.Floor((z - bz0) / Bucket);
			if (a < 0 || c < 0 || a >= bw || c >= bh || buckets[c * bw + a] == null)
			{
				return double.MaxValue;
			}
			double best = Reach * Reach;
			foreach (int i in buckets[c * bw + a])
			{
				int q = (i + 1) % Count;
				double sx = X[q] - X[i], sz = Z[q] - Z[i];
				double len2 = sx * sx + sz * sz;
				double f = len2 > 0.0 ? ((x - X[i]) * sx + (z - Z[i]) * sz) / len2 : 0.0;
				f = f < 0.0 ? 0.0 : f > 1.0 ? 1.0 : f;
				double px = X[i] + sx * f - x, pz = Z[i] + sz * f - z;
				best = Math.Min(best, px * px + pz * pz);
			}
			return Math.Sqrt(best);
		}

		/// <summary>True inside the loop.</summary>
		internal static bool Inside(double x, double z)
		{
			if (!Exists)
			{
				return false;
			}
			int i = (int)Math.Floor((x - ix0) / InCell), j = (int)Math.Floor((z - iz0) / InCell);
			return i >= 0 && j >= 0 && i < iw && j < ih && inside[j * iw + i];
		}

		/// <summary>(x, z) in the service area's frame: u along the straight from its middle, v out from the stage's centre line.</summary>
		private static void ServiceLocal(double x, double z, out double u, out double v)
		{
			double dx = x - MidX, dz = z - MidZ;
			u = dx * DirX + dz * DirZ;
			v = dx * OutX + dz * OutZ;
		}

		/// <summary>
		/// True on the stage's ground: inside the loop, within <paramref name="margin"/> of its edge, or by the service area (plus the
		/// margin). No road but its own access track, and no farm, comes in here.
		/// </summary>
		internal static bool Fenced(double x, double z, double margin)
		{
			if (!Exists)
			{
				return false;
			}
			if (Inside(x, z) || Distance(x, z) <= Half + margin)
			{
				return true;
			}
			ServiceLocal(x, z, out double u, out double v);
			return Math.Abs(u) <= ServiceW * 0.5 + Town.Pad + margin && v >= 0.0 && v <= Half + 4.0 + ServiceD + Town.Pad + margin;
		}

		/// <summary>False if (x, z) is in the loop or within <paramref name="margin"/> of the stage (as far out as need be): where farms may not be.</summary>
		internal static bool Clear(double x, double z, double margin)
		{
			if (!Exists)
			{
				return true;
			}
			if (Inside(x, z))
			{
				return false;
			}
			double reach = Half + margin;
			for (int i = 0; i < Count; i += 2)
			{
				double dx = X[i] - x, dz = Z[i] - z;
				if (dx * dx + dz * dz < reach * reach)
				{
					return false;
				}
			}
			return !Fenced(x, z, margin);
		}

		/// <summary>True where the forest is kept round the stage: in the loop and near it (the service area's yard is drawn over it).</summary>
		internal static bool Woods(double x, double z)
		{
			return Exists && (Inside(x, z) || Distance(x, z) <= Half + WoodsReach);
		}

		/// <summary>True on the stage's bales or arches (with <paramref name="margin"/>), where no plant grows.</summary>
		internal static bool Covered(double x, double z, double margin)
		{
			if (!Exists || Distance(x, z) > BaleOut + BaleDepth + margin + 2.0)
			{
				return false;
			}
			foreach (Bales row in Corners)
			{
				for (double s = row.S0; s <= row.S1 + 0.01; s += BaleLength)
				{
					Bale(row, s, out double bx, out double bz, out double dx, out double dz);
					double ex = x - bx, ez = z - bz;
					if (Math.Abs(ex * dx + ez * dz) <= BaleLength * 0.5 + margin && Math.Abs(-ex * dz + ez * dx) <= BaleDepth * 0.5 + margin)
					{
						return true;
					}
				}
			}
			foreach (double s in new[] { StartS, FinishS })
			{
				for (int side = -1; side <= 1; side += 2)
				{
					Post(s, side, out double px, out double pz);
					if ((x - px) * (x - px) + (z - pz) * (z - pz) <= (2.0 + margin) * (2.0 + margin))
					{
						return true;
					}
				}
			}
			return false;
		}

		/// <summary>An arch's post: at s along the straight, on side <paramref name="side"/> (+1 left), just past the stage's soft edge.</summary>
		internal static void Post(double s, int side, out double x, out double z)
		{
			double off = (Half + 4.0) * side;
			x = X[0] + DirX * s - DirZ * off;
			z = Z[0] + DirZ * s + DirX * off;
		}
	}
}
