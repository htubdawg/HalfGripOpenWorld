using System;
using System.Collections.Generic;

namespace HalfGripOpenWorld
{
	/// <summary>
	/// Where a deer could live, fixed with the map: places along the quiet roads (country lanes, now and then a secondary road, never
	/// the main roads) where woods meet open ground beside the road, the edges deer live on, and the road can be crossed there (no
	/// bridge, no junction, no other road near, no house near, no water or drop across it). Which of them get herds is drawn each race
	/// by <see cref="Herds"/>.
	///
	/// Built once from the layout (a pure function of it), under the world's determinism rules.
	/// </summary>
	internal static class DeerSites
	{
		internal struct Site
		{
			/// <summary>The road crossed and the sample it is crossed at.</summary>
			public int Road, I;
			/// <summary>The crossing's middle (on the road's centre line), the unit way across it (to the road's left) and the road's half width.</summary>
			public double X, Z, Nx, Nz, Half;
			/// <summary>The side the woods are on (+1 the left, -1 the right, 0 woods on neither: a hedge), and the side the open ground is on.</summary>
			public int Woods, Open;
		}

		/// <summary>Every site, in the roads' order. Null until <see cref="Ensure"/>.</summary>
		internal static List<Site> All;

		/// <summary>Milliseconds the sites took, for the log.</summary>
		internal static double BuildMs;

		/// <summary>Sites are looked for this far apart along a road.</summary>
		private const double Step = 110.0;

		/// <summary>How far out the cover each side is looked at.</summary>
		private static readonly double[] CoverOut = { 18.0, 40.0, 75.0 };

		private static readonly object gate = new object();
		private static readonly ulong Salt = Noise.Mix(WorldGen.Seed + 9900UL);

		internal static void Ensure()
		{
			if (All != null)
			{
				return;
			}
			lock (gate)
			{
				if (All != null)
				{
					return;
				}
				Network.Ensure();
				var watch = System.Diagnostics.Stopwatch.StartNew();
				List<Site> sites = new List<Site>();
				for (int r = 0; r < Network.Roads.Count; r++)
				{
					Network.Road road = Network.Roads[r];
					if (road.Kind != RoadKind.Lane && road.Kind != RoadKind.Secondary)
					{
						continue;
					}
					int n = road.Count;
					double length = road.S[n - 1];
					double next = 60.0;
					for (int i = 1; i < n - 1; i++)
					{
						if (road.S[i] < next || length - road.S[i] < 60.0)
						{
							continue;
						}
						next = road.S[i] + Step;
						// Seldom on a secondary road: they are busier.
						if (road.Kind == RoadKind.Secondary && Herds.Unit(Salt, (ulong)r, (ulong)i, 1) >= 0.25)
						{
							continue;
						}
						if (Try(r, road, i, out Site site))
						{
							sites.Add(site);
						}
					}
				}
				BuildMs = watch.Elapsed.TotalMilliseconds;
				All = sites;
			}
		}

		private static bool Try(int r, Network.Road road, int i, out Site site)
		{
			site = default;
			for (int k = Math.Max(0, i - 4); k <= Math.Min(road.Count - 1, i + 4); k++)
			{
				if (road.Bridge[k] || road.Over[k] || road.Abut[k])
				{
					return false;
				}
			}
			if (!Network.Shown(road, road.S[i] - 25.0) || !Network.Shown(road, road.S[i]) || !Network.Shown(road, road.S[i] + 25.0))
			{
				return false;
			}
			double cx = road.X[i], cz = road.Z[i];
			if (Places.At(cx, cz) != null || !WorldGen.Inside(cx, cz) || Math.Abs(cx) > WorldGen.Half - 600.0 || Math.Abs(cz) > WorldGen.Half - 600.0)
			{
				return false;
			}
			double tx = road.X[i + 1] - road.X[i - 1], tz = road.Z[i + 1] - road.Z[i - 1];
			double tl = Math.Sqrt(tx * tx + tz * tz);
			if (tl < 1e-6)
			{
				return false;
			}
			double nx = -tz / tl, nz = tx / tl;
			double half = road.Half;
			// No other road near the crossing (a junction, a drive's mouth, a track alongside).
			for (int side = -1; side <= 1; side += 2)
			{
				double px = cx + nx * side * (half + 7.0), pz = cz + nz * side * (half + 7.0);
				if (Network.Nearest(px, pz, 40.0, RoadKind.Trunk) < half + 5.0)
				{
					return false;
				}
				px = cx + nx * side * (half + 7.0) + tx / tl * 20.0;
				pz = cz + nz * side * (half + 7.0) + tz / tl * 20.0;
				if (Network.Nearest(px, pz, 40.0, RoadKind.Trunk) < half + 4.0)
				{
					return false;
				}
				px = cx + nx * side * (half + 7.0) - tx / tl * 20.0;
				pz = cz + nz * side * (half + 7.0) - tz / tl * 20.0;
				if (Network.Nearest(px, pz, 40.0, RoadKind.Trunk) < half + 4.0)
				{
					return false;
				}
			}
			if (!Herds.AwayFromHomes(cx, cz, 90.0))
			{
				return false;
			}
			// The cover each side: an edge is woods on one side and open ground beside the road (or a hedge and open ground).
			int[] woods = new int[2], hedge = new int[2], open = new int[2];
			for (int s = 0; s < 2; s++)
			{
				int side = s == 0 ? -1 : 1;
				foreach (double o in CoverOut)
				{
					double px = cx + nx * side * (half + o), pz = cz + nz * side * (half + o);
					Cover c = Herds.CoverAt(px, pz);
					switch (c)
					{
						case Cover.Forest:
							woods[s]++;
							break;
						case Cover.Hedge:
							hedge[s]++;
							break;
						case Cover.Meadow:
						case Cover.Wheat:
						case Cover.Crop:
						case Cover.Ploughed:
						case Cover.Pasture:
						case Cover.Grass:
						case Cover.Moor:
							open[s]++;
							break;
						default:
							if (o < 50.0)
							{
								return false;
							}
							break;
					}
				}
			}
			// The open side: the one with more open ground (on a tie, fewer woods); the woods' side: across the road from it if it has woods
			// there, else behind the field on the open side, else none (a hedge).
			int o0 = open[0] - woods[0], o1 = open[1] - woods[1];
			int openSide = open[1] > open[0] || (open[1] == open[0] && o1 >= o0) ? 1 : -1;
			int across = openSide > 0 ? 0 : 1, same = 1 - across;
			bool edge = (woods[0] + woods[1] > 0 && open[0] + open[1] > 0) || (hedge[0] + hedge[1] > 0 && open[0] + open[1] >= 2);
			if (!edge || open[same] == 0)
			{
				return false;
			}
			int woodsSide = woods[across] > 0 ? -openSide : woods[same] > 0 ? openSide : 0;
			// Across the road from verge to verge: no water, no drop.
			double ax = cx - nx * (half + 6.0), az = cz - nz * (half + 6.0);
			double bx = cx + nx * (half + 6.0), bz = cz + nz * (half + 6.0);
			if (!Herds.Passable(ax, az, bx, bz))
			{
				return false;
			}
			site = new Site { Road = r, I = i, X = cx, Z = cz, Nx = nx, Nz = nz, Half = half, Woods = woodsSide, Open = openSide };
			return true;
		}
	}

