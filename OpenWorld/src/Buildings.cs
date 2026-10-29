using System;
using System.Collections.Generic;
using UnityEngine;

namespace HalfGripOpenWorld
{
	/// <summary>
	/// Buildings, made in code in the game's low-poly pixel style: houses along the roads of every town and village, and a farmhouse,
	/// a barn and a shed at every farm. Worked out per tile on a worker thread, all of a tile's buildings in one mesh (a submesh per
	/// material) with one collider.
	///
	/// Houses stand on plots along each road, one every so far along it (counted from the road's start, so every plot is decided once,
	/// by the tile its road piece is in, and the same on every machine): terraced, taller and close to the road in a town's middle,
	/// detached and set back further out and in villages. A plot is only built on if the house fits: every corner nearer its own road
	/// than any other (each road gets its own side of every block, so houses on different streets never overlap), off every road,
	/// out of the water, and on ground that does not fall more than 6 across it.
	/// </summary>
	internal static class Buildings
	{
		/// <summary>Materials, in submesh order: five walls, three pitched roofs, a flat roof.</summary>
		internal const int Brick = 0, Render = 1, White = 2, Stone = 3, Wood = 4, RedTile = 5, Slate = 6, Metal = 7, Flat = 8, Count = 9;

		/// <summary>A storey's height (3 m), and a bay's width (one window).</summary>
		private const float Storey = 10f;
		private const float Bay = 8f;

		internal sealed class Data
		{
			internal RoadMeshData Mesh;
			/// <summary>World x, z and radius of every building, so trees and grass keep off them.</summary>
			internal List<(double x, double z, double r)> Footprints = new List<(double, double, double)>();
		}

		private static readonly ulong Salt = Noise.Mix(WorldGen.Seed + 601);

		internal static Data Build(int tx, int tz, float tileSize)
		{
			Data data = new Data();
			Builder b = new Builder(tx * (double)tileSize, tz * (double)tileSize);
			List<long> pieces = Network.PiecesIn(tx, tz, tileSize);
			if (pieces != null)
			{
				foreach (long piece in pieces)
				{
					int r = (int)(piece >> 32);
					Network.Road road = Network.Roads[r];
					if (road.Kind == RoadKind.Trunk || road.Kind == RoadKind.Track)
					{
						continue;
					}
					Plots(b, data, r, road, (int)(uint)piece);
				}
			}
			foreach (Places.Place p in Places.All)
			{
				if (p.Kind == PlaceKind.Hamlet && Math.Floor(p.X / tileSize) == tx && Math.Floor(p.Z / tileSize) == tz)
				{
					Farm(b, data, p);
				}
			}
			data.Mesh = b.Done();
			return data;
		}

		// ---- where -------------------------------------------------------------------------------------------------

