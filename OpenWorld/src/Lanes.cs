using System;
using System.Collections.Generic;

namespace HalfGripOpenWorld
{
	/// <summary>
	/// The lane graph the traffic drives (docs/TRAFFIC_REWORK.md, phase 1): a lane for each way along every stretch of the traffic's map
	/// (<see cref="Traffic.Edges"/>; two each way on the trunk, the left-turn bays off it from <see cref="TrunkLanes"/> and Kingsbridge's
	/// bay from <see cref="TurnBays"/>), and connectors through each junction from the end of a lane coming in to the start of one going
	/// out, as curves over the drawn surface. A lane runs between the junctions' boxes (<see cref="Traffic.Node.Box"/>): its end is where
	/// a car's front waits, the stop line (<see cref="SignalPoles.StopLine"/> is drawn there). The wrap joins the trunk's lanes at its two
	/// tunnels. The conflicts between connectors are <see cref="Conflicts"/>.
	///
	/// Built once after <see cref="Traffic"/>, with + - * / and square roots only, ties by index, so every machine gets the same graph. No
	/// Unity types: meshcheck's <c>lanes</c> checks it, <c>jview lanes</c> draws it.
	/// </summary>
	internal static class Lanes
	{
		/// <summary>What a lane is: a through lane, or a left-turn bay (it opens off the through lane beside it and only turns left at its end).</summary>
		internal const int Through = 0, Bay = 1;

		/// <summary>What a connector does: goes on, turns left (across the other way's lane: traffic keeps right), turns right, turns round at a
		/// dead end, or jumps through the wrap (no length: the trunk's tunnels joined).</summary>
		internal const int Straight = 0, Left = 1, Right = 2, Round = 3, Jump = 4;

		/// <summary>A lane along edge <see cref="Edge"/> for traffic going <see cref="Dir"/> (+1 from its A to its B, the way its road's distance grows).</summary>
		internal sealed class Lane
		{
			public int Edge, Dir;
			/// <summary>Which: 0 the outer (the only one on most roads), 1 the trunk's inner; a bay is after them.</summary>
			public int Index;
			public int Kind;
			/// <summary>Where it starts and ends along its road, in the way of travel (Start &gt; End going -1).</summary>
			public double Start, End;
			/// <summary>The node it leaves (-1: a bay, opening off <see cref="Parent"/>) and the node it comes to.</summary>
			public int From, To;
			/// <summary>A bay's through lane beside it, which its cars move over from; -1 for a through lane.</summary>
			public int Parent = -1;
			/// <summary>The lanes beside it going its way (-1: none): left (towards the middle) and right.</summary>
			public int LeftOf = -1, RightOf = -1;
			/// <summary>The legal speed (units a second).</summary>
			public double Limit;
			/// <summary>Its length (0 where the junctions' boxes meet on a short stretch).</summary>
			public double Length;
			/// <summary>How far before its end a car waiting to go on stands, clear of the cars it gives way to (<see cref="Conflicts"/>): 0
			/// at most junctions; more where a car turning in swings past the stop line of a narrow road.</summary>
			public double Hold;
			/// <summary>The connectors from its end.</summary>
			public readonly List<int> Out = new List<int>();
			/// <summary>The connectors into its start.</summary>
			public readonly List<int> In = new List<int>();
		}

		/// <summary>
		/// A way through junction <see cref="Node"/> from lane <see cref="From"/> to lane <see cref="To"/>, as a polyline about a unit a point:
		/// the path of a car's rear axle, from where it is when the car's front is at the stop line (<see cref="Axle"/> before the lane's
		/// end) to the lane out. Distance along it is so also how far the car's front is past the stop line.
		/// </summary>
		internal sealed class Connector
		{
			public int Node, From, To;
			/// <summary>The arms (indices in <see cref="Traffic.Links"/>) it comes in and goes out along.</summary>
			public int InArm, OutArm;
			public int Turn;
			public double[] X, Z, Y;
			/// <summary>Distance along it at each point.</summary>
			public double[] D;
			public double Length;
			/// <summary>The fastest it is driven (units a second): the lanes' limits and its tightest bend.</summary>
			public double Limit;
			/// <summary>Whether it crosses a junction's box (three arms or more), which is kept clear: a car only goes in if it can get out.</summary>
			public bool KeepClear;
			/// <summary>Where along its road it joins lane <see cref="To"/>: the lane's start, or further on where a turn too tight for a car swings
			/// wide and comes back to the lane past the box.</summary>
			public double Join;
			/// <summary>Whether it swings wide of the plain curve (<see cref="MinRadius"/>).</summary>
			public bool Wide;
		}