	/// <summary>What a deer is doing, for the pose it is drawn in.</summary>
	internal enum DeerMotion : byte
	{
		Graze,
		Stand,
		Alert,
		Walk,
		Trot,
		/// <summary>Fleeing: the bound, tail up.</summary>
		Gallop,
	}

	/// <summary>Where one deer is and what it is doing at a moment.</summary>
	internal struct DeerState
	{
		/// <summary>World position, and the way it faces (radians from +x toward +z, as <see cref="Network.Atan2"/>(dz, dx)).</summary>
		public double X, Z, Yaw;
		public DeerMotion Motion;
		/// <summary>How far through its gait (cycles; the walk's or trot's legs follow it), and its speed (units a second).</summary>
		public double Gait, Speed;
		/// <summary>Alarmed: the tail up as a white flag (fleeing, or watching after).</summary>
		public bool Flag;
	}

	/// <summary>
	/// The white-tailed deer of one race: groups drawn from the race's seed (<see cref="Weather.Seed"/>: the room's in a room, so the same
	/// for everybody, and somewhere else every race) at <see cref="DeerSites"/>, each with a few spots near its crossing place (one in the
	/// woods for cover, the rest feeding on the verges and in the field), and what every deer does as a pure function of (seed, group,
	/// deer, race clock), under the world's determinism rules as <see cref="Storms"/> is: every machine has the same deer in the same
	/// places with nothing sent, whether or not it has their ground loaded, so only the generator's functions are used, never the tiles.
	///
	/// Calm deer (no reaction to cars yet): the race clock is cut into slots of <see cref="SlotSeconds"/>, and in each slot a group stays
	/// where it is or moves to another of its spots, more often out feeding at dawn and dusk, more often in cover at midday. A move goes in
	/// single file, the first deer then each follower a second or more after; a move to the other side of the road crosses it at the
	/// crossing place (now and then at a trot, and now and then one turns back halfway and crosses after the others). At a spot each deer
	/// grazes, takes a step now and then and lifts its head to look about.
	///
	/// Every spot and every leg between them is tested along its length: never into water above a deer's belly (with a margin for the
	/// drawn ground, which can sit a unit or two off the pure height at a channel's edge), never over a drop (a bank, a cutting), never
	/// onto another road, never through a house or a lot. A leg that fails is never walked; a spot no leg reaches is dropped.
	/// </summary>
	internal sealed partial class Herds
	{
		/// <summary>Seconds of race time per slot.</summary>
		internal const double SlotSeconds = 90.0;

		/// <summary>A walk and a trot, units a second (about 1.4 m/s and 3 m/s).</summary>
		internal const double WalkSpeed = 4.5, TrotSpeed = 10.0;

		/// <summary>Units covered by one cycle of the walk's and the trot's legs.</summary>
		internal const double WalkStride = 5.0, TrotStride = 7.5;

		/// <summary>The groups a race draws, at most, and how far apart their crossing places are at least.</summary>
		internal const int MostGroups = 160;
		internal const double Spacing = 750.0;

		/// <summary>Water no deeper than this is waded (a deer's belly is about 2 units up), less the drawn ground's margin.</summary>
		internal const double Belly = 2.0, Margin = 1.5;

		/// <summary>The steepest rise between two points a leg is tested at (<see cref="TestStep"/> apart) on the drawn ground: steeper is a drop.</summary>
		internal const double TestStep = 1.25, MostRise = 1.1;

		/// <summary>A grazing deer takes a step every this many seconds, lasting <see cref="StepSeconds"/>.</summary>
		internal const double SubSeconds = 11.0, StepSeconds = 1.6;

		/// <summary>A deer settles into (and out of) its wandering about a spot over this long after arriving (before leaving).</summary>
		private const double Settle = 3.0;

		internal sealed class Spot
		{
			public double X, Z;
			/// <summary>Which side of the crossing road it is on (+1 its left, -1 its right); the woods' spot is cover.</summary>
			public int Side;
			public bool Cover;
		}

		/// <summary>
		/// The share of families with a fawn: few (the owner has never seen a spotted fawn try to cross a road), about 3 a race at the
		/// standard amount. Their size against a grown doe's.
		/// </summary>
		internal const double FawnChance = 0.035, FawnSmallest = 0.56, FawnLargest = 0.66;

