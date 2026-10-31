using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using HalfGripOpenWorld;

// Checks the world generator without the game and draws it.
//   dotnet run -c Release --project tools/preview -- <out.ppm> [pixels] [west x] [north z] [span]
// Prints the layout (rivers, lakes, places, roads by class), realism figures (road density at real scale, junction types, dead
// ends), the steepest grade per class, how far the ground is from each road's bed, repeatability and the time a tile takes; then
// writes a map (land use, hillshade, water, roads by class, bridges in red), the whole world by default, or the square given; the
// same map with the places and courses named (<out>-labels.ppm), and every road sign's face as the game draws it (<out>-signs.ppm).
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
		int[] lots = new int[7];
		foreach (Town.Lot lot in Town.Lots)
		{
			lots[(int)lot.Kind]++;
			if (lot.Kind == LotKind.Retail)
			{
				Console.WriteLine("  retail park at " + lot.Place + " (" + lot.X.ToString("0") + ", " + lot.Z.ToString("0") + ")");
			}
		}
		Console.WriteLine("  " + lots[1] + " shops, " + lots[2] + " workshops, " + lots[3] + " filling stations on the main roads");
		Console.WriteLine("  " + GarageSites.All.Count + " garages: " + string.Join(", ", GarageSites.All.GroupBy(g => g.Kind).Select(g => g.Count() + " " + g.Key).ToArray()) + "; " + GarageSites.Marks.Count + " marks on the map (" + string.Join(", ", GarageSites.Marks.Where(m => m.Count > 1).Select(m => m.Count + " at (" + m.X.ToString("0") + ", " + m.Z.ToString("0") + ")").ToArray()) + " grouped)");
		Console.WriteLine("  events: " + string.Join(", ", Courses.All.Select(c => c.Name + " " + (c.Distance / 3300.0).ToString("0.0") + " km" + (c.Laps > 1 ? " (" + c.Laps + " laps)" : "") + ", " + c.Gates.Count + " checkpoints, start box at (" + c.BoxX.ToString("0") + ", " + c.BoxZ.ToString("0") + ")").ToArray()));
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
		Console.WriteLine("  " + Circuit.Report);
		if (!Circuit.Exists)
		{
			Console.WriteLine("FAIL: no circuit");
			ok = false;
		}
		else
		{
			Console.WriteLine("  circuit lap " + (Circuit.Length / PerMetre / 1000.0).ToString("0.00") + " km real, start straight " + (Circuit.StraightLength / PerMetre).ToString("0") + " m, tightest left " + (Circuit.MinLeftRadius / PerMetre).ToString("0") + " m, right " + (Circuit.MinRightTurn / PerMetre).ToString("0") + " m radius; ground tilted " + (Math.Sqrt(Circuit.Px * Circuit.Px + Circuit.Pz * Circuit.Pz) * 100.0).ToString("0.0") + "%, worst cut or fill " + (Circuit.Earthwork / PerMetre).ToString("0") + " m; " + Circuit.Stands.Count + " stands; road " + (Network.CircuitRoad == null ? "none" : (Network.CircuitRoad.S[Network.CircuitRoad.Count - 1] / PerMetre).ToString("0") + " m"));
			if (Network.CircuitRoad == null)
			{
				Console.WriteLine("FAIL: no circuit road");
				ok = false;
			}
		}
		Console.WriteLine("  " + Rally.Report);
		if (!Rally.Exists || Rally.Road == null)
		{
			Console.WriteLine("FAIL: no rally stage");
			ok = false;
		}
		else
		{
			Network.Road stage = Rally.Road;
			double climb = 0.0, low = double.MaxValue, high = double.MinValue;
			for (int i = 1; i < stage.Count; i++)
			{
				climb += Math.Max(0.0, stage.Y[i] - stage.Y[i - 1]);
				low = Math.Min(low, stage.Y[i]);
				high = Math.Max(high, stage.Y[i]);
			}
			Console.WriteLine("  rally stage " + (Rally.Length / PerMetre / 1000.0).ToString("0.00") + " km real, tightest corner " + (Rally.TightestRadius / PerMetre).ToString("0") + " m radius, " + (climb / PerMetre).ToString("0") + " m climbed a lap, " + ((high - low) / PerMetre).ToString("0") + " m from lowest to highest; service road " + (Network.RallyAccess == null ? "none" : (Network.RallyAccess.S[Network.RallyAccess.Count - 1] / PerMetre).ToString("0") + " m"));
			bool service = Town.Lots.Exists(l => l.Kind == LotKind.Service);
			Console.WriteLine("  rally start straight at (" + Rally.MidX.ToString("0") + ", " + Rally.MidZ.ToString("0") + "), service road ends at (" + (Network.RallyAccess == null ? "-" : Network.RallyAccess.X[Network.RallyAccess.Count - 1].ToString("0") + ", " + Network.RallyAccess.Z[Network.RallyAccess.Count - 1].ToString("0")) + "), service area " + (service ? "laid out" : "MISSING"));
			if (Network.RallyAccess == null || !service)
			{
				Console.WriteLine("FAIL: no rally service road or area");
				ok = false;
			}
		}

		Console.WriteLine("  " + HillClimb.Report);
		if (!HillClimb.Exists)
		{
			Console.WriteLine("FAIL: no hill climb");
			ok = false;
		}
		else
		{
			Network.Road climb = HillClimb.Road;
			double low = double.MaxValue, high = double.MinValue, worst = 0.0;
			for (int i = 0; climb != null && i < climb.Count; i++)
			{
				low = Math.Min(low, climb.Y[i]);
				high = Math.Max(high, climb.Y[i]);
				if (i > 0)
				{
					worst = Math.Max(worst, Math.Abs(climb.Y[i] - climb.Y[i - 1]) / Math.Max(1e-6, climb.S[i] - climb.S[i - 1]));
				}
			}
			bool paddock = Town.Lots.Exists(l => l.Kind == LotKind.Paddock), turning = Town.Lots.Exists(l => l.Kind == LotKind.Turning);
			Console.WriteLine("  hill climb " + (HillClimb.Length / PerMetre / 1000.0).ToString("0.00") + " km real, " + HillClimb.Hairpins + " hairpins, tightest corner " + (HillClimb.TightestRadius / PerMetre).ToString("0") + " m radius, " + ((high - low) / PerMetre).ToString("0") + " m from foot to top, steepest " + (worst * 100.0).ToString("0") + "%; lane " + (Network.ClimbAccess == null ? "none" : (Network.ClimbAccess.S[Network.ClimbAccess.Count - 1] / PerMetre).ToString("0") + " m") + ", paddock " + (paddock ? "laid out" : "MISSING") + ", turning area " + (turning ? "laid out" : "MISSING"));
			if (climb == null || Network.ClimbAccess == null || !paddock || !turning)
			{
				Console.WriteLine("FAIL: no hill climb road, lane, paddock or turning area");
				ok = false;
			}
		}

		// The road signs: how many, and every board's face drawn out to look at.
		Console.WriteLine("  signs: " + Signs.NameSigns + " place-name signs, " + Signs.Fingerposts + " fingerposts with " + Signs.Fingers + " fingers; lettering atlas " + Signs.AtlasW + " x " + Signs.AtlasH + "; " + Signs.Report);
		if (Signs.NameSigns == 0 || Signs.Fingerposts == 0)
		{
			Console.WriteLine("FAIL: no road signs");
			ok = false;
		}
		string signsOut = Path.ChangeExtension(output, null) + "-signs.ppm";
		Write(signsOut, Signs.AtlasW, Signs.AtlasH, Signs.Atlas, true);
		Console.WriteLine("wrote " + signsOut);

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
		double[] length = new double[9];
		int[] count = new int[9];
		double[] steepest = new double[9];
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
		for (int k = 0; k < 9; k++)
		{
			double km = length[k] / PerMetre / 1000.0;
			total += km;
			Console.WriteLine("  " + ((RoadKind)k).ToString().PadRight(10) + count[k].ToString().PadLeft(4) + " roads " + km.ToString("0.0").PadLeft(6) + " km real   steepest " + (steepest[k] * 100.0).ToString("0") + "%");
		}
		Console.WriteLine("  all roads " + total.ToString("0") + " km real, " + (total / realKm2).ToString("0.0") + " km per km² (rural England is about 2, its towns 10 to 20)");

		// Junctions: a road end on another road is a T; roads crossing mid-way a crossroads; an end on nothing a dead end.
		// Dead ends out in the country (outside every town and village) are counted by class: the through ways keep them down.
		int tees = 0, deadEnds = 0, crossings = 0;
		int[] country = new int[9];
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
					bool town = false;
					foreach (Places.Place p in Places.All)
					{
						town |= p.Kind != PlaceKind.Hamlet && (r.X[end] - p.X) * (r.X[end] - p.X) + (r.Z[end] - p.Z) * (r.Z[end] - p.Z) < p.Radius * p.Radius;
					}
					country[(int)r.Kind] += town ? 0 : 1;
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
		Console.WriteLine("junctions: " + tees + " T-junction ends, about " + crossings / 2 + " crossroads, " + deadEnds + " dead ends; in the country " + country[(int)RoadKind.Lane] + " lanes', " + country[(int)RoadKind.Track] + " tracks', " + country[(int)RoadKind.Path] + " footpaths'");

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

		byte[] map = Draw(size, x0, z0, span);
		Garages(map, size, x0, z0, span);
		Events(map, size, x0, z0, span);
		Write(output, size, size, map, false);
		Console.WriteLine("wrote " + output);
		// The same map with the places and courses named, in the signs' lettering.
		string labelled = Path.ChangeExtension(output, null) + "-labels.ppm";
		Label(map, size, x0, z0, span);
		Write(labelled, size, size, map, false);
		Console.WriteLine("wrote " + labelled);
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

	/// <summary>The map as RGBA, rows from the top.</summary>
	private static byte[] Draw(int size, double x0, double z0, double span)
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
		byte[] rgb = new byte[size * size * 4];
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
				// The circuit: track dark, kerbs red, gravel sand, pits and paddock grey.
				CircuitSurface cs = Circuit.SurfaceAt(x, z);
				if (cs != CircuitSurface.None)
				{
					(r, g, b) = cs == CircuitSurface.Track ? (60, 60, 64) : cs == CircuitSurface.Kerb ? (200, 50, 50) : cs == CircuitSurface.Gravel ? (205, 190, 150) : (120, 120, 126);
				}
				if (Circuit.Building(x, z))
				{
					(r, g, b) = (95, 90, 100);
				}
				// Lots: their surface grey, their building darker.
				if (Town.At(x, z, out bool building) != null)
				{
					(r, g, b) = building ? (95, 90, 100) : (150, 150, 155);
				}
				int o = (j * size + i) * 4;
				rgb[o] = (byte)Math.Min(255.0, r);
				rgb[o + 1] = (byte)Math.Min(255.0, g);
				rgb[o + 2] = (byte)Math.Min(255.0, b);
				rgb[o + 3] = 255;
			}
		}
		byte[][] colours = { new byte[] { 225, 110, 40 }, new byte[] { 235, 190, 60 }, new byte[] { 240, 225, 130 }, new byte[] { 70, 70, 74 }, new byte[] { 238, 236, 228 }, new byte[] { 150, 110, 70 }, new byte[] { 196, 180, 150 }, new byte[] { 200, 120, 50 }, new byte[] { 200, 70, 150 } };
		for (int kind = 8; kind >= 0; kind--)
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
								int o = (py * size + px) * 4;
								rgb[o] = c[0];
								rgb[o + 1] = c[1];
								rgb[o + 2] = c[2];
							}
						}
					}
				}
			}
		}
		return rgb;
	}

	/// <summary>Writes an RGBA buffer as a PPM, its rows from the bottom if <paramref name="up"/>.</summary>
	private static void Write(string output, int w, int h, byte[] rgba, bool up)
	{
		byte[] rgb = new byte[w * h * 3];
		for (int y = 0; y < h; y++)
		{
			int from = up ? h - 1 - y : y;
			for (int x = 0; x < w; x++)
			{
				for (int c = 0; c < 3; c++)
				{
					rgb[(y * w + x) * 3 + c] = rgba[(from * w + x) * 4 + c];
				}
			}
		}
		using (FileStream f = File.Create(output))
		{
			byte[] head = System.Text.Encoding.ASCII.GetBytes("P6\n" + w + " " + h + "\n255\n");
			f.Write(head, 0, head.Length);
			f.Write(rgb, 0, rgb.Length);
		}
	}

	/// <summary>
	/// Names the towns, villages, race courses and the start on the map, in the road signs' lettering with a dark outline, each beside
	/// a dot where it is, on whichever side of the dot is clear of the names already put down (towns first).
	/// </summary>
	private static void Label(byte[] map, int size, double x0, double z0, double span)
	{
		double cell = span / size;
		List<(int x0, int y0, int x1, int y1)> taken = new List<(int, int, int, int)>();
		List<(string text, double x, double z, int scale, byte[] colour, int dot)> labels = new List<(string, double, double, int, byte[], int)>();
		byte[] place = { 255, 255, 255 }, course = { 255, 214, 120 };
		foreach (Places.Place p in Places.All)
		{
			if (p.Kind == PlaceKind.Town)
			{
				labels.Add((p.Name, p.X, p.Z, 3, place, 4));
			}
		}
		labels.Add(("Start", Network.SpawnX, Network.SpawnZ, 2, new byte[] { 140, 230, 255 }, 3));
		if (Circuit.Exists)
		{
			double cx = 0.0, cz = 0.0;
			for (int i = 0; i < Circuit.Count; i++)
			{
				cx += Circuit.X[i] / Circuit.Count;
				cz += Circuit.Z[i] / Circuit.Count;
			}
			labels.Add((Names.Circuit, cx, cz, 2, course, 3));
		}
		if (Rally.Exists)
		{
			labels.Add((Names.Rally, Rally.MidX, Rally.MidZ, 2, course, 3));
		}
		if (HillClimb.Exists && HillClimb.Road != null)
		{
			Network.Road climb = HillClimb.Road;
			labels.Add((Names.HillClimb, climb.X[climb.Count / 2], climb.Z[climb.Count / 2], 2, course, 3));
		}
		foreach (Places.Place p in Places.All)
		{
			if (p.Kind == PlaceKind.Village)
			{
				labels.Add((p.Name, p.X, p.Z, 2, place, 3));
			}
		}
		// Each dot first (so no name covers another place's dot), then the names.
		foreach ((string _, double x, double z, int _, byte[] _, int dot) in labels)
		{
			int px = (int)((x - x0) / cell), py = (int)((z0 - z) / cell);
			taken.Add((px - dot - 1, py - dot - 1, px + dot + 1, py + dot + 1));
			for (int a = -dot - 1; a <= dot + 1; a++)
			{
				for (int b = -dot - 1; b <= dot + 1; b++)
				{
					bool rim = Math.Abs(a) == dot + 1 || Math.Abs(b) == dot + 1;
					Pixel(map, size, px + a, py + b, rim ? new byte[] { 20, 20, 24 } : new byte[] { 255, 255, 255 });
				}
			}
		}
		foreach ((string text, double x, double z, int scale, byte[] colour, int dot) in labels)
		{
			int px = (int)((x - x0) / cell), py = (int)((z0 - z) / cell);
			int w = Lettering.Width(text) * scale, h = Lettering.Rows * scale, cap = Lettering.Cap * scale;
			int gap = dot + 4;
			(int x, int y)[] tries = { (px + gap, py - cap / 2), (px - gap - w, py - cap / 2), (px - w / 2, py - gap - h), (px - w / 2, py + gap), (px + gap, py - gap - cap), (px - gap - w, py + gap) };
			(int x, int y) at = tries[0];
			foreach ((int tx, int ty) in tries)
			{
				if (tx < 6 || ty < 6 || tx + w > size - 8 || ty + h > size - 6)
				{
					continue;
				}
				bool clash = taken.Exists(r => tx - 2 < r.x1 && tx + w + 2 > r.x0 && ty - 2 < r.y1 && ty + h + 2 > r.y0);
				at = (tx, ty);
				if (!clash)
				{
					break;
				}
			}
			taken.Add((at.x, at.y, at.x + w, at.y + h));
			// A dark outline a pixel round every stroke, then the letters.
			byte[] ink = new byte[size * size * 4];
			for (int dy = -1; dy <= 1; dy++)
			{
				for (int dx = -1; dx <= 1; dx++)
				{
					Lettering.Draw(ink, size, size, false, at.x + dx, at.y + dy, text, scale, 20, 20, 24);
				}
			}
			Lettering.Draw(ink, size, size, false, at.x, at.y, text, scale, colour[0], colour[1], colour[2]);
			for (int k = 0; k < size * size; k++)
			{
				if (ink[k * 4 + 3] != 0)
				{
					map[k * 4] = ink[k * 4];
					map[k * 4 + 1] = ink[k * 4 + 1];
					map[k * 4 + 2] = ink[k * 4 + 2];
				}
			}
		}
	}

	/// <summary>Each group of garages as the map pictures mark it: a cyan disc in a dark ring, about 100 units across, at least 9 pixels.</summary>
	private static void Garages(byte[] map, int size, double x0, double z0, double span)
	{
		double cell = span / size;
		foreach (GarageSites.Mark g in GarageSites.Marks)
		{
			double cp = (g.X - x0) / cell, cq = (z0 - g.Z) / cell, disc = Math.Max(3.5, 11.0 * 250.0 / 64.0 / cell), ring = disc + Math.Max(1.0, 2.0 * 250.0 / 64.0 / cell);
			for (int q = (int)Math.Floor(cq - ring); q <= (int)Math.Floor(cq + ring); q++)
			{
				for (int p = (int)Math.Floor(cp - ring); p <= (int)Math.Floor(cp + ring); p++)
				{
					double d = Math.Sqrt((p + 0.5 - cp) * (p + 0.5 - cp) + (q + 0.5 - cq) * (q + 0.5 - cq));
					if (d <= ring)
					{
						Pixel(map, size, p, q, d <= disc ? new byte[] { 40, 220, 255 } : new byte[] { 16, 36, 52 });
					}
				}
			}
		}
	}

	/// <summary>Each event's start box as the map pictures mark it: the chequered flag of <see cref="Courses.MapFlag"/>, at least 0.4 of its size.</summary>
	private static void Events(byte[] map, int size, double x0, double z0, double span)
	{
		byte[][] colours = { null, new byte[] { 18, 18, 22 }, new byte[] { 255, 140, 26 }, new byte[] { 245, 245, 245 }, new byte[] { 30, 30, 34 } };
		double cell = span / size, k = Math.Max(0.4, 250.0 / 64.0 / cell);
		foreach (Courses.Course c in Courses.All)
		{
			double cp = (c.BoxX - x0) / cell, cq = (z0 - c.BoxZ) / cell;
			for (int q = (int)Math.Floor(cq - Courses.FlagUp * k); q <= (int)Math.Ceiling(cq + Courses.FlagDown * k); q++)
			{
				for (int p = (int)Math.Floor(cp - Courses.FlagLeft * k); p <= (int)Math.Ceiling(cp + Courses.FlagRight * k); p++)
				{
					int part = Courses.MapFlag((int)Math.Floor((p + 0.5 - cp) / k), (int)Math.Floor((cq - (q + 0.5)) / k));
					if (part != Courses.FlagNone)
					{
						Pixel(map, size, p, q, colours[part]);
					}
				}
			}
		}
	}

		private static void Pixel(byte[] map, int size, int x, int y, byte[] c)
	{
		if (x >= 0 && y >= 0 && x < size && y < size)
		{
			map[(y * size + x) * 4] = c[0];
			map[(y * size + x) * 4 + 1] = c[1];
			map[(y * size + x) * 4 + 2] = c[2];
		}
	}
}
