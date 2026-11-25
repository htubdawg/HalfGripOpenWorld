using System;
using System.Collections.Generic;

namespace HalfGripOpenWorld
{
	/// <summary>
	/// The house plots along the towns' and villages' roads, and whether a footprint fits on the ground: the decisions behind the houses
	/// <see cref="Buildings"/> draws, kept here without Unity so the traffic (<see cref="Traffic"/>) knows every household from the same
	/// arithmetic, on a machine that has never loaded the house's tile. Change a plot here and both follow.
	/// </summary>
	internal static class Plots
	{
		/// <summary>The houses' hash salt (the same as <see cref="Buildings"/>' own, so the drawn houses did not change when this moved here).</summary>
		internal static readonly ulong Salt = Noise.Mix(WorldGen.Seed + 601);

		/// <summary>The main town's centre (as a share of its radius) where the plots carry towers.</summary>
		internal const double TowerReach = 0.16;

		/// <summary>One plot by a road: a tower, a terrace house, a high-street shop with rooms over it, or a house in its garden.</summary>
		internal struct Plot
		{
			public int Road;
			/// <summary>The plot's number along the road, and its side (-1 right, 1 left).</summary>
			public long K;
			public int Side;
			public ulong Hash;
			public Places.Place Place;
			/// <summary>How far out from the place's middle, as a share of its radius.</summary>
			public double From;
			public bool Town, Village, Core, Towers, High, Terrace;
			/// <summary>The point on the road's line in front of the plot, the road's way there, and the way away from the road on this side.</summary>
			public double Px, Pz, Dx, Dz, Nx, Nz;
			public double Spacing;
			/// <summary>The house: its middle, width along the road, depth, storeys, how far it stands back from the road's edge; thatched or not.</summary>
			public double Cx, Cz, Width, Depth, Setback;
			public int Floors;
			public bool Thatched;
		}

		/// <summary>A number from 0 to 1 from hash <paramref name="h"/>, the <paramref name="k"/>th of several (as <see cref="Buildings"/>' own).</summary>
		internal static double Unit(ulong h, int k)
		{
			return (Noise.Mix(h + (ulong)k) >> 11) * (1.0 / 9007199254740992.0);
		}

		/// <summary>
		/// The plots along one piece of a road (between samples i and i + 1), both sides, into <paramref name="into"/> (not cleared): those
		/// the hash keeps. Whether a house then fits is <see cref="Fits"/> (a tower's footprint is its own, see <see cref="Buildings"/>).
		/// No plots by the trunk road or an unpaved one, nor outside a town or village.
		/// </summary>
		internal static void Along(int r, Network.Road road, int i, List<Plot> into)
		{
			if (road.Kind == RoadKind.Trunk || Network.Unpaved(road.Kind))
			{
				return;
			}
			double mx = (road.X[i] + road.X[i + 1]) * 0.5, mz = (road.Z[i] + road.Z[i + 1]) * 0.5;
			Places.Place place = Places.At(mx, mz);
			if (place == null || place.Kind == PlaceKind.Hamlet)
			{
				return;
			}
			bool town = place.Kind == PlaceKind.Town;
			double from = Math.Sqrt((mx - place.X) * (mx - place.X) + (mz - place.Z) * (mz - place.Z)) / place.Radius;
			bool core = town && from < 0.35;
			// The main town's centre has its towers; the main roads through a town's core or a village's middle are its high street.
			bool towers = town && place == Places.All[0] && from < TowerReach;
			bool main = road.Kind == RoadKind.Primary || road.Kind == RoadKind.Secondary;
			bool high = main && !towers && (core || (place.Kind == PlaceKind.Village && from < 0.3));
			bool terrace = core || high;
			bool village = place.Kind == PlaceKind.Village;
			double spacing = towers ? 52.0 : terrace ? 26.0 : town ? 36.0 : 44.0;
			// A seafront has houses on its landward side only: the other looks over the beach.
			int seaSide = 0;
			if (road.Name.EndsWith(" seafront"))
			{
				double len0 = Math.Max(1e-6, road.S[i + 1] - road.S[i]);
				double ex = -(road.Z[i + 1] - road.Z[i]) / len0, ez = (road.X[i + 1] - road.X[i]) / len0;
				seaSide = WorldGen.Base(mx + ex * 60.0, mz + ez * 60.0) < WorldGen.Base(mx - ex * 60.0, mz - ez * 60.0) ? 1 : -1;
			}
			double s0 = road.S[i], s1 = road.S[i + 1];
			for (long k = (long)Math.Ceiling(s0 / spacing); k * spacing < s1; k++)
			{
				double t = (k * spacing - s0) / Math.Max(1e-6, s1 - s0);
				double px = road.X[i] + (road.X[i + 1] - road.X[i]) * t;
				double pz = road.Z[i] + (road.Z[i + 1] - road.Z[i]) * t;
				double len = Math.Max(1e-6, s1 - s0);
				double dx = (road.X[i + 1] - road.X[i]) / len, dz = (road.Z[i + 1] - road.Z[i]) / len;
				for (int side = -1; side <= 1; side += 2)
				{
					if (side == seaSide)
					{
						continue;
					}
					ulong h = Noise.Mix(Salt ^ (ulong)r * 0x9E3779B97F4A7C15UL ^ (ulong)k * 0xBF58476D1CE4E5B9UL ^ (ulong)(side + 2));
					double chance = towers ? 0.9 : terrace ? 0.95 : town ? 0.35 + 0.5 * (1.0 - from) : 0.7 * (1.0 - from * 0.6);
					if (Unit(h, 0) >= chance)
					{
						continue;
					}
					// Left of the road is (-dz, dx).
					Plot p = new Plot
					{
						Road = r, K = k, Side = side, Hash = h, Place = place, From = from, Town = town, Village = village, Core = core, Towers = towers,
						High = high, Terrace = terrace, Px = px, Pz = pz, Dx = dx, Dz = dz, Nx = -dz * side, Nz = dx * side, Spacing = spacing,
					};
					if (!towers)
					{
						p.Width = terrace ? spacing - 0.5 : spacing * (0.55 + 0.25 * Unit(h, 1));
						p.Depth = terrace ? 26.0 + 6.0 * Unit(h, 2) : 20.0 + 8.0 * Unit(h, 2);
						p.Floors = core ? 2 + (int)(Unit(h, 3) * (from < 0.2 ? 4.0 : 3.0)) : 1 + (int)(Unit(h, 3) * 2.0);
						if (high)
						{
							p.Floors = Math.Max(2, p.Floors);
						}
						// A cottage now and then in a village: one storey under a deep thatch.
						p.Thatched = village && !terrace && Unit(h, 10) < 0.18;
						if (p.Thatched)
						{
							p.Floors = 1;
							p.Depth = Math.Min(p.Depth, 22.0);
						}
						// A terrace stands at the back of the sidewalk.
						p.Setback = terrace ? Network.Verge + 0.5 : 6.0 + 8.0 * Unit(h, 4);
						// (Back from the paving's edge where the road is widened for a left-turn bay too.)
						double off = Network.Widened(road, k * spacing) + p.Setback + p.Depth * 0.5;
						p.Cx = px + p.Nx * off;
						p.Cz = pz + p.Nz * off;
					}
					into.Add(p);
				}
			}
		}

