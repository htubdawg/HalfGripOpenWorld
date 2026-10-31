using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace HalfGripOpenWorld
{
	/// <summary>
	/// The whole world at low detail, for the far view (<see cref="FarView"/>): the ground's height every <see cref="Step"/> units and a
	/// picture of it every <see cref="Pixel"/> units, worked out once in the background after the layout (it is the same world every
	/// time). Past the loaded tiles the far view draws this, so the mountains, the coast and the towns are there to the horizon.
	///
	/// The heights are <see cref="WorldGen.Height"/>'s at the same points the tiles' own vertices take (every tenth of a tile's 5-unit
	/// grid), so where the far view meets a loaded tile they share their vertices exactly. Water (the sea, lakes, a wide river) lifts a
	/// point to its surface, so the far view needs no water of its own to fight with the ground at a distance; woods stand
	/// <see cref="Canopy"/> over the ground, so a forest reads as a mass against the sky and not as a green stain. The picture is the
	/// tiles' own land use (<see cref="LandUse"/>) with the water, the lots, the circuit and the roads (each as much of its pixel as it
	/// covers) painted over it.
	///
	/// Pure arithmetic on the generator (no Unity types), so <c>tools/meshcheck</c> runs it; display only, nothing is driven on.
	/// </summary>
	internal static class FarGrid
	{
		/// <summary>Units between height samples: a tenth of a tile's 5-unit quads, and a fifth of a tile, so tile edges fall on samples.</summary>
		internal const double Step = 50.0;

		/// <summary>Samples along a tile's side (the streamer's 250-unit tiles): every fifth row and column of samples is a tile edge.</summary>
		internal const int PerTile = 5;

		/// <summary>Height samples along a side, edge to edge of the world square.</summary>
		internal static readonly int N = (int)Math.Round(WorldGen.Half * 2.0 / Step) + 1;

		/// <summary>Units a pixel of the picture covers (half a sample: a 25-unit pixel shows the main roads as lines).</summary>
		internal const double Pixel = 25.0;

		/// <summary>Pixels along a side of the picture.</summary>
		internal static readonly int Pixels = (int)Math.Round(WorldGen.Half * 2.0 / Pixel);

		/// <summary>How high woods stand over the ground in the far view: a little under the pines' height (30), as a canopy seen from afar.</summary>
		internal const float Canopy = 22f;

		/// <summary>How far the far view's skirts hang below the edges they hang from at the least, at the finest detail (more at coarser ones);
		/// on a tile's edge as deep as the real surface dips under it (<see cref="SkirtAt"/>), so no gap shows between a loaded tile and the far
		/// view (meshcheck farview).</summary>
		internal const float Skirt = 24f;

		/// <summary>The colour of open water in the picture: the tiles' water material's.</summary>
		private static readonly LandUse.Rgb WaterColour = new LandUse.Rgb(44, 79, 120);
		private static readonly LandUse.Rgb Asphalt = new LandUse.Rgb(84, 84, 88);
		private static readonly LandUse.Rgb Gravel = new LandUse.Rgb(150, 132, 104);
		private static readonly LandUse.Rgb LotColour = new LandUse.Rgb(150, 150, 155);
		private static readonly LandUse.Rgb Roof = new LandUse.Rgb(112, 92, 86);
		private static readonly LandUse.Rgb Kerb = new LandUse.Rgb(200, 60, 60);
		private static readonly LandUse.Rgb Sandtrap = new LandUse.Rgb(205, 190, 150);

		/// <summary>Scene heights (with <see cref="Network.Lift"/>) of the drawn surface, row by row from the south: the ground, raised to
		/// any water over it and by <see cref="Canopy"/> under woods.</summary>
		internal static float[] Top;

		/// <summary>The same without the canopy: the ground, or the water over it.</summary>
		internal static float[] Ground;

		/// <summary>
		/// Along the tile edges (every fifth row and column of samples), for each piece between two samples: how far the far view's
		/// straight edge there (its canopy too) stands over the real surface (the tiles' ground or the water drawn over it) at worst, at
		/// the tiles' 5-unit vertices between. Sharp mountain valleys dip up to 140 under it; a skirt hung there that deep hides the gap.
		/// <see cref="DipX"/> is for the rows (a piece along x), <see cref="DipZ"/> for the columns, each line's pieces in order.
		/// </summary>
		internal static float[] DipX, DipZ;

		/// <summary>The picture, RGBA, row by row from the south (Unity's bottom row first).</summary>
		internal static byte[] Colours;

		/// <summary>How long the build took, in milliseconds.</summary>
		internal static double BuildMs;

		private static volatile bool ready;
		private static readonly object gate = new object();

		/// <summary>True once <see cref="Ensure"/> has finished.</summary>
		internal static bool Ready => ready;

		/// <summary>World x (or z) of sample <paramref name="i"/>.</summary>
		internal static double At(int i)
		{
			return -WorldGen.Half + i * Step;
		}

		/// <summary>Builds the grid and the picture if they are not built (on a worker: about a second on four cores). Safe from any thread.</summary>
		internal static void Ensure()
		{
			if (ready)
			{
				return;
			}
			lock (gate)
			{
				if (ready)
				{
					return;
				}
				Network.Ensure();
				Stopwatch watch = Stopwatch.StartNew();
				int n = N;
				float lift = (float)Network.Lift;
				float[] land = new float[n * n];
				float[] ground = new float[n * n];
				Parallel.For(0, n, j =>
				{
					double z = At(j);
					for (int i = 0; i < n; i++)
					{
						double x = At(i);
						float h = (float)WorldGen.Height(x, z);
						land[j * n + i] = h;
						if (Rivers.Water(x, z, out double level) && level + lift > h)
						{
							h = (float)(level + lift);
						}
						ground[j * n + i] = h;
					}
				});

				int p = Pixels;
				byte[] colours = new byte[p * p * 4];
				bool[] woods = new bool[p * p];
				Parallel.For(0, p, pj =>
				{
					double z = -WorldGen.Half + (pj + 0.5) * Pixel;
					for (int pi = 0; pi < p; pi++)
					{
						double x = -WorldGen.Half + (pi + 0.5) * Pixel;
						Bilinear(land, n, x, z, out float h, out float sx, out float sz);
						Cover cover = LandUse.Classify(x, z, h - lift, Math.Sqrt(sx * sx + sz * sz), out int crop);
						LandUse.Rgb rgb = LandUse.Colour(cover, crop, x, z);
						woods[pj * p + pi] = cover == Cover.Forest;
						if (Rivers.Water(x, z, out double level) && level + lift > h)
						{
							rgb = WaterColour;
							woods[pj * p + pi] = false;
						}
						Town.Lot lot = Town.At(x, z, out bool building);
						if (lot != null)
						{
							rgb = building ? Roof : LotColour;
						}
						switch (Circuit.SurfaceAt(x, z))
						{
							case CircuitSurface.Track:
							case CircuitSurface.Apron:
								rgb = Asphalt;
								break;
							case CircuitSurface.Kerb:
								rgb = Kerb;
								break;
							case CircuitSurface.Gravel:
								rgb = Sandtrap;
								break;
						}
						int o = (pj * p + pi) * 4;
						colours[o] = rgb.R;
						colours[o + 1] = rgb.G;
						colours[o + 2] = rgb.B;
						colours[o + 3] = 255;
					}
				});
				Roads(colours);

				// The canopy: a sample stands up by the share of woods in the 4 by 4 pixels round it (100 units), so a forest's edge slopes.
				float[] top = new float[n * n];
				Parallel.For(0, n, j =>
				{
					for (int i = 0; i < n; i++)
					{
						int count = 0, all = 0;
						for (int q = 2 * j - 2; q <= 2 * j + 1; q++)
						{
							for (int r = 2 * i - 2; r <= 2 * i + 1; r++)
							{
								if (q < 0 || r < 0 || q >= p || r >= p)
								{
									continue;
								}
								all++;
								if (woods[q * p + r])
								{
									count++;
								}
							}
						}
						top[j * n + i] = ground[j * n + i] + (all > 0 ? Canopy * count / all : 0f);
					}
				});

				// The dips along the tile edges.
				int per = PerTile, lines = (n - 1) / per + 1, sub = (int)Math.Round(Step / 5.0);
				float[] dipX = new float[lines * (n - 1)], dipZ = new float[lines * (n - 1)];
				Parallel.For(0, lines, l =>
				{
					double along = At(l * per);
					for (int q = 0; q < n - 1; q++)
					{
						float wx = 0f, wz = 0f;
						for (int k = 1; k < sub; k++)
						{
							double f = k / (double)sub;
							double across = At(q) + Step * f;
							float lineX = top[l * per * n + q] + (top[l * per * n + q + 1] - top[l * per * n + q]) * (float)f;
							wx = Math.Max(wx, lineX - Real(across, along, lift));
							float lineZ = top[q * n + l * per] + (top[(q + 1) * n + l * per] - top[q * n + l * per]) * (float)f;
							wz = Math.Max(wz, lineZ - Real(along, across, lift));
						}
						dipX[l * (n - 1) + q] = wx;
						dipZ[l * (n - 1) + q] = wz;
					}
				});

				DipX = dipX;
				DipZ = dipZ;
				Ground = ground;
				Top = top;
				Colours = colours;
				BuildMs = watch.Elapsed.TotalMilliseconds;
				ready = true;
			}
		}

		/// <summary>The real surface at world (x, z), as the tiles draw it at their vertices: the ground, or the water over it.</summary>
		private static float Real(double x, double z, float lift)
		{
			float h = (float)WorldGen.Height(x, z);
			if (Rivers.Water(x, z, out double level) && level + lift > h)
			{
				h = (float)(level + lift);
			}
			return h;
		}

		/// <summary>
		/// How deep a skirt must hang under the far view's edge between neighbouring samples (i0, j0) and (i1, j1) to reach the real surface
		/// everywhere along it (see <see cref="DipX"/>), at least <see cref="Skirt"/>. Off the tile edges, just <see cref="Skirt"/>.
		/// </summary>
		internal static float SkirtAt(int i0, int j0, int i1, int j1)
		{
			const int per = PerTile;
			int n = N;
			float dip = 0f;
			if (j0 == j1 && j0 % per == 0 && Math.Abs(i1 - i0) == 1)
			{
				dip = DipX[j0 / per * (n - 1) + Math.Min(i0, i1)];
			}
			else if (i0 == i1 && i0 % per == 0 && Math.Abs(j1 - j0) == 1)
			{
				dip = DipZ[i0 / per * (n - 1) + Math.Min(j0, j1)];
			}
			return Math.Max(Skirt, dip + 2f);
		}

		/// <summary>Forgets the grid so the next <see cref="Ensure"/> builds it again: only for <c>tools/farview</c>, which builds it twice to
		/// check it comes out the same (its rows are worked out in parallel, in any order).</summary>
		internal static void Forget()
		{
			lock (gate)
			{
				ready = false;
			}
		}

		/// <summary>A height grid's value and slopes (rise over run) at world (x, z), read between its samples.</summary>
		private static void Bilinear(float[] grid, int n, double x, double z, out float h, out float sx, out float sz)
		{
			double u = (x + WorldGen.Half) / Step, v = (z + WorldGen.Half) / Step;
			int i = Math.Max(0, Math.Min(n - 2, (int)Math.Floor(u)));
			int j = Math.Max(0, Math.Min(n - 2, (int)Math.Floor(v)));
			float fu = (float)(u - i), fv = (float)(v - j);
			float a = grid[j * n + i], b = grid[j * n + i + 1], c = grid[(j + 1) * n + i], d = grid[(j + 1) * n + i + 1];
			float bottom = a + (b - a) * fu, topRow = c + (d - c) * fu;
			h = bottom + (topRow - bottom) * fv;
			sx = ((b - a) + (d - c)) * 0.5f / (float)Step;
			sz = ((c - a) + (d - b)) * 0.5f / (float)Step;
		}

		/// <summary>
		/// The roads over the picture, the small ones first: each pixel takes the road's colour by the share of it the road covers (its
		/// paved width across a 25-unit pixel), so a lane is a faint line and the trunk road a solid one. Tracks and footpaths are left
		/// out: from afar they do not show.
		/// </summary>
		private static void Roads(byte[] colours)
		{
			int p = Pixels;
			for (int kind = (int)RoadKind.Climb; kind >= 0; kind--)
			{
				RoadKind k = (RoadKind)kind;
				if (k == RoadKind.Track || k == RoadKind.Path)
				{
					continue;
				}
				LandUse.Rgb colour = k == RoadKind.Rally ? Gravel : Asphalt;
				foreach (Network.Road road in Network.Roads)
				{
					if (road.Kind != k)
					{
						continue;
					}
					for (int s = 0; s + 1 < road.X.Length; s++)
					{
						double reach = road.Half + Pixel;
						int p0 = Math.Max(0, (int)Math.Floor((Math.Min(road.X[s], road.X[s + 1]) - reach + WorldGen.Half) / Pixel));
						int p1 = Math.Min(p - 1, (int)Math.Floor((Math.Max(road.X[s], road.X[s + 1]) + reach + WorldGen.Half) / Pixel));
						int q0 = Math.Max(0, (int)Math.Floor((Math.Min(road.Z[s], road.Z[s + 1]) - reach + WorldGen.Half) / Pixel));
						int q1 = Math.Min(p - 1, (int)Math.Floor((Math.Max(road.Z[s], road.Z[s + 1]) + reach + WorldGen.Half) / Pixel));
						for (int q = q0; q <= q1; q++)
						{
							for (int r = p0; r <= p1; r++)
							{
								double x = -WorldGen.Half + (r + 0.5) * Pixel, z = -WorldGen.Half + (q + 0.5) * Pixel;
								Lines.Project(road.X, road.Z, s, x, z, out double _, out double d);
								// An antialiased line as wide as the paving: full where the pixel is all road, a share at its edges.
								double cover = Math.Max(0.0, Math.Min(1.0, (road.Half + Pixel * 0.5 - d) / Pixel)) * Math.Min(1.0, road.Half * 2.0 / Pixel);
								if (cover <= 0.0)
								{
									continue;
								}
								int o = (q * p + r) * 4;
								// Pieces overlap at their joints: the most a pixel was covered stays, not the sum (alpha remembers it).
								double had = (255 - colours[o + 3]) / 255.0;
								if (cover <= had)
								{
									continue;
								}
								double f = (cover - had) / (1.0 - had);
								colours[o] = (byte)Math.Round(colours[o] + (colour.R - colours[o]) * f);
								colours[o + 1] = (byte)Math.Round(colours[o + 1] + (colour.G - colours[o + 1]) * f);
								colours[o + 2] = (byte)Math.Round(colours[o + 2] + (colour.B - colours[o + 2]) * f);
								colours[o + 3] = (byte)Math.Round(255 - cover * 255.0);
							}
						}
					}
				}
			}
			for (int o = 3; o < colours.Length; o += 4)
			{
				colours[o] = 255;
			}
		}
	}
}
