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
	/// from the drivers they face. Each lamp shows its light's plan
	/// at the race clock (<see cref="TrafficSignals.State"/>), the same on every machine in a room. The masts are laid out on a worker at
	/// the start (the traffic's map and junctions) and built once that is done; each mast shown while its ground is loaded and near
	/// enough. World positions are kept as doubles and turned into the scene's every frame, so an origin shift
	/// needs nothing here.
	/// </summary>
	internal sealed class SignalHeads : MonoBehaviour
	{
		/// <summary>How far a mast is drawn (units: about 450 m).</summary>
		private const float Reach = 1500f;

		/// <summary>
		/// The lamps: red, amber, green, as <see cref="TrafficSignals"/>' states. The red has no green in it at all and glows less than
		/// the others: bright emission with any green turned orange on screen (the owner, 2026-11-25: "the red light is a bit too yellow").
		/// </summary>
		private static readonly Color[] LampLit =
		{
			new Color(1.00f, 0.00f, 0.03f),
			new Color(1.00f, 0.62f, 0.05f),
			new Color(0.15f, 1.00f, 0.62f),
		};

		/// <summary>How much each lit lamp glows (its colour times this).</summary>
		private static readonly float[] LampGlow = { 1.9f, 3.2f, 3.2f };

		private static readonly Color[] LampDark =
		{
			new Color(0.20f, 0.01f, 0.02f),
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

		private TrackManager tm;
		private System.Threading.Tasks.Task layout;
		private Shader shader;
		private Material body, board;
		private Texture2D boardTexture;
		private readonly Material[] lit = new Material[3], dark = new Material[3];
		private readonly List<View> views = new List<View>();
		private readonly List<Mesh> meshes = new List<Mesh>();
		private bool built, failed;

		internal void Setup(TrackManager track, Shader lit, WorldAssets assets)
		{
			shader = lit;
			tm = track;
			try
			{
				// The traffic's map, the junctions and which have lights, off the main thread (a few hundred ms).
				layout = System.Threading.Tasks.Task.Run(() => Junctions.Ensure());
				body = BodyMaterial();
				for (int c = 0; c < 3; c++)
				{
					this.lit[c] = LampMaterial(LampLit[c], LampGlow[c], "OpenWorld Signal Lit " + c);
					dark[c] = LampMaterial(LampDark[c], 0f, "OpenWorld Signal Dark " + c);
				}
				board = BoardMaterial();
				assets.Add(body);
				if (board != null)
				{
					assets.Add(board);
				}
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

		/// <summary>The green boards' material: the road signs' lettering atlas (<see cref="Signs.Atlas"/>, as <see cref="Buildings"/> draws it); null if there is none.</summary>
		private Material BoardMaterial()
		{
			if (Signs.Overheads.Count == 0)
			{
				return null;
			}
			Texture2D t = Buildings.SignAtlas();
			Material m = new Material(shader) { name = "OpenWorld Signal Boards", hideFlags = HideFlags.HideAndDontSave };
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
				m.SetFloat("_Smoothness", 0.1f);
			}
			boardTexture = t;
			return m;
		}

		private Material LampMaterial(Color colour, float glow, string name)
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
				if (glow > 0f)
				{
					m.EnableKeyword("_EMISSION");
					m.SetColor("_EmissionColor", colour * glow);
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
			if (failed || tm == null || layout == null || !layout.IsCompleted)
			{
				return;
			}
			try
			{
				if (!built)
				{
					built = true;
					if (layout.IsFaulted)
					{
						throw layout.Exception;
					}
					Build();
				}
				Frame();
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
		private void Frame()
		{
			CarControllerNew car = tm.playerCarController;
			Vector3 focus = car != null ? car.transform.position : Vector3.zero;
			Origin.World(focus, out double cx, out double cz);
			double w = tm.TimeSinceRaceStart;
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
				int state = TrafficSignals.State(m.Node, m.Arm, -1, w);
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
					int arrow = TrafficSignals.State(m.Node, m.Arm, v.LeftOut, w);
					int onArrow = arrow == TrafficSignals.Green ? 2 : arrow == TrafficSignals.Amber ? 1 : 0;
					if (arrow == TrafficSignals.Green && !TrafficSignals.Protected(m.Node, m.Arm, v.LeftOut, w))
					{
						// A permissive left (a bay off the trunk, on the main line's green): the flashing yellow arrow, once a second
						// (the MUTCD's permissive indication), all dark between flashes.
						onArrow = w - Math.Floor(w) < 0.5 ? 1 : -1;
					}
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
			float foot2 = m.Span > 0.0 ? (float)WorldGen.Height(m.X2, m.Z2) - foot : 0f;
			float top = SignalMesh.Shape(m, (float)(m.Road + Network.Lift) - foot, foot2, b, lamps, out float armY, out float armEnd);
			// The green board for this way in, on the arm (the fingerpost's places, which no longer stand on the corner).
			SignalMesh.Builder signs = null;
			Signs.OverheadWay way = board != null ? SignalPoles.Board(m) : null;
			if (way != null)
			{
				signs = new SignalMesh.Builder();
				SignalMesh.Board(m, way, armY, armEnd, b, signs, Signs.AtlasW, Signs.AtlasH);
			}
			// The stop line for the same way in, in the mast's frame (drawn while the mast is).
			SignalMesh.StopLine(m, foot, b);

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
			if (m.Span > 0.0)
			{
				BoxCollider far = go.AddComponent<BoxCollider>();
				far.center = new Vector3((float)(m.X2 - m.X), (foot2 + top) * 0.5f, (float)(m.Z2 - m.Z));
				far.size = new Vector3(1.1f, top - foot2 + 1f, 1.1f);
			}
			if (signs != null)
			{
				GameObject g = new GameObject("Board");
				g.transform.SetParent(go.transform, false);
				Mesh sm = signs.Mesh("OpenWorld Signal Board");
				meshes.Add(sm);
				g.AddComponent<MeshFilter>().sharedMesh = sm;
				g.AddComponent<MeshRenderer>().sharedMaterial = board;
			}
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
					if (j != m.Arm && Junctions.LeftTurn(m.Node, m.Arm, j))
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
			if (boardTexture != null)
			{
				Destroy(boardTexture);
			}
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
}
