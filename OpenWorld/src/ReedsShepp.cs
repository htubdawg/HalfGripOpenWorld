using System;
using System.Collections.Generic;

namespace HalfGripOpenWorld
{
	/// <summary>
	/// Reeds–Shepp paths (Reeds and Shepp 1990; the formulas as OMPL's <c>ReedsSheppStateSpace</c> lays them out): every shortest way a car
	/// that turns no tighter than a radius can drive, forwards and in reverse, from one pose to another, as words of arcs (left, right) and
	/// straights, each driven forwards or backwards. Among them are three-point turns and the backing round a driver does where a plain
	/// curve won't fit. <see cref="Manoeuvre"/> takes its ways out of a spot and in from them. Each word found is driven through once and kept
	/// only if it lands on the end pose, so a slip in a formula only loses a word. Pure arithmetic, no Unity types; needn't be deterministic.
	/// </summary>
	internal static class ReedsShepp
	{
		/// <summary>A segment: 'L' an arc turning left (anticlockwise seen from above), 'R' right, 'S' straight; its length in radii (an arc's the angle turned), negative in reverse.</summary>
		internal struct Seg
		{
			public char Kind;
			public double Length;
		}

		private const double Zero = 1e-9;

		/// <summary>
		/// Every Reeds–Shepp word from the origin facing +x to (<paramref name="x"/>, <paramref name="y"/>) facing <paramref name="phi"/>
		/// (radians, anticlockwise), all in radii; y is to the left. Each checked by driving it.
		/// </summary>
		internal static List<Seg[]> All(double x, double y, double phi)
		{
			List<Seg[]> found = new List<Seg[]>();
			void Add(string kinds, params double[] lengths)
			{
				Seg[] w = new Seg[kinds.Length];
				for (int k = 0; k < w.Length; k++)
				{
					w[k] = new Seg { Kind = kinds[k], Length = lengths[k] };
				}
				Drive(w, out double ex, out double ey, out double eth);
				if (Math.Abs(ex - x) < 1e-6 && Math.Abs(ey - y) < 1e-6 && Math.Abs(Mod2Pi(eth - phi)) < 1e-6)
				{
					found.Add(w);
				}
			}
			CSC(x, y, phi, Add);
			CCC(x, y, phi, Add);
			CCCC(x, y, phi, Add);
			CCSC(x, y, phi, Add);
			CCSCC(x, y, phi, Add);
			return found;
		}

		/// <summary>Where word <paramref name="w"/> ends, from the origin facing +x.</summary>
		internal static void Drive(Seg[] w, out double x, out double y, out double th)
		{
			x = 0.0;
			y = 0.0;
			th = 0.0;
			foreach (Seg s in w)
			{
				Step(s.Kind, s.Length, ref x, ref y, ref th);
			}
		}

		/// <summary>One segment driven <paramref name="t"/> radii (negative in reverse) from (x, y) facing th.</summary>
		internal static void Step(char kind, double t, ref double x, ref double y, ref double th)
		{
			switch (kind)
			{
				case 'L':
					x += Math.Sin(th + t) - Math.Sin(th);
					y += -Math.Cos(th + t) + Math.Cos(th);
					th += t;
					break;
				case 'R':
					x += Math.Sin(th) - Math.Sin(th - t);
					y += Math.Cos(th - t) - Math.Cos(th);
					th -= t;
					break;
				default:
					x += t * Math.Cos(th);
					y += t * Math.Sin(th);
					break;
			}
		}

		/// <summary>The total length of a word (radii).</summary>
		internal static double Length(Seg[] w)
		{
			double l = 0.0;
			foreach (Seg s in w)
			{
				l += Math.Abs(s.Length);
			}
			return l;
		}

		private static double Mod2Pi(double a)
		{
			double v = a % (2.0 * Math.PI);
			if (v < -Math.PI)
			{
				v += 2.0 * Math.PI;
			}
			else if (v > Math.PI)
			{
				v -= 2.0 * Math.PI;
			}
			return v;
		}

