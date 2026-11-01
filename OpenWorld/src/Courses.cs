using System;
using System.Collections.Generic;

namespace HalfGripOpenWorld
{
	/// <summary>
	/// The events' courses: the circuit (laps from its grid), the rally stage (once round its loop, start line to finish line), the
	/// hill climb (foot to col) and the highway (the trunk road one way, from the west edge's tunnel to the east's), each as a centre line, a start box off the course (where a car signs on), the grid spot it is then
	/// put on behind the start line, and checkpoint gates across the road every few hundred metres to the finish. Read from the layout, never added to it: generation is unchanged, and every player gets the same
	/// course because they get the same layout (the events in a room, later, rely on that). Pure arithmetic (no Unity), so the preview
	/// lists them and meshcheck checks the gates and boxes.
	/// </summary>
	internal static class Courses
	{
		internal enum Kind
		{
			Circuit = 0,
			Rally = 1,
			HillClimb = 2,
			Highway = 3,
		}

		/// <summary>A checkpoint: a line across the road at S along the centre line, crossed going (Dx, Dz).</summary>
		internal struct Gate
		{
			/// <summary>Distance along the course from its start line (the laps run on: lap 2's gates are a lap further).</summary>
			public double Along;
			public double X;
			public double Z;
			/// <summary>The road's drawn surface at the gate, from sea level (add <see cref="Network.Lift"/> for the scene).</summary>
			public double Y;
			public double Dx;
			public double Dz;
			/// <summary>How far either side of the centre line a crossing counts: the road, its kerbs and room for running wide.</summary>
			public double Half;
			/// <summary>The lap this gate ends (1 on), or 0 if it is a checkpoint within a lap.</summary>
			public int LapEnd;
		}

		internal sealed class Course
		{
			public Kind Kind;
			public string Name;
			/// <summary>The centre line's samples (a loop for the circuit and the rally stage), heights of the drawn surface from sea level.</summary>
			public double[] X;
			public double[] Z;
			public double[] Y;
			public double[] S;
			public int Count;
			public double Length;
			public bool Loop;
			/// <summary>The road's half width.</summary>
			public double Half;
			/// <summary>The course's road (null for the circuit, which is not one of the network's roads).</summary>
			public Network.Road Road;
			/// <summary>
			/// How far right of the centre line a car is put, on the grid and after a wreck: 0 but on the highway, whose centre line is
			/// its median, where the car goes in the middle of the carriageway the way it goes (traffic keeps right).
			/// </summary>
			public double Keep;
			/// <summary>Where the start line is along the centre line.</summary>
			public double StartS;
			/// <summary>Laps (1 for the stage and the climb).</summary>
			public int Laps;
			/// <summary>From the start line to the finish, all laps.</summary>
			public double Distance;
			/// <summary>The checkpoints in the order they are passed; the last is the finish line.</summary>
			public readonly List<Gate> Gates = new List<Gate>();
			/// <summary>
			/// The start box: a circle off the course, near its start, where stopping signs the car on (in the circuit's pit lane past
			/// the garages, in the rally stage's service area, in the hill climb's paddock, beside each garage's circle; on the highway
			/// on the westbound carriageway behind the start line, where a car driven west to the world's edge stops); its chevron
			/// points (BoxDx, BoxDz), the way the course leaves its start.
			/// </summary>
			public double BoxX;
			public double BoxZ;
			public double BoxY;
			public double BoxDx;
			public double BoxDz;
			public double BoxRadius;
			/// <summary>The surface's rise per unit east and north round the box (the road's grade along it, the circuit's plane's tilt).</summary>
			public double BoxGx;
			public double BoxGz;

			/// <summary>Where a signed-on car is put for the start: on the circuit, pole position; elsewhere behind the start line. Facing (GridDx, GridDz).</summary>
			public double GridX;
			public double GridZ;
			public double GridY;
			public double GridDx;
			public double GridDz;

