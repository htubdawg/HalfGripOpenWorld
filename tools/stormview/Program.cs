using System;
using System.Diagnostics;
using System.IO;
using HalfGripOpenWorld;

// Checks the storms (Storms.cs) without the game and draws them.
//   dotnet run -c Release --project tools/stormview -- [out.ppm] [room seed] [minutes apart]
// Prints, over 20 room seeds and two hours of race, how much of the land is under rain and under cloud, how often a spot is wet,
// whether the same seed gives the same weather twice, and what a sample costs; then draws the world at six times for the seed given
// (land shaded by height, cloud darkening it, rain blue, snow white) side by side. Exits 1 if a check fails.
internal static class Program
{
	private static int Main(string[] args)
	{
		string output = args.Length > 0 ? args[0] : "storms.ppm";
		int seed = args.Length > 1 ? int.Parse(args[1]) : 12345;
		double apart = (args.Length > 2 ? double.Parse(args[2]) : 10.0) * 60.0;
		bool ok = true;

		Console.WriteLine("seed   rainy  wet day  snow line  wind      raining: mean  min  max   overcast  wet");
		double all = 0.0;
		int dryRaces = 0;
		for (int s = 1; s <= 20; s++)
		{
			Storms storms = new Storms(s * 7919);
			double sum = 0.0, min = 1.0, max = 0.0, over = 0.0, wet = 0.0;
			int frames = 0;
			for (double t = 0.0; t <= 7200.0; t += 600.0)
			{
				int land = 0, raining = 0, cloudy = 0, wetSpots = 0;
				for (int j = 0; j < 24; j++)
				{
					for (int i = 0; i < 24; i++)
					{
						double x = -WorldGen.Half + (i + 0.5) * WorldGen.Half * 2.0 / 24.0;
						double z = -WorldGen.Half + (j + 0.5) * WorldGen.Half * 2.0 / 24.0;
						double h = WorldGen.Natural(x, z);
						if (h < 0.0)
						{
							continue;
						}
						land++;
						storms.At(x, z, t, h, out double rain, out double cloud);
						raining += rain > 0.05 ? 1 : 0;
						cloudy += cloud > 0.3 ? 1 : 0;
						if (i % 4 == 0 && j % 4 == 0)
						{
							wetSpots += storms.Wetness(x, z, t, h) > 0.2 ? 16 : 0;
						}
					}
				}
				double share = raining / (double)land;
				sum += share;
				min = Math.Min(min, share);
				max = Math.Max(max, share);
				over += cloudy / (double)land;
				wet += wetSpots / (double)land;
				frames++;
			}
			all += sum / frames;
			dryRaces += max < 0.02 ? 1 : 0;
			string wind = (storms.WindSpeed.ToString("0.0") + " u/s").PadRight(10);
			Console.WriteLine((s * 7919).ToString().PadRight(7) + storms.Raininess.ToString("0.00").PadRight(7) + "         " + storms.SnowLine.ToString("0").PadRight(11) + wind
				+ "       " + Pct(sum / frames) + " " + Pct(min) + " " + Pct(max) + "   " + Pct(over / frames) + "     " + Pct(wet / frames));
		}
		Console.WriteLine("mean share of the land under rain over all seeds: " + Pct(all / 20.0) + "; races with no rain anywhere: " + dryRaces);
		if (all / 20.0 < 0.03 || all / 20.0 > 0.4)
		{
			Console.WriteLine("FAIL: the rain covers too little or too much of the world");
			ok = false;
		}

		// The same seed, position and time give the same weather (every player in a room has to agree).
		Storms a = new Storms(seed), b = new Storms(seed);
		for (int n = 0; n < 2000; n++)
		{
			double x = (n * 7919 % 32000) - 16000.0, z = (n * 104729 % 32000) - 16000.0, t = n * 3.7;
			a.At(x, z, t, 100.0, out double r1, out double c1);
			b.At(x, z, t, 100.0, out double r2, out double c2);
			if (r1 != r2 || c1 != c2)
			{
				Console.WriteLine("FAIL: the same seed gave different weather at " + x + ", " + z + ", " + t);
				ok = false;
				break;
			}
		}

		// The cost: the car's weather is sampled every frame, its wetness every second.
		Stopwatch watch = Stopwatch.StartNew();
		double junk = 0.0;
		for (int n = 0; n < 10000; n++)
		{
			a.At(n * 3.1 - 15000.0, n * 1.7 - 8000.0, 1000.0 + n * 0.5, 120.0, out double r, out double _);
			junk += r;
		}
		double perSample = watch.Elapsed.TotalMilliseconds / 10000.0;
		watch.Restart();
		for (int n = 0; n < 200; n++)
		{
			junk += a.Wetness(n * 31.0 - 3000.0, n * 17.0, 1000.0 + n * 5.0, 120.0);
		}
		double perWet = watch.Elapsed.TotalMilliseconds / 200.0;
		Console.WriteLine("a sample costs " + (perSample * 1000.0).ToString("0.0") + " us, a wetness " + perWet.ToString("0.00") + " ms" + (junk < -1.0 ? "" : ""));
		if (perSample > 0.05 || perWet > 2.0)
		{
			Console.WriteLine("FAIL: too slow for every frame");
			ok = false;
		}

		Draw(output, new Storms(seed), apart);
		Console.WriteLine("wrote " + output + " (room seed " + seed + ", six times " + (apart / 60.0).ToString("0") + " minutes apart from the start)");
		return ok ? 0 : 1;
	}

	private static string Pct(double v)
	{
		return ((v * 100.0).ToString("0") + "%").PadLeft(4);
	}

	private static void Draw(string path, Storms storms, double apart)
	{
		const int size = 256, frames = 6, gap = 4;
		int width = size * frames + gap * (frames - 1);
		byte[] pixels = new byte[width * size * 3];
		for (int f = 0; f < frames; f++)
		{
			double t = f * apart;
			for (int py = 0; py < size; py++)
			{
				for (int px = 0; px < size; px++)
				{
					double x = -WorldGen.Half + (px + 0.5) * WorldGen.Half * 2.0 / size;
					double z = WorldGen.Half - (py + 0.5) * WorldGen.Half * 2.0 / size;
					double h = WorldGen.Natural(x, z);
					storms.At(x, z, t, h, out double rain, out double cloud);
					double r, g, b;
					if (h < 0.0)
					{
						r = 0.20; g = 0.32; b = 0.50;
					}
					else
					{
						double k = 0.35 + 0.5 * Math.Min(1.0, h / 700.0);
						r = k * 0.75; g = k * 0.85; b = k * 0.6;
					}
					double dark = 1.0 - 0.55 * cloud;
					r *= dark; g *= dark; b *= dark;
					double snow = h >= 0.0 ? storms.SnowShare(h) : 0.0;
					double cr = 0.25 + 0.75 * snow, cg = 0.45 + 0.55 * snow, cb = 1.0;
					r += (cr - r) * rain * 0.8;
					g += (cg - g) * rain * 0.8;
					b += (cb - b) * rain * 0.8;
					int o = (py * width + f * (size + gap) + px) * 3;
					pixels[o] = (byte)(Math.Min(1.0, r) * 255.0);
					pixels[o + 1] = (byte)(Math.Min(1.0, g) * 255.0);
					pixels[o + 2] = (byte)(Math.Min(1.0, b) * 255.0);
				}
			}
		}
		using FileStream file = File.Create(path);
		byte[] header = System.Text.Encoding.ASCII.GetBytes("P6\n" + width + " " + size + "\n255\n");
		file.Write(header, 0, header.Length);
		file.Write(pixels, 0, pixels.Length);
	}
}