		/// <summary>The plots along one piece of a road (between samples i and i + 1), both sides.</summary>
		private static void Plots(Builder b, Data data, int r, Network.Road road, int i)
		{
			double mx = (road.X[i] + road.X[i + 1]) * 0.5, mz = (road.Z[i] + road.Z[i + 1]) * 0.5;
			Places.Place place = Places.At(mx, mz);
			if (place == null || place.Kind == PlaceKind.Hamlet)
			{
				return;
			}
			bool town = place.Kind == PlaceKind.Town;
			double from = Math.Sqrt((mx - place.X) * (mx - place.X) + (mz - place.Z) * (mz - place.Z)) / place.Radius;
			bool core = town && from < 0.35;
			double spacing = core ? 26.0 : town ? 36.0 : 44.0;
			double s0 = road.S[i], s1 = road.S[i + 1];
			for (long k = (long)Math.Ceiling(s0 / spacing); k * spacing < s1; k++)
			{
				double t = (k * spacing - s0) / Math.Max(1e-6, s1 - s0);
				double px = road.X[i] + (road.X[i + 1] - road.X[i]) * t;
				double pz = road.Z[i] + (road.Z[i + 1] - road.Z[i]) * t;
				double len = Math.Max(1e-6, s1 - s0);
				double dx = (road.X[i + 1] - road.X[i]) / len, dz = (road.Z[i + 1] - road.Z[i]) / len;
				for (int side = -1; side <= 1; side += 2)
				{
					ulong h = Noise.Mix(Salt ^ (ulong)r * 0x9E3779B97F4A7C15UL ^ (ulong)k * 0xBF58476D1CE4E5B9UL ^ (ulong)(side + 2));
					double chance = core ? 0.95 : town ? 0.35 + 0.5 * (1.0 - from) : 0.7 * (1.0 - from * 0.6);
					if (Unit(h, 0) >= chance)
					{
						continue;
					}
					double width = core ? spacing - 0.5 : spacing * (0.55 + 0.25 * Unit(h, 1));
					double depth = core ? 26.0 + 6.0 * Unit(h, 2) : 20.0 + 8.0 * Unit(h, 2);
					int floors = core ? 2 + (int)(Unit(h, 3) * (from < 0.2 ? 4.0 : 3.0)) : 1 + (int)(Unit(h, 3) * 2.0);
					double setback = core ? 2.0 : 6.0 + 8.0 * Unit(h, 4);
					// Left of the road is (-dz, dx).
					double nx = -dz * side, nz = dx * side;
					double off = road.Half + setback + depth * 0.5;
					double cx = px + nx * off, cz = pz + nz * off;
					int wall = (int)(Unit(h, 5) * 4.0);
					int roof = floors >= 4 ? Flat : RedTile + (int)(Unit(h, 6) * 2.0);
					// The house's frame turned so its across axis points away from the road on this side.
					House(b, data, r, setback, cx, cz, dx * side, dz * side, width, depth, floors * Storey + 1.5f, wall, roof);
				}
			}
		}

		/// <summary>A farmhouse, a barn and a shed round a farm's yard, at angles from the farm's own hash.</summary>
		private static void Farm(Builder b, Data data, Places.Place p)
		{
			ulong h = Noise.Mix(Salt ^ (ulong)(long)Math.Floor(p.X) * 0x632BE59BUL ^ (ulong)(long)Math.Floor(p.Z));
			double a = Unit(h, 0) * 2.0 * DetMath.Pi;
			double ax = DetMath.Cos(a), az = DetMath.Sin(a);
			(double dist, double turn, double w, double d, float height, int wall, int roof)[] parts =
			{
				(45.0, 0.0, 24.0, 20.0, 2f * Storey + 1.5f, Unit(h, 1) < 0.5 ? Stone : Render, Unit(h, 2) < 0.5 ? Slate : RedTile),
				(55.0, 2.2, 50.0, 30.0, 16f, Wood, Metal),
				(48.0, 4.1, 18.0, 14.0, 9f, Wood, Metal),
			};
			foreach ((double dist, double turn, double w, double d, float height, int wall, int roof) in parts)
			{
				double ca = DetMath.Cos(a + turn), sa = DetMath.Sin(a + turn);
				House(b, data, -1, 0.0, p.X + ca * dist, p.Z + sa * dist, ax, az, w, d, height, wall, roof);
			}
		}

