using System;
using System.Collections.Generic;

namespace HalfGripOpenWorld
{
	/// <summary>
	/// The traffic's map: what is fixed with the world and the same in every race. The roads cars drive as a graph of junctions and lanes
	/// (drives included, a road passing over another not joined to it), every household with its cars (the drawn houses in the towns and
	/// villages, the homes on the drives, the farms), the places that draw trips (offices in the towers, the retail park, shops, workshops,
	/// filling stations, the market places, the courses' yards, the edges of the map where roads leave it) and where each one's cars park.
	///
	/// Built once after the layout, with plain arithmetic and ties broken by index, under the same rules as <see cref="Network"/>, so every
	/// machine gets the same households and the same graph. No Unity types: <c>tools/trafficsim</c> builds and checks it.
	/// Each race's plans for every car (its day's stops and the drives between them) are <see cref="Schedules"/>.
	/// </summary>
	internal static class Traffic
	{
		// ---- real-world figures (see docs/research/TRAFFIC_STUDIES.md) -------------------------------------------

		/// <summary>Game units in a mile an hour: 0.44704 m/s at 3.3 units a metre.</summary>
		internal const double Mph = 0.44704 * 3.3;

		/// <summary>Real seconds of the world's day (<c>Sky.DaySeconds</c>: 100 seconds an hour).</summary>
		internal const double DaySeconds = 2400.0;

		/// <summary>
		/// The share of a day a household has a car moving: 1.89 drivers driving 59 minutes each of 1,440 (NHTS 2017, Tables 30 and
		/// p. 9). Kept to this share, the roads are as busy as Pennsylvania's counters say; it sets how many trips each day holds.
		/// </summary>
		internal const double DrivingShare = 1.89 * 59.0 / 1440.0 * TrafficAmount;

		/// <summary>
		/// How much of the survey's local traffic is on the roads: half (the owner, 2026-11-26: "use less cars until you can figure this
		/// out"). The physics cars near the player keep meeting and getting stuck at busy junctions; fewer cars driving, fewer meetings.
		/// Through traffic on the highway is not cut. Changes the day: everyone in a room needs the same build. Back to 1 once the
		/// junctions are right.
		/// </summary>
		internal const double TrafficAmount = 0.5;

		/// <summary>A car's length and width (the game's enemy cars are about 14 by 5.4).</summary>
		internal const double CarLength = 14.0;

		// ---- the graph ---------------------------------------------------------------------------------------------------------

		/// <summary>A stretch of road between two junctions (or a junction and a dead end): road <see cref="Road"/> from Sa to Sb.</summary>
		internal sealed class Edge
		{
			public int A, B, Road;
			public double Sa, Sb, Length;
			/// <summary>Seconds to drive it at its roads' limit (bends not counted): what routes and the gravity of places are worked from.</summary>
			public double Time;
		}

		internal struct Node
		{
			public double X, Z;
			/// <summary>How far round the junction's middle a car is in it: the widest road's half width and a little.</summary>
			public double Box;
		}

		internal static readonly List<Node> Nodes = new List<Node>();
		internal static readonly List<Edge> Edges = new List<Edge>();
		/// <summary>Per node, the edges at it, in order.</summary>
		internal static List<int>[] Links;
		/// <summary>Per road, its edges in order along it (empty for a road not driven).</summary>
		internal static List<int>[] RoadEdges;

		/// <summary>Two points this close are one junction.</summary>
		private const double Merge = 8.0;
		private const double NodeCell = 16.0;
		/// <summary>Two roads that cross at heights further apart than this pass over one another: no junction.</summary>
		private const double OverPass = 4.0;

		private static readonly Dictionary<long, List<int>> nodeCells = new Dictionary<long, List<int>>();

		/// <summary>
		/// The roads cars drive: every road but the footpaths, the farm tracks, the courses themselves and a road cut out whole (a short
		/// street lying along a main road, which is never drawn: driven, its lanes ran off the paving beside the road it lies along).
		/// </summary>
		internal static bool Driven(Network.Road road)
		{
			return road.Count >= 2 && (road.Kind <= RoadKind.Lane || road.Kind == RoadKind.Drive) && !Network.CutWhole(road);
		}

		/// <summary>The legal speed on a road at a point (units a second): Pennsylvania's limits, lower inside a town or village.</summary>
		internal static double Limit(RoadKind kind, double x, double z)
		{
			Places.Place place = kind <= RoadKind.Secondary ? Places.At(x, z) : null;
			bool built = place != null && place.Kind != PlaceKind.Hamlet;
			switch (kind)
			{
				case RoadKind.Trunk: return (built ? 45.0 : 55.0) * Mph;
				case RoadKind.Primary: return (built ? 35.0 : 50.0) * Mph;
				case RoadKind.Secondary: return (built ? 30.0 : 45.0) * Mph;
				case RoadKind.Street: return 25.0 * Mph;
				case RoadKind.Lane: return 35.0 * Mph;
				default: return 10.0 * Mph;
			}
		}

		/// <summary>How far right of a road's line the middle of the lane a car keeps to is: the outer lane of the trunk's carriageway, else half a two-lane road.</summary>
		internal static double LaneOffset(Network.Road road)
		{
			if (road.Kind == RoadKind.Trunk)
			{
				// The middle of the outer lane: the median, its shoulder, the inner lane and half the outer (TrunkLanes' cross-section).
				return TrunkLanes.OuterMid;
			}
			if (road.Kind == RoadKind.Drive)
			{
				return 0.0;
			}
			return Math.Min(5.5, road.Half * 0.5);
		}

