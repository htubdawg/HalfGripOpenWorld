using System;
using System.Collections.Generic;
using HalfGripOpenWorld;

// The highway's traffic as a player on it meets it (docs/research/PERFORMANCE.md, "How to find out"): the owner found the trunk "barely" busier
// with 100 circling cars than with 10. Reported, not failed: the gaps between the circling cars along the loop each way at three hours
// (evenly spread, or bunched into platoons with long empty stretches), and how many cars are within the drawn reach (1500 units) of a
// player driving the trunk at 168 km/h and at 85 km/h, averaged over ten minutes from each hour: what "busier" means in numbers.
internal static partial class Program
{
	/// <summary>Moving cars are drawn this near the player (TrafficCars.MovingReach).</summary>
	private const double HighwayReach = 1500.0;

	private static bool Highway(TrafficDay day)
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
			return true;
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
		return PopIn(day, portals, ways);
	}

	/// <summary>
	/// Cars appearing close (the owner, 2026-11-26: "cars pop-in extremely close on the highway, at speed"): a player on each way's lap at
	/// 168 km/h for ten minutes from four hours, the cars to draw chosen every 0.4 s as TrafficCars.Choose does (moving within 1500, parked
	/// within 750, on a tile within the loaded radius), and how near each car is the first time it is chosen after not being chosen.
	/// Arriving at the reach's edge is how it should be; nearer, only out of a tunnel. Fails if a car turns up within 1000 another way.
	/// </summary>
	private static bool PopIn(TrafficDay day, List<int> portals, Dictionary<int, (double first, double lap, int leg)> ways)
	{
		const double Speed = 168.0 / 3.6 * 3.3, Step = 0.4, Near = 1000.0, Parked = 750.0, Tile = 250.0;
		const int Radius = 5;
		bool ok = true;
		foreach (int mine in portals)
		{
			(double first, double lap, int leg) route = ways[mine];
			int appeared = 0, tunnel = 0, close = 0, setOff = 0;
			double setOffNear = double.MaxValue;
			List<double> dists = new List<double>(), parkedDists = new List<double>();
			string firstClose = null;
			foreach (double hour in new[] { 6.0, 12.0, 17.0, 19.0 })
			{
				double w0 = (hour - 3.0) * 100.0;
				Dictionary<int, bool> was = new Dictionary<int, bool>();
				HashSet<int> wasTunnel = new HashSet<int>();
				double lastX = 0.0, lastZ = 0.0;
				for (int k = 0; k * Step < 600.0; k++)
				{
					double t = k * Step, w = (w0 + t) % Traffic.DaySeconds;
					TrafficDay.Place me = new TrafficDay.Place();
					TrafficDay.OnPath(day.Legs[route.leg], route.first + (1000.0 + Speed * t) % route.lap, ref me);
					int mx = (int)Math.Floor(me.X / Tile), mz = (int)Math.Floor(me.Z / Tile);
					// Through the wrap (the far end's cars come into reach at once, seen from the tunnel's dark): start afresh there.
					bool wrapped = k > 0 && (me.X - lastX) * (me.X - lastX) + (me.Z - lastZ) * (me.Z - lastZ) > 4.0 * Speed * Step * Speed * Step;
					lastX = me.X;
					lastZ = me.Z;
					Dictionary<int, bool> now = new Dictionary<int, bool>();
					HashSet<int> inTunnel = new HashSet<int>();
					for (int c = 0; c < day.Cars.Count; c++)
					{
						TrafficDay.Place at = day.At(c, w);
						double x, z, reach;
						if (at.State == TrafficDay.State.Driving)
						{
							if (at.InTunnel)
							{
								inTunnel.Add(c);
								continue;
							}
							x = at.X;
							z = at.Z;
							reach = HighwayReach;
						}
						else if (at.State == TrafficDay.State.Parked)
						{
							TrafficParking.Spot spot = day.Parking.Parked(c, at.Leg);
							if (spot.Away)
							{
								continue;
							}
							x = spot.X;
							z = spot.Z;
							reach = Parked;
						}
						else
						{
							continue;
						}
						double dx = x - me.X, dz = z - me.Z;
						int tx = (int)Math.Floor(x / Tile) - mx, tz = (int)Math.Floor(z / Tile) - mz;
						if (dx * dx + dz * dz >= reach * reach || tx * tx + tz * tz > (Radius + 0.5) * (Radius + 0.5))
						{
							continue;
						}
						now[c] = true;
						if (k == 0 || wrapped || was.ContainsKey(c))
						{
							continue;
						}
						appeared++;
						double d = Math.Sqrt(dx * dx + dz * dz);
						TrafficDay.Place before = day.At(c, (w - Step + Traffic.DaySeconds) % Traffic.DaySeconds);
						bool jumped = before.State == TrafficDay.State.Driving && at.State == TrafficDay.State.Driving
							&& (before.X - at.X) * (before.X - at.X) + (before.Z - at.Z) * (before.Z - at.Z) > 200.0 * 200.0;
						if (wasTunnel.Contains(c) || jumped)
						{
							// Through a tunnel's dark between two choices (at speed it is in it for less than one).
							tunnel++;
							continue;
						}
						if (at.State == TrafficDay.State.Driving && before.State != TrafficDay.State.Driving)
						{
							// Set off from a space past the parked cars' reach (or out of the world): where it is.
							setOff++;
							setOffNear = Math.Min(setOffNear, d);
							continue;
						}
						if (at.State == TrafficDay.State.Parked)
						{
							parkedDists.Add(d);
							if (d >= Parked - 150.0)
							{
								continue;
							}
						}
						else
						{
							dists.Add(d);
						}
						if (d < (at.State == TrafficDay.State.Parked ? Parked - 150.0 : Near))
						{
							close++;
							if (Environment.GetEnvironmentVariable("TV_POPLIST") != null)
							{
								TrafficDay.Place prev = day.At(c, (w - Step + Traffic.DaySeconds) % Traffic.DaySeconds);
								Console.WriteLine("      near: car " + c + " " + at.State + " leg " + at.Leg + " T " + at.T.ToString("0.0") + " P " + at.P.ToString("0") + " edge " + at.Edge + " at (" + x.ToString("0") + ", " + z.ToString("0") + ") d " + d.ToString("0")
									+ "; 0.4 s before " + prev.State + " leg " + prev.Leg + " at (" + prev.X.ToString("0") + ", " + prev.Z.ToString("0") + ") tunnel " + prev.InTunnel + "; player (" + me.X.ToString("0") + ", " + me.Z.ToString("0") + ") " + (3.0 + w / 100.0).ToString("0.00") + " h");
							}
							firstClose ??= "car " + c + " " + at.State + " at (" + x.ToString("0") + ", " + z.ToString("0") + "), " + d.ToString("0") + " from the player at (" + me.X.ToString("0") + ", " + me.Z.ToString("0") + "), " + (3.0 + w / 100.0).ToString("0.00") + " h";
						}
					}
					was = now;
					wasTunnel = inTunnel;
				}
			}
			dists.Sort();
			parkedDists.Sort();
			Console.WriteLine("    cars appearing round a player on the lap out of tunnel " + mine + " at 168 km/h (chosen every 0.4 s as the game does): " + appeared + ", " + tunnel + " out of a tunnel, " + setOff
				+ " setting off" + (setOff > 0 ? " (nearest " + setOffNear.ToString("0") + ")" : "") + "; moving cars first chosen at "
				+ (dists.Count > 0 ? "10% " + Pick(dists, 0.1).ToString("0") + ", median " + Pick(dists, 0.5).ToString("0") + ", nearest " + dists[0].ToString("0") : "-") + ", parked at "
				+ (parkedDists.Count > 0 ? "10% " + Pick(parkedDists, 0.1).ToString("0") + ", nearest " + parkedDists[0].ToString("0") : "-") + "; " + close + " too near (moving within " + Near.ToString("0")
				+ ", parked within " + (Parked - 150.0).ToString("0") + ")" + (firstClose != null ? " (first: " + firstClose + ")" : "") + (close == 0 ? "  ok" : "  FAIL"));
			ok &= close == 0;
		}
		return ok;
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
