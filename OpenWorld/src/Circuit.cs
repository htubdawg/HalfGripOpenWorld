using System;
using System.Collections.Generic;

namespace HalfGripOpenWorld
{
	/// <summary>What the circuit is at a point, for the map pictures, the plants and the checks.</summary>
	internal enum CircuitSurface
	{
		None = 0,
		Track = 1,
		Kerb = 2,
		Gravel = 3,
		/// <summary>The pit lane and the paddock behind it: one sheet of tarmac beside the start straight.</summary>
		Apron = 4,
	}

	/// <summary>
	/// The tarmac circuit: a closed lap of about 3 km (real) on open, flat country, laid out once from the world's seed before any road
	/// is routed, so the roads go round it and only its own road joins it (at the paddock gate).
	///
	///   1. **Site.** On the 200-unit grid, every centre and heading for a box big enough for the lap, its run-off, stands and banks is
	///      tried: no water, river floodplain, coast, mountains or edge wall in it, and clear of the towns and villages. The box whose land
	///      lies closest to a plane is taken (least earthwork).
	///   2. **Lap.** A chain of seven to ten corners round a ring squashed to the box, some pulled in towards the middle (so corners go
	///      both ways), each rounded with an arc, the sharpest made a hairpin, joined by straights, scaled to the lap's length; the longest
	///      straight is the start straight. Then a few long sine waves bend the lap off the straights and the hairpin (the way ProcTrack's
	///      generator makes its laps). Laps that leave the box, turn too tightly, come near themselves or leave no room for the pits and
	///      stands are thrown away and drawn again. The lap runs anticlockwise, so its right is always the outside of the loop.
	///   3. **Ground.** The whole of it (the infield, the run-off, the pits and paddock, the stands) is levelled to the plane that fits the
	///      land best, tilted at most 2%, with banks back to the land round it (<see cref="Level"/>, a layer of the land under the roads).
	///
	/// Its parts, across the lap (d, metres to the right of the centre line, in units):
	///   track 40 wide (12 m); kerbs 4 wide on both edges through the corners; gravel traps on the outside of the corners; grass run-off;
	///   an armco barrier outside the loop (none along the pits); grandstands beyond it at turn 1 and the hairpin, and one in the infield
	///   opposite the pits. Along the start straight, outside: the pit wall, the pit lane, the garages, the paddock and its gate.
	///
	/// Built with <see cref="Rng"/>, <see cref="DetMath"/>, <see cref="Network.Atan2"/> and plain arithmetic, ties broken by index, so it
	/// is the same on every machine; read-only once built, so tiles can read it from any thread.
	/// </summary>
	internal static class Circuit
	{
		/// <summary>Half the track's width: 40 across, 12 m, a modern circuit's.</summary>
		internal const double Half = 20.0;

		/// <summary>The kerbs' width, on both edges through the corners.</summary>
		internal const double KerbWidth = 4.0;

		/// <summary>The gravel traps, outside the corners: from this far out to <see cref="GravelOut"/> at their widest.</summary>
		internal const double GravelIn = Half + KerbWidth + 3.0;
		internal const double GravelOut = Half + 58.0;

		/// <summary>The armco barrier outside the loop stands this far from the centre line.</summary>
		internal const double Barrier = Half + 70.0;

		/// <summary>Outside the loop the ground is levelled this far from the centre line (room for the stands beyond the barrier).</summary>
		internal const double Band = Half + 135.0;

		/// <summary>Pit lane, garages and paddock, out from the centre line along the pit straight.</summary>
		internal const double PitWallIn = Half + 1.0;
		internal const double PitWallOut = Half + 3.0;
		internal const double PitLaneOut = Half + 38.0;
		internal const double GaragesOut = Half + 88.0;
		internal const double PaddockOut = Half + 270.0;

		/// <summary>Along the pit straight the ground is levelled this far out (the paddock and a margin round its gate).</summary>
		internal const double PaddockBand = PaddockOut + 30.0;

		/// <summary>Distance between the lap's samples.</summary>
		internal const double Step = 8.0;

		/// <summary>The lap's length: about 3 km real.</summary>
		private const double TargetLength = 10000.0;

		/// <summary>The lap's box (it must fit in it, as laid out before it is turned onto the site).</summary>
		private const double BoxLong = 4000.0;
		private const double BoxShort = 2500.0;

		/// <summary>The site must be clear this far round the lap's box (the band, the stands, the banks back to the land).</summary>
		private const double SiteMargin = 450.0;

		/// <summary>The tightest corner (the hairpin) and the tightest a corner turning right may be (its outside is the infield, its inside the outside of the loop, where the barrier and stands are).</summary>
		private const double MinRadius = 60.0;
		private const double MinRightRadius = 190.0;

		/// <summary>Two stretches of the lap more than <see cref="SeparationAlong"/> apart along it keep this far apart: both barriers and a gap.</summary>
		private const double Separation = 260.0;
		private const double SeparationAlong = 700.0;

		/// <summary>The banks back to the land round the levelled ground are at most this wide.</summary>
		private const double BankMax = 250.0;

		/// <summary>Steepest tilt of the levelled ground.</summary>
		private const double MaxTilt = 0.02;

		/// <summary>Kept clear of towns' and villages' edges.</summary>
		private const double PlaceClearance = 1500.0;

		internal static bool Exists;
		internal static double[] X;
		internal static double[] Z;
		/// <summary>Distance along the lap; the start straight runs from 0 to <see cref="StraightLength"/>.</summary>
		internal static double[] S;
		/// <summary>The unit vector to the right of the way the lap runs (outside the loop), at each sample.</summary>
		internal static double[] Rx;
		internal static double[] Rz;
		/// <summary>Signed curvature, smoothed (positive turning left).</summary>
		internal static double[] Curve;
		/// <summary>0 to 1: how much kerb (both edges) at each sample.</summary>
		internal static double[] Kerb;
		/// <summary>0 to 1: how wide a gravel trap at each sample, on the side <see cref="GravelSide"/> (+1 right, -1 left).</summary>
		internal static double[] Gravel;
		internal static int[] GravelSide;
		internal static int Count;
		internal static double Length;

		/// <summary>The start straight: from sample 0 along (DirX, DirZ); its right is (DirZ, -DirX).</summary>
		internal static double StraightLength;
		internal static double DirX;
		internal static double DirZ;

