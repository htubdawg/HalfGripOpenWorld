using System;
using System.Collections.Generic;

namespace HalfGripOpenWorld
{
	/// <summary>
	/// The hill climb: a narrow tarmac road, about 1.5 to 2 km real (a British hill climb's length), from a valley floor up to a col (a
	/// pass between two summits) in the northern mountains, winding, with hairpins where the ground is steep. Laid out once from the
	/// world's seed after the towns, villages, circuit and rally stage, before the farms (which keep off it) and every road (which go
	/// round it). A lane joins its foot to the network.
	///
	///   1. **Site.** The cols: points in the mountains where the land round them (a ring of 400) rises and falls twice over, both ways by
	///      more than 40 (a saddle), best the deepest. Each col's top straight is settled first (<see cref="Top"/>: a line near it, gentle,
	///      with room for the turning area beside its end), then for each the valley floors 1.5 to 4.5 km off, 250 or more below, about
	///      flat and in the mountains, each with a start straight that has room for the paddock (<see cref="StartWay"/>). Each pair is
	///      routed on a coarse 100-unit grid of the whole north (costed as below, no turning limit) and ranked by how much it climbs and
	///      how near its length is to the target.
	///   2. **Road.** The best few pairs are routed again by A* on a 25-unit grid along the coarse route (600 either side), each state a
	///      cell and a heading (8 ways), turning at most 45 degrees a step, leaving the start straight in line and arriving in line with
	///      the top straight. A step costs its length times (1 + steepness (grade / soft)²), far more past <see cref="SteepestStep"/>, a
	///      little more for each turn; no step crosses a river (the road has no bridges). The cheapest way up a slope steeper than the road
	///      may be zigzags. Every other cell is kept, rounded (Chaikin) and the corners tighter than <see cref="MinRadius"/> eased. Roads
	///      that come out too short or long, turn too tight, come near themselves or would be cut or built up too far are thrown away; of
	///      those left, the one with the most hairpins and the most climbed is kept.
	///   3. **Network.** <see cref="Network"/> makes it a <see cref="RoadKind.Climb"/> road before any other: paved like a lane, held to
	///      13%, not in the network's segments (no road joins or crosses it) and its ground closed to the router (<see cref="Fenced"/>);
	///      its lane is routed from just behind its foot and drawn straight on from it, so the two meet end to end.
	///
	/// The world's relief sets the length: from a valley floor to a col is 100 to 150 m real, so a longer road would mostly run along
	/// valley floors. Built with plain arithmetic and <see cref="DetMath"/>, ties broken by index, so it is the same on every machine;
	/// read-only once built, so tiles can read it from any thread.
	/// </summary>
	internal static class HillClimb
	{
		/// <summary>Half the road's width: a lane's, 15 across (about 4.5 m), so the lane at its foot runs straight on into it.</summary>
		internal const double Half = 7.5;

		/// <summary>Distance between the road's samples (as the roads').</summary>
		internal const double Step = 8.0;

		/// <summary>The straight start up from the valley floor: the paddock beside it, then the start line near its end.</summary>
		internal const double StartLength = 260.0;
		internal const double PaddockS = 110.0;
		internal const double StartS = 236.0;

		/// <summary>The paddock: a tarmac yard beside the start (a <see cref="LotKind.Paddock"/> lot), the competitors' vans and trailers at the back.</summary>
		internal const double PaddockW = 100.0;
		internal const double PaddockD = 64.0;

		/// <summary>
		/// The straight at the top, through a point near the col <see cref="EndBefore"/> along it: the finish line, then the turning area
		/// beside its end (its middle <see cref="TurnBack"/> from the end).
		/// </summary>
		internal const double EndLength = 200.0;
		internal const double EndBefore = 120.0;
		internal const double FinishBack = 150.0;
		internal const double TurnW = 56.0;
		internal const double TurnD = 40.0;
		internal const double TurnBack = 60.0;

		/// <summary>The stretch at the top held to a straight grade (no steeper than 6%), for the turning area beside it.</summary>
		internal const double TopLength = 100.0;

		/// <summary>About 2 km real; roads between the limits (1.4 to 3 km) are kept.</summary>
		private const double TargetLength = 6600.0;
		private const double MinLength = 4500.0;
		private const double MaxLength = 10000.0;

		/// <summary>The valley floor is this far from the col, and at least <see cref="LeastDrop"/> below it.</summary>
		private const double NearBottom = 1500.0;
		private const double FarBottom = 4500.0;
		private const double LeastDrop = 250.0;

		/// <summary>
		/// The top straight goes through a point within this of the col and no more than <see cref="TopBelow"/> under it: right at a
		/// col the ground rises either side (it is a saddle), so the turning area beside the straight seldom has room there.
		/// </summary>
		private const double TopReach = 500.0;
		private const double TopBelow = 60.0;

		/// <summary>The valley floor is in the mountains (this much of them at least), not out on the farmland below.</summary>
		private const double BottomMask = 0.35;

		/// <summary>
		/// The routing grids, and the steepest step either takes freely (the road is held to 13%: a steeper step is cut down or filled);
		/// steeper ones cost far more (the land round a col is creased, and a hard limit left no way up), and none steeper than a cliff.
		/// </summary>
		private const double Coarse = 100.0;
		private const double Cell = 25.0;
		private const double SteepestStep = 0.25;
		private const double Over = 10.0;
		private const double Cliff = 0.6;
		private const double Soft = 0.10;
		private const double Steep = 1.2;

		/// <summary>What a turn of 45 degrees adds to a step's cost: enough that the road runs straight unless the ground says otherwise.</summary>
		private const double TurnCost = 20.0;

		/// <summary>How far either side of the coarse route the fine route may stray.</summary>
		private const double Corridor = 600.0;

		/// <summary>The tightest corner the road may have, on the curve through samples 3 either side: a hairpin of about 12 m.</summary>
		internal const double MinRadius = 40.0;

		/// <summary>Two stretches more than <see cref="SeparationAlong"/> apart along the road keep this far apart: both roads, their verges and banks.</summary>
		internal const double Separation = 70.0;
		internal const double SeparationAlong = 400.0;

		/// <summary>Nearer along than that (a hairpin's two legs, past its turn), they keep this far apart: both roads and their verges, and a gap.</summary>
		internal const double HairpinApart = 42.0;

