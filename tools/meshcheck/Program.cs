using System;
using System.Collections.Generic;
using HalfGripOpenWorld;
using UnityEngine;

// Checks of the built world that the preview tool cannot make (they need Unity's types and the mesh builders), outside the game:
//   dotnet run -c Release --project tools/meshcheck [-p:GameDir=<game folder>] [-- check names, e.g. signs, to run only those]
//   1. clip:    road surfaces under the terrain mesh (sampled across every road, against the 5-unit triangles the game draws)
//   2. poke:    where two drawn road surfaces overlap (more than half a unit in), the one that should be under (the smaller, or the
//               later of one class) poking through the other (junctions, crossings), by more than 0.02
//   3. float:   road centres floating more than 0.5 over the ground
//   4. grades:  drawn road pieces no steeper than their class's grade allows (Network.Leeway near junctions)
//   5. bridges: every point of a road surface over a river's water at least 4 above the water
//   5b. flooded: no road surface under any water surface drawn over it (a river's out to where its surface is drawn, a lake's or
//                the sea's on its cells and the cells round them)
//   6. verges:  the step from a verge's outer edge to the ground (its inner edge is the road's edge by construction), under 0.1
//   7. tracks:  the ground along farm tracks pressed into their bed, 0.3 under the track's line
//   8. bases:   building bases round the main town: never above the ground, at most the slope allowance under it
//   9. faces:   road, verge and water meshes with no upside-down flat faces, under 65000 vertices
//  11. ribbons:  farm tracks' and footpaths' ribbons over the drawn ground everywhere across them (away from paved roads, which
//                they run under)
//  12. sidewalks: a sidewalk's back on or over the ground, its kerb's foot under it
//  10. lots:    car parks, yards and forecourts: the drawn ground just under their surface (0.02 to 0.3), their front at the foot of
//               the road's verge (within 0.25), clear of every other road; a retail park and businesses laid out
//  13. plants:  no tree or grass tuft standing in water (under a river's, a lake's or the sea's drawn surface), on the tiles along
//                the rivers and round the lakes
//  15. circuit: the lap closed and never near itself away along it, its ground tilted at most 2%, its track, kerbs, pit apron, paint
//                and gravel over the drawn ground everywhere, no road but its own, no place, lot or park in its fence, its barrier
//                and stands clear of the track, its hairpin's radius; the circuit's road joins it to the network
//  14. parks:   each pond's drawn water edge under the drawn ground and its middle under water; no building standing on a park's
//                lawn; every bench clear of the roads and on its ground; every park with a path
//  16. rally:   the stage a closed loop (its ends meet in place, height and width), never near itself (100 apart past 500 along,
//                40 past 100: a hairpin's legs), no corner tighter than its limit; nothing in its fence but its service area and
//                track; every straw bale clear of the stage's edges and standing on the ground; the service area laid out
//  17. climb:   the hill climb never near itself (70 apart past 400 along, 42 past 120: a hairpin's legs), no corner tighter than
//                its limit, no steeper than 13%; its lane meeting its foot end to end (point, height and edges); nothing in its fence but
//                the lane, its paddock and turning area (both laid out); its arches' posts off the verge
//  18. signs:   place-name signs and fingerposts laid out; every post and board off the roads' paving (a name sign's off the verge
//                too), out of the water; no other building's footprint on a sign; every face inside the lettering atlas, none
//                overlapping another; the boards drawn in their tiles' meshes
// Exits 1 if a check fails.
internal static class Program
{
	private static readonly TerrainGrid Terrain = new TerrainGrid(0, 0, 0, null);

	private static int Main(string[] args)
	{
		Network.Ensure();
		bool ok = true;
		// Every check, or only those named on the command line (by the names they print).
		(string name, Func<bool> check)[] checks =
		{
			("clip", Clip),
			("poke", Poke),
			("float", Float),
			("grades", Grades),
			("bridges", Bridges),
			("flooded", Flooded),
			("water", Water),
			("verges", Verges),
			("tracks", Tracks),
			("bases", Bases),
			("faces", Faces),
			("lots", Lots),
			("ribbons", Ribbons),
			("sidewalks", Sidewalks),
			("plants", Plants),
			("parks", ParkChecks),
			("circuit", CircuitChecks),
			("rally", RallyChecks),
			("climb", ClimbChecks),
			("signs", SignChecks),
		};
		foreach ((string name, Func<bool> check) in checks)
		{
			if (args.Length == 0 || Array.IndexOf(args, name) >= 0)
			{
				ok &= check();
			}
		}
		Console.WriteLine(ok ? "all checks pass" : "SOME CHECKS FAIL");
		return ok ? 0 : 1;
	}

	/// <summary>The terrain mesh's height at world (x, z): the 5-unit grid's triangles, as the streamer builds them.</summary>
	private static double Mesh(double x, double z)
	{
		return Terrain.At(x, z);
	}

	private static readonly List<(double t0, double t1)> spans = new List<(double, double)>();

	/// <summary>The first stretch of piece i drawn across the road's whole width, as fractions along it (most pieces have one).</summary>
	private static bool Drawn(Network.Road r, int i, out double t0, out double t1)
	{
		Network.Spans(r, i, spans);
		t0 = spans.Count > 0 ? spans[0].t0 : 0.0;
		t1 = spans.Count > 0 ? spans[0].t1 : 0.0;
		return spans.Count > 0;
	}

	private static bool Clip()
	{
		int samples = 0, bad = 0;
		double worst = 0;
		foreach (Network.Road r in Network.Roads)
		{
			if (Network.Unpaved(r.Kind))
			{
				continue;
			}
			for (int i = 0; i + 1 < r.Count; i += 2)
			{
				if (r.Bridge[i] || !Drawn(r, i, out double t0, out double _))
				{
					continue;
				}
				foreach (double f in new[] { -0.95, -0.5, 0.0, 0.5, 0.95 })
				{
					Network.Point(r, i, t0, f, 0.0, out double x, out double z, out double y);
					double over = Mesh(x, z) - (y + Network.Lift);
					samples++;
					if (over > -0.02)
					{
						bad++;
						worst = Math.Max(worst, over);
					}
				}
			}
		}
		Console.WriteLine("clip:    " + bad + " of " + samples + " road surface samples under the terrain, worst " + worst.ToString("0.00"));
		return bad * 1000 < samples && worst < 0.5;
	}

	/// <summary>Road segments by 64-unit cell, for finding the roads near a point.</summary>
	private static Dictionary<long, List<(int r, int i)>> grid;

	private static long Key(double x, double z)
	{
		return ((long)Math.Floor(x / 64.0) << 32) | (uint)(int)Math.Floor(z / 64.0);
	}