		/// <summary>Along the start straight (distance from sample 0): the pit lane's full width, the tapers in and out, the start line.</summary>
		internal static double PitA;
		internal static double PitB;
		internal const double PitTaper = 140.0;
		internal static double StartS;
		/// <summary>Along the straight, the garages and the paddock.</summary>
		internal static double GaragesA;
		internal static double GaragesB;
		internal static double PaddockA;
		internal static double PaddockB;

		/// <summary>The paddock gate (the circuit road starts here) and a point out beyond it the road is routed from.</summary>
		internal static double GateX;
		internal static double GateZ;
		internal static double GateOutX;
		internal static double GateOutZ;

		/// <summary>The levelled ground's plane, from sea level: P0 at (Cx, Cz), rising Px a unit east and Pz a unit north.</summary>
		internal static double P0;
		internal static double Px;
		internal static double Pz;
		internal static double Cx;
		internal static double Cz;

		/// <summary>The track's surface is drawn this far over the levelled ground.</summary>
		internal const double Lift = 0.10;

		/// <summary>A grandstand: its middle, the way it faces (towards the track), its length and depth.</summary>
		internal struct Stand
		{
			public double X;
			public double Z;
			public double Fx;
			public double Fz;
			public double Length;
			public double Depth;
			public string Name;
		}

		internal static readonly List<Stand> Stands = new List<Stand>();

		/// <summary>For the log and the preview: the site's earthwork, worst cut or fill.</summary>
		internal static double Earthwork;
		internal static double MinLeftRadius;
		internal static double MinRightTurn;
		internal static int Attempts;
		internal static string Report = "";


		private static readonly ulong Salt = Noise.Mix(WorldGen.Seed + 0xC1C1UL);

		// ---- building --------------------------------------------------------------------------------------------

		/// <summary>
		/// Lays the circuit out. Called by <see cref="Places.Build"/> once the towns and villages are placed and before the farms, so the
		/// farms keep off it; the rivers are built. If no site or lap is found, there is no circuit and nothing else changes.
		/// </summary>
		internal static void Build()
		{
			Exists = false;
			Stands.Clear();
			List<(double x, double z, double angle, double score)> sites = Sites();
			Rng rng = new Rng(Noise.Mix(Salt));
			for (int k = 0; k < sites.Count && k < 12; k++)
			{
				(double sx, double sz, double angle, double score) = sites[k];
				for (int attempt = 0; attempt < 400; attempt++)
				{
					Attempts++;
					if (Try(rng, sx, sz, angle))
					{
						Fit(sx, sz, angle);
						Index();
						Exists = true;
						Report = "circuit at (" + sx.ToString("0") + ", " + sz.ToString("0") + "), site " + (k + 1) + " of " + sites.Count + ", " + Attempts + " laps drawn";
						return;
					}
				}
			}
			Report = "no circuit: " + sites.Count + " sites, no lap fitted";
		}

		// ---- the site --------------------------------------------------------------------------------------------

		private const double Grid = 200.0;
		private const int N = 160;
		private const double Min = -N * Grid / 2.0;

		/// <summary>
		/// The sites a lap's box fits on, best (flattest) first, each at least the box's length from every better one: (centre x, z, the
		/// box's heading, the rms of the land about its best plane).
		/// </summary>
		private static List<(double x, double z, double angle, double score)> Sites()
		{
			double[] land = new double[N * N];
			bool[] bad = new bool[N * N];
			double wall = WorldGen.Half - WorldGen.WallDepth - 700.0;
			for (int j = 0; j < N; j++)
			{
				for (int i = 0; i < N; i++)
				{
					int c = j * N + i;
					double x = Min + (i + 0.5) * Grid, z = Min + (j + 0.5) * Grid;
					land[c] = WorldGen.Land(x, z);
					bool b = Math.Abs(x) > wall || z > wall || WorldGen.Inland(x, z) < 2200.0 || WorldGen.MountainMask(x, z) > 0.02;
					if (!b)
					{
						b = !double.IsNaN(Rivers.SurfaceAt(x, z)) || Rivers.Water(x, z, out double _) || Rivers.Distance(x, z, out double plain) < plain + 300.0;
					}
					if (!b)
					{
						foreach (Places.Place p in Places.All)
						{
							double reach = p.Radius + PlaceClearance;
							if ((p.X - x) * (p.X - x) + (p.Z - z) * (p.Z - z) < reach * reach)
							{
								b = true;
								break;
							}
						}
					}
					bad[c] = b;
				}
			}
			List<(double x, double z, double angle, double score)> found = new List<(double, double, double, double)>();
			double hl = BoxLong * 0.5 + SiteMargin, hs = BoxShort * 0.5 + SiteMargin;
			for (double cz = Min + hs; cz <= -Min - hs; cz += 400.0)
			{
				for (double cx = Min + hs; cx <= -Min - hs; cx += 400.0)
				{
					for (int a = 0; a < 8; a++)
					{
						double angle = a * DetMath.Pi / 8.0;
						double ca = DetMath.Cos(angle), sa = DetMath.Sin(angle);
						double n = 0.0, su = 0.0, sv = 0.0, sh = 0.0, suu = 0.0, svv = 0.0, suv = 0.0, suh = 0.0, svh = 0.0;
						bool ok = true;
						for (double v = -hs; v <= hs && ok; v += Grid)
						{
							for (double u = -hl; u <= hl; u += Grid)
							{
								double x = cx + u * ca - v * sa, z = cz + u * sa + v * ca;
								int c = Cell(x, z);
								if (c < 0 || bad[c])
								{
									ok = false;
									break;
								}
								double h = land[c];
								n += 1.0;
								su += u;
								sv += v;
								sh += h;
								suu += u * u;
								svv += v * v;
								suv += u * v;
								suh += u * h;
								svh += v * h;
							}
						}
						if (!ok)
						{
							continue;
						}
						// Least squares plane h = a + b u + c v over the box (u and v are about centred, but solved in full).
						Plane(n, su, sv, sh, suu, svv, suv, suh, svh, out double pa, out double pb, out double pc);
						double sq = 0.0;
						for (double v = -hs; v <= hs; v += Grid)
						{
							for (double u = -hl; u <= hl; u += Grid)
							{
								double x = cx + u * ca - v * sa, z = cz + u * sa + v * ca;
								double r = land[Cell(x, z)] - (pa + pb * u + pc * v);
								sq += r * r;
							}
						}
						found.Add((cx, cz, angle, Math.Sqrt(sq / n)));
					}
				}
			}
			found.Sort((p, q) => p.score != q.score ? p.score.CompareTo(q.score) : (p.x != q.x ? p.x.CompareTo(q.x) : (p.z != q.z ? p.z.CompareTo(q.z) : p.angle.CompareTo(q.angle))));
			List<(double x, double z, double angle, double score)> kept = new List<(double, double, double, double)>();
			foreach (var s in found)
			{
				bool apart = true;
				foreach (var k in kept)
				{
					if ((k.x - s.x) * (k.x - s.x) + (k.z - s.z) * (k.z - s.z) < BoxLong * BoxLong)
					{
						apart = false;
						break;
					}
				}
				if (apart)
				{
					kept.Add(s);
				}
			}
			return kept;
		}

