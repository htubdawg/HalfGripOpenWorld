using System;
using System.Collections.Generic;

namespace HalfGripOpenWorld
{
	/// <summary>
	/// The towns' streets, laid out the way English market towns grew, ring by ring, instead of grown out of every road alike (which
	/// made a starburst of straight rays with thin triangles of land between them):
	///
	///   - the <b>seafront</b>: a town by the sea gets a promenade along the top of its beach first, so the streets coming down to the
	///     shore end on it instead of on the sand;
	///   - the <b>old roads</b>: the main roads through the middle, and where they leave a gap of more than about 115 degrees round
	///     the compass, a gently bending old road out to the fields in it;
	///   - the <b>old town</b> (the core): a back lane behind the plots each side of every main road through it, joined to the main road
	///     by short lanes, so the core is a ladder of long narrow plots (burgage plots) along the high street;
	///   - a <b>ring road</b> of streets about two thirds of the way out, between each pair of old roads, bending round the town;
	///   - the <b>terraces</b>: between the core and the ring road, each wedge between two old roads is a patch of parallel streets
	///     crossed by cross streets, square to one of its old roads (or along the slope where the ground falls), so the town is gridded
	///     in patches, each at its own angle, never one grid over the whole;
	///   - the <b>suburbs</b>: past the ring road, crescents (a loop out and back) and closes (cul-de-sacs) off it, and closes off the
	///     old roads, out to the town's edge (footpaths go on from there, as they do from every dead end).
	///
	/// Every street that ends on another road meets it square (<see cref="SquareEnd"/>): one coming in at a slant bends round over its
	/// last stretch. Deterministic like the rest of the layout: <see cref="Rng"/>, <see cref="DetMath"/>, <see cref="Atan2"/>, and
	/// fixed orders.
	/// </summary>
	internal static partial class Network
	{
		/// <summary>How far out each ring of a town reaches, as a fraction of its radius (wobbled round the compass by noise).</summary>
		private const double CoreReach = 0.3, RingReach = 0.64, TerraceFrom = 0.25;

		/// <summary>A street ending on a road more slanted than this (65 degrees, as a cosine of the angle between them) is bent to meet it square.</summary>
		private const double SquareCos = 0.42261826174069944;

		/// <summary>A street may cross a road of its own patch only at this angle or more (60 degrees, as a cosine).</summary>
		private const double CrossCos = 0.5;

		/// <summary>Step of a laid street's march.</summary>
		private const double LayStep = 20.0;

		private static readonly ulong SaltPlan = Noise.Mix(WorldGen.Seed + 7700);

		/// <summary>Where a laid street ends: on a road (its index, and the point and the road's height there), or not (road -1).</summary>
		private struct End
		{
			public int Road;
			public double X, Z, Y;

			public static End None => new End { Road = -1 };
		}

		/// <summary>Where a road crosses a circle round a town's middle: the angle round it, the road, the point and the road's way there.</summary>
		private struct RingHit
		{
			public double Angle;
			public int Road;
			public double X, Z, Tx, Tz;
		}

		/// <summary>Lays out a town's streets (see the class summary).</summary>
		private static void Plan(Rng rng, Places.Place place)
		{
			double r0 = place.Radius;
			// The roads that bound the wedges: the main roads, and the old roads added below.
			HashSet<int> bounds = new HashSet<int>();
			for (int r = 0; r < Roads.Count; r++)
			{
				if (Roads[r].Kind == RoadKind.Primary || Roads[r].Kind == RoadKind.Secondary)
				{
					bounds.Add(r);
				}
			}

			// The main town's retail park keeps its ground: no street is laid over it.
			if (place == Places.All[0])
			{
				ReserveRetail(place);
			}

			// 1. The seafront.
			Seafront(place);

			// 2. Old roads into the gaps between the main roads.
			List<RingHit> hits = Ring(place, r0 * 0.45, bounds);
			List<double> gaps = new List<double>();
			if (hits.Count == 0)
			{
				double turn = rng.Next() * 2.0 * DetMath.Pi;
				for (int k = 0; k < 4; k++)
				{
					gaps.Add(turn + k * DetMath.Pi * 0.5);
				}
			}
			for (int k = 0; k < hits.Count; k++)
			{
				double a = hits[k].Angle, b = k + 1 < hits.Count ? hits[k + 1].Angle : hits[0].Angle + 2.0 * DetMath.Pi;
				double gap = b - a;
				int count = gap > 2.0 ? Math.Max(1, (int)Math.Floor(gap / 1.6)) : 0;
				for (int c = 1; c <= count; c++)
				{
					// The way with the most room within a third of a share each side of the even spot, nearest it if equal.
					double even = a + gap * c / (count + 1), share = gap / (count + 1);
					double bestAngle = double.NaN, bestScore = r0 * 0.6;
					for (double off = -share * 0.35; off <= share * 0.35 + 1e-9; off += share * 0.07)
					{
						double score = Room(place, even + off) - 150.0 * Math.Abs(off);
						if (score > bestScore)
						{
							bestScore = score;
							bestAngle = even + off;
						}
					}
					if (!double.IsNaN(bestAngle))
					{
						gaps.Add(bestAngle + rng.Range(-0.04, 0.04));
					}
				}
			}
			foreach (double angle in gaps)
			{
				Road old = OldRoad(rng, place, angle);
				if (old != null)
				{
					bounds.Add(Roads.Count - 1);
				}
			}

			// 3. The old town: back lanes behind the high street's plots, joined to it.
			BackLanes(rng, place);

			// 4 and 5. Round each wedge between two bounding roads: the ring road, then the terraces inside it.
			hits = Ring(place, r0 * 0.45, bounds);
			List<int> rings = new List<int>();
			for (int k = 0; k < hits.Count && hits.Count >= 2; k++)
			{
				RingHit a = hits[k], b = hits[(k + 1) % hits.Count];
				double span = b.Angle - a.Angle + (k + 1 == hits.Count ? 2.0 * DetMath.Pi : 0.0);
				if (span < 0.45 || (a.Road == b.Road && span < 1.0))
				{
					continue;
				}
				int ring = RingRoad(place, bounds, a, span);
				if (ring >= 0)
				{
					rings.Add(ring);
				}
				Terraces(rng, place, a, b, span, ring);
			}

			// 6. The suburbs: crescents and closes off the ring roads, closes off the old roads.
			foreach (int ring in rings)
			{
				Estates(rng, place, ring);
			}
			List<int> arms = new List<int>();
			foreach (int r in segments.RoadsNear(place.X, place.Z, r0))
			{
				if (bounds.Contains(r))
				{
					arms.Add(r);
				}
			}
			foreach (int r in arms)
			{
				Closes(rng, place, r);
			}
		}

		// ---- the parts ---------------------------------------------------------------------------------------------

