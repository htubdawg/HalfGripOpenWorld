using System;
using System.Collections.Generic;

namespace HalfGripOpenWorld
{
	/// <summary>
	/// The road signs, British style:
	///
	///   - a place-name sign (white board, black letters, on two posts) on the nearside of every road coming into a town or village,
	///     where it crosses the place's edge, facing the traffic coming in;
	///   - a fingerpost where main roads meet, and wherever the way to a race course turns off: a white post with a pointed
	///     finger for each way that leads somewhere, pointing along that road, giving the nearest town or village that way and the
	///     nearest town, with the miles to each by road; brown fingers for the race courses.
	///
	/// The ways and miles come from the roads themselves: the roads are turned into a graph of junctions (a road's end on another road,
	/// two roads crossing), and the shortest way by road (main roads counted a little shorter, so the signs send you along them) from
	/// every place to every junction says which way at each junction leads to it, and how far. The lettering is drawn from the names in
	/// <see cref="Names"/> when the world is built (<see cref="Lettering"/>), into one texture of every board's face (<see cref="Atlas"/>),
	/// and each board is sized to fit its words: rename a place and its signs change with it.
	///
	/// Laid out once after the roads, lots and parks, with doubles and <see cref="DetMath"/> only, so every player gets the same signs;
	/// houses (<see cref="Buildings"/>) and trees keep off them. Each sign is built by the tile its foot is in.
	/// </summary>
	internal static class Signs
	{
		/// <summary>World units to a pixel of lettering: a village's name sign, a town's (bigger), a finger.</summary>
		internal const double VillagePixel = 0.22;
		internal const double TownPixel = 0.27;
		internal const double FingerPixel = 0.15;

		/// <summary>A board's thickness, a post's side.</summary>
		internal const double Thickness = 0.3;
		internal const double PostSide = 0.5;

		/// <summary>A name sign's board stands this high over the ground (about a metre), clear of the verge's grass.</summary>
		private const double NameLift = 3.3;

		/// <summary>A fingerpost's lowest finger is this high over the ground, over a car's roof; the gap between fingers.</summary>
		private const double FingerLowest = 5.0;
		private const double FingerGap = 0.3;

		/// <summary>Places nearer than this by road (about 150 m) are not signed: you are there.</summary>
		private const double Nearest = 500.0;

		/// <summary>A mile in game units (3.3 to a metre).</summary>
		private const double Mile = 1609.344 * 3.3;

		/// <summary>How far away a town is still signed, and a race course.</summary>
		private const double TownReach = 16.0 * Mile;
		private const double CourseReach = 10.0 * Mile;

		/// <summary>The graph's cost per unit of road by class (<see cref="RoadKind"/>): main roads cheaper, so the signs follow them.</summary>
		private static readonly double[] Costs = { 0.6, 0.75, 0.9, 1.15, 1.4, 2.5, 99.0, 99.0, 99.0 };

		internal enum Look
		{
			/// <summary>Black letters and border on white.</summary>
			White = 0,
			/// <summary>White letters and border on brown: the way to a race course.</summary>
			Brown = 1,
		}

		/// <summary>A board's face in the atlas, in texels from its top-left corner.</summary>
		internal struct Face
		{
			public int X;
			public int Y;
			public int W;
			public int H;
		}

		/// <summary>
		/// A board: a flat box standing upright, its front facing (Nx, Nz). Seen from the front, its face runs left to right along the
		/// left of (Nx, Nz) as the viewer sees it; a finger's point is at its front face's left end (and so at its back face's right end).
		/// </summary>
		internal sealed class Board
		{
			public double X;
			public double Z;
			public double Nx;
			public double Nz;
			/// <summary>Its lower edge over the sign's foot.</summary>
			public double Lift;
			public double W;
			public double H;
			/// <summary>A finger, pointed; the point's length as a share of its width.</summary>
			public bool Pointed;
			public double Tip;
			public Face Front;
			public Face Back;
		}

		internal sealed class Post
		{
			public double X;
			public double Z;
			/// <summary>Its top over the sign's foot.</summary>
			public double Top;
		}

		internal sealed class Sign
		{
			public bool Finger;
			/// <summary>Where it stands: its post, or the middle between its posts. Its foot is the ground there.</summary>
			public double X;
			public double Z;
			/// <summary>How far round (X, Z) its parts reach, for keeping buildings and trees off.</summary>
			public double Radius;
			public readonly List<Post> Posts = new List<Post>();
			public readonly List<Board> Boards = new List<Board>();
			/// <summary>What it says, for the log and the checks.</summary>
			public string Text;
		}

		internal static readonly List<Sign> All = new List<Sign>();

		/// <summary>Every board's face, RGBA, rows from the bottom (as a Unity texture's), <see cref="AtlasW"/> by <see cref="AtlasH"/>.</summary>
		internal static byte[] Atlas;
		internal static int AtlasW;
		internal static int AtlasH;

		/// <summary>Plain patches in the atlas for the posts and the boards' edges and backs.</summary>
		internal static Face WhitePatch;
		internal static Face GreyPatch;
		internal static Face BrownPatch;

		/// <summary>How the layout went, for the log.</summary>
		internal static string Report = "";

		internal static int NameSigns;
		internal static int Fingerposts;
		internal static int Fingers;

		// ---- the road graph ----------------------------------------------------------------------------------------

		private sealed class Edge
		{
			public int A;
			public int B;
			public int Road;
			public double Sa;
			public double Sb;
			public double Length;
			public double Cost;
		}

