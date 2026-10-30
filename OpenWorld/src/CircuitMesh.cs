using System;
using UnityEngine;

namespace HalfGripOpenWorld
{
	/// <summary>
	/// The circuit's driven surfaces, per tile (see <see cref="Circuit"/>): the track, its kerbs, the pit lane and paddock, the paint (start
	/// line, grid boxes, pit lane line), each a little over the levelled ground, which is a plane there, so the surfaces are flat quads
	/// that never dip into it; and the gravel traps, a mesh of their own for their own grip. The barrier, pit wall, garages, stands and
	/// gantry are buildings (<see cref="Buildings"/>).
	///
	/// Each piece of the lap (sample i to i + 1) is built by the tile its first sample is in, and each strip of the pit apron by the tile
	/// its corner on the track is in, so every piece is built once.
	/// </summary>
	internal static class CircuitMesh
	{
		/// <summary>Submeshes of the surface: track, kerbs, pit apron, paint.</summary>
		internal const int Track = 0, Kerb = 1, Apron = 2, Paint = 3, Count = 4;

		/// <summary>Over the levelled ground: the track and apron, the kerbs' outer edge, the paint over the track, the gravel.</summary>
		internal const double TrackTop = Circuit.Lift;
		internal const double KerbTop = Circuit.Lift + 0.12;
		internal const double PaintTop = Circuit.Lift + 0.04;
		internal const double GravelTop = 0.06;

		/// <summary>The track's and kerbs' outer edge runs down to under the grass over this far, so no lip shows.</summary>
		private const double Skirt = 1.2;

		/// <summary>Texture repeat along the lap.</summary>
		private const double Repeat = 40.0;

		internal static RoadMeshData Build(int tx, int tz, float tileSize, out RoadMeshData gravel)
		{
			gravel = null;
			if (!Circuit.Exists)
			{
				return null;
			}
			double ox = tx * (double)tileSize, oz = tz * (double)tileSize;
			RoadMesh.Mesher m = new RoadMesh.Mesher(ox, oz, Count);
			RoadMesh.Mesher g = new RoadMesh.Mesher(ox, oz, 1);
			int n = Circuit.Count;
			for (int i = 0; i < n; i++)
			{
				if (Math.Floor(Circuit.X[i] / tileSize) != tx || Math.Floor(Circuit.Z[i] / tileSize) != tz)
				{
					continue;
				}
				Piece(m, g, i);
			}
			Pits(m, tx, tz, tileSize);
			Markings(m, tx, tz, tileSize);
			gravel = g.Done();
			return m.Done();
		}

		/// <summary>A point d to the right of the lap's centre line at sample i, h over the levelled ground, relative to the mesher's corner.</summary>
		private static Vector3 At(RoadMesh.Mesher m, int i, double d, double h)
		{
			double x = Circuit.X[i] + Circuit.Rx[i] * d, z = Circuit.Z[i] + Circuit.Rz[i] * d;
			return new Vector3((float)(x - m.Ox), (float)(Circuit.PlaneAt(x, z) + Network.Lift + h), (float)(z - m.Oz));
		}

		/// <summary>The point u along the start straight, v to its right, h over the levelled ground.</summary>
		private static Vector3 OnStraight(RoadMesh.Mesher m, double u, double v, double h)
		{
			Circuit.Straight(u, v, out double x, out double z);
			return new Vector3((float)(x - m.Ox), (float)(Circuit.PlaneAt(x, z) + Network.Lift + h), (float)(z - m.Oz));
		}