		/// <summary>
		/// The promenade along the top of a town's beach: on rays from its middle every 2 degrees, the first point (from 0.3 of its radius
		/// out) where the land comes down to the beach, 20 back from it; the longest run of rays that found one (at least 20 degrees of
		/// them), smoothed, laid as a street. Roads it meets on the way it crosses (at 60 degrees or more), so streets end on it.
		/// </summary>
		private static void Seafront(Places.Place place)
		{
			const int rays = 180;
			double r0 = place.Radius;
			(double x, double z)?[] shore = new (double x, double z)?[rays];
			for (int k = 0; k < rays; k++)
			{
				double a = k * 2.0 * DetMath.Pi / rays, cx = DetMath.Cos(a), cz = DetMath.Sin(a);
				for (double t = r0 * 0.3; t <= r0 * 1.02; t += 20.0)
				{
					double x = place.X + cx * t, z = place.Z + cz * t;
					if (Beach(x, z, WorldGen.Base(x, z) - 1.5))
					{
						if (t - 20.0 > r0 * 0.3)
						{
							shore[k] = (place.X + cx * (t - 20.0), place.Z + cz * (t - 20.0));
						}
						break;
					}
				}
			}
			// The longest run round the circle (it may wrap past ray 0).
			int bestStart = -1, bestLength = 0;
			for (int k = 0; k < rays; k++)
			{
				if (shore[k] == null || shore[(k + rays - 1) % rays] != null)
				{
					continue;
				}
				int n = 0;
				while (n < rays && shore[(k + n) % rays] != null)
				{
					n++;
				}
				if (n > bestLength)
				{
					bestLength = n;
					bestStart = k;
				}
			}
			if (bestLength < 10 || bestLength >= rays)
			{
				return;
			}
			List<(double x, double z)> want = new List<(double x, double z)>();
			for (int n = 0; n < bestLength; n++)
			{
				double sx = 0.0, sz = 0.0;
				int c = 0;
				for (int m = Math.Max(0, n - 2); m <= Math.Min(bestLength - 1, n + 2); m++)
				{
					(double x, double z) p = shore[(bestStart + m) % rays].Value;
					sx += p.x;
					sz += p.z;
					c++;
				}
				want.Add((sx / c, sz / c));
			}
			want = Smooth(want, 1, 2);
			Both(want, want.Count / 2, place, RoadKind.Street, place.Name + " seafront", r => true, (x, z) => true, 200.0);
		}

		/// <summary>
		/// An old road out of the middle at <paramref name="angle"/> to the town's edge, bending gently (a slow noise across it), ending in
		/// the fields (a footpath may go on from it).
		/// </summary>
		private static Road OldRoad(Rng rng, Places.Place place, double angle)
		{
			double cx = DetMath.Cos(angle), cz = DetMath.Sin(angle);
			double key = rng.Next() * 1000.0;
			double length = place.Radius * rng.Range(0.9, 1.02);
			List<(double x, double z)> want = new List<(double x, double z)> { (place.X, place.Z) };
			for (double t = LayStep; t <= length; t += LayStep)
			{
				double push = Noise.Fbm(SaltPlan, t, key, 700.0, 2, 0.5) * 70.0 * Math.Min(1.0, t / 300.0);
				want.Add((place.X + cx * t - cz * push, place.Z + cz * t + cx * push));
			}
			List<(double x, double z)> pts = Follow(want, -1, true, place, RoadKind.Street, null, null, out End end);
			End start = End.None;
			start.X = place.X;
			start.Z = place.Z;
			if (segments.Snap(place.X, place.Z, 1.0, -1, out double _, out double _, out double sy))
			{
				start.Y = sy;
				start.Road = segments.Hit;
			}
			return Lay(pts, start, end, RoadKind.Street, place.Name + " street", 300.0, true);
		}

		/// <summary>
		/// The old town's back lanes: behind the plots each side of every main road through the core, about 55 m back, a lane running
		/// alongside it from near the middle to the core's edge, then short lanes from the main road to it at its ends and every 45 to
		/// 75 m between. A back lane meeting another road (the next main road's lane, near the middle) ends on it.
		/// </summary>
		private static void BackLanes(Rng rng, Places.Place place)
		{
			double r0 = place.Radius;
			List<int> mains = new List<int>();
			foreach (int r in segments.RoadsNear(place.X, place.Z, 100.0))
			{
				if (Roads[r].Kind == RoadKind.Primary || Roads[r].Kind == RoadKind.Secondary)
				{
					mains.Add(r);
				}
			}
			ReserveMarket(place, mains);
			foreach (int r in mains)
			{
				Road road = Roads[r];
				int k0 = 0;
				double best = double.MaxValue;
				for (int i = 0; i < road.Count; i++)
				{
					double dx = road.X[i] - place.X, dz = road.Z[i] - place.Z;
					if (dx * dx + dz * dz < best)
					{
						best = dx * dx + dz * dz;
						k0 = i;
					}
				}
				if (best > 80.0 * 80.0)
				{
					continue;
				}
				// One arm each way the road leaves the middle.
				for (int dir = -1; dir <= 1; dir += 2)
				{
					if ((dir > 0 && k0 > road.Count - 12) || (dir < 0 && k0 < 11))
					{
						continue;
					}
					for (int side = -1; side <= 1; side += 2)
					{
						double back = rng.Range(170.0, 210.0);
						double reach = r0 * CoreReach * (1.0 + 0.15 * Noise.Fbm(SaltPlan, road.X[k0] + side * 97.0, road.Z[k0] + dir * 53.0, 900.0, 1, 0.5));
						// The offset line, from 110 out along the arm to the core's edge, and on 60 past each end so it reaches a road there.
						List<(double x, double z)> want = new List<(double x, double z)>();
						List<double> along = new List<double>();
						double s = 0.0;
						for (int i = k0; i >= 0 && i < road.Count; i += dir)
						{
							if (i != k0)
							{
								double dx = road.X[i] - road.X[i - dir], dz = road.Z[i] - road.Z[i - dir];
								s += Math.Sqrt(dx * dx + dz * dz);
							}
							if (s < 110.0)
							{
								continue;
							}
							double fx = road.X[i] - place.X, fz = road.Z[i] - place.Z;
							if (fx * fx + fz * fz > reach * reach || road.Bridge[i])
							{
								break;
							}
							int a = Math.Max(0, Math.Min(road.Count - 2, i - (dir > 0 ? 0 : 1)));
							double tx = (road.X[a + 1] - road.X[a]) * dir, tz = (road.Z[a + 1] - road.Z[a]) * dir;
							double len = Math.Sqrt(tx * tx + tz * tz);
							if (len <= 0.0)
							{
								continue;
							}
							// Left of the way the arm runs out is (-tz, tx).
							want.Add((road.X[i] - tz / len * back * side, road.Z[i] + tx / len * back * side));
							along.Add(s);
						}
						if (want.Count < 20)
						{
							continue;
						}
						Extend(want, 60.0);
						Road lane = Both(want, want.Count / 2, place, RoadKind.Lane, place.Name + " back lane", null, null, 150.0);
						if (lane == null)
						{
							continue;
						}
						int laneIndex = Roads.Count - 1;
						// The short lanes from the high street: at the lane's ends and every 150 to 250 between.
						double from = along[0] + 20.0, to = along[along.Count - 1] - 20.0;
						for (double at = from; at <= to + 1.0; at += rng.Range(150.0, 250.0))
						{
							Connector(place, r, road, k0, dir, Math.Min(at, to), side, back + 60.0, laneIndex);
						}
						Connector(place, r, road, k0, dir, to, side, back + 60.0, laneIndex);
					}
				}
			}
		}