		private static long Key(int i, int j)
		{
			return ((long)i << 32) | (uint)j;
		}

		private static int NodeAt(double x, double z)
		{
			int ci = (int)Math.Floor(x / NodeCell), cj = (int)Math.Floor(z / NodeCell);
			int found = -1;
			double best = Merge * Merge;
			for (int j = cj - 1; j <= cj + 1; j++)
			{
				for (int i = ci - 1; i <= ci + 1; i++)
				{
					if (!nodeCells.TryGetValue(Key(i, j), out List<int> list))
					{
						continue;
					}
					foreach (int n in list)
					{
						double d = (Nodes[n].X - x) * (Nodes[n].X - x) + (Nodes[n].Z - z) * (Nodes[n].Z - z);
						if (d < best || (d == best && found >= 0 && n < found))
						{
							best = d;
							found = n;
						}
					}
				}
			}
			if (found >= 0)
			{
				return found;
			}
			Nodes.Add(new Node { X = x, Z = z });
			if (!nodeCells.TryGetValue(Key(ci, cj), out List<int> cell))
			{
				cell = new List<int>();
				nodeCells[Key(ci, cj)] = cell;
			}
			cell.Add(Nodes.Count - 1);
			return Nodes.Count - 1;
		}

		private const double PieceCell = 50.0;

		/// <summary>The driven roads' pieces (road << 32 | sample), by 50-unit cell.</summary>
		private static Dictionary<long, List<long>> pieces;

		private static void IndexPieces()
		{
			pieces = new Dictionary<long, List<long>>();
			for (int r = 0; r < Network.Roads.Count; r++)
			{
				Network.Road road = Network.Roads[r];
				if (!Driven(road))
				{
					continue;
				}
				for (int i = 0; i + 1 < road.Count; i++)
				{
					int i0 = (int)Math.Floor(Math.Min(road.X[i], road.X[i + 1]) / PieceCell), i1 = (int)Math.Floor(Math.Max(road.X[i], road.X[i + 1]) / PieceCell);
					int j0 = (int)Math.Floor(Math.Min(road.Z[i], road.Z[i + 1]) / PieceCell), j1 = (int)Math.Floor(Math.Max(road.Z[i], road.Z[i + 1]) / PieceCell);
					for (int j = j0; j <= j1; j++)
					{
						for (int c = i0; c <= i1; c++)
						{
							if (!pieces.TryGetValue(Key(c, j), out List<long> list))
							{
								list = new List<long>();
								pieces[Key(c, j)] = list;
							}
							list.Add(((long)r << 32) | (uint)i);
						}
					}
				}
			}
		}

		/// <summary>The driven pieces within <paramref name="reach"/> of the box (x0, z0)-(x1, z1), each once, in order.</summary>
		private static List<long> PiecesNear(double x0, double z0, double x1, double z1, double reach)
		{
			SortedSet<long> found = new SortedSet<long>();
			for (int j = (int)Math.Floor((Math.Min(z0, z1) - reach) / PieceCell); j <= (int)Math.Floor((Math.Max(z0, z1) + reach) / PieceCell); j++)
			{
				for (int i = (int)Math.Floor((Math.Min(x0, x1) - reach) / PieceCell); i <= (int)Math.Floor((Math.Max(x0, x1) + reach) / PieceCell); i++)
				{
					if (pieces.TryGetValue(Key(i, j), out List<long> list))
					{
						foreach (long p in list)
						{
							found.Add(p);
						}
					}
				}
			}
			return new List<long>(found);
		}

		/// <summary>Whether segments a-b and c-d cross, and where along each (0 to 1).</summary>
		private static bool Cross(double ax, double az, double bx, double bz, double cx, double cz, double dx, double dz, out double ta, out double tb)
		{
			double rx = bx - ax, rz = bz - az, sx = dx - cx, sz = dz - cz;
			double den = rx * sz - rz * sx;
			ta = tb = 0.0;
			if (Math.Abs(den) < 1e-9)
			{
				return false;
			}
			ta = ((cx - ax) * sz - (cz - az) * sx) / den;
			tb = ((cx - ax) * rz - (cz - az) * rx) / den;
			return ta >= 0.0 && ta <= 1.0 && tb >= 0.0 && tb <= 1.0;
		}

		/// <summary>A road's line point and height (from sea level) at distance <paramref name="s"/> along it.</summary>
		internal static void At(Network.Road road, double s, out double x, out double z, out double y)
		{
			Network.Locate(road, s, out int i, out double t);
			x = road.X[i] + (road.X[i + 1] - road.X[i]) * t;
			z = road.Z[i] + (road.Z[i + 1] - road.Z[i]) * t;
			y = road.Y[i] + (road.Y[i + 1] - road.Y[i]) * t;
		}

		/// <summary>The way along a road (unit) at distance <paramref name="s"/>.</summary>
		internal static void Heading(Network.Road road, double s, out double dx, out double dz)
		{
			Network.Locate(road, s, out int i, out double _);
			dx = road.X[i + 1] - road.X[i];
			dz = road.Z[i + 1] - road.Z[i];
			double len = Math.Sqrt(dx * dx + dz * dz);
			if (len > 1e-9)
			{
				dx /= len;
				dz /= len;
			}
		}

