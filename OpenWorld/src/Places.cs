using System;
using System.Collections.Generic;

namespace HalfGripOpenWorld
{
	internal enum PlaceKind
	{
		Town = 0,
		Village = 1,
		Hamlet = 2,
	}

	/// <summary>
	/// Where people live. Every spot on a 200-unit grid is scored for how good a place to settle it is, the way real settlements were
	/// placed: flat ground; near a river (water, a crossing) but above its floodplain; near the coast; low and out of the mountains. Then
	/// places are taken best first, in tiers, each tier keeping its distance from what is already there: one or two towns, about a dozen
	/// villages, and hamlets and farms in between. That gives the spread real country has, a few big places and many small ones, fairly
	/// evenly spaced (central place theory), without any being put by hand.
	///
	/// Sizes are in game units, about 3.3 to a real metre: a town about 1.1 km across, a village 300 m, a hamlet or farm 70 m.
	/// </summary>
	internal static class Places
	{
		internal sealed class Place
		{
			public PlaceKind Kind;
			public string Name;
			public double X;
			public double Z;
			/// <summary>The height the place is levelled towards.</summary>
			public double Y;
			public double Radius;
			/// <summary>How much of the land's own relief is kept inside the place (a town keeps its slopes; a farmyard is flat).</summary>
			public double Relief;
			public double Population;
			public double Score;
		}

		internal static readonly List<Place> All = new List<Place>();

		private static readonly string[] TownNames = { "Kingsbridge", "Westhaven" };
		private static readonly string[] VillageNames =
		{
			"Ashby", "Brookfield", "Carrow", "Dunmore", "Elmsted", "Fenwick", "Glenford", "Harrow", "Ivybridge", "Kestrel", "Lowmoor", "Millbrook",
			"Northcote", "Oakhurst", "Pinewick", "Redford",
		};

		private const double Grid = 200.0;
		private const int N = 160;
		private const double Min = -N * Grid / 2.0;

		/// <summary>Spacing between places of a tier and everything placed before it.</summary>
		private const double TownSpacing = 11000.0;
		private const double VillageSpacing = 4200.0;
		private const double HamletSpacing = 1500.0;

		internal static void Build(Rng rng)
		{
			double[] score = new double[N * N];
			double[] farm = new double[N * N];
			double best = 0.0, bestFarm = 0.0;
			for (int j = 0; j < N; j++)
			{
				for (int i = 0; i < N; i++)
				{
					double x = Min + (i + 0.5) * Grid, z = Min + (j + 0.5) * Grid;
					score[j * N + i] = Suitability(x, z);
					farm[j * N + i] = FarmSuitability(x, z);
					best = Math.Max(best, score[j * N + i]);
					bestFarm = Math.Max(bestFarm, farm[j * N + i]);
				}
			}
			List<int> order = Order(score);
			Take(rng, order, score, PlaceKind.Town, TownSpacing, best * 0.75, TownNames.Length, TownNames);
			Take(rng, order, score, PlaceKind.Village, VillageSpacing, best * 0.3, VillageNames.Length, VillageNames);
			Take(rng, Order(farm), farm, PlaceKind.Hamlet, HamletSpacing, bestFarm * 0.25, 120, null);
			Index();
		}

		private static List<int> Order(double[] score)
		{
			List<int> order = new List<int>(N * N);
			for (int c = 0; c < N * N; c++)
			{
				if (score[c] > 0.0)
				{
					order.Add(c);
				}
			}
			order.Sort((a, b) => score[b] != score[a] ? score[b].CompareTo(score[a]) : a.CompareTo(b));
			return order;
		}