		/// <summary>
		/// Keeps ground for the town's market place (laid out later by <see cref="Town"/> as a lot beside the high street), so the short
		/// lanes to the back lanes leave a gap for it: beside the first main road out of the middle, about 64 m out, on the side whose
		/// ground lies nearest the road's height.
		/// </summary>
		private static void ReserveMarket(Places.Place place, List<int> mains)
		{
			Town.Size(LotKind.Market, out double w, out double d, out double _);
			foreach (int r in mains)
			{
				Road road = Roads[r];
				// The end of the road at the middle, and 210 out from it.
				bool atStart = (road.X[0] - place.X) * (road.X[0] - place.X) + (road.Z[0] - place.Z) * (road.Z[0] - place.Z) < 80.0 * 80.0;
				bool atEnd = (road.X[road.Count - 1] - place.X) * (road.X[road.Count - 1] - place.X) + (road.Z[road.Count - 1] - place.Z) * (road.Z[road.Count - 1] - place.Z) < 80.0 * 80.0;
				double length = road.S[road.Count - 1];
				if ((!atStart && !atEnd) || length < 500.0)
				{
					continue;
				}
				foreach (double out0 in new[] { 210.0, 290.0 })
				{
				double s = atStart ? out0 : length - out0;
				PointOn(road, s, out double x, out double z, out double y, out double tx, out double tz);
				int bestSide = 0;
				double best = 5.0;
				for (int side = -1; side <= 1; side += 2)
				{
					double nx = -tz * side, nz = tx * side, off = road.Half + Verge + d * 0.5;
					double rough = 0.0;
					for (int i = -1; i <= 1; i++)
					{
						for (int j = -1; j <= 1; j++)
						{
							double px = x + nx * off + tx * w * 0.4 * i + nx * d * 0.4 * j, pz = z + nz * off + tz * w * 0.4 * i + nz * d * 0.4 * j;
							rough = Math.Max(rough, double.IsNaN(Rivers.SurfaceAt(px, pz)) ? Math.Abs(WorldGen.Base(px, pz) - y) : 99.0);
						}
					}
					if (rough < best)
					{
						best = rough;
						bestSide = side;
					}
				}
				if (bestSide != 0)
				{
					double off = road.Half + Verge + d * 0.5;
					Reserved.Add((x - tz * bestSide * off, z + tx * bestSide * off, Math.Sqrt(w * w + d * d) * 0.5 + 12.0));
					return;
				}
				}
			}
		}

		/// <summary>A short lane square off the arm of road <paramref name="r"/> at <paramref name="at"/> out from the middle, kept only if it reaches the back lane.</summary>
		private static void Connector(Places.Place place, int r, Road road, int k0, int dir, double at, int side, double length, int lane)
		{
			double s = 0.0;
			int i = k0;
			while (i + dir >= 0 && i + dir < road.Count && s < at)
			{
				double dx = road.X[i + dir] - road.X[i], dz = road.Z[i + dir] - road.Z[i];
				s += Math.Sqrt(dx * dx + dz * dz);
				i += dir;
			}
			int a = Math.Max(0, Math.Min(road.Count - 2, i - (dir > 0 ? 1 : 0)));
			double tx = (road.X[a + 1] - road.X[a]) * dir, tz = (road.Z[a + 1] - road.Z[a]) * dir;
			double len = Math.Sqrt(tx * tx + tz * tz);
			if (len <= 0.0 || road.Bridge[i])
			{
				return;
			}
			double nx = -tz / len * side, nz = tx / len * side;
			// Not where another road already leaves the arm close by on this side.
			if (segments.Snap(road.X[i] + nx * (road.Half + 25.0), road.Z[i] + nz * (road.Half + 25.0), 30.0, r, out double _, out double _, out double _))
			{
				return;
			}
			List<(double x, double z)> want = new List<(double x, double z)> { (road.X[i], road.Z[i]), (road.X[i] + nx * length, road.Z[i] + nz * length) };
			List<(double x, double z)> pts = Follow(Lines.Resample(want, LayStep), r, false, place, RoadKind.Lane, null, null, out End end);
			if (end.Road != lane)
			{
				return;
			}
			End start = new End { Road = r, X = road.X[i], Z = road.Z[i], Y = road.Y[i] };
			Lay(pts, start, end, RoadKind.Lane, place.Name + " lane", 60.0, false);
		}

		/// <summary>
		/// The ring road across a wedge: from where road <paramref name="a"/> crosses the ring (about two thirds out) round to road
		/// <paramref name="b"/>, its distance from the middle wobbled by a slow noise. Where that way is blocked early (a junction right at
		/// the start, water, the beach), a ring a little further in or out: the first that reaches a road, else the longest that runs at
		/// least a fifth of the way. The road's index, or -1.
		/// </summary>
		private static int RingRoad(Places.Place place, HashSet<int> bounds, RingHit a, double span)
		{
			List<(double x, double z)> bestPts = null;
			End bestEnd = End.None, bestFrom = End.None;
			double bestShare = 0.2;
			foreach (double f in new[] { RingReach, RingReach - 0.06, RingReach + 0.06, RingReach - 0.12 })
			{
				double rho = place.Radius * f;
				List<RingHit> outer = Ring(place, rho, bounds);
				// Where road a crosses this ring (not where it ends short of it), nearest the wedge's start.
				int pick = -1;
				double best = double.MaxValue;
				for (int k = 0; k < outer.Count; k++)
				{
					double d = Turn(outer[k].Angle - a.Angle);
					double ex = outer[k].X - place.X, ez = outer[k].Z - place.Z;
					if (outer[k].Road == a.Road && Math.Abs(d) < best && Math.Abs(d) < 0.5 && Math.Abs(Math.Sqrt(ex * ex + ez * ez) - rho) < 1.0)
					{
						best = Math.Abs(d);
						pick = k;
					}
				}
				if (pick < 0)
				{
					continue;
				}
				RingHit start = outer[pick];
				Road road = Roads[start.Road];
				if (!segments.Nearest(start.Road, start.X, start.Z, 20.0, out double _, out int seg, out double t0))
				{
					continue;
				}
				End from = new End { Road = start.Road, X = start.X, Z = start.Z, Y = road.Y[seg] + (road.Y[seg + 1] - road.Y[seg]) * t0 };
				double a0 = start.Angle, a1 = a.Angle + span + 0.15;
				List<(double x, double z)> want = new List<(double x, double z)> { (start.X, start.Z) };
				double step = LayStep / rho;
				for (double t = a0 + step; t <= a1; t += step)
				{
					double rr = rho * (1.0 + 0.06 * Noise.Fbm(SaltPlan, DetMath.Cos(t) * 2000.0 + place.X, DetMath.Sin(t) * 2000.0 + place.Z, 1500.0, 2, 0.5));
					want.Add((place.X + DetMath.Cos(t) * rr, place.Z + DetMath.Sin(t) * rr));
				}
				List<(double x, double z)> pts = Follow(want, start.Road, false, place, RoadKind.Street, null, null, out End end);
				double share = (double)pts.Count / want.Count;
				if (end.Road >= 0 && end.Road != start.Road && pts.Count > 15)
				{
					bestPts = pts;
					bestEnd = end;
					bestFrom = from;
					break;
				}
				if (share > bestShare)
				{
					bestShare = share;
					bestPts = pts;
					bestEnd = end;
					bestFrom = from;
				}
			}
			if (bestPts == null)
			{
				return -1;
			}
			Road ring = Lay(bestPts, bestFrom, bestEnd, RoadKind.Street, place.Name + " street", 300.0, false);
			return ring != null ? Roads.Count - 1 : -1;
		}

