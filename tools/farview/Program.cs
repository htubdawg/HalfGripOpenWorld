using System;
using System.Diagnostics;
using System.IO;
using HalfGripOpenWorld;

// Checks the far view's low-detail world (FarGrid.cs) without the game and draws it.
//   dotnet run -c Release --project tools/farview -- [out.ppm] [reach]
// Builds the grid and the picture (and times it), checks that a second build of the same world gives the same grid (it is the
// generator's, so it must), then draws the whole world's picture (left) and three views along the ground, as the far view and the fog
// show them on a clear day out to <reach> (14000 by default): from the spawn along the road, from the highest road looking south over
// the land to the sea, and from the highest road near the coast looking north to the mountains. The views march rays over the grid with the canopy, light
// the picture from a morning sun and fade into the horizon's colour as the game's linear fog does. The near ground, which the loaded tiles
// draw in the game, is drawn here from the grid too. Exits 1 if a check fails.
internal static class Program
{
	private const int ViewW = 900;
	private const int ViewH = 300;

	private static int Main(string[] args)
	{
		string output = args.Length > 0 ? args[0] : "farview.ppm";
		float reach = args.Length > 1 ? float.Parse(args[1]) : 14000f;
		bool ok = true;

		Stopwatch watch = Stopwatch.StartNew();
		Network.Ensure();
		Console.WriteLine("Layout: " + watch.ElapsedMilliseconds + " ms");
		watch.Restart();
		FarGrid.Ensure();
		Console.WriteLine("Far grid: " + FarGrid.N + " samples a side every " + FarGrid.Step + ", picture " + FarGrid.Pixels + " a side every " + FarGrid.Pixel + ", built in "
			+ watch.ElapsedMilliseconds + " ms on " + Environment.ProcessorCount + " cores");
		long bytes = FarGrid.Top.Length * 8L + FarGrid.Colours.Length + (FarGrid.DipX.Length + FarGrid.DipZ.Length) * 4L;
		Console.WriteLine("Memory: " + (bytes / 1048576.0).ToString("0.0") + " MB (the picture freed once uploaded would save " + (FarGrid.Colours.Length / 1048576.0).ToString("0.0") + ")");

		// The same world twice: the grid's checksum is stable (the generator's determinism, through Parallel.For's rows in any order).
		ulong sum = Checksum();
		FarGrid.Forget();
		FarGrid.Ensure();
		bool same = Checksum() == sum;
		Console.WriteLine("Built twice, the same grid and picture (checksum " + sum.ToString("X16") + ")" + (same ? "  ok" : "  FAIL"));
		ok &= same;

		// Where to look from.
		int best = -1, bestI = 0;
		double coastX = 0.0, coastZ = 0.0, coastDx = 0.0, coastDz = 1.0, coastBest = double.MaxValue;
		for (int r = 0; r < Network.Roads.Count; r++)
		{
			Network.Road road = Network.Roads[r];
			if (road.Kind > RoadKind.Lane)
			{
				continue;
			}
			for (int i = 0; i + 1 < road.X.Length; i++)
			{
				if (best < 0 || road.Y[i] > Network.Roads[best].Y[bestI])
				{
					best = r;
					bestI = i;
				}
				double inland = WorldGen.Inland(road.X[i], road.Z[i]);
				// The highest road near the coast, to look inland over the country from.
				if (inland > 200.0 && inland < 3500.0 && Math.Abs(road.X[i]) < 10000.0 && -road.Y[i] < coastBest)
				{
					coastBest = -road.Y[i];
					coastX = road.X[i];
					coastZ = road.Z[i];
				}
			}
		}
		Network.Road high = Network.Roads[best];
		(string name, double x, double z, double dx, double dz)[] views =
		{
			("the spawn, along the road", Network.SpawnX, Network.SpawnZ, Network.SpawnDirX, Network.SpawnDirZ),
			("the highest road (" + high.Name + "), south", high.X[bestI], high.Z[bestI], 0.0, -1.0),
			("the highest road near the coast, north", coastX, coastZ, coastDx, coastDz),
		};

		// ---- the drawing: the picture on the left, the views on the right ----
		int mapSide = ViewH * views.Length;
		int width = mapSide + ViewW, height = mapSide;
		byte[] image = new byte[width * height * 3];
		int p = FarGrid.Pixels;
		for (int y = 0; y < mapSide; y++)
		{
			for (int x = 0; x < mapSide; x++)
			{
				int pi = x * p / mapSide, pj = (mapSide - 1 - y) * p / mapSide;
				int o = (pj * p + pi) * 4;
				int q = (y * width + x) * 3;
				image[q] = FarGrid.Colours[o];
				image[q + 1] = FarGrid.Colours[o + 1];
				image[q + 2] = FarGrid.Colours[o + 2];
			}
		}
		watch.Restart();
		for (int v = 0; v < views.Length; v++)
		{
			View(image, width, mapSide, v * ViewH, views[v].x, views[v].z, views[v].dx, views[v].dz, reach);
			// The place on the map.
			int mx = (int)((views[v].x + WorldGen.Half) / (WorldGen.Half * 2.0) * mapSide), my = mapSide - 1 - (int)((views[v].z + WorldGen.Half) / (WorldGen.Half * 2.0) * mapSide);
			for (int k = -4; k <= 4; k++)
			{
				Dot(image, width, mapSide, mx + k, my);
				Dot(image, width, mapSide, mx, my + k);
			}
			Console.WriteLine("View " + (v + 1) + ": " + views[v].name + " at (" + views[v].x.ToString("0") + ", " + views[v].z.ToString("0") + ")");
		}
		Console.WriteLine("Views drawn in " + watch.ElapsedMilliseconds + " ms");
		using (FileStream file = File.Create(output))
		{
			byte[] header = System.Text.Encoding.ASCII.GetBytes("P6\n" + width + " " + height + "\n255\n");
			file.Write(header, 0, header.Length);
			file.Write(image, 0, image.Length);
		}
		Console.WriteLine("Drew " + output);
		Console.WriteLine(ok ? "All checks passed" : "A CHECK FAILED");
		return ok ? 0 : 1;
	}