		/// <summary>
		/// One building, if it fits: <paramref name="w"/> along (dx, dz), <paramref name="d"/> across (to the left of (dx, dz), away
		/// from its road), <paramref name="height"/> to the eaves. <paramref name="own"/> is the road it fronts (-1 for none), its front
		/// <paramref name="front"/> from that road's edge.
		/// </summary>
		private static void House(Builder b, Data data, int own, double front, double cx, double cz, double dx, double dz, double w, double d, float height, int wall, int roof)
		{
			// Across the house, away from its road.
			double nx = -dz, nz = dx;
			double lo = double.MaxValue, hi = double.MinValue;
			for (int c = 0; c < 5; c++)
			{
				double u = c == 4 ? 0.0 : (c % 2 == 0 ? -0.5 : 0.5) * w;
				double v = c == 4 ? 0.0 : (c < 2 ? -0.5 : 0.5) * d;
				double x = cx + dx * u + nx * v, z = cz + dz * u + nz * v;
				// How far this corner is from its own road's edge: the front corners the setback, the back ones the setback and depth.
				if (!Network.Claims(x, z, own, front + (v > 0.0 ? d : (v < 0.0 ? 0.0 : d * 0.5)), 1.5))
				{
					return;
				}
				if (Rivers.Water(x, z, out double level) && WorldGen.Height(x, z) - Network.Lift < level + 1.0)
				{
					return;
				}
				double gh = WorldGen.Height(x, z);
				lo = Math.Min(lo, gh);
				hi = Math.Max(hi, gh);
			}
			if (hi - lo > 6.0)
			{
				return;
			}
			data.Footprints.Add((cx, cz, Math.Sqrt(w * w + d * d) * 0.5));
			b.Box(cx, cz, (float)(lo - 0.6), dx, dz, (float)w, (float)d, height + (float)(hi - lo), wall, roof);
		}

		/// <summary>A number from 0 to 1 from hash <paramref name="h"/>, the <paramref name="k"/>th of several.</summary>
		private static double Unit(ulong h, int k)
		{
			return (Noise.Mix(h + (ulong)k) >> 11) * (1.0 / 9007199254740992.0);
		}

		// ---- the mesh ----------------------------------------------------------------------------------------------

		/// <summary>Collects the boxes and roofs of a tile into one mesh, a triangle list per material.</summary>
		private sealed class Builder
		{
			private readonly double ox, oz;
			private readonly List<Vector3> v = new List<Vector3>();
			private readonly List<Vector3> n = new List<Vector3>();
			private readonly List<Vector2> uv = new List<Vector2>();
			private readonly List<int>[] tris = new List<int>[Count];

			internal Builder(double ox, double oz)
			{
				this.ox = ox;
				this.oz = oz;
				for (int k = 0; k < Count; k++)
				{
					tris[k] = new List<int>();
				}
			}

			/// <summary>
			/// A box standing on <paramref name="y"/>, <paramref name="w"/> along (dx, dz) and <paramref name="d"/> across, walls
			/// <paramref name="h"/> high, with a pitched roof (ridge along the width, gables at the ends) or a flat one.
			/// </summary>
			internal void Box(double cx, double cz, float y, double dx, double dz, float w, float d, float h, int wall, int roof)
			{
				float lift = (float)Network.Lift;
				Vector3 c = new Vector3((float)(cx - ox), y + lift, (float)(cz - oz));
				Vector3 along = new Vector3((float)dx, 0f, (float)dz);
				Vector3 across = new Vector3((float)-dz, 0f, (float)dx);
				Vector3 a = along * (w * 0.5f), b = across * (d * 0.5f), up = Vector3.up * h;
				Vector3 p0 = c - a - b, p1 = c + a - b, p2 = c + a + b, p3 = c - a + b;
				Wall(p0, p1, h, wall, -across);
				Wall(p1, p2, h, wall, along);
				Wall(p2, p3, h, wall, across);
				Wall(p3, p0, h, wall, -along);
				if (roof == Flat)
				{
					Quad(p0 + up, p1 + up, p3 + up, p2 + up, Vector3.up, roof, w / Bay, d / Bay);
					Quad(p0 + up, p1 + up, p3 + up, p2 + up, Vector3.down, roof, w / Bay, d / Bay);
					return;
				}
				// Pitched: the ridge runs along the width, over the middle, a little past the walls.
				// About 48 degrees, a usual pitch: shallower, and from the street the near slope hides behind the front wall's top.
				float rise = Mathf.Min(d * 0.55f, 16f);
				Vector3 eave = Vector3.up * h, ridge = Vector3.up * (h + rise);
				Vector3 over = across * 1.2f, end = along * 0.8f;
				Vector3 r0 = c - a - end + ridge, r1 = c + a + end + ridge;
				Vector3 e0 = p0 - end - over + eave, e1 = p1 + end - over + eave, e2 = p2 + end + over + eave, e3 = p3 - end + over + eave;
				float slope = Mathf.Sqrt(rise * rise + d * d * 0.25f);
				Vector3 out0 = (Vector3.up * (d * 0.5f) - across * rise).normalized, out1 = (Vector3.up * (d * 0.5f) + across * rise).normalized;
				Quad(e0, e1, r0, r1, out0, roof, (w + 1.6f) / Bay, slope / Bay);
				Quad(e3, e2, r0, r1, out1, roof, (w + 1.6f) / Bay, slope / Bay);
				// The undersides too: the eaves overhang the walls, and a roof drawn one side only is see-through from below.
				Quad(e0, e1, r0, r1, -out0, roof, (w + 1.6f) / Bay, slope / Bay);
				Quad(e3, e2, r0, r1, -out1, roof, (w + 1.6f) / Bay, slope / Bay);
				Triangle(p0 + eave, p3 + eave, c - a + ridge, -along, wall);
				Triangle(p1 + eave, p2 + eave, c + a + ridge, along, wall);
				Triangle(p0 + eave, p3 + eave, c - a + ridge, along, wall);
				Triangle(p1 + eave, p2 + eave, c + a + ridge, -along, wall);
			}

