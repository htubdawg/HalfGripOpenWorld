using System;
using System.Collections.Generic;
using System.Linq;
using HalfGripOpenWorld;
using UnityEngine;

// Checks of the built world that the preview tool cannot make (they need Unity's types and the mesh builders), outside the game:
//   dotnet run -c Release --project tools/meshcheck [-p:GameDir=<game folder>] [-- check names, e.g. signs, to run only those]
//   1. clip:    road surfaces under the terrain mesh (sampled across every road, bridge decks too, against the 5-unit triangles the
//               game draws)
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
//   9. faces:   road, verge and water meshes with no upside-down flat faces and no zero normals (a white glow), under 65000 vertices
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
//  19. holes:   round every junction (each paved road's ends and crossings), the tiles' road, verge, sidewalk and track meshes drawn
//                from above at half a unit a pixel: the share of the roads' paving (each road's own strip, its cuts ignored) where no
//                paved surface is on top (the ground, a verge or a sidewalk showing through a mouth), under 0.2%, and the worst place
//                under 400 pixels (an L of two streets cut square for each other left 1,096, a T's through road cut under the end of
//                the road ending on it 415, a main road hooking back into its end 1,168)
//  20. joints:  where two roads carry on end to end (Network.Joints), their ends' edges meet: the same points and heights
//  21. deadends: every dead end rounded off: a paved road's paving reaching on past its end, nothing past its verge; a track's or
//                path's ribbon narrowed to a fifth
//  22. tucked:  no track or path ribbon over the drawn surface of a paved road it runs onto
//  23. hubs:    where three or more roads end at one place (Network.Hub), each arm's cut meets the plate's outline (points and
//                heights), the tiles' paving covers the whole plate, and the drawn ground is under it
//  24. farview: the far view's grid (FarGrid) on the tiles' own vertices at tile corners; along the edges of tiles across the world, the
//                far view's straight edge (its canopy too) never more than its skirt above the drawn ground or water, so no gap shows
//                between a loaded tile and the far view; its build time and what its picture shows
//  25. facades: houses round the main town and a village: every wall drawn from an atlas reads one row of it per face, and a window,
//                lancet or door row whole from its bottom to its top (no window cut off by the eaves or the ground); no roof covering
//                drawn facing down (a roof is a slab with an underside in the trim, not a sheet drawn both ways); chimneys and trim drawn
//  26. homes:   every drive's home: its mailbox off every road's paving and verge and out of the water (or left out, 1 in 50 at most); a roadside house's footprint
//                clear of every road, lot, park and the water, at its drive's end
//  27. backroads: the back roads joined up (Network.Links, Throughs, Drives): few lane dead ends out in the country (a farm at the
//                end of a long lane boxed in by water or hills), no through way hooking round on itself, and no main road (trunk,
//                primary, secondary) ending anywhere but on another road or at the map's edge
//  27b. alongside: no paved road lying along another (its centre line within the wider one's half width for 40 units or more,
//                running the same way): a lane's through way routed straight back along its own lane was laid on top of it
//                (Network.Build), five farms' lanes doubled, their junctions' stop lines on the other's paving
//  27c. beside: no lane's end running in alongside the road it ends on (within both half widths and 10 more for over 1.6 times
//                that and 10: coming in square it is within reach about that far), and no lane under 120 units with both ends on
//                other roads: a link routed to a farm ran in beside the farm's own lane, grass between, and a through way from the
//                farmyard closed a little triangle beside it (Network.Unbeside, ThroughShortest; the lane graph's lines ran over the
//                grass, node 681); and no driven road cut out whole (Traffic.Driven: node 22's buried street had lanes)
//  28. deer:    the deer's mesh (weights summing to one, no broken faces, bones and vertices in range); for three race seeds, every
//                group's spots and the legs between them on the drawn ground, never in water above a deer's belly nor over a drop,
//                every crossing place on the road it crosses, and every deer followed over half an hour of race time never deep in
//                water nor on any road's paving away from its group's crossing place, standing on the ground as drawn; how far
//                that is from the drawn ground (nowhere); dead deer (DeerKills): bodies thrown by a car at every deer of one race,
//                at speeds up to 150 on the speedometer, and shot ones, all coming to rest within the time allowed lying on the
//                drawn ground (never under it while moving), the same on a second run with frames of other lengths; the kill
//                messages read back as sent, and the host's kill put in place of another; how many groups the DeerAmount setting
//                gives; reactions: every group of a race alerted and fleeing each way at three times of day, never into deep water,
//                onto paving away from its crossing or jumping (through the run, the watch and back to its calm day), the same twice
//  29. wrap:    the highway wraps round (Wrap): the trunk's two tunnels found, facing opposite ways, as wide as each other; the move
//                there and back adding up to nothing; every lane's point past one glue plane landing on the other tunnel's floor in
//                the same lane, going in; the ground under each open tunnel's floor and the road level into its mouth; the highway
//                event's ends clear of both tunnels
//  30. median:  the trunk's median is no lane (the owner, 2026-11-25): every junction lane a left-turn bay, ending at the mouth of a
//                junction where its carriageway turns left and running through none; the bay on its own carriageway's side of the
//                centre line, clear of the inner lane; K-rail on a few stretches only, each well clear of every junction, bay and
//                opening, the median open to drive across over most of the trunk
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
			("seastrip", SeaStrip),
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
			("garages", GarageChecks),
			("events", EventChecks),
			("holes", Holes),
			("joints", Joints),
			("deadends", DeadEnds),
			("tucked", Tucked),
			("hubs", HubChecks),
			("farview", FarChecks),
			("facades", Facades),
			("homes", Homes),
			("clear", ClearOfRoads),
			("backroads", BackRoads),
			("alongside", Alongside),
			("beside", Beside),
			("deer", DeerChecks),
			("wrap", WrapChecks),
			("median", MedianChecks),
			("bays", BayChecks),
			("lights", SignalChecks),
			("lanes", LaneChecks),
		};
		// Some checks' lines don't say FAIL themselves: the failing ones are named at the end.
		List<string> failed = new List<string>();
		foreach ((string name, Func<bool> check) in checks)
		{
			if (args.Length == 0 || Array.IndexOf(args, name) >= 0)
			{
				if (!check())
				{
					ok = false;
					failed.Add(name);
				}
			}
		}
		Console.WriteLine(ok ? "all checks pass" : "SOME CHECKS FAIL: " + string.Join(", ", failed));
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
				if (!Drawn(r, i, out double t0, out double _))
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
						if (bad <= 5)
						{
							Console.WriteLine("  under the terrain: " + r.Kind + " '" + r.Name + "' at (" + x.ToString("0") + ", " + z.ToString("0") + ") side " + f.ToString("0.00") + " by " + over.ToString("0.00"));
						}
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

	/// <summary>The drawn surface's height of road o at (x, z), if (x, z) is well inside its drawn paving (NaN if not), with its cross-fall.</summary>
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
			// Only beside the piece: a point past its end is not on it there (the piece's end height is not the road's beyond), nor
			// past the road's ends (its end itself is).
			double ex = road.X[i + 1] - road.X[i], ez = road.Z[i + 1] - road.Z[i];
			double raw = ((x - road.X[i]) * ex + (z - road.Z[i]) * ez) / Math.Max(1e-9, ex * ex + ez * ez);
			if (raw < -0.001 || raw > 1.001 || d >= bestD || !Drawn(road, i, out double t0, out double t1) || t < t0 || t > t1)
			{
				continue;
			}
			double s = road.S[i] + (road.S[i + 1] - road.S[i]) * t;
			// Half a unit in: a cut overlaps the other road by a quarter unit on purpose (no ground shows at the seam).
			double half = Network.HalfAt(road, s);
			if (d < half - 0.5)
			{
				// At the point across it, with its own cross-fall (a mouth's surface is tilted to meet the road it joins).
				double sx = road.X[i + 1] - road.X[i], sz = road.Z[i + 1] - road.Z[i];
				double across = ((z - road.Z[i]) * sx - (x - road.X[i]) * sz) / Math.Max(1e-9, Math.Sqrt(sx * sx + sz * sz));
				bestD = d;
				Network.Point(road, i, t, across / half, 0.0, out double _, out double _, out best);
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
							if (o == r || !seen.Add(o) || other.Kind > road.Kind || (other.Kind == road.Kind && o > r) || Network.Joined(r, o, x, z))
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
	private static readonly double[] Grade = { 0.05, 0.07, 0.09, 0.11, 0.11, 0.14, 0.14, 0.18, 0.12, HillClimb.Grade };

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
		int over = 0, bad = 0, decks = 0, covered = 0;
		double worst = double.MaxValue;
		foreach (Network.Road r in Network.Roads)
		{
			for (int i = 0; i + 1 < r.Count; i++)
			{
				if (r.Bridge[i] && (i == 0 || !r.Bridge[i - 1]))
				{
					covered += RoadMesh.Covered(r, i, out bool _, out bool _) ? 1 : 0;
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
		Console.WriteLine("bridges: " + decks + " decks (" + covered + " covered); " + bad + " of " + over + " road points over water less than 4 above it, lowest " + (over > 0 ? worst.ToString("0.0") : "-"));
		return bad == 0;
	}

	private static bool Flooded()
	{
		int samples = 0, bad = 0;
		double worst = 0;
		int[] byKind = new int[10];
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

	/// <summary>
	/// The owner's report (0.32.0, world 7,548, -9,002): the ring of water round the sea stood as a strip with a wall along the foot of the
	/// trunk's cutting south of the road. The tile's lake and sea quads (four vertices at one height, larger than a pond's) are sampled
	/// every unit over x 7,500 to 7,700 and z -9,012 to -9,000: none may be above the ground.
	/// </summary>
	/// <summary>
	/// The left-turn bays off the trunk (TurnBays): one at each site asked for; the road a lane wider all along the bay; and no road but
	/// the bay's own two (the side road at its T, the junction behind) meeting the road anywhere on its widened stretch or its tapers, where
	/// the widening would run through a junction it was not laid out for.
	/// </summary>
	private static bool BayChecks()
	{
		int unmatched = 0, narrow = 0, others = 0;
		foreach ((double X, double Z) site in TurnBays.Sites)
		{
			int found = 0;
			foreach (TurnBays.Bay b in TurnBays.Bays)
			{
				Network.Edge(Network.Roads[b.Road], b.Junction, 0, 0.0, out double jx, out double jz, out double _);
				found += Math.Abs(jx - site.X) < TurnBays.SiteReach + 5.0 && Math.Abs(jz - site.Z) < TurnBays.SiteReach + 5.0 ? 1 : 0;
			}
			if (found != 1)
			{
				unmatched++;
				Console.WriteLine("  bays: " + found + " bays at the site (" + site.X.ToString("0") + ", " + site.Z.ToString("0") + ")");
			}
		}
		foreach (TurnBays.Bay b in TurnBays.Bays)
		{
			Network.Road road = Network.Roads[b.Road];
			for (double s = b.S0; s <= b.S1; s += 1.0)
			{
				if (Math.Abs(Network.WideAt(road, s) - TurnBays.Extra) > 1e-6)
				{
					narrow++;
					Console.WriteLine("  bays: road " + b.Road + " not a lane wider at " + s.ToString("0") + " along it, in its bay");
					break;
				}
			}
			for (int q = 0; q < Network.Roads.Count; q++)
			{
				Network.Road other = Network.Roads[q];
				if (q == b.Road || q == b.Other || other.Kind > RoadKind.Lane || other.Count < 2)
				{
					continue;
				}
				for (int k = 0; k < other.Count; k++)
				{
					double best = double.MaxValue, bs = 0.0;
					for (int i = 0; i + 1 < road.Count; i++)
					{
						if (road.S[i + 1] < road.WideA0 || road.S[i] > road.WideB1)
						{
							continue;
						}
						Lines.Project(road.X, road.Z, i, other.X[k], other.Z[k], out double t, out double d);
						if (d < best)
						{
							best = d;
							bs = road.S[i] + (road.S[i + 1] - road.S[i]) * t;
						}
					}
					if (best > Network.Widened(road, bs) + other.Half + 1.0 || bs <= road.WideA0 || bs >= road.WideB1)
					{
						continue;
					}
					bool own = Math.Abs(bs - b.Back) < b.BackHalf + other.Half + 20.0;
					if (!own)
					{
						others++;
						Console.WriteLine("  bays: road " + q + " (" + other.Kind + ") meets road " + b.Road + " at " + bs.ToString("0") + " along it, on its widened stretch " + road.WideA0.ToString("0") + " to " + road.WideB1.ToString("0"));
						break;
					}
				}
			}
		}
		bool ok = unmatched == 0 && narrow == 0 && others == 0;
		Console.WriteLine("bays: " + TurnBays.Bays.Count + " left-turn bays off the trunk at " + TurnBays.Sites.Length + " sites; sites without one " + unmatched + ", bays not a lane wider all along " + narrow + ", other roads meeting a widened stretch " + others + (ok ? "  ok" : "  FAIL"));
		return ok;
	}

	private static bool MedianChecks()
	{
		int notBay = 0, through = 0, loose = 0, shortBays = 0, railNear = 0, railOnBay = 0;
		double trunkLength = 0.0, railed = 0.0;
		foreach (TrunkLanes.Zone z in TrunkLanes.Zones)
		{
			if (z.Kind != TrunkLanes.Median)
			{
				notBay++;
			}
			if (z.S1 - z.S0 < TrunkLanes.Taper * 2.0)
			{
				shortBays++;
			}
			double end = z.Dir > 0 ? z.S1 : z.S0;
			bool leads = false;
			foreach (TrunkLanes.Junction j in TrunkLanes.Junctions)
			{
				if (j.Road != z.Road)
				{
					continue;
				}
				if (j.S > z.S0 + 1.0 && j.S < z.S1 - 1.0)
				{
					through++;
					Console.WriteLine("  median: the bay " + z.S0.ToString("0") + " to " + z.S1.ToString("0") + " runs through the junction at " + j.S.ToString("0"));
				}
				// The junction ahead of the bay's end, on the side its carriageway turns left into.
				if (Math.Abs(j.S - (end + z.Dir * TrunkLanes.Mouth)) < 1.0 && (j.Side == 0 || j.Side == z.Dir))
				{
					leads = true;
				}
			}
			if (!leads)
			{
				loose++;
				Console.WriteLine("  median: the bay " + z.S0.ToString("0") + " to " + z.S1.ToString("0") + " (way " + z.Dir + ") ends at no junction where its carriageway turns left");
			}
		}
		foreach (Network.Road road in Network.Roads)
		{
			if (road.Kind == RoadKind.Trunk && road.Count > 1)
			{
				trunkLength += road.S[road.Count - 1];
			}
		}
		foreach (TrunkLanes.Rail r in TrunkLanes.Rails)
		{
			railed += r.S1 - r.S0;
			Network.Road rr = Network.Roads[r.Road];
			Network.Locate(rr, (r.S0 + r.S1) * 0.5, out int ri, out double rf);
			Console.WriteLine("  median: K-rail " + ((r.S1 - r.S0) / 3.3).ToString("0") + " m real, its middle at (" + (rr.X[ri] + (rr.X[ri + 1] - rr.X[ri]) * rf).ToString("0") + ", " + (rr.Z[ri] + (rr.Z[ri + 1] - rr.Z[ri]) * rf).ToString("0") + ")");
			foreach (TrunkLanes.Junction j in TrunkLanes.Junctions)
			{
				if (j.Road == r.Road && j.S > r.S0 - TrunkLanes.RailClear + 1.0 && j.S < r.S1 + TrunkLanes.RailClear - 1.0)
				{
					railNear++;
					Console.WriteLine("  median: K-rail " + r.S0.ToString("0") + " to " + r.S1.ToString("0") + " within " + TrunkLanes.RailClear.ToString("0") + " of the junction at " + j.S.ToString("0"));
				}
			}
			foreach (TrunkLanes.Zone z in TrunkLanes.Zones)
			{
				if (z.Road == r.Road && z.S1 > r.S0 && z.S0 < r.S1)
				{
					railOnBay++;
				}
			}
		}
		// The bay between the centre line and the inner lane, on its own side; the lanes clear of the median and inside the road.
		bool section = TrunkLanes.BayOut > 0.0 && TrunkLanes.BayIn <= TrunkLanes.MedianHalf + TrunkLanes.InnerShoulder && TrunkLanes.InnerMid - TrunkLanes.LaneWidth * 0.5 >= TrunkLanes.MedianHalf + TrunkLanes.InnerShoulder
			&& TrunkLanes.EdgeLine <= 40.0 && Math.Abs(Traffic.LaneOffset(Network.Roads.First(r => r.Kind == RoadKind.Trunk)) - TrunkLanes.OuterMid) < 1e-9;
		double share = trunkLength > 0.0 ? railed / trunkLength : 1.0;
		bool ok = notBay == 0 && through == 0 && loose == 0 && shortBays == 0 && railNear == 0 && railOnBay == 0 && section && TrunkLanes.Rails.Count > 0 && share < 0.3 && TrunkLanes.Zones.Count > 0;
		Console.WriteLine("median: " + TrunkLanes.Junctions.Count + " junctions on the trunk, " + TrunkLanes.Zones.Count + " left-turn bays (" + notBay + " not bays, " + through + " through a junction, " + loose + " leading nowhere, " + shortBays + " too short); "
			+ TrunkLanes.Rails.Count + " K-rails, " + (railed / 3.3).ToString("0") + " m real, " + (share * 100.0).ToString("0") + "% of the trunk (" + railNear + " near a junction, " + railOnBay + " on a bay); cross-section " + (section ? "ok" : "wrong") + "  " + (ok ? "ok" : "FAIL"));
		return ok;
	}

	/// <summary>
	/// The drawn traffic lights' masts (<see cref="SignalPoles"/>): every paved way into a junction with a light has one; no pole on a
	/// road's paving, in the water or in another pole; and the drivers can see them from the stop line (the owner, on the first drawn
	/// lights: heads over the stop line were seen only from 50 to 100 ft back): from the middle of the outer lane at the line, at least two
	/// heads within 20 degrees of straight ahead and at least 40 ft past the line (the MUTCD's cone of vision and nearest far-side head);
	/// heads more than 180 ft past it (its most) are reported. The lit junctions the same as the signs' (no fingerpost at a light, none
	/// missing elsewhere). (From the old trafficview.)
	/// </summary>
	private static bool SignalChecks()
	{
		SignalPoles.Build();
		int heads = 0, unseen = 0, tooFar = 0, onPaving = 0, together = 0, far = 0, shared = 0, ways = 0, spans = 0, boards = 0;
		double near = 40.0 * 0.3048 * 3.3, most = 180.0 * 0.3048 * 3.3, cone = Math.Tan(20.0 * Math.PI / 180.0);
		for (int i = 0; i < SignalPoles.All.Count; i++)
		{
			SignalPoles.Mast m = SignalPoles.All[i];
			far += m.Far ? 1 : 0;
			heads += m.Heads.Length + 1;
			if (!SignalPoles.Clear(m.X, m.Z) || (m.Span > 0.0 && !SignalPoles.Clear(m.X2, m.Z2)))
			{
				onPaving++;
			}
			spans += m.Span > 0.0 ? 1 : 0;
			boards += SignalPoles.Board(m) != null ? 1 : 0;
			for (int j = 0; j < i; j++)
			{
				SignalPoles.Mast o = SignalPoles.All[j];
				// One pole shared by two arms stands at the same spot; two poles nearly touching is the fault.
				double dd = (o.X - m.X) * (o.X - m.X) + (o.Z - m.Z) * (o.Z - m.Z);
				if (dd < 2.5 * 2.5 && dd > 1e-6)
				{
					together++;
				}
				shared += dd <= 1e-6 ? 1 : 0;
			}
		}
		// Each way in: what its drivers see from the stop line.
		var byWay = new Dictionary<(int, int), List<SignalPoles.Mast>>();
		foreach (SignalPoles.Mast m in SignalPoles.All)
		{
			if (!byWay.TryGetValue((m.Node, m.Arm), out List<SignalPoles.Mast> l))
			{
				byWay[(m.Node, m.Arm)] = l = new List<SignalPoles.Mast>();
			}
			l.Add(m);
		}
		foreach (KeyValuePair<(int, int), List<SignalPoles.Mast>> kv in byWay)
		{
			ways++;
			(int node, int arm) = kv.Key;
			Traffic.Node n = Traffic.Nodes[node];
			Network.Road road = Network.Roads[Traffic.Edges[Traffic.Links[node][arm]].Road];
			Junctions.ArmOf(node, arm, out double dx, out double dz);
			double hx = -dx, hz = -dz, rx = hz, rz = -hx, lane = SignalPoles.Lanes(road)[0];
			double sx = n.X + dx * n.Box + rx * lane, sz = n.Z + dz * n.Box + rz * lane;
			int seen = 0;
			double closest = double.MaxValue;
			foreach (SignalPoles.Mast m in kv.Value)
			{
				var at = new List<double>(m.Heads) { 1.35 };
				foreach (double d in at)
				{
					double px = m.X + m.Ax * d - sx, pz = m.Z + m.Az * d - sz;
					double along = px * hx + pz * hz, side = Math.Abs(px * rx + pz * rz);
					closest = Math.Min(closest, along);
					if (along >= near && side <= along * cone)
					{
						seen++;
					}
					if (along > most)
					{
						tooFar++;
					}
				}
			}
			if (seen < 2)
			{
				unseen++;
				if (unseen <= 8)
				{
					Console.WriteLine("    lights not seen from the stop line: node " + node + " at (" + n.X.ToString("0") + ", " + n.Z.ToString("0") + ") arm " + arm + " (" + road.Kind + "), " + seen + " heads in view, the nearest " + (closest / 3.3 / 0.3048).ToString("0") + " ft past the line");
				}
			}
		}
		// Each way in with a mast has its stop line (the owner, 2026-11-25: "we need stop-lines for lights"), across its own side of the
		// road a little behind where the cars' fronts stop, never wider than its road.
		int noLine = 0;
		foreach ((int node, int arm) in byWay.Keys)
		{
			if (!SignalPoles.StopLine(node, arm, out int lr, out double s0, out double s1, out double lo, out double hi) || hi - lo < 5.0
				|| Math.Max(Math.Abs(lo), Math.Abs(hi)) > Network.Widened(Network.Roads[lr], (s0 + s1) * 0.5))
			{
				noLine++;
				if (noLine <= 8)
				{
					Traffic.Node n = Traffic.Nodes[node];
					Console.WriteLine("    no stop line: node " + node + " at (" + n.X.ToString("0") + ", " + n.Z.ToString("0") + ") arm " + arm);
				}
			}
		}
		foreach ((int sn, int sa) in SignalPoles.SkippedAt)
		{
			Traffic.Node node = Traffic.Nodes[sn];
			Console.WriteLine("    no mast: node " + sn + " at (" + node.X.ToString("0") + ", " + node.Z.ToString("0") + ") arm " + sa + " (" + Network.Roads[Traffic.Edges[Traffic.Links[sn][sa]].Road].Kind + "), box " + node.Box.ToString("0"));
		}
		// The signs take a junction to have lights by the roads' classes alone (they are laid out long before the traffic): where they
		// disagree, a lit junction keeps its fingerpost in front of the poles (the owner, 2026-11-25), or an unlit one loses it.
		int litNoSign = 0, signNoLight = 0;
		foreach (int node in TrafficSignals.Nodes)
		{
			Traffic.Node n = Traffic.Nodes[node];
			if (!Signs.LitAt.Exists(p => (p.x - n.X) * (p.x - n.X) + (p.z - n.Z) * (p.z - n.Z) < 40.0 * 40.0))
			{
				litNoSign++;
				Console.WriteLine("    lights the signs don't know of (a fingerpost there): node " + node + " at (" + n.X.ToString("0") + ", " + n.Z.ToString("0") + ")");
			}
		}
		foreach ((double x, double z) in Signs.LitAt)
		{
			if (!TrafficSignals.Nodes.Exists(node => (Traffic.Nodes[node].X - x) * (Traffic.Nodes[node].X - x) + (Traffic.Nodes[node].Z - z) * (Traffic.Nodes[node].Z - z) < 40.0 * 40.0))
			{
				signNoLight++;
				Console.WriteLine("    no fingerpost but no lights either: (" + x.ToString("0") + ", " + z.ToString("0") + ")");
			}
		}
		bool ok = unseen == 0 && onPaving == 0 && together == 0 && SignalPoles.Skipped == 0 && noLine == 0 && litNoSign == 0 && signNoLight == 0;
		Console.WriteLine("lights: " + SignalPoles.All.Count + " masts (" + far + " far side, " + shared + " sharing a pole) with " + heads + " heads; ways in with no mast " + SignalPoles.Skipped
			+ ", ways in whose lights can't be seen from the stop line " + unseen + " of " + ways + ", poles on paving or in water " + onPaving + ", poles in each other " + together + ", ways in with no stop line " + noLine + ", spans " + spans + ", green boards " + boards + ", lit junctions with a fingerpost " + litNoSign + ", without lights or fingerpost " + signNoLight + ", heads past 180 ft " + tooFar + " (reported)" + (ok ? "  ok" : "  FAIL"));
		return ok && SignalPoles.All.Count > 0;
	}

	private static readonly Dictionary<long, byte[]> laneMasks = new Dictionary<long, byte[]>();

	/// <summary>Whether the drawn surfaces over the unit square holding (x, z) include one of <paramref name="bits"/> (as <see cref="SurfaceMask"/>).</summary>
	private static bool Surface(double x, double z, int bits)
	{
		int tx = (int)Math.Floor(x / 250.0), tz = (int)Math.Floor(z / 250.0);
		long key = ((long)tx << 32) | (uint)tz;
		if (!laneMasks.TryGetValue(key, out byte[] mask))
		{
			laneMasks[key] = mask = SurfaceMask(tx, tz);
		}
		int i = Math.Min(249, Math.Max(0, (int)Math.Floor(x - tx * 250.0))), j = Math.Min(249, Math.Max(0, (int)Math.Floor(z - tz * 250.0)));
		return (mask[j * 250 + i] & bits) != 0;
	}

	/// <summary>Kingsbridge's node 6 (the old traffic's "cars stuck here", where the road to Pinewick meets the town's link: <see cref="TurnBays.Sites"/>): its place, and the pair of moves that cross there (arms 2 to 0 against 0 to 1).</summary>
	private const double Node6X = -2388.0, Node6Z = -4337.0;

	/// <summary>
	/// The lane graph and its conflict areas (docs/TRAFFIC_REWORK.md, phase 1): every lane coming to a junction has a way on and every one
	/// leaving is reached; every connector's line and a car's width either side of it on the paving (a drive's ribbon for a drive);
	/// every pair of connectors whose lines cross given a conflict area, node 6's Pinewick-road pair named; a car waiting at its stop line
	/// short of every area it gives way at; at a junction with lights, each lane's end on the drawn stop line.
	/// </summary>
	private static bool LaneChecks()
	{
		Lanes.Ensure();
		Console.WriteLine("    lane graph: " + Lanes.Report);
		int deadIn = 0, unreached = 0;
		for (int li = 0; li < Lanes.All.Count; li++)
		{
			Lanes.Lane l = Lanes.All[li];
			if (l.Out.Count == 0 && Traffic.Links[l.To].Count >= 2)
			{
				deadIn++;
				if (deadIn <= 6)
				{
					Traffic.Node n = Traffic.Nodes[l.To];
					Console.WriteLine("    a lane with no way on: lane " + li + " (" + (l.Kind == Lanes.Bay ? "bay" : "lane " + l.Index) + ") into node " + l.To + " at (" + n.X.ToString("0") + ", " + n.Z.ToString("0") + ")");
				}
			}
			if (l.In.Count == 0 && l.Kind == Lanes.Through && l.From >= 0 && Traffic.Links[l.From].Count >= 2)
			{
				unreached++;
				if (unreached <= 6)
				{
					Traffic.Node n = Traffic.Nodes[l.From];
					Console.WriteLine("    a lane nothing leads into: lane " + li + " (lane " + l.Index + ") out of node " + l.From + " at (" + n.X.ToString("0") + ", " + n.Z.ToString("0") + ")");
				}
			}
		}
		// On the paving: the line and a car's half width either side.
		const double half = 2.2;
		int offPaving = 0, offConnectors = 0, points = 0;
		var worstOff = new List<(int c, int off)>();
		var offWhy = new Dictionary<string, int>();
		int middleOff = 0;
		int[] offSizes = new int[4];
		for (int ci = 0; ci < Lanes.Connectors.Count; ci++)
		{
			Lanes.Connector c = Lanes.Connectors[ci];
			if (c.Turn == Lanes.Jump)
			{
				continue;
			}
			bool drive = Network.Roads[Traffic.Edges[Lanes.All[c.From].Edge].Road].Kind == RoadKind.Drive || Network.Roads[Traffic.Edges[Lanes.All[c.To].Edge].Road].Kind == RoadKind.Drive;
			int bits = drive ? 1 | 8 : 1, off = 0;
			for (int p = 0; p < c.X.Length; p++)
			{
				int a = Math.Max(0, p - 1), b = Math.Min(c.X.Length - 1, p + 1);
				double hx = c.X[b] - c.X[a], hz = c.Z[b] - c.Z[a], len = Math.Sqrt(hx * hx + hz * hz);
				if (len < 1e-9)
				{
					hx = 1.0;
					hz = 0.0;
					len = 1.0;
				}
				double rx = hz / len, rz = -hx / len;
				foreach (double side in new[] { 0.0, -half, half })
				{
					points++;
					double qx = c.X[p] + rx * side, qz = c.Z[p] + rz * side;
					// The mask is the triangles sampled at each unit square's middle: a point near the edge of the paving may sit in a square
					// whose middle is just off it, so a neighbouring square paved counts.
					if (!Surface(qx, qz, bits) && !Surface(qx + 1.0, qz, bits) && !Surface(qx - 1.0, qz, bits) && !Surface(qx, qz + 1.0, bits) && !Surface(qx, qz - 1.0, bits))
					{
						off++;
						middleOff += side == 0.0 ? 1 : 0;
					}
				}
			}
			if (off > 0)
			{
				offSizes[off <= 3 ? 0 : off <= 10 ? 1 : off <= 30 ? 2 : 3]++;
				offPaving += off;
				offConnectors++;
				worstOff.Add((ci, off));
				if (Environment.GetEnvironmentVariable("LANES_WHY") == "1")
				{
					string key = TurnName(c.Turn) + (c.Wide ? " wide" : "") + " " + Network.Roads[Traffic.Edges[Lanes.All[c.From].Edge].Road].Kind + ">" + Network.Roads[Traffic.Edges[Lanes.All[c.To].Edge].Road].Kind + (off * 3 > c.X.Length * 3 / 4 ? " much" : "");
					offWhy[key] = offWhy.GetValueOrDefault(key) + 1;
				}
			}
		}
		foreach (var kv in offWhy.OrderByDescending(k => k.Value).Take(20))
		{
			Console.WriteLine("    off the paving by kind: " + kv.Value + " " + kv.Key);
		}
		worstOff.Sort((x, y) => y.off != x.off ? y.off.CompareTo(x.off) : x.c.CompareTo(y.c));
		// LANES_WHY=all lists every connector off the paving.
		for (int k = 0; k < Math.Min(Environment.GetEnvironmentVariable("LANES_WHY") == "all" ? worstOff.Count : 8, worstOff.Count); k++)
		{
			Lanes.Connector c = Lanes.Connectors[worstOff[k].c];
			Traffic.Node n = Traffic.Nodes[c.Node];
			Console.WriteLine("    off the paving: connector " + worstOff[k].c + " at node " + c.Node + " (" + n.X.ToString("0") + ", " + n.Z.ToString("0") + ") arms " + c.InArm + ">" + c.OutArm
				+ " " + TurnName(c.Turn) + ", " + worstOff[k].off + " of " + (c.X.Length * 3) + " points");
		}
		// Every pair whose lines cross has an area (an independent test: the segments themselves).
		int crossings = 0, missed = 0;
		HashSet<long> paired = new HashSet<long>();
		foreach (Conflicts.Area area in Conflicts.All)
		{
			paired.Add(((long)area.A << 32) | (uint)area.B);
		}
		for (int n = 0; n < Traffic.Nodes.Count; n++)
		{
			List<int> here = Lanes.AtNode[n];
			for (int i = 0; i < here.Count; i++)
			{
				for (int j = i + 1; j < here.Count; j++)
				{
					Lanes.Connector a = Lanes.Connectors[here[i]], b = Lanes.Connectors[here[j]];
					if (a.Turn == Lanes.Jump || b.Turn == Lanes.Jump || a.From == b.From || a.To == b.To || !LinesCross(a, b))
					{
						continue;
					}
					crossings++;
					if (!paired.Contains(((long)Math.Min(here[i], here[j]) << 32) | (uint)Math.Max(here[i], here[j])))
					{
						missed++;
						if (missed <= 6)
						{
							Console.WriteLine("    crossing with no conflict area: node " + n + " connectors " + here[i] + " and " + here[j]);
						}
					}
				}
			}
		}
		// Node 6: the old traffic let 2>0 and 0>1 through together.
		int six = -1;
		double sixD = double.MaxValue;
		for (int n = 0; n < Traffic.Nodes.Count; n++)
		{
			double d = (Traffic.Nodes[n].X - Node6X) * (Traffic.Nodes[n].X - Node6X) + (Traffic.Nodes[n].Z - Node6Z) * (Traffic.Nodes[n].Z - Node6Z);
			if (d < sixD && Traffic.Links[n].Count >= 3)
			{
				sixD = d;
				six = n;
			}
		}
		bool sixOk = false;
		string sixWhat = "not found";
		if (six >= 0 && sixD < 30.0 * 30.0)
		{
			sixWhat = "no conflict area";
			foreach (Conflicts.Area area in Conflicts.All)
			{
				Lanes.Connector a = Lanes.Connectors[area.A], b = Lanes.Connectors[area.B];
				if (a.Node != six || b.Node != six)
				{
					continue;
				}
				if ((a.InArm == 2 && a.OutArm == 0 && b.InArm == 0 && b.OutArm == 1) || (b.InArm == 2 && b.OutArm == 0 && a.InArm == 0 && a.OutArm == 1))
				{
					sixOk = area.Kind == Conflicts.Crossing && area.Yield != Conflicts.Follow;
					sixWhat = KindName(area.Kind) + ", " + YieldName(area, a.InArm == 2 ? "2>0" : "0>1", a.InArm == 2 ? "0>1" : "2>0") + ", " + (area.A1 - area.A0).ToString("0") + " and " + (area.B1 - area.B0).ToString("0") + " long";
					break;
				}
			}
		}
		if (six >= 0 && !sixOk && sixWhat == "no conflict area")
		{
			// No area: then their cars' bodies must keep well apart, as far as they can be grown before they meet.
			int a = -1, b = -1;
			foreach (int ci in Lanes.AtNode[six])
			{
				Lanes.Connector c = Lanes.Connectors[ci];
				a = c.InArm == 2 && c.OutArm == 0 ? ci : a;
				b = c.InArm == 0 && c.OutArm == 1 ? ci : b;
			}
			if (a >= 0 && b >= 0)
			{
				double grown = 0.0;
				while (grown < 10.0 && !Conflicts.Meet(a, b, grown + 0.25))
				{
					grown += 0.25;
				}
				sixOk = grown >= 1.0;
				sixWhat = "no conflict area: the bodies keep " + (2.0 * grown).ToString("0.0") + " apart" + (grown >= 10.0 ? " or more" : "") + " (2 wanted)";
			}
		}
		Console.WriteLine("    node 6 (" + (six >= 0 ? six.ToString() : "?") + " at " + (six >= 0 ? Traffic.Nodes[six].X.ToString("0") + ", " + Traffic.Nodes[six].Z.ToString("0") : "?") + "): 2>0 against 0>1: " + sixWhat);
		// A car waiting to go on stands clear of every area it gives way at (or may come second to): its lane's hold, how far back from
		// the stop line that is, within the lane and no more than two cars' lengths (drivers wait back from a narrow lane's mouth so a car
		// can turn in past them). A lane shorter than a car (two junctions' boxes all but
		// touching) is no place to wait: its cars are let through both junctions as one, reported.
		int early = 0, chained = 0, held = 0;
		double mostHold = 0.0;
		for (int li = 0; li < Lanes.All.Count; li++)
		{
			Lanes.Lane l = Lanes.All[li];
			if (l.Hold <= 0.0)
			{
				continue;
			}
			held++;
			if (l.Length < Traffic.CarLength)
			{
				chained++;
				continue;
			}
			mostHold = Math.Max(mostHold, l.Hold);
			if (l.Hold > 2.0 * Traffic.CarLength || l.Hold > l.Length)
			{
				early++;
				if (early <= 6)
				{
					Traffic.Node n = Traffic.Nodes[l.To];
					Console.WriteLine("    a car can't wait clear of the cars it gives way to: lane " + li + " (" + Network.Roads[Traffic.Edges[l.Edge].Road].Kind + ") into node " + l.To + " (" + n.X.ToString("0") + ", " + n.Z.ToString("0") + "), held back " + l.Hold.ToString("0.0"));
				}
			}
		}
		// At a junction with lights, every lane's end is on its way in's drawn stop line.
		int offLine = 0, lines = 0;
		foreach (int node in TrafficSignals.Nodes)
		{
			List<int> links = Traffic.Links[node];
			for (int k = 0; k < links.Count; k++)
			{
				if (!SignalPoles.StopLine(node, k, out int road, out double s0, out double s1, out double lo, out double hi))
				{
					continue;
				}
				lines++;
				foreach (int ci in Lanes.AtNode[node])
				{
					Lanes.Connector c = Lanes.Connectors[ci];
					Lanes.Lane l = Lanes.All[c.From];
					if (c.InArm != k)
					{
						continue;
					}
					double off = Lanes.Offset(l, l.End) * -l.Dir;
					if (l.End < s0 - 1.0 || l.End > s1 + 1.0 || off < lo - 0.5 || off > hi + 0.5)
					{
						offLine++;
						if (offLine <= 6)
						{
							Console.WriteLine("    a lane ending off its stop line: node " + node + " arm " + k + " lane " + c.From + " ends at " + l.End.ToString("0.0") + " (line " + s0.ToString("0.0") + "-" + s1.ToString("0.0") + "), " + off.ToString("0.0") + " across (line " + lo.ToString("0.0") + " to " + hi.ToString("0.0") + ")");
						}
						break;
					}
				}
			}
		}
		int[] kinds = new int[4], yields = new int[5];
		foreach (Conflicts.Area area in Conflicts.All)
		{
			kinds[area.Kind]++;
			yields[area.Yield]++;
		}
		// Repeatable: built again from scratch, the same lanes, lines and areas to the last bit.
		ulong first = LaneHash();
		Lanes.Rebuild();
		bool same = LaneHash() == first;
		if (!same)
		{
			Console.WriteLine("    the lane graph built twice is not the same");
		}
		// Most turns are fillets: a sign slip in the fillet's solve left 9 of about 4,100 taking it, every other line a fallback.
		int turns = 0, fillets = 0;
		foreach (Lanes.Connector c in Lanes.Connectors)
		{
			if (c.Turn == Lanes.Left || c.Turn == Lanes.Right)
			{
				turns++;
				fillets += c.Fillet ? 1 : 0;
			}
		}
		bool filletsOk = fillets * 2 > turns;
		bool ok = deadIn == 0 && unreached == 0 && offConnectors == 0 && missed == 0 && sixOk && early == 0 && offLine == 0 && same && filletsOk;
		Console.WriteLine("lanes:   " + (same ? "repeatable; " : "NOT repeatable; ") + Lanes.All.Count + " lanes, " + Lanes.Connectors.Count + " connectors, " + Conflicts.All.Count + " conflict areas (" + kinds[0] + " crossing, " + kinds[1] + " merging, " + kinds[2] + " diverging, " + kinds[3] + " head on; "
			+ (yields[0] + yields[1]) + " give way, " + yields[2] + " kept apart by lights, " + yields[3] + " first come, " + yields[4] + " follow); lanes with no way on " + deadIn + ", lanes nothing leads into " + unreached
			+ ", connectors off the paving " + offConnectors + " (" + offPaving + " of " + points + " points, " + middleOff + " of them the line itself; by points off: " + offSizes[0] + " 1-3, " + offSizes[1] + " 4-10, " + offSizes[2] + " 11-30, " + offSizes[3] + " more), crossing pairs " + crossings + " with no area " + missed + ", node 6's pair " + (sixOk ? "ok" : "MISSING")
			+ ", lanes held back from the stop line " + held + " (most " + mostHold.ToString("0.0") + "; shorter than a car, let through as one " + chained + "), held back more than two cars or off the lane " + early + ", lanes ending off the stop line " + offLine + " of " + lines + " lines, turns that are fillets " + fillets + " of " + turns + "; " + Lanes.BuildMs.ToString("0") + " ms" + (ok ? "  ok" : "  FAIL"));
		return ok;
	}

	private static string TurnName(int turn)
	{
		return turn == Lanes.Straight ? "on" : turn == Lanes.Left ? "left" : turn == Lanes.Right ? "right" : turn == Lanes.Round ? "round" : "jump";
	}

	private static string KindName(int kind)
	{
		return kind == Conflicts.Crossing ? "crossing" : kind == Conflicts.Merging ? "merging" : kind == Conflicts.Diverging ? "diverging" : "head on";
	}

	private static string YieldName(Conflicts.Area area, string a, string b)
	{
		switch (area.Yield)
		{
			case Conflicts.YieldA: return a + " gives way";
			case Conflicts.YieldB: return b + " gives way";
			case Conflicts.Apart: return "kept apart by the lights";
			case Conflicts.FirstCome: return "first come" + (area.Tie == Conflicts.YieldA ? " (" + a + " yields on a tie)" : area.Tie == Conflicts.YieldB ? " (" + b + " yields on a tie)" : "");
			default: return "follow";
		}
	}

	/// <summary>Whether two connectors' lines cross (a segment of one crossing a segment of the other, ends shared by both left out).</summary>
	private static bool LinesCross(Lanes.Connector a, Lanes.Connector b)
	{
		for (int i = 0; i + 1 < a.X.Length; i++)
		{
			for (int j = 0; j + 1 < b.X.Length; j++)
			{
				double rx = a.X[i + 1] - a.X[i], rz = a.Z[i + 1] - a.Z[i], sx = b.X[j + 1] - b.X[j], sz = b.Z[j + 1] - b.Z[j];
				double den = rx * sz - rz * sx;
				if (Math.Abs(den) < 1e-12)
				{
					continue;
				}
				double qx = b.X[j] - a.X[i], qz = b.Z[j] - a.Z[i];
				double t = (qx * sz - qz * sx) / den, u = (qx * rz - qz * rx) / den;
				if (t >= 0.0 && t <= 1.0 && u >= 0.0 && u <= 1.0)
				{
					return true;
				}
			}
		}
		return false;
	}

	private static bool SeaStrip()
	{
		const float tile = 250f;
		int tx = (int)Math.Floor(7500.0 / tile), tz = (int)Math.Floor(-9006.0 / tile);
		RoadMeshData water = WaterMesh.Build(tx, tz, tile);
		double ox = tx * tile, oz = tz * tile;
		int exposed = 0, samples = 0;
		double worst = 0.0;
		for (double x = 7500.5; x < 7700.0; x += 1.0)
		{
			for (double z = -9011.5; z < -9000.0; z += 1.0)
			{
				double ground = WorldGen.Height(x, z);
				// The flat quads: every four vertices from the first quad after the rivers' ribbons are one axis-aligned square at a level.
				for (int i = 0; i + 3 < water.Vertices.Length; i += 4)
				{
					Vector3 a = water.Vertices[i], d = water.Vertices[i + 3];
					if (a.y != d.y || water.Vertices[i + 1].y != a.y || water.Vertices[i + 2].y != a.y || a.x != water.Vertices[i + 1].x || a.z != water.Vertices[i + 2].z)
					{
						continue;
					}
					double x0 = ox + Math.Min(a.x, d.x), x1 = ox + Math.Max(a.x, d.x), z0 = oz + Math.Min(a.z, d.z), z1 = oz + Math.Max(a.z, d.z);
					if (x < x0 || x > x1 || z < z0 || z > z1)
					{
						continue;
					}
					samples++;
					if (a.y > ground + 0.02)
					{
						exposed++;
						worst = Math.Max(worst, a.y - ground);
					}
				}
			}
		}
		bool ok = exposed == 0;
		Console.WriteLine("seastrip: the sea's ring at the foot of the trunk's cutting (7,500 to 7,700, -9,012 to -9,000): " + exposed + " of " + samples + " water samples above the ground (worst " + worst.ToString("0.00") + ")  " + (ok ? "ok" : "FAIL"));
		return ok;
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
					// Each vertex in the footprint's circle against the ground under that vertex, not under the middle: a terrace's
					// houses abut, so the circle takes in the next house's base too, which stands lower on a slope (with the
					// terraced streets, a house 4.8 under its own ground read as 7.3 by its lower neighbour's base).
					float lo = float.MaxValue;
					double deepest = double.MinValue;
					double g = WorldGen.Height(x, z);
					foreach (Vector3 v in d.Mesh.Vertices)
					{
						double wx = v.x + tx * 250.0, wz = v.z + tz * 250.0;
						if ((wx - x) * (wx - x) + (wz - z) * (wz - z) < r * r * 1.05)
						{
							lo = Math.Min(lo, v.y);
							if (v.y < g + 1.0)
							{
								deepest = Math.Max(deepest, WorldGen.Height(wx, wz) - v.y);
							}
						}
					}
					if (lo == float.MaxValue)
					{
						continue;
					}
					n++;
					sunk = Math.Max(sunk, deepest);
					raised = Math.Max(raised, lo - g);
				}
			}
		}
		Console.WriteLine("bases:   " + n + " buildings round " + town.Name + ", base at most " + sunk.ToString("0.0") + " under the ground, " + raised.ToString("0.0") + " above");
		return sunk < 7.0 && raised < 0.1;
	}

	private static bool Facades()
	{
		List<(int tx, int tz)> tiles = new List<(int, int)>();
		Places.Place town = Places.All[0];
		Places.Place village = Places.All.FirstOrDefault(p => p.Kind == PlaceKind.Village);
		foreach (Places.Place p in new[] { town, village })
		{
			if (p == null)
			{
				continue;
			}
			int tx0 = (int)Math.Floor(p.X / 250), tz0 = (int)Math.Floor(p.Z / 250), reach = p == town ? 4 : 2;
			for (int tz = tz0 - reach; tz <= tz0 + reach; tz++)
			{
				for (int tx = tx0 - reach; tx <= tx0 + reach; tx++)
				{
					tiles.Add((tx, tz));
				}
			}
		}
		int walls = 0, mixed = 0, cut = 0, roofs = 0, paper = 0, trim = 0;
		int[] coverings = { Buildings.RedTile, Buildings.Slate, Buildings.BrownTile, Buildings.Thatch };
		foreach ((int tx, int tz) in tiles)
		{
			RoadMeshData d = Buildings.Build(tx, tz, 250f).Mesh;
			if (d == null)
			{
				continue;
			}
			trim += d.Triangles[Buildings.Trim].Length / 3;
			for (int m = 0; m < d.Triangles.Length; m++)
			{
				int[] t = d.Triangles[m];
				bool atlas = Buildings.Atlas(m), covering = Array.IndexOf(coverings, m) >= 0;
				for (int k = 0; k + 2 < t.Length; k += 3)
				{
					if (covering)
					{
						roofs++;
						paper += d.Normals[t[k]].y < -0.01f ? 1 : 0;
					}
					if (!atlas)
					{
						continue;
					}
					walls++;
					// Which row each corner reads, and how far up it (0 to 1).
					float lo = float.MaxValue, hi = float.MinValue;
					int row = -1;
					bool same = true;
					for (int c = 0; c < 3; c++)
					{
						float v = d.Uv[t[k + c]].y * Buildings.AtlasRows;
						int r = Math.Min(Buildings.AtlasRows - 1, (int)Math.Floor(v));
						same &= row < 0 || r == row;
						row = r;
						lo = Math.Min(lo, v - r);
						hi = Math.Max(hi, v - r);
					}
					if (!same)
					{
						mixed++;
						continue;
					}
					bool window = row == Buildings.Sash || row == Buildings.Casement || row == Buildings.Lancet || row == Buildings.DoorRow;
					cut += window && (lo > 0.05f || hi < 0.95f) ? 1 : 0;
				}
			}
		}
		Console.WriteLine("facades: " + walls + " wall triangles on " + tiles.Count + " tiles, " + mixed + " reading two atlas rows, " + cut + " with a window row cut short; " + paper + " of " + roofs + " roof covering triangles facing down; " + trim + " trim triangles");
		return walls > 0 && mixed == 0 && cut == 0 && roofs > 0 && paper == 0 && trim > 0;
	}

	private static bool Faces()
	{
		int down = 0, most = 0, badNormals = 0;
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
					// No zero or broken normal: it is NaN in the lighting, a white glowing patch through the bloom (the trunk's
					// junction lanes' paint narrowing to nothing at a taper's end, RoadMesh.Quad).
					for (int k = 0; d.Normals != null && k < d.Normals.Length; k++)
					{
						Vector3 nk = d.Normals[k];
						if (!(nk.sqrMagnitude > 0.5f && nk.sqrMagnitude < 1.5f))
						{
							if (badNormals < 5)
							{
								Console.WriteLine("  a broken normal (" + nk.x + ", " + nk.y + ", " + nk.z + ") at (" + (tx * 250f + d.Vertices[k].x).ToString("0.0") + ", " + (tz * 250f + d.Vertices[k].z).ToString("0.0") + ")");
							}
							badNormals++;
						}
					}
					for (int m = 0; m < d.Triangles.Length; m++)
					{
						// A covered bridge's roof has an underside.
						if (d == road && m == RoadMesh.Shingles)
						{
							continue;
						}
						int[] t = d.Triangles[m];
						for (int k = 0; k < t.Length; k += 3)
						{
							Vector3 nrm = Vector3.Cross(d.Vertices[t[k + 1]] - d.Vertices[t[k]], d.Vertices[t[k + 2]] - d.Vertices[t[k]]);
							if (nrm.y < -0.5f * nrm.magnitude)
							{
								down++;
								Vector3 c = (d.Vertices[t[k]] + d.Vertices[t[k + 1]] + d.Vertices[t[k + 2]]) / 3f;
								Console.WriteLine("  a face down at (" + (tx * 250f + c.x).ToString("0.0") + ", " + (tz * 250f + c.z).ToString("0.0") + ") in submesh " + m);
							}
						}
					}
				}
			}
		}
		Console.WriteLine("faces:   " + down + " upside-down flat faces in road, verge, sidewalk, track, water and circuit meshes; " + badNormals + " zero or broken normals; most vertices in one mesh " + most);
		return down == 0 && badNormals == 0 && most < 65000;
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
					// A market place only keeps other roads off it (see Town.Fit).
					if (v > 0.0 && !(lot.Kind == LotKind.Market ? Network.Clear(x, z, lot.Road, 14.0) : Network.Claims(x, z, lot.Road, Network.Verge + v, 2.0, also)))
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
		double worst = 0.0, wx = 0.0, wz = 0.0;
		string wroad = "";
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
							if (over > worst)
							{
								worst = over;
								wx = x;
								wz = z;
								wroad = r.Name;
							}
						}
					}
				}
			}
		}
		Console.WriteLine("ribbons: " + bad + " of " + samples + " track and path surface samples with the ground through them, worst " + worst.ToString("0.00") + (bad > 0 ? " ('" + wroad + "' at " + wx.ToString("0") + ", " + wz.ToString("0") + ")" : ""));
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
		int onRoad = 0, wet = 0, crowded = 0, undrawn = 0, misplaced = 0;
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
					Console.WriteLine("  a building (" + x.ToString("0.0") + ", " + z.ToString("0.0") + ", radius " + r.ToString("0.0") + ") on the sign at (" + sign.X.ToString("0.0") + ", " + sign.Z.ToString("0.0") + ")");
				}
			}
			undrawn += sign.Hidden || (own && d.Mesh != null && d.Mesh.Triangles[Buildings.Board].Length > 0) ? 0 : 1;
			// A fingerpost is hidden only at a junction with lights, where its places hang on the mast arms (the owner, 2026-11-25).
			misplaced += sign.Hidden && !Signs.LitAt.Exists(q => (q.x - sign.X) * (q.x - sign.X) + (q.z - sign.Z) * (q.z - sign.Z) < 80.0 * 80.0) ? 1 : 0;
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
		Console.WriteLine("signs:   " + Signs.NameSigns + " place-name signs, " + Signs.Fingerposts + " fingerposts (" + Signs.Fingers + " fingers): " + onRoad + " points over a road, " + wet + " posts in water, " + crowded + " buildings on a sign, " + undrawn + " not drawn; " + Signs.Hidden + " fingerposts at lights left for the masts' " + Signs.Overheads.Count + " junctions' green boards (" + misplaced + " away from lights); " + distinct.Count + " faces, " + outside + " outside the atlas, " + overlap + " overlapping");
		return Signs.NameSigns > 0 && Signs.Fingerposts > 0 && onRoad == 0 && wet == 0 && crowded == 0 && undrawn == 0 && misplaced == 0 && outside == 0 && overlap == 0;
	}

	/// <summary>
	/// A car respawned at a garage: its heading a unit vector pointing out (a point ahead nearer the lot's front, or further along the
	/// start straight in the pits), its footprint (14 by 6) inside the circle, and the ground <c>Streamer.Place</c> drops it on within
	/// half a unit of the garage's surface under each corner and the middle.
	/// </summary>
	private static bool RespawnFits(GarageSites.Site g, Town.Lot lot)
	{
		bool ok = true;
		if (Math.Abs(g.Hx * g.Hx + g.Hz * g.Hz - 1.0) > 1e-6)
		{
			Console.WriteLine("  garage " + g.Name + ": its respawn heading is not a unit vector");
			return false;
		}
		double ax = g.X + g.Hx * 10.0, az = g.Z + g.Hz * 10.0;
		bool outward;
		if (lot != null)
		{
			Town.Local(lot, g.X, g.Z, out _, out double v0);
			Town.Local(lot, ax, az, out _, out double v1);
			outward = v1 < v0 - 9.0;
		}
		else
		{
			Circuit.Local(g.X, g.Z, out double u0, out _);
			Circuit.Local(ax, az, out double u1, out _);
			outward = u1 > u0 + 9.0;
		}
		if (!outward)
		{
			Console.WriteLine("  garage " + g.Name + ": a car respawned there would not face out");
			ok = false;
		}
		for (int k = 0; k < 5; k++)
		{
			double along = k == 0 ? 0.0 : (k < 3 ? 7.0 : -7.0), across = k == 0 ? 0.0 : (k % 2 == 0 ? 3.0 : -3.0);
			double x = g.X + g.Hx * along + g.Hz * across, z = g.Z + g.Hz * along - g.Hx * across;
			double dx = x - g.X, dz = z - g.Z;
			double drop = WorldGen.Height(x, z) - (GarageSites.SurfaceAt(g, x, z) + Network.Lift);
			if (dx * dx + dz * dz > g.Radius * g.Radius || Math.Abs(drop) > 0.5)
			{
				Console.WriteLine("  garage " + g.Name + ": a respawned car's corner at (" + x.ToString("0") + ", " + z.ToString("0") + ") is out of the circle or " + drop.ToString("0.00") + " off the surface");
				ok = false;
			}
		}
		return ok;
	}

	/// <summary>
	/// Every garage's circle on clear, drivable ground: round its edge and at its middle, on its own lot's surface and off its building
	/// (pumps, canopy posts, vans and awnings too), or on the circuit's pit lane off the garages; at the surface's height (the ring
	/// follows the slope); its ring over the drawn ground; no two circles overlapping. A car respawned there (<c>GameMode.ToSpawn</c>,
	/// put at <c>WorldGen.Height</c> by <c>Streamer.Place</c>) lands on the surface, faces out (to the lot's road, or along the pit lane
	/// to its exit) and fits in the circle.
	/// </summary>
	private static bool GarageChecks()
	{
		int samples = 0, off = 0, blocked = 0, height = 0, sunk = 0, overlap = 0, respawn = 0;
		List<GarageSites.Site> all = GarageSites.All;
		foreach (GarageSites.Site g in all)
		{
			Town.Lot lot = g.Kind == GarageKind.Pits ? null : Town.Lots.OrderBy(l => (l.X - g.X) * (l.X - g.X) + (l.Z - g.Z) * (l.Z - g.Z)).First();
			if (!RespawnFits(g, lot))
			{
				respawn++;
			}
			for (int k = -1; k < 32; k++)
			{
				double a = k * Math.PI / 16.0, r = k < 0 ? 0.0 : g.Radius;
				double x = g.X + Math.Cos(a) * r, z = g.Z + Math.Sin(a) * r;
				double surface = GarageSites.SurfaceAt(g, x, z);
				samples++;
				if (lot != null)
				{
					Town.Lot on = Town.At(x, z, out bool building);
					Town.Local(lot, x, z, out double u, out double v);
					if (on != lot)
					{
						off++;
						Console.WriteLine("  garage " + g.Name + ": (" + x.ToString("0") + ", " + z.ToString("0") + ") is off its lot");
						continue;
					}
					if (building || Obstacle(lot, u, v))
					{
						blocked++;
						Console.WriteLine("  garage " + g.Name + ": (" + x.ToString("0") + ", " + z.ToString("0") + ") is on a building or in the way of one (u " + u.ToString("0.0") + ", v " + v.ToString("0.0") + ")");
					}
					if (Math.Abs(Town.Surface(lot, u) - surface) > 0.01)
					{
						height++;
					}
				}
				else
				{
					Circuit.Local(x, z, out double u, out double v);
					if (Circuit.SurfaceAt(x, z) == CircuitSurface.None || Circuit.Building(x, z) || v < Circuit.PitWallOut || v > Circuit.PitLaneOut)
					{
						off++;
						Console.WriteLine("  garage " + g.Name + ": (" + x.ToString("0") + ", " + z.ToString("0") + ") is off the pit lane");
					}
					if (Math.Abs(Circuit.PlaneAt(x, z) + Circuit.Lift - surface) > 0.01)
					{
						height++;
					}
				}
				// The ring is drawn 0.14 over the surface.
				if (surface + Network.Lift + 0.14 < Mesh(x, z) + 0.05)
				{
					sunk++;
				}
			}
			foreach (GarageSites.Site o in all)
			{
				if (o != g && Math.Sqrt((o.X - g.X) * (o.X - g.X) + (o.Z - g.Z) * (o.Z - g.Z)) < o.Radius + g.Radius + 2.0)
				{
					overlap++;
				}
			}
		}
		// The map's marks: every garage in one, and no two marks (about 100 units across) running into each other.
		List<GarageSites.Mark> marks = GarageSites.Marks;
		int marked = marks.Sum(m => m.Count), blots = 0;
		for (int i = 0; i < marks.Count; i++)
		{
			for (int j = i + 1; j < marks.Count; j++)
			{
				if (Math.Sqrt((marks[i].X - marks[j].X) * (marks[i].X - marks[j].X) + (marks[i].Z - marks[j].Z) * (marks[i].Z - marks[j].Z)) < 110.0)
				{
					blots++;
				}
			}
		}
		bool ok = all.Count > 0 && off == 0 && blocked == 0 && height == 0 && sunk == 0 && overlap == 0 && respawn == 0 && marked == all.Count && blots == 0;
		Console.WriteLine("garages: " + all.Count + " (" + string.Join(", ", all.GroupBy(g => g.Kind).Select(g => g.Count() + " " + g.Key).ToArray()) + "), " + samples + " samples: " + off + " off their surface, " + blocked + " on a building or in the way, "
			+ height + " at the wrong height, " + sunk + " ring under the ground, " + overlap / 2 + " overlapping, " + respawn + " bad respawn spots; " + marks.Count + " map marks with " + marked + " garages, " + blots + " running into each other " + (ok ? "ok" : "FAIL"));
		return ok;
	}

	/// <summary>
	/// The events' courses: gates in order to the finish (the last on the finish line), each at the road's height and reaching over
	/// its own stretch of road only (never over another part of the course, where a car would pass it out of turn); each gate's
	/// respawn point off any bridge (the reset puts the car on the ground); the grid spot on the road; the start box off the course,
	/// whole on its lot's open surface (the circuit's pit lane; the highway's westbound carriageway behind its grid), its ring just over
	/// it, clear of the garages' circles; every grid slot (a car's footprint on the road, the highway's on its eastbound carriageway,
	/// at its height, off bridges, behind the start line, clear of the gates and the other slots) and waiting spot (on the box's
	/// ground, off the course, clear of the box, the garages and the other spots).
	/// </summary>
	private static bool EventChecks()
	{
		int order = 0, height = 0, reach = 0, bridged = 0, grid = 0, off = 0, sunk = 0, floating = 0, garages = 0, gateCount = 0, slots = 0, waits = 0;
		List<string> limits = new List<string>();
		List<Courses.Course> all = Courses.All;
		foreach (Courses.Course c in all)
		{
			Network.Road road = c.Road;
			double last = 0.0;
			for (int k = 0; k < c.Gates.Count; k++)
			{
				Courses.Gate g = c.Gates[k];
				gateCount++;
				if (g.Along <= last || (k == c.Gates.Count - 1 && Math.Abs(g.Along - c.Distance) > 0.01))
				{
					order++;
					Console.WriteLine("  event " + c.Name + ": gate " + k + " out of order (" + g.Along.ToString("0") + ")");
				}
				last = g.Along;
				bool bridge = false;
				c.At(c.StartS + g.Along + Courses.RespawnPast, out double rx, out double rz, out double _, out double _, out double _);
				if (road != null)
				{
					int near = 0;
					double best = double.MaxValue;
					for (int i = 0; i < road.Count; i++)
					{
						double d = (road.X[i] - rx) * (road.X[i] - rx) + (road.Z[i] - rz) * (road.Z[i] - rz);
						if (d < best)
						{
							best = d;
							near = i;
						}
					}
					bridge = road.Bridge[near] || road.Bridge[Math.Max(0, near - 1)] || road.Bridge[Math.Min(road.Count - 1, near + 1)];
				}
				if (bridge)
				{
					bridged++;
					Console.WriteLine("  event " + c.Name + ": gate " + k + "'s respawn (" + rx.ToString("0") + ", " + rz.ToString("0") + ") is on a bridge");
				}
				else if (Math.Abs(g.Y - Drawn(c, road, g.X, g.Z)) > 0.05)
				{
					height++;
					Console.WriteLine("  event " + c.Name + ": gate " + k + " at " + g.Y.ToString("0.00") + ", the drawn surface " + Drawn(c, road, g.X, g.Z).ToString("0.00"));
				}
				// Along the gate's line: the nearest point of the course is always on the gate's own stretch.
				for (double a = -g.Half; a <= g.Half + 0.01; a += 2.0)
				{
					double x = g.X + g.Dz * a, z = g.Z - g.Dx * a;
					double own = double.MaxValue, other = double.MaxValue;
					int pieces = c.Loop ? c.Count : c.Count - 1;
					double gs = c.StartS + g.Along;
					for (int i = 0; i < pieces; i++)
					{
						int q = (i + 1) % c.Count;
						double sx = c.X[q] - c.X[i], sz = c.Z[q] - c.Z[i];
						double len2 = sx * sx + sz * sz;
						double f = len2 > 0.0 ? Math.Max(0.0, Math.Min(1.0, ((x - c.X[i]) * sx + (z - c.Z[i]) * sz) / len2)) : 0.0;
						double px = c.X[i] + sx * f - x, pz = c.Z[i] + sz * f - z;
						double d = px * px + pz * pz;
						double ds = Math.Abs(c.S[i] - gs);
						if (c.Loop)
						{
							ds = Math.Min(ds % c.Length, c.Length - ds % c.Length);
						}
						if (ds < g.Half * 2.0 + 20.0)
						{
							own = Math.Min(own, d);
						}
						else
						{
							other = Math.Min(other, d);
						}
					}
					if (other <= own)
					{
						reach++;
						Console.WriteLine("  event " + c.Name + ": gate " + k + " reaches over another stretch of the course at (" + x.ToString("0") + ", " + z.ToString("0") + ")");
						break;
					}
				}
			}
			// The grid spot: a car's length round it on the course's road.
			for (int k = -1; k < 16; k++)
			{
				double a = k * Math.PI / 8.0, r = k < 0 ? 0.0 : 7.0;
				double x = c.GridX + Math.Cos(a) * r, z = c.GridZ + Math.Sin(a) * r;
				if (Math.Sqrt(c.Distance2(x, z)) > c.Half)
				{
					grid++;
					Console.WriteLine("  event " + c.Name + ": the grid spot's (" + x.ToString("0") + ", " + z.ToString("0") + ") is off the road");
				}
			}
			if (Math.Abs(c.GridY - Drawn(c, road, c.GridX, c.GridZ)) > 0.05)
			{
				grid++;
				Console.WriteLine("  event " + c.Name + ": the grid spot at " + c.GridY.ToString("0.00") + ", the road " + Drawn(c, road, c.GridX, c.GridZ).ToString("0.00"));
			}
			// The start box: on its lot's open surface (the circuit's pit lane), off the course, its ring just over the surface. The
			// highway's is on the westbound carriageway behind the start line: whole on it, clear of the median and the shoulder.
			bool highway = c.Kind == Courses.Kind.Highway;
			Town.Lot lot = road == null || highway ? null : Town.Lots.FirstOrDefault(l => l.Kind == (c.Kind == Courses.Kind.Rally ? LotKind.Service : LotKind.Paddock));
			// The box's and the waiting spots' ground: the lot's open surface, the circuit's pit lane, the highway's westbound
			// carriageway behind the grid. Its surface's height at (x, z); false (and what it is off) when (x, z) is not on it.
			bool Open(double x, double z, out double surface, out string what)
			{
				if (highway)
				{
					surface = Drawn(c, road, x, z);
					double across = Math.Sqrt(c.Distance2(x, z));
					c.At(c.StartS - Courses.HighwayBoxBack, out double sx, out double sz, out double _, out double sdx, out double sdz);
					double ahead = (x - sx) * sdx + (z - sz) * sdz, left = (z - sz) * sdx - (x - sx) * sdz;
					what = "the westbound carriageway behind the grid";
					return !(across < TrunkLanes.MedianHalf + TrunkLanes.InnerShoulder || across > c.Half - 4.0 || left < 0.0 || c.StartS - Courses.HighwayBoxBack + ahead > c.StartS - Courses.GridBack - 10.0);
				}
				if (lot != null)
				{
					Town.Lot on = Town.At(x, z, out bool building);
					Town.Local(lot, x, z, out double u, out double v);
					surface = Town.Surface(lot, u);
					what = "its lot's open surface";
					return !(on != lot || building || Obstacle(lot, u, v));
				}
				Circuit.Local(x, z, out double cu, out double cv);
				surface = Circuit.PlaneAt(x, z) + Circuit.Lift;
				what = "the pit lane";
				return !(Circuit.SurfaceAt(x, z) == CircuitSurface.None || Circuit.Building(x, z) || cv < Circuit.PitWallOut || cv > Circuit.PitLaneOut || cu < Circuit.PitA || cu > Circuit.PitB);
			}
			for (int k = -1; k < 32; k++)
			{
				double a = k * Math.PI / 16.0, r = k < 0 ? 0.0 : c.BoxRadius;
				double x = c.BoxX + Math.Cos(a) * r, z = c.BoxZ + Math.Sin(a) * r;
				if (!Open(x, z, out double surface, out string what))
				{
					off++;
					Console.WriteLine("  event " + c.Name + ": the start box's (" + x.ToString("0") + ", " + z.ToString("0") + ") is off " + what);
				}
				if (!highway && Math.Sqrt(c.Distance2(x, z)) < c.Half + 2.0)
				{
					off++;
					Console.WriteLine("  event " + c.Name + ": the start box's (" + x.ToString("0") + ", " + z.ToString("0") + ") is on the course");
				}
				// The ring is drawn 0.2 over the box's surface: over the ground, never far over the surface.
				double ring = c.BoxSurface(x, z) + 0.2;
				if (ring + Network.Lift < Mesh(x, z) + 0.05 || ring < surface + 0.05)
				{
					sunk++;
					Console.WriteLine("  event " + c.Name + ": the start box's ring at (" + x.ToString("0") + ", " + z.ToString("0") + ") is under the ground or its surface");
				}
				else if (ring > surface + 0.4)
				{
					floating++;
					Console.WriteLine("  event " + c.Name + ": the start box's ring at (" + x.ToString("0") + ", " + z.ToString("0") + ") is " + (ring - surface).ToString("0.00") + " over its surface");
				}
			}
			foreach (GarageSites.Site g in GarageSites.All)
			{
				if (Math.Sqrt((g.X - c.BoxX) * (g.X - c.BoxX) + (g.Z - c.BoxZ) * (g.Z - c.BoxZ)) < g.Radius + c.BoxRadius + 2.0)
				{
					garages++;
				}
			}
			// The grid slots: the first is the grid spot; each car's footprint on the road (the highway's on the eastbound
			// carriageway), at the drawn surface's height, off any bridge, behind the start line, clear of the gates and of each other.
			if (c.Slots.Count < 2 || c.Slots.Count > Courses.PairSlots || c.Slots[0].X != c.GridX || c.Slots[0].Z != c.GridZ)
			{
				slots++;
				Console.WriteLine("  event " + c.Name + ": " + c.Slots.Count + " grid slots, the first " + (c.Slots.Count > 0 && c.Slots[0].X == c.GridX && c.Slots[0].Z == c.GridZ ? "the grid spot" : "not the grid spot"));
			}
			for (int k = 0; k < c.Slots.Count; k++)
			{
				Courses.Spot slot = c.Slots[k];
				string name = "  event " + c.Name + ": grid slot " + k;
				foreach ((double x, double z) in Footprint(slot))
				{
					Nearest(c, x, z, out double _, out double across);
					bool on = highway ? across > 5.0 && across < c.Half - 4.0 : Math.Abs(across) <= c.Half;
					if (!on)
					{
						slots++;
						Console.WriteLine(name + "'s footprint (" + x.ToString("0") + ", " + z.ToString("0") + ") is " + across.ToString("0.0") + " across, off " + (highway ? "the eastbound carriageway" : "the road"));
						break;
					}
				}
				if (Math.Abs(slot.Y - Drawn(c, road, slot.X, slot.Z)) > 0.05)
				{
					slots++;
					Console.WriteLine(name + " at " + slot.Y.ToString("0.00") + ", the road " + Drawn(c, road, slot.X, slot.Z).ToString("0.00"));
				}
				if (Bridged(road, slot.X, slot.Z))
				{
					slots++;
					Console.WriteLine(name + " (" + slot.X.ToString("0") + ", " + slot.Z.ToString("0") + ") is on a bridge");
				}
				Nearest(c, slot.X, slot.Z, out double ss, out double _);
				double behind = c.StartS - ss;
				if (c.Loop)
				{
					behind = ((behind % c.Length) + c.Length) % c.Length;
				}
				if (behind <= 7.0 || behind > 400.0)
				{
					slots++;
					Console.WriteLine(name + " is " + behind.ToString("0") + " behind the start line");
				}
				foreach (Courses.Gate g in c.Gates)
				{
					double apart = Math.Abs(ss - (c.StartS + g.Along));
					if (c.Loop)
					{
						apart %= c.Length;
						apart = Math.Min(apart, c.Length - apart);
					}
					// A lap's end on the start line itself: the slots are behind it by design (checked above).
					bool line = c.Loop && Math.Abs(Math.IEEERemainder(g.Along, c.Length)) < 0.01;
					if (apart < 20.0 && !line)
					{
						slots++;
						Console.WriteLine(name + " is " + apart.ToString("0") + " from a gate");
					}
				}
				for (int j = 0; j < k; j++)
				{
					if (Overlap(slot, c.Slots[j]))
					{
						slots++;
						Console.WriteLine(name + " runs into slot " + j);
					}
				}
			}
			// The waiting spots: each car's footprint on the box's ground (the lot's open surface, the pit lane, the westbound
			// carriageway), off the course, clear of the box, the garages and each other, at its surface's height.
			if (c.Waits.Count != c.MaxEntrants)
			{
				waits++;
				Console.WriteLine("  event " + c.Name + ": " + c.Waits.Count + " waiting spots");
			}
			for (int k = 0; k < c.Waits.Count; k++)
			{
				Courses.Spot wait = c.Waits[k];
				string name = "  event " + c.Name + ": waiting spot " + k;
				foreach ((double x, double z) in Footprint(wait))
				{
					string problem = null;
					if (!Open(x, z, out double _, out string what))
					{
						problem = "off " + what;
					}
					else if (!highway && Math.Sqrt(c.Distance2(x, z)) < c.Half + 2.0)
					{
						problem = "on the course";
					}
					else if (Math.Sqrt((x - c.BoxX) * (x - c.BoxX) + (z - c.BoxZ) * (z - c.BoxZ)) < c.BoxRadius + 1.0)
					{
						problem = "in the start box";
					}
					else if (GarageSites.All.Any(g => Math.Sqrt((g.X - x) * (g.X - x) + (g.Z - z) * (g.Z - z)) < g.Radius + 1.0))
					{
						problem = "in a garage's circle";
					}
					if (problem != null)
					{
						waits++;
						Console.WriteLine(name + "'s footprint (" + x.ToString("0") + ", " + z.ToString("0") + ") is " + problem);
						break;
					}
				}
				if (!Open(wait.X, wait.Z, out double ground, out string _) || Math.Abs(wait.Y - ground) > 0.05)
				{
					waits++;
					Console.WriteLine(name + " at " + wait.Y.ToString("0.00") + ", its surface " + ground.ToString("0.00"));
				}
				if (Math.Abs(wait.Dx * wait.Dx + wait.Dz * wait.Dz - 1.0) > 1e-6)
				{
					waits++;
					Console.WriteLine(name + "'s heading is not a unit vector");
				}
				for (int j = 0; j < k; j++)
				{
					if (Overlap(wait, c.Waits[j]))
					{
						waits++;
						Console.WriteLine(name + " runs into spot " + j);
					}
				}
			}
			int limit = (int)c.TimeLimit;
			limits.Add(c.Name + " " + c.Slots.Count + " slots, " + c.Waits.Count + " waiting, limit " + limit / 60 + ":" + (limit % 60).ToString("00"));
		}
		bool ok = all.Count == 4 && order == 0 && height == 0 && reach == 0 && bridged == 0 && grid == 0 && off == 0 && sunk == 0 && floating == 0 && garages == 0 && slots == 0 && waits == 0;
		List<string> names = all.Select(c => c.Name + " " + c.Gates.Count + " gates over " + c.Distance.ToString("0")).ToList();
		Console.WriteLine("events:  " + string.Join(", ", names) + "; " + gateCount + " gates: " + order + " out of order, " + height + " off the road's height, " + reach + " reaching over another stretch, " + bridged + " respawning on a bridge; " + grid + " grid spot samples off the road; start boxes: "
			+ off + " samples off their surface or on the course, " + sunk + " ring under the ground, " + floating + " floating, " + garages + " on a garage; "
			+ string.Join(", ", limits) + "; " + slots + " bad grid slots, " + waits + " bad waiting spots " + (ok ? "ok" : "FAIL"));
		return ok;
	}

	/// <summary>A car's footprint at a spot: 14 long and 6 wide, facing its heading, as points round and through it.</summary>
	private static IEnumerable<(double x, double z)> Footprint(Courses.Spot s)
	{
		for (double along = -7.0; along <= 7.01; along += 3.5)
		{
			for (double side = -3.0; side <= 3.01; side += 1.5)
			{
				yield return (s.X + s.Dx * along + s.Dz * side, s.Z + s.Dz * along - s.Dx * side);
			}
		}
	}

	/// <summary>Whether two cars' footprints at these spots overlap (or come within half a unit).</summary>
	private static bool Overlap(Courses.Spot a, Courses.Spot b)
	{
		bool Inside(Courses.Spot s, double x, double z)
		{
			double along = (x - s.X) * s.Dx + (z - s.Z) * s.Dz, side = (x - s.X) * s.Dz - (z - s.Z) * s.Dx;
			return Math.Abs(along) <= 7.5 && Math.Abs(side) <= 3.5;
		}
		return Footprint(a).Any(p => Inside(b, p.x, p.z)) || Footprint(b).Any(p => Inside(a, p.x, p.z));
	}

	/// <summary>The nearest point of the course's centre line to (x, z): its distance along, and how far right of it (x, z) is.</summary>
	private static void Nearest(Courses.Course c, double x, double z, out double s, out double across)
	{
		double best = double.MaxValue;
		s = 0.0;
		across = 0.0;
		int pieces = c.Loop ? c.Count : c.Count - 1;
		for (int i = 0; i < pieces; i++)
		{
			int q = (i + 1) % c.Count;
			double sx = c.X[q] - c.X[i], sz = c.Z[q] - c.Z[i];
			double len2 = sx * sx + sz * sz, len = Math.Sqrt(len2);
			double f = len2 > 0.0 ? Math.Max(0.0, Math.Min(1.0, ((x - c.X[i]) * sx + (z - c.Z[i]) * sz) / len2)) : 0.0;
			double px = c.X[i] + sx * f, pz = c.Z[i] + sz * f;
			double d = (px - x) * (px - x) + (pz - z) * (pz - z);
			if (d < best && len > 0.0)
			{
				best = d;
				s = c.S[i] + len * f;
				// Right of the way the course goes, (dz, -dx).
				across = ((x - px) * sz - (z - pz) * sx) / len;
			}
		}
	}

	/// <summary>Whether the road's samples nearest (x, z) are on a bridge (false for the circuit, which has none).</summary>
	private static bool Bridged(Network.Road road, double x, double z)
	{
		if (road == null)
		{
			return false;
		}
		int near = 0;
		double best = double.MaxValue;
		for (int i = 0; i < road.Count; i++)
		{
			double d = (road.X[i] - x) * (road.X[i] - x) + (road.Z[i] - z) * (road.Z[i] - z);
			if (d < best)
			{
				best = d;
				near = i;
			}
		}
		return road.Bridge[near] || road.Bridge[Math.Max(0, near - 1)] || road.Bridge[Math.Min(road.Count - 1, near + 1)];
	}

	/// <summary>The course's drawn surface at (x, z), from sea level: its road's centre line at the nearest point (as RoadMesh draws it), or the circuit's track.</summary>
	private static double Drawn(Courses.Course c, Network.Road road, double x, double z)
	{
		if (road == null)
		{
			return Circuit.PlaneAt(x, z) + Circuit.Lift;
		}
		int piece = 0;
		double best = double.MaxValue, at = 0.0;
		for (int i = 0; i + 1 < road.Count; i++)
		{
			double sx = road.X[i + 1] - road.X[i], sz = road.Z[i + 1] - road.Z[i];
			double len2 = sx * sx + sz * sz;
			double f = len2 > 0.0 ? Math.Max(0.0, Math.Min(1.0, ((x - road.X[i]) * sx + (z - road.Z[i]) * sz) / len2)) : 0.0;
			double px = road.X[i] + sx * f - x, pz = road.Z[i] + sz * f - z;
			if (px * px + pz * pz < best)
			{
				best = px * px + pz * pz;
				piece = i;
				at = f;
			}
		}
		Network.Point(road, piece, at, 0.0, 0.0, out double _, out double _, out double y);
		return y;
	}

	/// <summary>Whether lot-frame (u, v) is on one of the things standing on a lot's open surface (as Buildings lays them out), with a unit to spare.</summary>
	private static bool Obstacle(Town.Lot lot, double u, double v)
	{
		bool In(double cu, double cv, double w, double d)
		{
			return Math.Abs(u - cu) <= w * 0.5 + 1.0 && Math.Abs(v - cv) <= d * 0.5 + 1.0;
		}
		switch (lot.Kind)
		{
			case LotKind.Garage:
				return In(-7.0, 20.0, 3.0, 1.4) || In(7.0, 20.0, 3.0, 1.4) || In(-14.0, 13.0, 1.0, 1.0) || In(14.0, 13.0, 1.0, 1.0) || In(-14.0, 27.0, 1.0, 1.0) || In(14.0, 27.0, 1.0, 1.0);
			// The vans, trailers and awnings along the back.
			case LotKind.Service:
				return v >= lot.D - 41.0 - 1.0;
			case LotKind.Paddock:
				return v >= lot.D - 25.0 - 1.0;
			default:
				return false;
		}
	}

	/// <summary>Tile meshes' triangles in world coordinates, by tile, with each triangle's kind: 0 paving, 1 verge, 2 sidewalk, 3 track or path.</summary>
	private static readonly Dictionary<long, List<(Vector3 a, Vector3 b, Vector3 c, int kind)>> tileTris = new Dictionary<long, List<(Vector3, Vector3, Vector3, int)>>();

	private static List<(Vector3 a, Vector3 b, Vector3 c, int kind)> TileTris(int tx, int tz)
	{
		long key = ((long)tx << 32) | (uint)tz;
		if (tileTris.TryGetValue(key, out List<(Vector3, Vector3, Vector3, int)> found))
		{
			return found;
		}
		List<(Vector3, Vector3, Vector3, int)> list = new List<(Vector3, Vector3, Vector3, int)>();
		RoadMeshData road = RoadMesh.Build(tx, tz, 250f, Terrain, out RoadMeshData verges, out RoadMeshData sidewalks, out RoadMeshData tracks);
		Vector3 o = new Vector3(tx * 250f, 0f, tz * 250f);
		int kind = 0;
		foreach (RoadMeshData d in new[] { road, verges, sidewalks, tracks })
		{
			if (d != null)
			{
				for (int m = 0; m < d.Triangles.Length; m++)
				{
					// A covered bridge's walls and roof stand over its deck: only the surfaces are looked at.
					if (d == road && m >= RoadMesh.Boards)
					{
						continue;
					}
					int[] t = d.Triangles[m];
					for (int k = 0; k < t.Length; k += 3)
					{
						list.Add((d.Vertices[t[k]] + o, d.Vertices[t[k + 1]] + o, d.Vertices[t[k + 2]] + o, kind));
					}
				}
			}
			kind++;
		}
		tileTris[key] = list;
		return list;
	}

	/// <summary>
	/// The meshes round (cx, cz) drawn from above, <paramref name="px"/> pixels a side over <paramref name="span"/>: per pixel the kind
	/// of the topmost triangle over the drawn ground (-1 for the ground), and its height.
	/// </summary>
	private static void Raster(double cx, double cz, double span, int px, int[] kind, float[] top)
	{
		double x0 = cx - span * 0.5, z0 = cz - span * 0.5, step = span / px;
		for (int k = 0; k < kind.Length; k++)
		{
			kind[k] = -1;
			top[k] = float.NegativeInfinity;
		}
		for (int tz = (int)Math.Floor(z0 / 250.0) - 1; tz <= (int)Math.Floor((z0 + span) / 250.0) + 1; tz++)
		{
			for (int tx = (int)Math.Floor(x0 / 250.0) - 1; tx <= (int)Math.Floor((x0 + span) / 250.0) + 1; tx++)
			{
				foreach ((Vector3 a, Vector3 b, Vector3 c, int tk) in TileTris(tx, tz))
				{
					double minx = Math.Min(a.x, Math.Min(b.x, c.x)), maxx = Math.Max(a.x, Math.Max(b.x, c.x));
					double minz = Math.Min(a.z, Math.Min(b.z, c.z)), maxz = Math.Max(a.z, Math.Max(b.z, c.z));
					if (maxx < x0 || minx > x0 + span || maxz < z0 || minz > z0 + span)
					{
						continue;
					}
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
							if (u < -1e-6 || v < -1e-6 || u + v > 1.0 + 1e-6)
							{
								continue;
							}
							float y = (float)(a.y + (b.y - a.y) * u + (c.y - a.y) * v);
							int p = j * px + i;
							if (y > top[p])
							{
								top[p] = y;
								kind[p] = tk;
							}
						}
					}
				}
			}
		}
		for (int j = 0; j < px; j++)
		{
			for (int i = 0; i < px; i++)
			{
				int p = j * px + i;
				if (kind[p] >= 0 && top[p] < Mesh(x0 + (i + 0.5) * step, z0 + (j + 0.5) * step) + Network.Lift - 0.005)
				{
					kind[p] = -1;
				}
			}
		}
	}

	/// <summary>Where junctions are: each paved road's ends and the middles of its crossings, one a 20-unit square.</summary>
	/// <summary>Whether a road is cut out whole (a dead end lying along another road: <c>Network.DeadEnds</c>), so nothing of it is drawn.</summary>
	private static bool Gone(Network.Road road)
	{
		double length = road.S[road.Count - 1];
		foreach (Network.Gap g in road.Gaps)
		{
			if (Math.Min(g.L0, g.R0) < 0.0 && Math.Max(g.L1, g.R1) > length)
			{
				return true;
			}
		}
		return false;
	}

	private static List<(double x, double z)> JunctionSpots()
	{
		List<(double x, double z)> spots = new List<(double, double)>();
		HashSet<long> taken = new HashSet<long>();
		void Add(double x, double z)
		{
			if (taken.Add(((long)Math.Floor(x / 20.0) << 32) | (uint)(int)Math.Floor(z / 20.0)))
			{
				spots.Add((x, z));
			}
		}
		foreach (Network.Road road in Network.Roads)
		{
			if (Network.Unpaved(road.Kind) || road.Kind > RoadKind.Lane || Gone(road))
			{
				continue;
			}
			double length = road.S[road.Count - 1];
			Add(road.X[0], road.Z[0]);
			Add(road.X[road.Count - 1], road.Z[road.Count - 1]);
			foreach (Network.Gap g in road.Gaps)
			{
				if (g.C0 > 0.0 && g.C1 < length)
				{
					Network.Locate(road, (g.C0 + g.C1) * 0.5, out int i, out double t);
					Add(road.X[i] + (road.X[i + 1] - road.X[i]) * t, road.Z[i] + (road.Z[i + 1] - road.Z[i]) * t);
				}
			}
		}
		return spots;
	}

	/// <summary>Whether a tunnel mouth into the world's edge is within <paramref name="reach"/> of (x, z): its road ends in the wall there.</summary>
	private static bool NearPortal(double x, double z, double reach)
	{
		foreach (Network.Portal p in Network.Portals)
		{
			if (Math.Abs(p.X - x) < reach + p.Half && Math.Abs(p.Z - z) < reach + p.Half)
			{
				return true;
			}
		}
		return false;
	}

	private static bool Holes()
	{
		const double span = 60.0;
		const int px = 120;
		int[] kind = new int[px * px];
		float[] top = new float[px * px];
		bool[] paving = new bool[px * px];
		long nominal = 0, holes = 0;
		int worstHoles = 0;
		(double x, double z) worstAt = (0.0, 0.0);
		List<(double x, double z)> spots = JunctionSpots();
		foreach ((double cx, double cz) in spots)
		{
			if (NearPortal(cx, cz, span))
			{
				continue;
			}
			Raster(cx, cz, span, px, kind, top);
			double x0 = cx - span * 0.5, z0 = cz - span * 0.5, step = span / px;
			Array.Clear(paving, 0, paving.Length);
			// Each road's own strip: within its half width (half a unit in) of a piece, beside it (not past its ends, nor round its
			// samples' corners).
			foreach (Network.Road road in Network.Roads)
			{
				if (Network.Unpaved(road.Kind) || Gone(road))
				{
					continue;
				}
				for (int i = 0; i + 1 < road.Count; i++)
				{
					double ax = road.X[i], az = road.Z[i], sx = road.X[i + 1] - ax, sz = road.Z[i + 1] - az;
					if (Math.Max(ax, ax + sx) + road.Half < x0 || Math.Min(ax, ax + sx) - road.Half > x0 + span || Math.Max(az, az + sz) + road.Half < z0 || Math.Min(az, az + sz) - road.Half > z0 + span)
					{
						continue;
					}
					double len2 = sx * sx + sz * sz;
					if (len2 < 1e-9)
					{
						continue;
					}
					int i0 = Math.Max(0, (int)((Math.Min(ax, ax + sx) - road.Half - x0) / step)), i1 = Math.Min(px - 1, (int)((Math.Max(ax, ax + sx) + road.Half - x0) / step));
					int j0 = Math.Max(0, (int)((Math.Min(az, az + sz) - road.Half - z0) / step)), j1 = Math.Min(px - 1, (int)((Math.Max(az, az + sz) + road.Half - z0) / step));
					for (int j = j0; j <= j1; j++)
					{
						double z = z0 + (j + 0.5) * step;
						for (int k = i0; k <= i1; k++)
						{
							double x = x0 + (k + 0.5) * step;
							double t = ((x - ax) * sx + (z - az) * sz) / len2;
							if (t < 0.0 || t > 1.0)
							{
								continue;
							}
							double dx = ax + sx * t - x, dz = az + sz * t - z;
							if (dx * dx + dz * dz < (road.Half - 0.5) * (road.Half - 0.5))
							{
								paving[j * px + k] = true;
							}
						}
					}
				}
			}
			int here = 0;
			for (int p = 0; p < paving.Length; p++)
			{
				if (!paving[p])
				{
					continue;
				}
				nominal++;
				if (kind[p] != 0)
				{
					holes++;
					here++;
				}
			}
			if (here > worstHoles)
			{
				worstHoles = here;
				worstAt = (cx, cz);
			}
		}
		Console.WriteLine("holes:   " + spots.Count + " junctions; " + holes + " of " + nominal + " pixels of the roads' paving with no paved surface on top (" + (100.0 * holes / Math.Max(1, nominal)).ToString("0.00") + "%), the most " + worstHoles + " at (" + worstAt.x.ToString("0") + ", " + worstAt.z.ToString("0") + ")");
		return holes * 500 < nominal && worstHoles < 400;
	}

	private static bool Joints()
	{
		int joints = 0, apart = 0;
		double worst = 0.0;
		List<(int r, int end)> joined = new List<(int, int)>();
		for (int r = 0; r < Network.Roads.Count; r++)
		{
			Network.Road road = Network.Roads[r];
			if (road.Joined0)
			{
				joined.Add((r, 0));
			}
			if (road.Joined1)
			{
				joined.Add((r, 1));
			}
		}
		foreach ((int r, int end) in joined)
		{
			Network.Road a = Network.Roads[r];
			int ka = end == 0 ? 0 : a.Count - 1;
			foreach ((int o, int eo) in joined)
			{
				Network.Road b = Network.Roads[o];
				int kb = eo == 0 ? 0 : b.Count - 1;
				if (o <= r || Math.Abs(a.X[ka] - b.X[kb]) > 0.5 || Math.Abs(a.Z[ka] - b.Z[kb]) > 0.5)
				{
					continue;
				}
				joints++;
				double bad = 0.0;
				for (int side = -1; side <= 1; side += 2)
				{
					Network.Edge(a, a.S[ka], side, 0.0, out double ax, out double az, out double ay);
					// The same edge of b: its left if it runs on the way a does (one's end on the other's start), else its right.
					int bs = end != eo ? side : -side;
					Network.Edge(b, b.S[kb], bs, 0.0, out double bx, out double bz, out double by);
					bad = Math.Max(bad, Math.Max(Math.Sqrt((ax - bx) * (ax - bx) + (az - bz) * (az - bz)), Math.Abs(ay - by)));
				}
				worst = Math.Max(worst, bad);
				if (bad > 0.02)
				{
					apart++;
					Console.WriteLine("  '" + a.Name + "' and '" + b.Name + "' apart by " + bad.ToString("0.000") + " at (" + a.X[ka].ToString("0") + ", " + a.Z[ka].ToString("0") + ")");
				}
			}
		}
		Console.WriteLine("joints:  " + joints + " roads carrying on end to end; " + apart + " whose ends' edges are apart (points or heights), worst " + worst.ToString("0.000"));
		return apart == 0;
	}

	private static bool HubChecks()
	{
		int arms = 0, apart = 0, samples = 0, bare = 0, under = 0;
		double worstApart = 0.0, worstUnder = 0.0;
		foreach (Network.Hub hub in Network.Hubs)
		{
			arms += hub.Arm.Length;
			// The outline's points at each arm's cut, against the arm's drawn edge there (where its ribbon stops).
			for (int k = 0; k < hub.PX.Length; k++)
			{
				int i = hub.VArm[k];
				if (i < 0 || hub.VU[k] != hub.Cut[i])
				{
					continue;
				}
				Network.Road road = Network.Roads[hub.Arm[i]];
				Network.Edge(road, hub.At[i] + hub.Dir[i] * hub.Cut[i], hub.Dir[i] * hub.VSide[k], 0.0, out double x, out double z, out double y);
				double bad = Math.Max(Math.Sqrt((x - hub.PX[k]) * (x - hub.PX[k]) + (z - hub.PZ[k]) * (z - hub.PZ[k])), Math.Abs(y - hub.PY[k]));
				worstApart = Math.Max(worstApart, bad);
				if (bad > 0.02)
				{
					apart++;
				}
			}
			// Points over each of the plate's triangles: paved in the tiles' meshes, the drawn ground under them. (A sliver along an
			// arm's edge, under a twentieth of a square unit, has nothing to show.)
			int[] t = hub.Tris;
			for (int k = 0; k + 2 < t.Length; k += 3)
			{
				double area = 0.5 * Math.Abs((hub.PX[t[k + 1]] - hub.PX[t[k]]) * (hub.PZ[t[k + 2]] - hub.PZ[t[k]]) - (hub.PX[t[k + 2]] - hub.PX[t[k]]) * (hub.PZ[t[k + 1]] - hub.PZ[t[k]]));
				if (area < 0.05)
				{
					continue;
				}
				foreach ((double wa, double wb) in new[] { (1.0 / 3.0, 1.0 / 3.0), (0.7, 0.15), (0.15, 0.7), (0.15, 0.15) })
				{
					double wc = 1.0 - wa - wb;
					double x = hub.PX[t[k]] * wa + hub.PX[t[k + 1]] * wb + hub.PX[t[k + 2]] * wc;
					double z = hub.PZ[t[k]] * wa + hub.PZ[t[k + 1]] * wb + hub.PZ[t[k + 2]] * wc;
					double y = hub.PY[t[k]] * wa + hub.PY[t[k + 1]] * wb + hub.PY[t[k + 2]] * wc + Network.Lift;
					samples++;
					if (!Paved(x, z))
					{
						bare++;
					}
					double over = Terrain.At(x, z) - y;
					worstUnder = Math.Max(worstUnder, over);
					if (over > 0.0)
					{
						under++;
					}
				}
			}
		}
		Console.WriteLine("hubs:    " + Network.Hubs.Count + " places where three or more roads end (" + arms + " arms); " + apart + " cut points off the plate's outline (worst " + worstApart.ToString("0.000") + "); of " + samples + " plate points " + bare + " not paved, " + under + " with the ground over them (worst " + worstUnder.ToString("0.00") + ")");
		return apart == 0 && bare == 0 && under == 0;
	}

	/// <summary>Whether the paving triangles of the tiles round (x, z) cover it.</summary>
	private static bool Paved(double x, double z)
	{
		int tx0 = (int)Math.Floor(x / 250.0), tz0 = (int)Math.Floor(z / 250.0);
		for (int tz = tz0 - 1; tz <= tz0 + 1; tz++)
		{
			for (int tx = tx0 - 1; tx <= tx0 + 1; tx++)
			{
				foreach ((Vector3 a, Vector3 b, Vector3 c, int kind) in TileTris(tx, tz))
				{
					if (kind != 0)
					{
						continue;
					}
					double det = (b.x - a.x) * (c.z - a.z) - (c.x - a.x) * (b.z - a.z);
					if (Math.Abs(det) < 1e-9)
					{
						continue;
					}
					double u = ((x - a.x) * (c.z - a.z) - (c.x - a.x) * (z - a.z)) / det;
					double v = ((b.x - a.x) * (z - a.z) - (x - a.x) * (b.z - a.z)) / det;
					if (u >= 0.0 && v >= 0.0 && u + v <= 1.0)
					{
						return true;
					}
				}
			}
		}
		return false;
	}

	private static bool DeadEnds()
	{
		int paved = 0, unpaved = 0, bad = 0;
		foreach (Network.Road road in Network.Roads)
		{
			for (int end = 0; end < 2; end++)
			{
				if (!(end == 0 ? road.Dead0 : road.Dead1))
				{
					continue;
				}
				int k = end == 0 ? 0 : road.Count - 1;
				if (Network.Unpaved(road.Kind))
				{
					unpaved++;
					if (Network.HalfAt(road, road.S[k]) > road.Half * 0.21)
					{
						bad++;
					}
					continue;
				}
				paved++;
				// Out from the end along the road's way there.
				int nk = end == 0 ? 1 : road.Count - 2;
				double dx = road.X[k] - road.X[nk], dz = road.Z[k] - road.Z[nk], len = Math.Sqrt(dx * dx + dz * dz);
				dx /= len;
				dz /= len;
				double half = Network.HalfAt(road, road.S[k]);
				bool on = Paved(road.X[k] + dx * half * 0.6, road.Z[k] + dz * half * 0.6);
				bool past = Paved(road.X[k] + dx * (half + Network.Verge + 1.0), road.Z[k] + dz * (half + Network.Verge + 1.0)) && !OnOtherRoad(road, road.X[k] + dx * (half + Network.Verge + 1.0), road.Z[k] + dz * (half + Network.Verge + 1.0));
				if (!on || past)
				{
					bad++;
				}
			}
		}
		Console.WriteLine("deadends: " + paved + " paved roads rounded off, " + unpaved + " tracks and paths narrowed; " + bad + " not");
		return bad == 0;
	}

	private static bool Homes()
	{
		int farms = 0, houses = 0, mailBad = 0, houseBad = 0, far = 0;
		foreach (Network.Home home in Network.Homes)
		{
			// Buildings leaves such a mailbox out: a few may be missing, not many.
			if (!Network.Claims(home.MailX, home.MailZ, -1, 0.0, 0.5) || Wet(home.MailX, home.MailZ))
			{
				mailBad++;
			}
			if (!home.House)
			{
				farms++;
				continue;
			}
			houses++;
			double ax = home.DirZ, az = -home.DirX;
			bool bad = false;
			foreach ((double u, double v) in new[] { (-0.5, -0.5), (0.5, -0.5), (-0.5, 0.5), (0.5, 0.5), (0.0, 0.0), (0.0, -0.5), (0.0, 0.5), (-0.5, 0.0), (0.5, 0.0) })
			{
				double x = home.X + ax * u * Network.HomeWidth + home.DirX * v * Network.HomeDepth;
				double z = home.Z + az * u * Network.HomeWidth + home.DirZ * v * Network.HomeDepth;
				bad |= !Network.Claims(x, z, -1, 0.0, 1.5) || Town.Taken(x, z, 2.0) || Parks.At(x, z) || Wet(x, z);
			}
			if (bad)
			{
				houseBad++;
				Console.WriteLine("  house not clear at (" + home.X.ToString("0") + ", " + home.Z.ToString("0") + ")");
			}
			// Its drive ends just in front of it.
			double best = double.MaxValue;
			foreach (Network.Road road in Network.Roads)
			{
				if (road.Kind == RoadKind.Drive)
				{
					int e = road.Count - 1;
					best = Math.Min(best, Math.Sqrt((road.X[e] - home.X) * (road.X[e] - home.X) + (road.Z[e] - home.Z) * (road.Z[e] - home.Z)));
				}
			}
			far += best > Network.HomeDepth * 0.5 + 10.0 ? 1 : 0;
		}
		Console.WriteLine("homes:   " + farms + " farm drives, " + houses + " houses on drives; " + mailBad + " mailboxes left out (on a road or in water), " + houseBad + " houses not clear, " + far + " away from their drive's end");
		return houses > 50 && mailBad * 50 <= Network.Homes.Count && houseBad == 0 && far == 0;
	}

	/// <summary>
	/// Per tile, which surfaces the drawn road meshes put over each unit square (bit 1 a road's paving, 2 a verge, 4 a sidewalk, 8 a
	/// track's, drive's or path's ribbon), from the triangles as the game draws them.
	/// </summary>
	private static byte[] SurfaceMask(int tx, int tz)
	{
		byte[] mask = new byte[250 * 250];
		double x0 = tx * 250.0, z0 = tz * 250.0;
		for (int nz = tz - 1; nz <= tz + 1; nz++)
		{
			for (int nx = tx - 1; nx <= tx + 1; nx++)
			{
				foreach ((Vector3 a, Vector3 b, Vector3 c, int kind) in TopTris(nx, nz))
				{
					double det = (b.x - a.x) * (c.z - a.z) - (c.x - a.x) * (b.z - a.z);
					if (Math.Abs(det) < 1e-9)
					{
						continue;
					}
					int i0 = Math.Max(0, (int)Math.Floor(Math.Min(a.x, Math.Min(b.x, c.x)) - x0)), i1 = Math.Min(249, (int)Math.Ceiling(Math.Max(a.x, Math.Max(b.x, c.x)) - x0));
					int j0 = Math.Max(0, (int)Math.Floor(Math.Min(a.z, Math.Min(b.z, c.z)) - z0)), j1 = Math.Min(249, (int)Math.Ceiling(Math.Max(a.z, Math.Max(b.z, c.z)) - z0));
					for (int j = j0; j <= j1; j++)
					{
						for (int i = i0; i <= i1; i++)
						{
							double x = x0 + i + 0.5, z = z0 + j + 0.5;
							double u = ((x - a.x) * (c.z - a.z) - (c.x - a.x) * (z - a.z)) / det;
							double v = ((b.x - a.x) * (z - a.z) - (x - a.x) * (b.z - a.z)) / det;
							if (u >= 0.0 && v >= 0.0 && u + v <= 1.0)
							{
								mask[j * 250 + i] |= (byte)(1 << kind);
							}
						}
					}
				}
			}
		}
		return mask;
	}

	private static readonly Dictionary<long, List<(Vector3 a, Vector3 b, Vector3 c, int kind)>> topTris = new Dictionary<long, List<(Vector3, Vector3, Vector3, int)>>();

	/// <summary>As <see cref="TileTris"/>, but a sidewalk's kerb (the strip down under the ground at its back, all its corners at the texture's kerb) left out: it is not seen.</summary>
	private static List<(Vector3 a, Vector3 b, Vector3 c, int kind)> TopTris(int tx, int tz)
	{
		long key = ((long)tx << 32) | (uint)tz;
		if (topTris.TryGetValue(key, out List<(Vector3, Vector3, Vector3, int)> found))
		{
			return found;
		}
		List<(Vector3, Vector3, Vector3, int)> list = new List<(Vector3, Vector3, Vector3, int)>();
		RoadMeshData road = RoadMesh.Build(tx, tz, 250f, Terrain, out RoadMeshData verges, out RoadMeshData sidewalks, out RoadMeshData tracks);
		Vector3 o = new Vector3(tx * 250f, 0f, tz * 250f);
		int kind = 0;
		foreach (RoadMeshData d in new[] { road, verges, sidewalks, tracks })
		{
			if (d != null)
			{
				for (int m = 0; m < d.Triangles.Length; m++)
				{
					if (d == road && m >= RoadMesh.Boards)
					{
						continue;
					}
					int[] t = d.Triangles[m];
					for (int k = 0; k < t.Length; k += 3)
					{
						if (d == sidewalks && d.Uv[t[k]].x >= 0.89f && d.Uv[t[k + 1]].x >= 0.89f && d.Uv[t[k + 2]].x >= 0.89f)
						{
							continue;
						}
						list.Add((d.Vertices[t[k]] + o, d.Vertices[t[k + 1]] + o, d.Vertices[t[k + 2]] + o, kind));
					}
				}
			}
			kind++;
		}
		topTris[key] = list;
		return list;
	}

	/// <summary>
	/// Every house, wing, tower and church (<see cref="Buildings.Data.Boxes"/>) in the towns, villages, farms and drives' homes against
	/// the drawn road meshes: none of its footprint (half a unit inside its walls, sampled every unit; a sidewalk's kerb, under the ground, aside) on a road's paving, a sidewalk or a
	/// track's, drive's or path's ribbon. Buildings tested only at their corners and sides' middles let a lane through between them.
	/// </summary>
	private static bool ClearOfRoads()
	{
		HashSet<long> tiles = new HashSet<long>();
		foreach (Places.Place p in Places.All)
		{
			double reach = p.Kind == PlaceKind.Hamlet ? 120.0 : p.Radius + 120.0;
			for (int tz = (int)Math.Floor((p.Z - reach) / 250.0); tz <= (int)Math.Floor((p.Z + reach) / 250.0); tz++)
			{
				for (int tx = (int)Math.Floor((p.X - reach) / 250.0); tx <= (int)Math.Floor((p.X + reach) / 250.0); tx++)
				{
					tiles.Add(((long)tx << 32) | (uint)tz);
				}
			}
		}
		foreach (Network.Home home in Network.Homes)
		{
			tiles.Add(((long)Math.Floor(home.X / 250.0) << 32) | (uint)(int)Math.Floor(home.Z / 250.0));
		}
		int n = 0, onRoad = 0, onWalk = 0, onTrack = 0, shown = 0;
		List<long> sorted = tiles.OrderBy(t => t).ToList();
		foreach (long key in sorted)
		{
			int tx = (int)(key >> 32), tz = (int)(uint)key;
			Buildings.Data d = Buildings.Build(tx, tz, 250f);
			if (d.Boxes.Count == 0)
			{
				continue;
			}
			Dictionary<long, byte[]> masks = new Dictionary<long, byte[]>();
			foreach ((double cx, double cz, double dx, double dz, double w, double dd) in d.Boxes)
			{
				n++;
				int bits = 0;
				double nx = -dz, nz = dx;
				for (double u = -w * 0.5 + 0.5; u <= w * 0.5 - 0.5 + 1e-9; u += Math.Max(0.5, (w - 1.0) / Math.Ceiling(w - 1.0)))
				{
					for (double v = -dd * 0.5 + 0.5; v <= dd * 0.5 - 0.5 + 1e-9; v += Math.Max(0.5, (dd - 1.0) / Math.Ceiling(dd - 1.0)))
					{
						double x = cx + dx * u + nx * v, z = cz + dz * u + nz * v;
						int mx = (int)Math.Floor(x / 250.0), mz = (int)Math.Floor(z / 250.0);
						long mk = ((long)mx << 32) | (uint)mz;
						if (!masks.TryGetValue(mk, out byte[] mask))
						{
							masks[mk] = mask = SurfaceMask(mx, mz);
						}
						int i = Math.Min(249, (int)Math.Floor(x - mx * 250.0)), j = Math.Min(249, (int)Math.Floor(z - mz * 250.0));
						bits |= mask[j * 250 + i];
					}
				}
				onRoad += (bits & 1) != 0 ? 1 : 0;
				onWalk += (bits & 1) == 0 && (bits & 4) != 0 ? 1 : 0;
				onTrack += (bits & 5) == 0 && (bits & 8) != 0 ? 1 : 0;
				if ((bits & 13) != 0 && shown++ < 30)
				{
					Console.WriteLine("  building on " + ((bits & 1) != 0 ? "a road" : (bits & 4) != 0 ? "a sidewalk" : "a track") + " at (" + cx.ToString("0") + ", " + cz.ToString("0") + "), " + w.ToString("0") + " by " + dd.ToString("0"));
				}
			}
		}
		Console.WriteLine("clear:   " + n + " houses, wings, towers and churches on " + tiles.Count + " tiles; " + onRoad + " on a road's paving, " + onWalk + " on a sidewalk, " + onTrack + " on a track, drive or path");
		return onRoad == 0 && onWalk == 0 && onTrack == 0;
	}

	/// <summary>How deep the drawn water is over the drawn ground at (x, z) (negative: dry by that much).</summary>
	private static double DeerDepth(double x, double z)
	{
		double w = Rivers.DrawnWater(x, z);
		return double.IsNaN(w) ? -100.0 : w + Network.Lift - Mesh(x, z);
	}

	/// <summary>Whether (x, z) is on any road's paving (or a track's or path's ribbon), and which road's.</summary>
	private static bool OnAnyPaving(double x, double z, out RoadKind kind)
	{
		for (kind = RoadKind.Trunk; kind <= RoadKind.Rally; kind++)
		{
			if (Network.Nearest(x, z, 60.0, kind, kind) < Network.HalfOf(kind))
			{
				return true;
			}
		}
		return false;
	}

	/// <summary>
	/// The buck's antlers: their faces and indices, all on the head bone, and in every pose (posed in the model's frame as the
	/// exporter writes them) clear of the ground and of every vertex of the body not carried by the head (ears, neck, back, legs).
	/// </summary>
	private static bool AntlerChecks()
	{
		int n = DeerModel.Region.Length, a = DeerModel.AntlerShade.Length, bones = DeerModel.BoneNames.Length, badFaces = 0;
		bool shadeOk = true;
		for (int i = 0; i < a; i++)
		{
			shadeOk &= DeerModel.AntlerShade[i] >= 0f && DeerModel.AntlerShade[i] <= 1f;
		}
		Vector3 AP(int i) => new Vector3(DeerModel.AntlerPositions[i * 3], DeerModel.AntlerPositions[i * 3 + 1], DeerModel.AntlerPositions[i * 3 + 2]);
		Vector3 AN(int i) => new Vector3(DeerModel.AntlerNormals[i * 3], DeerModel.AntlerNormals[i * 3 + 1], DeerModel.AntlerNormals[i * 3 + 2]);
		for (int t = 0; t < DeerModel.AntlerTriangles.Length; t += 3)
		{
			int i0 = DeerModel.AntlerTriangles[t], i1 = DeerModel.AntlerTriangles[t + 1], i2 = DeerModel.AntlerTriangles[t + 2];
			if (i0 >= a || i1 >= a || i2 >= a || i0 == i1 || i1 == i2 || i0 == i2)
			{
				badFaces++;
				continue;
			}
			Vector3 normal = Vector3.Cross(AP(i1) - AP(i0), AP(i2) - AP(i0));
			badFaces += Vector3.Dot(normal, AN(i0) + AN(i1) + AN(i2)) <= 0f ? 1 : 0;
		}
		int head = Array.IndexOf(DeerModel.BoneNames, "head");
		// Each pose: the bones' world rotations and places (Rx.Ry.Rz, absolute local rotations, bind rotations zero).
		int poses = DeerModel.PoseNames.Length, worstPose = 0;
		double lowest = double.MaxValue, nearest = double.MaxValue;
		int nearestPose = 0;
		for (int p = 0; p < poses; p++)
		{
			var rot = new double[bones][];
			var at = new double[bones][];
			for (int b = 0; b < bones; b++)
			{
				int o = (p * bones + b) * 3;
				double[] local = Mul(Mul(Rot(0, DeerModel.Poses[o]), Rot(1, DeerModel.Poses[o + 1])), Rot(2, DeerModel.Poses[o + 2]));
				double[] off = { DeerModel.BoneOffsets[b * 3], DeerModel.BoneOffsets[b * 3 + 1], DeerModel.BoneOffsets[b * 3 + 2] };
				int par = DeerModel.BoneParents[b];
				if (par < 0)
				{
					rot[b] = local;
					at[b] = off;
				}
				else
				{
					rot[b] = Mul(rot[par], local);
					double[] w = Apply(rot[par], off);
					at[b] = new[] { at[par][0] + w[0], at[par][1] + w[1], at[par][2] + w[2] };
				}
			}
			// The bind places, to take each vertex into its bones' frames.
			var bind = new double[bones][];
			for (int b = 0; b < bones; b++)
			{
				int par = DeerModel.BoneParents[b];
				double[] off = { DeerModel.BoneOffsets[b * 3], DeerModel.BoneOffsets[b * 3 + 1], DeerModel.BoneOffsets[b * 3 + 2] };
				bind[b] = par < 0 ? off : new[] { bind[par][0] + off[0], bind[par][1] + off[1], bind[par][2] + off[2] };
			}
			double[] Posed(int bone, double x, double y, double z)
			{
				double[] q = Apply(rot[bone], new[] { x - bind[bone][0], y - bind[bone][1], z - bind[bone][2] });
				return new[] { q[0] + at[bone][0], q[1] + at[bone][1], q[2] + at[bone][2] };
			}
			var body = new List<double[]>();
			for (int i = 0; i < n; i++)
			{
				double headWeight = 0.0;
				double[] sum = new double[3];
				for (int j = 0; j < 4; j++)
				{
					int b = DeerModel.BoneIndex[i * 4 + j];
					double w = DeerModel.BoneWeight[i * 4 + j];
					if (w <= 0.0)
					{
						continue;
					}
					headWeight += b == head ? w : 0.0;
					double[] q = Posed(b, DeerModel.Positions[i * 3], DeerModel.Positions[i * 3 + 1], DeerModel.Positions[i * 3 + 2]);
					sum[0] += w * q[0];
					sum[1] += w * q[1];
					sum[2] += w * q[2];
				}
				if (headWeight < 0.98)
				{
					body.Add(sum);
				}
			}
			// The pedicles (where the antlers grow from the skull) are left out: within 7 cm of the beams' bases.
			double px = Math.Abs(DeerModel.AntlerPositions[0]), py = DeerModel.AntlerPositions[1], pz = DeerModel.AntlerPositions[2];
			for (int i = 0; i < a; i++)
			{
				double x = DeerModel.AntlerPositions[i * 3], y = DeerModel.AntlerPositions[i * 3 + 1], z = DeerModel.AntlerPositions[i * 3 + 2];
				double[] q = Posed(DeerModel.AntlerBone, x, y, z);
				if (q[1] < lowest)
				{
					lowest = q[1];
					worstPose = p;
				}
				double dpx = Math.Abs(x) - px, dpy = y - py, dpz = z - pz;
				if (dpx * dpx + dpy * dpy + dpz * dpz < 0.07 * 0.07)
				{
					continue;
				}
				foreach (double[] v in body)
				{
					double dx = v[0] - q[0], dy = v[1] - q[1], dz = v[2] - q[2];
					double d2 = dx * dx + dy * dy + dz * dz;
					if (d2 < nearest)
					{
						nearest = d2;
						nearestPose = p;
					}
				}
			}
		}
		nearest = Math.Sqrt(nearest);
		bool ok = badFaces == 0 && shadeOk && DeerModel.AntlerBone == head && lowest > 0.03 && nearest > 0.003;
		Console.WriteLine("         the buck's antlers: " + a + " vertices, " + DeerModel.AntlerTriangles.Length / 3 + " triangles, " + badFaces + " broken or flipped faces, on the " + DeerModel.BoneNames[DeerModel.AntlerBone]
			+ " bone; their lowest point " + lowest.ToString("0.00") + " m above the hooves (" + DeerModel.PoseNames[worstPose] + "), at least " + (nearest * 100.0).ToString("0.0") + " cm from the body (" + DeerModel.PoseNames[nearestPose] + ")" + (ok ? "" : ": FAIL"));
		return ok;
	}

	private static double[] Rot(int axis, double angle)
	{
		double c = Math.Cos(angle), s = Math.Sin(angle);
		return axis == 0 ? new[] { 1, 0, 0, 0, c, -s, 0, s, c } : axis == 1 ? new[] { c, 0, s, 0, 1, 0, -s, 0, c } : new[] { c, -s, 0, s, c, 0, 0, 0, 1 };
	}

	private static double[] Mul(double[] m, double[] k)
	{
		double[] r = new double[9];
		for (int i = 0; i < 3; i++)
		{
			for (int j = 0; j < 3; j++)
			{
				r[i * 3 + j] = m[i * 3] * k[j] + m[i * 3 + 1] * k[3 + j] + m[i * 3 + 2] * k[6 + j];
			}
		}
		return r;
	}

	private static double[] Apply(double[] m, double[] v)
	{
		return new[] { m[0] * v[0] + m[1] * v[1] + m[2] * v[2], m[3] * v[0] + m[4] * v[1] + m[5] * v[2], m[6] * v[0] + m[7] * v[1] + m[8] * v[2] };
	}

	/// <summary>
	/// The fawns over 20 race seeds: few (about <see cref="Herds.FawnChance"/> of the families), only the last deer of a family, never a
	/// buck, a fawn's size; and drawn from a hash of their own, so every deer that is not a fawn keeps the size the group's draws gave it
	/// (the young 0.72 to 1, a family's doe 0.95 to 1.05, bucks 1 to 1.12).
	/// </summary>
	private static bool FawnChecks()
	{
		int fawns = 0, families = 0, wrong = 0, groups = 0;
		for (int seed = 1; seed <= 20; seed++)
		{
			Herds herds = new Herds(seed, Sky0(seed), 2400.0);
			foreach (Herds.Group g in herds.Groups)
			{
				groups++;
				families += g.Kind == "family" ? 1 : 0;
				for (int d = 0; d < g.Members.Length; d++)
				{
					Herds.Member m = g.Members[d];
					if (m.Fawn)
					{
						fawns++;
						wrong += g.Kind != "family" || d != g.Members.Length - 1 || m.Buck || m.Scale < Herds.FawnSmallest - 1e-9 || m.Scale > Herds.FawnLargest + 1e-9 ? 1 : 0;
					}
					else
					{
						wrong += m.Buck ? (m.Scale < 1.0 || m.Scale > 1.12 ? 1 : 0) : d == 0 ? (m.Scale < 0.72 || m.Scale > 1.05 ? 1 : 0) : (m.Scale < 0.72 || m.Scale > 1.0 ? 1 : 0);
					}
				}
			}
		}
		double share = families > 0 ? (double)fawns / families : 0.0;
		bool ok = wrong == 0 && fawns > 0 && Math.Abs(share - Herds.FawnChance) < 0.02;
		Console.WriteLine("         fawns over 20 races: " + fawns + " in " + families + " families (" + (share * 100.0).ToString("0.0") + "%; " + (fawns / 20.0).ToString("0.0") + " a race in " + groups / 20 + " groups), "
			+ wrong + " deer of the wrong kind or size" + (ok ? "" : ": FAIL"));
		return ok;
	}

	private static bool DeerChecks()
	{
		bool ok = true;
		// The mesh.
		int n = DeerModel.Region.Length, bones = DeerModel.BoneNames.Length, badWeights = 0, badFaces = 0, badIndex = 0, slivers = 0;
		for (int i = 0; i < n; i++)
		{
			float sum = 0f;
			for (int j = 0; j < 4; j++)
			{
				sum += DeerModel.BoneWeight[i * 4 + j];
				badIndex += DeerModel.BoneIndex[i * 4 + j] >= bones ? 1 : 0;
			}
			badWeights += Math.Abs(sum - 1f) > 0.01f ? 1 : 0;
		}
		for (int t = 0; t < DeerModel.Triangles.Length; t += 3)
		{
			int a = DeerModel.Triangles[t], b = DeerModel.Triangles[t + 1], c = DeerModel.Triangles[t + 2];
			if (a >= n || b >= n || c >= n || a == b || b == c || a == c)
			{
				badFaces++;
				continue;
			}
			Vector3 pa = new Vector3(DeerModel.Positions[a * 3], DeerModel.Positions[a * 3 + 1], DeerModel.Positions[a * 3 + 2]);
			Vector3 pb = new Vector3(DeerModel.Positions[b * 3], DeerModel.Positions[b * 3 + 1], DeerModel.Positions[b * 3 + 2]);
			Vector3 pc = new Vector3(DeerModel.Positions[c * 3], DeerModel.Positions[c * 3 + 1], DeerModel.Positions[c * 3 + 2]);
			Vector3 normal = Vector3.Cross(pb - pa, pc - pa);
			Vector3 vn = new Vector3(DeerModel.Normals[a * 3] + DeerModel.Normals[b * 3] + DeerModel.Normals[c * 3], DeerModel.Normals[a * 3 + 1] + DeerModel.Normals[b * 3 + 1] + DeerModel.Normals[c * 3 + 1], DeerModel.Normals[a * 3 + 2] + DeerModel.Normals[b * 3 + 2] + DeerModel.Normals[c * 3 + 2]);
			// A face wound against its vertices' normals (flipped): the decimation leaves a few slivers in the creases, a few square
			// centimetres (under 20 cm², a fifth of a unit across in the game), which are let be; a bigger one is broken.
			if (Vector3.Dot(normal, vn) <= 0f)
			{
				if (normal.magnitude * 0.5f < 0.002f)
				{
					slivers++;
				}
				else
				{
					badFaces++;
				}
			}
		}
		for (int b = 0; b < bones; b++)
		{
			badIndex += DeerModel.BoneParents[b] >= b ? 1 : 0;
		}
		Console.WriteLine("deer:    the mesh: " + n + " vertices, " + DeerModel.Triangles.Length / 3 + " triangles, " + bones + " bones; " + badWeights + " vertices whose weights do not sum to one, " + badFaces + " broken or flipped faces (and " + slivers + " flipped slivers under 20 cm²), " + badIndex + " indices out of range");
		ok &= badWeights == 0 && badFaces == 0 && badIndex == 0 && slivers * 100 < DeerModel.Triangles.Length / 3;

		ok &= AntlerChecks();
		ok &= FawnChecks();

		DeerSites.Ensure();
		int notOnRoad = 0;
		foreach (DeerSites.Site site in DeerSites.All)
		{
			Network.Road road = Network.Roads[site.Road];
			Lines.Project(road.X, road.Z, site.I, site.X, site.Z, out double _, out double d);
			notOnRoad += d > 0.5 || road.Kind > RoadKind.Lane ? 1 : 0;
		}
		Console.WriteLine("         " + DeerSites.All.Count + " places a group could live, " + notOnRoad + " whose crossing is not on a lane or secondary road");
		ok &= notOnRoad == 0 && DeerSites.All.Count > 200;

		var legs = new List<(double x, double z)>();
		for (int seed = 1; seed <= 3; seed++)
		{
			Herds herds = new Herds(seed, Sky0(seed), 2400.0);
			int spotsBad = 0, legsBad = 0, deep = 0, paved = 0, samples = 0;
			double worstDeep = -100.0, worstOff = 0.0, sumOff = 0.0;
			foreach (Herds.Group g in herds.Groups)
			{
				foreach (Herds.Spot s in g.Spots)
				{
					if (DeerDepth(s.X, s.Z) > Herds.Belly - 0.5 || OnAnyPaving(s.X, s.Z, out RoadKind _))
					{
						spotsBad++;
						Console.WriteLine("  seed " + seed + " group " + g.Index + ": a spot wet or on a road at (" + s.X.ToString("0") + ", " + s.Z.ToString("0") + ")");
					}
				}
				for (int a = 0; a < g.Spots.Length; a++)
				{
					for (int b = a + 1; b < g.Spots.Length; b++)
					{
						if (!g.Links[a, b])
						{
							continue;
						}
						Herds.Legs(g, g.Spots[a], g.Spots[b], legs);
						bool bad = false;
						for (int k = 0; k + 1 < legs.Count && !bad; k++)
						{
							double dx = legs[k + 1].x - legs[k].x, dz = legs[k + 1].z - legs[k].z;
							int steps = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(dx * dx + dz * dz)));
							double last = Mesh(legs[k].x, legs[k].z);
							for (int q = 1; q <= steps && !bad; q++)
							{
								double x = legs[k].x + dx * q / steps, z = legs[k].z + dz * q / steps;
								double h = Mesh(x, z);
								// The drawn ground: no deeper than the belly, no step of more than a unit and a half over a unit (a drop).
								if (DeerDepth(x, z) > Herds.Belly || Math.Abs(h - last) > 1.5 * Math.Sqrt(dx * dx + dz * dz) / steps)
								{
									bad = true;
									Console.WriteLine("  seed " + seed + " group " + g.Index + ": a leg " + (DeerDepth(x, z) > Herds.Belly ? "deep in water" : "over a drop (" + (h - last).ToString("0.0") + ")") + " at (" + x.ToString("0") + ", " + z.ToString("0") + ")");
								}
								last = h;
							}
						}
						legsBad += bad ? 1 : 0;
					}
				}
				// Every deer over half an hour.
				Herds.Move mv = default, spare = default;
				for (int d = 0; d < g.Members.Length; d++)
				{
					for (double t = 0.0; t < 1800.0; t += 4.7)
					{
						herds.At(g, d, t, ref mv, ref spare, out DeerState s);
						samples++;
						double depth = DeerDepth(s.X, s.Z);
						if (depth > worstDeep)
						{
							worstDeep = depth;
						}
						if (depth > Herds.Belly)
						{
							deep++;
						}
						double off = Math.Abs(Herds.Ground(s.X, s.Z) - Mesh(s.X, s.Z));
						sumOff += off;
						if (off > worstOff)
						{
							worstOff = off;
						}
						double cx = s.X - g.Site.X, cz = s.Z - g.Site.Z;
						bool atCrossing = cx * cx + cz * cz < (g.Site.Half + 12.0) * (g.Site.Half + 12.0);
						if (!atCrossing && OnAnyPaving(s.X, s.Z, out RoadKind kind))
						{
							if (paved == 0)
							{
								Console.WriteLine("  seed " + seed + " group " + g.Index + " deer " + d + ": on a " + kind + "'s paving away from its crossing at (" + s.X.ToString("0") + ", " + s.Z.ToString("0") + "), t " + t.ToString("0"));
							}
							paved++;
						}
					}
				}
			}
			Console.WriteLine("         race seed " + seed + ": " + herds.Groups.Length + " groups, " + herds.DeerCount + " deer; " + spotsBad + " spots and " + legsBad + " legs wet, on a road or over a drop; "
				+ samples + " deer positions: " + deep + " deep in water (deepest " + Math.Max(0.0, worstDeep).ToString("0.0") + "), " + paved + " on paving away from a crossing; the ground deer stand on off the drawn ground by "
				+ (sumOff / Math.Max(1, samples)).ToString("0.00") + " on average, " + worstOff.ToString("0.00") + " at worst");
			ok &= spotsBad == 0 && legsBad == 0 && deep == 0 && paved == 0 && worstOff < 1e-3 && herds.Groups.Length >= 100;
		}
		ok &= DeadDeerChecks();
		ok &= ReactionChecks();
		return ok;
	}

	/// <summary>
	/// The deer's reactions (Reactions.cs): for one race, every group alerted and then fleeing (each way in turn) from a car on its road
	/// 60 units from the crossing, at three times of day; every deer followed from the alert to a minute after the group is calm again:
	/// never in water above its belly, never on any road's paving away from its crossing, and never jumping (no step longer than a gallop
	/// covers, through the start, the run, the watch and the calm day going on from the refuge); and the same play from the same reactions.
	/// </summary>
	private static bool ReactionChecks()
	{
		bool ok = true;
		Herds herds = new Herds(1, Sky0(1), 2400.0);
		var mv = new Herds.Move();
		var spare = new Herds.Move();
		int plays = 0, deep = 0, paved = 0, jumps = 0, samples = 0, across = 0, notCalm = 0, dartNever = 0, dartWrong = 0;
		var darts = new List<double>();
		double worstJump = 0.0, longest = 0.0;
		const double step = 0.2;
		foreach (Herds.Group g in herds.Groups)
		{
			for (int when = 0; when < 3; when++)
			{
				double t0 = 300.0 + when * 700.0 + g.Index % 7 * 9.0;
				FleeWay way = (FleeWay)((g.Index + when) % 3);
				double side = g.Index % 2 == 0 ? 60.0 : -60.0;
				var alert = new Reaction { Seed = 1, Group = g.Index, Seq = 0, Kind = ReactKind.Alert, T0 = t0, FromX = g.Site.X + g.Tx * side, FromZ = g.Site.Z + g.Tz * side };
				var flee = alert;
				flee.Seq = 1;
				flee.Kind = ReactKind.Flee;
				flee.Way = way;
				flee.T0 = t0 + 2.0;
				// A dart is timed by when its first deer would be on the paving: it must be there then (the car's machine waits for it).
				double dartAfter = -1.0;
				if (way == FleeWay.Across && g.Crosses)
				{
					herds.SetReactions(g, new[] { alert });
					dartAfter = herds.CrossAfter(g, flee, ref mv, ref spare);
				}
				herds.SetReactions(g, new[] { alert, flee });
				Reacted rr = g.Reactions[1];
				if (way == FleeWay.Across && g.Crosses)
				{
					bool there = false;
					double reach = g.Site.Half + 3.5;
					for (int d = 0; dartAfter >= 0.0 && d < g.Members.Length; d++)
					{
						herds.Live(g, d, flee.T0 + dartAfter + 0.05, ref mv, ref spare, out DeerState s);
						double cx = s.X - g.Site.X, cz = s.Z - g.Site.Z;
						there |= cx * cx + cz * cz < reach * reach;
					}
					if (dartAfter < 0.0)
					{
						dartNever++;
					}
					else if (!there)
					{
						if (dartWrong < 3)
						{
							Console.WriteLine("  group " + g.Index + ": a dart's first deer should be on the road " + dartAfter.ToString("0.0") + " s after it starts, and is not");
						}
						dartWrong++;
					}
					else
					{
						darts.Add(dartAfter);
					}
				}
				plays++;
				longest = Math.Max(longest, rr.Done - flee.T0);
				int dstSide = g.Spots[rr.Dst].Side;
				for (int d = 0; d < g.Members.Length; d++)
				{
					double lx = 0.0, lz = 0.0;
					bool crossed = false;
					int startSide = Herds.SideOf(g, rr.StartX[d], rr.StartZ[d]);
					for (double t = t0 - 1.0; t < rr.End + 60.0; t += step)
					{
						herds.Live(g, d, t, ref mv, ref spare, out DeerState s);
						samples++;
						if (t > t0 - 1.0)
						{
							double jx = s.X - lx, jz = s.Z - lz;
							double jump = Math.Sqrt(jx * jx + jz * jz);
							if (jump > Herds.GallopSpeed * step * 1.05 + 0.3)
							{
								if (jumps < 3)
								{
									Console.WriteLine("  group " + g.Index + " deer " + d + " (" + way + "): a jump of " + jump.ToString("0.0") + " at t " + t.ToString("0.0") + " (flee " + flee.T0.ToString("0.0") + ", done " + rr.Done.ToString("0.0") + ", calm " + rr.End.ToString("0.0") + ")");
								}
								jumps++;
								worstJump = Math.Max(worstJump, jump);
							}
						}
						lx = s.X;
						lz = s.Z;
						if (DeerDepth(s.X, s.Z) > Herds.Belly)
						{
							deep++;
						}
						double cx = s.X - g.Site.X, cz = s.Z - g.Site.Z;
						bool atCrossing = cx * cx + cz * cz < (g.Site.Half + 12.0) * (g.Site.Half + 12.0);
						if (!atCrossing && OnAnyPaving(s.X, s.Z, out RoadKind kind))
						{
							if (paved < 3)
							{
								Console.WriteLine("  group " + g.Index + " deer " + d + " (" + way + "): on a " + kind + "'s paving at (" + s.X.ToString("0") + ", " + s.Z.ToString("0") + "), t " + t.ToString("0.0") + ", " + Math.Sqrt(cx * cx + cz * cz).ToString("0.0") + " from its crossing (" + g.Site.X.ToString("0") + ", " + g.Site.Z.ToString("0") + "; flee " + flee.T0.ToString("0.0") + ", done " + rr.Done.ToString("0.0") + ")");
							}
							paved++;
						}
						crossed |= Herds.SideOf(g, s.X, s.Z) != startSide && t < rr.Done;
					}
					if (way == FleeWay.Across && g.Crosses && d == 0 && crossed)
					{
						across++;
					}
				}
				// The calm day goes on from the refuge: just after the end, the group's spot is the refuge.
				if (herds.SpotAt(g, rr.Slot - 1) != rr.Dst)
				{
					notCalm++;
				}
				// The same play again from the same reactions (made afresh).
				herds.SetReactions(g, Array.Empty<Reaction>());
				herds.SetReactions(g, new[] { alert, flee });
				if (g.Reactions[1].Done != rr.Done || g.Reactions[1].Dst != rr.Dst)
				{
					Console.WriteLine("  group " + g.Index + ": the same reactions played otherwise the second time");
					ok = false;
				}
				herds.SetReactions(g, Array.Empty<Reaction>());
			}
		}
		Console.WriteLine("         reactions: " + plays + " flees played (" + samples + " deer positions; the longest run " + longest.ToString("0.0") + " s); " + deep + " deep in water, " + paved
			+ " on paving away from a crossing, " + jumps + " jumps (worst " + worstJump.ToString("0.0") + "), " + notCalm + " not calm from the refuge after; " + across + " groups darted across");
		darts.Sort();
		Console.WriteLine("         darts timed: " + darts.Count + " first deer on the road when worked out (" + (darts.Count > 0 ? "in " + darts[0].ToString("0.0") + " to " + darts[darts.Count - 1].ToString("0.0")
			+ " s, half within " + darts[darts.Count / 2].ToString("0.0") + " s" : "none") + "), " + dartWrong + " not there then, " + dartNever + " never on it");
		ok &= deep == 0 && paved == 0 && jumps == 0 && notCalm == 0 && across > 20 && dartWrong == 0 && darts.Count > 20 && dartNever * 10 <= darts.Count;
		return ok;
	}

	/// <summary>
	/// Dead deer: every deer of one race thrown by a car from where it stands at half an hour (at a speed and a way from its index, up to
	/// 150 on the speedometer, some fast enough to bounce) and shot; each must come to rest within <see cref="DeerKills.Longest"/> lying
	/// on the drawn ground, never under it on the way, and land in the same place when its frames are of other lengths. Also the kill
	/// messages, a second kill of a deer, and the DeerAmount setting's groups.
	/// </summary>
	private static bool DeadDeerChecks()
	{
		bool ok = true;
		const double gravity = 9.81 * 3.3;
		Herds herds = new Herds(1, Sky0(1), 2400.0);
		var mv = new Herds.Move();
		var spare = new Herds.Move();
		int bodies = 0, under = 0, restless = 0, offGround = 0, differ = 0;
		double worstUnder = 0.0, worstOff = 0.0, worstDiffer = 0.0, longest = 0.0;
		foreach (Herds.Group g in herds.Groups)
		{
			for (int d = 0; d < g.Members.Length; d++)
			{
				herds.At(g, d, 1800.0, ref mv, ref spare, out DeerState s);
				double size = g.Members[d].Scale;
				double u = Herds.Unit(7UL, (ulong)g.Index, (ulong)d, 1UL), w = Herds.Unit(7UL, (ulong)g.Index, (ulong)d, 2UL);
				double speed = 5.0 + 78.0 * u, a = w * 2.0 * Math.PI;
				for (int how = 0; how < 2; how++)
				{
					var kill = new DeerKills.Kill
					{
						Seed = 1, Group = g.Index, Member = d, How = how == 0 ? DeerKills.How.Car : DeerKills.How.Shot,
						X = s.X, Z = s.Z, Y = how == 0 ? Herds.Ground(s.X, s.Z) + DeerKills.CentreHeight * size : Herds.Ground(s.X, s.Z),
						Vx = how == 0 ? Math.Cos(a) * speed * 1.1 : 0.0, Vz = how == 0 ? Math.Sin(a) * speed * 1.1 : 0.0, Vy = how == 0 ? Math.Min(DeerKills.MostLaunch, 1.5 + 0.09 * speed) : 0.0,
						Yaw = s.Yaw, Scale = size, Spin = (u - 0.5) * 14.0, Turn = (w - 0.5) * 3.0, Side = u < 0.5 ? -1 : 1,
					};
					// Read back as it would be on another machine.
					if (!DeerKills.Decode(DeerKills.Encode(kill), out DeerKills.Kill sent))
					{
						Console.WriteLine("  a kill message did not read back");
						ok = false;
						continue;
					}
					var body = new DeerKills.Body(sent, 100.0);
					var other = new DeerKills.Body(sent, 100.0);
					double t = 100.0;
					DeerKills.Pose p = default;
					while (!body.Still && t < 100.0 + DeerKills.Longest + 1.0)
					{
						t += 1.0 / 60.0;
						p = body.At(t, Herds.Ground, gravity);
						double below = Herds.Ground(p.X, p.Z) + DeerKills.Lift * size - p.Y;
						if (how == 0 && below > 0.05)
						{
							under++;
							worstUnder = Math.Max(worstUnder, below);
						}
					}
					longest = Math.Max(longest, t - 100.0);
					p = body.At(t + 1.0, Herds.Ground, gravity);
					restless += body.Still ? 0 : 1;
					double off = Math.Abs(p.Y - (Herds.Ground(p.X, p.Z) + DeerKills.Lift * size));
					double lying = Math.Abs(Math.Abs(p.Roll) % (2.0 * Math.PI) - Math.PI / 2.0);
					if (off > 0.01 || Math.Min(lying, Math.Abs(lying - Math.PI)) > 0.01)
					{
						offGround++;
						worstOff = Math.Max(worstOff, off);
					}
					// The same body on frames of other lengths (a slow machine's): it must lie in the same place.
					for (double tt = 100.0; tt < t + 1.0; tt += 0.05 + 0.1 * Herds.Unit(3UL, (ulong)g.Index, (ulong)d, (ulong)(tt * 100.0)))
					{
						other.At(tt, Herds.Ground, gravity);
					}
					DeerKills.Pose q = other.At(t + 1.0, Herds.Ground, gravity);
					double gap = Math.Sqrt((p.X - q.X) * (p.X - q.X) + (p.Z - q.Z) * (p.Z - q.Z) + (p.Y - q.Y) * (p.Y - q.Y));
					if (gap > 1e-6)
					{
						differ++;
						worstDiffer = Math.Max(worstDiffer, gap);
					}
					bodies++;
				}
			}
		}
		Console.WriteLine("         dead deer: " + bodies + " bodies thrown or shot; " + under + " moments under the ground (worst " + worstUnder.ToString("0.00") + "), " + restless + " never at rest (longest "
			+ longest.ToString("0.0") + " s), " + offGround + " not lying on the ground at rest (worst " + worstOff.ToString("0.00") + "), " + differ + " in another place on other frames (worst " + worstDiffer.ToString("0.000") + ")");
		ok &= under == 0 && restless == 0 && offGround == 0 && differ == 0 && bodies > 100;

		// A kill's body is put in place of another's for the same deer (the host's for this machine's own), and another race's is dropped.
		var kills = new DeerKills(5);
		var k1 = new DeerKills.Kill { Seed = 5, Group = 3, Member = 1, Scale = 1.0, T = 10.0, X = 1.0 };
		var k2 = k1;
		k2.X = 2.0;
		bool first = kills.Set(k1), again = kills.Set(k2), elsewhere = kills.Set(new DeerKills.Kill { Seed = 6, Group = 4, Member = 0, Scale = 1.0 });
		bool replaced = kills.Dead[DeerKills.Key(3, 1)].Kill.X == 2.0;
		Console.WriteLine("         kills: the first taken " + first + ", the host's in place of it " + (again && replaced) + ", another race's " + elsewhere);
		ok &= first && again && replaced && !elsewhere && kills.Count == 1;

		// The DeerAmount setting (alone only).
		string amounts = "";
		int last = -1;
		bool rising = true;
		foreach (double amount in new[] { 0.0, 0.5, 1.0, 2.0, 4.0 })
		{
			Herds h = new Herds(1, Sky0(1), 2400.0, amount);
			amounts += (amounts.Length > 0 ? ", " : "") + amount.ToString("0.#") + ": " + h.Groups.Length + " groups (" + h.DeerCount + " deer)";
			rising &= h.Groups.Length > last || (amount > 0.0 && h.Groups.Length == last && last > 300);
			ok &= amount > 0.0 || h.Groups.Length == 0;
			last = h.Groups.Length;
		}
		Console.WriteLine("         DeerAmount " + amounts);
		ok &= rising;
		return ok;
	}

	/// <summary>The hour a race from <paramref name="seed"/> starts at, as <c>Sky.StartHourOf</c> (Sky is left out of this tool).</summary>
	private static double Sky0(int seed)
	{
		ulong h = Noise.Mix((ulong)(uint)seed ^ 0x0E7A_7140UL);
		double u = (double)(h >> 11) * (1.0 / 9007199254740992.0);
		double v = (double)(Noise.Mix(h) >> 11) * (1.0 / 9007199254740992.0);
		return u < 0.75 ? 7.0 + 10.0 * v : 24.0 * v;
	}

	/// <summary>Whether (x, z) is under a river's or lake's water.</summary>
	private static bool Wet(double x, double z)
	{
		return Rivers.Water(x, z, out double level) && WorldGen.Height(x, z) - Network.Lift < level + 0.5;
	}

	private static bool BackRoads()
	{
		int dead = 0, hooks = 0, lanes = 0, mainDead = 0;
		foreach (Network.Road road in Network.Roads)
		{
			// A main road (trunk, primary, secondary) never stops in the fields: Westhaven's link to the trunk did, 270 units short of
			// it, and the town had no road out (Network.SnapJoin). Its ends at the map's edge are portals, not dead ends.
			if (road.Kind <= RoadKind.Secondary && road.Count >= 2)
			{
				for (int end = 0; end < 2; end++)
				{
					int k = end == 0 ? 0 : road.Count - 1;
					if (end == 0 ? road.Dead0 : road.Dead1)
					{
						mainDead++;
						Console.WriteLine("  main road dead end '" + road.Name + "' (" + road.Kind + ") at (" + road.X[k].ToString("0") + ", " + road.Z[k].ToString("0") + ")");
					}
				}
			}
			if (road.Kind != RoadKind.Lane)
			{
				continue;
			}
			lanes++;
			for (int end = 0; end < 2; end++)
			{
				int k = end == 0 ? 0 : road.Count - 1;
				if ((end == 0 ? road.Dead0 : road.Dead1) && Places.At(road.X[k], road.Z[k]) is not { Kind: not PlaceKind.Hamlet })
				{
					dead++;
					Console.WriteLine("  lane dead end '" + road.Name + "' at (" + road.X[k].ToString("0") + ", " + road.Z[k].ToString("0") + ")");
				}
			}
			if (road.Name != null && road.Name.EndsWith(" on", StringComparison.Ordinal) && road.Count > 4)
			{
				int n = road.Count;
				double h0 = Math.Atan2(road.Z[2] - road.Z[0], road.X[2] - road.X[0]), h1 = Math.Atan2(road.Z[n - 1] - road.Z[n - 3], road.X[n - 1] - road.X[n - 3]);
				double turn = Math.Abs(Math.IEEERemainder(h1 - h0, 2.0 * Math.PI));
				if (turn > 2.6)
				{
					hooks++;
					Console.WriteLine("  through way '" + road.Name + "' hooks round at (" + road.X[n - 1].ToString("0") + ", " + road.Z[n - 1].ToString("0") + ")");
				}
			}
		}
		Console.WriteLine("backroads: " + lanes + " lanes, " + Network.LinkCount + " of them links; " + dead + " lane dead ends in the country, " + hooks + " through ways hooking round; "
			+ mainDead + " main road dead ends");
		return dead <= 10 && hooks == 0 && mainDead == 0;
	}

	/// <summary>Paved roads lying along each other (see the list at the top, 27b).</summary>
	private static bool Alongside()
	{
		int pairs = 0, checkedRoads = 0;
		HashSet<long> seen = new HashSet<long>();
		for (int r = 0; r < Network.Roads.Count; r++)
		{
			Network.Road road = Network.Roads[r];
			if (Network.Unpaved(road.Kind) || road.Count < 2)
			{
				continue;
			}
			checkedRoads++;
			// For each other road, the run of this road's samples (by distance along) whose point is on it, running the same way.
			Dictionary<int, (double start, double last)> runs = new Dictionary<int, (double, double)>();
			HashSet<int> on = new HashSet<int>();
			for (int i = 0; i < road.Count; i++)
			{
				int a = Math.Max(0, i - 1), b = Math.Min(road.Count - 1, i + 1);
				double tx = road.X[b] - road.X[a], tz = road.Z[b] - road.Z[a], tl = Math.Sqrt(tx * tx + tz * tz);
				on.Clear();
				foreach ((int o, int k) in Near(road.X[i], road.Z[i]))
				{
					if (o == r || on.Contains(o))
					{
						continue;
					}
					Network.Road other = Network.Roads[o];
					double sx = other.X[k + 1] - other.X[k], sz = other.Z[k + 1] - other.Z[k], sl = sx * sx + sz * sz;
					if (sl <= 0.0 || tl <= 0.0)
					{
						continue;
					}
					double u = Math.Max(0.0, Math.Min(1.0, ((road.X[i] - other.X[k]) * sx + (road.Z[i] - other.Z[k]) * sz) / sl));
					double dx = road.X[i] - other.X[k] - sx * u, dz = road.Z[i] - other.Z[k] - sz * u;
					double cos = Math.Abs(tx * sx + tz * sz) / (tl * Math.Sqrt(sl));
					double wide = Math.Max(road.Half, other.Half);
					if (dx * dx + dz * dz < wide * wide && cos > 0.9)
					{
						on.Add(o);
					}
				}
				foreach (int o in on)
				{
					bool carries = i > 0 && runs.TryGetValue(o, out (double start, double last) run) && run.last == road.S[i - 1];
					(double start, double last) now = (carries ? runs[o].start : road.S[i], road.S[i]);
					runs[o] = now;
					long key = ((long)Math.Min(r, o) << 32) | (uint)Math.Max(r, o);
					if (now.last - now.start >= 40.0 && seen.Add(key))
					{
						pairs++;
						if (pairs <= 10)
						{
							Network.Road other = Network.Roads[o];
							Console.WriteLine("  " + road.Kind + " '" + road.Name + "' (" + r + ") along " + other.Kind + " '" + other.Name + "' (" + o + ") from " + now.start.ToString("0") + " at ("
								+ road.X[i].ToString("0") + ", " + road.Z[i].ToString("0") + ")");
						}
					}
				}
			}
		}
		Console.WriteLine("alongside: " + checkedRoads + " paved roads, " + pairs + " pairs lying along each other for 40 units or more" + (pairs == 0 ? "" : "  FAIL"));
		return pairs == 0;
	}

	/// <summary>Lanes' ends beside the road they end on, sliver lanes, buried roads driven (see the list at the top, 27c).</summary>
	private static bool Beside()
	{
		int ends = 0, slivers = 0, buried = 0, lanes = 0, slanted = 0;
		for (int r = 0; r < Network.Roads.Count; r++)
		{
			Network.Road road = Network.Roads[r];
			if (Network.CutWhole(road))
			{
				if (Traffic.Driven(road))
				{
					buried++;
					Console.WriteLine("  driven road cut out whole: " + road.Kind + " '" + road.Name + "' (" + r + ")");
				}
				continue;
			}
			if (road.Kind != RoadKind.Lane || road.Count < 2)
			{
				continue;
			}
			lanes++;
			int onRoads = 0;
			for (int end = 0; end < 2; end++)
			{
				int k = end == 0 ? 0 : road.Count - 1, step = end == 0 ? 1 : -1;
				int on = -1;
				double best = double.MaxValue;
				for (int o = 0; o < Network.Roads.Count; o++)
				{
					Network.Road other = Network.Roads[o];
					if (o == r || Network.Unpaved(other.Kind) || other.Kind >= RoadKind.Rally || Network.CutWhole(other))
					{
						continue;
					}
					double d = LineDistance(other, road.X[k], road.Z[k]);
					if (d < other.Half + 2.0 && d < best)
					{
						best = d;
						on = o;
					}
				}
				if (on < 0)
				{
					continue;
				}
				Network.Road j = Network.Roads[on];
				// Meeting another road's end, the two carry on as one lane: not a sliver's end.
				int last = j.Count - 1;
				if (Math.Abs(j.X[0] - road.X[k]) + Math.Abs(j.Z[0] - road.Z[k]) > 3.0 && Math.Abs(j.X[last] - road.X[k]) + Math.Abs(j.Z[last] - road.Z[k]) > 3.0)
				{
					onRoads++;
					// How square it meets the road: its way over its last 30 against the road's there.
					int b = k;
					while (b + step >= 0 && b + step < road.Count && Math.Abs(road.S[b + step] - road.S[k]) < 30.0)
					{
						b += step;
					}
					double ax = road.X[k] - road.X[b], az = road.Z[k] - road.Z[b], al = Math.Sqrt(ax * ax + az * az);
					int seg = Nearest(j, road.X[k], road.Z[k]);
					double tx = j.X[seg + 1] - j.X[seg], tz = j.Z[seg + 1] - j.Z[seg], tl = Math.Sqrt(tx * tx + tz * tz);
					if (al > 0.0 && tl > 0.0 && Math.Abs(ax * tx + az * tz) / (al * tl) > 0.5)
					{
						slanted++;
						if (slanted <= 10)
						{
							Console.WriteLine("  lane '" + road.Name + "' (" + r + ") meets '" + j.Name + "' at a slant at (" + road.X[k].ToString("0") + ", " + road.Z[k].ToString("0") + ")");
						}
					}
				}
				double reach = j.Half + road.Half + 10.0, run = 0.0;
				for (int i = k; i + step >= 0 && i + step < road.Count && LineDistance(j, road.X[i + step], road.Z[i + step]) < reach; i += step)
				{
					run += Math.Abs(road.S[i + step] - road.S[i]);
				}
				if (run > reach * 1.6 + 10.0)
				{
					ends++;
					if (ends <= 10)
					{
						Console.WriteLine("  lane '" + road.Name + "' (" + r + ") runs in beside '" + j.Name + "' (" + on + ") for " + run.ToString("0") + " to (" + road.X[k].ToString("0") + ", " + road.Z[k].ToString("0") + ")");
					}
				}
			}
			if (onRoads == 2 && road.S[road.Count - 1] < 120.0)
			{
				slivers++;
				Console.WriteLine("  sliver lane '" + road.Name + "' (" + r + "), " + road.S[road.Count - 1].ToString("0") + " long, at (" + road.X[0].ToString("0") + ", " + road.Z[0].ToString("0") + ")");
			}
		}
		// Hooks: a paved road's line running past its end and turning back to it (within 40 of the end, a piece heading away from it).
		int hooks = 0, apart = 0;
		List<(double x, double z, int r)> tips = new List<(double, double, int)>();
		for (int r = 0; r < Network.Roads.Count; r++)
		{
			Network.Road road = Network.Roads[r];
			if (Network.Unpaved(road.Kind) || road.Kind >= RoadKind.Rally || road.Count < 6 || Network.CutWhole(road))
			{
				continue;
			}
			for (int end = 0; end < 2; end++)
			{
				int k = end == 0 ? 0 : road.Count - 1, step = end == 0 ? 1 : -1;
				tips.Add((road.X[k], road.Z[k], r));
				for (int i = k + step; i + step >= 0 && i + step < road.Count; i += step)
				{
					double dx = road.X[i] - road.X[k], dz = road.Z[i] - road.Z[k];
					if (dx * dx + dz * dz > 1600.0)
					{
						break;
					}
					// The piece from i towards the end (i - step) heading away from the end.
					double tx = road.X[i - step] - road.X[i], tz = road.Z[i - step] - road.Z[i];
					if (tx * -dx + tz * -dz < 0.0)
					{
						hooks++;
						if (hooks <= 10)
						{
							Console.WriteLine("  " + road.Kind + " '" + road.Name + "' (" + r + ") hooks back into its end at (" + road.X[k].ToString("0") + ", " + road.Z[k].ToString("0") + ")");
						}
						break;
					}
				}
			}
		}
		// Road ends a few units apart (not one point, not well apart) away from a hub (whose arms end round the place): the joint
		// between them leaves ground showing (Farm 82's drive, lane and through way).
		for (int a = 0; a < tips.Count; a++)
		{
			for (int b = a + 1; b < tips.Count; b++)
			{
				double dx = tips[a].x - tips[b].x, dz = tips[a].z - tips[b].z, d = Math.Sqrt(dx * dx + dz * dz);
				if (tips[a].r != tips[b].r && d > 1.0 && d < 8.0 && !Network.Hubs.Exists(h => Math.Abs(h.X - tips[a].x) < 30.0 && Math.Abs(h.Z - tips[a].z) < 30.0))
				{
					apart++;
					if (apart <= 10)
					{
						Console.WriteLine("  road ends " + d.ToString("0.0") + " apart: " + tips[a].r + " and " + tips[b].r + " at (" + tips[a].x.ToString("0") + ", " + tips[a].z.ToString("0") + ")");
					}
				}
			}
		}
		// Two paved roads alone at a place (their ends one point, no other road's end or paving there) not drawn as one road round
		// the bend: each cut square for the other, the outside of the corner ground (Harrow, Ivybridge).
		int unjoined = 0;
		for (int a = 0; a < tips.Count; a++)
		{
			int mates = 0, mate = -1;
			for (int b = 0; b < tips.Count; b++)
			{
				if (b != a && Math.Abs(tips[a].x - tips[b].x) < 0.5 && Math.Abs(tips[a].z - tips[b].z) < 0.5)
				{
					mates++;
					mate = b;
				}
			}
			if (mates != 1 || mate < a)
			{
				continue;
			}
			Network.Road ra = Network.Roads[tips[a].r], rb = Network.Roads[tips[mate].r];
			bool aEnd0 = Math.Abs(ra.X[0] - tips[a].x) + Math.Abs(ra.Z[0] - tips[a].z) < 0.01, bEnd0 = Math.Abs(rb.X[0] - tips[mate].x) + Math.Abs(rb.Z[0] - tips[mate].z) < 0.01;
			if ((aEnd0 ? ra.Joined0 : ra.Joined1) || (bEnd0 ? rb.Joined0 : rb.Joined1) || Network.Hubs.Exists(h => Math.Abs(h.X - tips[a].x) < 1.0 && Math.Abs(h.Z - tips[a].z) < 1.0))
			{
				continue;
			}
			bool other = false;
			for (int o = 0; o < Network.Roads.Count && !other; o++)
			{
				Network.Road od = Network.Roads[o];
				if (o != tips[a].r && o != tips[mate].r && !Network.Unpaved(od.Kind) && od.Kind < RoadKind.Rally && LineDistance(od, tips[a].x, tips[a].z) < od.Half + 2.0)
				{
					other = true;
				}
			}
			if (!other)
			{
				unjoined++;
				if (unjoined <= 10)
				{
					Console.WriteLine("  two roads alone at (" + tips[a].x.ToString("0") + ", " + tips[a].z.ToString("0") + ") not joined: " + tips[a].r + " and " + tips[mate].r);
				}
			}
		}
		// No routed road's end kinks (its line turning more than 15 degrees from one sample to the next within 60 of the end): an unhooked
		// end taken straight to its point kinked 20 degrees, and the road meeting it there left a sliver of ground in its mouth
		// (Kestrel). Town streets turn their corners on purpose.
		int kinks = 0;
		double worstKink = 0.0;
		for (int r = 0; r < Network.Roads.Count; r++)
		{
			Network.Road rd = Network.Roads[r];
			if (rd.Kind != RoadKind.Primary && rd.Kind != RoadKind.Secondary && rd.Kind != RoadKind.Lane)
			{
				continue;
			}
			// A kink is a turn at one sample with the line straight either side (a bend, a squared end, turns over several).
			for (int i = 2; i + 2 < rd.Count; i++)
			{
				if (rd.S[i] > 60.0 && rd.S[i] < rd.S[rd.Count - 1] - 60.0)
				{
					continue;
				}
				double deg = TurnAt(rd, i), before = TurnAt(rd, i - 1), after = TurnAt(rd, i + 1);
				if (Math.Max(before, after) * 3.0 > deg)
				{
					continue;
				}
				worstKink = Math.Max(worstKink, deg);
				if (deg > 18.0)
				{
					kinks++;
					if (kinks <= 10)
					{
						Console.WriteLine("  kink of " + deg.ToString("0") + " degrees in road " + r + " (" + rd.Kind + ") at (" + rd.X[i].ToString("0") + ", " + rd.Z[i].ToString("0") + ")");
					}
				}
			}
		}
		bool ok = ends == 0 && slivers == 0 && buried == 0 && hooks == 0 && apart == 0 && slanted == 0 && unjoined == 0 && kinks == 0;
		Console.WriteLine("beside:  " + lanes + " lanes, " + ends + " ends running in beside the road they end on, " + slivers + " sliver lanes, " + buried + " driven roads cut out whole, "
			+ hooks + " paved road ends hooking back, " + apart + " pairs of road ends 1 to 8 apart, " + slanted + " lanes meeting a road more than 30 degrees off square, " + unjoined + " pairs of roads alone at a place not joined, " + kinks + " routed road ends kinking over 18 degrees at one sample (worst " + worstKink.ToString("0") + ")" + (ok ? "" : "  FAIL"));
		return ok;
	}

	/// <summary>How far (degrees) a road's line turns at sample i, from the piece before it to the piece after.</summary>
	private static double TurnAt(Network.Road rd, int i)
	{
		double ax = rd.X[i] - rd.X[i - 1], az = rd.Z[i] - rd.Z[i - 1], bx = rd.X[i + 1] - rd.X[i], bz = rd.Z[i + 1] - rd.Z[i];
		double la = Math.Sqrt(ax * ax + az * az), lb = Math.Sqrt(bx * bx + bz * bz);
		if (la < 1e-6 || lb < 1e-6)
		{
			return 0.0;
		}
		return Math.Acos(Math.Max(-1.0, Math.Min(1.0, (ax * bx + az * bz) / (la * lb)))) * 180.0 / Math.PI;
	}

	/// <summary>A hash of the whole lane graph and its conflict areas (every number's bits), for the repeatability check.</summary>
	private static ulong LaneHash()
	{
		ulong h = 1469598103934665603UL;
		void Mix(long v)
		{
			h = (h ^ (ulong)v) * 1099511628211UL;
		}
		void D(double v)
		{
			Mix(BitConverter.DoubleToInt64Bits(v));
		}
		foreach (Lanes.Lane l in Lanes.All)
		{
			Mix(l.Edge); Mix(l.Dir); Mix(l.Index); Mix(l.Kind); Mix(l.From); Mix(l.To); Mix(l.Parent); D(l.Start); D(l.End); D(l.Limit); D(l.Hold);
			foreach (int o in l.Out) Mix(o);
		}
		foreach (Lanes.Connector c in Lanes.Connectors)
		{
			Mix(c.Node); Mix(c.From); Mix(c.To); Mix(c.Turn); D(c.Length); D(c.Limit); Mix(c.Wide ? 1 : 0);
			for (int i = 0; i < c.X.Length; i++) { D(c.X[i]); D(c.Z[i]); D(c.Y[i]); }
		}
		foreach (Conflicts.Area a in Conflicts.All)
		{
			Mix(a.A); Mix(a.B); Mix(a.Kind); Mix(a.Yield); Mix(a.Tie); D(a.A0); D(a.A1); D(a.B0); D(a.B1);
		}
		return h;
	}

	/// <summary>The piece of a road nearest (x, z).</summary>
	private static int Nearest(Network.Road road, double x, double z)
	{
		int best = 0;
		double bd = double.MaxValue;
		for (int i = 0; i + 1 < road.Count; i++)
		{
			double sx = road.X[i + 1] - road.X[i], sz = road.Z[i + 1] - road.Z[i], sl = sx * sx + sz * sz;
			double u = sl > 0.0 ? Math.Max(0.0, Math.Min(1.0, ((x - road.X[i]) * sx + (z - road.Z[i]) * sz) / sl)) : 0.0;
			double dx = x - road.X[i] - sx * u, dz = z - road.Z[i] - sz * u, d = dx * dx + dz * dz;
			if (d < bd)
			{
				bd = d;
				best = i;
			}
		}
		return best;
	}

	/// <summary>How far (x, z) is from a road's centre line.</summary>
	private static double LineDistance(Network.Road road, double x, double z)
	{
		double best = double.MaxValue;
		for (int i = 0; i + 1 < road.Count; i++)
		{
			double sx = road.X[i + 1] - road.X[i], sz = road.Z[i + 1] - road.Z[i], sl = sx * sx + sz * sz;
			double u = sl > 0.0 ? Math.Max(0.0, Math.Min(1.0, ((x - road.X[i]) * sx + (z - road.Z[i]) * sz) / sl)) : 0.0;
			double dx = x - road.X[i] - sx * u, dz = z - road.Z[i] - sz * u;
			best = Math.Min(best, dx * dx + dz * dz);
		}
		return Math.Sqrt(best);
	}

	/// <summary>The highway's wrap round (see the list at the top, 29).</summary>
	private static bool WrapChecks()
	{
		if (Wrap.A < 0)
		{
			Console.WriteLine("wrap:    no pair of tunnels found  FAIL");
			return false;
		}
		Network.Portal a = Network.Portals[Wrap.A], b = Network.Portals[Wrap.B];
		int bad = 0;
		bool trunk = Network.Roads[a.Road].Kind == RoadKind.Trunk;
		bool facing = a.DirX == -b.DirX && a.DirZ == -b.DirZ && a.Half == b.Half;
		Wrap.Move(Wrap.A, out double ax, out double az, out double ay);
		Wrap.Move(Wrap.B, out double bx, out double bz, out double by);
		double roundTrip = Math.Abs(ax + bx) + Math.Abs(az + bz) + Math.Abs(ay + by);
		// Every lane's middle, both ways, a little past the glue plane: on the other side the same lane, a little short of it.
		int lanes = 0;
		foreach (int from in new[] { Wrap.A, Wrap.B })
		{
			Network.Portal p = Network.Portals[from];
			Wrap.Move(from, out double dx, out double dz, out double _);
			foreach (double across in new[] { -Courses.HighwayOuter, -Courses.HighwayInner, Courses.HighwayInner, Courses.HighwayOuter })
			{
				double k = 1.5;
				double x = p.X + p.DirX * (Wrap.Depth + k) - p.DirZ * across, z = p.Z + p.DirZ * (Wrap.Depth + k) + p.DirX * across;
				if (!Wrap.Past(from, x, z))
				{
					bad++;
					Console.WriteLine("  wrap: lane " + across.ToString("0.0") + " past the plane at portal " + from + " not seen as past");
				}
				Wrap.Into(Wrap.Other(from), x + dx, z + dz, out double along, out double across2);
				if (Math.Abs(along - (Wrap.Depth - k)) > 1e-6 || Math.Abs(across2 + across) > 1e-6 || Math.Abs(across2) > p.Half)
				{
					bad++;
					Console.WriteLine("  wrap: lane " + across.ToString("0.0") + " from portal " + from + " lands " + along.ToString("0.00") + " in, " + across2.ToString("0.00") + " across");
				}
				lanes++;
			}
		}
		// The ground under each open tunnel's floor (the floor's top is the road's height at the mouth), and the road into it level.
		int ground = 0, samples = 0;
		double worst = double.NegativeInfinity, step = 0.0;
		foreach (int i in new[] { Wrap.A, Wrap.B })
		{
			Network.Portal p = Network.Portals[i];
			double top = p.Y + Network.Lift, width = p.Half * 2.0 + 8.0 + 4.0;
			for (double along = 0.0; along <= Wrap.Length; along += 2.0)
			{
				for (double across = -width / 2.0; across <= width / 2.0 + 1e-9; across += 2.0)
				{
					double x = p.X + p.DirX * along - p.DirZ * across, z = p.Z + p.DirZ * along + p.DirX * across;
					double over = Mesh(x, z) - top;
					samples++;
					worst = Math.Max(worst, over);
					if (over > -0.05)
					{
						ground++;
					}
				}
			}
			Network.Road road = Network.Roads[p.Road];
			int end = road.X[0] == p.X && road.Z[0] == p.Z ? 0 : road.Count - 1, back = end == 0 ? 10 : road.Count - 11;
			step = Math.Max(step, Math.Abs(road.Y[back] - road.Y[end]));
		}
		// The highway event's start and finish clear of the tunnels.
		double clear = Math.Min(Courses.HighwayStart, Courses.HighwayFinish) - Courses.HighwayBoxBack;
		bool ok = trunk && facing && roundTrip < 1e-6 && bad == 0 && ground == 0 && step < 0.01 && clear > Wrap.Length + 50.0;
		Console.WriteLine("wrap:    portals " + Wrap.A + " and " + Wrap.B + " (" + (trunk ? "the trunk's" : "NOT the trunk's") + ", " + (facing ? "facing opposite ways, as wide" : "NOT facing opposite ways or not as wide")
			+ "); there and back " + roundTrip.ToString("0.000000") + "; " + bad + " of " + lanes + " lanes not landing in their lane; " + ground + " of " + samples
			+ " floor points with the ground through them (highest " + worst.ToString("0.00") + "); road into the mouths level within " + step.ToString("0.000")
			+ "; the event's nearest spot " + clear.ToString("0") + " from a mouth" + (ok ? "  ok" : "  FAIL"));
		return ok;
	}

	/// <summary>Whether (x, z) is within the paving of a road other than <paramref name="self"/>.</summary>
	private static bool OnOtherRoad(Network.Road self, double x, double z)
	{
		foreach ((int o, int i) in Near(x, z))
		{
			Network.Road road = Network.Roads[o];
			Lines.Project(road.X, road.Z, i, x, z, out double _, out double d);
			if (road != self && d < road.Half + 2.0)
			{
				return true;
			}
		}
		return false;
	}

	private static bool Tucked()
	{
		int samples = 0, bad = 0;
		double worst = 0.0;
		foreach (Network.Road r in Network.Roads)
		{
			if (!Network.Unpaved(r.Kind) || r.Kind == RoadKind.Rally)
			{
				continue;
			}
			for (int i = 0; i + 1 < r.Count; i++)
			{
				foreach (double t in new[] { 0.0, 0.5 })
				{
					foreach (double f in new[] { -1.0, 0.0, 1.0 })
					{
						Network.Point(r, i, t, f, 0.0, out double x, out double z, out double y);
						HashSet<int> seen = new HashSet<int>();
						foreach ((int o, int _) in Near(x, z))
						{
							if (!seen.Add(o))
							{
								continue;
							}
							double surface = SurfaceOf(o, x, z);
							if (double.IsNaN(surface))
							{
								continue;
							}
							samples++;
							if (y > surface - 0.02)
							{
								bad++;
								worst = Math.Max(worst, y - surface);
								Console.WriteLine("  a ribbon at (" + x.ToString("0.0") + ", " + z.ToString("0.0") + ") " + (y - surface).ToString("0.000") + " from the surface over it");
							}
						}
					}
				}
			}
		}
		Console.WriteLine("tucked:  " + bad + " of " + samples + " track and path ribbon points on a paved road's surface over it, worst " + worst.ToString("0.00"));
		return bad == 0;
	}

	/// <summary>The streamer's tile side (Streamer is not built here).</summary>
	private const double TileSize = 250.0;

	private static bool FarChecks()
	{
		System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
		FarGrid.Ensure();
		Console.WriteLine("farview: grid " + FarGrid.N + " a side, picture " + FarGrid.Pixels + " a side, built in " + watch.ElapsedMilliseconds + " ms (" + FarGrid.BuildMs.ToString("0") + " ms inside)");
		int n = FarGrid.N;
		double lift = Network.Lift;
		bool ok = true;

		// The real surface at a point: the drawn ground, or the water drawn over it.
		double Real(double x, double z)
		{
			double h = Mesh(x, z);
			if (Rivers.Water(x, z, out double level))
			{
				h = Math.Max(h, level + lift);
			}
			return h;
		}

		// 1. At tile corners the far view's ground is the tiles' own vertex (or the water over it).
		double worstCorner = 0.0;
		int corners = 0;
		int per = (int)Math.Round(TileSize / FarGrid.Step);
		for (int j = 0; j < n; j += per * 3)
		{
			for (int i = 0; i < n; i += per * 3)
			{
				double x = FarGrid.At(i), z = FarGrid.At(j);
				worstCorner = Math.Max(worstCorner, Math.Abs(FarGrid.Ground[j * n + i] - Real(x, z)));
				corners++;
			}
		}
		bool cornerOk = worstCorner < 0.05;
		Console.WriteLine("farview: " + corners + " tile corners, the far view off the tiles' vertex by at most " + worstCorner.ToString("0.000") + (cornerOk ? "  ok" : "  FAIL"));
		ok &= cornerOk;

		// 2. Along tile edges: how far the far view's straight edge (with its canopy) stands over the real surface between its samples.
		List<double> gaps = new List<double>();
		double worst = double.MinValue, wx = 0.0, wz = 0.0;
		double short_ = double.MinValue;
		int deeper = 0, pieces = 0;
		int tiles = (int)Math.Round(WorldGen.Half * 2.0 / TileSize);
		for (int tj = 1; tj < tiles; tj += 3)
		{
			for (int ti = 1; ti < tiles; ti += 3)
			{
				for (int edge = 0; edge < 2; edge++)
				{
					// The tile's south edge (along x) or its west edge (along z), sample by 5-unit vertex.
					int gi = ti * per, gj = tj * per;
					for (int q = 0; q < per; q++)
					{
						int ai = edge == 0 ? gi + q : gi, aj = edge == 0 ? gj : gj + q;
						int bi = edge == 0 ? ai + 1 : ai, bj = edge == 0 ? aj : aj + 1;
						float a = FarGrid.Top[aj * n + ai], b = FarGrid.Top[bj * n + bi];
						float skirt = FarGrid.SkirtAt(ai, aj, bi, bj);
						pieces++;
						if (skirt > FarGrid.Skirt)
						{
							deeper++;
						}
						for (int k = 0; k < 10; k++)
						{
							double f = k / 10.0;
							double x = FarGrid.At(ai) + (FarGrid.At(bi) - FarGrid.At(ai)) * f, z = FarGrid.At(aj) + (FarGrid.At(bj) - FarGrid.At(aj)) * f;
							double gap = a + (b - a) * f - Real(x, z);
							gaps.Add(gap);
							short_ = Math.Max(short_, gap - skirt);
							if (gap > worst)
							{
								worst = gap;
								wx = x;
								wz = z;
							}
						}
					}
				}
			}
		}
		gaps.Sort();
		bool edgeOk = short_ < -0.5;
		Console.WriteLine("farview: " + gaps.Count + " points on tile edges, the far view's edge over the real surface: median " + gaps[gaps.Count / 2].ToString("0.00") + ", 99% " + gaps[(int)(gaps.Count * 0.99)].ToString("0.00")
			+ ", worst " + worst.ToString("0.00") + " at (" + wx.ToString("0") + ", " + wz.ToString("0") + "); skirts " + FarGrid.Skirt.ToString("0") + ", deeper on " + deeper + " of " + pieces
			+ " pieces; the skirt's foot over the surface by at most " + short_.ToString("0.00") + (edgeOk ? "  ok" : "  FAIL: a gap would show under the skirt"));
		ok &= edgeOk;

		// 3. What the picture shows.
		byte[] c = FarGrid.Colours;
		int water = 0, grey = 0;
		for (int o = 0; o < c.Length; o += 4)
		{
			if (c[o] == 44 && c[o + 1] == 79 && c[o + 2] == 120)
			{
				water++;
			}
			else if (Math.Abs(c[o] - c[o + 1]) < 6 && Math.Abs(c[o + 1] - c[o + 2]) < 8 && c[o] < 140)
			{
				grey++;
			}
		}
		int pixels = c.Length / 4;
		float woods = 0f;
		for (int k = 0; k < FarGrid.Top.Length; k++)
		{
			woods += (FarGrid.Top[k] - FarGrid.Ground[k]) / FarGrid.Canopy;
		}
		Console.WriteLine("farview: picture " + (100.0 * water / pixels).ToString("0.0") + "% water, " + (100.0 * grey / pixels).ToString("0.0") + "% grey (roads, towns, rock); canopy over " + (100.0 * woods / FarGrid.Top.Length).ToString("0.0") + "% of the grid");
		return ok;
	}
}