		internal static readonly List<Lane> All = new List<Lane>();
		internal static readonly List<Connector> Connectors = new List<Connector>();
		/// <summary>Per node, its connectors.</summary>
		internal static List<int>[] AtNode;
		/// <summary>Per edge, its lanes going +1 and going -1 (through lanes by index, then the bays).</summary>
		internal static List<int>[] Forward, Backward;

		/// <summary>The spacing of a connector's points.</summary>
		internal const double Step = 1.0;
		/// <summary>The sideways acceleration a driver takes a turn at: about 3 m/s² (a comfortable turn), in units a second squared.</summary>
		internal const double TurnGrip = 3.0 * 3.3;
		/// <summary>
		/// A car's rear axle to its front, of its <see cref="Traffic.CarLength"/> (the wheelbase and the front overhang; the rear overhang
		/// is the rest). The lines are where the rear axle goes: a car's body lies along the line's way at its rear axle, so on a bend its
		/// front swings wide of the line, as a real car's does. A car with its front at a lane's end has its rear axle this far before it.
		/// </summary>
		internal const double Axle = 11.0;
		/// <summary>
		/// The tightest a connector's line bends: about 4.2 m, a family car's rear axle on full lock (its turning circle is about 11 m
		/// across at the kerb). The line starts where the rear axle of a car waiting at the stop line is (<see cref="Axle"/> back), so a
		/// street corner's right turn fits; where even that is too tight (the sharp right off the Pinewick road at Kingsbridge's node 6,
		/// where the old traffic's physics cars swung wide into the cars going the other way) the line swings wide and comes back to its
		/// lane past the box.
		/// </summary>
		internal const double MinRadius = 14.0;
		/// <summary>The slowest a connector's limit goes (a dead end's turn round): about 6 mph.</summary>
		internal const double Crawl = 6.0 * Traffic.Mph;

		private static readonly object gate = new object();
		private static volatile bool built;
		internal static double BuildMs;
		internal static string Report = "";

		/// <summary>Builds the lane graph and its conflicts once (after <see cref="Junctions.Ensure"/>, which it calls). Safe from any thread.</summary>
		internal static void Ensure()
		{
			if (built)
			{
				return;
			}
			lock (gate)
			{
				if (built)
				{
					return;
				}
				Junctions.Ensure();
				System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
				All.Clear();
				Connectors.Clear();
				int edges = Traffic.Edges.Count;
				Forward = new List<int>[edges];
				Backward = new List<int>[edges];
				for (int e = 0; e < edges; e++)
				{
					Forward[e] = new List<int>();
					Backward[e] = new List<int>();
					if (Traffic.OnMain(e))
					{
						AddLanes(e, 1);
						AddLanes(e, -1);
					}
				}
				AtNode = new List<int>[Traffic.Nodes.Count];
				for (int n = 0; n < AtNode.Length; n++)
				{
					AtNode[n] = new List<int>();
					Connect(n);
				}
				JoinWrap();
				double lanesMs = watch.Elapsed.TotalMilliseconds;
				Conflicts.Build();
				BuildMs = watch.Elapsed.TotalMilliseconds;
				int bays = 0;
				foreach (Lane l in All)
				{
					bays += l.Kind == Bay ? 1 : 0;
				}
				Report = All.Count + " lanes (" + bays + " bays), " + Connectors.Count + " connectors, " + Conflicts.All.Count + " conflict areas; "
					+ lanesMs.ToString("0") + " + " + (BuildMs - lanesMs).ToString("0") + " ms";
				built = true;
			}
		}

		// ---- lanes along the stretches ------------------------------------------------------------------------------------------

		/// <summary>How far before a node its lanes end: its box at a junction or a bend; at a dead end the same (room to turn round).</summary>
		private static double BoxOf(int node)
		{
			return Traffic.Nodes[node].Box;
		}