		/// <summary>
		/// The graph: a junction wherever a driven road's end lies on another (within its half width) and wherever two cross at about the
		/// same height; an edge along each road between its junctions. As <see cref="Signs"/> builds its own, with the drives in and no
		/// junction where a road passes over another on a bridge.
		/// </summary>
		private static void Graph()
		{
			Nodes.Clear();
			Edges.Clear();
			nodeCells.Clear();
			IndexPieces();
			List<(double s, int node)>[] stops = new List<(double s, int node)>[Network.Roads.Count];
			for (int r = 0; r < Network.Roads.Count; r++)
			{
				stops[r] = new List<(double s, int node)>();
			}
			for (int r = 0; r < Network.Roads.Count; r++)
			{
				Network.Road road = Network.Roads[r];
				if (!Driven(road))
				{
					continue;
				}
				foreach (int end in new[] { 0, road.Count - 1 })
				{
					double ex = road.X[end], ez = road.Z[end], ey = road.Y[end];
					int bestRoad = -1;
					double bestD = double.MaxValue, bestS = 0.0, bx = 0.0, bz = 0.0;
					foreach (long p in PiecesNear(ex, ez, ex, ez, 40.0))
					{
						int q = (int)(p >> 32), i = (int)(uint)p;
						if (q == r)
						{
							continue;
						}
						Network.Road other = Network.Roads[q];
						Lines.Project(other.X, other.Z, i, ex, ez, out double t, out double d);
						double oy = other.Y[i] + (other.Y[i + 1] - other.Y[i]) * t;
						if (d <= other.Half + 3.0 && d < bestD && Math.Abs(oy - ey) < OverPass)
						{
							bestD = d;
							bestRoad = q;
							bestS = other.S[i] + (other.S[i + 1] - other.S[i]) * t;
							bx = other.X[i] + (other.X[i + 1] - other.X[i]) * t;
							bz = other.Z[i] + (other.Z[i + 1] - other.Z[i]) * t;
						}
					}
					if (bestRoad >= 0)
					{
						int node = NodeAt(bx, bz);
						stops[bestRoad].Add((bestS, node));
						stops[r].Add((road.S[end], node));
					}
					else
					{
						stops[r].Add((road.S[end], NodeAt(ex, ez)));
					}
				}
			}
			for (int r = 0; r < Network.Roads.Count; r++)
			{
				Network.Road road = Network.Roads[r];
				if (!Driven(road))
				{
					continue;
				}
				for (int i = 0; i + 1 < road.Count; i++)
				{
					double ax = road.X[i], az = road.Z[i], bx = road.X[i + 1], bz = road.Z[i + 1];
					foreach (long p in PiecesNear(ax, az, bx, bz, 0.0))
					{
						int q = (int)(p >> 32), j = (int)(uint)p;
						if (q <= r)
						{
							continue;
						}
						Network.Road other = Network.Roads[q];
						if (Cross(ax, az, bx, bz, other.X[j], other.Z[j], other.X[j + 1], other.Z[j + 1], out double ta, out double tb))
						{
							double ya = road.Y[i] + (road.Y[i + 1] - road.Y[i]) * ta, yb = other.Y[j] + (other.Y[j + 1] - other.Y[j]) * tb;
							if (Math.Abs(ya - yb) >= OverPass)
							{
								continue;
							}
							int node = NodeAt(ax + (bx - ax) * ta, az + (bz - az) * ta);
							stops[r].Add((road.S[i] + (road.S[i + 1] - road.S[i]) * ta, node));
							stops[q].Add((other.S[j] + (other.S[j + 1] - other.S[j]) * tb, node));
						}
					}
				}
			}
			RoadEdges = new List<int>[Network.Roads.Count];
			for (int r = 0; r < Network.Roads.Count; r++)
			{
				RoadEdges[r] = new List<int>();
				List<(double s, int node)> list = stops[r];
				list.Sort((a, b) => a.s != b.s ? a.s.CompareTo(b.s) : a.node.CompareTo(b.node));
				for (int k = 0; k + 1 < list.Count; k++)
				{
					if (list[k].node == list[k + 1].node || list[k + 1].s - list[k].s < 1e-6)
					{
						continue;
					}
					Network.Road road = Network.Roads[r];
					double length = list[k + 1].s - list[k].s;
					At(road, (list[k].s + list[k + 1].s) * 0.5, out double mx, out double mz, out double _);
					RoadEdges[r].Add(Edges.Count);
					Edges.Add(new Edge { A = list[k].node, B = list[k + 1].node, Road = r, Sa = list[k].s, Sb = list[k + 1].s, Length = length, Time = length / Limit(road.Kind, mx, mz) });
				}
			}
			Links = new List<int>[Nodes.Count];
			for (int n = 0; n < Nodes.Count; n++)
			{
				Links[n] = new List<int>();
			}
			for (int e = 0; e < Edges.Count; e++)
			{
				Links[Edges[e].A].Add(e);
				Links[Edges[e].B].Add(e);
				double half = Network.Roads[Edges[e].Road].Half;
				foreach (int n in new[] { Edges[e].A, Edges[e].B })
				{
					Node node = Nodes[n];
					node.Box = Math.Max(node.Box, half + 2.0);
					Nodes[n] = node;
				}
			}
			for (int n = 0; n < Nodes.Count; n++)
			{
				ObliqueBox(n);
			}
		}

		/// <summary>The most a junction's box is widened for a road meeting another at a slant.</summary>
		private const double BoxMost = 75.0;

		/// <summary>How far a car waiting at a box's edge, in its lane, keeps off another road's paving (about half a car's width).</summary>
		internal const double StopClear = 2.7;

