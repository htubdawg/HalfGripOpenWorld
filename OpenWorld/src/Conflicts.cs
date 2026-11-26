using System;
using System.Collections.Generic;

namespace HalfGripOpenWorld
{
	/// <summary>
	/// Where the lane graph's connectors (<see cref="Lanes"/>) meet, from their geometry and not from the order of the arms round a node
	/// (docs/TRAFFIC_REWORK.md, phase 1: the old traffic's "cars stuck here" at Kingsbridge's node 6 came from reading conflicts off the
	/// topology). Every pair of connectors through a junction, or through two junctions whose boxes touch, is swept as two car-wide
	/// corridors; where they overlap is a conflict area, with how far along each connector it begins and ends, its kind (crossing,
	/// merging into one lane, diverging out of one) and who gives way: the lights' phases keep some apart, else Pennsylvania's rules
	/// (a minor road's driver yields to the main line's, a left turn to the cars coming the other way, a driver to the one on the right),
	/// else first come first served.
	///
	/// Built with the lanes, under <see cref="Network"/>'s rules (+ - * / and square roots, ties by index). No Unity types.
	/// </summary>
	internal static class Conflicts
	{
		/// <summary>The kinds of area: two ways crossing, merging into one lane, diverging out of one, or meeting head on along a road one car
		/// wide both ways (a drive: a car coming out and one turning in share its track).</summary>
		internal const int Crossing = 0, Merging = 1, Diverging = 2, Opposing = 3;

		/// <summary>How far back along the lane in a car's body is swept before its stop line (no further than the lane holds the whole car):
		/// where a car waiting to give way must stand clear of the cars it gives way to (<see cref="Lanes.Lane.Hold"/>).</summary>
		internal const double Approach = 2.0 * Traffic.CarLength;

		/// <summary>
		/// Who gives way: the car on connector A, the car on B; neither because the lights never show them green together (<see cref="Apart"/>);
		/// whoever comes second (<see cref="FirstCome"/>: an all-way junction, or two junctions' boxes overlapping, ties to the driver on the
		/// right); or the cars keep their order in the lane they share (<see cref="Follow"/>, diverging).
		/// </summary>
		internal const int YieldA = 0, YieldB = 1, Apart = 2, FirstCome = 3, Follow = 4;

		/// <summary>
		/// A conflict area between connectors <see cref="A"/> and <see cref="B"/> (A &lt; B): a car on A whose rear axle is between A0 and
		/// A1 along it (0 with its front at the stop line, negative before it; up to its length and the rear overhang, the tail leaving) can touch a car on B
		/// whose rear axle is between B0 and B1.
		/// </summary>
		internal struct Area
		{
			public int A, B, Kind, Yield;
			public double A0, A1, B0, B1;
			/// <summary>For <see cref="FirstCome"/>: who yields when both come at once (<see cref="YieldA"/> or <see cref="YieldB"/>, -1 none).</summary>
			public int Tie;
		}

		internal static readonly List<Area> All = new List<Area>();
		/// <summary>Per connector, its conflict areas (indices in <see cref="All"/>).</summary>
		internal static List<int>[] Of;

