using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using HalfGripOpenWorld;

// Checks the world generator without the game and draws it.
//   dotnet run -c Release --project tools/preview -- <out.ppm> [pixels] [west x] [north z] [span]
// Prints the layout (rivers, lakes, places, roads by class), realism figures (road density at real scale, junction types, dead
// ends), the steepest grade per class, how far the ground is from each road's bed, repeatability and the time a tile takes; then
// writes a map (land use, hillshade, water, roads by class, bridges in red), the whole world by default, or the square given.
// Exits 1 if a check fails.
internal static class Program
{
	/// <summary>Game units per real metre (a car is about 14 units and 4.3 m).</summary>
	private const double PerMetre = 3.3;

	private static int Main(string[] args)
	{
		string output = args.Length > 0 ? args[0] : "world.ppm";
		int size = args.Length > 1 ? int.Parse(args[1]) : 1024;
		double x0 = args.Length > 2 ? double.Parse(args[2]) : -WorldGen.Half;
		double z0 = args.Length > 3 ? double.Parse(args[3]) : WorldGen.Half;
		double span = args.Length > 4 ? double.Parse(args[4]) : WorldGen.Half * 2.0;
		bool ok = true;

		Network.Ensure();
		Console.WriteLine("layout built in " + Network.BuildMs.ToString("0") + " ms");
		Console.WriteLine("  " + Rivers.All.Count + " rivers, " + Rivers.LakeCount + " lakes");
		int[] places = new int[3];
		foreach (Places.Place p in Places.All)
		{
			places[(int)p.Kind]++;
			if (p.Kind != PlaceKind.Hamlet)
			{
				Console.WriteLine("  " + p.Kind.ToString().ToLower().PadRight(8) + p.Name.PadRight(12) + "(" + p.X.ToString("0") + ", " + p.Z.ToString("0") + ")");
			}
		}
		Console.WriteLine("  " + places[0] + " towns, " + places[1] + " villages, " + places[2] + " hamlets and farms");
		int[] lots = new int[4];
		foreach (Town.Lot lot in Town.Lots)
		{
			lots[(int)lot.Kind]++;
			if (lot.Kind == LotKind.Retail)
			{
				Console.WriteLine("  retail park at " + lot.Place + " (" + lot.X.ToString("0") + ", " + lot.Z.ToString("0") + ")");
			}
		}
		Console.WriteLine("  " + lots[1] + " shops, " + lots[2] + " workshops, " + lots[3] + " filling stations on the main roads");
		int ponds = 0;
		foreach (Parks.Park park in Parks.All)
		{
			ponds += park.HasPond ? 1 : 0;
			Console.WriteLine("  park " + park.Name.PadRight(22) + "(" + park.X.ToString("0") + ", " + park.Z.ToString("0") + ") " + (park.Cells * Parks.CellSize * Parks.CellSize / (PerMetre * PerMetre) / 10000.0).ToString("0.00") + " ha" + (park.HasPond ? ", a pond " + (park.PondR * 2.0 / PerMetre).ToString("0") + " m across" : ""));
		}
		Console.WriteLine("  " + Parks.All.Count + " parks (" + ponds + " with a pond), " + Parks.TrailCount + " lake trails (" + (Parks.TrailLength / PerMetre / 1000.0).ToString("0.0") + " km real), " + Parks.Benches.Count + " benches");
		if (Parks.All.Count == 0)
		{
			Console.WriteLine("FAIL: no parks");
			ok = false;
		}
		if (lots[0] == 0)
		{
			Console.WriteLine("FAIL: no retail park");
			ok = false;
		}

		// Lengths and density per class, at real scale (km of road per km² of land).
		double landArea = 0.0;
		for (double z = -WorldGen.Half; z < WorldGen.Half; z += 200.0)
		{
			for (double x = -WorldGen.Half; x < WorldGen.Half; x += 200.0)
			{
				if (WorldGen.Base(x, z) >= WorldGen.SeaLevel)
				{
					landArea += 200.0 * 200.0;
				}
			}
		}
		double realKm2 = landArea / (PerMetre * 1000.0) / (PerMetre * 1000.0);
		double[] length = new double[7];
		int[] count = new int[7];
		double[] steepest = new double[7];
		List<(double, double)> spans = new List<(double, double)>();
		foreach (Network.Road r in Network.Roads)
		{
			length[(int)r.Kind] += r.S[r.Count - 1];
			count[(int)r.Kind]++;
			for (int i = 1; i < r.Count; i++)
			{
				// Only where it is drawn: a stretch cut out at a junction lies under the other road.
				Network.Spans(r, i - 1, spans);
				if (!Network.Unpaved(r.Kind) && spans.Count == 0)
				{
					continue;
				}
				double g = Math.Abs(r.Y[i] - r.Y[i - 1]) / Math.Max(1e-6, r.S[i] - r.S[i - 1]);
				steepest[(int)r.Kind] = Math.Max(steepest[(int)r.Kind], g);
			}
		}
		Console.WriteLine("land " + realKm2.ToString("0") + " km² at real scale");
		double total = 0.0;
		for (int k = 0; k < 7; k++)
		{
			double km = length[k] / PerMetre / 1000.0;
			total += km;
			Console.WriteLine("  " + ((RoadKind)k).ToString().PadRight(10) + count[k].ToString().PadLeft(4) + " roads " + km.ToString("0.0").PadLeft(6) + " km real   steepest " + (steepest[k] * 100.0).ToString("0") + "%");
		}
		Console.WriteLine("  all roads " + total.ToString("0") + " km real, " + (total / realKm2).ToString("0.0") + " km per km² (rural England is about 2, its towns 10 to 20)");

		// Junctions: a road end on another road is a T; roads crossing mid-way a crossroads; an end on nothing a dead end.
		int tees = 0, deadEnds = 0, crossings = 0;
		foreach (Network.Road r in Network.Roads)
		{
			foreach (int end in new[] { 0, r.Count - 1 })
			{
				if (OnAnother(r, r.X[end], r.Z[end]))
				{
					tees++;
				}
				else
				{
					deadEnds++;
				}
			}
		}
		foreach (Network.Road r in Network.Roads)
		{
			for (int i = 3; i < r.Count - 4; i += 3)
			{
				if (OnAnother(r, r.X[i], r.Z[i]) && !OnAnother(r, r.X[i - 3], r.Z[i - 3]))
				{
					crossings++;
				}
			}
		}
		Console.WriteLine("junctions: " + tees + " T-junction ends, about " + crossings / 2 + " crossroads, " + deadEnds + " dead ends");

		// The ground meets each road's bed at its centre (bridges and cuts at junctions excepted; junctions may differ a little by design).
		double worstBed = 0.0;
		foreach (Network.Road r in Network.Roads)
		{
			for (int i = 0; i < r.Count; i += 11)
			{
				if (!r.Bridge[i] && Network.Shown(r, r.S[i]))
				{
					double depth = Network.Unpaved(r.Kind) ? Network.TrackDepth : Network.BedDepth;
					worstBed = Math.Max(worstBed, Math.Abs(WorldGen.Height(r.X[i], r.Z[i]) - Network.Lift - (r.Y[i] - depth)));
				}
			}
		}
		Console.WriteLine("largest gap between the ground and a road's bed at its centre: " + worstBed.ToString("0.00"));
		Console.WriteLine("spawn (" + Network.SpawnX.ToString("0") + ", " + Network.SpawnZ.ToString("0") + "), " + Network.Portals.Count + " roads leave the world through tunnels");

		bool repeatable = BitConverter.DoubleToInt64Bits(WorldGen.Height(12345.6, -7890.1)) == BitConverter.DoubleToInt64Bits(WorldGen.Height(12345.6, -7890.1));
		ok &= repeatable;
		Console.WriteLine("repeatable: " + repeatable + "   sample " + WorldGen.Height(12345.6, -7890.1).ToString("R"));

		// A tile's heights (53 x 53) and its 64 x 64 ground colours, as the streamer works them out, in a town and in the country.
		foreach ((string where, double tx, double tz) in new[] { ("town", Places.All[0].X, Places.All[0].Z), ("country", 3000.0, -2000.0) })
		{
			Stopwatch watch = Stopwatch.StartNew();
			for (int t = 0; t < 5; t++)
			{
				for (int j = 0; j < 53; j++)
				{
					for (int i = 0; i < 53; i++)
					{
						WorldGen.Height(tx + t * 250.0 + i * 5.0, tz + j * 5.0);
					}
				}
				for (int j = 0; j < 64; j++)
				{
					for (int i = 0; i < 64; i++)
					{
						LandUse.At(tx + t * 250.0 + i * 3.9, tz + j * 3.9, 20.0, 0.05);
					}
				}
			}
			Console.WriteLine("one tile, " + where + ": " + (watch.Elapsed.TotalMilliseconds / 5).ToString("0.0") + " ms");
		}

		Draw(output, size, x0, z0, span);
		Console.WriteLine("wrote " + output);
		return ok ? 0 : 1;
	}

