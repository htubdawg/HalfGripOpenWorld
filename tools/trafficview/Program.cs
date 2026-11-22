using System;
using System.Collections.Generic;
using System.Diagnostics;
using HalfGripOpenWorld;

// Builds the traffic's map (Traffic.cs: the driving graph, the households and their cars, the places that draw trips) without the game
// and reports what it holds; checks that every household and place is on the main road network and that routes join up (not yet that
// it builds the same twice: Traffic builds once a process). More checks and pictures come with each
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

		int seedAt = Array.IndexOf(args, "--seed");
		int seed = seedAt >= 0 ? int.Parse(args[seedAt + 1]) : 20261122;
		int picAt = Array.IndexOf(args, "--picture");
		ok &= Day(seed, picAt >= 0 ? args[picAt + 1] : null);
		return ok ? 0 : 1;
	}

	/// <summary>Builds race <paramref name="seed"/>'s day of traffic twice and checks it (docs/TRAFFIC_HANDOFF.md, "Checks").</summary>
	private static bool Day(int seed, string picture)
	{
		bool ok = true;
		Stopwatch watch = Stopwatch.StartNew();
		TrafficDay day = new TrafficDay(seed);
		double ms = watch.Elapsed.TotalMilliseconds;
		Console.WriteLine("Day " + seed + ": " + (day.Weekend ? "a weekend day" : "a weekday") + ", built in " + ms.ToString("0") + " ms (plan " + day.PlanMs.ToString("0") + " ms)"
			+ (ms < 2000.0 ? "  ok" : "  SLOW (aim under 2 s)"));
		Console.WriteLine("  tours: " + day.Candidates + " candidates, " + (2.0 * 0 + day.CandidateSeconds / 60.0).ToString("0") + " min of driving; kept with q = " + day.Keep.ToString("0.000")
			+ " for " + (day.TargetSeconds / 60.0).ToString("0") + " min (" + (day.TargetSeconds / Math.Max(1, CountWithCars())).ToString("0") + " s a household with a car)");
		Console.WriteLine("  kept " + day.Kept + " (work " + day.TourCount[0] + ", shop " + day.TourCount[1] + ", errand " + day.TourCount[2] + ", social " + day.TourCount[3] + "), "
			+ day.Dropped + " dropped (no car home by 21:00), " + day.Through + " through journeys, " + day.Unrouted + " unrouted" + (day.Unrouted == 0 ? "  ok" : "  FAIL"));
		ok &= day.Unrouted == 0;
		int ran = 0, never = 0, toEdge = 0;
		double driven = 0.0, freeT = 0.0;
		foreach (TrafficDay.Leg leg in day.Legs)
		{
			if (leg.Start < 0.0)
			{
				never++;
				continue;
			}
			ran++;
			driven += leg.End - leg.Start;
			toEdge += leg.Vanishes && leg.Purpose != Traffic.Purpose.Through ? 1 : 0;
			foreach (TrafficDay.Seg s in leg.Path)
			{
				freeT += s.Len / Math.Max(1.0, s.Speed);
			}
		}
		Console.WriteLine("  legs: " + ran + " driven, " + never + " never started, " + day.Unfinished + " unfinished at the end" + (never == 0 && day.Unfinished == 0 ? "  ok" : "  FAIL")
			+ "; " + toEdge + " local legs out by a road out; " + (driven / 60.0).ToString("0") + " min driven against " + (freeT / 60.0).ToString("0") + " free-flow ("
			+ (driven / Math.Max(1.0, freeT)).ToString("0.00") + "x)");
		ok &= never == 0 && day.Unfinished == 0;
		{
			double[] lenBy = new double[6], nBy = new double[6], edgeN = new double[6];
			foreach (TrafficDay.Leg leg in day.Legs)
			{
				int k = (int)leg.Purpose;
				nBy[k]++;
				lenBy[k] += leg.Length;
				edgeN[k] += leg.Vanishes || leg.Appears ? 1 : 0;
			}
			for (int k = 0; k < 5; k++)
			{
				Console.WriteLine("    " + (Traffic.Purpose)k + ": " + nBy[k] + " legs, mean " + (lenBy[k] / Math.Max(1, nBy[k])).ToString("0") + " units, " + edgeN[k] + " by a road out");
			}
		}
		{
			double all = 0.0;
			List<KeyValuePair<int, double>> waits = new List<KeyValuePair<int, double>>(day.WaitAt);
			foreach (KeyValuePair<int, double> kv in waits)
			{
				all += kv.Value;
			}
			waits.Sort((x, y) => y.Value.CompareTo(x.Value));
			Console.WriteLine("    waiting at junctions: " + (all / 60.0).ToString("0") + " min in all; refused: behind a refused car " + day.RefusedOrder + ", crossing a grant " + day.RefusedCross + ", no room " + day.RefusedRoom);
			for (int i = 0; i < Math.Min(12, waits.Count); i++)
			{
				int n = waits[i].Key;
				string arms = "";
				foreach (int e in Traffic.Links[n])
				{
					arms += " " + Network.Roads[Traffic.Edges[e].Road].Kind + "(" + Traffic.Edges[e].Length.ToString("0") + ")";
				}
				Console.WriteLine("      node " + n + " at (" + Traffic.Nodes[n].X.ToString("0") + ", " + Traffic.Nodes[n].Z.ToString("0") + ") box " + Traffic.Nodes[n].Box.ToString("0") + ": " + (waits[i].Value / 60.0).ToString("0.0") + " min;" + arms);
			}
		}
		Console.WriteLine("    at the profile's speeds the legs would take " + (day.ProfileSeconds / 60.0).ToString("0") + " min");
		{
			// How much road is slowed by its bends, by class.
			foreach (RoadKind kind in new[] { RoadKind.Trunk, RoadKind.Primary, RoadKind.Secondary, RoadKind.Street, RoadKind.Lane })
			{
				double len = 0, slow = 0, t0 = 0, t1 = 0;
				for (int e = 0; e < Traffic.Edges.Count; e++)
				{
					Traffic.Edge ed = Traffic.Edges[e];
					Network.Road road = Network.Roads[ed.Road];
					if (road.Kind != kind) continue;
					double sp = ed.Length / ed.Time;
					for (double s0 = ed.Sa; s0 < ed.Sb; s0 += 10)
					{
						double bs = TrafficDay.BendSpeed(ed.Road, s0);
						len += 10; t0 += 10 / sp; t1 += 10 / Math.Min(sp, bs);
						if (bs < sp) slow += 10;
					}
				}
				Console.WriteLine("      " + kind + ": " + (100 * slow / Math.Max(1, len)).ToString("0") + "% of its length slowed by bends; time x" + (t1 / Math.Max(1e-9, t0)).ToString("0.00"));
			}
		}
		{
			double all = 0.0;
			List<KeyValuePair<int, double>> st = new List<KeyValuePair<int, double>>(day.StoppedOn);
			foreach (KeyValuePair<int, double> kv in st) all += kv.Value;
			st.Sort((x, y) => y.Value.CompareTo(x.Value));
			Console.WriteLine("    stopped (any reason): " + (all / 60.0).ToString("0") + " min");
			for (int i = 0; i < Math.Min(10, st.Count); i++)
			{
				int e = st[i].Key;
				if (e < 0) { Console.WriteLine("      tunnel " + (st[i].Value / 60).ToString("0.0")); continue; }
				Traffic.Edge ed = Traffic.Edges[e];
				Network.Road road = Network.Roads[ed.Road];
				Traffic.At(road, (ed.Sa + ed.Sb) / 2, out double x, out double z, out double _);
				Console.WriteLine("      edge " + e + " " + road.Kind + " '" + road.Name + "' len " + ed.Length.ToString("0") + " at (" + x.ToString("0") + ", " + z.ToString("0") + ") nodes " + ed.A + "/" + ed.B + ": " + (st[i].Value / 60.0).ToString("0.0") + " min");
			}
		}
		{
			List<double> ratio = new List<double>(), dur = new List<double>();
			TrafficDay.Leg worstLeg = null; double worstR = 0;
			foreach (TrafficDay.Leg leg in day.Legs)
			{
				if (leg.Start < 0 || leg.Purpose == Traffic.Purpose.Through) continue;
				double f = 0; foreach (TrafficDay.Seg sg in leg.Path) f += sg.Len / Math.Max(1.0, sg.Speed);
				double r = (leg.End - leg.Start) / Math.Max(1.0, f);
				ratio.Add(r); dur.Add(leg.End - leg.Start);
				if (r > worstR) { worstR = r; worstLeg = leg; }
			}
			ratio.Sort(); dur.Sort();
			Console.WriteLine("    local legs: duration s p10 " + Pct(dur, 0.1).ToString("0") + " p50 " + Pct(dur, 0.5).ToString("0") + " p90 " + Pct(dur, 0.9).ToString("0") + " max " + dur[dur.Count - 1].ToString("0")
				+ "; x free p10 " + Pct(ratio, 0.1).ToString("0.00") + " p50 " + Pct(ratio, 0.5).ToString("0.00") + " p90 " + Pct(ratio, 0.9).ToString("0.00") + " max " + worstR.ToString("0.0"));
			if (worstLeg != null)
			{
				Console.WriteLine("    worst: length " + worstLeg.Length.ToString("0") + " from " + worstLeg.Start.ToString("0") + " to " + worstLeg.End.ToString("0") + " segs " + worstLeg.Path.Length + " rec n " + worstLeg.Rec.Length);
				string trace = "";
				for (int i = 0; i < worstLeg.Rec.Length; i += 40) trace += " " + worstLeg.Rec[i].ToString("0");
				Console.WriteLine("     p every 20 s:" + trace);
			}
		}
		{
			int[,] st = new int[5, 24];
			int[] back = new int[24];
			foreach (TrafficDay.Leg leg in day.Legs)
			{
				if (leg.Start < 0) continue;
				int hr = ((int)Math.Floor((leg.Start % Traffic.DaySeconds) / 100.0) + 3) % 24;
				if (leg.Return && leg.Purpose != Traffic.Purpose.Through) back[hr]++; else st[(int)leg.Purpose, hr]++;
			}
			for (int k = 0; k < 5; k++)
			{
				string l = "";
				for (int h = 0; h < 24; h++) l += st[k, h].ToString().PadLeft(4);
				Console.WriteLine("    out " + ((Traffic.Purpose)k).ToString().PadRight(8) + l);
			}
			string lb = "";
			for (int h = 0; h < 24; h++) lb += back[h].ToString().PadLeft(4);
			Console.WriteLine("    home         " + lb);
		}
		{
			Dictionary<string, int> to = new Dictionary<string, int>();
			foreach (TrafficDay.Leg leg in day.Legs)
			{
				if (leg.Return || leg.Start < 0 || leg.Purpose == Traffic.Purpose.Through) continue;
				string key = leg.Dest >= 0 ? Traffic.Destinations[leg.Dest].Kind + " " + Traffic.Destinations[leg.Dest].Name : "a household";
				to.TryGetValue(key, out int n);
				to[key] = n + 1;
			}
			List<KeyValuePair<string, int>> l = new List<KeyValuePair<string, int>>(to);
			l.Sort((x, y) => y.Value.CompareTo(x.Value));
			string line2 = "";
			for (int i = 0; i < Math.Min(14, l.Count); i++) line2 += l[i].Key + " " + l[i].Value + "; ";
			Console.WriteLine("    tours to: " + line2);
		}
		Console.WriteLine("    on the road each sim hour (03:00 first): " + string.Join(" ", day.ActiveBy));
		Console.WriteLine("    legs started each sim hour:              " + string.Join(" ", day.StartsBy));
		foreach (string st in day.Stuck)
		{
			Console.WriteLine("    stuck: " + st);
		}

		watch.Restart();
		TrafficDay again = new TrafficDay(seed);
		ulong a = day.Checksum(), b = again.Checksum();
		Console.WriteLine("  the same day twice: " + a.ToString("x16") + (a == b ? " both times  ok" : " then " + b.ToString("x16") + "  FAIL"));
		ok &= a == b;
		TrafficDay other = new TrafficDay(seed + 1);
		Console.WriteLine("  another seed's day differs: " + (other.Checksum() != a ? "yes  ok" : "no  FAIL"));
		ok &= other.Checksum() != a;

		// Driving.
		Console.WriteLine("  cars on a lane closer than a car's length: " + day.OverlapPairs + " pairs (" + day.Overlaps + " steps), closest " + day.MinGapSeen.ToString("0.0")
			+ (day.OverlapPairs == 0 ? "  ok" : "  FAIL"));
		ok &= day.OverlapPairs == 0;
		Console.WriteLine("  crossing cars in a junction box together: " + day.ConflictPairs + " pairs (" + day.BoxConflicts + " steps)" + (day.ConflictPairs == 0 ? "  ok" : "  FAIL"));
		ok &= day.ConflictPairs == 0;
		Console.WriteLine("  let go of the queue's order after 45 s: " + day.Forced + " (never into a car in the box, never without room past it)");
		foreach (string n in day.OverlapNotes) Console.WriteLine("    overlap: " + n);
		foreach (string n in day.ConflictNotes) Console.WriteLine("    conflict: " + n);
		Console.WriteLine("  over the limit + 5%: " + day.Speeding + " steps" + (day.Speeding > 0 ? ", worst " + (100.0 * (day.MaxOver - 1.0)).ToString("0") + "% over" : "") + (day.Speeding == 0 ? "  ok" : "  FAIL"));
		ok &= day.Speeding == 0;
		Console.WriteLine("  pull-outs forced after a minute: " + day.PullOutsForced);

		// Counts: vehicles past each edge's middle in a game day x 36, by road class, against PennDOT.
		int[] passes = new int[Traffic.Edges.Count];
		double[] hourly = new double[24];
		double[] hourlyLocal = new double[24];
		int[] passesLocal = new int[Traffic.Edges.Count];
		day.Passes((e, dir, t, leg) =>
		{
			passes[e]++;
			int hour = ((int)Math.Floor((t % Traffic.DaySeconds) / 100.0) + 3) % 24;
			hourly[hour]++;
			if (leg.Purpose != Traffic.Purpose.Through)
			{
				hourlyLocal[hour]++;
				passesLocal[e]++;
			}
		});
		Dictionary<RoadKind, List<double>> byClass = new Dictionary<RoadKind, List<double>>();
		for (int e = 0; e < Traffic.Edges.Count; e++)
		{
			if (!Traffic.OnMain(e) || Traffic.Edges[e].Length < 60.0)
			{
				continue;
			}
			RoadKind kind = Network.Roads[Traffic.Edges[e].Road].Kind;
			if (!byClass.TryGetValue(kind, out List<double> list))
			{
				list = new List<double>();
				byClass[kind] = list;
			}
			list.Add(passes[e] * 36.0);
		}
		Console.WriteLine("  AADT-equivalent (passes a game day x 36), by road class: median, 10th to 90th percentile (PennDOT for comparison)");
		foreach (RoadKind kind in new[] { RoadKind.Trunk, RoadKind.Primary, RoadKind.Secondary, RoadKind.Street, RoadKind.Lane, RoadKind.Drive })
		{
			if (!byClass.TryGetValue(kind, out List<double> list))
			{
				continue;
			}
			list.Sort();
			string penn = kind == RoadKind.Trunk ? "principal arterial: 2,600 to 21,600, median about 5,400" : kind == RoadKind.Primary ? "minor arterial: 2,000 to 15,700, about 5,000"
				: kind == RoadKind.Secondary ? "collector: 400 to 8,800, about 1,800" : kind == RoadKind.Street ? "urban local: not counted (a few hundred to 1,500)" : kind == RoadKind.Lane ? "local: a few hundred" : "a drive: a house's 9 or so";
			Console.WriteLine("    " + kind.ToString().PadRight(9) + Pct(list, 0.5).ToString("0").PadLeft(7) + Pct(list, 0.1).ToString("0").PadLeft(7) + " to " + Pct(list, 0.9).ToString("0").PadRight(7)
				+ list.Count.ToString().PadLeft(5) + " edges   " + penn);
		}
		// The hours' shape against TPG 9.
		double total = 0.0, worst = 0.0;
		foreach (double v in hourly)
		{
			total += v;
		}
		string line = "";
		double[] shape = day.Weekend ? TrafficDay.HoursSaturday : TrafficDay.HoursCounted;
		for (int h = 0; h < 24; h++)
		{
			double share = 100.0 * hourly[h] / Math.Max(1.0, total);
			worst = Math.Max(worst, Math.Abs(share - shape[h]));
			line += share.ToString("0.0").PadLeft(5);
		}
		Console.WriteLine("  passes by hour, % from midnight:\n   " + line);
		line = "";
		for (int h = 0; h < 24; h++)
		{
			line += shape[h].ToString("0.0").PadLeft(5);
		}
		Console.WriteLine("   " + line + (day.Weekend ? "  (Saturday, NHTS)" : "  (TPG 9)"));
		{
			double tl = 0.0;
			foreach (double v in hourlyLocal) tl += v;
			string ll = "";
			for (int h = 0; h < 24; h++) ll += (100.0 * hourlyLocal[h] / Math.Max(1.0, tl)).ToString("0.0").PadLeft(5);
			Console.WriteLine("   " + ll + "  (local only; " + (100.0 * tl / Math.Max(1.0, total)).ToString("0") + "% of passes)");
			List<double> trunkLocal = new List<double>();
			for (int e = 0; e < Traffic.Edges.Count; e++)
			{
				if (Network.Roads[Traffic.Edges[e].Road].Kind == RoadKind.Trunk && Traffic.Edges[e].Length >= 60.0) trunkLocal.Add(passesLocal[e] * 36.0);
			}
			trunkLocal.Sort();
			Console.WriteLine("   trunk AADT-equivalent from local traffic alone: median " + Pct(trunkLocal, 0.5).ToString("0"));
			double trunkLen = 0, throughTrunk = 0, throughAll = 0;
			foreach (TrafficDay.Leg leg in day.Legs)
			{
				if (leg.Purpose != Traffic.Purpose.Through) continue;
				foreach (TrafficDay.Seg sg in leg.Path)
				{
					throughAll += sg.Len;
					if (sg.Edge >= 0 && Network.Roads[Traffic.Edges[sg.Edge].Road].Kind == RoadKind.Trunk) throughTrunk += sg.Len;
				}
			}
			for (int e = 0; e < Traffic.Edges.Count; e++) if (Network.Roads[Traffic.Edges[e].Road].Kind == RoadKind.Trunk) trunkLen += Traffic.Edges[e].Length;
			Console.WriteLine("   through journeys: " + (throughAll / Math.Max(1, day.Through)).ToString("0") + " units each, " + (throughTrunk / Math.Max(1, day.Through)).ToString("0") + " of them on the trunk (" + trunkLen.ToString("0") + " of trunk in all)");
		}
		Console.WriteLine("  the hours' shape: off " + (day.Weekend ? "a Saturday's" : "TPG 9") + " by at most " + worst.ToString("0.0") + " points an hour" + (worst < 3.5 ? "  ok" : "  FAIL"));
		// 3.5 points: a drive here spans an hour or more of the world's clock (it runs 36 times fast, driving does not), which smears
		// every hour into the next; the tables are tuned against it, but no table can undo it entirely.
		ok &= worst < 3.5;
		// Cars moving.
		// Expected: households with a car x their share of driving x the hour's share of the day (TPG 9), over the hour's 100 s.
		double perHour = CountWithCars() * Traffic.DrivingShare * Traffic.DaySeconds / 100.0;
		foreach ((double hour, string what) in new[] { (17.0, "the 5 pm peak, about " + (perHour * 0.0877).ToString("0") + " expected"), (3.0, "3 am, about " + (perHour * 0.0048).ToString("0")), (8.0, "8 am, about " + (perHour * 0.0545).ToString("0")), (12.0, "noon, about " + (perHour * 0.0642).ToString("0")) })
		{
			int moving = 0;
			double w = TrafficDay.SimOfHour(hour + 0.5);
			for (int c = 0; c < day.Cars.Count; c++)
			{
				moving += day.At(c, w).State == TrafficDay.State.Driving ? 1 : 0;
			}
			Console.WriteLine("  cars moving at " + hour.ToString("00") + ":30: " + moving + " (" + what + ")");
		}
		// Lots: each place's peak of cars parked against its spaces.
		int over = 0, worstOver = 0;
		string worstName = "";
		for (int d = 0; d < Traffic.Destinations.Count; d++)
		{
			Traffic.Destination dest = Traffic.Destinations[d];
			if (dest.Spaces == 0)
			{
				continue;
			}
			List<(double t, int k)> events = new List<(double t, int k)>();
			foreach (TrafficDay.Car car in day.Cars)
			{
				for (int i = 0; i + 1 < car.Legs.Count; i++)
				{
					TrafficDay.Leg l0 = day.Legs[car.Legs[i]], l1 = day.Legs[car.Legs[i + 1]];
					if (!l0.Return && l0.Dest == d && !l0.Vanishes && l0.End >= 0.0 && l1.Start >= 0.0)
					{
						events.Add((l0.End, 1));
						events.Add((l1.Start, -1));
					}
				}
			}
			events.Sort((x, y) => x.t != y.t ? x.t.CompareTo(y.t) : x.k.CompareTo(y.k));
			int now = 0, peak = 0;
			foreach ((double t, int k) in events)
			{
				now += k;
				peak = Math.Max(peak, now);
			}
			if (peak > dest.Spaces)
			{
				over++;
				if (peak - dest.Spaces > worstOver)
				{
					worstOver = peak - dest.Spaces;
					worstName = dest.Kind + " in " + dest.Name + " (" + peak + " cars, " + dest.Spaces + " spaces)";
				}
			}
		}
		Console.WriteLine("  places whose cars outgrow their spaces at the peak: " + over + (over > 0 ? "; worst " + worstName : "") + "  (reported, not failed)");
		// The tunnels: nobody stopped at a mouth; every car into a trunk tunnel out of the other at the same moment, as Wrap.Move says.
		int jumps = 0, wrongJumps = 0;
		double worstJump = 0.0;
		foreach (TrafficDay.Leg leg in day.Legs)
		{
			for (int k = 0; k + 1 < leg.Path.Length; k++)
			{
				TrafficDay.Seg s0 = leg.Path[k], s1 = leg.Path[k + 1];
				if (s0.Edge >= 0 || s1.Edge >= 0 || s0.Inbound || !s1.Inbound)
				{
					continue;
				}
				jumps++;
				if (!Wrap.Open(s0.Portal) || Wrap.Other(s0.Portal) != s1.Portal)
				{
					wrongJumps++;
					continue;
				}
				TrafficDay.Place p0 = default, p1 = default;
				TrafficDay.OnPath(leg, s1.P0 - 1e-6, ref p0);
				TrafficDay.OnPath(leg, s1.P0, ref p1);
				Wrap.Move(s0.Portal, out double dx, out double dz, out double _);
				worstJump = Math.Max(worstJump, Math.Sqrt((p0.X + dx - p1.X) * (p0.X + dx - p1.X) + (p0.Z + dz - p1.Z) * (p0.Z + dz - p1.Z)));
			}
		}
		Console.WriteLine("  the wrap: " + jumps + " crossings, " + wrongJumps + " not between the trunk's tunnels, worst miss of Wrap.Move " + worstJump.ToString("0.0") + " units"
			+ (wrongJumps == 0 && worstJump < 3.0 ? "  ok" : "  FAIL"));
		ok &= wrongJumps == 0 && worstJump < 3.0;
		Console.WriteLine("  cars stopped in a tunnel's mouth or within 60 of it: " + day.StoppedAtMouth + " steps" + (day.StoppedAtMouth == 0 ? "  ok" : "  FAIL"));
		ok &= day.StoppedAtMouth == 0;
		if (picture != null)
		{
			Pictures.Draw(day, passes, picture);
		}
		return ok;
	}

	private static int CountWithCars()
	{
		int n = 0;
		foreach (Traffic.Household h in Traffic.Households)
		{
			n += h.Cars > 0 ? 1 : 0;
		}
		return n;
	}

	private static double Pct(List<double> sorted, double q)
	{
		return sorted.Count == 0 ? 0.0 : sorted[Math.Min(sorted.Count - 1, (int)Math.Floor(q * sorted.Count))];
	}
}