		/// <summary>The most the road may be cut into or built up over the land, once held to its grade.</summary>
		private const double MostEarthwork = 33.0;

		/// <summary>The road's grade, as <see cref="Network"/> holds it.</summary>
		internal const double Grade = 0.13;

		/// <summary>Kept clear of towns' and villages' edges.</summary>
		private const double PlaceClearance = 500.0;

		internal static bool Exists;
		/// <summary>The road's samples, from the foot of the start straight to the end of the top straight.</summary>
		internal static double[] X;
		internal static double[] Z;
		internal static double[] S;
		internal static int Count;
		internal static double Length;

		/// <summary>The start straight's direction (up the road) and the end straight's.</summary>
		internal static double DirX;
		internal static double DirZ;
		internal static double EndDirX;
		internal static double EndDirZ;

		/// <summary>The point near the col the top straight goes through, and a point just behind the road's foot that its lane is routed from.</summary>
		internal static double ColX;
		internal static double ColZ;
		internal static double AccessOutX;
		internal static double AccessOutZ;

		/// <summary>The road, once <see cref="Network"/> has made it.</summary>
		internal static Network.Road Road;

		/// <summary>For the log and the preview.</summary>
		internal static double TightestRadius;
		internal static int Hairpins;
		internal static double Climbed;
		internal static string Report = "";

		/// <summary>Where the finish line is, along the road, and the middle of the turning area.</summary>
		internal static double FinishS => Length - FinishBack;
		internal static double TurnS => Length - TurnBack;

		// ---- building --------------------------------------------------------------------------------------------

		/// <summary>
		/// Lays the road out. Called by <see cref="Places.Build"/> once the towns, villages, circuit and rally stage are placed and before
		/// the farms, so the farms keep off it; the rivers are built. If no site or road is found, there is no hill climb and nothing else
		/// changes.
		/// </summary>
		internal static void Build()
		{
			Exists = false;
			Road = null;
			circuitBox = Bounds(Circuit.Exists, Circuit.X, Circuit.Z, Circuit.Count);
			rallyBox = Bounds(Rally.Exists, Rally.X, Rally.Z, Rally.Count);
			List<(double x, double z, double h, double depth)> cols = Cols();
			if (cols.Count == 0)
			{
				Report = "no hill climb: no col";
				return;
			}
			Grid coarse = new Grid(-Limit, Limit, -3000.0, Limit, Coarse);
			// Each col's top straight and each valley floor's start straight are settled first, each with room for its lot; then the
			// coarse route between the end of the one and the start of the other.
			List<(double score, Straight start, Straight top, List<int> path)> pairs = new List<(double, Straight, Straight, List<int>)>();
			int tops = 0;
			foreach ((double cx, double cz, double ch, double _) in cols)
			{
				Straight top = Top(cx, cz, ch);
				if (top == null)
				{
					continue;
				}
				tops++;
				foreach (Straight start in Bottoms(cx, cz, ch))
				{
					List<int> path = coarse.Route(coarse.Index(start.EndX, start.EndZ), coarse.Index(top.X, top.Z), false, 0, out double length);
					if (path == null || length < MinLength * 0.6 || length > MaxLength)
					{
						continue;
					}
					double score = (ch - start.H) - Math.Abs(length - TargetLength) * 0.03;
					pairs.Add((score, start, top, path));
				}
			}
			pairs.Sort((p, q) => p.score != q.score ? q.score.CompareTo(p.score) : (p.start.X != q.start.X ? p.start.X.CompareTo(q.start.X) : p.start.Z.CompareTo(q.start.Z)));
			int tried = 0;
			double bestScore = double.MinValue;
			Line best = null;
			foreach (var pair in pairs)
			{
				if (tried >= 6)
				{
					break;
				}
				tried++;
				Line line = Try(coarse, pair.start, pair.top, pair.path);
				if (line != null && line.Score > bestScore)
				{
					bestScore = line.Score;
					best = line;
				}
			}
			if (best == null)
			{
				Report = "no hill climb: " + cols.Count + " cols (" + tops + " with a top), " + pairs.Count + " routes, none of " + tried + " fitted";
				return;
			}
			Keep(best);
			Index();
			Exists = true;
			Report = "hill climb from (" + X[0].ToString("0") + ", " + Z[0].ToString("0") + ") to the col by (" + ColX.ToString("0") + ", " + ColZ.ToString("0") + "), best of " + tried + " of " + pairs.Count + " routes (" + cols.Count + " cols, " + tops + " with a top), " + Hairpins + " hairpins";
		}

		/// <summary>How far in from the edge the road may go: clear of the edge wall's foot.</summary>
		private const double Limit = WorldGen.Half - WorldGen.WallDepth - 400.0;

		/// <summary>Whether the road may run at (x, z): inside the walls, away from the coast, out of the water, clear of places and the other courses.</summary>
		private static bool Open(double x, double z, List<Places.Place> places)
		{
			if (Math.Abs(x) > Limit || z > Limit || WorldGen.Inland(x, z) < 1500.0)
			{
				return false;
			}
			if (!double.IsNaN(Rivers.SurfaceAt(x, z)) || Rivers.Channel(x, z, 30.0, out double _))
			{
				return false;
			}
			foreach (Places.Place p in places)
			{
				double reach = p.Radius + PlaceClearance;
				if ((p.X - x) * (p.X - x) + (p.Z - z) * (p.Z - z) < reach * reach)
				{
					return false;
				}
			}
			return (!Box(circuitBox, x, z) || Circuit.Clear(x, z, 300.0)) && (!Box(rallyBox, x, z) || Rally.Clear(x, z, 300.0));
		}

		/// <summary>The circuit's and rally stage's bounding boxes (min x, min z, max x, max z), widened by 1000: the courses' own tests loop over their samples.</summary>
		private static double[] circuitBox;
		private static double[] rallyBox;

		private static double[] Bounds(bool exists, double[] xs, double[] zs, int count)
		{
			double[] box = { double.MaxValue, double.MaxValue, double.MinValue, double.MinValue };
			for (int i = 0; exists && i < count; i++)
			{
				box[0] = Math.Min(box[0], xs[i] - 1000.0);
				box[1] = Math.Min(box[1], zs[i] - 1000.0);
				box[2] = Math.Max(box[2], xs[i] + 1000.0);
				box[3] = Math.Max(box[3], zs[i] + 1000.0);
			}
			return box;
		}