		internal sealed class Member
		{
			/// <summary>Its size (1 a grown doe) and where it stands about a spot (an offset, turned for each spot).</summary>
			public double Scale, RingAngle, RingRadius;
			/// <summary>Its own phase in the grazing steps, and the gap it leaves behind the deer in front when the group moves.</summary>
			public double Phase, Gap;
			public bool Buck;
			/// <summary>A spotted fawn's age, drawn as the doe's mesh small (<see cref="FawnChance"/>); never a buck.</summary>
			public bool Fawn;
		}

		internal sealed class Group
		{
			public int Index;
			public DeerSites.Site Site;
			public Spot[] Spots;
			/// <summary>Whether a deer can walk from spot a to spot b (both ways).</summary>
			public bool[,] Links;
			public Member[] Members;
			public ulong Salt;
			/// <summary>The crossing's two verge points (left: +1, right: -1) and the road's way along at the crossing.</summary>
			public double LeftX, LeftZ, RightX, RightZ, Tx, Tz;
			/// <summary>Family, a lone deer, bucks, or several families together.</summary>
			public string Kind;
			/// <summary>Spots each side of the road: a group with spots on both crosses it.</summary>
			public bool Crosses;

			/// <summary>The spot each slot ends at, worked out in order from slot 0 (each slot's depends on the last's); grown as needed.</summary>
			internal readonly List<int> Chain = new List<int>();
			internal readonly object Gate = new object();

			/// <summary>Its reactions, in order (see Reactions.cs), and the spots they leave it at: slot -> spot, the chain going on from it.</summary>
			internal readonly List<Reacted> Reactions = new List<Reacted>();
			internal readonly Dictionary<long, int> Overrides = new Dictionary<long, int>();
		}

		internal readonly int Seed;
		internal readonly double StartHour, DaySeconds;
		internal readonly Group[] Groups;
		internal readonly int DeerCount;
		internal readonly double BuildMs;
		private readonly ulong salt;

		/// <summary>
		/// The herds of the race with this seed. <paramref name="amount"/> scales how many groups there are (1 the standard; 2 twice as
		/// many, their crossing places closer; at most what the sites allow): it is the player's own setting, so it is only ever other
		/// than 1 alone, never in a room, where every machine must have the same deer.
		/// </summary>
		internal Herds(int raceSeed, double startHour, double daySeconds, double amount = 1.0)
		{
			var watch = System.Diagnostics.Stopwatch.StartNew();
			Seed = raceSeed;
			StartHour = startHour;
			DaySeconds = daySeconds;
			salt = Noise.Mix((ulong)(uint)raceSeed ^ 0x0DEE_4F00_D5EEUL);
			DeerSites.Ensure();
			List<DeerSites.Site> sites = DeerSites.All;
			// The sites in an order of the race's: a shuffle from the seed, then each taken that is far enough from those taken before.
			int[] order = new int[sites.Count];
			for (int i = 0; i < order.Length; i++)
			{
				order[i] = i;
			}
			Rng rng = new Rng(salt);
			for (int i = order.Length - 1; i > 0; i--)
			{
				int j = (int)(rng.NextU64() % (ulong)(i + 1));
				int keep = order[i];
				order[i] = order[j];
				order[j] = keep;
			}
			List<Group> groups = new List<Group>();
			int count = 0;
			amount = Math.Max(0.0, Math.Min(4.0, amount));
			int most = (int)Math.Floor(MostGroups * amount + 0.5);
			double spacing = amount > 0.0 ? Spacing / Math.Sqrt(amount) : Spacing;
			foreach (int k in order)
			{
				if (groups.Count >= most)
				{
					break;
				}
				DeerSites.Site site = sites[k];
				bool near = false;
				foreach (Group g in groups)
				{
					double dx = g.Site.X - site.X, dz = g.Site.Z - site.Z;
					if (dx * dx + dz * dz < spacing * spacing)
					{
						near = true;
						break;
					}
				}
				if (near)
				{
					continue;
				}
				Group made = Make(groups.Count, site, Noise.Mix(salt ^ ((ulong)k * 0x9E3779B97F4A7C15UL)));
				if (made != null)
				{
					groups.Add(made);
					count += made.Members.Length;
				}
			}
			Groups = groups.ToArray();
			DeerCount = count;
			BuildMs = watch.Elapsed.TotalMilliseconds;
		}

		// ---- the tests on the ground --------------------------------------------------------------------------------

		/// <summary>A value from 0 to 1 from a salt and three numbers.</summary>
		internal static double Unit(ulong salt, ulong a, ulong b, ulong c)
		{
			unchecked
			{
				ulong h = Noise.Mix(salt ^ Noise.Mix(a * 0x9E3779B97F4A7C15UL ^ Noise.Mix(b * 0xBF58476D1CE4E5B9UL ^ Noise.Mix(c + 0x632BE59BD9B4E019UL))));
				return (double)(h >> 11) * (1.0 / 9007199254740992.0);
			}
		}

		/// <summary>The drawn ground's grid corners already worked out on this thread (the same numbers every time, so only a saving).</summary>
		[ThreadStatic]
		private static Dictionary<long, double> corners;

		/// <summary>
		/// The ground as the tiles draw it at (x, z), a scene height: the 5-unit grid's triangles over <see cref="WorldGen.Height"/> at its
		/// corners, as <c>TerrainGrid.At</c> (which the pure height can be a few units off at the top of a bank or a channel's edge).
		/// Deer stand on this and their legs are tested on it.
		/// </summary>
		internal static double Ground(double x, double z)
		{
			const double step = GridStep;
			double gx = Math.Floor(x / step), gz = Math.Floor(z / step);
			double fx = x / step - gx, fz = z / step - gz;
			long ix = (long)gx, iz = (long)gz;
			double a = Corner(ix, iz), b = Corner(ix + 1, iz), c = Corner(ix, iz + 1), d = Corner(ix + 1, iz + 1);
			return fx + fz <= 1.0 ? a + (b - a) * fx + (c - a) * fz : d + (c - d) * (1.0 - fx) + (b - d) * (1.0 - fz);
		}