		/// <summary>Works out the conflict areas of every connector. Called by <see cref="Lanes.Ensure"/>.</summary>
		internal static void Build()
		{
			All.Clear();
			List<Lanes.Connector> cs = Lanes.Connectors;
			Of = new List<int>[cs.Count];
			bodies = new Body[cs.Count];
			for (int c = 0; c < cs.Count; c++)
			{
				Of[c] = new List<int>();
			}
			int nodes = Traffic.Nodes.Count;
			// The nodes by 100-unit cell, to find those whose boxes touch.
			const double cell = 100.0;
			Dictionary<long, List<int>> grid = new Dictionary<long, List<int>>();
			for (int n = 0; n < nodes; n++)
			{
				if (Lanes.AtNode[n].Count == 0)
				{
					continue;
				}
				long key = ((long)Math.Floor(Traffic.Nodes[n].X / cell) << 32) | (uint)(int)Math.Floor(Traffic.Nodes[n].Z / cell);
				if (!grid.TryGetValue(key, out List<int> list))
				{
					grid[key] = list = new List<int>();
				}
				list.Add(n);
			}
			for (int n = 0; n < nodes; n++)
			{
				List<int> here = Lanes.AtNode[n];
				for (int i = 0; i < here.Count; i++)
				{
					for (int j = i + 1; j < here.Count; j++)
					{
						Pair(here[i], here[j], n, n);
					}
				}
				if (here.Count == 0)
				{
					continue;
				}
				Traffic.Node a = Traffic.Nodes[n];
				int ci = (int)Math.Floor(a.X / cell), cj = (int)Math.Floor(a.Z / cell);
				for (int gj = cj - 2; gj <= cj + 2; gj++)
				{
					for (int gi = ci - 2; gi <= ci + 2; gi++)
					{
						if (!grid.TryGetValue(((long)gi << 32) | (uint)gj, out List<int> list))
						{
							continue;
						}
						foreach (int o in list)
						{
							Traffic.Node b = Traffic.Nodes[o];
							double reach = a.Box + b.Box + Traffic.CarLength;
							if (o <= n || (a.X - b.X) * (a.X - b.X) + (a.Z - b.Z) * (a.Z - b.Z) > reach * reach)
							{
								continue;
							}
							foreach (int p in here)
							{
								foreach (int q in Lanes.AtNode[o])
								{
									Pair(Math.Min(p, q), Math.Max(p, q), n, o);
								}
							}
						}
					}
				}
			}
			Holds();
		}

		/// <summary>
		/// Each lane's hold: how far before its end a car waits so its body is clear of every area where it gives way (or may come second),
		/// 0 at most junctions. Areas meeting head on along a drive are left out (the car coming out stands on the track it waits to leave).
		/// </summary>
		private static void Holds()
		{
			foreach (Lanes.Lane l in Lanes.All)
			{
				l.Hold = 0.0;
			}
			foreach (Area area in All)
			{
				if (area.Yield == Follow || area.Yield == Apart || area.Kind == Opposing)
				{
					continue;
				}
				if (area.Yield != YieldB)
				{
					Hold(area.A, area.A0);
				}
				if (area.Yield != YieldA)
				{
					Hold(area.B, area.B0);
				}
			}
		}

		/// <summary>Lane before connector <paramref name="c"/> holds its cars back far enough to be clear of an area from <paramref name="from"/> along it.</summary>
		private static void Hold(int c, double from)
		{
			Lanes.Lane l = Lanes.All[Lanes.Connectors[c].From];
			l.Hold = Math.Max(l.Hold, HoldGap - from);
		}

		/// <summary>The room a waiting car leaves before an area.</summary>
		internal const double HoldGap = 0.5;

		/// <summary>The conflict area of connectors <paramref name="a"/> and <paramref name="b"/> (a &lt; b), at nodes <paramref name="na"/> and <paramref name="nb"/>, if their corridors overlap.</summary>
		private static void Pair(int a, int b, int na, int nb)
		{
			Lanes.Connector ca = Lanes.Connectors[a], cb = Lanes.Connectors[b];
			if (ca.Turn == Lanes.Jump || cb.Turn == Lanes.Jump)
			{
				return;
			}
			if (ca.To == cb.From || cb.To == ca.From)
			{
				// One after the other through a lane between two junctions: the cars keep their order in it.
				return;
			}
			if (!Touch(a, b, out double a0, out double a1, out double b0, out double b1))
			{
				return;
			}
			Area area = new Area { A = a, B = b, A0 = a0, A1 = a1, B0 = b0, B1 = b1, Tie = -1 };
			area.Kind = ca.From == cb.From ? Diverging : ca.To == cb.To ? Merging : OneTrack(ca, cb) ? Opposing : Crossing;
			if (area.Kind == Diverging)
			{
				area.Yield = Follow;
			}
			else if (area.Kind == Opposing)
			{
				// The car already on the track has it: the one coming in waits on the road for the one coming out.
				area.Yield = FirstCome;
				area.Tie = Edge(ca.To) == Edge(cb.From) ? YieldA : YieldB;
			}
			else if (na != nb)
			{
				area.Yield = FirstCome;
				area.Tie = RightHand(na, ca, cb);
			}
			else
			{
				area.Yield = Priority(na, ca, cb, out area.Tie);
			}
			Of[a].Add(All.Count);
			Of[b].Add(All.Count);
			All.Add(area);
		}

