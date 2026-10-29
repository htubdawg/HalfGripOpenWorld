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
	}
}