			/// <summary>Height of the surface round the box at (x, z), from sea level.</summary>
			public double BoxSurface(double x, double z)
			{
				return BoxY + BoxGx * (x - BoxX) + BoxGz * (z - BoxZ);
			}

			/// <summary>
			/// The key its best time is kept under: the course and its length and laps, so a course that a new version lays out
			/// differently (or a different number of laps) starts with no best time instead of one it cannot be compared with.
			/// </summary>
			public string Key => Kind + "_" + Math.Round(Distance).ToString("0") + "_" + Laps;

			/// <summary>The centre line's point at distance s along it (wrapping round a loop, clamped otherwise), its height and direction.</summary>
			public void At(double s, out double x, out double z, out double y, out double dx, out double dz)
			{
				s = Loop ? ((s % Length) + Length) % Length : Math.Max(0.0, Math.Min(Length, s));
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
				int i = Loop && S[hi] <= s ? hi : lo, q = Loop ? (i + 1) % Count : hi;
				double len = (Loop && q == 0 ? Length : S[q]) - S[i];
				double t = len > 0.0 ? Math.Max(0.0, Math.Min(1.0, (s - S[i]) / len)) : 0.0;
				x = X[i] + (X[q] - X[i]) * t;
				z = Z[i] + (Z[q] - Z[i]) * t;
				y = Y[i] + (Y[q] - Y[i]) * t;
				dx = (X[q] - X[i]) / Math.Max(1e-9, len);
				dz = (Z[q] - Z[i]) / Math.Max(1e-9, len);
			}

			/// <summary>Squared distance from (x, z) to the centre line (every piece looked at: a course has a few thousand at most).</summary>
			public double Distance2(double x, double z)
			{
				double best = double.MaxValue;
				int pieces = Loop ? Count : Count - 1;
				for (int i = 0; i < pieces; i++)
				{
					int q = (i + 1) % Count;
					double sx = X[q] - X[i], sz = Z[q] - Z[i];
					double len2 = sx * sx + sz * sz;
					double f = len2 > 0.0 ? ((x - X[i]) * sx + (z - Z[i]) * sz) / len2 : 0.0;
					f = f < 0.0 ? 0.0 : f > 1.0 ? 1.0 : f;
					double px = X[i] + sx * f - x, pz = Z[i] + sz * f - z;
					best = Math.Min(best, px * px + pz * pz);
				}
				return best;
			}
		}

		/// <summary>Checkpoints about this far apart (a few hundred metres real), spaced evenly to the finish.</summary>
		internal const double GateSpacing = 1000.0;

		/// <summary>How far past the road's edge a gate's crossing still counts.</summary>
		internal const double GateMargin = 10.0;

		/// <summary>The start box's circle: as a garage's (room for a car to stop in it without aiming).</summary>
		internal const double BoxRadius = 9.0;

		/// <summary>The rally stage's and the hill climb's grid spot is this far behind the start line (the circuit's is its pole slot).</summary>
		internal const double GridBack = 20.0;

		/// <summary>In the service area and the paddock the box is this fraction of the lot's width to one side of the garage's circle in its middle.</summary>
		internal const double BoxAside = 0.3;

		/// <summary>The circuit's laps.</summary>
		internal const int CircuitLaps = 3;

		/// <summary>The highway's start line is this far along the trunk road from the west edge's tunnel mouth, its finish this far short of the east's.</summary>
		internal const double HighwayStart = 500.0;
		internal const double HighwayFinish = 400.0;

		/// <summary>The highway's start box is this far behind its start line, between the grid and the tunnel mouth.</summary>
		internal const double HighwayBoxBack = 200.0;

		/// <summary>
		/// The middle of a carriageway of the trunk road, from its centre line: the road is 66 across, a median 10 wide in the middle,
		/// two lanes of 11 each side and a shoulder of 4 outside them (RoadMesh's texture).
		/// </summary>
		internal const double Carriageway = 17.5;

		/// <summary>How far a highway checkpoint may be moved from its even spacing to put the respawn after it off a bridge.</summary>
		internal const double BridgeShift = 400.0;

