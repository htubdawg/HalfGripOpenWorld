using System;
using System.Collections.Generic;
using UnityEngine;

namespace HalfGripOpenWorld
{
	/// <summary>
	/// Buildings, made in code in the game's low-poly pixel style: houses along the roads of every town and village, high-street shops
	/// with flats over them on the main roads through their middles, a cluster of glass towers in the main town's centre, a church in
	/// every village, a farmhouse, a barn and a shed at every farm, and the retail park and businesses on their lots (<see cref="Town"/>)
	/// with the car parks, yards and forecourts in front of them. Worked out per tile on a worker thread, all of a tile's buildings in one
	/// mesh (a submesh per material) with one collider.
	///
	/// Houses stand on plots along each road, one every so far along it (counted from the road's start, so every plot is decided once,
	/// by the tile its road piece is in, and the same on every machine): terraced, taller and close to the road in a town's middle,
	/// detached and set back further out and in villages. A plot is only built on if the house fits: every corner nearer its own road
	/// than any other (each road gets its own side of every block, so houses on different streets never overlap), off every road and
	/// lot, out of the water, and on ground that does not fall more than 6 across it.
	/// </summary>
	internal static class Buildings
	{
		/// <summary>
		/// Materials, in submesh order: five walls, three pitched roofs, a flat roof; glass curtain wall, shopfronts, metal cladding,
		/// signs, a filling station's canopy; the lots' asphalt, parking bays and concrete; the circuit's garage doors and stand seats;
		/// the rally stage's gravel yard, straw bales and arch banners.
		/// </summary>
		internal const int Brick = 0, Render = 1, White = 2, Stone = 3, Wood = 4, RedTile = 5, Slate = 6, Metal = 7, Flat = 8;
		internal const int Glass = 9, Shopfront = 10, Cladding = 11, Sign = 12, Canopy = 13, Asphalt = 14, Bays = 15, Concrete = 16, GarageDoor = 17, Seats = 18;
		internal const int Gravel = 19, Straw = 20, Banner = 21, Count = 22;

		/// <summary>No roof (a box drawn with walls only).</summary>
		private const int None = -1;

		/// <summary>A storey's height (3 m), and a bay's width (one window).</summary>
		private const float Storey = 10f;
		private const float Bay = 8f;

		/// <summary>A parking bay's width (2.7 m) and length (4.8 m).</summary>
		private const double BayWidth = 9.0;
		private const double BayLength = 16.0;

		/// <summary>How far from the main town's middle (as a share of its radius) its towers stand.</summary>
		private const double TowerReach = 0.16;

		internal sealed class Data
		{
			internal RoadMeshData Mesh;
			/// <summary>World x, z and radius of every building, so trees and grass keep off them.</summary>
			internal List<(double x, double z, double r)> Footprints = new List<(double, double, double)>();
		}

		private static readonly ulong Salt = Noise.Mix(WorldGen.Seed + 601);

		internal static Data Build(int tx, int tz, float tileSize)
		{
			Data data = new Data();
			Builder b = new Builder(tx * (double)tileSize, tz * (double)tileSize);
			List<long> pieces = Network.PiecesIn(tx, tz, tileSize);
			if (pieces != null)
			{
				foreach (long piece in pieces)
				{
					int r = (int)(piece >> 32);
					Network.Road road = Network.Roads[r];
					if (road.Kind == RoadKind.Trunk || Network.Unpaved(road.Kind))
					{
						continue;
					}
					Plots(b, data, r, road, (int)(uint)piece);
				}
			}
			foreach (Places.Place p in Places.All)
			{
				if (Math.Floor(p.X / tileSize) != tx || Math.Floor(p.Z / tileSize) != tz)
				{
					continue;
				}
				if (p.Kind == PlaceKind.Hamlet)
				{
					Farm(b, data, p);
				}
				else if (p.Kind == PlaceKind.Village)
				{
					Church(b, data, p);
				}
			}
			foreach (Parks.Bench bench in Parks.Benches)
			{
				if (Math.Floor(bench.X / tileSize) == tx && Math.Floor(bench.Z / tileSize) == tz)
				{
					Bench(b, data, bench);
				}
			}
			// Each lot is built whole by the tile its middle is in.
			foreach (Town.Lot lot in Town.Lots)
			{
				if (Math.Floor(lot.X / tileSize) == tx && Math.Floor(lot.Z / tileSize) == tz)
				{
					Lot(b, data, lot);
				}
			}
			CircuitParts(b, data, tx, tz, tileSize);
			RallyParts(b, data, tx, tz, tileSize);
			ClimbParts(b, data, tx, tz, tileSize);
			data.Mesh = b.Done();
			return data;
		}

		// ---- the circuit -------------------------------------------------------------------------------------------

		/// <summary>
		/// The circuit's barrier, pit wall, garages, grandstands and start gantry (<see cref="Circuit"/>), each built by the tile its start
		/// or middle is in. All stand on the levelled ground, a plane tilted at most 2%: each is set into it as far as it falls across.
		/// </summary>
		private static void CircuitParts(Builder b, Data data, int tx, int tz, float tileSize)
		{
			if (!Circuit.Exists)
			{
				return;
			}
			// The armco outside the loop, a rail along each piece of the lap (none along the pits).
			for (int i = 0; i < Circuit.Count; i++)
			{
				int q = (i + 1) % Circuit.Count;
				if (Math.Floor(Circuit.X[i] / tileSize) != tx || Math.Floor(Circuit.Z[i] / tileSize) != tz || !Circuit.BarrierAt(Circuit.S[i]))
				{
					continue;
				}
				double ax = Circuit.X[i] + Circuit.Rx[i] * Circuit.Barrier, az = Circuit.Z[i] + Circuit.Rz[i] * Circuit.Barrier;
				double bx = Circuit.X[q] + Circuit.Rx[q] * Circuit.Barrier, bz = Circuit.Z[q] + Circuit.Rz[q] * Circuit.Barrier;
				double len = Math.Sqrt((bx - ax) * (bx - ax) + (bz - az) * (bz - az));
				if (len < 0.01)
				{
					continue;
				}
				CircuitSolid(b, (ax + bx) * 0.5, (az + bz) * 0.5, (bx - ax) / len, (bz - az) / len, len + 0.3, 0.5, 2.6f, Metal);
			}
			// The pit wall between the track and the pit lane, in 20-unit lengths.
			double wallV = (Circuit.PitWallIn + Circuit.PitWallOut) * 0.5;
			for (double u = Circuit.PitA; u < Circuit.PitB - 0.01; u += 20.0)
			{
				double u1 = Math.Min(Circuit.PitB, u + 20.0);
				Circuit.Straight((u + u1) * 0.5, wallV, out double x, out double z);
				if (Math.Floor(x / tileSize) == tx && Math.Floor(z / tileSize) == tz)
				{
					CircuitSolid(b, x, z, Circuit.DirX, Circuit.DirZ, u1 - u, Circuit.PitWallOut - Circuit.PitWallIn, 3.5f, Concrete);
				}
			}
			// The garages: one long building, a door for each garage on the pit lane, a glass floor of hospitality boxes over them.
			{
				double u = (Circuit.GaragesA + Circuit.GaragesB) * 0.5, v = (Circuit.PitLaneOut + Circuit.GaragesOut) * 0.5;
				Circuit.Straight(u, v, out double x, out double z);
				if (Math.Floor(x / tileSize) == tx && Math.Floor(z / tileSize) == tz)
				{
					double w = Circuit.GaragesB - Circuit.GaragesA, d = Circuit.GaragesOut - Circuit.PitLaneOut;
					Footing(x, z, -Circuit.DirX, -Circuit.DirZ, w, d, out float y, out float extra);
					// Turned to face the pit lane (a box's front faces away from its "across").
					b.Box(x, z, y, -Circuit.DirX, -Circuit.DirZ, (float)w, (float)d, 22f + extra, Cladding, Flat, GarageDoor, Storey + extra);
					// Standing out from the front wall a little (flush with it, the two faces would flicker).
					Circuit.Straight(u, Circuit.PitLaneOut - 0.2, out double gx, out double gz);
					Footing(gx, gz, -Circuit.DirX, -Circuit.DirZ, w, 1.0, out float gy, out float _);
					b.Box(gx, gz, gy + 12f, -Circuit.DirX, -Circuit.DirZ, (float)w, 1.2f, 8f, Glass, Flat, Glass, 8f);
					data.Footprints.Add((x, z, Math.Sqrt(w * w + d * d) * 0.5));
				}
			}
			// The start gantry over the start line: a post each side, a beam of lights and a sign facing the grid.
			{
				double v0 = -Circuit.Half - 4.0, v1 = Circuit.PitWallOut + 1.0;
				Circuit.Straight(Circuit.StartS, (v0 + v1) * 0.5, out double x, out double z);
				if (Math.Floor(x / tileSize) == tx && Math.Floor(z / tileSize) == tz)
				{
					foreach (double v in new[] { v0, v1 })
					{
						Circuit.Straight(Circuit.StartS, v, out double px, out double pz);
						CircuitSolid(b, px, pz, Circuit.DirX, Circuit.DirZ, 1.6, 1.6, 26f, Metal);
					}
					float y = (float)(Circuit.PlaneAt(x, z) + Network.Lift);
					// Along across the track (to its right), so its front faces back down the straight at the grid.
					b.Box(x, z, y + 20f, Circuit.DirZ, -Circuit.DirX, (float)(v1 - v0 + 2.0), 2.4f, 6f, Metal, Flat, Sign, 6f, 0);
				}
			}
			foreach (Circuit.Stand s in Circuit.Stands)
			{
				if (Math.Floor(s.X / tileSize) == tx && Math.Floor(s.Z / tileSize) == tz)
				{
					Stand(b, data, s);
				}
			}
		}

