using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HalfGripOpenWorld;
using UnityEngine;

// The junction renderer: the tiles' road, verge, sidewalk and track meshes (RoadMesh.Build, as the game builds them) drawn from above
// as PPM pictures, half a unit a pixel or finer, with what is wrong marked. For looking at junctions, hubs and dead ends after a
// change to Network or RoadMesh, and for finding the bad ones. meshcheck's holes check is this renderer's core.
//
//   dotnet run -c Release --project tools/jview -- <mode> ...    (-p:GameDir=<game folder> if HalfGripRefs is not next to the repo)
//
//   at <out.ppm> <x> <z> [span]        one picture centred on world (x, z), span units across (100), 400 pixels; stats on stdout
//   list <file> <outdir> [span]        one picture per "x z" line of the file (000.ppm, 001.ppm...), stats per line
//   junctions <outdir> <count> <seed>  pictures of <count> junctions picked at random (seeded), and their list
//   deadends <outdir> <count> <seed>   the same for dead ends
//   measure [outdir]                   every paved junction end and crossing at 60 across: the totals and the worst 25
//   hubs                               every hub (Network.Hub): place, arms, cuts, outline points, triangles, class
//   places                             every town, village and hamlet: name, kind, x, z (the teleport's villages are among them)
//   near <x> <z> <radius>              the roads within radius: kind, samples, ends, gaps (each edge's cuts), length, joints, name
//   dump <road> <from> <to>            a road's samples: x, z, height, distance along, side, cross-fall, drawn half width
//   meet <road> <others,...>           how a road's mouth meets the others' surface along its first 24 units
//   pairs                              road ends meeting road ends: counts by distance, bend and height step
//   ends                               what every road end meets (a bigger road, one of its class, a smaller, nothing)
//
// The colours: asphalt by class (darker bigger), verge sand, sidewalk pale, tracks brown, ground green; centre lines one colour per
// road. Marked: cyan, a road's own strip (cuts ignored) with nothing drawn on top (ground showing in a mouth); magenta, a smaller
// road or a verge or sidewalk on top of a bigger road's strip, or two surfaces at one height (flicker); yellow, paving under the
// ground; orange, neighbouring paving pixels more than 0.1 apart (a lip); red, the visible surface stepping more than 0.12 (often
// just a verge's outer edge). JVIEW_CLEAN=1 leaves the marks and centre lines out (for pictures to show).
//
// "gap" counts cyan pixels, "poke" magenta (on a road's strip), "lip" orange; a street lying wholly along a bigger road is hidden
// under it but still counted, so read the pictures, not only the numbers.
//
// Before and after: build the same tool against an older commit and render the same list, e.g.
//   git worktree add ../ow-before <commit>
//   dotnet run -c Release --project ../ow-before/tools/jview -- list spots.txt before 160    (if that commit has tools/jview;
//   else copy this folder into it)
//   dotnet run -c Release --project tools/jview -- list spots.txt after 160
internal static class Program
{
	const float Tile = 250f;
	static readonly TerrainGrid Terrain = new TerrainGrid(0, 0, 0, null);
	static readonly System.Collections.Concurrent.ConcurrentDictionary<long, Lazy<List<(Vector3 a, Vector3 b, Vector3 c, int cat)>>> cache = new System.Collections.Concurrent.ConcurrentDictionary<long, Lazy<List<(Vector3, Vector3, Vector3, int)>>>();

