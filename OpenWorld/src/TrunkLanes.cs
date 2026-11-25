using System;
using System.Collections.Generic;

namespace HalfGripOpenWorld
{
	/// <summary>
	/// The trunk's junction lanes and median, worked out once from the layout (pure, deterministic, no Unity types). The trunk is a rural
	/// divided highway with at-grade junctions (the owner, 2026-11-25: no overpasses; lights where needed; "the median is not meant to be
	/// used as a lane"):
	///   - a grass median 24 wide (about 7 m), flush with the road, so a player may cross it anywhere into the oncoming carriageway;
	///   - at each side road, the median paved over for the opening, and a dedicated left-turn bay cut into it before the junction for
	///     the carriageway whose traffic turns left there: a car turning left moves into it, slows and waits there out of the through lanes
	///     (at a junction with a light, for the green arrow: <see cref="TrafficSignals"/>; else for a gap in the oncoming traffic);
	///   - no deceleration or acceleration lanes on the shoulder and no median lanes to merge from: a car turning right slows in the outer
	///     lane, one coming out of a side road waits at its line for a gap and crosses in one go (never standing in the median);
	///   - K-rails (a concrete barrier down the middle) on a few stretches well away from any junction (<see cref="Rails"/>).
	/// Each direction's bay is on its own side of the centre line, so a crossroads' two bays sit back to back. The lanes are paint over the
	/// trunk's drawn median: <see cref="RoadMesh"/> draws them (and the rails), <see cref="TrafficDay"/> drives them.
	/// </summary>
	internal static class TrunkLanes
	{
		/// <summary>The kind of every junction lane: a left-turn bay in the median (the shoulder lanes, kind 0, are gone).</summary>
		internal const int Median = 1;

		// ---- the cross-section (from the centre line out; the road is 80 across, as RoadMesh's texture) ----

		/// <summary>Half the median (grass, flush): 24 across.</summary>
		internal const double MedianHalf = 12.0;
		/// <summary>The paved shoulder between the median and the inner lane.</summary>
		internal const double InnerShoulder = 2.0;
		/// <summary>A lane's width.</summary>
		internal const double LaneWidth = 11.0;
		/// <summary>The middle of the inner lane, of the outer lane, and the outer edge line, from the centre line.</summary>
		internal const double InnerMid = MedianHalf + InnerShoulder + LaneWidth * 0.5, OuterMid = InnerMid + LaneWidth, EdgeLine = OuterMid + LaneWidth * 0.5;
		/// <summary>The middle of a carriageway (between its two lanes), from the centre line.</summary>
		internal const double Carriageway = MedianHalf + InnerShoulder + LaneWidth;
		/// <summary>A left-turn bay's inner edge (beside the inner lane, where the yellow edge line was) and outer edge (by the centre line), and its middle.</summary>
		internal const double BayIn = MedianHalf + 1.0, BayOut = BayIn - LaneWidth, BayMid = (BayIn + BayOut) * 0.5;

		/// <summary>A junction lane: on trunk road <see cref="Road"/>, for traffic going <see cref="Dir"/> (+1 the way its distance grows), from <see cref="S0"/> to <see cref="S1"/> (S0 &lt; S1).</summary>
		internal struct Zone
		{
			public int Road, Kind, Dir;
			public double S0, S1;
		}

		/// <summary>A side road meeting the trunk: where along it, and on which side (+1 left of the way its distance grows, -1 right; 0 both, a crossroads).</summary>
		internal struct Junction
		{
			public int Road, Side, Other;
			public double S;
		}

		/// <summary>A stretch of K-rail down the middle of trunk road <see cref="Road"/>, from <see cref="S0"/> to <see cref="S1"/>.</summary>
		internal struct Rail
		{
			public int Road;
			public double S0, S1;
		}

		internal static readonly List<Zone> Zones = new List<Zone>();
		internal static readonly List<Junction> Junctions = new List<Junction>();
		internal static readonly List<Rail> Rails = new List<Rail>();