	private static List<(int r, int i)> Near(double x, double z)
	{
		if (grid == null)
		{
			grid = new Dictionary<long, List<(int, int)>>();
			for (int r = 0; r < Network.Roads.Count; r++)
			{
				Network.Road road = Network.Roads[r];
				if (Network.Unpaved(road.Kind))
				{
					continue;
				}
				for (int i = 0; i + 1 < road.Count; i++)
				{
					double mx = (road.X[i] + road.X[i + 1]) * 0.5, mz = (road.Z[i] + road.Z[i + 1]) * 0.5;
					for (int dz = -1; dz <= 1; dz++)
					{
						for (int dx = -1; dx <= 1; dx++)
						{
							long k = Key(mx + dx * 64.0, mz + dz * 64.0);
							if (!grid.TryGetValue(k, out List<(int, int)> list))
							{
								list = new List<(int, int)>();
								grid[k] = list;
							}
							list.Add((r, i));
						}
					}
				}
			}
		}
		return grid.TryGetValue(Key(x, z), out List<(int r, int i)> found) ? found : new List<(int, int)>();
	}

	/// <summary>The drawn surface's height of road o at (x, z), if (x, z) is well inside its drawn paving (NaN if not).</summary>
	private static double SurfaceOf(int o, double x, double z)
	{
		Network.Road road = Network.Roads[o];
		double best = double.NaN, bestD = double.MaxValue;
		foreach ((int r, int i) in Near(x, z))
		{
			if (r != o)
			{
				continue;
			}
			Lines.Project(road.X, road.Z, i, x, z, out double t, out double d);
			if ((i == 0 && t <= 0.0) || (i == road.Count - 2 && t >= 1.0) || d >= bestD || !Drawn(road, i, out double t0, out double t1) || t < t0 || t > t1)
			{
				continue;
			}
			double s = road.S[i] + (road.S[i + 1] - road.S[i]) * t;
			// Half a unit in: a cut overlaps the other road by a quarter unit on purpose (no ground shows at the seam).
			if (d < Network.HalfAt(road, s) - 0.5)
			{
				bestD = d;
				Network.Point(road, i, t, 0, 0.0, out double _, out double _, out best);
			}
		}
		return best;
	}

	/// <summary>How far through counts as poking: 0.02 is about 6 mm, beyond what can be seen; a mouth is cut 0.05 under the other road.</summary>
	private const double PokeSlack = 0.02;

	private static bool Poke()
	{
		int overlaps = 0, poke = 0;
		double worst = 0;
		for (int r = 0; r < Network.Roads.Count; r++)
		{
			Network.Road road = Network.Roads[r];
			if (Network.Unpaved(road.Kind))
			{
				continue;
			}
			for (int i = 0; i + 1 < road.Count; i++)
			{
				if (!Drawn(road, i, out double t0, out double t1))
				{
					continue;
				}
				foreach (double t in new[] { t0, (t0 + t1) * 0.5, t1 })
				{
					foreach (double f in new[] { -0.9, 0.0, 0.9 })
					{
						Network.Point(road, i, t, f, 0.0, out double x, out double z, out double y);
						HashSet<int> seen = new HashSet<int>();
						foreach ((int o, int _) in Near(x, z))
						{
							Network.Road other = Network.Roads[o];
							// Only against a road this one should be under: bigger, or of its class and built before it.
							if (o == r || !seen.Add(o) || other.Kind > road.Kind || (other.Kind == road.Kind && o > r))
							{
								continue;
							}
							double top = SurfaceOf(o, x, z);
							if (double.IsNaN(top))
							{
								continue;
							}
							overlaps++;
							if (y > top + PokeSlack)
							{
								poke++;
								worst = Math.Max(worst, y - top);
							}
						}
					}
				}
			}
		}
		Console.WriteLine("poke:    " + poke + " of " + overlaps + " points where a road's surface is on another's poke through it, worst " + worst.ToString("0.000"));
		return poke == 0;
	}

	private static bool Float()
	{
		int total = 0, bad = 0;
		foreach (Network.Road r in Network.Roads)
		{
			if (Network.Unpaved(r.Kind))
			{
				continue;
			}
			for (int i = 0; i < r.Count; i += 3)
			{
				if (r.Bridge[i] || !Network.Shown(r, r.S[i]))
				{
					continue;
				}
				total++;
				if (r.Y[i] - Network.BedDepth - (WorldGen.Height(r.X[i], r.Z[i]) - Network.Lift) > 0.5)
				{
					bad++;
				}
			}
		}
		Console.WriteLine("float:   " + bad + " of " + total + " road centres more than 0.5 over the ground");
		return bad * 1000 < total;
	}

	/// <summary>Steepest grade each class is held to (as in Network's classes); a road with cuts at junctions has Network.Leeway.</summary>
	private static readonly double[] Grade = { 0.05, 0.07, 0.09, 0.11, 0.11, 0.14, 0.18, 0.12, HillClimb.Grade };

	private static bool Grades()
	{
		int pieces = 0, bad = 0;
		double worst = 0;
		foreach (Network.Road r in Network.Roads)
		{
			for (int i = 0; i + 1 < r.Count; i++)
			{
				Network.Spans(r, i, spans);
				if (!Network.Unpaved(r.Kind) && spans.Count == 0)
				{
					continue;
				}
				pieces++;
				double g = Math.Abs(r.Y[i + 1] - r.Y[i]) / Math.Max(1e-6, r.S[i + 1] - r.S[i]);
				worst = Math.Max(worst, g / Grade[(int)r.Kind]);
				if (g > (r.Gaps.Count > 0 ? Network.Leeway(r, i) : Grade[(int)r.Kind]) + 0.01)
				{
					bad++;
				}
			}
		}
		Console.WriteLine("grades:  " + bad + " of " + pieces + " drawn road pieces steeper than their class allows, steepest " + worst.ToString("0.00") + " times its limit");
		return bad == 0;
	}

	private static bool Bridges()
	{
		int over = 0, bad = 0, decks = 0;
		double worst = double.MaxValue;
		foreach (Network.Road r in Network.Roads)
		{
			for (int i = 0; i + 1 < r.Count; i++)
			{
				if (r.Bridge[i] && (i == 0 || !r.Bridge[i - 1]))
				{
					decks++;
				}
				bool track = Network.Unpaved(r.Kind);
				double t0 = 0.0, t1 = 1.0;
				if (!track && !Drawn(r, i, out t0, out t1))
				{
					continue;
				}
				for (int k = 0; k <= 8; k++)
				{
					double t = t0 + (t1 - t0) * k / 8.0;
					foreach (double f in track ? new[] { 0.0 } : new[] { -1.0, -0.5, 0.0, 0.5, 1.0 })
					{
						Network.Point(r, i, t, f, 0.0, out double x, out double z, out double y);
						if (!Rivers.Channel(x, z, 2.0, out double water))
						{
							continue;
						}
						over++;
						// A track has no deck: one over water at all is wrong.
						double clear = track ? -1.0 : y - water;
						worst = Math.Min(worst, clear);
						if (clear < 4.0)
						{
							bad++;
						}
					}
				}
			}
		}
		Console.WriteLine("bridges: " + decks + " decks; " + bad + " of " + over + " road points over water less than 4 above it, lowest " + (over > 0 ? worst.ToString("0.0") : "-"));
		return bad == 0;
	}