		/// <summary>One piece of the lap: the track across, the kerbs and skirts at both edges, the gravel trap outside a corner.</summary>
		private static void Piece(RoadMesh.Mesher m, RoadMesh.Mesher g, int i)
		{
			int q = (i + 1) % Circuit.Count;
			double s0 = Circuit.S[i], s1 = q == 0 ? Circuit.Length : Circuit.S[q];
			float v0 = (float)(s0 / Repeat), v1 = (float)(s1 / Repeat);
			double h = Circuit.Half;
			m.Quad(Track, At(m, i, -h, TrackTop), At(m, q, -h, TrackTop), At(m, i, h, TrackTop), At(m, q, h, TrackTop), new Vector2(0f, v0), new Vector2(0f, v1), new Vector2(1f, v0), new Vector2(1f, v1));
			for (int side = -1; side <= 1; side += 2)
			{
				// Along the pits the apron meets the track's right edge: no kerb or skirt there.
				if (side > 0 && OnApron(i) && OnApron(q))
				{
					continue;
				}
				double k0 = Circuit.KerbWidth * Circuit.Kerb[i], k1 = Circuit.KerbWidth * Circuit.Kerb[q];
				double e0 = h, e1 = h;
				if (k0 > 0.01 || k1 > 0.01)
				{
					// The kerb rises from the track's edge to its outer edge, painted in blocks along it (4 units each colour).
					float a = (float)(s0 / 8.0), b = (float)(s1 / 8.0);
					Strip(m, Kerb, At(m, i, side * h, TrackTop), At(m, q, side * h, TrackTop), At(m, i, side * (h + k0), KerbTop), At(m, q, side * (h + k1), KerbTop), new Vector2(0f, a), new Vector2(0f, b), new Vector2(1f, a), new Vector2(1f, b));
					e0 = h + k0;
					e1 = h + k1;
				}
				double t0 = e0 > h + 0.01 ? KerbTop : TrackTop, t1 = e1 > h + 0.01 ? KerbTop : TrackTop;
				m.Quad(Track, At(m, i, side * e0, t0), At(m, q, side * e1, t1), At(m, i, side * (e0 + Skirt), -0.08), At(m, q, side * (e1 + Skirt), -0.08), new Vector2(0f, v0), new Vector2(0f, v1), new Vector2(0.02f, v0), new Vector2(0.02f, v1));
			}
			// The gravel trap, on the outside of the corner, where both ends of the piece have it on the same side.
			double g0 = Circuit.Gravel[i], g1 = Circuit.Gravel[q];
			if ((g0 > 0.0 || g1 > 0.0) && (g0 <= 0.0 || g1 <= 0.0 || Circuit.GravelSide[i] == Circuit.GravelSide[q]))
			{
				int gs = g0 > 0.0 ? Circuit.GravelSide[i] : Circuit.GravelSide[q];
				double w0 = Circuit.GravelIn + (Circuit.GravelOut - Circuit.GravelIn) * g0, w1 = Circuit.GravelIn + (Circuit.GravelOut - Circuit.GravelIn) * g1;
				Vector3 a = At(g, i, gs * Circuit.GravelIn, GravelTop), b = At(g, q, gs * Circuit.GravelIn, GravelTop);
				Vector3 c = At(g, i, gs * w0, GravelTop), d = At(g, q, gs * w1, GravelTop);
				Strip(g, 0, a, b, c, d, World(g, a), World(g, b), World(g, c), World(g, d));
			}
		}