		private static int Cell(double x, double z)
		{
			int i = (int)Math.Floor((x - Min) / Grid), j = (int)Math.Floor((z - Min) / Grid);
			return i < 0 || j < 0 || i >= N || j >= N ? -1 : j * N + i;
		}

		/// <summary>Solves the normal equations of the least squares plane h = a + b u + c v from its sums.</summary>
		private static void Plane(double n, double su, double sv, double sh, double suu, double svv, double suv, double suh, double svh, out double a, out double b, out double c)
		{
			// Centred, the cross terms with the mean drop out.
			double mu = su / n, mv = sv / n, mh = sh / n;
			double cuu = suu - n * mu * mu, cvv = svv - n * mv * mv, cuv = suv - n * mu * mv;
			double cuh = suh - n * mu * mh, cvh = svh - n * mv * mh;
			double det = cuu * cvv - cuv * cuv;
			if (Math.Abs(det) < 1e-9)
			{
				b = c = 0.0;
			}
			else
			{
				b = (cuh * cvv - cvh * cuv) / det;
				c = (cvh * cuu - cuh * cuv) / det;
			}
			a = mh - b * mu - c * mv;
		}

		// ---- the lap ---------------------------------------------------------------------------------------------

		/// <summary>
		/// Draws a lap and, if it passes, keeps it (on the site centred at (sx, sz), its long side along <paramref name="angle"/>). The
		/// lap is made in the box's own frame (u along, v across), then turned onto the site.
		/// </summary>
		private static bool Try(Rng rng, double sx, double sz, double angle)
		{
			if (!Chain(rng, out List<(double u, double v)> line, out double straight, out double hairpinS, out double hairpinHalf))
			{
				return false;
			}
			// Turn half the laps round, so the pits are on either side of the box.
			bool turn = rng.Next() < 0.5;
			double minU = double.MaxValue, maxU = double.MinValue, minV = double.MaxValue, maxV = double.MinValue;
			foreach ((double u, double v) in line)
			{
				minU = Math.Min(minU, u);
				maxU = Math.Max(maxU, u);
				minV = Math.Min(minV, v);
				maxV = Math.Max(maxV, v);
			}
			if (maxU - minU > BoxLong || maxV - minV > BoxShort)
			{
				return false;
			}
			double mu = (minU + maxU) * 0.5, mv = (minV + maxV) * 0.5;
			double ca = DetMath.Cos(angle), sa = DetMath.Sin(angle);
			if (turn)
			{
				ca = -ca;
				sa = -sa;
			}
			int n = line.Count;
			double[] x = new double[n], z = new double[n];
			for (int i = 0; i < n; i++)
			{
				double u = line[i].u - mu, v = line[i].v - mv;
				x[i] = sx + u * ca - v * sa;
				z[i] = sz + u * sa + v * ca;
			}
			X = x;
			Z = z;
			Count = n;
			S = new double[n];
			for (int i = 1; i < n; i++)
			{
				S[i] = S[i - 1] + Math.Sqrt((x[i] - x[i - 1]) * (x[i] - x[i - 1]) + (z[i] - z[i - 1]) * (z[i] - z[i - 1]));
			}
			Length = S[n - 1] + Math.Sqrt((x[0] - x[n - 1]) * (x[0] - x[n - 1]) + (z[0] - z[n - 1]) * (z[0] - z[n - 1]));
			StraightLength = Math.Floor(straight / Step) * Step;
			Frame();
			if (!Shaped(hairpinS, hairpinHalf))
			{
				return false;
			}
			return Layout();
		}

