using System;
using System.Collections.Generic;
using HalfGripOpenWorld;

// Contact physics' touch test (TrafficDrive.Sweeps) without the game: a few cases worked out by hand, then every car driving its lane at
// the evening rush taken as the player and swept past the parked cars as TrafficPhysics.Contact sweeps the player's car past the drawn
// ones (0.3 s and 2 units ahead alone or on the host, 0.5 s on a room's client). A car keeping to its lane should not be about to touch a
// parked car: each such touch would make a loose car for nothing (a body taken, a physics car more), and would show the parked spots
// are too near the lanes.
internal static partial class Program
{
	/// <summary>The player's box in the touch test and the drawn car's (half sizes, units), and the margin, as TrafficPhysics has them.</summary>
	private const double TouchPlayerL = 7.5, TouchPlayerW = 2.7, TouchCarL = 7.1, TouchCarW = 2.65, TouchMargin = 0.2;
	/// <summary>The share of the cars driving their lanes that may sweep a parked car: none (a parked car in a lane is in the way; until 2026-11-26, 2% passed).</summary>
	private const double MostTouchingShare = 0.0;

	private static bool Contact(TrafficDay day)
	{
		bool ok = true;
		// By hand: the player at the origin facing +z.
		(string what, bool want, bool got)[] cases =
		{
			("a parked car 15 ahead at 30 units/s", true, TrafficDrive.Sweeps(0, 0, 0, 1, TouchPlayerL, TouchPlayerW, 0, 30, 0.3, 2, 0, 15, 0, 1, TouchCarL, TouchCarW, TouchMargin)),
			("a parked car 40 ahead at 30 units/s", false, TrafficDrive.Sweeps(0, 0, 0, 1, TouchPlayerL, TouchPlayerW, 0, 30, 0.3, 2, 0, 40, 0, 1, TouchCarL, TouchCarW, TouchMargin)),
			("a car 25 ahead at 30 units/s, 0.5 s ahead", true, TrafficDrive.Sweeps(0, 0, 0, 1, TouchPlayerL, TouchPlayerW, 0, 30, 0.5, 2, 0, 25, 0, 1, TouchCarL, TouchCarW, TouchMargin)),
			("standing, a car side by side 5.5 off", true, TrafficDrive.Sweeps(0, 0, 0, 1, TouchPlayerL, TouchPlayerW, 0, 0, 0.3, 2, 5.5, 0, 0, 1, TouchCarL, TouchCarW, TouchMargin)),
			("standing, a car side by side 7 off", false, TrafficDrive.Sweeps(0, 0, 0, 1, TouchPlayerL, TouchPlayerW, 0, 0, 0.3, 2, 7, 0, 0, 1, TouchCarL, TouchCarW, TouchMargin)),
			("passing a car at a narrow street's kerb 5.8 off at 30 units/s", false, TrafficDrive.Sweeps(0, 0, 0, 1, TouchPlayerL, TouchPlayerW, 0, 30, 0.3, 2, 5.8, 10, 0, 1, TouchCarL, TouchCarW, TouchMargin)),
			("a car across the road 14 ahead", true, TrafficDrive.Sweeps(0, 0, 0, 1, TouchPlayerL, TouchPlayerW, 0, 20, 0.3, 2, 0, 14, 1, 0, TouchCarL, TouchCarW, TouchMargin)),
			("a car across the road 12 behind, the player going away", false, TrafficDrive.Sweeps(0, 0, 0, 1, TouchPlayerL, TouchPlayerW, 0, 20, 0.3, 2, 0, -12, 1, 0, TouchCarL, TouchCarW, TouchMargin)),
			("sliding sideways into a car 8 to the right", true, TrafficDrive.Sweeps(0, 0, 0, 1, TouchPlayerL, TouchPlayerW, 15, 0, 0.3, 2, 8, 0, 0, 1, TouchCarL, TouchCarW, TouchMargin)),
		};
		int wrong = 0;
		foreach ((string what, bool want, bool got) in cases)
		{
			if (want != got)
			{
				wrong++;
				Console.WriteLine("  contact: " + what + ": " + (got ? "touches" : "doesn't") + ", should " + (want ? "touch" : "not") + "  FAIL");
			}
		}
		Console.WriteLine("  contact: the touch test on " + cases.Length + " cases worked out by hand, " + wrong + " wrong" + (wrong == 0 ? "  ok" : "  FAIL"));
		ok &= wrong == 0;

		TrafficParking parking = day.Parking;
		double w = TrafficDay.SimOfHour(17.0);
		List<(double x, double z, double hx, double hz)> parked = new List<(double, double, double, double)>();
		List<(double x, double z, double hx, double hz, double v)> driving = new List<(double, double, double, double, double)>();
		for (int c = 0; c < day.Cars.Count; c++)
		{
			TrafficDay.Place at = day.At(c, w);
			if (at.State == TrafficDay.State.Parked)
			{
				TrafficParking.Spot s = parking.Parked(c, at.Leg);
				if (!s.Away)
				{
					parked.Add((s.X, s.Z, s.Hx, s.Hz));
				}
				continue;
			}
			if (at.State != TrafficDay.State.Driving || at.InTunnel || at.Leg < 0)
			{
				continue;
			}
			TrafficDay.Leg leg = day.Legs[at.Leg];
			// Only cars on their lanes: one pulling in or out comes up to the parked cars by its space, as the player parking would.
			if ((!leg.Appears && at.T < leg.PullOut + 0.5) || (!leg.Vanishes && leg.End - leg.Start - at.T < leg.PullIn + 0.5))
			{
				continue;
			}
			parking.Drawn(c, at, out double x, out double z, out double hx, out double hz);
			driving.Add((x, z, hx, hz, Math.Max(0.0, at.Speed)));
		}
		// The parked cars in cells of 64, for the nearby ones.
		Dictionary<(int, int), List<int>> cells = new Dictionary<(int, int), List<int>>();
		for (int i = 0; i < parked.Count; i++)
		{
			(int, int) k = ((int)Math.Floor(parked[i].x / 64.0), (int)Math.Floor(parked[i].z / 64.0));
			if (!cells.TryGetValue(k, out List<int> l))
			{
				cells[k] = l = new List<int>();
			}
			l.Add(i);
		}
		foreach (double ahead in new[] { 0.3, 0.5 })
		{
			int touching = 0, pairs = 0;
			double worstX = 0.0, worstZ = 0.0;
			foreach ((double x, double z, double hx, double hz, double v) d in driving)
			{
				int cx = (int)Math.Floor(d.x / 64.0), cz = (int)Math.Floor(d.z / 64.0);
				bool any = false;
				for (int i = cx - 1; i <= cx + 1; i++)
				{
					for (int j = cz - 1; j <= cz + 1; j++)
					{
						if (!cells.TryGetValue((i, j), out List<int> l))
						{
							continue;
						}
						foreach (int k in l)
						{
							(double x, double z, double hx, double hz) p = parked[k];
							if (TrafficDrive.Sweeps(d.x, d.z, d.hx, d.hz, TouchPlayerL, TouchPlayerW, d.hx * d.v, d.hz * d.v, ahead, 2.0, p.x, p.z, p.hx, p.hz, TouchCarL, TouchCarW, TouchMargin))
							{
								pairs++;
								if (Environment.GetEnvironmentVariable("TV_CONTACT_LIST") != null)
								{
									double ex = p.x - d.x, ez = p.z - d.z;
									Console.WriteLine("    " + d.x.ToString("0") + ", " + d.z.ToString("0") + " at " + d.v.ToString("0") + ": parked " + (ex * d.hx + ez * d.hz).ToString("0.0") + " ahead, " + (ex * d.hz - ez * d.hx).ToString("0.0") + " to the side, facing " + (Math.Acos(Math.Max(-1, Math.Min(1, d.hx * p.hx + d.hz * p.hz))) * 57.3).ToString("0") + " deg off");
								}
								if (!any)
								{
									worstX = d.x;
									worstZ = d.z;
								}
								any = true;
							}
						}
					}
				}
				touching += any ? 1 : 0;
			}
			double share = driving.Count > 0 ? touching / (double)driving.Count : 0.0;
			Console.WriteLine("  contact: " + driving.Count + " cars driving their lanes at 17:00 swept " + ahead.ToString("0.0") + " s ahead past " + parked.Count + " parked: " + touching + " would touch one ("
				+ (share * 100.0).ToString("0.0") + "%, " + pairs + " touches" + (touching > 0 ? ", one at " + worstX.ToString("0") + ", " + worstZ.ToString("0") : "") + ")" + (share <= MostTouchingShare ? "  ok" : "  FAIL"));
			ok &= share <= MostTouchingShare;
		}
		return ok;
	}
}