	private static ulong Checksum()
	{
		ulong h = 1469598103934665603UL;
		foreach (float f in FarGrid.Top)
		{
			h = (h ^ (ulong)BitConverter.SingleToInt32Bits(f)) * 1099511628211UL;
		}
		foreach (byte b in FarGrid.Colours)
		{
			h = (h ^ b) * 1099511628211UL;
		}
		return h;
	}

	private static void Dot(byte[] image, int width, int side, int x, int y)
	{
		if (x < 0 || y < 0 || x >= side || y >= side)
		{
			return;
		}
		int q = (y * width + x) * 3;
		image[q] = 255;
		image[q + 1] = 30;
		image[q + 2] = 30;
	}

	/// <summary>A view from 3 over the road at world (x, z) facing (dx, dz), 70 degrees across, rays marched over the grid's surface.</summary>
	private static void View(byte[] image, int width, int left, int top, double x, double z, double dx, double dz, float reach)
	{
		double l = Math.Sqrt(dx * dx + dz * dz);
		dx /= l;
		dz /= l;
		double eye = Surface(x, z, out _) + 3.0;
		double rx = dz, rz = -dx;
		double span = Math.Tan(35.0 * Math.PI / 180.0);
		// The sky: the painted sky's morning palette, the horizon the fog's colour.
		double[] horizon = { 0.72, 0.80, 0.90 }, zenith = { 0.22, 0.42, 0.78 };
		double sx = -0.5, sy = 0.6, sz = -0.62;
		double sl = Math.Sqrt(sx * sx + sy * sy + sz * sz);
		sx /= sl;
		sy /= sl;
		sz /= sl;
		double start = reach * 0.03;
		for (int py = 0; py < ViewH; py++)
		{
			for (int px = 0; px < ViewW; px++)
			{
				double a = (2.0 * (px + 0.5) / ViewW - 1.0) * span;
				double b = (1.0 - 2.0 * (py + 0.5) / ViewH) * span * ViewH / ViewW - 0.02;
				double vx = dx + rx * a, vy = b, vz = dz + rz * a;
				double vl = Math.Sqrt(vx * vx + vy * vy + vz * vz);
				vx /= vl;
				vy /= vl;
				vz /= vl;
				double[] c;
				double t = 2.0, hit = -1.0;
				while (t < reach * 1.05)
				{
					double hx = x + vx * t, hz = z + vz * t;
					if (Math.Abs(hx) > WorldGen.Half || Math.Abs(hz) > WorldGen.Half)
					{
						// Past the world: the sea to the south, else nothing (the edge walls).
						if (hz < -WorldGen.Half && vy < 0.0 && eye + vy * t <= Network.Lift)
						{
							hit = t;
						}
						break;
					}
					if (eye + vy * t <= Surface(hx, hz, out _))
					{
						hit = t;
						break;
					}
					t += Math.Max(1.0, t * 0.004);
				}
				if (hit < 0.0)
				{
					double f = Math.Sqrt(Math.Max(0.0, vy));
					c = new[] { horizon[0] + (zenith[0] - horizon[0]) * f, horizon[1] + (zenith[1] - horizon[1]) * f, horizon[2] + (zenith[2] - horizon[2]) * f };
				}
				else
				{
					double hx = x + vx * hit, hz = z + vz * hit;
					Surface(hx, hz, out double[] colour);
					double e = 2.0;
					double nx = Surface(hx - e, hz, out _) - Surface(hx + e, hz, out _), nz = Surface(hx, hz - e, out _) - Surface(hx, hz + e, out _);
					double nl = Math.Sqrt(nx * nx + 4.0 * e * e + nz * nz);
					double lambert = Math.Max(0.0, (nx * sx + 2.0 * e * sy + nz * sz) / nl);
					double light = 0.45 + 0.75 * lambert;
					double fog = Math.Max(0.0, Math.Min(1.0, (hit - start) / (reach - start)));
					c = new double[3];
					for (int k = 0; k < 3; k++)
					{
						c[k] = colour[k] * light + (horizon[k] - colour[k] * light) * fog;
					}
				}
				int q = ((top + py) * width + left + px) * 3;
				for (int k = 0; k < 3; k++)
				{
					image[q + k] = (byte)Math.Max(0, Math.Min(255, (int)(c[k] * 255.0)));
				}
			}
		}
	}