		/// <summary>The lanes of edge <paramref name="e"/> going <paramref name="dir"/>.</summary>
		private static void AddLanes(int e, int dir)
		{
			Traffic.Edge edge = Traffic.Edges[e];
			Network.Road road = Network.Roads[edge.Road];
			int from = dir > 0 ? edge.A : edge.B, to = dir > 0 ? edge.B : edge.A;
			double s0 = dir > 0 ? edge.Sa : edge.Sb, s1 = dir > 0 ? edge.Sb : edge.Sa;
			double b0 = Traffic.Links[from].Count == 1 && Portal(from) >= 0 ? 0.0 : BoxOf(from);
			double b1 = Traffic.Links[to].Count == 1 && Portal(to) >= 0 ? 0.0 : BoxOf(to);
			double start, end;
			if (edge.Length > b0 + b1)
			{
				start = s0 + dir * b0;
				end = s1 - dir * b1;
			}
			else
			{
				// The two junctions' boxes meet: a lane of no length where they do, in proportion.
				start = end = s0 + dir * edge.Length * (b0 + b1 > 0.0 ? b0 / (b0 + b1) : 0.5);
			}
			Traffic.At(road, (edge.Sa + edge.Sb) * 0.5, out double mx, out double mz, out double _);
			double limit = Traffic.Limit(road.Kind, mx, mz);
			List<int> list = dir > 0 ? Forward[e] : Backward[e];
			int count = road.Kind == RoadKind.Trunk ? 2 : 1;
			for (int k = 0; k < count; k++)
			{
				Lane l = new Lane { Edge = e, Dir = dir, Index = k, Kind = Through, Start = start, End = end, From = from, To = to, Limit = limit, Length = Math.Abs(end - start) };
				if (k > 0)
				{
					l.RightOf = All.Count - 1;
					All[All.Count - 1].LeftOf = All.Count;
				}
				list.Add(All.Count);
				All.Add(l);
			}
			// A left-turn bay up to this stretch's far junction: off the trunk (TrunkLanes) or on a road widened for one (TurnBays).
			double bayFrom = double.NaN;
			if (road.Kind == RoadKind.Trunk)
			{
				for (int zi = 0; zi < TrunkLanes.Zones.Count; zi++)
				{
					TrunkLanes.Zone z = TrunkLanes.Zones[zi];
					if (z.Road == edge.Road && z.Kind == TrunkLanes.Median && z.Dir == dir)
					{
						double zEnd = TrunkLanes.EndOf(zi);
						// Its end is at the junction's mouth, this stretch's far node.
						if (Math.Abs(zEnd - s1) <= TrunkLanes.Mouth + 5.0)
						{
							bayFrom = TrunkLanes.StartOf(zi);
						}
					}
				}
			}
			else
			{
				int b = TurnBays.At(edge.Road, dir, s1);
				if (b >= 0)
				{
					bayFrom = dir > 0 ? TurnBays.Bays[b].S0 : TurnBays.Bays[b].S1;
				}
			}
			if (!double.IsNaN(bayFrom) && (end - start) * dir > 0.0)
			{
				// It opens off the through lane next to the middle, no nearer the stretch's start than that lane does.
				double bs = dir > 0 ? Math.Max(start, Math.Min(bayFrom, end)) : Math.Min(start, Math.Max(bayFrom, end));
				int parent = list[list.Count - 1];
				Lane bay = new Lane { Edge = e, Dir = dir, Index = list.Count, Kind = Bay, Start = bs, End = end, From = -1, To = to, Parent = parent, Limit = limit, Length = Math.Abs(end - bs) };
				list.Add(All.Count);
				All.Add(bay);
			}
		}

		/// <summary>The open tunnel (wrap portal) node <paramref name="node"/> is at, or -1: a trunk end at the world's edge.</summary>
		private static int Portal(int node)
		{
			Traffic.Node n = Traffic.Nodes[node];
			for (int i = 0; i < Network.Portals.Count; i++)
			{
				Network.Portal p = Network.Portals[i];
				if ((p.X - n.X) * (p.X - n.X) + (p.Z - n.Z) * (p.Z - n.Z) < (p.Half + 20.0) * (p.Half + 20.0))
				{
					return i;
				}
			}
			return -1;
		}

		/// <summary>How far right of its road's line (in its way of travel) lane <paramref name="l"/>'s middle is at distance <paramref name="s"/> along the road.</summary>
		internal static double Offset(Lane l, double s)
		{
			Network.Road road = Network.Roads[Traffic.Edges[l.Edge].Road];
			if (road.Kind == RoadKind.Trunk)
			{
				return l.Kind == Bay ? TrunkLanes.BayMid : l.Index == 0 ? TrunkLanes.OuterMid : TrunkLanes.InnerMid;
			}
			if (l.Kind == Bay)
			{
				// The middle of a road widened for a bay, between its through lanes (the bay is all of it up to the T).
				return 0.0;
			}
			// Where the road is widened for a bay, its through lanes are half the widening further out.
			return Traffic.LaneOffset(road) + Network.WideAt(road, s) * 0.5;
		}