		/// <summary>
		/// How good a spot is for a farm: flat enough, a stream within reach, out of the mountains; height matters little (farms run up
		/// into the uplands, where villages do not).
		/// </summary>
		private static double FarmSuitability(double x, double z)
		{
			if (Score(x, z, false) <= 0.0)
			{
				return 0.0;
			}
			double gx = (WorldGen.Land(x + 100.0, z) - WorldGen.Land(x - 100.0, z)) / 200.0;
			double gz = (WorldGen.Land(x, z + 100.0) - WorldGen.Land(x, z - 100.0)) / 200.0;
			double flat = Math.Max(0.0, 1.0 - Math.Sqrt(gx * gx + gz * gz) / 0.1);
			double d = Rivers.Distance(x, z, out double plain);
			double water = d < plain + 2500.0 ? 1.0 - (d - plain) / 2500.0 : 0.0;
			double h = WorldGen.Land(x, z);
			double high = Math.Max(0.0, 1.0 - Math.Max(0.0, h - 300.0) / 300.0);
			return flat * (0.55 + 0.45 * water) * high * (1.0 - WorldGen.MountainMask(x, z));
		}

		private static void Take(Rng rng, List<int> order, double[] score, PlaceKind kind, double spacing, double floor, int most, string[] names)
		{
			int taken = 0;
			foreach (int c in order)
			{
				if (taken >= most || score[c] < floor)
				{
					break;
				}
				// A little jitter inside the cell, so places do not sit on a grid.
				double x = Min + (c % N + 0.2 + 0.6 * rng.Next()) * Grid;
				double z = Min + (c / N + 0.2 + 0.6 * rng.Next()) * Grid;
				bool clear = true;
				foreach (Place p in All)
				{
					// Hamlets keep their own spacing from each other but sit closer to villages and towns (outside their edge).
					double need = kind == PlaceKind.Hamlet && p.Kind != PlaceKind.Hamlet ? p.Radius + 900.0 : spacing;
					if ((p.X - x) * (p.X - x) + (p.Z - z) * (p.Z - z) < need * need)
					{
						clear = false;
						break;
					}
				}
				if (!clear || Score(x, z, false) <= 0.0)
				{
					continue;
				}
				Place place = new Place { Kind = kind, X = x, Z = z, Score = score[c] };
				switch (kind)
				{
					case PlaceKind.Town:
						place.Radius = 1800.0;
						place.Relief = 0.45;
						place.Population = taken == 0 ? 6000.0 : 2500.0;
						break;
					case PlaceKind.Village:
						place.Radius = 500.0;
						place.Relief = 0.35;
						place.Population = 150.0 + 450.0 * score[c] / Math.Max(1e-9, order.Count > 0 ? score[order[0]] : 1.0);
						break;
					default:
						place.Radius = 120.0;
						place.Relief = 0.0;
						place.Population = 15.0;
						break;
				}
				place.Name = names != null && taken < names.Length ? names[taken] : (kind == PlaceKind.Hamlet ? "Farm " + (taken + 1) : kind + " " + (taken + 1));
				place.Y = Level(place);
				All.Add(place);
				taken++;
			}
		}

		/// <summary>
		/// How good a place to settle (x, z) is, 0 (no) to about 1: flat ground first, then a river within reach (but not its floodplain),
		/// the coast within reach, and low ground out of the mountains. Never on water, at the shore, or near the edge walls.
		/// </summary>
		internal static double Suitability(double x, double z)
		{
			return Score(x, z, true);
		}