		/// <summary>The tiles' ground grid, as <c>TerrainGrid.Step</c> (kept here so the preview, which has no meshes, can use it; meshcheck
		/// checks the two agree).</summary>
		private const double GridStep = 5.0;

		private static double Corner(long ix, long iz)
		{
			Dictionary<long, double> cache = corners ??= new Dictionary<long, double>();
			long key = (ix << 32) ^ (iz & 0xFFFFFFFFL);
			if (cache.TryGetValue(key, out double h))
			{
				return h;
			}
			if (cache.Count > 400000)
			{
				cache.Clear();
			}
			h = (float)WorldGen.Height(ix * GridStep, iz * GridStep);
			cache[key] = h;
			return h;
		}

		/// <summary>The ground's cover at (x, z), from the pure height (as the tiles' pictures are drawn).</summary>
		internal static Cover CoverAt(double x, double z)
		{
			double h = WorldGen.Height(x, z) - Network.Lift;
			double hx = WorldGen.Height(x + 2.0, z) - Network.Lift, hz = WorldGen.Height(x, z + 2.0) - Network.Lift;
			double gx = (hx - h) / 2.0, gz = (hz - h) / 2.0;
			return LandUse.Classify(x, z, h, Math.Sqrt(gx * gx + gz * gz), out int _);
		}

		/// <summary>True where the drawn water at (x, z) is above a deer's belly on ground of scene height <paramref name="ground"/>.</summary>
		internal static bool Deep(double x, double z, double ground)
		{
			double water = Rivers.DrawnWater(x, z);
			return !double.IsNaN(water) && water + Network.Lift > ground + Belly - Margin;
		}

		/// <summary>No house within <paramref name="r"/> of (x, z), nor a mailbox within half of it.</summary>
		internal static bool AwayFromHomes(double x, double z, double r)
		{
			foreach (Network.Home h in Network.Homes)
			{
				double dx = h.X - x, dz = h.Z - z;
				if (h.House && dx * dx + dz * dz < r * r)
				{
					return false;
				}
				dx = h.MailX - x;
				dz = h.MailZ - z;
				if (dx * dx + dz * dz < r * r * 0.25)
				{
					return false;
				}
			}
			return true;
		}

		/// <summary>
		/// Whether a deer can walk straight from a to b: tested every <see cref="TestStep"/>, never deep water nor a rise or fall steeper
		/// than <see cref="MostRise"/> over a step. Roads may be crossed (the crossing itself is tested with this).
		/// </summary>
		internal static bool Passable(double ax, double az, double bx, double bz)
		{
			double dx = bx - ax, dz = bz - az;
			double len = Math.Sqrt(dx * dx + dz * dz);
			int steps = Math.Max(1, (int)Math.Floor(len / TestStep) + 1);
			double last = 0.0;
			for (int k = 0; k <= steps; k++)
			{
				double f = (double)k / steps;
				double x = ax + dx * f, z = az + dz * f;
				double h = Ground(x, z);
				if (Deep(x, z, h) || (k > 0 && Math.Abs(h - last) > MostRise * (len / steps) / TestStep))
				{
					return false;
				}
				last = h;
			}
			return true;
		}

		/// <summary>
		/// Whether (x, z) is clear for a deer to stand or walk off the road: off every road's paving by <paramref name="margin"/> (the big roads by
		/// more), off the lots, the courses' ground and houses.
		/// </summary>
		internal static bool Clear(double x, double z, double margin)
		{
			for (RoadKind kind = RoadKind.Trunk; kind <= RoadKind.Rally; kind++)
			{
				// The main roads by more: busy, and their banks are big.
				double keep = Network.HalfOf(kind) + margin + (kind <= RoadKind.Secondary ? 6.0 : 0.0);
				if (Network.Nearest(x, z, keep + 1.0, kind, kind) < keep)
				{
					return false;
				}
			}
			return !Town.Taken(x, z, 4.0) && !Circuit.Covered(x, z, 8.0) && !Rally.Covered(x, z, 4.0) && !HillClimb.Covered(x, z, 4.0) && Places.At(x, z) == null;
		}

		/// <summary>A leg off the road: passable and clear all along.</summary>
		private static bool Walkable(double ax, double az, double bx, double bz, double margin)
		{
			if (!Passable(ax, az, bx, bz))
			{
				return false;
			}
			double dx = bx - ax, dz = bz - az;
			double len = Math.Sqrt(dx * dx + dz * dz);
			int steps = Math.Max(1, (int)Math.Floor(len / 5.0) + 1);
			for (int k = 0; k <= steps; k++)
			{
				double f = (double)k / steps;
				if (!Clear(ax + dx * f, az + dz * f, margin))
				{
					return false;
				}
			}
			return true;
		}

		/// <summary>A spot a group can stand about: dry and fairly flat for <see cref="SpotRadius"/> round, clear of roads and houses.</summary>
		internal static bool GoodSpot(double x, double z, bool cover)
		{
			if (!WorldGen.Inside(x, z) || !Clear(x, z, 4.0) || !AwayFromHomes(x, z, 30.0))
			{
				return false;
			}
			double lo = double.MaxValue, hi = double.MinValue;
			for (int k = 0; k <= 8; k++)
			{
				double px = x, pz = z;
				if (k > 0)
				{
					double a = (k - 1) * (DetMath.Pi / 4.0);
					px += DetMath.Cos(a) * SpotRadius;
					pz += DetMath.Sin(a) * SpotRadius;
				}
				double h = Ground(px, pz);
				if (Deep(px, pz, h) || (k > 0 && !Clear(px, pz, 2.0)))
				{
					return false;
				}
				lo = Math.Min(lo, h);
				hi = Math.Max(hi, h);
			}
			if (hi - lo > 4.5)
			{
				return false;
			}
			Cover c = CoverAt(x, z);
			return cover ? c == Cover.Forest || c == Cover.Hedge || c == Cover.Grass || c == Cover.Moor || c == Cover.Meadow
				: c != Cover.Paving && c != Cover.Garden && c != Cover.Yard && c != Cover.Park && c != Cover.Circuit && c != Cover.Rock && c != Cover.Snow && c != Cover.Sand;
		}