		/// <summary>
		/// Widens node <paramref name="n"/>'s box so a car waiting at its edge on any arm, in its lane coming in, is off every other road's
		/// paving: a road meeting the trunk at a slant is on its paving well past its half width and a little (enough only where roads meet
		/// square), and cars waited in the highway's lanes. Worked out from the roads themselves (they curve near a junction): each arm
		/// stepped back a unit at a time until clear of the other roads' lines by their half widths and <see cref="StopClear"/>.
		/// </summary>
		private static void ObliqueBox(int n)
		{
			List<int> links = Links[n];
			if (links.Count < 3)
			{
				return;
			}
			Node node = Nodes[n];
			double box = node.Box;
			foreach (int ei in links)
			{
				Edge e = Edges[ei];
				Network.Road road = Network.Roads[e.Road];
				double sign = e.A == n ? -1.0 : 1.0;
				double d = node.Box;
				while (d < BoxMost && d < e.Length - 1.0)
				{
					double s = e.A == n ? e.Sa + d : e.Sb - d;
					// (The lane's own place: where the road is widened for a bay the lane is out a little further, which would push the
					// whole junction's box back by the bay's own length.)
					double off = LaneOffset(road);
					At(road, s, out double x, out double z, out double _);
					Heading(road, s, out double hx, out double hz);
					x += hz * sign * off;
					z -= hx * sign * off;
					if (ClearOfOthers(n, e.Road, x, z))
					{
						break;
					}
					d += 1.0;
				}
				box = Math.Max(box, Math.Min(d, BoxMost));
			}
			node.Box = box;
			Nodes[n] = node;
		}

		/// <summary>Whether point (x, z) is off the paving (by <see cref="StopClear"/>) of every road at node <paramref name="n"/> but <paramref name="road"/>.</summary>
		internal static bool ClearOfOthers(int n, int road, double x, double z)
		{
			foreach (int ej in Links[n])
			{
				Edge f = Edges[ej];
				if (f.Road == road)
				{
					continue;
				}
				Network.Road other = Network.Roads[f.Road];
				double sNode = f.A == n ? f.Sa : f.Sb, lo = Math.Max(0.0, sNode - 150.0), hi = Math.Min(other.S[other.Count - 1], sNode + 150.0);
				for (double q = lo; q <= hi; q += 2.0)
				{
					// (Its own width, not a bay's widening: the box is the whole junction's, so a side road meeting a road widened for a bay
					// at a slant pushed every way in's stop line back, 29 against 23 at Kingsbridge's node 6, and the junction held each car
					// longer. A car waiting there stands a little onto the widened shoulder, clear of the lane.)
					double need = other.Half + StopClear;
					At(other, q, out double ox, out double oz, out double _);
					if ((ox - x) * (ox - x) + (oz - z) * (oz - z) < need * need)
					{
						return false;
					}
				}
			}
			return true;
		}

		/// <summary>Per node, which connected part of the graph it is in; the biggest part (by length of road) is <see cref="MainPart"/>.</summary>
		internal static int[] Part;
		internal static int MainPart;
		/// <summary>Households and places left out because their road is not joined to the main part.</summary>
		internal static int CutOff;

		/// <summary>
		/// The graph's connected parts. Every road should join the rest, but a few town streets are laid with nothing at either end but
		/// footpaths, and no car could reach them; the biggest part is where all the traffic is.
		/// </summary>
		private static void Parts()
		{
			Part = new int[Nodes.Count];
			for (int n = 0; n < Part.Length; n++)
			{
				Part[n] = -1;
			}
			List<double> length = new List<double>();
			Stack<int> stack = new Stack<int>();
			for (int n0 = 0; n0 < Part.Length; n0++)
			{
				if (Part[n0] >= 0)
				{
					continue;
				}
				int part = length.Count;
				double sum = 0.0;
				Part[n0] = part;
				stack.Push(n0);
				while (stack.Count > 0)
				{
					int u = stack.Pop();
					foreach (int e in Links[u])
					{
						int v = Edges[e].A == u ? Edges[e].B : Edges[e].A;
						// Each edge counted once, from its A end.
						if (Edges[e].A == u)
						{
							sum += Edges[e].Length;
						}
						if (Part[v] < 0)
						{
							Part[v] = part;
							stack.Push(v);
						}
					}
				}
				length.Add(sum);
			}
			MainPart = 0;
			for (int p = 1; p < length.Count; p++)
			{
				if (length[p] > length[MainPart])
				{
					MainPart = p;
				}
			}
		}

		/// <summary>Whether edge <paramref name="e"/> is on the graph's main part.</summary>
		internal static bool OnMain(int e)
		{
			return Part[Edges[e].A] == MainPart;
		}

		/// <summary>The edge of road <paramref name="r"/> that distance <paramref name="s"/> along it is on (-1 if none).</summary>
		internal static int EdgeAt(int r, double s)
		{
			List<int> list = RoadEdges[r];
			int lo = 0, hi = list.Count - 1, found = -1;
			while (lo <= hi)
			{
				int mid = (lo + hi) / 2;
				Edge e = Edges[list[mid]];
				if (s < e.Sa)
				{
					hi = mid - 1;
				}
				else if (s > e.Sb)
				{
					lo = mid + 1;
				}
				else
				{
					found = list[mid];
					break;
				}
			}
			return found;
		}

		/// <summary>The nearest point of a driven road of the kinds <paramref name="ok"/> allows within <paramref name="reach"/> of (x, z), as an edge and distance along its road.</summary>
		private static bool Nearest(double x, double z, double reach, Func<RoadKind, bool> ok, out int edge, out double s)
		{
			edge = -1;
			s = 0.0;
			double best = reach;
			foreach (long p in PiecesNear(x, z, x, z, reach))
			{
				int q = (int)(p >> 32), i = (int)(uint)p;
				Network.Road road = Network.Roads[q];
				if (!ok(road.Kind))
				{
					continue;
				}
				Lines.Project(road.X, road.Z, i, x, z, out double t, out double d);
				if (d < best)
				{
					double sq = road.S[i] + (road.S[i + 1] - road.S[i]) * t;
					int e = EdgeAt(q, sq);
					if (e >= 0)
					{
						best = d;
						edge = e;
						s = sq;
					}
				}
			}
			return edge >= 0;
		}

