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
// road is cut off. With TV_MEET set in the environment, every meeting of two moving cars as drawn is printed (cars, edges, way, road
// distance, speed, place on the leg and the leg's times): for finding why they meet.
internal static partial class Program
{
	/// <summary>Cars with the right of way refused for a car giving way to them, a day, at most.</summary>
	private const int MostMainHeld = 100;

	/// <summary>
	/// Every junction's stop lines: a car waiting at the box's edge on any arm (its front there, in its lane) is off every other road's
	/// paving. A box sized for roads meeting square left cars waiting at a slanted trunk junction standing in its lanes (the owner's
	/// screenshot, 0.33.0).
	/// </summary>
	private static bool StopLines()
	{
		int arms = 0, bad = 0;
		string first = "";
		for (int n = 0; n < Traffic.Nodes.Count; n++)
		{
			List<int> links = Traffic.Links[n];
			if (links.Count < 3)
			{
				continue;
			}
			Traffic.Node node = Traffic.Nodes[n];
			foreach (int ei in links)
			{
				Traffic.Edge e = Traffic.Edges[ei];
				if (e.Length < node.Box + 2.0)
				{
					continue;
				}
				Network.Road road = Network.Roads[e.Road];
				// Coming in along this arm: its stop line, the lane on the right of the way in.
				double s = e.A == n ? e.Sa + node.Box : e.Sb - node.Box;
				Traffic.At(road, s, out double x, out double z, out double _);
				Traffic.Heading(road, s, out double hx, out double hz);
				double sign = e.A == n ? -1.0 : 1.0;
				double off = Traffic.LaneOffset(road);
				x += hz * sign * off;
				z -= hx * sign * off;
				arms++;
				if (!Traffic.ClearOfOthers(n, e.Road, x, z))
				{
					bad++;
					if (Environment.GetEnvironmentVariable("TV_DRIVE_NOTES") != null)
					{
						double d = node.Box;
						while (d < e.Length - 1.0 && d < 400.0)
						{
							double sd = e.A == n ? e.Sa + d : e.Sb - d;
							Traffic.At(road, sd, out double xd, out double zd, out double _);
							Traffic.Heading(road, sd, out double hxd, out double hzd);
							if (Traffic.ClearOfOthers(n, e.Road, xd + hzd * sign * off, zd - hxd * sign * off))
							{
								break;
							}
							d += 1.0;
						}
						string others = "";
						foreach (int ej in links)
						{
							others += Network.Roads[Traffic.Edges[ej].Road].Kind + " ";
						}
						Console.WriteLine("    stop line: node " + n + " at (" + node.X.ToString("0") + ", " + node.Z.ToString("0") + ") " + road.Kind + " arm, box " + node.Box.ToString("0") + ", clear at " + d.ToString("0") + " of " + e.Length.ToString("0") + "; arms " + others);
					}
					if (first.Length == 0)
					{
						first = " (first: node " + n + " at (" + node.X.ToString("0") + ", " + node.Z.ToString("0") + "), a " + road.Kind + " arm, box " + node.Box.ToString("0") + ")";
					}
				}
			}
		}
		Console.WriteLine("  stop lines on another road's paving: " + bad + " of " + arms + " arms" + first + (bad == 0 ? "  ok" : "  FAIL"));
		return bad == 0;
	}

	/// <summary>
	/// The longest a car of the day stands in one place on a trip (past pulling out, short of pulling in), and how many stand over 5 and
	/// 10 minutes of sim time: a car on Kingsbridge street stood at the link from 17:40 to 02:30 (seed 9), the link jammed back from the
	/// trunk. Reported, not failed (the day's queues are its junction waiting, above); the worst is named.
	/// </summary>
	private static void LongStands(TrafficDay day)
	{
		double worst = 0.0, worstAt = 0.0;
		int worstLeg = -1, over300 = 0, over600 = 0;
		double wx = 0.0, wz = 0.0;
		for (int li = 0; li < day.Legs.Count; li++)
		{
			TrafficDay.Leg l = day.Legs[li];
			if (l.Start < 0.0 || l.End <= l.Start)
			{
				continue;
			}
			double t0 = l.Start + (l.Appears ? 0.0 : l.PullOut + 1.0), t1 = l.End - (l.Vanishes ? 0.0 : l.PullIn + 1.0);
			double since = double.NaN, lastP = double.NaN, most = 0.0, mostAt = 0.0;
			double mx = 0.0, mz = 0.0;
			for (double t = t0; t < t1; t += 5.0)
			{
				TrafficDay.Place p = day.Driving(li, t);
				if (p.InTunnel || double.IsNaN(lastP) || Math.Abs(p.P - lastP) > 0.5)
				{
					since = t;
					lastP = p.P;
					continue;
				}
				if (t - since > most)
				{
					most = t - since;
					mostAt = since;
					mx = p.X;
					mz = p.Z;
				}
			}
			over300 += most > 300.0 ? 1 : 0;
			over600 += most > 600.0 ? 1 : 0;
			if (most > worst)
			{
				worst = most;
				worstAt = mostAt;
				worstLeg = li;
				wx = mx;
				wz = mz;
			}
		}
		Console.WriteLine("  cars standing in one place on a trip: the longest " + worst.ToString("0") + " s" + (worstLeg >= 0 ? " (leg " + worstLeg + " from " + (3.0 + worstAt / 100.0).ToString("0.00") + " h at " + wx.ToString("0") + ", " + wz.ToString("0") + ")" : "")
			+ ", " + over300 + " trips with a stand over 300 s, " + over600 + " over 600 s  (reported)");
	}