	private static bool Flooded()
	{
		int samples = 0, bad = 0;
		double worst = 0;
		int[] byKind = new int[9];
		foreach (Network.Road r in Network.Roads)
		{
			bool track = Network.Unpaved(r.Kind);
			for (int i = 0; i + 1 < r.Count; i++)
			{
				Network.Spans(r, i, spans);
				if (track && spans.Count == 0)
				{
					spans.Add((0.0, 1.0));
				}
				foreach ((double t0, double t1) in spans)
				{
					for (int k = 0; k <= 4; k++)
					{
						double t = t0 + (t1 - t0) * k / 4.0;
						foreach (double f in track ? new[] { 0.0 } : new[] { -1.0, -0.5, 0.0, 0.5, 1.0 })
						{
							Network.Point(r, i, t, f, 0.0, out double x, out double z, out double y);
							samples++;
							double water = Rivers.DrawnWater(x, z);
							if (!double.IsNaN(water) && water > y - 0.05)
							{
								bad++;
								byKind[(int)r.Kind]++;
								worst = Math.Max(worst, water - y);
							}
						}
					}
				}
			}
		}
		Console.WriteLine("flooded: " + bad + " of " + samples + " road surface samples under a water surface drawn over them, worst " + worst.ToString("0.00") + " (by class " + string.Join(" ", byKind) + ")");
		return bad == 0;
	}

	private static bool Plants()
	{
		const float tile = 250f;
		const int quads = 50, pixels = 64;
		HashSet<long> tiles = new HashSet<long>();
		foreach (Rivers.River river in Rivers.All)
		{
			for (int i = 0; i < river.Count; i += 40)
			{
				tiles.Add(((long)(int)Math.Floor(river.X[i] / tile) << 32) | (uint)(int)Math.Floor(river.Z[i] / tile));
			}
		}
		int lakes = 0;
		for (int c = 0; c < Rivers.GridN * Rivers.GridN && lakes < 60; c += 97)
		{
			double x = Rivers.GridMin + (c % Rivers.GridN + 0.5) * Rivers.GridSize, z = Rivers.GridMin + (c / Rivers.GridN + 0.5) * Rivers.GridSize;
			if (!double.IsNaN(Rivers.Surface[c]) && double.IsNaN(Rivers.SurfaceAt(x + Rivers.GridSize, z)) && Math.Abs(x) < 15000 && Math.Abs(z) < 15000)
			{
				tiles.Add(((long)(int)Math.Floor((x + Rivers.GridSize) / tile) << 32) | (uint)(int)Math.Floor(z / tile));
				lakes++;
			}
		}
		foreach (Parks.Park park in Parks.All)
		{
			if (park.HasPond)
			{
				tiles.Add(((long)(int)Math.Floor(park.PondX / tile) << 32) | (uint)(int)Math.Floor(park.PondZ / tile));
			}
		}
		List<long> order = new List<long>(tiles);
		order.Sort();
		int plants = 0, bad = 0;
		double worst = 0;
		int side = quads + 3;
		double step = tile / quads;
		float pixel = tile / pixels;
		foreach (long key in order)
		{
			int tx = (int)(key >> 32), tz = (int)(uint)key;
			double x0 = tx * (double)tile - step, z0 = tz * (double)tile - step;
			float[] h = new float[side * side];
			for (int j = 0; j < side; j++)
			{
				for (int i = 0; i < side; i++)
				{
					h[j * side + i] = (float)WorldGen.Height(x0 + i * step, z0 + j * step);
				}
			}
			// The covers as the streamer works them out (Streamer.Colours).
			Cover[] covers = new Cover[pixels * pixels];
			for (int pj = 0; pj < pixels; pj++)
			{
				for (int pi = 0; pi < pixels; pi++)
				{
					float lx = (pi + 0.5f) * pixel, lz = (pj + 0.5f) * pixel;
					float gx = lx / (float)step + 1f, gz = lz / (float)step + 1f;
					int i = Math.Min(side - 2, (int)gx), j = Math.Min(side - 2, (int)gz);
					float fx = gx - i, fz = gz - j;
					float a = h[j * side + i], b = h[j * side + i + 1], c = h[(j + 1) * side + i], d = h[(j + 1) * side + i + 1];
					float height = (a + (b - a) * fx) + ((c + (d - c) * fx) - (a + (b - a) * fx)) * fz;
					float sx = ((b - a) + (d - c)) * 0.5f / (float)step, sz = ((c - a) + (d - b)) * 0.5f / (float)step;
					covers[pj * pixels + pi] = LandUse.Classify(tx * (double)tile + lx, tz * (double)tile + lz, height - Network.Lift, Math.Sqrt(sx * sx + sz * sz), out int _);
				}
			}
			Foliage.Scatter(tx, tz, tile, pixels, covers, Network.Clearance(tx, tz, tile, pixels), h, side, (float)step, new List<(double x, double z, double r)>(), out Plant[] trees, out Plant[] grass);
			TerrainGrid ground = new TerrainGrid(x0, z0, side, h);
			foreach (Plant[] list in new[] { trees, grass })
			{
				foreach (Plant p in list)
				{
					double x = tx * (double)tile + p.Pos.x, z = tz * (double)tile + p.Pos.z;
					double water = Rivers.DrawnWater(x, z);
					plants++;
					if (!double.IsNaN(water) && water + Network.Lift > ground.At(x, z) - 0.05)
					{
						bad++;
						worst = Math.Max(worst, water + Network.Lift - ground.At(x, z));
					}
				}
			}
		}
		Console.WriteLine("plants:  " + order.Count + " tiles by water; " + bad + " of " + plants + " trees and grass tufts standing in water, deepest " + worst.ToString("0.00"));
		return bad == 0;
	}

	private static bool ParkChecks()
	{
		int ponds = 0, edges = 0, high = 0, shallow = 0;
		foreach (Parks.Park park in Parks.All)
		{
			if (!park.HasPond)
			{
				continue;
			}
			ponds++;
			double level = park.PondLevel + Network.Lift;
			for (int k = 0; k < 64; k++)
			{
				double a = k * DetMath.Pi / 32.0, r = park.PondR + Parks.PondDrawn;
				edges++;
				if (Mesh(park.PondX + DetMath.Cos(a) * r, park.PondZ + DetMath.Sin(a) * r) < level + 0.02)
				{
					high++;
				}
			}
			if (Mesh(park.PondX, park.PondZ) > level - 1.0)
			{
				shallow++;
			}
		}
		// Buildings on the parks' tiles: none standing on a lawn (benches are small, 3.5 across).
		HashSet<long> tiles = new HashSet<long>();
		int paths = 0;
		foreach (Parks.Park park in Parks.All)
		{
			for (double z = park.Oz; z <= park.Oz + park.H * Parks.CellSize; z += 125.0)
			{
				for (double x = park.Ox; x <= park.Ox + park.W * Parks.CellSize; x += 125.0)
				{
					tiles.Add(((long)(int)Math.Floor(x / 250.0) << 32) | (uint)(int)Math.Floor(z / 250.0));
				}
			}
			bool any = false;
			foreach (Network.Road r in Network.Roads)
			{
				any |= r.Name != null && r.Name.StartsWith(park.Name + " ", StringComparison.Ordinal);
			}
			paths += any ? 1 : 0;
		}
		List<long> order = new List<long>(tiles);
		order.Sort();
		int buildings = 0, onLawn = 0;
		foreach (long key in order)
		{
			Buildings.Data d = Buildings.Build((int)(key >> 32), (int)(uint)key, 250f);
			foreach ((double x, double z, double r) in d.Footprints)
			{
				if (r <= 4.0)
				{
					continue;
				}
				buildings++;
				if (Parks.At(x, z))
				{
					onLawn++;
				}
			}
		}
		int benchesBad = 0;
		foreach (Parks.Bench b in Parks.Benches)
		{
			double g = Mesh(b.X, b.Z) - Network.Lift;
			double w = Rivers.DrawnWater(b.X, b.Z);
			if (!Network.Claims(b.X, b.Z, -1, 0.0, 1.0) || (!double.IsNaN(w) && w > g - 0.5))
			{
				benchesBad++;
			}
		}
		Console.WriteLine("parks:   " + Parks.All.Count + " parks, " + paths + " with paths; " + ponds + " ponds, " + high + " of " + edges + " water edges over the ground, " + shallow + " with no water in the middle; " + onLawn + " of " + buildings + " buildings on their tiles on a lawn; " + benchesBad + " of " + Parks.Benches.Count + " benches on a road or in water");
		return Parks.All.Count > 0 && paths == Parks.All.Count && high == 0 && shallow == 0 && onLawn == 0 && benchesBad == 0;
	}

