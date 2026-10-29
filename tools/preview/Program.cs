using System;
using System.Diagnostics;
using System.IO;
using HalfGripOpenWorld;

// Checks the world generator without the game: height range, slopes, the spawn pad, the edge wall, how long a tile takes, and a
// shaded relief map of the whole world written as a PGM picture (any image viewer, or `python3 -c` with Pillow, turns it into a PNG).
//   dotnet run --project tools/preview -- [out.pgm] [pixels]
internal static class Program
{
	private static int Main(string[] args)
	{
		string output = args.Length > 0 ? args[0] : "world.pgm";
		int size = args.Length > 1 ? int.Parse(args[1]) : 1024;
		double span = WorldGen.Half * 2.0;
		double cell = span / size;

		double[,] h = new double[size, size];
		double min = double.MaxValue, max = double.MinValue;
		for (int j = 0; j < size; j++)
		{
			for (int i = 0; i < size; i++)
			{
				double x = -WorldGen.Half + (i + 0.5) * cell;
				double z = WorldGen.Half - (j + 0.5) * cell;
				double v = WorldGen.Height(x, z);
				h[j, i] = v;
				min = Math.Min(min, v);
				max = Math.Max(max, v);
			}
		}

		// Slopes on a fine grid inside the edge wall, at the 5 m spacing the tiles use.
		int steep = 0, total = 0;
		double worst = 0.0;
		for (double z = -14000; z <= 14000; z += 97.0)
		{
			for (double x = -14000; x <= 14000; x += 97.0)
			{
				double gx = (WorldGen.Height(x + 5, z) - WorldGen.Height(x - 5, z)) / 10.0;
				double gz = (WorldGen.Height(x, z + 5) - WorldGen.Height(x, z - 5)) / 10.0;
				double deg = Math.Atan(Math.Sqrt(gx * gx + gz * gz)) * 180.0 / Math.PI;
				worst = Math.Max(worst, deg);
				if (deg > 30.0)
				{
					steep++;
				}
				total++;
			}
		}

		// One tile's worth of samples (53 x 53), as the streamer computes it.
		Stopwatch watch = Stopwatch.StartNew();
		double sink = 0.0;
		int tiles = 20;
		for (int t = 0; t < tiles; t++)
		{
			for (int j = 0; j < 53; j++)
			{
				for (int i = 0; i < 53; i++)
				{
					sink += WorldGen.Height(3000.0 + t * 250.0 + i * 5.0, -2000.0 + j * 5.0);
				}
			}
		}
		watch.Stop();

		// The same point twice must give the same bits (no hidden state).
		bool repeatable = BitConverter.DoubleToInt64Bits(WorldGen.Height(12345.6, -7890.1)) == BitConverter.DoubleToInt64Bits(WorldGen.Height(12345.6, -7890.1));

		Console.WriteLine("heights " + min.ToString("0") + " to " + max.ToString("0") + " m");
		Console.WriteLine("pad: height at spawn " + WorldGen.Height(0, 0).ToString("0.000") + ", at 200 m " + WorldGen.Height(200, 0).ToString("0.000") + ", at 1 km " + WorldGen.Height(1000, 0).ToString("0.0"));
		Console.WriteLine("edge wall: at 14 km " + WorldGen.Height(14000, 0).ToString("0") + ", at 15.5 km " + WorldGen.Height(15500, 0).ToString("0") + ", at 16 km " + WorldGen.Height(16000, 0).ToString("0"));
		Console.WriteLine("slopes: steepest " + worst.ToString("0") + " deg, " + (100.0 * steep / total).ToString("0.0") + "% of the land over 30 deg");
		Console.WriteLine("one tile: " + (watch.Elapsed.TotalMilliseconds / tiles).ToString("0.0") + " ms (" + sink.ToString("0").Length + ")");
		Console.WriteLine("repeatable: " + repeatable);
		Console.WriteLine("sample: " + WorldGen.Height(12345.6, -7890.1).ToString("R"));

		// Shaded relief: light from the north-west, tinted by height.
		using (StreamWriter w = new StreamWriter(output))
		{
			w.Write("P5\n" + size + " " + size + "\n255\n");
			w.Flush();
			Stream s = w.BaseStream;
			for (int j = 0; j < size; j++)
			{
				for (int i = 0; i < size; i++)
				{
					double dx = h[j, Math.Min(size - 1, i + 1)] - h[j, Math.Max(0, i - 1)];
					double dz = h[Math.Min(size - 1, j + 1), i] - h[Math.Max(0, j - 1), i];
					double shade = 0.5 + (-dx - dz) / (4.0 * cell) * 1.5;
					double tone = (h[j, i] - min) / (max - min);
					double v = Math.Max(0.0, Math.Min(1.0, 0.25 + 0.45 * tone + 0.35 * (shade - 0.5)));
					s.WriteByte((byte)(v * 255.0));
				}
			}
		}
		Console.WriteLine("wrote " + output);
		return repeatable ? 0 : 1;
	}
}
