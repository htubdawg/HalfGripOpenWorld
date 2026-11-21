using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace HalfGripOpenWorld
{
	/// <summary>
	/// The deer's mesh in Unity's frame and game units, made once from <see cref="DeerModel"/>: the model's x mirrored (it is
	/// right-handed, Unity left-handed) with the triangles' winding reversed, scaled from metres by 3.3, each vertex's colour region as a
	/// texel of a small palette texture (the scene's lit shader takes no vertex colours), skinned to the 26 bones.
	/// </summary>
	internal static class DeerMesh
	{
		/// <summary>Game units per metre.</summary>
		internal const float Scale = 3.3f;

		private static Mesh mesh;

		/// <summary>Each bone's offset from its parent in Unity's frame and units.</summary>
		internal static Vector3[] Offsets;

		/// <summary>Per pose, per bone: its local rotation in Unity's frame.</summary>
		internal static Quaternion[][] Poses;

		internal static int Pose(string name)
		{
			return Array.IndexOf(DeerModel.PoseNames, name);
		}

		internal static Mesh Get()
		{
			if (mesh != null)
			{
				return mesh;
			}
			int bones = DeerModel.BoneNames.Length;
			Offsets = new Vector3[bones];
			Vector3[] bind = new Vector3[bones];
			for (int b = 0; b < bones; b++)
			{
				Offsets[b] = new Vector3(-DeerModel.BoneOffsets[b * 3], DeerModel.BoneOffsets[b * 3 + 1], DeerModel.BoneOffsets[b * 3 + 2]) * Scale;
				int p = DeerModel.BoneParents[b];
				bind[b] = p < 0 ? Offsets[b] : bind[p] + Offsets[b];
			}
			// A pose's three angles are applied as Rx.Ry.Rz in the model; mirrored in x, Ry and Rz turn the other way.
			int poses = DeerModel.PoseNames.Length;
			Poses = new Quaternion[poses][];
			for (int p = 0; p < poses; p++)
			{
				Poses[p] = new Quaternion[bones];
				for (int b = 0; b < bones; b++)
				{
					int o = (p * bones + b) * 3;
					Poses[p][b] = Quaternion.AngleAxis(DeerModel.Poses[o] * Mathf.Rad2Deg, Vector3.right)
						* Quaternion.AngleAxis(-DeerModel.Poses[o + 1] * Mathf.Rad2Deg, Vector3.up)
						* Quaternion.AngleAxis(-DeerModel.Poses[o + 2] * Mathf.Rad2Deg, Vector3.forward);
				}
			}

			int n = DeerModel.Region.Length;
			Vector3[] v = new Vector3[n];
			Vector3[] nor = new Vector3[n];
			Vector2[] uv = new Vector2[n];
			BoneWeight[] w = new BoneWeight[n];
			int regions = DeerModel.Regions.Length;
			for (int i = 0; i < n; i++)
			{
				v[i] = new Vector3(-DeerModel.Positions[i * 3], DeerModel.Positions[i * 3 + 1], DeerModel.Positions[i * 3 + 2]) * Scale;
				nor[i] = new Vector3(-DeerModel.Normals[i * 3], DeerModel.Normals[i * 3 + 1], DeerModel.Normals[i * 3 + 2]);
				uv[i] = new Vector2((DeerModel.Region[i] + 0.5f) / regions, 0.5f);
				float w0 = DeerModel.BoneWeight[i * 4], w1 = DeerModel.BoneWeight[i * 4 + 1], w2 = DeerModel.BoneWeight[i * 4 + 2], w3 = DeerModel.BoneWeight[i * 4 + 3];
				float sum = w0 + w1 + w2 + w3;
				if (sum <= 0f)
				{
					w0 = 1f;
					sum = 1f;
				}
				w[i] = new BoneWeight
				{
					boneIndex0 = DeerModel.BoneIndex[i * 4], weight0 = w0 / sum,
					boneIndex1 = DeerModel.BoneIndex[i * 4 + 1], weight1 = w1 / sum,
					boneIndex2 = DeerModel.BoneIndex[i * 4 + 2], weight2 = w2 / sum,
					boneIndex3 = DeerModel.BoneIndex[i * 4 + 3], weight3 = w3 / sum,
				};
			}
			int[] tris = new int[DeerModel.Triangles.Length];
			for (int t = 0; t < tris.Length; t += 3)
			{
				tris[t] = DeerModel.Triangles[t];
				tris[t + 1] = DeerModel.Triangles[t + 2];
				tris[t + 2] = DeerModel.Triangles[t + 1];
			}
			// Bind rotations are all zero and the deer stands at the origin: each bind pose only takes the bone's place away.
			Matrix4x4[] bindposes = new Matrix4x4[bones];
			for (int b = 0; b < bones; b++)
			{
				bindposes[b] = Matrix4x4.Translate(-bind[b]);
			}
			Mesh m = new Mesh { name = "OpenWorld Deer", hideFlags = HideFlags.HideAndDontSave };
			m.vertices = v;
			m.normals = nor;
			m.uv = uv;
			m.boneWeights = w;
			m.bindposes = bindposes;
			m.triangles = tris;
			m.RecalculateBounds();
			mesh = m;
			return m;
		}

		/// <summary>The coat as a texture a texel per region (point filtered, so each region is one flat colour).</summary>
		internal static Texture2D Palette(bool winter)
		{
			int[] coat = winter ? DeerModel.Winter : DeerModel.Summer;
			Texture2D tex = new Texture2D(coat.Length, 1, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "OpenWorld Deer Coat", hideFlags = HideFlags.HideAndDontSave };
			for (int i = 0; i < coat.Length; i++)
			{
				tex.SetPixel(i, 0, new Color32((byte)(coat[i] >> 16), (byte)(coat[i] >> 8), (byte)coat[i], 255));
			}
			tex.Apply(false, false);
			return tex;
		}
	}

	/// <summary>
	/// The deer in a race: <see cref="Herds"/> from the race's seed, built on a worker at the start, and each deer within
	/// <see cref="Reach"/> of the car drawn as a skinned mesh posed in code from what <see cref="Herds.At"/> says it is doing (grazing,
	/// standing, looking up, walking, trotting). Where a deer is comes from the herds alone (the same on every machine); how its pose
	/// blends from one thing to the next is only drawing, and is eased here.
	///
	/// Calm deer only: they do not react to cars yet and have no collider (step 2 of the plan in docs/PLAN.md).
	/// </summary>
	internal sealed class Deer : MonoBehaviour
	{
		/// <summary>One line for the debug overlay (ShowCoords).</summary>
		internal static string Status;

		/// <summary>Deer are drawn this far from the car, at most <see cref="MostDrawn"/> of them (the nearest).</summary>
		private const float Reach = 500f;
		private const int MostDrawn = 40;

		/// <summary>Seconds a change from one pose to another takes.</summary>
		private const float BlendSeconds = 0.45f;

		private TrackManager tm;
		private Material material;
		private System.Threading.Tasks.Task<Herds> building;
		private Herds herds;
		private bool failed;
		private int layer;

		private int stand, alert, graze, walk1, walk2;

		private sealed class View
		{
			public GameObject Go;
			public Transform Root;
			public Transform[] Bones;
			public SkinnedMeshRenderer Renderer;
			public Quaternion[] From, Shown;
			public DeerMotion Motion;
			public float Blend;
			public float Yaw;
			public double LastX, LastZ;
			public float Y, Pitch;
			public bool Fresh;
			public int Key;
			public bool Seen;
		}

		private readonly Dictionary<int, View> live = new Dictionary<int, View>();
		private readonly Stack<View> spare = new Stack<View>();
		private readonly List<(float d, int g, int m)> near = new List<(float, int, int)>();
		private readonly List<int> gone = new List<int>();
		private Herds.Move move, scratch;
		private readonly Quaternion[] target = new Quaternion[32];

		internal void Setup(TrackManager track, Shader lit)
		{
			tm = track;
			layer = LayerMask.NameToLayer("Ignore Raycast");
			if (layer < 0)
			{
				layer = 0;
			}
			int seed = Weather.Seed;
			double hour = Sky.StartHourOf(seed);
			building = System.Threading.Tasks.Task.Run(() => new Herds(seed, hour, Sky.DaySeconds));
			try
			{
				DeerMesh.Get();
				stand = DeerMesh.Pose("stand");
				alert = DeerMesh.Pose("alert");
				graze = DeerMesh.Pose("graze");
				walk1 = DeerMesh.Pose("walk1");
				walk2 = DeerMesh.Pose("walk2");
				// Winter coat on a cold day (the race's snow line low), the summer coat otherwise.
				bool winter = new Storms(seed).SnowLine < 390.0;
				material = new Material(lit) { name = "OpenWorld Deer", hideFlags = HideFlags.HideAndDontSave };
				Texture2D coat = DeerMesh.Palette(winter);
				if (material.HasProperty("_BaseMap"))
				{
					material.SetTexture("_BaseMap", coat);
				}
				if (material.HasProperty("_MainTex"))
				{
					material.SetTexture("_MainTex", coat);
				}
				if (material.HasProperty("_BaseColor"))
				{
					material.SetColor("_BaseColor", Color.white);
				}
				if (material.HasProperty("_Smoothness"))
				{
					material.SetFloat("_Smoothness", 0.15f);
				}
				GetComponent<WorldAssets>()?.Add(material);
			}
			catch (Exception e)
			{
				Mod.Log.LogWarning("The deer could not be made (no deer this race): " + e);
				failed = true;
			}
		}

		private void Update()
		{
			if (failed || tm == null)
			{
				return;
			}
			if (herds == null)
			{
				if (building == null || !building.IsCompleted)
				{
					return;
				}
				if (building.IsFaulted || building.Result == null)
				{
					Mod.Log.LogWarning("The deer could not be placed (no deer this race): " + building.Exception);
					failed = true;
					return;
				}
				herds = building.Result;
				building = null;
				Mod.Log.LogInfo("Deer: " + herds.Groups.Length + " groups, " + herds.DeerCount + " deer drawn from the race's seed (" + DeerSites.All.Count + " places they could live), " + herds.BuildMs.ToString("0") + " ms");
			}
			if (!Mod.ShowDeer.Value)
			{
				Clear();
				Status = null;
				return;
			}
			try
			{
				Step();
			}
			catch (Exception e)
			{
				Mod.Log.LogWarning("The deer stopped: " + e);
				Clear();
				failed = true;
			}
		}

		private void Step()
		{
			CarControllerNew car = tm.playerCarController;
			if (car == null)
			{
				return;
			}
			double t = tm.TimeSinceRaceStart;
			Origin.World(car.transform.position, out double cx, out double cz);

			// The nearest deer within reach: the groups near enough first (a group's deer keep within a few hundred units of its spots).
			near.Clear();
			for (int g = 0; g < herds.Groups.Length; g++)
			{
				Herds.Group group = herds.Groups[g];
				double dx = group.Site.X - cx, dz = group.Site.Z - cz;
				if (dx * dx + dz * dz > (Reach + 300.0) * (Reach + 300.0))
				{
					continue;
				}
				for (int m = 0; m < group.Members.Length; m++)
				{
					herds.At(group, m, t, ref move, ref scratch, out DeerState s);
					double ex = s.X - cx, ez = s.Z - cz;
					double d2 = ex * ex + ez * ez;
					if (d2 < Reach * Reach)
					{
						near.Add(((float)Math.Sqrt(d2), g, m));
					}
				}
			}
			near.Sort((a, b) => a.d.CompareTo(b.d));
			foreach (View v in live.Values)
			{
				v.Seen = false;
			}
			int drawn = 0;
			float dt = Time.deltaTime;
			for (int k = 0; k < near.Count && drawn < MostDrawn; k++, drawn++)
			{
				int g = near[k].g, m = near[k].m;
				int key = g * 64 + m;
				if (!live.TryGetValue(key, out View v))
				{
					v = Take(key);
					live[key] = v;
				}
				v.Seen = true;
				Herds.Group group = herds.Groups[g];
				herds.At(group, m, t, ref move, ref scratch, out DeerState s);
				Draw(v, group.Members[m], s, dt);
			}
			gone.Clear();
			foreach (KeyValuePair<int, View> kv in live)
			{
				if (!kv.Value.Seen)
				{
					gone.Add(kv.Key);
				}
			}
			foreach (int key in gone)
			{
				Give(live[key]);
				live.Remove(key);
			}
			if (Mod.ShowCoords.Value)
			{
				// For finding them: the nearest group's crossing place, how far and which way.
				double best = double.MaxValue, bx = 0.0, bz = 0.0;
				foreach (Herds.Group group in herds.Groups)
				{
					double dx = group.Site.X - cx, dz = group.Site.Z - cz;
					if (dx * dx + dz * dz < best)
					{
						best = dx * dx + dz * dz;
						bx = dx;
						bz = dz;
					}
				}
				float nearest = near.Count > 0 ? near[0].d : -1f;
				Status = "deer: " + drawn + " drawn" + (nearest >= 0f ? ", nearest " + (nearest / 3.3f).ToString("0") + " m" : "") + "; the nearest group's crossing "
					+ (Math.Sqrt(best) / 3.3).ToString("0") + " m " + Sky.Compass(bx, bz) + " (" + herds.Groups.Length + " groups, " + herds.DeerCount + " deer this race; out feeding: "
					+ (Herds.Activity(herds.Hour(t)) * 100.0).ToString("0") + "%)";
			}
		}

		/// <summary>Puts one deer where the herds say and poses it.</summary>
		private void Draw(View v, Herds.Member member, DeerState s, float dt)
		{
			// Yaw: Network.Atan2 gives the angle from +x toward +z; Unity's turn about y is from +z toward +x.
			float yaw = 90f - (float)(s.Yaw * (180.0 / Math.PI));
			if (v.Fresh)
			{
				v.Yaw = yaw;
			}
			else
			{
				v.Yaw = Mathf.MoveTowardsAngle(v.Yaw, yaw, (s.Motion == DeerMotion.Walk || s.Motion == DeerMotion.Trot ? 360f : 160f) * dt);
			}
			// The ground under it, and its slope along the body (only worked out again once it has moved).
			double mx = s.X - v.LastX, mz = s.Z - v.LastZ;
			if (v.Fresh || mx * mx + mz * mz > 0.04 * 0.04)
			{
				v.Y = (float)Herds.Ground(s.X, s.Z);
				if (v.Fresh || mx * mx + mz * mz > 0.25)
				{
					double fx = Math.Sin(v.Yaw * Mathf.Deg2Rad) * 2.4, fz = Math.Cos(v.Yaw * Mathf.Deg2Rad) * 2.4;
					double front = Herds.Ground(s.X + fx, s.Z + fz), back = Herds.Ground(s.X - fx, s.Z - fz);
					v.Pitch = Mathf.Clamp(-(float)Math.Atan2(front - back, 4.8) * Mathf.Rad2Deg, -25f, 25f);
					v.LastX = s.X;
					v.LastZ = s.Z;
				}
			}

			// The pose: a still one, or a gait swung between two.
			int bones = DeerMesh.Offsets.Length;
			float bob = 0f;
			switch (s.Motion)
			{
				case DeerMotion.Walk:
				case DeerMotion.Trot:
					{
						double phase = s.Gait - Math.Floor(s.Gait);
						float k = 0.5f - 0.5f * (float)Math.Cos(phase * 2.0 * Math.PI);
						Quaternion[] a = DeerMesh.Poses[walk1], b = DeerMesh.Poses[walk2];
						for (int i = 0; i < bones; i++)
						{
							target[i] = Quaternion.Slerp(a[i], b[i], k);
						}
						bob = (s.Motion == DeerMotion.Trot ? 0.05f : 0.012f) * DeerMesh.Scale * Mathf.Abs(Mathf.Sin((float)(phase * 2.0 * Math.PI)));
						break;
					}
				default:
					{
						Quaternion[] p = DeerMesh.Poses[s.Motion == DeerMotion.Graze ? graze : s.Motion == DeerMotion.Alert ? alert : stand];
						Array.Copy(p, target, bones);
						break;
					}
			}
			if (v.Fresh)
			{
				for (int i = 0; i < bones; i++)
				{
					v.Shown[i] = target[i];
				}
				v.Motion = s.Motion;
				v.Blend = 1f;
			}
			bool gait = s.Motion == DeerMotion.Walk || s.Motion == DeerMotion.Trot;
			bool wasGait = v.Motion == DeerMotion.Walk || v.Motion == DeerMotion.Trot;
			if (s.Motion != v.Motion && !(gait && wasGait))
			{
				Array.Copy(v.Shown, v.From, bones);
				v.Blend = 0f;
			}
			v.Motion = s.Motion;
			v.Blend = Mathf.Min(1f, v.Blend + dt / BlendSeconds);
			float e = v.Blend * v.Blend * (3f - 2f * v.Blend);
			for (int i = 0; i < bones; i++)
			{
				v.Shown[i] = e >= 1f ? target[i] : Quaternion.Slerp(v.From[i], target[i], e);
				v.Bones[i].localRotation = v.Shown[i];
			}
			v.Bones[0].localPosition = DeerMesh.Offsets[0] + new Vector3(0f, bob, 0f);

			Vector3 at = Origin.ToLocal(new Vector3((float)s.X, v.Y, (float)s.Z));
			v.Root.SetPositionAndRotation(at, Quaternion.Euler(v.Pitch, v.Yaw, 0f));
			float size = (float)member.Scale;
			v.Root.localScale = new Vector3(size, size, size);
			if (v.Fresh)
			{
				v.Go.SetActive(true);
				v.Fresh = false;
			}
		}

		/// <summary>A deer's objects: from the spares, or made (a root with the bones under it and the skinned mesh on it).</summary>
		private View Take(int key)
		{
			View v = spare.Count > 0 ? spare.Pop() : Make();
			v.Key = key;
			v.Fresh = true;
			return v;
		}

		private void Give(View v)
		{
			v.Go.SetActive(false);
			spare.Push(v);
		}

		private void Clear()
		{
			foreach (View v in live.Values)
			{
				Give(v);
			}
			live.Clear();
		}

		private View Make()
		{
			int bones = DeerMesh.Offsets.Length;
			GameObject go = new GameObject("Deer") { layer = layer };
			go.SetActive(false);
			go.transform.SetParent(transform, false);
			Transform[] t = new Transform[bones];
			for (int b = 0; b < bones; b++)
			{
				GameObject bone = new GameObject(DeerModel.BoneNames[b]) { layer = layer };
				int p = DeerModel.BoneParents[b];
				bone.transform.SetParent(p < 0 ? go.transform : t[p], false);
				bone.transform.localPosition = DeerMesh.Offsets[b];
				t[b] = bone.transform;
			}
			SkinnedMeshRenderer r = go.AddComponent<SkinnedMeshRenderer>();
			r.sharedMesh = DeerMesh.Get();
			r.bones = t;
			r.rootBone = t[0];
			r.sharedMaterial = material;
			r.shadowCastingMode = ShadowCastingMode.On;
			r.receiveShadows = true;
			r.updateWhenOffscreen = false;
			r.quality = SkinQuality.Bone4;
			// Room for every pose about the hips (the bounds are the root bone's).
			r.localBounds = new Bounds(new Vector3(0f, -0.3f, 0.6f), new Vector3(4f, 6.5f, 9f));
			return new View { Go = go, Root = go.transform, Bones = t, Renderer = r, From = new Quaternion[bones], Shown = new Quaternion[bones] };
		}

		private void OnDestroy()
		{
			Status = null;
		}
	}
}