	private static bool Water()
	{
		int edges = 0, bad = 0, natural = 0, softened = 0, roads = 0, mesh = 0, meets = 0, beside = 0;
		double worst = 0;
		foreach (Rivers.River river in Rivers.All)
		{
			for (int i = 0; i < river.Count; i += 2)
			{
				int a = Math.Max(0, i - 1), b = Math.Min(river.Count - 1, i + 1);
				double tx = river.X[b] - river.X[a], tz = river.Z[b] - river.Z[a], len = Math.Sqrt(tx * tx + tz * tz);
				if (len <= 0)
				{
					continue;
				}
				foreach (int side in new[] { -1, 1 })
				{
					double off = Rivers.Edge(river.Half[i]) * side;
					double x = river.X[i] - tz / len * off, z = river.Z[i] + tx / len * off;
					if (!double.IsNaN(Rivers.SurfaceAt(x, z)))
					{
						continue;
					}
					edges++;
					double w = river.W[i] + Network.Lift;
					double under = w - Mesh(x, z);
					if (under <= 0.02)
					{
						continue;
					}
					// Where two rivers meet, one's surface runs into the other's water.
					double other = Rivers.OtherWater(x, z, river);
					if (!double.IsNaN(other) && other + Network.Lift > w - 0.5)
					{
						meets++;
						continue;
					}
					// Beside a lower river's water (its last stretch run in alongside after the meanders): nothing can hold the water there.
					if (!double.IsNaN(other))
					{
						beside++;
						continue;
					}
					bad++;
					worst = Math.Max(worst, under);
					if (WorldGen.Land(x, z) + Network.Lift < w)
					{
						natural++;
					}
					else if (WorldGen.Base(x, z) + Network.Lift < w)
					{
						softened++;
					}
					else if (WorldGen.Height(x, z) < w)
					{
						roads++;
					}
					else
					{
						mesh++;
					}
				}
			}
		}
		Console.WriteLine("water:   " + bad + " of " + edges + " river surface edges over the drawn ground (" + meets + " more run into another river's water, " + beside + " beside a lower river's), worst " + worst.ToString("0.00") + " (land under the water " + natural + ", a place's ground " + softened + ", a road's cut " + roads + ", the mesh's triangles " + mesh + ")");
		// The few left are where nothing can hold the water: a mountain stretch falling steeply round a tight bend, or beside a lower
		// river's channel.
		return (bad + beside) * 100 < edges;
	}

	private static bool Verges()
	{
		int sections = 0, bad = 0;
		double worst = 0;
		RoadMesh.Mesher m = new RoadMesh.Mesher(0, 0, 1);
		for (int r = 0; r < Network.Roads.Count; r++)
		{
			Network.Road road = Network.Roads[r];
			if (Network.Unpaved(road.Kind))
			{
				continue;
			}
			for (int i = 0; i + 1 < road.Count; i += 2)
			{
				if (road.Bridge[i] || road.Bridge[i + 1] || !Drawn(road, i, out double t0, out double _))
				{
					continue;
				}
				for (int side = -1; side <= 1; side += 2)
				{
					if (RoadMesh.Sidewalk(road, i, side))
					{
						continue;
					}
					RoadMesh.Section(m, road, i, t0, side, Terrain, out Vector3 inner, out Vector3 middle, out Vector3 outer, out double wx, out double wz);
					if (Network.OnOtherPaving(wx, wz, r))
					{
						continue;
					}
					sections++;
					Vector3 edge = m.At(road, i, t0, side, 0.0);
					double step = Math.Max(Math.Abs(inner.y - edge.y), Math.Abs(outer.y - Mesh(wx, wz)));
					worst = Math.Max(worst, step);
					if (step > 0.1)
					{
						bad++;
					}
				}
			}
		}
		Console.WriteLine("verges:  " + bad + " of " + sections + " verge sections with a step over 0.1 at an edge, worst " + worst.ToString("0.00"));
		return bad == 0;
	}

	private static bool Tracks()
	{
		int total = 0, bad = 0;
		double worst = 0;
		foreach (Network.Road r in Network.Roads)
		{
			if (!Network.Unpaved(r.Kind))
			{
				continue;
			}
			// Away from the road it leaves and any it meets, where the bigger road's bed is the ground.
			for (int i = 3; i + 3 < r.Count; i++)
			{
				if (OnPaved(r.X[i], r.Z[i]))
				{
					continue;
				}
				total++;
				double off = Math.Abs(WorldGen.Height(r.X[i], r.Z[i]) - Network.Lift - (r.Y[i] - Network.TrackDepth));
				worst = Math.Max(worst, off);
				if (off > 0.15)
				{
					bad++;
				}
			}
		}
		Console.WriteLine("tracks:  " + bad + " of " + total + " track and path points whose ground is more than 0.15 off the bed, worst " + worst.ToString("0.00"));
		return bad * 100 < total;
	}

	/// <summary>Whether (x, z) is near a paved road (within its half width, shoulder and widest bank's start).</summary>
	private static bool OnPaved(double x, double z)
	{
		foreach ((int o, int i) in Near(x, z))
		{
			Network.Road road = Network.Roads[o];
			Lines.Project(road.X, road.Z, i, x, z, out double _, out double d);
			if (d < road.Half + Network.Shoulder + 24.0)
			{
				return true;
			}
		}
		return false;
	}

	private static bool Bases()
	{
		Places.Place town = Places.All[0];
		int tx0 = (int)Math.Floor(town.X / 250), tz0 = (int)Math.Floor(town.Z / 250), n = 0;
		double sunk = 0, raised = 0;
		for (int tz = tz0 - 4; tz <= tz0 + 4; tz++)
		{
			for (int tx = tx0 - 4; tx <= tx0 + 4; tx++)
			{
				Buildings.Data d = Buildings.Build(tx, tz, 250f);
				if (d.Mesh == null)
				{
					continue;
				}
				foreach ((double x, double z, double r) in d.Footprints)
				{
					float lo = float.MaxValue;
					foreach (Vector3 v in d.Mesh.Vertices)
					{
						double wx = v.x + tx * 250.0, wz = v.z + tz * 250.0;
						if ((wx - x) * (wx - x) + (wz - z) * (wz - z) < r * r * 1.05)
						{
							lo = Math.Min(lo, v.y);
						}
					}
					if (lo == float.MaxValue)
					{
						continue;
					}
					double g = WorldGen.Height(x, z);
					n++;
					sunk = Math.Max(sunk, g - lo);
					raised = Math.Max(raised, lo - g);
				}
			}
		}
		Console.WriteLine("bases:   " + n + " buildings round " + town.Name + ", base at most " + sunk.ToString("0.0") + " under the ground, " + raised.ToString("0.0") + " above");
		return sunk < 7.0 && raised < 0.1;
	}