		/// <summary>
		/// A strip from edge a-b to edge c-d, where one end may have no width (a kerb or gravel trap coming in or fading out): a triangle
		/// there, since a quad with two corners on one point takes its facing from a zero-length edge and can come out upside down.
		/// </summary>
		private static void Strip(RoadMesh.Mesher m, int material, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
		{
			// Measured across, in plan: a kerb's outer edge is higher than its inner one even where it has no width.
			bool startless = new Vector2(c.x - a.x, c.z - a.z).sqrMagnitude < 0.01f, endless = new Vector2(d.x - b.x, d.z - b.z).sqrMagnitude < 0.01f;
			if (startless && endless)
			{
				return;
			}
			if (startless)
			{
				m.Triangle(material, a, b, d, ua, ub, ud);
			}
			else if (endless)
			{
				m.Triangle(material, a, b, c, ua, ub, uc);
			}
			else
			{
				m.Quad(material, a, b, c, d, ua, ub, uc, ud);
			}
		}

		/// <summary>The texture coordinates of a point at 8 units a repeat (the gravel's texture is laid on the world, not along the lap).</summary>
		private static Vector2 World(RoadMesh.Mesher m, Vector3 p)
		{
			return new Vector2((float)((p.x + m.Ox) / 8.0), (float)((p.z + m.Oz) / 8.0));
		}

		/// <summary>True if sample i is on the start straight beside the pit apron.</summary>
		private static bool OnApron(int i)
		{
			double s = Circuit.S[i];
			return s >= Circuit.PitA - Circuit.PitTaper && s <= Circuit.PitB + Circuit.PitTaper;
		}

		/// <summary>The pit lane and paddock: one sheet of tarmac out from the track's right edge along the start straight, in 8-unit strips.</summary>
		private static void Pits(RoadMesh.Mesher m, int tx, int tz, float tileSize)
		{
			double from = Circuit.PitA - Circuit.PitTaper, to = Circuit.PitB + Circuit.PitTaper;
			for (double u = from; u < to - 0.01; u += 8.0)
			{
				double u1 = Math.Min(to, u + 8.0);
				Circuit.Straight(u, Circuit.Half, out double cx, out double cz);
				if (Math.Floor(cx / tileSize) != tx || Math.Floor(cz / tileSize) != tz)
				{
					continue;
				}
				double a = Circuit.ApronOut(u), b = Circuit.ApronOut(u1);
				// Where the paddock steps out from the pit lane, the step is a strip of its own (an apron only as far as each end reaches).
				double reach = Math.Min(a, b);
				Vector3 p0 = OnStraight(m, u, Circuit.Half, TrackTop), p1 = OnStraight(m, u1, Circuit.Half, TrackTop);
				Vector3 r0 = OnStraight(m, u, reach, TrackTop), r1 = OnStraight(m, u1, reach, TrackTop);
				m.Quad(Apron, p0, p1, r0, r1, Uv(u, Circuit.Half), Uv(u1, Circuit.Half), Uv(u, reach), Uv(u1, reach));
				if (Math.Abs(a - b) > 0.01)
				{
					double ue = a > b ? u : u1;
					Vector3 far = OnStraight(m, ue, Math.Max(a, b), TrackTop);
					m.Triangle(Apron, r0, r1, far, Uv(u, reach), Uv(u1, reach), Uv(ue, Math.Max(a, b)));
				}
				// Its outer edge runs down under the grass.
				Vector3 o0 = OnStraight(m, u, a, TrackTop), o1 = OnStraight(m, u1, b, TrackTop);
				Vector3 d0 = OnStraight(m, u, a + Skirt, -0.08), d1 = OnStraight(m, u1, b + Skirt, -0.08);
				m.Quad(Apron, o0, o1, d0, d1, Uv(u, a), Uv(u1, b), Uv(u, a + Skirt), Uv(u1, b + Skirt));
			}
			// The ends of the paddock, where it steps back to the pit lane, run down under the grass too.
			foreach (double u in new[] { Circuit.PaddockA, Circuit.PaddockB })
			{
				Circuit.Straight(u, Circuit.Half, out double cx, out double cz);
				if (Math.Floor(cx / tileSize) != tx || Math.Floor(cz / tileSize) != tz)
				{
					continue;
				}
				double outward = u == Circuit.PaddockA ? -Skirt : Skirt;
				m.Quad(Apron, OnStraight(m, u, Circuit.PitLaneOut, TrackTop), OnStraight(m, u, Circuit.PaddockOut, TrackTop), OnStraight(m, u + outward, Circuit.PitLaneOut, -0.08), OnStraight(m, u + outward, Circuit.PaddockOut, -0.08), Uv(u, Circuit.PitLaneOut), Uv(u, Circuit.PaddockOut), Uv(u + outward, Circuit.PitLaneOut), Uv(u + outward, Circuit.PaddockOut));
			}
		}

		private static Vector2 Uv(double u, double v)
		{
			return new Vector2((float)(v / 8.0), (float)(u / 8.0));
		}

		/// <summary>
		/// The paint: the start line across the track, a box for each grid slot, and the line between the pit lane and the track's edge
		/// (the pit lane's entry and exit lines run along the tapers).
		/// </summary>
		private static void Markings(RoadMesh.Mesher m, int tx, int tz, float tileSize)
		{
			Circuit.Straight(Circuit.StartS, 0.0, out double sx, out double sz);
			if (Math.Floor(sx / tileSize) == tx && Math.Floor(sz / tileSize) == tz)
			{
				Line(m, Circuit.StartS - 1.5, Circuit.StartS + 1.5, -Circuit.Half, Circuit.Half);
				for (int k = 0; k < Circuit.GridSlots; k++)
				{
					Circuit.Slot(k, out double x, out double z);
					Circuit.Local(x, z, out double u, out double v);
					// The slot's front line and its sides' first few units, the car's nose at the front line.
					Line(m, u + 8.0, u + 8.6, v - 5.0, v + 5.0);
					Line(m, u + 3.0, u + 8.6, v - 5.0, v - 4.4);
					Line(m, u + 3.0, u + 8.6, v + 4.4, v + 5.0);
				}
			}
			for (double u = Circuit.PitA - Circuit.PitTaper; u < Circuit.PitB + Circuit.PitTaper - 0.01; u += 8.0)
			{
				Circuit.Straight(u, Circuit.Half, out double cx, out double cz);
				if (Math.Floor(cx / tileSize) != tx || Math.Floor(cz / tileSize) != tz)
				{
					continue;
				}
				double u1 = Math.Min(Circuit.PitB + Circuit.PitTaper, u + 8.0);
				// Along the tapers the line follows the pit lane's inner edge out from the track.
				double a = LineAt(u), b = LineAt(u1);
				m.Quad(Paint, OnStraight(m, u, a - 0.4, PaintTop), OnStraight(m, u1, b - 0.4, PaintTop), OnStraight(m, u, a + 0.4, PaintTop), OnStraight(m, u1, b + 0.4, PaintTop), new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 0f), new Vector2(1f, 1f));
			}
		}