	private static bool OnAnother(Network.Road self, double x, double z)
	{
		foreach (Network.Road o in Network.Roads)
		{
			if (o == self)
			{
				continue;
			}
			for (int i = 0; i + 1 < o.Count; i++)
			{
				if (Math.Abs(o.X[i] - x) > 20.0 || Math.Abs(o.Z[i] - z) > 20.0)
				{
					continue;
				}
				Lines.Project(o.X, o.Z, i, x, z, out double _, out double d);
				if (d < 1.0)
				{
					return true;
				}
			}
		}
		return false;
	}

	private static void Draw(string output, int size, double x0, double z0, double span)
	{
		double cell = span / size;
		double[,] h = new double[size, size];
		for (int j = 0; j < size; j++)
		{
			for (int i = 0; i < size; i++)
			{
				h[j, i] = WorldGen.Height(x0 + (i + 0.5) * cell, z0 - (j + 0.5) * cell) - Network.Lift;
			}
		}
		byte[] rgb = new byte[size * size * 3];
		for (int j = 0; j < size; j++)
		{
			for (int i = 0; i < size; i++)
			{
				double x = x0 + (i + 0.5) * cell, z = z0 - (j + 0.5) * cell;
				double dx = h[j, Math.Min(size - 1, i + 1)] - h[j, Math.Max(0, i - 1)];
				double dz = h[Math.Min(size - 1, j + 1), i] - h[Math.Max(0, j - 1), i];
				double slope = Math.Sqrt(dx * dx + dz * dz) / (2.0 * cell);
				double shade = Math.Max(0.55, Math.Min(1.2, 0.9 + (-dx - dz) / (4.0 * cell) * 1.5));
				LandUse.Rgb c = LandUse.At(x, z, h[j, i], slope);
				double r = c.R * shade, g = c.G * shade, b = c.B * shade;
				if (Rivers.Water(x, z, out double level) && level > h[j, i])
				{
					(r, g, b) = (46, 82, 128);
				}
				// Lots: their surface grey, their building darker.
				if (Town.At(x, z, out bool building) != null)
				{
					(r, g, b) = building ? (95, 90, 100) : (150, 150, 155);
				}
				int o = (j * size + i) * 3;
				rgb[o] = (byte)Math.Min(255.0, r);
				rgb[o + 1] = (byte)Math.Min(255.0, g);
				rgb[o + 2] = (byte)Math.Min(255.0, b);
			}
		}
		byte[][] colours = { new byte[] { 225, 110, 40 }, new byte[] { 235, 190, 60 }, new byte[] { 240, 225, 130 }, new byte[] { 70, 70, 74 }, new byte[] { 238, 236, 228 }, new byte[] { 150, 110, 70 }, new byte[] { 196, 180, 150 } };
		for (int kind = 6; kind >= 0; kind--)
		{
			foreach (Network.Road r in Network.Roads)
			{
				if ((int)r.Kind != kind)
				{
					continue;
				}
				int radius = (int)(r.Half / cell);
				for (int k = 0; k < r.Count; k++)
				{
					int pi = (int)((r.X[k] - x0) / cell), pj = (int)((z0 - r.Z[k]) / cell);
					byte[] c = r.Bridge[k] ? new byte[] { 200, 40, 40 } : colours[kind];
					for (int a = -radius; a <= radius; a++)
					{
						for (int b = -radius; b <= radius; b++)
						{
							int px = pi + a, py = pj + b;
							if (px >= 0 && py >= 0 && px < size && py < size)
							{
								int o = (py * size + px) * 3;
								rgb[o] = c[0];
								rgb[o + 1] = c[1];
								rgb[o + 2] = c[2];
							}
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
	}
}
