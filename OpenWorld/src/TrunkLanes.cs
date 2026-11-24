using System;
using System.Collections.Generic;

namespace HalfGripOpenWorld
{
	/// <summary>
	/// The trunk's junction lanes, worked out once from the layout (pure, deterministic, no Unity types). A dual carriageway carrying
	/// fast traffic past side roads has them so nobody turning holds up the cars going on (the owner's design, after "cars cutting across
	/// the entire highway"; the PA Driver's Manual's rules for them, pp. 45 to 52):
	///   - a deceleration lane on the right before a side road, in the outer shoulder: a car turning right moves into it, then slows
	///     ("Do not slow down until after you have moved into the exit lane");
	///   - an acceleration lane on the right after it: a car turning right out of the side road comes up to speed there, then merges
	///     ("Accelerate up to the speed of traffic. Merge into the opening in traffic");
	///   - a left-turn lane in the median before it, for the oncoming carriageway: a car turning left waits there for a gap, out of the
	///     through lanes ("enter it completely");
	///   - a median lane after it for the cars turning left out of the side road: they cross the near carriageway, then wait and come
	///     up to speed in the median before merging into the far one ("cross the 'first' roadway ... then turn left on the other side").
	/// Where two junctions' lanes overlap, they join into one (a continuous auxiliary lane between close interchanges and junctions,
	/// as real roads are built); where two median lanes of opposite directions meet, the median is shared out between them (back to
	/// back left-turn lanes), a median lane too short to merge from is left out (a left from that side road crosses in one go, as at
	/// a crossroads, where the median is the two directions' turn lanes).
	/// The outer shoulder is a lane wide all along the trunk (a paved shoulder, as on Pennsylvania's rural four-lane roads), so the lanes
	/// are paint: <see cref="RoadMesh"/> draws them, <see cref="TrafficDay"/> drives them.
	/// </summary>
	internal static class TrunkLanes
	{
		internal const int Aux = 0, Median = 1;

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

		internal static readonly List<Zone> Zones = new List<Zone>();
		internal static readonly List<Junction> Junctions = new List<Junction>();

		/// <summary>How far from the junction's middle a lane starts or ends: past the side road's mouth.</summary>
		internal const double Mouth = 45.0;
		/// <summary>
		/// The deceleration lane: 55 mph (81 units a second) down to a turn's 13 at the traffic's braking (8.25, about 2.5 m/s²) takes
		/// about 390; with the taper to move over in.
		/// </summary>
		internal const double Decel = 520.0;
		/// <summary>The acceleration lane: from a turn up to the trunk's speed at the traffic's 5 (about 1.5 m/s²) takes about 560; with its taper.</summary>
		internal const double Accel = 680.0;
		/// <summary>The left-turn lane: slowing as in the deceleration lane, and room for about five cars waiting (5 x 21).</summary>
		internal const double TurnLane = 620.0;
		/// <summary>The shortest median lane a car could come up to speed in and merge from: shorter, it is left out.</summary>
		internal const double ShortestMerge = 380.0;
		/// <summary>The taper at a lane's ends, drawn as the edge line moving out (or the median opening).</summary>
		internal const double Taper = 100.0;
		/// <summary>Two lanes of one kind this close end to end are joined.</summary>
		private const double JoinGap = 200.0;
		/// <summary>No junction lanes this near a trunk road's ends (its tunnels, where the highway wraps round).</summary>
		private const double EndClear = 400.0;
		/// <summary>Roads crossing more than this apart in height pass over or under (no junction), as in <see cref="Traffic"/>.</summary>
		private const double OverPass = 4.0;

		/// <summary>How far left of a carriageway's outer lane the middle of a lane is, in lane widths (11): the outer lane 0, the inner 1, the median lane (on the centre line) and the deceleration or acceleration lane (in the outer shoulder, its middle 34.5 out).</summary>
		internal const double MedianLane = (5.0 + 2.0 + 11.0 + 5.5) / 11.0, AuxLane = -1.0;

