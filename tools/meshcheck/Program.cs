using System;
using System.Collections.Generic;
using HalfGripOpenWorld;
using UnityEngine;

// Checks of the built world that the preview tool cannot make (they need Unity's types and the mesh builders), outside the game:
//   dotnet run -c Release --project tools/meshcheck [-p:GameDir=<game folder>]
//   1. clip:    road surfaces under the terrain mesh (sampled across every road, against the 5-unit triangles the game draws)
//   2. poke:    a smaller road's surface above a bigger road's where it lies on its paving (junctions)
//   3. float:   road centres floating more than 0.5 over the ground
//   4. bases:   building bases round the main town: never above the ground, at most the slope allowance under it
//   5. faces:   road, track and water meshes with no upside-down flat faces, under 65000 vertices
// Exits 1 if a check fails. Keep the surface lifts below in step with RoadMesh.
internal static class Program
{
	private static readonly float[] Lift = { 0.20f, 0.16f, 0.12f, 0.08f, 0.05f, 0.02f };

	private static int Main()
	{
		Network.Ensure();
		bool ok = true;
		ok &= Clip();
		ok &= Poke();
		ok &= Float();
		ok &= Bases();
		ok &= Faces();
		Console.WriteLine(ok ? "all checks pass" : "SOME CHECKS FAIL");
		return ok ? 0 : 1;
	}

	/// <summary>The terrain mesh's height at world (x, z): the 5-unit grid's triangles, as the streamer builds them.</summary>
	private static double Mesh(double x, double z)
	{
		double gx = Math.Floor(x / 5.0), gz = Math.Floor(z / 5.0), fx = x / 5.0 - gx, fz = z / 5.0 - gz;
		double a = WorldGen.Height(gx * 5, gz * 5), b = WorldGen.Height(gx * 5 + 5, gz * 5);
		double c = WorldGen.Height(gx * 5, gz * 5 + 5), d = WorldGen.Height(gx * 5 + 5, gz * 5 + 5);
		return fx + fz <= 1.0 ? a + (b - a) * fx + (c - a) * fz : d + (c - d) * (1.0 - fx) + (b - d) * (1.0 - fz);
	}

	private static bool Clip()
	{
		int samples = 0, bad = 0;
		double worst = 0;
		foreach (Network.Road r in Network.Roads)
		{
			for (int i = 0; i + 1 < r.Count; i += 2)
			{
				if (r.Bridge[i])
				{
					continue;
				}
				double tx = r.X[i + 1] - r.X[i], tz = r.Z[i + 1] - r.Z[i], len = Math.Sqrt(tx * tx + tz * tz);
				if (len <= 0)
				{
					continue;
				}
				foreach (double f in new[] { -0.95, -0.5, 0.0, 0.5, 0.95 })
				{
					double x = r.X[i] - tz / len * r.Half * f, z = r.Z[i] + tx / len * r.Half * f;
					double over = Mesh(x, z) - (r.Y[i] + Lift[(int)r.Kind] + Network.Lift);
					samples++;
					if (over > -0.02)
					{
						bad++;
						worst = Math.Max(worst, over);
					}
				}
			}
		}
		Console.WriteLine("clip:  " + bad + " of " + samples + " road surface samples under the terrain, worst " + worst.ToString("0.00"));
		return bad * 1000 < samples && worst < 0.5;
	}

	private static bool Poke()
	{
		int overlaps = 0, poke = 0;
		double worst = 0;
		foreach (Network.Road r in Network.Roads)
		{
			for (int i = 0; i + 1 < r.Count; i += 2)
			{
				foreach (Network.Road o in Network.Roads)
				{
					if (o == r || o.Kind >= r.Kind)
					{
						continue;
					}
					int bj = -1;
					double bd = double.MaxValue, bt = 0;
					for (int j = 0; j + 1 < o.Count; j++)
					{
						if (Math.Abs(o.X[j] - r.X[i]) > 60 || Math.Abs(o.Z[j] - r.Z[i]) > 60)
						{
							continue;
						}
						Lines.Project(o.X, o.Z, j, r.X[i], r.Z[i], out double t, out double d);
						if (d < bd)
						{
							bd = d;
							bj = j;
							bt = t;
						}
					}
					if (bj < 0 || bd >= o.Half)
					{
						continue;
					}
					overlaps++;
					double above = r.Y[i] + Lift[(int)r.Kind] - (o.Y[bj] + (o.Y[bj + 1] - o.Y[bj]) * bt + Lift[(int)o.Kind]);
					if (above > 0)
					{
						poke++;
						worst = Math.Max(worst, above);
					}
				}
			}
		}
		Console.WriteLine("poke:  " + poke + " of " + overlaps + " points of a smaller road on a bigger road's paving poke through it, worst " + worst.ToString("0.00"));
		return worst < 0.1;
	}

	private static bool Float()
	{
		int total = 0, bad = 0;
		foreach (Network.Road r in Network.Roads)
		{
			for (int i = 0; i < r.Count; i += 3)
			{
				if (r.Bridge[i])
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
		Console.WriteLine("float: " + bad + " of " + total + " road centres more than 0.5 over the ground");
		return bad * 1000 < total;
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
		Console.WriteLine("bases: " + n + " buildings round " + town.Name + ", base at most " + sunk.ToString("0.0") + " under the ground, " + raised.ToString("0.0") + " above");
		return sunk < 7.0 && raised < 0.1;
	}

	private static bool Faces()
	{
		int down = 0, most = 0;
		for (int tz = -64; tz < 64; tz++)
		{
			for (int tx = -64; tx < 64; tx++)
			{
				foreach (RoadMeshData d in new[] { RoadMesh.Build(tx, tz, 250f, false), RoadMesh.Build(tx, tz, 250f, true), WaterMesh.Build(tx, tz, 250f) })
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
		Console.WriteLine("faces: " + down + " upside-down flat faces in road, track and water meshes; most vertices in one mesh " + most);
		return down == 0 && most < 65000;
	}
}
