using System;

namespace HalfGripOpenWorld
{
	/// <summary>
	/// What the ground is at a point, as a colour for the tile's pixel-art ground texture. The rules follow how real country is used:
	/// snow on the high peaks, bare rock on cliffs, sand on the shore, built-up ground in towns and villages, a muddy yard at each farm,
	/// wet meadow on river floodplains, fields round every place (in parcels of different crops with hedges between them, the parcels
	/// turned to their own angle region by region), forest on slopes and wherever nobody farms, and grass or upland moor elsewhere.
	///
	/// Only looks: nothing here changes the ground's shape. Plain arithmetic and hashing, so it is the same every time.
	/// </summary>
	/// <summary>What the ground is, which decides its colour and what grows or stands on it.</summary>
	internal enum Cover : byte
	{
		Snow,
		Rock,
		Sand,
		Paving,
		Garden,
		Yard,
		Meadow,
		Hedge,
		Wheat,
		Crop,
		Ploughed,
		Pasture,
		Forest,
		Grass,
		Moor,
	}

	internal static class LandUse
	{
		private static readonly ulong SaltForest = Noise.Mix(WorldGen.Seed + 401);
		private static readonly ulong SaltParcel = Noise.Mix(WorldGen.Seed + 402);
		private static readonly ulong SaltSpeckle = Noise.Mix(WorldGen.Seed + 403);
		private static readonly ulong SaltTown = Noise.Mix(WorldGen.Seed + 404);

		internal struct Rgb
		{
			public byte R;
			public byte G;
			public byte B;

			public Rgb(int r, int g, int b)
			{
				R = (byte)Math.Max(0, Math.Min(255, r));
				G = (byte)Math.Max(0, Math.Min(255, g));
				B = (byte)Math.Max(0, Math.Min(255, b));
			}
		}

		private static readonly Rgb Snow = new Rgb(232, 236, 240);
		private static readonly Rgb Rock = new Rgb(120, 112, 104);
		private static readonly Rgb Sand = new Rgb(214, 198, 150);
		private static readonly Rgb Paving = new Rgb(128, 124, 118);
		private static readonly Rgb Garden = new Rgb(88, 128, 64);
		private static readonly Rgb Yard = new Rgb(122, 102, 74);
		private static readonly Rgb Meadow = new Rgb(104, 150, 76);
		private static readonly Rgb Hedge = new Rgb(46, 82, 40);
		private static readonly Rgb Forest = new Rgb(42, 78, 42);
		private static readonly Rgb Grass = new Rgb(92, 134, 62);
		private static readonly Rgb Moor = new Rgb(120, 122, 74);

		private static readonly Rgb[] Crops =
		{
			new Rgb(206, 178, 92),   // wheat
			new Rgb(170, 168, 86),   // barley
			new Rgb(110, 152, 66),   // green crop
			new Rgb(128, 96, 66),    // ploughed
			new Rgb(118, 160, 80),   // pasture
			new Rgb(98, 142, 60),    // pasture
		};

		/// <summary>The ground's colour at world (x, z), whose ground height (from sea level) is <paramref name="h"/> and slope (rise over run) <paramref name="slope"/>.</summary>
		internal static Rgb At(double x, double z, double h, double slope)
		{
			return Colour(Classify(x, z, h, slope, out int crop), crop, x, z);
		}

		/// <summary>What the ground is at world (x, z) (see the class summary for the rules); <paramref name="crop"/> picks a field's crop colour.</summary>
		internal static Cover Classify(double x, double z, double h, double slope, out int crop)
		{
			crop = 0;
			if (h > 650.0 + Noise.Gradient(SaltForest, x, z, 400.0) * 60.0 && slope < 0.9)
			{
				return Cover.Snow;
			}
			double mountain = WorldGen.MountainMask(x, z);
			if (slope > 0.62 || (mountain > 0.3 && slope > 0.4) || h > 720.0)
			{
				return Cover.Rock;
			}
			if (h < WorldGen.SeaLevel + 5.0 && WorldGen.Inland(x, z) < 600.0)
			{
				return Cover.Sand;
			}

			Places.Place place = Places.At(x, z);
			if (place != null)
			{
				if (place.Kind == PlaceKind.Hamlet)
				{
					// The yard round the farm buildings: an irregular patch, not the farm's whole circle.
					double fx = x - place.X, fz = z - place.Z;
					if (Math.Sqrt(fx * fx + fz * fz) * (1.0 + Noise.Gradient(SaltTown, x, z, 50.0) * 0.4) < 80.0)
					{
						return Cover.Yard;
					}
				}
				// Built-up along the streets (yards and house plots), gardens behind them; away from the streets the country goes on, so a
				// town is its streets, not a disc.
				double dx = x - place.X, dz = z - place.Z;
				double from = Math.Sqrt(dx * dx + dz * dz) / place.Radius;
				double street = Network.Nearest(x, z, 90.0, RoadKind.Trunk);
				double plots = 14.0 + 22.0 * (1.0 - from) + Noise.Gradient(SaltTown, x, z, 40.0) * 8.0;
				if (street < plots)
				{
					return Cover.Paving;
				}
				if (street < plots + 45.0)
				{
					return Cover.Garden;
				}
			}

			if (Rivers.Distance(x, z, out double plain) < plain)
			{
				return Cover.Meadow;
			}

			// Whether land is farmed is decided field by field, at each parcel's middle, so farmland ends at a hedge and woods meet
			// fields along field edges (deciding it point by point draws rings round the places).
			Parcel(x, z, out double cx, out double cz, out bool edge, out ulong parcel);
			double farmed = Places.Farmland(cx, cz);
			double wild = Noise.Fbm(SaltForest, cx, cz, 1800.0, 3, 0.5);
			if (farmed > 0.0 && slope < 0.14 && h < 420.0 && farmed + wild * 0.6 > 0.3)
			{
				if (edge)
				{
					return Cover.Hedge;
				}
				crop = (int)(parcel % (ulong)Crops.Length);
				return crop <= 1 ? Cover.Wheat : crop == 2 ? Cover.Crop : crop == 3 ? Cover.Ploughed : Cover.Pasture;
			}
			if (wild + slope * 1.6 - farmed * 0.8 + (h > 250.0 ? 0.1 : 0.0) > 0.02)
			{
				return Cover.Forest;
			}
			return h > 280.0 ? Cover.Moor : Cover.Grass;
		}

