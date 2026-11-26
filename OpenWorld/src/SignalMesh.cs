using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace HalfGripOpenWorld
{
	/// <summary>
	/// The traffic lights' meshes (<see cref="SignalHeads"/>): a mast's pole, arm and heads built in its own frame from plain-coloured
	/// parts (one palette texture), apart from the component so they can be built and looked at outside the game.
	/// </summary>
	internal static class SignalMesh
	{
		/// <summary>Colours in the palette texture, one texel each.</summary>
		internal const int Steel = 0, Yellow = 1, Black = 2, Concrete = 3, Dark = 4, Paint = 5, PaletteSize = 6;

		internal static readonly Color[] Palette =
		{
			new Color(0.62f, 0.64f, 0.66f),
			new Color(0.93f, 0.72f, 0.06f),
			new Color(0.05f, 0.05f, 0.05f),
			new Color(0.66f, 0.65f, 0.62f),
			new Color(0.30f, 0.31f, 0.32f),
			new Color(0.86f, 0.86f, 0.82f),
		};

		/// <summary>A mesh being built, in the mast's frame (its foot at 0, the world's axes).</summary>
		internal sealed class Builder
		{
			public readonly List<Vector3> V = new List<Vector3>();
			public readonly List<Vector3> N = new List<Vector3>();
			public readonly List<Vector2> Uv = new List<Vector2>();
			public readonly List<int> T = new List<int>();

			/// <summary>A flat quad a b c d (round its edge either way), facing <paramref name="n"/>.</summary>
			public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n, Vector2 uv)
			{
				int i = V.Count;
				V.Add(a);
				V.Add(b);
				V.Add(c);
				V.Add(d);
				for (int k = 0; k < 4; k++)
				{
					N.Add(n);
					Uv.Add(uv);
				}
				// Unity draws a triangle's face whose corners go clockwise as seen: cross(b - a, c - a) toward the viewer.
				if (Vector3.Dot(Vector3.Cross(b - a, c - a), n) >= 0f)
				{
					T.Add(i); T.Add(i + 1); T.Add(i + 2);
					T.Add(i); T.Add(i + 2); T.Add(i + 3);
				}
				else
				{
					T.Add(i); T.Add(i + 2); T.Add(i + 1);
					T.Add(i); T.Add(i + 3); T.Add(i + 2);
				}
			}

			/// <summary>A flat quad a b c d facing <paramref name="n"/>, each corner with its own texture coordinate.</summary>
			public void QuadUv(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud)
			{
				int i = V.Count;
				Quad(a, b, c, d, n, ua);
				Uv[i + 1] = ub;
				Uv[i + 2] = uc;
				Uv[i + 3] = ud;
			}

			/// <summary>A flat triangle a b c facing <paramref name="n"/>.</summary>
			public void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 n, Vector2 uv)
			{
				int i = V.Count;
				V.Add(a);
				V.Add(b);
				V.Add(c);
				for (int k = 0; k < 3; k++)
				{
					N.Add(n);
					Uv.Add(uv);
				}
				if (Vector3.Dot(Vector3.Cross(b - a, c - a), n) >= 0f)
				{
					T.Add(i); T.Add(i + 1); T.Add(i + 2);
				}
				else
				{
					T.Add(i); T.Add(i + 2); T.Add(i + 1);
				}
			}

			/// <summary>A box round <paramref name="c"/>, its half sizes along the unit axes u, v, w.</summary>
			public void Box(Vector3 c, Vector3 u, Vector3 v, Vector3 w, float hu, float hv, float hw, Vector2 uv)
			{
				Vector3 a = u * hu, b = v * hv, d = w * hw;
				Quad(c + a - b - d, c + a + b - d, c + a + b + d, c + a - b + d, u, uv);
				Quad(c - a - b - d, c - a - b + d, c - a + b + d, c - a + b - d, -u, uv);
				Quad(c - a + b - d, c - a + b + d, c + a + b + d, c + a + b - d, v, uv);
				Quad(c - a - b - d, c + a - b - d, c + a - b + d, c - a - b + d, -v, uv);
				Quad(c - a - b + d, c + a - b + d, c + a + b + d, c - a + b + d, w, uv);
				Quad(c - a - b - d, c - a + b - d, c + a + b - d, c + a - b - d, -w, uv);
			}

			/// <summary>A tapered eight-sided tube from <paramref name="p0"/> (radius r0) to <paramref name="p1"/> (r1), capped at p1.</summary>
			public void Tube(Vector3 p0, Vector3 p1, float r0, float r1, Vector2 uv)
			{
				Vector3 axis = (p1 - p0).normalized;
				Vector3 s = Vector3.Cross(axis, Mathf.Abs(axis.y) > 0.9f ? Vector3.right : Vector3.up).normalized, t = Vector3.Cross(axis, s);
				const int sides = 8;
				for (int k = 0; k < sides; k++)
				{
					float a0 = k * Mathf.PI * 2f / sides, a1 = (k + 1) * Mathf.PI * 2f / sides, am = (a0 + a1) * 0.5f;
					Vector3 e0 = s * Mathf.Cos(a0) + t * Mathf.Sin(a0), e1 = s * Mathf.Cos(a1) + t * Mathf.Sin(a1), n = s * Mathf.Cos(am) + t * Mathf.Sin(am);
					Quad(p0 + e0 * r0, p0 + e1 * r0, p1 + e1 * r1, p1 + e0 * r1, n, uv);
				}
				Disc(p1, axis, r1, sides, uv);
			}

			/// <summary>A flat disc round <paramref name="c"/> facing <paramref name="n"/>.</summary>
			public void Disc(Vector3 c, Vector3 n, float r, int sides, Vector2 uv)
			{
				Vector3 s = Vector3.Cross(n, Mathf.Abs(n.y) > 0.9f ? Vector3.right : Vector3.up).normalized, t = Vector3.Cross(n, s);
				int i = V.Count;
				V.Add(c);
				N.Add(n);
				Uv.Add(uv);
				for (int k = 0; k < sides; k++)
				{
					float a = k * Mathf.PI * 2f / sides;
					V.Add(c + (s * Mathf.Cos(a) + t * Mathf.Sin(a)) * r);
					N.Add(n);
					Uv.Add(uv);
				}
				Vector3 p = V[i + 1] - c, q = V[i + 2] - c;
				bool clockwise = Vector3.Dot(Vector3.Cross(p, q), n) >= 0f;
				for (int k = 0; k < sides; k++)
				{
					int a = i + 1 + k, b = i + 1 + (k + 1) % sides;
					T.Add(i);
					T.Add(clockwise ? a : b);
					T.Add(clockwise ? b : a);
				}
			}

			public Mesh Mesh(string name)
			{
				Mesh m = new Mesh { name = name, hideFlags = HideFlags.HideAndDontSave };
				if (V.Count > 65000)
				{
					m.indexFormat = IndexFormat.UInt32;
				}
				m.SetVertices(V);
				m.SetNormals(N);
				m.SetUVs(0, Uv);
				m.SetTriangles(T, 0);
				m.RecalculateBounds();
				return m;
			}
		}

		/// <summary>How far over the road the stop line is drawn: over the road mesh's own paint, so it never fights with it.</summary>
		private const double StopLift = 0.07;

		/// <summary>
		/// The stop line for mast <paramref name="m"/>'s way in (<see cref="SignalPoles.StopLine"/>) into <paramref name="b"/>, in the
		/// mast's frame (its foot <paramref name="foot"/> high): white paint on the road's surface, in short pieces across so it follows
		/// the road's camber.
		/// </summary>
		internal static void StopLine(SignalPoles.Mast m, float foot, Builder b)
		{
			if (!SignalPoles.StopLine(m.Node, m.Arm, out int r, out double s0, out double s1, out double lo, out double hi))
			{
				return;
			}
			Network.Road road = Network.Roads[r];
			int n = Math.Max(1, (int)Math.Ceiling((hi - lo) / 6.0));
			for (int k = 0; k < n; k++)
			{
				double o0 = lo + (hi - lo) * k / n, o1 = lo + (hi - lo) * (k + 1) / n;
				Vector3 a = OnRoad(road, s0, o0, m, foot), c = OnRoad(road, s1, o1, m, foot);
				Vector3 bb = OnRoad(road, s1, o0, m, foot), d = OnRoad(road, s0, o1, m, foot);
				b.Quad(a, bb, c, d, Vector3.up, Colour(Paint));
			}
		}

		/// <summary>The point <paramref name="off"/> left of road's line at <paramref name="s"/> along it, on its surface, in mast <paramref name="m"/>'s frame.</summary>
		private static Vector3 OnRoad(Network.Road road, double s, double off, SignalPoles.Mast m, float foot)
		{
			Network.Locate(road, s, out int i, out double t);
			Network.Point(road, i, t, off / Network.HalfAt(road, s), 0.0, out double x, out double z, out double y);
			return new Vector3((float)(x - m.X), (float)(y + Network.Lift + StopLift - foot), (float)(z - m.Z));
		}

		internal static Vector2 Colour(int i)
		{
			return new Vector2((i + 0.5f) / PaletteSize, 0.5f);
		}

		/// <summary>
		/// One mast's pole, footing, arm and heads into <paramref name="b"/> and its lenses into <paramref name="lamps"/> (red, amber,
		/// green), in its frame: its foot at 0, the world's axes, the road <paramref name="road"/> over the foot. Returns the pole's top.
		/// </summary>
		internal static float Shape(SignalPoles.Mast m, float road, float foot2, Builder b, Builder[] lamps, out float armY, out float armEnd)
		{
			Vector3 up = Vector3.up, arm = new Vector3((float)m.Ax, 0f, (float)m.Az), face = new Vector3((float)m.Fx, 0f, (float)m.Fz);

			// The footing, a little proud of the ground, and the pole from under it to its top with a cap.
			float top = Mathf.Max(road + (float)SignalPoles.PoleTop, 9f);
			b.Tube(new Vector3(0f, -0.8f, 0f), new Vector3(0f, 0.35f, 0f), 1.0f, 1.0f, Colour(Concrete));
			b.Box(new Vector3(0f, 0.45f, 0f), arm, up, face, 0.75f, 0.1f, 0.75f, Colour(Dark));
			b.Tube(new Vector3(0f, 0.4f, 0f), new Vector3(0f, top, 0f), 0.55f, 0.38f, Colour(Steel));

			// The mast arm, rising a little toward its end (as real ones do), clamped to the pole; or a level truss to a second pole.
			armY = Mathf.Min(road + (float)SignalPoles.ArmHeight, top - 0.6f);
			armEnd = m.Span > 0.0 ? (float)m.Span : (float)m.Reach;
			{
				float reach = (float)m.Reach, rise = m.Span > 0.0 ? 0f : 0.025f;
				b.Box(new Vector3(0f, armY, 0f), arm, up, face, 0.65f, 0.7f, 0.65f, Colour(Steel));
				if (m.Span > 0.0)
				{
					Span(b, arm, up, face, (float)m.Span, armY, foot2, top);
				}
				else
				{
					b.Tube(new Vector3(0f, armY, 0f), arm * reach + up * (armY + reach * rise), 0.36f, 0.17f, Colour(Steel));
				}
				for (int h = 0; h < m.Heads.Length; h++)
				{
					float along = (float)m.Heads[h], armAt = armY + along * rise;
					Vector3 hc = arm * along + up * (armAt - 0.45f - 1.8f);
					b.Box(arm * along + up * (armAt - 0.35f), arm, up, face, 0.14f, 0.4f, 0.14f, Colour(Dark));
					if (h == m.Arrow && lamps.Length >= 6)
					{
						Head(b, new[] { lamps[3], lamps[4], lamps[5] }, hc, face, true);
					}
					else
					{
						Head(b, lamps, hc, face);
					}
				}
			}

			// The head on the pole, at a driver's eye from the stop line, on a bracket toward the road.
			Vector3 pc = arm * 1.35f + up * Mathf.Min(road + (float)SignalPoles.PoleHeadClear + 1.8f, top - 2.4f);
			b.Box(arm * 0.75f + up * pc.y, arm, up, face, 0.35f, 0.12f, 0.12f, Colour(Dark));
			b.Box(arm * 0.75f + up * (pc.y - 1.2f), arm, up, face, 0.35f, 0.12f, 0.12f, Colour(Dark));
			Head(b, lamps, pc, face);
			return top;
		}

		/// <summary>The truss's half depth (up and down) and half width (toward the drivers and away), and the bays of its lacing.</summary>
		private const float TrussHalfH = 0.65f, TrussHalfW = 0.45f, TrussBay = 2.6f;

		/// <summary>
		/// A span: a box truss level from the first pole (at 0) to a second pole <paramref name="span"/> out along <paramref name="arm"/>,
		/// four steel chords laced with struts and diagonals, and the second pole from its own footing (<paramref name="foot2"/> over
		/// the first's) to the top.
		/// </summary>
		private static void Span(Builder b, Vector3 arm, Vector3 up, Vector3 face, float span, float y, float foot2, float top)
		{
			Vector2 steel = Colour(Steel);
			Vector3 far = arm * span;
			for (int a = -1; a <= 1; a += 2)
			{
				for (int c = -1; c <= 1; c += 2)
				{
					Vector3 o = up * (y + a * TrussHalfH) + face * (c * TrussHalfW);
					b.Box(far * 0.5f + o, arm, up, face, span * 0.5f, 0.11f, 0.11f, steel);
				}
			}
			int bays = Mathf.Max(1, Mathf.RoundToInt(span / TrussBay));
			for (int k = 0; k <= bays; k++)
			{
				Vector3 at = far * ((float)k / bays) + up * y;
				// Struts across at every node of the truss, top and bottom and both faces.
				for (int c = -1; c <= 1; c += 2)
				{
					b.Box(at + face * (c * TrussHalfW), arm, up, face, 0.07f, TrussHalfH, 0.07f, steel);
				}
				b.Box(at + up * TrussHalfH, arm, up, face, 0.07f, 0.07f, TrussHalfW, steel);
				b.Box(at - up * TrussHalfH, arm, up, face, 0.07f, 0.07f, TrussHalfW, steel);
				if (k == bays)
				{
					break;
				}
				// A diagonal in each face, turning by bays (a Warren truss).
				Vector3 next = far * ((float)(k + 1) / bays) + up * y;
				float flip = k % 2 == 0 ? 1f : -1f;
				for (int c = -1; c <= 1; c += 2)
				{
					Vector3 p0 = at + face * (c * TrussHalfW) - up * (TrussHalfH * flip), p1 = next + face * (c * TrussHalfW) + up * (TrussHalfH * flip);
					Strut(b, p0, p1, face, 0.06f, steel);
				}
			}
			// The second pole and its footing, its top at the first's, a little proud of the truss.
			b.Tube(new Vector3(far.x, foot2 - 0.8f, far.z), new Vector3(far.x, foot2 + 0.35f, far.z), 1.0f, 1.0f, Colour(Concrete));
			b.Box(new Vector3(far.x, foot2 + 0.45f, far.z), arm, up, face, 0.75f, 0.1f, 0.75f, Colour(Dark));
			b.Tube(new Vector3(far.x, foot2 + 0.4f, far.z), new Vector3(far.x, top, far.z), 0.55f, 0.38f, steel);
			b.Box(far + up * y, arm, up, face, 0.65f, 0.7f, 0.65f, steel);
		}

		/// <summary>A thin square bar from <paramref name="p0"/> to <paramref name="p1"/>, one face toward <paramref name="face"/>'s side.</summary>
		private static void Strut(Builder b, Vector3 p0, Vector3 p1, Vector3 face, float half, Vector2 uv)
		{
			Vector3 u = (p1 - p0).normalized, w = face, v = Vector3.Cross(w, u).normalized;
			b.Box((p0 + p1) * 0.5f, u, v, w, (p1 - p0).magnitude * 0.5f, half, half, uv);
		}

		/// <summary>
		/// The green board for a way in on mast <paramref name="m"/>'s arm (its line at <paramref name="armY"/>, out to
		/// <paramref name="armEnd"/>): hung under the arm where it fits between the pole and the heads, else past the last head (a mast
		/// arm made long enough), on a span in the widest gap; scaled down only if no gap can hold it. Its face in <paramref name="signs"/>
		/// (UVs in the atlas <paramref name="aw"/> by <paramref name="ah"/>), its brackets in <paramref name="b"/>.
		/// </summary>
		internal static void Board(SignalPoles.Mast m, Signs.OverheadWay way, float armY, float armEnd, Builder b, Builder signs, int aw, int ah)
		{
			Vector3 up = Vector3.up, arm = new Vector3((float)m.Ax, 0f, (float)m.Az), face = new Vector3((float)m.Fx, 0f, (float)m.Fz);
			float w = (float)way.W, h = (float)way.H;
			List<float> heads = new List<float>();
			foreach (double d in m.Heads)
			{
				heads.Add((float)d);
			}
			heads.Sort();
			// The gaps along the arm clear of the heads' backplates (and the pole), and past the last head.
			const float clear = 1.45f;
			List<(float a, float b)> gaps = new List<(float, float)>();
			float from = 1.3f;
			foreach (float hd in heads)
			{
				gaps.Add((from, hd - clear));
				from = hd + clear;
			}
			float end = m.Span > 0.0 ? armEnd - 1.3f : float.MaxValue;
			gaps.Add((from, end));
			float at = -1f, scale = 1f;
			// Beside the pole if it fits (the drivers look across at it with the heads), else the first that does.
			foreach ((float a, float z) in gaps)
			{
				if (z - a >= w + 0.3f)
				{
					at = a + 0.15f;
					break;
				}
			}
			if (at < 0f)
			{
				(float a, float z) widest = gaps[0];
				foreach ((float a, float z) g in gaps)
				{
					if (g.z - g.a > widest.z - widest.a)
					{
						widest = g;
					}
				}
				scale = Mathf.Max(0.3f, (widest.z - widest.a - 0.3f) / w);
				at = widest.a + 0.15f;
			}
			w *= scale;
			h *= scale;
			float rise = m.Span > 0.0 ? 0f : 0.025f;
			float topAt = armY - (m.Span > 0.0 ? TrussHalfH + 0.15f : 0.45f) + at * rise;
			Vector3 c = arm * (at + w * 0.5f) + up * (topAt - h * 0.5f);
			// Hung a little in front of the arm, toward the drivers; across its face, left to right as they see it: -arm or +arm.
			Vector3 front = c + face * 0.35f;
			Vector3 right = Vector3.Cross(face, up).normalized;
			Vector3 hx = right * (w * 0.5f), hy = up * (h * 0.5f);
			Signs.Face f = way.Face;
			Vector2 Uv(float u, float v) => new Vector2((f.X + u * f.W) / aw, 1f - (f.Y + (1f - v) * f.H) / ah);
			Vector2 grey = new Vector2((Signs.GreyPatch.X + Signs.GreyPatch.W * 0.5f) / aw, 1f - (Signs.GreyPatch.Y + Signs.GreyPatch.H * 0.5f) / ah);
			const float t = 0.08f;
			Vector3 ff = front + face * t, bf = front - face * t;
			signs.QuadUv(ff - hx - hy, ff + hx - hy, ff + hx + hy, ff - hx + hy, face, Uv(0f, 0f), Uv(1f, 0f), Uv(1f, 1f), Uv(0f, 1f));
			signs.Quad(bf - hx - hy, bf + hx - hy, bf + hx + hy, bf - hx + hy, -face, grey);
			signs.Quad(ff - hx + hy, ff + hx + hy, bf + hx + hy, bf - hx + hy, up, grey);
			signs.Quad(ff - hx - hy, ff + hx - hy, bf + hx - hy, bf - hx - hy, -up, grey);
			signs.Quad(ff + hx - hy, ff + hx + hy, bf + hx + hy, bf + hx - hy, right, grey);
			signs.Quad(ff - hx - hy, ff - hx + hy, bf - hx + hy, bf - hx - hy, -right, grey);
			// Two hangers from the arm to the board's top.
			float hangTop = armY + at * rise;
			for (int k = -1; k <= 1; k += 2)
			{
				Vector3 hp = c + right * (k * w * 0.32f) + face * 0.2f;
				float y0 = topAt, y1 = hangTop;
				b.Box(new Vector3(hp.x, (y0 + y1) * 0.5f, hp.z), arm, up, face, 0.08f, Mathf.Max(0.05f, (y1 - y0) * 0.5f), 0.12f, Colour(Dark));
			}
			if (m.Span <= 0.0 && at + w + 0.6f > m.Reach)
			{
				// A mast arm made long enough to carry the board past its last head.
				b.Tube(arm * (float)m.Reach + up * (armY + (float)m.Reach * rise), arm * (at + w + 0.6f) + up * (armY + (at + w + 0.6f) * rise), 0.17f, 0.15f, Colour(Steel));
			}
		}

		/// <summary>
		/// One three-light head centred at <paramref name="c"/>, facing <paramref name="f"/>: a yellow housing, a black backplate with a
		/// yellow border round its front, and red over amber over green lenses under yellow cap visors; or (<paramref name="arrows"/>) a
		/// left-turn head, each lens dark but for a left arrow (pointing to the drivers' left) lit in its colour.
		/// </summary>
		private static void Head(Builder b, Builder[] lamps, Vector3 c, Vector3 f, bool arrows = false)
		{
			Vector3 up = Vector3.up;
			// Across the face (the arm is, near enough; this keeps the head square to the drivers whatever the arm's angle).
			Vector3 s = Vector3.Cross(up, f).normalized;
			b.Box(c, s, up, f, 0.62f, 1.8f, 0.45f, Colour(Yellow));
			// The backplate round the housing (its back stands out behind, as a real one does), and its border just in front of it.
			Vector3 bp = c - f * 0.12f;
			b.Box(bp, s, up, f, 1.25f, 2.35f, 0.04f, Colour(Black));
			Vector3 front = bp + f * 0.05f;
			const float bw = 0.16f;
			b.Quad(front + s * -1.25f + up * 2.35f, front + s * 1.25f + up * 2.35f, front + s * 1.25f + up * (2.35f - bw), front + s * -1.25f + up * (2.35f - bw), f, Colour(Yellow));
			b.Quad(front + s * -1.25f + up * -2.35f, front + s * 1.25f + up * -2.35f, front + s * 1.25f + up * (-2.35f + bw), front + s * -1.25f + up * (-2.35f + bw), f, Colour(Yellow));
			b.Quad(front + s * -1.25f + up * 2.35f, front + s * (-1.25f + bw) + up * 2.35f, front + s * (-1.25f + bw) + up * -2.35f, front + s * -1.25f + up * -2.35f, f, Colour(Yellow));
			b.Quad(front + s * 1.25f + up * 2.35f, front + s * (1.25f - bw) + up * 2.35f, front + s * (1.25f - bw) + up * -2.35f, front + s * 1.25f + up * -2.35f, f, Colour(Yellow));
			for (int i = 0; i < 3; i++)
			{
				// Red at the top (0), amber, green at the bottom.
				Vector3 lens = c + up * (1.2f - i * 1.2f) + f * 0.46f;
				if (arrows)
				{
					// A dark lens, the arrow on it. The drivers face the head (looking along -f), so their left is up x f.
					b.Disc(lens, f, 0.46f, 12, Colour(Black));
					Vector3 a = lens + f * 0.02f;
					// Bold, filling the lens, so it still reads as an arrow from back down the bay (the owner, 2026-11-25, saw only round lamps).
					lamps[i].Quad(a - s * 0.42f - up * 0.13f, a + s * 0.02f - up * 0.13f, a + s * 0.02f + up * 0.13f, a - s * 0.42f + up * 0.13f, f, Vector2.zero);
					lamps[i].Tri(a + s * 0.44f, a - s * 0.04f - up * 0.38f, a - s * 0.04f + up * 0.38f, f, Vector2.zero);
				}
				else
				{
					lamps[i].Disc(lens, f, 0.46f, 12, Vector2.zero);
				}
				b.Box(lens + f * 0.36f + up * 0.53f, s, up, f, 0.55f, 0.03f, 0.36f, Colour(Yellow));
				b.Box(lens + f * 0.36f + up * 0.2f + s * 0.53f, s, up, f, 0.03f, 0.33f, 0.36f, Colour(Yellow));
				b.Box(lens + f * 0.36f + up * 0.2f - s * 0.53f, s, up, f, 0.03f, 0.33f, 0.36f, Colour(Yellow));
			}
		}
	}
}
