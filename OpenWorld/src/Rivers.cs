using System;
using System.Collections.Generic;

namespace HalfGripOpenWorld
{
	/// <summary>
	/// Rivers, from the way water would really run off the land. On a 100-unit grid of <see cref="WorldGen.Natural"/>, every cell is
	/// drained to the sea by a priority flood (the sea is the only outlet; a hollow fills until it spills, as a real one would), which
	/// gives each cell the cell its water runs to. Adding up how many cells drain through each cell gives its catchment; where that is big
	/// enough there is a stream, bigger downstream. The streams are smoothed into lines with a width and a water level that only falls
	/// downstream, and they cut the land: a channel, a flat floodplain either side, and banks back up to the land.
	///
	/// Built once, from the land alone (no config, no randomness), so it is the same for every player.
	/// </summary>
	internal static class Rivers
	{
		internal sealed class River
		{
			public double[] X;
			public double[] Z;
			/// <summary>Water level at each sample: only ever falls downstream.</summary>
			public double[] W;
			/// <summary>Half the channel's width at each sample.</summary>
			public double[] Half;
			/// <summary>Half the floodplain's width (from the centre) at each sample.</summary>
			public double[] Plain;
			/// <summary>The river this one flows into (-1 for the sea) and the sample of it where.</summary>
			public int Into = -1;
			public int At;
			public int Count => X.Length;
		}

		private const double Size = 100.0;
		private const int N = 320;
		private const double Min = -N * Size / 2.0;

		/// <summary>Catchment (in cells) at which a stream starts. 700 cells is 7 million square units: about 0.65 km² at car scale.</summary>
		private const int Source = 700;

		private const double Step = 20.0;

		/// <summary>
		/// The channel's edge rises from just under the water to the floodplain over this width, not in one step: the ground is drawn
		/// as 5-unit triangles, and across a step they dipped under the water's edge, leaving it hanging in the air.
		/// </summary>
		private const double EdgeRise = 5.0;

		/// <summary>
		/// How far from a river's centre its water surface is drawn, for a channel <paramref name="half"/> wide each side: past where
		/// the edge rises above the water (a third of <see cref="EdgeRise"/> out) by a triangle's diagonal, so the drawn ground covers
		/// the surface's edge wherever it is.
		/// </summary>
		internal static double Edge(double half)
		{
			return half + EdgeRise / 3.0 + 7.2;
		}

		/// <summary>
		/// Where the ground beside a river's water is under its floodplain's height (cut lower by another river beside it in the same
		/// valley, or a tributary held up to the river it flows into), the river raises a bank up to it, at most this high, falling
		/// away 1 in 2 past where its surface is drawn; never in a channel, and never over the land.
		/// </summary>
		private const double LeveeMost = 10.0;

		/// <summary>Bank slope beside the floodplain: 1 up for every 3 across.</summary>
		private const double BankRun = 3.0;
		private const double MaxBank = 450.0;

		internal static readonly List<River> All = new List<River>();

		/// <summary>Catchment (cells draining through) per grid cell, for placing settlements by rivers.</summary>
		internal static int[] Catchment;

		/// <summary>
		/// Water surface per grid cell: the sea's level for sea cells, a lake's level for cells in a lake, NaN for dry land. Land made from
		/// noise is full of hollows that real, worn-down land does not have, so only the eight biggest hollows hold a lake, and only in
		/// their deepest part: up to 6 units above the hollow's floor (never above its spill point), at least 40 cells big.
		/// </summary>
		internal static double[] Surface;

		/// <summary>The lakes, biggest first: each one's level and its grid cells (j * <see cref="GridN"/> + i).</summary>
		internal static readonly List<(double level, List<int> cells)> Lakes = new List<(double, List<int>)>();

		/// <summary>Number of lakes, for the log.</summary>
		internal static int LakeCount;

		internal const double GridSize = Size;
		internal const int GridN = N;
		internal const double GridMin = Min;

		private static readonly ulong SaltRoute = Noise.Mix(WorldGen.Seed + 101);
		private static readonly ulong SaltMeander = Noise.Mix(WorldGen.Seed + 102);

