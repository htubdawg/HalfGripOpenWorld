using System;
using System.Collections.Generic;
using HalfGripOpenWorld;

/// <summary>
/// Which junctions the day's counts say should have a light or a four-way stop, by the US rules (MUTCD 2009, warrant 1 for signals,
/// section 2B.07 for multi-way stops), at their 70% volumes for country roads (speeds over 40 mph or a place under 10,000 people):
/// a light where, in each of 8 hours, the main road carries 350 vehicles an hour (both ways; 420 with two lanes each way) and the
/// busier side approach 105 (condition A, cars that cannot get out), or 525 (630) and 53 (condition B, cars that cannot get across),
/// or 80% of both together; a four-way stop where in 8 hours the main road carries 210 and the side roads together 140. An hour of a
/// game day is 100 s, and a game day's counts x 36 are its AADT (as the PennDOT comparison), so an hour's count x 36 is its volume.
/// Reported, for deciding the rule the game builds from.
/// </summary>
internal static class Warrants
{
	internal static void Report(TrafficDay day)
	{
		int n = Traffic.Nodes.Count;
		var counts = new Dictionary<int, double[]>();
		for (int li = 0; li < day.Legs.Count; li++)
		{
			TrafficDay.Leg l = day.Legs[li];
			if (l.Start < 0.0 || l.Rec == null)
			{
				continue;
			}
			int idx = 0;
			for (int k = 0; k < l.Path.Length; k++)
			{
				int node = TrafficDay.NodeAfter(l.Path, k);
				if (node < 0)
				{
					continue;
				}
				double p = l.Path[k].P0 + l.Path[k].Len;
				while (idx < l.Rec.Length && l.Rec[idx] < p)
				{
					idx++;
				}
				double t = l.Start + idx * TrafficDay.RecordStep;
				int hour = (int)(t / 100.0) % 24;
				int arm = TrafficDay.LinkIndex(node, l.Path[k].Edge);
				if (arm < 0)
				{
					continue;
				}
				int key = node * 16 + arm;
				if (!counts.TryGetValue(key, out double[] h))
				{
					counts[key] = h = new double[24];
				}
				h[hour] += 36.0;
			}
		}
		int lit = 0, litWarranted = 0, newSignals = 0, newStops = 0, litStops = 0;
		var lines = new List<string>();
		for (int node = 0; node < n; node++)
		{
			List<int> links = Traffic.Links[node];
			if (links.Count < 3)
			{
				continue;
			}
			TrafficDay.MainArms(node, out int a, out int b);
			double[][] arm = new double[links.Count][];
			double[] total = new double[links.Count];
			for (int i = 0; i < links.Count; i++)
			{
				arm[i] = counts.TryGetValue(node * 16 + i, out double[] h) ? h : new double[24];
				foreach (double v in arm[i])
				{
					total[i] += v;
				}
			}
			if (a < 0)
			{
				// No main line by the map: the two busiest ways in.
				a = 0;
				for (int i = 1; i < links.Count; i++) if (total[i] > total[a]) a = i;
				b = a == 0 ? 1 : 0;
				for (int i = 0; i < links.Count; i++) if (i != a && total[i] > total[b]) b = i;
			}
			bool twoLane = Network.Roads[Traffic.Edges[links[a]].Road].Kind == RoadKind.Trunk;
			double aMaj = twoLane ? 420 : 350, bMaj = twoLane ? 630 : 525;
			int hA = 0, hB = 0, hAB = 0, hStop = 0;
			double peakMaj = 0, peakMin = 0;
			for (int hr = 0; hr < 24; hr++)
			{
				double maj = arm[a][hr] + arm[b][hr], minMax = 0, minSum = 0;
				for (int i = 0; i < links.Count; i++)
				{
					if (i != a && i != b)
					{
						minMax = Math.Max(minMax, arm[i][hr]);
						minSum += arm[i][hr];
					}
				}
				hA += maj >= aMaj && minMax >= 105 ? 1 : 0;
				hB += maj >= bMaj && minMax >= 53 ? 1 : 0;
				hAB += maj >= 0.8 * aMaj && minMax >= 84 && maj >= 0.8 * bMaj && minMax >= 42 ? 1 : 0;
				hStop += maj >= 210 && minSum >= 140 ? 1 : 0;
				if (maj + minSum > peakMaj + peakMin)
				{
					peakMaj = maj;
					peakMin = minSum;
				}
			}
			bool signal = hA >= 8 || hB >= 8 || hAB >= 8, stop = !signal && hStop >= 8;
			bool has = TrafficSignals.Has(node);
			lit += has ? 1 : 0;
			litWarranted += has && signal ? 1 : 0;
			litStops += has && stop ? 1 : 0;
			newSignals += !has && signal ? 1 : 0;
			newStops += !has && stop ? 1 : 0;
			if (signal || stop || has)
			{
				string kinds = "";
				for (int i = 0; i < links.Count; i++)
				{
					kinds += (i == 0 ? "" : " ") + Network.Roads[Traffic.Edges[links[i]].Road].Kind + (i == a || i == b ? "*" : "") + ":" + (total[i] / 36.0).ToString("0");
				}
				lines.Add("    node " + node + " at (" + Traffic.Nodes[node].X.ToString("0") + ", " + Traffic.Nodes[node].Z.ToString("0") + ")" + (has ? " lights" : "") + " -> " + (signal ? "SIGNAL" : stop ? "4-WAY STOP" : "neither")
					+ "; hours A " + hA + ", B " + hB + ", A+B " + hAB + ", stop " + hStop + "; busiest hour main " + peakMaj.ToString("0") + " vph, side " + peakMin.ToString("0") + "; trips a day by way in " + kinds);
			}
		}
		Console.WriteLine("  warrants (MUTCD at 70%, from the day's counts): lights now " + lit + " (" + litWarranted + " warranted, " + litStops + " only a four-way stop's); new lights warranted " + newSignals + ", new four-way stops " + newStops + "  (reported)");
		foreach (string s in lines)
		{
			Console.WriteLine(s);
		}
	}
}