	private static int Main(string[] args)
	{
		bool ok = true;
		// TV_FCFS=1: every junction first come first served, as before 0.33.0, to compare the right of way with.
		TrafficDay.NoRightOfWay = Environment.GetEnvironmentVariable("TV_FCFS") == "1";
		// TV_TURNS=1: cars turning off the main line give way to oncoming main-line cars too.
		TrafficDay.MainTurnsGiveWay = Environment.GetEnvironmentVariable("TV_TURNS") == "1";
		// TV_CIRCLING=N: N cars circling the trunk instead of the day's number (to measure more highway traffic).
		TrafficDay.CirclingTry = int.TryParse(Environment.GetEnvironmentVariable("TV_CIRCLING"), out int circling) ? circling : 0;
		// The cars at rest on their springs first: no map needed.
		ok &= Rest();
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
		int closeAt = Array.IndexOf(args, "--closeup");
		string[] closeup = null;
		if (closeAt >= 0)
		{
			List<string> rest = new List<string>();
			for (int k = closeAt + 1; k < args.Length && !args[k].StartsWith("--"); k++)
			{
				rest.Add(args[k]);
			}
			closeup = rest.ToArray();
		}
		ok &= Day(seed, picAt >= 0 ? args[picAt + 1] : null, closeup);
		return ok ? 0 : 1;
	}