		/// <summary>
		/// The terraces in the wedge from road <paramref name="a"/> round to road <paramref name="b"/>: parallel streets 52 to 64 m apart
		/// (a terrace and its yard each side of the block) and cross streets 105 to 150 m apart, square to one of the two roads, or along
		/// the slope if the ground falls more than 1 in 25 across the wedge; between the core and the ring road. Then an alley down the
		/// middle of about half the blocks, a lane or a footpath.
		/// </summary>
		private static void Terraces(Rng rng, Places.Place place, RingHit a, RingHit b, double span, int ring)
		{
			double r0 = place.Radius;
			double mid = a.Angle + span * 0.5;
			double mx = place.X + DetMath.Cos(mid) * r0 * 0.45, mz = place.Z + DetMath.Sin(mid) * r0 * 0.45;
			double gx = (WorldGen.Base(mx + 150.0, mz) - WorldGen.Base(mx - 150.0, mz)) / 300.0;
			double gz = (WorldGen.Base(mx, mz + 150.0) - WorldGen.Base(mx, mz - 150.0)) / 300.0;
			double slope = Math.Sqrt(gx * gx + gz * gz);
			double ux, uz, ox, oz;
			double pick = rng.Next();
			if (slope > 0.04)
			{
				// Along the slope: the long streets on the level, the cross streets up and down it.
				ux = -gz / slope;
				uz = gx / slope;
				ox = mx;
				oz = mz;
			}
			else if (pick < 0.5)
			{
				Unit(a.Tx, a.Tz, out ux, out uz);
				ox = a.X;
				oz = a.Z;
			}
			else
			{
				Unit(b.Tx, b.Tz, out ux, out uz);
				ox = b.X;
				oz = b.Z;
			}
			// Across the long streets, into the wedge.
			double nx = -uz, nz = ux;
			if ((mx - ox) * nx + (mz - oz) * nz < 0.0)
			{
				nx = -nx;
				nz = -nz;
			}
			double inner = r0 * TerraceFrom, outerR = r0 * RingReach * 1.1;
			Func<double, double, bool> inside = (x, z) =>
			{
				double dx = x - place.X, dz = z - place.Z;
				double d = Math.Sqrt(dx * dx + dz * dz);
				if (d < inner || d > outerR)
				{
					return false;
				}
				double turn = Turn(Atan2(dz, dx) - a.Angle);
				if (turn < -0.05)
				{
					turn += 2.0 * DetMath.Pi;
				}
				return turn < span + 0.05;
			};
			bool centred = slope > 0.04;
			string name = place.Name + " street";
			HashSet<int> longs = new HashSet<int>();
			List<double> offsets = new List<double>();
			// Long streets, out from the origin's line (both ways if it is in the middle of the wedge).
			for (int way = centred ? -1 : 1; way <= 1; way += 2)
			{
				double v = centred && way > 0 ? rng.Range(80.0, 110.0) : 0.0;
				int misses = 0;
				while (misses < 3 && Math.Abs(v) < 2.2 * r0)
				{
					v += way * rng.Range(170.0, 210.0);
					double tilt = rng.Range(-0.025, 0.025);
					double lx = ux * DetMath.Cos(tilt) - uz * DetMath.Sin(tilt), lz = uz * DetMath.Cos(tilt) + ux * DetMath.Sin(tilt);
					Road made = Line(place, ox + nx * v, oz + nz * v, lx, lz, inside, null, RoadKind.Street, name, v);
					if (made == null)
					{
						misses++;
						continue;
					}
					misses = 0;
					longs.Add(Roads.Count - 1);
					offsets.Add(v);
				}
			}
			if (longs.Count == 0)
			{
				return;
			}
			// Cross streets, both ways along the long streets from the origin.
			List<double> crosses = new List<double>();
			for (int way = -1; way <= 1; way += 2)
			{
				double u = way > 0 ? rng.Range(-60.0, 60.0) : 0.0;
				int misses = 0;
				bool first = true;
				while (misses < 3 && Math.Abs(u) < 2.2 * r0)
				{
					u += first && way > 0 ? 0.0 : way * rng.Range(350.0, 500.0);
					first = false;
					double tilt = rng.Range(-0.025, 0.025);
					double lx = nx * DetMath.Cos(tilt) - nz * DetMath.Sin(tilt), lz = nz * DetMath.Cos(tilt) + nx * DetMath.Sin(tilt);
					Road made = Line(place, ox + ux * u, oz + uz * u, lx, lz, inside, longs, RoadKind.Street, name, u + 5000.0);
					if (made == null)
					{
						misses++;
						continue;
					}
					misses = 0;
					crosses.Add(u);
				}
			}
			// Alleys down the middle of about half the blocks: between neighbouring long streets, between neighbouring cross streets.
			offsets.Sort();
			crosses.Sort();
			for (int i = 0; i + 1 < offsets.Count; i++)
			{
				if (offsets[i + 1] - offsets[i] > 240.0)
				{
					continue;
				}
				double v = (offsets[i] + offsets[i + 1]) * 0.5;
				for (int j = 0; j + 1 < crosses.Count; j++)
				{
					if (rng.Next() >= 0.5)
					{
						continue;
					}
					bool lane = rng.Next() < 0.5;
					double u = (crosses[j] + crosses[j + 1]) * 0.5;
					double cx = ox + nx * v + ux * u, cz = oz + nz * v + uz * u;
					if (!inside(cx, cz) || segments.Snap(cx, cz, 50.0, -1, out double _, out double _, out double _))
					{
						continue;
					}
					List<(double x, double z)> want = new List<(double x, double z)>();
					double half = (crosses[j + 1] - crosses[j]) * 0.5 + 60.0;
					for (double t = -half; t <= half + 0.1; t += LayStep)
					{
						want.Add((cx + ux * t, cz + uz * t));
					}
					RoadKind kind = lane ? RoadKind.Lane : RoadKind.Path;
					Both(want, want.Count / 2, place, kind, lane ? place.Name + " back lane" : "Footpath", null, inside, 120.0, true);
				}
			}
		}

