using System;
using System.Collections.Generic;

namespace HalfGripOpenWorld
{
	/// <summary>
	/// Road classes, biggest first. A road may follow a road of its own class or bigger, never a smaller one. The courses' roads come
	/// last: no road follows them.
	/// </summary>
	internal enum RoadKind
	{
		Trunk = 0,
		Primary = 1,
		Secondary = 2,
		Street = 3,
		Lane = 4,
		Track = 5,
		/// <summary>A gravel footpath, out from the dead ends of streets into the country. Unpaved like a track, and narrower.</summary>
		Path = 6,
		/// <summary>
		/// The rally stage (<see cref="Rally"/>): a closed loop of gravel road through the upland woods, unpaved like a track and wider.
		/// Smallest of all, so no road follows it; none comes near it either (its ground is closed to the router).
		/// </summary>
		Rally = 7,
		/// <summary>
		/// The hill climb (<see cref="HillClimb"/>): a narrow tarmac road from a valley floor up to a col, paved like a lane. Not in the
		/// network: no road joins or crosses it (its ground is closed to the router) but the lane at its foot, which meets it end to end.
		/// </summary>
		Climb = 8,
	}

	/// <summary>
	/// The roads, laid out the way real road networks come about, in order of importance:
	///
	///   1. a dual-carriageway trunk road crossing the region from the west edge to the east, bypassing the towns, with each town
	///      joined to it;
	///   2. main roads between towns and villages: the Gabriel graph of the places (two places are neighbours if no other place is in
	///      the circle between them, which matches real road links far better than a tree), taken in order of "gravity" (population
	///      times population over distance squared), the strongest links as primary roads, the rest secondary, and a road over the
	///      mountains to the north edge;
	///   3. streets grown out from the roads in each town and village (<see cref="Grow"/>);
	///   4. a lane from every hamlet and farm to the nearest road, and some between neighbouring hamlets;
	///   5. farm tracks branching off the lanes and minor roads.
	///
	/// Every road is routed with A* over the land on a 100-unit grid (<see cref="Router"/>), costed by grade (each class has its own
	/// limit), with bridges dear and water closed. Following a road already built (of the same class or bigger) is cheap, so a new
	/// road shares the way with the network and only the new part is built: it leaves and joins the network at junctions. Then it is
	/// smoothed into curves, given a height profile held to its class's grade that meets the roads it joins and crosses at their height,
	/// and lifted onto bridges over rivers. The ground is then cut and filled to every road's bed (<see cref="Ground"/>).
	///
	/// Built once, with <see cref="Rng"/> and plain arithmetic only, so every player gets the same roads, and never changed afterwards,
	/// so the worker threads that build tiles can read it freely.
	/// </summary>
	internal static class Network
	{
		internal sealed class Road
		{
			public RoadKind Kind;
			public string Name;
			/// <summary>Half the paved width.</summary>
			public double Half;
			public double[] X;
			public double[] Z;
			/// <summary>Height of the road line, from sea level (add <see cref="Lift"/> for the scene).</summary>
			public double[] Y;
			/// <summary>Distance along the road at each sample, for the texture.</summary>
			public double[] S;
			/// <summary>True where the road is on a bridge: the ground under it is left as it is.</summary>
			public bool[] Bridge;
			/// <summary>
			/// True where the bridge's deck is over the water: held at its height, clear of the water. The rest of a deck (out over the
			/// banks) may ramp down at the road's grade, so a junction just past a bridge can be reached.
			/// </summary>
			public bool[] Over;
			/// <summary>The stretches not drawn because they lie on a road drawn over them (<see cref="Junctions"/>), in order.</summary>
			public List<Gap> Gaps = new List<Gap>();
			/// <summary>Per sample: the surface's cross-fall (rise per unit to the left), in a mouth, so it meets the other road flush.</summary>
			public double[] Tilt;
			/// <summary>Per piece (sample i to i + 1): the fractions of it drawn, from the first to the last (a piece a cut runs into is part drawn).</summary>
			public double[] From;
			public double[] To;
			/// <summary>Per sample: near a bridge, so the bank built for it never fills the river's channel.</summary>
			public bool[] Abut;
			/// <summary>A closed loop (the rally stage): its last sample is its first, and its sides are taken round the join.</summary>
			public bool Loop;
			public int Count => X.Length;
		}

		/// <summary>What each class is like: width, steepest grade, how much it smooths the land, how it curves.</summary>
		private struct Class
		{
			public double Half;
			public double Grade;
			public int Window;
			public int Keep;
			public int Rounds;
			public double Soft;
			public double Steep;
			public double Bank;
		}

		private static readonly Class[] Classes =
		{
			// Half widths: a lane is 11 units (a car is about 5.4 wide). Trunk: two carriageways of two lanes and a median.
			new Class { Half = 33.0, Grade = 0.05, Window = 40, Keep = 6, Rounds = 4, Soft = 0.035, Steep = 8.0, Bank = 160.0 },
			new Class { Half = 14.0, Grade = 0.07, Window = 22, Keep = 4, Rounds = 3, Soft = 0.05, Steep = 5.0, Bank = 120.0 },
			new Class { Half = 12.0, Grade = 0.09, Window = 14, Keep = 3, Rounds = 3, Soft = 0.07, Steep = 3.0, Bank = 90.0 },
			new Class { Half = 10.0, Grade = 0.11, Window = 3, Keep = 1, Rounds = 2, Soft = 0.09, Steep = 2.0, Bank = 30.0 },
			new Class { Half = 7.5, Grade = 0.11, Window = 6, Keep = 2, Rounds = 3, Soft = 0.09, Steep = 2.0, Bank = 40.0 },
			new Class { Half = 5.0, Grade = 0.14, Window = 3, Keep = 1, Rounds = 2, Soft = 0.12, Steep = 1.5, Bank = 25.0 },
			// A footpath 1.5 m wide.
			new Class { Half = 2.5, Grade = 0.18, Window = 3, Keep = 1, Rounds = 2, Soft = 0.15, Steep = 1.5, Bank = 15.0 },
			// The rally stage, 5 m wide: routed by Rally itself, so only its grade, window and bank are used.
			new Class { Half = Rally.Half, Grade = 0.12, Window = 4, Keep = 2, Rounds = 3, Soft = 0.10, Steep = 3.0, Bank = 30.0 },
			// The hill climb, a lane's width: routed by HillClimb itself, so only its grade, window and bank are used; its banks are
			// wide, as it is cut into the mountainside up to 10 m.
			new Class { Half = HillClimb.Half, Grade = HillClimb.Grade, Window = 3, Keep = 1, Rounds = 2, Soft = 0.10, Steep = 1.2, Bank = 60.0 },
		};

		internal static double HalfOf(RoadKind kind)
		{
			return Classes[(int)kind].Half;
		}

		/// <summary>
		/// Farm tracks, footpaths and the rally stage: no paving, verge, junction cuts or bridges; a ribbon on a shallow bed pressed into
		/// the ground. (The hill climb, after them, is paved.)
		/// </summary>
		internal static bool Unpaved(RoadKind kind)
		{
			return kind >= RoadKind.Track && kind <= RoadKind.Rally;
		}

		/// <summary>The soft edge each side of a track's or path's ribbon, from the ribbon down to the ground.</summary>
		internal static double EdgeOf(RoadKind kind)
		{
			return kind == RoadKind.Path ? 1.5 : kind == RoadKind.Rally ? 2.5 : 2.0;
		}

		/// <summary>
		/// How far past a track's or path's soft edges its bed stays flat: further than the ground's 5-unit triangles reach, so none of
		/// them rises into the ribbon from the bank beyond.
		/// </summary>
		internal const double UnpavedFlat = 3.0;

		/// <summary>A track's or path's ribbon lies this far over its bed (which is <see cref="TrackDepth"/> under its line).</summary>
		internal const double UnpavedLift = 0.15;

		/// <summary>Distance between a road's samples.</summary>
		internal const double Step = 8.0;

		/// <summary>Flat ground either side of the paving before the bank starts.</summary>
		internal const double Shoulder = 4.0;

		/// <summary>The road bed is this far under the road surface, so the ground never shows through the paving.</summary>
		internal const double BedDepth = 0.5;

		/// <summary>The ground is pressed this far into a shallow bed along a farm track or footpath, with soft edges; its ribbon lies on it.</summary>
		internal const double TrackDepth = 0.3;

		/// <summary>The gravel verge each side of a paved road, from its edge down to the ground, as wide as the flat shoulder it lies on.</summary>
		internal const double Verge = Shoulder;

		/// <summary>A bridge's deck runs this far past the water each side, so its ends stand on the banks.</summary>
		private const double Abutment = 6.0;

		/// <summary>Clearance of a bridge's deck over the water, before the allowance for a wide road.</summary>
		private const double DeckClear = 5.0;

		/// <summary>How far the mouth of a road that ends on another flares out (each side, at most) and over what length of road.</summary>
		private const double Flare = 4.0;
		private const double FlareLength = 14.0;

		/// <summary>Length of the apron round a cut, over which the road is brought to meet the other road's surface.</summary>
		private const double Apron = 16.0;

		/// <summary>Where a road meets another at a cut, its surface is this far under the other's: never through it, and no lip to feel.</summary>
		private const double MouthDrop = 0.05;

		/// <summary>
		/// How far each class's surface is drawn above its road line: bigger roads higher, so where two surfaces overlap the bigger road
		/// is cleanly on top (as its markings would be); small enough to drive over. A track's or path's ribbon is under its line, on its
		/// pressed bed, so where it meets a road it is under that road's surface and needs no cut; a path's a little under a track's, so
		/// where one ends on the other they do not flicker.
		/// </summary>
		private static readonly double[] Tops = { 0.20, 0.16, 0.12, 0.08, 0.05, UnpavedLift - TrackDepth, UnpavedLift - TrackDepth - 0.02, UnpavedLift - TrackDepth, 0.05 };

		/// <summary>How far a road of this class is drawn above its road line (see <see cref="Tops"/>).</summary>
		internal static double Top(RoadKind kind)
		{
			return Tops[(int)kind];
		}

		internal static readonly List<Road> Roads = new List<Road>();

		/// <summary>The circuit's road, from its paddock gate to the network (null if there is no circuit).</summary>
		internal static Road CircuitRoad;

		/// <summary>The rally stage's track, from the back of its service area to the network (null if there is no stage).</summary>
		internal static Road RallyAccess;

		/// <summary>The hill climb's lane, from its foot to the network (null if there is no hill climb).</summary>
		internal static Road ClimbAccess;

		/// <summary>Where roads leave the world: the mouth of a tunnel into the edge wall, facing out along (DirX, DirZ).</summary>
		internal struct Portal
		{
			public double X;
			public double Z;
			public double Y;
			public double DirX;
			public double DirZ;
			public double Half;
		}

		internal static readonly List<Portal> Portals = new List<Portal>();

		/// <summary>
		/// Where the player starts: on the main town's road in, just outside the town, facing its middle (or, if the town has no such
		/// road, on the trunk road near it).
		/// </summary>
		internal static double SpawnX;
		internal static double SpawnZ;
		internal static double SpawnY;
		internal static double SpawnDirX = 1.0;
		internal static double SpawnDirZ;

		/// <summary>Sideways offset (to the right of the way the grid faces) of the second car of each pair on the grid: the other lane.</summary>
		internal static double SpawnPair = 11.0;

		/// <summary>
		/// Added to every height for the scene, so the road at the spawn is at 0.5: the Multiplayer mod puts every car at height 1 on
		/// the grid (its own grid rule), which is then just resting on the road.
		/// </summary>
		internal static double Lift;

		/// <summary>Milliseconds the layout took, for the log.</summary>
		internal static double BuildMs;

		private static readonly object gate = new object();
		private static volatile bool built;

