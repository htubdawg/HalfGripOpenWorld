using System;
using System.Collections.Generic;
using System.Linq;

namespace HalfGripOpenWorld
{
	/// <summary>
	/// A parked car's way between its spot and the lane (the rework's phase 5): pulling out of a kerb space forwards, backing out of a
	/// nose-in bay or a pad facing the house and then driving off, pulling into a space ahead, or drawing up past one behind and reversing
	/// into it, as drivers do. A path of one or two legs (cubic curves, each driven one way: forwards or in reverse), the car's middle and
	/// the way its nose faces along it, no tighter than a car turns (<see cref="TurnRadius"/>), and timed as a driver drives it: from
	/// standing, at a walking pace in reverse and a crawl forwards, standing a moment where it changes gear, standing at the end.
	///
	/// <para>The micro holds a car pulling out at its place in the lane while its drawn copy drives this from its spot (<see
	/// cref="Micro.PullOutTime"/>); one that has pulled up at its stop drives it into its spot after. While its body is on the road the
	/// micro's drivers see it as a body (<see cref="TrafficLive"/>). Spots are each machine's own (<see cref="TrafficParking"/>), so this
	/// needn't be deterministic. No Unity types.</para>
	/// </summary>
	internal sealed class Manoeuvre
	{
		/// <summary>The tightest a car's rear axle turns (about 4.2 m: a family car on full lock, an 11.5 m turning circle).</summary>
		internal const double TurnRadius = 14.0;
		/// <summary>The fastest it goes forwards and in reverse while manoeuvring (4 m/s and 2 m/s).</summary>
		internal const double Forwards = 4.0 * 3.3, Reverse = 2.0 * 3.3;
		/// <summary>How hard it pulls away and stops (gentler than on the road: 1.2 m/s² and 1.5 m/s²).</summary>
		internal const double Pull = 1.2 * 3.3, Stop = 1.5 * 3.3;
		/// <summary>How long it stands where it changes gear (seconds).</summary>
		internal const double GearPause = 0.6;
		/// <summary>Points sampled along each leg.</summary>
		private const int Samples = 32;

		/// <summary>One leg: its points (the car's middle) with their distance along it, the way the nose faces, forwards or in reverse, and its time.</summary>
		internal sealed class Leg
		{
			public double[] X, Z, Hx, Hz, D;
			public bool Back;
			public double Length, Top, Time, Start;
			/// <summary>The tightest radius on it.</summary>
			public double MinRadius;
		}

		internal readonly List<Leg> Legs = new List<Leg>();

		/// <summary>How long it takes, from standing to standing.</summary>
		internal double Duration;

		/// <summary>No way clear of the parked cars, the houses and the water was found: this is the quickest regardless (trafficsim counts those; <see cref="TrafficLive"/> looks for another place on the lane first).</summary>
		internal bool Hemmed;

		/// <summary>The tightest radius on any leg.</summary>
		internal double MinRadius = double.MaxValue;

		/// <summary>How long it is, all legs.</summary>
		internal double Length;

		/// <summary>
		/// Pulling out: from the spot (middle (<paramref name="sx"/>, <paramref name="sz"/>), nose facing (<paramref name="shx"/>, <paramref
		/// name="shz"/>)) to standing in the lane at (<paramref name="ex"/>, <paramref name="ez"/>) facing (<paramref name="ehx"/>, <paramref
		/// name="ehz"/>), clear of the cars parked round it (<paramref name="parked"/>; may be null), joining the lane no further back than
		/// <paramref name="laneBack"/> behind where it is to stand (the stretch the cars coming leave it).
		/// </summary>
		internal static Manoeuvre Out(double sx, double sz, double shx, double shz, double ex, double ez, double ehx, double ehz, List<TrafficParking.Spot> parked = null, double laneBack = 0.0)
		{
			return Plan(sx, sz, shx, shz, ex, ez, ehx, ehz, parked, false, laneBack);
		}

		/// <summary>
		/// Pulling in: from standing in the lane at (<paramref name="ex"/>, <paramref name="ez"/>) facing (<paramref name="ehx"/>, <paramref
		/// name="ehz"/>) into the spot, clear of the cars parked round it. Worked out as the way out driven backwards in time (the same
		/// ground covered, forwards and reverse swapped): a space it would back out of it drives into, one at the kerb it draws up past and
		/// reverses into.
		/// </summary>
		internal static Manoeuvre In(double ex, double ez, double ehx, double ehz, double sx, double sz, double shx, double shz, List<TrafficParking.Spot> parked = null)
		{
			return Plan(sx, sz, shx, shz, ex, ez, ehx, ehz, parked, true, 0.0);
		}

		/// <summary>How far behind its middle a car's rear axle is, which it turns about (its nose swings out, its tail follows in).</summary>
		internal const double RearAxle = 4.4;

		/// <summary>The turning radii tried, as multiples of <see cref="TurnRadius"/>: full lock where it must, gentler (and quicker) where there is room.</summary>
		private static readonly double[] Radii = { 2.2, 1.6, 1.25, 1.0 };

		/// <summary>Samples along a leg: one every this many units of the rear axle's path, and no fewer than <see cref="Samples"/>.</summary>
		private const double SampleStep = 2.0;

		/// <summary>How far back along the lane, in car lengths, a way out may join it and drive straight on to where it stands (a Reeds–Shepp way ends on its last arc, beside whatever is parked there), as far as the caller allows (the lane it runs along must be kept clear of the cars coming).</summary>
		private static readonly double[] LaneBack = { 0.0, 0.7, 1.4, 2.1 };

		/// <summary>How far ahead of the spot, in car lengths, a car hemmed in pulls out into its lane before turning there.</summary>
		private static readonly double[] Aheads = { 0.6, 1.0, 1.5, 2.2, 3.0, 4.5 };