			/// <summary>A wall from bottom edge (p, q) up <paramref name="h"/>, facing <paramref name="facing"/>; a window to a bay and a storey.</summary>
			private void Wall(Vector3 p, Vector3 q, float h, int material, Vector3 facing)
			{
				float len = (q - p).magnitude;
				Quad(p, q, p + Vector3.up * h, q + Vector3.up * h, facing, material, Mathf.Max(1f, Mathf.Round(len / Bay)), h / Storey);
			}

			/// <summary>A quad a-b (bottom or one edge), c-d (the opposite edge), facing <paramref name="facing"/>, its texture repeated (u, v) times.</summary>
			private void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 facing, int material, float u, float vv)
			{
				int i = v.Count;
				v.Add(a);
				v.Add(b);
				v.Add(c);
				v.Add(d);
				uv.Add(new Vector2(0f, 0f));
				uv.Add(new Vector2(u, 0f));
				uv.Add(new Vector2(0f, vv));
				uv.Add(new Vector2(u, vv));
				for (int k = 0; k < 4; k++)
				{
					n.Add(facing);
				}
				Add(material, i, i + 1, i + 2, facing);
				Add(material, i + 2, i + 1, i + 3, facing);
			}

			private void Triangle(Vector3 a, Vector3 b, Vector3 c, Vector3 facing, int material)
			{
				int i = v.Count;
				v.Add(a);
				v.Add(b);
				v.Add(c);
				float w = (b - a).magnitude / Bay;
				uv.Add(new Vector2(0f, 0f));
				uv.Add(new Vector2(w, 0f));
				uv.Add(new Vector2(w * 0.5f, 0.6f));
				for (int k = 0; k < 3; k++)
				{
					n.Add(facing);
				}
				Add(material, i, i + 1, i + 2, facing);
			}

			private void Add(int material, int a, int b, int c, Vector3 facing)
			{
				List<int> list = tris[material];
				list.Add(a);
				if (Vector3.Dot(Vector3.Cross(v[b] - v[a], v[c] - v[a]), facing) >= 0f)
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

			internal RoadMeshData Done()
			{
				if (v.Count == 0)
				{
					return null;
				}
				int[][] t = new int[Count][];
				for (int k = 0; k < Count; k++)
				{
					t[k] = tris[k].ToArray();
				}
				return new RoadMeshData { Vertices = v.ToArray(), Normals = n.ToArray(), Uv = uv.ToArray(), Triangles = t };
			}
		}

		// ---- looks -------------------------------------------------------------------------------------------------