		/// <summary>Lane <paramref name="l"/>'s middle at distance <paramref name="s"/> along its road: the point, its height (from sea level) and the way of travel (unit).</summary>
		internal static void Point(Lane l, double s, out double x, out double z, out double y, out double hx, out double hz)
		{
			Network.Road road = Network.Roads[Traffic.Edges[l.Edge].Road];
			Network.Locate(road, s, out int i, out double t);
			Network.Side(road, i, out double ax, out double az);
			Network.Side(road, i + 1, out double bx, out double bz);
			double sx = ax * (1.0 - t) + bx * t, sz = az * (1.0 - t) + bz * t, len = Math.Sqrt(sx * sx + sz * sz);
			if (len > 1e-9)
			{
				sx /= len;
				sz /= len;
			}
			// The left side is (sx, sz); the way of travel is that turned right, flipped going -1.
			hx = sz * l.Dir;
			hz = -sx * l.Dir;
			double off = -l.Dir * Offset(l, s);
			x = road.X[i] + (road.X[i + 1] - road.X[i]) * t + sx * off;
			z = road.Z[i] + (road.Z[i + 1] - road.Z[i]) * t + sz * off;
			y = road.Y[i] + (road.Y[i + 1] - road.Y[i]) * t;
		}

		// ---- connectors through the junctions ------------------------------------------------------------------------------------

		/// <summary>Arm <paramref name="k"/> of node <paramref name="node"/>: whether it is its edge's B end (a road that loops back to one node has it twice, its A end first).</summary>
		private static bool AtB(int node, int k)
		{
			List<int> links = Traffic.Links[node];
			Traffic.Edge e = Traffic.Edges[links[k]];
			if (e.A != e.B)
			{
				return e.B == node;
			}
			return links.IndexOf(links[k]) != k;
		}

		/// <summary>The connectors through node <paramref name="n"/>: for each arm in and each arm out, from the lanes a driver uses for that move.</summary>
		private static void Connect(int n)
		{
			List<int> links = Traffic.Links[n];
			int m = links.Count;
			if (m == 0 || !Traffic.OnMain(links[0]))
			{
				return;
			}
			for (int i = 0; i < m; i++)
			{
				// The lanes coming in along arm i: going +1 if the arm is its edge's B end.
				List<int> ins = AtB(n, i) ? Forward[links[i]] : Backward[links[i]];
				if (ins.Count == 0)
				{
					continue;
				}
				for (int j = 0; j < m; j++)
				{
					if (j == i && m > 1)
					{
						continue;
					}
					List<int> outs = AtB(n, j) ? Backward[links[j]] : Forward[links[j]];
					if (outs.Count == 0)
					{
						continue;
					}
					int turn = TurnOf(n, i, j);
					int throughIn = Throughs(ins), throughOut = Throughs(outs);
					int bay = ins.Count > throughIn ? ins[throughIn] : -1;
					switch (turn)
					{
						case Left:
							// From the bay if there is one, else the lane nearest the middle; into the lane nearest the middle (PA 3331).
							Add(n, i, j, bay >= 0 ? bay : ins[throughIn - 1], outs[throughOut - 1], turn);
							break;
						case Right:
							Add(n, i, j, ins[0], outs[0], turn);
							break;
						case Round:
							if (Offset(All[ins[0]], All[ins[0]].End) > 1.0 && Portal(n) < 0)
							{
								Add(n, i, j, ins[0], outs[0], turn);
							}
							break;
						default:
							for (int k = 0; k < throughIn; k++)
							{
								Add(n, i, j, ins[k], outs[Math.Min(k, throughOut - 1)], turn);
							}
							break;
					}
				}
			}
		}

		private static int Throughs(List<int> lanes)
		{
			int c = 0;
			foreach (int l in lanes)
			{
				c += All[l].Kind == Through ? 1 : 0;
			}
			return c;
		}