		private static bool Box(double[] box, double x, double z)
		{
			return x >= box[0] && z >= box[1] && x <= box[2] && z <= box[3];
		}

		/// <summary>
		/// The cols: on a 200-unit grid in the mountains, the points whose ring of land 400 round rises above them twice and falls below
		/// them twice (by more than 40 each time, so a crease in a slope is not taken for one), deepest first (the least of the two rises
		/// and two falls), each 1500 or more from every deeper one; at most eight.
		/// </summary>
		private static List<(double x, double z, double h, double depth)> Cols()
		{
			List<Places.Place> places = Places.All;
			List<(double x, double z, double h, double depth)> found = new List<(double, double, double, double)>();
			const int ring = 16;
			double[] cos = new double[ring], sin = new double[ring];
			for (int k = 0; k < ring; k++)
			{
				cos[k] = DetMath.Cos(k * 2.0 * DetMath.Pi / ring);
				sin[k] = DetMath.Sin(k * 2.0 * DetMath.Pi / ring);
			}
			double[] d = new double[ring];
			for (double z = 0.0; z <= Limit - 1000.0; z += 200.0)
			{
				for (double x = -Limit + 1000.0; x <= Limit - 1000.0; x += 200.0)
				{
					if (WorldGen.MountainMask(x, z) < 0.6)
					{
						continue;
					}
					double h = WorldGen.Land(x, z);
					int start = -1;
					for (int k = 0; k < ring; k++)
					{
						d[k] = WorldGen.Land(x + cos[k] * 400.0, z + sin[k] * 400.0) - h;
						start = start < 0 && Math.Abs(d[k]) > 40.0 ? k : start;
					}
					if (start < 0)
					{
						continue;
					}
					// Round the ring from the first clear rise or fall: each lobe's extreme, and how many times it changes over.
					int sign = Math.Sign(d[start]), changes = 0;
					double extreme = 0.0, rise = double.MaxValue, fall = double.MaxValue;
					for (int m = 1; m <= ring; m++)
					{
						int k = (start + m) % ring;
						extreme = sign > 0 ? Math.Max(extreme, d[k]) : Math.Min(extreme, d[k]);
						if (Math.Abs(d[k]) > 40.0 && Math.Sign(d[k]) != sign)
						{
							rise = extreme > 0.0 ? Math.Min(rise, extreme) : rise;
							fall = extreme < 0.0 ? Math.Min(fall, -extreme) : fall;
							sign = -sign;
							extreme = d[k];
							changes++;
						}
					}
					if (changes < 4 || !Open(x, z, places))
					{
						continue;
					}
					found.Add((x, z, h, Math.Min(rise, fall)));
				}
			}
			found.Sort((p, q) => p.depth != q.depth ? q.depth.CompareTo(p.depth) : (p.x != q.x ? p.x.CompareTo(q.x) : p.z.CompareTo(q.z)));
			List<(double x, double z, double h, double depth)> kept = new List<(double, double, double, double)>();
			foreach (var c in found)
			{
				bool apart = true;
				foreach (var k in kept)
				{
					apart &= (k.x - c.x) * (k.x - c.x) + (k.z - c.z) * (k.z - c.z) >= 1500.0 * 1500.0;
				}
				if (apart && kept.Count < 8)
				{
					kept.Add(c);
				}
			}
			return kept;
		}

		/// <summary>A straight at one end of the road: from (X, Z) along (Dx, Dz), with room for its lot beside it; H is the land at its start.</summary>
		private sealed class Straight
		{
			internal double X;
			internal double Z;
			internal double Dx;
			internal double Dz;
			internal double H;
			internal double Length;
			internal double EndX => X + Dx * Length;
			internal double EndZ => Z + Dz * Length;
		}

		/// <summary>
		/// The valley floors below a col: on a 200-unit grid, between <see cref="NearBottom"/> and <see cref="FarBottom"/> off, at least
		/// <see cref="LeastDrop"/> below it, about flat (5% or less over 120), open, in the mountains, and with a start straight from them
		/// (<see cref="StartWay"/>); the three deepest, 1500 or more apart.
		/// </summary>
		private static List<Straight> Bottoms(double cx, double cz, double ch)
		{
			List<(double x, double z, double h)> found = new List<(double, double, double)>();
			for (double v = -FarBottom; v <= FarBottom; v += 200.0)
			{
				for (double u = -FarBottom; u <= FarBottom; u += 200.0)
				{
					double r2 = u * u + v * v;
					if (r2 < NearBottom * NearBottom || r2 > FarBottom * FarBottom)
					{
						continue;
					}
					double x = cx + u, z = cz + v;
					double h = WorldGen.Land(x, z);
					if (ch - h < LeastDrop)
					{
						continue;
					}
					double gx = (WorldGen.Land(x + 60.0, z) - WorldGen.Land(x - 60.0, z)) / 120.0, gz = (WorldGen.Land(x, z + 60.0) - WorldGen.Land(x, z - 60.0)) / 120.0;
					if (gx * gx + gz * gz > 0.05 * 0.05 || WorldGen.MountainMask(x, z) < BottomMask || !Open(x, z, Places.All))
					{
						continue;
					}
					found.Add((x, z, h));
				}
			}
			found.Sort((p, q) => p.h != q.h ? p.h.CompareTo(q.h) : (p.x != q.x ? p.x.CompareTo(q.x) : p.z.CompareTo(q.z)));
			List<Straight> kept = new List<Straight>();
			foreach (var b in found)
			{
				bool apart = true;
				foreach (Straight k in kept)
				{
					apart &= (k.X - b.x) * (k.X - b.x) + (k.Z - b.z) * (k.Z - b.z) >= 1500.0 * 1500.0;
				}
				Straight start = apart ? StartWay(b.x, b.z, b.h, cx, cz) : null;
				if (start != null)
				{
					kept.Add(start);
				}
				if (kept.Count == 3)
				{
					break;
				}
			}
			return kept;
		}

