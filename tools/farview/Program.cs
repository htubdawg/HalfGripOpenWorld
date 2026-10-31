using System;
using System.Diagnostics;
using System.IO;
using HalfGripOpenWorld;

#pragma warning disable CS0649

// Checks the far view's low-detail world (FarGrid.cs) and the land painted past it into the sky (Skyline.cs, SkyPaint.cs) without the
// game, and draws them.
//   dotnet run -c Release --project tools/farview -- [out.ppm] [drawn]
// Builds the grid and the picture (and times it), checks that a second build of the same world gives the same grid (it is the
// generator's, so it must), then draws the whole world's picture (left) and three views along the ground on a clear morning, the fog
// ending at 40000: from the spawn along the road, from the highest road looking south over the land to the sea, and from the highest
// road near the coast looking north to the mountains. Each view is drawn twice: in the middle, rays marched over the grid's surface out to
// the fog's end (the reference, as if the far view were drawn that far); on the right, as the game shows it: the far view out to <drawn>
// (FarViewDistance, 14000 by default) and past that the painted sky with the skyline's land in it, read a texel at a time. Both are lit
// with SkyPaint.Shade and fade into the horizon's colour as the game's linear fog does. Checks that the skyline builds the same twice,
// that past the drawn distance the painted land stands where the reference's does (few holes or ghosts, at the outlines) in near colours,
// and times a skyline build and what the land adds to the sky's painting. The near ground, which the loaded tiles draw in the game, is
// drawn here from the grid too. Exits 1 if a check fails.
internal static class Program
{
	private const int ViewW = 900;
	private const int ViewH = 300;

	/// <summary>How far one sees on the clearest day with the land past the far view painted into the sky (Sky.ClearSight).</summary>
	private const float Sight = 40000f;