		/// <summary>
		/// The lap in the box's frame, every <see cref="Step"/>: the chain of corners (see the class summary) bent by sine waves, starting
		/// at the beginning of the start straight, heading +u. False if the chain leaves no straight long enough for the pits.
		/// </summary>
		private static bool Chain(Rng rng, out List<(double u, double v)> line, out double lead, out double hairpinS, out double hairpinHalf)
		{
			line = null;
			lead = hairpinS = hairpinHalf = 0.0;
			int k = 7 + (int)(rng.Next() * 4.0);
			double squash = BoxShort / BoxLong;
			double[] vx = new double[k], vz = new double[k], want = new double[k];
			for (int i = 0; i < k; i++)
			{
				double jitter = rng.Next() - 0.5;
				bool dent = rng.Next() < 0.4;
				double depth = rng.Next();
				want[i] = MinRadius * (1.3 + 3.5 * rng.Next());
				double a = 2.0 * DetMath.Pi * (i + 0.6 * jitter) / k;
				double f = dent ? 1.0 - 0.6 * (0.6 + 0.4 * depth) : 1.0 - 0.12 * depth;
				vx[i] = DetMath.Cos(a) * f;
				vz[i] = DetMath.Sin(a) * f * squash;
			}
			double[] turn = new double[k], side = new double[k];
			double perimeter = 0.0;
			int sharpest = 0;
			for (int i = 0; i < k; i++)
			{
				int p = (i - 1 + k) % k, q = (i + 1) % k;
				double ix = vx[i] - vx[p], iz = vz[i] - vz[p], ox = vx[q] - vx[i], oz = vz[q] - vz[i];
				turn[i] = Network.Atan2(ix * oz - iz * ox, ix * ox + iz * oz);
				side[i] = Math.Sqrt(ox * ox + oz * oz);
				perimeter += side[i];
				if (turn[i] > turn[sharpest])
				{
					sharpest = i;
				}
			}
			// The sharpest left-hander is the hairpin: as tight as a corner may be.
			if (turn[sharpest] < 1.7)
			{
				return false;
			}
			want[sharpest] = MinRadius;
			for (int i = 0; i < k; i++)
			{
				if (turn[i] < 0.0)
				{
					want[i] = Math.Max(want[i], MinRightRadius * 1.15);
				}
			}
			double scale = TargetLength / perimeter;
			double[] rho = new double[k], tan = new double[k];
			for (int pass = 0; pass < 6; pass++)
			{
				for (int i = 0; i < k; i++)
				{
					int p = (i - 1 + k) % k;
					double room = 0.45 * scale * Math.Min(side[p], side[i]);
					double half = Math.Abs(turn[i]) * 0.5;
					double t = DetMath.Sin(half) / Math.Max(1e-6, DetMath.Cos(half));
					rho[i] = want[i];
					if (rho[i] * t > room)
					{
						rho[i] = room / Math.Max(1e-6, t);
					}
					tan[i] = rho[i] * t;
				}
				double length = 0.0;
				for (int i = 0; i < k; i++)
				{
					length += scale * side[i] - tan[i] - tan[(i + 1) % k] + rho[i] * Math.Abs(turn[i]);
				}
				scale *= TargetLength / length;
			}
			int first = -1;
			double longest = 0.0;
			for (int i = 0; i < k; i++)
			{
				double straight = scale * side[i] - tan[i] - tan[(i + 1) % k];
				if (straight < 20.0 || rho[i] < MinRadius * 0.99 || (turn[i] < 0.0 && rho[i] < MinRightRadius))
				{
					return false;
				}
				if (straight > longest)
				{
					longest = straight;
					first = i;
				}
			}
			if (longest < 1300.0)
			{
				return false;
			}
			// The pieces from the start straight on: straight, arc, straight, arc...
			double[] len = new double[2 * k], segU = new double[2 * k], segV = new double[2 * k], segH = new double[2 * k], segR = new double[2 * k];
			double hu = 0.0, hv = 0.0, hh = 0.0, acc = 0.0;
			for (int m = 0; m < k; m++)
			{
				int i = (first + m) % k, c = (i + 1) % k;
				double straight = scale * side[i] - tan[i] - tan[c];
				len[2 * m] = straight;
				segU[2 * m] = hu;
				segV[2 * m] = hv;
				segH[2 * m] = hh;
				hu += straight * DetMath.Cos(hh);
				hv += straight * DetMath.Sin(hh);
				acc += straight;
				double arc = rho[c] * Math.Abs(turn[c]);
				double r = turn[c] >= 0.0 ? rho[c] : -rho[c];
				len[2 * m + 1] = arc;
				segU[2 * m + 1] = hu;
				segV[2 * m + 1] = hv;
				segH[2 * m + 1] = hh;
				segR[2 * m + 1] = r;
				double ccu = hu - r * DetMath.Sin(hh), ccv = hv + r * DetMath.Cos(hh);
				hh += arc / r;
				hu = ccu + r * DetMath.Sin(hh);
				hv = ccv - r * DetMath.Cos(hh);
				if (c == sharpest)
				{
					hairpinS = acc + arc * 0.5;
					hairpinHalf = arc * 0.5;
				}
				acc += arc;
			}
			// The corners' ring must be a simple loop turning once anticlockwise, or the chain does not close on itself.
			if (Math.Abs(hh - 2.0 * DetMath.Pi) > 0.01 || hu * hu + hv * hv > 4.0)
			{
				return false;
			}
			lead = len[0];
			double total = acc;

			// Sine waves along the lap, bending the straights other than the start straight: off the corners (an offset across a tight
			// arc folds it into a cusp) and fading in over 200 beyond them. All the waves together bend no tighter than a 300 radius.
			const int harmonics = 6;
			double[] amp = new double[harmonics], phase = new double[harmonics];
			for (int h = 0; h < harmonics; h++)
			{
				double w = 2.0 * DetMath.Pi * (h + 2) / total;
				amp[h] = rng.Range(-1.0, 1.0) * Math.Min(1.0 / (300.0 * harmonics) / (w * w), 260.0);
				phase[h] = rng.Range(0.0, 2.0 * DetMath.Pi);
			}
			// Where each arc is along the lap, for the fade.
			List<(double from, double to)> arcs = new List<(double, double)>();
			double at = 0.0;
			for (int q = 0; q < len.Length; q++)
			{
				if (segR[q] != 0.0)
				{
					arcs.Add((at, at + len[q]));
				}
				at += len[q];
			}
			int dense = (int)(total / 2.0);
			List<(double u, double v)> pts = new List<(double u, double v)>(dense + 1);
			for (int i = 0; i < dense; i++)
			{
				double s = total * i / dense;
				Piece(s, len, segU, segV, segH, segR, out double u, out double v, out double nu, out double nv);
				double fromStraight = s <= lead ? 0.0 : Math.Min(s - lead, total - s);
				double fromHairpin = Math.Max(0.0, Math.Abs(s - hairpinS) - hairpinHalf);
				double fromArc = double.MaxValue;
				foreach ((double from, double to) in arcs)
				{
					double d = s < from ? Math.Min(from - s, s + total - to) : s > to ? Math.Min(s - to, from + total - s) : 0.0;
					fromArc = Math.Min(fromArc, d);
				}
				double window = Noise.Step(0.0, 250.0, fromStraight) * Noise.Step(0.0, 250.0, fromHairpin) * Noise.Step(0.0, 200.0, fromArc);
				double off = 0.0;
				for (int h = 0; h < harmonics; h++)
				{
					off += amp[h] * DetMath.Sin(2.0 * DetMath.Pi * (h + 2) * s / total + phase[h]);
				}
				off *= window;
				pts.Add((u + nu * off, v + nv * off));
			}
			line = Closed(pts, out double length2);
			// Distances along the bent lap differ a little from the chain's: scale the marks to it.
			double ratio = length2 / total;
			lead *= ratio;
			hairpinS *= ratio;
			hairpinHalf *= ratio;
			return true;
		}

		/// <summary>The chain at distance s along it, with the unit vector to its right.</summary>
		private static void Piece(double s, double[] len, double[] su, double[] sv, double[] sh, double[] sr, out double u, out double v, out double nu, out double nv)
		{
			double acc = 0.0;
			for (int k = 0; k < len.Length; k++)
			{
				if (s <= acc + len[k] || k == len.Length - 1)
				{
					double along = s - acc, h = sh[k];
					if (sr[k] == 0.0)
					{
						u = su[k] + along * DetMath.Cos(h);
						v = sv[k] + along * DetMath.Sin(h);
						nu = DetMath.Sin(h);
						nv = -DetMath.Cos(h);
						return;
					}
					double r = sr[k];
					double cu = su[k] - r * DetMath.Sin(h), cv = sv[k] + r * DetMath.Cos(h);
					double hh = h + along / r;
					u = cu + r * DetMath.Sin(hh);
					v = cv - r * DetMath.Cos(hh);
					nu = DetMath.Sin(hh);
					nv = -DetMath.Cos(hh);
					return;
				}
				acc += len[k];
			}
			u = v = nu = nv = 0.0;
		}