		private static void Polar(double x, double y, out double r, out double theta)
		{
			r = Math.Sqrt(x * x + y * y);
			theta = Math.Atan2(y, x);
		}

		private static void TauOmega(double u, double v, double xi, double eta, double phi, out double tau, out double omega)
		{
			double delta = Mod2Pi(u - v), a = Math.Sin(u) - Math.Sin(delta), b = Math.Cos(u) - Math.Cos(delta) - 1.0;
			double t1 = Math.Atan2(eta * a - xi * b, xi * a + eta * b), t2 = 2.0 * (Math.Cos(delta) - Math.Cos(v) - Math.Cos(u)) + 3.0;
			tau = t2 < 0.0 ? Mod2Pi(t1 + Math.PI) : Mod2Pi(t1);
			omega = Mod2Pi(tau - u + v - phi);
		}

		private static bool LpSpLp(double x, double y, double phi, out double t, out double u, out double v)
		{
			Polar(x - Math.Sin(phi), y - 1.0 + Math.Cos(phi), out u, out t);
			v = 0.0;
			if (t >= -Zero)
			{
				v = Mod2Pi(phi - t);
				if (v >= -Zero)
				{
					return true;
				}
			}
			return false;
		}

		private static bool LpSpRp(double x, double y, double phi, out double t, out double u, out double v)
		{
			Polar(x + Math.Sin(phi), y - 1.0 - Math.Cos(phi), out double u1, out double t1);
			u1 *= u1;
			t = u = v = 0.0;
			if (u1 >= 4.0)
			{
				u = Math.Sqrt(u1 - 4.0);
				double theta = Math.Atan2(2.0, u);
				t = Mod2Pi(t1 + theta);
				v = Mod2Pi(t - phi);
				return t >= -Zero && v >= -Zero;
			}
			return false;
		}

		private static void CSC(double x, double y, double phi, Action<string, double[]> add)
		{
			if (LpSpLp(x, y, phi, out double t, out double u, out double v)) add("LSL", new[] { t, u, v });
			if (LpSpLp(-x, y, -phi, out t, out u, out v)) add("LSL", new[] { -t, -u, -v });
			if (LpSpLp(x, -y, -phi, out t, out u, out v)) add("RSR", new[] { t, u, v });
			if (LpSpLp(-x, -y, phi, out t, out u, out v)) add("RSR", new[] { -t, -u, -v });
			if (LpSpRp(x, y, phi, out t, out u, out v)) add("LSR", new[] { t, u, v });
			if (LpSpRp(-x, y, -phi, out t, out u, out v)) add("LSR", new[] { -t, -u, -v });
			if (LpSpRp(x, -y, -phi, out t, out u, out v)) add("RSL", new[] { t, u, v });
			if (LpSpRp(-x, -y, phi, out t, out u, out v)) add("RSL", new[] { -t, -u, -v });
		}

		private static bool LpRmL(double x, double y, double phi, out double t, out double u, out double v)
		{
			double xi = x - Math.Sin(phi), eta = y - 1.0 + Math.Cos(phi);
			Polar(xi, eta, out double u1, out double theta);
			t = u = v = 0.0;
			if (u1 <= 4.0)
			{
				u = -2.0 * Math.Asin(0.25 * u1);
				t = Mod2Pi(theta + 0.5 * u + Math.PI);
				v = Mod2Pi(phi - t + u);
				return t >= -Zero && u <= Zero;
			}
			return false;
		}

		private static void CCC(double x, double y, double phi, Action<string, double[]> add)
		{
			if (LpRmL(x, y, phi, out double t, out double u, out double v)) add("LRL", new[] { t, u, v });
			if (LpRmL(-x, y, -phi, out t, out u, out v)) add("LRL", new[] { -t, -u, -v });
			if (LpRmL(x, -y, -phi, out t, out u, out v)) add("RLR", new[] { t, u, v });
			if (LpRmL(-x, -y, phi, out t, out u, out v)) add("RLR", new[] { -t, -u, -v });
			// Backwards.
			double xb = x * Math.Cos(phi) + y * Math.Sin(phi), yb = x * Math.Sin(phi) - y * Math.Cos(phi);
			if (LpRmL(xb, yb, phi, out t, out u, out v)) add("LRL", new[] { v, u, t });
			if (LpRmL(-xb, yb, -phi, out t, out u, out v)) add("LRL", new[] { -v, -u, -t });
			if (LpRmL(xb, -yb, -phi, out t, out u, out v)) add("RLR", new[] { v, u, t });
			if (LpRmL(-xb, -yb, phi, out t, out u, out v)) add("RLR", new[] { -v, -u, -t });
		}

