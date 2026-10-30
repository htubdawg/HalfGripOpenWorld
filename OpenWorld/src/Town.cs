using System;
using System.Collections.Generic;

namespace HalfGripOpenWorld
{
	internal enum LotKind
	{
		/// <summary>The main town's shopping centre: a big low building behind a large car park.</summary>
		Retail = 0,
		/// <summary>A shop with a row of parking bays in front.</summary>
		Shop = 1,
		/// <summary>A metal-clad workshop behind a concrete yard.</summary>
		Workshop = 2,
		/// <summary>A filling station: a forecourt under a canopy, a kiosk behind.</summary>
		Garage = 3,
	}

	/// <summary>
	/// The town fabric that needs more room than a house plot: a retail park at the main town's edge and small businesses along the
	/// main roads at the edges of towns and villages, each a lot with a flat drivable surface (car park, yard or forecourt) between the
	/// road and its building. Laid out once with the roads, in a fixed order, so every machine gets the same.
	///
	/// A lot's surface is a mesh (see <see cref="Buildings"/>), not paint on the ground: the ground texture's pixels are 2 units, far too
	/// coarse for bay lines. The ground under a lot is levelled just under that surface (<see cref="Ground"/>) and blends back to the land
	/// round it, the way a road's bed does, so the surface lies flat on it and meets the road's verge.
	/// </summary>
	internal static class Town
	{
		internal sealed class Lot
		{
			public LotKind Kind;
			public int Road;
			/// <summary>World x, z of the lot's middle.</summary>
			public double X;
			public double Z;
			/// <summary>Along the road (unit); away from the road is (-Dz, Dx).</summary>
			public double Dx;
			public double Dz;
			/// <summary>Width along the road and depth away from it.</summary>
			public double W;
			public double D;
			/// <summary>Height of the lot's surface at its middle, from sea level (add <see cref="Network.Lift"/> for the scene).</summary>
			public double Y;
			/// <summary>The surface's rise per unit along the road: a lot is level across, but follows its road's grade along it.</summary>
			public double Grade;
			public ulong Hash;
			public string Place;
			/// <summary>
			/// Where the foot of the road's verge runs in front of the lot, as v (0 at the lot's front, less in front of it), every
			/// <see cref="FrontStep"/> along from the left edge of its pad: the lot's surface reaches forward to it.
			/// </summary>
			public double[] Foot;
		}

		/// <summary>Spacing along a lot's front of its <see cref="Lot.Foot"/> samples.</summary>
		internal const double FrontStep = 4.0;

		internal static readonly List<Lot> Lots = new List<Lot>();

		/// <summary>How far the levelled ground reaches past a lot's sides and back (the front meets the foot of the road's verge).</summary>
		internal const double Pad = 6.0;

		/// <summary>How far under a lot's surface its ground is levelled.</summary>
		internal const double Bed = 0.12;

		/// <summary>Spacing of the spots looked at along a main road.</summary>
		private const double Spacing = 40.0;

		private static readonly ulong Salt = Noise.Mix(WorldGen.Seed + 701);

		/// <summary>Width, depth and how far the ground under it may stray from the road's height, by kind.</summary>
		internal static void Size(LotKind kind, out double w, out double d, out double rough)
		{
			switch (kind)
			{
				case LotKind.Retail: w = 220.0; d = 200.0; rough = 9.0; return;
				case LotKind.Shop: w = 48.0; d = 50.0; rough = 5.0; return;
				case LotKind.Workshop: w = 56.0; d = 62.0; rough = 5.0; return;
				default: w = 64.0; d = 64.0; rough = 5.0; return;
			}
		}

		/// <summary>
		/// Where a lot's building stands, in the lot's frame: <paramref name="w"/> wide, centred along the road, from <paramref name="v0"/>
		/// back from the lot's front, <paramref name="d"/> deep. In front of it is the car park, yard or forecourt.
		/// </summary>
		internal static void Building(LotKind kind, out double w, out double v0, out double d)
		{
			switch (kind)
			{
				case LotKind.Retail: w = 170.0; v0 = 128.0; d = 66.0; return;
				case LotKind.Shop: w = 34.0; v0 = 26.0; d = 22.0; return;
				case LotKind.Workshop: w = 42.0; v0 = 26.0; d = 32.0; return;
				default: w = 26.0; v0 = 44.0; d = 16.0; return;
			}
		}