		/// <summary>The move in along arm <paramref name="i"/> and out along <paramref name="j"/>: a bend (two arms) always goes on.</summary>
		private static int TurnOf(int n, int i, int j)
		{
			int m = Traffic.Links[n].Count;
			if (m == 1)
			{
				return Round;
			}
			if (m == 2)
			{
				return Straight;
			}
			Junctions.ArmOf(n, i, out double ix, out double iz);
			Junctions.ArmOf(n, j, out double jx, out double jz);
			double tx = -ix, tz = -iz, cross = tx * jz - tz * jx;
			return cross > 0.3 ? Left : cross < -0.3 ? Right : Straight;
		}

		/// <summary>Adds the connector from the end of lane <paramref name="from"/> to the start of lane <paramref name="to"/>.</summary>
		private static void Add(int n, int inArm, int outArm, int from, int to, int turn)
		{
			Lane a = All[from], b = All[to];
			RearAt(a, out double x0, out double z0, out double y0, out double h0x, out double h0z);
			Point(b, b.Start, out double x1, out double z1, out double y1, out double h1x, out double h1z);
			Connector c = new Connector { Node = n, From = from, To = to, InArm = inArm, OutArm = outArm, Turn = turn, KeepClear = Traffic.Links[n].Count >= 3 };
			Curve(c, x0, z0, y0, h0x, h0z, x1, z1, y1, h1x, h1z);
			c.Join = b.Start;
			if (turn != Round && Tightest(c) < MinRadius)
			{
				Fit(c, b, x0, z0, y0, h0x, h0z);
			}
			c.Limit = Math.Max(Crawl, Math.Min(Math.Min(a.Limit, b.Limit), Math.Sqrt(TurnGrip * Tightest(c))));
			a.Out.Add(Connectors.Count);
			b.In.Add(Connectors.Count);
			AtNode[n].Add(Connectors.Count);
			Connectors.Add(c);
		}

		/// <summary>Where a connector from lane <paramref name="l"/> starts: the rear axle of a car whose front is at the lane's end, on the lane (straight on back from its end if the lane is shorter).</summary>
		private static void RearAt(Lane l, out double x, out double z, out double y, out double hx, out double hz)
		{
			if (l.Length >= Axle)
			{
				Point(l, l.End - l.Dir * Axle, out x, out z, out y, out hx, out hz);
				return;
			}
			Point(l, l.End, out x, out z, out y, out hx, out hz);
			x -= hx * Axle;
			z -= hz * Axle;
		}

		/// <summary>
		/// The connector's line: a cubic Bézier leaving along the lane in and arriving along the lane out, its handles as long as a circular
		/// arc's would be for the angle turned (a third of the way for going on, about 0.39 of it for a right angle, two thirds to turn
		/// round), then spaced about <see cref="Step"/> apart by distance along it.
		/// </summary>
		private static void Curve(Connector c, double x0, double z0, double y0, double h0x, double h0z, double x1, double z1, double y1, double h1x, double h1z)
		{
			double dx = x1 - x0, dz = z1 - z0, dist = Math.Sqrt(dx * dx + dz * dz);
			double k = Handle(dist, h0x * h1x + h0z * h1z);
			Curve(c, x0, z0, y0, h0x, h0z, k, x1, z1, y1, h1x, h1z, k);
		}

		/// <summary>A circular arc's handle for a Bézier spanning <paramref name="dist"/> while turning through the angle whose cosine is <paramref name="cos"/>.</summary>
		private static double Handle(double dist, double cos)
		{
			cos = Math.Max(-1.0, Math.Min(1.0, cos));
			return dist * (2.0 / 3.0) / (1.0 + Math.Sqrt((1.0 + cos) * 0.5));
		}

