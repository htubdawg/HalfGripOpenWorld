using System;
using System.Diagnostics;

namespace HalfGripOpenWorld
{
	/// <summary>
	/// Filling a <see cref="Skyline"/>: rays marched out over <see cref="FarGrid"/>'s surface from the eye, one a column of the painted
	/// sky. Pure arithmetic on the grid (no Unity types), so <c>tools/farview</c> builds it and checks it against views drawn to the fog's
	/// end; display only.
	/// </summary>
	internal sealed partial class Skyline
	{
		/// <summary>How far out a ray goes: past the far corner of the world from anywhere in it, and down the sea to the south.</summary>
		internal const double Farthest = 60000.0;

		/// <summary>The earth's radius in game units (6370 km at 3.3 a metre): the land 40 km off sinks under the eye by about 40.</summary>
		private const double Earth = 2.1e7;

		/// <summary>Cells of <see cref="FarGrid"/>'s picture averaged into one of the painted land's colours (200 units): the far view's
		/// mipmapped picture is as soft at those distances, and single pixels would shimmer from one build to the next.</summary>
		private const int Coarse = 8;

		/// <summary>The tangent of each band row's height, and each column's way across (x east, z north), as the panoramic shader maps
		/// them (see <see cref="SkyPaint"/>).</summary>
		private static readonly double[] RowTan = new double[Rows];
		private static readonly double[] ColX = new double[SkyPaint.W], ColZ = new double[SkyPaint.W];

		private static volatile float[] coarse;
		private static int coarseN;
		private static readonly object gate = new object();

		static Skyline()
		{
			for (int k = 0; k < Rows; k++)
			{
				double v = (Lowest + k + 0.5) / SkyPaint.H;
				RowTan[k] = Math.Tan((v - 0.5) * Math.PI);
			}
			for (int col = 0; col < SkyPaint.W; col++)
			{
				double phi = (0.5 - (col + 0.5) / SkyPaint.W) * 2.0 * Math.PI;
				ColX[col] = Math.Cos(phi);
				ColZ[col] = Math.Sin(phi);
			}
		}

		/// <summary>
		/// Fills this with the land seen from world (<paramref name="x"/>, <paramref name="z"/>) at scene height <paramref name="eye"/>,
		/// past <paramref name="from"/>. Each column steps out over the far view's surface (its heights with the canopy, sunk by the earth's
		/// curve), keeping the highest the land has stood so far: every row it passes takes the land there. Past the world's south edge the
		/// sea goes on; past the other edges (the walls) nothing. About 1024 columns of 300 to 600 steps; on a worker. <see cref="FarGrid"/>
		/// must be built.
		/// </summary>
		internal void Build(double x, double z, double eye, double from)
		{
			Stopwatch watch = Stopwatch.StartNew();
			Coarsen();
			X = x;
			Z = z;
			Eye = eye;
			From = from;
			Array.Clear(Dist, 0, Dist.Length);
			int hits = 0;
			int n = FarGrid.N;
			float[] top = FarGrid.Top;
			double half = WorldGen.Half;
			double sea = WorldGen.SeaLevel + Network.Lift;
			from = Math.Max(from, 100.0);
			for (int col = 0; col < SkyPaint.W; col++)
			{
				double dx = ColX[col], dz = ColZ[col];
				int k = 0;
				double d = from;
				while (d <= Farthest && k < Rows)
				{
					double px = x + dx * d, pz = z + dz * d;
					if (Math.Abs(px) > half || pz > half)
					{
						break;
					}
					bool water = pz < -half;
					// Out on the sea past the south edge and heading on south, with only rows above the horizon left: the sea, under the eye,
					// never rises to one, so the rest of the ray is wasted (at the spawn, half of every build's steps).
					if (water && k >= Band && dz <= 0.0 && eye >= sea)
					{
						break;
					}
					double h = water ? sea : Height(top, n, px, pz);
					double t = (h - d * d / (2.0 * Earth) - eye) / d;
					if (RowTan[k] <= t)
					{
						float nx = 0f, ny = 1f, nz = 0f;
						byte r, g, b;
						if (water)
						{
							r = 44;
							g = 79;
							b = 120;
						}
						else
						{
							Slope(top, n, px, pz, out nx, out ny, out nz);
							Paint(px, pz, out r, out g, out b);
						}
						do
						{
							int i = k * SkyPaint.W + col;
							Dist[i] = (float)d;
							Colour[i * 4] = r;
							Colour[i * 4 + 1] = g;
							Colour[i * 4 + 2] = b;
							Colour[i * 4 + 3] = water ? (byte)0 : (byte)255;
							Normal[i * 3] = nx;
							Normal[i * 3 + 1] = ny;
							Normal[i * 3 + 2] = nz;
							hits++;
							k++;
						}
						while (k < Rows && RowTan[k] <= t);
					}
					// A step a little under a texel's height at this distance, never under half a grid sample.
					d += Math.Max(25.0, d * 0.005);
				}
			}
			Hits = hits;
			Ms = watch.Elapsed.TotalMilliseconds;
		}