		private static readonly List<(double x, double z)> nodes = new List<(double x, double z)>();
		private static readonly Dictionary<long, List<int>> nodeCells = new Dictionary<long, List<int>>();
		private static readonly List<Edge> edges = new List<Edge>();
		private static List<int>[] links;

		/// <summary>A place or course the signs point to, and the shortest way to it from every junction.</summary>
		private sealed class Destination
		{
			public string Name;
			public bool Town;
			public bool Course;
			public int Node;
			public double[] Cost;
			public double[] Length;
			/// <summary>Per junction, the edge that leads on towards the destination (-1: none, or the destination itself).</summary>
			public int[] Via;
		}

		private static readonly List<Destination> destinations = new List<Destination>();

		/// <summary>Two points this close are one junction (a road's end on another road's line, where they cross).</summary>
		private const double Merge = 8.0;
		private const double NodeCell = 16.0;

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
						double d = (nodes[n].x - x) * (nodes[n].x - x) + (nodes[n].z - z) * (nodes[n].z - z);
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
			nodes.Add((x, z));
			if (!nodeCells.TryGetValue(Key(ci, cj), out List<int> cell))
			{
				cell = new List<int>();
				nodeCells[Key(ci, cj)] = cell;
			}
			cell.Add(nodes.Count - 1);
			return nodes.Count - 1;
		}

		/// <summary>The roads in the graph: every road but the footpaths and the courses themselves (their access roads are in).</summary>
		private static bool InGraph(Network.Road road)
		{
			return road.Kind != RoadKind.Path && road.Kind != RoadKind.Rally && road.Kind != RoadKind.Climb && road.Count >= 2;
		}

		private const double PieceCell = 50.0;