		internal static void Build()
		{
			// Water is routed over the land plus a gentle swell of noise: on a smooth slope pure steepest-descent runs in straight grid lines
			// side by side, while real streams wander and gather into fewer, bigger rivers. The water level is then taken from the real
			// land along the river, so the noise never makes water stand above the ground.
			double[] h = new double[N * N];
			for (int j = 0; j < N; j++)
			{
				for (int i = 0; i < N; i++)
				{
					double x = Min + (i + 0.5) * Size, z = Min + (j + 0.5) * Size;
					double land = WorldGen.Natural(x, z);
					h[j * N + i] = land < WorldGen.SeaLevel ? land : land + Noise.Fbm(SaltRoute, x, z, 2500.0, 3, 0.5) * 30.0;
				}
			}

			// Priority flood from the sea: the lowest cell on the edge of the drained area is taken next, and its unvisited neighbours drain
			// into it, raised to just above it if they are lower (a filled hollow).
			int[] down = new int[N * N];
			double[] fill = new double[N * N];
			bool[] seen = new bool[N * N];
			List<int> order = new List<int>(N * N);
			Heap heap = new Heap();
			for (int c = 0; c < N * N; c++)
			{
				down[c] = -1;
				if (h[c] < WorldGen.SeaLevel)
				{
					seen[c] = true;
					fill[c] = h[c];
					heap.Push(h[c], c);
				}
			}
			while (heap.Count > 0)
			{
				int c = heap.Pop(out double z);
				order.Add(c);
				int ci = c % N, cj = c / N;
				for (int dj = -1; dj <= 1; dj++)
				{
					for (int di = -1; di <= 1; di++)
					{
						int ni = ci + di, nj = cj + dj;
						if ((di == 0 && dj == 0) || ni < 0 || nj < 0 || ni >= N || nj >= N)
						{
							continue;
						}
						int n = nj * N + ni;
						if (seen[n])
						{
							continue;
						}
						seen[n] = true;
						down[n] = c;
						fill[n] = Math.Max(h[n], z + 0.01);
						heap.Push(fill[n], n);
					}
				}
			}

			int[] acc = new int[N * N];
			for (int k = order.Count - 1; k >= 0; k--)
			{
				int c = order[k];
				acc[c] += 1;
				if (down[c] >= 0 && h[c] >= WorldGen.SeaLevel)
				{
					acc[down[c]] += acc[c];
				}
			}
			Catchment = acc;
			FindLakes();

			// A cell is on a stream if enough drains through it and it is land. A stream starts where no stream flows in.
			bool[] stream = new bool[N * N];
			bool[] fed = new bool[N * N];
			for (int c = 0; c < N * N; c++)
			{
				stream[c] = acc[c] >= Source && h[c] >= WorldGen.SeaLevel;
			}
			for (int c = 0; c < N * N; c++)
			{
				if (stream[c] && down[c] >= 0)
				{
					fed[down[c]] = true;
				}
			}
			bool[] drawn = new bool[N * N];
			for (int head = 0; head < N * N; head++)
			{
				if (!stream[head] || fed[head])
				{
					continue;
				}
				// Down to the sea, or to a stream already drawn (a confluence: that cell is included, so the two meet).
				List<int> cells = new List<int>();
				int c = head;
				while (c >= 0)
				{
					cells.Add(c);
					if (drawn[c] || h[c] < WorldGen.SeaLevel)
					{
						break;
					}
					drawn[c] = true;
					c = down[c];
				}
				if (cells.Count >= 4)
				{
					All.Add(Make(cells, acc, drawn, c));
				}
			}
			// Longest rivers first, so a tributary meeting a main river is cut after it and never deeper.
			All.Sort((a, b) => b.Count != a.Count ? b.Count.CompareTo(a.Count) : a.X[0] != b.X[0] ? a.X[0].CompareTo(b.X[0]) : a.Z[0].CompareTo(b.Z[0]));
			Join();
			Index();
			// Each river's water level from the land as the rivers before it have cut it: a tributary coming down into a bigger river's
			// valley runs on that valley's cut banks, far under the land it would have had.
			for (int r = 0; r < All.Count; r++)
			{
				Level(r);
			}
		}