		/// <summary>
		/// The start straight from a valley floor at (x, z): of sixteen ways, nearest first to the way to the col (cx, cz), the first that
		/// is open all along (and crosses no river), no steeper than 7%, with room for the paddock beside it (which follows its grade).
		/// </summary>
		private static Straight StartWay(double x, double z, double h, double cx, double cz)
		{
			double wx = cx - x, wz = cz - z, wl = Math.Sqrt(wx * wx + wz * wz);
			for (int k = 0; k < 16; k++)
			{
				double turn = (k + 1) / 2 * (k % 2 == 1 ? 1.0 : -1.0) * DetMath.Pi / 8.0;
				double cs = DetMath.Cos(turn), sn = DetMath.Sin(turn);
				double dx = (wx * cs - wz * sn) / wl, dz = (wx * sn + wz * cs) / wl;
				if (Math.Abs(WorldGen.Land(x + dx * StartLength, z + dz * StartLength) - h) > 0.07 * StartLength || !Clear(x, z, dx, dz, 0.0, StartLength))
				{
					continue;
				}
				if (Room(x, z, dx, dz, StartLength, PaddockS, PaddockW, PaddockD))
				{
					return new Straight { X = x, Z = z, Dx = dx, Dz = dz, H = h, Length = StartLength };
				}
			}
			return null;
		}

		/// <summary>
		/// The straight at the top: of the lines through points within <see cref="TopReach"/> of the col and no more than
		/// <see cref="TopBelow"/> under it (on the 200 grid the col is rough, and right at it the ground rises either side, a saddle), in
		/// sixteen ways, those open all along with room for the turning area beside their end; the flattest (the land along it nearest
		/// the point's height, as a grade from it), if no steeper than 15%. Null if none.
		/// </summary>
		private static Straight Top(double cx, double cz, double ch)
		{
			Straight best = null;
			double flattest = 0.15;
			for (double qv = -TopReach; qv <= TopReach; qv += 100.0)
			{
				for (double qu = -TopReach; qu <= TopReach; qu += 100.0)
				{
					double qx = cx + qu, qz = cz + qv, qh = WorldGen.Land(qx, qz);
					if (qh < ch - TopBelow || qu * qu + qv * qv > TopReach * TopReach)
					{
						continue;
					}
					for (int k = 0; k < 16; k++)
					{
						double dx = DetMath.Cos(k * DetMath.Pi / 8.0), dz = DetMath.Sin(k * DetMath.Pi / 8.0);
						double steepest = 0.0;
						for (double s = -EndBefore; s <= EndLength - EndBefore && steepest < flattest; s += Cell)
						{
							steepest = Math.Max(steepest, Math.Abs(WorldGen.Land(qx + dx * s, qz + dz * s) - qh) / Math.Max(Cell, Math.Abs(s)));
						}
						// The turning area's stretch, the last TopLength, is held to a straight grade between the land at its ends (6% or
						// less), and the area follows it.
						double x0 = qx - dx * EndBefore, z0 = qz - dz * EndBefore;
						double hx = x0 + dx * (EndLength - TopLength), hz = z0 + dz * (EndLength - TopLength);
						bool gentle = Math.Abs(WorldGen.Land(x0 + dx * EndLength, z0 + dz * EndLength) - WorldGen.Land(hx, hz)) <= 0.06 * TopLength;
						if (steepest < flattest && gentle && Clear(x0, z0, dx, dz, 0.0, EndLength) && Room(hx, hz, dx, dz, TopLength, TopLength - TurnBack, TurnW, TurnD))
						{
							flattest = steepest;
							best = new Straight { X = x0, Z = z0, Dx = dx, Dz = dz, H = WorldGen.Land(x0, z0), Length = EndLength };
						}
					}
				}
			}
			return best;
		}

		/// <summary>Whether a straight from (x, z) along (dx, dz), from s0 to s1 along, is open every 25 and crosses no river.</summary>
		private static bool Clear(double x, double z, double dx, double dz, double s0, double s1)
		{
			for (double s = s0; s <= s1; s += Cell)
			{
				if (!Open(x + dx * s, z + dz * s, Places.All))
				{
					return false;
				}
			}
			Rivers.Crossings(x + dx * s0, z + dz * s0, x + dx * s1, z + dz * s1, crossings);
			return crossings.Count == 0;
		}

		private static readonly List<(double t, double w, double half, double sin)> crossings = new List<(double, double, double, double)>();

		// ---- routing ---------------------------------------------------------------------------------------------

		/// <summary>The eight headings, east first, anticlockwise.</summary>
		private static readonly int[] Di = { 1, 1, 0, -1, -1, -1, 0, 1 };
		private static readonly int[] Dj = { 0, 1, 1, 1, 0, -1, -1, -1 };

		/// <summary>A routing grid: the land and whether each cell is open, by cell (only those <see cref="Grid.Use"/> lets in, for the fine grid).</summary>
		private sealed class Grid
		{
			internal readonly int W;
			internal readonly int H;
			internal readonly double X0;
			internal readonly double Z0;
			internal readonly double Size;
			private readonly double[] land;
			private readonly bool[] closed;
			/// <summary>Per cell and heading, whether the step crosses a river's line (0 not yet known, 1 no, 2 yes): worked out as the routes need them.</summary>
			private readonly byte[] river;
			private readonly List<(double t, double w, double half, double sin)> found = new List<(double, double, double, double)>();

			/// <summary>A grid of <paramref name="size"/> cells over the box given; <paramref name="use"/>, if given, says which cells are worked out (the rest closed).</summary>
			internal Grid(double x0, double x1, double z0, double z1, double size, Func<double, double, bool> use = null)
			{
				Size = size;
				X0 = x0;
				Z0 = z0;
				W = (int)Math.Ceiling((x1 - x0) / size);
				H = (int)Math.Ceiling((z1 - z0) / size);
				land = new double[W * H];
				closed = new bool[W * H];
				river = new byte[W * H * 8];
				List<Places.Place> places = new List<Places.Place>();
				foreach (Places.Place p in Places.All)
				{
					double reach = p.Radius + PlaceClearance + size;
					if (p.X + reach > x0 && p.X - reach < x1 && p.Z + reach > z0 && p.Z - reach < z1)
					{
						places.Add(p);
					}
				}
				for (int c = 0; c < W * H; c++)
				{
					double x = X(c), z = Z(c);
					if (use != null && !use(x, z))
					{
						closed[c] = true;
						continue;
					}
					land[c] = WorldGen.Land(x, z);
					closed[c] = !Open(x, z, places);
				}
			}

			internal double X(int c)
			{
				return X0 + (c % W + 0.5) * Size;
			}

			internal double Z(int c)
			{
				return Z0 + (c / W + 0.5) * Size;
			}

