using System;
using System.Collections.Generic;
using HalfGripOpenWorld;

/// <summary>
/// How well the country's roads (drives, lanes and bigger) join up, as a driver finds it: a graph of every road sample, joined along
/// each road and to any other road's sample within a junction's reach, then for pairs of hamlets and farms 1 to 4 km (game units)
/// apart, the way by road over the straight line (the detour), and for the farmland round them, how far it is to the nearest road.
/// Built here from the finished roads, not from the generator's own bookkeeping, so it checks what is drawn.
/// </summary>
internal static class Connectivity
{
	private const double Cell = 16.0;
	private const double Reach = 12.0;
	private const double Coarse = 200.0;

	internal sealed class Result
	{
		public int Pairs;
		public int Unreachable;
		public double Median;
		public double P90;
		public int OverTwo;
		public double GapP90;
		public double GapMost;
		public double GapMostX, GapMostZ;
	}

	internal static Result Measure()
	{
		List<double> xs = new List<double>(), zs = new List<double>();
		List<int> owner = new List<int>();
		List<List<(int to, double w)>> adj = new List<List<(int, double)>>();
		Dictionary<long, List<int>> grid = new Dictionary<long, List<int>>();
		for (int r = 0; r < Network.Roads.Count; r++)
		{
			Network.Road road = Network.Roads[r];
			if (road.Kind > RoadKind.Drive)
			{
				continue;
			}
			int first = xs.Count;
			for (int i = 0; i < road.Count; i++)
			{
				int n = xs.Count;
				xs.Add(road.X[i]);
				zs.Add(road.Z[i]);
				owner.Add(r);
				adj.Add(new List<(int, double)>());
				if (i > 0)
				{
					double w = road.S[i] - road.S[i - 1];
					adj[n].Add((n - 1, w));
					adj[n - 1].Add((n, w));
				}
				long key = Key(road.X[i], road.Z[i]);
				if (!grid.TryGetValue(key, out List<int> list))
				{
					grid[key] = list = new List<int>();
				}
				list.Add(n);
			}
			if (road.Loop && road.Count > 1)
			{
				adj[first].Add((xs.Count - 1, 0.0));
				adj[xs.Count - 1].Add((first, 0.0));
			}
		}
		// Junctions: samples of different roads within reach of each other.
		for (int n = 0; n < xs.Count; n++)
		{
			foreach (int m in Near(grid, xs[n], zs[n], Reach))
			{
				if (m > n && owner[m] != owner[n])
				{
					double d = Math.Sqrt((xs[m] - xs[n]) * (xs[m] - xs[n]) + (zs[m] - zs[n]) * (zs[m] - zs[n]));
					if (d <= Reach)
					{
						adj[n].Add((m, d));
						adj[m].Add((n, d));
					}
				}
			}
		}

		Result res = new Result();
		List<Places.Place> hamlets = Places.All.FindAll(p => p.Kind == PlaceKind.Hamlet);
		int[] anchor = new int[hamlets.Count];
		for (int h = 0; h < hamlets.Count; h++)
		{
			anchor[h] = -1;
			double best = 400.0;
			foreach (int m in Near(grid, hamlets[h].X, hamlets[h].Z, 400.0))
			{
				double d = Math.Sqrt((xs[m] - hamlets[h].X) * (xs[m] - hamlets[h].X) + (zs[m] - hamlets[h].Z) * (zs[m] - hamlets[h].Z));
				if (d < best)
				{
					best = d;
					anchor[h] = m;
				}
			}
		}
		List<double> ratios = new List<double>();
		double[] dist = new double[xs.Count];
		for (int a = 0; a < hamlets.Count; a++)
		{
			List<int> partners = new List<int>();
			for (int b = a + 1; b < hamlets.Count; b++)
			{
				double s = Straight(hamlets[a], hamlets[b]);
				if (s >= 1000.0 && s <= 4000.0)
				{
					partners.Add(b);
				}
			}
			if (partners.Count == 0)
			{
				continue;
			}
			if (anchor[a] < 0)
			{
				res.Pairs += partners.Count;
				res.Unreachable += partners.Count;
				continue;
			}
			Dijkstra(adj, anchor[a], 30000.0, dist);
			foreach (int b in partners)
			{
				res.Pairs++;
				double s = Straight(hamlets[a], hamlets[b]);
				if (anchor[b] < 0 || dist[anchor[b]] == double.MaxValue)
				{
					res.Unreachable++;
					continue;
				}
				ratios.Add(dist[anchor[b]] / s);
			}
		}
		ratios.Sort();
		if (ratios.Count > 0)
		{
			res.Median = ratios[ratios.Count / 2];
			res.P90 = ratios[ratios.Count * 9 / 10];
			res.OverTwo = ratios.FindAll(v => v > 2.0).Count;
		}

		// The farmland's distance to a road: points every 100 within 2 km of a hamlet, on dry land (a coarse grid of the samples).
		Dictionary<long, List<int>> coarse = new Dictionary<long, List<int>>();
		for (int n = 0; n < xs.Count; n++)
		{
			long key = ((long)Math.Floor(xs[n] / Coarse) << 32) ^ (uint)(int)Math.Floor(zs[n] / Coarse);
			if (!coarse.TryGetValue(key, out List<int> list))
			{
				coarse[key] = list = new List<int>();
			}
			list.Add(n);
		}
		List<double> gaps = new List<double>();
		for (double z = -WorldGen.Half; z < WorldGen.Half; z += 100.0)
		{
			for (double x = -WorldGen.Half; x < WorldGen.Half; x += 100.0)
			{
				bool near = false;
				foreach (Places.Place p in hamlets)
				{
					near |= (p.X - x) * (p.X - x) + (p.Z - z) * (p.Z - z) < 2000.0 * 2000.0;
				}
				if (!near || WorldGen.Base(x, z) < WorldGen.SeaLevel || !double.IsNaN(Rivers.SurfaceAt(x, z)))
				{
					continue;
				}
				double best = 3000.0;
				for (double rad = 200.0; rad <= 3000.0 && best >= rad - 200.0; rad += 400.0)
				{
					foreach (int m in Near(coarse, x, z, rad, Coarse))
					{
						best = Math.Min(best, Math.Sqrt((xs[m] - x) * (xs[m] - x) + (zs[m] - z) * (zs[m] - z)));
					}
				}
				gaps.Add(best);
				if (best > res.GapMost)
				{
					res.GapMost = best;
					res.GapMostX = x;
					res.GapMostZ = z;
				}
			}
		}
		gaps.Sort();
		res.GapP90 = gaps.Count > 0 ? gaps[gaps.Count * 9 / 10] : 0.0;
		return res;
	}

