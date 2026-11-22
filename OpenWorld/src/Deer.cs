using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace HalfGripOpenWorld
{
	/// <summary>
	/// The deer's mesh in Unity's frame and game units, made once from <see cref="DeerModel"/>: the model's x mirrored (it is
	/// right-handed, Unity left-handed) with the triangles' winding reversed, scaled from metres by 3.3, skinned to the 26 bones. Its
	/// coat is the painted texture (<c>textures\deer.png</c> in the mod's folder, on the model's UVs); if that cannot be read, each
	/// vertex's colour region as a texel of a small palette texture instead (the scene's lit shader takes no vertex colours).
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

		/// <summary>The mesh, its UVs on the painted texture (<paramref name="painted"/>) or on the palette's texels.</summary>
		internal static Mesh Get(bool painted)
		{
			Mesh m = Get();
			if (painted != onPainting)
			{
				m.uv = Uvs(painted);
				onPainting = painted;
			}
			return m;
		}

		private static bool onPainting;

		private static Vector2[] Uvs(bool painted)
		{
			int n = DeerModel.Region.Length, regions = DeerModel.Regions.Length;
			Vector2[] uv = new Vector2[n];
			for (int i = 0; i < n; i++)
			{
				// The model's UVs run from the texture's bottom left, as Unity's do.
				uv[i] = painted ? new Vector2(DeerModel.Uvs[i * 2], DeerModel.Uvs[i * 2 + 1]) : new Vector2((DeerModel.Region[i] + 0.5f) / regions, 0.5f);
			}
			return uv;
		}

		/// <summary>
		/// The painted coat from the mod's folder (a new texture each race: the race's material frees it with the world), or null if it
		/// is missing or cannot be read.
		/// </summary>
		internal static Texture2D Painted()
		{
			try
			{
				string path = System.IO.Path.Combine(Mod.Ctx.Folder, "textures", "deer.png");
				if (!System.IO.File.Exists(path))
				{
					Mod.Log.LogWarning("The deer's coat " + path + " is missing: the deer are drawn in flat colours");
					return null;
				}
				Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, true) { name = "OpenWorld Deer Coat", hideFlags = HideFlags.HideAndDontSave };
				if (!ImageConversion.LoadImage(tex, System.IO.File.ReadAllBytes(path), true))
				{
					UnityEngine.Object.Destroy(tex);
					Mod.Log.LogWarning("The deer's coat " + path + " could not be read: the deer are drawn in flat colours");
					return null;
				}
				tex.filterMode = FilterMode.Trilinear;
				tex.anisoLevel = 4;
				tex.wrapMode = TextureWrapMode.Clamp;
				return tex;
			}
			catch (Exception e)
			{
				Mod.Log.LogWarning("The deer's coat could not be loaded (flat colours instead): " + e.Message);
				return null;
			}
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
			onPainting = false;
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
	/// They react (<see cref="React"/>): a car coming is noticed (heads up), and at about 40% of the distance it was noticed at the group
	/// flees (<c>Reactions.cs</c>); a shot near them or one of them killed sends them off too. This machine decides only for its
	/// own car and gun and claims the reaction of the room's host (<see cref="DeerRoom"/>), which settles it for everybody.
	///
	/// They can be killed, tested in code with no colliders:
	/// each frame this machine's car (its colliders' box) against the bodies of the deer drawn near it (<see cref="CarHits"/>), and each
	/// of this player's gun shots against the deer it passes before what it struck (<see cref="Shoot"/>, from a patch on the gun's ray).
	/// A car's hit damages the car as a crash does, scaled from the game's own crash numbers to a deer's weight, slows it a little and
	/// throws the deer; a shot drops it where it stands. Each kill is sent to the room (<see cref="DeerKills"/>), so the body lies in the
	/// same place on every screen.
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
		private Mesh mesh;
		private System.Threading.Tasks.Task<Herds> building;
		private Herds herds;
		private bool failed;
		private int layer;

		private int stand, alert, graze, walk1, walk2, extend, gather, flag;

		/// <summary>The room's reactions and kills (the host settles them; alone, this machine).</summary>
		private DeerRoom room;

		/// <summary>Per group: how far off this machine's car was when the group noticed it (the flee comes at 40% of that).</summary>
		private readonly Dictionary<int, float> noticed = new Dictionary<int, float>();

		/// <summary>Per group: when a shot here last sent it off (a shotgun's pellets are one shot).</summary>
		private readonly Dictionary<int, double> shotAt = new Dictionary<int, double>();

		/// <summary>A group notices a car coming at this many seconds' closing (about 4.6 s before it would reach them at 40%, as deer were
		/// measured to flee), between the nearest and furthest distances; a car not coming at them, only this near.</summary>
		private const double NoticeSeconds = 11.5, NoticeLeast = 45.0, NoticeMost = 200.0, NoticeStill = 35.0;

		/// <summary>How far a shot is heard by deer (units, about 75 m).</summary>
		private const double ShotHeard = 250.0;

		/// <summary>The deer drawn now, for the gun's patch (null with no world).</summary>
		internal static Deer Active;

		/// <summary>A deer's share of the damage a wall would do hit at the same speed, and the most one deer can do, as shares of the
		/// car's most crash damage (a grown doe; a bigger deer more, as its weight).</summary>
		private const float HitShare = 0.55f, HitMost = 0.9f;

		/// <summary>A car slower than this (units a second, about 3 km/h) goes through a deer without hurting either.</summary>
		private const float HitSlowest = 3f;

		/// <summary>How much of the car's speed one grown doe takes off it.</summary>
		private const float HitSlowing = 0.08f;

		private DeerKills kills;
		private int hitCount, shotCount;

		// The car's box in its own frame (from its colliders, made once per car) and where it was last frame, in world coordinates.
		private int boxCar;
		private Vector3 boxMin, boxMax;
		private bool havePrev;
		private double prevX, prevZ;
		private float prevY;
		private Quaternion prevRot;

		private const int Tail1 = 8, Tail2 = 9, Hips = 0, Chest = 2, Neck2 = 4, Head = 5, ShoulderL = 10, FetlockL = 13, ThighL = 14, HfetlockL = 17, ShoulderR = 18, FetlockR = 21, ThighR = 22, HfetlockR = 25;

		private readonly List<(Vector3 a, Vector3 b, float r, int n)> shapes = new List<(Vector3, Vector3, float, int)>();

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
			/// <summary>Drawn as a body (dead), and its group and deer, for the hit tests.</summary>
			public bool Dead;
			public int G, M;
			public double WorldX, WorldZ, StateYaw;
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
			// The player's amount only alone: in a room everybody must have the same deer.
			double amount = EventLink.InRoom ? 1.0 : Mod.DeerAmount.Value;
			building = System.Threading.Tasks.Task.Run(() => new Herds(seed, hour, Sky.DaySeconds, amount));
			kills = new DeerKills(seed);
			room = new DeerRoom(seed, new Link());
			room.ReactionsChanged = g =>
			{
				if (herds != null && g < herds.Groups.Length)
				{
					herds.SetReactions(herds.Groups[g], room.Reactions(g));
				}
			};
			room.KillChanged = k =>
			{
				if (herds != null && k.Group < herds.Groups.Length && k.Member < herds.Groups[k.Group].Members.Length)
				{
					kills.Set(k);
				}
			};
			Active = this;
			try
			{
				stand = DeerMesh.Pose("stand");
				alert = DeerMesh.Pose("alert");
				graze = DeerMesh.Pose("graze");
				walk1 = DeerMesh.Pose("walk1");
				walk2 = DeerMesh.Pose("walk2");
				extend = DeerMesh.Pose("extend");
				gather = DeerMesh.Pose("gather");
				flag = DeerMesh.Pose("flag");
				// Winter coat on a cold day (the race's snow line low), the summer coat otherwise: the painted coat is a summer one, so on
				// a cold day it is tinted toward the winter coat's grey-brown.
				bool winter = new Storms(seed).SnowLine < 390.0;
				material = new Material(lit) { name = "OpenWorld Deer", hideFlags = HideFlags.HideAndDontSave };
				Texture2D painted = DeerMesh.Painted();
				Texture2D coat = painted ?? DeerMesh.Palette(winter);
				mesh = DeerMesh.Get(painted != null);
				Color tint = painted != null && winter ? new Color(0.84f, 0.88f, 0.93f) : Color.white;
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
					material.SetColor("_BaseColor", tint);
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
				Mod.Log.LogInfo("Deer: " + herds.Groups.Length + " groups, " + herds.DeerCount + " deer drawn from the race's seed (" + DeerSites.All.Count + " places they could live), " + herds.BuildMs.ToString("0") + " ms"
					+ (Math.Abs(Mod.DeerAmount.Value - 1f) > 0.001f ? (EventLink.InRoom ? " (the standard amount: DeerAmount is only used alone)" : " (DeerAmount " + Mod.DeerAmount.Value.ToString("0.##") + ")") : ""));
				if (EventLink.InRoom && !HalfGripTools.Net.IsHost)
				{
					room.Hello();
				}
			}
			ReadMessages();
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

			// The nearest deer within reach: the groups near enough first (a group's deer keep within a few hundred units of its spots, and a
			// body thrown by a fast car lands up to a few hundred more down the road).
			near.Clear();
			for (int g = 0; g < herds.Groups.Length; g++)
			{
				Herds.Group group = herds.Groups[g];
				double dx = group.Site.X - cx, dz = group.Site.Z - cz;
				if (dx * dx + dz * dz > (Reach + 650.0) * (Reach + 650.0))
				{
					continue;
				}
				for (int m = 0; m < group.Members.Length; m++)
				{
					double sx, sz;
					if (kills.Dead.TryGetValue(DeerKills.Key(g, m), out DeerKills.Body body))
					{
						DeerKills.Pose bp = body.At(t, Herds.Ground, Gravity);
						sx = bp.X;
						sz = bp.Z;
					}
					else
					{
						herds.Live(group, m, t, ref move, ref scratch, out DeerState s);
						sx = s.X;
						sz = s.Z;
					}
					double ex = sx - cx, ez = sz - cz;
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
				v.G = g;
				v.M = m;
				Herds.Group group = herds.Groups[g];
				if (kills.Dead.TryGetValue(key, out DeerKills.Body body))
				{
					DrawBody(v, group.Members[m], body.At(t, Herds.Ground, Gravity), dt);
				}
				else
				{
					herds.Live(group, m, t, ref move, ref scratch, out DeerState s);
					v.Dead = false;
					v.WorldX = s.X;
					v.WorldZ = s.Z;
					v.StateYaw = s.Yaw;
					Draw(v, group.Members[m], s, dt);
				}
			}
			CarHits(car, t);
			React(car, t, cx, cz);
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
					+ (Herds.Activity(herds.Hour(t)) * 100.0).ToString("0") + "%; killed: " + kills.Count + ", " + hitCount + " by this car, " + shotCount + " shot here; reacting: " + reacting + ")";
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
				v.Yaw = Mathf.MoveTowardsAngle(v.Yaw, yaw, (s.Motion == DeerMotion.Gallop ? 720f : s.Motion == DeerMotion.Walk || s.Motion == DeerMotion.Trot ? 360f : 160f) * dt);
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
				case DeerMotion.Gallop:
					{
						// The bound: gathered (legs under) to stretched out and back, once a stride; the tail up in both.
						double phase = s.Gait - Math.Floor(s.Gait);
						float k = 0.5f - 0.5f * (float)Math.Cos(phase * 2.0 * Math.PI);
						Quaternion[] a = DeerMesh.Poses[gather], b = DeerMesh.Poses[extend];
						for (int i = 0; i < bones; i++)
						{
							target[i] = Quaternion.Slerp(a[i], b[i], k);
						}
						bob = 0.1f * DeerMesh.Scale * Mathf.Sin((float)(phase * 2.0 * Math.PI));
						break;
					}
				default:
					{
						Quaternion[] p = DeerMesh.Poses[s.Motion == DeerMotion.Graze ? graze : s.Motion == DeerMotion.Alert ? (s.Flag ? flag : alert) : stand];
						Array.Copy(p, target, bones);
						break;
					}
			}
			if (s.Flag)
			{
				// Alarmed: the tail up, a white flag.
				Quaternion[] up = DeerMesh.Poses[flag];
				target[Tail1] = up[Tail1];
				target[Tail2] = up[Tail2];
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

		/// <summary>Gravity for the thrown bodies: the scene's (units a second, a second).</summary>
		private static double Gravity
		{
			get
			{
				float g = -Physics.gravity.y;
				return g > 1f ? g : 9.81;
			}
		}

		/// <summary>The room's deer messages come since the last frame, read in order.</summary>
		private void ReadMessages()
		{
			List<(ulong from, byte[] data)> list = DeerRoom.Take();
			if (list == null)
			{
				return;
			}
			foreach ((ulong from, byte[] data) in list)
			{
				room.Receive(from, data);
			}
		}

		/// <summary>A kill found here, claimed of the host; the rest of the group flees from it.</summary>
		private void Kill(DeerKills.Kill kill, double t)
		{
			kill.T = t;
			if (room.Claim(kill))
			{
				Flee(kill.Group, kill.X, kill.Z, t, kill.How == DeerKills.How.Shot);
			}
		}

		/// <summary>The groups this machine's deer messages go through: the game's network (in a room), the host settling.</summary>
		private sealed class Link : DeerRoom.ILink
		{
			public bool IsHost => !EventLink.InRoom || HalfGripTools.Net.IsHost;

			public void ToHost(byte[] data)
			{
				EventLink.DeerToHost(data);
			}

			public void ToAll(byte[] data)
			{
				EventLink.DeerToAll(data);
			}

			public void ToOne(ulong to, byte[] data)
			{
				EventLink.DeerToOne(to, data);
			}
		}

		private int reacting;

		/// <summary>
		/// The groups near this machine's car notice it and flee from it: noticed when it is <see cref="NoticeSeconds"/> away at its speed
		/// toward them (heads up, an alert claimed), fled at 40% of the distance it was noticed at, or when it is very near or under 1.6 s
		/// off. Which way by chance (<see cref="Herds.ChooseWay"/>). A group galloping off, or with a claim of this machine's waiting, is
		/// left alone.
		/// </summary>
		private void React(CarControllerNew car, double t, double cx, double cz)
		{
			Vector3 vel = car.rb != null ? car.rb.linearVelocity : Vector3.zero;
			reacting = 0;
			for (int g = 0; g < herds.Groups.Length; g++)
			{
				Herds.Group group = herds.Groups[g];
				double sx = group.Site.X - cx, sz = group.Site.Z - cz;
				if (sx * sx + sz * sz > 450.0 * 450.0)
				{
					noticed.Remove(g);
					continue;
				}
				if (Herds.Alerted(group, t) || Herds.Running(group, t))
				{
					reacting++;
				}
				if (room.Waiting(g) || Herds.Running(group, t))
				{
					continue;
				}
				// The live deer nearest the car.
				double best = double.MaxValue, nx = 0.0, nz = 0.0;
				for (int m = 0; m < group.Members.Length; m++)
				{
					if (room.IsDead(g, m))
					{
						continue;
					}
					herds.Live(group, m, t, ref move, ref scratch, out DeerState s);
					double ex = s.X - cx, ez = s.Z - cz;
					double d2 = ex * ex + ez * ez;
					if (d2 < best)
					{
						best = d2;
						nx = s.X;
						nz = s.Z;
					}
				}
				if (best == double.MaxValue)
				{
					continue;
				}
				double dist = Math.Sqrt(best);
				double closing = dist > 0.01 ? (vel.x * (nx - cx) + vel.z * (nz - cz)) / dist : 0.0;
				double notice = closing > 2.0 ? Math.Max(NoticeLeast, Math.Min(NoticeMost, closing * NoticeSeconds)) : NoticeStill;
				if (!noticed.TryGetValue(g, out float at))
				{
					if (dist < notice)
					{
						noticed[g] = (float)dist;
						if (!Herds.Alerted(group, t))
						{
							room.Claim(new Reaction { Group = g, Kind = ReactKind.Alert, T0 = t, FromX = cx, FromZ = cz });
						}
					}
					continue;
				}
				if (dist > Math.Max(at, notice) * 1.4 + 40.0)
				{
					noticed.Remove(g); // gone by
					continue;
				}
				if (dist < 0.4 * at || dist < 14.0 || (closing > 2.0 && dist / closing < 1.6))
				{
					FleeWay way = Herds.ChooseWay(group, room.Count(g), Herds.OnRoad(group, nx, nz), herds.Hour(t), false);
					if (room.Claim(new Reaction { Group = g, Kind = ReactKind.Flee, Way = way, T0 = t, FromX = cx, FromZ = cz }))
					{
						noticed.Remove(g);
						Mod.Info("Deer: group " + g + " flees " + way + " from the car at " + (dist / 3.3).ToString("0") + " m (noticed at " + (at / 3.3f).ToString("0") + " m)");
					}
				}
			}
		}

		/// <summary>Group g flees from a point (a kill, a shot), unless it is already galloping off or a claim of this machine's waits.</summary>
		private void Flee(int g, double x, double z, double t, bool shot)
		{
			Herds.Group group = herds.Groups[g];
			if (room.Waiting(g) || Herds.Running(group, t))
			{
				return;
			}
			bool onRoad = Herds.OnRoad(group, x, z);
			FleeWay way = Herds.ChooseWay(group, room.Count(g), onRoad, herds.Hour(t), shot);
			room.Claim(new Reaction { Group = g, Kind = ReactKind.Flee, Way = way, T0 = t, FromX = x, FromZ = z });
		}

		/// <summary>A shot of this player's: every group with a live deer within <see cref="ShotHeard"/> of the gun flees from it.</summary>
		internal void Gunshot(Vector3 origin)
		{
			if (herds == null || tm == null)
			{
				return;
			}
			double t = tm.TimeSinceRaceStart;
			Origin.World(origin, out double ox, out double oz);
			for (int g = 0; g < herds.Groups.Length; g++)
			{
				Herds.Group group = herds.Groups[g];
				double sx = group.Site.X - ox, sz = group.Site.Z - oz;
				if (sx * sx + sz * sz > (ShotHeard + 300.0) * (ShotHeard + 300.0) || (shotAt.TryGetValue(g, out double last) && t - last < 2.0))
				{
					continue;
				}
				for (int m = 0; m < group.Members.Length; m++)
				{
					if (room.IsDead(g, m))
					{
						continue;
					}
					herds.Live(group, m, t, ref move, ref scratch, out DeerState s);
					double ex = s.X - ox, ez = s.Z - oz;
					if (ex * ex + ez * ez < ShotHeard * ShotHeard)
					{
						shotAt[g] = t;
						Flee(g, ox, oz, t, true);
						break;
					}
				}
			}
		}

		/// <summary>A dead deer: its body where <see cref="DeerKills.Body"/> says, rolled onto its side, legs stretched in the air and going limp.</summary>
		private void DrawBody(View v, Herds.Member member, DeerKills.Pose p, float dt)
		{
			int bones = DeerMesh.Offsets.Length;
			Quaternion[] fly = DeerMesh.Poses[extend], lie = DeerMesh.Poses[stand];
			float limp = (float)p.Limp * 0.6f;
			for (int i = 0; i < bones; i++)
			{
				target[i] = Quaternion.Slerp(fly[i], lie[i], limp);
			}
			if (v.Fresh)
			{
				Array.Copy(target, v.Shown, bones);
				v.Blend = 1f;
			}
			else if (!v.Dead)
			{
				Array.Copy(v.Shown, v.From, bones);
				v.Blend = 0f;
			}
			v.Dead = true;
			v.Blend = Mathf.Min(1f, v.Blend + dt / 0.15f);
			for (int i = 0; i < bones; i++)
			{
				v.Shown[i] = v.Blend >= 1f ? target[i] : Quaternion.Slerp(v.From[i], target[i], v.Blend);
				v.Bones[i].localRotation = v.Shown[i];
			}
			v.Bones[0].localPosition = DeerMesh.Offsets[0];

			// The body turns about its centre: the root (at the hooves) is put so the centre is where the body is.
			float size = (float)member.Scale;
			float yaw = 90f - (float)(p.Yaw * (180.0 / Math.PI));
			Quaternion rot = Quaternion.Euler(0f, yaw, 0f) * Quaternion.AngleAxis(-(float)(p.Roll * (180.0 / Math.PI)), Vector3.forward);
			Vector3 centre = Origin.ToLocal(new Vector3((float)p.X, (float)p.Y, (float)p.Z));
			Vector3 at = centre - rot * new Vector3(0f, (float)DeerKills.CentreHeight * size, 0f);
			v.Root.SetPositionAndRotation(at, rot);
			v.Root.localScale = new Vector3(size, size, size);
			v.Yaw = yaw;
			v.Fresh = false;
			if (!v.Go.activeSelf)
			{
				v.Go.SetActive(true);
			}
		}

		/// <summary>
		/// A drawn deer's body as capsules in the scene (from its bones, so a grazing deer's head is low): the trunk, the neck, the head
		/// and the four legs; each with the points it is tested at along it.
		/// </summary>
		private void Shapes(View v)
		{
			shapes.Clear();
			Transform[] b = v.Bones;
			float s = v.Root.localScale.x;
			shapes.Add((b[Hips].TransformPoint(new Vector3(0f, -0.5f, -0.25f)), b[Chest].TransformPoint(new Vector3(0f, -0.6f, 0.25f)), 0.62f * s, 7));
			shapes.Add((b[Chest].TransformPoint(new Vector3(0f, 0.1f, 0.45f)), b[Head].TransformPoint(Vector3.zero), 0.3f * s, 4));
			Vector3 head = b[Head].TransformPoint(new Vector3(0f, 0.05f, 0.45f));
			shapes.Add((head, head, 0.33f * s, 1));
			shapes.Add((b[ShoulderL].position, b[FetlockL].position, 0.17f * s, 4));
			shapes.Add((b[ShoulderR].position, b[FetlockR].position, 0.17f * s, 4));
			shapes.Add((b[ThighL].position, b[HfetlockL].position, 0.17f * s, 4));
			shapes.Add((b[ThighR].position, b[HfetlockR].position, 0.17f * s, 4));
		}

		/// <summary>The car's box in the frame of its body (its rigidbody's transform), from its solid colliders; made once per car.</summary>
		private void CarBox(CarControllerNew car)
		{
			int id = car.GetInstanceID();
			if (id == boxCar)
			{
				return;
			}
			boxCar = id;
			Transform frame = car.rb != null ? car.rb.transform : car.transform;
			bool any = false;
			Vector3 lo = Vector3.zero, hi = Vector3.zero;
			Vector3[] corners = new Vector3[8];
			foreach (Collider c in car.GetComponentsInChildren<Collider>(false))
			{
				if (c == null || c.isTrigger || c is WheelCollider)
				{
					continue;
				}
				Bounds local;
				switch (c)
				{
					case BoxCollider box:
						local = new Bounds(box.center, box.size);
						break;
					case MeshCollider mesh when mesh.sharedMesh != null:
						local = mesh.sharedMesh.bounds;
						break;
					case SphereCollider sphere:
						local = new Bounds(sphere.center, Vector3.one * sphere.radius * 2f);
						break;
					case CapsuleCollider capsule:
						local = new Bounds(capsule.center, Vector3.one * Mathf.Max(capsule.radius * 2f, capsule.height));
						break;
					default:
						continue;
				}
				for (int k = 0; k < 8; k++)
				{
					Vector3 corner = local.center + Vector3.Scale(local.extents, new Vector3((k & 1) == 0 ? -1f : 1f, (k & 2) == 0 ? -1f : 1f, (k & 4) == 0 ? -1f : 1f));
					corners[k] = frame.InverseTransformPoint(c.transform.TransformPoint(corner));
				}
				// Something far from the body (a long trigger-like part, a gun held out) is not the car's body.
				Vector3 mid = (corners[0] + corners[7]) * 0.5f;
				if (mid.magnitude > 12f)
				{
					continue;
				}
				for (int k = 0; k < 8; k++)
				{
					lo = any ? Vector3.Min(lo, corners[k]) : corners[k];
					hi = any ? Vector3.Max(hi, corners[k]) : corners[k];
					any = true;
				}
			}
			Vector3 size = hi - lo;
			if (!any || size.x < 2f || size.x > 16f || size.z < 5f || size.z > 30f || size.y < 1f || size.y > 14f)
			{
				// A car is about 14 long and 6 wide.
				lo = new Vector3(-3.2f, 0f, -7f);
				hi = new Vector3(3.2f, 4.5f, 7f);
				Mod.Info("Deer: the car's colliders gave no sensible box (" + (any ? size.ToString("0.0") : "none") + "): a box 6.4 x 4.5 x 14 is used for hitting deer");
			}
			else
			{
				Mod.Info("Deer: the car's box for hitting deer is " + size.ToString("0.0") + " from " + lo.ToString("0.0"));
			}
			boxMin = lo;
			boxMax = hi;
		}

		/// <summary>
		/// This machine's car against the live deer drawn near it, at its pose now, last frame's and between (so a fast car does not
		/// pass through one between frames). A deer touched by a car going faster than <see cref="HitSlowest"/> is killed and thrown, and
		/// the car is damaged, shaken and slowed.
		/// </summary>
		private void CarHits(CarControllerNew car, double t)
		{
			Rigidbody rb = car.rb;
			if (rb == null)
			{
				return;
			}
			CarBox(car);
			Transform frame = rb.transform;
			Origin.World(frame.position, out double wx, out double wz);
			float wy = frame.position.y;
			Quaternion rot = frame.rotation;
			if (!havePrev)
			{
				prevX = wx;
				prevZ = wz;
				prevY = wy;
				prevRot = rot;
				havePrev = true;
			}
			Vector3 vel = rb.linearVelocity;
			float speed = vel.magnitude;
			if (speed >= HitSlowest)
			{
				// The poses tested: last frame's (moved into this scene's frame), between, and now.
				Vector3 prevPos = Origin.ToLocal(new Vector3((float)prevX, prevY, (float)prevZ));
				Vector3 nowPos = frame.position;
				float jump = (nowPos - prevPos).magnitude;
				int steps = jump > 40f ? 0 : Mathf.Clamp(Mathf.CeilToInt(jump / 1.5f), 0, 8);
				foreach (View v in live.Values)
				{
					if (v.Dead || !v.Seen || v.Fresh || (v.Root.position - nowPos).sqrMagnitude > 30f * 30f)
					{
						continue;
					}
					Shapes(v);
					if (!Touches(prevPos, prevRot, nowPos, rot, steps, out Vector3 contact))
					{
						continue;
					}
					Hit(car, v, contact, vel, t);
				}
			}
			prevX = wx;
			prevZ = wz;
			prevY = wy;
			prevRot = rot;
		}

		/// <summary>Whether any of <see cref="shapes"/> is in the car's box at any of the poses tested; where, if so.</summary>
		private bool Touches(Vector3 prevPos, Quaternion prevRot, Vector3 nowPos, Quaternion nowRot, int steps, out Vector3 contact)
		{
			for (int k = steps; k >= 0; k--)
			{
				float f = steps == 0 ? 1f : (float)k / steps;
				Vector3 pos = Vector3.Lerp(prevPos, nowPos, f);
				Quaternion inv = Quaternion.Inverse(Quaternion.Slerp(prevRot, nowRot, f));
				foreach ((Vector3 a, Vector3 b, float r, int n) in shapes)
				{
					for (int i = 0; i < n; i++)
					{
						Vector3 w = n == 1 ? a : Vector3.Lerp(a, b, (float)i / (n - 1));
						Vector3 p = inv * (w - pos);
						if (p.x > boxMin.x - r && p.x < boxMax.x + r && p.y > boxMin.y - r && p.y < boxMax.y + r && p.z > boxMin.z - r && p.z < boxMax.z + r)
						{
							contact = w;
							return true;
						}
					}
				}
			}
			contact = default;
			return false;
		}

		/// <summary>A car's hit on a deer: the damage, the shake, the sound, the car slowed, and the deer killed and thrown.</summary>
		private void Hit(CarControllerNew car, View v, Vector3 contact, Vector3 vel, double t)
		{
			Herds.Member member = herds.Groups[v.G].Members[v.M];
			float size = (float)member.Scale;
			float weight = size * size * size;
			float speed = vel.magnitude;
			Vector3 dir = vel / speed;
			Vector3 toDeer = contact - car.rb.worldCenterOfMass;
			toDeer.y = 0f;
			toDeer = toDeer.sqrMagnitude > 0.01f ? toDeer.normalized : new Vector3(dir.x, 0f, dir.z).normalized;
			// Head on, the whole speed; a glancing blow (the deer at the car's side), less.
			float along = Mathf.Clamp(0.45f + 0.55f * Vector3.Dot(dir, toDeer), 0.2f, 1f);
			float impact = speed * along;

			// The damage: the game's own crash, scaled to a deer (a wall at the speed the game deals its full crash damage would do
			// maxCrashDamage; a doe does HitShare of that, and never more than HitMost of it).
			float slowest = car.crashMinImpactSpeed / SpeedDashboard.SpeedToDisplay, full = car.crashFullDamageSpeed / SpeedDashboard.SpeedToDisplay;
			float most = car.maxCrashDamage;
			float damage = most * HitShare * weight * (impact * impact - slowest * slowest) / Mathf.Max(1f, full * full);
			damage = Mathf.Min(damage, most * HitMost * weight, most);
			CarLink link = car.carLink;
			if (damage >= 1f && link != null)
			{
				link.TakeCrashDamage(damage, shake: true, DamageKind.Crash);
				if (Shaker.instance != null && link.crashShakeDamage > 0f)
				{
					// Harder than a wall's kick for the same damage: a deer strike is sudden, at the front, in the face.
					Shaker.instance.KickSpring(Mathf.Clamp01(1.6f * damage / link.crashShakeDamage + 0.15f));
				}
			}
			else
			{
				damage = 0f;
			}
			Strike(car, impact, weight);
			// The car loses some of its way to the deer's weight, and the nose is knocked up a little as the body goes over the bonnet.
			car.rb.AddForceAtPosition(-vel * (HitSlowing * weight * along * car.rb.mass), contact, ForceMode.Impulse);
			car.rb.AddForceAtPosition(Vector3.up * (0.04f * weight * impact * car.rb.mass), contact, ForceMode.Impulse);

			// The deer thrown on along the car's way, a little aside and up, tumbling toward the side it was pushed.
			Vector3 side = toDeer - dir * Vector3.Dot(toDeer, dir);
			Vector3 thrown = vel * (1.0f + 0.1f * along) + side * (0.15f * speed) + Vector3.up * Mathf.Min((float)DeerKills.MostLaunch, 1.5f + 0.09f * impact);
			double yaw = v.StateYaw;
			double rightX = Math.Sin(yaw), rightZ = -Math.Cos(yaw);
			double push = thrown.x * rightX + thrown.z * rightZ;
			double u = Herds.Unit((ulong)(uint)kills.Seed, (ulong)v.G, (ulong)v.M, 0xD1E), u2 = Herds.Unit((ulong)(uint)kills.Seed, (ulong)v.G, (ulong)v.M, 0xD1F);
			double ground = Herds.Ground(v.WorldX, v.WorldZ);
			Kill(new DeerKills.Kill
			{
				Seed = kills.Seed, Group = v.G, Member = v.M, How = DeerKills.How.Car,
				X = v.WorldX, Z = v.WorldZ, Y = ground + DeerKills.CentreHeight * size,
				Vx = thrown.x, Vy = thrown.y, Vz = thrown.z, Yaw = yaw, Scale = size,
				Spin = (push >= 0.0 ? 1.0 : -1.0) * (2.5 + 4.0 * u) * Math.Min(1.0, impact / 20.0 + 0.3), Turn = (u2 - 0.5) * 3.0,
				Side = push >= 0.0 ? 1 : -1,
			}, t);
			hitCount++;
			Mod.Info("Deer: hit at " + (impact * SpeedDashboard.SpeedToDisplay).ToString("0") + " on the speedometer (the car at " + (speed * SpeedDashboard.SpeedToDisplay).ToString("0") + "): "
				+ damage.ToString("0.0") + " damage" + (CarLink.godMode ? " (god mode: none taken)" : ""));
		}

		/// <summary>
		/// The sound of a strike, in the cockpit (2D, through the game's own mixer, so its volume settings count): the game's own
		/// collision sounds, a wall crash and a car-on-car crunch together, a touch lower for the weight; louder the harder the hit.
		/// </summary>
		private static void Strike(CarControllerNew car, float impact, float weight)
		{
			try
			{
				float loud = Mathf.Clamp01(0.55f + impact / 45f) * Mathf.Clamp(weight, 0.7f, 1.2f);
				float pitch = UnityEngine.Random.Range(0.9f, 1.0f);
				GameObject at = car.gameObject;
				SoundController.PlayRandomSoundWithPitch2D(car.wallCollisionSounds, at, Mathf.Min(1f, loud), pitch);
				SoundController.PlayRandomSoundWithPitch2D(car.carCollisionSounds, at, Mathf.Min(1f, loud * 0.8f), pitch);
			}
			catch (Exception e)
			{
				Mod.Log.LogWarning("Deer: the strike's sound could not be played: " + e.Message);
			}
		}

		/// <summary>
		/// A shot from this player's gun (the gun's ray, from the patch on it): the nearest live deer drawn that the ray passes through
		/// before <paramref name="reach"/> (what the gun's ray struck, or its range) is killed, falling away from the shot; true, and
		/// where the bullet met it, if one was.
		/// </summary>
		internal bool Shoot(Vector3 origin, Vector3 direction, float reach, out Vector3 at)
		{
			at = default;
			if (herds == null || tm == null || direction.sqrMagnitude < 1e-6f)
			{
				return false;
			}
			Vector3 d = direction.normalized;
			View best = null;
			float bestT = reach;
			foreach (View v in live.Values)
			{
				if (v.Dead || !v.Seen || v.Fresh)
				{
					continue;
				}
				Vector3 c = v.Root.position - origin;
				float along = Vector3.Dot(c, d);
				if (along < -10f || along > bestT + 10f || (c - d * along).sqrMagnitude > 64f)
				{
					continue;
				}
				Shapes(v);
				foreach ((Vector3 a, Vector3 b, float r, int n) in shapes)
				{
					int samples = n == 1 ? 1 : n * 2 - 1;
					for (int i = 0; i < samples; i++)
					{
						Vector3 w = samples == 1 ? a : Vector3.Lerp(a, b, (float)i / (samples - 1));
						Vector3 q = w - origin;
						float tq = Vector3.Dot(q, d);
						float miss2 = (q - d * tq).sqrMagnitude;
						if (tq <= 0f || miss2 > r * r)
						{
							continue;
						}
						float enter = tq - Mathf.Sqrt(r * r - miss2);
						if (enter < bestT)
						{
							bestT = Mathf.Max(0f, enter);
							best = v;
						}
					}
				}
			}
			if (best == null)
			{
				return false;
			}
			at = origin + d * bestT;
			double yaw = best.StateYaw;
			double rightX = Math.Sin(yaw), rightZ = -Math.Cos(yaw);
			int side = d.x * rightX + d.z * rightZ >= 0.0 ? 1 : -1;
			Kill(new DeerKills.Kill
			{
				Seed = kills.Seed, Group = best.G, Member = best.M, How = DeerKills.How.Shot,
				X = best.WorldX, Z = best.WorldZ, Y = Herds.Ground(best.WorldX, best.WorldZ),
				Yaw = yaw, Scale = herds.Groups[best.G].Members[best.M].Scale, Side = side,
			}, tm.TimeSinceRaceStart);
			shotCount++;
			Mod.Info("Deer: shot at " + (bestT / 3.3f).ToString("0") + " m");
			return true;
		}

		/// <summary>A deer's objects: from the spares, or made (a root with the bones under it and the skinned mesh on it).</summary>
		private View Take(int key)
		{
			View v = spare.Count > 0 ? spare.Pop() : Make();
			v.Key = key;
			v.Fresh = true;
			v.Dead = false;
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
			r.sharedMesh = mesh;
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
			if (Active == this)
			{
				Active = null;
			}
		}
	}
}