		// ---- where cars stand and go ----------------------------------------------------------------------------------------------

		/// <summary>
		/// A spot on the graph: road distance <see cref="S"/> along edge <see cref="Edge"/>, on its <see cref="Side"/> (1 left of the
		/// road's way, -1 right), and the parking spot off it (<see cref="X"/>, <see cref="Z"/>) a car pulls into from there.
		/// </summary>
		internal struct Anchor
		{
			public int Edge;
			public double S;
			public int Side;
			public double X, Z;
		}

		internal enum HomeKind
		{
			/// <summary>A house in its garden in a town or village: a drive pad beside it.</summary>
			House,
			/// <summary>A terrace house or a flat over a shop: parked at the kerb in front.</summary>
			Terrace,
			/// <summary>A house at the end of its drive out in the country.</summary>
			Cottage,
			/// <summary>A farm: the farmyard.</summary>
			Farm,
		}

		internal sealed class Household
		{
			public HomeKind Kind;
			public Anchor Home;
			public int Cars;
			public int Workers;
			/// <summary>The place it belongs to (null out in the country) and its index in <see cref="Places.All"/>.</summary>
			public int Place = -1;
			public ulong Hash;
		}

		internal static readonly List<Household> Households = new List<Household>();

		internal enum DestinationKind
		{
			Offices,
			Retail,
			Shop,
			HighStreet,
			Workshop,
			Filling,
			Market,
			Church,
			Course,
			/// <summary>Where a road leaves the map: work, shops and friends beyond it, and the traffic passing through.</summary>
			Edge,
		}

		internal sealed class Destination
		{
			public DestinationKind Kind;
			public Anchor At;
			/// <summary>How many cars it parks at once (0: no limit, a road leaving the map).</summary>
			public int Spaces;
			/// <summary>How strongly it draws each purpose's trips (<see cref="Purpose"/>).</summary>
			public double Work, Shop, Errand, Social;
			public string Name;
			/// <summary>Its lot in <see cref="Town.Lots"/> (-1: parked at the kerb, or a road out): where its cars park.</summary>
			public int Lot = -1;
		}

		internal static readonly List<Destination> Destinations = new List<Destination>();

		internal enum Purpose
		{
			Work,
			Shop,
			Errand,
			Social,
			/// <summary>Through traffic, from one edge of the map to another.</summary>
			Through,
		}

		private static readonly object gate = new object();
		private static volatile bool built;

		/// <summary>Milliseconds the map took, and what it holds, for the log.</summary>
		internal static double BuildMs;
		internal static string Report = "";

		/// <summary>Builds the traffic's map if it has not been built (after the layout). Safe from any thread; the first call does the work.</summary>
		internal static void Ensure()
		{
			if (built)
			{
				return;
			}
			lock (gate)
			{
				if (!built)
				{
					Network.Ensure();
					System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
					Graph();
					Parts();
					Homes();
					Draws();
					// A household or place on a road cut off from the rest (a town street joined to nothing but footpaths) has no way
					// anywhere: left out, so every trip has a route.
					CutOff = Households.RemoveAll(h => !OnMain(h.Home.Edge)) + Destinations.RemoveAll(d => !OnMain(d.At.Edge));
					BuildMs = watch.Elapsed.TotalMilliseconds;
					int cars = 0;
					foreach (Household h in Households)
					{
						cars += h.Cars;
					}
					Report = Nodes.Count + " junctions, " + Edges.Count + " stretches of road, " + Households.Count + " households with " + cars + " cars, "
						+ Destinations.Count + " places that draw trips (" + CutOff + " left out on roads cut off from the rest); " + BuildMs.ToString("0") + " ms";
					built = true;
				}
			}
		}

		private static readonly ulong Salt = Noise.Mix(WorldGen.Seed + 7300UL);

		/// <summary>
		/// Cars a household keeps, from its hash: 4% none, 31% one, 41% two, 24% three (1.85 on average; NHTS 2017 gives 1.88, rural
		/// households fewer without one).
		/// </summary>
		private static int CarsOf(ulong h)
		{
			double u = Plots.Unit(h, 20);
			return u < 0.04 ? 0 : u < 0.35 ? 1 : u < 0.76 ? 2 : 3;
		}

		/// <summary>Workers in a household with <paramref name="cars"/> cars: 1.33 on average (NHTS 2017).</summary>
		private static int WorkersOf(ulong h, int cars)
		{
			double u = Plots.Unit(h, 21);
			int w = u < 0.22 ? 0 : u < 0.62 ? 1 : u < 0.97 ? 2 : 3;
			return Math.Min(w, Math.Max(cars, 1));
		}

