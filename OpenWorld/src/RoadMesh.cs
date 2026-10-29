using System;
using System.Collections.Generic;
using UnityEngine;

namespace HalfGripOpenWorld
{
	/// <summary>One tile's road surface, worked out on a worker thread: vertices relative to the tile's corner, one triangle list per road kind.</summary>
	internal sealed class RoadMeshData
	{
		internal Vector3[] Vertices;
		internal Vector3[] Normals;
		internal Vector2[] Uv;
		internal int[][] Triangles;
	}

	/// <summary>
	/// The road surfaces: a ribbon per road, cut into the pieces each tile draws (<see cref="Network.PiecesIn"/>), with a pixel-art
	/// texture per kind (lanes, edge lines, dashes) made in code to match the game's look. The ribbon's corners come from the road's
	/// samples and their sideways direction, worked out the same way in every tile, so pieces meet exactly across tile edges.
	/// </summary>
	internal static class RoadMesh
	{
		/// <summary>Length of road one texture repeat covers (and the dash pattern's period).</summary>
		internal const float TextureLength = 40f;

		/// <summary>
		/// How far each kind's surface sits above its road line. Different, so where two roads' surfaces overlap at a junction one is
		/// cleanly on top instead of flickering; small enough to drive over.
		/// </summary>
		private static float Lift(RoadKind kind)
		{
			return kind == RoadKind.Highway ? 0.06f : (kind == RoadKind.Rural ? 0.03f : 0f);
		}

		internal static RoadMeshData Build(int tx, int tz, float tileSize)
		{
			List<long> pieces = Network.PiecesIn(tx, tz, tileSize);
			if (pieces == null || pieces.Count == 0)
			{
				return null;
			}
			double ox = tx * (double)tileSize;
			double oz = tz * (double)tileSize;
			int n = pieces.Count;
			Vector3[] vertices = new Vector3[n * 4];
			Vector3[] normals = new Vector3[n * 4];
			Vector2[] uv = new Vector2[n * 4];
			List<int>[] triangles = { new List<int>(), new List<int>(), new List<int>() };
			int v = 0;
			foreach (long piece in pieces)
			{
				Network.Road road = Network.Roads[(int)(piece >> 32)];
				int i = (int)(uint)piece;
				float lift = Lift(road.Kind);
				for (int end = 0; end < 2; end++)
				{
					int k = i + end;
					Side(road, k, out double sx, out double sz);
					float y = (float)road.Y[k] + lift;
					float x = (float)(road.X[k] - ox);
					float z = (float)(road.Z[k] - oz);
					float hx = (float)(sx * road.Half);
					float hz = (float)(sz * road.Half);
					float along = (float)(road.S[k] / TextureLength);
					vertices[v + end * 2] = new Vector3(x + hx, y, z + hz);
					vertices[v + end * 2 + 1] = new Vector3(x - hx, y, z - hz);
					uv[v + end * 2] = new Vector2(0f, along);
					uv[v + end * 2 + 1] = new Vector2(1f, along);
				}
				// Corners: 0 left and 1 right at sample i, 2 left and 3 right at i + 1.
				Vector3 normal = Vector3.Cross(vertices[v + 2] - vertices[v], vertices[v + 1] - vertices[v]).normalized;
				if (normal.y < 0f)
				{
					normal = -normal;
				}
				for (int c = 0; c < 4; c++)
				{
					normals[v + c] = normal;
				}
				AddTriangle(triangles[(int)road.Kind], vertices, v, v + 2, v + 1);
				AddTriangle(triangles[(int)road.Kind], vertices, v + 1, v + 2, v + 3);
				v += 4;
			}
			return new RoadMeshData
			{
				Vertices = vertices,
				Normals = normals,
				Uv = uv,
				Triangles = new[] { triangles[0].ToArray(), triangles[1].ToArray(), triangles[2].ToArray() },
			};
		}

		/// <summary>The unit vector to the left of the road at sample k (from the direction between its neighbours).</summary>
		private static void Side(Network.Road road, int k, out double sx, out double sz)
		{
			int a = Math.Max(0, k - 1);
			int b = Math.Min(road.Count - 1, k + 1);
			double tx = road.X[b] - road.X[a];
			double tz = road.Z[b] - road.Z[a];
			double len = Math.Sqrt(tx * tx + tz * tz);
			if (len < 1e-9)
			{
				sx = 1.0;
				sz = 0.0;
				return;
			}
			sx = -tz / len;
			sz = tx / len;
		}

