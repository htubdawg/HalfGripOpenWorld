using System;
using System.Diagnostics;
using System.IO;
using HalfGripOpenWorld;

// Checks the painted sky (SkyPaint.cs) without the game and draws it.
//   dotnet run -c Release --project tools/skyview -- [out.ppm] [room seed]
// Checks that the sun's disc is painted where its light comes from (the panoramic shader's mapping, inverted), that the sky at the
// horizon and everything under it is the horizon colour the fog takes (no seam between the world in fog and the sky), that a seed
// always paints the same sky, and what a frame's rows and a whole picture cost. Then draws a row per scene: the upper half of the
// picture (left: the whole turn, east in the middle; top: straight up) and a 70-degree view from the car facing the sun or the moon
// (or the storm, in the scenes with one: a storm field made by hand, its dark cloud and rain curtains seen from afar, which must show
// toward the storm and not away from it, and must not touch the horizon's last rows).
// Exits 1 if a check fails.
internal static class Program
{
	private struct Scene
	{
		internal string Name;
		internal float Hour, Cloud, Coverage;
		/// <summary>A storm this far east and north of the car, this wide (0: none).</summary>
		internal float StormX, StormZ, StormR;
	}

	private static readonly SkyPaint.Rgb DayHaze = new SkyPaint.Rgb(0.41f, 0.42f, 0.44f);
	private static readonly SkyPaint.Rgb NightHaze = new SkyPaint.Rgb(0.10f, 0.13f, 0.16f);