	/// <summary>Builds race <paramref name="seed"/>'s day of traffic twice and checks it (docs/TRAFFIC_HANDOFF.md, "Checks").</summary>
	private static bool Day(int seed, string picture, string[] closeup)
	{
		bool ok = true;
		Stopwatch watch = Stopwatch.StartNew();
		TrafficDay day = new TrafficDay(seed);
		double ms = watch.Elapsed.TotalMilliseconds;
		Console.WriteLine("Day " + seed + ": " + (day.Weekend ? "a weekend day" : "a weekday") + ", built in " + ms.ToString("0") + " ms (plan " + day.PlanMs.ToString("0") + " ms, homes' parking " + day.ParkingMs.ToString("0") + " ms)"
			+ " (the first in a process: it makes the indexes kept for later days)");
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
			Console.WriteLine("    waiting at junctions: " + (all / 60.0).ToString("0") + " min in all; refused: behind a refused car " + day.RefusedOrder + ", crossing a grant " + day.RefusedCross + ", no room " + day.RefusedRoom + ", at a red light " + day.RefusedSignal + " (" + TrafficSignals.Nodes.Count + " junctions with lights, " + day.Lights.Changes + " changes)");
			for (int i = 0; i < Math.Min(12, waits.Count); i++)
			{
				int n = waits[i].Key;
				string arms = "";
				foreach (int e in Traffic.Links[n])
				{
					arms += " " + Network.Roads[Traffic.Edges[e].Road].Kind + "(" + Traffic.Edges[e].Length.ToString("0") + ")";
				}
				string kinds = "";
				for (int c = 0; c < 4; c++)
				{
					day.WaitKind.TryGetValue(n * 4 + c, out double w);
					kinds += (c == 0 ? " side " : c == 1 ? ", main on " : c == 2 ? ", main off " : ", none ") + (w / 60.0).ToString("0.0");
				}
				TrafficDay.MainArms(n, out int ma, out int mb);
				arms += " (main line arms " + ma + ", " + mb + ";" + kinds + ")";
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
			{
				double trunk = 0.0;
				string trunkWorst = "";
				foreach (KeyValuePair<int, double> kv in st)
				{
					if (kv.Key >= 0 && Network.Roads[Traffic.Edges[kv.Key].Road].Kind == RoadKind.Trunk)
					{
						trunk += kv.Value;
						if (kv.Value > 60.0)
						{
							trunkWorst += " edge " + kv.Key + " (nodes " + Traffic.Edges[kv.Key].A + "/" + Traffic.Edges[kv.Key].B + ") " + (kv.Value / 60.0).ToString("0.0");
						}
					}
				}
				Console.WriteLine("      on the trunk: " + (trunk / 60.0).ToString("0.0") + " min (going on along it " + (day.TrunkStoppedOn / 60.0).ToString("0.0") + ", waiting to turn off " + (day.TrunkStoppedTurning / 60.0).ToString("0.0") + ");" + trunkWorst);
			}
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
		Console.WriteLine("  the same day twice: " + a.ToString("x16") + (a == b ? " both times  ok" : " then " + b.ToString("x16") + "  FAIL") + "; built again in " + again.BuildMs.ToString("0") + " ms (homes' parking " + again.ParkingMs.ToString("0") + " ms)" + (again.BuildMs < 2000.0 ? "  ok" : "  SLOW (aim under 2 s; reported, not failed: it varies with the machine's load)"));
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
		Console.WriteLine("  let go of the queue's order after 45 s: " + day.Forced + " (never into a car in the box, never without room past it, never across a car with the right of way); let in at the junction just past the trunk's box with it: " + day.Chained);
		{
			int lines = 0, three = 0, four = 0, many = 0;
			for (int n = 0; n < Traffic.Nodes.Count; n++)
			{
				int c = Traffic.Links[n].Count;
				if (c < 3)
				{
					continue;
				}
				TrafficDay.MainArms(n, out int ma, out int _);
				lines += ma >= 0 ? 1 : 0;
				three += c == 3 ? 1 : 0;
				four += c == 4 ? 1 : 0;
				many += c > 4 ? 1 : 0;
			}
			ok &= StopLines();
			Console.WriteLine("  right of way: " + lines + " junctions with a main line of " + (three + four + many) + " (" + three + " of three arms, " + four + " of four, " + many + " of more); " + day.RefusedGap + " refusals waiting for a gap");
			Console.WriteLine("  cars stopped in a trunk junction crossing a carriageway: " + day.StoppedAcrossCars + " (" + day.StoppedAcross + " steps)" + (day.StoppedAcrossCars == 0 ? "  ok" : "  FAIL"));
			ok &= day.StoppedAcrossCars == 0;
			foreach (string note in day.StoppedAcrossNotes) Console.WriteLine("    across: " + note);
			Console.WriteLine("  junction lanes: " + day.IntoJunctionLanes + " into one to turn off, " + day.Merged + " merged out of one, " + day.Missed + " turns missed (went on and round)");
			Console.WriteLine("  the trunk's two lanes: " + day.Overtakes + " overtakes, " + day.MovedOver + " moved over for a car joining, " + day.KeptRight + " back to the outer lane");
			// Not none: a car let out with time to clear may still be slower through the box than reckoned (it slows for its turn, or
			// for a car on its way out). First come first served gave about 650 a day (TV_FCFS=1).
			Console.WriteLine("  cars with the right of way refused at a junction for a car giving way to them: " + day.MainHeld + " (at most " + MostMainHeld + ")" + (day.MainHeld <= MostMainHeld ? "  ok" : "  FAIL"));
			ok &= day.MainHeld <= MostMainHeld;
			foreach (string n in day.MainHeldNotes) Console.WriteLine("    held: " + n);
			foreach (string n in day.SpeedNotes) Console.WriteLine("    speeding: " + n);
		}
		foreach (string n in day.OverlapNotes) Console.WriteLine("    overlap: " + n);
		foreach (string n in day.ConflictNotes) Console.WriteLine("    conflict: " + n);
		Console.WriteLine("  over the limit + 5%: " + day.Speeding + " steps" + (day.Speeding > 0 ? ", worst " + (100.0 * (day.MaxOver - 1.0)).ToString("0") + "% over" : "") + (day.Speeding == 0 ? "  ok" : "  FAIL"));
		ok &= day.Speeding == 0;
		LongStands(day);
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
		// 4 points (3.5 before 0.33.0): with the right of way the side roads' waits at the evening peak move a little of it later
		// (3.5 to 3.7 on the 16 seeds; seed 1 first come first served: 3.1).
		Console.WriteLine("  the hours' shape: off " + (day.Weekend ? "a Saturday's" : "TPG 9") + " by at most " + worst.ToString("0.0") + " points an hour" + (worst < 4.0 ? "  ok" : "  FAIL"));
		// 3.5 points: a drive here spans an hour or more of the world's clock (it runs 36 times fast, driving does not), which smears
		// every hour into the next; the tables are tuned against it, but no table can undo it entirely.
		ok &= worst < 4.0;
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
		// TV_DRIVE_ONLY: only the physics cars' check after the day's (the parking and the ledger take most of a run).
		if (Environment.GetEnvironmentVariable("TV_DRIVE_ONLY") == null)
		{
			ok &= Parking(day, closeup);
			ok &= Lateness(day);
		}
		ok &= Drive(day);
		ok &= Contact(day);
		Highway(day);
		if (picture != null)
		{
			Pictures.Draw(day, passes, picture);
		}
		return ok;
	}

	/// <summary>A parked car's footprint for the checks: a little under a car (about 14 by 5.4).</summary>
	private const double CarLong = 13.0, CarWide = 5.0;

	/// <summary>
	/// The parking (TrafficParking): every hour, no two parked cars overlapping; spaces off the paving of roads that are not their own,
	/// out of the lots' buildings and the water; how far a car goes between its space and its lane; and what choosing the cars near the
	/// player costs, as the game does it every 0.4 s.
	/// </summary>
	private static bool Parking(TrafficDay day, string[] closeup)
	{
		bool ok = true;
		Stopwatch watch = Stopwatch.StartNew();
		TrafficParking parking = day.Parking;
		Console.WriteLine("  parking (chosen as the day ran): at most " + parking.Busiest + " cars at one place, " + parking.Overflow + " stays at the kerb past a place's spaces, " + parking.KerbFull + " found the kerb full; kerb spaces " + parking.KerbSpaces + ", struck over a home's own space " + parking.StruckHome + ", in a house " + parking.StruckHouse + ", on another road or water " + parking.StruckOther);
		// The lots' spaces: how many each kind has, and how many a road, the water or a building took.
		Dictionary<LotKind, (int kept, int all, int lots)> lotKinds = new Dictionary<LotKind, (int, int, int)>();
		List<(double u, double v, double nu, double nv)> raw = new List<(double, double, double, double)>();
		string retailAt = "";
		for (int d = 0; d < Traffic.Destinations.Count; d++)
		{
			Traffic.Destination dest = Traffic.Destinations[d];
			if (dest.Lot < 0)
			{
				continue;
			}
			Town.Lot lot = Town.Lots[dest.Lot];
			TrafficParking.Spaces(lot, raw);
			int kept = parking.LotSpaces(d).Count;
			lotKinds.TryGetValue(lot.Kind, out (int kept, int all, int lots) had);
			lotKinds[lot.Kind] = (had.kept + kept, had.all + raw.Count, had.lots + 1);
			if (lot.Kind == LotKind.Retail)
			{
				retailAt = " (the retail park at " + lot.X.ToString("0") + ", " + lot.Z.ToString("0") + ")";
			}
		}
		string lotText = "";
		foreach (KeyValuePair<LotKind, (int kept, int all, int lots)> kv in lotKinds)
		{
			lotText += (lotText.Length > 0 ? ", " : "") + kv.Key + " " + kv.Value.lots + " lots " + kv.Value.kept + " of " + kv.Value.all;
		}
		Console.WriteLine("  lots' spaces (kept of laid out, the rest taken by a road, water or a building): " + lotText + retailAt);
		int overlaps = 0, samples = 0;
		Dictionary<string, int> overlapKinds = new Dictionary<string, int>();
		string firstOverlap = null;
		List<(TrafficParking.Spot s, int car)> spots = new List<(TrafficParking.Spot, int)>();
		Dictionary<long, List<int>> grid = new Dictionary<long, List<int>>();
		for (int hour = 0; hour < 24; hour++)
		{
			double w = TrafficDay.SimOfHour(hour + 0.25);
			spots.Clear();
			grid.Clear();
			for (int c = 0; c < day.Cars.Count; c++)
			{
				TrafficDay.Place at = day.At(c, w);
				if (at.State != TrafficDay.State.Parked)
				{
					continue;
				}
				TrafficParking.Spot s = parking.Parked(c, at.Leg);
				if (s.Away)
				{
					continue;
				}
				int i = spots.Count;
				spots.Add((s, c));
				long key = ((long)Math.Floor(s.X / 32.0) << 32) ^ (uint)(int)Math.Floor(s.Z / 32.0);
				if (!grid.TryGetValue(key, out List<int> cell))
				{
					cell = new List<int>();
					grid[key] = cell;
				}
				cell.Add(i);
			}
			samples += spots.Count;
			for (int i = 0; i < spots.Count; i++)
			{
				TrafficParking.Spot a = spots[i].s;
				int gx = (int)Math.Floor(a.X / 32.0), gz = (int)Math.Floor(a.Z / 32.0);
				for (int ox = -1; ox <= 1; ox++)
				{
					for (int oz = -1; oz <= 1; oz++)
					{
						if (!grid.TryGetValue(((long)(gx + ox) << 32) ^ (uint)(gz + oz), out List<int> cell))
						{
							continue;
						}
						foreach (int j in cell)
						{
							if (j > i && TrafficParking.Overlap(a, spots[j].s, -0.5))
							{
								string ka = What(day, spots[i].car, w), kb = What(day, spots[j].car, w);
								string kk = string.CompareOrdinal(ka, kb) < 0 ? ka + " / " + kb : kb + " / " + ka;
								overlapKinds[kk] = overlapKinds.TryGetValue(kk, out int nk) ? nk + 1 : 1;
								overlaps++;
								if (overlaps <= 6)
								{
									TrafficParking.Spot b = spots[j].s;
									Console.WriteLine("    pair: " + kk + " (" + a.X.ToString("0.0") + ", " + a.Z.ToString("0.0") + ") facing (" + a.Hx.ToString("0.00") + ", " + a.Hz.ToString("0.00") + ") and (" + b.X.ToString("0.0") + ", " + b.Z.ToString("0.0") + ") facing (" + b.Hx.ToString("0.00") + ", " + b.Hz.ToString("0.00") + ") " + parking.Describe(spots[i].car, day.At(spots[i].car, w).Leg) + " / " + parking.Describe(spots[j].car, day.At(spots[j].car, w).Leg));
								}
								firstOverlap ??= "cars " + spots[i].car + " and " + spots[j].car + " at (" + a.X.ToString("0") + ", " + a.Z.ToString("0") + ") at " + hour + ":15";
							}
						}
					}
				}
			}
		}
		foreach (KeyValuePair<string, int> kv in overlapKinds)
		{
			Console.WriteLine("    overlapping: " + kv.Key + " " + kv.Value);
		}
		foreach (double hour in new[] { 12.5, 21.5 })
		{
			double w = TrafficDay.SimOfHour(hour);
			int atKerb = 0, offRoad = 0, hidden = 0;
			for (int c = 0; c < day.Cars.Count; c++)
			{
				TrafficDay.Place at = day.At(c, w);
				if (at.State != TrafficDay.State.Parked)
				{
					continue;
				}
				if (parking.Parked(c, at.Leg).Away)
				{
					hidden += day.Cars[c].Household >= 0 ? 1 : 0;
				}
				else if (parking.IsKerb(c, at.Leg, out int _))
				{
					atKerb++;
				}
				else
				{
					offRoad++;
				}
			}
			Console.WriteLine("  parked at " + hour.ToString("00.0") + ": " + atKerb + " at the kerb, " + offRoad + " off the road, " + hidden + " out of sight (round the back)");
		}
		Console.WriteLine("  parked cars overlapping, looked at every hour: " + overlaps + " pairs in " + samples + " parked cars" + (firstOverlap != null ? " (first: " + firstOverlap + ")" : "") + (overlaps == 0 ? "  ok" : "  FAIL"));
		ok &= overlaps == 0;

		// The houses (the plots that fit, as Buildings draws them; their wings at the back left out), for the spaces' test.
		houses = Houses();
		// Every space any car uses over the day, once: off other roads' paving, out of buildings and water.
		HashSet<(long, long)> seen = new HashSet<(long, long)>();
		int spaces = 0, onRoad = 0, inBuilding = 0, inWater = 0, kerb = 0, inHouse = 0;
		string firstRoad = null, firstBuilding = null, firstWater = null, firstHouse = null;
		Dictionary<string, int> roadKinds = new Dictionary<string, int>(), waterKinds = new Dictionary<string, int>();
		void Look(TrafficParking.Spot s, int ownRoad, bool atKerb, string what)
		{
			if (s.Away || !seen.Add(((long)Math.Round(s.X * 4.0), (long)Math.Round(s.Z * 4.0))))
			{
				return;
			}
			spaces++;
			kerb += atKerb ? 1 : 0;
			double ax = s.Hx * CarLong * 0.5, az = s.Hz * CarLong * 0.5, bx = s.Hz * CarWide * 0.5, bz = -s.Hx * CarWide * 0.5;
			bool road = false, water = false;
			foreach ((double u, double v) in new[] { (1.0, 1.0), (1.0, -1.0), (-1.0, 1.0), (-1.0, -1.0), (0.0, 0.0) })
			{
				double x = s.X + ax * u + bx * v, z = s.Z + az * u + bz * v;
				road |= Network.OnOtherPaving(x, z, atKerb ? ownRoad : -1);
				water |= Rivers.Water(x, z, out double _);
				Town.Lot lot = Town.At(x, z, out bool building);
				if (lot != null && building)
				{
					inBuilding++;
					firstBuilding ??= "(" + s.X.ToString("0") + ", " + s.Z.ToString("0") + ") on a " + lot.Kind + " lot";
					break;
				}
			}
			if (InHouse(s))
			{
				inHouse++;
				firstHouse ??= "(" + s.X.ToString("0") + ", " + s.Z.ToString("0") + ") " + what;
			}
			if (road)
			{
				roadKinds[what] = roadKinds.TryGetValue(what, out int nr) ? nr + 1 : 1;
				onRoad++;
				firstRoad ??= "(" + s.X.ToString("0") + ", " + s.Z.ToString("0") + ")" + (atKerb ? " at the kerb" : "");
			}
			if (water)
			{
				waterKinds[what] = waterKinds.TryGetValue(what, out int nw) ? nw + 1 : 1;
				inWater++;
				firstWater ??= "(" + s.X.ToString("0") + ", " + s.Z.ToString("0") + ")";
			}
		}
		for (int c = 0; c < day.Cars.Count; c++)
		{
			TrafficDay.Car car = day.Cars[c];
			if (car.Household >= 0)
			{
				Traffic.Household h = Traffic.Households[car.Household];
				bool homeKerb = parking.IsKerb(c, -1, out int homeRoad);
				Look(parking.Parked(c, -1), homeRoad, homeKerb, "home " + h.Kind + (homeKerb ? " (kerb)" : ""));
			}
			foreach (int li in car.Legs)
			{
				TrafficDay.Leg leg = day.Legs[li];
				if (leg.Start < 0.0 || leg.Vanishes)
				{
					continue;
				}
				TrafficParking.Spot s = parking.Parked(c, li);
				bool atKerb = parking.IsKerb(c, li, out int kerbRoad);
				Look(s, kerbRoad, atKerb, (leg.Return ? "home" : leg.Host >= 0 ? "visit" : "at " + Traffic.Destinations[leg.Dest].Kind) + (atKerb ? " (kerb)" : ""));
			}
		}
		foreach (KeyValuePair<string, int> kv in roadKinds)
		{
			Console.WriteLine("    on another road's paving: " + kv.Key + " " + kv.Value);
		}
		foreach (KeyValuePair<string, int> kv in waterKinds)
		{
			Console.WriteLine("    in water: " + kv.Key + " " + kv.Value);
		}
		Console.WriteLine("  spaces used: " + spaces + " (" + kerb + " at the kerb); on another road's paving " + onRoad + (firstRoad != null ? " (first " + firstRoad + ")" : "")
			+ ", in a lot's building " + inBuilding + (firstBuilding != null ? " (first " + firstBuilding + ")" : "") + ", in water " + inWater + (firstWater != null ? " (first " + firstWater + ")" : "")
			+ ", in a house " + inHouse + (firstHouse != null ? " (first " + firstHouse + ")" : "")
			+ (onRoad == 0 && inBuilding == 0 && inWater == 0 && inHouse == 0 ? "  ok" : "  FAIL"));
		ok &= onRoad == 0 && inBuilding == 0 && inWater == 0 && inHouse == 0;

		// Between the space and the lane: how far apart they are at a trip's ends (the game pulls the car between them over as long a
		// stretch of the trip, 30 to 150 units).
		List<double> pulls = new List<double>();
		foreach (TrafficDay.Leg leg in day.Legs)
		{
			if (leg.Start < 0.0)
			{
				continue;
			}
			TrafficDay.Place p = default;
			if (!leg.Appears)
			{
				TrafficParking.Spot s = parking.Before(day.Legs.IndexOf(leg));
				TrafficDay.OnPath(leg, 0.0, ref p);
				if (!s.Away)
				{
					pulls.Add(Math.Sqrt((s.X - p.X) * (s.X - p.X) + (s.Z - p.Z) * (s.Z - p.Z)));
				}
			}
			if (!leg.Vanishes)
			{
				TrafficParking.Spot s = parking.Parked(leg.Car, day.Legs.IndexOf(leg));
				TrafficDay.OnPath(leg, leg.Length, ref p);
				if (!s.Away)
				{
					pulls.Add(Math.Sqrt((s.X - p.X) * (s.X - p.X) + (s.Z - p.Z) * (s.Z - p.Z)));
				}
			}
		}
		pulls.Sort();
		Console.WriteLine("  space to lane at a trip's ends: median " + Pct(pulls, 0.5).ToString("0") + ", 90th percentile " + Pct(pulls, 0.9).ToString("0") + ", 99th " + Pct(pulls, 0.99).ToString("0")
			+ ", most " + (pulls.Count > 0 ? pulls[pulls.Count - 1] : 0.0).ToString("0") + " units (" + pulls.Count + " ends)");

		// Moving cars, where the game draws them (pulling in and out included), against the parked cars, every 20 s of the day.
		int through = 0, looks = 0;
		string firstThrough = null;
		Dictionary<long, List<(TrafficParking.Spot s, int c, int leg)>> parkedCells = new Dictionary<long, List<(TrafficParking.Spot, int, int)>>();
		Dictionary<string, int> throughKinds = new Dictionary<string, int>();
		for (double w = 0.0; w < Traffic.DaySeconds; w += 20.0)
		{
			parkedCells.Clear();
			List<(int c, TrafficDay.Place at)> moving = new List<(int, TrafficDay.Place)>();
			for (int c = 0; c < day.Cars.Count; c++)
			{
				TrafficDay.Place at = day.At(c, w);
				if (at.State == TrafficDay.State.Driving)
				{
					if (!at.InTunnel)
					{
						moving.Add((c, at));
					}
					continue;
				}
				if (at.State != TrafficDay.State.Parked)
				{
					continue;
				}
				TrafficParking.Spot s = parking.Parked(c, at.Leg);
				if (s.Away)
				{
					continue;
				}
				long key = ((long)Math.Floor(s.X / 32.0) << 32) ^ (uint)(int)Math.Floor(s.Z / 32.0);
				if (!parkedCells.TryGetValue(key, out List<(TrafficParking.Spot, int, int)> cell))
				{
					cell = new List<(TrafficParking.Spot, int, int)>();
					parkedCells[key] = cell;
				}
				cell.Add((s, c, at.Leg));
			}
			foreach ((int c, TrafficDay.Place at) in moving)
			{
				parking.Drawn(c, at, out double x, out double z, out double hx, out double hz);
				TrafficParking.Spot m = new TrafficParking.Spot { X = x, Z = z, Hx = hx, Hz = hz };
				looks++;
				int gx = (int)Math.Floor(x / 32.0), gz = (int)Math.Floor(z / 32.0);
				bool hit = false;
				for (int ox = -1; ox <= 1 && !hit; ox++)
				{
					for (int oz = -1; oz <= 1 && !hit; oz++)
					{
						if (!parkedCells.TryGetValue(((long)(gx + ox) << 32) ^ (uint)(gz + oz), out List<(TrafficParking.Spot s, int c, int leg)> cell))
						{
							continue;
						}
						foreach ((TrafficParking.Spot s, int pc, int pleg) in cell)
						{
							if (TrafficParking.Overlap(m, s, -0.5))
							{
								hit = true;
								string phase = at.P < 250.0 ? "pulling out" : day.Legs[at.Leg].Length - at.P < 250.0 ? "pulling in" : "on its way";
								string kind = phase + " / parked " + (parking.IsKerb(pc, pleg, out int _) ? "at the kerb" : What(day, pc, w));
								if (!throughKinds.ContainsKey(kind))
								{
									Console.WriteLine("    first " + kind + ": car " + c + " at (" + x.ToString("0") + ", " + z.ToString("0") + ") at " + (3.0 + w / 100.0).ToString("0.00") + " h, parked car " + pc + " at (" + s.X.ToString("0") + ", " + s.Z.ToString("0") + ")" + (Environment.GetEnvironmentVariable("TV_MEET") != null ? DebugPull(day, parking, c, at, s) : ""));
								}
								throughKinds[kind] = throughKinds.TryGetValue(kind, out int nk) ? nk + 1 : 1;
								firstThrough ??= "car " + c + " at (" + x.ToString("0") + ", " + z.ToString("0") + ") at sim " + w.ToString("0") + " s, " + (at.P < 250.0 ? "pulling out" : day.Legs[at.Leg].Length - at.P < 250.0 ? "pulling in" : "on its way");
								break;
							}
						}
					}
				}
				through += hit ? 1 : 0;
			}
		}
		foreach (KeyValuePair<string, int> kv in throughKinds)
		{
			Console.WriteLine("    through: " + kv.Key + " " + kv.Value);
		}
		Console.WriteLine("  moving cars (as drawn) through a parked car, every 20 s: " + through + " of " + looks + (firstThrough != null ? " (first: " + firstThrough + ")" : "") + (through == 0 ? "  ok" : "  FAIL"));
		ok &= through == 0;

		// Moving cars against each other as drawn: the day keeps them apart on its lanes, but the pulls in and out of spaces (and a
		// drive's single lane) are drawn off them.
		watch.Restart();
		int movingLooks = 0, movingHits = 0;
		Dictionary<string, int> movingKinds = new Dictionary<string, int>();
		Dictionary<long, List<(TrafficParking.Spot s, int c, TrafficDay.Place at)>> movingCells = new Dictionary<long, List<(TrafficParking.Spot, int, TrafficDay.Place)>>();
		for (double w = 0.0; w < Traffic.DaySeconds; w += 5.0)
		{
			movingCells.Clear();
			for (int c = 0; c < day.Cars.Count; c++)
			{
				TrafficDay.Place at = day.At(c, w);
				if (at.State != TrafficDay.State.Driving || at.InTunnel)
				{
					continue;
				}
				parking.Drawn(c, at, out double x, out double z, out double hx, out double hz);
				TrafficParking.Spot m = new TrafficParking.Spot { X = x, Z = z, Hx = hx, Hz = hz };
				movingLooks++;
				int gx = (int)Math.Floor(x / 32.0), gz = (int)Math.Floor(z / 32.0);
				bool hit = false;
				for (int ox = -1; ox <= 1 && !hit; ox++)
				{
					for (int oz = -1; oz <= 1 && !hit; oz++)
					{
						if (!movingCells.TryGetValue(((long)(gx + ox) << 32) ^ (uint)(gz + oz), out List<(TrafficParking.Spot s, int c, TrafficDay.Place at)> cell))
						{
							continue;
						}
						foreach ((TrafficParking.Spot s, int oc, TrafficDay.Place oat) in cell)
						{
							if (!TrafficParking.Overlap(m, s, -0.5))
							{
								continue;
							}
							hit = true;
							string kind = Phase(day, at) + " / " + Phase(day, oat);
							if (!movingKinds.ContainsKey(kind))
							{
								Console.WriteLine("    first " + kind + ": cars " + c + " and " + oc + " at (" + x.ToString("0") + ", " + z.ToString("0") + ") at " + (3.0 + w / 100.0).ToString("0.00") + " h");
							}
							if (Environment.GetEnvironmentVariable("TV_MEET") != null)
							{
								Console.WriteLine("      meet " + (3.0 + w / 100.0).ToString("0.00") + "h " + kind + " | car " + c + " e" + at.Edge + "/" + at.Dir + " s" + at.S.ToString("0") + " v" + at.Speed.ToString("0.0") + " p" + at.P.ToString("0") + "/" + day.Legs[at.Leg].Length.ToString("0") + " t" + day.Legs[at.Leg].Start.ToString("0") + "-" + day.Legs[at.Leg].End.ToString("0") + " (" + x.ToString("0.0") + "," + z.ToString("0.0") + ") h(" + hx.ToString("0.00") + "," + hz.ToString("0.00") + ") | car " + oc + " e" + oat.Edge + "/" + oat.Dir + " s" + oat.S.ToString("0") + " v" + oat.Speed.ToString("0.0") + " p" + oat.P.ToString("0") + "/" + day.Legs[oat.Leg].Length.ToString("0") + " t" + day.Legs[oat.Leg].Start.ToString("0") + "-" + day.Legs[oat.Leg].End.ToString("0") + " (" + s.X.ToString("0.0") + "," + s.Z.ToString("0.0") + ") h(" + s.Hx.ToString("0.00") + "," + s.Hz.ToString("0.00") + ")");
							}
							movingKinds[kind] = movingKinds.TryGetValue(kind, out int nk) ? nk + 1 : 1;
							break;
						}
					}
				}
				movingHits += hit ? 1 : 0;
				long key = ((long)gx << 32) ^ (uint)gz;
				if (!movingCells.TryGetValue(key, out List<(TrafficParking.Spot, int, TrafficDay.Place)> own))
				{
					own = new List<(TrafficParking.Spot, int, TrafficDay.Place)>();
					movingCells[key] = own;
				}
				own.Add((m, c, at));
			}
		}
		foreach (KeyValuePair<string, int> kv in movingKinds)
		{
			Console.WriteLine("    meeting: " + kv.Key + " " + kv.Value);
		}
		// Pulling in and out is drawn off the day's lanes, and the day holds the car still beside its space meanwhile: those meetings
		// fail over a few a day (before the day knew the spaces, about 50). The rest are reported: cars turning across each other inside
		// a junction's box, and late ways home driven after the day's end, which the next morning's traffic does not see.
		int pullMeets = 0;
		foreach (KeyValuePair<string, int> kv in movingKinds)
		{
			pullMeets += kv.Key.Contains("pulling") ? kv.Value : 0;
		}
		Console.WriteLine("  moving cars (as drawn) through each other, every 5 s: " + movingHits + " of " + movingLooks + ", " + pullMeets + " pulling in or out (" + watch.ElapsedMilliseconds + " ms)" + (pullMeets <= MostPullMeetings ? "  ok (the rest reported)" : "  FAIL (more than " + MostPullMeetings + " pulling in or out)"));
		ok &= pullMeets <= MostPullMeetings;

		// Choosing the cars near the player, as TrafficCars.Choose does: every car's place, the parked ones' spaces.
		watch.Restart();
		int calls = 0, nearest = 0;
		foreach (double hour in new[] { 8.5, 12.5, 17.5 })
		{
			double w = TrafficDay.SimOfHour(hour);
			foreach (Places.Place place in Places.All)
			{
				if (place.Kind != PlaceKind.Town)
				{
					continue;
				}
				calls++;
				for (int c = 0; c < day.Cars.Count; c++)
				{
					TrafficDay.Car car = day.Cars[c];
					double hx = car.Home.X - place.X, hz = car.Home.Z - place.Z;
					if (car.Legs.Count == 0 && hx * hx + hz * hz > 510.0 * 510.0)
					{
						continue;
					}
					TrafficDay.Place at = day.At(c, w);
					double x = at.X, z = at.Z;
					if (at.State == TrafficDay.State.Parked)
					{
						TrafficParking.Spot s = parking.Parked(c, at.Leg);
						x = s.X;
						z = s.Z;
					}
					nearest += (x - place.X) * (x - place.X) + (z - place.Z) * (z - place.Z) < 450.0 * 450.0 ? 1 : 0;
				}
			}
		}
		double each = watch.Elapsed.TotalMilliseconds / Math.Max(1, calls);
		if (closeup != null)
		{
			// --closeup out.ppm [x z span hour]: by default the main town's middle at 12:30.
			Places.Place main = null;
			foreach (Places.Place pl in Places.All)
			{
				if (pl.Kind == PlaceKind.Town && (main == null || pl.Radius > main.Radius))
				{
					main = pl;
				}
			}
			double x0 = closeup.Length > 1 ? double.Parse(closeup[1]) : main.X, z0 = closeup.Length > 2 ? double.Parse(closeup[2]) : main.Z;
			double span = closeup.Length > 3 ? double.Parse(closeup[3]) : 700.0, hour = closeup.Length > 4 ? double.Parse(closeup[4]) : 12.5;
			Pictures.Closeup(day, parking, closeup[0], x0, z0, span, hour);
		}
		Console.WriteLine("  choosing the cars near the player (in a town's middle): " + each.ToString("0.00") + " ms a time, " + (nearest / Math.Max(1, calls)) + " cars within 140 m" + (each < 4.0 ? "  ok" : "  SLOW (aim under 4 ms: it runs every 0.4 s)"));
		return ok;
	}

	/// <summary>The houses: middle, the way along the road (unit), half width along it and half depth across, in cells of 64.</summary>
	internal static Dictionary<long, List<(double x, double z, double ux, double uz, double hw, double hd)>> houses;

	internal static Dictionary<long, List<(double x, double z, double ux, double uz, double hw, double hd)>> Houses()
	{
		Dictionary<long, List<(double, double, double, double, double, double)>> grid = new Dictionary<long, List<(double, double, double, double, double, double)>>();
		List<Plots.Plot> plots = new List<Plots.Plot>();
		for (int r = 0; r < Network.Roads.Count; r++)
		{
			Network.Road road = Network.Roads[r];
			for (int i = 0; i + 1 < road.Count; i++)
			{
				plots.Clear();
				Plots.Along(r, road, i, plots);
				foreach (Plots.Plot p in plots)
				{
					if (!Plots.Fits(p))
					{
						continue;
					}
					long key = ((long)Math.Floor(p.Cx / 64.0) << 32) ^ (uint)(int)Math.Floor(p.Cz / 64.0);
					if (!grid.TryGetValue(key, out List<(double, double, double, double, double, double)> list))
					{
						list = new List<(double, double, double, double, double, double)>();
						grid[key] = list;
					}
					list.Add((p.Cx, p.Cz, p.Dx, p.Dz, p.Width * 0.5, p.Depth * 0.5));
				}
			}
		}
		return grid;
	}

	/// <summary>Whether a parked car at <paramref name="s"/> (a little under a car's size) stands in a house.</summary>
	private static bool InHouse(TrafficParking.Spot s)
	{
		int gx = (int)Math.Floor(s.X / 64.0), gz = (int)Math.Floor(s.Z / 64.0);
		for (int ox = -1; ox <= 1; ox++)
		{
			for (int oz = -1; oz <= 1; oz++)
			{
				if (!houses.TryGetValue(((long)(gx + ox) << 32) ^ (uint)(gz + oz), out List<(double x, double z, double ux, double uz, double hw, double hd)> list))
				{
					continue;
				}
				foreach ((double x, double z, double ux, double uz, double hw, double hd) h in list)
				{
					if (Boxes(s.X, s.Z, s.Hx, s.Hz, CarLong * 0.5, CarWide * 0.5, h.x, h.z, h.ux, h.uz, h.hw, h.hd))
					{
						return true;
					}
				}
			}
		}
		return false;
	}

	/// <summary>Whether two rectangles overlap (middle, unit axis, half sizes along it and across), by separating axes.</summary>
	private static bool Boxes(double ax, double az, double aux, double auz, double ahl, double ahw, double bx, double bz, double bux, double buz, double bhl, double bhw)
	{
		double dx = bx - ax, dz = bz - az;
		foreach ((double ux, double uz) in new[] { (aux, auz), (auz, -aux), (bux, buz), (buz, -bux) })
		{
			double ra = Math.Abs(aux * ux + auz * uz) * ahl + Math.Abs(auz * ux - aux * uz) * ahw;
			double rb = Math.Abs(bux * ux + buz * uz) * bhl + Math.Abs(buz * ux - bux * uz) * bhw;
			if (Math.Abs(dx * ux + dz * uz) > ra + rb)
			{
				return false;
			}
		}
		return true;
	}

	/// <summary>What kind of space a parked car is in, for the overlap report.</summary>
	private static string DebugPull(TrafficDay day, TrafficParking parking, int c, TrafficDay.Place at, TrafficParking.Spot other)
	{
		TrafficDay.Leg leg = day.Legs[at.Leg];
		TrafficParking.Spot own = parking.Before(at.Leg);
		string r = " | T " + at.T.ToString("0.0") + " leg " + leg.Start.ToString("0") + "-" + leg.End.ToString("0") + " pullOut " + leg.PullOut.ToString("0.0") + " day at (" + at.X.ToString("0.0") + "," + at.Z.ToString("0.0") + ") h(" + at.Hx.ToString("0.00") + "," + at.Hz.ToString("0.00") + ") own (" + own.X.ToString("0.0") + "," + own.Z.ToString("0.0") + ") h(" + own.Hx.ToString("0.00") + "," + own.Hz.ToString("0.00") + ") other (" + other.X.ToString("0.0") + "," + other.Z.ToString("0.0") + ") h(" + other.Hx.ToString("0.00") + "," + other.Hz.ToString("0.00") + ") path:";
		for (double t = 0; t <= 1.0001; t += 0.125)
		{
			TrafficDay.Place p = at;
			p.T = leg.PullOut * t;
			parking.Drawn(c, p, out double x, out double z, out double hx, out double hz);
			r += " (" + x.ToString("0.0") + "," + z.ToString("0.0") + ")";
		}
		return r;
	}

	/// <summary>Meetings of a car pulling in or out with another moving car, as drawn every 5 s, allowed in a day.</summary>
	private const int MostPullMeetings = 10;

	/// <summary>What a moving car is doing where it is drawn: pulling out of or into its space, on a drive, or on its way.</summary>
	private static string Phase(TrafficDay day, TrafficDay.Place at)
	{
		if (day.Parking.InPull(at, out bool start))
		{
			return start ? "pulling out" : "pulling in";
		}
		if (at.Edge >= 0 && Network.Roads[Traffic.Edges[at.Edge].Road].Kind == RoadKind.Drive)
		{
			return "on a drive";
		}
		return "on its way";
	}

	private static string What(TrafficDay day, int c, double w)
	{
		TrafficDay.Place at = day.At(c, w);
		if (at.Leg < 0 || day.Legs[at.Leg].Return)
		{
			return "home " + Traffic.Households[day.Cars[c].Household].Kind;
		}
		TrafficDay.Leg leg = day.Legs[at.Leg];
		return leg.Host >= 0 ? "visiting " + Traffic.Households[leg.Host].Kind : "at " + Traffic.Destinations[leg.Dest].Kind;
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