		/// <summary>The colour of a cover, with a little per-pixel speckle.</summary>
		internal static Rgb Colour(Cover cover, int crop, double x, double z)
		{
			int speckle = (int)(Noise.Mix(SaltSpeckle ^ (ulong)(long)Math.Floor(x / 4.0) * 73856093UL ^ (ulong)(long)Math.Floor(z / 4.0) * 19349663UL) >> 61) - 4;
			switch (cover)
			{
				case Cover.Snow: return Shade(Snow, speckle);
				case Cover.Rock: return Shade(Rock, speckle * 2);
				case Cover.Sand: return Shade(Sand, speckle);
				case Cover.Paving: return Shade(Paving, speckle);
				case Cover.Garden: return Shade(Garden, speckle);
				case Cover.Yard: return Shade(Yard, speckle);
				case Cover.Meadow: return Shade(Meadow, speckle);
				case Cover.Hedge: return Shade(Hedge, speckle);
				case Cover.Wheat:
				case Cover.Crop:
				case Cover.Ploughed:
				case Cover.Pasture: return Shade(Crops[crop], speckle);
				case Cover.Forest: return Shade(Forest, speckle * 2);
				case Cover.Moor: return Shade(Moor, speckle);
				default: return Shade(Grass, speckle);
			}
		}

		/// <summary>
		/// The field parcel (x, z) is in: the land is cut into parcels on a grid turned to a region's own angle (regions of about 3 km with
		/// wandering borders), parcels of 150 to 270 units by 1.4 times that; its middle (cx, cz), whether (x, z) is on its edge (where
		/// a hedge grows), and a hash of it for its crop.
		/// </summary>
		private static void Parcel(double x, double z, out double cx, out double cz, out bool edge, out ulong parcel)
		{
			double qx = x + Noise.Fbm(SaltParcel + 1, x, z, 2500.0, 2, 0.5) * 900.0;
			double qz = z + Noise.Fbm(SaltParcel + 2, x, z, 2500.0, 2, 0.5) * 900.0;
			long rx = (long)Math.Floor(qx / 3000.0), rz = (long)Math.Floor(qz / 3000.0);
			ulong region = Noise.Mix(SaltParcel ^ (ulong)rx * 0x9E3779B1UL ^ (ulong)rz * 0x85EBCA77UL);
			double angle = (region >> 40) / (double)(1UL << 24) * DetMath.Pi / 2.0;
			double c = DetMath.Cos(angle), s = DetMath.Sin(angle);
			double u = x * c + z * s, v = -x * s + z * c;
			double size = 150.0 + (region >> 60) * 8.0;
			long pu = (long)Math.Floor(u / size), pv = (long)Math.Floor(v / (size * 1.4));
			double fu = u - pu * size, fv = v - pv * size * 1.4;
			edge = fu < 5.0 || fv < 5.0;
			double mu = (pu + 0.5) * size, mv = (pv + 0.5) * size * 1.4;
			cx = mu * c - mv * s;
			cz = mu * s + mv * c;
			parcel = Noise.Mix(region ^ (ulong)pu * 0x632BE59BUL ^ (ulong)pv * 0x9E3779B97F4A7C15UL);
		}

		private static Rgb Shade(Rgb c, int by)
		{
			return new Rgb(c.R + by * 3, c.G + by * 3, c.B + by * 3);
		}
	}
}
