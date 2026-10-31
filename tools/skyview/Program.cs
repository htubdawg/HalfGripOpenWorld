using System;
using System.Diagnostics;
using System.IO;
using HalfGripOpenWorld;

// Checks the painted sky (SkyPaint.cs) without the game and draws it.
//   dotnet run -c Release --project tools/skyview -- [out.ppm] [room seed]
// Checks that the sun's disc is painted where its light comes from (the panoramic shader's mapping, inverted), that the sky at the
// horizon and everything under it is the horizon colour the fog takes (no seam between the world in fog and the sky), that a seed
// always paints the same sky, and what a frame's rows and a whole picture cost. Then draws a row per scene: the upper half of the
// picture (left: the whole turn, east in the middle; top: straight up) and a 70-degree view from the car facing the sun or the moon.
// Exits 1 if a check fails.
internal static class Program
{
	private struct Scene
	{
		internal string Name;
		internal float Hour, Cloud, Coverage;
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
			SkyPaint.State st = Make(sc.Hour, sc.Cloud, sc.Coverage);
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
			bool seam = worst <= 4;
			Console.WriteLine("Horizon meets the fog, " + sc.Name.PadRight(26) + " off by " + worst + "/255" + (seam ? "  ok" : "  FAIL"));
			ok &= seam;
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

		// ---- cost ----
		{
			SkyPaint.State st = Make(17.5f, 0.2f, 0.5f);
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
			Console.WriteLine("Cost: " + perFrame.ToString("0.00") + " ms a frame (" + SkyPaint.RowsPerFrame + " rows), a picture every " + (frames / (double)pictures).ToString("0") + " frames ("
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
			SkyPaint.State st = Make(scenes[n].Hour, scenes[n].Cloud, scenes[n].Coverage);
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
			// The view: facing the sun, or the moon when the sun is down, 10 degrees up, 70 degrees across.
			bool sunUp = st.SunElevation > -4f;
			float fx = sunUp ? st.SunX : st.MoonX, fz = sunUp ? st.SunZ : st.MoonZ;
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

	private static SkyPaint.State Make(float hour, float cloud, float coverage)
	{
		SkyPaint.State st = new SkyPaint.State { Cloud = cloud, Coverage = coverage, DayHaze = DayHaze, NightHaze = NightHaze };
		SkyPaint.Place(ref st, hour, out _);
		return st;
	}
}