		private static readonly object gate = new object();
		private static List<Course> all;

		/// <summary>Every course in the world, once the layout is built.</summary>
		internal static List<Course> All
		{
			get
			{
				lock (gate)
				{
					if (all == null)
					{
						Network.Ensure();
						all = Build();
					}
					return all;
				}
			}
		}

		/// <summary>The map mark's parts: <see cref="MapMark"/> gives one of these for each pixel.</summary>
		internal const int MarkNone = 0, MarkOutline = 1, MarkRing = 2, MarkWhite = 3, MarkBlack = 4;

		/// <summary>The map mark's reach from the start box, in pixels.</summary>
		internal const double MarkReach = 16.5;

		/// <summary>
		/// A start box's mark on the map pictures: a disc of black and white squares 5 pixels on a side, 26 pixels across, in an orange
		/// ring and a thin dark one, 33 pixels across in all, a little larger than the garages' discs (26 with their ring) and laid out
		/// the same way, centred on the box, so it stays inside the round mark a minimap expects. <paramref name="dp"/> and
		/// <paramref name="dq"/> are the pixel's middle less the box's, in pixels (either way up: the squares meet at the middle, so the
		/// disc looks the same flipped). Shared by the map pictures (Streamer) and the preview.
		/// </summary>
		internal static int MapMark(double dp, double dq)
		{
			const double disc = 13.0, ring = 15.0, square = 5.0;
			double d = Math.Sqrt(dp * dp + dq * dq);
			if (d > MarkReach)
			{
				return MarkNone;
			}
			if (d > ring)
			{
				return MarkOutline;
			}
			if (d > disc)
			{
				return MarkRing;
			}
			long i = (long)Math.Floor(dp / square) + (long)Math.Floor(dq / square);
			return (i & 1) == 0 ? MarkWhite : MarkBlack;
		}

		private static List<Course> Build()
		{
			List<Course> list = new List<Course>();
			if (Circuit.Exists)
			{
				list.Add(MakeCircuit());
			}
			if (Rally.Exists && Rally.Road != null)
			{
				list.Add(FromRoad(Kind.Rally, Names.Rally, Rally.X, Rally.Z, Rally.S, Rally.Count, Rally.Length, true, Rally.Half, Rally.Road, Rally.StartS, Rally.Length - Rally.StartS + Rally.FinishS, LotKind.Service));
			}
			if (HillClimb.Exists && HillClimb.Road != null)
			{
				list.Add(FromRoad(Kind.HillClimb, Names.HillClimb, HillClimb.X, HillClimb.Z, HillClimb.S, HillClimb.Count, HillClimb.Length, false, HillClimb.Half, HillClimb.Road, HillClimb.StartS, HillClimb.FinishS - HillClimb.StartS, LotKind.Paddock));
			}
			Course highway = MakeHighway();
			if (highway != null)
			{
				list.Add(highway);
			}
			return list;
		}

