using System;
using System.Collections.Generic;

namespace HalfGripOpenWorld
{
	/// <summary>
	/// Where the traffic lights (<see cref="TrafficSignals"/>) stand, American style: one steel mast arm for each paved way into a junction
	/// with a light, always across the junction from the drivers coming (far side, as the MUTCD wants: from the stop line they look ahead
	/// at the heads, not up at them overhead). Where a road carries on ahead, on its right just past the junction, the arm reaching back
	/// over their lanes; at the stem of a T, on the far verge of the road crossed, the arm along that verge with a head straight ahead of
	/// each lane; on the left where the right is not clear. A three-light head for each lane (two at least) and one more on the pole. Only
	/// where nothing on the far side is clear does a mast stand at the stop line. Only where they are: <see cref="SignalHeads"/> draws
	/// them and lights them from the day. No Unity types (trafficview checks them).
	/// </summary>
	internal static class SignalPoles
	{
		/// <summary>One mast arm, facing the cars coming in along arm <see cref="Arm"/> of junction <see cref="Node"/>.</summary>
		internal sealed class Mast
		{
			public int Node, Arm;
			/// <summary>The pole's foot (world).</summary>
			public double X, Z;
			/// <summary>The way the arm reaches out from the pole, over the road (unit).</summary>
			public double Ax, Az;
			/// <summary>The way the heads face: back at the cars coming (unit).</summary>
			public double Fx, Fz;
			/// <summary>The road surface at the junction, from sea level: the heads' clearance is over it.</summary>
			public double Road;
			/// <summary>The arm's length, from the pole.</summary>
			public double Reach;
			/// <summary>How far out along the arm each head hangs, one for each lane.</summary>
			public double[] Heads;
			/// <summary>On the far side of the junction (false: the near side, at the stop line).</summary>
			public bool Far;
		}

		/// <summary>From the road's edge to the pole: on the verge or the sidewalk, clear of a car's mirror at the kerb.</summary>
		internal const double PoleOut = 3.5;

		/// <summary>The heads' bottoms over the road (17 ft, about 5.2 m, as American signals hang), the arm over the road and the pole's top.</summary>
		internal const double HeadClear = 17.0, ArmHeight = 21.6, PoleTop = 23.0;

		/// <summary>The head on the pole: its bottom over the road (about 3 m, a driver's eye at the stop line sees it under the arm's).</summary>
		internal const double PoleHeadClear = 10.0;

		/// <summary>Every mast arm, in junction and arm order.</summary>
		internal static readonly List<Mast> All = new List<Mast>();

		/// <summary>Ways in with no mast: no clear corner either side (for the log and trafficview).</summary>
		internal static int Skipped;
		internal static readonly List<(int node, int arm)> SkippedAt = new List<(int, int)>();

		/// <summary>Lays out the masts at the junctions with lights. After <see cref="TrafficSignals.Build"/> (a day of traffic made).</summary>
		internal static void Build()
		{
			All.Clear();
			Skipped = 0;
			SkippedAt.Clear();
			foreach (int node in TrafficSignals.Nodes)
			{
				List<int> links = Traffic.Links[node];
				for (int k = 0; k < links.Count; k++)
				{
					Network.Road road = Network.Roads[Traffic.Edges[links[k]].Road];
					if (Network.Unpaved(road.Kind) || road.Kind == RoadKind.Path)
					{
						continue;
					}
					if (!Place(node, k))
					{
						Skipped++;
						SkippedAt.Add((node, k));
					}
				}
			}
		}

		/// <summary>How far right of the road's line the middle of each lane of a way in is: the trunk's two and its median lane; else the lane and the centre line (two heads at least, about 8 ft apart or more).</summary>
		internal static double[] Lanes(Network.Road road)
		{
			if (road.Kind == RoadKind.Trunk)
			{
				double outer = Traffic.LaneOffset(road);
				return new[] { outer, outer - 11.0, outer - TrunkLanes.MedianLane * 11.0 };
			}
			double lane = Traffic.LaneOffset(road);
			return new[] { lane + 1.5, lane - 6.5 };
		}

