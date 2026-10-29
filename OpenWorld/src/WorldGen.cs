using System;

namespace HalfGripOpenWorld
{
	/// <summary>
	/// The shape of the world: ground height as a pure function of world coordinates (metres, x east and z north of the spawn). Nothing
	/// here keeps state, so a tile can be built at any time, in any order, on any thread, and comes out the same on every machine.
	///
	/// The layers, from biggest to smallest: rolling lowland, mountain ranges where a very slow mask allows them, mid-size hills, and fine
	/// bumps. The spawn sits on a flat pad that blends into the land, and the edge of the world is a wall of mountains.
	/// </summary>
	internal static class WorldGen
	{
		/// <summary>
		/// The world's seed. Fixed, so the world is the same every time and for everybody: it is a place to learn, not a new track each
		/// race. Changing it changes the world, so every player in a room needs the same mod version.
		/// </summary>
		internal const ulong Seed = 0x0E7A_2026_1029_0001UL;

		/// <summary>Half the side of the world square: 16 km, so the world is 32 km (20 miles) across.</summary>
		internal const double Half = 16000.0;

		/// <summary>The mountain wall at the edge starts this far inside it.</summary>
		private const double WallDepth = 1500.0;

		/// <summary>Within this radius of the spawn the ground is flat at height 0 (the grid and the start).</summary>
		private const double PadRadius = 250.0;

		/// <summary>The flat pad blends into the land over this distance beyond <see cref="PadRadius"/>.</summary>
		private const double PadBlend = 1250.0;

		private static readonly ulong SaltLow = Noise.Mix(Seed + 1);
		private static readonly ulong SaltMask = Noise.Mix(Seed + 2);
		private static readonly ulong SaltRidge = Noise.Mix(Seed + 3);
		private static readonly ulong SaltHills = Noise.Mix(Seed + 4);
		private static readonly ulong SaltFine = Noise.Mix(Seed + 5);
		private static readonly ulong SaltWarpX = Noise.Mix(Seed + 6);
		private static readonly ulong SaltWarpZ = Noise.Mix(Seed + 7);

		/// <summary>Ground height in metres at world point (x, z).</summary>
		internal static double Height(double x, double z)
		{
			// Rolling lowland: long, gentle swells.
			double h = Noise.Fbm(SaltLow, x, z, 4000.0, 4, 0.45) * 90.0;

			// Mountain ranges where a very slow mask allows them, so the world has open country and high country. The ridges are sampled
			// through a warp (the point pushed about by slower noise), which bends them into ranges rather than a regular pattern.
			double mask = Noise.Step(0.0, 0.5, Noise.Gradient(SaltMask, x, z, 9000.0));
			if (mask > 0.0)
			{
				double wx = x + Noise.Fbm(SaltWarpX, x, z, 3000.0, 2, 0.5) * 900.0;
				double wz = z + Noise.Fbm(SaltWarpZ, x, z, 3000.0, 2, 0.5) * 900.0;
				h += Noise.Ridged(SaltRidge, wx, wz, 2600.0, 5, 0.5) * 520.0 * mask;
			}

			// Hills a few hundred metres across, and bumps that make the ground feel driven on.
			h += Noise.Fbm(SaltHills, x, z, 600.0, 3, 0.5) * 22.0;
			h += Noise.Gradient(SaltFine, x, z, 70.0) * 1.2;

			// The spawn pad: flat at 0, blending out.
			double fromSpawn = Math.Sqrt(x * x + z * z);
			h *= Noise.Step(PadRadius, PadRadius + PadBlend, fromSpawn);

			// The edge: a wall of mountains that rises steeply over the last WallDepth metres.
			double edge = Math.Max(Math.Abs(x), Math.Abs(z));
			double wall = Noise.Step(Half - WallDepth, Half, edge);
			h += wall * wall * 700.0;
			return h;
		}

		/// <summary>True when (x, z) is inside the world square, where tiles are built.</summary>
		internal static bool Inside(double x, double z)
		{
			return Math.Abs(x) <= Half && Math.Abs(z) <= Half;
		}

		/// <summary>The nearest point at least <paramref name="margin"/> metres inside the edge wall (for resets).</summary>
		internal static void ClampInside(ref double x, ref double z, double margin)
		{
			double limit = Half - WallDepth - margin;
			x = Math.Max(-limit, Math.Min(limit, x));
			z = Math.Max(-limit, Math.Min(limit, z));
		}
	}
}
