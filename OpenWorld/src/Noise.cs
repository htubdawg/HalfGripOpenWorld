using System;

namespace HalfGripOpenWorld
{
	/// <summary>
	/// Value and gradient noise from integer hashing and plain additions and multiplications. Those are exact to the last bit on every x64 machine, so
	/// every player in a room builds the same ground. Nothing here draws from System.Random or UnityEngine.Random, and nothing calls the C
	/// library's sin() or exp(), which may differ in the last digit between versions.
	/// </summary>
	internal static class Noise
	{
		/// <summary>The SplitMix64 finaliser: a well mixed 64-bit value from any 64-bit value.</summary>
		internal static ulong Mix(ulong z)
		{
			unchecked
			{
				z += 0x9E3779B97F4A7C15UL;
				z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
				z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
				return z ^ (z >> 31);
			}
		}

		/// <summary>A value from -1 to 1 for one lattice corner.</summary>
		private static double Corner(ulong salt, long ix, long iz)
		{
			unchecked
			{
				ulong h = Mix(salt ^ Mix((ulong)ix ^ Mix((ulong)iz)));
				return (double)(h >> 11) * (2.0 / 9007199254740992.0) - 1.0;
			}
		}

		/// <summary>0 at 0, 1 at 1, flat at both ends (first and second derivative 0), so the ground has no creases on the lattice lines.</summary>
		internal static double Fade(double t)
		{
			return t * t * t * (t * (t * 6.0 - 15.0) + 10.0);
		}

		/// <summary>Smooth noise from -1 to 1 with features about <paramref name="wavelength"/> metres across.</summary>
		internal static double Value(ulong salt, double x, double z, double wavelength)
		{
			double fx = x / wavelength;
			double fz = z / wavelength;
			double flx = Math.Floor(fx);
			double flz = Math.Floor(fz);
			long ix = (long)flx;
			long iz = (long)flz;
			double u = Fade(fx - flx);
			double v = Fade(fz - flz);
			double a = Corner(salt, ix, iz);
			double b = Corner(salt, ix + 1, iz);
			double c = Corner(salt, ix, iz + 1);
			double d = Corner(salt, ix + 1, iz + 1);
			double ab = a + (b - a) * u;
			double cd = c + (d - c) * u;
			return ab + (cd - ab) * v;
		}

		/// <summary>Sixteen unit directions, written out (not computed with sin/cos, whose last digit may differ between machines).</summary>
		private static readonly double[] GradX =
		{
			1.0, 0.9238795325112867, 0.7071067811865476, 0.3826834323650898, 0.0, -0.3826834323650897, -0.7071067811865475, -0.9238795325112867,
			-1.0, -0.9238795325112868, -0.7071067811865477, -0.3826834323650903, 0.0, 0.38268343236509, 0.7071067811865474, 0.9238795325112865,
		};

		private static readonly double[] GradZ =
		{
			0.0, 0.3826834323650898, 0.7071067811865475, 0.9238795325112867, 1.0, 0.9238795325112867, 0.7071067811865476, 0.3826834323650899,
			0.0, -0.3826834323650897, -0.7071067811865475, -0.9238795325112865, -1.0, -0.9238795325112866, -0.7071067811865477, -0.3826834323650904,
		};

		private static double Dot(ulong salt, long ix, long iz, double dx, double dz)
		{
			unchecked
			{
				int g = (int)(Mix(salt ^ Mix((ulong)ix ^ Mix((ulong)iz))) >> 60);
				return GradX[g] * dx + GradZ[g] * dz;
			}
		}

		/// <summary>
		/// Gradient (Perlin) noise from about -1 to 1, features about <paramref name="wavelength"/> metres across. Unlike value noise its
		/// zero crossings do not follow the lattice, so ridges made from it wander instead of running in squares.
		/// </summary>
		internal static double Gradient(ulong salt, double x, double z, double wavelength)
		{
			double fx = x / wavelength;
			double fz = z / wavelength;
			double flx = Math.Floor(fx);
			double flz = Math.Floor(fz);
			long ix = (long)flx;
			long iz = (long)flz;
			double tx = fx - flx;
			double tz = fz - flz;
			double u = Fade(tx);
			double v = Fade(tz);
			double a = Dot(salt, ix, iz, tx, tz);
			double b = Dot(salt, ix + 1, iz, tx - 1.0, tz);
			double c = Dot(salt, ix, iz + 1, tx, tz - 1.0);
			double d = Dot(salt, ix + 1, iz + 1, tx - 1.0, tz - 1.0);
			double ab = a + (b - a) * u;
			double cd = c + (d - c) * u;
			return (ab + (cd - ab) * v) * 1.4142135623730951;
		}

		/// <summary>Each octave is turned 37 degrees from the one before, so no layer's lattice lines up with another's.</summary>
		private const double TurnCos = 0.7986355100472928;
		private const double TurnSin = 0.6018150231520483;

		private static void Turn(ref double x, ref double z)
		{
			double nx = x * TurnCos - z * TurnSin;
			z = x * TurnSin + z * TurnCos;
			x = nx;
		}

		/// <summary>
		/// Fractal noise: <paramref name="octaves"/> layers, each half the wavelength and <paramref name="gain"/> times the height of the one
		/// before. About -1 to 1.
		/// </summary>
		internal static double Fbm(ulong salt, double x, double z, double wavelength, int octaves, double gain)
		{
			double sum = 0.0;
			double amplitude = 1.0;
			double norm = 0.0;
			for (int i = 0; i < octaves; i++)
			{
				sum += Gradient(Mix(salt + (ulong)i), x, z, wavelength) * amplitude;
				norm += amplitude;
				amplitude *= gain;
				wavelength *= 0.5;
				Turn(ref x, ref z);
			}
			return sum / norm;
		}

		/// <summary>
		/// Ridged fractal noise, 0 to about 1: sharp crests where the plain noise crosses zero. Each layer is weighted by the one before, so
		/// the small detail sits on the ridges and the valleys stay smooth.
		/// </summary>
		internal static double Ridged(ulong salt, double x, double z, double wavelength, int octaves, double gain)
		{
			double sum = 0.0;
			double amplitude = 1.0;
			double weight = 1.0;
			double norm = 0.0;
			for (int i = 0; i < octaves; i++)
			{
				double n = 1.0 - Math.Abs(Gradient(Mix(salt + (ulong)i), x, z, wavelength));
				n *= n;
				n *= weight;
				weight = n < 1.0 ? n : 1.0;
				sum += n * amplitude;
				norm += amplitude;
				amplitude *= gain;
				wavelength *= 0.5;
				Turn(ref x, ref z);
			}
			return sum / norm;
		}

		/// <summary>0 at or below <paramref name="a"/>, 1 at or above <paramref name="b"/>, smooth in between.</summary>
		internal static double Step(double a, double b, double x)
		{
			if (x <= a)
			{
				return 0.0;
			}
			if (x >= b)
			{
				return 1.0;
			}
			return Fade((x - a) / (b - a));
		}
	}
}