		/// <summary>The least a car pulled out beside the spot is off it sideways: clear of a car parked in line ahead (a car's width and a gap, as a car passing one at the kerb).</summary>
		private const double Abreast = TrafficParking.CarWide + 1.2;

		/// <summary>The most moves a turn in the road takes (a five-point turn and one to spare).</summary>
		private const int MostMoves = 6;

		/// <summary>A rear axle pose: where, and the way the nose faces (unit).</summary>
		private struct Pose
		{
			public double X, Z, Hx, Hz;

			public Pose(double x, double z, double hx, double hz)
			{
				X = x;
				Z = z;
				Hx = hx;
				Hz = hz;
			}

			/// <summary>Moved <paramref name="ahead"/> along the way it faces and <paramref name="left"/> to its left.</summary>
			public Pose Moved(double ahead, double left)
			{
				return new Pose(X + Hx * ahead - Hz * left, Z + Hz * ahead + Hx * left, Hx, Hz);
			}
		}

		/// <summary>A Reeds–Shepp word from a pose at a radius, timed as driven (without sampling it: most are never drawn).</summary>
		private struct Word
		{
			public ReedsShepp.Seg[] W;
			public double R, Duration;
			public Pose From;
		}

		/// <summary>
		/// The way between the spot (s) and the lane (e), out of the spot or (<paramref name="into"/>) into it, worked out for the car's rear
		/// axle (the nose swinging out as a real car's does): the quickest as a driver drives it that keeps clear of the parked cars, the houses
		/// and the water, of the Reeds–Shepp ways (<see cref="ReedsShepp"/>: arcs no tighter than a car turns and straights, forwards and in
		/// reverse, three-point turns among them) at a few radii, straight from the spot; hemmed in, through the lane beside it (out into it and
		/// on, or drawing up in it and backing in); then each of those from the spot backed straight up a little first; then turning in the
		/// road in as many moves as it takes (<see cref="Shuffled"/>). Else the quickest (trafficsim counts those).
		/// </summary>
		private static Manoeuvre Plan(double sx, double sz, double shx, double shz, double ex, double ez, double ehx, double ehz, List<TrafficParking.Spot> parked, bool into, double laneBack)
		{
			long started = System.Diagnostics.Stopwatch.GetTimestamp();
			int collections = GC.CollectionCount(2);
			budget = Budget;
			Manoeuvre m = Planned(sx, sz, shx, shz, ex, ez, ehx, ehz, parked, into, laneBack);
			double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
			lock (Costs)
			{
				Costs.Plans++;
				Costs.Ms += ms;
				if (GC.CollectionCount(2) == collections)
				{
					// The worst of those no full collection paused (its pause is the runtime's, not the planning's).
					Costs.Worst = Math.Max(Costs.Worst, ms);
				}
			}
			return m;
		}

		/// <summary>What planning ways has cost (for trafficsim): how many, their total and the worst, in milliseconds.</summary>
		internal sealed class Cost
		{
			public int Plans;
			public double Ms, Worst;
		}

		internal static readonly Cost Costs = new Cost();

		/// <summary>How many words one plan may build and test before it gives up looking (about 10 ms): hemmed in where no way is clear, it would try thousands.</summary>
		private const int Budget = 800;

		/// <summary>The words this plan may still build (each thread plans its own).</summary>
		[ThreadStatic]
		private static int budget;

		private static Manoeuvre Planned(double sx, double sz, double shx, double shz, double ex, double ez, double ehx, double ehz, List<TrafficParking.Spot> parked, bool into, double laneBack)
		{
			Unit(ref shx, ref shz);
			Unit(ref ehx, ref ehz);
			Pose p = new Pose(sx - shx * RearAxle, sz - shz * RearAxle, shx, shz), l = new Pose(ex - ehx * RearAxle, ez - ehz * RearAxle, ehx, ehz);
			// Into the spot by a turn in the road: that way out driven backwards in time.
			Manoeuvre m = Shifted(p, l, parked, into, q => (into ? InFrom(q, l, parked) : OutFrom(q, l, parked, laneBack)) ?? (into ? Reversed(Shuffled(q, l, parked, 0.0)) : Shuffled(q, l, parked, laneBack)));
			if (m != null)
			{
				return m;
			}
			// None clear: the quickest (trafficsim counts it). There is always a Reeds–Shepp way.
			List<Word> ways = into ? Words(l, p) : Words(p, l);
			m = ways.Count > 0 ? Build(ways[0]) : Build(new Word { W = new[] { new ReedsShepp.Seg { Kind = 'S', Length = Math.Max(1e-3, Dist(p.X, p.Z, l.X, l.Z)) } }, R = 1.0, From = p });
			m.Hemmed = true;
			return m;
		}

		/// <summary>
		/// <paramref name="plan"/> from the spot (p), else from the spot backed straight up a little (1.5, then 3), the straight back added
		/// (out first; into it, last, drawing forwards into the spot). Null if none is clear.
		/// </summary>
		private static Manoeuvre Shifted(Pose p, Pose l, List<TrafficParking.Spot> parked, bool into, Func<Pose, Manoeuvre> plan)
		{
			foreach (double shift in new[] { 0.0, 1.5, 3.0 })
			{
				Pose q = p.Moved(-shift, 0.0);
				// Backed up first: the way from where it backs to (into the spot: the way to there, then drawing forwards into it).
				Manoeuvre edge = shift > 0.0 ? (into ? Straight(q, shift) : Straight(p, -shift)) : null;
				if (edge != null && !edge.ClearOf(parked))
				{
					continue;
				}
				Manoeuvre m = plan(q);
				if (m == null)
				{
					continue;
				}
				return edge == null ? m : into ? Join(m, edge) : Join(edge, m);
			}
			return null;
		}