	static int Main(string[] args)
	{
		Network.Ensure();
		string mode = args[0];
		if (mode == "meet")
		{
			int rr = int.Parse(args[1]);
			var road = Network.Roads[rr];
			var others = args[2].Split(',').Select(int.Parse).ToList();
			var mi = typeof(Network).GetMethod("Meet", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
			var su = typeof(Network).GetMethod("Surface", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
			for (double sv = 0; sv <= 24; sv += 2)
			{
				object[] a = { road, others, sv, 0.0, 0.0 };
				bool ok = (bool)mi.Invoke(null, a);
				Network.Edge(road, sv, 1, 0, out double lx, out double lz, out double ly);
				Network.Edge(road, sv, -1, 0, out double rx, out double rz, out double ry);
				object[] b = { others, lx, lz, 0.0 }; su.Invoke(null, b);
				object[] c = { others, rx, rz, 0.0 }; su.Invoke(null, c);
				string per = string.Join(" ", others.Select(o => { object[] d = { new List<int> { o }, lx, lz, 0.0 }; su.Invoke(null, d); return o + ":" + ((double)d[3]).ToString("0.00"); }));
				Console.WriteLine(sv + " meet " + ok + " y " + ((double)a[3]).ToString("0.000") + " tilt " + ((double)a[4]).ToString("0.000") + " drawnL " + ly.ToString("0.00") + " drawnR " + ry.ToString("0.00") + " surfL " + ((double)b[3]).ToString("0.00") + " surfR " + ((double)c[3]).ToString("0.00") + " perL " + per);
			}
			return 0;
		}
		if (mode == "hubs")
		{
			foreach (var h in Network.Hubs) Console.WriteLine(h.X.ToString("0") + " " + h.Z.ToString("0") + " arms " + h.Arm.Length + " cuts " + string.Join(",", h.Cut.Select(c => c.ToString("0"))) + " pts " + h.PX.Length + " tris " + (h.Tris == null ? 0 : h.Tris.Length / 3) + " kind " + h.Kind);
			return 0;
		}
		if (mode == "places")
		{
			foreach (Places.Place pl in Places.All) Console.WriteLine(pl.Name + " " + pl.Kind + " " + pl.X.ToString("0") + " " + pl.Z.ToString("0"));
			return 0;
		}
		if (mode == "list")
		{
			string[] lines = File.ReadAllLines(args[1]);
			Directory.CreateDirectory(args[2]);
			double span = args.Length > 3 ? double.Parse(args[3]) : 100.0;
			for (int n = 0; n < lines.Length; n++)
			{
				string[] f = lines[n].Split(' ', StringSplitOptions.RemoveEmptyEntries);
				if (f.Length < 2) continue;
				Render(Path.Combine(args[2], n.ToString("000") + ".ppm"), double.Parse(f[0]), double.Parse(f[1]), span, 400, out string st);
				Console.WriteLine(n.ToString("000") + " " + lines[n] + " " + st);
			}
			return 0;
		}
		if (mode == "dump")
		{
			var rd = Network.Roads[int.Parse(args[1])];
			int from = int.Parse(args[2]), to = Math.Min(rd.Count - 1, int.Parse(args[3]));
			for (int i = from; i <= to; i++) { Network.Side(rd, i, out double sx, out double sz); Console.WriteLine(i + " " + rd.X[i].ToString("0.00") + " " + rd.Z[i].ToString("0.00") + " y " + rd.Y[i].ToString("0.000") + " s " + rd.S[i].ToString("0.0") + " side " + sx.ToString("0.000") + "," + sz.ToString("0.000") + " tilt " + rd.Tilt[i].ToString("0.000") + " half " + Network.HalfAt(rd, rd.S[i]).ToString("0.00")); }
			return 0;
		}
		if (mode == "near")
		{
			double qx = double.Parse(args[1]), qz = double.Parse(args[2]), rad = double.Parse(args[3]);
			for (int r = 0; r < Network.Roads.Count; r++)
			{
				var rd = Network.Roads[r];
				double best = 1e9; int bi = 0;
				for (int i = 0; i < rd.Count; i++) { double d = Math.Sqrt(Math.Pow(rd.X[i] - qx, 2) + Math.Pow(rd.Z[i] - qz, 2)); if (d < best) { best = d; bi = i; } }
				if (best > rad) continue;
				Console.WriteLine(r + " " + rd.Kind + " n=" + rd.Count + " nearest sample " + bi + " d=" + best.ToString("0") + " start " + rd.X[0].ToString("0") + "," + rd.Z[0].ToString("0") + " end " + rd.X[rd.Count - 1].ToString("0") + "," + rd.Z[rd.Count - 1].ToString("0") + " gaps " + string.Join(";", rd.Gaps.Select(g => g.C0.ToString("0") + ".." + g.C1.ToString("0") + "[L" + g.L0.ToString("0") + ".." + g.L1.ToString("0") + " R" + g.R0.ToString("0") + ".." + g.R1.ToString("0") + "]")) + " len " + rd.S[rd.Count - 1].ToString("0") + " j " + rd.Joined0 + rd.Joined1 + " dead " + rd.Dead0 + rd.Dead1 + " name " + rd.Name);
			}
			return 0;
		}
		if (mode == "measure")
		{
			var mspots = Spots();
			var worst = new List<(long bad, string line)>();
			string dirm = args.Length > 1 ? args[1] : null;
			if (dirm != null) Directory.CreateDirectory(dirm);
			EnsureBoxes();
			var results = new (long bad, string line)[mspots.Count];
			System.Threading.Tasks.Parallel.For(0, mspots.Count, n =>
			{
				Render("/dev/null", mspots[n].Item1, mspots[n].Item2, 60.0, 240, out string st, out long bad);
				results[n] = (bad, mspots[n].Item1.ToString("0") + " " + mspots[n].Item2.ToString("0") + " " + mspots[n].Item3 + " " + st);
			});
			worst.AddRange(results);
			Console.WriteLine("spots " + mspots.Count + " nominal px " + Nominal + " gap " + Gap + " poke " + Poke + " lip " + Lip + " lip>0.2 " + BigLip);
			foreach (var w in worst.OrderByDescending(w => w.bad).Take(25)) Console.WriteLine(w.line);
			return 0;
		}
		if (mode == "pairs")
		{
			Pairs();
			return 0;
		}
		if (mode == "ends")
		{
			Ends();
			return 0;
		}
		if (mode == "at")
		{
			double span = args.Length > 4 ? double.Parse(args[4]) : 100.0;
			Render(args[1], double.Parse(args[2]), double.Parse(args[3]), span, 400, out string st);
			Console.WriteLine(st);
			return 0;
		}
		string dir = args[1];
		int count = int.Parse(args[2]);
		ulong seed = ulong.Parse(args[3]);
		Directory.CreateDirectory(dir);
		List<(double x, double z, string what)> spots = mode == "junctions" ? Junctions() : DeadEnds();
		Console.WriteLine(mode + ": " + spots.Count);
		Rng rng = new Rng(seed);
		List<int> order = Enumerable.Range(0, spots.Count).ToList();
		for (int k = order.Count - 1; k > 0; k--)
		{
			int j = (int)(rng.Next() * (k + 1));
			(order[k], order[j]) = (order[j], order[k]);
		}
		for (int n = 0; n < Math.Min(count, order.Count); n++)
		{
			var sp = spots[order[n]];
			Render(Path.Combine(dir, n.ToString("000") + ".ppm"), sp.x, sp.z, 100.0, 400, out string st);
			Console.WriteLine(n.ToString("000") + " " + sp.x.ToString("0") + " " + sp.z.ToString("0") + " " + sp.what + " " + st);
		}
		return 0;
	}

	static void Pairs()
	{
		int n = Network.Roads.Count;
		var hist = new SortedDictionary<string, int>();
		for (int r = 0; r < n; r++)
		{
			var a = Network.Roads[r];
			if (Network.Unpaved(a.Kind) || a.Kind > RoadKind.Lane) continue;
			for (int ea = 0; ea < 2; ea++)
			{
				int ka = ea == 0 ? 0 : a.Count - 1, na = ea == 0 ? 1 : a.Count - 2;
				for (int o = r + 1; o < n; o++)
				{
					var b = Network.Roads[o];
					if (Network.Unpaved(b.Kind) || b.Kind > RoadKind.Lane) continue;
					for (int eb = 0; eb < 2; eb++)
					{
						int kb = eb == 0 ? 0 : b.Count - 1, nb = eb == 0 ? 1 : b.Count - 2;
						double d = Math.Sqrt(Math.Pow(a.X[ka] - b.X[kb], 2) + Math.Pow(a.Z[ka] - b.Z[kb], 2));
						if (d > 20) continue;
						// outward directions at each end
						double ax = a.X[ka] - a.X[na], az = a.Z[ka] - a.Z[na], bx = b.X[kb] - b.X[nb], bz = b.Z[kb] - b.Z[nb];
						double cos = -(ax * bx + az * bz) / Math.Sqrt((ax * ax + az * az) * (bx * bx + bz * bz));
						double ang = Math.Acos(Math.Max(-1, Math.Min(1, cos))) * 180 / Math.PI;
						string key = "d" + (d < 0.01 ? "0" : d < 1 ? "<1" : d < 5 ? "<5" : "<20") + " turn" + ((int)(ang / 15) * 15).ToString().PadLeft(3) + " dy" + (Math.Abs(a.Y[ka] - b.Y[kb]) < 0.01 ? "0" : Math.Abs(a.Y[ka] - b.Y[kb]) < 0.3 ? "<.3" : ">.3");
						hist[key] = hist.TryGetValue(key, out int c) ? c + 1 : 1;
						if (d < 0.01 && ang > 45) Console.WriteLine("sharp " + a.Kind + "/" + b.Kind + " " + a.X[ka].ToString("0") + " " + a.Z[ka].ToString("0") + " turn " + ang.ToString("0"));
					}
				}
			}
		}
		foreach (var kv in hist) Console.WriteLine(kv.Value.ToString().PadLeft(5) + " " + kv.Key);
	}

	static void Ends()
	{
		var counts = new SortedDictionary<string, int>();
		var examples = new Dictionary<string, string>();
		for (int r = 0; r < Network.Roads.Count; r++)
		{
			var road = Network.Roads[r];
			if (road.Kind > RoadKind.Path) continue;
			for (int e = 0; e < 2; e++)
			{
				int k = e == 0 ? 0 : road.Count - 1;
				double x = road.X[k], z = road.Z[k];
				string best = null; double bd = 1e9;
				for (int o = 0; o < Network.Roads.Count; o++)
				{
					if (o == r) continue;
					var other = Network.Roads[o];
					for (int i = 0; i + 1 < other.Count; i++)
					{
						if (Math.Abs(other.X[i] - x) > 60 || Math.Abs(other.Z[i] - z) > 60) continue;
						Lines.Project(other.X, other.Z, i, x, z, out double t, out double d);
						if (d < other.Half + 1.0 && d < bd)
						{
							bd = d;
							bool nearEnd = (i == 0 && t < 0.5) || (i + 2 >= other.Count && t > 0.5) || (i <= 1) || (i >= other.Count - 3);
							string rel = other.Kind < road.Kind ? "onBigger" : other.Kind == road.Kind ? (o < r ? "onSameEarlier" : "onSameLater") : "onSmaller";
							best = road.Kind + " " + rel + (nearEnd ? " endToEnd" : " through") + (Network.Unpaved(other.Kind) || Network.Unpaved(road.Kind) ? " unpaved" : "");
						}
					}
				}
				if (best == null) best = road.Kind + " dead";
				counts[best] = counts.TryGetValue(best, out int c) ? c + 1 : 1;
				if (!examples.ContainsKey(best)) examples[best] = x.ToString("0") + " " + z.ToString("0");
			}
		}
		foreach (var kv in counts) Console.WriteLine(kv.Value.ToString().PadLeft(5) + "  " + kv.Key + "   e.g. " + examples[kv.Key]);
	}

	static List<(double, double, string)> Spots()
	{
		var list = new List<(double, double, string)>();
		for (int r = 0; r < Network.Roads.Count; r++)
		{
			var road = Network.Roads[r];
			if (Network.Unpaved(road.Kind) || road.Kind > RoadKind.Lane) continue;
			double len = road.S[road.Count - 1];
			foreach (int k in new[] { 0, road.Count - 1 })
			{
				double x = road.X[k], z = road.Z[k];
				if (!list.Any(p => Math.Abs(p.Item1 - x) < 20 && Math.Abs(p.Item2 - z) < 20)) list.Add((x, z, road.Kind + (k == 0 ? "-start" : "-end")));
			}
			foreach (var g in road.Gaps)
			{
				if (g.C0 < 0 || g.C1 > len) continue;
				double s = (g.C0 + g.C1) * 0.5;
				Network.Locate(road, s, out int i, out double t);
				double x = road.X[i] + (road.X[i + 1] - road.X[i]) * t, z = road.Z[i] + (road.Z[i + 1] - road.Z[i]) * t;
				if (!list.Any(p => Math.Abs(p.Item1 - x) < 20 && Math.Abs(p.Item2 - z) < 20)) list.Add((x, z, road.Kind + "-cross"));
			}
		}
		return list;
	}

	static List<(double, double, string)> Junctions()
	{
		var list = new List<(double, double, string)>();
		for (int r = 0; r < Network.Roads.Count; r++)
		{
			var road = Network.Roads[r];
			double len = road.S[road.Count - 1];
			foreach (var g in road.Gaps)
			{
				double s = Math.Max(0, Math.Min(len, (Math.Max(0, g.C0) + Math.Min(len, g.C1)) * 0.5));
				Network.Locate(road, s, out int i, out double t);
				double x = road.X[i] + (road.X[i + 1] - road.X[i]) * t, z = road.Z[i] + (road.Z[i + 1] - road.Z[i]) * t;
				if (list.Any(p => Math.Abs(p.Item1 - x) < 30 && Math.Abs(p.Item2 - z) < 30))
				{
					continue;
				}
				list.Add((x, z, road.Kind + (g.C0 < 0 ? "-start" : g.C1 > len ? "-end" : "-cross")));
			}
		}
		return list;
	}

	static List<(double, double, string)> DeadEnds()
	{
		var list = new List<(double, double, string)>();
		for (int r = 0; r < Network.Roads.Count; r++)
		{
			var road = Network.Roads[r];
			if (road.Kind > RoadKind.Path)
			{
				continue;
			}
			double len = road.S[road.Count - 1];
			for (int e = 0; e < 2; e++)
			{
				int k = e == 0 ? 0 : road.Count - 1;
				double x = road.X[k], z = road.Z[k];
				if (road.Gaps.Any(g => e == 0 ? g.C0 < 0 : g.C1 > len))
				{
					continue;
				}
				bool joined = false;
				for (int o = 0; o < Network.Roads.Count && !joined; o++)
				{
					if (o == r)
					{
						continue;
					}
					var other = Network.Roads[o];
					for (int i = 0; i + 1 < other.Count; i++)
					{
						if (Math.Abs(other.X[i] - x) > 60 || Math.Abs(other.Z[i] - z) > 60)
						{
							continue;
						}
						Lines.Project(other.X, other.Z, i, x, z, out double _, out double d);
						if (d < other.Half + 3.0)
						{
							joined = true;
							break;
						}
					}
				}
				if (!joined)
				{
					list.Add((x, z, road.Kind + "-deadend"));
				}
			}
		}
		return list;
	}

	static List<(Vector3, Vector3, Vector3, int)> TileTris(int tx, int tz)
	{
		long key = ((long)tx << 32) | (uint)tz;
		return cache.GetOrAdd(key, _ => new Lazy<List<(Vector3, Vector3, Vector3, int)>>(() => BuildTile(tx, tz))).Value;
	}

	static List<(Vector3, Vector3, Vector3, int)> BuildTile(int tx, int tz)
	{
		var list = new List<(Vector3, Vector3, Vector3, int)>();
		RoadMeshData road = RoadMesh.Build(tx, tz, Tile, Terrain, out RoadMeshData verges, out RoadMeshData sidewalks, out RoadMeshData tracks);
		Vector3 o = new Vector3(tx * Tile, 0, tz * Tile);
		void Add(RoadMeshData d, int baseCat)
		{
			if (d == null)
			{
				return;
			}
			for (int m = 0; m < d.Triangles.Length; m++)
			{
				int[] t = d.Triangles[m];
				for (int k = 0; k < t.Length; k += 3)
				{
					list.Add((d.Vertices[t[k]] + o, d.Vertices[t[k + 1]] + o, d.Vertices[t[k + 2]] + o, baseCat + m));
				}
			}
		}
		Add(road, 0);
		Add(verges, 10);
		Add(sidewalks, 11);
		Add(tracks, 12);
		return list;
	}

	static readonly int[][] Colours =
	{
		new[] { 60, 60, 66 }, new[] { 80, 80, 88 }, new[] { 95, 95, 102 }, new[] { 112, 112, 118 }, new[] { 128, 124, 118 }, new[] { 175, 175, 175 },
		null, null, null, null,
		new[] { 165, 145, 100 }, new[] { 200, 198, 185 }, new[] { 125, 95, 55 }, new[] { 150, 120, 80 }, new[] { 140, 110, 70 },
	};

	static long Gap, Poke, Lip, Nominal, BigLip;
	static readonly bool Clean = Environment.GetEnvironmentVariable("JVIEW_CLEAN") == "1";
	static double[,] boxes;

	static void Render(string file, double cx, double cz, double span, int px, out string stats) => Render(file, cx, cz, span, px, out stats, out long _);

	static void EnsureBoxes()
	{
		if (boxes != null) return;
		var b = new double[Network.Roads.Count, 4];
		for (int r = 0; r < Network.Roads.Count; r++)
		{
			var rd = Network.Roads[r];
			b[r, 0] = rd.X.Min() - rd.Half; b[r, 1] = rd.X.Max() + rd.Half; b[r, 2] = rd.Z.Min() - rd.Half; b[r, 3] = rd.Z.Max() + rd.Half;
		}
		boxes = b;
	}

	static void Render(string file, double cx, double cz, double span, int px, out string stats, out long badOut)
	{
		EnsureBoxes();
		double x0 = cx - span / 2, z0 = cz - span / 2, step = span / px;
		var tris = new List<(Vector3 a, Vector3 b, Vector3 c, int cat)>();
		for (int tz = (int)Math.Floor(z0 / Tile) - 1; tz <= (int)Math.Floor((z0 + span) / Tile) + 1; tz++)
		{
			for (int tx = (int)Math.Floor(x0 / Tile) - 1; tx <= (int)Math.Floor((x0 + span) / Tile) + 1; tx++)
			{
				tris.AddRange(TileTris(tx, tz));
			}
		}
		float[] top = new float[px * px], second = new float[px * px], shade = new float[px * px];
		int[] cat = new int[px * px], cat2 = new int[px * px], id = new int[px * px], id2 = new int[px * px];
		for (int k = 0; k < top.Length; k++)
		{
			top[k] = second[k] = float.NegativeInfinity;
			cat[k] = cat2[k] = -1;
		}
		Vector3 light = new Vector3(-0.5f, 1.5f, 0.8f).normalized;
		int tid = 0;
		foreach (var tr in tris)
		{
			tid++;
			Vector3 a = tr.a, b = tr.b, c = tr.c;
			double minx = Math.Min(a.x, Math.Min(b.x, c.x)), maxx = Math.Max(a.x, Math.Max(b.x, c.x));
			double minz = Math.Min(a.z, Math.Min(b.z, c.z)), maxz = Math.Max(a.z, Math.Max(b.z, c.z));
			if (maxx < x0 || minx > x0 + span || maxz < z0 || minz > z0 + span)
			{
				continue;
			}
			Vector3 nrm = Vector3.Cross(b - a, c - a).normalized;
			if (nrm.y < 0)
			{
				nrm = -nrm;
			}
			float sh = 0.55f + 0.45f * Math.Max(0f, Vector3.Dot(nrm, light));
			double det = (b.x - a.x) * (c.z - a.z) - (c.x - a.x) * (b.z - a.z);
			if (Math.Abs(det) < 1e-9)
			{
				continue;
			}
			int i0 = Math.Max(0, (int)((minx - x0) / step)), i1 = Math.Min(px - 1, (int)((maxx - x0) / step));
			int j0 = Math.Max(0, (int)((minz - z0) / step)), j1 = Math.Min(px - 1, (int)((maxz - z0) / step));
			for (int j = j0; j <= j1; j++)
			{
				double z = z0 + (j + 0.5) * step;
				for (int i = i0; i <= i1; i++)
				{
					double x = x0 + (i + 0.5) * step;
					double u = ((x - a.x) * (c.z - a.z) - (c.x - a.x) * (z - a.z)) / det;
					double v = ((b.x - a.x) * (z - a.z) - (x - a.x) * (b.z - a.z)) / det;
					if (u < -1e-6 || v < -1e-6 || u + v > 1 + 1e-6)
					{
						continue;
					}
					float y = (float)(a.y + (b.y - a.y) * u + (c.y - a.y) * v);
					bool deep = u > 0.04 && v > 0.04 && u + v < 0.96;
					int p = (px - 1 - j) * px + i;
					if (y > top[p])
					{
						if (!deep) { if (y > top[p]) { top[p] = y; cat[p] = tr.cat; id[p] = tid; shade[p] = sh; } continue; }
						second[p] = top[p];
						cat2[p] = cat[p];
						id2[p] = id[p];
						top[p] = y;
						cat[p] = tr.cat;
						id[p] = tid;
						shade[p] = sh;
					}
					else if (y > second[p] && deep)
					{
						second[p] = y;
						cat2[p] = tr.cat;
						id2[p] = tid;
					}
				}
			}
		}
		byte[] img = new byte[px * px * 3];
		float[] vis = new float[px * px];
		int terrainThrough = 0, fights = 0, roadPx = 0;
		for (int j = 0; j < px; j++)
		{
			for (int i = 0; i < px; i++)
			{
				int p = (px - 1 - j) * px + i;
				double x = x0 + (i + 0.5) * step, z = z0 + (j + 0.5) * step;
				double g = Terrain.At(x, z);
				int[] col;
				float s;
				bool through = false;
				if (cat[p] >= 0 && top[p] >= g - 0.005)
				{
					col = Colours[cat[p]];
					s = shade[p];
					vis[p] = top[p];
					roadPx++;
				}
				else
				{
					double gx = Terrain.At(x + 0.5, z) - Terrain.At(x - 0.5, z), gz = Terrain.At(x, z + 0.5) - Terrain.At(x, z - 0.5);
					Vector3 n = new Vector3((float)-gx, 1f, (float)-gz).normalized;
					s = 0.55f + 0.45f * Math.Max(0f, Vector3.Dot(n, light));
					col = new[] { 70, 125, 55 };
					vis[p] = (float)g;
					through = cat[p] >= 0 && cat[p] <= 5;
					if (through)
					{
						terrainThrough++;
					}
				}
				int r = (int)(col[0] * s), gg = (int)(col[1] * s), bb = (int)(col[2] * s);
				if (through && !Clean)
				{
					r = 255; gg = 230; bb = 0;
				}
				else if (!Clean && cat[p] >= 0 && cat2[p] >= 0 && top[p] - second[p] < 0.012 && top[p] >= g && Overlapping(cat[p], cat2[p]))
				{
					r = 255; gg = 0; bb = 255;
					fights++;
				}
				img[p * 3] = (byte)Math.Min(255, r);
				img[p * 3 + 1] = (byte)Math.Min(255, gg);
				img[p * 3 + 2] = (byte)Math.Min(255, bb);
			}
		}
		// Nominal paving: the road drawn over all others covering each pixel (smallest kind, then earliest), ignoring cuts.
		int[] over = new int[px * px];
		for (int k = 0; k < over.Length; k++) over[k] = -1;
		for (int r = 0; r < Network.Roads.Count; r++)
		{
			var road = Network.Roads[r];
			if (Network.Unpaved(road.Kind)) continue;
			if (boxes[r, 1] < x0 || boxes[r, 0] > x0 + span || boxes[r, 3] < z0 || boxes[r, 2] > z0 + span) continue;
			for (int i = 0; i + 1 < road.Count; i++)
			{
				double mnx = Math.Min(road.X[i], road.X[i + 1]) - road.Half, mxx = Math.Max(road.X[i], road.X[i + 1]) + road.Half;
				double mnz = Math.Min(road.Z[i], road.Z[i + 1]) - road.Half, mxz = Math.Max(road.Z[i], road.Z[i + 1]) + road.Half;
				if (mxx < x0 || mnx > x0 + span || mxz < z0 || mnz > z0 + span) continue;
				int i0 = Math.Max(0, (int)((mnx - x0) / step)), i1 = Math.Min(px - 1, (int)((mxx - x0) / step));
				int j0 = Math.Max(0, (int)((mnz - z0) / step)), j1 = Math.Min(px - 1, (int)((mxz - z0) / step));
				for (int j = j0; j <= j1; j++)
					for (int ii = i0; ii <= i1; ii++)
					{
						double x = x0 + (ii + 0.5) * step, z = z0 + (j + 0.5) * step;
						Lines.Project(road.X, road.Z, i, x, z, out double t, out double d);
						double ax = road.X[i] + (road.X[i + 1] - road.X[i]) * t - x, az = road.Z[i] + (road.Z[i + 1] - road.Z[i]) * t - z;
						if ((t <= 0 && i == 0) || (t >= 1 && i == road.Count - 2)) continue;
						if (d > road.Half - 0.4) continue;
						int p = (px - 1 - j) * px + ii;
						int cur = over[p];
						if (cur < 0 || road.Kind < Network.Roads[cur].Kind || (road.Kind == Network.Roads[cur].Kind && r < cur)) over[p] = r;
					}
			}
		}
		int gapPx = 0, pokePx = 0, lipPx = 0, nominal = 0;
		for (int p = 0; p < over.Length; p++)
		{
			if (over[p] < 0) continue;
			nominal++;
			int vc = (cat[p] >= 0 && top[p] >= Terrain.At(0, 0) - 1e9 && vis[p] == top[p]) ? cat[p] : -1;
			var o = Network.Roads[over[p]];
			if (vc < 0 && Clean) gapPx++; else if (vc < 0) { gapPx++; img[p * 3] = 0; img[p * 3 + 1] = 255; img[p * 3 + 2] = 255; }
			else if ((vc > 5 || (vc < 5 && vc > (int)o.Kind)) && Clean) pokePx++; else if (vc > 5 || (vc < 5 && vc > (int)o.Kind)) { pokePx++; img[p * 3] = 255; img[p * 3 + 1] = 0; img[p * 3 + 2] = 255; }
		}
		for (int j = 0; j + 1 < px; j++)
			for (int i = 0; i + 1 < px; i++)
			{
				int p = j * px + i;
				foreach (int q in new[] { p + 1, p + px })
				{
					if (over[p] < 0 || over[q] < 0 || cat[p] < 0 || cat[q] < 0 || cat[p] > 5 || cat[q] > 5 || vis[p] != top[p] || vis[q] != top[q]) continue;
					float dl = Math.Abs(vis[p] - vis[q]);
					if (dl > 0.2f) System.Threading.Interlocked.Increment(ref BigLip);
					if (dl > 0.1f && Clean) lipPx++; else if (dl > 0.1f) { lipPx++; img[p * 3] = 255; img[p * 3 + 1] = 128; img[p * 3 + 2] = 0; }
				}
			}
		System.Threading.Interlocked.Add(ref Gap, gapPx); System.Threading.Interlocked.Add(ref Poke, pokePx); System.Threading.Interlocked.Add(ref Lip, lipPx); System.Threading.Interlocked.Add(ref Nominal, nominal);
		// Steps in the visible surface: red where it jumps more than 0.12 between neighbouring pixels.
		int steps = 0;
		for (int j = 0; j + 1 < px; j++)
		{
			for (int i = 0; i + 1 < px; i++)
			{
				int p = j * px + i;
				float d = Math.Max(Math.Abs(vis[p] - vis[p + 1]), Math.Abs(vis[p] - vis[p + px]));
				if (d > 0.12f && !Clean)
				{
					img[p * 3] = 255; img[p * 3 + 1] = 0; img[p * 3 + 2] = 0;
					steps++;
				}
			}
		}
		// centre lines, coloured by road index
		for (int r = 0; !Clean && r < Network.Roads.Count; r++)
		{
			var road = Network.Roads[r];
			if (boxes[r, 1] < x0 || boxes[r, 0] > x0 + span || boxes[r, 3] < z0 || boxes[r, 2] > z0 + span) continue;
			int cr = (r * 97) % 200 + 55, cg = (r * 57) % 200 + 55, cb = (r * 31) % 200 + 55;
			for (int i = 0; i + 1 < road.Count; i++)
				for (double t = 0; t <= 1; t += 0.02)
				{
					double x = road.X[i] + (road.X[i + 1] - road.X[i]) * t, z = road.Z[i] + (road.Z[i + 1] - road.Z[i]) * t;
					int ii = (int)((x - x0) / step), jj = (int)((z - z0) / step);
					if (ii < 0 || jj < 0 || ii >= px || jj >= px) continue;
					int p = (px - 1 - jj) * px + ii;
					img[p * 3] = (byte)cr; img[p * 3 + 1] = (byte)cg; img[p * 3 + 2] = (byte)cb;
				}
		}
		if (file != "/dev/null") using (var f = File.Create(file))
		{
			byte[] head = System.Text.Encoding.ASCII.GetBytes("P6\n" + px + " " + px + "\n255\n");
			f.Write(head, 0, head.Length);
			f.Write(img, 0, img.Length);
		}
		badOut = gapPx + 3 * pokePx + lipPx;
		stats = "gap=" + gapPx + " poke=" + pokePx + " lip=" + lipPx + " through=" + terrainThrough + " fight=" + fights + " steps=" + steps;
	}

	/// <summary>Two different surfaces at nearly one height: road on road, verge on road, and so on (a mesh with itself is not a fight).</summary>
	static bool Overlapping(int a, int b)
	{
		return a != b;
	}
}