		private static int Edge(int lane)
		{
			return Lanes.All[lane].Edge;
		}

		/// <summary>Whether one connector goes into a road one car wide both ways (its lanes on its line) that the other comes out of.</summary>
		private static bool OneTrack(Lanes.Connector a, Lanes.Connector b)
		{
			int e = Edge(a.To) == Edge(b.From) ? Edge(a.To) : Edge(b.To) == Edge(a.From) ? Edge(b.To) : -1;
			return e >= 0 && Lanes.Offset(Lanes.All[Lanes.Forward[e][0]], Traffic.Edges[e].Sa) < 1.0;
		}

		/// <summary>A car's body as swept: half its length and half its width (the game's enemy cars are about 14 by 5.4), with a little room.</summary>
		internal const double HalfLength = Traffic.CarLength * 0.5 + 0.3, HalfWidth = 2.7 + 0.3;

		/// <summary>Per connector, its car's body at each distance of the rear axle along it (a step apart, from the front at the stop line to the tail out of the box): middle, axis, and the box round them all.</summary>
		private static Body[] bodies;

		private sealed class Body
		{
			public double[] Cx, Cz, Ux, Uz, F;
			public double X0, X1, Z0, Z1;
		}

		/// <summary>
		/// The car's body along connector <paramref name="c"/>: for each distance of its rear axle along the line, from <see cref="Approach"/>
		/// before 0 (its front at the stop line) to the line's length and the rear overhang (the tail out of the box), a rectangle along the line's way at the rear axle,
		/// <see cref="Lanes.Axle"/> of it ahead of the axle: on a bend the front swings wide of the line, as a rigid car's does. Past the
		/// line's ends it carries on straight, back along the lane in and on along the lane out.
		/// </summary>
		private static Body Sweep(Lanes.Connector c)
		{
			int last = c.X.Length - 1;
			Head(c, 0, out double h0x, out double h0z);
			Head(c, last, out double h1x, out double h1z);
			double length = c.Length, tail = Traffic.CarLength - Lanes.Axle, ahead = Lanes.Axle - Traffic.CarLength * 0.5;
			// Back no further than its whole body stays on the lane in: before that it is on the junction behind, and its conflicts there
			// are its connector's there.
			double back = Math.Max(0.0, Math.Min(Approach, Lanes.All[c.From].Length - Traffic.CarLength));
			back = Math.Floor(back / Lanes.Step) * Lanes.Step;
			int count = (int)Math.Floor((back + length + tail) / Lanes.Step) + 1;
			Body b = new Body { Cx = new double[count], Cz = new double[count], Ux = new double[count], Uz = new double[count], F = new double[count] };
			b.X0 = b.Z0 = double.MaxValue;
			b.X1 = b.Z1 = double.MinValue;
			for (int k = 0; k < count; k++)
			{
				double f = Math.Min(length + tail, k * Lanes.Step - back);
				At(c, f, h0x, h0z, h1x, h1z, out double rx, out double rz, out double ux, out double uz);
				b.Cx[k] = rx + ux * ahead;
				b.Cz[k] = rz + uz * ahead;
				b.Ux[k] = ux;
				b.Uz[k] = uz;
				b.F[k] = f;
				b.X0 = Math.Min(b.X0, b.Cx[k]);
				b.X1 = Math.Max(b.X1, b.Cx[k]);
				b.Z0 = Math.Min(b.Z0, b.Cz[k]);
				b.Z1 = Math.Max(b.Z1, b.Cz[k]);
			}
			return b;
		}

		/// <summary>The way along connector <paramref name="c"/> at its point <paramref name="p"/> (unit), from its neighbours.</summary>
		private static void Head(Lanes.Connector c, int p, out double x, out double z)
		{
			int a = Math.Max(0, p - 1), b = Math.Min(c.X.Length - 1, p + 1);
			x = c.X[b] - c.X[a];
			z = c.Z[b] - c.Z[a];
			double len = Math.Sqrt(x * x + z * z);
			if (len > 1e-9)
			{
				x /= len;
				z /= len;
			}
		}