		/// <summary>Out of the spot (rear axle pose q) onto the lane at l: straight there, else out into the lane beside the spot and on (as a driver does where a turn from the space would swing into the cars parked round it). Null if none is clear.</summary>
		private static Manoeuvre OutFrom(Pose q, Pose l, List<TrafficParking.Spot> parked, double laneBack)
		{
			Manoeuvre best = ToLane(q, l, parked, double.MaxValue, laneBack);
			if (best != null)
			{
				return best;
			}
			foreach (Pose v in Beside(q, l))
			{
				Manoeuvre f = FirstClear(Words(q, v), parked, double.MaxValue), t = f == null ? null : ToLane(v, l, parked, (best?.Duration ?? double.MaxValue) - f.Duration, laneBack);
				if (t != null && (best == null || f.Duration + t.Duration < best.Duration))
				{
					best = Join(f, t);
				}
			}
			return best;
		}

		/// <summary>From the lane at l into the spot (rear axle pose q): straight in, else drawing up in the lane beside it first and backing in. Null if none is clear.</summary>
		private static Manoeuvre InFrom(Pose q, Pose l, List<TrafficParking.Spot> parked)
		{
			Manoeuvre best = FirstClear(Words(l, q), parked, double.MaxValue);
			if (best != null)
			{
				return best;
			}
			foreach (Pose v in Beside(q, l))
			{
				Manoeuvre f = FirstClear(Words(l, v), parked, best?.Duration ?? double.MaxValue), t = f == null ? null : FirstClear(Words(v, q), parked, (best?.Duration ?? double.MaxValue) - f.Duration);
				if (t != null && (best == null || f.Duration + t.Duration < best.Duration))
				{
					best = Join(f, t);
				}
			}
			return best;
		}

		/// <summary>Poses in the lane beside the spot (rear axle pose q), facing as it does, a little ahead of it: towards the lane at l, a third and a half of the way across.</summary>
		private static IEnumerable<Pose> Beside(Pose q, Pose l)
		{
			double across = -(l.X - q.X) * q.Hz + (l.Z - q.Z) * q.Hx, side = across >= 0.0 ? 1.0 : -1.0;
			foreach (double ahead in Aheads)
			{
				foreach (double share in new[] { 0.3, 0.55 })
				{
					yield return q.Moved(ahead * Traffic.CarLength, side * Math.Max(Abreast, Math.Abs(across) * share));
				}
			}
		}

		/// <summary>From pose a onto the lane at l, joining it at l or else a little back along it and driving straight on (<see cref="LaneBack"/>, no further than <paramref name="laneBack"/>): the quickest clear way of the nearest join that has one, quicker than <paramref name="under"/>; null if none.</summary>
		private static Manoeuvre ToLane(Pose a, Pose l, List<TrafficParking.Spot> parked, double under, double laneBack)
		{
			Manoeuvre best = null;
			foreach (double back in LaneBack)
			{
				double d = back * Traffic.CarLength;
				if (d > laneBack)
				{
					break;
				}
				Pose j = l.Moved(-d, 0.0);
				if (d > 0.0 && (j.X - a.X) * l.Hx + (j.Z - a.Z) * l.Hz < 0.0)
				{
					// Joining the lane behind where it is: back along the lane, never.
					continue;
				}
				Manoeuvre on = d > 0.0 ? Straight(j, d) : null;
				if (on != null && !on.ClearOf(parked))
				{
					continue;
				}
				double limit = Math.Min(under, best?.Duration ?? double.MaxValue) - (on?.Duration ?? 0.0) + (on != null ? 2.0 : 0.0);
				Manoeuvre m = FirstClear(Words(a, j), parked, limit);
				if (m == null)
				{
					continue;
				}
				m = on != null ? Join(m, on) : m;
				if (m.Duration < under)
				{
					// The nearest join that is clear: running along the lane behind where it comes onto it, it holds up the cars queued there.
					return m;
				}
			}
			return best;
		}