		/// <summary>How far round a spot its deer stand and wander (the members' ring and their steps).</summary>
		internal const double SpotRadius = 9.0;

		// ---- making a group -----------------------------------------------------------------------------------------

		private Group Make(int index, DeerSites.Site site, ulong groupSalt)
		{
			Rng r = new Rng(groupSalt);
			Network.Road road = Network.Roads[site.Road];
			Group g = new Group { Index = index, Site = site, Salt = groupSalt };
			// Along the road (the way its samples run): the left is +normal, so along is (normal z, -normal x).
			g.Tx = site.Nz;
			g.Tz = -site.Nx;
			g.LeftX = site.X + site.Nx * (site.Half + 5.0);
			g.LeftZ = site.Z + site.Nz * (site.Half + 5.0);
			g.RightX = site.X - site.Nx * (site.Half + 5.0);
			g.RightZ = site.Z - site.Nz * (site.Half + 5.0);

			List<Spot> spots = new List<Spot>();
			// Cover: in the woods (or along the hedge) on the woods' side, set back from the road.
			int coverSide = site.Woods != 0 ? site.Woods : -site.Open;
			for (int tries = 0; tries < 6 && spots.Count == 0; tries++)
			{
				double along = r.Range(-60.0, 60.0);
				double back = site.Half + r.Range(35.0, 80.0);
				PointBeside(road, site, along, coverSide, back, out double x, out double z);
				if (GoodSpot(x, z, true))
				{
					spots.Add(new Spot { X = x, Z = z, Side = coverSide, Cover = true });
				}
			}
			if (spots.Count == 0)
			{
				return null;
			}
			// Feeding spots: on the verges either side, and out in the field on the open side.
			int want = 3 + (r.Next() < 0.5 ? 1 : 0);
			for (int tries = 0; tries < 14 && spots.Count < want + 1; tries++)
			{
				bool field = r.Next() < 0.4;
				int side = field ? site.Open : (r.Next() < 0.6 ? site.Open : -site.Open);
				double along = r.Range(-140.0, 140.0);
				double back = field ? site.Half + r.Range(25.0, 70.0) : site.Half + r.Range(9.0, 15.0);
				PointBeside(road, site, along, side, back, out double x, out double z);
				bool apart = true;
				foreach (Spot s in spots)
				{
					double dx = s.X - x, dz = s.Z - z;
					if (dx * dx + dz * dz < 30.0 * 30.0)
					{
						apart = false;
						break;
					}
				}
				if (apart && GoodSpot(x, z, false))
				{
					spots.Add(new Spot { X = x, Z = z, Side = side });
				}
			}
			int n = spots.Count;
			bool[,] links = new bool[n, n];
			for (int a = 0; a < n; a++)
			{
				for (int b = a + 1; b < n; b++)
				{
					bool ok = LegsOk(g, spots[a], spots[b]);
					links[a, b] = ok;
					links[b, a] = ok;
				}
			}
			// Keep the spots reached from the cover.
			bool[] reached = new bool[n];
			reached[0] = true;
			Queue<int> queue = new Queue<int>();
			queue.Enqueue(0);
			while (queue.Count > 0)
			{
				int a = queue.Dequeue();
				for (int b = 0; b < n; b++)
				{
					if (links[a, b] && !reached[b])
					{
						reached[b] = true;
						queue.Enqueue(b);
					}
				}
			}
			List<int> kept = new List<int>();
			for (int a = 0; a < n; a++)
			{
				if (reached[a])
				{
					kept.Add(a);
				}
			}
			if (kept.Count < 2)
			{
				return null;
			}
			g.Spots = new Spot[kept.Count];
			g.Links = new bool[kept.Count, kept.Count];
			bool left = false, right = false;
			for (int a = 0; a < kept.Count; a++)
			{
				g.Spots[a] = spots[kept[a]];
				left |= g.Spots[a].Side > 0;
				right |= g.Spots[a].Side < 0;
				for (int b = 0; b < kept.Count; b++)
				{
					g.Links[a, b] = links[kept[a], kept[b]];
				}
			}
			g.Crosses = left && right;

			// Who is in it: mostly a doe and her young, some lone deer, some bucks together, now and then families feeding together.
			double u = r.Next();
			int size;
			if (u < 0.15)
			{
				g.Kind = "lone";
				size = 1;
			}
			else if (u < 0.72)
			{
				g.Kind = "family";
				size = 2 + (int)Math.Floor(r.Next() * 4.0);
			}
			else if (u < 0.9)
			{
				g.Kind = "bucks";
				size = 2 + (int)Math.Floor(r.Next() * 3.0);
			}
			else
			{
				g.Kind = "herd";
				size = 6 + (int)Math.Floor(r.Next() * 4.0);
			}
			g.Members = new Member[size];
			for (int d = 0; d < size; d++)
			{
				Member m = new Member();
				m.Buck = g.Kind == "bucks" || (g.Kind == "lone" && r.Next() < 0.4);
				m.Scale = m.Buck ? r.Range(1.0, 1.12) : d == 0 ? r.Range(0.95, 1.05) : r.Range(0.72, 1.0);
				m.RingAngle = r.Range(0.0, 2.0 * DetMath.Pi);
				m.RingRadius = d == 0 ? r.Range(0.0, 2.0) : r.Range(3.0, SpotRadius - 3.0);
				m.Phase = r.Range(0.0, SubSeconds);
				m.Gap = r.Range(1.3, 3.8);
				g.Members[d] = m;
			}
			// Now and then a family has this year's fawn with it, its last deer: a hash of its own, not a draw from r, so nothing else
			// in the group (or any other group) moves. Its size from the young's drawn one (0.72 to 1), brought down to a fawn's.
			if (g.Kind == "family" && Unit(groupSalt, 0xFA3EUL, 0, 0) < FawnChance)
			{
				Member f = g.Members[size - 1];
				f.Fawn = true;
				f.Scale = FawnSmallest + (f.Scale - 0.72) / 0.28 * (FawnLargest - FawnSmallest);
			}
			return g;
		}