		/// <summary>The point distance <paramref name="d"/> along connector <paramref name="c"/> and the way there (unit), carried on straight past its end.</summary>
		private static void At(Lanes.Connector c, double d, double h0x, double h0z, double h1x, double h1z, out double x, out double z, out double ux, out double uz)
		{
			int last = c.X.Length - 1;
			if (d <= 0.0)
			{
				x = c.X[0] + h0x * d;
				z = c.Z[0] + h0z * d;
				ux = h0x;
				uz = h0z;
				return;
			}
			if (d >= c.Length)
			{
				x = c.X[last] + h1x * (d - c.Length);
				z = c.Z[last] + h1z * (d - c.Length);
				ux = h1x;
				uz = h1z;
				return;
			}
			int lo = 0, hi = last;
			while (hi - lo > 1)
			{
				int mid = (lo + hi) / 2;
				if (c.D[mid] <= d)
				{
					lo = mid;
				}
				else
				{
					hi = mid;
				}
			}
			double span = c.D[hi] - c.D[lo], t = span > 1e-12 ? (d - c.D[lo]) / span : 0.0;
			x = c.X[lo] + (c.X[hi] - c.X[lo]) * t;
			z = c.Z[lo] + (c.Z[hi] - c.Z[lo]) * t;
			// The way: the piece's, eased into the next one's past its middle, so the body turns smoothly.
			Head(c, lo, out double ax, out double az);
			Head(c, hi, out double bx, out double bz);
			ux = ax + (bx - ax) * t;
			uz = az + (bz - az) * t;
			double len = Math.Sqrt(ux * ux + uz * uz);
			if (len > 1e-9)
			{
				ux /= len;
				uz /= len;
			}
			else
			{
				ux = h0x;
				uz = h0z;
			}
		}

		private static Body BodyOf(int c)
		{
			if (bodies[c] == null)
			{
				bodies[c] = Sweep(Lanes.Connectors[c]);
			}
			return bodies[c];
		}

		/// <summary>Where along each connector its car's body can touch the other's: the first and last distances of each one's rear axle that do.</summary>
		private static bool Touch(int a, int b, out double a0, out double a1, out double b0, out double b1)
		{
			a0 = a1 = b0 = b1 = 0.0;
			Body p = BodyOf(a), q = BodyOf(b);
			double reach = 2.0 * Math.Sqrt(HalfLength * HalfLength + HalfWidth * HalfWidth);
			if (p.X0 - reach > q.X1 || q.X0 - reach > p.X1 || p.Z0 - reach > q.Z1 || q.Z0 - reach > p.Z1)
			{
				return false;
			}
			int ia0 = int.MaxValue, ia1 = -1, ib0 = int.MaxValue, ib1 = -1;
			double r2 = reach * reach;
			for (int i = 0; i < p.F.Length; i++)
			{
				double cx = p.Cx[i], cz = p.Cz[i];
				if (cx < q.X0 - reach || cx > q.X1 + reach || cz < q.Z0 - reach || cz > q.Z1 + reach)
				{
					continue;
				}
				for (int j = 0; j < q.F.Length; j++)
				{
					double dx = q.Cx[j] - cx, dz = q.Cz[j] - cz;
					if (dx * dx + dz * dz > r2 || !Overlap(dx, dz, p.Ux[i], p.Uz[i], q.Ux[j], q.Uz[j]))
					{
						continue;
					}
					ia0 = Math.Min(ia0, i);
					ia1 = Math.Max(ia1, i);
					ib0 = Math.Min(ib0, j);
					ib1 = Math.Max(ib1, j);
				}
			}
			if (ia1 < 0)
			{
				return false;
			}
			a0 = p.F[ia0];
			a1 = p.F[ia1];
			b0 = q.F[ib0];
			b1 = q.F[ib1];
			return true;
		}