		/// <summary>The mast for the way in along arm <paramref name="k"/> of <paramref name="node"/>, added to <see cref="All"/>: on the far right corner, else across the junction on the line of the way in, else at the stop line; false if nothing is clear.</summary>
		private static bool Place(int node, int k)
		{
			List<int> links = Traffic.Links[node];
			Traffic.Node n = Traffic.Nodes[node];
			Traffic.Edge e = Traffic.Edges[links[k]];
			Network.Road road = Network.Roads[e.Road];
			TrafficDay.ArmOf(node, k, out double dx, out double dz);
			// The way the cars come (h) and their right (r): traffic keeps right; x right, z forward, seen from above.
			double hx = -dx, hz = -dz, rx = hz, rz = -hx;
			double[] lanes = Lanes(road);
			Traffic.At(road, e.A == node ? e.Sa : e.Sb, out double _, out double _, out double y);

			// Straight on: the arm most nearly ahead, within 30 degrees.
			int ahead = -1;
			double best = 0.87;
			for (int j = 0; j < links.Count; j++)
			{
				if (j == k)
				{
					continue;
				}
				TrafficDay.ArmOf(node, j, out double jx, out double jz);
				double dot = jx * hx + jz * hz;
				if (dot > best)
				{
					best = dot;
					ahead = j;
				}
			}

			// The far right corner: on the right of the road ahead, just past the junction.
			if (ahead >= 0)
			{
				Traffic.Edge o = Traffic.Edges[links[ahead]];
				Network.Road onto = Network.Roads[o.Road];
				double into = Math.Min(n.Box + 8.0, o.Length * 0.5);
				Traffic.At(onto, o.A == node ? o.Sa + into : o.Sb - into, out double ox, out double oz, out double _);
				double fx = hx, fz = hz, lx = ox - n.X, lz = oz - n.Z, len = Math.Sqrt(lx * lx + lz * lz);
				if (len > 1e-6)
				{
					fx = lx / len;
					fz = lz / len;
				}
				Mast m = Make(node, k, ox, oz, fx, fz, onto.Half + PoleOut, y, lanes, true);
				if (m != null)
				{
					All.Add(m);
					return true;
				}
			}

			// Across the junction where there is no road ahead to reach over (the stem of a T) or its far corner is not clear: on the far
			// verge of the road crossed, on the line of the way in, the arm along that verge with a head straight ahead of each lane. The
			// drivers at the stop line look across the junction at them, as at any far-side light; on the right, else on the left.
			double across = n.Box + 8.0, ax = n.X + hx * across, az = n.Z + hz * across;
			Mast over = Make(node, k, ax, az, hx, hz, road.Half + PoleOut, y, lanes, true) ?? Make(node, k, ax, az, hx, hz, road.Half + PoleOut, y, lanes, true, true);
			if (over != null)
			{
				All.Add(over);
				return true;
			}

			// The last resort, the near right (else left) corner at the stop line: the heads over the drivers, hard to see from the line
			// (trafficview fails a way in left so).
			double back = Math.Min(n.Box + 3.0, e.Length * 0.5);
			Traffic.At(road, e.A == node ? e.Sa + back : e.Sb - back, out double sx, out double sz, out double _);
			Mast near = Make(node, k, sx, sz, hx, hz, road.Half + PoleOut, y, lanes, false) ?? Make(node, k, sx, sz, hx, hz, road.Half + PoleOut, y, lanes, false, true);
			if (near == null)
			{
				return false;
			}
			All.Add(near);
			return true;
		}

		/// <summary>Where a pole may slide from its first spot when that is not clear: away from the junction (along) and out from the road. On the far side well along; on the near side only a little either way, or the heads would be behind the cars stopped at the line.</summary>
		private static readonly (double along, double out_)[] FarTries = { (0.0, 0.0), (4.0, 0.0), (0.0, 2.5), (8.0, 0.0), (8.0, 2.5), (14.0, 0.0), (14.0, 2.5), (20.0, 0.0), (20.0, 3.0) },
			NearTries = { (0.0, 0.0), (-2.5, 0.0), (3.0, 0.0), (0.0, 2.5), (-2.5, 2.5), (3.0, 2.5), (-5.0, 0.0), (-5.0, 3.0) };

		/// <summary>
		/// A mast whose road's line is at (<paramref name="cx"/>, <paramref name="cz"/>) going <paramref name="fx"/>, <paramref name="fz"/>
		/// (the cars' way), its pole <paramref name="side"/> to the right; slid a little away from the junction or out where that spot is
		/// paving, water or a sign (a fingerpost often stands on the corner), or put on another way in's pole on the corner; null if nowhere near is clear.
		/// </summary>
		private static Mast Make(int node, int k, double cx, double cz, double fx, double fz, double side, double road, double[] lanes, bool far, bool left = false)
		{
			// The pole's side of the road (r): the cars' right, or their left as a last resort, its arm reaching back across.
			double sign = left ? -1.0 : 1.0, rx = fz * sign, rz = -fx * sign;
			// Away from the junction: on along the road ahead on the far side, back along the way in on the near side.
			double wx = far ? fx : -fx, wz = far ? fz : -fz;
			double x = 0.0, z = 0.0, used = side;
			bool found = false;
			foreach ((double along, double out_) in far ? FarTries : NearTries)
			{
				x = cx + rx * (side + out_) + wx * along;
				z = cz + rz * (side + out_) + wz * along;
				Mast shared = PoleNear(x, z);
				if (shared != null)
				{
					// Another way in's pole on this corner: this arm goes on it too, as two arms often share a corner's pole.
					x = shared.X;
					z = shared.Z;
					used = (x - cx) * rx + (z - cz) * rz;
					found = used > side - 3.0;
					break;
				}
				if (Clear(x, z))
				{
					used = side + out_;
					found = true;
					break;
				}
			}
			if (!found)
			{
				return null;
			}
			double[] heads = new double[lanes.Length];
			double reach = 0.0;
			for (int i = 0; i < lanes.Length; i++)
			{
				heads[i] = used - sign * lanes[i];
				reach = Math.Max(reach, heads[i]);
			}
			return new Mast
			{
				Node = node,
				Arm = k,
				X = x,
				Z = z,
				Ax = -rx,
				Az = -rz,
				Fx = -fx,
				Fz = -fz,
				Road = road,
				Reach = reach + 1.5,
				Heads = heads,
				Far = far,
			};
		}

		/// <summary>The mast whose pole stands within 3.5 of (x, z), if any.</summary>
		private static Mast PoleNear(double x, double z)
		{
			foreach (Mast other in All)
			{
				double dx = other.X - x, dz = other.Z - z;
				if (dx * dx + dz * dz < 3.5 * 3.5)
				{
					return other;
				}
			}
			return null;
		}

		/// <summary>Whether a pole may stand at (x, z): off every road's paving, out of the water, not on a sign.</summary>
		internal static bool Clear(double x, double z)
		{
			if (!Network.Claims(x, z, -1, 0.0, 1.2))
			{
				return false;
			}
			if (Rivers.Water(x, z, out double level) && WorldGen.Height(x, z) - Network.Lift < level + 0.3)
			{
				return false;
			}
			return !Signs.Near(x, z, 2.0);
		}
	}
}