		/// <summary>A point <paramref name="along"/> the road from the crossing and <paramref name="back"/> from its centre line to <paramref name="side"/>.</summary>
		private static void PointBeside(Network.Road road, DeerSites.Site site, double along, int side, double back, out double x, out double z)
		{
			double s = Math.Max(0.0, Math.Min(road.S[road.Count - 1], road.S[site.I] + along));
			Network.Locate(road, s, out int i, out double t);
			double cx = road.X[i] * (1.0 - t) + road.X[i + 1] * t, cz = road.Z[i] * (1.0 - t) + road.Z[i + 1] * t;
			double tx = road.X[i + 1] - road.X[i], tz = road.Z[i + 1] - road.Z[i];
			double tl = Math.Sqrt(tx * tx + tz * tz);
			double nx = tl > 1e-9 ? -tz / tl : site.Nx, nz = tl > 1e-9 ? tx / tl : site.Nz;
			// The road's own left, matched to the site's (a road's samples all run one way, so they agree unless it doubles back).
			if (nx * site.Nx + nz * site.Nz < 0.0)
			{
				nx = -nx;
				nz = -nz;
			}
			x = cx + nx * side * back;
			z = cz + nz * side * back;
		}

		/// <summary>The legs from spot a to spot b: straight on one side of the road, through the crossing's verge points to the other.</summary>
		internal static void Legs(Group g, Spot a, Spot b, List<(double x, double z)> points)
		{
			points.Clear();
			points.Add((a.X, a.Z));
			if (a.Side != b.Side)
			{
				points.Add(a.Side > 0 ? (g.LeftX, g.LeftZ) : (g.RightX, g.RightZ));
				points.Add(b.Side > 0 ? (g.LeftX, g.LeftZ) : (g.RightX, g.RightZ));
			}
			points.Add((b.X, b.Z));
		}

		private static bool LegsOk(Group g, Spot a, Spot b)
		{
			List<(double x, double z)> p = new List<(double, double)>();
			Legs(g, a, b, p);
			for (int k = 0; k + 1 < p.Count; k++)
			{
				bool crossing = a.Side != b.Side && k == 1;
				if (crossing)
				{
					continue; // tested when the site was found
				}
				// Off the road along the leg, by a little less than the spots are (the crossing's verge points are 5 off the paving).
				if (!Walkable(p[k].x, p[k].z, p[k + 1].x, p[k + 1].z, 2.0))
				{
					return false;
				}
			}
			return true;
		}

		// ---- the day ------------------------------------------------------------------------------------------------

		/// <summary>The hour (0 to 24) at race time <paramref name="t"/>.</summary>
		internal double Hour(double t)
		{
			double h = StartHour + t / DaySeconds * 24.0;
			return h - Math.Floor(h / 24.0) * 24.0;
		}

		/// <summary>How likely a group is to be out feeding rather than in cover at an hour: most at dawn and dusk, least at midday.</summary>
		internal static double Activity(double hour)
		{
			double dawn = Bump(hour, 6.5, 2.2), dusk = Bump(hour, 18.8, 2.6);
			double night = hour < 5.0 || hour > 21.0 ? 0.25 : 0.0;
			return Math.Min(0.95, 0.28 + night + 0.65 * Math.Max(dawn, dusk));
		}

		private static double Bump(double x, double centre, double half)
		{
			double d = Math.Abs(x - centre) / half;
			return d >= 1.0 ? 0.0 : 1.0 - Noise.Step(0.0, 1.0, d);
		}

		// ---- the schedule -------------------------------------------------------------------------------------------

		private double U(Group g, long slot, int d, int j)
		{
			return Unit(g.Salt, (ulong)slot, (ulong)d, (ulong)j);
		}

		/// <summary>The spot group g is at (or going to) in slot k (k = -1: where it starts).</summary>
		internal int SpotAt(Group g, long k)
		{
			if (k < 0)
			{
				return Start(g);
			}
			lock (g.Gate)
			{
				// Long races are a few hundred slots: worked out once, in order.
				while (g.Chain.Count <= k)
				{
					long slot = g.Chain.Count;
					int prev = slot == 0 ? Start(g) : g.Chain[(int)slot - 1];
					g.Chain.Add(g.Overrides.TryGetValue(slot, out int left) ? left : Choose(g, slot, prev));
				}
				return g.Chain[(int)k];
			}
		}

		private int Start(Group g)
		{
			bool out_ = Unit(g.Salt, 0x57A27UL, 0, 0) < Activity(Hour(0.0));
			int pick = (int)Math.Floor(Unit(g.Salt, 0x57A27UL, 1, 0) * (g.Spots.Length - 1));
			return out_ ? 1 + Math.Min(pick, g.Spots.Length - 2) : 0;
		}

		private int Choose(Group g, long k, int prev)
		{
			double activity = Activity(Hour(k * SlotSeconds));
			bool feed = U(g, k, 255, 1) < activity;
			bool here = g.Spots[prev].Cover != feed;
			if (here && U(g, k, 255, 0) < 0.55)
			{
				return prev;
			}
			int count = 0, any = 0;
			for (int b = 0; b < g.Spots.Length; b++)
			{
				if (g.Links[prev, b])
				{
					any++;
					if (g.Spots[b].Cover != feed)
					{
						count++;
					}
				}
			}
			if (any == 0)
			{
				return prev;
			}
			bool fitting = count > 0;
			int n = fitting ? count : any;
			int pick = Math.Min(n - 1, (int)Math.Floor(U(g, k, 255, 2) * n));
			for (int b = 0; b < g.Spots.Length; b++)
			{
				if (g.Links[prev, b] && (!fitting || g.Spots[b].Cover != feed))
				{
					if (pick-- == 0)
					{
						return b;
					}
				}
			}
			return prev;
		}

