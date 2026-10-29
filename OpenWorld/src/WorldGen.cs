using System;

namespace HalfGripOpenWorld
{
	/// <summary>
	/// The shape of the world: ground height as a pure function of world coordinates (x east, z north, in game units). Nothing here keeps
	/// state but the rivers, places and roads, which are built once from the seed and never change, so a tile can be built at any time,
	/// in any order, on any thread, and comes out the same on every machine.
	///
	/// Layers, each built on the one before:
	///   <see cref="Natural"/>  the land: sea and coastal plain in the south, uplands and mountain ranges in the north, edge walls;
	///   <see cref="Land"/>     with the rivers' channels, floodplains and banks cut in (<see cref="Rivers"/>);
	///   <see cref="Base"/>     softened under each place (<see cref="Places"/>), rivers cut again;
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

		private static readonly ulong SaltCoast = Noise.Mix(Seed + 8);
		private static readonly ulong SaltRolling = Noise.Mix(Seed + 9);

		/// <summary>Sea level, before the world's lift (<see cref="Network.Lift"/>).</summary>
		internal const double SeaLevel = 0.0;

		/// <summary>
		/// How far north of the coast (x, z) is, in units: negative out at sea. The coast runs east-west about 5 km in from the south edge,
		/// wandering by up to 2 km.
		/// </summary>
		internal static double Inland(double x, double z)
		{
			double coastZ = -Half + 5000.0 + Noise.Fbm(SaltCoast, x, 0.0, 7000.0, 3, 0.5) * 2200.0;
			return z - coastZ;
		}

		/// <summary>How much of the mountain ranges is at (x, z), 0 to 1: mostly in the north, none near the coast.</summary>
		internal static double MountainMask(double x, double z)
		{
			double north = Inland(x, z) / 20000.0;
			return Noise.Step(0.0, 0.5, Noise.Gradient(SaltMask, x, z, 9000.0) + 1.1 * north - 0.9);
		}

		/// <summary>
		/// The land before anything is built on it or cut by rivers: a coastal plain and the sea to the south, the ground rising to uplands
		/// and mountain ranges to the north, and a wall of mountains along the north, west and east edges (the south edge is open sea).
		/// </summary>
		internal static double Natural(double x, double z)
		{
			double inland = Inland(x, z);
			// 0 out at sea, 1 inland: the sea floor shelves down over 2.5 km off the coast.
			double shelf = Noise.Step(-2500.0, 1200.0, inland);
			double rise = 230.0 * Noise.Step(0.0, 22000.0, inland);

			// The swells grow inland, so the coastal plain is low and gentle and does not dip under the sea behind the shore.
			double swell = 100.0 * (0.3 + 0.7 * Noise.Step(0.0, 9000.0, inland));
			double h = Noise.Fbm(SaltLow, x, z, 4000.0, 4, 0.45) * swell + rise + 14.0;

			double mask = MountainMask(x, z);
			if (mask > 0.0)
			{
				double wx = x + Noise.Fbm(SaltWarpX, x, z, 3000.0, 2, 0.5) * 900.0;
				double wz = z + Noise.Fbm(SaltWarpZ, x, z, 3000.0, 2, 0.5) * 900.0;
				h += Noise.Ridged(SaltRidge, wx, wz, 2600.0, 5, 0.5) * 560.0 * mask;
			}
			// Rolling hills a kilometre or two across, and smaller hills on them: the lowland is never flat for long.
			h += Noise.Fbm(SaltRolling, x, z, 1600.0, 2, 0.5) * 45.0 * Noise.Step(-500.0, 3000.0, inland);
			h += Noise.Fbm(SaltHills, x, z, 600.0, 3, 0.5) * 40.0 * (0.3 + 0.7 * shelf);
			h += Noise.Gradient(SaltFine, x, z, 70.0) * 1.2;
			// Inland, the land does not sink under the sea: its hollows are squeezed into shallow basins just above sea level (where the
			// lakes form), fading in over the first 2.5 km from the coast.
			const double floor = 3.0;
			if (h < floor)
			{
				double squeeze = Noise.Step(500.0, 2500.0, inland);
				h = h + (floor + (h - floor) * 0.12 - h) * squeeze;
			}
			h = h * shelf - 45.0 * (1.0 - shelf);

			// The wall: north, west and east edges.
			double edge = Math.Max(Math.Abs(x), z);
			double wall = Noise.Step(Half - WallDepth, Half, edge);
			h += wall * wall * 700.0;
			return h;
		}

		/// <summary>The land with the rivers cut into it (what places are chosen on).</summary>
		internal static double Land(double x, double z)
		{
			return Rivers.Cut(x, z, Natural(x, z));
		}

		/// <summary>
		/// The land as roads are laid on it: softened under each town, village and farm (<see cref="Places.Soften"/>), then the rivers cut
		/// in again, so a river keeps its channel and floodplain through a town.
		/// </summary>
		internal static double Base(double x, double z)
		{
			return Rivers.Cut(x, z, Places.Soften(x, z, Natural(x, z)));
		}

		/// <summary>
		/// Ground height at world point (x, z) in the scene: the land with the road beds in it, plus the world's lift (everything inside
		/// the generator is measured from sea level; the scene is lifted so the road at the spawn is at 0.5). Road surfaces sit just
		/// above this.
		/// </summary>
		internal static double Height(double x, double z)
		{
			Network.Ensure();
			return Network.Ground(x, z, Base(x, z)) + Network.Lift;
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