		/// <summary>A closed line resampled into pieces of (nearly) <see cref="Step"/>, as many as fit its length evenly; its length.</summary>
		private static List<(double u, double v)> Closed(List<(double u, double v)> pts, out double length)
		{
			int n = pts.Count;
			double[] acc = new double[n + 1];
			for (int i = 0; i < n; i++)
			{
				(double u0, double v0) = pts[i];
				(double u1, double v1) = pts[(i + 1) % n];
				acc[i + 1] = acc[i] + Math.Sqrt((u1 - u0) * (u1 - u0) + (v1 - v0) * (v1 - v0));
			}
			length = acc[n];
			int count = (int)Math.Round(length / Step);
			List<(double u, double v)> output = new List<(double u, double v)>(count);
			int k = 0;
			for (int m = 0; m < count; m++)
			{
				double s = length * m / count;
				while (k < n - 1 && acc[k + 1] < s)
				{
					k++;
				}
				double t = (s - acc[k]) / Math.Max(1e-9, acc[k + 1] - acc[k]);
				(double u0, double v0) = pts[k];
				(double u1, double v1) = pts[(k + 1) % n];
				output.Add((u0 + (u1 - u0) * t, v0 + (v1 - v0) * t));
			}
			return output;
		}

		/// <summary>Each sample's right vector, curvature, kerbs and gravel.</summary>
		private static void Frame()
		{
			int n = Count;
			Rx = new double[n];
			Rz = new double[n];
			double[] raw = new double[n];
			for (int i = 0; i < n; i++)
			{
				int p = (i - 1 + n) % n, q = (i + 1) % n;
				double fx = X[q] - X[p], fz = Z[q] - Z[p];
				double len = Math.Sqrt(fx * fx + fz * fz);
				fx /= len;
				fz /= len;
				Rx[i] = fz;
				Rz[i] = -fx;
				// The circle through the samples 3 either side (48 apart): its signed curvature, positive turning left.
				int a = (i - 3 + n) % n, b = (i + 3) % n;
				double ax = X[i] - X[a], az = Z[i] - Z[a], bx = X[b] - X[i], bz = Z[b] - Z[i], cx = X[b] - X[a], cz = Z[b] - Z[a];
				double cross = ax * bz - az * bx;
				raw[i] = 2.0 * cross / (Math.Sqrt(ax * ax + az * az) * Math.Sqrt(bx * bx + bz * bz) * Math.Sqrt(cx * cx + cz * cz));
			}
			Curve = new double[n];
			for (int i = 0; i < n; i++)
			{
				double sum = 0.0;
				for (int k = -2; k <= 2; k++)
				{
					sum += raw[(i + k + n) % n];
				}
				Curve[i] = sum / 5.0;
			}
			// Kerbs through every corner tighter than 600, coming in over 4 samples either side; gravel outside every corner tighter
			// than 350, reaching on 15 samples (120) past it, where a car that runs wide on the exit goes.
			Kerb = new double[n];
			Gravel = new double[n];
			GravelSide = new int[n];
			for (int i = 0; i < n; i++)
			{
				double c = Math.Abs(Curve[i]);
				if (c > 1.0 / 600.0)
				{
					for (int k = -4; k <= 4; k++)
					{
						int m = (i + k + n) % n;
						Kerb[m] = Math.Max(Kerb[m], 1.0 - Math.Abs(k) / 5.0);
					}
				}
				if (c > 1.0 / 350.0)
				{
					double w = Math.Min(1.0, c * 350.0 - 0.6);
					int side = Curve[i] > 0.0 ? 1 : -1;
					for (int k = -6; k <= 15; k++)
					{
						int m = (i + k + n) % n;
						double fade = k < 0 ? 1.0 + k / 7.0 : 1.0 - Math.Max(0, k - 8) / 8.0;
						double g = w * fade;
						if (g > Gravel[m])
						{
							Gravel[m] = g;
							GravelSide[m] = side;
						}
					}
				}
			}
		}

		/// <summary>The lap's own checks: its corners and how near it comes to itself.</summary>
		private static bool Shaped(double hairpinS, double hairpinHalf)
		{
			int n = Count;
			double left = double.MaxValue, right = double.MaxValue;
			for (int i = 0; i < n; i++)
			{
				double c = Curve[i];
				if (c > 0.0)
				{
					left = Math.Min(left, 1.0 / c);
				}
				else if (c < 0.0)
				{
					right = Math.Min(right, -1.0 / c);
				}
			}
			if (left < MinRadius * 0.9 || right < MinRightRadius)
			{
				return false;
			}
			MinLeftRadius = left;
			MinRightTurn = right;
			// No gravel or kerb on the start straight.
			for (int i = 0; i * Step <= StraightLength; i++)
			{
				Gravel[i] = 0.0;
				Kerb[i] = 0.0;
			}
			for (int i = 0; i < n; i += 3)
			{
				for (int j = i + 3; j < n; j += 3)
				{
					double along = Math.Min(S[j] - S[i], Length - (S[j] - S[i]));
					if (along < SeparationAlong)
					{
						continue;
					}
					double dx = X[j] - X[i], dz = Z[j] - Z[i];
					if (dx * dx + dz * dz < Separation * Separation)
					{
						return false;
					}
				}
			}
			HairpinS = hairpinS;
			HairpinHalf = hairpinHalf;
			return true;
		}

		internal static double HairpinS;
		private static double HairpinHalf;