		/// <summary>
		/// Every household: one to a house drawn on a plot in the towns and villages (terraces and flats over the high streets' shops park
		/// at the kerb, a house in its garden on a pad beside it), one to each home on a drive, one to each farm.
		/// </summary>
		private static void Homes()
		{
			Households.Clear();
			List<Plots.Plot> plots = new List<Plots.Plot>();
			for (int r = 0; r < Network.Roads.Count; r++)
			{
				Network.Road road = Network.Roads[r];
				if (road.Kind == RoadKind.Trunk || Network.Unpaved(road.Kind) || RoadEdges[r].Count == 0)
				{
					continue;
				}
				for (int i = 0; i + 1 < road.Count; i++)
				{
					plots.Clear();
					Plots.Along(r, road, i, plots);
					foreach (Plots.Plot p in plots)
					{
						if (p.Towers || !Plots.Fits(p))
						{
							continue;
						}
						double s = p.K * p.Spacing;
						int e = EdgeAt(r, s);
						if (e < 0)
						{
							continue;
						}
						bool terrace = p.Terrace;
						// A house in its garden: the pad beside it, a car's length in front of its front; a terrace: the kerb in front.
						double along = terrace ? 0.0 : p.Width * 0.5 + 6.0;
						double off = terrace ? road.Half - 3.5 : road.Half + p.Setback * 0.5 + 2.0;
						double px = p.Px + p.Dx * along * (p.Side) + p.Nx * off, pz = p.Pz + p.Dz * along * (p.Side) + p.Nz * off;
						int cars = CarsOf(p.Hash);
						Households.Add(new Household
						{
							Kind = terrace ? HomeKind.Terrace : HomeKind.House,
							Home = new Anchor { Edge = e, S = s, Side = p.Side, X = px, Z = pz },
							Cars = cars,
							Workers = WorkersOf(p.Hash, cars),
							Place = Places.All.IndexOf(p.Place),
							Hash = p.Hash,
						});
					}
				}
			}
			// The homes on the drives: the house at the drive's end (a farm's drive is the farm's, below).
			foreach (Network.Home home in Network.Homes)
			{
				if (!home.House)
				{
					continue;
				}
				// The drive's end by the house: a little in front of its front.
				double fx = home.X - home.DirX * (Network.HomeDepth * 0.5 + 8.0), fz = home.Z - home.DirZ * (Network.HomeDepth * 0.5 + 8.0);
				if (!Nearest(fx, fz, 30.0, k => k == RoadKind.Drive, out int e, out double s) && !Nearest(fx, fz, 30.0, k => k <= RoadKind.Lane, out e, out s))
				{
					continue;
				}
				ulong h = Noise.Mix(Salt ^ (ulong)(long)Math.Floor(home.X) * 0x632BE59BUL ^ (ulong)(long)Math.Floor(home.Z));
				int cars = Math.Max(1, CarsOf(h));
				Households.Add(new Household
				{
					Kind = HomeKind.Cottage,
					Home = new Anchor { Edge = e, S = s, Side = 1, X = fx + home.DirZ * 9.0, Z = fz - home.DirX * 9.0 },
					Cars = cars,
					Workers = WorkersOf(h, cars),
					Hash = h,
				});
			}
			// The farms: the yard, at the road nearest the farm's middle.
			for (int pi = 0; pi < Places.All.Count; pi++)
			{
				Places.Place p = Places.All[pi];
				if (p.Kind != PlaceKind.Hamlet)
				{
					continue;
				}
				if (!Nearest(p.X, p.Z, p.Radius + 40.0, k => k <= RoadKind.Lane || k == RoadKind.Drive, out int e, out double s))
				{
					continue;
				}
				ulong h = Noise.Mix(Salt ^ 0xFA53UL ^ (ulong)pi);
				Households.Add(new Household
				{
					Kind = HomeKind.Farm,
					Home = new Anchor { Edge = e, S = s, Side = 1, X = p.X, Z = p.Z },
					Cars = 2 + (Plots.Unit(h, 22) < 0.5 ? 1 : 0),
					Workers = 2,
					Place = pi,
					Hash = h,
				});
			}
		}

		/// <summary>An anchor on a lot's road in front of its middle, parking on the lot.</summary>
		private static bool LotAnchor(Town.Lot lot, out Anchor a)
		{
			a = default;
			Network.Road road = Network.Roads[lot.Road];
			if (RoadEdges[lot.Road].Count == 0)
			{
				return false;
			}
			double best = double.MaxValue, bs = 0.0;
			int side = 1;
			for (int i = 0; i + 1 < road.Count; i++)
			{
				Lines.Project(road.X, road.Z, i, lot.X, lot.Z, out double t, out double d);
				if (d < best)
				{
					best = d;
					bs = road.S[i] + (road.S[i + 1] - road.S[i]) * t;
					double dx = road.X[i + 1] - road.X[i], dz = road.Z[i + 1] - road.Z[i];
					// Left of the road is (-dz, dx).
					side = (lot.X - road.X[i]) * -dz + (lot.Z - road.Z[i]) * dx >= 0.0 ? 1 : -1;
				}
			}
			int e = EdgeAt(lot.Road, bs);
			if (e < 0)
			{
				return false;
			}
			Town.World(lot, 0.0, lot.D * 0.35, out double px, out double pz);
			a = new Anchor { Edge = e, S = bs, Side = side, X = px, Z = pz };
			return true;
		}