		/// <summary>The lakes: a second flood over the real land (no routing noise) finds the hollows, kept where deep and big enough.</summary>
		private static void FindLakes()
		{
			double[] h = new double[N * N];
			double[] fill = new double[N * N];
			bool[] seen = new bool[N * N];
			Heap heap = new Heap();
			Surface = new double[N * N];
			for (int c = 0; c < N * N; c++)
			{
				h[c] = WorldGen.Natural(Min + (c % N + 0.5) * Size, Min + (c / N + 0.5) * Size);
				Surface[c] = double.NaN;
				if (h[c] < WorldGen.SeaLevel)
				{
					seen[c] = true;
					fill[c] = h[c];
					Surface[c] = WorldGen.SeaLevel;
					heap.Push(h[c], c);
				}
			}
			while (heap.Count > 0)
			{
				int c = heap.Pop(out double z);
				int ci = c % N, cj = c / N;
				for (int dj = -1; dj <= 1; dj++)
				{
					for (int di = -1; di <= 1; di++)
					{
						int ni = ci + di, nj = cj + dj;
						if ((di == 0 && dj == 0) || ni < 0 || nj < 0 || ni >= N || nj >= N)
						{
							continue;
						}
						int n = nj * N + ni;
						if (!seen[n])
						{
							seen[n] = true;
							fill[n] = Math.Max(h[n], z);
							heap.Push(fill[n], n);
						}
					}
				}
			}
			// Connected cells under water (filled above the land), grouped; each group's lake is the part within 6 of its floor.
			List<(int size, double level, List<int> cells)> lakes = new List<(int, double, List<int>)>();
			int[] group = new int[N * N];
			for (int c = 0; c < N * N; c++)
			{
				group[c] = -1;
			}
			Stack<int> stack = new Stack<int>();
			List<int> members = new List<int>();
			for (int start = 0; start < N * N; start++)
			{
				if (group[start] >= 0 || !(fill[start] - h[start] > 0.05) || h[start] < WorldGen.SeaLevel)
				{
					continue;
				}
				members.Clear();
				double deepest = 0.0;
				group[start] = start;
				stack.Push(start);
				while (stack.Count > 0)
				{
					int c = stack.Pop();
					members.Add(c);
					deepest = Math.Max(deepest, fill[c] - h[c]);
					int ci = c % N, cj = c / N;
					for (int dj = -1; dj <= 1; dj++)
					{
						for (int di = -1; di <= 1; di++)
						{
							int ni = ci + di, nj = cj + dj;
							if (ni < 0 || nj < 0 || ni >= N || nj >= N)
							{
								continue;
							}
							int n = nj * N + ni;
							if (group[n] < 0 && fill[n] - h[n] > 0.05 && h[n] >= WorldGen.SeaLevel)
							{
								group[n] = start;
								stack.Push(n);
							}
						}
					}
				}
				double floor = double.MaxValue;
				double spill = 0.0;
				foreach (int c in members)
				{
					floor = Math.Min(floor, h[c]);
					spill = Math.Max(spill, fill[c]);
				}
				double level = Math.Min(spill, floor + 6.0);
				List<int> wet = members.FindAll(c => h[c] < level - 0.5);
				if (deepest > 2.0 && wet.Count >= 40)
				{
					lakes.Add((wet.Count, level, wet));
				}
			}
			lakes.Sort((a, b) => b.size != a.size ? b.size.CompareTo(a.size) : a.cells[0].CompareTo(b.cells[0]));
			for (int k = 0; k < Math.Min(8, lakes.Count); k++)
			{
				LakeCount++;
				Lakes.Add((lakes[k].level, lakes[k].cells));
				foreach (int c in lakes[k].cells)
				{
					Surface[c] = lakes[k].level;
				}
			}
		}

		/// <summary>The water surface of the grid cell holding (x, z): sea or lake level, or NaN.</summary>
		internal static double SurfaceAt(double x, double z)
		{
			int i = (int)Math.Floor((x - Min) / Size);
			int j = (int)Math.Floor((z - Min) / Size);
			return Surface == null || i < 0 || j < 0 || i >= N || j >= N ? double.NaN : Surface[j * N + i];
		}