			internal int Index(double x, double z)
			{
				int i = (int)Math.Floor((x - X0) / Size), j = (int)Math.Floor((z - Z0) / Size);
				return i < 0 || j < 0 || i >= W || j >= H ? -1 : j * W + i;
			}

			internal bool Closed(int c)
			{
				return closed[c];
			}

			/// <summary>
			/// A* from cell <paramref name="a"/> to <paramref name="b"/>; with <paramref name="turns"/>, each state a cell and a heading,
			/// starting on <paramref name="heading"/>, turning at most one heading (45 degrees) a step, and (with <paramref name="arrive"/>)
			/// arriving on that heading. The cells in order (null if there is no way), and the length. Ties broken by state number.
			/// </summary>
			internal List<int> Route(int a, int b, bool turns, int heading, out double length, int arrive = -1)
			{
				length = 0.0;
				if (a < 0 || b < 0)
				{
					return null;
				}
				int states = turns ? W * H * 8 : W * H;
				int hdOf(int state) => turns ? state % 8 : 0;
				double[] g = new double[states];
				int[] from = new int[states];
				for (int s = 0; s < states; s++)
				{
					g[s] = double.MaxValue;
					from[s] = -1;
				}
				Heap open = new Heap();
				int first = turns ? a * 8 + heading : a;
				g[first] = 0.0;
				open.Push(Guess(a, b), first);
				int goal = -1;
				while (open.Count > 0)
				{
					int cur = open.Pop(out double f);
					int cell = turns ? cur / 8 : cur;
					if (f > g[cur] + Guess(cell, b) + 1e-9)
					{
						continue;
					}
					if (cell == b && (arrive < 0 || hdOf(cur) == arrive))
					{
						goal = cur;
						break;
					}
					int ci = cell % W, cj = cell / W;
					int hd = hdOf(cur);
					for (int m = 0; m < (turns ? 3 : 8); m++)
					{
						int dh = turns ? m - 1 : 0;
						int nh = turns ? (hd + dh + 8) % 8 : m;
						int ni = ci + Di[nh], nj = cj + Dj[nh];
						if (ni < 0 || nj < 0 || ni >= W || nj >= H)
						{
							continue;
						}
						int next = nj * W + ni;
						if (closed[next] && next != b)
						{
							continue;
						}
						double len = nh % 2 == 1 ? Size * 1.4142135623730951 : Size;
						double grade = Math.Abs(land[next] - land[cell]) / len;
						if (grade > Cliff || Crosses(cell, next, nh))
						{
							continue;
						}
						double r = grade / Soft, over = Math.Max(0.0, grade - SteepestStep) / 0.05;
						double total = g[cur] + len * (1.0 + Steep * r * r + Over * over * over) + (dh != 0 ? TurnCost : 0.0);
						int ns = turns ? next * 8 + nh : next;
						if (total < g[ns])
						{
							g[ns] = total;
							from[ns] = cur;
							open.Push(total + Guess(next, b), ns);
						}
					}
				}
				if (goal < 0)
				{
					return null;
				}
				List<int> path = new List<int>();
				for (int s = goal; s >= 0; s = from[s])
				{
					path.Add(turns ? s / 8 : s);
				}
				path.Reverse();
				for (int k = 1; k < path.Count; k++)
				{
					double dx = X(path[k]) - X(path[k - 1]), dz = Z(path[k]) - Z(path[k - 1]);
					length += Math.Sqrt(dx * dx + dz * dz);
				}
				return path;
			}

			/// <summary>
			/// Whether the step from <paramref name="cell"/> to <paramref name="next"/> (heading <paramref name="h"/>) crosses a river: a narrow
			/// one can run between two cells' middles, both open, and the road has no bridges.
			/// </summary>
			private bool Crosses(int cell, int next, int h)
			{
				int k = cell * 8 + h;
				if (river[k] == 0)
				{
					Rivers.Crossings(X(cell), Z(cell), X(next), Z(next), found);
					river[k] = (byte)(found.Count > 0 ? 2 : 1);
				}
				return river[k] == 2;
			}

			private double Guess(int a, int b)
			{
				double dx = (a % W - b % W) * Size, dz = (a / W - b / W) * Size;
				return Math.Sqrt(dx * dx + dz * dz);
			}
		}

		/// <summary>A road drawn and checked, not yet kept.</summary>
		private sealed class Line
		{
			internal double[] X;
			internal double[] Z;
			internal double[] S;
			internal double Length;
			internal double Tightest;
			internal int Hairpins;
			internal double Climbed;
			internal double Score;
			internal double DirX;
			internal double DirZ;
			internal double EndDirX;
			internal double EndDirZ;
			internal double ColX;
			internal double ColZ;
		}