	private static bool Faces()
	{
		int down = 0, most = 0;
		for (int tz = -64; tz < 64; tz++)
		{
			for (int tx = -64; tx < 64; tx++)
			{
				RoadMeshData road = RoadMesh.Build(tx, tz, 250f, Terrain, out RoadMeshData verges, out RoadMeshData sidewalks, out RoadMeshData tracks);
				RoadMeshData circuit = CircuitMesh.Build(tx, tz, 250f, out RoadMeshData gravel);
				foreach (RoadMeshData d in new[] { road, verges, sidewalks, tracks, WaterMesh.Build(tx, tz, 250f), circuit, gravel })
				{
					if (d == null)
					{
						continue;
					}
					most = Math.Max(most, d.Vertices.Length);
					foreach (int[] t in d.Triangles)
					{
						for (int k = 0; k < t.Length; k += 3)
						{
							Vector3 nrm = Vector3.Cross(d.Vertices[t[k + 1]] - d.Vertices[t[k]], d.Vertices[t[k + 2]] - d.Vertices[t[k]]);
							if (nrm.y < -0.5f * nrm.magnitude)
							{
								down++;
							}
						}
					}
				}
			}
		}
		Console.WriteLine("faces:   " + down + " upside-down flat faces in road, verge, sidewalk, track, water and circuit meshes; most vertices in one mesh " + most);
		return down == 0 && most < 65000;
	}

	private static bool Lots()
	{
		int samples = 0, under = 0, over = 0, front = 0, roads = 0, retail = 0;
		double worstUnder = 0.0, worstOver = 0.0, worstFront = 0.0;
		foreach (Town.Lot lot in Town.Lots)
		{
			retail += lot.Kind == LotKind.Retail ? 1 : 0;
			for (double v = -5.5; v < lot.D; v += 2.0)
			{
				for (double u = -lot.W * 0.5 + 0.5; u < lot.W * 0.5; u += 2.0)
				{
					// The surface reaches forward to the verge's foot.
					if (v < Town.Foot(lot, u) + 0.5)
					{
						continue;
					}
					Town.World(lot, u, v, out double x, out double z);
					double gap = Town.Surface(lot, u) + Network.Lift - Mesh(x, z);
					samples++;
					if (gap < 0.02)
					{
						under++;
						worstUnder = Math.Max(worstUnder, -gap);
					}
					else if (gap > 0.3)
					{
						over++;
						worstOver = Math.Max(worstOver, gap);
					}
					// The rally service area's own track leaves from its back, as Town.Fit allows.
					int also = lot.Kind == LotKind.Service && Network.RallyAccess != null ? Network.Roads.IndexOf(Network.RallyAccess) : -1;
					if (v > 0.0 && !Network.Claims(x, z, lot.Road, Network.Verge + v, 2.0, also))
					{
						roads++;
					}
				}
			}
			for (double u = -lot.W * 0.5 + 0.5; u < lot.W * 0.5; u += 2.0)
			{
				// The verge comes down to the ground's height at its foot (not the drawn triangles under it, which it hides).
				Town.World(lot, u, Town.Foot(lot, u), out double x, out double z);
				double step = Math.Abs(Town.Surface(lot, u) + Network.Lift - WorldGen.Height(x, z));
				worstFront = Math.Max(worstFront, step);
				if (step > 0.25)
				{
					front++;
				}
			}
		}
		Console.WriteLine("lots:    " + Town.Lots.Count + " lots (" + retail + " retail park); of " + samples + " surface points " + under + " with the ground through them (worst " + worstUnder.ToString("0.00") + "), " + over + " floating (worst " + worstOver.ToString("0.00") + "), " + roads + " on or by another road; " + front + " front points off the verge's foot (worst " + worstFront.ToString("0.00") + ")");
		return retail == 1 && Town.Lots.Count > 10 && under == 0 && over == 0 && roads == 0 && front == 0;
	}

	private static bool Ribbons()
	{
		int samples = 0, bad = 0;
		double worst = 0.0;
		foreach (Network.Road r in Network.Roads)
		{
			if (!Network.Unpaved(r.Kind))
			{
				continue;
			}
			for (int i = 0; i + 1 < r.Count; i++)
			{
				for (int k = 0; k < 4; k++)
				{
					double t = (k + 0.5) / 4.0;
					foreach (double f in new[] { -1.0, -0.5, 0.0, 0.5, 1.0 })
					{
						Network.Point(r, i, t, f, 0.0, out double x, out double z, out double y);
						if (OnPaved(x, z))
						{
							continue;
						}
						samples++;
						double over = Mesh(x, z) - (y + Network.Lift);
						if (over > -0.02)
						{
							bad++;
							worst = Math.Max(worst, over);
						}
					}
				}
			}
		}
		Console.WriteLine("ribbons: " + bad + " of " + samples + " track and path surface samples with the ground through them, worst " + worst.ToString("0.00"));
		return bad * 1000 < samples && worst < 0.1;
	}

	private static bool Sidewalks()
	{
		int sections = 0, floating = 0, kerbs = 0;
		RoadMesh.Mesher m = new RoadMesh.Mesher(0, 0, 1);
		for (int r = 0; r < Network.Roads.Count; r++)
		{
			Network.Road road = Network.Roads[r];
			if (Network.Unpaved(road.Kind))
			{
				continue;
			}
			for (int i = 0; i + 1 < road.Count; i++)
			{
				if (road.Bridge[i] || road.Bridge[i + 1] || !Drawn(road, i, out double t0, out double _))
				{
					continue;
				}
				for (int side = -1; side <= 1; side += 2)
				{
					if (!RoadMesh.Sidewalk(road, i, side))
					{
						continue;
					}
					RoadMesh.PavementSection(m, road, i, t0, side, Terrain, out Vector3 inner, out Vector3 back, out Vector3 foot, out double wx, out double wz);
					if (Network.OnOtherPaving(wx, wz, r))
					{
						continue;
					}
					sections++;
					if (back.y < Mesh(wx, wz))
					{
						floating++;
					}
					Network.Point(road, i, t0, side, Network.Verge + 0.8, out double fx, out double fz, out double _);
					if (foot.y > Mesh(fx, fz) + 0.001 && foot.y < back.y)
					{
						kerbs++;
					}
				}
			}
		}
		Console.WriteLine("sidewalks: " + sections + " sections; " + floating + " with the ground over their back, " + kerbs + " kerbs not reaching under the ground");
		return sections > 100 && floating == 0 && kerbs == 0;
	}