		/// <summary>
		/// Out of the spot (rear axle pose q) to the lane (l) where the lane goes back the way the car faces and there is no room to swing
		/// round: out into its lane a little ahead (as <see cref="OutFrom"/>), then turning on full lock, forwards then back then forwards,
		/// each move driven until the car would touch a parked car, a house or the water (or has turned a quarter), until a way onto the lane is
		/// clear (<see cref="ToLane"/>). The quickest such; null if none.
		/// </summary>
		private static Manoeuvre Shuffled(Pose q, Pose l, List<TrafficParking.Spot> parked, double laneBack)
		{
			double across = -(l.X - q.X) * q.Hz + (l.Z - q.Z) * q.Hx, side = across >= 0.0 ? 1.0 : -1.0;
			Manoeuvre best = null;
			// Further along its lane too, to turn just short of where it joins the other (a street too narrow to turn in is turned in at its end).
			double far = ((l.X - q.X) * q.Hx + (l.Z - q.Z) * q.Hz) / Traffic.CarLength - 1.0;
			foreach (double ahead in far > Aheads[Aheads.Length - 1] + 1.0 ? Aheads.Concat(new[] { far - 2.0, far }) : Aheads)
			{
				Pose v = q.Moved(ahead * Traffic.CarLength, side * Math.Max(Abreast, Math.Abs(across) * 0.3));
				Manoeuvre way = FirstClear(Words(q, v), parked, best?.Duration ?? double.MaxValue);
				if (way == null)
				{
					continue;
				}
				double x = v.X, z = v.Z, th = DetMath.Atan2(v.Hz, v.Hx);
				for (int move = 0; move < MostMoves && (best == null || way.Duration < best.Duration); move++)
				{
					Manoeuvre end = ToLane(new Pose(x, z, DetMath.Cos(th), DetMath.Sin(th)), l, parked, (best?.Duration ?? double.MaxValue) - way.Duration, laneBack);
					if (end != null)
					{
						best = Join(way, end);
						break;
					}
					// Turning towards the lane's way: forwards on full lock that way, back on the other lock (the nose swinging on round).
					bool fwd = move % 2 == 0;
					char kind = fwd == (side > 0.0) ? 'L' : 'R';
					double sign = fwd ? 1.0 : -1.0, r = TurnRadius, step = 0.5 / r, turned = 0.0;
					double cx = x, cz = z, cth = th;
					// No further round than the lane's way.
					double left = (side > 0.0 ? DetMath.Atan2(l.Hz, l.Hx) - th : th - DetMath.Atan2(l.Hz, l.Hx)) % (2.0 * Math.PI);
					left = left < 0.0 ? left + 2.0 * Math.PI : left;
					while (turned < Math.Min(0.5 * Math.PI, left) && turned * r < 40.0)
					{
						double nx = cx, nz = cz, nth = cth;
						ReedsShepp.Step(kind, sign * step, ref nx, ref nz, ref nth);
						if (!PoseClear(r * (nx - cx) + cx, r * (nz - cz) + cz, DetMath.Cos(nth), DetMath.Sin(nth), parked, 0.2))
						{
							break;
						}
						(cx, cz, cth) = (r * (nx - cx) + cx, r * (nz - cz) + cz, nth);
						turned += step;
					}
					if (turned * r < 1.0)
					{
						break;
					}
					way = Join(way, Build(new Word { W = new[] { new ReedsShepp.Seg { Kind = kind, Length = sign * turned } }, R = r, From = new Pose(x, z, DetMath.Cos(th), DetMath.Sin(th)) }));
					(x, z, th) = (cx, cz, cth);
				}
			}
			return best;
		}

		/// <summary>Whether a car with its rear axle at (<paramref name="rx"/>, <paramref name="rz"/>) facing (hx, hz) is clear of the parked cars (by <paramref name="margin"/>), the houses and the water.</summary>
		private static bool PoseClear(double rx, double rz, double hx, double hz, List<TrafficParking.Spot> parked, double margin)
		{
			TrafficParking.Spot me = new TrafficParking.Spot { X = rx + hx * RearAxle, Z = rz + hz * RearAxle, Hx = hx, Hz = hz };
			if (parked != null)
			{
				foreach (TrafficParking.Spot o in parked)
				{
					if (Math.Abs(o.X - me.X) < 20.0 && Math.Abs(o.Z - me.Z) < 20.0 && TrafficParking.Overlap(me, o, margin))
					{
						return false;
					}
				}
			}
			return !TrafficParking.InHouse(me, -0.5 + margin) && !Rivers.Water(me.X, me.Z, out double _);
		}

		/// <summary>Whether every leg keeps clear of the cars parked at <paramref name="parked"/>, the houses and the water.</summary>
		internal bool ClearOf(List<TrafficParking.Spot> parked)
		{
			return Legs.TrueForAll(l => Clear(l, parked));
		}

		/// <summary>How many of the quickest words between two poses are tried for one clear of the parked cars (the slower ones are the same shapes the long way round, and each tried costs).</summary>
		private const int Tried = 8;

		/// <summary>The quickest of the first <see cref="Tried"/> of <paramref name="ways"/> (sorted quickest first) clear of the parked cars, quicker than <paramref name="under"/> seconds; null if none.</summary>
		private static Manoeuvre FirstClear(List<Word> ways, List<TrafficParking.Spot> parked, double under)
		{
			for (int k = 0; k < Math.Min(Tried, ways.Count); k++)
			{
				Word w = ways[k];
				if (w.Duration >= under || budget-- <= 0)
				{
					return null;
				}
				Manoeuvre m = Build(w);
				if (m != null && m.ClearOf(parked))
				{
					return m;
				}
			}
			return null;
		}

		/// <summary>A straight leg from rear axle pose a, <paramref name="d"/> forwards (negative: in reverse).</summary>
		private static Manoeuvre Straight(Pose a, double d)
		{
			return Build(new Word { W = new[] { new ReedsShepp.Seg { Kind = 'S', Length = d } }, R = 1.0, From = a });
		}

		/// <summary>A way driven backwards in time: the same ground the other way, its legs in the other order, forwards and reverse swapped.</summary>
		private static Manoeuvre Reversed(Manoeuvre w)
		{
			if (w == null)
			{
				return null;
			}
			Manoeuvre m = new Manoeuvre();
			for (int k = w.Legs.Count - 1; k >= 0; k--)
			{
				Leg l = w.Legs[k];
				int n = l.X.Length;
				Leg r = new Leg { X = new double[n], Z = new double[n], Hx = new double[n], Hz = new double[n], D = new double[n], Back = !l.Back, Length = l.Length, MinRadius = l.MinRadius };
				for (int j = 0; j < n; j++)
				{
					r.X[j] = l.X[n - 1 - j];
					r.Z[j] = l.Z[n - 1 - j];
					r.Hx[j] = l.Hx[n - 1 - j];
					r.Hz[j] = l.Hz[n - 1 - j];
					r.D[j] = l.Length - l.D[n - 1 - j];
				}
				m.Legs.Add(r);
			}
			m.Time();
			return m;
		}