		/// <summary>
		/// The road from the start straight <paramref name="start"/> to the top straight <paramref name="top"/>, along the coarse route
		/// <paramref name="path"/> between them: the fine route from the end of the one to the start of the other, arriving in line with
		/// it; rounded and checked. Null if it does not pass.
		/// </summary>
		private static Line Try(Grid coarse, Straight start, Straight top, List<int> path)
		{
			// The fine grid: cells within the corridor of the coarse route.
			double minX = double.MaxValue, maxX = double.MinValue, minZ = double.MaxValue, maxZ = double.MinValue;
			List<(double x, double z)> spine = new List<(double x, double z)>();
			foreach (int c in path)
			{
				minX = Math.Min(minX, coarse.X(c));
				maxX = Math.Max(maxX, coarse.X(c));
				minZ = Math.Min(minZ, coarse.Z(c));
				maxZ = Math.Max(maxZ, coarse.Z(c));
				spine.Add((coarse.X(c), coarse.Z(c)));
			}
			Grid fine = new Grid(minX - Corridor - Cell, maxX + Corridor + Cell, minZ - Corridor - Cell, maxZ + Corridor + Cell, Cell, (x, z) => Near(spine, x, z, Corridor));
			double bx = start.X, bz = start.Z, ax = start.Dx, az = start.Dz, sx = start.EndX, sz = start.EndZ;
			double tx = top.X, tz = top.Z, ex = top.Dx, ez = top.Dz;
			int from = fine.Index(sx, sz), to = fine.Index(tx, tz);
			if (from < 0 || to < 0)
			{
				return null;
			}
			List<int> cells = fine.Route(from, to, true, Heading(ax, az), out double _, Heading(ex, ez));
			if (cells == null || cells.Count < 8)
			{
				return null;
			}
			// The line: the start straight's end, every other cell, the top straight's start; rounded, then the start straight in front
			// of it and the top straight after it, exactly straight.
			List<(double x, double z)> pts = new List<(double x, double z)> { (sx, sz) };
			for (int m = 2; m + 1 < cells.Count; m += 2)
			{
				pts.Add((fine.X(cells[m]), fine.Z(cells[m])));
			}
			pts.Add((tx, tz));
			for (int r = 0; r < 3; r++)
			{
				List<(double x, double z)> next = new List<(double x, double z)> { pts[0] };
				for (int i = 0; i + 1 < pts.Count; i++)
				{
					(double x0, double z0) = pts[i];
					(double x1, double z1) = pts[i + 1];
					next.Add((x0 * 0.75 + x1 * 0.25, z0 * 0.75 + z1 * 0.25));
					next.Add((x0 * 0.25 + x1 * 0.75, z0 * 0.25 + z1 * 0.75));
				}
				next.Add(pts[pts.Count - 1]);
				pts = next;
			}
			List<(double x, double z)> road = new List<(double x, double z)>();
			int straight = (int)Math.Round(StartLength / Step);
			for (int i = 0; i < straight; i++)
			{
				road.Add((bx + ax * StartLength * i / straight, bz + az * StartLength * i / straight));
			}
			List<(double x, double z)> rest = Lines.Resample(pts, Step);
			for (int i = 0; i + 1 < rest.Count; i++)
			{
				road.Add(rest[i]);
			}
			int end = (int)Math.Round(EndLength / Step);
			// The end straight may be eased (the route comes into it at up to 22 degrees) but for the turning area's stretch at its end.
			int fixedEnd = (int)Math.Ceiling((TurnBack + TurnW * 0.5 + Town.Pad + 3.0 * Step) / Step);
			for (int i = 0; i <= end; i++)
			{
				road.Add((tx + ex * EndLength * i / end, tz + ez * EndLength * i / end));
			}
			// The start straight may be eased past the paddock (the route leaves it at up to 22 degrees).
			Ease(road, (int)Math.Ceiling((PaddockS + PaddockW * 0.5 + Town.Pad + 2.0 * Step) / Step), fixedEnd);
			Line line = Check(road);
			if (line == null)
			{
				return null;
			}
			line.DirX = ax;
			line.DirZ = az;
			line.EndDirX = ex;
			line.EndDirZ = ez;
			line.ColX = tx + ex * EndBefore;
			line.ColZ = tz + ez * EndBefore;
			line.Score = line.Hairpins * 40.0 + line.Climbed - Math.Abs(line.Length - TargetLength) * 0.02;
			return line;
		}

		/// <summary>
		/// Whether there is room for a lot <paramref name="w"/> by <paramref name="d"/> (and its levelled pad) beside a straight from
		/// (x0, z0) along (dx, dz), <paramref name="length"/> long, its middle <paramref name="s"/> along: the land there, on one side or the
		/// other, within <see cref="RoomRough"/> of the straight's line between the land at its ends (the road is held to that line, and the lot follows it).
		/// </summary>
		private static bool Room(double x0, double z0, double dx, double dz, double length, double s, double w, double d)
		{
			return Unevenness(x0, z0, dx, dz, length, s, w, d) <= RoomRough;
		}

		/// <summary>How far the land under a lot beside a straight may stray from the straight's line (the lot's ground is levelled, banked back).</summary>
		private const double RoomRough = 7.0;

		/// <summary>How far the land beside a straight strays from the straight's line where a lot would go: the less of the two sides.</summary>
		private static double Unevenness(double x0, double z0, double dx, double dz, double length, double s, double w, double d)
		{
			double h0 = WorldGen.Land(x0, z0), h1 = WorldGen.Land(x0 + dx * length, z0 + dz * length);
			double best = double.MaxValue;
			for (int side = -1; side <= 1; side += 2)
			{
				double worst = 0.0;
				for (double v = 0.0; v <= d + Town.Pad && worst < best; v += 10.0)
				{
					for (double u = -w * 0.5 - Town.Pad; u <= w * 0.5 + Town.Pad && worst < best; u += 10.0)
					{
						double along = s + u, off = (Half + Network.Verge + v) * side;
						double line = h0 + (h1 - h0) * along / length;
						worst = Math.Max(worst, Math.Abs(WorldGen.Land(x0 + dx * along - dz * off, z0 + dz * along + dx * off) - line));
					}
				}
				best = Math.Min(best, worst);
			}
			return best;
		}

		/// <summary>The nearest of the eight headings to direction (dx, dz).</summary>
		private static int Heading(double dx, double dz)
		{
			int heading = 0;
			double most = double.MinValue;
			for (int h = 0; h < 8; h++)
			{
				double dot = (Di[h] * dx + Dj[h] * dz) / (h % 2 == 1 ? 1.4142135623730951 : 1.0);
				if (dot > most)
				{
					most = dot;
					heading = h;
				}
			}
			return heading;
		}

