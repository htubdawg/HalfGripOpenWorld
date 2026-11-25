using System;
using System.Collections.Generic;
using HalfGripOpenWorld;

// The highway's traffic as a player on it meets it (docs/research/PERFORMANCE.md, "How to find out"): the owner found the trunk "barely" busier
// with 100 circling cars than with 10. Reported, not failed: the gaps between the circling cars along the loop each way at three hours
// (evenly spread, or bunched into platoons with long empty stretches), and how many cars are within the drawn reach (900 units) of a
// player driving the trunk at 168 km/h and at 85 km/h, averaged over ten minutes from each hour: what "busier" means in numbers.
internal static partial class Program
{
	/// <summary>Moving cars are drawn this near the player (TrafficCars.MovingReach).</summary>
	private const double HighwayReach = 900.0;

	private static void Highway(TrafficDay day)
	{
		// Each way's loop: a circling leg's path is its way from where it started to a tunnel, then the same lap over and over, each lap
		// starting where the path comes out of a tunnel into the world. Every car going one way shares that lap.
		Dictionary<int, (double first, double lap, int leg)> ways = new Dictionary<int, (double, double, int)>();
		Dictionary<int, List<int>> carsOf = new Dictionary<int, List<int>>();
		for (int li = 0; li < day.Legs.Count; li++)
		{
			TrafficDay.Leg leg = day.Legs[li];
			if (leg.Purpose != Traffic.Purpose.Through || leg.Start < 0.0)
			{
				continue;
			}
			double first = -1.0, second = -1.0;
			int portal = -1;
			foreach (TrafficDay.Seg s in leg.Path)
			{
				if (s.Edge == -1 && s.Inbound)
				{
					if (first < 0.0)
					{
						first = s.P0;
						portal = s.Portal;
					}
					else if (s.Portal == portal && s.P0 > first + 1.0)
					{
						second = s.P0;
						break;
					}
				}
			}
			if (first < 0.0 || second < 0.0)
			{
				continue;
			}
			if (!ways.ContainsKey(portal))
			{
				ways[portal] = (first, second - first, li);
				carsOf[portal] = new List<int>();
			}
			carsOf[portal].Add(li);
		}
		if (ways.Count == 0)
		{
			Console.WriteLine("  the highway: no circling cars found");
			return;
		}
		List<int> portals = new List<int>(ways.Keys);
		portals.Sort();
		Console.WriteLine("  the highway (reported, not checked): " + string.Join(", ", portals.ConvertAll(p => carsOf[p].Count + " cars out of tunnel " + p + ", a lap of " + ways[p].lap.ToString("0") + " units")));
		foreach (double hour in new[] { 6.0, 12.0, 17.0 })
		{
			double w = (hour - 3.0) * 100.0;
			string line = "    " + hour.ToString("00") + ":00 gaps between circling cars along the lap:";
			foreach (int portal in portals)
			{
				List<double> at = new List<double>();
				foreach (int li in carsOf[portal])
				{
					double p = Along(day, li, w, ways[portal]);
					if (p >= 0.0)
					{
						at.Add(p);
					}
				}
				at.Sort();
				double lap = ways[portal].lap;
				List<double> gaps = new List<double>();
				for (int i = 0; i < at.Count; i++)
				{
					gaps.Add(i + 1 < at.Count ? at[i + 1] - at[i] : at[0] + lap - at[i]);
				}
				gaps.Sort();
				int close = gaps.FindAll(g => g < 60.0).Count, empty = gaps.FindAll(g => g > 2000.0).Count;
				double even = lap / Math.Max(1, at.Count);
				line += " out of tunnel " + portal + ": " + at.Count + " driving, even would be " + even.ToString("0") + ", median " + Pick(gaps, 0.5).ToString("0") + ", 10% under " + Pick(gaps, 0.1).ToString("0")
					+ ", 10% over " + Pick(gaps, 0.9).ToString("0") + ", longest " + (gaps.Count > 0 ? gaps[gaps.Count - 1] : 0.0).ToString("0") + "; " + close + " closer than 60 (a queue), " + empty + " over 2000;";
			}
			Console.WriteLine(line);
		}
		// A player driving the first way's lap, starting 1000 units out of its tunnel, at two speeds; cars within reach each second.
		int mine = portals[0];
		(double first, double lap, int leg) route = ways[mine];
		foreach ((string name, double speed) in new[] { ("168 km/h", 168.0 / 3.6 * 3.3), ("85 km/h", 85.0 / 3.6 * 3.3) })
		{
			string line = "    a player on the lap out of tunnel " + mine + " at " + name + ", cars within " + HighwayReach.ToString("0") + " on average (circling same way + oncoming; all moving; parked):";
			foreach (double hour in new[] { 6.0, 12.0, 17.0 })
			{
				double w0 = (hour - 3.0) * 100.0;
				double same = 0.0, oncoming = 0.0, moving = 0.0, parked = 0.0;
				int samples = 0, slow = 0, emptyS = 0;
				for (int t = 0; t < 600; t++)
				{
					double w = w0 + t;
					double p = route.first + (1000.0 + speed * t) % route.lap;
					TrafficDay.Place me = new TrafficDay.Place();
					TrafficDay.OnPath(day.Legs[route.leg], p, ref me);
					int seen = 0;
					foreach (int portal in portals)
					{
						foreach (int li in carsOf[portal])
						{
							TrafficDay.Place c = day.At(day.Legs[li].Car, w % Traffic.DaySeconds);
							if ((c.X - me.X) * (c.X - me.X) + (c.Z - me.Z) * (c.Z - me.Z) < HighwayReach * HighwayReach)
							{
								seen++;
								if (portal == mine)
								{
									same++;
								}
								else
								{
									oncoming++;
								}
							}
						}
					}
					if (seen == 0)
					{
						emptyS++;
					}
					samples++;
					// Every car, every fifth second (3,700 of them).
					if (t % 5 == 0)
					{
						slow++;
						for (int car = 0; car < day.Cars.Count; car++)
						{
							TrafficDay.Place c = day.At(car, w % Traffic.DaySeconds);
							if ((c.X - me.X) * (c.X - me.X) + (c.Z - me.Z) * (c.Z - me.Z) < HighwayReach * HighwayReach)
							{
								if (c.State == TrafficDay.State.Driving)
								{
									moving++;
								}
								else
								{
									parked++;
								}
							}
						}
					}
				}
				line += " " + hour.ToString("00") + ":00 " + (same / samples).ToString("0.0") + " + " + (oncoming / samples).ToString("0.0") + "; " + (moving / slow).ToString("0.0") + "; "
					+ (parked / slow).ToString("0.0") + " (none circling in reach " + (100.0 * emptyS / samples).ToString("0") + "% of the time);";
			}
			Console.WriteLine(line);
		}
	}

	/// <summary>Where circling leg <paramref name="li"/> is along its way's lap at sim time <paramref name="w"/> (0 at its tunnel), or -1 if it is not driving then.</summary>
	private static double Along(TrafficDay day, int li, double w, (double first, double lap, int leg) way)
	{
		TrafficDay.Leg leg = day.Legs[li];
		double t = w;
		if (t < leg.Start || t >= leg.End)
		{
			t += Traffic.DaySeconds;
			if (t < leg.Start || t >= leg.End)
			{
				return -1.0;
			}
		}
		double p = TrafficDay.DistanceAt(leg, t);
		double first = -1.0;
		foreach (TrafficDay.Seg s in leg.Path)
		{
			if (s.Edge == -1 && s.Inbound)
			{
				first = s.P0;
				break;
			}
		}
		double u = (p - first) % way.lap;
		return u < 0.0 ? u + way.lap : u;
	}

	private static double Pick(List<double> sorted, double q)
	{
		return sorted.Count == 0 ? 0.0 : sorted[Math.Min(sorted.Count - 1, (int)(q * sorted.Count))];
	}
}
