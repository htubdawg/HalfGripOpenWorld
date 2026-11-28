using System;
using System.Collections.Generic;

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

		/// <summary>The tightest radius on any leg.</summary>
		internal double MinRadius = double.MaxValue;

		/// <summary>How long it is, all legs.</summary>
		internal double Length;

		/// <summary>
		/// Pulling out: from the spot (middle (<paramref name="sx"/>, <paramref name="sz"/>), nose facing (<paramref name="shx"/>, <paramref
		/// name="shz"/>)) to standing in the lane at (<paramref name="ex"/>, <paramref name="ez"/>) facing (<paramref name="ehx"/>, <paramref
		/// name="ehz"/>), clear of the cars parked round it (<paramref name="parked"/>; may be null).
		/// </summary>
		internal static Manoeuvre Out(double sx, double sz, double shx, double shz, double ex, double ez, double ehx, double ehz, List<TrafficParking.Spot> parked = null)
		{
			return Plan(sx, sz, shx, shz, ex, ez, ehx, ehz, parked, false);
		}

		/// <summary>
		/// Pulling in: from standing in the lane at (<paramref name="ex"/>, <paramref name="ez"/>) facing (<paramref name="ehx"/>, <paramref
		/// name="ehz"/>) into the spot, clear of the cars parked round it. Worked out as the way out driven backwards in time (the same
		/// ground covered, forwards and reverse swapped): a space it would back out of it drives into, one at the kerb it draws up past and
		/// reverses into.
		/// </summary>
		internal static Manoeuvre In(double ex, double ez, double ehx, double ehz, double sx, double sz, double shx, double shz, List<TrafficParking.Spot> parked = null)
		{
			return Plan(sx, sz, shx, shz, ex, ez, ehx, ehz, parked, true);
		}

		/// <summary>How far behind its middle a car's rear axle is, which it turns about (its nose swings out, its tail follows in).</summary>
		internal const double RearAxle = 4.4;

		/// <summary>
		/// The way between the spot (s) and the lane (e), out of the spot or (<paramref name="into"/>) into it: of every way of one leg or two
		/// (straight off, backing off first, forwards and back), the quickest no tighter than a car turns that keeps clear of the parked cars,
		/// else the least bad (trafficsim counts those). Worked out for the car's rear axle (the nose swinging out as a real car's does).
		/// Into the spot, the ways are worked out from the spot with forwards and reverse swapped, then turned round in time.
		/// </summary>
		private static Manoeuvre Plan(double sx, double sz, double shx, double shz, double ex, double ez, double ehx, double ehz, List<TrafficParking.Spot> parked, bool into)
		{
			Unit(ref shx, ref shz);
			Unit(ref ehx, ref ehz);
			double ax = sx - shx * RearAxle, az = sz - shz * RearAxle, bx = ex - ehx * RearAxle, bz = ez - ehz * RearAxle;
			List<(Leg a, Leg b, double cost)> ways = new List<(Leg, Leg, double)>();
			// Reverse is slow: a metre back costs twice one forwards, and stopping to change gear a little more.
			double Cost(Leg l)
			{
				return l == null ? 0.0 : l.Length * (l.Back != into ? 2.0 : 1.0);
			}
			void Add(Leg a, Leg b, bool two)
			{
				if (a == null || a.MinRadius < TurnRadius || (two && (b == null || b.MinRadius < TurnRadius)))
				{
					return;
				}
				ways.Add((a, b, Cost(a) + Cost(b) + (b != null ? 10.0 : 0.0)));
			}
			// One leg, forwards or in reverse.
			foreach (int sign in new[] { 1, -1 })
			{
				Add(Gentlest(ax, az, shx, shz, bx, bz, ehx, ehz, sign), null, false);
			}
			// Two legs, the gear changed between: off the spot to a turning place, then on to the lane.
			double ahead = (bx - ax) * shx + (bz - az) * shz;
			List<double> firsts = new List<double> { 0.15, 0.3, 0.6, 1.0, 1.5, 2.0, 2.5, 3.0, 4.0 };
			for (int k = 0; k < firsts.Count; k++)
			{
				firsts[k] *= Traffic.CarLength;
			}
			firsts.Add(Math.Abs(ahead) + Traffic.CarLength);
			firsts.Add(Math.Abs(ahead) + Traffic.CarLength * 1.6);
			foreach (int sign in new[] { -1, 1 })
			{
				foreach (double d in firsts)
				{
					for (int a = -4; a <= 4; a++)
					{
						double phi = a * Math.PI / 6.0;
						double c = DetMath.Cos(phi), si = DetMath.Sin(phi);
						double mhx = shx * c - shz * si, mhz = shx * si + shz * c;
						// The chord of a turning leg runs along the mean of the headings at its ends.
						double chx = shx + mhx, chz = shz + mhz;
						if (!Unit(ref chx, ref chz))
						{
							continue;
						}
						double mx = ax + chx * d * sign, mz = az + chz * d * sign;
						Leg first = Gentlest(ax, az, shx, shz, mx, mz, mhx, mhz, sign);
						if (first != null && first.MinRadius >= TurnRadius)
						{
							Add(first, Gentlest(mx, mz, mhx, mhz, bx, bz, ehx, ehz, -sign), true);
						}
					}
				}
				// Off the spot onto the line it leaves along, then straight on along it (out of a drive whose house it faced).
				foreach (double k in new[] { 0.5, 1.0, 1.5, 2.0, 3.0 })
				{
					double mx = bx + ehx * Traffic.CarLength * k * sign, mz = bz + ehz * Traffic.CarLength * k * sign;
					Leg first = Gentlest(ax, az, shx, shz, mx, mz, ehx, ehz, sign);
					if (first != null && first.MinRadius >= TurnRadius)
					{
						Add(first, Gentlest(mx, mz, ehx, ehz, bx, bz, ehx, ehz, -sign), true);
					}
				}
			}
			ways.Sort((p, q) => p.cost.CompareTo(q.cost));
			(Leg a, Leg b, double cost) pick = (null, null, 0.0);
			foreach (var w in ways)
			{
				if (Clear(w.a, parked) && (w.b == null || Clear(w.b, parked)))
				{
					pick = w;
					break;
				}
			}
			if (pick.a == null && ways.Count > 0)
			{
				// None clear of the parked cars: the quickest (trafficsim counts it).
				pick = ways[0];
			}
			if (pick.a == null)
			{
				// Nothing gentle enough: straight across, whatever it takes (trafficsim counts it).
				Leg l = Gentlest(ax, az, shx, shz, bx, bz, ehx, ehz, 1) ?? Gentlest(ax, az, shx, shz, bx, bz, ehx, ehz, -1) ?? Curve(ax, az, shx, shz, bx, bz, ehx, ehz, Dist(ax, az, bx, bz) * 0.5, false);
				pick = (l, null, 0.0);
			}
			Manoeuvre m = new Manoeuvre();
			m.Legs.Add(pick.a);
			if (pick.b != null)
			{
				m.Legs.Add(pick.b);
			}
			if (into)
			{
				m.Legs.Reverse();
				for (int k = 0; k < m.Legs.Count; k++)
				{
					m.Legs[k] = Backwards(m.Legs[k]);
				}
			}
			m.Time();
			return m;
		}

		/// <summary>A leg driven backwards in time: the same ground, the other way, forwards and reverse swapped.</summary>
		private static Leg Backwards(Leg l)
		{
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
			return r;
		}

		/// <summary>Whether leg <paramref name="l"/> keeps clear of every parked car (a hand's breadth between).</summary>
		private static bool Clear(Leg l, List<TrafficParking.Spot> parked)
		{
			if (parked == null || parked.Count == 0)
			{
				return true;
			}
			for (int j = 0; j < l.X.Length; j++)
			{
				TrafficParking.Spot me = new TrafficParking.Spot { X = l.X[j], Z = l.Z[j], Hx = l.Hx[j], Hz = l.Hz[j] };
				foreach (TrafficParking.Spot o in parked)
				{
					if (Math.Abs(o.X - me.X) < 20.0 && Math.Abs(o.Z - me.Z) < 20.0 && TrafficParking.Overlap(me, o, 0.1))
					{
						return false;
					}
				}
			}
			return true;
		}

		/// <summary>
		/// The rear axle driven from one pose to another, forwards (<paramref name="sign"/> 1) or in reverse (-1): the gentlest of a few
		/// curves, or null if the end isn't that way.
		/// </summary>
		private static Leg Gentlest(double sx, double sz, double shx, double shz, double ex, double ez, double ehx, double ehz, int sign)
		{
			double dx = ex - sx, dz = ez - sz, d = Math.Sqrt(dx * dx + dz * dz);
			if (d < 1e-6)
			{
				return null;
			}
			if ((dx * shx + dz * shz) * sign <= 0.0 || (dx * ehx + dz * ehz) * sign <= 0.0)
			{
				return null;
			}
			Leg best = null;
			foreach (double k in new[] { 0.4, 0.6, 0.8, 1.0, 1.3 })
			{
				Leg l = Curve(sx, sz, shx * sign, shz * sign, ex, ez, ehx * sign, ehz * sign, d * k, sign < 0);
				if (best == null || l.MinRadius > best.MinRadius + 1e-9)
				{
					best = l;
				}
			}
			return best;
		}

		/// <summary>
		/// The cubic the rear axle follows from (sx, sz) going (tx0, tz0) to (ex, ez) going (tx1, tz1) (unit), its ends' pull <paramref
		/// name="pull"/>: the car's middle along it (ahead of the axle) and its nose's way (against the curve in reverse).
		/// </summary>
		private static Leg Curve(double sx, double sz, double tx0, double tz0, double ex, double ez, double tx1, double tz1, double pull, bool back)
		{
			double ax = sx, az = sz;
			double bx = sx + tx0 * pull / 3.0, bz = sz + tz0 * pull / 3.0;
			double cx = ex - tx1 * pull / 3.0, cz = ez - tz1 * pull / 3.0;
			double dx = ex, dz = ez;
			Leg l = new Leg { X = new double[Samples + 1], Z = new double[Samples + 1], Hx = new double[Samples + 1], Hz = new double[Samples + 1], D = new double[Samples + 1], Back = back, MinRadius = double.MaxValue };
			double px = 0.0, pz = 0.0;
			for (int j = 0; j <= Samples; j++)
			{
				double t = (double)j / Samples, u = 1.0 - t;
				double rx = u * u * u * ax + 3.0 * u * u * t * bx + 3.0 * u * t * t * cx + t * t * t * dx;
				double rz = u * u * u * az + 3.0 * u * u * t * bz + 3.0 * u * t * t * cz + t * t * t * dz;
				double vx = 3.0 * (u * u * (bx - ax) + 2.0 * u * t * (cx - bx) + t * t * (dx - cx));
				double vz = 3.0 * (u * u * (bz - az) + 2.0 * u * t * (cz - bz) + t * t * (dz - cz));
				double wx = 6.0 * (u * (cx - 2.0 * bx + ax) + t * (dx - 2.0 * cx + bx));
				double wz = 6.0 * (u * (cz - 2.0 * bz + az) + t * (dz - 2.0 * cz + bz));
				double sp = Math.Sqrt(vx * vx + vz * vz);
				double hx, hz;
				if (sp > 1e-9)
				{
					double curv = Math.Abs(vx * wz - vz * wx) / (sp * sp * sp);
					if (curv > 1e-12)
					{
						l.MinRadius = Math.Min(l.MinRadius, 1.0 / curv);
					}
					hx = back ? -vx / sp : vx / sp;
					hz = back ? -vz / sp : vz / sp;
				}
				else
				{
					hx = back ? -tx0 : tx0;
					hz = back ? -tz0 : tz0;
				}
				l.Hx[j] = hx;
				l.Hz[j] = hz;
				l.X[j] = rx + hx * RearAxle;
				l.Z[j] = rz + hz * RearAxle;
				// Its distance driven: the rear axle's.
				if (j > 0)
				{
					l.D[j] = l.D[j - 1] + Dist(px, pz, rx, rz);
				}
				px = rx;
				pz = rz;
			}
			l.Length = l.D[Samples];
			return l;
		}

		/// <summary>Each leg's time: from standing up to its top speed (no faster than a gentle bend allows) and back to standing, a pause between legs.</summary>
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
				// Round a tight bend no more than 1.5 m/s² across it.
				double top = Math.Min(l.Back ? Reverse : Forwards, Math.Sqrt(1.5 * 3.3 * Math.Max(1.0, l.MinRadius)));
				double dUp = top * top / (2.0 * Pull), dDown = top * top / (2.0 * Stop);
				if (dUp + dDown > l.Length)
				{
					top = Math.Sqrt(2.0 * l.Length * Pull * Stop / (Pull + Stop));
					dUp = top * top / (2.0 * Pull);
					dDown = top * top / (2.0 * Stop);
				}
				l.Top = top;
				l.Start = Duration;
				l.Time = top > 1e-9 ? top / Pull + top / Stop + (l.Length - dUp - dDown) / top : 0.0;
				Duration += l.Time;
				Length += l.Length;
				MinRadius = Math.Min(MinRadius, l.MinRadius);
			}
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