	private static int Main(string[] args)
	{
		string output = args.Length > 0 ? args[0] : "sky.ppm";
		int seed = args.Length > 1 ? int.Parse(args[1]) : 12345;
		bool ok = true;

		Scene[] scenes =
		{
			new Scene { Name = "10:00 clear", Hour = 10f, Cloud = 0f, Coverage = 0.15f },
			new Scene { Name = "13:00 fair weather cloud", Hour = 13f, Cloud = 0.1f, Coverage = 0.35f },
			new Scene { Name = "17:20 golden hour", Hour = 17.33f, Cloud = 0f, Coverage = 0.2f },
			new Scene { Name = "18:00 sunset", Hour = 18f, Cloud = 0f, Coverage = 0.2f },
			new Scene { Name = "18:40 dusk", Hour = 18.67f, Cloud = 0f, Coverage = 0.15f },
			new Scene { Name = "23:00 night, moon", Hour = 23f, Cloud = 0f, Coverage = 0.1f },
			new Scene { Name = "02:00 night, cloud", Hour = 2f, Cloud = 0.3f, Coverage = 0.5f },
			new Scene { Name = "12:00 under a storm", Hour = 12f, Cloud = 0.9f, Coverage = 0.95f },
			new Scene { Name = "11:00 a storm 9 km west", Hour = 11f, Cloud = 0.05f, Coverage = 0.25f, StormX = -9000f, StormZ = 1500f, StormR = 3000f },
			new Scene { Name = "17:00 a storm 5 km north", Hour = 17f, Cloud = 0.25f, Coverage = 0.4f, StormX = 800f, StormZ = 5000f, StormR = 2500f },
		};

		Stopwatch build = Stopwatch.StartNew();
		SkyPaint paint = new SkyPaint(seed);
		build.Stop();
		Console.WriteLine("Set up (cloud pattern, stars): " + build.Elapsed.TotalMilliseconds.ToString("0.0") + " ms");

		// ---- the sun where its light is ----
		{
			SkyPaint.State st = Make(12f, 0f, 0f);
			paint.All(st);
			int col = (int)Math.Floor((0.5 - Math.Atan2(st.SunZ, st.SunX) / (2.0 * Math.PI)) * SkyPaint.W);
			int row = (int)Math.Floor((1.0 - Math.Acos(st.SunY) / Math.PI) * SkyPaint.H);
			int o = (row * SkyPaint.W + col) * 4;
			bool sunThere = paint.Pixels[o] > 240 && paint.Pixels[o + 1] > 230;
			Console.WriteLine("Sun at noon: texel " + col + ", " + row + " is " + paint.Pixels[o] + " " + paint.Pixels[o + 1] + " " + paint.Pixels[o + 2] + (sunThere ? "  ok" : "  FAIL: no sun where its light comes from"));
			ok &= sunThere;
			// South at noon: the shader's u = 0.5 - atan2(z, x) / 2 pi puts south (z -1) at u 0.75.
			bool south = Math.Abs(col - SkyPaint.W * 3 / 4) <= 1;
			Console.WriteLine("Sun at noon is south (column " + col + ", expected " + SkyPaint.W * 3 / 4 + ")" + (south ? "  ok" : "  FAIL"));
			ok &= south;
		}

		// ---- the horizon is the fog's colour ----
		foreach (Scene sc in scenes)
		{
			SkyPaint.State st = Make(sc);
			paint.All(st);
			SkyPaint.Palette(st, out _, out SkyPaint.Rgb horizon, out _);
			int worst = 0;
			// A quarter turn from the sun (its glow lightens the horizon toward it, and the moon is opposite), the lowest row above the horizon and
			// a row under it.
			int col = (int)Math.Floor((0.5 - Math.Atan2(st.SunX, -st.SunZ) / (2.0 * Math.PI)) * SkyPaint.W);
			col = ((col % SkyPaint.W) + SkyPaint.W) % SkyPaint.W;
			foreach (int row in new[] { SkyPaint.H / 2 - 1, SkyPaint.H / 2 })
			{
				int o = (row * SkyPaint.W + col) * 4;
				worst = Math.Max(worst, Math.Abs(paint.Pixels[o] - (int)(horizon.R * 255f)));
				worst = Math.Max(worst, Math.Abs(paint.Pixels[o + 1] - (int)(horizon.G * 255f)));
				worst = Math.Max(worst, Math.Abs(paint.Pixels[o + 2] - (int)(horizon.B * 255f)));
			}
			if (sc.StormR > 0f)
			{
				// Toward the storm too.
				int sCol = Column(sc.StormX, sc.StormZ);
				foreach (int row in new[] { SkyPaint.H / 2 - 1, SkyPaint.H / 2 })
				{
					int o = (row * SkyPaint.W + sCol) * 4;
					worst = Math.Max(worst, Math.Abs(paint.Pixels[o] - (int)(horizon.R * 255f)));
					worst = Math.Max(worst, Math.Abs(paint.Pixels[o + 1] - (int)(horizon.G * 255f)));
					worst = Math.Max(worst, Math.Abs(paint.Pixels[o + 2] - (int)(horizon.B * 255f)));
				}
			}
			bool seam = worst <= 4;
			Console.WriteLine("Horizon meets the fog, " + sc.Name.PadRight(26) + " off by " + worst + "/255" + (seam ? "  ok" : "  FAIL"));
			ok &= seam;
		}

		// ---- storms seen from afar: darker toward the storm than the same sky with none, and the sky away from it untouched ----
		foreach (Scene sc in scenes)
		{
			if (sc.StormR <= 0f)
			{
				continue;
			}
			// The same sky with and without the storm, toward it.
			SkyPaint.State st = Make(sc);
			SkyPaint.State clear = st;
			clear.Storm = null;
			SkyPaint without = new SkyPaint(seed);
			without.All(clear);
			paint.All(st);
			int toward = Column(sc.StormX, sc.StormZ);
			// Two degrees up, in the rain curtain, and where the cloud layer (4000 up, as SkyPaint sees it) is over the storm's middle.
			double distance = Math.Sqrt(sc.StormX * sc.StormX + sc.StormZ * sc.StormZ);
			double y = 4000.0 / distance - 0.08;
			float over = (float)(Math.Atan(y / Math.Sqrt(1.0 - y * y)) * 180.0 / Math.PI);
			foreach (float degrees in new[] { 2f, over })
			{
				int row = (int)Math.Floor((1.0 - Math.Acos(Math.Sin(degrees * Math.PI / 180.0)) / Math.PI) * SkyPaint.H);
				// Grey against blue: the colour changes more than the brightness, so the measure is the colours' distance.
				float diff = Diff(paint, without, row, toward);
				bool dark = diff > 0.08f;
				Console.WriteLine("Storm, " + sc.Name.PadRight(26) + degrees.ToString("0").PadLeft(2) + " degrees up toward it: off the sky with no storm by " + diff.ToString("0.00") + (dark ? "  ok" : "  FAIL: no storm to be seen"));
				ok &= dark;
			}
			// Away from it, the sky is as it would be with no storm.
			int away = Column(-sc.StormX, -sc.StormZ);
			int awayRow = (int)Math.Floor((1.0 - Math.Acos(Math.Sin(10.0 * Math.PI / 180.0)) / Math.PI) * SkyPaint.H);
			bool untouched = Diff(paint, without, awayRow, away) < 0.005f;
			Console.WriteLine("Storm, " + sc.Name.PadRight(26) + " away from it, the sky as with no storm" + (untouched ? "  ok" : "  FAIL"));
			ok &= untouched;
		}

		// ---- the same seed paints the same sky ----
		{
			SkyPaint.State st = Make(22f, 0.2f, 0.4f);
			SkyPaint a = new SkyPaint(seed), b = new SkyPaint(seed);
			a.All(st);
			b.All(st);
			bool same = true;
			for (int i = 0; i < a.Pixels.Length && same; i++)
			{
				same = a.Pixels[i] == b.Pixels[i];
			}
			Console.WriteLine("Same seed, same sky" + (same ? "  ok" : "  FAIL"));
			ok &= same;
		}

		// ---- cost, with no storm and with one in view ----
		foreach (float stormR in new[] { 0f, 3500f })
		{
			SkyPaint.State st = Make(new Scene { Hour = 17.5f, Cloud = 0.2f, Coverage = 0.5f, StormX = 6000f, StormZ = -4000f, StormR = stormR });
			paint.All(st);
			Stopwatch watch = Stopwatch.StartNew();
			int frames = 0, pictures = 0;
			while (pictures < 40)
			{
				st.CloudX += 3f;
				st.CloudZ += 1f;
				if (paint.Frame(st))
				{
					pictures++;
				}
				frames++;
			}
			watch.Stop();
			double perFrame = watch.Elapsed.TotalMilliseconds / frames;
			Console.WriteLine("Cost" + (stormR > 0f ? " with a storm: " : ": ") + perFrame.ToString("0.00") + " ms a frame (" + SkyPaint.RowsPerFrame + " rows), a picture every " + (frames / (double)pictures).ToString("0") + " frames ("
				+ (watch.Elapsed.TotalMilliseconds / pictures).ToString("0.0") + " ms of painting)");
		}

		// ---- the drawing ----
		const int viewW = 480;
		int stripH = SkyPaint.H / 2;
		int width = SkyPaint.W + viewW;
		int height = stripH * scenes.Length;
		byte[] image = new byte[width * height * 3];
		for (int n = 0; n < scenes.Length; n++)
		{
			SkyPaint.State st = Make(scenes[n]);
			paint.All(st);
			for (int y = 0; y < stripH; y++)
			{
				int row = SkyPaint.H - 1 - y;
				for (int x = 0; x < SkyPaint.W; x++)
				{
					int o = (row * SkyPaint.W + x) * 4;
					int p = ((n * stripH + y) * width + x) * 3;
					image[p] = paint.Pixels[o];
					image[p + 1] = paint.Pixels[o + 1];
					image[p + 2] = paint.Pixels[o + 2];
				}
			}
			// The view: facing the sun, or the moon when the sun is down, or the storm if there is one, 12 degrees up, 70 degrees across.
			bool sunUp = st.SunElevation > -4f;
			float fx = sunUp ? st.SunX : st.MoonX, fz = sunUp ? st.SunZ : st.MoonZ;
			if (scenes[n].StormR > 0f)
			{
				fx = scenes[n].StormX;
				fz = scenes[n].StormZ;
			}
			double heading = Math.Atan2(fz, fx) * 180.0 / Math.PI;
			SkyPaint.Dir((float)heading, 12f, out float ex, out float ey, out float ez);
			// Right of the view, cross(up, forward); up of the view, cross(forward, right).
			float rx = ez, ry = 0f, rz = -ex;
			float rl = (float)Math.Sqrt(rx * rx + rz * rz);
			rx /= rl;
			rz /= rl;
			float ux = ey * rz - ez * ry, uy = ez * rx - ex * rz, uz = ex * ry - ey * rx;
			double span = Math.Tan(35.0 * Math.PI / 180.0);
			for (int y = 0; y < stripH; y++)
			{
				for (int x = 0; x < viewW; x++)
				{
					double a = (2.0 * (x + 0.5) / viewW - 1.0) * span;
					double b = (1.0 - 2.0 * (y + 0.5) / stripH) * span * stripH / viewW;
					double dx = ex + rx * a + ux * b, dy = ey + ry * a + uy * b, dz = ez + rz * a + uz * b;
					double l = Math.Sqrt(dx * dx + dy * dy + dz * dz);
					dx /= l;
					dy /= l;
					dz /= l;
					int col = (int)Math.Floor((0.5 - Math.Atan2(dz, dx) / (2.0 * Math.PI)) * SkyPaint.W);
					col = ((col % SkyPaint.W) + SkyPaint.W) % SkyPaint.W;
					int row = Math.Min(SkyPaint.H - 1, (int)Math.Floor((1.0 - Math.Acos(Math.Max(-1.0, Math.Min(1.0, dy))) / Math.PI) * SkyPaint.H));
					int o = (row * SkyPaint.W + col) * 4;
					int p = ((n * stripH + y) * width + SkyPaint.W + x) * 3;
					// Under the horizon, a ground the colour of the fog darkened, as the world in fog would be.
					float k = dy < 0.0 ? 0.8f : 1f;
					image[p] = (byte)(paint.Pixels[o] * k);
					image[p + 1] = (byte)(paint.Pixels[o + 1] * k);
					image[p + 2] = (byte)(paint.Pixels[o + 2] * k);
				}
			}
		}
		using (FileStream file = File.Create(output))
		{
			byte[] header = System.Text.Encoding.ASCII.GetBytes("P6\n" + width + " " + height + "\n255\n");
			file.Write(header, 0, header.Length);
			file.Write(image, 0, image.Length);
		}
		Console.WriteLine("Drew " + output + ": " + string.Join(", ", Array.ConvertAll(scenes, sc => sc.Name)) + ", top to bottom");
		Console.WriteLine(ok ? "All checks passed" : "A CHECK FAILED");
		return ok ? 0 : 1;
	}