		/// <summary>A river from its grid cells: smoothed, meandering by its size, with widths from its catchment and a level that only falls.</summary>
		private static River Make(List<int> cells, int[] acc, bool[] drawn, int mouth)
		{
			List<(double x, double z)> pts = new List<(double x, double z)>();
			foreach (int c in cells)
			{
				pts.Add((Min + (c % N + 0.5) * Size, Min + (c / N + 0.5) * Size));
			}
			// Round the grid steps off into bends (Chaikin, ends kept), then sample every Step.
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
			List<(double x, double z)> line = Lines.Resample(pts, Step);
			int n = line.Count;
			// Meanders: each point pushed sideways by slow noise along the river's length, more for a bigger river, never at the ends.
			double[] side = new double[n];
			for (int i = 0; i < n; i++)
			{
				int c = cells[Math.Min(cells.Count - 1, (int)((long)i * cells.Count / n))];
				double flow = Math.Sqrt(acc[c] / (double)Source);
				double fade = Math.Min(1.0, Math.Min(i, n - 1 - i) / 15.0);
				side[i] = Noise.Gradient(SaltMeander, i * Step, cells[0], 260.0 + 60.0 * flow) * Math.Min(90.0, 18.0 + 14.0 * flow) * fade;
			}
			List<(double x, double z)> bent = new List<(double x, double z)>(n);
			for (int i = 0; i < n; i++)
			{
				int a = Math.Max(0, i - 1), b = Math.Min(n - 1, i + 1);
				double tx = line[b].x - line[a].x, tz = line[b].z - line[a].z;
				double len = Math.Sqrt(tx * tx + tz * tz);
				bent.Add(len > 0.0 ? (line[i].x - tz / len * side[i], line[i].z + tx / len * side[i]) : line[i]);
			}
			line = Lines.Resample(bent, Step);
			n = line.Count;
			River river = new River { X = new double[n], Z = new double[n], W = new double[n], Half = new double[n], Plain = new double[n] };
			for (int i = 0; i < n; i++)
			{
				river.X[i] = line[i].x;
				river.Z[i] = line[i].z;
				// Width and level from the nearest grid cell of the stream (the cells run along the line in order).
				int c = cells[Math.Min(cells.Count - 1, (int)((long)i * cells.Count / n))];
				double flow = Math.Sqrt(acc[c] / (double)Source);
				river.Half[i] = Math.Min(40.0, 4.0 + 4.0 * flow);
				river.Plain[i] = river.Half[i] + Math.Min(260.0, 10.0 + 22.0 * flow);
			}
			return river;
		}

		/// <summary>
		/// River r's water level: a unit under the lowest land along it, at the centre and across past where its surface is drawn on both
		/// sides, as cut by the rivers before it (the meanders put the line on slopes, where one bank is well under the centre); smoothed,
		/// but never over the land near it (a smoothed level rose above the floor of a narrow valley); held so it only falls downstream;
		/// and a tributary's held up to the level of the river it flows into at its mouth, so the two meet. Rivers only ever lower the land,
		/// so water above the land at a bank would hang in the air there.
		/// </summary>
		private static void Level(int r)
		{
			River river = All[r];
			int n = river.Count;
			double[] low = new double[n];
			for (int i = 0; i < n; i++)
			{
				int a = Math.Max(0, i - 1), b = Math.Min(n - 1, i + 1);
				double tx = river.X[b] - river.X[a], tz = river.Z[b] - river.Z[a];
				double len = Math.Sqrt(tx * tx + tz * tz);
				double sx = len > 0.0 ? -tz / len : 0.0, sz = len > 0.0 ? tx / len : 0.0;
				double reach = Edge(river.Half[i]) + 8.0;
				int steps = (int)Math.Ceiling(reach / 4.0);
				low[i] = Before(river.X[i], river.Z[i], r);
				for (int k = 1; k <= steps; k++)
				{
					double off = reach * k / steps;
					low[i] = Math.Min(low[i], Math.Min(Before(river.X[i] + sx * off, river.Z[i] + sz * off, r), Before(river.X[i] - sx * off, river.Z[i] - sz * off, r)));
				}
				low[i] -= 1.0;
			}
			// Between two samples (20 apart) the surface is drawn straight, so each sample is also held to its neighbours' lows.
			for (int i = 0; i < n; i++)
			{
				int a = Math.Max(0, i - 6), b = Math.Min(n - 1, i + 6);
				double sum = 0.0, floor = double.MaxValue;
				for (int k = a; k <= b; k++)
				{
					sum += low[k];
					if (Math.Abs(k - i) <= 1)
					{
						floor = Math.Min(floor, low[k]);
					}
				}
				river.W[i] = Math.Max(WorldGen.SeaLevel + 0.3, Math.Min(sum / (b - a + 1), floor));
			}
			for (int i = 1; i < n; i++)
			{
				river.W[i] = Math.Max(WorldGen.SeaLevel + 0.3, Math.Min(river.W[i], river.W[i - 1] - 0.002 * Step));
			}
			if (river.Into >= 0)
			{
				// Never over its own banks, though: its last stretch can run beside the other river downstream of where it joins it,
				// where that river (and so its floodplain) is lower.
				double meet = All[river.Into].W[river.At];
				for (int i = n - 1; i >= 0 && river.W[i] < meet; i--)
				{
					river.W[i] = Math.Max(river.W[i], Math.Min(meet, low[i]));
				}
			}
		}

