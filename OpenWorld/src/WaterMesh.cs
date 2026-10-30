using System;
using System.Collections.Generic;

namespace HalfGripOpenWorld
{
	/// <summary>
	/// One tile's water, worked out on a worker thread: the river channels that fall in the tile as ribbons, and the lake and sea
	/// surfaces as flat squares on their 100-unit cells (and one cell round them, under the shore, so no edge shows), clipped to the tile;
	/// and the parks' ponds.
	/// </summary>
	internal static class WaterMesh
	{
		private static Dictionary<long, List<long>> pieces;
		private static readonly object gate = new object();

		internal static RoadMeshData Build(int tx, int tz, float tileSize)
		{
			Network.Ensure();
			double ox = tx * (double)tileSize, oz = tz * (double)tileSize;
			float lift = (float)Network.Lift;
			List<UnityEngine.Vector3> v = new List<UnityEngine.Vector3>();
			List<int> tris = new List<int>();

			List<long> mine;
			lock (gate)
			{
				if (pieces == null)
				{
					pieces = new Dictionary<long, List<long>>();
					for (int r = 0; r < Rivers.All.Count; r++)
					{
						Rivers.River river = Rivers.All[r];
						for (int i = 0; i + 1 < river.Count; i++)
						{
							long key = ((long)(int)Math.Floor((river.X[i] + river.X[i + 1]) * 0.5 / tileSize) << 32) | (uint)(int)Math.Floor((river.Z[i] + river.Z[i + 1]) * 0.5 / tileSize);
							if (!pieces.TryGetValue(key, out List<long> list))
							{
								list = new List<long>();
								pieces[key] = list;
							}
							list.Add(((long)r << 32) | (uint)i);
						}
					}
				}
				pieces.TryGetValue(((long)tx << 32) | (uint)tz, out mine);
			}
			if (mine != null)
			{
				foreach (long piece in mine)
				{
					Rivers.River river = Rivers.All[(int)(piece >> 32)];
					int i = (int)(uint)piece;
					int b = v.Count;
					for (int end = 0; end < 2; end++)
					{
						int k = i + end;
						int a0 = Math.Max(0, k - 1), a1 = Math.Min(river.Count - 1, k + 1);
						double dx = river.X[a1] - river.X[a0], dz = river.Z[a1] - river.Z[a0];
						double len = Math.Sqrt(dx * dx + dz * dz);
						double sx = len > 0.0 ? -dz / len : 1.0, sz = len > 0.0 ? dx / len : 0.0;
						double half = Rivers.Edge(river.Half[k]);
						float y = (float)river.W[k] + lift;
						v.Add(new UnityEngine.Vector3((float)(river.X[k] + sx * half - ox), y, (float)(river.Z[k] + sz * half - oz)));
						v.Add(new UnityEngine.Vector3((float)(river.X[k] - sx * half - ox), y, (float)(river.Z[k] - sz * half - oz)));
					}
					Up(tris, v, b, b + 2, b + 1);
					Up(tris, v, b + 1, b + 2, b + 3);
				}
			}

			// Lakes and sea, cell by cell.
			double size = Rivers.GridSize;
			int i0 = (int)Math.Floor((ox - Rivers.GridMin) / size), i1 = (int)Math.Floor((ox + tileSize - Rivers.GridMin) / size);
			int j0 = (int)Math.Floor((oz - Rivers.GridMin) / size), j1 = (int)Math.Floor((oz + tileSize - Rivers.GridMin) / size);
			for (int j = j0; j <= j1; j++)
			{
				for (int i = i0; i <= i1; i++)
				{
					double level = double.NaN;
					for (int dj = -1; dj <= 1; dj++)
					{
						for (int di = -1; di <= 1; di++)
						{
							double s = Surface(i + di, j + dj);
							if (!double.IsNaN(s) && (double.IsNaN(level) || s > level))
							{
								level = s;
							}
						}
					}
					if (double.IsNaN(level))
					{
						continue;
					}
					double x0 = Math.Max(ox, Rivers.GridMin + i * size), x1 = Math.Min(ox + tileSize, Rivers.GridMin + (i + 1) * size);
					double z0 = Math.Max(oz, Rivers.GridMin + j * size), z1 = Math.Min(oz + tileSize, Rivers.GridMin + (j + 1) * size);
					if (x1 <= x0 || z1 <= z0)
					{
						continue;
					}
					float y = (float)level + lift;
					int b = v.Count;
					v.Add(new UnityEngine.Vector3((float)(x0 - ox), y, (float)(z0 - oz)));
					v.Add(new UnityEngine.Vector3((float)(x0 - ox), y, (float)(z1 - oz)));
					v.Add(new UnityEngine.Vector3((float)(x1 - ox), y, (float)(z0 - oz)));
					v.Add(new UnityEngine.Vector3((float)(x1 - ox), y, (float)(z1 - oz)));
					Up(tris, v, b, b + 1, b + 2);
					Up(tris, v, b + 2, b + 1, b + 3);
				}
			}
			// The parks' ponds, each whole by the tile its middle is in: a disc drawn a little past the water's edge, under the ground.
			foreach (Parks.Park park in Parks.All)
			{
				if (!park.HasPond || Math.Floor(park.PondX / tileSize) != tx || Math.Floor(park.PondZ / tileSize) != tz)
				{
					continue;
				}
				float y = (float)park.PondLevel + lift;
				double r = park.PondR + Parks.PondDrawn;
				int b = v.Count;
				v.Add(new UnityEngine.Vector3((float)(park.PondX - ox), y, (float)(park.PondZ - oz)));
				for (int k = 0; k < 32; k++)
				{
					double a = k * DetMath.Pi / 16.0;
					v.Add(new UnityEngine.Vector3((float)(park.PondX + DetMath.Cos(a) * r - ox), y, (float)(park.PondZ + DetMath.Sin(a) * r - oz)));
				}
				for (int k = 0; k < 32; k++)
				{
					Up(tris, v, b, b + 1 + k, b + 1 + (k + 1) % 32);
				}
			}
			if (v.Count == 0)
			{
				return null;
			}
			UnityEngine.Vector3[] normals = new UnityEngine.Vector3[v.Count];
			UnityEngine.Vector2[] uv = new UnityEngine.Vector2[v.Count];
			for (int k = 0; k < v.Count; k++)
			{
				normals[k] = UnityEngine.Vector3.up;
				uv[k] = new UnityEngine.Vector2(v[k].x / 40f, v[k].z / 40f);
			}
			return new RoadMeshData { Vertices = v.ToArray(), Normals = normals, Uv = uv, Triangles = new[] { tris.ToArray() } };
		}

		private static double Surface(int i, int j)
		{
			return i < 0 || j < 0 || i >= Rivers.GridN || j >= Rivers.GridN ? double.NaN : Rivers.Surface[j * Rivers.GridN + i];
		}

		/// <summary>A triangle facing up.</summary>
		private static void Up(List<int> tris, List<UnityEngine.Vector3> v, int a, int b, int c)
		{
			UnityEngine.Vector3 n = UnityEngine.Vector3.Cross(v[b] - v[a], v[c] - v[a]);
			tris.Add(a);
			if (n.y >= 0f)
			{
				tris.Add(b);
				tris.Add(c);
			}
			else
			{
				tris.Add(c);
				tris.Add(b);
			}
		}
	}
}