		/// <summary>Whether lot-frame point (u, v) is under the lot's building.</summary>
		internal static bool InBuilding(Lot lot, double u, double v)
		{
			Building(lot.Kind, out double w, out double v0, out double d);
			return Math.Abs(u) <= w * 0.5 && v >= v0 && v <= v0 + d;
		}

		/// <summary>Lays out the lots. Runs inside <see cref="Network"/>'s build, once the roads and their index are in.</summary>
		internal static void Build()
		{
			Lots.Clear();
			index = null;
			List<Lot> found = new List<Lot>();
			Dictionary<Places.Place, int> count = new Dictionary<Places.Place, int>();
			// The retail park first: the main town's, on the first spot along a main road at its edge where it fits.
			Places.Place main = Places.All.Count > 0 && Places.All[0].Kind == PlaceKind.Town ? Places.All[0] : null;
			if (main != null)
			{
				Walk(found, (r, k, side, place, from) =>
				{
					if (place != main || from < 0.6 || from > 1.05 || found.Count > 0)
					{
						return null;
					}
					return LotKind.Retail;
				});
			}
			Walk(found, (r, k, side, place, from) =>
			{
				bool town = place.Kind == PlaceKind.Town;
				if (from < (town ? 0.55 : 0.35) || from > (town ? 0.98 : 0.95))
				{
					return null;
				}
				count.TryGetValue(place, out int n);
				if (n >= (town ? 8 : 2))
				{
					return null;
				}
				ulong h = Hash(r, k, side);
				if (Unit(h, 0) >= (town ? 0.5 : 0.45))
				{
					return null;
				}
				double pick = Unit(h, 1);
				return pick < 0.45 ? LotKind.Shop : pick < 0.75 ? LotKind.Workshop : LotKind.Garage;
			}, lot =>
			{
				Places.Place p = Places.At(lot.X, lot.Z) ?? main;
				count.TryGetValue(p, out int n);
				count[p] = n + 1;
			});
			Lots.AddRange(found);
			Index();
		}

		/// <summary>
		/// Goes along every main road through a town or village, a spot every <see cref="Spacing"/> on each side, asking
		/// <paramref name="want"/> what (if anything) to put there, and keeps each lot that fits.
		/// </summary>
		private static void Walk(List<Lot> found, Func<int, long, int, Places.Place, double, LotKind?> want, Action<Lot> taken = null)
		{
			for (int r = 0; r < Network.Roads.Count; r++)
			{
				Network.Road road = Network.Roads[r];
				if (road.Kind != RoadKind.Primary && road.Kind != RoadKind.Secondary)
				{
					continue;
				}
				double length = road.S[road.Count - 1];
				for (long k = 1; k * Spacing < length - Spacing; k++)
				{
					Network.Locate(road, k * Spacing, out int i, out double t);
					if (road.Bridge[i] || road.Bridge[i + 1])
					{
						continue;
					}
					double px = road.X[i] + (road.X[i + 1] - road.X[i]) * t, pz = road.Z[i] + (road.Z[i + 1] - road.Z[i]) * t;
					Places.Place place = Places.At(px, pz);
					if (place == null || place.Kind == PlaceKind.Hamlet)
					{
						continue;
					}
					// Not by the start grid, where the cars stand.
					double sx = px - Network.SpawnX, sz = pz - Network.SpawnZ;
					if (sx * sx + sz * sz < 300.0 * 300.0)
					{
						continue;
					}
					double from = Math.Sqrt((px - place.X) * (px - place.X) + (pz - place.Z) * (pz - place.Z)) / place.Radius;
					double len = Math.Max(1e-6, road.S[i + 1] - road.S[i]);
					double dx = (road.X[i + 1] - road.X[i]) / len, dz = (road.Z[i + 1] - road.Z[i]) / len;
					double py = road.Y[i] + (road.Y[i + 1] - road.Y[i]) * t;
					for (int side = -1; side <= 1; side += 2)
					{
						LotKind? kind = want(r, k, side, place, from);
						if (kind == null)
						{
							continue;
						}
						Lot lot = Fit(kind.Value, r, road, k * Spacing, side, px, pz, py, dx * side, dz * side, Hash(r, k, side), found);
						if (lot != null)
						{
							lot.Place = place.Name;
							found.Add(lot);
							taken?.Invoke(lot);
						}
					}
				}
			}
		}