	private static bool CircuitChecks()
	{
		if (!Circuit.Exists)
		{
			Console.WriteLine("circuit: none");
			return false;
		}
		int n = Circuit.Count;
		// Closed: the last sample a step from the first; never near itself.
		double gap = Math.Sqrt((Circuit.X[0] - Circuit.X[n - 1]) * (Circuit.X[0] - Circuit.X[n - 1]) + (Circuit.Z[0] - Circuit.Z[n - 1]) * (Circuit.Z[0] - Circuit.Z[n - 1]));
		double nearest = double.MaxValue;
		for (int i = 0; i < n; i += 2)
		{
			for (int j = i + 2; j < n; j += 2)
			{
				double along = Math.Min(Circuit.S[j] - Circuit.S[i], Circuit.Length - (Circuit.S[j] - Circuit.S[i]));
				if (along < 700.0)
				{
					continue;
				}
				double dx = Circuit.X[j] - Circuit.X[i], dz = Circuit.Z[j] - Circuit.Z[i];
				nearest = Math.Min(nearest, Math.Sqrt(dx * dx + dz * dz));
			}
		}
		double tilt = Math.Sqrt(Circuit.Px * Circuit.Px + Circuit.Pz * Circuit.Pz);

		// Every surface vertex over the drawn ground (the skirts, which run down under it at the edges, aside), and the track across at
		// every piece's middle.
		int vertices = 0, under = 0;
		double worst = 0.0;
		double minX = double.MaxValue, maxX = double.MinValue, minZ = double.MaxValue, maxZ = double.MinValue;
		for (int i = 0; i < n; i++)
		{
			minX = Math.Min(minX, Circuit.X[i]);
			maxX = Math.Max(maxX, Circuit.X[i]);
			minZ = Math.Min(minZ, Circuit.Z[i]);
			maxZ = Math.Max(maxZ, Circuit.Z[i]);
		}
		for (int tz = (int)Math.Floor((minZ - 400.0) / 250.0); tz <= (int)Math.Floor((maxZ + 400.0) / 250.0); tz++)
		{
			for (int tx = (int)Math.Floor((minX - 400.0) / 250.0); tx <= (int)Math.Floor((maxX + 400.0) / 250.0); tx++)
			{
				RoadMeshData track = CircuitMesh.Build(tx, tz, 250f, out RoadMeshData gravel);
				foreach (RoadMeshData d in new[] { track, gravel })
				{
					if (d == null)
					{
						continue;
					}
					foreach (Vector3 v in d.Vertices)
					{
						double x = v.x + tx * 250.0, z = v.z + tz * 250.0;
						double over = v.y - (Circuit.PlaneAt(x, z) + Network.Lift);
						if (over < 0.0)
						{
							continue;
						}
						vertices++;
						double g = v.y - Mesh(x, z);
						if (g < 0.02)
						{
							under++;
							worst = Math.Max(worst, -g);
						}
					}
				}
			}
		}
		for (int i = 0; i < n; i++)
		{
			int q = (i + 1) % n;
			for (double d = -Circuit.Half; d <= Circuit.Half; d += 5.0)
			{
				double x = (Circuit.X[i] + Circuit.X[q]) * 0.5 + (Circuit.Rx[i] + Circuit.Rx[q]) * 0.5 * d, z = (Circuit.Z[i] + Circuit.Z[q]) * 0.5 + (Circuit.Rz[i] + Circuit.Rz[q]) * 0.5 * d;
				vertices++;
				double g = Circuit.PlaneAt(x, z) + Network.Lift + CircuitMesh.TrackTop - Mesh(x, z);
				if (g < 0.02)
				{
					under++;
					worst = Math.Max(worst, -g);
				}
			}
		}

		// Nothing in the fence but the circuit's own road (which starts at the paddock's gate, inside it).
		int roads = 0, places = 0, lots = 0, parks = 0;
		foreach (Network.Road road in Network.Roads)
		{
			if (road == Network.CircuitRoad)
			{
				continue;
			}
			for (int i = 0; i < road.Count; i++)
			{
				if (Circuit.Fenced(road.X[i], road.Z[i], road.Half))
				{
					roads++;
					break;
				}
			}
		}
		foreach (Places.Place p in Places.All)
		{
			places += Circuit.Fenced(p.X, p.Z, p.Radius) ? 1 : 0;
		}
		foreach (Town.Lot lot in Town.Lots)
		{
			lots += Circuit.Fenced(lot.X, lot.Z, Math.Max(lot.W, lot.D)) ? 1 : 0;
		}
		foreach (Parks.Park park in Parks.All)
		{
			parks += Circuit.Fenced(park.X, park.Z, 100.0) ? 1 : 0;
		}

		// The barrier and the stands keep clear of every part of the lap.
		double barrier = double.MaxValue, stands = double.MaxValue;
		for (int i = 0; i < n; i++)
		{
			if (!Circuit.BarrierAt(Circuit.S[i]))
			{
				continue;
			}
			double x = Circuit.X[i] + Circuit.Rx[i] * Circuit.Barrier, z = Circuit.Z[i] + Circuit.Rz[i] * Circuit.Barrier;
			if (Circuit.Nearest(x, z, out int _, out double _, out double dist, out int _))
			{
				barrier = Math.Min(barrier, dist);
			}
		}
		foreach (Circuit.Stand st in Circuit.Stands)
		{
			// The main stand is in the infield (no barrier there): it keeps 30 back from the track's edge. The others are beyond the barrier.
			double need = st.Name == "Main stand" ? Circuit.Half + 30.0 : Circuit.Barrier + 5.0;
			for (int a = -1; a <= 1; a++)
			{
				for (int b = -1; b <= 1; b++)
				{
					double x = st.X - st.Fz * a * st.Length * 0.5 + st.Fx * b * st.Depth * 0.5, z = st.Z + st.Fx * a * st.Length * 0.5 + st.Fz * b * st.Depth * 0.5;
					if (Circuit.Nearest(x, z, out int _, out double _, out double dist, out int _))
					{
						stands = Math.Min(stands, dist - need);
					}
				}
			}
		}
		bool ok = gap < Circuit.Step * 1.5 && nearest >= 260.0 && tilt <= 0.0201 && under == 0 && roads == 0 && places == 0 && lots == 0 && parks == 0
			&& barrier >= Circuit.GravelOut + 5.0 && stands >= 0.0 && Circuit.MinLeftRadius >= 54.0 && Network.CircuitRoad != null;
		Console.WriteLine("circuit: lap " + Circuit.Length.ToString("0") + " long, " + Circuit.Corners + " corners, closing gap " + gap.ToString("0.0") + ", nearest itself " + nearest.ToString("0") + ", hairpin radius " + Circuit.MinLeftRadius.ToString("0") + ", ground tilted " + (tilt * 100.0).ToString("0.00") + "%; " + under + " of " + vertices + " surface points with the ground through them (worst " + worst.ToString("0.00") + "); in the fence: " + roads + " roads, " + places + " places, " + lots + " lots, " + parks + " parks; barrier at least " + barrier.ToString("0") + ", stands " + stands.ToString("0") + " beyond the room they need; road " + (Network.CircuitRoad != null ? "joined" : "MISSING"));
		return ok;
	}