		/// <summary>Builds the world's layout if it has not been built yet. Safe from any thread; the first call does the work.</summary>
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
					System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
					Build();
					BuildMs = watch.Elapsed.TotalMilliseconds;
					built = true;
				}
			}
		}

		private static Router router;
		private static Segments segments;

		private static void Build()
		{
			Rivers.Build();
			Rng rng = new Rng(Noise.Mix(WorldGen.Seed ^ 0x5157_E5UL));
			Places.Build(rng);
			segments = new Segments();
			router = new Router();
			RallyAccess = null;
			ClimbAccess = null;

			// 0. The rally stage and the hill climb, before any other road: they all go round them.
			if (Rally.Exists)
			{
				AddRally();
			}
			if (HillClimb.Exists)
			{
				AddClimb();
			}

			List<Places.Place> towns = Places.All.FindAll(p => p.Kind == PlaceKind.Town);
			List<Places.Place> villages = Places.All.FindAll(p => p.Kind == PlaceKind.Village);
			List<Places.Place> hamlets = Places.All.FindAll(p => p.Kind == PlaceKind.Hamlet);

			// 1. The trunk road, west edge to east edge, then each town joined to it.
			Gate(true, out double wx, out double wz);
			Gate(false, out double ex, out double ez);
			Road trunk = null;
			foreach (Road r in Route(RoadKind.Trunk, "Trunk road", wx, wz, ex, ez, false))
			{
				trunk = trunk == null || r.Count > trunk.Count ? r : trunk;
			}
			if (trunk != null)
			{
				trunk = Extend(trunk, true);
				trunk = Extend(trunk, false);
				Spawn(trunk, towns.Count > 0 ? towns[0] : null);
			}
			for (int t = 0; t < towns.Count; t++)
			{
				List<Road> link = RouteToNetwork(RoadKind.Primary, towns[t].Name + " link", towns[t].X, towns[t].Z, RoadKind.Trunk);
				// The spawn: on the main town's road in, just outside the town, facing its middle (levelled now, before anything joins it).
				if (t == 0 && link.Count > 0)
				{
					SpawnOnLink(link[0], towns[0]);
				}
			}

			// 2. Main roads: the Gabriel graph of towns and villages (and the north gate), strongest links first.
			List<(double x, double z, double pop, string name)> nodes = new List<(double, double, double, string)>();
			foreach (Places.Place p in towns)
			{
				nodes.Add((p.X, p.Z, p.Population, p.Name));
			}
			foreach (Places.Place p in villages)
			{
				nodes.Add((p.X, p.Z, p.Population, p.Name));
			}
			NorthGate(out double nx, out double nz);
			// Told apart by its index, not its name, so a village may be given any name (see Names).
			int north = nodes.Count;
			nodes.Add((nx, nz, 3000.0, "north"));
			List<(int a, int b, double g)> links = Gabriel(nodes);
			links.Sort((p, q) => q.g != p.g ? q.g.CompareTo(p.g) : (p.a * 1000 + p.b).CompareTo(q.a * 1000 + q.b));
			// Main roads first, then minor ones: a smaller road is always built after the bigger roads it meets, so it is the one that
			// takes the other's height where they overlap.
			for (int pass = 0; pass < 2; pass++)
			{
				for (int k = 0; k < links.Count; k++)
				{
					(int a, int b, double _) = links[k];
					bool main = k < links.Count / 3 || a == north || b == north;
					if (main != (pass == 0))
					{
						continue;
					}
					foreach (Road r in Route(main ? RoadKind.Primary : RoadKind.Secondary, nodes[a].name + " - " + nodes[b].name, nodes[a].x, nodes[a].z, nodes[b].x, nodes[b].z, false))
					{
						if (b == north && r.X[r.Count - 1] == nodes[b].x && r.Z[r.Count - 1] == nodes[b].z)
						{
							Extend(r, false);
						}
					}
				}
			}

			// The circuit's road, from its paddock gate to the nearest main or minor road: routed from a point out beyond the gate (the
			// cells round the circuit are closed), drawn from the gate itself.
			if (Circuit.Exists)
			{
				List<int> path = router.Find(RoadKind.Secondary, Circuit.GateOutX, Circuit.GateOutZ, 0.0, 0.0, (int)RoadKind.Secondary);
				if (path != null)
				{
					CircuitRoad = Build(RoadKind.Secondary, "Circuit road", path, Circuit.GateX, Circuit.GateZ, double.NaN, 0.0, 60.0).Find(r => r.X[0] == Circuit.GateX && r.Z[0] == Circuit.GateZ);
				}
			}

			// 3. Streets, grown out from the roads in each town and village.
			for (int i = 0; i < Places.All.Count; i++)
			{
				Places.Place p = Places.All[i];
				if (p.Kind != PlaceKind.Hamlet)
				{
					Grow(new Rng(Noise.Mix(WorldGen.Seed + 7000UL + (ulong)i)), p);
				}
			}

			// 4. A lane from each hamlet to the nearest road, and to some neighbouring hamlets.
			foreach (Places.Place h in hamlets)
			{
				RouteToNetwork(RoadKind.Lane, h.Name + " lane", h.X, h.Z, RoadKind.Lane);
			}
			for (int i = 0; i < hamlets.Count; i++)
			{
				int nearest = -1;
				double best = 3200.0 * 3200.0;
				for (int j = 0; j < hamlets.Count; j++)
				{
					double dx = hamlets[i].X - hamlets[j].X, dz = hamlets[i].Z - hamlets[j].Z;
					if (j != i && dx * dx + dz * dz < best)
					{
						best = dx * dx + dz * dz;
						nearest = j;
					}
				}
				if (nearest > i && rng.Next() < 0.45)
				{
					Route(RoadKind.Lane, hamlets[i].Name + " - " + hamlets[nearest].Name, hamlets[i].X, hamlets[i].Z, hamlets[nearest].X, hamlets[nearest].Z, false);
				}
			}

			// The rally stage's track, from the back of its service area to the nearest lane or bigger road; the hill climb's lane, from
			// its foot.
			if (Rally.Exists)
			{
				RallyAccess = RallyTrack();
			}
			if (HillClimb.Exists)
			{
				ClimbAccess = ClimbLane();
			}

			// 5. Farm tracks off the lanes and minor roads, and footpaths on from the dead ends of streets; then many of the country's
			// dead ends carried on to another road.
			Tracks(new Rng(Noise.Mix(WorldGen.Seed + 9000UL)));
			Footpaths(new Rng(Noise.Mix(WorldGen.Seed + 9500UL)));
			Throughs(new Rng(Noise.Mix(WorldGen.Seed + 9700UL)));

			// The junctions, now every road is in, then the samples near bridges.
			Junctions();
			Abutments();
			Index();

			// 6. Parks in the towns' blocks and by their rivers, with paths through them, and trails round the lakes: the blocks are
			// found between the roads, so after the index; the paths meet roads under their surface and need no junctions.
			Parks.Build();
			foreach (Road road in Roads)
			{
				road.Tilt ??= new double[road.Count];
			}
			Abutments();
			Index();
			Lift = 0.5 - SpawnY;

			// 7. The retail park and businesses on the main roads, now the roads are fixed; then the benches, clear of them.
			Town.Build();
			Parks.Furnish();

			// 8. The road signs, last: they keep off the roads, lots and parks.
			Signs.Build();
		}

		/// <summary>
		/// The rally stage as a road: its loop closed (the last sample on the first), its straight held to a straight grade between the
		/// land at its ends (the service area beside it is level across and follows that grade), the rest given its height as any road's
		/// is. It is not put in the segments or marked in the router: no road joins or follows it, and its ground is closed to them.
		/// </summary>
		private static void AddRally()
		{
			List<(double x, double z)> points = new List<(double x, double z)>(Rally.Count + 1);
			for (int i = 0; i < Rally.Count; i++)
			{
				points.Add((Rally.X[i], Rally.Z[i]));
			}
			points.Add((Rally.X[0], Rally.Z[0]));
			Road road = Make(RoadKind.Rally, "Rally stage", points);
			road.Loop = true;
			int n = road.Count;
			int straight = 0;
			while (straight + 1 < n && road.S[straight + 1] <= Rally.StraightLength)
			{
				straight++;
			}
			double y0 = WorldGen.Base(road.X[0], road.Z[0]), y1 = WorldGen.Base(road.X[straight], road.Z[straight]);
			List<(int idx, double y)> pins = new List<(int, double)>();
			for (int i = 0; i <= straight; i++)
			{
				pins.Add((i, y0 + (y1 - y0) * road.S[i] / road.S[straight]));
			}
			pins.Add((n - 1, y0));
			Class cl = Classes[(int)RoadKind.Rally];
			Profile(road, cl.Grade, cl.Window, pins);
			Roads.Add(road);
			Rally.Road = road;
		}

		/// <summary>
		/// The hill climb as a road: its start straight held to a straight grade between the land at its ends (the paddock beside it is
		/// level across and follows that grade), and the turning area's stretch at the top likewise, no steeper than 6%; the rest given
		/// its height as any road's is, held to 13%. It is not put in the segments or marked in the router: no road joins or follows it,
		/// and its ground is closed to them; its lane meets its foot end to end.
		/// </summary>
		private static void AddClimb()
		{
			List<(double x, double z)> points = new List<(double x, double z)>(HillClimb.Count);
			for (int i = 0; i < HillClimb.Count; i++)
			{
				points.Add((HillClimb.X[i], HillClimb.Z[i]));
			}
			Road road = Make(RoadKind.Climb, "Hill climb", points);
			int n = road.Count;
			int straight = 0;
			while (straight + 1 < n && road.S[straight + 1] <= HillClimb.StartLength)
			{
				straight++;
			}
			int top = n - 1;
			while (top > 0 && road.S[n - 1] - road.S[top - 1] <= HillClimb.TopLength)
			{
				top--;
			}
			List<(int idx, double y)> pins = new List<(int, double)>();
			double y0 = WorldGen.Base(road.X[0], road.Z[0]), y1 = WorldGen.Base(road.X[straight], road.Z[straight]);
			for (int i = 0; i <= straight; i++)
			{
				pins.Add((i, y0 + (y1 - y0) * road.S[i] / road.S[straight]));
			}
			double t0 = WorldGen.Base(road.X[top], road.Z[top]), run = road.S[n - 1] - road.S[top];
			double t1 = WorldGen.Base(road.X[n - 1], road.Z[n - 1]);
			t1 = t0 + Math.Max(-0.06 * run, Math.Min(0.06 * run, t1 - t0));
			for (int i = top; i < n; i++)
			{
				pins.Add((i, t0 + (t1 - t0) * (road.S[i] - road.S[top]) / run));
			}
			Class cl = Classes[(int)RoadKind.Climb];
			Profile(road, cl.Grade, cl.Window, pins);
			Roads.Add(road);
			HillClimb.Road = road;
		}

		/// <summary>
		/// The hill climb's lane to the nearest lane or bigger road: routed from a point just behind its foot (where its fence opens),
		/// drawn from the foot itself straight back in line with the start straight, so the two roads meet end to end at one height, then
		/// rounded into the route; ended where its line first meets a road (as the rally's track is).
		/// </summary>
		private static Road ClimbLane()
		{
			Road climb = HillClimb.Road;
			List<int> path = router.Find(RoadKind.Lane, HillClimb.AccessOutX, HillClimb.AccessOutZ, 0.0, 0.0, (int)RoadKind.Lane);
			if (path == null || climb == null)
			{
				return null;
			}
			double fx = climb.X[0], fz = climb.Z[0];
			double bx = (HillClimb.AccessOutX - fx) / HillClimb.AccessBack, bz = (HillClimb.AccessOutZ - fz) / HillClimb.AccessBack;
			// The straight back (three points, so the rounding leaves its first stretch in line), then every other routed cell from the
			// first clear of the foot (the first cells, round the point it was routed from, would only add a kink).
			List<(double x, double z)> pts = new List<(double x, double z)> { (fx, fz), (fx + bx * 40.0, fz + bz * 40.0), (HillClimb.AccessOutX, HillClimb.AccessOutZ) };
			double endY = double.NaN, lx = HillClimb.AccessOutX, lz = HillClimb.AccessOutZ;
			int kept = 0;
			foreach (int c in path)
			{
				double cx = router.X(c), cz = router.Z(c);
				if (kept == 0 && (cx - fx) * (cx - fx) + (cz - fz) * (cz - fz) < 180.0 * 180.0)
				{
					continue;
				}
				if (segments.Intersect(lx, lz, cx, cz, -1, out double ix, out double iz, out double iy))
				{
					pts.Add((ix, iz));
					endY = iy;
					break;
				}
				if (kept++ % 2 == 0)
				{
					pts.Add((cx, cz));
				}
				lx = cx;
				lz = cz;
			}
			if (double.IsNaN(endY))
			{
				if (!segments.Snap(lx, lz, 160.0, -1, out double sx, out double sz, out double sy))
				{
					return null;
				}
				pts.Add((sx, sz));
				endY = sy;
			}
			Road road = Make(RoadKind.Lane, "Hill climb lane", Smooth(pts, 1, Classes[(int)RoadKind.Lane].Rounds));
			if (road.Count < 3)
			{
				return null;
			}
			// Its first stretch carries on the start straight's grade, so the two surfaces meet in line.
			double grade = (climb.Y[1] - climb.Y[0]) / Math.Max(1e-6, climb.S[1]);
			List<(int idx, double y)> pins = new List<(int, double)> { (0, climb.Y[0]) };
			for (int i = 1; i < road.Count - 1 && road.S[i] <= 30.0; i++)
			{
				pins.Add((i, climb.Y[0] - grade * road.S[i]));
			}
			pins.Add((road.Count - 1, endY));
			Add(road, pins);
			foreach (int c in path)
			{
				router.Mark(c, RoadKind.Lane);
			}
			return road;
		}

		/// <summary>
		/// The rally service area's track to the nearest lane or bigger road: routed from a point out beyond the service area's back
		/// (the cells round the stage are closed), drawn from the back itself, and ended where its line first meets a road. (A route
		/// reaches a road at the first cell the road's samples are in; a road can cross a cell's corner with none in it, and a track
		/// built to the cell's road ran across the road and hooked back to it.) Its first 30 units are held level with the yard.
		/// </summary>
		private static Road RallyTrack()
		{
			List<int> path = router.Find(RoadKind.Track, Rally.AccessOutX, Rally.AccessOutZ, 0.0, 0.0, (int)RoadKind.Lane);
			if (path == null)
			{
				return null;
			}
			List<(double x, double z)> pts = new List<(double x, double z)> { (Rally.AccessX, Rally.AccessZ) };
			double endY = double.NaN, lx = Rally.AccessX, lz = Rally.AccessZ;
			foreach (int c in path)
			{
				double cx = router.X(c), cz = router.Z(c);
				if (segments.Intersect(lx, lz, cx, cz, -1, out double ix, out double iz, out double iy))
				{
					pts.Add((ix, iz));
					endY = iy;
					break;
				}
				pts.Add((cx, cz));
				lx = cx;
				lz = cz;
			}
			if (double.IsNaN(endY))
			{
				if (!segments.Snap(lx, lz, 160.0, -1, out double sx, out double sz, out double sy))
				{
					return null;
				}
				pts.Add((sx, sz));
				endY = sy;
			}
			Class cl = Classes[(int)RoadKind.Track];
			Road road = Make(RoadKind.Track, "Rally service road", Smooth(pts, cl.Keep, cl.Rounds));
			if (road.Count < 3)
			{
				return null;
			}
			// Its bed level with the yard's levelled ground (the stage's height beside it, less the yard's own drop and bed: see Town.Fit
			// and Town.Ground), so the bank round the yard's back does not rise through its ribbon.
			Road stage = Rally.Road;
			Locate(stage, Rally.ServiceS, out int si, out double st);
			double yard = stage.Y[si] + (stage.Y[si + 1] - stage.Y[si]) * st - TrackDepth + 0.1 - Town.Bed + TrackDepth;
			List<(int idx, double y)> pins = new List<(int, double)> { (0, yard) };
			for (int i = 1; i < road.Count - 1 && road.S[i] <= 30.0; i++)
			{
				pins.Add((i, pins[0].y));
			}
			pins.Add((road.Count - 1, endY));
			Add(road, pins);
			foreach (int c in path)
			{
				router.Mark(c, RoadKind.Track);
			}
			return road;
		}

		// ---- paths added from outside (parks and trails) ---------------------------------------------------------

		/// <summary>
		/// A footpath along <paramref name="points"/> (corners rounded, resampled), its ends at the heights given (NaN: the land's), if it
		/// is at least <paramref name="least"/> long. For <see cref="Parks"/>, while the layout is built.
		/// </summary>
		internal static Road AddPath(List<(double x, double z)> points, string name, double startY, double endY, double least)
		{
			if (points.Count < 2)
			{
				return null;
			}
			Road road = Make(RoadKind.Path, name, Lines.Resample(Rounded(points), Step));
			if (road.Count < 3 || road.S[road.Count - 1] < least)
			{
				return null;
			}
			List<(int idx, double y)> pins = new List<(int, double)>
			{
				(0, double.IsNaN(startY) ? WorldGen.Base(road.X[0], road.Z[0]) : startY),
				(road.Count - 1, double.IsNaN(endY) ? WorldGen.Base(road.X[road.Count - 1], road.Z[road.Count - 1]) : endY),
			};
			Add(road, pins);
			return road;
		}

		/// <summary>The nearest point within <paramref name="radius"/> on any road, and the road's height there (while the layout is built).</summary>
		internal static bool SnapRoad(double x, double z, double radius, out double px, out double pz, out double py)
		{
			return segments.Snap(x, z, radius, -1, out px, out pz, out py);
		}

		/// <summary>Where segment (ax, az)-(bx, bz) first crosses a road, and the road's height there (while the layout is built).</summary>
		internal static bool CrossRoad(double ax, double az, double bx, double bz, out double ix, out double iz, out double iy)
		{
			return segments.Intersect(ax, az, bx, bz, -1, out ix, out iz, out iy);
		}

		/// <summary>A footpath to the nearest lane or bigger road from (x, z), along the router's grid (while the layout is built).</summary>
		internal static List<Road> PathToNetwork(string name, double x, double z)
		{
			return RouteToNetwork(RoadKind.Path, name, x, z, RoadKind.Lane);
		}

		// ---- the edges ---------------------------------------------------------------------------------------------

		/// <summary>Where the trunk road meets the west (or east) edge: the lowest open ground on the edge line, between 1 and 8 km from the coast.</summary>
		private static void Gate(bool west, out double x, out double z)
		{
			x = (west ? -1.0 : 1.0) * (WorldGen.Half - WorldGen.WallDepth - 400.0);
			z = 0.0;
			double lowest = double.MaxValue;
			for (double t = -WorldGen.Half + 3000.0; t < 8000.0; t += 100.0)
			{
				double inland = WorldGen.Inland(x, t);
				if (inland < 1000.0 || inland > 8000.0 || !double.IsNaN(Rivers.SurfaceAt(x, t)))
				{
					continue;
				}
				double h = WorldGen.Base(x, t) + (Rivers.Distance(x, t, out double plain) < plain + 100.0 ? 1000.0 : 0.0);
				if (h < lowest)
				{
					lowest = h;
					z = t;
				}
			}
		}

		/// <summary>Where the road over the mountains meets the north edge: the lowest point on the edge line (a pass).</summary>
		private static void NorthGate(out double x, out double z)
		{
			z = WorldGen.Half - WorldGen.WallDepth - 400.0;
			x = 0.0;
			double lowest = double.MaxValue;
			for (double t = -12000.0; t <= 12000.0; t += 100.0)
			{
				if (!double.IsNaN(Rivers.SurfaceAt(t, z)))
				{
					continue;
				}
				double h = WorldGen.Base(t, z);
				if (h < lowest)
				{
					lowest = h;
					x = t;
				}
			}
		}

		/// <summary>
		/// Carries a road that ends at a gate on, level, into the edge wall, ending at a tunnel mouth: the road goes on to somewhere
		/// outside the world. Returns the longer road, which takes the old one's place in the network.
		/// </summary>
		private static Road Extend(Road road, bool atStart)
		{
			Road original = road;
			int end = atStart ? 0 : road.Count - 1;
			double dx, dz;
			// Straight out through the edge: the nearest edge's outward direction.
			if (Math.Abs(road.X[end]) > Math.Abs(road.Z[end]))
			{
				dx = Math.Sign(road.X[end]);
				dz = 0.0;
			}
			else
			{
				dx = 0.0;
				dz = 1.0;
			}
			// The route can reach the gate from a little beyond it and turn back; the road is cut at its outermost point near the end,
			// so the way into the wall carries straight on instead of doubling back over the road.
			int outer = end;
			for (int k = 0; k < Math.Min(60, road.Count - 2); k++)
			{
				int i = atStart ? k : road.Count - 1 - k;
				if (road.X[i] * dx + road.Z[i] * dz > road.X[outer] * dx + road.Z[outer] * dz)
				{
					outer = i;
				}
			}
			if (outer != end)
			{
				road = Trim(road, atStart ? outer : 0, atStart ? road.Count - 1 : outer);
				end = atStart ? 0 : road.Count - 1;
			}
			int add = (int)(1100.0 / Step);
			int n = road.Count + add;
			Road longer = new Road { Kind = road.Kind, Name = road.Name, Half = road.Half, X = new double[n], Z = new double[n], Y = new double[n], S = new double[n], Bridge = new bool[n], Over = new bool[n] };
			int offset = atStart ? add : 0;
			Array.Copy(road.X, 0, longer.X, offset, road.Count);
			Array.Copy(road.Z, 0, longer.Z, offset, road.Count);
			Array.Copy(road.Y, 0, longer.Y, offset, road.Count);
			Array.Copy(road.Bridge, 0, longer.Bridge, offset, road.Count);
			Array.Copy(road.Over, 0, longer.Over, offset, road.Count);
			for (int k = 1; k <= add; k++)
			{
				int i = atStart ? add - k : road.Count - 1 + k;
				longer.X[i] = road.X[end] + dx * Step * k;
				longer.Z[i] = road.Z[end] + dz * Step * k;
				longer.Y[i] = road.Y[end];
			}
			Lengths(longer);
			int at = Roads.IndexOf(original);
			Roads[at] = longer;
			segments.Replace(at, original, longer);
			int tip = atStart ? 0 : n - 1;
			Portals.Add(new Portal { X = longer.X[tip], Z = longer.Z[tip], Y = longer.Y[tip], DirX = dx, DirZ = dz, Half = longer.Half });
			return longer;
		}

		/// <summary>A copy of samples <paramref name="a"/> to <paramref name="b"/> of a road (lengths counted afresh).</summary>
		private static Road Trim(Road road, int a, int b)
		{
			int n = b - a + 1;
			Road cut = new Road { Kind = road.Kind, Name = road.Name, Half = road.Half, X = new double[n], Z = new double[n], Y = new double[n], S = new double[n], Bridge = new bool[n], Over = new bool[n] };
			Array.Copy(road.X, a, cut.X, 0, n);
			Array.Copy(road.Z, a, cut.Z, 0, n);
			Array.Copy(road.Y, a, cut.Y, 0, n);
			Array.Copy(road.Bridge, a, cut.Bridge, 0, n);
			Array.Copy(road.Over, a, cut.Over, 0, n);
			Lengths(cut);
			return cut;
		}

		// ---- links -------------------------------------------------------------------------------------------------

		/// <summary>The Gabriel graph of the nodes, each edge with its gravity (population times population over distance squared).</summary>
		private static List<(int a, int b, double g)> Gabriel(List<(double x, double z, double pop, string name)> nodes)
		{
			List<(int, int, double)> edges = new List<(int, int, double)>();
			for (int a = 0; a < nodes.Count; a++)
			{
				for (int b = a + 1; b < nodes.Count; b++)
				{
					double mx = (nodes[a].x + nodes[b].x) * 0.5, mz = (nodes[a].z + nodes[b].z) * 0.5;
					double dx = nodes[a].x - nodes[b].x, dz = nodes[a].z - nodes[b].z;
					double r2 = (dx * dx + dz * dz) * 0.25;
					bool empty = true;
					for (int k = 0; k < nodes.Count && empty; k++)
					{
						if (k != a && k != b)
						{
							double kx = nodes[k].x - mx, kz = nodes[k].z - mz;
							empty = kx * kx + kz * kz >= r2;
						}
					}
					if (empty)
					{
						edges.Add((a, b, nodes[a].pop * nodes[b].pop / (dx * dx + dz * dz)));
					}
				}
			}
			return edges;
		}

		// ---- routing and building ----------------------------------------------------------------------------------

		/// <summary>A road from (ax, az) to (bx, bz): routed, and its new parts (where it does not follow a road already built) built.</summary>
		private static List<Road> Route(RoadKind kind, string name, double ax, double az, double bx, double bz, bool toNetwork)
		{
			List<int> path = router.Find(kind, ax, az, bx, bz, toNetwork ? (int)kind : -1);
			return path == null ? new List<Road>() : Build(kind, name, path, ax, az, toNetwork ? double.NaN : bx, bz);
		}

		/// <summary>A road from (x, z) to the nearest road of class <paramref name="joins"/> or bigger.</summary>
		private static List<Road> RouteToNetwork(RoadKind kind, string name, double x, double z, RoadKind joins)
		{
			List<int> path = router.Find(kind, x, z, 0.0, 0.0, (int)joins);
			return path == null ? new List<Road>() : Build(kind, name, path, x, z, double.NaN, 0.0);
		}

		/// <summary>
		/// Turns a routed path into roads: every stretch of the path not on a road already built becomes a road, from where it leaves the
		/// network (or the start) to where it joins it again (or the end). Ends that join the network are moved onto the nearest point of
		/// the road there, and pinned to its height. With <paramref name="level"/>, the start's height is held that far along (the circuit's
		/// road leaves its paddock level, or its bed and banks would rise through the paddock's edge).
		/// </summary>
		private static List<Road> Build(RoadKind kind, string name, List<int> path, double ax, double az, double bx, double bz, double level = 0.0)
		{
			List<Road> made = new List<Road>();
			int n = path.Count;
			int k = 0;
			while (k < n)
			{
				if (router.OnRoad(path[k]))
				{
					k++;
					continue;
				}
				int s = k;
				while (k < n && !router.OnRoad(path[k]))
				{
					k++;
				}
				int e = k - 1;
				List<(double x, double z)> pts = new List<(double x, double z)>();
				List<(int idx, double y)> pins = new List<(int, double)>();
				double pinStart = double.NaN, pinEnd = double.NaN;
				if (s == 0)
				{
					pts.Add((ax, az));
				}
				else if (segments.Snap(router.X(path[s - 1]), router.Z(path[s - 1]), 140.0, -1, out double px, out double pz, out double py))
				{
					pts.Add((px, pz));
					pinStart = py;
				}
				for (int c = s; c <= e; c++)
				{
					pts.Add((router.X(path[c]), router.Z(path[c])));
				}
				if (e == n - 1)
				{
					if (!double.IsNaN(bx))
					{
						pts.Add((bx, bz));
					}
					else if (segments.Snap(router.X(path[e]), router.Z(path[e]), 160.0, -1, out double px2, out double pz2, out double py2))
					{
						pts.Add((px2, pz2));
						pinEnd = py2;
					}
				}
				else if (segments.Snap(router.X(path[e + 1]), router.Z(path[e + 1]), 140.0, -1, out double px3, out double pz3, out double py3))
				{
					pts.Add((px3, pz3));
					pinEnd = py3;
				}
				if (pts.Count < 2)
				{
					continue;
				}
				Class cl = Classes[(int)kind];
				List<(double x, double z)> line = Wobble(Smooth(pts, cl.Keep, cl.Rounds), kind);
				Road road = Make(kind, name, line);
				if (road.Count < 6)
				{
					continue;
				}
				pins.Add((0, double.IsNaN(pinStart) ? WorldGen.Base(road.X[0], road.Z[0]) : pinStart));
				for (int i = 1; s == 0 && i < road.Count - 1 && road.S[i] <= level; i++)
				{
					pins.Add((i, pins[0].y));
				}
				pins.Add((road.Count - 1, double.IsNaN(pinEnd) ? WorldGen.Base(road.X[road.Count - 1], road.Z[road.Count - 1]) : pinEnd));
				Add(road, pins);
				// The cells it was routed through are on it too, though its smoothed line may run through their neighbours: a later
				// route through them follows it, instead of building the same stretch again on top of it.
				for (int c = s; c <= e; c++)
				{
					router.Mark(path[c], kind);
				}
				made.Add(road);
			}
			return made;
		}

		/// <summary>Gives a new road its height (pins at its ends and where it crosses roads), its bridges, and adds it to the network.</summary>
		private static void Add(Road road, List<(int idx, double y)> pins)
		{
			Class cl = Classes[(int)road.Kind];
			segments.Crossings(road, pins);
			Profile(road, cl.Grade, cl.Window, pins);
			Bridges(road, cl.Grade);
			Seat(road, cl.Grade);
			Roads.Add(road);
			segments.Add(Roads.Count - 1, road);
			router.Mark(road);
		}

		private static Road Make(RoadKind kind, string name, List<(double x, double z)> points)
		{
			int n = points.Count;
			Road road = new Road { Kind = kind, Name = name, Half = HalfOf(kind), X = new double[n], Z = new double[n], Y = new double[n], S = new double[n], Bridge = new bool[n], Over = new bool[n] };
			for (int i = 0; i < n; i++)
			{
				road.X[i] = points[i].x;
				road.Z[i] = points[i].z;
			}
			Lengths(road);
			return road;
		}

		private static void Lengths(Road road)
		{
			road.S[0] = 0.0;
			for (int i = 1; i < road.Count; i++)
			{
				double dx = road.X[i] - road.X[i - 1];
				double dz = road.Z[i] - road.Z[i - 1];
				road.S[i] = road.S[i - 1] + Math.Sqrt(dx * dx + dz * dz);
			}
		}

		/// <summary>
		/// Grid path to curve: every <paramref name="keep"/>th point (and the ends), rounded off by Chaikin's corner cutting
		/// <paramref name="rounds"/> times, then resampled every <see cref="Step"/> units.
		/// </summary>
		private static List<(double x, double z)> Smooth(List<(double x, double z)> path, int keep, int rounds)
		{
			List<(double x, double z)> pts = new List<(double x, double z)> { path[0] };
			for (int i = keep; i < path.Count - 1; i += keep)
			{
				pts.Add(path[i]);
			}
			pts.Add(path[path.Count - 1]);
			for (int r = 0; r < rounds; r++)
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
			return Lines.Resample(pts, Step);
		}

		private static readonly ulong SaltWobble = Noise.Mix(WorldGen.Seed + 303);

		/// <summary>
		/// Small roads wander: each point pushed sideways by slow noise along the road (country lanes follow old field edges and kinks;
		/// a main road barely, the trunk not at all), fading to nothing at the ends so junctions stay exactly where they are.
		/// </summary>
		private static List<(double x, double z)> Wobble(List<(double x, double z)> line, RoadKind kind)
		{
			double amount = kind == RoadKind.Lane ? 22.0 : kind == RoadKind.Secondary ? 12.0 : kind == RoadKind.Primary ? 5.0 : 0.0;
			int n = line.Count;
			if (amount <= 0.0 || n < 8)
			{
				return line;
			}
			List<(double x, double z)> bent = new List<(double x, double z)>(n);
			double key = line[0].x * 0.37 + line[0].z * 0.61;
			for (int i = 0; i < n; i++)
			{
				int a = Math.Max(0, i - 1), b = Math.Min(n - 1, i + 1);
				double tx = line[b].x - line[a].x, tz = line[b].z - line[a].z;
				double len = Math.Sqrt(tx * tx + tz * tz);
				double fade = Math.Min(1.0, Math.Min(i, n - 1 - i) / 12.0);
				double push = Noise.Fbm(SaltWobble, i * Step, key, 180.0, 2, 0.5) * amount * fade;
				bent.Add(len > 0.0 ? (line[i].x - tz / len * push, line[i].z + tx / len * push) : line[i]);
			}
			return Lines.Resample(bent, Step);
		}

		/// <summary>
		/// The road's heights: the land under it averaged over <paramref name="window"/> samples each way (so it rides over bumps and
		/// through small dips), moved to meet each pin exactly (a straight-line correction between pins), then held to
		/// <paramref name="grade"/> by passes along the road in both directions.
		/// </summary>
		private static void Profile(Road road, double grade, int window, List<(int idx, double y)> pins)
		{
			int n = road.Count;
			double[] raw = new double[n + 1];
			for (int i = 0; i < n; i++)
			{
				raw[i + 1] = raw[i] + WorldGen.Base(road.X[i], road.Z[i]);
			}
			double[] y = road.Y;
			for (int i = 0; i < n; i++)
			{
				int a = Math.Max(0, i - window);
				int b = Math.Min(n - 1, i + window);
				y[i] = (raw[b + 1] - raw[a]) / (b - a + 1);
			}
			for (int round = 0; round < 3; round++)
			{
				Pin(y, pins);
				Hold(road, grade, 0, n - 1);
			}
			Pin(y, pins);
			Dry(road, grade);
		}

		/// <summary>How far a road's line is kept above any water surface drawn over its paving.</summary>
		private const double DryClear = 1.0;

		/// <summary>
		/// Keeps the road out of the water: a lake's or the sea's surface is drawn flat over its cells and the cells round them (so its
		/// edge hides under the shore), and on low land by the coast a road following the land ran up to 4 under it, the water drawn over
		/// the paving. Each sample is raised to <see cref="DryClear"/> over the highest water drawn across its paving, with ramps at the
		/// grade either side (the most of two profiles held to the grade is held to it too), so the road runs on a low embankment.
		/// </summary>
		private static void Dry(Road road, double grade)
		{
			int n = road.Count;
			double[] floor = new double[n];
			bool any = false;
			for (int i = 0; i < n; i++)
			{
				floor[i] = double.MinValue;
				Side(road, i, out double sx, out double sz);
				foreach (double f in new[] { -1.0, 0.0, 1.0 })
				{
					double w = Rivers.DrawnWater(road.X[i] + sx * road.Half * f, road.Z[i] + sz * road.Half * f);
					if (!double.IsNaN(w) && w + DryClear > floor[i])
					{
						floor[i] = w + DryClear;
						any |= floor[i] > road.Y[i];
					}
				}
			}
			if (!any)
			{
				return;
			}
			for (int i = 1; i < n; i++)
			{
				floor[i] = Math.Max(floor[i], floor[i - 1] - grade * (road.S[i] - road.S[i - 1]));
			}
			for (int i = n - 2; i >= 0; i--)
			{
				floor[i] = Math.Max(floor[i], floor[i + 1] - grade * (road.S[i + 1] - road.S[i]));
			}
			for (int i = 0; i < n; i++)
			{
				road.Y[i] = Math.Max(road.Y[i], floor[i]);
			}
		}

		/// <summary>
		/// Holds the road's heights from sample <paramref name="a"/> to <paramref name="b"/> to <paramref name="grade"/>, both ways, by
		/// each segment's real length (the last one of a road can be much shorter than the rest). Bridge decks stay where they are and
		/// the road ramps up to them: holding them too pulled a deck that stood above its approaches down towards the water.
		/// </summary>
		private static void Hold(Road road, double grade, int a, int b)
		{
			double[] y = road.Y;
			for (int i = a + 1; i <= b; i++)
			{
				if (!road.Over[i])
				{
					double rise = grade * (road.S[i] - road.S[i - 1]);
					y[i] = Math.Max(y[i - 1] - rise, Math.Min(y[i - 1] + rise, y[i]));
				}
			}
			for (int i = b - 1; i >= a; i--)
			{
				if (!road.Over[i])
				{
					double rise = grade * (road.S[i + 1] - road.S[i]);
					y[i] = Math.Max(y[i + 1] - rise, Math.Min(y[i + 1] + rise, y[i]));
				}
			}
		}

		/// <summary>Moves the heights by a straight-line correction between pins so each pinned sample is exactly at its pin.</summary>
		private static void Pin(double[] y, List<(int idx, double y)> pins)
		{
			for (int p = 0; p + 1 < pins.Count; p++)
			{
				(int i0, double y0) = pins[p];
				(int i1, double y1) = pins[p + 1];
				double d0 = y0 - y[i0];
				double d1 = y1 - y[i1];
				for (int i = i0; i <= i1; i++)
				{
					double t = i1 > i0 ? (double)(i - i0) / (i1 - i0) : 0.0;
					y[i] += d0 + (d1 - d0) * t;
				}
			}
		}

		/// <summary>
		/// Where the new road runs on a road already built (its first and last few samples at a junction, a crossing), it takes that road's
		/// height there, so it neither pokes up through the other's paving nor sinks under it; then its grade runs on from the edge of the
		/// paving. (Pinning only the junction point is not enough: across a 66-wide trunk road a lane at 11% would climb 3 or more.)
		/// A bridge's deck is never lowered here, and stays where it is while the rest is held to the grade.
		/// </summary>
		private static void Seat(Road road, double grade)
		{
			int n = road.Count;
			bool[] seated = new bool[n];
			bool any = false;
			for (int i = 0; i < n; i++)
			{
				seated[i] = road.Over[i];
				if (segments.Under(road.X[i], road.Z[i], 1.5, out double y) && !(road.Over[i] && y < road.Y[i]))
				{
					road.Y[i] = y;
					seated[i] = true;
					any = true;
				}
			}
			if (!any)
			{
				return;
			}
			// Hold the rest to the grade away from the seated samples, which stay as they are.
			for (int round = 0; round < 2; round++)
			{
				for (int i = 1; i < n; i++)
				{
					if (!seated[i])
					{
						double rise = grade * (road.S[i] - road.S[i - 1]);
						road.Y[i] = Math.Max(road.Y[i - 1] - rise, Math.Min(road.Y[i - 1] + rise, road.Y[i]));
					}
				}
				for (int i = n - 2; i >= 0; i--)
				{
					if (!seated[i])
					{
						double rise = grade * (road.S[i + 1] - road.S[i]);
						road.Y[i] = Math.Max(road.Y[i + 1] - rise, Math.Min(road.Y[i + 1] + rise, road.Y[i]));
					}
				}
			}
		}

		/// <summary>
		/// Bridges: wherever the road's paving would be over a river's water, it is carried on a deck clear of the water (5 units, more
		/// for a wide road) from a little before the water to a little after it (the deck's ends over the banks may already ramp down),
		/// with ramps up to it at the road's grade, and the ground
		/// under the deck is left alone so the river runs under it. Crossings are found by intersecting the road's line with the rivers'
		/// lines, so a narrow stream is never stepped over between two samples (8 apart) as it was when only the samples were tested;
		/// each span reaches across the water at the angle the road crosses it, to the paving's far corners. Samples whose paving reaches
		/// a channel (a road running along a river) are on the deck too. Farm tracks stop short of water and have no deck.
		/// </summary>
		private static void Bridges(Road road, double grade)
		{
			if (Unpaved(road.Kind))
			{
				return;
			}
			int n = road.Count;
			List<(double s0, double s1, double w)> spans = new List<(double, double, double)>();
			List<(double s0, double s1)> wet = new List<(double, double)>();
			List<(double t, double w, double half, double sin)> found = new List<(double, double, double, double)>();
			for (int i = 0; i + 1 < n; i++)
			{
				Rivers.Crossings(road.X[i], road.Z[i], road.X[i + 1], road.Z[i + 1], found);
				foreach ((double t, double w, double half, double sin) in found)
				{
					double s = road.S[i] + (road.S[i + 1] - road.S[i]) * t;
					// Along the road, the water (drawn 2 past the channel) is half / sin each way of the crossing, and the paving's corners
					// reach Half / tan further at a slant. Near alongside, the samples' own test below takes over.
					double sn = Math.Max(0.3, sin);
					double slant = road.Half * Math.Sqrt(1.0 - sn * sn) / sn;
					double reach = (half + 2.0 + Abutment) / sn + slant;
					spans.Add((s - reach, s + reach, w));
					wet.Add((s - (half + 2.0) / sn - slant, s + (half + 2.0) / sn + slant));
				}
			}
			for (int i = 0; i < n; i++)
			{
				if (Rivers.Channel(road.X[i], road.Z[i], road.Half + 2.0, out double level))
				{
					spans.Add((road.S[i] - Abutment - Step, road.S[i] + Abutment + Step, level));
					wet.Add((road.S[i] - Step, road.S[i] + Step));
				}
			}
			if (spans.Count == 0)
			{
				return;
			}
			spans.Sort((p, q) => p.s0 != q.s0 ? p.s0.CompareTo(q.s0) : p.s1 != q.s1 ? p.s1.CompareTo(q.s1) : p.w.CompareTo(q.w));
			int k = 0;
			while (k < spans.Count)
			{
				(double s0, double s1, double top) = spans[k++];
				// Spans closer than two samples are one bridge.
				while (k < spans.Count && spans[k].s0 < s1 + 2.0 * Step)
				{
					s1 = Math.Max(s1, spans[k].s1);
					top = Math.Max(top, spans[k].w);
					k++;
				}
				int a = 0, b = n - 1;
				while (a + 1 < n && road.S[a + 1] <= s0)
				{
					a++;
				}
				while (b - 1 >= 0 && road.S[b - 1] >= s1)
				{
					b--;
				}
				if (b < a)
				{
					continue;
				}
				double deck = Math.Max(top + DeckClear + road.Half * 0.05, Math.Max(road.Y[a], road.Y[b]));
				for (int j = a; j <= b; j++)
				{
					road.Y[j] = deck;
					road.Bridge[j] = true;
					foreach ((double w0, double w1) in wet)
					{
						road.Over[j] |= road.S[j] >= w0 && road.S[j] <= w1;
					}
				}
			}
			Hold(road, grade, 0, n - 1);
		}

		// ---- junctions ----------------------------------------------------------------------------------------------

		/// <summary>
		/// A stretch of a road that is not drawn because it lies on a road drawn over it (a bigger road, or one of its own class built
		/// before it): where it ends on that road, or crosses it. Its centre line is on the other road from <see cref="C0"/> to
		/// <see cref="C1"/> (distances along the road); its left edge is cut from <see cref="L0"/> to <see cref="L1"/>, its right edge
		/// from <see cref="R0"/> to <see cref="R1"/>, each where it meets the other road's edge, so the cut runs along that edge. A gap
		/// at the road's start begins before 0, one at its end runs past its length.
		/// </summary>
		internal sealed class Gap
		{
			public double C0;
			public double C1;
			public double L0;
			public double L1;
			public double R0;
			public double R1;
		}

		/// <summary>
		/// Where each road (in the order built) runs onto the paving of a road that should be drawn over it, it is cut out, so the two
		/// surfaces never overlap (where they did, the one underneath could poke through: a flat road across another on a grade is
		/// higher at one edge than the other road there). Either side of the cut, the road's mouth flares out a little, and over the
		/// apron its height and its cross-fall (<see cref="Road.Tilt"/>) are brought to the other road's surface along the cut, a
		/// little under it, so the two meet flush. Crossings of two roads of one class meet the same way, the later one cut.
		/// </summary>
		private static void Junctions()
		{
			for (int r = 0; r < Roads.Count; r++)
			{
				Road road = Roads[r];
				road.Tilt = new double[road.Count];
				// The hill climb is in no junction: nothing crosses it, and its lane meets it end to end.
				if (Unpaved(road.Kind) || road.Kind == RoadKind.Climb || road.Count < 4)
				{
					continue;
				}
				// Samples whose paving the other road's can reach, in runs; each run holds at most one cut (or two merged).
				int n = road.Count;
				double reach = road.Half + Flare + 2.0;
				int k = 0;
				while (k < n)
				{
					if (segments.Paving(road.X[k], road.Z[k], r, road.Kind, r, -reach, out double _) < 0)
					{
						k++;
						continue;
					}
					int a = k;
					while (k < n && segments.Paving(road.X[k], road.Z[k], r, road.Kind, r, -reach, out double _) >= 0)
					{
						k++;
					}
					Cut(r, road, Math.Max(0, a - 1), Math.Min(n - 1, k));
				}
				if (road.Gaps.Count > 0)
				{
					Regrade(road);
				}
			}
		}

		/// <summary>
		/// After its cuts, the drawn road is held to its class's grade, with some leeway (<see cref="Leeway"/>): the samples in a cut,
		/// where it meets the other road, and bridge decks over water stay where they are.
		/// </summary>
		private static void Regrade(Road road)
		{
			int n = road.Count;
			bool[] fixedAt = new bool[n];
			for (int k = 0; k < n; k++)
			{
				fixedAt[k] = road.Over[k] || !Shown(road, road.S[k]);
			}
			for (int i = 1; i < n; i++)
			{
				if (!fixedAt[i])
				{
					double rise = Leeway(road, i - 1) * (road.S[i] - road.S[i - 1]);
					road.Y[i] = Math.Max(road.Y[i - 1] - rise, Math.Min(road.Y[i - 1] + rise, road.Y[i]));
				}
			}
			for (int i = n - 2; i >= 0; i--)
			{
				if (!fixedAt[i])
				{
					double rise = Leeway(road, i) * (road.S[i + 1] - road.S[i]);
					road.Y[i] = Math.Max(road.Y[i + 1] - rise, Math.Min(road.Y[i + 1] + rise, road.Y[i]));
				}
			}
		}

		/// <summary>
		/// The steepest grade piece i of a road with cuts may have: half as much again as its class's in a cut's apron, three quarters
		/// more for a street or lane (a short pitch where it meets the other road, as real lanes have: one coming off a bridge onto a
		/// street on the floodplain needs it), a quarter more elsewhere.
		/// </summary>
		internal static double Leeway(Road road, int i)
		{
			double grade = Classes[(int)road.Kind].Grade;
			foreach (Gap g in road.Gaps)
			{
				if (road.S[i] < Math.Max(g.L1, g.R1) + Apron + Step && road.S[i + 1] > Math.Min(g.L0, g.R0) - Apron - Step)
				{
					return grade * (road.Kind >= RoadKind.Street ? 1.75 : 1.5);
				}
			}
			return grade * 1.25;
		}

		/// <summary>The cut in samples a to b of road r, if its centre line is on another road there (see <see cref="Junctions"/>).</summary>
		private static void Cut(int r, Road road, int a, int b)
		{
			int n = road.Count;
			double length = road.S[n - 1];
			// The centre line's stretch on the others.
			List<int> others = new List<int>();
			double c0 = double.NaN, c1 = double.NaN;
			for (double s = road.S[a]; s <= road.S[b] + 1e-9; s += 0.5)
			{
				int o = OnOther(r, road, s);
				if (o >= 0)
				{
					if (!others.Contains(o))
					{
						others.Add(o);
					}
					c0 = double.IsNaN(c0) ? s : c0;
					c1 = s;
				}
			}
			if (double.IsNaN(c0))
			{
				// Only an edge runs onto another road (passing close by it): not cut, but brought just under its surface there.
				double e0 = double.NaN, e1 = double.NaN;
				for (int side = -1; side <= 1; side += 2)
				{
					for (double s = road.S[a]; s <= road.S[b] + 1e-9; s += 0.5)
					{
						Edge(road, s, side, 0.0, out double ex, out double ez, out double _);
						int o = segments.Paving(ex, ez, r, road.Kind, r, 0.0, out double _);
						if (o >= 0)
						{
							if (!others.Contains(o))
							{
								others.Add(o);
							}
							e0 = double.IsNaN(e0) ? s : Math.Min(e0, s);
							e1 = double.IsNaN(e1) ? s : Math.Max(e1, s);
						}
					}
				}
				if (!double.IsNaN(e0))
				{
					Level(road, others, e0 - 0.5, e1 + 0.5, null);
				}
				return;
			}
			// The ends of the stretch exactly (to a hundredth), so the drawn surface stops at the other's edge, not up to half a unit on it.
			c0 = Boundary(r, road, c0, Math.Max(road.S[a], c0 - 0.5));
			c1 = Boundary(r, road, c1, Math.Min(road.S[b], c1 + 0.5));
			bool start = c0 <= road.S[a] + 1e-9 && a == 0, end = c1 >= road.S[b] - 0.5 && b == n - 1;
			Gap gap = new Gap { C0 = start ? -1.0 : c0, C1 = end ? length + 1.0 : c1 };
			// A cut next to one already made (two roads met at one place) is one cut.
			foreach (Gap g in road.Gaps)
			{
				if (gap.C0 < g.C1 + 2.0 * FlareLength && gap.C1 > g.C0 - 2.0 * FlareLength)
				{
					g.C0 = Math.Min(g.C0, gap.C0);
					g.C1 = Math.Max(g.C1, gap.C1);
					road.Gaps.Remove(g);
					gap = g;
					break;
				}
			}
			gap.L0 = gap.R0 = gap.C0;
			gap.L1 = gap.R1 = gap.C1;
			road.Gaps.Add(gap);
			road.Gaps.Sort((p, q) => p.C0.CompareTo(q.C0));

			// Each edge is cut where it, or the road's half on its side, is on any of the others' paving (a quarter unit inside their
			// edge, so no ground shows between): lines from the edge to the centre a quarter of the way apart are tested, so a cut also clears another
			// road's square end or a bend in its edge.
			double window = 3.0 * (road.Half + Flare) + FlareLength;
			for (int side = -1; side <= 1; side += 2)
			{
				double e0 = double.NaN, e1 = double.NaN;
				for (double s = Math.Max(0.0, gap.C0 - window); s <= Math.Min(length, gap.C1 + window) + 1e-9; s += 0.25)
				{
					Locate(road, s, out int pi, out double pt);
					for (int line = 0; line < 5; line++)
					{
						Point(road, pi, pt, side * (1.0 - line * 0.25), 0.0, out double ex, out double ez, out double _);
						int o = segments.Paving(ex, ez, r, road.Kind, r, 0.25, out double _);
						if (o >= 0)
						{
							if (!others.Contains(o))
							{
								others.Add(o);
							}
							e0 = double.IsNaN(e0) ? s : e0;
							e1 = s + 0.25;
							break;
						}
					}
				}
				double lo = Math.Min(gap.C0, double.IsNaN(e0) ? gap.C0 : e0), hi = Math.Max(gap.C1, double.IsNaN(e1) ? gap.C1 : e1);
				if (start)
				{
					lo = -1.0;
				}
				if (end)
				{
					hi = length + 1.0;
				}
				if (side > 0)
				{
					gap.L0 = lo;
					gap.L1 = hi;
				}
				else
				{
					gap.R0 = lo;
					gap.R1 = hi;
				}
			}

			Level(road, others, Math.Min(gap.L0, gap.R0), Math.Max(gap.L1, gap.R1), gap);
		}

		/// <summary>
		/// The apron round a cut from <paramref name="from"/> to <paramref name="to"/> (where the road is not drawn): inside it the road's
		/// heights and cross-fall are those that meet the surface of the <paramref name="others"/> at its edges, a little under it;
		/// beyond it, the difference that made at the last sample inside fades over <see cref="Apron"/>, so the road keeps its own grade
		/// (holding it level with the other road out to the apron's end steepened it). Then the sample inside next to each end of the
		/// cut is set so the drawn surface, which starts between it and the first sample out, meets the other's surface at the cut's
		/// corners and is nowhere above it.
		/// </summary>
		private static void Level(Road road, List<int> others, double from, double to, Gap gap)
		{
			int n = road.Count;
			double[] own = (double[])road.Y.Clone();
			int first = -1, last = -1;
			for (int k = 0; k < n; k++)
			{
				if (road.S[k] >= from && road.S[k] <= to && Meet(road, others, road.S[k], out double y, out double tilt))
				{
					road.Y[k] = y;
					road.Tilt[k] = tilt;
				}
				if (road.S[k] < from)
				{
					first = k;
				}
				if (road.S[k] > to && last < 0)
				{
					last = k;
				}
			}
			for (int end = 0; end < 2; end++)
			{
				int k0 = end == 0 ? first : last;
				int kh = end == 0 ? first + 1 : last - 1;
				if (k0 < 0 || kh < 0 || kh >= n || !Meet(road, others, road.S[kh], out double y, out double tilt))
				{
					continue;
				}
				double lift = y - own[kh];
				for (int k = k0; k >= 0 && k < n; k += end == 0 ? -1 : 1)
				{
					double fade = 1.0 - Noise.Step(0.0, 1.0, Math.Abs(road.S[k] - road.S[kh]) / Apron);
					if (fade <= 0.0)
					{
						break;
					}
					road.Y[k] += lift * fade;
					road.Tilt[k] = tilt * fade;
				}
				// The cut's corners (each edge's own cut) on the line from the sample inside to the first one out.
				if (gap == null || road.S[kh] < from || road.S[kh] > to)
				{
					continue;
				}
				double[] corners = end == 0 ? new[] { gap.L0, gap.R0 } : new[] { gap.L1, gap.R1 };
				foreach (double c in corners)
				{
					double u = (c - road.S[kh]) / (road.S[k0] - road.S[kh]);
					if (u <= 0.0 || u >= 1.0 || !Meet(road, others, c, out double at, out double meet))
					{
						continue;
					}
					// Both edges, with the cross-fall the surface has there (between the two samples') against the one that meets.
					double half = HalfAt(road, c);
					double drawn = road.Tilt[kh] * (1.0 - u) + road.Tilt[k0] * u;
					double centre = at - Math.Abs(meet - drawn) * half;
					// Mostly by the sample inside, at most a unit under where it was (a corner close to the sample out would take it far
					// down); the rest by the sample out, at most 0.6, so the road keeps near its grade.
					road.Y[kh] = Math.Max(road.Y[kh] - 1.0, Math.Min(road.Y[kh], (centre - road.Y[k0] * u) / (1.0 - u)));
					road.Y[k0] = Math.Max(road.Y[k0] - 0.6, Math.Min(road.Y[k0], (centre - road.Y[kh] * (1.0 - u)) / u));
				}
			}
		}

		/// <summary>
		/// The height and cross-fall the road would have at distance <paramref name="s"/> along it to meet the surface of the
		/// <paramref name="others"/> at its edges, just under it.
		/// </summary>
		private static bool Meet(Road road, List<int> others, double s, out double y, out double tilt)
		{
			y = tilt = 0.0;
			double half = HalfAt(road, s);
			Locate(road, s, out int i, out double t);
			Side(road, i, out double ax, out double az);
			Side(road, i + 1, out double bx, out double bz);
			double sx = ax * (1.0 - t) + bx * t, sz = az * (1.0 - t) + bz * t;
			double len = Math.Sqrt(sx * sx + sz * sz);
			sx /= Math.Max(1e-9, len);
			sz /= Math.Max(1e-9, len);
			Centre(road, i, t, out double cx, out double cz);
			if (!Surface(others, cx + sx * half, cz + sz * half, out double left) || !Surface(others, cx - sx * half, cz - sz * half, out double right))
			{
				return false;
			}
			y = (left + right) * 0.5 - MouthDrop - Top(road.Kind);
			tilt = Math.Max(-0.08, Math.Min(0.08, (left - right) / (2.0 * half)));
			return true;
		}

		/// <summary>The road drawn over road r's centre line at distance <paramref name="s"/> along it, or -1.</summary>
		private static int OnOther(int r, Road road, double s)
		{
			Locate(road, s, out int i, out double t);
			Centre(road, i, t, out double cx, out double cz);
			return segments.Paving(cx, cz, r, road.Kind, r, 0.0, out double _);
		}

		/// <summary>Between <paramref name="inside"/> (on another road) and <paramref name="outside"/>, where road r's centre line leaves it.</summary>
		private static double Boundary(int r, Road road, double inside, double outside)
		{
			if (OnOther(r, road, outside) >= 0)
			{
				return outside;
			}
			for (int k = 0; k < 6; k++)
			{
				double mid = (inside + outside) * 0.5;
				if (OnOther(r, road, mid) >= 0)
				{
					inside = mid;
				}
				else
				{
					outside = mid;
				}
			}
			return inside;
		}

		/// <summary>The surface (road line plus its <see cref="Top"/>) of the nearest of <paramref name="others"/> to (x, z), level across it.</summary>
		private static bool Surface(List<int> others, double x, double z, out double y)
		{
			y = 0.0;
			double best = double.MaxValue;
			foreach (int o in others)
			{
				if (segments.Nearest(o, x, z, 80.0, out double d, out int k, out double t) && d - Roads[o].Half < best)
				{
					// With the other's own cross-fall, if it is in a mouth itself, out to its drawn edge.
					Road other = Roads[o];
					double sx = other.X[k + 1] - other.X[k], sz = other.Z[k + 1] - other.Z[k];
					double sl = Math.Sqrt(sx * sx + sz * sz);
					double across = sl > 0.0 ? ((z - other.Z[k]) * sx - (x - other.X[k]) * sz) / sl : 0.0;
					double half = HalfAt(other, other.S[k] + (other.S[k + 1] - other.S[k]) * t);
					double tilt = other.Tilt == null ? 0.0 : other.Tilt[k] + (other.Tilt[k + 1] - other.Tilt[k]) * t;
					best = d - other.Half;
					y = other.Y[k] + (other.Y[k + 1] - other.Y[k]) * t + Top(other.Kind) + tilt * Math.Max(-half, Math.Min(half, across));
				}
			}
			return best < double.MaxValue;
		}

		/// <summary>
		/// Marks the samples within reach of a bridge (the widest bank and more), whose banks <see cref="Ground"/> keeps out of the river;
		/// and notes how much of each piece is drawn, for the ground's beds.
		/// </summary>
		private static void Abutments()
		{
			List<(double t0, double t1)> spans = new List<(double, double)>();
			foreach (Road road in Roads)
			{
				int n = road.Count;
				road.From = new double[n - 1];
				road.To = new double[n - 1];
				for (int i = 0; i + 1 < n; i++)
				{
					Spans(road, i, spans);
					road.From[i] = spans.Count > 0 ? spans[0].t0 : 0.0;
					road.To[i] = spans.Count > 0 ? spans[spans.Count - 1].t1 : 1.0;
				}
				road.Abut = new bool[n];
				int reach = (int)Math.Ceiling(Reach(road) / Step) + 2;
				for (int k = 0; k < n; k++)
				{
					if (!road.Bridge[k])
					{
						continue;
					}
					for (int j = Math.Max(0, k - reach); j <= Math.Min(n - 1, k + reach); j++)
					{
						road.Abut[j] = true;
					}
				}
			}
		}

		// ---- the drawn surface -------------------------------------------------------------------------------------

		/// <summary>The piece (sample i to i + 1) and fraction along it at distance <paramref name="s"/> along the road.</summary>
		internal static void Locate(Road road, double s, out int i, out double t)
		{
			int n = road.Count;
			int lo = 0, hi = n - 1;
			while (hi - lo > 1)
			{
				int mid = (lo + hi) / 2;
				if (road.S[mid] <= s)
				{
					lo = mid;
				}
				else
				{
					hi = mid;
				}
			}
			i = Math.Min(lo, n - 2);
			double len = road.S[i + 1] - road.S[i];
			t = len > 0.0 ? Math.Max(0.0, Math.Min(1.0, (s - road.S[i]) / len)) : 0.0;
		}

		private static void Centre(Road road, int i, double t, out double x, out double z)
		{
			x = road.X[i] * (1.0 - t) + road.X[i + 1] * t;
			z = road.Z[i] * (1.0 - t) + road.Z[i + 1] * t;
		}

		/// <summary>The unit vector to the left of the road at sample k (from the direction between its neighbours).</summary>
		internal static void Side(Road road, int k, out double sx, out double sz)
		{
			int a = Math.Max(0, k - 1);
			int b = Math.Min(road.Count - 1, k + 1);
			// Round a loop's join, its first and last samples (the same point) take the same direction, so its two ends meet exactly.
			if (road.Loop && (k == 0 || k == road.Count - 1))
			{
				a = road.Count - 2;
				b = 1;
			}
			double tx = road.X[b] - road.X[a];
			double tz = road.Z[b] - road.Z[a];
			double len = Math.Sqrt(tx * tx + tz * tz);
			if (len < 1e-9)
			{
				sx = 1.0;
				sz = 0.0;
				return;
			}
			sx = -tz / len;
			sz = tx / len;
		}

		/// <summary>Half the drawn width at distance <paramref name="s"/> along the road: its own, flaring out by a cut (a mouth).</summary>
		internal static double HalfAt(Road road, double s)
		{
			double flare = 0.0;
			foreach (Gap g in road.Gaps)
			{
				flare = Math.Max(flare, s < g.C0 ? Bell((g.C0 - s) / FlareLength) : s > g.C1 ? Bell((s - g.C1) / FlareLength) : 1.0);
			}
			return road.Half + Math.Min(Flare, road.Half * 0.5) * flare;
		}

		/// <summary>The flare's shape: full at the other road's edge, curving in to nothing (a kerb's radius, roughly).</summary>
		private static double Bell(double u)
		{
			return u >= 1.0 ? 0.0 : (1.0 - u) * (1.0 - u);
		}

		/// <summary>
		/// A point of the drawn surface on piece i at fraction t: <paramref name="side"/> 1 the left edge, -1 the right, 0 the centre (or
		/// a fraction of the way), pushed <paramref name="beyond"/> further out; and the surface's height there (from sea level), with the
		/// cross-fall of a mouth. The same arithmetic for a sample whichever piece asks, so pieces drawn by different tiles meet exactly.
		/// </summary>
		internal static void Point(Road road, int i, double t, double side, double beyond, out double x, out double z, out double y)
		{
			Side(road, i, out double ax, out double az);
			Side(road, i + 1, out double bx, out double bz);
			double sx = ax * (1.0 - t) + bx * t, sz = az * (1.0 - t) + bz * t;
			double len = Math.Sqrt(sx * sx + sz * sz);
			if (len > 1e-9)
			{
				sx /= len;
				sz /= len;
			}
			double s = road.S[i] * (1.0 - t) + road.S[i + 1] * t;
			double off = side * HalfAt(road, s) + Math.Sign(side) * beyond;
			x = road.X[i] * (1.0 - t) + road.X[i + 1] * t + sx * off;
			z = road.Z[i] * (1.0 - t) + road.Z[i + 1] * t + sz * off;
			double tilt = road.Tilt != null ? road.Tilt[i] * (1.0 - t) + road.Tilt[i + 1] * t : 0.0;
			y = road.Y[i] * (1.0 - t) + road.Y[i + 1] * t + Top(road.Kind) + tilt * side * HalfAt(road, s);
		}

		/// <summary>The drawn edge's point at distance <paramref name="s"/> along the road (see <see cref="Point"/>).</summary>
		internal static void Edge(Road road, double s, int side, double beyond, out double x, out double z, out double y)
		{
			Locate(road, s, out int i, out double t);
			Point(road, i, t, side, beyond, out x, out z, out y);
		}

		/// <summary>
		/// The stretches of piece i drawn across the road's whole width, as fractions along it, into <paramref name="spans"/> (cleared
		/// first). Where one edge is cut further than the other, the rest of the cut's slant is a fan (<see cref="Fans"/>).
		/// </summary>
		internal static void Spans(Road road, int i, List<(double t0, double t1)> spans)
		{
			spans.Clear();
			double s0 = road.S[i], s1 = road.S[i + 1], len = s1 - s0;
			if (len <= 0.0)
			{
				return;
			}
			double at = s0;
			foreach (Gap g in road.Gaps)
			{
				double lo = Math.Min(g.L0, g.R0), hi = Math.Max(g.L1, g.R1);
				if (hi <= at || lo >= s1)
				{
					continue;
				}
				if (lo > at)
				{
					spans.Add(((at - s0) / len, (lo - s0) / len));
				}
				at = Math.Max(at, hi);
			}
			if (at < s1)
			{
				spans.Add(((at - s0) / len, 1.0));
			}
		}

		/// <summary>
		/// The fans that finish the cuts' slanted ends drawn by piece i: each its apex (the corner where the full-width surface ends, at
		/// distance <c>apex</c> on side <c>side</c>) and the other edge's stretch from <c>from</c> to <c>to</c> it fans over.
		/// </summary>
		internal static void Fans(Road road, int i, List<(double apex, int side, double from, double to)> fans)
		{
			fans.Clear();
			double length = road.S[road.Count - 1];
			foreach (Gap g in road.Gaps)
			{
				// Before the cut: the full width ends at the earlier of the two edges' cuts; the other edge runs on to its own.
				if (g.L0 != g.R0 && Math.Max(g.L0, g.R0) > 0.0)
				{
					double apex = Math.Min(g.L0, g.R0);
					Add(road, i, fans, apex, g.L0 < g.R0 ? 1 : -1, apex, Math.Max(g.L0, g.R0));
				}
				// After it: the full width starts at the later; the other edge starts at its own, before it.
				if (g.L1 != g.R1 && Math.Min(g.L1, g.R1) < length)
				{
					double apex = Math.Max(g.L1, g.R1);
					Add(road, i, fans, apex, g.L1 > g.R1 ? 1 : -1, Math.Min(g.L1, g.R1), apex);
				}
			}
		}

		private static void Add(Road road, int i, List<(double apex, int side, double from, double to)> fans, double apex, int side, double from, double to)
		{
			Locate(road, apex, out int ai, out double _);
			if (ai == i)
			{
				fans.Add((apex, side, Math.Max(0.0, from), Math.Min(road.S[road.Count - 1], to)));
			}
		}

		/// <summary>Whether distance <paramref name="s"/> along the road is drawn (not in a cut), for things that lie beside it.</summary>
		internal static bool Shown(Road road, double s)
		{
			foreach (Gap g in road.Gaps)
			{
				if (s > g.C0 && s < g.C1)
				{
					return false;
				}
			}
			return true;
		}

		// ---- growth ------------------------------------------------------------------------------------------------

		/// <summary>
		/// Streets grown out from the roads of a town or village, the way Parish and Müller grow city streets: along each road in the place,
		/// side streets branch off every 60 to 120 m (more often near the centre), each a short random walk; each step obeys local rules: a
		/// street that would cross another stops there (a junction), one that comes near another joins it, one near an existing junction
		/// snaps to it, and one that leaves the place, reaches water or gets too steep stops. Streets then branch again, less often. The
		/// result is the denser-in-the-middle, T-junction-rich network real towns have, never a grid.
		/// </summary>
		private static void Grow(Rng rng, Places.Place place)
		{
			bool town = place.Kind == PlaceKind.Town;
			int depthMost = town ? 3 : 2;
			Queue<(int road, int depth)> queue = new Queue<(int, int)>();

			// The old roads out of the centre to the place's fields, spread round the compass between the roads already arriving.
			int radials = town ? 5 : 2;
			double turn = rng.Next() * 2.0 * DetMath.Pi;
			double start = WorldGen.Base(place.X, place.Z);
			bool hub = segments.Snap(place.X, place.Z, place.Radius * 0.25, -1, out double cx, out double cz, out double cy);
			for (int k = 0; k < radials; k++)
			{
				double angle = turn + k * 2.0 * DetMath.Pi / radials + rng.Range(-0.3, 0.3);
				Road radial = Walk(rng, hub ? cx : place.X, hub ? cz : place.Z, hub ? cy : start, angle, place.Radius * rng.Range(0.8, 1.05), place, RoadKind.Street, -1, 0.08);
				if (radial != null)
				{
					queue.Enqueue((Roads.Count - 1, 1));
				}
			}
			foreach (int r in segments.RoadsNear(place.X, place.Z, place.Radius))
			{
				if (Roads[r].Kind <= RoadKind.Lane)
				{
					queue.Enqueue((r, 0));
				}
			}
			int made = 0;
			while (queue.Count > 0 && made < (town ? 700 : 60))
			{
				(int r, int depth) = queue.Dequeue();
				Road parent = Roads[r];
				// Side streets 60 to 120 m apart in a town (200 to 400 units), so the blocks between them hold houses with gardens.
				double gap = rng.Range(0.7, 1.3) * (town ? 260.0 : 300.0);
				for (int i = 2; i < parent.Count - 2; i++)
				{
					if (parent.S[i] < gap)
					{
						continue;
					}
					double dx = parent.X[i] - place.X, dz = parent.Z[i] - place.Z;
					double from = Math.Sqrt(dx * dx + dz * dz) / place.Radius;
					if (from > 1.0)
					{
						continue;
					}
					gap = parent.S[i] + rng.Range(0.7, 1.3) * (town ? 260.0 : 300.0) * (1.0 + 0.6 * from);
					for (int side = -1; side <= 1; side += 2)
					{
						double chance = (town ? 0.92 : 0.7) * (1.0 - from * from) * (depth <= 1 ? 1.0 : 0.85 / depth);
						if (rng.Next() >= chance)
						{
							continue;
						}
						double length = rng.Range(250.0, town ? 900.0 : 500.0) * (1.0 - 0.4 * from);
						Road street = Branch(rng, parent, r, i, side, length, place, RoadKind.Street);
						if (street != null)
						{
							made++;
							if (depth + 1 <= depthMost)
							{
								queue.Enqueue((Roads.Count - 1, depth + 1));
							}
						}
					}
				}
			}
		}

		/// <summary>Farm tracks: dead ends off the lanes and minor roads in the country, into the fields.</summary>
		private static void Tracks(Rng rng)
		{
			int count = Roads.Count;
			for (int r = 0; r < count; r++)
			{
				Road parent = Roads[r];
				if (parent.Kind != RoadKind.Lane && parent.Kind != RoadKind.Secondary)
				{
					continue;
				}
				double next = rng.Range(300.0, 900.0);
				for (int i = 2; i < parent.Count - 2; i++)
				{
					if (parent.S[i] < next || parent.Bridge[i])
					{
						continue;
					}
					next = parent.S[i] + rng.Range(400.0, 1100.0);
					if (InPlace(parent.X[i], parent.Z[i]) || rng.Next() > 0.45)
					{
						continue;
					}
					Branch(rng, parent, r, i, rng.Next() < 0.5 ? -1 : 1, rng.Range(150.0, 450.0), null, RoadKind.Track);
				}
			}
		}

		/// <summary>
		/// Footpaths, the way real ones run: on from the dead end of a street in a town or village, out across the fields, until they
		/// meet a road (or a track) or have gone their length; they stop at water and on steep ground (<see cref="Walk"/>).
		/// </summary>
		private static void Footpaths(Rng rng)
		{
			int count = Roads.Count;
			for (int r = 0; r < count; r++)
			{
				Road street = Roads[r];
				if (street.Kind != RoadKind.Street || street.Count < 4)
				{
					continue;
				}
				for (int end = 0; end < 2; end++)
				{
					int i = end == 0 ? 0 : street.Count - 1, j = end == 0 ? 1 : street.Count - 2;
					double x = street.X[i], z = street.Z[i];
					// A dead end: no other road at this end.
					if (segments.Snap(x, z, street.Half + 12.0, r, out double _, out double _, out double _) || rng.Next() > 0.7)
					{
						continue;
					}
					double angle = Atan2(z - street.Z[j], x - street.X[j]);
					Walk(rng, x, z, street.Y[i], angle, rng.Range(250.0, 700.0), null, RoadKind.Path, r, 0.25);
				}
			}
		}

		/// <summary>
		/// Through ways. Country lanes, farm tracks and footpaths ended in nothing too often (the owner's report): real ones mostly run
		/// on to another road (a lane through a hamlet and out the far side, a track round the fields to the next lane, a path from stile
		/// to stile), so the small roads make loops. From some of the dead ends outside the towns and villages a way of the same class
		/// is walked on (<see cref="Walk"/>, steered) towards a point on another road: the nearest, counting a point behind as up to
		/// three times as far as one ahead; never one back by the road's own start, which would close only a sliver. Kept only if it
		/// reaches a road it may end on (a lane never on a track or path, a track never on a path); if not, a second point is tried away
		/// from the first, and if that fails too nothing is built.
		/// </summary>
		private static void Throughs(Rng rng)
		{
			int count = Roads.Count;
			for (int r = 0; r < count; r++)
			{
				Road road = Roads[r];
				if ((road.Kind != RoadKind.Lane && road.Kind != RoadKind.Track && road.Kind != RoadKind.Path) || road == RallyAccess || road == ClimbAccess || road.Count < 4)
				{
					continue;
				}
				// How far ahead to look and how often to carry on: lanes furthest, tracks least (some still end in a field).
				double reach = road.Kind == RoadKind.Lane ? 1500.0 : road.Kind == RoadKind.Track ? 700.0 : 1000.0;
				double chance = road.Kind == RoadKind.Track ? 0.55 : road.Kind == RoadKind.Lane ? 0.85 : 0.75;
				RoadKind smallest = road.Kind == RoadKind.Path ? RoadKind.Path : road.Kind == RoadKind.Track ? RoadKind.Track : RoadKind.Lane;
				for (int end = 0; end < 2; end++)
				{
					int i = end == 0 ? 0 : road.Count - 1, j = end == 0 ? Math.Min(3, road.Count - 1) : Math.Max(0, road.Count - 4);
					int o = end == 0 ? road.Count - 1 : 0;
					double x = road.X[i], z = road.Z[i];
					if (segments.Snap(x, z, road.Half + 12.0, r, out double _, out double _, out double _) || InTown(x, z) || rng.Next() > chance)
					{
						continue;
					}
					double ahead = Atan2(z - road.Z[j], x - road.X[j]);
					double ax = DetMath.Cos(ahead), az = DetMath.Sin(ahead);
					// Two tries: if the way to the best point is blocked (water, a slope too steep), the best point elsewhere.
					double fx = double.NaN, fz = double.NaN;
					for (int attempt = 0; attempt < 2; attempt++)
					{
						double tx = 0.0, tz = 0.0, best = double.MaxValue, dist = 0.0;
						foreach (int q in segments.RoadsNear(x, z, reach))
						{
							Road other = Roads[q];
							if (q == r || other.Kind > smallest)
							{
								continue;
							}
							for (int k = 0; k < other.Count; k += 2)
							{
								double cx = other.X[k], cz = other.Z[k];
								double d = Math.Sqrt((cx - x) * (cx - x) + (cz - z) * (cz - z));
								if (d < 60.0 || d > reach || (cx - fx) * (cx - fx) + (cz - fz) * (cz - fz) < 300.0 * 300.0)
								{
									continue;
								}
								// Ahead is cheapest; straight back costs three times the distance.
								double score = d * (2.0 - ((cx - x) * ax + (cz - z) * az) / d);
								double back = Math.Sqrt((cx - road.X[o]) * (cx - road.X[o]) + (cz - road.Z[o]) * (cz - road.Z[o]));
								if (score >= best || back < 250.0 || InTown(cx, cz))
								{
									continue;
								}
								best = score;
								dist = d;
								tx = cx;
								tz = cz;
							}
						}
						if (best == double.MaxValue)
						{
							break;
						}
						Road made = Walk(rng, x, z, road.Y[i], ahead, dist * 1.6 + 60.0, null, road.Kind, r, 0.12, tx, tz, smallest);
						if (made != null)
						{
							made.Name = road.Kind == RoadKind.Lane ? road.Name + " on" : made.Name;
							break;
						}
						fx = tx;
						fz = tz;
					}
				}
			}
		}

		/// <summary>Whether (x, z) is inside a town or village (not a hamlet or farm).</summary>
		private static bool InTown(double x, double z)
		{
			foreach (Places.Place p in Places.All)
			{
				double dx = x - p.X, dz = z - p.Z;
				if (p.Kind != PlaceKind.Hamlet && dx * dx + dz * dz < p.Radius * p.Radius)
				{
					return true;
				}
			}
			return false;
		}

		private static bool InPlace(double x, double z)
		{
			foreach (Places.Place p in Places.All)
			{
				double dx = x - p.X, dz = z - p.Z;
				if (dx * dx + dz * dz < (p.Radius + 100.0) * (p.Radius + 100.0))
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>One branch off road <paramref name="r"/> at sample <paramref name="i"/>, to the given side, at a right angle (give or take 12 degrees).</summary>
		private static Road Branch(Rng rng, Road parent, int r, int i, int side, double length, Places.Place place, RoadKind kind)
		{
			double tx = parent.X[i + 1] - parent.X[i - 1], tz = parent.Z[i + 1] - parent.Z[i - 1];
			if (tx * tx + tz * tz <= 0.0)
			{
				return null;
			}
			double angle = Atan2(-tx * side, tz * side) + rng.Range(-0.2, 0.2);
			return Walk(rng, parent.X[i], parent.Z[i], parent.Y[i], angle, length, place, kind, r, 0.18);
		}

		/// <summary>
		/// A new road walked out from (x, z) (on a road at height <paramref name="y"/>, or a place's centre): 30-unit steps, turning by up
		/// to <paramref name="wander"/> radians each step, stopped by the local rules (see <see cref="Grow"/>): it ends on a road it would
		/// cross, joins a road it comes near (not <paramref name="from"/>, the one it left, for its first steps), and stops before leaving
		/// the place, reaching water, or getting too steep. Added to the network if it came to 50 units or more.
		/// Steered (<paramref name="tx"/>, <paramref name="tz"/> given): it turns towards that point each step, never ends on the road it
		/// left, keeps out of towns and villages, and is kept only if it ends on a road of class <paramref name="smallest"/> or bigger.
		/// </summary>
		private static Road Walk(Rng rng, double x, double z, double y, double angle, double length, Places.Place place, RoadKind kind, int from, double wander, double tx = double.NaN, double tz = double.NaN, RoadKind smallest = RoadKind.Path)
		{
			bool steered = !double.IsNaN(tx);
			const double seg = 30.0;
			List<(double x, double z)> pts = new List<(double x, double z)> { (x, z) };
			double endPin = double.NaN;
			double walked = 0.0;
			double h = WorldGen.Base(x, z);
			while (walked < length)
			{
				angle += rng.Range(-wander, wander);
				if (steered)
				{
					double turn = Atan2(tz - z, tx - x) - angle;
					turn -= 2.0 * DetMath.Pi * Math.Floor((turn + DetMath.Pi) / (2.0 * DetMath.Pi));
					angle += Math.Max(-0.35, Math.Min(0.35, turn));
				}
				double nx = x + DetMath.Cos(angle) * seg, nz = z + DetMath.Sin(angle) * seg;
				if (steered && InTown(nx, nz))
				{
					break;
				}
				if (place != null)
				{
					double dx = nx - place.X, dz = nz - place.Z;
					if (dx * dx + dz * dz > place.Radius * place.Radius * 1.1)
					{
						break;
					}
				}
				if (!double.IsNaN(Rivers.SurfaceAt(nx, nz)) || Rivers.Distance(nx, nz, out double plain) < plain * 0.5 + 25.0 || Circuit.Fenced(nx, nz, 40.0) || Rally.Fenced(nx, nz, 60.0) || HillClimb.Fenced(nx, nz, HillClimb.Fence))
				{
					break;
				}
				double nh = WorldGen.Base(nx, nz);
				if (Math.Abs(nh - h) / seg > Classes[(int)kind].Grade * 1.3)
				{
					break;
				}
				bool first = pts.Count < 3;
				// Crossing a road: stop on it.
				if (segments.Intersect(x, z, nx, nz, first || steered ? from : -1, out double ix, out double iz, out double iy))
				{
					if (!first || from >= 0)
					{
						pts.Add((ix, iz));
						endPin = iy;
						break;
					}
				}
				// Near a road (not the one it left, at first): join it.
				if (!first && segments.Snap(nx, nz, 40.0, steered ? from : -1, out double sx, out double sz, out double sy))
				{
					pts.Add((sx, sz));
					endPin = sy;
					break;
				}
				pts.Add((nx, nz));
				x = nx;
				z = nz;
				h = nh;
				walked += seg;
			}
			if (pts.Count < 3 && double.IsNaN(endPin))
			{
				return null;
			}
			if (steered && (double.IsNaN(endPin) || Roads[segments.Hit].Kind > smallest))
			{
				return null;
			}
			Road road = Make(kind, kind == RoadKind.Track ? "Track" : kind == RoadKind.Path ? "Footpath" : (place != null ? place.Name + " street" : "Street"), Lines.Resample(Rounded(pts), Step));
			if (road.S[road.Count - 1] < 50.0)
			{
				return null;
			}
			// A steered way pinned at both ends to roads' heights must be able to climb between them at its grade (a short one between a
			// track in a hollow and a road on a bank was not).
			if (steered && Math.Abs(endPin - y) > Classes[(int)kind].Grade * 0.8 * road.S[road.Count - 1])
			{
				return null;
			}
			List<(int idx, double y)> pins = new List<(int, double)> { (0, y) };
			pins.Add((road.Count - 1, double.IsNaN(endPin) ? WorldGen.Base(road.X[road.Count - 1], road.Z[road.Count - 1]) : endPin));
			Add(road, pins);
			return road;
		}

		/// <summary>One round of corner cutting, ends kept: softens a walk's 30-unit steps.</summary>
		private static List<(double x, double z)> Rounded(List<(double x, double z)> pts)
		{
			List<(double x, double z)> next = new List<(double x, double z)> { pts[0] };
			for (int i = 0; i + 1 < pts.Count; i++)
			{
				(double x0, double z0) = pts[i];
				(double x1, double z1) = pts[i + 1];
				if (i > 0)
				{
					next.Add((x0 * 0.75 + x1 * 0.25, z0 * 0.75 + z1 * 0.25));
				}
				if (i + 2 < pts.Count)
				{
					next.Add((x0 * 0.25 + x1 * 0.75, z0 * 0.25 + z1 * 0.75));
				}
			}
			next.Add(pts[pts.Count - 1]);
			return next;
		}

		/// <summary>atan2 from plain arithmetic (a polynomial good to about 1e-5 radians), the same on every machine.</summary>
		internal static double Atan2(double y, double x)
		{
			double ax = Math.Abs(x), ay = Math.Abs(y);
			if (ax == 0.0 && ay == 0.0)
			{
				return 0.0;
			}
			double a = Math.Min(ax, ay) / Math.Max(ax, ay);
			double s = a * a;
			double r = ((-0.0464964749 * s + 0.15931422) * s - 0.327622764) * s * a + a;
			if (ay > ax)
			{
				r = DetMath.Pi / 2.0 - r;
			}
			if (x < 0.0)
			{
				r = DetMath.Pi - r;
			}
			return y < 0.0 ? -r : r;
		}

		/// <summary>
		/// The spawn on the town's road in (which runs from the town's middle out to the trunk road): the sample about the town's radius
		/// out, clear of bridges, in the right-hand lane facing the middle; the stretch round it is levelled for the grid.
		/// </summary>
		private static void SpawnOnLink(Road link, Places.Place town)
		{
			int best = -1;
			double bestOff = double.MaxValue;
			for (int k = 14; k < link.Count - 24; k++)
			{
				bool clear = true;
				for (int j = k - 14; j <= k + 22 && clear; j++)
				{
					clear = !link.Bridge[j];
				}
				double dx = link.X[k] - town.X, dz = link.Z[k] - town.Z;
				double off = Math.Abs(Math.Sqrt(dx * dx + dz * dz) - town.Radius * 1.05);
				if (clear && off < bestOff)
				{
					bestOff = off;
					best = k;
				}
			}
			if (best < 0)
			{
				return;
			}
			int i = best;
			// Towards the middle is towards the road's start.
			double dirX = link.X[i - 1] - link.X[i + 1], dirZ = link.Z[i - 1] - link.Z[i + 1];
			double d = Math.Sqrt(dirX * dirX + dirZ * dirZ);
			SpawnDirX = dirX / d;
			SpawnDirZ = dirZ / d;
			// Level from 12 samples ahead (towards the town) to 20 behind: the grid, the spawn and the start lights.
			int a = Math.Max(0, i - 12), b = Math.Min(link.Count - 1, i + 20);
			double level = link.Y[i];
			for (int k = a; k <= b; k++)
			{
				link.Y[k] = level;
			}
			Hold(link, Classes[(int)RoadKind.Primary].Grade, 0, link.Count - 1);
			// The right-hand lane's centre is 7 right of the middle; the other car of each pair goes in the other lane, 14 to the left.
			SpawnX = link.X[i] + SpawnDirZ * 7.0;
			SpawnZ = link.Z[i] - SpawnDirX * 7.0;
			SpawnY = level;
			SpawnPair = -14.0;
		}

		/// <summary>The spawn: on the trunk road where it passes closest to the main town, in the inner eastbound lane, on a level stretch.</summary>
		private static void Spawn(Road trunk, Places.Place town)
		{
			int i = trunk.Count / 2;
			if (town != null)
			{
				double best = double.MaxValue;
				for (int k = 60; k < trunk.Count - 60; k++)
				{
					double dx = trunk.X[k] - town.X, dz = trunk.Z[k] - town.Z;
					if (dx * dx + dz * dz < best && !trunk.Bridge[k])
					{
						best = dx * dx + dz * dz;
						i = k;
					}
				}
				// A little before the closest point, so the junction to the town is just ahead.
				i = Math.Max(30, i - 40);
			}
			double dirX = trunk.X[i + 1] - trunk.X[i - 1], dirZ = trunk.Z[i + 1] - trunk.Z[i - 1];
			double d = Math.Sqrt(dirX * dirX + dirZ * dirZ);
			SpawnDirX = dirX / d;
			SpawnDirZ = dirZ / d;
			// Dead level from 20 samples behind to 12 ahead: the grid, the spawn and the start lights. The Multiplayer mod puts every car
			// at one fixed height on the grid, which must not be under the road anywhere along it.
			int a = Math.Max(0, i - 20), b = Math.Min(trunk.Count - 1, i + 12);
			double level = trunk.Y[i];
			for (int k = a; k <= b; k++)
			{
				trunk.Y[k] = level;
				trunk.Bridge[k] = false;
				trunk.Over[k] = false;
			}
			Hold(trunk, Classes[0].Grade, 0, trunk.Count - 1);
			// Right of the direction of travel: (dz, -dx). The inner lane's centre is the median (5) plus its shoulder (2) plus half a lane.
			const double lane = 5.0 + 2.0 + 5.5;
			SpawnX = trunk.X[i] + SpawnDirZ * lane;
			SpawnZ = trunk.Z[i] - SpawnDirX * lane;
			SpawnY = level;
		}

		// ---- the ground --------------------------------------------------------------------------------------------

		private const double Cell = 128.0;
		private const int Cells = 260;
		private const double Origin0 = -Cells * Cell / 2.0;
		private static int[] cellStart;
		private static long[] cellItems;

		/// <summary>The widest bank (cut or fill) any road has, plus its shoulder: how far a road's bed can reach.</summary>
		private static double Reach(Road road)
		{
			return road.Half + Shoulder + Classes[(int)road.Kind].Bank;
		}

		/// <summary>Each road segment is listed in every 128-unit cell its bank can reach, so <see cref="Ground"/> only looks at the few near a point.</summary>
		private static void Index()
		{
			int[] counts = new int[Cells * Cells + 1];
			for (int pass = 0; pass < 2; pass++)
			{
				if (pass == 1)
				{
					cellStart = new int[Cells * Cells + 1];
					for (int c = 0; c < Cells * Cells; c++)
					{
						cellStart[c + 1] = cellStart[c] + counts[c];
					}
					cellItems = new long[cellStart[Cells * Cells]];
					Array.Clear(counts, 0, counts.Length);
				}
				for (int r = 0; r < Roads.Count; r++)
				{
					Road road = Roads[r];
					double reach = Reach(road);
					for (int i = 0; i + 1 < road.Count; i++)
					{
						// Stretches cut out at a junction leave the ground alone: the other road's bed is the ground there. Bridges are listed
						// (they may cut a bank, never fill: see Ground) but other lookups pass over them (Deck).
						if (Hidden(road, i))
						{
							continue;
						}
						int c0 = CellOf(Math.Min(road.X[i], road.X[i + 1]) - reach);
						int c1 = CellOf(Math.Max(road.X[i], road.X[i + 1]) + reach);
						int r0 = CellOf(Math.Min(road.Z[i], road.Z[i + 1]) - reach);
						int r1 = CellOf(Math.Max(road.Z[i], road.Z[i + 1]) + reach);
						for (int cz = r0; cz <= r1; cz++)
						{
							for (int cx = c0; cx <= c1; cx++)
							{
								int c = cz * Cells + cx;
								if (pass == 1)
								{
									cellItems[cellStart[c] + counts[c]] = ((long)r << 32) | (uint)i;
								}
								counts[c]++;
							}
						}
					}
				}
			}
		}

		private static int CellOf(double v)
		{
			return Math.Max(0, Math.Min(Cells - 1, (int)Math.Floor((v - Origin0) / Cell)));
		}

		/// <summary>Whether piece i of the road is on a bridge's deck (both ends): it only ever cuts the ground, and lookups other than <see cref="Ground"/> pass over it.</summary>
		private static bool Deck(Road road, int i)
		{
			return road.Bridge[i] && road.Bridge[i + 1];
		}

		/// <summary>Whether piece i of the road is wholly in a cut (not drawn at all).</summary>
		private static bool Hidden(Road road, int i)
		{
			foreach (Gap g in road.Gaps)
			{
				if (road.S[i] >= Math.Min(g.L0, g.R0) && road.S[i + 1] <= Math.Max(g.L1, g.R1))
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>
		/// The ground at (x, z) given the land there (<paramref name="land"/>), from sea level: the bed of the nearest road where a road
		/// is, and a bank blending back to the land beside it, wider where the road is far above or below the land (up to the class's
		/// limit), so cuts and embankments are about 1 in 1.6. Where two roads' banks overlap, the one that claims the point most wins.
		/// A farm track or footpath presses a shallow bed into the ground, flat a little past its ribbon, with soft edges; where such
		/// beds overlap the lowest is taken. A bridge never fills the ground, so the river runs under it, but it cuts a bank that stands
		/// above its deck (a road in a cutting crossing a river in a gorge: the bank tops stood 3 over the deck's ends at
		/// (208, -2517)); the banks by a bridge never fill the river's channel under it.
		/// </summary>
		internal static double Ground(double x, double z, double land)
		{
			if (cellStart == null)
			{
				return land;
			}
			int c = CellOf(z) * Cells + CellOf(x);
			double bestW = 0.0;
			double bestD = double.MaxValue;
			double bestY = land;
			bool bestAbut = false;
			bool bestUnpaved = false;
			double paved = double.MaxValue;
			double unpaved = double.MaxValue;
			int road1 = -1;
			double near1 = double.MaxValue, y1 = 0.0;
			// A cell lists each road's segments together, so the nearest segment of each road is tracked as they go by.
			int road0 = -1;
			double near0 = double.MaxValue, y0 = 0.0, half0 = 0.0;
			for (int k = cellStart[c]; k < cellStart[c + 1]; k++)
			{
				long item = cellItems[k];
				int r = (int)(item >> 32);
				Road road = Roads[r];
				int i = (int)(uint)item;
				bool track = Unpaved(road.Kind);
				Lines.Project(road.X, road.Z, i, x, z, out double t, out double d);
				// Of a piece a cut runs into, only the drawn part makes a bed.
				if (t < road.From[i] || t > road.To[i])
				{
					t = Math.Max(road.From[i], Math.Min(road.To[i], t));
					double px = road.X[i] + (road.X[i + 1] - road.X[i]) * t - x, pz = road.Z[i] + (road.Z[i + 1] - road.Z[i]) * t - z;
					d = Math.Sqrt(px * px + pz * pz);
				}
				double ry = road.Y[i] + (road.Y[i + 1] - road.Y[i]) * t - (track ? TrackDepth : BedDepth);
				// A mouth's cross-fall carries on into its bed and shoulders.
				double tilt = road.Tilt == null ? 0.0 : road.Tilt[i] + (road.Tilt[i + 1] - road.Tilt[i]) * t;
				if (tilt != 0.0)
				{
					double sx = road.X[i + 1] - road.X[i], sz = road.Z[i + 1] - road.Z[i];
					double sl = Math.Sqrt(sx * sx + sz * sz);
					double across = sl > 0.0 ? ((z - road.Z[i]) * sx - (x - road.X[i]) * sz) / sl : 0.0;
					double most0 = road.Half + Shoulder;
					ry += tilt * Math.Max(-most0, Math.Min(most0, across));
				}
				if (r != road0)
				{
					if (near0 < half0 + 1.5)
					{
						paved = Math.Min(paved, y0);
					}
					road0 = r;
					near0 = double.MaxValue;
				}
				// A track has no paving to keep the ground under.
				if (d < near0 && !track)
				{
					near0 = d;
					y0 = ry;
					half0 = road.Half;
				}
				// A deck over lower ground (the water, the river's banks below it) leaves that ground alone; the paving cap above still
				// lowers any bank under it that stands higher.
				if (Deck(road, i) && ry >= land)
				{
					continue;
				}
				double inner = road.Half + (track ? EdgeOf(road.Kind) + UnpavedFlat : Shoulder);
				// Where tracks and paths meet, their flat beds overlap: the ground takes the lowest (each road's by its nearest segment),
				// so every ribbon is over it.
				if (track && d < inner)
				{
					if (r != road1)
					{
						if (road1 >= 0)
						{
							unpaved = Math.Min(unpaved, y1);
						}
						road1 = r;
						near1 = double.MaxValue;
					}
					if (d < near1)
					{
						near1 = d;
						y1 = ry;
					}
				}
				double most = Classes[(int)road.Kind].Bank;
				if (d >= inner + most)
				{
					continue;
				}
				double bank = track ? Math.Max(4.0, Math.Min(most, Math.Abs(land - ry) * 1.6 + 2.0)) : Math.Max(12.0, Math.Min(most, Math.Abs(land - ry) * 1.6 + 8.0));
				double w = 1.0 - Noise.Step(inner, inner + bank, d);
				// Near a road several of its segments claim a point fully; the nearest one has the right height (the others' ends are a
				// little further along the road, and so a little higher or lower on a grade).
				if (w > bestW + 1e-9 || (w > bestW - 1e-9 && d < bestD))
				{
					bestW = w;
					bestD = d;
					bestY = ry;
					bestAbut = road.Abut != null && (road.Abut[i] || road.Abut[i + 1]);
					bestUnpaved = track;
				}
			}
			// Under any road's paving (each road by its nearest segment) the ground stays below it, whichever road claims the point.
			if (near0 < half0 + 1.5)
			{
				paved = Math.Min(paved, y0);
			}
			if (road1 >= 0)
			{
				unpaved = Math.Min(unpaved, y1);
			}
			// One road's own bed is the ground already; only where two or more overlap does the lowest matter.
			double ground = Math.Min(land + (bestY - land) * bestW, paved);
			if (unpaved < ground && bestUnpaved && bestW >= 1.0 - 1e-9)
			{
				ground = unpaved;
			}
			// The embankment up to a bridge stops at the water: the river keeps its channel under the deck.
			if (bestAbut && ground > land && Rivers.Channel(x, z, 2.0, out double _))
			{
				return land;
			}
			return ground;
		}

		/// <summary>
		/// Distance from (x, z) to the nearest road of class <paramref name="kind"/> or smaller (and <paramref name="smallest"/> or
		/// bigger), up to <paramref name="most"/>.
		/// </summary>
		internal static double Nearest(double x, double z, double most, RoadKind kind, RoadKind smallest = RoadKind.Path)
		{
			if (cellStart == null)
			{
				return most;
			}
			int c = CellOf(z) * Cells + CellOf(x);
			double best = most;
			for (int k = cellStart[c]; k < cellStart[c + 1]; k++)
			{
				long item = cellItems[k];
				Road road = Roads[(int)(item >> 32)];
				if (road.Kind < kind || road.Kind > smallest)
				{
					continue;
				}
				int i = (int)(uint)item;
				if (Deck(road, i) || Math.Abs(road.X[i] - x) > best + 10.0 || Math.Abs(road.Z[i] - z) > best + 10.0)
				{
					continue;
				}
				Lines.Project(road.X, road.Z, i, x, z, out double _, out double d);
				best = Math.Min(best, d);
			}
			return best;
		}

		/// <summary>
		/// For each of a tile's <paramref name="pixels"/> x <paramref name="pixels"/> pixels, how far its centre is from the nearest road's
		/// edge, counting a paved road's verge and a track's or path's soft edges as the road (up to 20; negative on a road), from the
		/// road pieces of the tile and its neighbours. For keeping trees and grass off the roads without asking every pixel.
		/// </summary>
		internal static float[] Clearance(int tx, int tz, float tileSize, int pixels)
		{
			float[] clear = new float[pixels * pixels];
			for (int k = 0; k < clear.Length; k++)
			{
				clear[k] = 20f;
			}
			double ox = tx * (double)tileSize, oz = tz * (double)tileSize;
			double size = tileSize / (double)pixels;
			for (int j = tz - 1; j <= tz + 1; j++)
			{
				for (int i = tx - 1; i <= tx + 1; i++)
				{
					List<long> list = PiecesIn(i, j, tileSize);
					if (list == null)
					{
						continue;
					}
					foreach (long piece in list)
					{
						Road road = Roads[(int)(piece >> 32)];
						int k = (int)(uint)piece;
						double width = road.Half + (Unpaved(road.Kind) ? EdgeOf(road.Kind) : Verge);
						double reach = width + 20.0;
						int p0 = Math.Max(0, (int)Math.Floor((Math.Min(road.X[k], road.X[k + 1]) - reach - ox) / size));
						int p1 = Math.Min(pixels - 1, (int)Math.Floor((Math.Max(road.X[k], road.X[k + 1]) + reach - ox) / size));
						int q0 = Math.Max(0, (int)Math.Floor((Math.Min(road.Z[k], road.Z[k + 1]) - reach - oz) / size));
						int q1 = Math.Min(pixels - 1, (int)Math.Floor((Math.Max(road.Z[k], road.Z[k + 1]) + reach - oz) / size));
						for (int q = q0; q <= q1; q++)
						{
							for (int p = p0; p <= p1; p++)
							{
								Lines.Project(road.X, road.Z, k, ox + (p + 0.5) * size, oz + (q + 0.5) * size, out double _, out double d);
								float edge = (float)(d - width);
								if (edge < clear[q * pixels + p])
								{
									clear[q * pixels + p] = edge;
								}
							}
						}
					}
				}
			}
			return clear;
		}

		/// <summary>
		/// Whether (x, z) is on the drawn paving of a road other than <paramref name="self"/> (a little past its edge counts): verges are
		/// left out there, so a verge never lies on a road it meets.
		/// </summary>
		internal static bool OnOtherPaving(double x, double z, int self)
		{
			if (cellStart == null)
			{
				return false;
			}
			int c = CellOf(z) * Cells + CellOf(x);
			for (int k = cellStart[c]; k < cellStart[c + 1]; k++)
			{
				long item = cellItems[k];
				int r = (int)(item >> 32);
				Road road = Roads[r];
				if (r == self || Unpaved(road.Kind))
				{
					continue;
				}
				int i = (int)(uint)item;
				if (Deck(road, i) || Math.Abs(road.X[i] - x) > road.Half + Flare + 12.0 || Math.Abs(road.Z[i] - z) > road.Half + Flare + 12.0)
				{
					continue;
				}
				Lines.Project(road.X, road.Z, i, x, z, out double t, out double d);
				double s = road.S[i] + (road.S[i + 1] - road.S[i]) * t;
				if (d < HalfAt(road, s) + 0.5 && Shown(road, s))
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>
		/// Whether (x, z) may be built on for road <paramref name="own"/> (-1: for no road): at least <paramref name="margin"/> off every
		/// road's edge, and no other road's edge nearer than <paramref name="ownEdge"/> (its distance from its own road's edge), so each
		/// road builds only on its own side of a block; road <paramref name="also"/> (-1: none) may be nearer.
		/// </summary>
		internal static bool Claims(double x, double z, int own, double ownEdge, double margin, int also = -1)
		{
			if (cellStart == null)
			{
				return true;
			}
			int c = CellOf(z) * Cells + CellOf(x);
			for (int k = cellStart[c]; k < cellStart[c + 1]; k++)
			{
				long item = cellItems[k];
				int r = (int)(item >> 32);
				Road road = Roads[r];
				int i = (int)(uint)item;
				if (Deck(road, i))
				{
					continue;
				}
				Lines.Project(road.X, road.Z, i, x, z, out double _, out double d);
				double edge = d - road.Half;
				if (edge < margin || (own >= 0 && r != own && r != also && edge < ownEdge - 0.5))
				{
					return false;
				}
			}
			return true;
		}

		/// <summary>
		/// The nearest point on a road (not on a bridge) to (x, z), looked for in the tiles round it, and the road's direction there. For
		/// putting a car back on dry land.
		/// </summary>
		internal static bool NearestRoad(double x, double z, float tileSize, out double rx, out double rz, out double dx, out double dz)
		{
			rx = rz = dx = dz = 0.0;
			int tx = (int)Math.Floor(x / tileSize), tz = (int)Math.Floor(z / tileSize);
			for (int reach = 1; reach <= 8; reach *= 2)
			{
				double best = double.MaxValue;
				for (int j = tz - reach; j <= tz + reach; j++)
				{
					for (int i = tx - reach; i <= tx + reach; i++)
					{
						List<long> list = PiecesIn(i, j, tileSize);
						if (list == null)
						{
							continue;
						}
						foreach (long piece in list)
						{
							Road road = Roads[(int)(piece >> 32)];
							int k = (int)(uint)piece;
							if (road.Bridge[k] || Unpaved(road.Kind))
							{
								continue;
							}
							Lines.Project(road.X, road.Z, k, x, z, out double t, out double d);
							if (d < best)
							{
								best = d;
								rx = road.X[k] + (road.X[k + 1] - road.X[k]) * t;
								rz = road.Z[k] + (road.Z[k + 1] - road.Z[k]) * t;
								double len = Math.Max(1e-9, road.S[k + 1] - road.S[k]);
								dx = (road.X[k + 1] - road.X[k]) / len;
								dz = (road.Z[k + 1] - road.Z[k]) / len;
							}
						}
					}
				}
				if (best < double.MaxValue)
				{
					return true;
				}
			}
			return false;
		}

		// ---- tiles -------------------------------------------------------------------------------------------------

		private static Dictionary<long, List<long>> pieces;
		private static readonly object piecesGate = new object();

		/// <summary>
		/// The road pieces (road, sample i to i + 1) whose middle is in tile (tx, tz) of side <paramref name="tileSize"/>. Each piece is
		/// drawn by exactly one tile, and neighbouring pieces share their end points, so the road is seamless across tile edges.
		/// </summary>
		internal static List<long> PiecesIn(int tx, int tz, float tileSize)
		{
			Ensure();
			lock (piecesGate)
			{
				if (pieces == null)
				{
					pieces = new Dictionary<long, List<long>>();
					for (int r = 0; r < Roads.Count; r++)
					{
						Road road = Roads[r];
						for (int i = 0; i + 1 < road.Count; i++)
						{
							int px = (int)Math.Floor((road.X[i] + road.X[i + 1]) * 0.5 / tileSize);
							int pz = (int)Math.Floor((road.Z[i] + road.Z[i + 1]) * 0.5 / tileSize);
							long key = ((long)px << 32) | (uint)pz;
							if (!pieces.TryGetValue(key, out List<long> list))
							{
								list = new List<long>();
								pieces[key] = list;
							}
							list.Add(((long)r << 32) | (uint)i);
						}
					}
				}
				return pieces.TryGetValue(((long)tx << 32) | (uint)tz, out List<long> found) ? found : null;
			}
		}

		// ---- the segments, while building --------------------------------------------------------------------------

		/// <summary>
		/// The segments of every road built so far, by 64-unit cell (a segment is listed under both its ends' cells; segments are 8
		/// long), for snapping a new road onto the network and finding where it crosses.
		/// </summary>
		private sealed class Segments
		{
			private const double Size = 64.0;
			private readonly Dictionary<long, List<long>> grid = new Dictionary<long, List<long>>();

			/// <summary>The road the last <see cref="Snap"/> or <see cref="Intersect"/> that found one found (for checking its class).</summary>
			internal int Hit = -1;

			private static long Key(int i, int j)
			{
				return ((long)i << 32) | (uint)j;
			}

			private static int Cell(double v)
			{
				return (int)Math.Floor(v / Size);
			}

			internal void Add(int r, Road road)
			{
				for (int i = 0; i + 1 < road.Count; i++)
				{
					long k0 = Key(Cell(road.X[i]), Cell(road.Z[i]));
					long k1 = Key(Cell(road.X[i + 1]), Cell(road.Z[i + 1]));
					Put(k0, ((long)r << 32) | (uint)i);
					if (k1 != k0)
					{
						Put(k1, ((long)r << 32) | (uint)i);
					}
				}
			}

			/// <summary>Road r was replaced by a longer copy (an extension at an edge): its entries are rebuilt.</summary>
			internal void Replace(int r, Road old, Road longer)
			{
				foreach (List<long> list in grid.Values)
				{
					list.RemoveAll(item => (int)(item >> 32) == r);
				}
				Add(r, longer);
			}

			private void Put(long key, long item)
			{
				if (!grid.TryGetValue(key, out List<long> list))
				{
					list = new List<long>();
					grid[key] = list;
				}
				list.Add(item);
			}

			/// <summary>Every road with a segment within <paramref name="radius"/> of (x, z) (by cells), each once, lowest index first.</summary>
			internal List<int> RoadsNear(double x, double z, double radius)
			{
				SortedSet<int> found = new SortedSet<int>();
				for (int j = Cell(z - radius); j <= Cell(z + radius); j++)
				{
					for (int i = Cell(x - radius); i <= Cell(x + radius); i++)
					{
						if (grid.TryGetValue(Key(i, j), out List<long> list))
						{
							foreach (long item in list)
							{
								found.Add((int)(item >> 32));
							}
						}
					}
				}
				return new List<int>(found);
			}

			/// <summary>The nearest point within <paramref name="radius"/> on any road (except road <paramref name="skip"/>), with the road's height there.</summary>
			internal bool Snap(double x, double z, double radius, int skip, out double px, out double pz, out double py)
			{
				px = pz = py = 0.0;
				double best = radius;
				bool found = false;
				for (int j = Cell(z - radius); j <= Cell(z + radius); j++)
				{
					for (int i = Cell(x - radius); i <= Cell(x + radius); i++)
					{
						if (!grid.TryGetValue(Key(i, j), out List<long> list))
						{
							continue;
						}
						foreach (long item in list)
						{
							int r = (int)(item >> 32);
							if (r == skip)
							{
								continue;
							}
							Road road = Roads[r];
							int k = (int)(uint)item;
							Lines.Project(road.X, road.Z, k, x, z, out double t, out double d);
							if (d < best)
							{
								best = d;
								px = road.X[k] + (road.X[k + 1] - road.X[k]) * t;
								pz = road.Z[k] + (road.Z[k + 1] - road.Z[k]) * t;
								py = road.Y[k] + (road.Y[k + 1] - road.Y[k]) * t;
								found = true;
								Hit = r;
							}
						}
					}
				}
				return found;
			}

			/// <summary>
			/// If (x, z) is on the paving of a road already built (within its half width plus <paramref name="margin"/>), that road's
			/// height there: the biggest such road (it is the one drawn on top), then the one the point is furthest inside.
			/// </summary>
			internal bool Under(double x, double z, double margin, out double y)
			{
				y = 0.0;
				double best = double.MaxValue;
				int bestKind = int.MaxValue;
				bool found = false;
				for (int j = Cell(z - 40.0); j <= Cell(z + 40.0); j++)
				{
					for (int i = Cell(x - 40.0); i <= Cell(x + 40.0); i++)
					{
						if (!grid.TryGetValue(Key(i, j), out List<long> list))
						{
							continue;
						}
						foreach (long item in list)
						{
							Road road = Roads[(int)(item >> 32)];
							int k = (int)(uint)item;
							Lines.Project(road.X, road.Z, k, x, z, out double t, out double d);
							double inside = d - road.Half - margin;
							if (inside < 0.0 && ((int)road.Kind < bestKind || ((int)road.Kind == bestKind && inside < best)))
							{
								best = inside;
								bestKind = (int)road.Kind;
								y = road.Y[k] + (road.Y[k + 1] - road.Y[k]) * t;
								found = true;
							}
						}
					}
				}
				return found;
			}

			/// <summary>
			/// The road (not <paramref name="skip"/>) whose paving (x, z) is on, within its half width less <paramref name="inset"/> and
			/// past neither of its ends (its ribbon ends square, with no round cap): of class <paramref name="kind"/> or bigger, and if of
			/// that class then built before road <paramref name="before"/>; the biggest, then the one it is furthest inside. -1 for none.
			/// </summary>
			internal int Paving(double x, double z, int skip, RoadKind kind, int before, double inset, out double y)
			{
				y = 0.0;
				int found = -1;
				double best = double.MaxValue;
				int bestKind = int.MaxValue;
				double reach = 40.0 - Math.Min(0.0, inset);
				for (int j = Cell(z - reach); j <= Cell(z + reach); j++)
				{
					for (int i = Cell(x - reach); i <= Cell(x + reach); i++)
					{
						if (!grid.TryGetValue(Key(i, j), out List<long> list))
						{
							continue;
						}
						foreach (long item in list)
						{
							int r = (int)(item >> 32);
							Road road = Roads[r];
							if (r == skip || road.Kind > kind || (road.Kind == kind && r >= before) || Unpaved(road.Kind))
							{
								continue;
							}
							int k = (int)(uint)item;
							if (!Across(road, k, x, z, out double t, out double d))
							{
								continue;
							}
							double inside = d - (road.Half - inset);
							if (inside < 0.0 && ((int)road.Kind < bestKind || ((int)road.Kind == bestKind && inside < best)))
							{
								best = inside;
								bestKind = (int)road.Kind;
								y = road.Y[k] + (road.Y[k + 1] - road.Y[k]) * t;
								found = r;
							}
						}
					}
				}
				return found;
			}

			/// <summary>The nearest point of road <paramref name="r"/> to (x, z) within <paramref name="reach"/>: its distance, and its segment and fraction along it.</summary>
			internal bool Nearest(int r, double x, double z, double reach, out double d, out int segment, out double along)
			{
				Road road = Roads[r];
				d = double.MaxValue;
				segment = 0;
				along = 0.0;
				for (int j = Cell(z - reach); j <= Cell(z + reach); j++)
				{
					for (int i = Cell(x - reach); i <= Cell(x + reach); i++)
					{
						if (!grid.TryGetValue(Key(i, j), out List<long> list))
						{
							continue;
						}
						foreach (long item in list)
						{
							if ((int)(item >> 32) != r)
							{
								continue;
							}
							int k = (int)(uint)item;
							Lines.Project(road.X, road.Z, k, x, z, out double t, out double dist);
							if (dist < d)
							{
								d = dist;
								segment = k;
								along = t;
							}
						}
					}
				}
				return d <= reach;
			}

			/// <summary>Where (x, z) projects onto segment k of the road, false if beyond the road's first or last sample.</summary>
			private static bool Across(Road road, int k, double x, double z, out double t, out double d)
			{
				double ax = road.X[k], az = road.Z[k];
				double sx = road.X[k + 1] - ax, sz = road.Z[k + 1] - az;
				double len2 = sx * sx + sz * sz;
				t = len2 > 0.0 ? ((x - ax) * sx + (z - az) * sz) / len2 : 0.0;
				d = 0.0;
				if ((k == 0 && t < 0.0) || (k == road.Count - 2 && t > 1.0))
				{
					return false;
				}
				t = t < 0.0 ? 0.0 : (t > 1.0 ? 1.0 : t);
				double px = ax + sx * t - x, pz = az + sz * t - z;
				d = Math.Sqrt(px * px + pz * pz);
				return true;
			}

			/// <summary>Where segment (ax, az)-(bx, bz) first crosses a road (except road <paramref name="skip"/>), with that road's height there.</summary>
			internal bool Intersect(double ax, double az, double bx, double bz, int skip, out double ix, out double iz, out double iy)
			{
				ix = iz = iy = 0.0;
				double bestT = 2.0;
				for (int j = Math.Min(Cell(az), Cell(bz)); j <= Math.Max(Cell(az), Cell(bz)); j++)
				{
					for (int i = Math.Min(Cell(ax), Cell(bx)); i <= Math.Max(Cell(ax), Cell(bx)); i++)
					{
						if (!grid.TryGetValue(Key(i, j), out List<long> list))
						{
							continue;
						}
						foreach (long item in list)
						{
							int r = (int)(item >> 32);
							if (r == skip)
							{
								continue;
							}
							Road road = Roads[r];
							int k = (int)(uint)item;
							if (Cross(ax, az, bx, bz, road.X[k], road.Z[k], road.X[k + 1], road.Z[k + 1], out double t, out double u) && t > 1e-6 && t < bestT)
							{
								bestT = t;
								ix = ax + (bx - ax) * t;
								iz = az + (bz - az) * t;
								iy = road.Y[k] + (road.Y[k + 1] - road.Y[k]) * u;
								Hit = r;
							}
						}
					}
				}
				return bestT <= 1.0;
			}

			/// <summary>
			/// Where the new road crosses roads already built, it is pinned to their height there, so the two meet level. Pins closer than
			/// 12 samples to one already pinned are skipped.
			/// </summary>
			internal void Crossings(Road road, List<(int idx, double y)> pins)
			{
				for (int i = 0; i + 1 < road.Count; i++)
				{
					if (Intersect(road.X[i], road.Z[i], road.X[i + 1], road.Z[i + 1], -1, out double _, out double _, out double iy))
					{
						bool close = false;
						foreach ((int idx, double _) in pins)
						{
							if (Math.Abs(idx - i) < 12)
							{
								close = true;
								break;
							}
						}
						if (!close)
						{
							pins.Add((i, iy));
						}
					}
				}
				pins.Sort((p, q) => p.idx.CompareTo(q.idx));
			}

			private static bool Cross(double ax, double az, double bx, double bz, double cx, double cz, double dx, double dz, out double t, out double u)
			{
				t = u = 0.0;
				double den = (bx - ax) * (dz - cz) - (bz - az) * (dx - cx);
				if (den == 0.0)
				{
					return false;
				}
				t = ((cx - ax) * (dz - cz) - (cz - az) * (dx - cx)) / den;
				u = ((cx - ax) * (bz - az) - (cz - az) * (bx - ax)) / den;
				return t >= 0.0 && t <= 1.0 && u >= 0.0 && u <= 1.0;
			}
		}

		// ---- routing -----------------------------------------------------------------------------------------------

		/// <summary>
		/// A* (or Dijkstra, to reach the nearest road) over a 100-unit grid of <see cref="WorldGen.Base"/>. A step costs its length times
		/// (1 + steepness (grade / soft)^2) for the road's class; water is closed, and so is the edge wall; crossing a river costs a
		/// bridge (dearer for small roads); stepping along a road of the same class or bigger costs a third, so roads share the way; and
		/// the trunk road pays to go through a town (it bypasses them). Ties are broken by cell number, so the result is the same on
		/// every machine.
		/// </summary>
		private sealed class Router
		{
			private const double Size = 100.0;
			private const int N = 320;
			private const double Min = -N * Size / 2.0;
			private readonly double[] h = new double[N * N];
			private readonly bool[] closed = new bool[N * N];
			private readonly double[] river = new double[N * N];
			private readonly bool[] core = new bool[N * N];
			/// <summary>The biggest road class through each cell (smallest number), or 255.</summary>
			private readonly byte[] road = new byte[N * N];

			internal Router()
			{
				double limit = WorldGen.Half - WorldGen.WallDepth - 300.0;
				for (int c = 0; c < N * N; c++)
				{
					double x = X(c), z = Z(c);
					h[c] = WorldGen.Base(x, z);
					closed[c] = Math.Abs(x) > limit || z > limit || !double.IsNaN(Rivers.SurfaceAt(x, z)) || Circuit.Fenced(x, z, 60.0) || Rally.Fenced(x, z, 150.0) || HillClimb.Fenced(x, z, HillClimb.Fence);
					road[c] = 255;
					foreach (Places.Place p in Places.All)
					{
						if (p.Kind == PlaceKind.Town && (p.X - x) * (p.X - x) + (p.Z - z) * (p.Z - z) < p.Radius * p.Radius * 0.6)
						{
							core[c] = true;
						}
					}
				}
				foreach (Rivers.River r in Rivers.All)
				{
					for (int i = 0; i < r.Count; i++)
					{
						int c = Index(r.X[i], r.Z[i]);
						if (c >= 0)
						{
							river[c] = Math.Max(river[c], r.Half[i]);
						}
					}
				}
			}

			internal double X(int c)
			{
				return Min + (c % N + 0.5) * Size;
			}

			internal double Z(int c)
			{
				return Min + (c / N + 0.5) * Size;
			}

			internal bool OnRoad(int c)
			{
				return road[c] != 255;
			}

			private static int Index(double x, double z)
			{
				int i = (int)Math.Floor((x - Min) / Size);
				int j = (int)Math.Floor((z - Min) / Size);
				return i < 0 || j < 0 || i >= N || j >= N ? -1 : j * N + i;
			}

			/// <summary>Marks cell c as on a road of class <paramref name="kind"/>.</summary>
			internal void Mark(int c, RoadKind kind)
			{
				road[c] = (byte)Math.Min(road[c], (int)kind);
			}

			/// <summary>Marks the cells a built road runs through with its class.</summary>
			internal void Mark(Road built)
			{
				for (int i = 0; i < built.Count; i++)
				{
					int c = Index(built.X[i], built.Z[i]);
					if (c >= 0)
					{
						road[c] = (byte)Math.Min(road[c], (int)built.Kind);
					}
				}
			}

			/// <summary>
			/// The cells from (ax, az) to (bx, bz); or, with <paramref name="joins"/> 0 or more, to the nearest cell with a road of that
			/// class or bigger. Null if there is no way.
			/// </summary>
			internal List<int> Find(RoadKind kind, double ax, double az, double bx, double bz, int joins)
			{
				int start = Index(ax, az);
				int goal = joins >= 0 ? -1 : Index(bx, bz);
				if (start < 0 || (joins < 0 && goal < 0))
				{
					return null;
				}
				Class cl = Classes[(int)kind];
				double[] g = new double[N * N];
				int[] from = new int[N * N];
				for (int k = 0; k < g.Length; k++)
				{
					g[k] = double.MaxValue;
					from[k] = -1;
				}
				Heap open = new Heap();
				g[start] = 0.0;
				open.Push(Guess(start, goal), start);
				int reached = -1;
				while (open.Count > 0)
				{
					int cur = open.Pop(out double f);
					if (f > g[cur] + Guess(cur, goal) + 1e-9)
					{
						continue;
					}
					if (cur == goal || (joins >= 0 && cur != start && road[cur] <= joins))
					{
						reached = cur;
						break;
					}
					int ci = cur % N, cj = cur / N;
					for (int dj = -1; dj <= 1; dj++)
					{
						for (int di = -1; di <= 1; di++)
						{
							if (di == 0 && dj == 0)
							{
								continue;
							}
							int ni = ci + di, nj = cj + dj;
							if (ni < 0 || nj < 0 || ni >= N || nj >= N)
							{
								continue;
							}
							int next = nj * N + ni;
							if (closed[next] && next != goal)
							{
								continue;
							}
							double len = (di != 0 && dj != 0) ? Size * 1.4142135623730951 : Size;
							double cost;
							if (road[next] <= (int)kind && road[cur] <= (int)kind)
							{
								cost = len * 0.33;
							}
							else
							{
								double grade = Math.Abs(h[next] - h[cur]) / len / cl.Soft;
								cost = len * (1.0 + cl.Steep * grade * grade);
								if (river[next] > 0.0 && road[next] == 255)
								{
									cost += len * (3.0 + river[next] / 4.0) * (kind >= RoadKind.Lane ? 3.0 : 1.0);
								}
								if (kind == RoadKind.Trunk && core[next])
								{
									cost += len * 3.0;
								}
							}
							double total = g[cur] + cost;
							if (total < g[next])
							{
								g[next] = total;
								from[next] = cur;
								open.Push(total + Guess(next, goal), next);
							}
						}
					}
				}
				if (reached < 0)
				{
					return null;
				}
				List<int> path = new List<int>();
				for (int c = reached; c >= 0; c = from[c])
				{
					path.Add(c);
					if (c == start)
					{
						break;
					}
				}
				path.Reverse();
				return path;
			}

			private double Guess(int a, int b)
			{
				if (b < 0)
				{
					return 0.0;
				}
				double dx = (a % N - b % N) * Size;
				double dz = (a / N - b / N) * Size;
				return Math.Sqrt(dx * dx + dz * dz) * 0.33;
			}
		}
	}
}