		/// <summary>How far from the junction's middle a bay ends (the median opening is paved this far each way): past the side road's mouth.</summary>
		internal const double Mouth = 45.0;
		/// <summary>The left-turn bay: 55 mph (81 units a second) down to a turn's 13 at the traffic's braking (8.25, about 2.5 m/s²) takes
		/// about 390, partly in the taper; with room for about five cars waiting (5 x 21), as a rural signalised junction's bay.</summary>
		internal const double TurnLane = 520.0;
		/// <summary>The taper at a bay's far end, drawn as the bay's edge moving out from the inner lane's edge.</summary>
		internal const double Taper = 100.0;
		/// <summary>No junction lanes this near a trunk road's ends (its tunnels, where the highway wraps round).</summary>
		private const double EndClear = 400.0;
		/// <summary>Roads crossing more than this apart in height pass over or under (no junction), as in <see cref="Traffic"/>.</summary>
		private const double OverPass = 4.0;
		/// <summary>K-rail is kept this far from any junction and the trunk's ends; a stretch shorter than <see cref="RailShortest"/> gets none, one longer is cut to <see cref="RailLongest"/>.</summary>
		internal const double RailClear = 450.0, RailShortest = 400.0, RailLongest = 1600.0;
		/// <summary>K-rail is kept this far from a bay's end.</summary>
		private const double RailBayGap = 60.0;

		/// <summary>How far left of a carriageway's outer lane the middle of its left-turn bay is, in lane widths (the outer lane 0, the inner 1).</summary>
		internal const double MedianLane = (OuterMid - BayMid) / LaneWidth;

		/// <summary>Works out the junctions and lanes from the layout. Called once, at the end of <see cref="Network.Ensure"/>.</summary>
		internal static void Build()
		{
			Zones.Clear();
			Junctions.Clear();
			Rails.Clear();
			for (int t = 0; t < Network.Roads.Count; t++)
			{
				Network.Road trunk = Network.Roads[t];
				if (trunk.Kind != RoadKind.Trunk || trunk.Count < 2)
				{
					continue;
				}
				List<Junction> found = Find(t);
				Lanes(t, found);
				Barriers(t, found);
				Junctions.AddRange(found);
			}
		}

