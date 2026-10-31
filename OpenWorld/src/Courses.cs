using System;
using System.Collections.Generic;

namespace HalfGripOpenWorld
{
	/// <summary>
	/// The events' courses: the circuit (laps from its grid), the rally stage (once round its loop, start line to finish line) and the
	/// hill climb (foot to col), each as a centre line, a start box behind the start line, and checkpoint gates across the road every few
	/// hundred metres to the finish. Read from the layout, never added to it: generation is unchanged, and every player gets the same
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
			/// <summary>Where the start line is along the centre line.</summary>
			public double StartS;
			/// <summary>Laps (1 for the stage and the climb).</summary>
			public int Laps;
			/// <summary>From the start line to the finish, all laps.</summary>
			public double Distance;
			/// <summary>The checkpoints in the order they are passed; the last is the finish line.</summary>
			public readonly List<Gate> Gates = new List<Gate>();
			/// <summary>The start box: a circle behind the start line (on the circuit, pole position), the car facing (BoxDx, BoxDz).</summary>
			public double BoxX;
			public double BoxZ;
			public double BoxY;
			public double BoxDx;
			public double BoxDz;
			public double BoxRadius;
			/// <summary>The surface's rise per unit east and north round the box (the road's grade along it, the circuit's plane's tilt).</summary>
			public double BoxGx;
			public double BoxGz;

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

		/// <summary>The start box's circle, at most: as a garage's (room for a car to stop in it without aiming), less on a narrow road.</summary>
		internal const double BoxRadius = 9.0;

		/// <summary>The rally stage's and the hill climb's start boxes are this far behind the start line (the circuit's is its pole slot).</summary>
		internal const double BoxBack = 20.0;

		/// <summary>The circuit's laps.</summary>
		internal const int CircuitLaps = 3;

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

		private static List<Course> Build()
		{
			List<Course> list = new List<Course>();
			if (Circuit.Exists)
			{
				list.Add(MakeCircuit());
			}
			if (Rally.Exists && Rally.Road != null)
			{
				list.Add(FromRoad(Kind.Rally, Names.Rally, Rally.X, Rally.Z, Rally.S, Rally.Count, Rally.Length, true, Rally.Half, Rally.Road, Rally.StartS, Rally.Length - Rally.StartS + Rally.FinishS));
			}
			if (HillClimb.Exists && HillClimb.Road != null)
			{
				list.Add(FromRoad(Kind.HillClimb, Names.HillClimb, HillClimb.X, HillClimb.Z, HillClimb.S, HillClimb.Count, HillClimb.Length, false, HillClimb.Half, HillClimb.Road, HillClimb.StartS, HillClimb.FinishS - HillClimb.StartS));
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
			Circuit.Slot(0, out c.BoxX, out c.BoxZ);
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
		/// from the same points, so each sample is matched to the road's nearest along the way), the box <see cref="BoxBack"/> behind
		/// the start line.
		/// </summary>
		private static Course FromRoad(Kind kind, string name, double[] xs, double[] zs, double[] ss, int count, double length, bool loop, double half, Network.Road road, double startS, double distance)
		{
			Course c = new Course { Kind = kind, Name = name, X = xs, Z = zs, S = ss, Count = count, Length = length, Loop = loop, Half = half, StartS = startS, Laps = 1, Distance = distance };
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
			c.At(startS - BoxBack, out c.BoxX, out c.BoxZ, out c.BoxY, out c.BoxDx, out c.BoxDz);
			c.BoxRadius = Math.Min(BoxRadius, half - 0.5);
			c.At(startS - BoxBack - 8.0, out double _, out double _, out double y0, out double _, out double _);
			c.At(startS - BoxBack + 8.0, out double _, out double _, out double y1, out double _, out double _);
			double grade = (y1 - y0) / 16.0;
			c.BoxGx = grade * c.BoxDx;
			c.BoxGz = grade * c.BoxDz;
			return c;
		}

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
