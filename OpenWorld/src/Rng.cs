using System;

namespace HalfGripOpenWorld
{
	/// <summary>
	/// SplitMix64. A generator of our own on purpose: System.Random and UnityEngine.Random are not promised to give the same numbers on every
	/// machine, and every player in a room has to build the same towns and roads.
	/// </summary>
	internal sealed class Rng
	{
		private ulong state;

		public Rng(ulong seed)
		{
			state = seed;
		}

		public ulong NextU64()
		{
			unchecked
			{
				state += 0x9E3779B97F4A7C15UL;
				ulong z = state;
				z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
				z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
				return z ^ (z >> 31);
			}
		}

		/// <summary>0 up to, not including, 1.</summary>
		public double Next()
		{
			return (double)(NextU64() >> 11) * (1.0 / 9007199254740992.0);
		}

		public double Range(double a, double b)
		{
			return a + (b - a) * Next();
		}
	}

	/// <summary>
	/// Sine and cosine from plain additions and multiplications only, which are exact to the last bit on every x64 machine (the C library's
	/// sin() may differ in the last digit between versions, and road geometry shapes the ground every player drives on).
	/// </summary>
	internal static class DetMath
	{
		public const double Pi = 3.14159265358979323846;

		public static double Sin(double x)
		{
			double twoPi = 2.0 * Pi;
			x -= twoPi * Math.Floor((x + Pi) / twoPi);
			if (x > Pi / 2.0)
			{
				x = Pi - x;
			}
			else if (x < -Pi / 2.0)
			{
				x = -Pi - x;
			}
			double x2 = x * x;
			double term = x;
			double sum = x;
			for (int k = 1; k <= 10; k++)
			{
				term = -term * x2 / ((2 * k) * (2 * k + 1));
				sum += term;
			}
			return sum;
		}

		public static double Cos(double x)
		{
			return Sin(x + Pi / 2.0);
		}

		/// <summary>
		/// atan2 from plain arithmetic, good to the last few bits (unlike <c>Network.Atan2</c>'s polynomial, which the world keeps): the
		/// traffic's parking ways (<c>ReedsShepp</c>, <c>Manoeuvre</c>) shape what every machine in a room simulates, and they check that a
		/// word lands on its end pose, so they need it accurate as well as the same everywhere.
		/// </summary>
		public static double Atan2(double y, double x)
		{
			double ax = Math.Abs(x), ay = Math.Abs(y);
			if (ax == 0.0 && ay == 0.0)
			{
				return 0.0;
			}
			double r = Atan01(Math.Min(ax, ay) / Math.Max(ax, ay));
			if (ay > ax)
			{
				r = Pi / 2.0 - r;
			}
			if (x < 0.0)
			{
				r = Pi - r;
			}
			return y < 0.0 ? -r : r;
		}

		/// <summary>atan of a in [0, 1]: past tan 15° reduced by 30° (atan a = 30° + atan((a√3 − 1) / (a + √3))), then its series.</summary>
		private static double Atan01(double a)
		{
			double off = 0.0;
			if (a > 0.2679491924311227)
			{
				double s3 = Math.Sqrt(3.0);
				a = (a * s3 - 1.0) / (a + s3);
				off = Pi / 6.0;
			}
			double a2 = a * a, term = a, sum = a;
			for (int k = 1; k <= 16; k++)
			{
				term *= -a2;
				sum += term / (2 * k + 1);
			}
			return off + sum;
		}

		/// <summary>asin and acos through <see cref="Atan2"/> (the argument clamped to [-1, 1]).</summary>
		public static double Asin(double x)
		{
			x = Math.Max(-1.0, Math.Min(1.0, x));
			return Atan2(x, Math.Sqrt((1.0 - x) * (1.0 + x)));
		}

		public static double Acos(double x)
		{
			x = Math.Max(-1.0, Math.Min(1.0, x));
			return Atan2(Math.Sqrt((1.0 - x) * (1.0 + x)), x);
		}
	}
}
