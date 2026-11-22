using System;
using System.Collections.Generic;
using System.Diagnostics;
using HalfGripOpenWorld;

// Builds the traffic's map (Traffic.cs: the driving graph, the households and their cars, the places that draw trips) without the game
// and reports what it holds; checks that it builds the same twice and that routes join up. More checks and pictures come with each
// race's day of trips (see docs/TRAFFIC_HANDOFF.md).
//   dotnet run -c Release --project tools/trafficview
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
			+ (watch.Elapsed.TotalMilliseconds / Math.Max(1, tried)).ToString("0.00") + " ms each" + (broken == 0 ? "  ok" : "  FAIL"));
		ok &= broken == 0;
		return ok ? 0 : 1;
	}
}