		/// <summary>
		/// The pits, paddock, gate and stands along and round the lap, if there is room: nothing of the lap may come into the paddock or
		/// in front of the stands. False if there is not.
		/// </summary>
		private static bool Layout()
		{
			DirX = -Rz[0];
			DirZ = Rx[0];
			// The straight's first sample is at the end of the corner before it: frame it from the straight's own line.
			int e = (int)(StraightLength / Step);
			double dx = X[e] - X[0], dz = Z[e] - Z[0], dl = Math.Sqrt(dx * dx + dz * dz);
			DirX = dx / dl;
			DirZ = dz / dl;
			double L = StraightLength;
			PitA = 120.0 + PitTaper;
			PitB = L - 120.0 - PitTaper;
			PaddockA = PitA + 40.0;
			PaddockB = PitB - 40.0;
			double garages = Math.Min(560.0, PaddockB - PaddockA - 200.0);
			double mid = (PitA + PitB) * 0.5;
			GaragesA = mid - garages * 0.5;
			GaragesB = mid + garages * 0.5;
			StartS = Math.Floor((mid + 80.0) / Step) * Step;
			Straight(mid, PaddockOut + 4.0, out GateX, out GateZ);
			Straight(mid, PaddockOut + 180.0, out GateOutX, out GateOutZ);
			// Nothing else of the lap in the pits and paddock (plus room), or in the infield in front of the stand opposite.
			for (int i = 0; i < Count; i++)
			{
				if (S[i] <= L + 40.0 || S[i] >= Length - 40.0)
				{
					continue;
				}
				Local(X[i], Z[i], out double u, out double v);
				if (u > PitA - PitTaper - 150.0 && u < PitB + PitTaper + 150.0 && v > -Half - 160.0 && v < PaddockBand + Separation)
				{
					return false;
				}
			}
			// The stands: one in the infield opposite the pits, facing them; one outside turn 1 and one outside the hairpin, beyond
			// the barrier, facing the corner's apex.
			Stands.Clear();
			Straight(StartS - 40.0, -(Half + 82.0), out double ix, out double iz);
			Stands.Add(new Stand { X = ix, Z = iz, Fx = -DirZ, Fz = DirX, Length = 240.0, Depth = 40.0, Name = "Main stand" });
			int turn1 = -1;
			for (int i = e; i < Count; i++)
			{
				if (Curve[i] > 1.0 / 400.0)
				{
					int apex = i;
					while (apex + 1 < Count && Curve[apex + 1] > Curve[apex])
					{
						apex++;
					}
					turn1 = apex;
					break;
				}
			}
			int hairpin = (int)Math.Round(HairpinS / Step) % Count;
			if (turn1 < 0 || !Outside(turn1, "Turn 1 stand") || !Outside(hairpin, "Hairpin stand"))
			{
				return false;
			}
			return true;
		}

		/// <summary>A stand outside the corner at sample i (a left-hander: its outside is the outside of the loop), if it has room.</summary>
		private static bool Outside(int i, string name)
		{
			if (Curve[i] <= 0.0)
			{
				return false;
			}
			const double length = 150.0, depth = 36.0, from = Barrier + 14.0;
			double cx = X[i] + Rx[i] * (from + depth * 0.5), cz = Z[i] + Rz[i] * (from + depth * 0.5);
			double fx = -Rx[i], fz = -Rz[i];
			// Its corners and middle: nearest to this corner, and within the band.
			for (int a = -1; a <= 1; a++)
			{
				for (int b = -1; b <= 1; b += 2)
				{
					double px = cx + (-fz) * a * length * 0.5 + fx * b * depth * 0.5, pz = cz + fx * a * length * 0.5 + fz * b * depth * 0.5;
					for (int k = 0; k < Count; k++)
					{
						double dx = X[k] - px, dz = Z[k] - pz;
						double near = Math.Min(Math.Abs(S[k] - S[i]), Length - Math.Abs(S[k] - S[i])) < 300.0 ? Barrier + 6.0 : Separation;
						if (dx * dx + dz * dz < near * near)
						{
							return false;
						}
					}
				}
			}
			foreach (Stand s in Stands)
			{
				if ((s.X - cx) * (s.X - cx) + (s.Z - cz) * (s.Z - cz) < 400.0 * 400.0)
				{
					return false;
				}
			}
			Stands.Add(new Stand { X = cx, Z = cz, Fx = fx, Fz = fz, Length = length, Depth = depth, Name = name });
			return true;
		}

		/// <summary>The point u along the start straight from sample 0, v to its right.</summary>
		internal static void Straight(double u, double v, out double x, out double z)
		{
			x = X[0] + DirX * u + DirZ * v;
			z = Z[0] + DirZ * u - DirX * v;
		}

		/// <summary>(x, z) in the start straight's frame: u along from sample 0, v to its right.</summary>
		internal static void Local(double x, double z, out double u, out double v)
		{
			double dx = x - X[0], dz = z - Z[0];
			u = dx * DirX + dz * DirZ;
			v = dx * DirZ - dz * DirX;
		}

		/// <summary>The levelled ground's plane, fitted to the land over the lap's levelled ground and tilted at most <see cref="MaxTilt"/>.</summary>
		private static void Fit(double sx, double sz, double angle)
		{
			double n = 0.0, su = 0.0, sv = 0.0, sh = 0.0, suu = 0.0, svv = 0.0, suv = 0.0, suh = 0.0, svh = 0.0;
			double hl = BoxLong * 0.5 + 200.0, hs = BoxShort * 0.5 + 200.0;
			double ca = DetMath.Cos(angle), sa = DetMath.Sin(angle);
			for (double v = -hs; v <= hs; v += 100.0)
			{
				for (double u = -hl; u <= hl; u += 100.0)
				{
					double x = sx + u * ca - v * sa, z = sz + u * sa + v * ca;
					double h = WorldGen.Land(x, z);
					double ex = x - sx, ez = z - sz;
					n += 1.0;
					su += ex;
					sv += ez;
					sh += h;
					suu += ex * ex;
					svv += ez * ez;
					suv += ex * ez;
					suh += ex * h;
					svh += ez * h;
				}
			}
			Plane(n, su, sv, sh, suu, svv, suv, suh, svh, out double a, out double b, out double c);
			double tilt = Math.Sqrt(b * b + c * c);
			if (tilt > MaxTilt)
			{
				b *= MaxTilt / tilt;
				c *= MaxTilt / tilt;
			}
			Cx = sx;
			Cz = sz;
			P0 = a;
			Px = b;
			Pz = c;
			Earthwork = 0.0;
			for (double v = -hs; v <= hs; v += 100.0)
			{
				for (double u = -hl; u <= hl; u += 100.0)
				{
					double x = sx + u * ca - v * sa, z = sz + u * sa + v * ca;
					Earthwork = Math.Max(Earthwork, Math.Abs(WorldGen.Land(x, z) - PlaneAt(x, z)));
				}
			}
		}

		/// <summary>The levelled ground's height at (x, z), from sea level.</summary>
		internal static double PlaneAt(double x, double z)
		{
			return P0 + Px * (x - Cx) + Pz * (z - Cz);
		}