	private static bool RallyChecks()
	{
		Network.Road stage = Rally.Road;
		if (!Rally.Exists || stage == null)
		{
			Console.WriteLine("rally:   none");
			return false;
		}
		int n = stage.Count;
		// Closed: the two ends the same point at the same height, their edges the same.
		double gap = Math.Sqrt((stage.X[0] - stage.X[n - 1]) * (stage.X[0] - stage.X[n - 1]) + (stage.Z[0] - stage.Z[n - 1]) * (stage.Z[0] - stage.Z[n - 1])) + Math.Abs(stage.Y[0] - stage.Y[n - 1]);
		foreach (int side in new[] { -1, 1 })
		{
			Network.Point(stage, 0, 0.0, side, 0.0, out double ax, out double az, out double ay);
			Network.Point(stage, n - 2, 1.0, side, 0.0, out double bx, out double bz, out double by);
			gap = Math.Max(gap, Math.Abs(ax - bx) + Math.Abs(az - bz) + Math.Abs(ay - by));
		}
		double nearest = double.MaxValue, hairpin = double.MaxValue;
		for (int i = 0; i < Rally.Count; i += 2)
		{
			for (int j = i + 2; j < Rally.Count; j += 2)
			{
				double along = Math.Min(Rally.S[j] - Rally.S[i], Rally.Length - (Rally.S[j] - Rally.S[i]));
				double d = Math.Sqrt((Rally.X[j] - Rally.X[i]) * (Rally.X[j] - Rally.X[i]) + (Rally.Z[j] - Rally.Z[i]) * (Rally.Z[j] - Rally.Z[i]));
				if (along >= 500.0)
				{
					nearest = Math.Min(nearest, d);
				}
				else if (along >= 100.0)
				{
					hairpin = Math.Min(hairpin, d);
				}
			}
		}

		// Nothing in the fence but the stage, its service area and its track (which leaves from the service area's back).
		int roads = 0, places = 0, lots = 0, parks = 0, service = 0;
		foreach (Network.Road road in Network.Roads)
		{
			if (road == stage || road == Network.RallyAccess)
			{
				continue;
			}
			for (int i = 0; i < road.Count; i++)
			{
				if (Rally.Fenced(road.X[i], road.Z[i], road.Half))
				{
					roads++;
					break;
				}
			}
		}
		foreach (Places.Place p in Places.All)
		{
			places += Rally.Fenced(p.X, p.Z, p.Radius) ? 1 : 0;
		}
		foreach (Town.Lot lot in Town.Lots)
		{
			if (lot.Kind == LotKind.Service)
			{
				service++;
				continue;
			}
			lots += Rally.Fenced(lot.X, lot.Z, Math.Max(lot.W, lot.D)) ? 1 : 0;
		}
		foreach (Parks.Park park in Parks.All)
		{
			parks += Rally.Fenced(park.X, park.Z, 100.0) ? 1 : 0;
		}

		// The bales: every corner of every bale clear of the stage's soft edges, its base under the drawn ground there.
		int bales = 0, onStage = 0, floating = 0;
		foreach (Rally.Bales row in Rally.Corners)
		{
			for (double s = row.S0; s <= row.S1 + 0.01; s += Rally.BaleLength)
			{
				Rally.Bale(row, s, out double x, out double z, out double dx, out double dz);
				bales++;
				double low = double.MaxValue;
				bool clear = true;
				for (int a = -1; a <= 1; a += 2)
				{
					for (int c = -1; c <= 1; c += 2)
					{
						double px = x + dx * Rally.BaleLength * 0.5 * a - dz * Rally.BaleDepth * 0.5 * c, pz = z + dz * Rally.BaleLength * 0.5 * a + dx * Rally.BaleDepth * 0.5 * c;
						clear &= Rally.Distance(px, pz) >= Rally.Half + Network.EdgeOf(RoadKind.Rally);
						low = Math.Min(low, WorldGen.Height(px, pz));
					}
				}
				onStage += clear ? 0 : 1;
				floating += WorldGen.Height(x, z) - low > 3.0 ? 1 : 0;
			}
		}
		double steepest = 0.0;
		for (int i = 1; i < n; i++)
		{
			steepest = Math.Max(steepest, Math.Abs(stage.Y[i] - stage.Y[i - 1]) / Math.Max(1e-6, stage.S[i] - stage.S[i - 1]));
		}
		bool ok = gap < 1e-6 && nearest >= 100.0 && hairpin >= 40.0 && Rally.TightestRadius >= 23.9 && roads == 0 && places == 0 && lots == 0 && parks == 0
			&& service == 1 && Network.RallyAccess != null && onStage == 0 && floating == 0 && steepest <= 0.1201;
		Console.WriteLine("rally:   stage " + Rally.Length.ToString("0") + " long, " + Rally.CornerCount + " corners, tightest radius " + Rally.TightestRadius.ToString("0") + ", steepest " + (steepest * 100.0).ToString("0.0") + "%, ends " + gap.ToString("0.000") + " apart, nearest itself " + nearest.ToString("0") + " (a hairpin's legs " + hairpin.ToString("0") + "); in the fence: " + roads + " roads, " + places + " places, " + lots + " lots, " + parks + " parks; service area " + service + ", track " + (Network.RallyAccess != null ? "joined" : "MISSING") + "; " + bales + " bales, " + onStage + " on the stage's edge, " + floating + " on ground falling over 3 across");
		return ok;
	}