		/// <summary>The graph roads' pieces (road << 32 | sample), by 50-unit cell.</summary>
		private static Dictionary<long, List<long>> Pieces()
		{
			Dictionary<long, List<long>> grid = new Dictionary<long, List<long>>();
			for (int r = 0; r < Network.Roads.Count; r++)
			{
				Network.Road road = Network.Roads[r];
				if (!InGraph(road))
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
							if (!grid.TryGetValue(Key(c, j), out List<long> list))
							{
								list = new List<long>();
								grid[Key(c, j)] = list;
							}
							list.Add(((long)r << 32) | (uint)i);
						}
					}
				}
			}
			return grid;
		}

		/// <summary>The pieces within <paramref name="reach"/> of (x, z) by cell, each once, in order.</summary>
		private static List<long> PiecesNear(Dictionary<long, List<long>> grid, double x0, double z0, double x1, double z1, double reach)
		{
			SortedSet<long> found = new SortedSet<long>();
			for (int j = (int)Math.Floor((Math.Min(z0, z1) - reach) / PieceCell); j <= (int)Math.Floor((Math.Max(z0, z1) + reach) / PieceCell); j++)
			{
				for (int i = (int)Math.Floor((Math.Min(x0, x1) - reach) / PieceCell); i <= (int)Math.Floor((Math.Max(x0, x1) + reach) / PieceCell); i++)
				{
					if (grid.TryGetValue(Key(i, j), out List<long> list))
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

		/// <summary>The point at distance <paramref name="s"/> along a road's line.</summary>
		private static void At(Network.Road road, double s, out double x, out double z)
		{
			Network.Locate(road, s, out int i, out double t);
			x = road.X[i] + (road.X[i + 1] - road.X[i]) * t;
			z = road.Z[i] + (road.Z[i + 1] - road.Z[i]) * t;
		}

		/// <summary>
		/// The graph: a junction wherever a road's end lies on another road (within its half width) and wherever two roads cross, and at
		/// the road nearest each place's middle; an edge along each road between its junctions.
		/// </summary>
		private static void Graph()
		{
			nodes.Clear();
			nodeCells.Clear();
			edges.Clear();
			Dictionary<long, List<long>> grid = Pieces();
			List<(double s, int node)>[] stops = new List<(double s, int node)>[Network.Roads.Count];
			for (int r = 0; r < Network.Roads.Count; r++)
			{
				stops[r] = new List<(double s, int node)>();
			}
			// Ends: on the nearest other road whose paving they are on, else where they are (a dead end, or two roads end to end).
			for (int r = 0; r < Network.Roads.Count; r++)
			{
				Network.Road road = Network.Roads[r];
				if (!InGraph(road))
				{
					continue;
				}
				foreach (int end in new[] { 0, road.Count - 1 })
				{
					double ex = road.X[end], ez = road.Z[end];
					int bestRoad = -1;
					double bestD = double.MaxValue, bestS = 0.0, bx = 0.0, bz = 0.0;
					foreach (long p in PiecesNear(grid, ex, ez, ex, ez, 40.0))
					{
						int q = (int)(p >> 32), i = (int)(uint)p;
						if (q == r)
						{
							continue;
						}
						Network.Road other = Network.Roads[q];
						Lines.Project(other.X, other.Z, i, ex, ez, out double t, out double d);
						if (d <= other.Half + 3.0 && d < bestD)
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
			// Crossings: each pair of pieces of two roads that cross, found once (from the lower road).
			for (int r = 0; r < Network.Roads.Count; r++)
			{
				Network.Road road = Network.Roads[r];
				if (!InGraph(road))
				{
					continue;
				}
				for (int i = 0; i + 1 < road.Count; i++)
				{
					double ax = road.X[i], az = road.Z[i], bx = road.X[i + 1], bz = road.Z[i + 1];
					foreach (long p in PiecesNear(grid, ax, az, bx, bz, 0.0))
					{
						int q = (int)(p >> 32), j = (int)(uint)p;
						if (q <= r)
						{
							continue;
						}
						Network.Road other = Network.Roads[q];
						if (Cross(ax, az, bx, bz, other.X[j], other.Z[j], other.X[j + 1], other.Z[j + 1], out double ta, out double tb))
						{
							int node = NodeAt(ax + (bx - ax) * ta, az + (bz - az) * ta);
							stops[r].Add((road.S[i] + (road.S[i + 1] - road.S[i]) * ta, node));
							stops[q].Add((other.S[j] + (other.S[j + 1] - other.S[j]) * tb, node));
						}
					}
				}
			}
			// The places: a junction on the road nearest each town's and village's middle.
			destinations.Clear();
			foreach (Places.Place place in Places.All)
			{
				if (place.Kind == PlaceKind.Hamlet)
				{
					continue;
				}
				int bestRoad = -1;
				double bestD = place.Radius * 0.5, bestS = 0.0, bx = 0.0, bz = 0.0;
				foreach (long p in PiecesNear(grid, place.X, place.Z, place.X, place.Z, bestD))
				{
					int q = (int)(p >> 32), i = (int)(uint)p;
					Network.Road road = Network.Roads[q];
					if (road.Kind > RoadKind.Street)
					{
						continue;
					}
					Lines.Project(road.X, road.Z, i, place.X, place.Z, out double t, out double d);
					if (d < bestD)
					{
						bestD = d;
						bestRoad = q;
						bestS = road.S[i] + (road.S[i + 1] - road.S[i]) * t;
						bx = road.X[i] + (road.X[i + 1] - road.X[i]) * t;
						bz = road.Z[i] + (road.Z[i + 1] - road.Z[i]) * t;
					}
				}
				if (bestRoad >= 0)
				{
					int node = NodeAt(bx, bz);
					stops[bestRoad].Add((bestS, node));
					destinations.Add(new Destination { Name = place.Name, Town = place.Kind == PlaceKind.Town, Node = node });
				}
			}
			// The courses: their access roads' far ends (the circuit's paddock gate, the rally's service area, the hill climb's foot).
			foreach ((Network.Road road, string name) in new[] { (Network.CircuitRoad, Names.Circuit), (Network.RallyAccess, Names.Rally), (Network.ClimbAccess, Names.HillClimb) })
			{
				if (road != null && road.Count >= 2)
				{
					destinations.Add(new Destination { Name = name, Course = true, Node = NodeAt(road.X[0], road.Z[0]) });
				}
			}
			// The edges between each road's junctions, in order along it.
			for (int r = 0; r < Network.Roads.Count; r++)
			{
				List<(double s, int node)> list = stops[r];
				list.Sort((a, b) => a.s != b.s ? a.s.CompareTo(b.s) : a.node.CompareTo(b.node));
				for (int k = 0; k + 1 < list.Count; k++)
				{
					if (list[k].node == list[k + 1].node || list[k + 1].s - list[k].s < 1e-6)
					{
						continue;
					}
					double length = list[k + 1].s - list[k].s;
					edges.Add(new Edge { A = list[k].node, B = list[k + 1].node, Road = r, Sa = list[k].s, Sb = list[k + 1].s, Length = length, Cost = length * Costs[(int)Network.Roads[r].Kind] });
				}
			}
			links = new List<int>[nodes.Count];
			for (int n = 0; n < nodes.Count; n++)
			{
				links[n] = new List<int>();
			}
			for (int e = 0; e < edges.Count; e++)
			{
				links[edges[e].A].Add(e);
				links[edges[e].B].Add(e);
			}
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

		/// <summary>The shortest way from every junction to each destination (Dijkstra from the destination, ties by index).</summary>
		private static void Ways()
		{
			foreach (Destination d in destinations)
			{
				int n = nodes.Count;
				d.Cost = new double[n];
				d.Length = new double[n];
				d.Via = new int[n];
				for (int k = 0; k < n; k++)
				{
					d.Cost[k] = double.MaxValue;
					d.Via[k] = -1;
				}
				d.Cost[d.Node] = 0.0;
				Heap heap = new Heap();
				heap.Push(0.0, d.Node);
				while (heap.Count > 0)
				{
					int u = heap.Pop(out double c);
					if (c > d.Cost[u])
					{
						continue;
					}
					foreach (int e in links[u])
					{
						Edge edge = edges[e];
						int v = edge.A == u ? edge.B : edge.A;
						double nc = c + edge.Cost;
						if (nc < d.Cost[v])
						{
							d.Cost[v] = nc;
							d.Length[v] = d.Length[u] + edge.Length;
							d.Via[v] = e;
							heap.Push(nc, v);
						}
					}
				}
			}
		}

		// ---- where the signs go ------------------------------------------------------------------------------------

		/// <summary>One way out of a junction: its edges (roads leaving in about the same direction), its heading, its biggest road.</summary>
		private sealed class Arm
		{
			public readonly List<int> Edges = new List<int>();
			public double Dx;
			public double Dz;
			public double Angle;
			public RoadKind Kind;
			public double Half;
			public bool Access;
		}

		/// <summary>The ways out of junction <paramref name="n"/>, by angle; edges leaving within 20 degrees of each other are one way.</summary>
		private static List<Arm> Arms(int n)
		{
			List<(double angle, int e, double dx, double dz)> out_ = new List<(double, int, double, double)>();
			foreach (int e in links[n])
			{
				Edge edge = edges[e];
				Network.Road road = Network.Roads[edge.Road];
				bool fromA = edge.A == n;
				double s0 = fromA ? edge.Sa : edge.Sb;
				double s1 = fromA ? Math.Min(edge.Sb, edge.Sa + 30.0) : Math.Max(edge.Sa, edge.Sb - 30.0);
				At(road, s0, out double x0, out double z0);
				At(road, s1, out double x1, out double z1);
				double len = Math.Sqrt((x1 - x0) * (x1 - x0) + (z1 - z0) * (z1 - z0));
				if (len < 1e-6)
				{
					continue;
				}
				out_.Add((Network.Atan2(z1 - z0, x1 - x0), e, (x1 - x0) / len, (z1 - z0) / len));
			}
			out_.Sort((a, b) => a.angle != b.angle ? a.angle.CompareTo(b.angle) : a.e.CompareTo(b.e));
			List<Arm> arms = new List<Arm>();
			foreach ((double angle, int e, double dx, double dz) in out_)
			{
				Network.Road road = Network.Roads[edges[e].Road];
				Arm arm = arms.Count > 0 && angle - arms[arms.Count - 1].Angle < 0.35 ? arms[arms.Count - 1] : null;
				if (arm == null)
				{
					arm = new Arm { Dx = dx, Dz = dz, Angle = angle, Kind = road.Kind };
					arms.Add(arm);
				}
				arm.Edges.Add(e);
				arm.Kind = road.Kind < arm.Kind ? road.Kind : arm.Kind;
				arm.Half = Math.Max(arm.Half, road.Half);
				arm.Access |= road == Network.CircuitRoad || road == Network.RallyAccess || road == Network.ClimbAccess;
			}
			// The last and the first, round the circle.
			if (arms.Count > 1 && arms[0].Angle + 2.0 * DetMath.Pi - arms[arms.Count - 1].Angle < 0.35)
			{
				Arm last = arms[arms.Count - 1];
				arms[0].Edges.AddRange(last.Edges);
				arms[0].Kind = last.Kind < arms[0].Kind ? last.Kind : arms[0].Kind;
				arms[0].Half = Math.Max(arms[0].Half, last.Half);
				arms[0].Access |= last.Access;
				arms.RemoveAt(arms.Count - 1);
			}
			return arms;
		}

		private static bool Major(RoadKind kind)
		{
			return kind <= RoadKind.Secondary;
		}

		/// <summary>What one finger says: its look and its lines (a name and the miles).</summary>
		private sealed class Finger
		{
			public Look Look;
			public readonly List<(string name, string miles)> Lines = new List<(string, string)>();
			public double Dx;
			public double Dz;
		}

		/// <summary>
		/// The fingers at junction <paramref name="n"/>: for each way, the destinations whose shortest way goes that way, the nearest
		/// town or village and the nearest town (white), and the race courses (brown).
		/// </summary>
		private static List<Finger> FingersAt(int n, List<Arm> arms)
		{
			List<Finger> fingers = new List<Finger>();
			List<Finger> brown = new List<Finger>();
			foreach (Arm arm in arms)
			{
				List<Destination> places = new List<Destination>(), courses = new List<Destination>();
				foreach (Destination d in destinations)
				{
					if (d.Via[n] < 0 || !arm.Edges.Contains(d.Via[n]) || d.Length[n] < Nearest || d.Length[n] > (d.Course ? CourseReach : TownReach))
					{
						continue;
					}
					(d.Course ? courses : places).Add(d);
				}
				places.Sort((a, b) => a.Length[n] != b.Length[n] ? a.Length[n].CompareTo(b.Length[n]) : string.CompareOrdinal(a.Name, b.Name));
				courses.Sort((a, b) => a.Length[n] != b.Length[n] ? a.Length[n].CompareTo(b.Length[n]) : string.CompareOrdinal(a.Name, b.Name));
				Finger white = new Finger { Look = Look.White, Dx = arm.Dx, Dz = arm.Dz };
				if (places.Count > 0)
				{
					Destination first = places[0];
					white.Lines.Add((first.Name, Lettering.Miles(first.Length[n])));
					Destination town = places.Find(d => d.Town && d != first);
					if (!first.Town && town != null)
					{
						white.Lines.Add((town.Name, Lettering.Miles(town.Length[n])));
					}
					fingers.Add(white);
				}
				if (courses.Count > 0)
				{
					Finger b = new Finger { Look = Look.Brown, Dx = arm.Dx, Dz = arm.Dz };
					for (int k = 0; k < courses.Count && k < 2; k++)
					{
						b.Lines.Add((courses[k].Name, Lettering.Miles(courses[k].Length[n])));
					}
					brown.Add(b);
				}
			}
			fingers.AddRange(brown);
			return fingers;
		}

		/// <summary>
		/// The junctions that get a fingerpost: three or more ways out by main roads; out in the country (outside every town and
		/// village), three or more by paved roads, lanes included, as British lanes have a post at almost every junction (the owner
		/// found the country roads too bare with main-road junctions only); or the way to a race course, from the nearest main road to
		/// its access road.
		/// </summary>
		private static bool[] Signed()
		{
			bool[] signed = new bool[nodes.Count];
			for (int n = 0; n < nodes.Count; n++)
			{
				List<Arm> arms = Arms(n);
				if (arms.Count < 3)
				{
					continue;
				}
				int major = 0, paved = 0;
				bool access = false;
				foreach (Arm arm in arms)
				{
					major += Major(arm.Kind) ? 1 : 0;
					paved += arm.Kind <= RoadKind.Lane ? 1 : 0;
					access |= arm.Access;
				}
				// Three main-road ways: a main road ending on another, or two crossing (a main road past a street's end has only two).
				signed[n] = major >= 3 || access || (paved >= 3 && Rural(nodes[n].x, nodes[n].z));
			}
			// Each course's way in: from the first junction on a main road its shortest way reaches, every junction on to its access road.
			foreach (Destination d in destinations)
			{
				if (!d.Course)
				{
					continue;
				}
				int entry = -1;
				double best = double.MaxValue;
				for (int n = 0; n < nodes.Count; n++)
				{
					if (d.Cost[n] >= best || d.Via[n] < 0)
					{
						continue;
					}
					foreach (int e in links[n])
					{
						if (Major(Network.Roads[edges[e].Road].Kind))
						{
							best = d.Cost[n];
							entry = n;
							break;
						}
					}
				}
				for (int n = entry, steps = 0; n >= 0 && n != d.Node && steps < 10000; steps++)
				{
					if (links[n].Count >= 3)
					{
						signed[n] = true;
					}
					Edge edge = edges[d.Via[n]];
					n = edge.A == n ? edge.B : edge.A;
				}
			}
			return signed;
		}

		/// <summary>Whether (x, z) is out in the country: outside every town's and village's edge (hamlets and farms are country).</summary>
		private static bool Rural(double x, double z)
		{
			foreach (Places.Place place in Places.All)
			{
				if (place.Kind != PlaceKind.Hamlet && Distance(place, x, z) < place.Radius)
				{
					return false;
				}
			}
			return true;
		}

		/// <summary>Whether a sign's part may stand at (x, z): <paramref name="margin"/> off every road's edge, off lots, parks, water and the circuit.</summary>
		private static bool Free(double x, double z, double margin)
		{
			// The circuit's own test walks its whole lap: only near it.
			bool nearCircuit = Circuit.Exists && x > circuitBox.x0 && x < circuitBox.x1 && z > circuitBox.z0 && z < circuitBox.z1;
			if (!Network.Claims(x, z, -1, 0.0, margin) || Town.Taken(x, z, 1.0) || Parks.At(x, z) || (nearCircuit && !Circuit.Clear(x, z, 5.0)))
			{
				return false;
			}
			return !(Rivers.Water(x, z, out double level) && Ground(x, z) < level + 0.5);
		}

		/// <summary>
		/// The ground from sea level, as <see cref="WorldGen.Height"/> gives it in the scene (which would start the layout again from
		/// inside it: it waits for the layout to be built, and this is part of building it).
		/// </summary>
		private static double Ground(double x, double z)
		{
			return Parks.Ground(x, z, Town.Ground(x, z, Network.Ground(x, z, WorldGen.Base(x, z))));
		}

		/// <summary>The circuit's lap's bounds and a wide margin (its band and paddock reach a few hundred past the lap).</summary>
		private static (double x0, double z0, double x1, double z1) circuitBox;

		private static void CircuitBox()
		{
			double x0 = double.MaxValue, z0 = double.MaxValue, x1 = double.MinValue, z1 = double.MinValue;
			for (int i = 0; Circuit.Exists && i < Circuit.Count; i++)
			{
				x0 = Math.Min(x0, Circuit.X[i]);
				z0 = Math.Min(z0, Circuit.Z[i]);
				x1 = Math.Max(x1, Circuit.X[i]);
				z1 = Math.Max(z1, Circuit.Z[i]);
			}
			circuitBox = (x0 - 800.0, z0 - 800.0, x1 + 800.0, z1 + 800.0);
		}

		/// <summary>Whether another sign is within <paramref name="distance"/> of (x, z).</summary>
		private static bool Crowded(double x, double z, double distance)
		{
			foreach (Sign s in All)
			{
				double dx = s.X - x, dz = s.Z - z, r = distance + s.Radius;
				if (dx * dx + dz * dz < r * r)
				{
					return true;
				}
			}
			return false;
		}

		// ---- faces -------------------------------------------------------------------------------------------------

		/// <summary>A face to be drawn: its size, look, lines, and its point's side (0 none, -1 left, 1 right).</summary>
		private sealed class Request
		{
			public int W;
			public int H;
			public Look Look;
			public List<(string name, string miles)> Lines;
			public int Tip;
			public bool Name;
			public int Plain = -1;
			public Face Face;
		}

		private static readonly List<Request> requests = new List<Request>();
		private static readonly Dictionary<string, Request> requested = new Dictionary<string, Request>();

		/// <summary>Space round the words: border, then margin.</summary>
		private const int Border = 1;
		private const int Margin = 3;

		/// <summary>A face's height in pixels for <paramref name="lines"/> lines.</summary>
		private static int FaceHeight(int lines)
		{
			return 2 * (Border + Margin) - 1 + (lines - 1) * Lettering.Line + Lettering.Rows;
		}

		/// <summary>The width of a finger's point for a face <paramref name="h"/> high.</summary>
		private static int TipWidth(int h)
		{
			return (h + 1) / 2;
		}

		private static Request Ask(Look look, List<(string name, string miles)> lines, int tip, bool name)
		{
			string key = (int)look + "|" + tip + "|" + (name ? "N" : "F");
			foreach ((string a, string b) in lines)
			{
				key += "|" + a + "\u0001" + b;
			}
			if (requested.TryGetValue(key, out Request found))
			{
				return found;
			}
			int text = 0;
			foreach ((string a, string b) in lines)
			{
				text = Math.Max(text, Lettering.Width(a) + (b.Length > 0 ? 6 + Lettering.Width(b) : 0));
			}
			int h = FaceHeight(lines.Count);
			Request q = new Request { Look = look, Lines = lines, Tip = tip, Name = name, H = h, W = text + 2 * (Border + Margin) + (tip != 0 ? TipWidth(h) : 0) };
			requests.Add(q);
			requested[key] = q;
			return q;
		}

		private static Request AskPlain(int which)
		{
			Request q = new Request { W = 4, H = 4, Plain = which };
			requests.Add(q);
			return q;
		}

		private static (byte r, byte g, byte b) Paper(Look look)
		{
			return look == Look.Brown ? ((byte)118, (byte)64, (byte)32) : ((byte)236, (byte)236, (byte)230);
		}

		private static (byte r, byte g, byte b) Ink(Look look)
		{
			return look == Look.Brown ? ((byte)240, (byte)238, (byte)230) : ((byte)24, (byte)24, (byte)26);
		}

		private static readonly (byte r, byte g, byte b) Grey = (130, 132, 134);

		/// <summary>Packs every face into rows of a 1024-wide texture (tallest first), then draws them.</summary>
		private static void Draw(List<(Request q, Action<Face> set)> uses)
		{
			const int width = 1024, pad = 2;
			List<Request> order = new List<Request>(requests);
			List<int> index = new List<int>();
			for (int k = 0; k < order.Count; k++)
			{
				index.Add(k);
			}
			index.Sort((a, b) => order[a].H != order[b].H ? order[b].H.CompareTo(order[a].H) : order[a].W != order[b].W ? order[b].W.CompareTo(order[a].W) : a.CompareTo(b));
			int x = 0, y = 0, row = 0;
			foreach (int k in index)
			{
				Request q = order[k];
				int w = Math.Min(q.W, width - 2 * pad);
				if (x + w + 2 * pad > width)
				{
					x = 0;
					y += row;
					row = 0;
				}
				q.Face = new Face { X = x + pad, Y = y + pad, W = w, H = q.H };
				x += w + 2 * pad;
				row = Math.Max(row, q.H + 2 * pad);
			}
			int height = 64;
			while (height < y + row)
			{
				height *= 2;
			}
			AtlasW = width;
			AtlasH = height;
			Atlas = new byte[width * height * 4];
			foreach (Request q in requests)
			{
				Paint(q);
			}
			foreach ((Request q, Action<Face> set) in uses)
			{
				set(q.Face);
			}
		}

		/// <summary>Fills a rectangle of the atlas, counted from its top-left.</summary>
		private static void Fill(int x0, int y0, int w, int h, (byte r, byte g, byte b) c)
		{
			for (int y = y0; y < y0 + h; y++)
			{
				for (int x = x0; x < x0 + w; x++)
				{
					Lettering.Set(Atlas, AtlasW, AtlasH, true, x, y, c.r, c.g, c.b);
				}
			}
		}

		/// <summary>
		/// Draws one face: paper, a border round it (round a finger's point too), and its lines: a name sign's centred, a finger's names
		/// at the left and the miles at the right. The two texels of padding round it take its border's colour, so mipmaps of
		/// one face never pick up its neighbour's.
		/// </summary>
		private static void Paint(Request q)
		{
			Face f = q.Face;
			if (q.Plain >= 0)
			{
				Fill(f.X - 2, f.Y - 2, f.W + 4, f.H + 4, q.Plain == 0 ? Paper(Look.White) : q.Plain == 1 ? Grey : Paper(Look.Brown));
				return;
			}
			(byte r, byte g, byte b) paper = Paper(q.Look), ink = Ink(q.Look);
			Fill(f.X - 2, f.Y - 2, f.W + 4, f.H + 4, ink);
			int tipW = q.Tip != 0 ? TipWidth(f.H) : 0;
			int rx0 = q.Tip < 0 ? tipW : 0, rx1 = q.Tip > 0 ? f.W - tipW : f.W;
			double mid = (f.H - 1) * 0.5;
			for (int y = 0; y < f.H; y++)
			{
				for (int x = 0; x < f.W; x++)
				{
					if (Inside(x, y, f.W, f.H, tipW, q.Tip, mid, Border))
					{
						Lettering.Set(Atlas, AtlasW, AtlasH, true, f.X + x, f.Y + y, paper.r, paper.g, paper.b);
					}
				}
			}
			int left = rx0 + Border + Margin, right = rx1 - Border - Margin;
			for (int k = 0; k < q.Lines.Count; k++)
			{
				(string name, string miles) = q.Lines[k];
				int top = f.Y + Border + Margin + k * Lettering.Line;
				if (q.Name)
				{
					Lettering.Draw(Atlas, AtlasW, AtlasH, true, f.X + (f.W - Lettering.Width(name)) / 2, top, name, 1, ink.r, ink.g, ink.b);
					continue;
				}
				// The name at the left and the miles at the right, on both faces, so each reads the usual way.
				Lettering.Draw(Atlas, AtlasW, AtlasH, true, f.X + left, top, name, 1, ink.r, ink.g, ink.b);
				Lettering.Draw(Atlas, AtlasW, AtlasH, true, f.X + right - Lettering.Width(miles), top, miles, 1, ink.r, ink.g, ink.b);
			}
		}

		/// <summary>Whether pixel (x, y) of a face is inside its border: the rectangle, and a finger's point tapering to its tip.</summary>
		private static bool Inside(int x, int y, int w, int h, int tipW, int tip, double mid, int inset)
		{
			if (y < inset || y >= h - inset)
			{
				return false;
			}
			// Distance in from the face's outer end at the point's side.
			int from = tip < 0 ? x : tip > 0 ? w - 1 - x : int.MaxValue;
			int other = tip < 0 ? w - 1 - x : tip > 0 ? x : Math.Min(x, w - 1 - x);
			if (other < inset)
			{
				return false;
			}
			if (tip == 0 || from >= tipW)
			{
				return tip != 0 || from >= inset;
			}
			// In the point: the half height it reaches grows from the tip (0) to the rectangle (h / 2), less the border.
			double reach = (from + 0.5) / tipW * (h * 0.5) - inset * 1.5;
			return Math.Abs(y - mid) <= reach;
		}

		// ---- building the signs -----------------------------------------------------------------------------------

		/// <summary>Lays out every sign and draws the atlas. After the roads, lots and parks (<see cref="Network"/>'s last step).</summary>
		internal static void Build()
		{
			All.Clear();
			requests.Clear();
			requested.Clear();
			NameSigns = Fingerposts = Fingers = 0;
			CircuitBox();
			System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
			Graph();
			Report = nodes.Count + " junctions, " + edges.Count + " edges, " + destinations.Count + " destinations; graph " + watch.ElapsedMilliseconds + " ms";
			Ways();
			List<(Request q, Action<Face> set)> uses = new List<(Request, Action<Face>)>();
			Request white = AskPlain(0), grey = AskPlain(1), brown = AskPlain(2);
			uses.Add((white, f => WhitePatch = f));
			uses.Add((grey, f => GreyPatch = f));
			uses.Add((brown, f => BrownPatch = f));
			FingerpostSigns(uses);
			Report += ", fingerposts " + watch.ElapsedMilliseconds + " ms";
			NameSignsAll(uses);
			Draw(uses);
			Report += ", all " + watch.ElapsedMilliseconds + " ms";
		}

		/// <summary>A fingerpost at every signed junction: its post in the widest angle between the roads, back far enough that no finger reaches over a road.</summary>
		private static void FingerpostSigns(List<(Request q, Action<Face> set)> uses)
		{
			bool[] signed = Signed();
			for (int n = 0; n < nodes.Count; n++)
			{
				if (!signed[n])
				{
					continue;
				}
				List<Arm> arms = Arms(n);
				List<Finger> fingers = FingersAt(n, arms);
				if (fingers.Count < 2 || Crowded(nodes[n].x, nodes[n].z, 120.0))
				{
					continue;
				}
				// Each finger's size in the world.
				List<(Finger f, double w, double h)> sized = new List<(Finger, double, double)>();
				double reach = 0.0, half = 0.0;
				foreach (Finger f in fingers)
				{
					Request q = Ask(f.Look, f.Lines, 1, false);
					double w = q.W * FingerPixel, h = q.H * FingerPixel;
					sized.Add((f, w, h));
					reach = Math.Max(reach, w);
				}
				foreach (Arm arm in arms)
				{
					half = Math.Max(half, arm.Half);
				}
				// The gaps between the ways, widest first.
				List<(double gap, double angle)> gaps = new List<(double, double)>();
				for (int k = 0; k < arms.Count; k++)
				{
					double a = arms[k].Angle, b = k + 1 < arms.Count ? arms[k + 1].Angle : arms[0].Angle + 2.0 * DetMath.Pi;
					gaps.Add((b - a, a + (b - a) * 0.5));
				}
				gaps.Sort((p, q) => p.gap != q.gap ? q.gap.CompareTo(p.gap) : p.angle.CompareTo(q.angle));
				bool placed = false;
				foreach ((double gap, double angle) in gaps)
				{
					double bx = DetMath.Cos(angle), bz = DetMath.Sin(angle);
					for (double d = half + Network.Verge + 2.0; d <= half + Network.Verge + 2.0 + reach + 12.0 && !placed; d += 2.0)
					{
						double px = nodes[n].x + bx * d, pz = nodes[n].z + bz * d;
						if (!Free(px, pz, Network.Verge + 1.0) || !FingersClear(px, pz, sized))
						{
							continue;
						}
						Sign sign = new Sign { Finger = true, X = px, Z = pz, Radius = reach + PostSide };
						double y = FingerLowest;
						// Stacked from the bottom: brown fingers lowest, then white (the main ways at the top).
						for (int k = sized.Count - 1; k >= 0; k--)
						{
							(Finger f, double w, double h) = sized[k];
							Board board = new Board
							{
								X = px + f.Dx * (PostSide * 0.5 + w * 0.5),
								Z = pz + f.Dz * (PostSide * 0.5 + w * 0.5),
								// Its front faces the left of the way it points, so its point is at the front face's left end.
								Nx = -f.Dz,
								Nz = f.Dx,
								Lift = y,
								W = w,
								H = h,
								Pointed = true,
							};
							Request front = Ask(f.Look, f.Lines, -1, false), back = Ask(f.Look, f.Lines, 1, false);
							board.Tip = TipWidth(front.H) / (double)front.W;
							uses.Add((front, face => board.Front = face));
							uses.Add((back, face => board.Back = face));
							sign.Boards.Add(board);
							y += h + FingerGap;
							Fingers++;
						}
						sign.Posts.Add(new Post { X = px, Z = pz, Top = y + 0.4 });
						sign.Text = "fingerpost:";
						foreach ((Finger f, double _, double _) in sized)
						{
							foreach ((string name, string miles) in f.Lines)
							{
								sign.Text += " " + name + " " + miles + ";";
							}
						}
						All.Add(sign);
						Fingerposts++;
						placed = true;
					}
					if (placed)
					{
						break;
					}
				}
			}
		}

		/// <summary>Whether every finger at a post at (x, z) is clear of the roads' paving, so none reaches out over a road.</summary>
		private static bool FingersClear(double x, double z, List<(Finger f, double w, double h)> sized)
		{
			foreach ((Finger f, double w, double _) in sized)
			{
				// Every 2 along it, and its tip.
				double tip = PostSide * 0.5 + w;
				for (double a = 1.0; a < tip + 2.0; a += 2.0)
				{
					double at = Math.Min(a, tip);
					if (!Free(x + f.Dx * at, z + f.Dz * at, 0.5))
					{
						return false;
					}
				}
			}
			return true;
		}

		/// <summary>
		/// A name sign on every road (main and minor roads, streets and lanes; not the trunk road, which bypasses the places) where it
		/// comes into a town or village: at the place's edge, on the nearside of the traffic coming in, facing it. Moved along the road
		/// (up to 64) if a junction or a lot is in the way.
		/// </summary>
		private static void NameSignsAll(List<(Request q, Action<Face> set)> uses)
		{
			Dictionary<Places.Place, Request> faces = new Dictionary<Places.Place, Request>();
			foreach (Places.Place place in Places.All)
			{
				if (place.Kind == PlaceKind.Hamlet)
				{
					continue;
				}
				for (int r = 0; r < Network.Roads.Count; r++)
				{
					Network.Road road = Network.Roads[r];
					if (road.Kind < RoadKind.Primary || road.Kind > RoadKind.Lane)
					{
						continue;
					}
					for (int i = 0; i + 1 < road.Count; i++)
					{
						double d0 = Distance(place, road.X[i], road.Z[i]), d1 = Distance(place, road.X[i + 1], road.Z[i + 1]);
						if ((d0 >= place.Radius) == (d1 >= place.Radius))
						{
							continue;
						}
						// The way in: along the road if it crosses inwards from i to i + 1, else back along it.
						int dir = d0 >= place.Radius ? 1 : -1;
						double s = road.S[i] + (road.S[i + 1] - road.S[i]) * (d0 - place.Radius) / (d0 - d1);
						if (!faces.TryGetValue(place, out Request face))
						{
							face = Ask(Look.White, new List<(string, string)> { (place.Name, "") }, 0, true);
							faces[place] = face;
						}
						NameSign(uses, place, face, road, s, dir);
					}
				}
			}
		}

		private static double Distance(Places.Place p, double x, double z)
		{
			return Math.Sqrt((x - p.X) * (x - p.X) + (z - p.Z) * (z - p.Z));
		}

		private static void NameSign(List<(Request q, Action<Face> set)> uses, Places.Place place, Request face, Network.Road road, double s0, int dir)
		{
			double pixel = place.Kind == PlaceKind.Town ? TownPixel : VillagePixel;
			double w = face.W * pixel, h = face.H * pixel;
			double length = road.S[road.Count - 1];
			// Tried at the edge, then further out (before the place) and in, by turns.
			for (int k = 0; k <= 16; k++)
			{
				double s = s0 + (k % 2 == 0 ? -1 : 1) * dir * ((k + 1) / 2) * 8.0;
				if (s < 20.0 || s > length - 20.0 || !Network.Shown(road, s))
				{
					continue;
				}
				Network.Locate(road, s, out int i, out double t);
				if (road.Bridge[i] || road.Bridge[i + 1])
				{
					continue;
				}
				double dx = (road.X[i + 1] - road.X[i]) * dir, dz = (road.Z[i + 1] - road.Z[i]) * dir;
				double len = Math.Sqrt(dx * dx + dz * dz);
				if (len < 1e-6)
				{
					continue;
				}
				dx /= len;
				dz /= len;
				At(road, s, out double cx, out double cz);
				// The nearside, on the left of the way in; the board's near end a unit behind the verge.
				double lx = -dz, lz = dx;
				double near = Network.HalfAt(road, s) + Network.Verge + 1.0;
				double mx = cx + lx * (near + w * 0.5), mz = cz + lz * (near + w * 0.5);
				bool clear = !Crowded(mx, mz, w * 0.5 + 20.0);
				for (double a = 0.0; a <= w + 0.01 && clear; a += Math.Max(1.0, w / 8.0))
				{
					clear = Free(cx + lx * (near + a), cz + lz * (near + a), Network.Verge + 0.5);
				}
				if (!clear)
				{
					continue;
				}
				Sign sign = new Sign { X = mx, Z = mz, Radius = w * 0.5 + 0.5, Text = "name: " + place.Name };
				Board board = new Board { X = mx, Z = mz, Nx = -dx, Nz = -dz, Lift = NameLift, W = w, H = h };
				uses.Add((face, f => board.Front = f));
				sign.Boards.Add(board);
				// Two posts a little in from the board's ends, up to its top.
				foreach (double a in new[] { -0.5 * w + 0.6, 0.5 * w - 0.6 })
				{
					sign.Posts.Add(new Post { X = mx + lx * a, Z = mz + lz * a, Top = NameLift + h - 0.2 });
				}
				All.Add(sign);
				NameSigns++;
				return;
			}
		}

		// ---- for the buildings and trees --------------------------------------------------------------------------

		/// <summary>Whether a sign's parts reach within <paramref name="radius"/> of (x, z): buildings keep off them.</summary>
		internal static bool Near(double x, double z, double radius)
		{
			foreach (Sign s in All)
			{
				double dx = s.X - x, dz = s.Z - z, r = radius + s.Radius;
				if (dx * dx + dz * dz < r * r)
				{
					return true;
				}
			}
			return false;
		}
	}
}
