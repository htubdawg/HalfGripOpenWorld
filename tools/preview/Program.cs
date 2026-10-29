using System;
using System.Diagnostics;
using System.IO;
using HalfGripOpenWorld;

// Checks the world generator without the game: the layout (towns, villages, roads), road grades, that the ground meets every road's
// bed, height range and slopes, repeatability, how long a tile's heights take, and a map of the whole world written as a PPM picture
// (Pillow or most image viewers turn it into a PNG).
//   dotnet run -c Release --project tools/preview -- [out.ppm] [pixels]
internal static class Program
{
	private static int Main(string[] args)
	{
		string output = args.Length > 0 ? args[0] : "world.ppm";
		int size = args.Length > 1 ? int.Parse(args[1]) : 1024;
		bool ok = true;

		Network.Ensure();
		Console.WriteLine("layout built in " + Network.BuildMs.ToString("0") + " ms");
		foreach (Network.Site s in Network.Sites)
		{
			Console.WriteLine((s.Town ? "  town    " : "  village ") + s.Name.PadRight(11) + " at (" + s.X.ToString("0") + ", " + s.Z.ToString("0") + ")  height " + s.Y.ToString("0"));
		}
		double[] length = new double[3];
		int[] count = new int[3];
		double worstGrade = 0.0;
		string worstRoad = "";
		double worstBed = 0.0;
		foreach (Network.Road r in Network.Roads)
		{
			length[(int)r.Kind] += r.S[r.Count - 1];
			count[(int)r.Kind]++;
			for (int i = 1; i < r.Count; i++)
			{
				double g = Math.Abs(r.Y[i] - r.Y[i - 1]) / Math.Max(1e-6, r.S[i] - r.S[i - 1]);
				if (g > worstGrade && r.Kind != RoadKind.Street)
				{
					worstGrade = g;
					worstRoad = r.Name;
				}
			}
			// The ground at the road's centre must be the road's bed (unless another road's bed claims it: a junction).
			for (int i = 0; i < r.Count; i += 7)
			{
				double off = Math.Abs(WorldGen.Height(r.X[i], r.Z[i]) - (r.Y[i] - Network.BedDepth));
				worstBed = Math.Max(worstBed, off);
			}
		}
		// Rural roads must not run along another road (a crossing is fine): count samples within both roads' widths of another road.
		foreach (Network.Road r in Network.Roads)
		{
			if (r.Kind != RoadKind.Rural)
			{
				continue;
			}
			int along = 0;
			foreach (Network.Road o in Network.Roads)
			{
				if (o == r || o.Kind == RoadKind.Street)
				{
					continue;
				}
				for (int i = 0; i < r.Count; i += 2)
				{
					for (int j = 0; j < o.Count; j += 2)
					{
						double dx = r.X[i] - o.X[j], dz = r.Z[i] - o.Z[j];
						if (dx * dx + dz * dz < (r.Half + o.Half) * (r.Half + o.Half))
						{
							along += 2;
							break;
						}
					}
				}
			}
			if (along * Network.Step > 300.0)
			{
				Console.WriteLine("  overlap: " + r.Name + " runs " + (along * Network.Step).ToString("0") + " units on top of other roads");
				ok = false;
			}
		}
		if (args.Length > 2)
		{
			foreach (Network.Road r in Network.Roads)
			{
				if (r.Kind != RoadKind.Street)
				{
					Console.WriteLine("  road " + r.Name + ": " + (r.S[r.Count - 1] / 1000.0).ToString("0.0") + " km from (" + r.X[0].ToString("0") + ", " + r.Z[0].ToString("0") + ") to (" + r.X[r.Count - 1].ToString("0") + ", " + r.Z[r.Count - 1].ToString("0") + ")");
				}
			}
		}
		string[] kinds = { "highway", "rural", "street" };
		for (int k = 0; k < 3; k++)
		{
			Console.WriteLine("  " + kinds[k].PadRight(8) + count[k] + " roads, " + (length[k] / 1000.0).ToString("0.0") + " km");
		}
		Console.WriteLine("steepest road grade " + (worstGrade * 100.0).ToString("0.0") + "% (" + worstRoad + ")");
		Console.WriteLine("largest gap between the ground and a road's bed at its centre: " + worstBed.ToString("0.00") + " (junctions differ by design)");
		Console.WriteLine("spawn at (" + Network.SpawnX.ToString("0") + ", " + Network.SpawnZ.ToString("0") + "), height " + Network.SpawnY.ToString("0") + ", heading (" + Network.SpawnDirX.ToString("0.00") + ", " + Network.SpawnDirZ.ToString("0.00") + ")");

		// One tile's worth of heights (53 x 53), as the streamer works them out, in a town and in the country.
		foreach ((string where, double x0, double z0) in new[] { ("town", Network.Sites[0].X - 125.0, Network.Sites[0].Z - 125.0), ("country", 3000.0, -2000.0) })
		{
			Stopwatch watch = Stopwatch.StartNew();
			double sink = 0.0;
			for (int t = 0; t < 10; t++)
			{
				for (int j = 0; j < 53; j++)
				{
					for (int i = 0; i < 53; i++)
					{
						sink += WorldGen.Height(x0 + t * 250.0 + i * 5.0, z0 + j * 5.0);
					}
				}
			}
			Console.WriteLine("one tile, " + where + ": " + (watch.Elapsed.TotalMilliseconds / 10).ToString("0.0") + " ms" + (sink == 0.123 ? "!" : ""));
		}

		bool repeatable = BitConverter.DoubleToInt64Bits(WorldGen.Height(12345.6, -7890.1)) == BitConverter.DoubleToInt64Bits(WorldGen.Height(12345.6, -7890.1));
		ok &= repeatable;
		Console.WriteLine("repeatable: " + repeatable + "   sample: " + WorldGen.Height(12345.6, -7890.1).ToString("R"));

		// The map: shaded relief tinted by height, roads drawn over it, north up.
		double span = WorldGen.Half * 2.0;
		double cell = span / size;
		double[,] h = new double[size, size];
		for (int j = 0; j < size; j++)
		{
			for (int i = 0; i < size; i++)
			{
				h[j, i] = WorldGen.Height(-WorldGen.Half + (i + 0.5) * cell, WorldGen.Half - (j + 0.5) * cell);
			}
		}
		byte[] rgb = new byte[size * size * 3];
		for (int j = 0; j < size; j++)
		{
			for (int i = 0; i < size; i++)
			{
				double dx = h[j, Math.Min(size - 1, i + 1)] - h[j, Math.Max(0, i - 1)];
				double dz = h[Math.Min(size - 1, j + 1), i] - h[Math.Max(0, j - 1), i];
				double shade = Math.Max(0.35, Math.Min(1.25, 0.85 + (-dx - dz) / (4.0 * cell) * 2.0));
				double v = h[j, i];
				(double r, double g, double b) = v < 60 ? (0.45, 0.62, 0.32) : v < 200 ? (0.55, 0.6, 0.35) : v < 450 ? (0.55, 0.5, 0.4) : (0.92, 0.92, 0.95);
				int o = (j * size + i) * 3;
				rgb[o] = (byte)Math.Min(255.0, r * shade * 255.0);
				rgb[o + 1] = (byte)Math.Min(255.0, g * shade * 255.0);
				rgb[o + 2] = (byte)Math.Min(255.0, b * shade * 255.0);
			}
		}
		foreach (Network.Road r in Network.Roads)
		{
			(byte cr, byte cg, byte cb) = r.Kind == RoadKind.Highway ? ((byte)230, (byte)120, (byte)30) : r.Kind == RoadKind.Rural ? ((byte)250, (byte)230, (byte)90) : ((byte)70, (byte)70, (byte)75);
			int thick = r.Kind == RoadKind.Highway ? 2 : 1;
			for (int k = 0; k < r.Count; k++)
			{
				int pi = (int)((r.X[k] + WorldGen.Half) / cell);
				int pj = (int)((WorldGen.Half - r.Z[k]) / cell);
				for (int a = 0; a < thick; a++)
				{
					for (int b = 0; b < thick; b++)
					{
						int x = pi + a, y = pj + b;
						if (x >= 0 && y >= 0 && x < size && y < size)
						{
							int o = (y * size + x) * 3;
							rgb[o] = cr;
							rgb[o + 1] = cg;
							rgb[o + 2] = cb;
						}
					}
				}
			}
		}
		using (FileStream f = File.Create(output))
		{
			byte[] head = System.Text.Encoding.ASCII.GetBytes("P6\n" + size + " " + size + "\n255\n");
			f.Write(head, 0, head.Length);
			f.Write(rgb, 0, rgb.Length);
		}
		Console.WriteLine("wrote " + output);
		return ok ? 0 : 1;
	}
}
