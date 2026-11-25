using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace HalfGripOpenWorld
{
	/// <summary>
	/// The traffic lights drawn, American style (<see cref="SignalPoles"/> says where): a galvanized steel pole on a concrete footing with
	/// a tapered mast arm over the lanes, a federal-yellow three-light head over each lane (red over amber over green, 12-inch lenses
	/// under cap visors, on a black backplate with a yellow reflective border), and one more head on the pole, all across the junction
	/// from the drivers they face. Each lamp shows what the
	/// race's day of traffic ran (<see cref="TrafficSignals.Lights"/>, read at the day's clock), so the lights are the ones the cars obey,
	/// the same on every machine in a room. Built once the day is (it decides which junctions have lights); each mast shown while its
	/// ground is loaded and near enough. World positions are kept as doubles and turned into the scene's every frame, so an origin shift
	/// needs nothing here.
	/// </summary>
	internal sealed class SignalHeads : MonoBehaviour
	{
		/// <summary>How far a mast is drawn (units: about 450 m).</summary>
		private const float Reach = 1500f;

		/// <summary>The lamps: red, amber, green, as <see cref="TrafficSignals"/>' states.</summary>
		private static readonly Color[] LampLit =
		{
			new Color(1.00f, 0.10f, 0.06f),
			new Color(1.00f, 0.62f, 0.05f),
			new Color(0.15f, 1.00f, 0.62f),
		};

		private static readonly Color[] LampDark =
		{
			new Color(0.22f, 0.04f, 0.03f),
			new Color(0.24f, 0.15f, 0.03f),
			new Color(0.03f, 0.15f, 0.10f),
		};

		private sealed class View
		{
			public SignalPoles.Mast Mast;
			public GameObject Go;
			public float Foot;
			/// <summary>Red, amber, green lenses; then the arrow head's red, amber, green arrows (none if it has no arrow head).</summary>
			public MeshRenderer[] Lamps;
			public int Shown = -1, ShownArrow = -1;
			/// <summary>The arm a left turn from this way in goes out along (for the arrow's state), or -1.</summary>
			public int LeftOut = -1;
		}

		private TrafficCars traffic;
		private Shader shader;
		private Material body;
		private readonly Material[] lit = new Material[3], dark = new Material[3];
		private readonly List<View> views = new List<View>();
		private readonly List<Mesh> meshes = new List<Mesh>();
		private bool built, failed;

		internal void Setup(Shader lit, WorldAssets assets)
		{
			shader = lit;
			traffic = GetComponent<TrafficCars>();
			try
			{
				body = BodyMaterial();
				for (int c = 0; c < 3; c++)
				{
					this.lit[c] = LampMaterial(LampLit[c], true, "OpenWorld Signal Lit " + c);
					dark[c] = LampMaterial(LampDark[c], false, "OpenWorld Signal Dark " + c);
				}
				assets.Add(body);
				assets.Add(this.lit);
				assets.Add(dark);
			}
			catch (Exception e)
			{
				Mod.Log.LogWarning("The traffic lights' materials could not be made (no lights drawn): " + e);
				failed = true;
			}
		}

		private Material BodyMaterial()
		{
			Texture2D t = new Texture2D(SignalMesh.PaletteSize, 1, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
			t.SetPixels(SignalMesh.Palette);
			t.Apply(false, true);
			Material m = new Material(shader) { name = "OpenWorld Signals", hideFlags = HideFlags.HideAndDontSave };
			if (m.HasProperty("_BaseMap"))
			{
				m.SetTexture("_BaseMap", t);
			}
			if (m.HasProperty("_MainTex"))
			{
				m.SetTexture("_MainTex", t);
			}
			if (m.HasProperty("_BaseColor"))
			{
				m.SetColor("_BaseColor", Color.white);
			}
			if (m.HasProperty("_Smoothness"))
			{
				m.SetFloat("_Smoothness", 0.25f);
			}
			return m;
		}

		private Material LampMaterial(Color colour, bool on, string name)
		{
			Material m = new Material(shader) { name = name, hideFlags = HideFlags.HideAndDontSave };
			if (m.HasProperty("_BaseColor"))
			{
				m.SetColor("_BaseColor", colour);
			}
			if (m.HasProperty("_Color"))
			{
				m.SetColor("_Color", colour);
			}
			if (m.HasProperty("_Smoothness"))
			{
				m.SetFloat("_Smoothness", 0.8f);
			}
			if (m.HasProperty("_EmissionColor"))
			{
				if (on)
				{
					m.EnableKeyword("_EMISSION");
					m.SetColor("_EmissionColor", colour * 3.2f);
				}
				else
				{
					m.DisableKeyword("_EMISSION");
					m.SetColor("_EmissionColor", Color.black);
				}
			}
			return m;
		}

		private void Update()
		{
			if (failed || traffic == null)
			{
				return;
			}
			TrafficDay day = traffic.Day;
			if (day == null)
			{
				return;
			}
			try
			{
				if (!built)
				{
					built = true;
					Build();
				}
				Frame(day);
			}
			catch (Exception e)
			{
				Mod.Log.LogWarning("The traffic lights stopped: " + e);
				failed = true;
				foreach (View v in views)
				{
					if (v.Go != null)
					{
						v.Go.SetActive(false);
					}
				}
			}
		}

		private void Build()
		{
			float started = Time.realtimeSinceStartup;
			SignalPoles.Build();
			int heads = 0;
			foreach (SignalPoles.Mast m in SignalPoles.All)
			{
				views.Add(Make(m));
				heads += m.Heads.Length + 1;
			}
			Mod.Log.LogInfo("Traffic lights: " + TrafficSignals.Nodes.Count + " junctions, " + SignalPoles.All.Count + " mast arms with " + heads + " heads, " + SignalPoles.Skipped + " ways in with no clear corner, built in "
				+ ((Time.realtimeSinceStartup - started) * 1000f).ToString("0") + " ms");
		}

		/// <summary>Each mast near the car and on loaded ground shown, placed and lit as its light is now.</summary>
		private void Frame(TrafficDay day)
		{
			CarControllerNew car = traffic.PlayerCar;
			Vector3 focus = car != null ? car.transform.position : Vector3.zero;
			Origin.World(focus, out double cx, out double cz);
			double w = traffic.Now;
			Streamer streamer = Streamer.Instance;
			foreach (View v in views)
			{
				SignalPoles.Mast m = v.Mast;
				double dx = m.X - cx, dz = m.Z - cz;
				bool show = dx * dx + dz * dz < Reach * Reach && (streamer == null || streamer.IsLoadedAt(m.X, m.Z));
				if (v.Go.activeSelf != show)
				{
					v.Go.SetActive(show);
				}
				if (!show)
				{
					continue;
				}
				v.Go.transform.position = Origin.Local(m.X, v.Foot, m.Z);
				int state = day.Lights.State(m.Node, m.Arm, -1, w);
				int on = state == TrafficSignals.Green ? 2 : state == TrafficSignals.Amber ? 1 : 0;
				if (on != v.Shown)
				{
					v.Shown = on;
					for (int c = 0; c < 3; c++)
					{
						v.Lamps[c].sharedMaterial = c == on ? lit[c] : dark[c];
					}
				}
				if (v.Lamps.Length > 3 && v.LeftOut >= 0)
				{
					int arrow = day.Lights.State(m.Node, m.Arm, v.LeftOut, w);
					int onArrow = arrow == TrafficSignals.Green ? 2 : arrow == TrafficSignals.Amber ? 1 : 0;
					if (onArrow != v.ShownArrow)
					{
						v.ShownArrow = onArrow;
						for (int c = 0; c < 3; c++)
						{
							v.Lamps[3 + c].sharedMaterial = c == onArrow ? lit[c] : dark[c];
						}
					}
				}
			}
		}

		/// <summary>One mast: its pole, footing and arm, its heads and their lenses (a renderer for each colour, lit by swapping its material).</summary>
		private View Make(SignalPoles.Mast m)
		{
			float foot = (float)WorldGen.Height(m.X, m.Z);
			SignalMesh.Builder b = new SignalMesh.Builder();
			int count = m.Arrow >= 0 ? 6 : 3;
			SignalMesh.Builder[] lamps = new SignalMesh.Builder[count];
			for (int c = 0; c < count; c++)
			{
				lamps[c] = new SignalMesh.Builder();
			}
			float top = SignalMesh.Shape(m, (float)(m.Road + Network.Lift) - foot, b, lamps);

			GameObject go = new GameObject("Traffic light");
			go.transform.SetParent(transform, false);
			go.SetActive(false);
			Mesh mesh = b.Mesh("OpenWorld Signal");
			meshes.Add(mesh);
			go.AddComponent<MeshFilter>().sharedMesh = mesh;
			go.AddComponent<MeshRenderer>().sharedMaterial = body;
			// The pole is solid, as a sign's post is.
			BoxCollider box = go.AddComponent<BoxCollider>();
			box.center = new Vector3(0f, top * 0.5f, 0f);
			box.size = new Vector3(1.1f, top + 1f, 1.1f);
			MeshRenderer[] renderers = new MeshRenderer[count];
			string[] names = { "Red", "Amber", "Green", "Red Arrow", "Amber Arrow", "Green Arrow" };
			for (int c = 0; c < count; c++)
			{
				GameObject l = new GameObject(names[c]);
				l.transform.SetParent(go.transform, false);
				Mesh lm = lamps[c].Mesh("OpenWorld Signal " + names[c]);
				meshes.Add(lm);
				l.AddComponent<MeshFilter>().sharedMesh = lm;
				MeshRenderer r = l.AddComponent<MeshRenderer>();
				r.sharedMaterial = dark[c % 3];
				r.shadowCastingMode = ShadowCastingMode.Off;
				renderers[c] = r;
			}
			int leftOut = -1;
			if (m.Arrow >= 0)
			{
				for (int j = 0; j < Traffic.Links[m.Node].Count; j++)
				{
					if (j != m.Arm && TrafficDay.LeftTurn(m.Node, m.Arm, j))
					{
						leftOut = j;
						break;
					}
				}
			}
			return new View { Mast = m, Go = go, Foot = foot, Lamps = renderers, LeftOut = leftOut };
		}

		private void OnDestroy()
		{
			foreach (Mesh m in meshes)
			{
				if (m != null)
				{
					Destroy(m);
				}
			}
			meshes.Clear();
		}
	}

	/// <summary>
	/// The traffic lights' meshes (<see cref="SignalHeads"/>): a mast's pole, arm and heads built in its own frame from plain-coloured
	/// parts (one palette texture), apart from the component so they can be built and looked at outside the game.
	/// </summary>
	internal static class SignalMesh
	{
		/// <summary>Colours in the palette texture, one texel each.</summary>
		internal const int Steel = 0, Yellow = 1, Black = 2, Concrete = 3, Dark = 4, PaletteSize = 5;

		internal static readonly Color[] Palette =
		{
			new Color(0.62f, 0.64f, 0.66f),
			new Color(0.93f, 0.72f, 0.06f),
			new Color(0.05f, 0.05f, 0.05f),
			new Color(0.66f, 0.65f, 0.62f),
			new Color(0.30f, 0.31f, 0.32f),
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

		internal static Vector2 Colour(int i)
		{
			return new Vector2((i + 0.5f) / PaletteSize, 0.5f);
		}

		/// <summary>
		/// One mast's pole, footing, arm and heads into <paramref name="b"/> and its lenses into <paramref name="lamps"/> (red, amber,
		/// green), in its frame: its foot at 0, the world's axes, the road <paramref name="road"/> over the foot. Returns the pole's top.
		/// </summary>
		internal static float Shape(SignalPoles.Mast m, float road, Builder b, Builder[] lamps)
		{
			Vector3 up = Vector3.up, arm = new Vector3((float)m.Ax, 0f, (float)m.Az), face = new Vector3((float)m.Fx, 0f, (float)m.Fz);

			// The footing, a little proud of the ground, and the pole from under it to its top with a cap.
			float top = Mathf.Max(road + (float)SignalPoles.PoleTop, 9f);
			b.Tube(new Vector3(0f, -0.8f, 0f), new Vector3(0f, 0.35f, 0f), 1.0f, 1.0f, Colour(Concrete));
			b.Box(new Vector3(0f, 0.45f, 0f), arm, up, face, 0.75f, 0.1f, 0.75f, Colour(Dark));
			b.Tube(new Vector3(0f, 0.4f, 0f), new Vector3(0f, top, 0f), 0.55f, 0.38f, Colour(Steel));

			// The mast arm, rising a little toward its end (as real ones do), clamped to the pole.
			{
				float armY = Mathf.Min(road + (float)SignalPoles.ArmHeight, top - 0.6f), reach = (float)m.Reach, rise = 0.025f;
				b.Box(new Vector3(0f, armY, 0f), arm, up, face, 0.65f, 0.7f, 0.65f, Colour(Steel));
				b.Tube(new Vector3(0f, armY, 0f), arm * reach + up * (armY + reach * rise), 0.36f, 0.17f, Colour(Steel));
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
					lamps[i].Quad(a - s * 0.36f - up * 0.08f, a + s * 0.02f - up * 0.08f, a + s * 0.02f + up * 0.08f, a - s * 0.36f + up * 0.08f, f, Vector2.zero);
					lamps[i].Tri(a + s * 0.38f, a - up * 0.25f, a + up * 0.25f, f, Vector2.zero);
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