	private static SkyPaint.State Make(Scene sc)
	{
		SkyPaint.State st = Make(sc.Hour, sc.Cloud, sc.Coverage);
		st.Reach = 12000f;
		st.CarX = 0f;
		st.CarZ = 0f;
		if (sc.StormR > 0f)
		{
			st.Storm = Storm(sc.StormX, sc.StormZ, sc.StormR);
		}
		return st;
	}

	/// <summary>A storm field round the car (at 0, 0) with one storm at (x, z) of radius r, shaped as Storms shapes a cell at full strength.</summary>
	private static StormField Storm(float x, float z, float r)
	{
		StormField f = new StormField(64, 800.0, 0.0, 0.0);
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

	/// <summary>The picture's column toward direction (x east, z north), as the panoramic shader maps it.</summary>
	private static int Column(double x, double z)
	{
		int col = (int)Math.Floor((0.5 - Math.Atan2(z, x) / (2.0 * Math.PI)) * SkyPaint.W);
		return ((col % SkyPaint.W) + SkyPaint.W) % SkyPaint.W;
	}

	/// <summary>How far apart two pictures' colours are, on the mean of each channel over the texels within 12 columns and 2 rows of (row, col)
	/// (the storm's lumps vary texel to texel): the length of the difference of the means, 0 to 1.7.</summary>
	private static float Diff(SkyPaint a, SkyPaint b, int row, int col)
	{
		float dr = 0f, dg = 0f, db = 0f;
		int count = 0;
		for (int r = Math.Max(SkyPaint.H / 2, row - 2); r <= Math.Min(SkyPaint.H - 1, row + 2); r++)
		{
			for (int c = col - 12; c <= col + 12; c++)
			{
				int o = (r * SkyPaint.W + ((c % SkyPaint.W) + SkyPaint.W) % SkyPaint.W) * 4;
				dr += (a.Pixels[o] - b.Pixels[o]) / 255f;
				dg += (a.Pixels[o + 1] - b.Pixels[o + 1]) / 255f;
				db += (a.Pixels[o + 2] - b.Pixels[o + 2]) / 255f;
				count++;
			}
		}
		dr /= count;
		dg /= count;
		db /= count;
		return (float)Math.Sqrt(dr * dr + dg * dg + db * db);
	}

	private static SkyPaint.State Make(float hour, float cloud, float coverage)
	{
		SkyPaint.State st = new SkyPaint.State { Cloud = cloud, Coverage = coverage, DayHaze = DayHaze, NightHaze = NightHaze };
		SkyPaint.Place(ref st, hour, out _);
		return st;
	}
}