		// ---- the index ---------------------------------------------------------------------------------------------

		/// <summary>How far round the lap the index reaches: the widest levelled band and its banks.</summary>
		private const double Reach = PaddockBand + BankMax + 20.0;

		private const double Bucket = 100.0;
		private static double bx0, bz0;
		private static int bw, bh;
		private static int[][] buckets;

		/// <summary>The infield: a raster of the inside of the loop.</summary>
		private const double InCell = 10.0;
		private static double ix0, iz0;
		private static int iw, ih;
		private static bool[] inside;

		private static void Index()
		{
			double minX = double.MaxValue, maxX = double.MinValue, minZ = double.MaxValue, maxZ = double.MinValue;
			for (int i = 0; i < Count; i++)
			{
				minX = Math.Min(minX, X[i]);
				maxX = Math.Max(maxX, X[i]);
				minZ = Math.Min(minZ, Z[i]);
				maxZ = Math.Max(maxZ, Z[i]);
			}
			bx0 = minX - Reach - Bucket;
			bz0 = minZ - Reach - Bucket;
			bw = (int)Math.Ceiling((maxX - minX + 2.0 * (Reach + Bucket)) / Bucket);
			bh = (int)Math.Ceiling((maxZ - minZ + 2.0 * (Reach + Bucket)) / Bucket);
			List<int>[] lists = new List<int>[bw * bh];
			for (int i = 0; i < Count; i++)
			{
				int q = (i + 1) % Count;
				double x0 = Math.Min(X[i], X[q]) - Reach, x1 = Math.Max(X[i], X[q]) + Reach;
				double z0 = Math.Min(Z[i], Z[q]) - Reach, z1 = Math.Max(Z[i], Z[q]) + Reach;
				int a0 = (int)Math.Floor((x0 - bx0) / Bucket), a1 = (int)Math.Floor((x1 - bx0) / Bucket);
				int c0 = (int)Math.Floor((z0 - bz0) / Bucket), c1 = (int)Math.Floor((z1 - bz0) / Bucket);
				for (int c = Math.Max(0, c0); c <= Math.Min(bh - 1, c1); c++)
				{
					for (int a = Math.Max(0, a0); a <= Math.Min(bw - 1, a1); a++)
					{
						(lists[c * bw + a] ??= new List<int>()).Add(i);
					}
				}
			}
			buckets = new int[bw * bh][];
			for (int k = 0; k < lists.Length; k++)
			{
				buckets[k] = lists[k]?.ToArray();
			}

			ix0 = minX - InCell;
			iz0 = minZ - InCell;
			iw = (int)Math.Ceiling((maxX - minX) / InCell) + 2;
			ih = (int)Math.Ceiling((maxZ - minZ) / InCell) + 2;
			inside = new bool[iw * ih];
			List<double> cross = new List<double>();
			for (int j = 0; j < ih; j++)
			{
				double z = iz0 + (j + 0.5) * InCell;
				cross.Clear();
				for (int i = 0; i < Count; i++)
				{
					int q = (i + 1) % Count;
					if ((Z[i] <= z) != (Z[q] <= z))
					{
						cross.Add(X[i] + (z - Z[i]) / (Z[q] - Z[i]) * (X[q] - X[i]));
					}
				}
				cross.Sort();
				for (int k = 0; k + 1 < cross.Count; k += 2)
				{
					int a = (int)Math.Ceiling((cross[k] - ix0) / InCell - 0.5), b = (int)Math.Floor((cross[k + 1] - ix0) / InCell - 0.5);
					for (int i = Math.Max(0, a); i <= Math.Min(iw - 1, b); i++)
					{
						inside[j * iw + i] = true;
					}
				}
			}
		}

		/// <summary>
		/// The nearest point of the lap to (x, z) within <see cref="Reach"/>: its piece (sample i to i + 1), the fraction t along it, the
		/// distance and which side (+1 right, outside the loop; -1 left). False if the lap is further than that.
		/// </summary>
		internal static bool Nearest(double x, double z, out int piece, out double t, out double dist, out int side)
		{
			piece = -1;
			t = dist = 0.0;
			side = 0;
			if (!Exists)
			{
				return false;
			}
			int a = (int)Math.Floor((x - bx0) / Bucket), c = (int)Math.Floor((z - bz0) / Bucket);
			if (a < 0 || c < 0 || a >= bw || c >= bh)
			{
				return false;
			}
			int[] list = buckets[c * bw + a];
			if (list == null)
			{
				return false;
			}
			double best = Reach * Reach;
			foreach (int i in list)
			{
				int q = (i + 1) % Count;
				double sx = X[q] - X[i], sz = Z[q] - Z[i];
				double len2 = sx * sx + sz * sz;
				double f = ((x - X[i]) * sx + (z - Z[i]) * sz) / len2;
				f = f < 0.0 ? 0.0 : f > 1.0 ? 1.0 : f;
				double px = X[i] + sx * f - x, pz = Z[i] + sz * f - z;
				double d2 = px * px + pz * pz;
				if (d2 < best)
				{
					best = d2;
					piece = i;
					t = f;
				}
			}
			if (piece < 0)
			{
				return false;
			}
			dist = Math.Sqrt(best);
			side = Inside(x, z) ? -1 : 1;
			return true;
		}

		/// <summary>True inside the loop (the infield).</summary>
		internal static bool Inside(double x, double z)
		{
			if (!Exists)
			{
				return false;
			}
			int i = (int)Math.Floor((x - ix0) / InCell), j = (int)Math.Floor((z - iz0) / InCell);
			if (i < 0 || j < 0 || i >= iw || j >= ih)
			{
				return false;
			}
			if (inside[j * iw + i])
			{
				return true;
			}
			return false;
		}

		/// <summary>Signed distance across the lap at piece i, t along: positive right of the centre line.</summary>
		internal static double Across(int i, double t, double x, double z)
		{
			int q = (i + 1) % Count;
			double rx = Rx[i] + (Rx[q] - Rx[i]) * t, rz = Rz[i] + (Rz[q] - Rz[i]) * t;
			double px = X[i] + (X[q] - X[i]) * t, pz = Z[i] + (Z[q] - Z[i]) * t;
			return (x - px) * rx + (z - pz) * rz;
		}

		/// <summary>Distance along the lap at piece i, t along.</summary>
		internal static double Along(int i, double t)
		{
			return S[i] + ((i + 1 < Count ? S[i + 1] : Length) - S[i]) * t;
		}