		/// <summary>
		/// A lot of the given kind beside road <paramref name="r"/> at (px, pz), its front at the outer edge of the road's verge, if it fits:
		/// clear of every other road (each road keeps its own side of a block, as with houses), out of the water, on ground near the road's
		/// height, and clear of the lots already laid out.
		/// </summary>
		private static Lot Fit(LotKind kind, int r, Network.Road road, double s, int side, double px, double pz, double py, double ax, double az, ulong h, List<Lot> found)
		{
			Size(kind, out double w, out double d, out double rough);
			// Away from the road (left of the way the lot runs along it).
			double nx = -az, nz = ax;
			if (s - w < 0.0 || s + w > road.S[road.Count - 1])
			{
				return null;
			}
			// Where the road's line runs past the lot's front (and its pad), as offsets towards the lot from (px, pz): the lot stands back
			// behind the outermost, its front a straight line, and the strip between it and the verge's foot elsewhere is part of the lot.
			int count = (int)Math.Ceiling((w + 2.0 * Pad) / FrontStep) + 1;
			double[] line = new double[count];
			Network.Locate(road, s - w, out int first, out double _);
			Network.Locate(road, s + w, out int last, out double _);
			double most = double.MinValue, least = double.MaxValue;
			for (int k = 0; k < count; k++)
			{
				double u = -w * 0.5 - Pad + k * FrontStep;
				bool hit = false;
				for (int i = first; i <= last && !hit; i++)
				{
					double ua = (road.X[i] - px) * ax + (road.Z[i] - pz) * az, ub = (road.X[i + 1] - px) * ax + (road.Z[i + 1] - pz) * az;
					if ((ua - u) * (ub - u) <= 0.0 && ua != ub)
					{
						double t = (u - ua) / (ub - ua);
						line[k] = (road.X[i] + (road.X[i + 1] - road.X[i]) * t - px) * nx + (road.Z[i] + (road.Z[i + 1] - road.Z[i]) * t - pz) * nz;
						hit = true;
					}
				}
				if (!hit)
				{
					return null;
				}
				most = Math.Max(most, line[k]);
				least = Math.Min(least, line[k]);
			}
			// A gentle bend only: the strip in front is at most 6 deep.
			if (most - least > 6.0)
			{
				return null;
			}
			double front = Network.Verge;
			double off = most + road.Half + front + d * 0.5;
			double[] foot = new double[count];
			for (int k = 0; k < count; k++)
			{
				// The verge's foot, in the lot's frame (0 at its front, less in front of it).
				foot[k] = line[k] - most;
			}
			// Its surface a little over the road's shoulder (the road's bed, which the verge comes down to), so it meets the verge's foot.
			double y = py - Network.BedDepth + 0.1;
			Lot lot = new Lot { Kind = kind, Road = r, X = px + nx * off, Z = pz + nz * off, Dx = ax, Dz = az, W = w, D = d, Y = y, Hash = h, Foot = foot };
			// The surface follows the road's grade along its front, so the front stays at the verge's foot all along: the road must run
			// about straight in height past it (not over a crest or through a dip), and not steeply.
			// u runs along the road on its right side (side 1), against it on its left.
			lot.Grade = (RoadY(road, s + side * w * 0.5) - RoadY(road, s - side * w * 0.5)) / w;
			if (Math.Abs(lot.Grade) > 0.07)
			{
				return null;
			}
			for (int k = 0; k <= 8; k++)
			{
				double u = w * (k / 8.0 - 0.5);
				if (Math.Abs(RoadY(road, s + side * u) - py - lot.Grade * u) > 0.3)
				{
					return null;
				}
			}
			double reach = Reach(lot);
			foreach (Lot other in found)
			{
				double ox = other.X - lot.X, oz = other.Z - lot.Z, need = reach + Reach(other) + 30.0;
				if (ox * ox + oz * oz < need * need)
				{
					return null;
				}
			}
			int nu = (int)Math.Ceiling((w + 2.0 * Pad) / 8.0), nv = (int)Math.Ceiling((d + Pad) / 8.0);
			for (int b = 0; b <= nv; b++)
			{
				double v = (d + Pad) * b / nv;
				for (int a = 0; a <= nu; a++)
				{
					double u = -w * 0.5 - Pad + (w + 2.0 * Pad) * a / nu;
					double x = lot.X + ax * u + nx * (v - d * 0.5), z = lot.Z + az * u + nz * (v - d * 0.5);
					bool inside = v <= d && Math.Abs(u) <= w * 0.5;
					// Its own road's edge is front + v away; every other road's must be further, and 4 clear of the lot.
					if (!Network.Claims(x, z, r, front + v, inside ? 4.0 : 0.5))
					{
						return null;
					}
					double g = Network.Ground(x, z, WorldGen.Base(x, z));
					if (Rivers.Water(x, z, out double level) && g < level + 2.0)
					{
						return null;
					}
					if (Math.Abs(g - y - lot.Grade * u) > rough)
					{
						return null;
					}
				}
			}
			return lot;
		}