		/// <summary>The nine materials, in submesh order, on the given lit shader: pixel-art walls with a window to a bay and storey, and roofs.</summary>
		internal static Material[] Materials(Shader shader)
		{
			Material[] m = new Material[Count];
			m[Brick] = Make(shader, WallTexture(new Color(0.55f, 0.30f, 0.24f), true), "Brick");
			m[Render] = Make(shader, WallTexture(new Color(0.86f, 0.80f, 0.66f), false), "Render");
			m[White] = Make(shader, WallTexture(new Color(0.88f, 0.88f, 0.86f), false), "White");
			m[Stone] = Make(shader, WallTexture(new Color(0.58f, 0.56f, 0.52f), true), "Stone");
			m[Wood] = Make(shader, Planks(new Color(0.46f, 0.22f, 0.16f)), "Wood");
			m[RedTile] = Make(shader, Rows(new Color(0.62f, 0.27f, 0.20f)), "Red Tile");
			m[Slate] = Make(shader, Rows(new Color(0.30f, 0.32f, 0.36f)), "Slate");
			m[Metal] = Make(shader, Rows(new Color(0.48f, 0.50f, 0.50f)), "Metal");
			m[Flat] = Make(shader, Plain(new Color(0.40f, 0.40f, 0.40f)), "Flat Roof");
			return m;
		}

		private static Material Make(Shader shader, Texture2D texture, string name)
		{
			Material m = new Material(shader) { name = "OpenWorld " + name, hideFlags = HideFlags.HideAndDontSave };
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
			if (m.HasProperty("_Smoothness"))
			{
				m.SetFloat("_Smoothness", 0.05f);
			}
			return m;
		}

		private static Texture2D New(int w, int h)
		{
			return new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat, hideFlags = HideFlags.HideAndDontSave };
		}

		/// <summary>One bay of one storey (8 x 10 pixels): wall with a framed window, brick or stone with a course line.</summary>
		private static Texture2D WallTexture(Color wall, bool courses)
		{
			Texture2D t = New(8, 10);
			Color glass = new Color(0.18f, 0.22f, 0.28f), frame = new Color(0.92f, 0.92f, 0.90f);
			for (int y = 0; y < 10; y++)
			{
				for (int x = 0; x < 8; x++)
				{
					float grain = ((int)(Noise.Mix((ulong)(y * 8 + x) + (ulong)(wall.r * 1000f)) >> 61) - 3.5f) * 0.015f;
					Color c = new Color(wall.r + grain, wall.g + grain, wall.b + grain);
					if (courses && y % 3 == 0)
					{
						c *= 0.92f;
					}
					if (x >= 2 && x <= 5 && y >= 3 && y <= 7)
					{
						c = x == 2 || x == 5 || y == 3 || y == 7 ? frame : glass;
					}
					t.SetPixel(x, y, c);
				}
			}
			t.Apply();
			return t;
		}

		private static Texture2D Planks(Color wood)
		{
			Texture2D t = New(8, 10);
			for (int y = 0; y < 10; y++)
			{
				for (int x = 0; x < 8; x++)
				{
					t.SetPixel(x, y, x % 4 == 0 ? wood * 0.75f : wood);
				}
			}
			t.Apply();
			return t;
		}

		private static Texture2D Rows(Color roof)
		{
			Texture2D t = New(8, 8);
			for (int y = 0; y < 8; y++)
			{
				for (int x = 0; x < 8; x++)
				{
					t.SetPixel(x, y, y % 2 == 0 ? roof * 0.82f : roof);
				}
			}
			t.Apply();
			return t;
		}

		private static Texture2D Plain(Color c)
		{
			Texture2D t = New(4, 4);
			for (int y = 0; y < 4; y++)
			{
				for (int x = 0; x < 4; x++)
				{
					t.SetPixel(x, y, (x + y) % 3 == 0 ? c * 0.9f : c);
				}
			}
			t.Apply();
			return t;
		}
	}
}