		/// <summary>The circuit: <see cref="CircuitLaps"/> laps from a standing start on pole, the start line the finish line too.</summary>
		private static Course MakeCircuit()
		{
			Course c = new Course { Kind = Kind.Circuit, Name = Names.Circuit, Count = Circuit.Count, Length = Circuit.Length, Loop = true, Half = Circuit.Half, StartS = Circuit.StartS, Laps = CircuitLaps };
			c.X = Circuit.X;
			c.Z = Circuit.Z;
			c.S = Circuit.S;
			c.Y = new double[c.Count];
			for (int i = 0; i < c.Count; i++)
			{
				c.Y[i] = Circuit.PlaneAt(c.X[i], c.Z[i]) + Circuit.Lift;
			}
			c.Distance = c.Length * c.Laps;
			// The gates' reach takes in the kerbs and the gravel's inner edge: a car that runs wide still passes.
			Gates(c, Circuit.Half + Circuit.KerbWidth + GateMargin);
			Circuit.Slot(0, out c.GridX, out c.GridZ);
			c.GridY = Circuit.PlaneAt(c.GridX, c.GridZ) + Circuit.Lift;
			c.GridDx = Circuit.DirX;
			c.GridDz = Circuit.DirZ;
			// In the pit lane between the last garage's circle and the end of the lane's full width, midway across it.
			double u = (Circuit.GaragesA + (Circuit.GaragesB - Circuit.GaragesA) * 0.75 + Circuit.PitB) * 0.5, v = (Circuit.PitWallOut + Circuit.PitLaneOut) * 0.5;
			Circuit.Straight(u, v, out c.BoxX, out c.BoxZ);
			c.BoxY = Circuit.PlaneAt(c.BoxX, c.BoxZ) + Circuit.Lift;
			c.BoxDx = Circuit.DirX;
			c.BoxDz = Circuit.DirZ;
			c.BoxRadius = BoxRadius;
			c.BoxGx = Circuit.Px;
			c.BoxGz = Circuit.Pz;
			return c;
		}

		/// <summary>
		/// The rally stage or the hill climb: the course's own centre line, its heights the drawn surface of its road (the road is made
		/// from the same points, so each sample is matched to the road's nearest along the way), the grid <see cref="GridBack"/> behind
		/// the start line, the box in the course's lot (<paramref name="lotKind"/>).
		/// </summary>
		private static Course FromRoad(Kind kind, string name, double[] xs, double[] zs, double[] ss, int count, double length, bool loop, double half, Network.Road road, double startS, double distance, LotKind lotKind)
		{
			Course c = new Course { Kind = kind, Name = name, X = xs, Z = zs, S = ss, Count = count, Length = length, Loop = loop, Half = half, Road = road, StartS = startS, Laps = 1, Distance = distance };
			c.Y = new double[count];
			int k = 0;
			for (int i = 0; i < count; i++)
			{
				// Along the road from the last match: the loop's road closes on its first sample, so the search never runs back.
				while (k + 1 < road.Count && Sq(road.X[k + 1] - xs[i], road.Z[k + 1] - zs[i]) <= Sq(road.X[k] - xs[i], road.Z[k] - zs[i]))
				{
					k++;
				}
				c.Y[i] = RoadY(road, k, xs[i], zs[i]) + Network.Top(road.Kind);
			}
			Gates(c, half + GateMargin);
			c.At(startS - GridBack, out c.GridX, out c.GridZ, out c.GridY, out c.GridDx, out c.GridDz);
			c.BoxDx = c.GridDx;
			c.BoxDz = c.GridDz;
			c.BoxRadius = BoxRadius;
			foreach (Town.Lot lot in Town.Lots)
			{
				if (lot.Kind != lotKind)
				{
					continue;
				}
				// Level with the garage's circle (GarageSites), to the side of it nearer the grid.
				double v = lot.D * 0.3, best = double.MaxValue;
				foreach (int side in new[] { -1, 1 })
				{
					double u = lot.W * BoxAside * side;
					Town.World(lot, u, v, out double x, out double z);
					double d = Sq(x - c.GridX, z - c.GridZ);
					if (d < best)
					{
						best = d;
						c.BoxX = x;
						c.BoxZ = z;
						c.BoxY = Town.Surface(lot, u);
					}
				}
				c.BoxGx = lot.Grade * lot.Dx;
				c.BoxGz = lot.Grade * lot.Dz;
				break;
			}
			return c;
		}