		/// <summary>The line between the pit lane and the track, at u along the straight: the pit wall's inner side, and along the tapers from it to the track's edge.</summary>
		private static double LineAt(double u)
		{
			double wall = Circuit.PitWallIn - 0.5;
			if (u < Circuit.PitA)
			{
				return Circuit.Half + (wall - Circuit.Half) * Noise.Step(Circuit.PitA - Circuit.PitTaper, Circuit.PitA, u);
			}
			if (u > Circuit.PitB)
			{
				return Circuit.Half + (wall - Circuit.Half) * (1.0 - Noise.Step(Circuit.PitB, Circuit.PitB + Circuit.PitTaper, u));
			}
			return wall;
		}

		/// <summary>A painted rectangle on the track, u0 to u1 along the start straight and v0 to v1 across.</summary>
		private static void Line(RoadMesh.Mesher m, double u0, double u1, double v0, double v1)
		{
			m.Quad(Paint, OnStraight(m, u0, v0, PaintTop), OnStraight(m, u1, v0, PaintTop), OnStraight(m, u0, v1, PaintTop), OnStraight(m, u1, v1, PaintTop), new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 0f), new Vector2(1f, 1f));
		}

		// ---- looks -------------------------------------------------------------------------------------------------

		/// <summary>The surface's materials, in submesh order: the track, the kerbs, the pit apron, the paint.</summary>
		internal static Material[] Materials(Shader shader)
		{
			return new[]
			{
				Make(shader, TrackTexture(), "OpenWorld Circuit"),
				Make(shader, KerbTexture(), "OpenWorld Kerb"),
				Make(shader, Tarmac(), "OpenWorld Pit Apron"),
				Make(shader, Solid(new Color(0.92f, 0.92f, 0.90f)), "OpenWorld Paint"),
			};
		}