		/// <summary>
		/// The places that draw trips, with how many cars each parks and how strongly it draws each purpose. Jobs: about one for every
		/// household in the offices, shops and yards, and a share at the map's edges (most country people work in a town beyond it).
		/// </summary>
		private static void Draws()
		{
			Destinations.Clear();
			for (int li = 0; li < Town.Lots.Count; li++)
			{
				Town.Lot lot = Town.Lots[li];
				if (!LotAnchor(lot, out Anchor a))
				{
					continue;
				}
				Destination d = new Destination { At = a, Name = lot.Place, Lot = li };
				switch (lot.Kind)
				{
					case LotKind.Retail:
						d.Kind = DestinationKind.Retail;
						d.Spaces = 80;
						d.Work = 60.0;
						d.Shop = 120.0;
						d.Errand = 30.0;
						d.Social = 6.0;
						break;
					case LotKind.Shop:
						d.Kind = DestinationKind.Shop;
						d.Spaces = 8;
						d.Work = 4.0;
						d.Shop = 8.0;
						d.Errand = 8.0;
						break;
					case LotKind.Workshop:
						d.Kind = DestinationKind.Workshop;
						d.Spaces = 8;
						d.Work = 8.0;
						d.Errand = 5.0;
						break;
					case LotKind.Garage:
						d.Kind = DestinationKind.Filling;
						d.Spaces = 4;
						d.Work = 3.0;
						d.Shop = 4.0;
						d.Errand = 10.0;
						break;
					case LotKind.Market:
						d.Kind = DestinationKind.Market;
						d.Spaces = 12;
						d.Work = 10.0;
						d.Shop = 20.0;
						d.Errand = 15.0;
						d.Social = 10.0;
						break;
					case LotKind.Service:
					case LotKind.Paddock:
						d.Kind = DestinationKind.Course;
						d.Spaces = 12;
						d.Work = 3.0;
						d.Social = 15.0;
						break;
					default:
						continue;
				}
				Destinations.Add(d);
			}
			// The offices: each glass tower in the main town's centre, parking at the kerb in front (about 200 jobs a tower would need a
			// multi-storey; the kerb and the town's other spaces stand in for it).
			List<Plots.Plot> plots = new List<Plots.Plot>();
			for (int r = 0; r < Network.Roads.Count; r++)
			{
				Network.Road road = Network.Roads[r];
				if (road.Kind == RoadKind.Trunk || Network.Unpaved(road.Kind) || RoadEdges[r].Count == 0)
				{
					continue;
				}
				for (int i = 0; i + 1 < road.Count; i++)
				{
					plots.Clear();
					Plots.Along(r, road, i, plots);
					foreach (Plots.Plot p in plots)
					{
						if (!p.Towers && !p.High)
						{
							continue;
						}
						double s = p.K * p.Spacing;
						int e = EdgeAt(r, s);
						if (e < 0 || (p.High && !Plots.Fits(p)))
						{
							continue;
						}
						Anchor a = new Anchor { Edge = e, S = s, Side = p.Side, X = p.Px + p.Nx * (road.Half - 3.5), Z = p.Pz + p.Nz * (road.Half - 3.5) };
						if (p.Towers)
						{
							Destinations.Add(new Destination { Kind = DestinationKind.Offices, At = a, Spaces = 10, Work = 40.0, Errand = 4.0, Name = p.Place.Name });
						}
						else
						{
							Destinations.Add(new Destination { Kind = DestinationKind.HighStreet, At = a, Spaces = 2, Work = 2.0, Shop = 3.0, Errand = 3.0, Social = 1.0, Name = p.Place.Name });
						}
					}
				}
			}
			// The village churches: social on a Sunday (and a little through the week).
			foreach (Places.Place p in Places.All)
			{
				if (p.Kind == PlaceKind.Village && Nearest(p.X, p.Z, p.Radius * 0.5, k => k <= RoadKind.Street, out int e, out double s))
				{
					At(Network.Roads[Edges[e].Road], s, out double x, out double z, out double _);
					Destinations.Add(new Destination { Kind = DestinationKind.Church, At = new Anchor { Edge = e, S = s, Side = 1, X = x, Z = z }, Spaces = 10, Social = 4.0, Errand = 1.0, Name = p.Name });
				}
			}
			// The roads leaving the map: work and shops beyond (a share of each purpose), and every through trip's ends.
			foreach (Network.Portal portal in Network.Portals)
			{
				if (!Nearest(portal.X, portal.Z, 80.0, k => k <= RoadKind.Lane, out int e, out double s))
				{
					continue;
				}
				RoadKind kind = Network.Roads[Edges[e].Road].Kind;
				double size = kind == RoadKind.Trunk ? 1.0 : kind == RoadKind.Primary ? 0.5 : 0.25;
				Destinations.Add(new Destination
				{
					Kind = DestinationKind.Edge,
					At = new Anchor { Edge = e, S = s, Side = 1, X = portal.X, Z = portal.Z },
					Work = 900.0 * size,
					Shop = 150.0 * size,
					Errand = 80.0 * size,
					Social = 200.0 * size,
					Name = "the " + kind.ToString().ToLowerInvariant() + " road out",
				});
			}
		}

		// ---- routes ------------------------------------------------------------------------------------------------------------