		/// <summary>The natural land at (x, z) with the rivers before river r cut into it.</summary>
		private static double Before(double x, double z, int r)
		{
			double natural = WorldGen.Natural(x, z);
			return Cut(x, z, natural, natural, r);
		}

		/// <summary>
		/// A tributary's last point is moved onto the river it flows into (the nearest point of a longer river), which is noted, so its
		/// water at the mouth is held no lower than that river's there (<see cref="Level"/>) and the two meet.
		/// </summary>
		private static void Join()
		{
			for (int r = 1; r < All.Count; r++)
			{
				River trib = All[r];
				int last = trib.Count - 1;
				double best = 250.0 * 250.0;
				int into = -1, at = 0;
				for (int o = 0; o < r; o++)
				{
					River main = All[o];
					for (int i = 0; i < main.Count; i++)
					{
						double dx = main.X[i] - trib.X[last], dz = main.Z[i] - trib.Z[last];
						double d2 = dx * dx + dz * dz;
						if (d2 < best)
						{
							best = d2;
							into = o;
							at = i;
						}
					}
				}
				if (into < 0)
				{
					continue;
				}
				River m = All[into];
				trib.X[last] = m.X[at];
				trib.Z[last] = m.Z[at];
				trib.Into = into;
				trib.At = at;
			}
		}

		// ---- the cut ---------------------------------------------------------------------------------------------

		private const double Cell = 128.0;
		private const int Cells = 260;
		private const double Origin0 = -Cells * Cell / 2.0;
		private static int[] cellStart;
		private static long[] cellItems;

