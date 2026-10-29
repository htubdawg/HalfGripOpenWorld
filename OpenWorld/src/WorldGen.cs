using System;

namespace HalfGripOpenWorld
{
	/// <summary>
	/// The shape of the world: ground height as a pure function of world coordinates (x east, z north, in game units). Nothing here keeps
	/// state but the road network, which is built once from the seed and never changes, so a tile can be built at any time, in any order,
	/// on any thread, and comes out the same on every machine.
	///
	/// Three layers, each built on the one before:
	///   <see cref="Natural"/>  the land: lowland, mountain ranges, hills, bumps, and a wall of mountains at the edge;
	///   <see cref="Base"/>     the land with a flat plateau under each town and village;
	///   <see cref="Height"/>   the ground as driven on: Base with every road's bed cut or filled into it (<see cref="Network"/>).
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
		internal const double WallDepth = 1500.0;

		private static readonly ulong SaltLow = Noise.Mix(Seed + 1);
		private static readonly ulong SaltMask = Noise.Mix(Seed + 2);
		private static readonly ulong SaltRidge = Noise.Mix(Seed + 3);
		private static readonly ulong SaltHills = Noise.Mix(Seed + 4);
		private static readonly ulong SaltFine = Noise.Mix(Seed + 5);
		private static readonly ulong SaltWarpX = Noise.Mix(Seed + 6);
		private static readonly ulong SaltWarpZ = Noise.Mix(Seed + 7);

		/// <summary>How much of the mountain ranges is at (x, z), 0 to 1. Towns and villages are put where this is 0.</summary>
		internal static double MountainMask(double x, double z)
		{
			return Noise.Step(0.0, 0.5, Noise.Gradient(SaltMask, x, z, 9000.0));
		}

		/// <summary>The land before anything is built on it.</summary>
		internal static double Natural(double x, double z)
		{
			// Rolling lowland: long, gentle swells.
			double h = Noise.Fbm(SaltLow, x, z, 4000.0, 4, 0.45) * 90.0;

			// Mountain ranges where a very slow mask allows them, so the world has open country and high country. The ridges are sampled
			// through a warp (the point pushed about by slower noise), which bends them into ranges rather than a regular pattern.
			double mask = MountainMask(x, z);
			if (mask > 0.0)
			{
				double wx = x + Noise.Fbm(SaltWarpX, x, z, 3000.0, 2, 0.5) * 900.0;
				double wz = z + Noise.Fbm(SaltWarpZ, x, z, 3000.0, 2, 0.5) * 900.0;
				h += Noise.Ridged(SaltRidge, wx, wz, 2600.0, 5, 0.5) * 520.0 * mask;
			}

			// Hills a few hundred metres across, and bumps that make the ground feel driven on.
			h += Noise.Fbm(SaltHills, x, z, 600.0, 3, 0.5) * 22.0;
			h += Noise.Gradient(SaltFine, x, z, 70.0) * 1.2;

			// The edge: a wall of mountains that rises steeply over the last WallDepth metres.
			double edge = Math.Max(Math.Abs(x), Math.Abs(z));
			double wall = Noise.Step(Half - WallDepth, Half, edge);
			h += wall * wall * 700.0;
			return h;
		}

		/// <summary>The land with a flat plateau at each town and village, blending out into the country round it (and the world's lift).</summary>
		internal static double Base(double x, double z)
		{
			double h = Natural(x, z) + Network.Lift;
			foreach (Network.Site site in Network.Sites)
			{
				double dx = x - site.X;
				double dz = z - site.Z;
				double reach = site.Radius + site.Blend;
				if (dx * dx + dz * dz >= reach * reach)
				{
					continue;
				}
				double t = Noise.Step(site.Radius, reach, Math.Sqrt(dx * dx + dz * dz));
				h = site.Y + (h - site.Y) * t;
			}
			return h;
		}

		/// <summary>Ground height at world point (x, z): the land with the road beds in it. The road surfaces sit just above this.</summary>
		internal static double Height(double x, double z)
		{
			Network.Ensure();
			return Network.Ground(x, z, Base(x, z));
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