	private static int Main(string[] args)
	{
		string output = args.Length > 0 ? args[0] : "farview.ppm";
		float drawn = args.Length > 1 ? float.Parse(args[1]) : 14000f;
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

		// ---- the land past the far view, painted into the sky (Skyline, SkyPaint) ----
		// A clear morning: the sky's state and the far view's light as Sky would hand them over, the fog out to the clear sight.
		SkyPaint.State state = new SkyPaint.State { Cloud = 0f, Coverage = 0.1f, DayHaze = new SkyPaint.Rgb(0.41f, 0.42f, 0.44f), NightHaze = new SkyPaint.Rgb(0.10f, 0.13f, 0.16f) };
		SkyPaint.Place(ref state, 10f, out _);
		state.Reach = Math.Min(20000f, Sight);
		SkyPaint.Rgb side = new SkyPaint.Rgb(0.22f, 0.24f, 0.28f);
		SkyPaint.LandLight light = new SkyPaint.LandLight
		{
			Linear = true,
			East = side, West = side, North = side, South = side,
			Up = new SkyPaint.Rgb(0.32f, 0.36f, 0.45f),
			Down = new SkyPaint.Rgb(0.09f, 0.09f, 0.09f),
			SunX = state.SunX, SunY = state.SunY, SunZ = state.SunZ,
			Sun = new SkyPaint.Rgb(1.2f, 1.1f, 0.95f),
			Tint = 1f,
			Reflect = 1f,
			FogStart = Sight * 0.03f,
			FogEnd = Sight,
		};
		state.Light = light;
		SkyPaint.Palette(state, out _, out SkyPaint.Rgb horizon, out _);
		SkyPaint.Rgb fogColour = SkyPaint.FogColour(light, horizon);
		SkyPaint painter = new SkyPaint(12345);
		painter.All(state);
		byte[] plain = (byte[])painter.Pixels.Clone();

		// ---- the drawing: the picture on the left, each view drawn to the fog's end (as if the far view reached it), and as the game
		// shows it: the far view to its drawn distance and the land past it painted into the sky ----
		int mapSide = ViewH * views.Length;
		int width = mapSide + ViewW * 2, height = mapSide;
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
		double buildMs = 0.0, paintMs = 0.0;
		int builds = 0;
		for (int v = 0; v < views.Length; v++)
		{
			double eye = Surface(views[v].x, views[v].z, out _) + 3.0;
			Skyline line = new Skyline();
			line.Build(views[v].x, views[v].z, eye, drawn * 0.9);
			buildMs += line.Ms;
			builds++;
			if (v == 0)
			{
				// Built twice from the same point, the same land (it is a pure function of the grid and the point).
				Skyline again = new Skyline();
				again.Build(views[v].x, views[v].z, eye, drawn * 0.9);
				bool alike = true;
				for (int i = 0; i < line.Dist.Length && alike; i++)
				{
					alike = line.Dist[i] == again.Dist[i];
				}
				for (int i = 0; i < line.Colour.Length && alike; i++)
				{
					alike = line.Colour[i] == again.Colour[i];
				}
				Console.WriteLine("Skyline built twice from the same point, the same land" + (alike ? "  ok" : "  FAIL"));
				ok &= alike;
			}
			state.Land = line;
			Stopwatch paint = Stopwatch.StartNew();
			painter.All(state);
			paintMs += paint.Elapsed.TotalMilliseconds;
			byte[] landed = (byte[])painter.Pixels.Clone();
			Stats stats = View(image, width, mapSide, v * ViewH, views[v].x, views[v].z, views[v].dx, views[v].dz, eye, drawn, light, fogColour, plain, landed);
			// The place on the map.
			int mx = (int)((views[v].x + WorldGen.Half) / (WorldGen.Half * 2.0) * mapSide), my = mapSide - 1 - (int)((views[v].z + WorldGen.Half) / (WorldGen.Half * 2.0) * mapSide);
			for (int k = -4; k <= 4; k++)
			{
				Dot(image, width, mapSide, mx + k, my);
				Dot(image, width, mapSide, mx, my + k);
			}
			Console.WriteLine("View " + (v + 1) + ": " + views[v].name + " at (" + views[v].x.ToString("0") + ", " + views[v].z.ToString("0") + "), skyline " + line.Hits + " texels in " + line.Ms.ToString("0") + " ms");
			// Past the drawn distance the painted land stands where the land drawn to the fog's end would, within a texel of the sky (0.35
			// degrees, coarser than a pixel of the view, 0.08): hardly a pixel of land with no land painted there (holes) or of sky with land
			// painted all round (ghosts); and inside the outlines its colours are near.
			double holes = stats.Land > 0 ? stats.Holes / (double)stats.Land : 0.0;
			double ghosts = stats.Sky > 0 ? stats.Ghosts / (double)stats.Sky : 0.0;
			double diff = stats.Inside > 0 ? stats.Diff / stats.Inside : 0.0;
			// (The colours by 15 at most: the reference takes the picture over 200 units at the point a ray meets, sharper than the far
			// view's mipmapped picture is at 20 or 30 km, and the skyline its 200-unit cells read between, so snow on a far ridge differs.)
			bool fine = holes < 0.01 && ghosts < 0.002 && diff < 15.0;
			Console.WriteLine("  past " + drawn.ToString("0") + ": " + stats.Land + " pixels of land in the fog's reach, " + (holes * 100.0).ToString("0.00") + "% with no land painted, "
				+ (ghosts * 100.0).ToString("0.00") + "% of the sky's " + stats.Sky + " with land painted, colours inside off by " + diff.ToString("0.0") + "/255" + (fine ? "  ok" : "  FAIL"));
			ok &= fine;
		}
		Console.WriteLine("Skyline: " + (buildMs / builds).ToString("0") + " ms a build (on a worker in the game), a picture with it " + (paintMs / builds).ToString("0.0") + " ms; views drawn in " + watch.ElapsedMilliseconds + " ms");

		// ---- a storm between the car and the far land: its rain hangs in front of the land, as in front of the sky ----
		{
			double cx = views[2].x, cz = views[2].z;
			Skyline line = new Skyline();
			line.Build(cx, cz, Surface(cx, cz, out _) + 3.0, drawn * 0.9);
			SkyPaint.State dry = state;
			dry.Land = line;
			dry.CarX = (float)cx;
			dry.CarZ = (float)cz;
			SkyPaint.State wet = dry;
			wet.Storm = Storm(cx, cz, cx, cz + 9000.0, 2500.0);
			painter.All(dry);
			byte[] without = (byte[])painter.Pixels.Clone();
			painter.All(wet);
			// Toward the storm (north), the land's texels in the band: greyed behind the rain; away from it (south), untouched.
			double toward = 0.0, away = 0.0;
			int nToward = 0, nAway = 0;
			int north = SkyPaint.W / 4, south = SkyPaint.W * 3 / 4;
			for (int k = 0; k < Skyline.Rows; k++)
			{
				int row = Skyline.Lowest + k;
				for (int dc = -6; dc <= 6; dc++)
				{
					foreach (bool isNorth in new[] { true, false })
					{
						int col = (isNorth ? north : south) + dc;
						if (line.Dist[k * SkyPaint.W + col] <= drawn)
						{
							continue;
						}
						int t = (row * SkyPaint.W + col) * 4;
						double d = Math.Abs(painter.Pixels[t] - without[t]) + Math.Abs(painter.Pixels[t + 1] - without[t + 1]) + Math.Abs(painter.Pixels[t + 2] - without[t + 2]);
						if (isNorth)
						{
							toward += d;
							nToward++;
						}
						else
						{
							away += d;
							nAway++;
						}
					}
				}
			}
			toward = nToward > 0 ? toward / nToward : 0.0;
			away = nAway > 0 ? away / nAway : 0.0;
			bool hidden = nToward > 0 && toward > 30.0 && away < 1.0;
			Console.WriteLine("A storm 9 km north of view 3: the land past it changed by " + toward.ToString("0") + "/765 (" + nToward + " texels), the land south of the car by "
				+ away.ToString("0.0") + " (" + nAway + ")" + (hidden ? "  ok" : "  FAIL"));
			ok &= hidden;
		}

		// What painting the land adds to a frame's sky painting (the rows in the skyline's band; the lower half, all at the end of a picture).
		{
			Skyline line = new Skyline();
			line.Build(views[2].x, views[2].z, Surface(views[2].x, views[2].z, out _) + 3.0, drawn * 0.9);
			double[] perFrame = new double[2];
			double[] worst = new double[2];
			for (int pass = 0; pass < 2; pass++)
			{
				state.Land = pass == 0 ? null : line;
				painter.All(state);
				Stopwatch all = Stopwatch.StartNew();
				int frames = 0, pictures = 0;
				while (pictures < 20)
				{
					Stopwatch one = Stopwatch.StartNew();
					if (painter.Frame(state))
					{
						pictures++;
					}
					worst[pass] = Math.Max(worst[pass], one.Elapsed.TotalMilliseconds);
					frames++;
				}
				perFrame[pass] = all.Elapsed.TotalMilliseconds / frames;
			}
			Console.WriteLine("Sky painting a frame: " + perFrame[0].ToString("0.00") + " ms (worst " + worst[0].ToString("0.00") + "), with the land " + perFrame[1].ToString("0.00") + " ms (worst " + worst[1].ToString("0.00") + ")");
		}

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

	/// <summary>What a view's two drawings say about the land past the drawn distance.</summary>
	private struct Stats
	{
		/// <summary>Pixels of land past the drawn distance and short of the fog's last tenth in the reference; of those, sky in the game's.</summary>
		internal int Land, Holes;
		/// <summary>Pixels of sky in the reference; of those, land painted in the game's.</summary>
		internal int Sky, Ghosts;
		/// <summary>Pixels of <see cref="Land"/> inside the painted outlines, and the sum over them of the largest channel's difference, in 255ths.</summary>
		internal int Inside;
		internal double Diff;
	}

	/// <summary>
	/// A view from <paramref name="eye"/> over world (x, z) facing (dx, dz), 70 degrees across, drawn twice: left, rays marched over the
	/// far view's surface out to the fog's end (the reference: as if the meshes were drawn that far); right, as the game shows it, marched
	/// to <paramref name="drawn"/> and past that the painted sky with the land in it (<paramref name="landed"/>), as the sky shader reads
	/// it, a texel at a time. Both lit with <see cref="SkyPaint.Shade"/>; the reference past the drawn distance takes the colours and slopes
	/// as softly as the skyline does, so only where the land stands is compared, not how finely it is drawn.
	/// </summary>
	private static Stats View(byte[] image, int width, int left, int top, double x, double z, double dx, double dz, double eye, float drawn,
		SkyPaint.LandLight light, SkyPaint.Rgb fogColour, byte[] plain, byte[] landed)
	{
		double l = Math.Sqrt(dx * dx + dz * dz);
		dx /= l;
		dz /= l;
		double rx = dz, rz = -dx;
		double span = Math.Tan(35.0 * Math.PI / 180.0);
		float clear = light.FogStart + 0.9f * (light.FogEnd - light.FogStart);
		Stats[] rows = new Stats[ViewH];
		System.Threading.Tasks.Parallel.For(0, ViewH, py =>
		{
			Stats st = new Stats();
			for (int px = 0; px < ViewW; px++)
			{
				double a = (2.0 * (px + 0.5) / ViewW - 1.0) * span;
				double b = (1.0 - 2.0 * (py + 0.5) / ViewH) * span * ViewH / ViewW - 0.02;
				double vx = dx + rx * a, vy = b, vz = dz + rz * a;
				double vl = Math.Sqrt(vx * vx + vy * vy + vz * vz);
				vx /= vl;
				vy /= vl;
				vz /= vl;
				double hit = March(x, z, eye, vx, vy, vz, light.FogEnd * 1.05, out bool sea);
				int col = (int)Math.Floor((0.5 - Math.Atan2(vz, vx) / (2.0 * Math.PI)) * SkyPaint.W);
				col = ((col % SkyPaint.W) + SkyPaint.W) % SkyPaint.W;
				int row = Math.Max(0, Math.Min(SkyPaint.H - 1, (int)Math.Floor((1.0 - Math.Acos(Math.Max(-1.0, Math.Min(1.0, vy))) / Math.PI) * SkyPaint.H)));
				int t = (row * SkyPaint.W + col) * 4;
				byte[] reference = Pixel(x, z, vx, vz, hit, sea, drawn, light, fogColour, plain, t);
				byte[] game = hit >= 0.0 && hit < drawn ? reference : new[] { landed[t], landed[t + 1], landed[t + 2] };
				// A texel of the sky is about four pixels of the view, so the outlines are a texel out either way: a hole is land with no land
				// painted within a texel up or down, a ghost sky with nothing but land there; the colours are compared inside the outlines.
				bool painted = Painted(landed, plain, row, col), above = Painted(landed, plain, row + 1, col), below = Painted(landed, plain, row - 1, col);
				if (hit >= drawn && hit < clear)
				{
					st.Land++;
					if (!painted && !above && !below)
					{
						st.Holes++;
					}
					if (painted && above && below)
					{
						st.Inside++;
						st.Diff += Math.Max(Math.Abs(game[0] - reference[0]), Math.Max(Math.Abs(game[1] - reference[1]), Math.Abs(game[2] - reference[2])));
					}
				}
				else if (hit < 0.0)
				{
					st.Sky++;
					if (painted && above && below)
					{
						st.Ghosts++;
					}
				}
				int q1 = ((top + py) * width + left + px) * 3, q2 = q1 + ViewW * 3;
				for (int k = 0; k < 3; k++)
				{
					image[q1 + k] = reference[k];
					image[q2 + k] = game[k];
				}
			}
			rows[py] = st;
		});
		Stats all = new Stats();
		foreach (Stats st in rows)
		{
			all.Land += st.Land;
			all.Holes += st.Holes;
			all.Sky += st.Sky;
			all.Ghosts += st.Ghosts;
			all.Inside += st.Inside;
			all.Diff += st.Diff;
		}
		return all;
	}

	/// <summary>A storm field round the car at (cx, cz) with one storm at (x, z) of radius r, shaped as Storms shapes a cell at full strength
	/// (as tools/skyview makes one).</summary>
	private static StormField Storm(double cx, double cz, double x, double z, double r)
	{
		StormField f = new StormField(64, 800.0, cx, cz);
		for (int j = 0; j < f.N; j++)
		{
			for (int i = 0; i < f.N; i++)
			{
				double dx = f.X0 + i * f.Spacing - x, dz = f.Z0 + j * f.Spacing - z;
				double d = Math.Sqrt(dx * dx + dz * dz) / r;
				double cloud = 1.0 - Noise.Step(0.6, 1.8, d);
				double rain = d < 1.0 ? Noise.Step(0.1, 0.85, 1.0 - Noise.Step(0.3, 1.0, d)) : 0.0;
				f.Cloud[j * f.N + i] = (float)Math.Max(cloud, rain);
				f.Rain[j * f.N + i] = (float)rain;
			}
		}
		f.Measure();
		return f;
	}

	/// <summary>True if the skyline painted the sky's texel at (row, col): it differs from the sky with no land.</summary>
	private static bool Painted(byte[] landed, byte[] plain, int row, int col)
	{
		if (row < 0 || row >= SkyPaint.H)
		{
			return false;
		}
		int t = (row * SkyPaint.W + col) * 4;
		return landed[t] != plain[t] || landed[t + 1] != plain[t + 1] || landed[t + 2] != plain[t + 2];
	}

	/// <summary>How far along a ray from (x, eye, z) the far view's surface is met, out to <paramref name="reach"/>; -1 for none. Past the
	/// world's south edge the sea, past the others nothing (the edge walls). The earth's curve as the skyline takes it.</summary>
	private static double March(double x, double z, double eye, double vx, double vy, double vz, double reach, out bool sea)
	{
		sea = false;
		double t = 2.0;
		while (t < reach)
		{
			double hx = x + vx * t, hz = z + vz * t;
			double drop = t * t / (2.0 * 2.1e7);
			if (Math.Abs(hx) > WorldGen.Half || hz > WorldGen.Half)
			{
				return -1.0;
			}
			if (hz < -WorldGen.Half)
			{
				if (eye + vy * t <= Network.Lift - drop)
				{
					sea = true;
					return t;
				}
			}
			else if (eye + vy * t <= Surface(hx, hz, out _) - drop)
			{
				return t;
			}
			t += Math.Max(1.0, t * 0.004);
		}
		return -1.0;
	}

	/// <summary>The reference's pixel: the land met at <paramref name="hit"/> lit and fogged, or the painted sky with no land.</summary>
	private static byte[] Pixel(double x, double z, double vx, double vz, double hit, bool sea, float drawn, SkyPaint.LandLight light, SkyPaint.Rgb fogColour, byte[] plain, int t)
	{
		if (hit < 0.0)
		{
			return new[] { plain[t], plain[t + 1], plain[t + 2] };
		}
		double hx = x + vx * hit, hz = z + vz * hit;
		byte r = 44, g = 79, b = 120;
		float nx = 0f, ny = 1f, nz = 0f;
		if (!sea)
		{
			double e = hit < drawn ? 2.0 : 100.0;
			double sx = Surface(hx - e, hz, out _) - Surface(hx + e, hz, out _), sz = Surface(hx, hz - e, out _) - Surface(hx, hz + e, out _);
			double nl = Math.Sqrt(sx * sx + 4.0 * e * e + sz * sz);
			nx = (float)(sx / nl);
			ny = (float)(2.0 * e / nl);
			nz = (float)(sz / nl);
			Colour(hx, hz, hit < drawn ? 1 : 8, out r, out g, out b);
		}
		if (!SkyPaint.Shade(light, fogColour, r, g, b, sea, nx, ny, nz, (float)hit, out float lr, out float lg, out float lb, out _))
		{
			SkyPaint.Rgb h = light.Linear ? Gamma(fogColour) : fogColour;
			lr = h.R;
			lg = h.G;
			lb = h.B;
		}
		return new[] { (byte)Math.Max(0, Math.Min(255, (int)(lr * 255f + 0.5f))), (byte)Math.Max(0, Math.Min(255, (int)(lg * 255f + 0.5f))), (byte)Math.Max(0, Math.Min(255, (int)(lb * 255f + 0.5f))) };
	}

	private static SkyPaint.Rgb Gamma(SkyPaint.Rgb c)
	{
		static float G(float v) => (float)(v <= 0.0031308 ? v * 12.92 : 1.055 * Math.Pow(v, 1.0 / 2.4) - 0.055);
		return new SkyPaint.Rgb(G(c.R), G(c.G), G(c.B));
	}

	/// <summary>The picture's colour at world (x, z), averaged over a block of <paramref name="cells"/> pixels a side round it.</summary>
	private static void Colour(double x, double z, int cells, out byte r, out byte g, out byte b)
	{
		int p = FarGrid.Pixels;
		int pi = (int)((x + WorldGen.Half) / FarGrid.Pixel) - cells / 2, pj = (int)((z + WorldGen.Half) / FarGrid.Pixel) - cells / 2;
		int sr = 0, sg = 0, sb = 0, n = 0;
		for (int j = pj; j < pj + cells; j++)
		{
			for (int i = pi; i < pi + cells; i++)
			{
				int o = (Math.Max(0, Math.Min(p - 1, j)) * p + Math.Max(0, Math.Min(p - 1, i))) * 4;
				sr += FarGrid.Colours[o];
				sg += FarGrid.Colours[o + 1];
				sb += FarGrid.Colours[o + 2];
				n++;
			}
		}
		r = (byte)(sr / n);
		g = (byte)(sg / n);
		b = (byte)(sb / n);
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