		/// <summary>Whether a plot's house (not a tower) fits on its plot: what <see cref="Buildings"/> tests before drawing it.</summary>
		internal static bool Fits(Plot p)
		{
			return !p.Towers && Fits(p.Road, p.Setback, p.Cx, p.Cz, p.Dx * p.Side, p.Dz * p.Side, p.Width, p.Depth, 1.5, out _, out _);
		}

		/// <summary>Where a footprint is tested, as shares of its width and depth: corners, middle, and the middles of its sides (a lot's corner can poke into a long building's side).</summary>
		private static readonly double[] Us = { -0.5, 0.5, -0.5, 0.5, 0.0, -0.5, 0.5, 0.0, 0.0 };
		private static readonly double[] Vs = { -0.5, -0.5, 0.5, 0.5, 0.0, 0.0, 0.0, -0.5, 0.5 };

		/// <summary>
		/// Whether a footprint <paramref name="w"/> along (dx, dz) by <paramref name="d"/> across fits, keeping <paramref name="margin"/>
		/// off every road; the lowest and highest ground (scene heights) under its corners and middle.
		/// The corners and middles are tested first (cheap, and most misfits fail there), then the whole footprint on a grid at most
		/// <see cref="Step"/> apart: a barn 50 wide tested only at its corners and middles once stood across a lane that ran between them.
		/// Off its own road a point keeps clear of the sidewalk at least (the setback less a hair, up to a verge's width and a little), so a terrace on
		/// the outside of a bend does not put its front corners on the sidewalk.
		/// </summary>
		internal static bool Fits(int own, double front, double cx, double cz, double dx, double dz, double w, double d, double margin, out double lo, out double hi)
		{
			// Across the footprint, away from its road.
			double nx = -dz, nz = dx;
			lo = double.MaxValue;
			hi = double.MinValue;
			for (int c = 0; c < Us.Length; c++)
			{
				double u = Us[c] * w, v = Vs[c] * d;
				double x = cx + dx * u + nx * v, z = cz + dz * u + nz * v;
				if (!Clear(own, front, d, v, x, z, margin))
				{
					return false;
				}
				if (c == 4 && Signs.Near(cx, cz, Math.Sqrt(w * w + d * d) * 0.5 + 1.0))
				{
					return false;
				}
				if (Rivers.Water(x, z, out double level) && WorldGen.Height(x, z) - Network.Lift < level + 1.0)
				{
					return false;
				}
				double gh = WorldGen.Height(x, z);
				lo = Math.Min(lo, gh);
				hi = Math.Max(hi, gh);
			}
			if (hi - lo > 6.0)
			{
				return false;
			}
			int nu = (int)Math.Ceiling(w / Step), nv = (int)Math.Ceiling(d / Step);
			for (int a = 0; a <= nu; a++)
			{
				for (int k = 0; k <= nv; k++)
				{
					double u = (a / (double)nu - 0.5) * w, v = (k / (double)nv - 0.5) * d;
					if (!Clear(own, front, d, v, cx + dx * u + nx * v, cz + dz * u + nz * v, margin))
					{
						return false;
					}
				}
			}
			return true;
		}

		/// <summary>The most a footprint's test points are apart: under the narrowest path's width and two margins, so none slips between them.</summary>
		private const double Step = 3.0;

		/// <summary>One point of a footprint <paramref name="v"/> across from its middle (depth <paramref name="d"/>) against the roads, the lots and the parks (see <see cref="Fits"/>).</summary>
		private static bool Clear(int own, double front, double d, double v, double x, double z, double margin)
		{
			// How far this point is from its own road's edge: the front the setback, the back the setback and depth.
			double ownEdge = front + v + d * 0.5;
			if (!Network.Claims(x, z, own, ownEdge, margin))
			{
				return false;
			}
			if (own >= 0 && front > 0.0 && !Network.Claims(x, z, -1, 0.0, Math.Min(front, Network.Verge + 0.5) - 0.3, own))
			{
				return false;
			}
			return !Town.Taken(x, z, 2.0) && !Parks.At(x, z);
		}
	}
}