		/// <summary>Two ways one after the other: the second's first leg run on from the first's last where both go the same way (no stop between), timed afresh.</summary>
		private static Manoeuvre Join(Manoeuvre f, Manoeuvre t)
		{
			Manoeuvre m = new Manoeuvre();
			m.Legs.AddRange(f.Legs);
			for (int k = 0; k < t.Legs.Count; k++)
			{
				Leg l = t.Legs[k], last = m.Legs[m.Legs.Count - 1];
				if (k == 0 && l.Back == last.Back)
				{
					int a = last.X.Length, b = l.X.Length - 1;
					Leg j = new Leg { X = new double[a + b], Z = new double[a + b], Hx = new double[a + b], Hz = new double[a + b], D = new double[a + b], Back = l.Back, Length = last.Length + l.Length, MinRadius = Math.Min(last.MinRadius, l.MinRadius) };
					for (int q = 0; q < a + b; q++)
					{
						bool mine = q < a;
						Leg src = mine ? last : l;
						int i = mine ? q : q - a + 1;
						j.X[q] = src.X[i];
						j.Z[q] = src.Z[i];
						j.Hx[q] = src.Hx[i];
						j.Hz[q] = src.Hz[i];
						j.D[q] = mine ? src.D[i] : last.Length + src.D[i];
					}
					m.Legs[m.Legs.Count - 1] = j;
				}
				else
				{
					m.Legs.Add(l);
				}
			}
			m.Time();
			return m;
		}

		/// <summary>The Reeds–Shepp words (<see cref="ReedsShepp"/>) for the rear axle from pose a to pose b at each of <see cref="Radii"/>, timed, quickest first.</summary>
		private static List<Word> Words(Pose a, Pose b)
		{
			// The end in the start's frame: x ahead, y to the left (left of (hx, hz) is (-hz, hx)).
			double dx = b.X - a.X, dz = b.Z - a.Z;
			double fx = dx * a.Hx + dz * a.Hz, fy = -dx * a.Hz + dz * a.Hx;
			double phi = DetMath.Atan2(-b.Hx * a.Hz + b.Hz * a.Hx, b.Hx * a.Hx + b.Hz * a.Hz);
			List<Word> ways = new List<Word>();
			foreach (double k in Radii)
			{
				double r = TurnRadius * k;
				foreach (ReedsShepp.Seg[] w in ReedsShepp.All(fx / r, fy / r, phi))
				{
					double t = WordTime(w, r);
					if (t > 0.0)
					{
						ways.Add(new Word { W = w, R = r, Duration = t, From = a });
					}
				}
			}
			ways.Sort((p, q) => p.Duration.CompareTo(q.Duration));
			return ways;
		}

		/// <summary>How long word <paramref name="w"/> at radius <paramref name="r"/> takes as <see cref="Time"/> times it (its runs one way as legs), a long way in reverse reckoned slower (<see cref="LongBack"/>), to choose by; 0 if it is nothing but a point.</summary>
		private static double WordTime(ReedsShepp.Seg[] w, double r)
		{
			double total = 0.0;
			int legs = 0, k = 0;
			while (k < w.Length)
			{
				if (Math.Abs(w[k].Length) * r < 1e-3)
				{
					k++;
					continue;
				}
				bool back = w[k].Length < 0.0, arc = false;
				double length = 0.0;
				while (k < w.Length && (Math.Abs(w[k].Length) * r < 1e-3 || (w[k].Length < 0.0) == back))
				{
					length += Math.Abs(w[k].Length) * r;
					arc |= w[k].Kind != 'S' && Math.Abs(w[k].Length) * r >= 1e-3;
					k++;
				}
				total += LegTime(length, back, arc ? r : double.MaxValue, out double _) + (legs > 0 ? GearPause : 0.0);
				if (back && length > LongBack)
				{
					// A driver backs a little way and then goes forwards: a long way in reverse is reckoned slower than it is driven.
					total += (length - LongBack) / Reverse * 2.0;
				}
				if (back && arc && r < TurnRadius * 1.2 && length > Traffic.CarLength)
				{
					// And on full lock only a short way (a long tight curve in reverse is hard to hold).
					total += (length - Traffic.CarLength) / Reverse;
				}
				legs++;
			}
			return total;
		}

		/// <summary>How far in reverse a way goes before it is reckoned slower than it is (<see cref="WordTime"/>): a car's length and a half.</summary>
		private const double LongBack = Traffic.CarLength * 1.5;

		/// <summary>A Reeds–Shepp word built: sampled and timed.</summary>
		private static Manoeuvre Build(Word w)
		{
			return FromWord(w.W, w.R, w.From.X, w.From.Z, w.From.Hx, w.From.Hz);
		}