		/// <summary>The settlement score; with <paramref name="lowland"/> false, only whether the spot can be built on at all (1 or 0).</summary>
		private static double Score(double x, double z, bool lowland)
		{
			double edge = WorldGen.Half - WorldGen.WallDepth - 1200.0;
			if (Math.Abs(x) > edge || z > edge || !double.IsNaN(Rivers.SurfaceAt(x, z)))
			{
				return 0.0;
			}
			double h = WorldGen.Land(x, z);
			if (h < WorldGen.SeaLevel + 3.0)
			{
				return 0.0;
			}
			double gx = (WorldGen.Land(x + 100.0, z) - WorldGen.Land(x - 100.0, z)) / 200.0;
			double gz = (WorldGen.Land(x, z + 100.0) - WorldGen.Land(x, z - 100.0)) / 200.0;
			double slope = Math.Sqrt(gx * gx + gz * gz);
			double flat = Math.Max(0.0, 1.0 - slope / 0.08);
			if (flat <= 0.0)
			{
				return 0.0;
			}
			double d = Rivers.Distance(x, z, out double plain);
			if (d < plain + 60.0)
			{
				return 0.0;
			}
			double river = d < plain + 1500.0 ? 1.0 - (d - plain) / 1500.0 : 0.0;
			double inland = WorldGen.Inland(x, z);
			double coast = inland > 300.0 && inland < 4000.0 ? 1.0 - (inland - 300.0) / 3700.0 : 0.0;
			if (!lowland)
			{
				return 1.0;
			}
			double low = Math.Max(0.0, 1.0 - Math.Max(0.0, h - 60.0) / 260.0);
			double mountains = 1.0 - WorldGen.MountainMask(x, z);
			return flat * (0.35 + 0.4 * river + 0.25 * coast) * low * mountains;
		}

		/// <summary>The height a place is levelled towards: the land's average over it, kept above any river's water there.</summary>
		private static double Level(Place place)
		{
			double sum = WorldGen.Land(place.X, place.Z);
			int n = 1;
			for (int ring = 1; ring <= 2; ring++)
			{
				double r = place.Radius * ring / 2.0;
				for (int k = 0; k < 8; k++)
				{
					double a = k * DetMath.Pi / 4.0;
					sum += WorldGen.Land(place.X + DetMath.Cos(a) * r, place.Z + DetMath.Sin(a) * r);
					n++;
				}
			}
			return Math.Max(sum / n, WorldGen.SeaLevel + 4.0);
		}

		private static readonly ulong SaltEdge = Noise.Mix(WorldGen.Seed + 501);

		private const double IndexCell = 1000.0;
		private const int IndexN = 34;
		private static List<Place>[] index;

		/// <summary>The places whose softened area may reach (x, z), from a 1000-unit grid built once all places exist.</summary>
		private static List<Place> Near(double x, double z)
		{
			if (index == null)
			{
				return null;
			}
			int i = (int)Math.Floor((x + IndexN * IndexCell / 2.0) / IndexCell);
			int j = (int)Math.Floor((z + IndexN * IndexCell / 2.0) / IndexCell);
			return i < 0 || j < 0 || i >= IndexN || j >= IndexN ? null : index[j * IndexN + i];
		}

		private static void Index()
		{
			List<Place>[] cells = new List<Place>[IndexN * IndexN];
			foreach (Place p in All)
			{
				double reach = p.Radius + (p.Kind == PlaceKind.Hamlet ? 250.0 : p.Radius);
				int i0 = Math.Max(0, (int)Math.Floor((p.X - reach + IndexN * IndexCell / 2.0) / IndexCell));
				int i1 = Math.Min(IndexN - 1, (int)Math.Floor((p.X + reach + IndexN * IndexCell / 2.0) / IndexCell));
				int j0 = Math.Max(0, (int)Math.Floor((p.Z - reach + IndexN * IndexCell / 2.0) / IndexCell));
				int j1 = Math.Min(IndexN - 1, (int)Math.Floor((p.Z + reach + IndexN * IndexCell / 2.0) / IndexCell));
				for (int j = j0; j <= j1; j++)
				{
					for (int i = i0; i <= i1; i++)
					{
						(cells[j * IndexN + i] ??= new List<Place>()).Add(p);
					}
				}
			}
			index = cells;
			List<Place>[] fields = new List<Place>[IndexN * IndexN];
			foreach (Place p in All)
			{
				double reach = p.Radius + FieldReach(p);
				int i0 = Math.Max(0, (int)Math.Floor((p.X - reach + IndexN * IndexCell / 2.0) / IndexCell));
				int i1 = Math.Min(IndexN - 1, (int)Math.Floor((p.X + reach + IndexN * IndexCell / 2.0) / IndexCell));
				int j0 = Math.Max(0, (int)Math.Floor((p.Z - reach + IndexN * IndexCell / 2.0) / IndexCell));
				int j1 = Math.Min(IndexN - 1, (int)Math.Floor((p.Z + reach + IndexN * IndexCell / 2.0) / IndexCell));
				for (int j = j0; j <= j1; j++)
				{
					for (int i = i0; i <= i1; i++)
					{
						(fields[j * IndexN + i] ??= new List<Place>()).Add(p);
					}
				}
			}
			fieldIndex = fields;
		}