	private static bool ClimbChecks()
	{
		Network.Road climb = HillClimb.Road, lane = Network.ClimbAccess;
		if (!HillClimb.Exists || climb == null)
		{
			Console.WriteLine("climb:   none");
			return false;
		}
		int n = climb.Count;
		// Never near itself: two stretches far apart along it keep their distance, a hairpin's legs theirs.
		double nearest = double.MaxValue, hairpin = double.MaxValue;
		for (int i = 0; i < n; i += 2)
		{
			for (int j = i + 2; j < n; j += 2)
			{
				double along = climb.S[j] - climb.S[i];
				double d = Math.Sqrt((climb.X[j] - climb.X[i]) * (climb.X[j] - climb.X[i]) + (climb.Z[j] - climb.Z[i]) * (climb.Z[j] - climb.Z[i]));
				if (along >= HillClimb.SeparationAlong)
				{
					nearest = Math.Min(nearest, d);
				}
				else if (along >= 120.0)
				{
					hairpin = Math.Min(hairpin, d);
				}
			}
		}
		// Its lane meets its foot end to end: the same point, height and edges.
		double join = double.MaxValue;
		if (lane != null)
		{
			join = Math.Abs(lane.X[0] - climb.X[0]) + Math.Abs(lane.Z[0] - climb.Z[0]) + Math.Abs(lane.Y[0] + Network.Top(RoadKind.Lane) - climb.Y[0] - Network.Top(RoadKind.Climb));
			foreach (int side in new[] { -1, 1 })
			{
				Network.Point(climb, 0, 0.0, side, 0.0, out double ax, out double az, out double ay);
				Network.Point(lane, 0, 0.0, -side, 0.0, out double bx, out double bz, out double by);
				join = Math.Max(join, Math.Abs(ax - bx) + Math.Abs(az - bz) + Math.Abs(ay - by));
			}
		}

		// Nothing in the fence but the road, its lane, its paddock and turning area.
		int roads = 0, places = 0, lots = 0, parks = 0, paddock = 0, turning = 0;
		foreach (Network.Road road in Network.Roads)
		{
			if (road == climb || road == lane)
			{
				continue;
			}
			for (int i = 0; i < road.Count; i++)
			{
				if (HillClimb.Fenced(road.X[i], road.Z[i], road.Half + HillClimb.Fence))
				{
					roads++;
					break;
				}
			}
		}
		foreach (Places.Place p in Places.All)
		{
			places += HillClimb.Fenced(p.X, p.Z, p.Radius) ? 1 : 0;
		}
		foreach (Town.Lot lot in Town.Lots)
		{
			paddock += lot.Kind == LotKind.Paddock ? 1 : 0;
			turning += lot.Kind == LotKind.Turning ? 1 : 0;
			if (lot.Kind != LotKind.Paddock && lot.Kind != LotKind.Turning)
			{
				lots += HillClimb.Fenced(lot.X, lot.Z, Math.Max(lot.W, lot.D)) ? 1 : 0;
			}
		}
		foreach (Parks.Park park in Parks.All)
		{
			parks += HillClimb.Fenced(park.X, park.Z, 100.0) ? 1 : 0;
		}
		// The arches' posts off the road and its verge.
		int posts = 0;
		foreach (double s in new[] { HillClimb.StartS, HillClimb.FinishS })
		{
			for (int side = -1; side <= 1; side += 2)
			{
				HillClimb.Post(s, side, out double px, out double pz);
				posts += HillClimb.Distance(px, pz) < HillClimb.Half + Network.Verge + 1.0 ? 1 : 0;
			}
		}
		double steepest = 0.0, low = double.MaxValue, high = double.MinValue;
		for (int i = 1; i < n; i++)
		{
			steepest = Math.Max(steepest, Math.Abs(climb.Y[i] - climb.Y[i - 1]) / Math.Max(1e-6, climb.S[i] - climb.S[i - 1]));
			low = Math.Min(low, climb.Y[i]);
			high = Math.Max(high, climb.Y[i]);
		}
		bool ok = nearest >= HillClimb.Separation && hairpin >= HillClimb.HairpinApart && HillClimb.TightestRadius >= HillClimb.MinRadius - 0.1 && join < 1e-6
			&& roads == 0 && places == 0 && lots == 0 && parks == 0 && paddock == 1 && turning == 1 && posts == 0 && steepest <= HillClimb.Grade + 0.0001;
		Console.WriteLine("climb:   road " + HillClimb.Length.ToString("0") + " long, " + ((high - low)).ToString("0") + " from foot to top, " + HillClimb.Hairpins + " hairpins, tightest radius " + HillClimb.TightestRadius.ToString("0") + ", steepest " + (steepest * 100.0).ToString("0.0") + "%, nearest itself " + nearest.ToString("0") + " (a hairpin's legs " + hairpin.ToString("0") + "); lane " + (lane == null ? "MISSING" : "joined " + join.ToString("0.000") + " off") + "; in the fence: " + roads + " roads, " + places + " places, " + lots + " lots, " + parks + " parks; paddock " + paddock + ", turning area " + turning + "; " + posts + " arch posts on the verge");
		return ok;
	}

	private static bool SignChecks()
	{
		int onRoad = 0, wet = 0, crowded = 0, undrawn = 0;
		foreach (Signs.Sign sign in Signs.All)
		{
			// A name sign stands behind the verge; a finger may reach over a verge (it is over a car's roof) but never over a road.
			double margin = sign.Finger ? 0.0 : Network.Verge;
			foreach (Signs.Post p in sign.Posts)
			{
				onRoad += Network.Claims(p.X, p.Z, -1, 0.0, Network.Verge) ? 0 : 1;
				wet += Rivers.Water(p.X, p.Z, out double level) && WorldGen.Height(p.X, p.Z) - Network.Lift < level ? 1 : 0;
			}
			foreach (Signs.Board b in sign.Boards)
			{
				double ux = -b.Nz, uz = b.Nx;
				for (double a = -0.5; a <= 0.501; a += 0.1)
				{
					onRoad += Network.Claims(b.X + ux * a * b.W, b.Z + uz * a * b.W, -1, 0.0, margin) ? 0 : 1;
				}
			}
			int tx = (int)Math.Floor(sign.X / 250.0), tz = (int)Math.Floor(sign.Z / 250.0);
			Buildings.Data d = Buildings.Build(tx, tz, 250f);
			bool own = false;
			foreach ((double x, double z, double r) in d.Footprints)
			{
				if (x == sign.X && z == sign.Z && r == sign.Radius)
				{
					own = true;
					continue;
				}
				// Footprints are circles round boxes: only the box's middle being within the sign's reach counts as standing on it.
				double dist = Math.Sqrt((x - sign.X) * (x - sign.X) + (z - sign.Z) * (z - sign.Z));
				if (dist < sign.Radius + Math.Min(r, 4.0) && !Signs.All.Exists(o => o != sign && o.X == x && o.Z == z))
				{
					crowded++;
				}
			}
			undrawn += own && d.Mesh != null && d.Mesh.Triangles[Buildings.Board].Length > 0 ? 0 : 1;
		}
		// The faces: inside the atlas and apart.
		List<Signs.Face> faces = new List<Signs.Face>();
		foreach (Signs.Sign sign in Signs.All)
		{
			foreach (Signs.Board b in sign.Boards)
			{
				faces.Add(b.Front);
				if (b.Back.W > 0)
				{
					faces.Add(b.Back);
				}
			}
		}
		int outside = 0, overlap = 0;
		HashSet<(int, int)> seen = new HashSet<(int, int)>();
		List<Signs.Face> distinct = new List<Signs.Face>();
		foreach (Signs.Face f in faces)
		{
			outside += f.W <= 0 || f.X < 0 || f.Y < 0 || f.X + f.W > Signs.AtlasW || f.Y + f.H > Signs.AtlasH ? 1 : 0;
			if (seen.Add((f.X, f.Y)))
			{
				distinct.Add(f);
			}
		}
		for (int a = 0; a < distinct.Count; a++)
		{
			for (int b = a + 1; b < distinct.Count; b++)
			{
				Signs.Face p = distinct[a], q = distinct[b];
				overlap += p.X < q.X + q.W && q.X < p.X + p.W && p.Y < q.Y + q.H && q.Y < p.Y + p.H ? 1 : 0;
			}
		}
		Console.WriteLine("signs:   " + Signs.NameSigns + " place-name signs, " + Signs.Fingerposts + " fingerposts (" + Signs.Fingers + " fingers): " + onRoad + " points over a road, " + wet + " posts in water, " + crowded + " buildings on a sign, " + undrawn + " not drawn; " + distinct.Count + " faces, " + outside + " outside the atlas, " + overlap + " overlapping");
		return Signs.NameSigns > 0 && Signs.Fingerposts > 0 && onRoad == 0 && wet == 0 && crowded == 0 && undrawn == 0 && outside == 0 && overlap == 0;
	}
}