		/// <summary>
		/// A Reeds–Shepp word (lengths in radii of <paramref name="r"/>) driven by the rear axle from (<paramref name="ax"/>, <paramref
		/// name="az"/>) facing (<paramref name="hx"/>, <paramref name="hz"/>): its runs one way, forwards or in reverse, as legs (the gear changed
		/// between), timed. Null if it is nothing but a point.
		/// </summary>
		private static Manoeuvre FromWord(ReedsShepp.Seg[] w, double r, double ax, double az, double hx, double hz)
		{
			Manoeuvre m = new Manoeuvre();
			double th = DetMath.Atan2(hz, hx), x = ax, z = az;
			int k = 0;
			while (k < w.Length)
			{
				if (Math.Abs(w[k].Length) * r < 1e-3)
				{
					k++;
					continue;
				}
				// A run of segments driven the same way.
				bool back = w[k].Length < 0.0;
				int end = k;
				double length = 0.0;
				while (end < w.Length && (Math.Abs(w[end].Length) * r < 1e-3 || (w[end].Length < 0.0) == back))
				{
					length += Math.Abs(w[end].Length) * r;
					end++;
				}
				int n = Math.Max(Samples, (int)Math.Ceiling(length / SampleStep));
				Leg l = new Leg { X = new double[n + 1], Z = new double[n + 1], Hx = new double[n + 1], Hz = new double[n + 1], D = new double[n + 1], Back = back, Length = length, MinRadius = double.MaxValue };
				// Each segment's share of the samples by its length; the pose carried along in radii from the run's start.
				double lx = 0.0, ly = 0.0, lth = th, done = 0.0;
				int seg = k;
				double segDone = 0.0;
				for (int j = 0; j <= n; j++)
				{
					double want = length * j / n;
					while (seg < end && done + (Math.Abs(w[seg].Length) * r - segDone) < want - 1e-12)
					{
						double rest = Math.Abs(w[seg].Length) * r - segDone;
						ReedsShepp.Step(w[seg].Kind, (back ? -rest : rest) / r, ref lx, ref ly, ref lth);
						done += rest;
						segDone = 0.0;
						seg++;
					}
					if (seg < end)
					{
						double step = want - done;
						ReedsShepp.Step(w[seg].Kind, (back ? -step : step) / r, ref lx, ref ly, ref lth);
						done += step;
						segDone += step;
						if (w[seg].Kind != 'S')
						{
							l.MinRadius = r;
						}
					}
					double rx = x + lx * r, rz = z + ly * r;
					double nhx = DetMath.Cos(lth), nhz = DetMath.Sin(lth);
					l.Hx[j] = nhx;
					l.Hz[j] = nhz;
					l.X[j] = rx + nhx * RearAxle;
					l.Z[j] = rz + nhz * RearAxle;
					l.D[j] = want;
					x = j == n ? rx : x;
					z = j == n ? rz : z;
				}
				// The run's start was carried in radii from (x, z) as it was; move on from its end.
				th = lth;
				m.Legs.Add(l);
				k = end;
			}
			if (m.Legs.Count == 0)
			{
				return null;
			}
			m.Time();
			return m;
		}

		/// <summary>Whether leg <paramref name="l"/> keeps clear of every parked car (a hand's breadth between), the houses and the water: every fourth sample first (most ways that touch something are found out at once), then the rest.</summary>
		private static bool Clear(Leg l, List<TrafficParking.Spot> parked)
		{
			// Only the parked cars the leg passes near.
			List<TrafficParking.Spot> near = null;
			if (parked != null && parked.Count > 0)
			{
				double x0 = double.MaxValue, x1 = double.MinValue, z0 = double.MaxValue, z1 = double.MinValue;
				for (int j = 0; j < l.X.Length; j++)
				{
					x0 = Math.Min(x0, l.X[j]);
					x1 = Math.Max(x1, l.X[j]);
					z0 = Math.Min(z0, l.Z[j]);
					z1 = Math.Max(z1, l.Z[j]);
				}
				foreach (TrafficParking.Spot o in parked)
				{
					if (o.X > x0 - 16.0 && o.X < x1 + 16.0 && o.Z > z0 - 16.0 && o.Z < z1 + 16.0)
					{
						(near ??= new List<TrafficParking.Spot>()).Add(o);
					}
				}
			}
			for (int pass = 0; pass < 2; pass++)
			{
				for (int j = pass == 0 ? 0 : 2; j < l.X.Length; j += 4)
				{
					if (!SampleClear(l, j, near) || (pass == 1 && (!SampleClear(l, j - 1, near) || (j + 1 < l.X.Length && !SampleClear(l, j + 1, near)))))
					{
						return false;
					}
				}
			}
			return SampleClear(l, l.X.Length - 1, near);
		}

		/// <summary>Whether sample <paramref name="j"/> of leg <paramref name="l"/> is clear (<see cref="Clear"/>; the houses and the water at even samples only, a car at the kerb standing right up to a terrace's front).</summary>
		private static bool SampleClear(Leg l, int j, List<TrafficParking.Spot> parked)
		{
			TrafficParking.Spot me = new TrafficParking.Spot { X = l.X[j], Z = l.Z[j], Hx = l.Hx[j], Hz = l.Hz[j] };
			if (parked != null)
			{
				foreach (TrafficParking.Spot o in parked)
				{
					if (Math.Abs(o.X - me.X) < 20.0 && Math.Abs(o.Z - me.Z) < 20.0 && TrafficParking.Overlap(me, o, 0.1))
					{
						return false;
					}
				}
			}
			return j % 2 != 0 || (!TrafficParking.InHouse(me, -0.5) && !Rivers.Water(me.X, me.Z, out double _));
		}

		/// <summary>Each leg's time (<see cref="LegTime"/>), a pause between legs.</summary>
		private void Time()
		{
			Duration = 0.0;
			Length = 0.0;
			MinRadius = double.MaxValue;
			for (int k = 0; k < Legs.Count; k++)
			{
				Leg l = Legs[k];
				if (k > 0)
				{
					Duration += GearPause;
				}
				l.Time = LegTime(l.Length, l.Back, l.MinRadius, out l.Top);
				l.Start = Duration;
				Duration += l.Time;
				Length += l.Length;
				MinRadius = Math.Min(MinRadius, l.MinRadius);
			}
		}

		/// <summary>A leg's time: from standing up to its top speed (<paramref name="top"/>: no faster than its tightest bend allows) and back to standing.</summary>
		private static double LegTime(double length, bool back, double minRadius, out double top)
		{
			// Round a tight bend no more than 1.5 m/s² across it.
			top = Math.Min(back ? Reverse : Forwards, Math.Sqrt(1.5 * 3.3 * Math.Max(1.0, minRadius)));
			double dUp = top * top / (2.0 * Pull), dDown = top * top / (2.0 * Stop);
			if (dUp + dDown > length)
			{
				top = Math.Sqrt(2.0 * length * Pull * Stop / (Pull + Stop));
				dUp = top * top / (2.0 * Pull);
				dDown = top * top / (2.0 * Stop);
			}
			return top > 1e-9 ? top / Pull + top / Stop + (length - dUp - dDown) / top : 0.0;
		}