		/// <summary>
		/// A grandstand: rows of seats stepping up away from the track, a back wall, a roof over them on posts at the front. Built in
		/// its own frame: along its length (ax, az), towards the track (s.Fx, s.Fz).
		/// </summary>
		private static void Stand(Builder b, Data data, Circuit.Stand s)
		{
			const int rows = 8;
			const float rise = 3f;
			double ax = -s.Fz, az = s.Fx;
			double row = s.Depth / (rows + 1);
			Footing(s.X, s.Z, ax, az, s.Length, s.Depth, out float y, out float extra);
			for (int r = 0; r < rows; r++)
			{
				// Each row's block runs from its front edge to the back, on top of the ones in front of it.
				double from = r * row, depth = s.Depth - from;
				double cx = s.X + s.Fx * (s.Depth * 0.5 - from - depth * 0.5), cz = s.Z + s.Fz * (s.Depth * 0.5 - from - depth * 0.5);
				b.Solid(cx, cz, y, ax, az, (float)s.Length, (float)depth, extra + 2f + (r + 1) * rise, r == 0 ? Concrete : Seats);
			}
			float top = extra + 2f + rows * rise;
			double backX = s.X - s.Fx * (s.Depth * 0.5 - 0.5), backZ = s.Z - s.Fz * (s.Depth * 0.5 - 0.5);
			b.Solid(backX, backZ, y, ax, az, (float)s.Length, 1.0f, top + 10f, Cladding);
			// The roof reaches out over the front row; posts hold it at the front.
			double roofX = s.X + s.Fx * 2.0, roofZ = s.Z + s.Fz * 2.0;
			b.Solid(roofX, roofZ, y + top + 10f, ax, az, (float)s.Length + 2f, (float)s.Depth + 4f, 1.2f, Metal);
			for (double along = -s.Length * 0.5 + 2.0; along <= s.Length * 0.5 - 1.9; along += (s.Length - 4.0) / Math.Max(1.0, Math.Round(s.Length / 40.0)))
			{
				double px = s.X + ax * along + s.Fx * (s.Depth * 0.5 + 3.0), pz = s.Z + az * along + s.Fz * (s.Depth * 0.5 + 3.0);
				b.Solid(px, pz, y, ax, az, 0.8f, 0.8f, top + 10f, Metal);
			}
			data.Footprints.Add((s.X, s.Z, Math.Sqrt(s.Length * s.Length + s.Depth * s.Depth) * 0.5));
		}

		/// <summary>A box of one material standing on the circuit's levelled ground, set into it as far as the ground falls across it.</summary>
		private static void CircuitSolid(Builder b, double x, double z, double dx, double dz, double w, double d, float h, int material)
		{
			Footing(x, z, dx, dz, w, d, out float y, out float extra);
			b.Solid(x, z, y, dx, dz, (float)w, (float)d, h + extra, material);
		}

		/// <summary>
		/// Where a box w along (dx, dz) by d across, centred at (x, z), stands on the circuit's plane: its base (a scene height) at the
		/// lowest of its corners and a little under, and how much higher the highest corner is (added to its walls).
		/// </summary>
		private static void Footing(double x, double z, double dx, double dz, double w, double d, out float y, out float extra)
		{
			double low = double.MaxValue, high = double.MinValue;
			for (int a = -1; a <= 1; a += 2)
			{
				for (int c = -1; c <= 1; c += 2)
				{
					double px = x + dx * w * 0.5 * a - dz * d * 0.5 * c, pz = z + dz * w * 0.5 * a + dx * d * 0.5 * c;
					double h = Circuit.PlaneAt(px, pz);
					low = Math.Min(low, h);
					high = Math.Max(high, h);
				}
			}
			y = (float)(low + Network.Lift) - 0.3f;
			extra = (float)(high - low) + 0.3f;
		}

		// ---- the hill climb ----------------------------------------------------------------------------------------

		/// <summary>
		/// The arches over the hill climb's start and finish lines (<see cref="HillClimb"/>), as the rally stage's: a post each side, a
		/// banner across, each built by the tile its middle is in, standing on the drawn ground.
		/// </summary>
		private static void ClimbParts(Builder b, Data data, int tx, int tz, float tileSize)
		{
			if (!HillClimb.Exists)
			{
				return;
			}
			foreach (double s in new[] { HillClimb.StartS, HillClimb.FinishS })
			{
				HillClimb.Post(s, 1, out double lx, out double lz);
				HillClimb.Post(s, -1, out double rx, out double rz);
				double mx = (lx + rx) * 0.5, mz = (lz + rz) * 0.5;
				if (Math.Floor(mx / tileSize) != tx || Math.Floor(mz / tileSize) != tz)
				{
					continue;
				}
				HillClimb.At(s, out double _, out double _, out double dx, out double dz);
				float top = float.MinValue;
				foreach ((double px, double pz) in new[] { (lx, lz), (rx, rz) })
				{
					GroundFooting(px, pz, dx, dz, 1.6, 1.6, out float y, out float extra);
					b.Solid(px, pz, y, dx, dz, 1.6f, 1.6f, 24f + extra, Metal);
					top = Math.Max(top, y + extra + 24f);
				}
				double width = Math.Sqrt((lx - rx) * (lx - rx) + (lz - rz) * (lz - rz));
				b.Solid(mx, mz, top - 7f, -dz, dx, (float)(width + 1.6), 1.0f, 6f, Banner);
				data.Footprints.Add((mx, mz, 1.0));
			}
		}

		// ---- the rally stage ---------------------------------------------------------------------------------------

		/// <summary>
		/// The rally stage's straw bales on the outside of its tightest corners and the arches over its start and finish lines
		/// (<see cref="Rally"/>), each bale and arch built by the tile its middle is in, standing on the drawn ground.
		/// </summary>
		private static void RallyParts(Builder b, Data data, int tx, int tz, float tileSize)
		{
			if (!Rally.Exists)
			{
				return;
			}
			foreach (Rally.Bales row in Rally.Corners)
			{
				for (double s = row.S0; s <= row.S1 + 0.01; s += Rally.BaleLength)
				{
					Rally.Bale(row, s, out double x, out double z, out double dx, out double dz);
					if (Math.Floor(x / tileSize) != tx || Math.Floor(z / tileSize) != tz)
					{
						continue;
					}
					// Laid end to end with no gap, so a car that runs wide meets a wall of straw, not posts.
					GroundFooting(x, z, dx, dz, Rally.BaleLength, Rally.BaleDepth, out float y, out float extra);
					b.Solid(x, z, y, dx, dz, (float)Rally.BaleLength, (float)Rally.BaleDepth, Rally.BaleHeight + extra, Straw);
				}
			}
			// The arches: a post each side, a banner across, facing the way the cars come (the finish arch the cars arriving, the start
			// arch the cars waiting on the line).
			foreach (double s in new[] { Rally.FinishS, Rally.StartS })
			{
				Rally.Post(s, 1, out double lx, out double lz);
				Rally.Post(s, -1, out double rx, out double rz);
				double mx = (lx + rx) * 0.5, mz = (lz + rz) * 0.5;
				if (Math.Floor(mx / tileSize) != tx || Math.Floor(mz / tileSize) != tz)
				{
					continue;
				}
				float top = float.MinValue;
				foreach ((double px, double pz) in new[] { (lx, lz), (rx, rz) })
				{
					GroundFooting(px, pz, Rally.DirX, Rally.DirZ, 1.6, 1.6, out float y, out float extra);
					b.Solid(px, pz, y, Rally.DirX, Rally.DirZ, 1.6f, 1.6f, 24f + extra, Metal);
					top = Math.Max(top, y + extra + 24f);
				}
				double width = Math.Sqrt((lx - rx) * (lx - rx) + (lz - rz) * (lz - rz));
				// Along across the stage (to its left), so the banner's faces look up and down the straight.
				b.Solid(mx, mz, top - 7f, -Rally.DirZ, Rally.DirX, (float)(width + 1.6), 1.0f, 6f, Banner);
				data.Footprints.Add((mx, mz, 1.0));
			}
		}