		/// <summary>Where deer d of group g stands at a spot: its place in the group's ring, turned for the spot.</summary>
		internal static void Base(Group g, int spot, int d, out double x, out double z)
		{
			Member m = g.Members[d];
			double a = m.RingAngle + spot * 2.399963;
			x = g.Spots[spot].X + DetMath.Cos(a) * m.RingRadius;
			z = g.Spots[spot].Z + DetMath.Sin(a) * m.RingRadius;
		}

		/// <summary>One deer's move in a slot: the points it passes, when it is at each, and how fast each leg goes.</summary>
		internal struct Move
		{
			public int Count;
			public double[] X, Z, T;
			/// <summary>How each point is reached from the one before (0 a walk, 1 the crossing's pace, 2 a wait).</summary>
			public byte[] Pace;
			public double Depart, Arrive;
		}

		/// <summary>
		/// Deer d's move in slot k from spot a to spot b: single file behind the deer in front, through the crossing if b is over the
		/// road, at a walk (a trot across the road now and then); one in a group sometimes turns back halfway over and crosses last.
		/// </summary>
		internal void MoveOf(Group g, long k, int a, int b, int d, ref Move mv)
		{
			if (mv.X == null)
			{
				mv.X = new double[MovePoints];
				mv.Z = new double[MovePoints];
				mv.T = new double[MovePoints];
				mv.Pace = new byte[MovePoints];
			}
			int n = g.Members.Length;
			double start = k * SlotSeconds;
			double depart = start + U(g, k, 254, 0) * 8.0;
			double gapScale = Math.Min(1.0, 22.0 / Math.Max(1, n - 1) / 3.8);
			for (int e = 1; e <= d; e++)
			{
				depart += g.Members[e].Gap * gapScale;
			}
			bool cross = g.Spots[a].Side != g.Spots[b].Side;
			bool trot = cross && U(g, k, 254, 1) < 0.35;
			int back = cross && n > 1 && U(g, k, 254, 2) < 0.3 ? 1 + (int)Math.Floor(U(g, k, 254, 3) * (n - 1)) : -1;
			// Single file: every deer on the same line between the spots' middles (the line the legs were tested along), a hair apart
			// over the road so they do not walk in each other's hoofprints exactly.
			double lateral = 0.4 * (d % 3 - 1);

			mv.Count = 0;
			Base(g, a, d, out double ax, out double az);
			Add(ref mv, ax, az, 0);
			Add(ref mv, g.Spots[a].X, g.Spots[a].Z, 0);
			if (cross)
			{
				double fx = (g.Spots[a].Side > 0 ? g.LeftX : g.RightX) + g.Tx * lateral;
				double fz = (g.Spots[a].Side > 0 ? g.LeftZ : g.RightZ) + g.Tz * lateral;
				double tx = (g.Spots[b].Side > 0 ? g.LeftX : g.RightX) + g.Tx * lateral;
				double tz = (g.Spots[b].Side > 0 ? g.LeftZ : g.RightZ) + g.Tz * lateral;
				Add(ref mv, fx, fz, 0);
				if (d == back)
				{
					// Halfway over, back to where it started, a wait at the verge, then over after all.
					Add(ref mv, (fx + tx) * 0.5, (fz + tz) * 0.5, 1);
					Add(ref mv, fx, fz, 1);
					Add(ref mv, fx, fz, 2);
				}
				Add(ref mv, tx, tz, 1);
			}
			Add(ref mv, g.Spots[b].X, g.Spots[b].Z, 0);
			Base(g, b, d, out double bx, out double bz);
			Add(ref mv, bx, bz, 0);

			// Each leg's time: across the road at the crossing's pace, the rest at a walk, the wait as long as it is; all quickened if
			// the move would run past the slot (a long way, a big group).
			double wait = 4.0 + U(g, k, 254, 4) * 6.0;
			double speedCross = trot ? TrotSpeed : WalkSpeed;
			double total = 0.0;
			for (int i = 1; i < mv.Count; i++)
			{
				double dx = mv.X[i] - mv.X[i - 1], dz = mv.Z[i] - mv.Z[i - 1];
				double len = Math.Sqrt(dx * dx + dz * dz);
				mv.T[i] = mv.Pace[i] == 2 ? wait : len / (mv.Pace[i] == 1 ? speedCross : WalkSpeed);
				total += mv.T[i];
			}
			double room = start + SlotSeconds - 6.0 - depart;
			double quicken = total > room && room > 1.0 ? total / room : 1.0;
			double time = depart;
			mv.T[0] = time;
			for (int i = 1; i < mv.Count; i++)
			{
				time += mv.T[i] / quicken;
				mv.T[i] = time;
			}
			mv.Depart = depart;
			mv.Arrive = time;
		}

		/// <summary>The most points a move has.</summary>
		private const int MovePoints = 10;

		/// <summary>Adds a point to a move, reached at <paramref name="pace"/> (0 a walk, 1 the crossing's pace, 2 a wait where it is).</summary>
		private static void Add(ref Move mv, double x, double z, byte pace)
		{
			mv.X[mv.Count] = x;
			mv.Z[mv.Count] = z;
			mv.Pace[mv.Count] = pace;
			mv.Count++;
		}