		/// <summary>
		/// Where the car is <paramref name="t"/> seconds in: its middle, the way its nose faces, and its velocity (world units a second;
		/// backwards in reverse).
		/// </summary>
		internal void At(double t, out double x, out double z, out double hx, out double hz, out double vx, out double vz)
		{
			t = Math.Max(0.0, t);
			Leg l = Legs[Legs.Count - 1];
			double d = l.Length, sp = 0.0;
			for (int k = 0; k < Legs.Count; k++)
			{
				Leg o = Legs[k];
				if (t < o.Start)
				{
					// Standing where the last leg ended, changing gear.
					l = Legs[k - 1];
					d = l.Length;
					sp = 0.0;
					break;
				}
				if (t <= o.Start + o.Time || k == Legs.Count - 1)
				{
					l = o;
					d = Along(o, Math.Max(0.0, Math.Min(o.Time, t - o.Start)), out sp);
					break;
				}
			}
			int lo = 0, hi = l.D.Length - 1;
			while (hi - lo > 1)
			{
				int mid = (lo + hi) >> 1;
				if (l.D[mid] <= d)
				{
					lo = mid;
				}
				else
				{
					hi = mid;
				}
			}
			double span = l.D[hi] - l.D[lo], f = span > 1e-12 ? Math.Max(0.0, Math.Min(1.0, (d - l.D[lo]) / span)) : 0.0;
			x = l.X[lo] + (l.X[hi] - l.X[lo]) * f;
			z = l.Z[lo] + (l.Z[hi] - l.Z[lo]) * f;
			hx = l.Hx[lo] + (l.Hx[hi] - l.Hx[lo]) * f;
			hz = l.Hz[lo] + (l.Hz[hi] - l.Hz[lo]) * f;
			Unit(ref hx, ref hz);
			// Moving along the curve: the nose's way forwards, against it in reverse.
			double s = l.Back ? -sp : sp;
			vx = hx * s;
			vz = hz * s;
		}

		/// <summary>How far along leg <paramref name="l"/> the car is <paramref name="t"/> seconds into it, and its speed.</summary>
		private static double Along(Leg l, double t, out double speed)
		{
			double top = l.Top;
			if (top <= 1e-9)
			{
				speed = 0.0;
				return 0.0;
			}
			double tUp = top / Pull, tDown = top / Stop, tFlat = Math.Max(0.0, l.Time - tUp - tDown);
			if (t < tUp)
			{
				speed = Pull * t;
				return 0.5 * Pull * t * t;
			}
			double dUp = 0.5 * Pull * tUp * tUp;
			if (t < tUp + tFlat)
			{
				speed = top;
				return dUp + top * (t - tUp);
			}
			double r = Math.Max(0.0, l.Time - t);
			speed = Stop * r;
			return Math.Max(0.0, l.Length - 0.5 * Stop * r * r);
		}

		// ---- a body driving the way (a physics car pulling out or in) ---------------------------------------------------------

		/// <summary>How far ahead of the rear axle along its leg a body driving the way aims (units).</summary>
		internal const double FollowLook = 7.0;

		/// <summary>
		/// What a body (its middle (<paramref name="x"/>, <paramref name="z"/>), facing unit (<paramref name="hx"/>, <paramref name="hz"/>),
		/// <paramref name="v"/> its speed forwards, negative in reverse) driving this way on its clock (<paramref name="t"/> seconds in) does:
		/// the leg it is on (<paramref name="leg"/>, kept between calls: it goes on to the next only once it has stopped at this one's end,
		/// as a driver changes gear), pure pursuit for a point <see cref="FollowLook"/> along the leg from its rear axle (the path is the rear
		/// axle's line), and the speed for where the clock has the car (its speed there and the gap to it closed, stopping at the leg's end).
		/// <paramref name="curv"/>: how fast its nose turns a unit driven forwards (anticlockwise seen from above, x east and z north, positive;
		/// the front wheels' angle is atan(wheelbase × curv)), the same in either gear. <paramref name="speed"/>: forwards (negative in reverse).
		/// <paramref name="behind"/>: how far the body is behind where the clock has it along the way (the game holds the clock back while it
		/// is more than a little: the micro's hold and the body the drivers see go with it). <paramref name="off"/>: how far it is off the leg's
		/// line, <paramref name="facing"/> the cosine between its nose and the way's there (knocked off the way, it stops following it).
		/// </summary>
		internal void Follow(ref int leg, double t, double x, double z, double hx, double hz, double v, out double curv, out double speed, out double behind, out double off, out double facing)
		{
			leg = Math.Max(0, Math.Min(Legs.Count - 1, leg));
			Leg l = Legs[leg];
			double rx = x - hx * RearAxle, rz = z - hz * RearAxle;
			double s = Project(l, rx, rz, out off, out int seg);
			// On to the next leg: stopped at this one's end and the clock past it.
			int clockLeg = ClockLeg(t);
			if (leg < Legs.Count - 1 && clockLeg > leg && l.Length - s < 1.5 && Math.Abs(v) < 1.0)
			{
				leg++;
				l = Legs[leg];
				s = Project(l, rx, rz, out off, out seg);
			}
			double wx = l.Hx[seg], wz = l.Hz[seg];
			facing = hx * wx + hz * wz;
			// Where the clock has it on this leg, and how fast.
			double sc, vc;
			if (clockLeg > leg || t >= Duration)
			{
				sc = l.Length;
				vc = 0.0;
			}
			else if (clockLeg < leg || t < l.Start)
			{
				sc = 0.0;
				vc = 0.0;
			}
			else
			{
				sc = Along(l, Math.Min(l.Time, t - l.Start), out vc);
			}
			behind = sc - s;
			for (int k = leg + 1; k <= Math.Min(clockLeg, Legs.Count - 1); k++)
			{
				behind += k == clockLeg && t < Legs[k].Start + Legs[k].Time ? Along(Legs[k], Math.Max(0.0, t - Legs[k].Start), out double _) : Legs[k].Length;
			}
			double left = Math.Max(0.0, l.Length - s);
			double mag = Math.Max(0.0, Math.Min(l.Top * 1.3 + 0.5, vc + 1.2 * (sc - s)));
			mag = Math.Min(mag, Math.Sqrt(2.0 * Stop * Math.Max(0.0, left - 0.3)));
			speed = l.Back ? -mag : mag;
			// The point aimed at: along the leg's rear axle line, past its end straight on along its last way.
			double want = s + FollowLook, px, pz;
			if (want >= l.Length)
			{
				int n = l.X.Length - 1;
				double tx = l.Back ? -l.Hx[n] : l.Hx[n], tz = l.Back ? -l.Hz[n] : l.Hz[n];
				px = l.X[n] - l.Hx[n] * RearAxle + tx * (want - l.Length);
				pz = l.Z[n] - l.Hz[n] * RearAxle + tz * (want - l.Length);
			}
			else
			{
				RearAt(l, want, out px, out pz);
			}
			// The way it moves: its nose forwards, its tail in reverse; the turn of that for the point, as the nose's turn a unit forwards.
			double dx = l.Back ? -hx : hx, dz = l.Back ? -hz : hz;
			double ax = px - rx, az = pz - rz, ad = Math.Sqrt(ax * ax + az * az);
			if (ad < 1e-6)
			{
				curv = 0.0;
				return;
			}
			double sin = (dx * az - dz * ax) / ad;
			double k2 = 2.0 * sin / Math.Max(ad, 3.0);
			curv = l.Back ? -k2 : k2;
		}