		/// <summary>
		/// The quickest way by road from <paramref name="from"/> to <paramref name="to"/>, as the edges driven in order with the way along
		/// each (+1 from A to B), into <paramref name="route"/> (cleared); false if there is none. Dijkstra from the start, ties by index.
		/// Both anchors' own edges are partly driven: from the start point to whichever end leads on, and in to the end point.
		/// </summary>
		internal static bool Route(Anchor from, Anchor to, List<(int edge, int dir)> route, Heap heap, double[] cost, int[] via, List<int> touched)
		{
			route.Clear();
			Edge e0 = Edges[from.Edge], e1 = Edges[to.Edge];
			if (from.Edge == to.Edge)
			{
				route.Add((from.Edge, to.S >= from.S ? 1 : -1));
				return true;
			}
			foreach (int n in touched)
			{
				cost[n] = double.MaxValue;
				via[n] = -1;
			}
			touched.Clear();
			heap.Clear();
			double speed0 = e0.Length / Math.Max(1e-6, e0.Time);
			// Seed the start edge's two ends.
			void Seed(int node, double c)
			{
				if (c < cost[node])
				{
					if (cost[node] == double.MaxValue)
					{
						touched.Add(node);
					}
					cost[node] = c;
					via[node] = -2 - (node == e0.A ? 0 : 1);
					heap.Push(c, node);
				}
			}
			Seed(e0.A, (from.S - e0.Sa) / speed0);
			Seed(e0.B, (e0.Sb - from.S) / speed0);
			double speed1 = e1.Length / Math.Max(1e-6, e1.Time);
			double best = double.MaxValue;
			int bestEnd = -1;
			while (heap.Count > 0)
			{
				int u = heap.Pop(out double c);
				if (c > cost[u])
				{
					continue;
				}
				if (c >= best)
				{
					break;
				}
				// Into the end edge from this node.
				if (u == e1.A || u == e1.B)
				{
					double inner = u == e1.A ? (to.S - e1.Sa) / speed1 : (e1.Sb - to.S) / speed1;
					if (c + inner < best)
					{
						best = c + inner;
						bestEnd = u;
					}
				}
				foreach (int e in Links[u])
				{
					Edge edge = Edges[e];
					int v = edge.A == u ? edge.B : edge.A;
					double nc = c + edge.Time;
					if (nc < cost[v])
					{
						if (cost[v] == double.MaxValue)
						{
							touched.Add(v);
						}
						cost[v] = nc;
						via[v] = e;
						heap.Push(nc, v);
					}
				}
			}
			if (bestEnd < 0)
			{
				return false;
			}
			// Back from the end edge's entry node to the start edge.
			route.Add((to.Edge, bestEnd == e1.A ? 1 : -1));
			int at = bestEnd;
			while (via[at] >= 0)
			{
				Edge edge = Edges[via[at]];
				int prev = edge.A == at ? edge.B : edge.A;
				route.Add((via[at], edge.B == at ? 1 : -1));
				at = prev;
			}
			route.Add((from.Edge, at == e0.B ? 1 : -1));
			route.Reverse();
			return true;
		}

		/// <summary>
		/// What <see cref="RouteTimed"/> adds for a turn: seconds lost at node <paramref name="node"/> coming in on its link
		/// <paramref name="inLink"/> and leaving on <paramref name="outLink"/> (indices in <see cref="Links"/>), reaching it at day time
		/// <paramref name="t"/>.
		/// </summary>
		internal delegate double TurnDelay(int node, int inLink, int outLink, double t);

		/// <summary>
		/// As <see cref="Route"/>, but the cost also counts what is lost at each junction, by the movement through it (the way in and the way
		/// out) and the time the car gets there (<paramref name="t0"/> when it sets off, plus the cost so far): a driver who knows the queues
		/// at that hour. Dijkstra over the edges arrived along (a turn's cost needs the way in), ties by index. <paramref name="cost"/> and
		/// <paramref name="via"/> are two per edge, MaxValue and -1 outside <paramref name="touched"/>.
		/// </summary>
		internal static bool RouteTimed(Anchor from, Anchor to, double t0, TurnDelay delay, List<(int edge, int dir)> route, Heap heap, double[] cost, int[] via, List<int> touched)
		{
			route.Clear();
			Edge e0 = Edges[from.Edge], e1 = Edges[to.Edge];
			if (from.Edge == to.Edge)
			{
				route.Add((from.Edge, to.S >= from.S ? 1 : -1));
				return true;
			}
			foreach (int st in touched)
			{
				cost[st] = double.MaxValue;
				via[st] = -1;
			}
			touched.Clear();
			heap.Clear();
			// A state: edge e driven the way d to its far end (2e driven A to B, 2e + 1 driven B to A).
			void Reach(int st, double c, int from)
			{
				if (c < cost[st])
				{
					if (cost[st] == double.MaxValue)
					{
						touched.Add(st);
					}
					cost[st] = c;
					via[st] = from;
					heap.Push(c, st);
				}
			}
			double speed0 = e0.Length / Math.Max(1e-6, e0.Time);
			Reach(2 * from.Edge + 1, (from.S - e0.Sa) / speed0, -2);
			Reach(2 * from.Edge, (e0.Sb - from.S) / speed0, -2);
			double speed1 = e1.Length / Math.Max(1e-6, e1.Time);
			double best = double.MaxValue;
			int bestState = -1;
			while (heap.Count > 0)
			{
				int st = heap.Pop(out double c);
				if (c > cost[st])
				{
					continue;
				}
				if (c >= best)
				{
					break;
				}
				int ein = st >> 1;
				Edge inEdge = Edges[ein];
				int u = (st & 1) == 0 ? inEdge.B : inEdge.A;
				List<int> links = Links[u];
				int inLink = links.IndexOf(ein);
				for (int j = 0; j < links.Count; j++)
				{
					int e = links[j];
					if (e == ein)
					{
						continue;
					}
					double d = delay(u, inLink, j, t0 + c);
					if (e == to.Edge)
					{
						double inner = u == e1.A ? (to.S - e1.Sa) / speed1 : (e1.Sb - to.S) / speed1;
						if (c + d + inner < best)
						{
							best = c + d + inner;
							bestState = st;
						}
						continue;
					}
					Edge edge = Edges[e];
					Reach(edge.A == u ? 2 * e : 2 * e + 1, c + d + edge.Time, st);
				}
			}
			if (bestState < 0)
			{
				return false;
			}
			{
				Edge last = Edges[bestState >> 1];
				int end = (bestState & 1) == 0 ? last.B : last.A;
				route.Add((to.Edge, end == e1.A ? 1 : -1));
			}
			for (int st = bestState; st >= 0; st = via[st])
			{
				route.Add((st >> 1, (st & 1) == 0 ? 1 : -1));
			}
			route.Reverse();
			return true;
		}
	}
}