		/// <summary>
		/// One straight street of a grid through (x, z) along (ux, uz): the longest run of the line inside the patch (out of the water), from
		/// its middle out both ways (<see cref="Both"/>). It may cross the roads in <paramref name="crossable"/>.
		/// </summary>
		private static Road Line(Places.Place place, double x, double z, double ux, double uz, Func<double, double, bool> inside, HashSet<int> crossable, RoadKind kind, string name, double key)
		{
			double r0 = place.Radius;
			int bestA = 0, bestB = -1, runA = 0;
			bool inRun = false;
			int steps = (int)(4.4 * r0 / LayStep);
			for (int k = 0; k <= steps; k++)
			{
				double t = -2.2 * r0 + k * LayStep;
				double px = x + ux * t, pz = z + uz * t;
				bool ok = inside(px, pz) && double.IsNaN(Rivers.SurfaceAt(px, pz)) && !Beach(px, pz, WorldGen.Base(px, pz));
				if (ok && !inRun)
				{
					runA = k;
					inRun = true;
				}
				if ((!ok || k == steps) && inRun)
				{
					int runB = ok ? k : k - 1;
					if (runB - runA > bestB - bestA)
					{
						bestA = runA;
						bestB = runB;
					}
					inRun = false;
				}
			}
			if ((bestB - bestA) * LayStep < 140.0)
			{
				return null;
			}
			// The line from 80 before the run to 80 past it, bent a little by a slow noise across it, and its middle clear of roads.
			double nx = -uz, nz = ux;
			List<(double x, double z)> want = new List<(double x, double z)>();
			for (double t = -2.2 * r0 + bestA * LayStep - 80.0; t <= -2.2 * r0 + bestB * LayStep + 80.0; t += LayStep)
			{
				double push = Noise.Fbm(SaltPlan, t, key, 500.0, 2, 0.5) * 10.0;
				want.Add((x + ux * t + nx * push, z + uz * t + nz * push));
			}
			int middle = want.Count / 2;
			for (int tries = 0; tries < 6; tries++)
			{
				int m = middle + (tries % 2 == 0 ? 1 : -1) * ((tries + 1) / 2) * 2;
				if (m > 0 && m < want.Count - 1 && !segments.Snap(want[m].x, want[m].z, 30.0, -1, out double _, out double _, out double _))
				{
					return Both(want, m, place, kind, name, crossable == null ? null : r => crossable.Contains(r), inside, 140.0);
				}
			}
			return null;
		}

		/// <summary>
		/// The estates off a ring road, on its outer side: crescents (out about 50 m, along, and back to it) and closes (dead ends 30 to
		/// 70 m long, bending a little), one after another along it with gaps between.
		/// </summary>
		private static void Estates(Rng rng, Places.Place place, int ring)
		{
			Road road = Roads[ring];
			double length = road.S[road.Count - 1];
			double s = rng.Range(60.0, 140.0);
			while (s < length - 80.0)
			{
				if (rng.Next() < 0.5 && s + 260.0 < length - 60.0)
				{
					double w = rng.Range(220.0, 380.0);
					w = Math.Min(w, length - 60.0 - s);
					Crescent(rng, place, ring, s, s + w, rng.Range(140.0, 200.0));
					s += w + rng.Range(70.0, 140.0);
				}
				else
				{
					Close(rng, place, ring, s, Outward(place, road, s), rng.Range(100.0, 230.0));
					s += rng.Range(120.0, 200.0);
				}
			}
		}

		/// <summary>Closes off both sides of an old road (or main road) where it runs through the suburbs, 50 to 80 m apart, about half taken.</summary>
		private static void Closes(Rng rng, Places.Place place, int r)
		{
			Road road = Roads[r];
			double next = rng.Range(80.0, 200.0);
			for (int i = 2; i < road.Count - 2; i++)
			{
				if (road.S[i] < next || road.Bridge[i])
				{
					continue;
				}
				double dx = road.X[i] - place.X, dz = road.Z[i] - place.Z;
				double from = Math.Sqrt(dx * dx + dz * dz) / place.Radius;
				if (from < 0.68 || from > 0.95)
				{
					continue;
				}
				next = road.S[i] + rng.Range(160.0, 260.0);
				for (int side = -1; side <= 1; side += 2)
				{
					if (rng.Next() < 0.5)
					{
						Close(rng, place, r, road.S[i], side, rng.Range(90.0, 200.0));
					}
				}
			}
		}

		/// <summary>Which side of road <paramref name="road"/> at <paramref name="s"/> faces away from the town's middle: 1 its left, -1 its right.</summary>
		private static int Outward(Places.Place place, Road road, double s)
		{
			Locate(road, s, out int i, out double _);
			double tx = road.X[i + 1] - road.X[i], tz = road.Z[i + 1] - road.Z[i];
			return (road.X[i] - place.X) * -tz + (road.Z[i] - place.Z) * tx >= 0.0 ? 1 : -1;
		}

		/// <summary>The point at <paramref name="s"/> along road <paramref name="r"/>, its height, and its unit way.</summary>
		private static void PointOn(Road road, double s, out double x, out double z, out double y, out double tx, out double tz)
		{
			Locate(road, s, out int i, out double t);
			x = road.X[i] + (road.X[i + 1] - road.X[i]) * t;
			z = road.Z[i] + (road.Z[i + 1] - road.Z[i]) * t;
			y = road.Y[i] + (road.Y[i + 1] - road.Y[i]) * t;
			Unit(road.X[i + 1] - road.X[i], road.Z[i + 1] - road.Z[i], out tx, out tz);
		}

		/// <summary>A crescent off road <paramref name="r"/>: out from it at <paramref name="s0"/>, round <paramref name="depth"/> out, and back to it at <paramref name="s1"/>.</summary>
		private static void Crescent(Rng rng, Places.Place place, int r, double s0, double s1, double depth)
		{
			Road road = Roads[r];
			int side = Outward(place, road, (s0 + s1) * 0.5);
			PointOn(road, s0, out double x0, out double z0, out double y0, out double _, out double _);
			List<(double x, double z)> want = new List<(double x, double z)> { (x0, z0) };
			for (double s = s0; s <= s1 + 0.1; s += 30.0)
			{
				PointOn(road, s, out double x, out double z, out double _, out double tx, out double tz);
				double f = Math.Min(1.0, Math.Min(s - s0, s1 - s) / 60.0 + 0.35);
				want.Add((x - tz * side * depth * f, z + tx * side * depth * f));
			}
			PointOn(road, s1, out double x1, out double z1, out double _, out double _, out double _);
			want.Add((x1, z1));
			want = Smooth(want, 1, 3);
			List<(double x, double z)> pts = Follow(want, r, false, place, RoadKind.Street, null, null, out End end);
			if (end.Road < 0)
			{
				return;
			}
			End start = new End { Road = r, X = x0, Z = z0, Y = y0 };
			Lay(pts, start, end, RoadKind.Street, place.Name + " street", 200.0, false);
		}

		/// <summary>A close off road <paramref name="r"/> at <paramref name="s"/> to its <paramref name="side"/>: a dead end bending a little, <paramref name="length"/> long.</summary>
		private static void Close(Rng rng, Places.Place place, int r, double s, int side, double length)
		{
			Road road = Roads[r];
			PointOn(road, s, out double x, out double z, out double y, out double tx, out double tz);
			double angle = Atan2(tx * side, -tz * side);
			double bend = rng.Range(-0.025, 0.025);
			List<(double x, double z)> want = new List<(double x, double z)> { (x, z) };
			double px = x, pz = z;
			for (double t = LayStep; t <= length; t += LayStep)
			{
				px += DetMath.Cos(angle) * LayStep;
				pz += DetMath.Sin(angle) * LayStep;
				want.Add((px, pz));
				angle += bend;
			}
			// Not where another road already leaves close by on this side.
			double nx = DetMath.Cos(Atan2(tx * side, -tz * side)), nz = DetMath.Sin(Atan2(tx * side, -tz * side));
			if (segments.Snap(x + nx * (road.Half + 25.0), z + nz * (road.Half + 25.0), 35.0, r, out double _, out double _, out double _))
			{
				return;
			}
			Func<double, double, bool> inside = (qx, qz) => (qx - place.X) * (qx - place.X) + (qz - place.Z) * (qz - place.Z) < place.Radius * place.Radius * 0.96;
			List<(double x, double z)> pts = Follow(want, r, false, place, RoadKind.Street, null, inside, out End end);
			End start = new End { Road = r, X = x, Z = z, Y = y };
			Lay(pts, start, end, RoadKind.Street, place.Name + " close", 70.0, false);
		}

