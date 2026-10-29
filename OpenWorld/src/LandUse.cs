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
			int speckle = (int)(Noise.Mix(SaltSpeckle ^ (ulong)(long)Math.Floor(x / 4.0) * 73856093UL ^ (ulong)(long)Math.Floor(z / 4.0) * 19349663UL) >> 61) - 4;

			if (h > 650.0 + Noise.Gradient(SaltForest, x, z, 400.0) * 60.0 && slope < 0.9)
			{
				return Shade(Snow, speckle);
			}
			double mountain = WorldGen.MountainMask(x, z);
			if (slope > 0.62 || (mountain > 0.3 && slope > 0.4) || h > 720.0)
			{
				return Shade(Rock, speckle * 2);
			}
			if (h < WorldGen.SeaLevel + 5.0 && WorldGen.Inland(x, z) < 600.0)
			{
				return Shade(Sand, speckle);
			}

			Places.Place place = Places.At(x, z);
			if (place != null)
			{
				double dx = x - place.X, dz = z - place.Z;
				double from = Math.Sqrt(dx * dx + dz * dz) / place.Radius;
				if (place.Kind == PlaceKind.Hamlet)
				{
					return Shade(Yard, speckle);
				}
				// Built-up along the streets (yards, house plots), gardens behind, and fewer houses towards the edge.
				double street = Network.Nearest(x, z, 60.0, RoadKind.Street);
				double plots = 22.0 + 18.0 * (1.0 - from) + Noise.Gradient(SaltTown, x, z, 40.0) * 10.0;
				return Shade(street < plots ? Paving : Garden, speckle);
			}

			if (Rivers.Distance(x, z, out double plain) < plain)
			{
				return Shade(Meadow, speckle);
			}

			double farmed = Places.Farmland(x, z);
			double wild = Noise.Fbm(SaltForest, x, z, 1800.0, 3, 0.5);
			if (farmed > 0.0 && slope < 0.14 && h < 420.0 && farmed + wild * 0.6 > 0.3)
			{
				return Field(x, z, speckle);
			}
			if (wild + slope * 1.6 - farmed * 0.8 + (h > 250.0 ? 0.1 : 0.0) > 0.02)
			{
				return Shade(Forest, speckle * 2);
			}
			return Shade(h > 280.0 ? Moor : Grass, speckle);
		}

		/// <summary>
		/// A field: the land is cut into parcels on a grid turned to a region's own angle (regions of 3 km), parcels of 150 to 260
		/// units, each with a crop chosen by hashing it, and a hedge round its edge.
		/// </summary>
		private static Rgb Field(double x, double z, int speckle)
		{
			long rx = (long)Math.Floor(x / 3000.0), rz = (long)Math.Floor(z / 3000.0);
			ulong region = Noise.Mix(SaltParcel ^ (ulong)rx * 0x9E3779B1UL ^ (ulong)rz * 0x85EBCA77UL);
			double angle = (region >> 40) / (double)(1UL << 24) * DetMath.Pi / 2.0;
			double c = DetMath.Cos(angle), s = DetMath.Sin(angle);
			double u = x * c + z * s, v = -x * s + z * c;
			double size = 150.0 + (region >> 60) * 8.0;
			long pu = (long)Math.Floor(u / size), pv = (long)Math.Floor(v / (size * 1.4));
			double fu = u - pu * size, fv = v - pv * size * 1.4;
			if (fu < 5.0 || fv < 5.0)
			{
				return Shade(Hedge, speckle);
			}
			ulong parcel = Noise.Mix(region ^ (ulong)pu * 0x632BE59BUL ^ (ulong)pv * 0x9E3779B97F4A7C15UL);
			return Shade(Crops[(int)(parcel % (ulong)Crops.Length)], speckle);
		}

		private static Rgb Shade(Rgb c, int by)
		{
			return new Rgb(c.R + by * 3, c.G + by * 3, c.B + by * 3);
		}
	}
}
