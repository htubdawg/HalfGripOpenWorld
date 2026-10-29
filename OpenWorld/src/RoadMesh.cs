using System;
using System.Collections.Generic;
using UnityEngine;

namespace HalfGripOpenWorld
{
	/// <summary>One tile's road surfaces, worked out on a worker thread: vertices relative to the tile's corner, one triangle list per material.</summary>
	internal sealed class RoadMeshData
	{
		internal Vector3[] Vertices;
		internal Vector3[] Normals;
		internal Vector2[] Uv;
		internal int[][] Triangles;
	}

	/// <summary>
	/// The road surfaces: a ribbon per road, cut into the pieces each tile draws (<see cref="Network.PiecesIn"/>), with a pixel-art
	/// texture per class made in code to match the game's look. Bridges get concrete sides and parapets. The ribbon's corners come from
	/// the road's samples and their sideways direction, worked out the same way in every tile, so pieces meet exactly across tile edges.
	/// Farm tracks are a mesh of their own (they are loose dirt, so they get their own grip).
	/// </summary>
	internal static class RoadMesh
	{
		/// <summary>Length of road one texture repeat covers (and the dash pattern's period).</summary>
		internal const float TextureLength = 40f;

		/// <summary>Materials of the paved mesh: one per paved class (Trunk to Lane), then the bridges' concrete.</summary>
		internal const int Concrete = 5;

		/// <summary>
		/// How far each class's surface sits above its road line: bigger roads higher, so where two surfaces overlap at a junction the
		/// bigger road is cleanly on top (as its markings would be); small enough to drive over.
		/// </summary>
		private static readonly float[] Lift = { 0.20f, 0.16f, 0.12f, 0.08f, 0.05f, 0.02f };

		/// <summary>The tile's paved roads (<paramref name="tracks"/> false) or its farm tracks (true); null if it has none.</summary>
		internal static RoadMeshData Build(int tx, int tz, float tileSize, bool tracks)
		{
			List<long> pieces = Network.PiecesIn(tx, tz, tileSize);
			if (pieces == null || pieces.Count == 0)
			{
				return null;
			}
			double ox = tx * (double)tileSize;
			double oz = tz * (double)tileSize;
			float lift = (float)Network.Lift;
			List<Vector3> vertices = new List<Vector3>();
			List<Vector3> normals = new List<Vector3>();
			List<Vector2> uv = new List<Vector2>();
			List<int>[] triangles = new List<int>[tracks ? 1 : Concrete + 1];
			for (int k = 0; k < triangles.Length; k++)
			{
				triangles[k] = new List<int>();
			}
			Vector3[] c = new Vector3[4];
			foreach (long piece in pieces)
			{
				Network.Road road = Network.Roads[(int)(piece >> 32)];
				if ((road.Kind == RoadKind.Track) != tracks)
				{
					continue;
				}
				int i = (int)(uint)piece;
				float up = Lift[(int)road.Kind] + lift;
				// Corners: 0 left and 1 right at sample i, 2 left and 3 right at i + 1.
				for (int end = 0; end < 2; end++)
				{
					int k = i + end;
					Side(road, k, out double sx, out double sz);
					float y = (float)road.Y[k] + up;
					float x = (float)(road.X[k] - ox);
					float z = (float)(road.Z[k] - oz);
					float hx = (float)(sx * road.Half);
					float hz = (float)(sz * road.Half);
					c[end * 2] = new Vector3(x + hx, y, z + hz);
					c[end * 2 + 1] = new Vector3(x - hx, y, z - hz);
				}
				float v0 = (float)(road.S[i] / TextureLength), v1 = (float)(road.S[i + 1] / TextureLength);
				List<int> list = triangles[tracks ? 0 : (int)road.Kind];
				Quad(vertices, normals, uv, list, c[0], c[2], c[1], c[3], new Vector2(0f, v0), new Vector2(0f, v1), new Vector2(1f, v0), new Vector2(1f, v1), true);
				if (!tracks && road.Bridge[i] && road.Bridge[i + 1])
				{
					// The deck's sides, 3 deep, and a parapet 1.4 high along each edge, seen from both sides.
					List<int> concrete = triangles[Concrete];
					Vector3 down = Vector3.down * 3f, upward = Vector3.up * 1.4f;
					Wall(vertices, normals, uv, concrete, c[0] + down, c[2] + down, c[0], c[2]);
					Wall(vertices, normals, uv, concrete, c[1] + down, c[3] + down, c[1], c[3]);
					Wall(vertices, normals, uv, concrete, c[0], c[2], c[0] + upward, c[2] + upward);
					Wall(vertices, normals, uv, concrete, c[1], c[3], c[1] + upward, c[3] + upward);
				}
			}
			if (vertices.Count == 0)
			{
				return null;
			}
			int[][] tris = new int[triangles.Length][];
			for (int k = 0; k < triangles.Length; k++)
			{
				tris[k] = triangles[k].ToArray();
			}
			return new RoadMeshData { Vertices = vertices.ToArray(), Normals = normals.ToArray(), Uv = uv.ToArray(), Triangles = tris };
		}