		/// <summary>How far round a place its fields reach (towns and villages farm a wide ring; a farm its own few fields).</summary>
		private static double FieldReach(Place p)
		{
			return p.Kind == PlaceKind.Town ? 2600.0 : p.Kind == PlaceKind.Village ? 1900.0 : 900.0;
		}

		private static List<Place>[] fieldIndex;

		/// <summary>How much (x, z) is farmed, 0 to 1: highest at the edge of a place, falling off over its field reach.</summary>
		internal static double Farmland(double x, double z)
		{
			if (fieldIndex == null)
			{
				return 0.0;
			}
			int i = (int)Math.Floor((x + IndexN * IndexCell / 2.0) / IndexCell);
			int j = (int)Math.Floor((z + IndexN * IndexCell / 2.0) / IndexCell);
			if (i < 0 || j < 0 || i >= IndexN || j >= IndexN || fieldIndex[j * IndexN + i] == null)
			{
				return 0.0;
			}
			double most = 0.0;
			foreach (Place p in fieldIndex[j * IndexN + i])
			{
				double dx = x - p.X, dz = z - p.Z;
				// The fields' edge wanders a lot (woods and rough ground come in between), so farmland is not a ring.
				double d = Math.Sqrt(dx * dx + dz * dz) * (1.0 + Noise.Fbm(SaltEdge, x, z, 900.0, 2, 0.5) * 0.55);
				double reach = FieldReach(p);
				if (d < p.Radius + reach)
				{
					most = Math.Max(most, 1.0 - Math.Max(0.0, d - p.Radius) / reach);
				}
			}
			return most;
		}

		/// <summary>The place whose own area (x, z) is in, if any (the nearest by its radius).</summary>
		internal static Place At(double x, double z)
		{
			List<Place> near = Near(x, z);
			if (near == null)
			{
				return null;
			}
			Place found = null;
			double best = 1.0;
			foreach (Place p in near)
			{
				double dx = x - p.X, dz = z - p.Z;
				double f = Math.Sqrt(dx * dx + dz * dz) / p.Radius;
				if (f < best)
				{
					best = f;
					found = p;
				}
			}
			return found;
		}

		/// <summary>
		/// The land softened under the places: inside a place the land is pulled towards its level, keeping <see cref="Place.Relief"/> of
		/// its own shape, and blends back out over a wide margin.
		/// </summary>
		internal static double Soften(double x, double z, double land)
		{
			double h = land;
			List<Place> near = Near(x, z);
			if (near == null)
			{
				return h;
			}
			foreach (Place p in near)
			{
				double blend = p.Kind == PlaceKind.Hamlet ? 250.0 : p.Radius;
				double reach = p.Radius + blend;
				double dx = x - p.X, dz = z - p.Z;
				double d2 = dx * dx + dz * dz;
				if (d2 >= reach * reach)
				{
					continue;
				}
				// The edge wanders (by up to a third of the radius), so a place does not sit in a circle.
				double wobble = 1.0 + Noise.Gradient(SaltEdge, x, z, 700.0) * 0.33;
				double t = Noise.Step(p.Radius * 0.7 * wobble, reach * wobble, Math.Sqrt(d2));
				double inside = p.Y + (h - p.Y) * p.Relief;
				h = inside + (h - inside) * t;
			}
			return h;
		}
	}
}