		/// <summary>The Bézier from (x0, z0) along (h0x, h0z) with handle <paramref name="k0"/> to (x1, z1) along (h1x, h1z) with handle <paramref name="k1"/>, as the connector's points.</summary>
		private static void Curve(Connector c, double x0, double z0, double y0, double h0x, double h0z, double k0, double x1, double z1, double y1, double h1x, double h1z, double k1)
		{
			double dx = x1 - x0, dz = z1 - z0, dist = Math.Sqrt(dx * dx + dz * dz);
			double ax = x0 + h0x * k0, az = z0 + h0z * k0, bx = x1 - h1x * k1, bz = z1 - h1z * k1;
			int dense = Math.Max(16, (int)(dist * 4.0) + 1);
			double[] px = new double[dense + 1], pz = new double[dense + 1], pd = new double[dense + 1];
			for (int q = 0; q <= dense; q++)
			{
				double t = (double)q / dense, u = 1.0 - t;
				double w0 = u * u * u, w1 = 3.0 * u * u * t, w2 = 3.0 * u * t * t, w3 = t * t * t;
				px[q] = w0 * x0 + w1 * ax + w2 * bx + w3 * x1;
				pz[q] = w0 * z0 + w1 * az + w2 * bz + w3 * z1;
				if (q > 0)
				{
					double ex = px[q] - px[q - 1], ez = pz[q] - pz[q - 1];
					pd[q] = pd[q - 1] + Math.Sqrt(ex * ex + ez * ez);
				}
			}
			double length = pd[dense];
			int count = Math.Max(1, (int)Math.Ceiling(length / Step - 1e-9));
			c.X = new double[count + 1];
			c.Z = new double[count + 1];
			c.Y = new double[count + 1];
			c.D = new double[count + 1];
			int at = 0;
			for (int p = 0; p <= count; p++)
			{
				double want = length * p / count;
				while (at < dense - 1 && pd[at + 1] < want)
				{
					at++;
				}
				double span = pd[at + 1] - pd[at], f = span > 1e-12 ? Math.Max(0.0, Math.Min(1.0, (want - pd[at]) / span)) : 0.0;
				c.X[p] = px[at] + (px[at + 1] - px[at]) * f;
				c.Z[p] = pz[at] + (pz[at + 1] - pz[at]) * f;
				c.Y[p] = y0 + (y1 - y0) * (length > 0.0 ? want / length : 0.0);
				c.D[p] = want;
			}
			c.Length = length;
		}

		/// <summary>
		/// Re-draws connector <paramref name="c"/>, whose plain curve bends tighter than a car can (<see cref="MinRadius"/>), as the shortest
		/// curve that doesn't: a Bézier from the lane's end with longer handles (it swings wide, as a driver does on a tight corner) and,
		/// if that isn't enough, joining lane <paramref name="to"/> further along (<see cref="Connector.Join"/>). The handles and the join
		/// are tried on a grid, nearest join first; if nothing on it is gentle enough, the gentlest is kept.
		/// </summary>
		private static void Fit(Connector c, Lane to, double x0, double z0, double y0, double h0x, double h0z)
		{
			double most = 1.0 / MinRadius, reach = Math.Min(to.Length, 60.0);
			double bestK = double.MaxValue, bestLen = double.MaxValue, bk0 = 0.0, bk1 = 0.0, bj = 0.0;
			bool fits = false;
			for (double j = 0.0; j <= reach + 1e-9 && !fits; j += 2.0)
			{
				Point(to, to.Start + to.Dir * j, out double x1, out double z1, out double _, out double h1x, out double h1z);
				double dist = Math.Sqrt((x1 - x0) * (x1 - x0) + (z1 - z0) * (z1 - z0));
				for (int i0 = 1; i0 <= 10; i0++)
				{
					for (int i1 = 1; i1 <= 10; i1++)
					{
						double k0 = dist * 0.15 * i0, k1 = dist * 0.15 * i1;
						Bend(x0, z0, h0x, h0z, k0, x1, z1, h1x, h1z, k1, out double kmax, out double len);
						bool ok = kmax <= most;
						if (ok && (!fits || len < bestLen))
						{
							fits = true;
							bestLen = len;
							bk0 = k0;
							bk1 = k1;
							bj = j;
						}
						else if (!fits && kmax < bestK)
						{
							bestK = kmax;
							bk0 = k0;
							bk1 = k1;
							bj = j;
						}
					}
				}
			}
			Point(to, to.Start + to.Dir * bj, out double fx, out double fz, out double fy, out double fhx, out double fhz);
			Curve(c, x0, z0, c.Y[0], h0x, h0z, bk0, fx, fz, fy, fhx, fhz, bk1);
			c.Join = to.Start + to.Dir * bj;
			c.Wide = true;
		}