		private static bool LpRupLumRm(double x, double y, double phi, out double t, out double u, out double v)
		{
			double xi = x + Math.Sin(phi), eta = y - 1.0 - Math.Cos(phi), rho = 0.25 * (2.0 + Math.Sqrt(xi * xi + eta * eta));
			t = u = v = 0.0;
			if (rho <= 1.0)
			{
				u = Math.Acos(rho);
				TauOmega(u, -u, xi, eta, phi, out t, out v);
				return t >= -Zero && v <= Zero;
			}
			return false;
		}

		private static bool LpRumLumRp(double x, double y, double phi, out double t, out double u, out double v)
		{
			double xi = x + Math.Sin(phi), eta = y - 1.0 - Math.Cos(phi), rho = (20.0 - xi * xi - eta * eta) / 16.0;
			t = u = v = 0.0;
			if (rho >= 0.0 && rho <= 1.0)
			{
				u = -Math.Acos(rho);
				if (u >= -0.5 * Math.PI)
				{
					TauOmega(u, u, xi, eta, phi, out t, out v);
					return t >= -Zero && v >= -Zero;
				}
			}
			return false;
		}

		private static void CCCC(double x, double y, double phi, Action<string, double[]> add)
		{
			if (LpRupLumRm(x, y, phi, out double t, out double u, out double v)) add("LRLR", new[] { t, u, -u, v });
			if (LpRupLumRm(-x, y, -phi, out t, out u, out v)) add("LRLR", new[] { -t, -u, u, -v });
			if (LpRupLumRm(x, -y, -phi, out t, out u, out v)) add("RLRL", new[] { t, u, -u, v });
			if (LpRupLumRm(-x, -y, phi, out t, out u, out v)) add("RLRL", new[] { -t, -u, u, -v });
			if (LpRumLumRp(x, y, phi, out t, out u, out v)) add("LRLR", new[] { t, u, u, v });
			if (LpRumLumRp(-x, y, -phi, out t, out u, out v)) add("LRLR", new[] { -t, -u, -u, -v });
			if (LpRumLumRp(x, -y, -phi, out t, out u, out v)) add("RLRL", new[] { t, u, u, v });
			if (LpRumLumRp(-x, -y, phi, out t, out u, out v)) add("RLRL", new[] { -t, -u, -u, -v });
		}

		private static bool LpRmSmLm(double x, double y, double phi, out double t, out double u, out double v)
		{
			double xi = x - Math.Sin(phi), eta = y - 1.0 + Math.Cos(phi);
			Polar(xi, eta, out double rho, out double theta);
			t = u = v = 0.0;
			if (rho >= 2.0)
			{
				double r = Math.Sqrt(rho * rho - 4.0);
				u = 2.0 - r;
				t = Mod2Pi(theta + Math.Atan2(r, -2.0));
				v = Mod2Pi(phi - 0.5 * Math.PI - t);
				return t >= -Zero && u <= Zero && v <= Zero;
			}
			return false;
		}

		private static bool LpRmSmRm(double x, double y, double phi, out double t, out double u, out double v)
		{
			double xi = x + Math.Sin(phi), eta = y - 1.0 - Math.Cos(phi);
			Polar(-eta, xi, out double rho, out double theta);
			t = u = v = 0.0;
			if (rho >= 2.0)
			{
				t = theta;
				u = 2.0 - rho;
				v = Mod2Pi(t + 0.5 * Math.PI - phi);
				return t >= -Zero && u <= Zero && v <= Zero;
			}
			return false;
		}