		/// <summary>Ground kept clear of the towns' streets (the retail park's), as discs.</summary>
		private static readonly List<(double x, double z, double r)> Reserved = new List<(double x, double z, double r)>();

		/// <summary>Whether (x, z) is on ground kept for a lot (the parks keep off it too).</summary>
		internal static bool Kept(double x, double z)
		{
			foreach ((double rx, double rz, double rr) in Reserved)
			{
				if ((x - rx) * (x - rx) + (z - rz) * (z - rz) < rr * rr)
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>
		/// Keeps ground for the main town's retail park (laid out later by <see cref="Town"/>, which needs a big lot clear of every road
		/// beside a main road at the town's edge): the first spot along a main road, 0.7 to 0.95 of the way out, where a lot of its size
		/// would stand clear of other roads, out of the water, on ground near the road's height, beside a road that runs about straight
		/// and level past it.
		/// </summary>
		private static void ReserveRetail(Places.Place place)
		{
			Town.Size(LotKind.Retail, out double w, out double d, out double _);
			for (int r = 0; r < Roads.Count; r++)
			{
				Road road = Roads[r];
				if (road.Kind != RoadKind.Primary && road.Kind != RoadKind.Secondary)
				{
					continue;
				}
				double length = road.S[road.Count - 1];
				for (double s = w; s < length - w; s += 40.0)
				{
					PointOn(road, s, out double x, out double z, out double y, out double tx, out double tz);
					double from = Math.Sqrt((x - place.X) * (x - place.X) + (z - place.Z) * (z - place.Z)) / place.Radius;
					if (from < 0.7 || from > 0.95)
					{
						continue;
					}
					PointOn(road, s - w * 0.5, out double _, out double _, out double y0, out double _, out double _);
					PointOn(road, s + w * 0.5, out double _, out double _, out double y1, out double _, out double _);
					PointOn(road, s - w * 0.5, out double ax, out double az, out double _, out double _, out double _);
					PointOn(road, s + w * 0.5, out double bx, out double bz, out double _, out double _, out double _);
					// About straight (the chord near the road's length) and level along it.
					if (Math.Abs(y1 - y0) / w > 0.06 || Math.Abs(y - (y0 + y1) * 0.5) > 0.3 || Math.Sqrt((bx - ax) * (bx - ax) + (bz - az) * (bz - az)) < w * 0.98)
					{
						continue;
					}
					for (int side = -1; side <= 1; side += 2)
					{
						double nx = -tz * side, nz = tx * side;
						double off = road.Half + Verge + d * 0.5;
						double cx = x + nx * off, cz = z + nz * off;
						double reach = Math.Sqrt(w * w + d * d) * 0.5 + 25.0;
						if (segments.Snap(cx, cz, reach, r, out double _, out double _, out double _))
						{
							continue;
						}
						bool ok = true;
						for (int i = -2; i <= 2 && ok; i++)
						{
							for (int j = -2; j <= 2 && ok; j++)
							{
								double px = cx + tx * w * 0.25 * i + nx * d * 0.25 * j, pz = cz + tz * w * 0.25 * i + nz * d * 0.25 * j;
								double g = WorldGen.Base(px, pz);
								ok = Math.Abs(g - y) < 7.0 && double.IsNaN(Rivers.SurfaceAt(px, pz)) && Rivers.Distance(px, pz, out double plain) > plain + 30.0 && !Beach(px, pz, g) && !Kept(px, pz);
							}
						}
						if (ok)
						{
							Reserved.Add((cx, cz, reach));
							return;
						}
					}
				}
			}
		}

		// ---- laying a street ---------------------------------------------------------------------------------------

		/// <summary>
		/// Lays a street along <paramref name="want"/> out both ways from its point <paramref name="middle"/> (<see cref="Follow"/> each
		/// way), so it runs until it meets a road or its rules stop it at both ends; then <see cref="Lay"/>.
		/// </summary>
		private static Road Both(List<(double x, double z)> want, int middle, Places.Place place, RoadKind kind, string name, Func<int, bool> crossable, Func<double, double, bool> inside, double least, bool bothOnRoads = false)
		{
			if (middle <= 0 || middle >= want.Count - 1)
			{
				return null;
			}
			List<(double x, double z)> ahead = want.GetRange(middle, want.Count - middle);
			List<(double x, double z)> behind = want.GetRange(0, middle + 1);
			behind.Reverse();
			List<(double x, double z)> a = Follow(behind, -1, false, place, kind, crossable, inside, out End endA);
			List<(double x, double z)> b = Follow(ahead, -1, false, place, kind, crossable, inside, out End endB);
			if (bothOnRoads && (endA.Road < 0 || endB.Road < 0))
			{
				return null;
			}
			a.Reverse();
			a.AddRange(b.GetRange(1, b.Count - 1));
			return Lay(a, endA, endB, kind, name, least, false);
		}

		/// <summary>
		/// Marches along <paramref name="want"/> from its first point (on road <paramref name="from"/>, or -1), by the local rules of
		/// <see cref="Walk"/>: it stops before leaving the place or <paramref name="inside"/>, at water, a river's floodplain, the shore,
		/// a course's fence, or a step steeper than its class; it ends on a road it would cross (but crosses one in
		/// <paramref name="crossable"/> at 60 degrees or more) or joins one it comes within 40 of. With <paramref name="free"/> it starts
		/// at a point where roads meet (a town's middle) and pays no heed to roads for its first steps. Ending on the trunk road (a dual
		/// carriageway takes no streets) it stops 60 short instead.
		/// </summary>
		private static List<(double x, double z)> Follow(List<(double x, double z)> want, int from, bool free, Places.Place place, RoadKind kind, Func<int, bool> crossable, Func<double, double, bool> inside, out End end)
		{
			List<(double x, double z)> line = Lines.Resample(want, LayStep);
			List<(double x, double z)> pts = new List<(double x, double z)> { line[0] };
			end = End.None;
			double x = line[0].x, z = line[0].z;
			double h = WorldGen.Base(x, z);
			double grade = Classes[(int)kind].Grade;
			for (int k = 1; k < line.Count; k++)
			{
				(double nx, double nz) = line[k];
				double sx0 = nx - x, sz0 = nz - z;
				double step = Math.Sqrt(sx0 * sx0 + sz0 * sz0);
				if (step <= 1e-6)
				{
					continue;
				}
				bool first = pts.Count < (free ? 4 : 3);
				if (inside != null && !inside(nx, nz))
				{
					break;
				}
				if (place != null)
				{
					double dx = nx - place.X, dz = nz - place.Z;
					if (dx * dx + dz * dz > place.Radius * place.Radius * 1.1)
					{
						break;
					}
				}
				if (Kept(nx, nz) || !double.IsNaN(Rivers.SurfaceAt(nx, nz)) || Rivers.Distance(nx, nz, out double plain) < plain * 0.5 + 25.0 || Circuit.Fenced(nx, nz, 40.0) || Rally.Fenced(nx, nz, 60.0) || HillClimb.Fenced(nx, nz, HillClimb.Fence))
				{
					break;
				}
				double nh = WorldGen.Base(nx, nz);
				if (Beach(nx, nz, nh) || Math.Abs(nh - h) / step > grade * 1.3)
				{
					break;
				}
				if (!(first && free) && segments.Intersect(x, z, nx, nz, first ? from : -1, out double ix, out double iz, out double iy))
				{
					int hit = segments.Hit;
					if (crossable == null || !crossable(hit) || !Crosses(hit, ix, iz, sx0 / step, sz0 / step))
					{
						pts.Add((ix, iz));
						end = new End { Road = hit, X = ix, Z = iz, Y = iy };
						break;
					}
				}
				if (!first && SnapExcept(nx, nz, 40.0, crossable, out double px, out double pz, out double py, out int snapped))
				{
					pts.Add((px, pz));
					end = new End { Road = snapped, X = px, Z = pz, Y = py };
					break;
				}
				pts.Add((nx, nz));
				x = nx;
				z = nz;
				h = nh;
			}
			if (end.Road >= 0 && Roads[end.Road].Kind == RoadKind.Trunk)
			{
				Trim(pts, 60.0);
				end = End.None;
			}
			return pts;
		}

		/// <summary>Whether a street going (dx, dz) where it meets road <paramref name="r"/> at (x, z) crosses it at 60 degrees or more.</summary>
		private static bool Crosses(int r, double x, double z, double dx, double dz)
		{
			if (!segments.Nearest(r, x, z, 10.0, out double _, out int seg, out double _))
			{
				return false;
			}
			Road road = Roads[r];
			Unit(road.X[seg + 1] - road.X[seg], road.Z[seg + 1] - road.Z[seg], out double tx, out double tz);
			return Math.Abs(dx * tx + dz * tz) <= CrossCos;
		}

		/// <summary>The nearest point within <paramref name="radius"/> on a road not in <paramref name="skip"/>, its height and the road.</summary>
		private static bool SnapExcept(double x, double z, double radius, Func<int, bool> skip, out double px, out double pz, out double py, out int road)
		{
			px = pz = py = 0.0;
			road = -1;
			double best = radius;
			foreach (int r in segments.RoadsNear(x, z, radius))
			{
				if (skip != null && skip(r))
				{
					continue;
				}
				if (segments.Nearest(r, x, z, radius, out double d, out int seg, out double t) && d < best)
				{
					Road o = Roads[r];
					best = d;
					px = o.X[seg] + (o.X[seg + 1] - o.X[seg]) * t;
					pz = o.Z[seg] + (o.Z[seg + 1] - o.Z[seg]) * t;
					py = o.Y[seg] + (o.Y[seg + 1] - o.Y[seg]) * t;
					road = r;
				}
			}
			return road >= 0;
		}

		/// <summary>
		/// Makes a street of a marched line: both ends brought to meet the roads they end on square (<see cref="SquareEnd"/>), corners
		/// rounded, resampled, heights pinned at its ends (to the roads', or the land's at a dead end) and where it crosses roads, and added
		/// to the network, if it came to <paramref name="least"/> or more. With <paramref name="hub"/> its start is where several roads
		/// end (a town's middle), and is left as it is.
		/// </summary>
		private static Road Lay(List<(double x, double z)> pts, End start, End end, RoadKind kind, string name, double least, bool hub)
		{
			if (pts.Count < 2)
			{
				return null;
			}
			if (!hub)
			{
				pts.Reverse();
				SquareEnd(pts, ref start);
				pts.Reverse();
			}
			SquareEnd(pts, ref end);
			if (pts.Count < 2)
			{
				return null;
			}
			Road road = Make(kind, name, Lines.Resample(Rounded(pts), Step));
			if (road.Count < 3 || road.S[road.Count - 1] < least)
			{
				return null;
			}
			int last = road.Count - 1;
			List<(int idx, double y)> pins = new List<(int, double)>
			{
				(0, start.Road >= 0 ? start.Y : WorldGen.Base(road.X[0], road.Z[0])),
				(last, end.Road >= 0 ? end.Y : WorldGen.Base(road.X[last], road.Z[last])),
			};
			// Ends pinned to roads at very different heights must be within the class's grade of each other along it.
			if (start.Road >= 0 && end.Road >= 0 && Math.Abs(end.Y - start.Y) > Classes[(int)kind].Grade * 0.9 * road.S[last])
			{
				return null;
			}
			Add(road, pins);
			return road;
		}

		/// <summary>
		/// Brings a street that ends on a road at a slant (more than 25 degrees off square) round to meet it square: from the last point
		/// of its line 45 or more from that road, a curve (a cubic leaving along the street, arriving along the road's normal) to the
		/// point of the road nearest it. If no such point is within 220 back, or the curve would cross another road, the street instead
		/// stops 60 short of the road, a dead end. Changes <paramref name="end"/> to the new end.
		/// </summary>
		private static void SquareEnd(List<(double x, double z)> pts, ref End end)
		{
			int n = pts.Count;
			if (end.Road < 0 || n < 3)
			{
				return;
			}
			Road road = Roads[end.Road];
			if (!segments.Nearest(end.Road, end.X, end.Z, 30.0, out double _, out int k, out double _))
			{
				return;
			}
			Unit(road.X[k + 1] - road.X[k], road.Z[k + 1] - road.Z[k], out double tx, out double tz);
			// The way the street comes in over its last 30 or so.
			int j = n - 2;
			while (j > 0 && Dist(pts[j], pts[n - 1]) < 30.0)
			{
				j--;
			}
			Unit(pts[n - 1].x - pts[j].x, pts[n - 1].z - pts[j].z, out double ax, out double az);
			if (Math.Abs(ax * tx + az * tz) <= SquareCos)
			{
				return;
			}
			// Back along the street to a point 45 or more from the road.
			double back = 0.0, d = 0.0;
			int p = n - 1;
			int seg = 0;
			double along = 0.0;
			while (p > 0)
			{
				back += Dist(pts[p], pts[p - 1]);
				p--;
				if (back > 220.0)
				{
					break;
				}
				if (segments.Nearest(end.Road, pts[p].x, pts[p].z, 400.0, out d, out seg, out along) && d >= 45.0)
				{
					break;
				}
			}
			bool ok = back <= 220.0 && d >= 45.0 && p > 0;
			List<(double x, double z)> curve = new List<(double x, double z)>();
			double ex = 0.0, ez = 0.0, ey = 0.0;
			if (ok)
			{
				ex = road.X[seg] + (road.X[seg + 1] - road.X[seg]) * along;
				ez = road.Z[seg] + (road.Z[seg + 1] - road.Z[seg]) * along;
				ey = road.Y[seg] + (road.Y[seg + 1] - road.Y[seg]) * along;
				(double x, double z) P = pts[p];
				Unit(P.x - pts[p - 1].x, P.z - pts[p - 1].z, out double hx, out double hz);
				Unit(P.x - ex, P.z - ez, out double qx, out double qz);
				double dist = Dist(P, (ex, ez));
				double c1x = P.x + hx * dist * 0.45, c1z = P.z + hz * dist * 0.45;
				double c2x = ex + qx * dist * 0.45, c2z = ez + qz * dist * 0.45;
				for (int m = 1; m <= 12; m++)
				{
					double t = m / 12.0, s = 1.0 - t;
					curve.Add((s * s * s * P.x + 3.0 * s * s * t * c1x + 3.0 * s * t * t * c2x + t * t * t * ex, s * s * s * P.z + 3.0 * s * s * t * c1z + 3.0 * s * t * t * c2z + t * t * t * ez));
				}
				// It must not cross another road on the way, nor this one before its end.
				(double x, double z) prev = P;
				for (int m = 0; m < curve.Count - 1 && ok; m++)
				{
					if (segments.Intersect(prev.x, prev.z, curve[m].x, curve[m].z, -1, out double _, out double _, out double _))
					{
						ok = false;
					}
					prev = curve[m];
				}
			}
			if (ok)
			{
				pts.RemoveRange(p + 1, n - p - 1);
				pts.AddRange(curve);
				end.X = ex;
				end.Z = ez;
				end.Y = ey;
				return;
			}
			Trim(pts, 60.0);
			end = End.None;
		}

		/// <summary>Cuts <paramref name="length"/> off the end of a line (all of it if shorter).</summary>
		private static void Trim(List<(double x, double z)> pts, double length)
		{
			double cut = 0.0;
			while (pts.Count > 1 && cut < length)
			{
				cut += Dist(pts[pts.Count - 1], pts[pts.Count - 2]);
				pts.RemoveAt(pts.Count - 1);
			}
		}

		/// <summary>Carries a line on straight past both its ends by <paramref name="length"/>.</summary>
		private static void Extend(List<(double x, double z)> pts, double length)
		{
			int n = pts.Count;
			Unit(pts[n - 1].x - pts[n - 2].x, pts[n - 1].z - pts[n - 2].z, out double ex, out double ez);
			Unit(pts[0].x - pts[1].x, pts[0].z - pts[1].z, out double sx, out double sz);
			for (double t = LayStep; t <= length; t += LayStep)
			{
				pts.Add((pts[n - 1].x + ex * t, pts[n - 1].z + ez * t));
			}
			List<(double x, double z)> head = new List<(double x, double z)>();
			for (double t = length; t >= LayStep - 0.01; t -= LayStep)
			{
				head.Add((pts[0].x + sx * t, pts[0].z + sz * t));
			}
			pts.InsertRange(0, head);
		}

		/// <summary>
		/// Where the given roads cross a circle of radius <paramref name="rho"/> round the town's middle, in order round it (by angle, then
		/// road); crossings within 0.12 radians of one before them dropped.
		/// </summary>
		private static List<RingHit> Ring(Places.Place place, double rho, HashSet<int> roads)
		{
			List<RingHit> hits = new List<RingHit>();
			foreach (int r in segments.RoadsNear(place.X, place.Z, rho + 40.0))
			{
				if (!roads.Contains(r))
				{
					continue;
				}
				Road road = Roads[r];
				bool crossed = false;
				for (int i = 0; i + 1 < road.Count; i++)
				{
					double d0 = Math.Sqrt((road.X[i] - place.X) * (road.X[i] - place.X) + (road.Z[i] - place.Z) * (road.Z[i] - place.Z)) - rho;
					double d1 = Math.Sqrt((road.X[i + 1] - place.X) * (road.X[i + 1] - place.X) + (road.Z[i + 1] - place.Z) * (road.Z[i + 1] - place.Z)) - rho;
					if ((d0 < 0.0) == (d1 < 0.0) || d0 == d1)
					{
						continue;
					}
					double t = d0 / (d0 - d1);
					double x = road.X[i] + (road.X[i + 1] - road.X[i]) * t, z = road.Z[i] + (road.Z[i + 1] - road.Z[i]) * t;
					hits.Add(new RingHit { Angle = Atan2(z - place.Z, x - place.X), Road = r, X = x, Z = z, Tx = road.X[i + 1] - road.X[i], Tz = road.Z[i + 1] - road.Z[i] });
					crossed = true;
				}
				// A road that ends short of the circle (an old road stopped by the beach) bounds a wedge too, from its far end.
				for (int e = 0; e < 2 && !crossed; e++)
				{
					int i = e == 0 ? 0 : road.Count - 1, j = e == 0 ? 1 : road.Count - 2;
					double dx = road.X[i] - place.X, dz = road.Z[i] - place.Z;
					double d = Math.Sqrt(dx * dx + dz * dz);
					if (d < rho && d > place.Radius * 0.2)
					{
						hits.Add(new RingHit { Angle = Atan2(dz, dx), Road = r, X = road.X[i], Z = road.Z[i], Tx = road.X[i] - road.X[j], Tz = road.Z[i] - road.Z[j] });
					}
				}
			}
			hits.Sort((p, q) => p.Angle != q.Angle ? p.Angle.CompareTo(q.Angle) : p.Road.CompareTo(q.Road));
			List<RingHit> kept = new List<RingHit>();
			foreach (RingHit hit in hits)
			{
				if (kept.Count == 0 || hit.Angle - kept[kept.Count - 1].Angle > 0.12)
				{
					kept.Add(hit);
				}
			}
			if (kept.Count > 1 && kept[0].Angle + 2.0 * DetMath.Pi - kept[kept.Count - 1].Angle <= 0.12)
			{
				kept.RemoveAt(kept.Count - 1);
			}
			return kept;
		}

		/// <summary>Whether (x, z), whose land is <paramref name="h"/> from sea level, is the beach (as <see cref="LandUse"/> draws sand).</summary>
		private static bool Beach(double x, double z, double h)
		{
			return h < WorldGen.SeaLevel + 5.0 && WorldGen.Inland(x, z) < 600.0;
		}

		/// <summary>
		/// How far a straight old road out of the middle at <paramref name="angle"/> could go before a road, water, the beach or the town's
		/// edge (from 100 out; checked every 40).
		/// </summary>
		private static double Room(Places.Place place, double angle)
		{
			double cx = DetMath.Cos(angle), cz = DetMath.Sin(angle);
			double px = place.X + cx * 100.0, pz = place.Z + cz * 100.0;
			for (double t = 140.0; t <= place.Radius; t += 40.0)
			{
				double x = place.X + cx * t, z = place.Z + cz * t;
				if (segments.Intersect(px, pz, x, z, -1, out double _, out double _, out double _) || !double.IsNaN(Rivers.SurfaceAt(x, z)) || Rivers.Distance(x, z, out double plain) < plain * 0.5 + 25.0 || Beach(x, z, WorldGen.Base(x, z)))
				{
					return t - 40.0;
				}
				px = x;
				pz = z;
			}
			return place.Radius;
		}

		/// <summary>An angle brought into -pi to pi.</summary>
		private static double Turn(double a)
		{
			return a - 2.0 * DetMath.Pi * Math.Floor((a + DetMath.Pi) / (2.0 * DetMath.Pi));
		}

		private static void Unit(double x, double z, out double ux, out double uz)
		{
			double len = Math.Sqrt(x * x + z * z);
			ux = len > 0.0 ? x / len : 1.0;
			uz = len > 0.0 ? z / len : 0.0;
		}

		private static double Dist((double x, double z) a, (double x, double z) b)
		{
			return Math.Sqrt((a.x - b.x) * (a.x - b.x) + (a.z - b.z) * (a.z - b.z));
		}
	}
}
