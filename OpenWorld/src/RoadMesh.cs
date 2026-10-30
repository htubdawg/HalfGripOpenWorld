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
	/// The drawn terrain's height (in the scene) at a world point: the 5-unit grid's triangles, split as the streamer splits them, from
	/// a tile's own height samples where it has them and <see cref="WorldGen.Height"/> beyond. For laying things exactly on the ground.
	/// </summary>
	internal sealed class TerrainGrid
	{
		internal const double Step = 5.0;
		private readonly double x0;
		private readonly double z0;
		private readonly int side;
		private readonly float[] h;

		/// <summary>Samples <paramref name="h"/> (<paramref name="side"/> a row, null for none) on the grid from world (x0, z0).</summary>
		internal TerrainGrid(double x0, double z0, int side, float[] h)
		{
			this.x0 = x0;
			this.z0 = z0;
			this.side = side;
			this.h = h;
		}

		private double Sample(double gx, double gz)
		{
			if (h != null)
			{
				int i = (int)Math.Round((gx - x0) / Step), j = (int)Math.Round((gz - z0) / Step);
				if (i >= 0 && j >= 0 && i < side && j < side)
				{
					return h[j * side + i];
				}
			}
			return (float)WorldGen.Height(gx, gz);
		}

		internal double At(double x, double z)
		{
			double gx = Math.Floor(x / Step), gz = Math.Floor(z / Step);
			double fx = x / Step - gx, fz = z / Step - gz;
			gx *= Step;
			gz *= Step;
			double a = Sample(gx, gz), b = Sample(gx + Step, gz), c = Sample(gx, gz + Step), d = Sample(gx + Step, gz + Step);
			return fx + fz <= 1.0 ? a + (b - a) * fx + (c - a) * fz : d + (c - d) * (1.0 - fx) + (b - d) * (1.0 - fz);
		}
	}

	/// <summary>
	/// The road surfaces: a ribbon per road, cut into the pieces each tile draws (<see cref="Network.PiecesIn"/>), with a pixel-art
	/// texture per class made in code to match the game's look. Bridges get concrete sides and parapets. The ribbon's corners come from
	/// <see cref="Network.Point"/>, worked out the same way in every tile, so pieces meet exactly across tile edges. Where a road ends on
	/// or crosses one drawn over it, it is cut out along the other's edges (<see cref="Network.Gap"/>), its mouth flared
	/// (<see cref="Network.HalfAt"/>). Every paved road has a gravel verge each side down to the ground (a mesh of its own: it has its own grip). Farm tracks
	/// have no mesh: they are pressed into the ground and painted on its texture.
	/// </summary>
	internal static class RoadMesh
	{
		/// <summary>Length of road one texture repeat covers (and the dash pattern's period).</summary>
		internal const float TextureLength = 40f;

		/// <summary>Materials of the paved mesh: one per paved class (Trunk to Lane), then the bridges' concrete.</summary>
		internal const int Concrete = 5;

		/// <summary>The verge's outer edge lies this far over the drawn ground, so it never flickers into it.</summary>
		private const double VergeLift = 0.03;

		/// <summary>Pieces within this distance of a mouth are cut finer (2 units), so the flare's curve is smooth.</summary>
		private const double Fine = 16.0;

		/// <summary>
		/// The tile's paved roads, and in <paramref name="verges"/> their verges; null if it has none. <paramref name="terrain"/> gives
		/// the drawn ground's height, which each verge comes down to.
		/// </summary>
		internal static RoadMeshData Build(int tx, int tz, float tileSize, TerrainGrid terrain, out RoadMeshData verges)
		{
			verges = null;
			List<long> pieces = Network.PiecesIn(tx, tz, tileSize);
			if (pieces == null || pieces.Count == 0)
			{
				return null;
			}
			Mesher roads = new Mesher(tx * (double)tileSize, tz * (double)tileSize, Concrete + 1);
			Mesher sides = new Mesher(tx * (double)tileSize, tz * (double)tileSize, 1);
			List<(double t0, double t1)> spans = new List<(double, double)>();
			List<(double apex, int side, double from, double to)> fans = new List<(double, int, double, double)>();
			foreach (long piece in pieces)
			{
				int r = (int)(piece >> 32);
				Network.Road road = Network.Roads[r];
				if (road.Kind == RoadKind.Track)
				{
					continue;
				}
				int i = (int)(uint)piece;
				bool bridge = road.Bridge[i] && road.Bridge[i + 1];
				bool verge = !road.Bridge[i] && !road.Bridge[i + 1];
				int cuts = NearCut(road, i) ? 4 : 1;
				Network.Spans(road, i, spans);
				foreach ((double t0, double t1) in spans)
				{
					for (int c = 0; c < cuts; c++)
					{
						double a = t0 + (t1 - t0) * c / cuts, b = c == cuts - 1 ? t1 : t0 + (t1 - t0) * (c + 1) / cuts;
						Strip(roads, road, i, a, b, bridge);
						if (verge)
						{
							Verge(sides, road, r, i, a, b, 1, terrain);
							Verge(sides, road, r, i, a, b, -1, terrain);
						}
					}
				}
				Network.Fans(road, i, fans);
				foreach ((double apex, int side, double from, double to) in fans)
				{
					Fan(roads, road, apex, side, from, to);
				}
			}
			verges = sides.Done();
			return roads.Done();
		}

		/// <summary>Whether piece i is within <see cref="Fine"/> of a cut, where the mouth flares.</summary>
		private static bool NearCut(Network.Road road, int i)
		{
			foreach (Network.Gap g in road.Gaps)
			{
				if (road.S[i] < g.C1 + Fine && road.S[i + 1] > g.C0 - Fine)
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>The road's surface over fractions a to b of piece i, and a bridge's sides and parapets.</summary>
		private static void Strip(Mesher m, Network.Road road, int i, double a, double b, bool bridge)
		{
			Vector3 la = m.At(road, i, a, 1, 0.0), lb = m.At(road, i, b, 1, 0.0);
			Vector3 ra = m.At(road, i, a, -1, 0.0), rb = m.At(road, i, b, -1, 0.0);
			float va = V(road, i, a), vb = V(road, i, b);
			m.Quad((int)road.Kind, la, lb, ra, rb, new Vector2(0f, va), new Vector2(0f, vb), new Vector2(1f, va), new Vector2(1f, vb));
			if (bridge)
			{
				// The deck's sides, 3 deep, and a parapet 1.4 high along each edge, seen from both sides.
				Vector3 down = Vector3.down * 3f, upward = Vector3.up * 1.4f;
				m.Wall(Concrete, la + down, lb + down, la, lb);
				m.Wall(Concrete, ra + down, rb + down, ra, rb);
				m.Wall(Concrete, la, lb, la + upward, lb + upward);
				m.Wall(Concrete, ra, rb, ra + upward, rb + upward);
			}
		}

		private static float V(Network.Road road, int i, double t)
		{
			return (float)((road.S[i] * (1.0 - t) + road.S[i + 1] * t) / TextureLength);
		}

		/// <summary>
		/// The slanted end of a cut, where one edge is cut further along the road than the other: a fan from the corner where the
		/// full-width surface ends (<paramref name="apex"/>, on <paramref name="side"/>) over the other edge's points from
		/// <paramref name="from"/> to <paramref name="to"/>, in 2-unit steps.
		/// </summary>
		private static void Fan(Mesher m, Network.Road road, double apex, int side, double from, double to)
		{
			if (to <= from)
			{
				return;
			}
			Network.Locate(road, apex, out int ai, out double at);
			Vector3 tip = m.At(road, ai, at, side, 0.0);
			float va = V(road, ai, at);
			float ua = side > 0 ? 0f : 1f, uo = 1f - ua;
			int steps = Math.Max(1, (int)Math.Ceiling((to - from) / 2.0));
			Network.Locate(road, from, out int pi, out double pt);
			Vector3 prev = m.At(road, pi, pt, -side, 0.0);
			float pv = V(road, pi, pt);
			for (int k = 1; k <= steps; k++)
			{
				double s = k == steps ? to : from + (to - from) * k / steps;
				Network.Locate(road, s, out int ni, out double nt);
				Vector3 next = m.At(road, ni, nt, -side, 0.0);
				float nv = V(road, ni, nt);
				m.Triangle((int)road.Kind, tip, prev, next, new Vector2(ua, va), new Vector2(uo, pv), new Vector2(uo, nv));
				prev = next;
				pv = nv;
			}
		}

		/// <summary>
		/// The verge on one side over fractions a to b of piece i: from the road's edge to <see cref="Network.Verge"/> beyond it, where it
		/// lies just over the drawn ground, with a middle row that never dips under the ground. None where it would lie on another road.
		/// </summary>
		private static void Verge(Mesher m, Network.Road road, int r, int i, double a, double b, int side, TerrainGrid terrain)
		{
			Section(m, road, i, a, side, terrain, out Vector3 ia, out Vector3 ma, out Vector3 oa, out double xa, out double za);
			Section(m, road, i, b, side, terrain, out Vector3 ib, out Vector3 mb, out Vector3 ob, out double xb, out double zb);
			if (Network.OnOtherPaving(xa, za, r) || Network.OnOtherPaving(xb, zb, r))
			{
				return;
			}
			float va = V(road, i, a), vb = V(road, i, b);
			m.Quad(0, ia, ib, ma, mb, new Vector2(0f, va), new Vector2(0f, vb), new Vector2(0.5f, va), new Vector2(0.5f, vb));
			m.Quad(0, ma, mb, oa, ob, new Vector2(0.5f, va), new Vector2(0.5f, vb), new Vector2(1f, va), new Vector2(1f, vb));
		}

		/// <summary>
		/// The verge's cross-section at fraction t of piece i: its inner edge (the road's edge), middle and outer edge, relative to the
		/// mesher's corner, and the outer edge's world position. Shared with tools/meshcheck, which checks the steps at both edges.
		/// </summary>
		internal static void Section(Mesher m, Network.Road road, int i, double t, int side, TerrainGrid terrain, out Vector3 inner, out Vector3 middle, out Vector3 outer, out double wx, out double wz)
		{
			float lift = (float)Network.Lift;
			inner = m.At(road, i, t, side, 0.0);
			Network.Point(road, i, t, side, Network.Verge * 0.5, out double mx, out double mz, out double _);
			Network.Point(road, i, t, side, Network.Verge, out wx, out wz, out double _);
			double outerY = terrain.At(wx, wz) + VergeLift;
			double middleY = Math.Max((inner.y + outerY) * 0.5, terrain.At(mx, mz) + VergeLift);
			middle = new Vector3((float)(mx - m.Ox), (float)middleY, (float)(mz - m.Oz));
			outer = new Vector3((float)(wx - m.Ox), (float)outerY, (float)(wz - m.Oz));
		}

		/// <summary>Collects a mesh relative to a tile's corner, one triangle list per material.</summary>
		internal sealed class Mesher
		{
			internal readonly double Ox;
			internal readonly double Oz;
			private readonly List<Vector3> v = new List<Vector3>();
			private readonly List<Vector3> n = new List<Vector3>();
			private readonly List<Vector2> uv = new List<Vector2>();
			private readonly List<int>[] tris;

			internal Mesher(double ox, double oz, int materials)
			{
				Ox = ox;
				Oz = oz;
				tris = new List<int>[materials];
				for (int k = 0; k < materials; k++)
				{
					tris[k] = new List<int>();
				}
			}

			/// <summary>A point of the drawn surface (<see cref="Network.Point"/>) relative to the corner, in the scene's heights.</summary>
			internal Vector3 At(Network.Road road, int i, double t, int side, double beyond)
			{
				Network.Point(road, i, t, side, beyond, out double x, out double z, out double y);
				return new Vector3((float)(x - Ox), (float)(y + Network.Lift), (float)(z - Oz));
			}

			internal void Quad(int material, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
			{
				RoadMesh.Quad(v, n, uv, tris[material], a, b, c, d, ua, ub, uc, ud, true);
			}

			internal void Wall(int material, Vector3 a, Vector3 b, Vector3 c, Vector3 d)
			{
				RoadMesh.Wall(v, n, uv, tris[material], a, b, c, d);
			}

			/// <summary>A triangle facing up.</summary>
			internal void Triangle(int material, Vector3 a, Vector3 b, Vector3 c, Vector2 ua, Vector2 ub, Vector2 uc)
			{
				Vector3 normal = Vector3.Cross(b - a, c - a);
				if (normal.sqrMagnitude < 1e-10f)
				{
					return;
				}
				normal = normal.normalized;
				if (normal.y < 0f)
				{
					normal = -normal;
				}
				int k = v.Count;
				v.Add(a);
				v.Add(b);
				v.Add(c);
				uv.Add(ua);
				uv.Add(ub);
				uv.Add(uc);
				n.Add(normal);
				n.Add(normal);
				n.Add(normal);
				RoadMesh.Triangle(tris[material], v, k, k + 1, k + 2, normal);
			}

			internal RoadMeshData Done()
			{
				if (v.Count == 0)
				{
					return null;
				}
				int[][] t = new int[tris.Length][];
				for (int k = 0; k < tris.Length; k++)
				{
					t[k] = tris[k].ToArray();
				}
				return new RoadMeshData { Vertices = v.ToArray(), Normals = n.ToArray(), Uv = uv.ToArray(), Triangles = t };
			}
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

		/// <summary>
		/// The verges' material: loose gravel, 4 pixels across (one a unit), speckled light and dark stones, a little grass creeping in
		/// at the outer edge.
		/// </summary>
		internal static Material VergeMaterial(Shader shader)
		{
			Texture2D t = Blank(4, (int)TextureLength, new Color(0.50f, 0.47f, 0.42f), false);
			for (int y = 0; y < t.height; y++)
			{
				for (int x = 0; x < 4; x++)
				{
					ulong h = Noise.Mix((ulong)(y * 4 + x) * 0x9E3779B97F4A7C15UL + 11UL);
					if ((h >> 61) == 0)
					{
						t.SetPixel(x, y, new Color(0.62f, 0.59f, 0.54f));
					}
					else if ((h >> 61) == 1)
					{
						t.SetPixel(x, y, new Color(0.40f, 0.37f, 0.33f));
					}
				}
				if ((Noise.Mix((ulong)y * 41UL + 5UL) >> 62) == 0)
				{
					t.SetPixel(3, y, new Color(0.40f, 0.47f, 0.27f));
				}
			}
			t.Apply();
			return Make(shader, t, "OpenWorld Verge");
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