		/// <summary>How far out from the centre line the ground is levelled outside the loop at distance s along the lap: more along the paddock.</summary>
		internal static double BandAt(double s)
		{
			double paddock = Noise.Step(PaddockA - 250.0, PaddockA, s) * (1.0 - Noise.Step(PaddockB, PaddockB + 250.0, s));
			return Band + (PaddockBand - Band) * paddock;
		}

		/// <summary>
		/// The ground with the circuit levelled into it: the plane in the infield and over the band round the lap, then a bank back to
		/// <paramref name="h"/> (the land), wider the more it differs, up to <see cref="BankMax"/>.
		/// </summary>
		internal static double Level(double x, double z, double h)
		{
			if (!Exists || !Nearest(x, z, out int i, out double t, out double dist, out int side))
			{
				return h;
			}
			double plane = PlaneAt(x, z);
			if (side < 0)
			{
				return plane;
			}
			double e = dist - BandAt(Along(i, t));
			if (e <= 0.0)
			{
				return plane;
			}
			double diff = h - plane;
			double w = Math.Min(BankMax, 1.6 * Math.Abs(diff) + 8.0);
			return e >= w ? h : plane + diff * Noise.Step(0.0, 1.0, e / w);
		}

		/// <summary>
		/// True inside the circuit's ground: the infield, or within the band round the lap plus <paramref name="margin"/>. No road but the
		/// circuit's own, no farm and no tree comes in here.
		/// </summary>
		internal static bool Fenced(double x, double z, double margin)
		{
			if (!Exists || !Nearest(x, z, out int i, out double t, out double dist, out int side))
			{
				return false;
			}
			return side < 0 || dist <= BandAt(Along(i, t)) + margin;
		}

		/// <summary>
		/// False if (x, z) is in the infield or within <paramref name="margin"/> of the band round the lap (as far out as need be, unlike
		/// <see cref="Fenced"/>): where villages and farms may not be.
		/// </summary>
		internal static bool Clear(double x, double z, double margin)
		{
			if (!Exists)
			{
				return true;
			}
			if (Inside(x, z))
			{
				return false;
			}
			double reach = PaddockBand + margin;
			for (int i = 0; i < Count; i += 2)
			{
				double dx = X[i] - x, dz = Z[i] - z;
				if (dx * dx + dz * dz < reach * reach)
				{
					return false;
				}
			}
			return true;
		}

		/// <summary>True where the levelled ground of the circuit is (infield and band), for the land use.</summary>
		internal static bool Grounds(double x, double z)
		{
			return Fenced(x, z, 0.0);
		}

		/// <summary>True in the band round the lap (the run-off, the stands' ground and the pits), not the infield beyond it.</summary>
		internal static bool InBand(double x, double z)
		{
			if (!Exists || !Nearest(x, z, out int i, out double t, out double dist, out int side))
			{
				return false;
			}
			return dist <= (side > 0 ? BandAt(Along(i, t)) : Band);
		}

		/// <summary>The pit apron's outer edge (from the centre line) at distance s along the start straight; <see cref="Half"/> where there is none.</summary>
		internal static double ApronOut(double s)
		{
			if (s < PitA - PitTaper || s > PitB + PitTaper)
			{
				return Half;
			}
			if (s < PitA)
			{
				return Half + (PitLaneOut - Half) * Noise.Step(PitA - PitTaper, PitA, s);
			}
			if (s > PitB)
			{
				return Half + (PitLaneOut - Half) * (1.0 - Noise.Step(PitB, PitB + PitTaper, s));
			}
			return s >= PaddockA && s <= PaddockB ? PaddockOut : PitLaneOut;
		}

		/// <summary>What the circuit's surface is at (x, z).</summary>
		internal static CircuitSurface SurfaceAt(double x, double z)
		{
			if (!Exists || !Nearest(x, z, out int i, out double t, out double _, out int _))
			{
				return CircuitSurface.None;
			}
			Local(x, z, out double u, out double v);
			if (u >= 0.0 && u <= StraightLength && v >= Half && v <= ApronOut(u))
			{
				return CircuitSurface.Apron;
			}
			double d = Across(i, t, x, z);
			double a = Math.Abs(d);
			if (a <= Half)
			{
				return CircuitSurface.Track;
			}
			int q = (i + 1) % Count;
			double kerb = Kerb[i] + (Kerb[q] - Kerb[i]) * t;
			if (kerb > 0.0 && a <= Half + KerbWidth * kerb)
			{
				return CircuitSurface.Kerb;
			}
			double g = Math.Max(Gravel[i], Gravel[q]);
			int gs = Gravel[i] >= Gravel[q] ? GravelSide[i] : GravelSide[q];
			if (g > 0.0 && Math.Sign(d) == gs && a >= GravelIn && a <= GravelIn + (GravelOut - GravelIn) * g)
			{
				return CircuitSurface.Gravel;
			}
			return CircuitSurface.None;
		}

		/// <summary>True on any of the circuit's surfaces or buildings (with <paramref name="margin"/>), where no plant grows.</summary>
		internal static bool Covered(double x, double z, double margin)
		{
			if (!Exists || !Nearest(x, z, out int i, out double t, out double dist, out int _))
			{
				return false;
			}
			if (dist <= GravelOut + margin)
			{
				return true;
			}
			Local(x, z, out double u, out double v);
			if (u >= -margin && u <= StraightLength + margin && v >= Half - margin && v <= ApronOut(Math.Max(0.0, Math.Min(StraightLength, u))) + margin)
			{
				return true;
			}
			foreach (Stand s in Stands)
			{
				double dx = x - s.X, dz = z - s.Z;
				double along = Math.Abs(dx * -s.Fz + dz * s.Fx), across = Math.Abs(dx * s.Fx + dz * s.Fz);
				if (along <= s.Length * 0.5 + margin && across <= s.Depth * 0.5 + margin)
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>The barrier's stretches are left out along the pits (the garages and the pit wall are the barrier there).</summary>
		internal static bool BarrierAt(double s)
		{
			return s < PitA - PitTaper - 40.0 || s > PitB + PitTaper + 40.0;
		}

		/// <summary>The grid: slot k's position (two abreast, staggered, behind the start line), facing along the straight.</summary>
		internal static void Slot(int k, out double x, out double z)
		{
			double u = StartS - 14.0 - 13.0 * k;
			double v = (k % 2 == 0 ? -1.0 : 1.0) * 9.0;
			Straight(u, v, out x, out z);
		}
	}
}