		/// <summary>Whether two car bodies overlap: middles (dx, dz) apart, axes u and v (separating axes, the four sides' normals).</summary>
		private static bool Overlap(double dx, double dz, double ux, double uz, double vx, double vz)
		{
			// Along each axis, the two half extents projected against the middles' distance.
			double uv = Math.Abs(ux * vx + uz * vz), uvp = Math.Abs(ux * -vz + uz * vx);
			// Axis u: the first body's length.
			if (Math.Abs(dx * ux + dz * uz) > HalfLength + HalfLength * uv + HalfWidth * uvp)
			{
				return false;
			}
			// Axis u's normal: the first body's width.
			if (Math.Abs(-dx * uz + dz * ux) > HalfWidth + HalfLength * uvp + HalfWidth * uv)
			{
				return false;
			}
			// Axis v.
			if (Math.Abs(dx * vx + dz * vz) > HalfLength + HalfLength * uv + HalfWidth * uvp)
			{
				return false;
			}
			return Math.Abs(-dx * vz + dz * vx) <= HalfWidth + HalfLength * uvp + HalfWidth * uv;
		}

		// ---- who gives way -------------------------------------------------------------------------------------------------------

		/// <summary>
		/// Who gives way between two connectors through one junction. With a light: none if their phases never show green together; else
		/// (both green: a permissive left, or two moves of one phase) the turning rules. Without: the main line's drivers go first (a stop
		/// or yield sign on the minor road), then the turning rules; with no main line (an all-way junction) first come first served.
		/// </summary>
		private static int Priority(int n, Lanes.Connector a, Lanes.Connector b, out int tie)
		{
			tie = RightHand(n, a, b);
			if (TrafficSignals.Has(n))
			{
				if (TrafficSignals.Apart(n, a.InArm, a.OutArm, b.InArm, b.OutArm))
				{
					return Apart;
				}
				return Turning(n, a, b, tie);
			}
			Junctions.MainOf(n, out int m0, out int m1);
			if (m0 < 0)
			{
				return FirstCome;
			}
			bool am = a.InArm == m0 || a.InArm == m1, bm = b.InArm == m0 || b.InArm == m1;
			if (am != bm)
			{
				return am ? YieldB : YieldA;
			}
			return Turning(n, a, b, tie);
		}

		/// <summary>
		/// Between two drivers of the same standing (both green, both on the main line, or both on minor roads): a left turn gives way to
		/// a driver going on or turning right (PA 3322); else the driver on the left gives way to the one on the right (3321); else (two
		/// coming at each other, neither turning left) the one turning gives way to the one going on; else first come.
		/// </summary>
		private static int Turning(int n, Lanes.Connector a, Lanes.Connector b, int tie)
		{
			bool al = a.Turn == Lanes.Left, bl = b.Turn == Lanes.Left;
			if (al != bl)
			{
				return al ? YieldA : YieldB;
			}
			if (tie >= 0)
			{
				return tie;
			}
			bool ast = a.Turn == Lanes.Straight, bst = b.Turn == Lanes.Straight;
			if (ast != bst)
			{
				return ast ? YieldB : YieldA;
			}
			return FirstCome;
		}

		/// <summary>The right-hand rule between two connectors: <see cref="YieldA"/> if B comes from A's right, <see cref="YieldB"/> if A comes from B's, -1 if neither (coming at each other or from one arm).</summary>
		private static int RightHand(int n, Lanes.Connector a, Lanes.Connector b)
		{
			if (a.InArm == b.InArm && a.Node == b.Node)
			{
				return -1;
			}
			// The ways in: from each connector's first two points (its lane's way of travel).
			Way(a, out double ax, out double az);
			Way(b, out double bx, out double bz);
			// B is on A's right when B's way in, seen from A's, points left (B drives towards A's left).
			double cross = ax * bz - az * bx;
			if (cross > 0.3)
			{
				return YieldA;
			}
			if (cross < -0.3)
			{
				return YieldB;
			}
			return -1;
		}

		private static void Way(Lanes.Connector c, out double x, out double z)
		{
			int last = Math.Min(1, c.X.Length - 1);
			x = c.X[last] - c.X[0];
			z = c.Z[last] - c.Z[0];
			double len = Math.Sqrt(x * x + z * z);
			if (len > 1e-9)
			{
				x /= len;
				z /= len;
			}
		}
	}
}
