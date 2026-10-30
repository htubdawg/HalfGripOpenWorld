using System;
using System.Collections.Generic;
using HalfGripOpenWorld;
using UnityEngine;

// Checks of the built world that the preview tool cannot make (they need Unity's types and the mesh builders), outside the game:
//   dotnet run -c Release --project tools/meshcheck [-p:GameDir=<game folder>]
//   1. clip:    road surfaces under the terrain mesh (sampled across every road, against the 5-unit triangles the game draws)
//   2. poke:    where two drawn road surfaces overlap (more than half a unit in), the one that should be under (the smaller, or the
//               later of one class) poking through the other (junctions, crossings), by more than 0.02
//   3. float:   road centres floating more than 0.5 over the ground
//   4. grades:  drawn road pieces no steeper than their class's grade allows (Network.Leeway near junctions)
//   5. bridges: every point of a road surface over a river's water at least 4 above the water
//   6. verges:  the step from a verge's outer edge to the ground (its inner edge is the road's edge by construction), under 0.1
//   7. tracks:  the ground along farm tracks pressed into their bed, 0.3 under the track's line
//   8. bases:   building bases round the main town: never above the ground, at most the slope allowance under it
//   9. faces:   road, verge and water meshes with no upside-down flat faces, under 65000 vertices
//  11. ribbons:  farm tracks' and footpaths' ribbons over the drawn ground everywhere across them (away from paved roads, which
//                they run under)
//  12. sidewalks: a sidewalk's back on or over the ground, its kerb's foot under it
//  10. lots:    car parks, yards and forecourts: the drawn ground just under their surface (0.02 to 0.3), their front at the foot of
//               the road's verge (within 0.25), clear of every other road; a retail park and businesses laid out
// Exits 1 if a check fails.
internal static class Program
{
	private static readonly TerrainGrid Terrain = new TerrainGrid(0, 0, 0, null);

	private static int Main()
	{
		Network.Ensure();
		bool ok = true;
		ok &= Clip();
		ok &= Poke();
		ok &= Float();
		ok &= Grades();
		ok &= Bridges();
		ok &= Water();
		ok &= Verges();
		ok &= Tracks();
		ok &= Bases();
		ok &= Faces();
		ok &= Lots();
		ok &= Ribbons();
		ok &= Sidewalks();
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
	private static readonly double[] Grade = { 0.05, 0.07, 0.09, 0.11, 0.11, 0.14, 0.18 };

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
				foreach (RoadMeshData d in new[] { road, verges, sidewalks, tracks, WaterMesh.Build(tx, tz, 250f) })
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
		Console.WriteLine("faces:   " + down + " upside-down flat faces in road, verge, sidewalk, track and water meshes; most vertices in one mesh " + most);
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
					if (v > 0.0 && !Network.Claims(x, z, lot.Road, Network.Verge + v, 2.0))
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
}