		/// <summary>The side roads meeting trunk road <paramref name="t"/> (ending on it, or crossing it at its height), in order along it.</summary>
		private static List<Junction> Find(int t)
		{
			Network.Road trunk = Network.Roads[t];
			double length = trunk.S[trunk.Count - 1];
			List<Junction> list = new List<Junction>();
			// The trunk's pieces in chunks, each with its box, so the search for crossings skips most of it.
			var boxes = new (double x0, double x1, double z0, double z1)[(trunk.Count + Chunk - 1) / Chunk];
			for (int c = 0; c < boxes.Length; c++)
			{
				double x0 = double.MaxValue, x1 = double.MinValue, z0 = double.MaxValue, z1 = double.MinValue;
				for (int k = c * Chunk; k <= Math.Min(trunk.Count - 1, (c + 1) * Chunk); k++)
				{
					x0 = Math.Min(x0, trunk.X[k]);
					x1 = Math.Max(x1, trunk.X[k]);
					z0 = Math.Min(z0, trunk.Z[k]);
					z1 = Math.Max(z1, trunk.Z[k]);
				}
				boxes[c] = (x0, x1, z0, z1);
			}
			for (int r = 0; r < Network.Roads.Count; r++)
			{
				Network.Road road = Network.Roads[r];
				if (r == t || road.Count < 2 || road.Kind > RoadKind.Lane || road.Kind == RoadKind.Trunk)
				{
					continue;
				}
				foreach (int end in new[] { 0, road.Count - 1 })
				{
					if (!Nearest(trunk, road.X[end], road.Z[end], out double s, out double d, out double y) || d > trunk.Half + 3.0 || Math.Abs(y - road.Y[end]) > OverPass)
					{
						continue;
					}
					// Which side the road lies on: a point well into it, against the trunk's way there.
					double into = trunk.Half + 25.0;
					Network.Locate(road, end == 0 ? Math.Min(road.S[road.Count - 1], into) : Math.Max(0.0, road.S[road.Count - 1] - into), out int i, out double f);
					double px = road.X[i] + (road.X[i + 1] - road.X[i]) * f, pz = road.Z[i] + (road.Z[i + 1] - road.Z[i]) * f;
					Network.Locate(trunk, s, out int k, out double g);
					double tx = trunk.X[k + 1] - trunk.X[k], tz = trunk.Z[k + 1] - trunk.Z[k];
					double cx = trunk.X[k] + tx * g, cz = trunk.Z[k] + tz * g;
					double cross = tx * (pz - cz) - tz * (px - cx);
					Add(list, t, r, s, cross > 0.0 ? 1 : -1, length);
				}
				// Crossing it at grade (a crossroads): both sides.
				for (int i = 0; i + 1 < road.Count; i++)
				{
					double rx0 = Math.Min(road.X[i], road.X[i + 1]), rx1 = Math.Max(road.X[i], road.X[i + 1]);
					double rz0 = Math.Min(road.Z[i], road.Z[i + 1]), rz1 = Math.Max(road.Z[i], road.Z[i + 1]);
					for (int k = 0; k + 1 < trunk.Count; k++)
					{
						if ((k & (Chunk - 1)) == 0)
						{
							int c = k / Chunk;
							if (rx1 < boxes[c].x0 || rx0 > boxes[c].x1 || rz1 < boxes[c].z0 || rz0 > boxes[c].z1)
							{
								k += Chunk - 1;
								continue;
							}
						}
						if (Cross(road, i, trunk, k, out double fa, out double fb))
						{
							double ya = road.Y[i] + (road.Y[i + 1] - road.Y[i]) * fa, yb = trunk.Y[k] + (trunk.Y[k + 1] - trunk.Y[k]) * fb;
							double sa = road.S[i] + (road.S[i + 1] - road.S[i]) * fa;
							if (Math.Abs(ya - yb) <= OverPass && sa > trunk.Half && sa < road.S[road.Count - 1] - trunk.Half)
							{
								Add(list, t, r, trunk.S[k] + (trunk.S[k + 1] - trunk.S[k]) * fb, 0, length);
							}
						}
					}
				}
			}
			// In order along the trunk; ties by road index (a stable sort by an integer key keeps every machine's order the same).
			list.Sort((a, b) => a.S != b.S ? a.S.CompareTo(b.S) : a.Other.CompareTo(b.Other));
			// A road meeting it from both sides at one place is a crossroads.
			for (int i = 0; i + 1 < list.Count; i++)
			{
				if (list[i + 1].S - list[i].S < 30.0 && list[i].Side != list[i + 1].Side)
				{
					Junction j = list[i];
					j.Side = 0;
					list[i] = j;
					list.RemoveAt(i + 1);
				}
			}
			return list;
		}

		private const int Chunk = 32;

		private static void Add(List<Junction> list, int t, int r, double s, int side, double length)
		{
			if (s < EndClear || s > length - EndClear)
			{
				return;
			}
			foreach (Junction j in list)
			{
				if (j.Other == r && Math.Abs(j.S - s) < 30.0 && (j.Side == side || j.Side == 0))
				{
					return;
				}
			}
			list.Add(new Junction { Road = t, Other = r, S = s, Side = side });
		}