		/// <summary>
		/// The highway: the trunk road (the network's one road of its class, made first, west edge to east edge and on into each edge's
		/// tunnel) from <see cref="HighwayStart"/> past the west tunnel mouth to <see cref="HighwayFinish"/> short of the east one. Its
		/// centre line is the road's own (the median), its gates reach over both carriageways; the grid is in the middle of the
		/// eastbound carriageway (<see cref="Carriageway"/>) <see cref="GridBack"/> behind the start line, the box in the middle of the
		/// westbound one <see cref="HighwayBoxBack"/> behind it, on the road itself (the trunk road has no lay-by, and nothing ever
		/// drives there but a car on its way to the tunnel, which is where the box is wanted). Null if there is no such road.
		/// </summary>
		private static Course MakeHighway()
		{
			Network.Road road = null;
			foreach (Network.Road r in Network.Roads)
			{
				if (r.Kind == RoadKind.Trunk && (road == null || r.Count > road.Count))
				{
					road = r;
				}
			}
			if (road == null || road.Count < 3 || road.Loop)
			{
				return null;
			}
			int n = road.Count;
			// West to east whichever way the road was made.
			bool flip = road.X[0] > road.X[n - 1];
			Course c = new Course { Kind = Kind.Highway, Name = Names.Highway, Count = n, Loop = false, Half = road.Half, Road = road, Laps = 1, Keep = Carriageway };
			c.X = new double[n];
			c.Z = new double[n];
			c.Y = new double[n];
			c.S = new double[n];
			double total = road.S[n - 1] - road.S[0];
			for (int i = 0; i < n; i++)
			{
				int k = flip ? n - 1 - i : i;
				c.X[i] = road.X[k];
				c.Z[i] = road.Z[k];
				// The drawn surface at the centre line, as RoadMesh draws it (Network.Point with no side: no cross-fall).
				c.Y[i] = road.Y[k] + Network.Top(road.Kind);
				c.S[i] = flip ? road.S[n - 1] - road.S[k] : road.S[k] - road.S[0];
			}
			c.Length = total;
			c.StartS = HighwayStart;
			c.Distance = total - HighwayStart - HighwayFinish;
			if (c.Distance < GateSpacing * 2.0)
			{
				return null;
			}
			Gates(c, road.Half + GateMargin);
			OffBridges(c, road, flip);
			c.At(c.StartS - GridBack, out double gx, out double gz, out c.GridY, out c.GridDx, out c.GridDz);
			// Right of the way the car faces is (dz, -dx).
			c.GridX = gx + c.GridDz * Carriageway;
			c.GridZ = gz - c.GridDx * Carriageway;
			c.At(c.StartS - HighwayBoxBack, out double bx, out double bz, out c.BoxY, out c.BoxDx, out c.BoxDz);
			c.BoxX = bx - c.BoxDz * Carriageway;
			c.BoxZ = bz + c.BoxDx * Carriageway;
			c.BoxRadius = BoxRadius;
			// The road's grade along it (no cross-fall away from a mouth).
			c.At(c.StartS - HighwayBoxBack + 10.0, out double _, out double _, out double ahead, out double _, out double _);
			c.At(c.StartS - HighwayBoxBack - 10.0, out double _, out double _, out double behind, out double _, out double _);
			double grade = (ahead - behind) / 20.0;
			c.BoxGx = grade * c.BoxDx;
			c.BoxGz = grade * c.BoxDz;
			return c;
		}

		/// <summary>
		/// Moves each highway checkpoint but the finish, as little as it can (up to <see cref="BridgeShift"/>), so the respawn just
		/// past it (<see cref="RespawnPast"/>) is not on a bridge: the reset puts the car on the ground, which there is the river.
		/// </summary>
		private static void OffBridges(Course c, Network.Road road, bool flip)
		{
			bool Bridged(double along)
			{
				// A car's length either side of the respawn point, by the road's own samples.
				double s0 = c.StartS + along + RespawnPast - 15.0, s1 = c.StartS + along + RespawnPast + 15.0;
				for (int i = 0; i < c.Count; i++)
				{
					if (c.S[i] >= s0 - 10.0 && c.S[i] <= s1 + 10.0 && road.Bridge[flip ? c.Count - 1 - i : i])
					{
						return true;
					}
				}
				return false;
			}
			for (int k = 0; k < c.Gates.Count - 1; k++)
			{
				Gate g = c.Gates[k];
				if (!Bridged(g.Along))
				{
					continue;
				}
				double lo = k > 0 ? c.Gates[k - 1].Along + 50.0 : 50.0, hi = c.Gates[k + 1].Along - 50.0;
				for (double d = 10.0; d <= BridgeShift; d += 10.0)
				{
					double along = g.Along + d <= hi && !Bridged(g.Along + d) ? g.Along + d : g.Along - d >= lo && !Bridged(g.Along - d) ? g.Along - d : -1.0;
					if (along < 0.0)
					{
						continue;
					}
					c.At(c.StartS + along, out g.X, out g.Z, out g.Y, out g.Dx, out g.Dz);
					g.Along = along;
					c.Gates[k] = g;
					break;
				}
			}
		}

