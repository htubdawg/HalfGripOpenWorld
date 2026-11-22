using System;
using System.Collections.Generic;
using System.Diagnostics;
using HalfGripOpenWorld;

// Builds the traffic's map (Traffic.cs: the driving graph, the households and their cars, the places that draw trips) without the game
// and reports what it holds; checks that it builds the same twice and that routes join up. More checks and pictures come with each
// race's day of trips (see docs/TRAFFIC_HANDOFF.md). Exits 1 if a check fails.
//   dotnet run -c Release --project tools/trafficview [--near <road>]
// --near lists the roads that come within 30 of a road (gap, heights, driven or not) and the graph's edges along it: for finding why a
// road is cut off.
internal static class Program
{
	private static int Main(string[] args)
	{
		bool ok = true;
		Stopwatch watch = Stopwatch.StartNew();
		Network.Ensure();
		Console.WriteLine("Layout: " + watch.ElapsedMilliseconds + " ms");
		Traffic.Ensure();
		Console.WriteLine("Traffic map: " + Traffic.Report);

		int[] kinds = new int[4];
		int[] cars = new int[4];
		int workers = 0;
		foreach (Traffic.Household h in Traffic.Households)
		{
			kinds[(int)h.Kind]++;
			cars[(int)h.Kind] += h.Cars;
			workers += h.Workers;
		}
		for (int k = 0; k < 4; k++)
		{
			Console.WriteLine("  " + ((Traffic.HomeKind)k).ToString().PadRight(8) + kinds[k].ToString().PadLeft(6) + " households " + cars[k].ToString().PadLeft(6) + " cars");
		}
		Console.WriteLine("  workers " + workers + " (" + (workers / (double)Math.Max(1, Traffic.Households.Count)).ToString("0.00") + " a household; NHTS 1.33)");
		Dictionary<Traffic.DestinationKind, int> byKind = new Dictionary<Traffic.DestinationKind, int>();
		int spaces = 0;
		foreach (Traffic.Destination d in Traffic.Destinations)
		{
			byKind.TryGetValue(d.Kind, out int n);
			byKind[d.Kind] = n + 1;
			spaces += d.Spaces;
		}
		foreach (KeyValuePair<Traffic.DestinationKind, int> kv in byKind)
		{
			Console.WriteLine("  " + kv.Key.ToString().PadRight(11) + kv.Value.ToString().PadLeft(5));
		}
		Console.WriteLine("  parking spaces at the places (not homes): " + spaces);

		// The graph's parts: every household and place on the main one (Traffic leaves out those cut off), and what the others are.
		int parts = 0;
		foreach (int p in Traffic.Part)
		{
			parts = Math.Max(parts, p + 1);
		}
		double[] partLength = new double[parts];
		int[] partEdge = new int[parts];
		for (int p = 0; p < parts; p++)
		{
			partEdge[p] = -1;
		}
		for (int e = 0; e < Traffic.Edges.Count; e++)
		{
			int p = Traffic.Part[Traffic.Edges[e].A];
			partLength[p] += Traffic.Edges[e].Length;
			if (partEdge[p] < 0)
			{
				partEdge[p] = e;
			}
		}
		int offMain = 0;
		foreach (Traffic.Household h in Traffic.Households)
		{
			offMain += Traffic.OnMain(h.Home.Edge) ? 0 : 1;
		}
		foreach (Traffic.Destination d in Traffic.Destinations)
		{
			offMain += Traffic.OnMain(d.At.Edge) ? 0 : 1;
		}
		double cutLength = 0.0;
		for (int p = 0; p < parts; p++)
		{
			cutLength += p == Traffic.MainPart ? 0.0 : partLength[p];
		}
		Console.WriteLine("Graph: " + parts + " parts; the main one " + partLength[Traffic.MainPart].ToString("0") + " units of road, the others " + cutLength.ToString("0")
			+ "; " + Traffic.CutOff + " households and places left out on them, " + offMain + " kept off the main part" + (offMain == 0 ? "  ok" : "  FAIL"));
		ok &= offMain == 0;
		for (int p = 0; p < parts; p++)
		{
			if (p == Traffic.MainPart || partEdge[p] < 0)
			{
				continue;
			}
			Traffic.Edge e = Traffic.Edges[partEdge[p]];
			Network.Road road = Network.Roads[e.Road];
			Traffic.At(road, (e.Sa + e.Sb) * 0.5, out double x, out double z, out double _);
			Console.WriteLine("  cut off: " + partLength[p].ToString("0").PadLeft(5) + " units at (" + x.ToString("0") + ", " + z.ToString("0") + "), " + road.Kind + " '" + road.Name + "' (road " + e.Road + ")");
		}
		// Every road leaving the map is a place (work, shops and friends beyond it, through traffic's ends).
		int edges = byKind.TryGetValue(Traffic.DestinationKind.Edge, out int en) ? en : 0;
		Console.WriteLine("Roads out: " + Network.Portals.Count + " leaving the map, " + edges + " places at them" + (edges == Network.Portals.Count ? "  ok" : "  FAIL"));
		ok &= edges == Network.Portals.Count;

		int nearAt = Array.IndexOf(args, "--near");
		if (nearAt >= 0)
		{
			// --near <road>: the roads (any kind) that come within 30 of road r: closest approach, the s on each, heights, and the graph's stops on r.
			int r = int.Parse(args[nearAt + 1]);
			Network.Road a = Network.Roads[r];
			Console.WriteLine("r" + r + " " + a.Kind + " half " + a.Half + " length " + a.S[a.Count - 1].ToString("0") + " from " + a.X[0].ToString("0") + "," + a.Z[0].ToString("0") + " y " + a.Y[0].ToString("0.0")
				+ " to " + a.X[a.Count - 1].ToString("0") + "," + a.Z[a.Count - 1].ToString("0") + " y " + a.Y[a.Count - 1].ToString("0.0"));
			foreach (int e in Traffic.RoadEdges[r])
			{
				Console.WriteLine("  edge " + e + " s " + Traffic.Edges[e].Sa.ToString("0") + "-" + Traffic.Edges[e].Sb.ToString("0") + " nodes " + Traffic.Edges[e].A + "(" + Traffic.Links[Traffic.Edges[e].A].Count + ") " + Traffic.Edges[e].B + "(" + Traffic.Links[Traffic.Edges[e].B].Count + ")");
			}
			for (int q = 0; q < Network.Roads.Count; q++)
			{
				Network.Road b = Network.Roads[q];
				if (q == r || b.Count < 2)
				{
					continue;
				}
				double best = 1e9, sa = 0, sb = 0, ya = 0, yb = 0;
				for (int i = 0; i < a.Count; i++)
				{
					for (int j = 0; j + 1 < b.Count; j++)
					{
						Lines.Project(b.X, b.Z, j, a.X[i], a.Z[i], out double t, out double d);
						if (d < best)
						{
							best = d;
							sa = a.S[i];
							ya = a.Y[i];
							sb = b.S[j] + (b.S[j + 1] - b.S[j]) * t;
							yb = b.Y[j] + (b.Y[j + 1] - b.Y[j]) * t;
						}
					}
				}
				for (int j = 0; j < b.Count; j++)
				{
					for (int i = 0; i + 1 < a.Count; i++)
					{
						Lines.Project(a.X, a.Z, i, b.X[j], b.Z[j], out double t, out double d);
						if (d < best)
						{
							best = d;
							sb = b.S[j];
							yb = b.Y[j];
							sa = a.S[i] + (a.S[i + 1] - a.S[i]) * t;
							ya = a.Y[i] + (a.Y[i + 1] - a.Y[i]) * t;
						}
					}
				}
				if (best < 30.0)
				{
					Console.WriteLine("  r" + q + " " + b.Kind + " half " + b.Half + " gap " + best.ToString("0.0") + " at s " + sa.ToString("0") + " (y " + ya.ToString("0.0") + ") / s " + sb.ToString("0") + " of " + b.S[b.Count - 1].ToString("0") + " (y " + yb.ToString("0.0") + ")"
						+ " driven " + Traffic.Driven(b) + " edges " + Traffic.RoadEdges[q].Count);
				}
			}
		}

		// Routes: from each household to a spread of places, every one found and joined end to end.
		Heap heap = new Heap();
		double[] cost = new double[Traffic.Nodes.Count];
		int[] via = new int[Traffic.Nodes.Count];
		for (int i = 0; i < cost.Length; i++)
		{
			cost[i] = double.MaxValue;
			via[i] = -1;
		}
		List<int> touched = new List<int>();
		List<(int edge, int dir)> route = new List<(int edge, int dir)>();
		int tried = 0, found = 0, broken = 0;
		watch.Restart();
		for (int h = 0; h < Traffic.Households.Count; h += 7)
		{
			for (int d = h % 11; d < Traffic.Destinations.Count; d += 37)
			{
				tried++;
				if (!Traffic.Route(Traffic.Households[h].Home, Traffic.Destinations[d].At, route, heap, cost, via, touched))
				{
					continue;
				}
				found++;
				for (int k = 0; k + 1 < route.Count; k++)
				{
					Traffic.Edge a = Traffic.Edges[route[k].edge], b = Traffic.Edges[route[k + 1].edge];
					int out_ = route[k].dir > 0 ? a.B : a.A, in_ = route[k + 1].dir > 0 ? b.A : b.B;
					if (out_ != in_)
					{
						broken++;
						break;
					}
				}
			}
		}
		Console.WriteLine("Routes: " + found + " of " + tried + " found (" + (100.0 * found / Math.Max(1, tried)).ToString("0.0") + "%), " + broken + " broken, "
			+ (watch.Elapsed.TotalMilliseconds / Math.Max(1, tried)).ToString("0.00") + " ms each" + (broken == 0 && found == tried ? "  ok" : "  FAIL"));
		ok &= broken == 0 && found == tried;
		return ok ? 0 : 1;
	}
}