		/// <summary>A quad a-b (one edge) to c-d (the other), facing up if <paramref name="up"/>.</summary>
		private static void Quad(List<Vector3> v, List<Vector3> n, List<Vector2> uv, List<int> tris, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud, bool up)
		{
			int i = v.Count;
			v.Add(a);
			v.Add(b);
			v.Add(c);
			v.Add(d);
			uv.Add(ua);
			uv.Add(ub);
			uv.Add(uc);
			uv.Add(ud);
			Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
			if (up && normal.y < 0f)
			{
				normal = -normal;
			}
			n.Add(normal);
			n.Add(normal);
			n.Add(normal);
			n.Add(normal);
			Triangle(tris, v, i, i + 1, i + 2, normal);
			Triangle(tris, v, i + 2, i + 1, i + 3, normal);
		}

		/// <summary>An upright strip from the bottom edge (a, b) to the top edge (c, d), drawn on both sides.</summary>
		private static void Wall(List<Vector3> v, List<Vector3> n, List<Vector2> uv, List<int> tris, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
		{
			Vector2 u0 = new Vector2(0f, 0f), u1 = new Vector2(1f, 0f), u2 = new Vector2(0f, 1f), u3 = new Vector2(1f, 1f);
			for (int side = 0; side < 2; side++)
			{
				int i = v.Count;
				v.Add(a);
				v.Add(b);
				v.Add(c);
				v.Add(d);
				uv.Add(u0);
				uv.Add(u1);
				uv.Add(u2);
				uv.Add(u3);
				Vector3 normal = Vector3.Cross(b - a, c - a).normalized * (side == 0 ? 1f : -1f);
				n.Add(normal);
				n.Add(normal);
				n.Add(normal);
				n.Add(normal);
				Triangle(tris, v, i, i + 1, i + 2, normal);
				Triangle(tris, v, i + 2, i + 1, i + 3, normal);
			}
		}

		/// <summary>Adds a triangle facing along <paramref name="facing"/> (Unity draws clockwise as seen from the front).</summary>
		private static void Triangle(List<int> list, List<Vector3> v, int a, int b, int c, Vector3 facing)
		{
			Vector3 n = Vector3.Cross(v[b] - v[a], v[c] - v[a]);
			list.Add(a);
			if (Vector3.Dot(n, facing) >= 0f)
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

		// ---- looks -------------------------------------------------------------------------------------------------

		/// <summary>The paved mesh's materials (Trunk to Lane, then concrete), on the given lit shader.</summary>
		internal static Material[] Materials(Shader shader)
		{
			Color white = new Color(0.86f, 0.86f, 0.82f);
			return new[]
			{
				Make(shader, Trunk(), "OpenWorld Trunk Road"),
				Make(shader, TwoLane(28, new Color(0.29f, 0.29f, 0.31f), 2, white, 25), "OpenWorld Main Road"),
				Make(shader, TwoLane(24, new Color(0.31f, 0.31f, 0.32f), 1, white, 15), "OpenWorld Minor Road"),
				Make(shader, TwoLane(20, new Color(0.26f, 0.26f, 0.28f), -1, white, 12), "OpenWorld Street"),
				Make(shader, Lane(), "OpenWorld Lane"),
				Make(shader, Blank(8, 8, new Color(0.58f, 0.57f, 0.54f), true), "OpenWorld Concrete"),
			};
		}

		/// <summary>The farm tracks' material: dirt with a strip of grass down the middle.</summary>
		internal static Material TrackMaterial(Shader shader)
		{
			Texture2D t = Blank(9, (int)TextureLength, new Color(0.47f, 0.38f, 0.27f), false);
			for (int y = 0; y < t.height; y++)
			{
				t.SetPixel(4, y, new Color(0.36f, 0.48f, 0.24f));
				t.SetPixel(0, y, new Color(0.40f, 0.46f, 0.26f));
				t.SetPixel(8, y, new Color(0.40f, 0.46f, 0.26f));
			}
			t.Apply();
			return Make(shader, t, "OpenWorld Track");
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
		private static Texture2D Trunk()
		{
			Color asphalt = new Color(0.27f, 0.27f, 0.29f);
			Color white = new Color(0.86f, 0.86f, 0.82f);
			Color yellow = new Color(0.85f, 0.70f, 0.20f);
			Texture2D t = Blank(66, (int)TextureLength, asphalt, false);
			Fill(t, 28, 38, new Color(0.55f, 0.55f, 0.52f));
			Line(t, 4, white, 40);
			Line(t, 15, white, 15);
			Line(t, 27, yellow, 40);
			Line(t, 38, yellow, 40);
			Line(t, 50, white, 15);
			Line(t, 61, white, 40);
			t.Apply();
			return t;
		}

		/// <summary>A two-lane road <paramref name="width"/> across: edge lines <paramref name="edge"/> in from each side (none if negative), a centre line dashed <paramref name="dash"/> of 40.</summary>
		private static Texture2D TwoLane(int width, Color asphalt, int edge, Color paint, int dash)
		{
			Texture2D t = Blank(width, (int)TextureLength, asphalt, false);
			Line(t, width / 2, paint, dash);
			if (edge >= 0)
			{
				Line(t, edge, paint, 40);
				Line(t, width - 1 - edge, paint, 40);
			}
			t.Apply();
			return t;
		}

		/// <summary>A single-track lane (15 across): worn asphalt, no markings, grass creeping in at the edges.</summary>
		private static Texture2D Lane()
		{
			Texture2D t = Blank(15, (int)TextureLength, new Color(0.33f, 0.33f, 0.32f), false);
			for (int y = 0; y < t.height; y++)
			{
				if ((Noise.Mix((ulong)y * 31UL) >> 62) == 0)
				{
					t.SetPixel(0, y, new Color(0.38f, 0.46f, 0.26f));
				}
				if ((Noise.Mix((ulong)y * 57UL + 3UL) >> 62) == 0)
				{
					t.SetPixel(14, y, new Color(0.38f, 0.46f, 0.26f));
				}
			}
			t.Apply();
			return t;
		}

		/// <summary>A texture of one colour with a little grain, point-filtered like the game's own, repeating along the road.</summary>
		private static Texture2D Blank(int w, int h, Color c, bool apply)
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
			if (apply)
			{
				t.Apply();
			}
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

		/// <summary>A one-pixel line along the road at column x, on for <paramref name="on"/> of every 40.</summary>
		private static void Line(Texture2D t, int x, Color c, int on)
		{
			for (int y = 0; y < t.height; y++)
			{
				if (y < on)
				{
					t.SetPixel(x, y, c);
				}
			}
		}
	}
}