		private static double RoadY(Network.Road road, double s)
		{
			Network.Locate(road, s, out int i, out double t);
			return road.Y[i] + (road.Y[i + 1] - road.Y[i]) * t;
		}

		/// <summary>Where the foot of the road's verge is in front of a lot at u along it, as v (0 or less).</summary>
		internal static double Foot(Lot lot, double u)
		{
			double f = (u + lot.W * 0.5 + Pad) / FrontStep;
			int k = Math.Max(0, Math.Min(lot.Foot.Length - 2, (int)Math.Floor(f)));
			double t = Math.Max(0.0, Math.Min(1.0, f - k));
			return lot.Foot[k] + (lot.Foot[k + 1] - lot.Foot[k]) * t;
		}

		/// <summary>Height of a lot's surface (from sea level) at u along it.</summary>
		internal static double Surface(Lot lot, double u)
		{
			return lot.Y + lot.Grade * Math.Max(-lot.W * 0.5 - Pad, Math.Min(lot.W * 0.5 + Pad, u));
		}

		/// <summary>The radius of a circle round a lot and its levelled ground.</summary>
		internal static double Reach(Lot lot)
		{
			return Math.Sqrt(lot.W * lot.W + lot.D * lot.D) * 0.5 + Pad;
		}

		/// <summary>(x, z) in a lot's frame: along the road from its middle, and away from the road from its front.</summary>
		internal static void Local(Lot lot, double x, double z, out double u, out double v)
		{
			double rx = x - lot.X, rz = z - lot.Z;
			u = rx * lot.Dx + rz * lot.Dz;
			v = rx * -lot.Dz + rz * lot.Dx + lot.D * 0.5;
		}

		/// <summary>World x, z of the point u along and v back from the front of a lot.</summary>
		internal static void World(Lot lot, double u, double v, out double x, out double z)
		{
			x = lot.X + lot.Dx * u - lot.Dz * (v - lot.D * 0.5);
			z = lot.Z + lot.Dz * u + lot.Dx * (v - lot.D * 0.5);
		}

		/// <summary>The lot whose surface (x, z) is on, if any, and whether it is under the lot's building.</summary>
		internal static Lot At(double x, double z, out bool building)
		{
			building = false;
			List<Lot> near = Near(x, z);
			if (near == null)
			{
				return null;
			}
			foreach (Lot lot in near)
			{
				Local(lot, x, z, out double u, out double v);
				if (Math.Abs(u) <= lot.W * 0.5 && v >= Foot(lot, u) && v <= lot.D)
				{
					building = InBuilding(lot, u, v);
					return lot;
				}
			}
			return null;
		}