		/// <summary>
		/// Where a box w along (dx, dz) by d across, centred at (x, z), stands on the drawn ground: its base (a scene height) a little under
		/// its lowest corner or middle, and how much higher the highest is (added to its walls).
		/// </summary>
		private static void GroundFooting(double x, double z, double dx, double dz, double w, double d, out float y, out float extra)
		{
			double low = WorldGen.Height(x, z), high = low;
			for (int a = -1; a <= 1; a += 2)
			{
				for (int c = -1; c <= 1; c += 2)
				{
					double h = WorldGen.Height(x + dx * w * 0.5 * a - dz * d * 0.5 * c, z + dz * w * 0.5 * a + dx * d * 0.5 * c);
					low = Math.Min(low, h);
					high = Math.Max(high, h);
				}
			}
			y = (float)low - 0.3f;
			extra = (float)(high - low) + 0.3f;
		}

		// ---- where -------------------------------------------------------------------------------------------------

		/// <summary>The plots along one piece of a road (between samples i and i + 1), both sides.</summary>
		private static void Plots(Builder b, Data data, int r, Network.Road road, int i)
		{
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
			double spacing = towers ? 52.0 : terrace ? 26.0 : town ? 36.0 : 44.0;
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
					ulong h = Noise.Mix(Salt ^ (ulong)r * 0x9E3779B97F4A7C15UL ^ (ulong)k * 0xBF58476D1CE4E5B9UL ^ (ulong)(side + 2));
					double chance = towers ? 0.9 : terrace ? 0.95 : town ? 0.35 + 0.5 * (1.0 - from) : 0.7 * (1.0 - from * 0.6);
					if (Unit(h, 0) >= chance)
					{
						continue;
					}
					// Left of the road is (-dz, dx).
					double nx = -dz * side, nz = dx * side;
					if (towers)
					{
						Tower(b, data, r, px, pz, dx * side, dz * side, nx, nz, road.Half, from, h);
						continue;
					}
					double width = terrace ? spacing - 0.5 : spacing * (0.55 + 0.25 * Unit(h, 1));
					double depth = terrace ? 26.0 + 6.0 * Unit(h, 2) : 20.0 + 8.0 * Unit(h, 2);
					int floors = core ? 2 + (int)(Unit(h, 3) * (from < 0.2 ? 4.0 : 3.0)) : 1 + (int)(Unit(h, 3) * 2.0);
					if (high)
					{
						floors = Math.Max(2, floors);
					}
					// A terrace stands at the back of the sidewalk.
					double setback = terrace ? Network.Verge + 0.5 : 6.0 + 8.0 * Unit(h, 4);
					double off = road.Half + setback + depth * 0.5;
					double cx = px + nx * off, cz = pz + nz * off;
					int wall = (int)(Unit(h, 5) * 4.0);
					int roof = floors >= 4 ? Flat : RedTile + (int)(Unit(h, 6) * 2.0);
					// A shop on the ground floor of every high-street building: its front (the road side) a shopfront, in one of four colours.
					int front = high ? Shopfront : -1;
					// The house's frame turned so its across axis points away from the road on this side.
					House(b, data, r, setback, cx, cz, dx * side, dz * side, width, depth, floors * Storey + 1.5f, wall, roof, front, (int)(Unit(h, 7) * 4.0));
				}
			}
		}

		/// <summary>
		/// A glass tower on a plot by road <paramref name="r"/> at (px, pz), if it fits: 8 to 14 storeys (the taller the nearer the town's
		/// middle), in two or three stacked blocks, each set back from the one under it, with plant rooms on its roof.
		/// </summary>
		private static void Tower(Builder b, Data data, int r, double px, double pz, double dx, double dz, double nx, double nz, double half, double from, ulong h)
		{
			double w = 40.0 + 6.0 * Unit(h, 1), d = 34.0 + 10.0 * Unit(h, 2);
			const double setback = Network.Verge + 1.0;
			double off = half + setback + d * 0.5;
			double cx = px + nx * off, cz = pz + nz * off;
			if (!Fits(r, setback, cx, cz, dx, dz, w, d, 1.5, out double lo, out double hi))
			{
				return;
			}
			int floors = Math.Max(8, Math.Min(14, 8 + (int)((1.0 - from / TowerReach) * 4.0 + Unit(h, 3) * 3.0)));
			int tiers = Unit(h, 4) < 0.5 ? 2 : 3;
			int[] split = tiers == 2 ? new[] { (int)Math.Ceiling(floors * 0.6), 0, 0 } : new[] { (int)Math.Ceiling(floors * 0.45), (int)Math.Ceiling(floors * 0.35), 0 };
			split[tiers - 1] = floors - (tiers == 2 ? split[0] : split[0] + split[1]);
			float y = (float)(lo - 0.6);
			double tw = w, td = d;
			for (int k = 0; k < tiers; k++)
			{
				// Each block above set back all round from the one under it.
				double scale = k == 0 ? 1.0 : k == 1 ? 0.8 : 0.62;
				tw = w * scale;
				td = d * scale;
				float height = split[k] * Storey + (k == 0 ? (float)(hi - lo) + 0.6f : 0f);
				b.Box(cx, cz, y, dx, dz, (float)tw, (float)td, height, Glass, Flat);
				y += height;
			}
			// Plant rooms and, now and then, a mast.
			b.Box(cx - dx * tw * 0.2, cz - dz * tw * 0.2, y, dx, dz, (float)(tw * 0.3), (float)(td * 0.35), 5f, Metal, Flat);
			b.Box(cx + dx * tw * 0.22, cz + dz * tw * 0.22, y, dx, dz, (float)(tw * 0.2), (float)(td * 0.25), 3.5f, Metal, Flat);
			if (Unit(h, 5) < 0.4)
			{
				b.Box(cx + dx * tw * 0.3 + nx * td * 0.25, cz + dz * tw * 0.3 + nz * td * 0.25, y, dx, dz, 1f, 1f, 14f, Metal, Flat);
			}
			data.Footprints.Add((cx, cz, Math.Sqrt(w * w + d * d) * 0.5));
		}

		/// <summary>
		/// A stone church near a village's middle, a tower with a spire at its west end: the first spot, ring by ring outwards, where it
		/// fits off the roads.
		/// </summary>
		private static void Church(Builder b, Data data, Places.Place p)
		{
			ulong h = Noise.Mix(Salt ^ 0x7E3D1EUL ^ (ulong)(long)Math.Floor(p.X) * 0x632BE59BUL ^ (ulong)(long)Math.Floor(p.Z));
			const double length = 56.0, width = 22.0;
			for (double ring = 40.0; ring <= 220.0; ring += 30.0)
			{
				for (int k = 0; k < 8; k++)
				{
					double a = (k + Unit(h, 1)) * DetMath.Pi / 4.0;
					double cx = p.X + DetMath.Cos(a) * ring, cz = p.Z + DetMath.Sin(a) * ring;
					// Turned a little off east-west, as old churches are.
					double turn = (Unit(h, 2) - 0.5) * 0.4;
					double ax = DetMath.Cos(turn), az = DetMath.Sin(turn);
					if (!Fits(-1, 0.0, cx, cz, ax, az, length, width, 4.0, out double lo, out double hi))
					{
						continue;
					}
					float y = (float)(lo - 0.6), extra = (float)(hi - lo) + 0.6f;
					b.Box(cx + ax * 7.0, cz + az * 7.0, y, ax, az, 40f, 18f, 14f + extra, Stone, Slate);
					double tx = cx - ax * 20.0, tz = cz - az * 20.0;
					b.Box(tx, tz, y, ax, az, 12f, 12f, 32f + extra, Stone, Flat);
					b.Spire(tx, tz, y + 32f + extra, ax, az, 12.5f, 20f, Slate);
					data.Footprints.Add((cx, cz, Math.Sqrt(length * length + width * width) * 0.5));
					return;
				}
			}
		}

		/// <summary>A farmhouse, a barn and a shed round a farm's yard, at angles from the farm's own hash.</summary>
		private static void Farm(Builder b, Data data, Places.Place p)
		{
			ulong h = Noise.Mix(Salt ^ (ulong)(long)Math.Floor(p.X) * 0x632BE59BUL ^ (ulong)(long)Math.Floor(p.Z));
			double a = Unit(h, 0) * 2.0 * DetMath.Pi;
			double ax = DetMath.Cos(a), az = DetMath.Sin(a);
			(double dist, double turn, double w, double d, float height, int wall, int roof)[] parts =
			{
				(45.0, 0.0, 24.0, 20.0, 2f * Storey + 1.5f, Unit(h, 1) < 0.5 ? Stone : Render, Unit(h, 2) < 0.5 ? Slate : RedTile),
				(55.0, 2.2, 50.0, 30.0, 16f, Wood, Metal),
				(48.0, 4.1, 18.0, 14.0, 9f, Wood, Metal),
			};
			foreach ((double dist, double turn, double w, double d, float height, int wall, int roof) in parts)
			{
				double ca = DetMath.Cos(a + turn), sa = DetMath.Sin(a + turn);
				House(b, data, -1, 0.0, p.X + ca * dist, p.Z + sa * dist, ax, az, w, d, height, wall, roof);
			}
		}

		/// <summary>
		/// One building, if it fits: <paramref name="w"/> along (dx, dz), <paramref name="d"/> across (to the left of (dx, dz), away
		/// from its road), <paramref name="height"/> to the eaves. <paramref name="own"/> is the road it fronts (-1 for none), its front
		/// <paramref name="front"/> from that road's edge. With a <paramref name="shop"/> material, its ground floor's front is that, in
		/// colour <paramref name="variant"/>.
		/// </summary>
		private static void House(Builder b, Data data, int own, double front, double cx, double cz, double dx, double dz, double w, double d, float height, int wall, int roof, int shop = -1, int variant = -1)
		{
			if (!Fits(own, front, cx, cz, dx, dz, w, d, 1.5, out double lo, out double hi))
			{
				return;
			}
			data.Footprints.Add((cx, cz, Math.Sqrt(w * w + d * d) * 0.5));
			float extra = (float)(hi - lo);
			b.Box(cx, cz, (float)(lo - 0.6), dx, dz, (float)w, (float)d, height + extra, wall, roof, shop, Storey + 0.6f + extra, variant);
		}

		/// <summary>Where a footprint is tested, as shares of its width and depth: corners, middle, and the middles of its sides (a lot's corner can poke into a long building's side).</summary>
		private static readonly double[] Us = { -0.5, 0.5, -0.5, 0.5, 0.0, -0.5, 0.5, 0.0, 0.0 };
		private static readonly double[] Vs = { -0.5, -0.5, 0.5, 0.5, 0.0, 0.0, 0.0, -0.5, 0.5 };

		/// <summary>
		/// Whether a footprint <paramref name="w"/> along (dx, dz) by <paramref name="d"/> across fits (see <see cref="House"/>), keeping
		/// <paramref name="margin"/> off every road; the lowest and highest ground (scene heights) under its corners and middle.
		/// </summary>
		private static bool Fits(int own, double front, double cx, double cz, double dx, double dz, double w, double d, double margin, out double lo, out double hi)
		{
			// Across the footprint, away from its road.
			double nx = -dz, nz = dx;
			lo = double.MaxValue;
			hi = double.MinValue;
			for (int c = 0; c < Us.Length; c++)
			{
				double u = Us[c] * w, v = Vs[c] * d;
				double x = cx + dx * u + nx * v, z = cz + dz * u + nz * v;
				// How far this point is from its own road's edge: the front corners the setback, the back ones the setback and depth.
				if (!Network.Claims(x, z, own, front + (v > 0.0 ? d : (v < 0.0 ? 0.0 : d * 0.5)), margin))
				{
					return false;
				}
				if (Town.Taken(x, z, 2.0) || Parks.At(x, z))
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
			return hi - lo <= 6.0;
		}

		/// <summary>
		/// A park bench (1.8 m long): a wooden seat and back on two metal legs, standing on the lowest ground under it, its seat facing
		/// (Dx, Dz).
		/// </summary>
		private static void Bench(Builder b, Data data, Parks.Bench bench)
		{
			// Along the bench is to the right of the way it faces; its back is behind.
			double ax = bench.Dz, az = -bench.Dx;
			float lo = float.MaxValue;
			foreach ((double u, double v) in new[] { (-3.0, -1.0), (3.0, -1.0), (-3.0, 1.0), (3.0, 1.0) })
			{
				lo = Math.Min(lo, (float)WorldGen.Height(bench.X + ax * u + bench.Dx * v, bench.Z + az * u + bench.Dz * v));
			}
			float y = lo - 0.3f;
			foreach (double u in new[] { -2.4, 2.4 })
			{
				b.Solid(bench.X + ax * u, bench.Z + az * u, y, ax, az, 0.4f, 1.4f, 1.6f, Metal);
			}
			b.Solid(bench.X, bench.Z, y + 1.5f, ax, az, 6f, 1.6f, 0.3f, Wood);
			b.Solid(bench.X - bench.Dx * 0.8, bench.Z - bench.Dz * 0.8, y + 1.8f, ax, az, 6f, 0.3f, 1.4f, Wood);
			data.Footprints.Add((bench.X, bench.Z, 3.5));
		}

		// ---- lots --------------------------------------------------------------------------------------------------

		/// <summary>
		/// A lot of <see cref="Town"/>: its surface (car park, yard or forecourt, and paving round its building) as flat quads just over
		/// its levelled ground, a low kerb round its edge, and its building; for the retail park, lamp posts and a sign; for a filling
		/// station, a canopy over its pumps.
		/// </summary>
		private static void Lot(Builder b, Data data, Town.Lot lot)
		{
			double half = lot.W * 0.5;
			Town.Building(lot.Kind, out double bw, out double bv, out double bd);
			int variant = (int)(Town.Unit(lot.Hash, 3) * 4.0);
			switch (lot.Kind)
			{
				case LotKind.Retail:
				{
					// Two blocks of bays either side of a middle island, ends left for driving round.
					double block = Math.Floor((half - 13.0) / BayWidth) * BayWidth;
					Surface(b, lot, -half, half, 0.0, 14.0, Asphalt);
					BayRows(b, lot, 14.0, block, true);
					Surface(b, lot, -half, half, 46.0, 64.0, Asphalt);
					BayRows(b, lot, 64.0, block, true);
					Surface(b, lot, -half, half, 96.0, bv - 8.0, Asphalt);
					Surface(b, lot, -half, half, bv - 8.0, bv, Concrete);
					foreach (double v in new[] { 22.0, 38.0, 72.0, 88.0 })
					{
						Block(b, lot, 0.0, v, 0.8, 0.8, 22f, Metal, Flat);
						Block(b, lot, 0.0, v, 1.0, 4.5, 1f, Metal, Flat, -1, 0f, -1, 21f);
					}
					Block(b, lot, 0.0, bv + bd * 0.5, bw, bd, 24f, Cladding, Flat, Glass, 15f);
					Block(b, lot, 0.0, bv - 0.7, 70.0, 1.2, 7f, Sign, Flat, Sign, 7f, variant, 16f);
					break;
				}
				case LotKind.Shop:
				{
					double block = Math.Floor((lot.W - 8.0) / BayWidth) * BayWidth;
					Surface(b, lot, -half, half, 0.0, 8.0, Asphalt);
					Surface(b, lot, -half, -block * 0.5, 8.0, 8.0 + BayLength, Asphalt);
					BayRow(b, lot, -block * 0.5, block * 0.5, 8.0, 8.0 + BayLength, false);
					Surface(b, lot, block * 0.5, half, 8.0, 8.0 + BayLength, Asphalt);
					Surface(b, lot, -half, half, 8.0 + BayLength, bv, Concrete);
					Block(b, lot, 0.0, bv + bd * 0.5, bw, bd, 13f, Town.Unit(lot.Hash, 4) < 0.5 ? Brick : Render, Flat, Shopfront, Storey, variant);
					break;
				}
				case LotKind.Workshop:
					Surface(b, lot, -half, half, 0.0, bv, Concrete);
					Block(b, lot, 0.0, bv + bd * 0.5, bw, bd, 16f, Cladding, Metal);
					break;
				case LotKind.Service:
				{
					// Gravel to the back; along the back four teams' vans, each with an awning on posts beside it to work under.
					Surface(b, lot, -half, half, 0.0, lot.D, Gravel);
					for (double u = -half + 12.0; u <= half - 35.9; u += (lot.W - 48.0) / 3.0)
					{
						Block(b, lot, u, lot.D - 15.0, 10.0, 24.0, 11f, Town.Unit(lot.Hash, (int)(u + 500.0)) < 0.5 ? White : Metal, Flat);
						Block(b, lot, u + 15.0, lot.D - 28.0, 18.0, 26.0, 1.2f, Canopy, Flat, -1, 0f, -1, 9f);
						foreach ((double cu, double cv) in new[] { (6.5, -40.0), (23.5, -40.0), (6.5, -16.0), (23.5, -16.0) })
						{
							Block(b, lot, u + cu, lot.D + cv, 0.6, 0.6, 9.2f, Metal, Flat);
						}
						Town.World(lot, u, lot.D - 15.0, out double vx, out double vz);
						data.Footprints.Add((vx, vz, 13.0));
					}
					b.Apron(lot, Gravel);
					b.Kerb(lot);
					return;
				}
				case LotKind.Paddock:
				{
					// Tarmac to the back; along the back the competitors' vans, each with its open trailer beside it.
					Surface(b, lot, -half, half, 0.0, lot.D, Asphalt);
					for (double u = -half + 10.0; u <= half - 27.9; u += (lot.W - 38.0) / 3.0)
					{
						Block(b, lot, u, lot.D - 14.0, 9.0, 22.0, 10f, Town.Unit(lot.Hash, (int)(u + 500.0)) < 0.5 ? White : Metal, Flat);
						Block(b, lot, u + 12.0, lot.D - 13.0, 7.5, 20.0, 1.6f, Metal, Flat);
						Town.World(lot, u + 6.0, lot.D - 14.0, out double vx, out double vz);
						data.Footprints.Add((vx, vz, 14.0));
					}
					b.Apron(lot, Asphalt);
					b.Kerb(lot);
					return;
				}
				case LotKind.Turning:
					Surface(b, lot, -half, half, 0.0, lot.D, Asphalt);
					b.Apron(lot, Asphalt);
					b.Kerb(lot);
					return;
				default:
				{
					Surface(b, lot, -half, half, 0.0, bv, Concrete);
					// The canopy on four posts, two pumps under it.
					Block(b, lot, 0.0, 20.0, 34.0, 18.0, 2.5f, Canopy, Flat, -1, 0f, -1, 13f);
					foreach ((double u, double v) in new[] { (-14.0, 13.0), (14.0, 13.0), (-14.0, 27.0), (14.0, 27.0) })
					{
						Block(b, lot, u, v, 1.0, 1.0, 13.3f, Metal, Flat);
					}
					Block(b, lot, -7.0, 20.0, 3.0, 1.4, 5f, Canopy, Flat);
					Block(b, lot, 7.0, 20.0, 3.0, 1.4, 5f, Canopy, Flat);
					Block(b, lot, 0.0, bv + bd * 0.5, bw, bd, 11f, Render, Flat, Shopfront, Storey, variant);
					break;
				}
			}
			// The strip out to the verge's foot where the road bends away, and paving round the building and behind it.
			b.Apron(lot, lot.Kind == LotKind.Retail || lot.Kind == LotKind.Shop ? Asphalt : Concrete);
			Surface(b, lot, -half, -bw * 0.5, bv, bv + bd, Concrete);
			Surface(b, lot, bw * 0.5, half, bv, bv + bd, Concrete);
			Surface(b, lot, -half, half, bv + bd, lot.D, Concrete);
			b.Kerb(lot);
			Town.World(lot, 0.0, bv + bd * 0.5, out double cx, out double cz);
			data.Footprints.Add((cx, cz, Math.Sqrt(bw * bw + bd * bd) * 0.5));
		}

		/// <summary>Two rows of bays back to back from <paramref name="v0"/>, each side of a middle island if <paramref name="island"/>.</summary>
		private static void BayRows(Builder b, Town.Lot lot, double v0, double block, bool island)
		{
			double half = lot.W * 0.5;
			for (int row = 0; row < 2; row++)
			{
				double a = v0 + row * BayLength, c = a + BayLength;
				// The first row's back at the second's: cars park nose to nose.
				bool back = row == 0;
				Surface(b, lot, -half, -3.0 - block, a, c, Asphalt);
				BayRow(b, lot, -3.0 - block, -3.0, a, c, !back);
				Surface(b, lot, -3.0, 3.0, a, c, Concrete);
				BayRow(b, lot, 3.0, 3.0 + block, a, c, !back);
				Surface(b, lot, 3.0 + block, half, a, c, Asphalt);
			}
		}

		/// <summary>A row of bays from u0 to u1 (a whole number of bays), v0 to v1 deep; its back line at v1, or at v0 if <paramref name="flip"/>.</summary>
		private static void BayRow(Builder b, Town.Lot lot, double u0, double u1, double v0, double v1, bool flip)
		{
			b.Surface(lot, u0, u1, v0, v1, Bays, 0.0, (u1 - u0) / BayWidth, flip ? 1.0 : 0.0, flip ? 0.0 : 1.0);
		}

		/// <summary>A plain stretch of a lot's surface, its texture laid at 8 units a repeat.</summary>
		private static void Surface(Builder b, Town.Lot lot, double u0, double u1, double v0, double v1, int material)
		{
			if (u1 - u0 < 0.01 || v1 - v0 < 0.01)
			{
				return;
			}
			b.Surface(lot, u0, u1, v0, v1, material, u0 / 8.0, u1 / 8.0, v0 / 8.0, v1 / 8.0);
		}

		/// <summary>
		/// A box on a lot, <paramref name="w"/> along the road by <paramref name="d"/> deep, centred u along and v back: standing on the
		/// lot's surface (its base a little under it, its walls running down to the lower end where the lot slopes), or raised
		/// <paramref name="lift"/> over the surface at its middle.
		/// </summary>
		private static void Block(Builder b, Town.Lot lot, double u, double v, double w, double d, float h, int wall, int roof, int front = -1, float frontH = 0f, int variant = -1, float lift = 0f)
		{
			Town.World(lot, u, v, out double x, out double z);
			float y, extra = 0f;
			if (lift > 0f)
			{
				y = (float)(Town.Surface(lot, u) + Network.Lift) + lift;
			}
			else
			{
				double low = Math.Min(Town.Surface(lot, u - w * 0.5), Town.Surface(lot, u + w * 0.5));
				extra = (float)(Math.Abs(lot.Grade) * w) + 0.3f;
				y = (float)(low + Network.Lift) - 0.3f;
			}
			b.Box(x, z, y, lot.Dx, lot.Dz, (float)w, (float)d, h + extra, wall, roof, front, frontH + extra, variant);
		}

		/// <summary>A number from 0 to 1 from hash <paramref name="h"/>, the <paramref name="k"/>th of several.</summary>
		private static double Unit(ulong h, int k)
		{
			return (Noise.Mix(h + (ulong)k) >> 11) * (1.0 / 9007199254740992.0);
		}

		// ---- the mesh ----------------------------------------------------------------------------------------------

		/// <summary>Collects the boxes and roofs of a tile into one mesh, a triangle list per material.</summary>
		private sealed class Builder
		{
			private readonly double ox, oz;
			private readonly List<Vector3> v = new List<Vector3>();
			private readonly List<Vector3> n = new List<Vector3>();
			private readonly List<Vector2> uv = new List<Vector2>();
			private readonly List<int>[] tris = new List<int>[Count];

			internal Builder(double ox, double oz)
			{
				this.ox = ox;
				this.oz = oz;
				for (int k = 0; k < Count; k++)
				{
					tris[k] = new List<int>();
				}
			}

			/// <summary>
			/// A box standing on <paramref name="y"/>, <paramref name="w"/> along (dx, dz) and <paramref name="d"/> across, walls
			/// <paramref name="h"/> high, with a pitched roof (ridge along the width, gables at the ends), a flat one, or none. With a
			/// <paramref name="front"/> material, the bottom <paramref name="frontH"/> of the front wall (the one facing away from across,
			/// towards the road) is that, in colour <paramref name="variant"/> for a material that has four (shopfronts, signs).
			/// </summary>
			internal void Box(double cx, double cz, float y, double dx, double dz, float w, float d, float h, int wall, int roof, int front = -1, float frontH = 0f, int variant = -1)
			{
				// y is already a scene height (WorldGen.Height includes the world's lift).
				Vector3 c = new Vector3((float)(cx - ox), y, (float)(cz - oz));
				Vector3 along = new Vector3((float)dx, 0f, (float)dz);
				Vector3 across = new Vector3((float)-dz, 0f, (float)dx);
				Vector3 a = along * (w * 0.5f), b = across * (d * 0.5f), up = Vector3.up * h;
				Vector3 p0 = c - a - b, p1 = c + a - b, p2 = c + a + b, p3 = c - a + b;
				if (front >= 0)
				{
					float lower = Mathf.Min(frontH, h);
					Vector3 split = Vector3.up * lower;
					float bays = Mathf.Max(1f, Mathf.Round(w / Bay));
					bool four = front == Shopfront || front == Sign;
					float v0 = four ? Mathf.Max(0, variant) * 0.25f : 0f, v1 = four ? v0 + 0.25f : lower / Storey;
					Quad(p0, p1, p0 + split, p1 + split, -across, front, 0f, bays, v0, v1);
					if (h > lower)
					{
						Quad(p0 + split, p1 + split, p0 + up, p1 + up, -across, wall, 0f, bays, lower / Storey, h / Storey);
					}
				}
				else
				{
					Wall(p0, p1, h, wall, -across);
				}
				Wall(p1, p2, h, wall, along);
				Wall(p2, p3, h, wall, across);
				Wall(p3, p0, h, wall, -along);
				if (roof == None)
				{
					return;
				}
				if (roof == Flat)
				{
					Quad(p0 + up, p1 + up, p3 + up, p2 + up, Vector3.up, roof, w / Bay, d / Bay);
					Quad(p0 + up, p1 + up, p3 + up, p2 + up, Vector3.down, roof, w / Bay, d / Bay);
					return;
				}
				// Pitched: the ridge runs along the width, over the middle, a little past the walls.
				// About 48 degrees, a usual pitch: shallower, and from the street the near slope hides behind the front wall's top.
				float rise = Mathf.Min(d * 0.55f, 16f);
				Vector3 eave = Vector3.up * h, ridge = Vector3.up * (h + rise);
				Vector3 over = across * 1.2f, end = along * 0.8f;
				Vector3 r0 = c - a - end + ridge, r1 = c + a + end + ridge;
				Vector3 e0 = p0 - end - over + eave, e1 = p1 + end - over + eave, e2 = p2 + end + over + eave, e3 = p3 - end + over + eave;
				float slope = Mathf.Sqrt(rise * rise + d * d * 0.25f);
				Vector3 out0 = (Vector3.up * (d * 0.5f) - across * rise).normalized, out1 = (Vector3.up * (d * 0.5f) + across * rise).normalized;
				Quad(e0, e1, r0, r1, out0, roof, (w + 1.6f) / Bay, slope / Bay);
				Quad(e3, e2, r0, r1, out1, roof, (w + 1.6f) / Bay, slope / Bay);
				// The undersides too: the eaves overhang the walls, and a roof drawn one side only is see-through from below.
				Quad(e0, e1, r0, r1, -out0, roof, (w + 1.6f) / Bay, slope / Bay);
				Quad(e3, e2, r0, r1, -out1, roof, (w + 1.6f) / Bay, slope / Bay);
				Triangle(p0 + eave, p3 + eave, c - a + ridge, -along, wall);
				Triangle(p1 + eave, p2 + eave, c + a + ridge, along, wall);
				Triangle(p0 + eave, p3 + eave, c - a + ridge, along, wall);
				Triangle(p1 + eave, p2 + eave, c + a + ridge, -along, wall);
			}

			/// <summary>A small box all of one material, top and sides (a bench's seat, its legs): <paramref name="w"/> along (dx, dz), <paramref name="d"/> across, <paramref name="h"/> high.</summary>
			internal void Solid(double cx, double cz, float y, double dx, double dz, float w, float d, float h, int material)
			{
				Vector3 c = new Vector3((float)(cx - ox), y, (float)(cz - oz));
				Vector3 along = new Vector3((float)dx, 0f, (float)dz);
				Vector3 across = new Vector3((float)-dz, 0f, (float)dx);
				Vector3 a = along * (w * 0.5f), b = across * (d * 0.5f), up = Vector3.up * h;
				Vector3 p0 = c - a - b, p1 = c + a - b, p2 = c + a + b, p3 = c - a + b;
				Quad(p0, p1, p0 + up, p1 + up, -across, material, w / Bay, h / Storey);
				Quad(p1, p2, p1 + up, p2 + up, along, material, d / Bay, h / Storey);
				Quad(p2, p3, p2 + up, p3 + up, across, material, w / Bay, h / Storey);
				Quad(p3, p0, p3 + up, p0 + up, -along, material, d / Bay, h / Storey);
				Quad(p0 + up, p1 + up, p3 + up, p2 + up, Vector3.up, material, w / Bay, d / Bay);
			}

			/// <summary>A four-sided spire standing on <paramref name="y"/>, <paramref name="side"/> across its base, <paramref name="h"/> high.</summary>
			internal void Spire(double cx, double cz, float y, double dx, double dz, float side, float h, int material)
			{
				Vector3 c = new Vector3((float)(cx - ox), y, (float)(cz - oz));
				Vector3 along = new Vector3((float)dx, 0f, (float)dz) * (side * 0.5f);
				Vector3 across = new Vector3((float)-dz, 0f, (float)dx) * (side * 0.5f);
				Vector3 top = c + Vector3.up * h;
				Vector3[] corners = { c - along - across, c + along - across, c + along + across, c - along + across };
				for (int k = 0; k < 4; k++)
				{
					Vector3 p = corners[k], q = corners[(k + 1) % 4];
					Vector3 mid = (p + q) * 0.5f - c;
					Vector3 facing = (mid.normalized * h + Vector3.up * mid.magnitude).normalized;
					Triangle(p, q, top, facing, material);
				}
			}

			/// <summary>
			/// A stretch of a lot's surface, u0 to u1 along and v0 to v1 back (see <see cref="Town.Local"/>), on its plane; its texture
			/// from (tu0, tv0) at (u0, v0) to (tu1, tv1) at (u1, v1).
			/// </summary>
			internal void Surface(Town.Lot lot, double u0, double u1, double v0, double v1, int material, double tu0, double tu1, double tv0, double tv1)
			{
				Vector3 a = Point(lot, u0, v0, 0.0), b = Point(lot, u1, v0, 0.0), c = Point(lot, u0, v1, 0.0), d = Point(lot, u1, v1, 0.0);
				Vector3 facing = (Vector3.up - new Vector3((float)lot.Dx, 0f, (float)lot.Dz) * (float)lot.Grade).normalized;
				Quad(a, b, c, d, facing, material, (float)tu0, (float)tu1, (float)tv0, (float)tv1);
			}

			/// <summary>
			/// A low kerb round a lot's edge: from the surface's edge out and down under the ground, so the surface never shows a gap or a
			/// lip where it meets the ground or the road's verge. The front runs along the verge's foot.
			/// </summary>
			internal void Kerb(Town.Lot lot)
			{
				List<(double u, double v)> outline = Front(lot);
				outline.Add((lot.W * 0.5, lot.D));
				outline.Add((-lot.W * 0.5, lot.D));
				for (int k = 0; k < outline.Count; k++)
				{
					(double u0, double v0) = outline[k];
					(double u1, double v1) = outline[(k + 1) % outline.Count];
					double len = Math.Sqrt((u1 - u0) * (u1 - u0) + (v1 - v0) * (v1 - v0));
					if (len < 0.01)
					{
						continue;
					}
					// Outward, in the lot's frame: the outline runs round with the lot on its left.
					double ou = (v1 - v0) / len * 1.5, ov = -(u1 - u0) / len * 1.5;
					Vector3 p = Point(lot, u0, v0, 0.0), q = Point(lot, u1, v1, 0.0);
					Vector3 po = Point(lot, u0 + ou, v0 + ov, -0.6), qo = Point(lot, u1 + ou, v1 + ov, -0.6);
					Vector3 outward = ((po + qo) - (p + q)).normalized;
					Quad(p, q, po, qo, (outward + Vector3.up * 2f).normalized, Concrete, 0f, (float)(len / 8.0), 0f, 0.2f);
				}
			}

			/// <summary>The strip of a lot's surface in front of its front line, out to the foot of the road's verge.</summary>
			internal void Apron(Town.Lot lot, int material)
			{
				List<(double u, double v)> front = Front(lot);
				Vector3 facing = (Vector3.up - new Vector3((float)lot.Dx, 0f, (float)lot.Dz) * (float)lot.Grade).normalized;
				for (int k = 0; k + 1 < front.Count; k++)
				{
					(double u0, double v0) = front[k];
					(double u1, double v1) = front[k + 1];
					if (v0 > -0.01 && v1 > -0.01)
					{
						continue;
					}
					Quad(Point(lot, u0, v0, 0.0), Point(lot, u1, v1, 0.0), Point(lot, u0, 0.0, 0.0), Point(lot, u1, 0.0, 0.0), facing, material, (float)(u0 / 8.0), (float)(u1 / 8.0), (float)(Math.Min(v0, v1) / 8.0), 0f);
				}
			}

			/// <summary>The foot of the verge in front of a lot, left to right across its width, as lot-frame points.</summary>
			private static List<(double u, double v)> Front(Town.Lot lot)
			{
				List<(double u, double v)> points = new List<(double, double)>();
				double half = lot.W * 0.5;
				points.Add((-half, Town.Foot(lot, -half)));
				for (int k = 0; k < lot.Foot.Length; k++)
				{
					double u = -half - Town.Pad + k * Town.FrontStep;
					if (u > -half + 0.01 && u < half - 0.01)
					{
						points.Add((u, lot.Foot[k]));
					}
				}
				points.Add((half, Town.Foot(lot, half)));
				return points;
			}

			/// <summary>Lot-frame point (u, v), <paramref name="dy"/> over the lot's surface, in the tile's frame.</summary>
			private Vector3 Point(Town.Lot lot, double u, double v, double dy)
			{
				Town.World(lot, u, v, out double x, out double z);
				return new Vector3((float)(x - ox), (float)(Town.Surface(lot, u) + Network.Lift + dy), (float)(z - oz));
			}

			/// <summary>A wall from bottom edge (p, q) up <paramref name="h"/>, facing <paramref name="facing"/>; a window to a bay and a storey.</summary>
			private void Wall(Vector3 p, Vector3 q, float h, int material, Vector3 facing)
			{
				float len = (q - p).magnitude;
				Quad(p, q, p + Vector3.up * h, q + Vector3.up * h, facing, material, Mathf.Max(1f, Mathf.Round(len / Bay)), h / Storey);
			}

			/// <summary>A quad a-b (bottom or one edge), c-d (the opposite edge), facing <paramref name="facing"/>, its texture repeated (u, v) times.</summary>
			private void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 facing, int material, float u, float vv)
			{
				Quad(a, b, c, d, facing, material, 0f, u, 0f, vv);
			}

			/// <summary>A quad as above, its texture from (u0, v0) at a to (u1, v1) at d.</summary>
			private void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 facing, int material, float u0, float u1, float v0, float v1)
			{
				int i = v.Count;
				v.Add(a);
				v.Add(b);
				v.Add(c);
				v.Add(d);
				uv.Add(new Vector2(u0, v0));
				uv.Add(new Vector2(u1, v0));
				uv.Add(new Vector2(u0, v1));
				uv.Add(new Vector2(u1, v1));
				for (int k = 0; k < 4; k++)
				{
					n.Add(facing);
				}
				Add(material, i, i + 1, i + 2, facing);
				Add(material, i + 2, i + 1, i + 3, facing);
			}

			private void Triangle(Vector3 a, Vector3 b, Vector3 c, Vector3 facing, int material)
			{
				int i = v.Count;
				v.Add(a);
				v.Add(b);
				v.Add(c);
				float w = (b - a).magnitude / Bay;
				uv.Add(new Vector2(0f, 0f));
				uv.Add(new Vector2(w, 0f));
				uv.Add(new Vector2(w * 0.5f, 0.6f));
				for (int k = 0; k < 3; k++)
				{
					n.Add(facing);
				}
				Add(material, i, i + 1, i + 2, facing);
			}

			private void Add(int material, int a, int b, int c, Vector3 facing)
			{
				List<int> list = tris[material];
				list.Add(a);
				if (Vector3.Dot(Vector3.Cross(v[b] - v[a], v[c] - v[a]), facing) >= 0f)
				{
					list.Add(b);
					list.Add(c);
				}
				else
				{
					list.Add(c);
					list.Add(b);
				}
			}

			internal RoadMeshData Done()
			{
				if (v.Count == 0)
				{
					return null;
				}
				int[][] t = new int[Count][];
				for (int k = 0; k < Count; k++)
				{
					t[k] = tris[k].ToArray();
				}
				return new RoadMeshData { Vertices = v.ToArray(), Normals = n.ToArray(), Uv = uv.ToArray(), Triangles = t };
			}
		}

		// ---- looks -------------------------------------------------------------------------------------------------

		/// <summary>
		/// The materials, in submesh order, on the given lit shader: pixel-art walls with a window to a bay and storey, roofs, and the lots'
		/// surfaces (drawn finer, 4 pixels a unit, so bay lines are crisp lines rather than blocks).
		/// </summary>
		internal static Material[] Materials(Shader shader)
		{
			Material[] m = new Material[Count];
			m[Brick] = Make(shader, WallTexture(new Color(0.55f, 0.30f, 0.24f), true), "Brick");
			m[Render] = Make(shader, WallTexture(new Color(0.86f, 0.80f, 0.66f), false), "Render");
			m[White] = Make(shader, WallTexture(new Color(0.88f, 0.88f, 0.86f), false), "White");
			m[Stone] = Make(shader, WallTexture(new Color(0.58f, 0.56f, 0.52f), true), "Stone");
			m[Wood] = Make(shader, Planks(new Color(0.46f, 0.22f, 0.16f)), "Wood");
			m[RedTile] = Make(shader, Rows(new Color(0.62f, 0.27f, 0.20f)), "Red Tile");
			m[Slate] = Make(shader, Rows(new Color(0.30f, 0.32f, 0.36f)), "Slate");
			m[Metal] = Make(shader, Rows(new Color(0.48f, 0.50f, 0.50f)), "Metal");
			m[Flat] = Make(shader, Plain(new Color(0.40f, 0.40f, 0.40f)), "Flat Roof");
			m[Glass] = Make(shader, CurtainWall(), "Glass");
			m[Shopfront] = Make(shader, Shopfronts(), "Shopfront");
			m[Cladding] = Make(shader, Ribs(new Color(0.60f, 0.63f, 0.64f)), "Cladding");
			m[Sign] = Make(shader, Signs(), "Sign");
			m[Canopy] = Make(shader, CanopyTexture(), "Canopy");
			m[Asphalt] = Make(shader, Paved(new Color(0.25f, 0.25f, 0.27f), false), "Car Park");
			m[Bays] = Make(shader, BayTexture(), "Bays");
			m[Concrete] = Make(shader, Paved(new Color(0.62f, 0.61f, 0.58f), true), "Concrete");
			m[GarageDoor] = Make(shader, GarageDoors(), "Garage Door");
			m[Seats] = Make(shader, SeatRows(), "Stand Seats");
			m[Gravel] = Make(shader, GravelTexture(), "Gravel");
			m[Straw] = Make(shader, StrawTexture(), "Straw");
			m[Banner] = Make(shader, BannerTexture(), "Banner");
			return m;
		}

		private static Material Make(Shader shader, Texture2D texture, string name)
		{
			Material m = new Material(shader) { name = "OpenWorld " + name, hideFlags = HideFlags.HideAndDontSave };
			if (m.HasProperty("_BaseMap"))
			{
				m.SetTexture("_BaseMap", texture);
			}
			if (m.HasProperty("_MainTex"))
			{
				m.SetTexture("_MainTex", texture);
			}
			if (m.HasProperty("_BaseColor"))
			{
				m.SetColor("_BaseColor", Color.white);
			}
			if (m.HasProperty("_Smoothness"))
			{
				m.SetFloat("_Smoothness", 0.05f);
			}
			return m;
		}

		private static Texture2D New(int w, int h, bool mips = false)
		{
			return new Texture2D(w, h, TextureFormat.RGBA32, mips) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat, hideFlags = HideFlags.HideAndDontSave };
		}

		/// <summary>The colours of the four shopfronts and signs.</summary>
		private static readonly Color[] Brands = { new Color(0.72f, 0.16f, 0.14f), new Color(0.14f, 0.45f, 0.26f), new Color(0.15f, 0.30f, 0.62f), new Color(0.86f, 0.66f, 0.12f) };

		private static float Grain(int x, int y, int salt)
		{
			return ((int)(Noise.Mix((ulong)(y * 977 + x) + (ulong)salt * 0x9E3779B9UL) >> 61) - 3.5f) * 0.012f;
		}

		/// <summary>One bay of one storey of a curtain wall: a mullion, a spandrel band at the floor, and glass with a glint.</summary>
		private static Texture2D CurtainWall()
		{
			Texture2D t = New(8, 10);
			Color glass = new Color(0.30f, 0.44f, 0.56f), light = new Color(0.52f, 0.66f, 0.76f), mullion = new Color(0.66f, 0.70f, 0.72f), spandrel = new Color(0.22f, 0.27f, 0.32f);
			for (int y = 0; y < 10; y++)
			{
				for (int x = 0; x < 8; x++)
				{
					Color c = x == 0 ? mullion : y <= 1 ? spandrel : (x + y) % 6 == 0 && y > 3 ? light : glass;
					if (x > 0 && y > 1)
					{
						c *= 1f - (y - 2) * 0.02f;
					}
					c.a = 1f;
					t.SetPixel(x, y, c);
				}
			}
			t.Apply();
			return t;
		}

		/// <summary>
		/// A shop's ground floor, a bay wide: a stall riser, a big window with a door in it, and a sign band with lettering over it; four
		/// colours stacked (a quarter of the texture each, picked by a quad's v).
		/// </summary>
		private static Texture2D Shopfronts()
		{
			Texture2D t = New(8, 40);
			Color glass = new Color(0.20f, 0.26f, 0.32f), frame = new Color(0.90f, 0.90f, 0.88f), riser = new Color(0.30f, 0.30f, 0.32f), letter = new Color(0.97f, 0.96f, 0.90f);
			for (int k = 0; k < 4; k++)
			{
				Color brand = Brands[k];
				for (int y = 0; y < 10; y++)
				{
					for (int x = 0; x < 8; x++)
					{
						Color c;
						if (y >= 7)
						{
							c = y == 8 && x > 0 && x < 7 && (Noise.Mix((ulong)(k * 8 + x)) & 3) != 0 ? letter : brand;
						}
						else if (y == 0)
						{
							c = riser;
						}
						else if (x == 0 || y == 6)
						{
							c = frame;
						}
						else
						{
							c = x >= 5 && x <= 6 && y <= 5 ? brand * 0.7f : glass;
						}
						c.a = 1f;
						t.SetPixel(x, k * 10 + y, c);
					}
				}
			}
			t.Apply();
			return t;
		}

		/// <summary>Profiled metal cladding: vertical ribs.</summary>
		private static Texture2D Ribs(Color metal)
		{
			// Two ribs to a bay (4 pixels, about 4 units apart: a lit edge, the sheet, its shadow), with mipmaps. Ribs a pixel apart with
			// no mipmaps broke up into moiré stripes beyond a few car lengths, pinkish under the scene's grading.
			Texture2D t = New(8, 10, true);
			float[] shade = { 1.08f, 1f, 1f, 0.84f };
			for (int y = 0; y < 10; y++)
			{
				for (int x = 0; x < 8; x++)
				{
					Color c = metal * shade[x % 4];
					c.a = 1f;
					t.SetPixel(x, y, c);
				}
			}
			t.Apply(true);
			return t;
		}

		/// <summary>A sign board: a band of colour with white lettering, four colours stacked like the shopfronts.</summary>
		private static Texture2D Signs()
		{
			Texture2D t = New(8, 40);
			for (int k = 0; k < 4; k++)
			{
				for (int y = 0; y < 10; y++)
				{
					for (int x = 0; x < 8; x++)
					{
						bool text = y >= 3 && y <= 6 && x != 7 && (Noise.Mix((ulong)(k * 80 + y * 8 + x) + 0x51UL) & 3) != 0;
						Color c = y == 0 || y == 9 ? new Color(0.9f, 0.9f, 0.88f) : text ? new Color(0.98f, 0.97f, 0.92f) : Brands[k];
						c.a = 1f;
						t.SetPixel(x, k * 10 + y, c);
					}
				}
			}
			t.Apply();
			return t;
		}

		/// <summary>A filling station's canopy fascia: white with a coloured stripe.</summary>
		private static Texture2D CanopyTexture()
		{
			Texture2D t = New(8, 10);
			for (int y = 0; y < 10; y++)
			{
				for (int x = 0; x < 8; x++)
				{
					Color c = y >= 3 && y <= 6 ? Brands[0] : new Color(0.93f, 0.93f, 0.91f);
					c.a = 1f;
					t.SetPixel(x, y, c);
				}
			}
			t.Apply();
			return t;
		}

		/// <summary>8 by 8 units of asphalt or concrete at 4 pixels a unit; concrete in slabs with joints at its edges.</summary>
		private static Texture2D Paved(Color colour, bool joints)
		{
			Texture2D t = New(32, 32, true);
			for (int y = 0; y < 32; y++)
			{
				for (int x = 0; x < 32; x++)
				{
					float g = Grain(x, y, joints ? 7 : 3) * 2f;
					Color c = new Color(colour.r + g, colour.g + g, colour.b + g);
					if (joints && (x == 0 || y == 0 || x == 16))
					{
						c *= 0.82f;
					}
					c.a = 1f;
					t.SetPixel(x, y, c);
				}
			}
			t.Apply(true);
			return t;
		}

		/// <summary>One parking bay (9 by 16 units) at 4 pixels a unit: asphalt, a white line down each side and across its back.</summary>
		private static Texture2D BayTexture()
		{
			Texture2D t = New(36, 64, true);
			Color line = new Color(0.90f, 0.90f, 0.86f), asphalt = new Color(0.25f, 0.25f, 0.27f);
			for (int y = 0; y < 64; y++)
			{
				for (int x = 0; x < 36; x++)
				{
					float g = Grain(x, y, 3) * 2f;
					Color c = x == 0 || x == 35 || y >= 62 ? line : new Color(asphalt.r + g, asphalt.g + g, asphalt.b + g);
					c.a = 1f;
					t.SetPixel(x, y, c);
				}
			}
			t.Apply(true);
			return t;
		}

		/// <summary>One bay of one storey (8 x 10 pixels): wall with a framed window, brick or stone with a course line.</summary>
		private static Texture2D WallTexture(Color wall, bool courses)
		{
			Texture2D t = New(8, 10);
			Color glass = new Color(0.18f, 0.22f, 0.28f), frame = new Color(0.92f, 0.92f, 0.90f);
			for (int y = 0; y < 10; y++)
			{
				for (int x = 0; x < 8; x++)
				{
					float grain = ((int)(Noise.Mix((ulong)(y * 8 + x) + (ulong)(wall.r * 1000f)) >> 61) - 3.5f) * 0.015f;
					Color c = new Color(wall.r + grain, wall.g + grain, wall.b + grain);
					if (courses && y % 3 == 0)
					{
						c *= 0.92f;
					}
					if (x >= 2 && x <= 5 && y >= 3 && y <= 7)
					{
						c = x == 2 || x == 5 || y == 3 || y == 7 ? frame : glass;
					}
					t.SetPixel(x, y, c);
				}
			}
			t.Apply();
			return t;
		}

		private static Texture2D Planks(Color wood)
		{
			Texture2D t = New(8, 10);
			for (int y = 0; y < 10; y++)
			{
				for (int x = 0; x < 8; x++)
				{
					t.SetPixel(x, y, x % 4 == 0 ? wood * 0.75f : wood);
				}
			}
			t.Apply();
			return t;
		}

		private static Texture2D Rows(Color roof)
		{
			// With mipmaps: a row a unit high breaks into moiré on a big roof seen from afar (a workshop's), as the cladding's ribs did.
			Texture2D t = New(8, 8, true);
			for (int y = 0; y < 8; y++)
			{
				for (int x = 0; x < 8; x++)
				{
					Color c = y % 2 == 0 ? roof * 0.82f : roof;
					c.a = 1f;
					t.SetPixel(x, y, c);
				}
			}
			t.Apply(true);
			return t;
		}

		/// <summary>One bay of a pit garage (8 by 10 pixels): a roller door in slats, a white post at its side, a dark band at the top.</summary>
		private static Texture2D GarageDoors()
		{
			Texture2D t = New(8, 10, true);
			for (int y = 0; y < 10; y++)
			{
				for (int x = 0; x < 8; x++)
				{
					Color c = x == 0 ? new Color(0.90f, 0.90f, 0.88f) : y >= 8 ? new Color(0.20f, 0.22f, 0.26f) : y % 2 == 0 ? new Color(0.50f, 0.53f, 0.56f) : new Color(0.60f, 0.63f, 0.66f);
					c.a = 1f;
					t.SetPixel(x, y, c);
				}
			}
			t.Apply(true);
			return t;
		}

		/// <summary>A stand's rows, as their walls show them (8 by 10 pixels a bay and storey): blue seats on a concrete step.</summary>
		private static Texture2D SeatRows()
		{
			Texture2D t = New(8, 10, true);
			for (int y = 0; y < 10; y++)
			{
				for (int x = 0; x < 8; x++)
				{
					Color c = y < 4 || x % 2 == 1 && y < 6 ? new Color(0.62f, 0.61f, 0.58f) : new Color(0.16f, 0.30f, 0.62f);
					c.a = 1f;
					t.SetPixel(x, y, c);
				}
			}
			t.Apply(true);
			return t;
		}

		/// <summary>8 by 8 units of loose gravel at 4 pixels a unit: speckled stones, a few darker.</summary>
		private static Texture2D GravelTexture()
		{
			Texture2D t = New(32, 32, true);
			for (int y = 0; y < 32; y++)
			{
				for (int x = 0; x < 32; x++)
				{
					float g = Grain(x, y, 11) * 4f;
					bool stone = (Noise.Mix((ulong)(y * 32 + x) + 0x6A11UL) & 7) == 0;
					Color c = new Color(0.62f + g, 0.56f + g, 0.45f + g) * (stone ? 0.8f : 1f);
					c.a = 1f;
					t.SetPixel(x, y, c);
				}
			}
			t.Apply(true);
			return t;
		}

		/// <summary>A straw bale's side, a bale (8 units) across and a storey up: straw in streaks along it, two strings round it.</summary>
		private static Texture2D StrawTexture()
		{
			Texture2D t = New(16, 20);
			for (int y = 0; y < 20; y++)
			{
				for (int x = 0; x < 16; x++)
				{
					float g = ((int)(Noise.Mix((ulong)(y * 7919 + x / 3) + 0x57AAUL) >> 61) - 3.5f) * 0.03f;
					Color c = x == 4 || x == 11 ? new Color(0.55f, 0.45f, 0.22f) : new Color(0.84f + g, 0.72f + g, 0.38f + g);
					c.a = 1f;
					t.SetPixel(x, y, c);
				}
			}
			t.Apply();
			return t;
		}

		/// <summary>An arch's banner, 8 units across and a storey up: chequered edges, a red band with white lettering between.</summary>
		private static Texture2D BannerTexture()
		{
			Texture2D t = New(16, 20);
			for (int y = 0; y < 20; y++)
			{
				for (int x = 0; x < 16; x++)
				{
					Color c;
					if (y < 2 || (y >= 10 && y < 12))
					{
						c = ((x / 2 + y) & 1) == 0 ? new Color(0.1f, 0.1f, 0.1f) : new Color(0.95f, 0.95f, 0.95f);
					}
					else if (y < 10)
					{
						bool text = y >= 4 && y <= 7 && x % 4 != 3 && (Noise.Mix((ulong)(y * 16 + x) + 0xBA77UL) & 3) != 0;
						c = text ? new Color(0.97f, 0.96f, 0.92f) : Brands[0];
					}
					else
					{
						c = new Color(0.12f, 0.12f, 0.14f);
					}
					c.a = 1f;
					t.SetPixel(x, y, c);
				}
			}
			t.Apply();
			return t;
		}

		private static Texture2D Plain(Color c)
		{
			Texture2D t = New(4, 4);
			for (int y = 0; y < 4; y++)
			{
				for (int x = 0; x < 4; x++)
				{
					t.SetPixel(x, y, (x + y) % 3 == 0 ? c * 0.9f : c);
				}
			}
			t.Apply();
			return t;
		}
	}
}