		/// <summary>Each thread's projections of a cell's river segments, made once per point in <see cref="Cut(double, double, double, double, int)"/>.</summary>
		[ThreadStatic]
		private static double[] projected;

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
				for (int r = 0; r < All.Count; r++)
				{
					River river = All[r];
					for (int i = 0; i + 1 < river.Count; i++)
					{
						double reach = river.Plain[i] + MaxBank;
						int c0 = CellOf(Math.Min(river.X[i], river.X[i + 1]) - reach);
						int c1 = CellOf(Math.Max(river.X[i], river.X[i + 1]) + reach);
						int r0 = CellOf(Math.Min(river.Z[i], river.Z[i + 1]) - reach);
						int r1 = CellOf(Math.Max(river.Z[i], river.Z[i + 1]) + reach);
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

		/// <summary>The nearest river to (x, z) within its reach: its distance, water level, channel and floodplain half widths there.</summary>
		private static bool Nearest(double x, double z, out double d, out double w, out double half, out double plain)
		{
			d = double.MaxValue;
			w = half = plain = 0.0;
			if (cellStart == null)
			{
				return false;
			}
			int c = CellOf(z) * Cells + CellOf(x);
			bool found = false;
			for (int k = cellStart[c]; k < cellStart[c + 1]; k++)
			{
				long item = cellItems[k];
				River river = All[(int)(item >> 32)];
				int i = (int)(uint)item;
				Lines.Project(river.X, river.Z, i, x, z, out double t, out double dist);
				if (dist < d)
				{
					d = dist;
					w = river.W[i] + (river.W[i + 1] - river.W[i]) * t;
					half = river.Half[i] + (river.Half[i + 1] - river.Half[i]) * t;
					plain = river.Plain[i] + (river.Plain[i + 1] - river.Plain[i]) * t;
					found = true;
				}
			}
			return found;
		}

		/// <summary>
		/// The land at (x, z) with the rivers cut into it: the channel, its edge rising over <see cref="EdgeRise"/> to a floodplain just
		/// above the water, and a bank of 1 in 3 back up to the land. Rivers mostly lower the land, with two exceptions, both so a river's
		/// water never hangs over lower ground: where the land given (<paramref name="land"/>) was lowered from the natural land
		/// (<paramref name="natural"/>) by a place's levelling under a river's floodplain, it is raised back towards the floodplain's
		/// height (never above the natural land); and land just beside the water lower than its floodplain gets a low bank
		/// (<see cref="LeveeMost"/>).
		/// </summary>
		internal static double Cut(double x, double z, double land, double natural)
		{
			return Cut(x, z, land, natural, int.MaxValue);
		}

		/// <summary>The land cut by the rivers before river <paramref name="limit"/> only (see <see cref="Cut(double, double, double, double)"/>).</summary>
		private static double Cut(double x, double z, double land, double natural, int limit)
		{
			if (cellStart == null)
			{
				return land;
			}
			int c = CellOf(z) * Cells + CellOf(x);
			double h = double.MaxValue;
			double floor = double.MinValue;
			double levee = double.MinValue;
			bool channel = false;
			// The nearest river shapes the ground. A farther one, or a farther stretch of the same river (a meander's next loop, lower
			// downstream), cuts it only through a bank of 1 in 1 rising away from the nearest: otherwise its wide floodplain spread
			// across the nearest river's banks, under that river's water.
			double nearest = double.MaxValue;
			int count = cellStart[c + 1] - cellStart[c];
			if (projected == null || projected.Length < count * 2)
			{
				projected = new double[Math.Max(64, count * 4)];
			}
			for (int q = cellStart[c]; q < cellStart[c + 1]; q++)
			{
				int rq = (int)(cellItems[q] >> 32);
				if (rq < limit)
				{
					River near = All[rq];
					Lines.Project(near.X, near.Z, (int)(uint)cellItems[q], x, z, out double tn, out double dn);
					projected[(q - cellStart[c]) * 2] = tn;
					projected[(q - cellStart[c]) * 2 + 1] = dn;
					nearest = Math.Min(nearest, dn);
				}
			}
			int k = cellStart[c], end = cellStart[c + 1];
			while (k < end)
			{
				int r = (int)(cellItems[k] >> 32);
				int k1 = k;
				while (k1 < end && (int)(cellItems[k1] >> 32) == r)
				{
					k1++;
				}
				if (r >= limit)
				{
					k = k1;
					continue;
				}
				River river = All[r];
				for (; k < k1; k++)
				{
					int i = (int)(uint)cellItems[k];
					double t = projected[(k - cellStart[c]) * 2], d = projected[(k - cellStart[c]) * 2 + 1];
					double plain = river.Plain[i] + (river.Plain[i + 1] - river.Plain[i]) * t;
					if (d >= plain + MaxBank)
					{
						continue;
					}
					double w = river.W[i] + (river.W[i + 1] - river.W[i]) * t;
					double half = river.Half[i] + (river.Half[i + 1] - river.Half[i]) * t;
					double depth = 1.5 + half * 0.08;
					double flat = w + 0.8 + Math.Max(0.0, d - half - EdgeRise) * 0.01;
					double target;
					if (d < half)
					{
						// A rounded channel bed.
						double u = d / half;
						target = w - depth * (1.0 - u * u) - 0.4;
					}
					else if (d < half + EdgeRise)
					{
						target = w - 0.4 + 1.2 * (d - half) / EdgeRise;
					}
					else if (d < plain)
					{
						target = flat;
					}
					else
					{
						target = w + 0.8 + (plain - half - EdgeRise) * 0.01 + (d - plain) / BankRun;
					}
					double away = (d - nearest) * 1.0;
					target += away;
					h = Math.Min(h, target);
					if (d >= half && d < plain + 40.0)
					{
						floor = Math.Max(floor, Math.Min(target, flat + away) - Noise.Step(plain, plain + 40.0, d) * 1000.0);
					}
					if (d >= half + EdgeRise)
					{
						levee = Math.Max(levee, flat + away - Math.Max(0.0, d - Edge(half) - 8.0) * 0.5);
					}
					else
					{
						channel = true;
					}
				}
			}
			double ground = land;
			if (land < natural)
			{
				ground = Math.Max(ground, Math.Min(natural, floor));
			}
			ground = Math.Min(ground, h);
			// Out of every channel, the bank is raised to the floodplain of any river whose water reaches here (never over the land).
			if (!channel)
			{
				ground = Math.Max(ground, Math.Min(Math.Min(levee, natural), ground + LeveeMost));
			}
			return ground;
		}

		/// <summary>The water surface at (x, z) if (x, z) is in a river's channel, a lake, the sea or a park's pond.</summary>
		internal static bool Water(double x, double z, out double level)
		{
			if (Parks.Pond(x, z, 0.0, out level))
			{
				return true;
			}
			level = SurfaceAt(x, z);
			if (!double.IsNaN(level))
			{
				return true;
			}
			level = 0.0;
			if (!Nearest(x, z, out double d, out double w, out double half, out double _) || d > half)
			{
				return false;
			}
			level = w;
			return true;
		}

		/// <summary>
		/// The water level at (x, z) if it is within <paramref name="margin"/> of a river's channel (lakes and the sea not counted). The
		/// water mesh reaches 2 past the channel's edge, so a margin of 2 is where water is drawn.
		/// </summary>
		internal static bool Channel(double x, double z, double margin, out double level)
		{
			level = 0.0;
			if (!Nearest(x, z, out double d, out double w, out double half, out double _) || d > half + margin)
			{
				return false;
			}
			level = w;
			return true;
		}

		/// <summary>
		/// Where segment (ax, az)-(bx, bz) crosses a river's centre line: for each crossing, the fraction along the segment, the water
		/// level and channel half width there, and the sine of the angle between the two (1 square on, near 0 alongside). A crossing is
		/// found however narrow the river is, which testing the road's samples (8 apart) for being in the channel is not.
		/// </summary>
		internal static void Crossings(double ax, double az, double bx, double bz, List<(double t, double w, double half, double sin)> found)
		{
			found.Clear();
			if (cellStart == null)
			{
				return;
			}
			// Every river segment is listed in each cell its floodplain and bank can reach (450 or more), so the cell of the segment's
			// middle lists every river segment that can cross it (road segments are 8 long).
			int c = CellOf((az + bz) * 0.5) * Cells + CellOf((ax + bx) * 0.5);
			double ux = bx - ax, uz = bz - az;
			double ul = Math.Sqrt(ux * ux + uz * uz);
			for (int k = cellStart[c]; k < cellStart[c + 1]; k++)
			{
				long item = cellItems[k];
				River river = All[(int)(item >> 32)];
				int i = (int)(uint)item;
				double cx = river.X[i], cz = river.Z[i];
				double vx = river.X[i + 1] - cx, vz = river.Z[i + 1] - cz;
				double den = ux * vz - uz * vx;
				if (den == 0.0)
				{
					continue;
				}
				double t = ((cx - ax) * vz - (cz - az) * vx) / den;
				double u = ((cx - ax) * uz - (cz - az) * ux) / den;
				// Half-open on the river's segment, so a crossing at a river sample is counted once.
				if (t < 0.0 || t > 1.0 || u < 0.0 || u >= 1.0)
				{
					continue;
				}
				double vl = Math.Sqrt(vx * vx + vz * vz);
				double sin = ul > 0.0 && vl > 0.0 ? Math.Abs(den) / (ul * vl) : 1.0;
				found.Add((t, river.W[i] + (river.W[i + 1] - river.W[i]) * u, river.Half[i] + (river.Half[i + 1] - river.Half[i]) * u, sin));
			}
		}

		/// <summary>
		/// The highest water surface drawn over (x, z) by a river other than <paramref name="skip"/> (its channel and the margin its
		/// surface is drawn past it), or NaN: where two rivers meet, one's water runs on into the other's.
		/// </summary>
		internal static double OtherWater(double x, double z, River skip)
		{
			double best = double.NaN;
			if (cellStart == null)
			{
				return best;
			}
			int c = CellOf(z) * Cells + CellOf(x);
			for (int k = cellStart[c]; k < cellStart[c + 1]; k++)
			{
				long item = cellItems[k];
				River river = All[(int)(item >> 32)];
				if (river == skip)
				{
					continue;
				}
				int i = (int)(uint)item;
				// A segment whose start is further from (x, z) than its length and its drawn water's reach cannot be under it: most of the
				// cell's list, which holds every segment whose floodplain and banks reach the cell.
				double reach = Edge(Math.Max(river.Half[i], river.Half[i + 1])) + Math.Abs(river.X[i + 1] - river.X[i]) + Math.Abs(river.Z[i + 1] - river.Z[i]) + 1.0;
				if (Math.Abs(river.X[i] - x) > reach || Math.Abs(river.Z[i] - z) > reach)
				{
					continue;
				}
				Lines.Project(river.X, river.Z, i, x, z, out double t, out double d);
				double half = river.Half[i] + (river.Half[i + 1] - river.Half[i]) * t;
				if (d < Edge(half))
				{
					double w = river.W[i] + (river.W[i + 1] - river.W[i]) * t;
					best = double.IsNaN(best) ? w : Math.Max(best, w);
				}
			}
			return best;
		}

		/// <summary>
		/// The highest water surface drawn over (x, z), or NaN: any river's, out to where its surface is drawn (<see cref="Edge"/>), and
		/// a lake's or the sea's, which is drawn over its own cells and one cell round them (see <see cref="WaterMesh"/>); and a park's pond's.
		/// </summary>
		internal static double DrawnWater(double x, double z)
		{
			double best = OtherWater(x, z, null);
			double pond = Parks.DrawnPond(x, z);
			if (!double.IsNaN(pond) && (double.IsNaN(best) || pond > best))
			{
				best = pond;
			}
			if (Surface == null)
			{
				return best;
			}
			int i = (int)Math.Floor((x - Min) / Size), j = (int)Math.Floor((z - Min) / Size);
			for (int dj = -1; dj <= 1; dj++)
			{
				for (int di = -1; di <= 1; di++)
				{
					int a = i + di, b = j + dj;
					if (a < 0 || b < 0 || a >= N || b >= N)
					{
						continue;
					}
					double s = Surface[b * N + a];
					if (!double.IsNaN(s) && (double.IsNaN(best) || s > best))
					{
						best = s;
					}
				}
			}
			return best;
		}

		/// <summary>Distance to the nearest river's centre line (MaxValue if none is near), and that river's floodplain half width.</summary>
		internal static double Distance(double x, double z, out double plain)
		{
			Nearest(x, z, out double d, out double _, out double _, out plain);
			return d;
		}
	}

	/// <summary>Polyline helpers shared by the roads and rivers.</summary>
	internal static class Lines
	{
		/// <summary>The polyline resampled every <paramref name="step"/> units (ends kept).</summary>
		internal static List<(double x, double z)> Resample(List<(double x, double z)> pts, double step)
		{
			List<(double x, double z)> output = new List<(double x, double z)> { pts[0] };
			double carry = 0.0;
			for (int i = 0; i + 1 < pts.Count; i++)
			{
				(double x0, double z0) = pts[i];
				(double x1, double z1) = pts[i + 1];
				double len = Math.Sqrt((x1 - x0) * (x1 - x0) + (z1 - z0) * (z1 - z0));
				if (len <= 0.0)
				{
					continue;
				}
				double at = step - carry;
				while (at <= len)
				{
					double t = at / len;
					output.Add((x0 + (x1 - x0) * t, z0 + (z1 - z0) * t));
					at += step;
				}
				carry = len - (at - step);
			}
			(double ex, double ez) = pts[pts.Count - 1];
			(double lx, double lz) = output[output.Count - 1];
			if ((ex - lx) * (ex - lx) + (ez - lz) * (ez - lz) > step * step * 0.25)
			{
				output.Add((ex, ez));
			}
			else
			{
				output[output.Count - 1] = (ex, ez);
			}
			return output;
		}

		/// <summary>Where (x, z) projects onto segment i of a polyline: the fraction t along it and the distance.</summary>
		internal static void Project(double[] xs, double[] zs, int i, double x, double z, out double t, out double d)
		{
			double ax = xs[i], az = zs[i];
			double sx = xs[i + 1] - ax, sz = zs[i + 1] - az;
			double len2 = sx * sx + sz * sz;
			t = len2 > 0.0 ? ((x - ax) * sx + (z - az) * sz) / len2 : 0.0;
			t = t < 0.0 ? 0.0 : (t > 1.0 ? 1.0 : t);
			double px = ax + sx * t - x;
			double pz = az + sz * t - z;
			d = Math.Sqrt(px * px + pz * pz);
		}
	}
}
