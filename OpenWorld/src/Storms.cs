using System;

namespace HalfGripOpenWorld
{
	/// <summary>
	/// The weather over the world: rain cells that are born, drift with the wind, build, rain and die away, so it rains in some places
	/// and not in others, and a shower can be seen coming, driven out of or waited out. Snow instead of rain above the race's snow line.
	///
	/// Everything is a pure function of the room's seed, a world position and the race clock (<c>TrackManager.TimeSinceRaceStart</c>,
	/// which the Multiplayer mod starts on the host's go for everybody). So every player in a room gets the same storms in the same places
	/// at the same moments without anything being sent, and a player who drives to where another is sees the same sky. Like the world's
	/// layout it draws only from integer hashing and plain arithmetic (<see cref="Noise"/>, <see cref="DetMath"/>), never System.Random or
	/// UnityEngine.Random.
	///
	/// The cells: race time is cut into slots of <see cref="Slot"/> seconds, and each slot gives birth to a few cells (more on a wetter
	/// day), each with its own birth time, life, size, strength and drift (the wind at its birth, which turns slowly over the race, plus a
	/// little of its own). Cells born before the race started are counted too, so the race does not begin under a clear sky everywhere.
	/// A cell's middle is where it rains hardest, ragged at the edge (noise carried along with the cell); its cloud reaches further, so
	/// the sky darkens before the rain comes. High ground gets a little more rain.
	///
	/// Wet roads have memory: <see cref="Wetness"/> looks back over the last <see cref="DrySeconds"/> of rain at a spot, so a road a storm
	/// has just passed over is still wet under a clear sky, and dries.
	/// </summary>
	internal sealed class Storms
	{
		/// <summary>Seconds of race time per birth slot.</summary>
		internal const double Slot = 600.0;

		/// <summary>The longest a cell lives; slots this far back can still have a cell alive.</summary>
		private const double MaxLife = 3600.0;

		/// <summary>The most cells one slot can give birth to (on the wettest day).</summary>
		private const int MaxPerSlot = 20;

		/// <summary>How long a road takes to dry after the rain on it stops, in seconds.</summary>
		internal const double DrySeconds = 600.0;

		/// <summary>Steps of the look back for <see cref="Wetness"/>.</summary>
		private const double WetStep = 30.0;

		private readonly ulong salt;

		/// <summary>How wet the day is, 0.1 to 1: scales how many cells are born.</summary>
		internal readonly double Raininess;

		/// <summary>Height from sea level above which it snows rather than rains (with a band of sleet either side).</summary>
		internal readonly double SnowLine;

		/// <summary>The wind's direction at the start (radians, 0 = towards +x, east) and speed (units a second).</summary>
		internal readonly double WindAngle;
		internal readonly double WindSpeed;

		private readonly double windPhase;

		internal Storms(int roomSeed)
		{
			salt = Noise.Mix((ulong)(uint)roomSeed ^ 0x5707_3A1D_0E7AUL);
			Rng rng = new Rng(salt);
			Raininess = 0.1 + 0.9 * rng.Next();
			SnowLine = rng.Range(330.0, 480.0);
			WindAngle = rng.Range(0.0, 2.0 * DetMath.Pi);
			WindSpeed = rng.Range(4.0, 11.0);
			windPhase = rng.Range(0.0, 2.0 * DetMath.Pi);
		}

		/// <summary>One rain cell, worked out from its slot and number.</summary>
		private struct Cell
		{
			internal double X0, Z0, Vx, Vz, Born, Life, Radius, Strength;
			internal ulong Salt;

			internal double X(double t)
			{
				return X0 + Vx * (t - Born);
			}

			internal double Z(double t)
			{
				return Z0 + Vz * (t - Born);
			}

			/// <summary>0 before birth and after death, rising over the first quarter of its life and dying away over the last third.</summary>
			internal double Envelope(double t)
			{
				double a = (t - Born) / Life;
				if (a <= 0.0 || a >= 1.0)
				{
					return 0.0;
				}
				return Noise.Step(0.0, 0.25, a) * (1.0 - Noise.Step(0.65, 1.0, a));
			}
		}

		/// <summary>A value from 0 to 1 for slot <paramref name="k"/>, cell <paramref name="i"/>, property <paramref name="j"/>.</summary>
		private double U(long k, int i, int j)
		{
			unchecked
			{
				ulong h = Noise.Mix(salt ^ Noise.Mix((ulong)k * 0x9E3779B97F4A7C15UL ^ Noise.Mix((ulong)(i * 64 + j))));
				return (double)(h >> 11) * (1.0 / 9007199254740992.0);
			}
		}

		/// <summary>How many cells slot <paramref name="k"/> gives birth to.</summary>
		private int Count(long k)
		{
			return (int)(Raininess * (MaxPerSlot + 1) * U(k, 255, 0));
		}