		/// <summary>
		/// Whether (x, z) is on a lot or within <paramref name="margin"/> of one (its levelled pad counts): nothing else is built there and
		/// no trees or grass grow there.
		/// </summary>
		internal static bool Taken(double x, double z, double margin)
		{
			List<Lot> near = Near(x, z);
			if (near == null)
			{
				return false;
			}
			foreach (Lot lot in near)
			{
				Local(lot, x, z, out double u, out double v);
				if (Math.Abs(u) <= lot.W * 0.5 + Pad + margin && v >= Foot(lot, u) - margin && v <= lot.D + Pad + margin)
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>
		/// The ground at (x, z) given the ground from the roads (<paramref name="ground"/>, from sea level): levelled just under a lot's
		/// surface over the lot and its pad, blending back over a bank about 1 in 1.6 round its sides and back, and over the verge to the
		/// road in front.
		/// </summary>
		internal static double Ground(double x, double z, double ground)
		{
			List<Lot> near = Near(x, z);
			if (near == null)
			{
				return ground;
			}
			foreach (Lot lot in near)
			{
				Local(lot, x, z, out double u, out double v);
				double du = Math.Max(0.0, Math.Abs(u) - lot.W * 0.5 - Pad);
				double y = Surface(lot, u) - Bed;
				if (v < 0.0)
				{
					// In front, out to the road's edge: the lot's surface reaches forward to the verge's foot, and the verge comes down
					// from the road's edge to the ground there, so this ground is never seen; levelled, the ground's triangles along the
					// lot's front are all at its level. The lot is under the road's surface, so the verge still falls to it. The ground
					// under the paving stays the road's, and the levelled ground never rises more than 0.4 over the road's bed (0.5
					// under its surface), so it stays under the road's edge where a mouth tilts it.
					if (du > 0.0 || v <= Foot(lot, u) - Network.Verge)
					{
						continue;
					}
					return Math.Min(y, ground + 0.4);
				}
				double dv = Math.Max(0.0, v - lot.D - Pad);
				if (du == 0.0 && dv == 0.0)
				{
					return y;
				}
				double e = Math.Sqrt(du * du + dv * dv);
				double bank = Math.Max(10.0, Math.Abs(ground - y) * 1.6 + 6.0);
				if (e < bank)
				{
					return y + (ground - y) * Noise.Step(0.0, bank, e);
				}
			}
			return ground;
		}

		// ---- index -------------------------------------------------------------------------------------------------

		private const double IndexCell = 500.0;
		private const int IndexN = 68;
		private static List<Lot>[] index;

		/// <summary>The lots whose levelled ground and its bank may reach (x, z).</summary>
		private static List<Lot> Near(double x, double z)
		{
			List<Lot>[] cells = index;
			if (cells == null)
			{
				return null;
			}
			int i = (int)Math.Floor((x + IndexN * IndexCell / 2.0) / IndexCell);
			int j = (int)Math.Floor((z + IndexN * IndexCell / 2.0) / IndexCell);
			return i < 0 || j < 0 || i >= IndexN || j >= IndexN ? null : cells[j * IndexN + i];
		}

		private static void Index()
		{
			List<Lot>[] cells = new List<Lot>[IndexN * IndexN];
			foreach (Lot lot in Lots)
			{
				// The bank is at most about 1.6 times the rough allowance plus 6.
				double reach = Reach(lot) + 30.0;
				int i0 = Math.Max(0, (int)Math.Floor((lot.X - reach + IndexN * IndexCell / 2.0) / IndexCell));
				int i1 = Math.Min(IndexN - 1, (int)Math.Floor((lot.X + reach + IndexN * IndexCell / 2.0) / IndexCell));
				int j0 = Math.Max(0, (int)Math.Floor((lot.Z - reach + IndexN * IndexCell / 2.0) / IndexCell));
				int j1 = Math.Min(IndexN - 1, (int)Math.Floor((lot.Z + reach + IndexN * IndexCell / 2.0) / IndexCell));
				for (int j = j0; j <= j1; j++)
				{
					for (int i = i0; i <= i1; i++)
					{
						(cells[j * IndexN + i] ??= new List<Lot>()).Add(lot);
					}
				}
			}
			index = cells;
		}

		private static ulong Hash(int r, long k, int side)
		{
			return Noise.Mix(Salt ^ (ulong)r * 0x9E3779B97F4A7C15UL ^ (ulong)k * 0xBF58476D1CE4E5B9UL ^ (ulong)(side + 2));
		}

		/// <summary>A number from 0 to 1 from hash <paramref name="h"/>, the <paramref name="k"/>th of several.</summary>
		internal static double Unit(ulong h, int k)
		{
			return (Noise.Mix(h + (ulong)k) >> 11) * (1.0 / 9007199254740992.0);
		}
	}
}