		/// <summary>After a wreck in an event the car goes back on the course this far past the last gate passed.</summary>
		internal const double RespawnPast = 6.0;

		private static double Sq(double a, double b)
		{
			return a * a + b * b;
		}

		/// <summary>The road's height at (x, z) near its sample k: along the nearer of the pieces either side.</summary>
		private static double RoadY(Network.Road road, int k, double x, double z)
		{
			double best = double.MaxValue, y = road.Y[k];
			for (int i = Math.Max(0, k - 1); i <= Math.Min(road.Count - 2, k); i++)
			{
				double sx = road.X[i + 1] - road.X[i], sz = road.Z[i + 1] - road.Z[i];
				double len2 = sx * sx + sz * sz;
				double f = len2 > 0.0 ? ((x - road.X[i]) * sx + (z - road.Z[i]) * sz) / len2 : 0.0;
				f = f < 0.0 ? 0.0 : f > 1.0 ? 1.0 : f;
				double d = Sq(road.X[i] + sx * f - x, road.Z[i] + sz * f - z);
				if (d < best)
				{
					best = d;
					y = road.Y[i] + (road.Y[i + 1] - road.Y[i]) * f;
				}
			}
			return y;
		}

		/// <summary>The gates: evenly spaced, about <see cref="GateSpacing"/> apart, over each lap, the last one of each lap on its line.</summary>
		private static void Gates(Course c, double reach)
		{
			double lap = c.Distance / c.Laps;
			int perLap = Math.Max(2, (int)Math.Round(lap / GateSpacing));
			for (int l = 0; l < c.Laps; l++)
			{
				for (int k = 1; k <= perLap; k++)
				{
					double along = lap * l + lap * k / perLap;
					c.At(c.StartS + along, out double x, out double z, out double y, out double dx, out double dz);
					c.Gates.Add(new Gate { Along = along, X = x, Z = z, Y = y, Dx = dx, Dz = dz, Half = reach, LapEnd = k == perLap ? l + 1 : 0 });
				}
			}
		}

		/// <summary>The course whose start box (x, z) is in, or null.</summary>
		internal static Course BoxAt(double x, double z)
		{
			foreach (Course c in All)
			{
				if (Sq(x - c.BoxX, z - c.BoxZ) <= c.BoxRadius * c.BoxRadius)
				{
					return c;
				}
			}
			return null;
		}

		/// <summary>
		/// Whether going from (x0, z0) to (x1, z1) crossed the gate forwards: from behind its line to on or past it, within its reach of
		/// the centre line where it crossed. Backwards over a gate never counts.
		/// </summary>
		internal static bool Crossed(Gate g, double x0, double z0, double x1, double z1)
		{
			double a0 = (x0 - g.X) * g.Dx + (z0 - g.Z) * g.Dz, a1 = (x1 - g.X) * g.Dx + (z1 - g.Z) * g.Dz;
			if (!(a0 < 0.0 && a1 >= 0.0))
			{
				return false;
			}
			double f = a0 / (a0 - a1);
			double cx = x0 + (x1 - x0) * f, cz = z0 + (z1 - z0) * f;
			double across = (cx - g.X) * g.Dz - (cz - g.Z) * g.Dx;
			return Math.Abs(across) <= g.Half;
		}
	}
}