		/// <summary>
		/// Where deer d of group g is and what it is doing at race time t. Pure: the same on every machine. <paramref name="mv"/> is
		/// scratch (reused between calls to save allocating).
		/// </summary>
		internal void At(Group g, int d, double t, ref Move mv, ref Move spare, out DeerState s)
		{
			s = default;
			long k = Math.Max(0L, (long)Math.Floor(t / SlotSeconds));
			int prev = SpotAt(g, k - 1), here = SpotAt(g, k);
			if (prev != here)
			{
				MoveOf(g, k, prev, here, d, ref mv);
				if (t < mv.Depart)
				{
					Rest(g, prev, d, t, LastArrival(g, k - 1, d, ref spare), mv.Depart, ref s);
				}
				else if (t < mv.Arrive)
				{
					Walking(ref mv, t, ref s);
				}
				else
				{
					Rest(g, here, d, t, mv.Arrive, NextDeparture(g, k + 1, here, d, ref spare), ref s);
				}
				return;
			}
			Rest(g, here, d, t, LastArrival(g, k - 1, d, ref spare), NextDeparture(g, k + 1, here, d, ref spare), ref s);
		}

		/// <summary>When deer d last arrived where it is, looking back from slot k (a long time ago if not lately).</summary>
		private double LastArrival(Group g, long k, int d, ref Move mv)
		{
			// Settled in long since if it came more than a couple of slots ago.
			for (long j = k; j >= 0 && j > k - 3; j--)
			{
				int before = SpotAt(g, j - 1), at = SpotAt(g, j);
				if (before != at)
				{
					MoveOf(g, j, before, at, d, ref mv);
					return mv.Arrive;
				}
			}
			return -1e9;
		}

		/// <summary>When deer d next leaves <paramref name="spot"/>, if in slot k (far off if not).</summary>
		internal double NextDeparture(Group g, long k, int spot, int d, ref Move mv)
		{
			int next = SpotAt(g, k);
			if (next == spot)
			{
				return 1e9;
			}
			MoveOf(g, k, spot, next, d, ref mv);
			return mv.Depart;
		}

		private static void Walking(ref Move mv, double t, ref DeerState s)
		{
			double gone = 0.0;
			for (int i = 0; i + 1 < mv.Count; i++)
			{
				double dx = mv.X[i + 1] - mv.X[i], dz = mv.Z[i + 1] - mv.Z[i];
				double len = Math.Sqrt(dx * dx + dz * dz);
				double t0 = mv.T[i], t1 = mv.T[i + 1];
				if (t < t1 || i + 2 == mv.Count)
				{
					double f = t1 > t0 ? Math.Max(0.0, Math.Min(1.0, (t - t0) / (t1 - t0))) : 1.0;
					s.X = mv.X[i] + dx * f;
					s.Z = mv.Z[i] + dz * f;
					if (mv.Pace[i + 1] == 2 || len < 1e-6)
					{
						// The wait at the verge after turning back (or a leg of no length): standing, alert, facing the road.
						s.Motion = DeerMotion.Alert;
						s.Speed = 0.0;
						double rx = mv.X[i] - mv.X[Math.Max(0, i - 1)], rz = mv.Z[i] - mv.Z[Math.Max(0, i - 1)];
						s.Yaw = Network.Atan2(-rz, -rx);
						s.Gait = gone / WalkStride;
						return;
					}
					s.Speed = len / Math.Max(1e-6, t1 - t0);
					s.Motion = s.Speed > 7.0 ? DeerMotion.Trot : DeerMotion.Walk;
					s.Yaw = Network.Atan2(dz, dx);
					gone += len * f;
					s.Gait = gone / (s.Motion == DeerMotion.Trot ? TrotStride : WalkStride);
					return;
				}
				gone += len;
			}
		}

		/// <summary>
		/// Deer d at a spot: its place in the ring, with small steps about it every <see cref="SubSeconds"/> (settling in after it arrived
		/// and out before it leaves), grazing, now and then head up.
		/// </summary>
		internal void Rest(Group g, int spot, int d, double t, double arrived, double leaves, ref DeerState s)
		{
			Base(g, spot, d, out double bx, out double bz);
			Member m = g.Members[d];
			double w = Math.Min(Noise.Step(0.0, Settle, t - arrived), 1.0 - Noise.Step(-Settle, 0.0, t - leaves));
			long j = (long)Math.Floor((t - m.Phase) / SubSeconds);
			double into = t - m.Phase - j * SubSeconds;
			Jitter(g, d, j - 1, out double px, out double pz);
			Jitter(g, d, j, out double cx, out double cz);
			double f = Noise.Step(0.0, StepSeconds, into);
			s.X = bx + w * (px + (cx - px) * f);
			s.Z = bz + w * (pz + (cz - pz) * f);
			double sx = cx - px, sz = cz - pz;
			s.Yaw = Network.Atan2(sz, sx);
			s.Speed = 0.0;
			if (into < StepSeconds && w > 0.5)
			{
				s.Motion = DeerMotion.Walk;
				s.Speed = Math.Sqrt(sx * sx + sz * sz) / StepSeconds;
				s.Gait = f * 0.5 + j * 0.5;
				return;
			}
			if (t - arrived < Settle * 2.0 || leaves - t < Settle)
			{
				s.Motion = DeerMotion.Alert;
				return;
			}
			double look = U(g, j, d, 7);
			double lookChance = g.Members.Length == 1 ? 0.4 : 0.22;
			if (look < lookChance)
			{
				// Head up for part of the step: the first few seconds after it.
				s.Motion = into < StepSeconds + 3.0 + look * 10.0 ? DeerMotion.Alert : DeerMotion.Graze;
				return;
			}
			s.Motion = g.Spots[spot].Cover && look > 0.8 ? DeerMotion.Stand : DeerMotion.Graze;
		}

		/// <summary>Deer d's small offset from its place in step j: within a couple of units, a little forward each time.</summary>
		private void Jitter(Group g, int d, long j, out double x, out double z)
		{
			double a = U(g, j, d, 5) * 2.0 * DetMath.Pi;
			double r = 0.6 + U(g, j, d, 6) * 2.0;
			x = DetMath.Cos(a) * r;
			z = DetMath.Sin(a) * r;
		}
	}
}