		/// <summary>The gravel traps' material: pale stones, 4 pixels a unit, 8 units a repeat.</summary>
		internal static Material GravelMaterial(Shader shader)
		{
			Texture2D t = New(32, 32);
			for (int y = 0; y < 32; y++)
			{
				for (int x = 0; x < 32; x++)
				{
					ulong h = Noise.Mix((ulong)(y * 32 + x) * 0x9E3779B97F4A7C15UL + 91UL);
					int k = (int)(h >> 61);
					Color c = k == 0 ? new Color(0.84f, 0.79f, 0.66f) : k == 1 ? new Color(0.60f, 0.55f, 0.45f) : new Color(0.74f, 0.69f, 0.56f);
					t.SetPixel(x, y, c);
				}
			}
			t.Apply(true);
			return Make(shader, t, "OpenWorld Gravel Trap");
		}

		/// <summary>
		/// The track across (40 pixels, a unit each) and along (40, one repeat): smooth dark tarmac, a racing line worn darker where the
		/// cars run, and a white line a unit in from each edge.
		/// </summary>
		private static Texture2D TrackTexture()
		{
			Texture2D t = New(40, 40);
			for (int y = 0; y < 40; y++)
			{
				for (int x = 0; x < 40; x++)
				{
					float grain = ((int)(Noise.Mix((ulong)(y * 40 + x) + 313UL) >> 61) - 3.5f) * 0.01f;
					float worn = Math.Abs(x - 19.5f) < 9f ? -0.02f : 0f;
					Color c = x == 1 || x == 38 ? new Color(0.90f, 0.90f, 0.87f) : new Color(0.24f + grain + worn, 0.24f + grain + worn, 0.26f + grain + worn);
					t.SetPixel(x, y, c);
				}
			}
			t.wrapModeU = TextureWrapMode.Clamp;
			t.Apply(true);
			return t;
		}

		/// <summary>The kerbs: red and white blocks 4 units long, a darker line where each block meets the next.</summary>
		private static Texture2D KerbTexture()
		{
			Texture2D t = New(4, 16);
			for (int y = 0; y < 16; y++)
			{
				for (int x = 0; x < 4; x++)
				{
					Color c = y < 8 ? new Color(0.78f, 0.12f, 0.10f) : new Color(0.92f, 0.92f, 0.90f);
					if (y == 0 || y == 8)
					{
						c *= 0.8f;
					}
					c.a = 1f;
					t.SetPixel(x, y, c);
				}
			}
			t.Apply(true);
			return t;
		}

		/// <summary>The pit apron: tarmac a shade lighter than the track's, 4 pixels a unit, 8 units a repeat.</summary>
		private static Texture2D Tarmac()
		{
			Texture2D t = New(32, 32);
			for (int y = 0; y < 32; y++)
			{
				for (int x = 0; x < 32; x++)
				{
					float grain = ((int)(Noise.Mix((ulong)(y * 32 + x) + 517UL) >> 61) - 3.5f) * 0.015f;
					t.SetPixel(x, y, new Color(0.30f + grain, 0.30f + grain, 0.32f + grain));
				}
			}
			t.Apply(true);
			return t;
		}

		private static Texture2D Solid(Color c)
		{
			Texture2D t = New(4, 4);
			for (int y = 0; y < 4; y++)
			{
				for (int x = 0; x < 4; x++)
				{
					t.SetPixel(x, y, c);
				}
			}
			t.Apply(true);
			return t;
		}

		/// <summary>A point-filtered texture like the game's own, with mipmaps (the track is seen far along its length).</summary>
		private static Texture2D New(int w, int h)
		{
			return new Texture2D(w, h, TextureFormat.RGBA32, true) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat, hideFlags = HideFlags.HideAndDontSave };
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
			return m;
		}
	}
}