		/// <summary>
		/// Whether a body driving the way, <paramref name="off"/> off its line and facing <paramref name="facing"/> (the cosine) from it
		/// (<see cref="Follow"/>), has been knocked off it: well off the line; or turned from it and a little off it (on the line but turned,
		/// it is only behind the way on a sharp bend); or turned right round.
		/// </summary>
		internal static bool OffWay(double off, double facing)
		{
			return off > 4.0 || (facing < 0.7 && off > 1.5) || facing < -0.5;
		}

		/// <summary>The leg the way's clock is on <paramref name="t"/> seconds in (a gear change counts as the leg after it): where a body taken on the way starts following it.</summary>
		internal int ClockLeg(double t)
		{
			for (int k = Legs.Count - 1; k > 0; k--)
			{
				if (t >= Legs[k - 1].Start + Legs[k - 1].Time)
				{
					return k;
				}
			}
			return 0;
		}

		/// <summary>The rear axle's distance along leg <paramref name="l"/> nearest (<paramref name="rx"/>, <paramref name="rz"/>), how far off it is, and the sample it is in.</summary>
		private static double Project(Leg l, double rx, double rz, out double off, out int seg)
		{
			double best = double.MaxValue, at = 0.0;
			seg = 0;
			for (int j = 0; j + 1 < l.X.Length; j++)
			{
				double ax = l.X[j] - l.Hx[j] * RearAxle, az = l.Z[j] - l.Hz[j] * RearAxle;
				double bx = l.X[j + 1] - l.Hx[j + 1] * RearAxle, bz = l.Z[j + 1] - l.Hz[j + 1] * RearAxle;
				double ex = bx - ax, ez = bz - az, e2 = ex * ex + ez * ez;
				double f = e2 > 1e-12 ? Math.Max(0.0, Math.Min(1.0, ((rx - ax) * ex + (rz - az) * ez) / e2)) : 0.0;
				double qx = ax + ex * f - rx, qz = az + ez * f - rz, d2 = qx * qx + qz * qz;
				if (d2 < best)
				{
					best = d2;
					at = l.D[j] + (l.D[j + 1] - l.D[j]) * f;
					seg = f < 0.5 ? j : j + 1;
				}
			}
			off = Math.Sqrt(best);
			return at;
		}

		/// <summary>The rear axle's place <paramref name="d"/> along leg <paramref name="l"/>.</summary>
		private static void RearAt(Leg l, double d, out double x, out double z)
		{
			int j = 0;
			while (j + 2 < l.D.Length && l.D[j + 1] < d)
			{
				j++;
			}
			double span = l.D[j + 1] - l.D[j], f = span > 1e-12 ? Math.Max(0.0, Math.Min(1.0, (d - l.D[j]) / span)) : 0.0;
			double ax = l.X[j] - l.Hx[j] * RearAxle, az = l.Z[j] - l.Hz[j] * RearAxle;
			double bx = l.X[j + 1] - l.Hx[j + 1] * RearAxle, bz = l.Z[j + 1] - l.Hz[j + 1] * RearAxle;
			x = ax + (bx - ax) * f;
			z = az + (bz - az) * f;
		}

		private static double Dist(double ax, double az, double bx, double bz)
		{
			double dx = bx - ax, dz = bz - az;
			return Math.Sqrt(dx * dx + dz * dz);
		}

		private static bool Unit(ref double x, ref double z)
		{
			double len = Math.Sqrt(x * x + z * z);
			if (len < 1e-9)
			{
				return false;
			}
			x /= len;
			z /= len;
			return true;
		}
	}
}