		/// <summary>The sharpest bend (1 / radius) and the length of a Bézier, from its derivatives at 32 points.</summary>
		private static void Bend(double x0, double z0, double h0x, double h0z, double k0, double x1, double z1, double h1x, double h1z, double k1, out double kmax, out double len)
		{
			double ax = x0 + h0x * k0, az = z0 + h0z * k0, bx = x1 - h1x * k1, bz = z1 - h1z * k1;
			kmax = 0.0;
			len = 0.0;
			double px = x0, pz = z0;
			for (int q = 0; q <= 32; q++)
			{
				double t = q / 32.0, u = 1.0 - t;
				// First and second derivatives of the cubic.
				double d1x = 3.0 * (u * u * (ax - x0) + 2.0 * u * t * (bx - ax) + t * t * (x1 - bx));
				double d1z = 3.0 * (u * u * (az - z0) + 2.0 * u * t * (bz - az) + t * t * (z1 - bz));
				double d2x = 6.0 * (u * (bx - 2.0 * ax + x0) + t * (x1 - 2.0 * bx + ax));
				double d2z = 6.0 * (u * (bz - 2.0 * az + z0) + t * (z1 - 2.0 * bz + az));
				double sp = Math.Sqrt(d1x * d1x + d1z * d1z);
				if (sp > 1e-9)
				{
					kmax = Math.Max(kmax, Math.Abs(d1x * d2z - d1z * d2x) / (sp * sp * sp));
				}
				double w0 = u * u * u, w1 = 3.0 * u * u * t, w2 = 3.0 * u * t * t, w3 = t * t * t;
				double x = w0 * x0 + w1 * ax + w2 * bx + w3 * x1, z = w0 * z0 + w1 * az + w2 * bz + w3 * z1;
				len += Math.Sqrt((x - px) * (x - px) + (z - pz) * (z - pz));
				px = x;
				pz = z;
			}
		}

		/// <summary>The tightest radius along a connector (from its points two steps apart either side), or a long one if it is straight.</summary>
		private static double Tightest(Connector c)
		{
			double best = 1e6;
			for (int p = 2; p + 2 < c.X.Length; p++)
			{
				double ax = c.X[p] - c.X[p - 2], az = c.Z[p] - c.Z[p - 2], bx = c.X[p + 2] - c.X[p], bz = c.Z[p + 2] - c.Z[p];
				double la = Math.Sqrt(ax * ax + az * az), lb = Math.Sqrt(bx * bx + bz * bz);
				if (la < 1e-9 || lb < 1e-9)
				{
					continue;
				}
				// The turn between the two chords over the distance between their middles: 1 / radius.
				double sin = Math.Abs(ax * bz - az * bx) / (la * lb);
				double r = (la + lb) * 0.5 / Math.Max(1e-9, sin);
				best = Math.Min(best, r);
			}
			return best;
		}

		/// <summary>The trunk's lanes run on through the wrap: each lane into one open tunnel joined to the same lane out of the other.</summary>
		private static void JoinWrap()
		{
			if (Wrap.A < 0 || Wrap.B < 0)
			{
				return;
			}
			for (int n = 0; n < Traffic.Nodes.Count; n++)
			{
				int p = Portal(n);
				if (p < 0 || !Wrap.Open(p) || Traffic.Links[n].Count != 1)
				{
					continue;
				}
				int far = -1;
				Network.Portal q = Network.Portals[Wrap.Other(p)];
				for (int o = 0; o < Traffic.Nodes.Count && far < 0; o++)
				{
					if (Traffic.Links[o].Count == 1 && Portal(o) == Wrap.Other(p))
					{
						far = o;
					}
				}
				if (far < 0)
				{
					continue;
				}
				List<int> ins = AtB(n, 0) ? Forward[Traffic.Links[n][0]] : Backward[Traffic.Links[n][0]];
				List<int> outs = AtB(far, 0) ? Backward[Traffic.Links[far][0]] : Forward[Traffic.Links[far][0]];
				for (int k = 0; k < Math.Min(Throughs(ins), Throughs(outs)); k++)
				{
					Lane a = All[ins[k]], b = All[outs[k]];
					Point(a, a.End, out double x0, out double z0, out double y0, out double _, out double _);
					Point(b, b.Start, out double x1, out double z1, out double y1, out double _, out double _);
					Connector c = new Connector
					{
						Node = n, From = ins[k], To = outs[k], InArm = 0, OutArm = 0, Turn = Jump, Limit = Math.Min(a.Limit, b.Limit),
						X = new[] { x0, x1 }, Z = new[] { z0, z1 }, Y = new[] { y0, y1 }, D = new[] { 0.0, 0.0 }, Length = 0.0,
					};
					a.Out.Add(Connectors.Count);
					b.In.Add(Connectors.Count);
					AtNode[n].Add(Connectors.Count);
					Connectors.Add(c);
				}
			}
		}
	}
}