		/// <summary>The far view's surface at world (x, z), read between the grid's samples.</summary>
		private static double Height(float[] top, int n, double x, double z)
		{
			double u = (x + WorldGen.Half) / FarGrid.Step, v = (z + WorldGen.Half) / FarGrid.Step;
			int i = Math.Max(0, Math.Min(n - 2, (int)Math.Floor(u)));
			int j = Math.Max(0, Math.Min(n - 2, (int)Math.Floor(v)));
			double fu = u - i, fv = v - j;
			double a = top[j * n + i], b = top[j * n + i + 1], c = top[(j + 1) * n + i], d = top[(j + 1) * n + i + 1];
			double bottom = a + (b - a) * fu, upper = c + (d - c) * fu;
			return bottom + (upper - bottom) * fv;
		}

		/// <summary>The surface's normal at world (x, z) over 200 units: about the far view's own at those distances (its coarsest detail
		/// takes every eighth sample, 400 apart).</summary>
		private static void Slope(float[] top, int n, double x, double z, out float nx, out float ny, out float nz)
		{
			const double e = 100.0;
			double hx = Height(top, n, x + e, z) - Height(top, n, x - e, z);
			double hz = Height(top, n, x, z + e) - Height(top, n, x, z - e);
			double l = Math.Sqrt(hx * hx + 4.0 * e * e + hz * hz);
			nx = (float)(-hx / l);
			ny = (float)(2.0 * e / l);
			nz = (float)(-hz / l);
		}

		/// <summary>The softened picture's colour at world (x, z), read between its cells.</summary>
		private static void Paint(double x, double z, out byte r, out byte g, out byte b)
		{
			int m = coarseN;
			double cell = FarGrid.Pixel * Coarse;
			double u = (x + WorldGen.Half) / cell - 0.5, v = (z + WorldGen.Half) / cell - 0.5;
			int i = Math.Max(0, Math.Min(m - 2, (int)Math.Floor(u)));
			int j = Math.Max(0, Math.Min(m - 2, (int)Math.Floor(v)));
			float fu = (float)Math.Max(0.0, Math.Min(1.0, u - i)), fv = (float)Math.Max(0.0, Math.Min(1.0, v - j));
			float[] c = coarse;
			int o00 = (j * m + i) * 3, o10 = o00 + 3, o01 = o00 + m * 3, o11 = o01 + 3;
			float Mix(int k)
			{
				float bottom = c[o00 + k] + (c[o10 + k] - c[o00 + k]) * fu;
				float upper = c[o01 + k] + (c[o11 + k] - c[o01 + k]) * fu;
				return bottom + (upper - bottom) * fv;
			}
			r = (byte)Math.Round(Mix(0));
			g = (byte)Math.Round(Mix(1));
			b = (byte)Math.Round(Mix(2));
		}

		/// <summary>Averages <see cref="FarGrid"/>'s picture into cells of <see cref="Coarse"/> pixels, once (it is the same world every time).</summary>
		private static void Coarsen()
		{
			if (coarse != null)
			{
				return;
			}
			lock (gate)
			{
				if (coarse != null)
				{
					return;
				}
				int p = FarGrid.Pixels, m = p / Coarse;
				byte[] colours = FarGrid.Colours;
				float[] c = new float[m * m * 3];
				for (int j = 0; j < m; j++)
				{
					for (int i = 0; i < m; i++)
					{
						float r = 0f, g = 0f, b = 0f;
						for (int q = 0; q < Coarse; q++)
						{
							for (int s = 0; s < Coarse; s++)
							{
								int o = ((j * Coarse + q) * p + i * Coarse + s) * 4;
								r += colours[o];
								g += colours[o + 1];
								b += colours[o + 2];
							}
						}
						float k = 1f / (Coarse * Coarse);
						c[(j * m + i) * 3] = r * k;
						c[(j * m + i) * 3 + 1] = g * k;
						c[(j * m + i) * 3 + 2] = b * k;
					}
				}
				coarseN = m;
				coarse = c;
			}
		}
	}
}