		/// <summary>Whether (x, z) is within <paramref name="reach"/> of the polyline.</summary>
		private static bool Near(List<(double x, double z)> line, double x, double z, double reach)
		{
			for (int i = 0; i < line.Count; i++)
			{
				double dx = line[i].x - x, dz = line[i].z - z;
				if (dx * dx + dz * dz <= reach * reach)
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>
		/// Eases the corners tighter than <see cref="MinRadius"/>: each such sample, and its neighbours, moved part way to the middle of
		/// the samples either side, a few times over, so a kink opens into a curve and the rest stays where it was routed. The first
		/// <paramref name="head"/> and last <paramref name="tail"/> samples (the straights) are left.
		/// </summary>
		private static void Ease(List<(double x, double z)> road, int head, int tail)
		{
			int n = road.Count;
			double[] x = new double[n], z = new double[n];
			for (int i = 0; i < n; i++)
			{
				x[i] = road[i].x;
				z[i] = road[i].z;
			}
			for (int pass = 0; pass < 150; pass++)
			{
				double[] curve = Curvature(x, z);
				bool any = false;
				for (int i = head; i < n - tail; i++)
				{
					if (Math.Abs(curve[i]) * MinRadius * 1.15 < 1.0)
					{
						continue;
					}
					any = true;
					for (int k = Math.Max(head, i - 2); k <= Math.Min(n - tail - 1, i + 2); k++)
					{
						x[k] = x[k] * 0.5 + (x[k - 1] + x[k + 1]) * 0.25;
						z[k] = z[k] * 0.5 + (z[k - 1] + z[k + 1]) * 0.25;
					}
				}
				if (!any)
				{
					break;
				}
			}
			for (int i = 0; i < n; i++)
			{
				road[i] = (x[i], z[i]);
			}
		}

		/// <summary>
		/// Checks a drawn road: its length, its tightest corner, how near it comes to itself, and how far it would be cut or built up once
		/// held to its grade. Null if it fails.
		/// </summary>
		private static Line Check(List<(double x, double z)> road)
		{
			int n = road.Count;
			double[] x = new double[n], z = new double[n], s = new double[n];
			for (int i = 0; i < n; i++)
			{
				x[i] = road[i].x;
				z[i] = road[i].z;
				if (i > 0)
				{
					s[i] = s[i - 1] + Math.Sqrt((x[i] - x[i - 1]) * (x[i] - x[i - 1]) + (z[i] - z[i - 1]) * (z[i] - z[i - 1]));
				}
			}
			double length = s[n - 1];
			if (length < MinLength || length > MaxLength)
			{
				return null;
			}
			double[] curve = Curvature(x, z);
			double tightest = double.MaxValue;
			for (int i = 3; i < n - 3; i++)
			{
				tightest = Math.Min(tightest, 1.0 / Math.Max(1e-9, Math.Abs(curve[i])));
			}
			if (tightest < MinRadius)
			{
				return null;
			}
			for (int i = 0; i < n; i += 2)
			{
				for (int j = i + 2; j < n; j += 2)
				{
					double along = s[j] - s[i];
					double need = along >= SeparationAlong ? Separation : along >= 120.0 ? HairpinApart : 0.0;
					double dx = x[j] - x[i], dz = z[j] - z[i];
					if (dx * dx + dz * dz < need * need)
					{
						return null;
					}
				}
			}
			// The land along it held to the grade both ways (a road's height, roughly): how far that is from the land.
			double[] y = new double[n];
			for (int i = 0; i < n; i++)
			{
				y[i] = WorldGen.Land(x[i], z[i]);
			}
			double[] land = (double[])y.Clone();
			for (int round = 0; round < 2; round++)
			{
				for (int i = 1; i < n; i++)
				{
					double rise = Grade * (s[i] - s[i - 1]);
					y[i] = Math.Max(y[i - 1] - rise, Math.Min(y[i - 1] + rise, y[i]));
				}
				for (int i = n - 2; i >= 0; i--)
				{
					double rise = Grade * (s[i + 1] - s[i]);
					y[i] = Math.Max(y[i + 1] - rise, Math.Min(y[i + 1] + rise, y[i]));
				}
			}
			double climbed = 0.0;
			for (int i = 0; i < n; i++)
			{
				if (Math.Abs(y[i] - land[i]) > MostEarthwork)
				{
					return null;
				}
				climbed += i > 0 ? Math.Max(0.0, y[i] - y[i - 1]) : 0.0;
			}
			return new Line { X = x, Z = z, S = s, Length = length, Tightest = tightest, Hairpins = Turns(x, z, s), Climbed = climbed };
		}

		/// <summary>Each sample's signed curvature (positive turning left), on the circle through the samples 3 either side, smoothed over 5.</summary>
		private static double[] Curvature(double[] x, double[] z)
		{
			int n = x.Length;
			double[] raw = new double[n];
			for (int i = 3; i < n - 3; i++)
			{
				int a = i - 3, b = i + 3;
				double ax = x[i] - x[a], az = z[i] - z[a], bx = x[b] - x[i], bz = z[b] - z[i], cx = x[b] - x[a], cz = z[b] - z[a];
				double cross = ax * bz - az * bx;
				double den = Math.Sqrt(ax * ax + az * az) * Math.Sqrt(bx * bx + bz * bz) * Math.Sqrt(cx * cx + cz * cz);
				raw[i] = den > 1e-9 ? 2.0 * cross / den : 0.0;
			}
			double[] curve = new double[n];
			for (int i = 0; i < n; i++)
			{
				double sum = 0.0;
				int count = 0;
				for (int k = Math.Max(0, i - 2); k <= Math.Min(n - 1, i + 2); k++)
				{
					sum += raw[k];
					count++;
				}
				curve[i] = sum / count;
			}
			return curve;
		}

		/// <summary>The hairpins: places where the road's heading turns through 150 degrees or more within 160 along it, 200 or more apart.</summary>
		private static int Turns(double[] x, double[] z, double[] s)
		{
			int n = x.Length, count = 0, j = 0;
			double last = double.MinValue;
			for (int i = 0; i + 1 < n; i++)
			{
				while (j + 1 < n - 1 && s[j] < s[i] + 160.0)
				{
					j++;
				}
				if (j + 1 >= n || s[i] - last < 200.0)
				{
					continue;
				}
				double ax = x[i + 1] - x[i], az = z[i + 1] - z[i], bx = x[j + 1] - x[j], bz = z[j + 1] - z[j];
				double dot = (ax * bx + az * bz) / Math.Max(1e-9, Math.Sqrt((ax * ax + az * az) * (bx * bx + bz * bz)));
				if (dot <= -0.866)
				{
					count++;
					last = s[i];
				}
			}
			return count;
		}

		/// <summary>Keeps the chosen road.</summary>
		private static void Keep(Line line)
		{
			X = line.X;
			Z = line.Z;
			S = line.S;
			Count = X.Length;
			Length = line.Length;
			DirX = line.DirX;
			DirZ = line.DirZ;
			EndDirX = line.EndDirX;
			EndDirZ = line.EndDirZ;
			ColX = line.ColX;
			ColZ = line.ColZ;
			TightestRadius = line.Tightest;
			Hairpins = line.Hairpins;
			Climbed = line.Climbed;
			AccessOutX = X[0] - DirX * AccessBack;
			AccessOutZ = Z[0] - DirZ * AccessBack;
		}

		/// <summary>The road's point at distance s along it (clamped to its ends), and its direction there.</summary>
		internal static void At(double s, out double x, out double z, out double dx, out double dz)
		{
			s = Math.Max(0.0, Math.Min(Length, s));
			int lo = 0, hi = Count - 1;
			while (hi - lo > 1)
			{
				int mid = (lo + hi) / 2;
				if (S[mid] <= s)
				{
					lo = mid;
				}
				else
				{
					hi = mid;
				}
			}
			double len = S[hi] - S[lo];
			double t = len > 0.0 ? (s - S[lo]) / len : 0.0;
			x = X[lo] + (X[hi] - X[lo]) * t;
			z = Z[lo] + (Z[hi] - Z[lo]) * t;
			dx = (X[hi] - X[lo]) / Math.Max(1e-9, len);
			dz = (Z[hi] - Z[lo]) / Math.Max(1e-9, len);
		}

		// ---- the index ---------------------------------------------------------------------------------------------

		/// <summary>How far round the road the router is kept off (the paddock and turning area are inside it).</summary>
		internal const double Fence = 120.0;

		/// <summary>How far round the road the index reaches.</summary>
		private const double Reach = Half + Fence + 40.0;

		private const double Bucket = 100.0;
		private static double bx0, bz0;
		private static int bw, bh;
		private static int[][] buckets;

		private static void Index()
		{
			double minX = double.MaxValue, maxX = double.MinValue, minZ = double.MaxValue, maxZ = double.MinValue;
			for (int i = 0; i < Count; i++)
			{
				minX = Math.Min(minX, X[i]);
				maxX = Math.Max(maxX, X[i]);
				minZ = Math.Min(minZ, Z[i]);
				maxZ = Math.Max(maxZ, Z[i]);
			}
			bx0 = minX - Reach - Bucket;
			bz0 = minZ - Reach - Bucket;
			bw = (int)Math.Ceiling((maxX - minX + 2.0 * (Reach + Bucket)) / Bucket);
			bh = (int)Math.Ceiling((maxZ - minZ + 2.0 * (Reach + Bucket)) / Bucket);
			List<int>[] lists = new List<int>[bw * bh];
			for (int i = 0; i + 1 < Count; i++)
			{
				int a0 = (int)Math.Floor((Math.Min(X[i], X[i + 1]) - Reach - bx0) / Bucket), a1 = (int)Math.Floor((Math.Max(X[i], X[i + 1]) + Reach - bx0) / Bucket);
				int c0 = (int)Math.Floor((Math.Min(Z[i], Z[i + 1]) - Reach - bz0) / Bucket), c1 = (int)Math.Floor((Math.Max(Z[i], Z[i + 1]) + Reach - bz0) / Bucket);
				for (int c = Math.Max(0, c0); c <= Math.Min(bh - 1, c1); c++)
				{
					for (int a = Math.Max(0, a0); a <= Math.Min(bw - 1, a1); a++)
					{
						(lists[c * bw + a] ??= new List<int>()).Add(i);
					}
				}
			}
			buckets = new int[bw * bh][];
			for (int k = 0; k < lists.Length; k++)
			{
				buckets[k] = lists[k]?.ToArray();
			}
		}

		/// <summary>Distance from (x, z) to the road's centre line, if within the index's reach; otherwise MaxValue.</summary>
		internal static double Distance(double x, double z)
		{
			if (!Exists)
			{
				return double.MaxValue;
			}
			int a = (int)Math.Floor((x - bx0) / Bucket), c = (int)Math.Floor((z - bz0) / Bucket);
			if (a < 0 || c < 0 || a >= bw || c >= bh || buckets[c * bw + a] == null)
			{
				return double.MaxValue;
			}
			double best = double.MaxValue;
			foreach (int i in buckets[c * bw + a])
			{
				double sx = X[i + 1] - X[i], sz = Z[i + 1] - Z[i];
				double len2 = sx * sx + sz * sz;
				double f = len2 > 0.0 ? ((x - X[i]) * sx + (z - Z[i]) * sz) / len2 : 0.0;
				f = f < 0.0 ? 0.0 : f > 1.0 ? 1.0 : f;
				double px = X[i] + sx * f - x, pz = Z[i] + sz * f - z;
				best = Math.Min(best, px * px + pz * pz);
			}
			return best < double.MaxValue ? Math.Sqrt(best) : best;
		}

		/// <summary>
		/// True within <paramref name="margin"/> of the road's edge (up to the index's reach): no road but its own lane comes in here, and no
		/// farm. Behind the road's foot (the way its lane leaves) it is open, so the lane can be routed from just behind it.
		/// </summary>
		internal static bool Fenced(double x, double z, double margin)
		{
			if (!Exists)
			{
				return false;
			}
			double dx = x - X[0], dz = z - Z[0];
			if (dx * DirX + dz * DirZ < -Behind && Math.Abs(-dx * DirZ + dz * DirX) < Fence)
			{
				return false;
			}
			return Distance(x, z) <= Half + Math.Min(margin, Reach - Half);
		}

		/// <summary>How far behind the road's foot the fence opens, and the lane's route starts.</summary>
		internal const double Behind = 40.0;
		internal const double AccessBack = 100.0;

		/// <summary>False within <paramref name="margin"/> of the road (as far out as need be): where farms may not be.</summary>
		internal static bool Clear(double x, double z, double margin)
		{
			if (!Exists)
			{
				return true;
			}
			double reach = Half + margin;
			for (int i = 0; i < Count; i += 2)
			{
				double dx = X[i] - x, dz = Z[i] - z;
				if (dx * dx + dz * dz < reach * reach)
				{
					return false;
				}
			}
			return true;
		}

		/// <summary>True on the start's or finish's arch posts (with <paramref name="margin"/>), where no plant grows.</summary>
		internal static bool Covered(double x, double z, double margin)
		{
			if (!Exists || Distance(x, z) > Half + 8.0 + margin)
			{
				return false;
			}
			foreach (double s in new[] { StartS, FinishS })
			{
				for (int side = -1; side <= 1; side += 2)
				{
					Post(s, side, out double px, out double pz);
					if ((x - px) * (x - px) + (z - pz) * (z - pz) <= (2.0 + margin) * (2.0 + margin))
					{
						return true;
					}
				}
			}
			return false;
		}

		/// <summary>An arch's post: at s along the road, on side <paramref name="side"/> (+1 left), just past the verge.</summary>
		internal static void Post(double s, int side, out double x, out double z)
		{
			At(s, out double px, out double pz, out double dx, out double dz);
			double off = (Half + Network.Verge + 2.0) * side;
			x = px - dz * off;
			z = pz + dx * off;
		}
	}
}