		/// <summary>The nearest point of the trunk's line to (x, z): its distance along, how far off, and its height there.</summary>
		private static bool Nearest(Network.Road trunk, double x, double z, out double s, out double d, out double y)
		{
			s = 0.0;
			d = double.MaxValue;
			y = 0.0;
			for (int k = 0; k + 1 < trunk.Count; k++)
			{
				// Cheap reject: both ends far away.
				double ax = trunk.X[k] - x, az = trunk.Z[k] - z;
				if (Math.Abs(ax) > 400.0 || Math.Abs(az) > 400.0)
				{
					continue;
				}
				Lines.Project(trunk.X, trunk.Z, k, x, z, out double f, out double dd);
				if (dd < d)
				{
					d = dd;
					s = trunk.S[k] + (trunk.S[k + 1] - trunk.S[k]) * f;
					y = trunk.Y[k] + (trunk.Y[k + 1] - trunk.Y[k]) * f;
				}
			}
			return d < double.MaxValue;
		}

		/// <summary>Whether piece i of road a and piece k of road b cross, and where along each (fractions).</summary>
		private static bool Cross(Network.Road a, int i, Network.Road b, int k, out double fa, out double fb)
		{
			fa = fb = 0.0;
			double ax = a.X[i], az = a.Z[i], adx = a.X[i + 1] - ax, adz = a.Z[i + 1] - az;
			double bx = b.X[k], bz = b.Z[k], bdx = b.X[k + 1] - bx, bdz = b.Z[k + 1] - bz;
			if (Math.Max(ax, ax + adx) < Math.Min(bx, bx + bdx) || Math.Max(bx, bx + bdx) < Math.Min(ax, ax + adx)
				|| Math.Max(az, az + adz) < Math.Min(bz, bz + bdz) || Math.Max(bz, bz + bdz) < Math.Min(az, az + adz))
			{
				return false;
			}
			double den = adx * bdz - adz * bdx;
			if (Math.Abs(den) < 1e-9)
			{
				return false;
			}
			fa = ((bx - ax) * bdz - (bz - az) * bdx) / den;
			fb = ((bx - ax) * adz - (bz - az) * adx) / den;
			return fa >= 0.0 && fa < 1.0 && fb >= 0.0 && fb < 1.0;
		}

		/// <summary>
		/// The left-turn bays for trunk road <paramref name="t"/>'s junctions: each back from its junction's mouth, but never past the
		/// mouth of the junction before it (a bay belongs to one junction; where two are close, the bay is as long as the stretch between).
		/// </summary>
		private static void Lanes(int t, List<Junction> found)
		{
			Network.Road trunk = Network.Roads[t];
			double length = trunk.S[trunk.Count - 1];
			foreach (Junction j in found)
			{
				for (int side = -1; side <= 1; side += 2)
				{
					if (j.Side != 0 && j.Side != side)
					{
						continue;
					}
					// Traffic keeps right: a side road left of S's way (+1) is a left turn for the cars going S's way (+1), one on the
					// right (-1) for those going the other way.
					Zone z = Before(t, Median, side, j.S, TurnLane);
					foreach (Junction o in found)
					{
						if (side > 0 && o.S < j.S - 1.0)
						{
							z.S0 = Math.Max(z.S0, o.S + Mouth);
						}
						else if (side < 0 && o.S > j.S + 1.0)
						{
							z.S1 = Math.Min(z.S1, o.S - Mouth);
						}
					}
					Clamp(z, length);
				}
			}
		}