		/// <summary>Works out the junctions and lanes from the layout. Called once, at the end of <see cref="Network.Ensure"/>.</summary>
		internal static void Build()
		{
			Zones.Clear();
			Junctions.Clear();
			for (int t = 0; t < Network.Roads.Count; t++)
			{
				Network.Road trunk = Network.Roads[t];
				if (trunk.Kind != RoadKind.Trunk || trunk.Count < 2)
				{
					continue;
				}
				List<Junction> found = Find(t);
				Lanes(t, found);
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

		/// <summary>The lanes for trunk road <paramref name="t"/>'s junctions, joined and shared out as the class summary says.</summary>
		private static void Lanes(int t, List<Junction> found)
		{
			Network.Road trunk = Network.Roads[t];
			double length = trunk.S[trunk.Count - 1];
			List<Zone> aux = new List<Zone>(), turn = new List<Zone>(), merge = new List<Zone>();
			foreach (Junction j in found)
			{
				for (int side = -1; side <= 1; side += 2)
				{
					if (j.Side != 0 && j.Side != side)
					{
						continue;
					}
					// The carriageway beside this side: traffic keeps right, so the right side (-1) is the one driven the way S grows.
					int d = -side;
					aux.Add(Before(t, Aux, d, j.S, Decel));
					aux.Add(After(t, Aux, d, j.S, Accel));
					turn.Add(Before(t, Median, -d, j.S, TurnLane));
					if (j.Side != 0)
					{
						merge.Add(After(t, Median, -d, j.S, Accel));
					}
				}
			}
			List<Zone> median = Join(turn, found);
			// Back to back left-turn lanes of the two ways: the median shared out at the middle of their overlap.
			Share(median);
			// The median lanes to merge from: what is left of each once the turn lanes (of either way) have their place.
			foreach (Zone m in merge)
			{
				Zone left = m;
				bool keep = true;
				foreach (Zone o in median)
				{
					if (o.S1 <= left.S0 || o.S0 >= left.S1)
					{
						continue;
					}
					if (o.Dir == left.Dir)
					{
						// The same way: one lane on, the merge into the next junction's turn lane (a car may stay in it to turn there).
						continue;
					}
					// The other way's turn lane: the merge keeps the part next to its own junction.
					if (left.Dir > 0)
					{
						left.S1 = Math.Min(left.S1, o.S0);
					}
					else
					{
						left.S0 = Math.Max(left.S0, o.S1);
					}
					keep = left.S1 - left.S0 >= ShortestMerge;
				}
				if (keep)
				{
					median.Add(left);
				}
			}
			median = Join(median, found);
			Share(median);
			foreach (Zone z in Join(aux))
			{
				Clamp(z, length);
			}
			foreach (Zone z in median)
			{
				Clamp(z, length);
			}
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

		/// <summary>
		/// Lanes of one direction and kind that overlap or nearly meet, joined: deceleration and acceleration lanes through a junction
		/// between them too (one auxiliary lane on from junction to junction); median lanes never through a junction (the median is
		/// open there, for the turns), each cut back to the junction's mouth instead.
		/// </summary>
		private static List<Zone> Join(List<Zone> zones, List<Junction> junctions = null)
		{
			if (junctions != null)
			{
				zones = Cut(zones, junctions);
			}
			List<Zone> sorted = new List<Zone>(zones);
			sorted.Sort((a, b) => a.Dir != b.Dir ? a.Dir.CompareTo(b.Dir) : a.Kind != b.Kind ? a.Kind.CompareTo(b.Kind) : a.S0 != b.S0 ? a.S0.CompareTo(b.S0) : a.S1.CompareTo(b.S1));
			List<Zone> joined = new List<Zone>();
			foreach (Zone z in sorted)
			{
				if (joined.Count > 0)
				{
					Zone last = joined[joined.Count - 1];
					if (last.Dir == z.Dir && last.Kind == z.Kind && z.S0 <= last.S1 + JoinGap)
					{
						last.S1 = Math.Max(last.S1, z.S1);
						joined[joined.Count - 1] = last;
						continue;
					}
				}
				joined.Add(z);
			}
			return joined;
		}

		/// <summary>Median lanes cut where a junction lies inside them, each piece kept back by <see cref="Mouth"/> from it (short pieces dropped).</summary>
		private static List<Zone> Cut(List<Zone> zones, List<Junction> junctions)
		{
			List<Zone> list = new List<Zone>(zones);
			foreach (Junction j in junctions)
			{
				List<Zone> next = new List<Zone>();
				foreach (Zone z in list)
				{
					if (j.S - Mouth >= z.S1 || j.S + Mouth <= z.S0)
					{
						next.Add(z);
						continue;
					}
					Zone a = z, b = z;
					a.S1 = Math.Min(z.S1, j.S - Mouth);
					b.S0 = Math.Max(z.S0, j.S + Mouth);
					if (a.S1 - a.S0 > Taper * 2.0)
					{
						next.Add(a);
					}
					if (b.S1 - b.S0 > Taper * 2.0)
					{
						next.Add(b);
					}
				}
				list = next;
			}
			return list;
		}

		/// <summary>
		/// Median lanes of the two directions overlapping: split at the middle of the overlap, the one reaching further back keeping
		/// the part on its side (each keeps the end at its own junction, back to back).
		/// </summary>
		private static void Share(List<Zone> median)
		{
			for (int a = 0; a < median.Count; a++)
			{
				for (int b = 0; b < median.Count; b++)
				{
					Zone za = median[a], zb = median[b];
					if (za.Dir <= 0 || zb.Dir >= 0 || za.S1 <= zb.S0 || zb.S1 <= za.S0)
					{
						continue;
					}
					double mid = (Math.Max(za.S0, zb.S0) + Math.Min(za.S1, zb.S1)) * 0.5;
					if (za.S0 + za.S1 <= zb.S0 + zb.S1)
					{
						za.S1 = mid;
						zb.S0 = mid;
					}
					else
					{
						za.S0 = mid;
						zb.S1 = mid;
					}
					median[a] = za;
					median[b] = zb;
				}
			}
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