		/// <summary>Adds a triangle facing up (Unity draws clockwise as seen from the front).</summary>
		private static void AddTriangle(List<int> list, Vector3[] v, int a, int b, int c)
		{
			Vector3 n = Vector3.Cross(v[b] - v[a], v[c] - v[a]);
			list.Add(a);
			if (n.y >= 0f)
			{
				list.Add(b);
				list.Add(c);
			}
			else
			{
				list.Add(c);
				list.Add(b);
			}
		}

		// ---- looks -------------------------------------------------------------------------------------------------

		/// <summary>The three materials, in <see cref="RoadKind"/> order, on the given lit shader.</summary>
		internal static Material[] Materials(Shader shader)
		{
			return new[]
			{
				Make(shader, Highway(), "OpenWorld Highway"),
				Make(shader, TwoLane(true, new Color(0.30f, 0.30f, 0.32f)), "OpenWorld Rural Road"),
				Make(shader, TwoLane(false, new Color(0.25f, 0.25f, 0.27f)), "OpenWorld Street"),
			};
		}

		private static Material Make(Shader shader, Texture2D texture, string name)
		{
			Material m = new Material(shader) { name = name, hideFlags = HideFlags.HideAndDontSave };
			if (m.HasProperty("_BaseMap"))
			{
				m.SetTexture("_BaseMap", texture);
			}
			if (m.HasProperty("_MainTex"))
			{
				m.SetTexture("_MainTex", texture);
			}
			if (m.HasProperty("_BaseColor"))
			{
				m.SetColor("_BaseColor", Color.white);
			}
			if (m.HasProperty("_Color"))
			{
				m.SetColor("_Color", Color.white);
			}
			if (m.HasProperty("_Smoothness"))
			{
				m.SetFloat("_Smoothness", 0.05f);
			}
			if (m.HasProperty("_Metallic"))
			{
				m.SetFloat("_Metallic", 0f);
			}
			return m;
		}

		/// <summary>One pixel per unit across (66) and along (40): two carriageways of two lanes, shoulders, a concrete median.</summary>
		private static Texture2D Highway()
		{
			Color asphalt = new Color(0.27f, 0.27f, 0.29f);
			Color white = new Color(0.86f, 0.86f, 0.82f);
			Color yellow = new Color(0.85f, 0.70f, 0.20f);
			Color median = new Color(0.55f, 0.55f, 0.52f);
			Texture2D t = Blank(66, (int)TextureLength, asphalt);
			Fill(t, 28, 38, median);
			Line(t, 4, white, false);
			Line(t, 15, white, true);
			Line(t, 27, yellow, false);
			Line(t, 38, yellow, false);
			Line(t, 50, white, true);
			Line(t, 61, white, false);
			t.Apply();
			return t;
		}

		/// <summary>One pixel per unit (24 across): two lanes with a dashed centre line, and edge lines on rural roads.</summary>
		private static Texture2D TwoLane(bool edges, Color asphalt)
		{
			Color white = new Color(0.86f, 0.86f, 0.82f);
			Texture2D t = Blank(24, (int)TextureLength, asphalt);
			Line(t, 12, white, true);
			if (edges)
			{
				Line(t, 1, white, false);
				Line(t, 22, white, false);
			}
			t.Apply();
			return t;
		}

		/// <summary>A texture of one colour with a little grain, point-filtered like the game's own, repeating along the road.</summary>
		private static Texture2D Blank(int w, int h, Color c)
		{
			Texture2D t = new Texture2D(w, h, TextureFormat.RGBA32, false)
			{
				filterMode = FilterMode.Point,
				wrapModeU = TextureWrapMode.Clamp,
				wrapModeV = TextureWrapMode.Repeat,
				hideFlags = HideFlags.HideAndDontSave,
			};
			Color[] pixels = new Color[w * h];
			for (int i = 0; i < pixels.Length; i++)
			{
				float grain = ((int)(Noise.Mix((ulong)i) >> 61) - 3.5f) * 0.012f;
				pixels[i] = new Color(c.r + grain, c.g + grain, c.b + grain, 1f);
			}
			t.SetPixels(pixels);
			return t;
		}

		private static void Fill(Texture2D t, int x0, int x1, Color c)
		{
			for (int y = 0; y < t.height; y++)
			{
				for (int x = x0; x < x1; x++)
				{
					t.SetPixel(x, y, c);
				}
			}
		}

		/// <summary>A one-pixel line along the road at column x; dashed lines are on for 15 of every 40.</summary>
		private static void Line(Texture2D t, int x, Color c, bool dashed)
		{
			for (int y = 0; y < t.height; y++)
			{
				if (!dashed || y < 15)
				{
					t.SetPixel(x, y, c);
				}
			}
		}
	}
}