	/// <summary>The far view's surface height at world (x, z) (its triangles, as the meshes draw them) and the picture's colour there.</summary>
	private static double Surface(double x, double z, out double[] colour)
	{
		int n = FarGrid.N;
		double u = (x + WorldGen.Half) / FarGrid.Step, v = (z + WorldGen.Half) / FarGrid.Step;
		int i = Math.Max(0, Math.Min(n - 2, (int)Math.Floor(u))), j = Math.Max(0, Math.Min(n - 2, (int)Math.Floor(v)));
		double fu = u - i, fv = v - j;
		float[] t = FarGrid.Top;
		double a = t[j * n + i], b = t[j * n + i + 1], c = t[(j + 1) * n + i], d = t[(j + 1) * n + i + 1];
		// The quad's two triangles, split as FarView splits them (v, v + side, v + 1 / v + 1, v + side, v + side + 1).
		double h = fu + fv <= 1.0 ? a + (b - a) * fu + (c - a) * fv : d + (c - d) * (1.0 - fu) + (b - d) * (1.0 - fv);
		int p = FarGrid.Pixels;
		int pi = Math.Max(0, Math.Min(p - 1, (int)((x + WorldGen.Half) / FarGrid.Pixel))), pj = Math.Max(0, Math.Min(p - 1, (int)((z + WorldGen.Half) / FarGrid.Pixel)));
		int o = (pj * p + pi) * 4;
		colour = new[] { FarGrid.Colours[o] / 255.0, FarGrid.Colours[o + 1] / 255.0, FarGrid.Colours[o + 2] / 255.0 };
		return h;
	}
}