		private static void CCSC(double x, double y, double phi, Action<string, double[]> add)
		{
			const double h = 0.5 * Math.PI;
			if (LpRmSmLm(x, y, phi, out double t, out double u, out double v)) add("LRSL", new[] { t, -h, u, v });
			if (LpRmSmLm(-x, y, -phi, out t, out u, out v)) add("LRSL", new[] { -t, h, -u, -v });
			if (LpRmSmLm(x, -y, -phi, out t, out u, out v)) add("RLSR", new[] { t, -h, u, v });
			if (LpRmSmLm(-x, -y, phi, out t, out u, out v)) add("RLSR", new[] { -t, h, -u, -v });
			if (LpRmSmRm(x, y, phi, out t, out u, out v)) add("LRSR", new[] { t, -h, u, v });
			if (LpRmSmRm(-x, y, -phi, out t, out u, out v)) add("LRSR", new[] { -t, h, -u, -v });
			if (LpRmSmRm(x, -y, -phi, out t, out u, out v)) add("RLSL", new[] { t, -h, u, v });
			if (LpRmSmRm(-x, -y, phi, out t, out u, out v)) add("RLSL", new[] { -t, h, -u, -v });
			// Backwards.
			double xb = x * Math.Cos(phi) + y * Math.Sin(phi), yb = x * Math.Sin(phi) - y * Math.Cos(phi);
			if (LpRmSmLm(xb, yb, phi, out t, out u, out v)) add("LSRL", new[] { v, u, -h, t });
			if (LpRmSmLm(-xb, yb, -phi, out t, out u, out v)) add("LSRL", new[] { -v, -u, h, -t });
			if (LpRmSmLm(xb, -yb, -phi, out t, out u, out v)) add("RSLR", new[] { v, u, -h, t });
			if (LpRmSmLm(-xb, -yb, phi, out t, out u, out v)) add("RSLR", new[] { -v, -u, h, -t });
			if (LpRmSmRm(xb, yb, phi, out t, out u, out v)) add("RSRL", new[] { v, u, -h, t });
			if (LpRmSmRm(-xb, yb, -phi, out t, out u, out v)) add("RSRL", new[] { -v, -u, h, -t });
			if (LpRmSmRm(xb, -yb, -phi, out t, out u, out v)) add("LSLR", new[] { v, u, -h, t });
			if (LpRmSmRm(-xb, -yb, phi, out t, out u, out v)) add("LSLR", new[] { -v, -u, h, -t });
		}

		private static bool LpRmSLmRp(double x, double y, double phi, out double t, out double u, out double v)
		{
			double xi = x + Math.Sin(phi), eta = y - 1.0 - Math.Cos(phi);
			Polar(xi, eta, out double rho, out double _);
			t = u = v = 0.0;
			if (rho >= 2.0)
			{
				u = 4.0 - Math.Sqrt(rho * rho - 4.0);
				if (u <= Zero)
				{
					t = Mod2Pi(Math.Atan2((4.0 - u) * xi - 2.0 * eta, -2.0 * xi + (u - 4.0) * eta));
					v = Mod2Pi(t - phi);
					return t >= -Zero && v >= -Zero;
				}
			}
			return false;
		}

		private static void CCSCC(double x, double y, double phi, Action<string, double[]> add)
		{
			const double h = 0.5 * Math.PI;
			if (LpRmSLmRp(x, y, phi, out double t, out double u, out double v)) add("LRSLR", new[] { t, -h, u, -h, v });
			if (LpRmSLmRp(-x, y, -phi, out t, out u, out v)) add("LRSLR", new[] { -t, h, -u, h, -v });
			if (LpRmSLmRp(x, -y, -phi, out t, out u, out v)) add("RLSRL", new[] { t, -h, u, -h, v });
			if (LpRmSLmRp(-x, -y, phi, out t, out u, out v)) add("RLSRL", new[] { -t, h, -u, h, -v });
		}
	}
}