	private static double Straight(Places.Place a, Places.Place b)
	{
		return Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Z - b.Z) * (a.Z - b.Z));
	}

	private static void Dijkstra(List<List<(int to, double w)>> adj, int start, double most, double[] dist)
	{
		for (int k = 0; k < dist.Length; k++)
		{
			dist[k] = double.MaxValue;
		}
		Heap open = new Heap();
		dist[start] = 0.0;
		open.Push(0.0, start);
		while (open.Count > 0)
		{
			int cur = open.Pop(out double d);
			if (d > dist[cur] || d > most)
			{
				continue;
			}
			foreach ((int to, double w) in adj[cur])
			{
				if (d + w < dist[to])
				{
					dist[to] = d + w;
					open.Push(d + w, to);
				}
			}
		}
	}

	private static long Key(double x, double z)
	{
		return ((long)Math.Floor(x / Cell) << 32) ^ (uint)(int)Math.Floor(z / Cell);
	}

	private static IEnumerable<int> Near(Dictionary<long, List<int>> grid, double x, double z, double radius, double cell = Cell)
	{
		int i0 = (int)Math.Floor((x - radius) / cell), i1 = (int)Math.Floor((x + radius) / cell);
		int j0 = (int)Math.Floor((z - radius) / cell), j1 = (int)Math.Floor((z + radius) / cell);
		for (int i = i0; i <= i1; i++)
		{
			for (int j = j0; j <= j1; j++)
			{
				if (grid.TryGetValue(((long)i << 32) ^ (uint)j, out List<int> list))
				{
					foreach (int m in list)
					{
						yield return m;
					}
				}
			}
		}
	}
}