		/// <summary>
		/// K-rail on trunk road <paramref name="t"/>: on about half the stretches between junctions long enough to hold one well
		/// clear of both (and of the tunnels), in the middle of the stretch, so the median stays open to drive across almost everywhere.
		/// Which stretches is from the trunk's own index and the stretch's (deterministic).
		/// </summary>
		private static void Barriers(int t, List<Junction> found)
		{
			Network.Road trunk = Network.Roads[t];
			double length = trunk.S[trunk.Count - 1];
			List<double> stops = new List<double> { EndClear - RailClear };
			foreach (Junction j in found)
			{
				stops.Add(j.S);
			}
			stops.Add(length - EndClear + RailClear);
			for (int k = 0; k + 1 < stops.Count; k++)
			{
				double a = stops[k] + RailClear, b = stops[k + 1] - RailClear;
				// Kept off the bays (and their tapers) at either end, with a little room to swing out into one.
				foreach (Zone z in Zones)
				{
					if (z.Road != t || z.S1 <= a - RailBayGap || z.S0 >= b + RailBayGap)
					{
						continue;
					}
					if (z.S0 + z.S1 < a + b)
					{
						a = Math.Max(a, z.S1 + RailBayGap);
					}
					else
					{
						b = Math.Min(b, z.S0 - RailBayGap);
					}
				}
				if (b - a < RailShortest || (Noise.Mix((ulong)(t * 7919 + k * 104729 + 17)) % 2UL) != 0UL)
				{
					continue;
				}
				double mid = (a + b) * 0.5, half = Math.Min(b - a, RailLongest) * 0.5;
				Rails.Add(new Rail { Road = t, S0 = mid - half, S1 = mid + half });
			}
		}

		/// <summary>Whether K-rail stands down trunk road <paramref name="road"/>'s middle at distance <paramref name="s"/> along it.</summary>
		internal static bool RailAt(int road, double s)
		{
			foreach (Rail r in Rails)
			{
				if (r.Road == road && s >= r.S0 && s <= r.S1)
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>Whether the median is paved at distance <paramref name="s"/> along trunk road <paramref name="road"/>: a junction's opening.</summary>
		internal static bool Opening(int road, double s)
		{
			foreach (Junction j in Junctions)
			{
				if (j.Road == road && Math.Abs(s - j.S) <= Mouth)
				{
					return true;
				}
			}
			return false;
		}

		private static void Clamp(Zone z, double length)
		{
			z.S0 = Math.Max(EndClear * 0.5, z.S0);
			z.S1 = Math.Min(length - EndClear * 0.5, z.S1);
			if (z.S1 - z.S0 > Taper * 2.0)
			{
				Zones.Add(z);
			}
		}

		/// <summary>A lane of <paramref name="length"/> ending at a junction at <paramref name="s"/>, for traffic going <paramref name="d"/>.</summary>
		private static Zone Before(int t, int kind, int d, double s, double length)
		{
			return d > 0 ? new Zone { Road = t, Kind = kind, Dir = d, S0 = s - Mouth - length, S1 = s - Mouth } : new Zone { Road = t, Kind = kind, Dir = d, S0 = s + Mouth, S1 = s + Mouth + length };
		}

		/// <summary>A lane of <paramref name="length"/> starting at a junction at <paramref name="s"/>, for traffic going <paramref name="d"/>.</summary>
		private static Zone After(int t, int kind, int d, double s, double length)
		{
			return d > 0 ? new Zone { Road = t, Kind = kind, Dir = d, S0 = s + Mouth, S1 = s + Mouth + length } : new Zone { Road = t, Kind = kind, Dir = d, S0 = s - Mouth - length, S1 = s - Mouth };
		}

		/// <summary>The junction lane of <paramref name="kind"/> on trunk road <paramref name="road"/> for traffic going <paramref name="dir"/> at distance <paramref name="s"/> along it, or -1.</summary>
		internal static int Find(int road, int kind, int dir, double s)
		{
			for (int i = 0; i < Zones.Count; i++)
			{
				Zone z = Zones[i];
				if (z.Road == road && z.Kind == kind && z.Dir == dir && s >= z.S0 && s <= z.S1)
				{
					return i;
				}
			}
			return -1;
		}

		/// <summary>Where junction lane <paramref name="zone"/> ends for a car going its way (its far end).</summary>
		internal static double EndOf(int zone)
		{
			Zone z = Zones[zone];
			return z.Dir > 0 ? z.S1 : z.S0;
		}

		/// <summary>Where it begins for a car going its way.</summary>
		internal static double StartOf(int zone)
		{
			Zone z = Zones[zone];
			return z.Dir > 0 ? z.S0 : z.S1;
		}
	}
}