		private Cell Make(long k, int i)
		{
			Cell c = new Cell();
			c.Born = (k + U(k, i, 0)) * Slot;
			c.Life = 1200.0 + 2400.0 * U(k, i, 1);
			c.Radius = 1400.0 + 3000.0 * U(k, i, 2);
			c.Strength = 0.35 + 0.65 * U(k, i, 3);
			// The wind at the cell's birth: it swings slowly either side of the day's direction over a couple of hours.
			double angle = WindAngle + 0.6 * DetMath.Sin(c.Born / 5400.0 + windPhase);
			double speed = WindSpeed * (0.8 + 0.4 * U(k, i, 4));
			c.Vx = speed * DetMath.Cos(angle) + (U(k, i, 5) - 0.5) * 3.0;
			c.Vz = speed * DetMath.Sin(angle) + (U(k, i, 6) - 0.5) * 3.0;
			// Placed so that at the middle of its life it is anywhere over the world or a little beyond, then wound back to its birth.
			double mx = (U(k, i, 7) * 2.0 - 1.0) * WorldGen.Half * 1.2;
			double mz = (U(k, i, 8) * 2.0 - 1.0) * WorldGen.Half * 1.2;
			c.X0 = mx - c.Vx * c.Life * 0.5;
			c.Z0 = mz - c.Vz * c.Life * 0.5;
			c.Salt = Noise.Mix(salt + (ulong)(k * MaxPerSlot + i) * 0x632BE59BD9B4E019UL);
			return c;
		}

		/// <summary>
		/// The weather at world (x, z) at race time <paramref name="t"/> seconds, for ground <paramref name="height"/> above sea level:
		/// <paramref name="rain"/> how hard it is raining or snowing (0 dry, 1 a downpour) and <paramref name="cloud"/> how overcast it is
		/// (0 clear, 1 a black sky).
		/// </summary>
		internal void At(double x, double z, double t, double height, out double rain, out double cloud)
		{
			double dry = 1.0, clear = 1.0;
			double lift = 1.0 + 0.35 * Noise.Step(150.0, 450.0, height);
			long first = (long)Math.Floor((t - MaxLife) / Slot);
			long last = (long)Math.Floor(t / Slot);
			for (long k = first; k <= last; k++)
			{
				int n = Count(k);
				for (int i = 0; i < n; i++)
				{
					Cell c = Make(k, i);
					double env = c.Envelope(t);
					if (env <= 0.0)
					{
						continue;
					}
					double dx = x - c.X(t), dz = z - c.Z(t);
					double d = Math.Sqrt(dx * dx + dz * dz) / c.Radius;
					if (d >= 1.8)
					{
						continue;
					}
					double over = c.Strength * env * (1.0 - Noise.Step(0.6, 1.8, d));
					clear *= 1.0 - Math.Min(1.0, over * 1.2);
					if (d < 1.0)
					{
						// Ragged: noise carried along with the cell, so the rain's edge is not a circle and has gaps. Sampled off the lattice at the
						// middle, where a corner's value would otherwise make a hole or a ring.
						double ragged = 0.7 + 0.3 * Noise.Value(c.Salt, dx + c.Radius * 0.37, dz - c.Radius * 0.21, c.Radius * 0.45);
						double r = c.Strength * env * (1.0 - Noise.Step(0.3, 1.0, d)) * ragged * lift;
						dry *= 1.0 - Noise.Step(0.1, 0.85, r);
					}
				}
			}
			rain = 1.0 - dry;
			cloud = Math.Max(1.0 - clear, rain);
		}

		/// <summary>How wet the ground at (x, z) is at time <paramref name="t"/>, 0 to 1: the rain there over the last <see cref="DrySeconds"/>, the older the less.</summary>
		internal double Wetness(double x, double z, double t, double height)
		{
			double wet = 0.0;
			for (double back = 0.0; back < DrySeconds; back += WetStep)
			{
				At(x, z, t - back, height, out double rain, out double _);
				wet = Math.Max(wet, Math.Min(1.0, rain * 2.0) * (1.0 - back / DrySeconds));
			}
			return wet;
		}

		/// <summary>How much of what falls at <paramref name="height"/> above sea level is snow, 0 to 1 (sleet in between).</summary>
		internal double SnowShare(double height)
		{
			return Noise.Step(SnowLine - 50.0, SnowLine + 50.0, height);
		}

		/// <summary>
		/// The nearest live cell to world (x, z) at time <paramref name="t"/> (by distance to its rain's edge), for the debug overlay:
		/// false if there is none. <paramref name="edge"/> is negative inside it.
		/// </summary>
		internal bool Nearest(double x, double z, double t, out double dx, out double dz, out double edge, out double strength)
		{
			dx = dz = 0.0;
			edge = double.MaxValue;
			strength = 0.0;
			long first = (long)Math.Floor((t - MaxLife) / Slot);
			long last = (long)Math.Floor(t / Slot);
			for (long k = first; k <= last; k++)
			{
				int n = Count(k);
				for (int i = 0; i < n; i++)
				{
					Cell c = Make(k, i);
					double env = c.Envelope(t);
					if (env < 0.2)
					{
						continue;
					}
					double cx = c.X(t) - x, cz = c.Z(t) - z;
					double e = Math.Sqrt(cx * cx + cz * cz) - c.Radius;
					if (e < edge)
					{
						edge = e;
						dx = cx;
						dz = cz;
						strength = c.Strength * env;
					}
				}
			}
			return edge < double.MaxValue;
		}
	}
}
