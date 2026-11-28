using System;
using System.Collections.Generic;
using UnityEngine;

namespace HalfGripOpenWorld
{
	/// <summary>
	/// The traffic in the game (the rework's phase 5): every car of the race's live traffic (<see cref="TrafficLive"/>) near the player drawn
	/// where the simulation has it, driving (micro near the player, meso further off) or parked in its spot. The traffic is built on a
	/// worker from the race's seed at the start, then run on the race clock here every frame.
	///
	/// <para>The cars are the game's own enemy car's bodies (its models, painted from a palette of everyday colours, the same colour for the
	/// same car every race), copied as meshes only: no physics, no AI, no guns (carried over from the old traffic's drawing on
	/// <c>traffic-test-branch</c>). Each has a box on the game's Enemy layer moved with it, so hitting one is hitting a wall, for now: the
	/// nearest moving cars become physics cars in a later step. At night the nearest few moving cars light the road with the enemy car's own
	/// headlight.</para>
	///
	/// <para>Drawn from a pool, the nearest cars only (moving ones count as nearer). World positions are kept as doubles and turned into the
	/// scene's on every frame, so an origin shift needs nothing here. A car pulling out of its spot or into one is drawn sliding from the one
	/// pose to the other over a couple of seconds (the meso parks a car at its stop's point on the road; its spot is beside it).</para>
	/// </summary>
	internal sealed partial class TrafficCars : MonoBehaviour
	{
		/// <summary>One line (or a few) for the debug overlay (ShowCoords).</summary>
		internal static string Status;

		/// <summary>The race's traffic once built (null before, or if it failed): the map overlay and other debug views read it.</summary>
		internal static TrafficLive Live;

		/// <summary>How far moving and parked cars are drawn (units: about 450 m and 230 m), and at most how many.</summary>
		private const float MovingReach = 1500f, ParkedReach = 750f;
		private const int MostDrawn = 240;

		/// <summary>Past this a car is drawn without shadows (about 75 m).</summary>
		private const float ShadowAt = 250f;

		/// <summary>Moving cars are chosen as if this much nearer than they are, so a town's parked cars never crowd out the road's.</summary>
		private const float MovingWeight = 0.4f;

		/// <summary>How often the nearest cars are chosen again (seconds).</summary>
		private const float ChooseEvery = 0.4f;

		/// <summary>How many moving cars at most have a real headlight at night (the renderer lights only so many lights an object).</summary>
		private const int MostLit = 6;

		/// <summary>How long a car takes to slide from its spot onto the road, or from the road into its spot (seconds).</summary>
		private const float PullTime = 2.5f;

		/// <summary>The paint: everyday car colours, with how often each is seen (white, black, greys and silver most).</summary>
		private static readonly (Color colour, int weight)[] Paints =
		{
			(new Color(0.92f, 0.92f, 0.90f), 25),
			(new Color(0.06f, 0.06f, 0.07f), 19),
			(new Color(0.38f, 0.39f, 0.41f), 18),
			(new Color(0.70f, 0.71f, 0.73f), 12),
			(new Color(0.10f, 0.20f, 0.45f), 9),
			(new Color(0.55f, 0.06f, 0.06f), 9),
			(new Color(0.42f, 0.33f, 0.24f), 3),
			(new Color(0.12f, 0.28f, 0.16f), 2),
			(new Color(0.85f, 0.65f, 0.12f), 1),
			(new Color(0.65f, 0.75f, 0.85f), 2),
		};

		/// <summary>A body made from one of the enemy car's models: an inactive copy of its meshes, and what the drawing needs to know of it.</summary>
		private sealed class Look
		{
			public GameObject Template;
			/// <summary>Its renderers that take the paint (indices in the template's renderers), and the painted renderers' own materials.</summary>
			public int[] Paint;
			public Material[][] PaintMaterials;
			/// <summary>The lamps (renderers with a glowing material, not painted): their lit materials and dark copies, put on when parked.</summary>
			public int[] Lamps;
			public Material[][] LampsLit, LampsDark;
			/// <summary>Its wheels (indices in its transforms) and each one's axle in its parent's frame.</summary>
			public int[] Wheels;
			public Vector3[] Axles;
			/// <summary>The body's box in the car's frame (units), the wheels' lowest point and radius, and half the wheelbase.</summary>
			public Bounds Box;
			public float Bottom, WheelRadius, Axle;
			/// <summary>The enemy car's headlight, in the car's frame.</summary>
			public bool HasLight;
			public Vector3 LightAt;
			public Quaternion LightTurn;
			/// <summary>Its renderers' own shadows (as the enemy car's), for a near car's.</summary>
			public UnityEngine.Rendering.ShadowCastingMode[] ShadowModes;
		}

		/// <summary>A car being drawn.</summary>
		private sealed class View
		{
			public GameObject Go;
			public Transform Root;
			public Look Look;
			public int LookIndex;
			public MeshRenderer[] Renderers;
			public Transform[] Wheels;
			public Quaternion[] WheelRest;
			public float Spin;
			public Light Light;
			public int Car = -1;
			public bool Seen;
			public int Paint = -1;
			/// <summary>Whether its lamps are lit (driving) or dark (parked); -1 not set yet. Whether its shadows are on.</summary>
			public int LampsOn = -1;
			public bool Shadows = true;
			/// <summary>Where it was last drawn (world x, z, the way it faced) and what it was doing; NaN: not drawn yet.</summary>
			public double LastX = double.NaN, LastZ, LastHx, LastHz;
			public byte LastKind;
			/// <summary>Sliding from a pose to the live one (pulling out of or into its spot): where from, and how long it has still to go.</summary>
			public double FromX, FromZ, FromHx, FromHz;
			public float Blend;
			/// <summary>Parked: where it was stood (world x, z), so a parked car is placed once.</summary>
			public double PlacedX = double.NaN, PlacedZ;
		}

		private TrackManager tm;
		private System.Threading.Tasks.Task<TrafficLive> building;
		private TrafficLive live;
		private bool failed;
		private int raceSeed;
		private double startSecond;
		private Look[] looks;
		private Light lightTemplate;
		private int layer, groundMask;
		private readonly Dictionary<(Material, int), Material> painted = new Dictionary<(Material, int), Material>();
		private readonly Dictionary<int, View> drawn = new Dictionary<int, View>();
		private readonly List<Stack<View>> spare = new List<Stack<View>>();
		private readonly List<(float d, int car, bool moving)> near = new List<(float, int, bool)>();
		private readonly List<int> chosen = new List<int>();
		private readonly HashSet<int> chosenMoving = new HashSet<int>();
		private readonly List<int> gone = new List<int>();
		private float nextChoose;
		private double chosenX, chosenZ;
		private int drawnMoving, drawnParked, lit;
		private float chooseMs, drawMs;
		private int following = -1;

		internal void Setup(TrackManager track)
		{
			tm = track;
			layer = LayerMask.NameToLayer("Enemy");
			if (layer < 0)
			{
				layer = 0;
			}
			// The ground and the roads, the lots and the drives: what a car stands on.
			int floor = LayerMask.NameToLayer("Floor");
			groundMask = (1 << 0) | (floor >= 0 ? 1 << floor : 0);
			raceSeed = Weather.Seed;
			startSecond = Schedules.StartOf(Sky.StartHourOf(raceSeed));
			Live = null;
			try
			{
				GameObject prefab = tm.enemyCarPrefab;
				if (prefab == null)
				{
					throw new Exception("the race has no enemy car to copy");
				}
				looks = MakeLooks(prefab);
				for (int i = 0; i < looks.Length; i++)
				{
					spare.Add(new Stack<View>());
				}
			}
			catch (Exception e)
			{
				Mod.Log.LogWarning("The traffic's cars could not be made (no traffic this race): " + e);
				failed = true;
				return;
			}
			int seed = raceSeed;
			double start = startSecond;
			building = System.Threading.Tasks.Task.Run(() => TrafficLive.Build(seed, start, Network.SpawnX, Network.SpawnZ));
		}

		// ---- the bodies -------------------------------------------------------------------------------------------------------

		/// <summary>One look for each of the enemy car's models (its <see cref="CarSkinController.carModels"/>; the whole car if it has none).</summary>
		private Look[] MakeLooks(GameObject prefab)
		{
			CarSkinController skin = prefab.GetComponent<CarSkinController>();
			GameObject[] models = skin != null && skin.carModels != null ? skin.carModels : new GameObject[0];
			HeadLightsController lights = prefab.GetComponentInChildren<HeadLightsController>(true);
			if (lights != null && lights.spotlight != null)
			{
				lightTemplate = lights.spotlight.GetComponent<Light>();
			}
			List<Look> made = new List<Look>();
			for (int i = 0; i < Math.Max(1, models.Length); i++)
			{
				Look look = MakeLook(prefab, skin, models, i);
				if (look != null)
				{
					made.Add(look);
				}
			}
			if (made.Count == 0)
			{
				throw new Exception("the enemy car has no meshes to draw");
			}
			return made.ToArray();
		}

		/// <summary>
		/// A copy of model <paramref name="index"/>'s meshes (and the wheels, which every model shares) as plain renderers under an inactive
		/// root, laid out as in the car with the car's own scale: everything a model shows when the game puts it on, nothing that is switched
		/// off (the guns, the nitro flames) and no effects, sounds or scripts. The wheels are let down to where the game draws them with the
		/// springs at full length (the prefab has them where a loaded spring would put them, so a parked copy squatted).
		/// </summary>
		private Look MakeLook(GameObject prefab, CarSkinController skin, GameObject[] models, int index)
		{
			Transform root = prefab.transform;
			GameObject template = new GameObject("Traffic car " + index) { layer = layer };
			template.SetActive(false);
			template.transform.SetParent(transform, false);
			template.transform.localScale = root.localScale;
			Dictionary<Transform, Transform> copies = new Dictionary<Transform, Transform> { [root] = template.transform };
			HashSet<Transform> lowered = new HashSet<Transform>();
			HashSet<MeshRenderer> paint = new HashSet<MeshRenderer>();
			HashSet<Transform> wheels = new HashSet<Transform>();
			Matrix4x4 toCar = Matrix4x4.Scale(root.localScale) * root.worldToLocalMatrix;
			bool any = false, anyWheel = false;
			Bounds box = default, wheelBox = default;
			float wheelRadius = 0f;
			Dictionary<Transform, Vector3> wheelPlace = WheelPlaces(prefab);
			foreach (MeshRenderer r in root.GetComponentsInChildren<MeshRenderer>(true))
			{
				MeshFilter f = r.GetComponent<MeshFilter>();
				if (!r.enabled || f == null || f.sharedMesh == null || !Shown(r.transform, root, models, index, out Transform wheel))
				{
					continue;
				}
				Transform copy = Copy(r.transform, copies);
				copy.gameObject.AddComponent<MeshFilter>().sharedMesh = f.sharedMesh;
				MeshRenderer cr = copy.gameObject.AddComponent<MeshRenderer>();
				cr.sharedMaterials = r.sharedMaterials;
				cr.shadowCastingMode = r.shadowCastingMode;
				cr.receiveShadows = r.receiveShadows;
				if (skin != null && ((skin.carMeshRenderers != null && index < skin.carMeshRenderers.Length && skin.carMeshRenderers[index] == r)
					|| (skin.hoodsRenderers != null && index < skin.hoodsRenderers.Length && skin.hoodsRenderers[index] == r)))
				{
					paint.Add(cr);
				}
				Bounds b = Through(f.sharedMesh.bounds, toCar * r.transform.localToWorldMatrix);
				if (wheel != null && wheelPlace.TryGetValue(wheel, out Vector3 local))
				{
					Transform wc = copies[wheel];
					if (lowered.Add(wc))
					{
						wc.localPosition = local;
					}
					b.center += (toCar * wheel.parent.localToWorldMatrix).MultiplyVector(local - wheel.localPosition);
				}
				if (wheel != null)
				{
					wheels.Add(copies[wheel]);
					wheelRadius = Mathf.Max(wheelRadius, b.extents.y);
					if (anyWheel)
					{
						wheelBox.Encapsulate(b);
					}
					else
					{
						wheelBox = b;
						anyWheel = true;
					}
				}
				if (any)
				{
					box.Encapsulate(b);
				}
				else
				{
					box = b;
					any = true;
				}
			}
			if (!any)
			{
				Destroy(template);
				return null;
			}
			MeshRenderer[] renderers = template.GetComponentsInChildren<MeshRenderer>(true);
			UnityEngine.Rendering.ShadowCastingMode[] shadowModes = new UnityEngine.Rendering.ShadowCastingMode[renderers.Length];
			for (int k = 0; k < renderers.Length; k++)
			{
				shadowModes[k] = renderers[k].shadowCastingMode;
			}
			Transform[] all = template.GetComponentsInChildren<Transform>(true);
			List<int> paintAt = new List<int>(), wheelAt = new List<int>();
			List<Vector3> axles = new List<Vector3>();
			for (int k = 0; k < renderers.Length; k++)
			{
				if (paint.Contains(renderers[k]))
				{
					paintAt.Add(k);
				}
			}
			for (int k = 0; k < all.Length; k++)
			{
				if (wheels.Contains(all[k]))
				{
					wheelAt.Add(k);
					// The car's right in the wheel's parent's frame: the axle it turns on, whatever way the wheel's own frame faces.
					Quaternion parent = Quaternion.Inverse(template.transform.rotation) * all[k].parent.rotation;
					axles.Add(Quaternion.Inverse(parent) * Vector3.right);
				}
			}
			// The lamps: a material that glows (headlights white, tail lights red) on a renderer that takes no paint. Their dark copies
			// (no glow) go on a parked car.
			List<int> lampAt = new List<int>();
			List<Material[]> lampLit = new List<Material[]>(), lampDark = new List<Material[]>();
			for (int k = 0; k < renderers.Length; k++)
			{
				if (paint.Contains(renderers[k]))
				{
					continue;
				}
				Material[] mats = renderers[k].sharedMaterials;
				Material[] dark = null;
				for (int m = 0; m < mats.Length; m++)
				{
					if (Glows(mats[m]))
					{
						dark ??= (Material[])mats.Clone();
						Material d = new Material(mats[m]) { name = mats[m].name + " (traffic, off)", hideFlags = HideFlags.HideAndDontSave };
						d.SetColor("_EmissionColor", Color.black);
						d.DisableKeyword("_EMISSION");
						dark[m] = d;
						GetComponent<WorldAssets>()?.Add(d);
					}
				}
				if (dark != null)
				{
					lampAt.Add(k);
					lampLit.Add(mats);
					lampDark.Add(dark);
				}
			}
			Material[][] paintMaterials = new Material[paintAt.Count][];
			for (int k = 0; k < paintAt.Count; k++)
			{
				paintMaterials[k] = renderers[paintAt[k]].sharedMaterials;
			}
			Look look = new Look
			{
				Template = template,
				Paint = paintAt.ToArray(),
				PaintMaterials = paintMaterials,
				Lamps = lampAt.ToArray(),
				LampsLit = lampLit.ToArray(),
				LampsDark = lampDark.ToArray(),
				Wheels = wheelAt.ToArray(),
				Axles = axles.ToArray(),
				Box = box,
				Bottom = anyWheel ? wheelBox.min.y : box.min.y,
				WheelRadius = Mathf.Max(0.5f, wheelRadius),
				Axle = anyWheel ? Mathf.Max(2f, wheelBox.extents.z * 0.85f) : Mathf.Max(2f, box.extents.z * 0.6f),
				ShadowModes = shadowModes,
			};
			if (lightTemplate != null)
			{
				Matrix4x4 m = toCar * lightTemplate.transform.localToWorldMatrix;
				look.HasLight = true;
				look.LightAt = m.MultiplyPoint3x4(Vector3.zero);
				look.LightTurn = Quaternion.Inverse(root.rotation) * lightTemplate.transform.rotation;
			}
			Mod.Info("Traffic: look " + index + ": " + renderers.Length + " meshes, " + look.Paint.Length + " painted, " + look.Lamps.Length + " lamps, " + look.Wheels.Length + " wheels, box " + box.size.ToString("0.0") + ", wheels' bottom " + look.Bottom.ToString("0.00") + ", radius " + look.WheelRadius.ToString("0.00"));
			return look;
		}

		/// <summary>
		/// Where each of the enemy car's wheels is drawn on a copy (in its parent's frame): as the game draws it with the spring at full length,
		/// its place widened by 1.1 as <c>InitializeWheels</c> widens it and let down by (length - 0.3).
		/// </summary>
		private static Dictionary<Transform, Vector3> WheelPlaces(GameObject prefab)
		{
			Dictionary<Transform, Vector3> at = new Dictionary<Transform, Vector3>();
			CarControllerNew cc = prefab.GetComponent<CarControllerNew>();
			if (cc == null)
			{
				return at;
			}
			Transform root = prefab.transform;
			foreach (List<Transform> list in new[] { cc.frontWheels, cc.backWheels })
			{
				foreach (Transform w in list ?? new List<Transform>())
				{
					if (w == null)
					{
						continue;
					}
					Vector3 place = root.InverseTransformPoint(w.position);
					Vector3 shown = new Vector3(place.x * 1.1f, place.y - (cc.suspensionRestLength - 0.3f), place.z);
					at[w] = w.parent.InverseTransformPoint(root.TransformPoint(shown));
				}
			}
			return at;
		}

		/// <summary>Whether a material glows (a lamp): emission on and brighter than a trace.</summary>
		internal static bool Glows(Material m)
		{
			if (m == null || !m.HasProperty("_EmissionColor"))
			{
				return false;
			}
			Color e = m.GetColor("_EmissionColor");
			float most = Mathf.Max(e.r, Mathf.Max(e.g, e.b));
			// The car's lamps are far over 1 (white 512, red 767), whether or not the keyword shows it.
			return (m.IsKeywordEnabled("_EMISSION") && most > 0.05f) || most > 1f;
		}

		/// <summary>
		/// Whether part <paramref name="t"/> shows on model <paramref name="index"/>: every object from it up to the car switched on, the models
		/// counted as the skin switches them (only the one worn), nothing under the guns or the effects. The wheel it belongs to, if any.
		/// </summary>
		private static bool Shown(Transform t, Transform root, GameObject[] models, int index, out Transform wheel)
		{
			wheel = null;
			for (Transform n = t; n != null && n != root; n = n.parent)
			{
				int model = Array.IndexOf(models, n.gameObject);
				if (model >= 0)
				{
					if (model != index)
					{
						return false;
					}
				}
				else if (!n.gameObject.activeSelf)
				{
					return false;
				}
				string name = n.name;
				if (name.StartsWith("GunFirePoint", StringComparison.Ordinal) || name.StartsWith("EffectsController", StringComparison.Ordinal) || name.StartsWith("Nitroflames", StringComparison.Ordinal))
				{
					return false;
				}
				if (name.StartsWith("Wheel_", StringComparison.Ordinal))
				{
					wheel = n;
				}
			}
			return true;
		}

		/// <summary>The copy of <paramref name="t"/> in the template, made with every parent up to the car it lacks.</summary>
		private Transform Copy(Transform t, Dictionary<Transform, Transform> copies)
		{
			if (copies.TryGetValue(t, out Transform found))
			{
				return found;
			}
			Transform parent = Copy(t.parent, copies);
			GameObject go = new GameObject(t.name) { layer = t.gameObject.layer };
			go.transform.SetParent(parent, false);
			go.transform.localPosition = t.localPosition;
			go.transform.localRotation = t.localRotation;
			go.transform.localScale = t.localScale;
			copies[t] = go.transform;
			return go.transform;
		}

		/// <summary>A mesh's bounds through a matrix, as a box round its eight corners.</summary>
		private static Bounds Through(Bounds b, Matrix4x4 m)
		{
			Bounds o = new Bounds(m.MultiplyPoint3x4(b.min), Vector3.zero);
			for (int i = 1; i < 8; i++)
			{
				Vector3 c = new Vector3((i & 1) != 0 ? b.max.x : b.min.x, (i & 2) != 0 ? b.max.y : b.min.y, (i & 4) != 0 ? b.max.z : b.min.z);
				o.Encapsulate(m.MultiplyPoint3x4(c));
			}
			return o;
		}

		/// <summary>The body's material in paint <paramref name="p"/>: one copy for each material and paint, shared by every car wearing it.</summary>
		private Material Painted(Material m, int p)
		{
			if (m == null || !m.HasProperty("_EntireColor"))
			{
				return m;
			}
			if (painted.TryGetValue((m, p), out Material found))
			{
				return found;
			}
			Material made = new Material(m) { name = m.name + " (traffic " + p + ")", hideFlags = HideFlags.HideAndDontSave };
			Color c = Paints[p].colour;
			// The whole body the paint, the accents a shade of it, the third channel (trim) dark.
			made.SetColor("_EntireColor", c);
			if (made.HasProperty("_AccentColor"))
			{
				made.SetColor("_AccentColor", c * 0.8f);
			}
			if (made.HasProperty("_TertiaryColor"))
			{
				made.SetColor("_TertiaryColor", new Color(0.08f, 0.08f, 0.09f));
			}
			painted[(m, p)] = made;
			GetComponent<WorldAssets>()?.Add(made);
			return made;
		}

		/// <summary>Car <paramref name="car"/>'s body and paint, the same every race (a household's car is the same car each time).</summary>
		private void Wears(int car, out int look, out int paint)
		{
			ulong h = Noise.Mix((ulong)(uint)car * 0x9E3779B97F4A7C15UL ^ 0xCA5UL);
			look = (int)(h % (ulong)looks.Length);
			int total = 0;
			foreach ((Color _, int weight) in Paints)
			{
				total += weight;
			}
			int pick = (int)((h >> 20) % (ulong)total);
			paint = 0;
			while (pick >= Paints[paint].weight)
			{
				pick -= Paints[paint].weight;
				paint++;
			}
		}

		// ---- each frame -------------------------------------------------------------------------------------------------------

		private void Update()
		{
			if (failed || tm == null)
			{
				return;
			}
			if (live == null)
			{
				if (building == null || !building.IsCompleted)
				{
					return;
				}
				if (building.IsFaulted || building.Result == null)
				{
					Mod.Log.LogWarning("The traffic could not be made (no traffic this race): " + building.Exception);
					failed = true;
					return;
				}
				live = building.Result;
				building = null;
				Live = live;
				Schedules p = live.Plans;
				Mod.Log.LogInfo("Traffic: " + p.Cars.Count + " cars on a " + (p.Weekend ? "weekend day" : "weekday") + " from the race's seed (" + raceSeed + "), from "
					+ TrafficLive.Clock(live.Start) + "; plans " + p.Report() + "; built in " + live.BuildMs.ToString("0") + " ms (the map: " + Traffic.Report + "); parking: "
					+ live.Parking.OwnSpaces + " cars with their own space at home, " + live.Parking.KerbHomes + " at the kerb, " + live.Parking.RoundTheBack + " round the back");
			}
			CarControllerNew car = tm.playerCarController;
			long started = System.Diagnostics.Stopwatch.GetTimestamp();
			try
			{
				if (car != null)
				{
					Origin.World(car.transform.position, out double cx, out double cz);
					live.PlayerX = cx;
					live.PlayerZ = cz;
					live.HasPlayer = true;
					// Its body for the micro's drivers: where it is, the way it faces and moves (its box as an everyday car's).
					Vector3 f = car.transform.forward;
					Vector3 vel = car.rb != null ? car.rb.linearVelocity : Vector3.zero;
					float fl = Mathf.Sqrt(f.x * f.x + f.z * f.z);
					live.PlayerBody = new Micro.Body
					{
						X = cx,
						Z = cz,
						Hx = fl > 1e-4f ? f.x / fl : 0.0,
						Hz = fl > 1e-4f ? f.z / fl : 1.0,
						Vx = vel.x,
						Vz = vel.z,
						HalfLong = Traffic.CarLength * 0.5,
						HalfWide = TrafficParking.CarWide * 0.5,
					};
					live.HasBody = true;
				}
				double now = live.DayAt(tm.TimeSinceRaceStart);
				bool show = Mod.ShowTraffic.Value && car != null;
				// The physics cars' bodies read into their agents first: the step places them where their bodies are.
				ReadBodies(now);
				live.Advance(now);
				Bodies(now, live.PlayerX, live.PlayerZ, show);
				if (!show)
				{
					Clear();
					Status = null;
				}
				else
				{
					Draw(now, live.PlayerX, live.PlayerZ);
					Follow(now);
				}
			}
			catch (Exception e)
			{
				Mod.Log.LogWarning("The traffic stopped: " + e);
				try
				{
					ClearPhysics();
				}
				catch (Exception e2)
				{
					Mod.Log.LogWarning("The traffic's physics cars could not be let go: " + e2.Message);
				}
				Clear();
				failed = true;
				Live = null;
			}
			FramePerf.Add(FramePerf.Part.Traffic, started);
		}

		private void Draw(double now, double cx, double cz)
		{
			double mx = cx - chosenX, mz = cz - chosenZ;
			if (Time.unscaledTime >= nextChoose || mx * mx + mz * mz > 150.0 * 150.0)
			{
				nextChoose = Time.unscaledTime + ChooseEvery;
				chosenX = cx;
				chosenZ = cz;
				Choose(cx, cz);
			}
			long drew = System.Diagnostics.Stopwatch.GetTimestamp();
			foreach (View v in drawn.Values)
			{
				v.Seen = false;
			}
			drawnMoving = drawnParked = lit = 0;
			float dt = Time.deltaTime;
			bool lights = Sky.LightsOn;
			foreach (int c in chosen)
			{
				if (!drawn.TryGetValue(c, out View v))
				{
					v = Take(c);
					drawn[c] = v;
				}
				v.Seen = true;
				if (bodies.TryGetValue(c, out TrafficBody body) && body != null)
				{
					// A physics car: its body is drawn, its copy hidden (and its box with it) until the body is let go.
					if (v.Go.activeSelf)
					{
						v.Go.SetActive(false);
					}
					bool beam = lights && lit < MostLit;
					body.Headlamp(beam);
					lit += beam ? 1 : 0;
					drawnMoving++;
					continue;
				}
				if (!Place(v, c, now, cx, cz, dt))
				{
					v.Go.SetActive(false);
					continue;
				}
				// The nearest moving cars' headlights (the chosen list is nearest first).
				bool on = lights && v.Light != null && v.LastKind == TrafficLive.Driving && lit < MostLit && v.Go.activeSelf;
				if (on)
				{
					lit++;
				}
				if (v.Light != null && v.Light.enabled != on)
				{
					v.Light.enabled = on;
				}
			}
			gone.Clear();
			foreach (KeyValuePair<int, View> kv in drawn)
			{
				if (!kv.Value.Seen)
				{
					gone.Add(kv.Key);
				}
			}
			foreach (int c in gone)
			{
				Give(drawn[c]);
				drawn.Remove(c);
			}
			drawMs = drawMs * 0.95f + 0.05f * (float)((System.Diagnostics.Stopwatch.GetTimestamp() - drew) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
			if (Mod.ShowCoords.Value)
			{
				Schedules p = live.Plans;
				Status = "traffic: " + TrafficLive.Clock(live.M.Now) + " on a " + (p.Weekend ? "weekend day" : "weekday") + ", " + live.DrivingNow + " driving (" + live.MicroNow + " near), "
					+ live.ParkedNow + " parked; drawn " + drawnMoving + " moving, " + drawnParked + " parked, " + lit + " lit; simulation " + live.StepMs.ToString("0.00")
					+ " ms a second, choosing " + chooseMs.ToString("0.0") + " ms, drawing " + drawMs.ToString("0.00") + " ms a frame" + PhysicsStatus() + (followText != null ? "\n" + followText : "");
			}
			else
			{
				Status = null;
			}
		}

		/// <summary>The cars to draw: every car's last pose, the nearest within reach (moving ones weighted nearer), at most <see cref="MostDrawn"/>.</summary>
		private void Choose(double cx, double cz)
		{
			long started = System.Diagnostics.Stopwatch.GetTimestamp();
			near.Clear();
			for (int c = 0; c < live.Count; c++)
			{
				byte k = live.Where(c, out double x, out double z);
				if (k == TrafficLive.None)
				{
					continue;
				}
				bool moving = k == TrafficLive.Driving;
				float reach = moving ? MovingReach : ParkedReach;
				double dx = x - cx, dz = z - cz, d2 = dx * dx + dz * dz;
				if (d2 < (double)reach * reach)
				{
					near.Add(((float)Math.Sqrt(d2) * (moving ? MovingWeight : 1f), c, moving));
				}
			}
			near.Sort((a, b) => a.d != b.d ? a.d.CompareTo(b.d) : a.car.CompareTo(b.car));
			chosen.Clear();
			chosenMoving.Clear();
			for (int k = 0; k < near.Count && k < MostDrawn; k++)
			{
				chosen.Add(near[k].car);
				if (near[k].moving)
				{
					chosenMoving.Add(near[k].car);
				}
			}
			chooseMs = (float)((System.Diagnostics.Stopwatch.GetTimestamp() - started) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
		}

		/// <summary>Puts car <paramref name="c"/> where the traffic has it now; false if it is not to be seen (away, in a tunnel, no ground under it).</summary>
		private bool Place(View v, int c, double now, double cx, double cz, float dt)
		{
			byte kind = live.Pose(c, now, out double x, out double z, out double hx, out double hz, out double y);
			if (kind == TrafficLive.None)
			{
				v.LastKind = TrafficLive.None;
				v.LastX = double.NaN;
				v.Blend = 0f;
				return false;
			}
			// Pulling out of its spot onto the road, or off the road into its spot: slid from the pose it was drawn at to the live one.
			if (!double.IsNaN(v.LastX) && v.LastKind != kind && v.LastKind != TrafficLive.None)
			{
				v.FromX = v.LastX;
				v.FromZ = v.LastZ;
				v.FromHx = v.LastHx;
				v.FromHz = v.LastHz;
				v.Blend = PullTime;
				v.PlacedX = double.NaN;
			}
			double dx = x, dz = z, dhx = hx, dhz = hz;
			if (v.Blend > 0f)
			{
				v.Blend = Mathf.Max(0f, v.Blend - dt);
				float f = 1f - v.Blend / PullTime;
				f = f * f * (3f - 2f * f);
				dx = v.FromX + (x - v.FromX) * f;
				dz = v.FromZ + (z - v.FromZ) * f;
				double ax = v.FromHx + (hx - v.FromHx) * f, az = v.FromHz + (hz - v.FromHz) * f, len = Math.Sqrt(ax * ax + az * az);
				if (len > 1e-6)
				{
					dhx = ax / len;
					dhz = az / len;
				}
			}
			bool moving = kind == TrafficLive.Driving || v.Blend > 0f;
			double ex = dx - cx, ez = dz - cz;
			bool shadows = ex * ex + ez * ez < (double)ShadowAt * ShadowAt;
			Shadows(v, shadows);
			Lamps(v, kind == TrafficLive.Driving);
			double refY = double.IsNaN(y) ? WorldGen.Height(dx, dz) : y;
			if (!moving)
			{
				// Parked: stood once where it is (it hangs from the world's root, which an origin shift moves with the world).
				if (v.PlacedX != dx || v.PlacedZ != dz || !v.Go.activeSelf)
				{
					if (!Stand(v, dx, dz, refY, dhx, dhz))
					{
						v.PlacedX = double.NaN;
						return false;
					}
					v.PlacedX = dx;
					v.PlacedZ = dz;
				}
				drawnParked++;
			}
			else
			{
				v.PlacedX = double.NaN;
				if (!Stand(v, dx, dz, refY, dhx, dhz))
				{
					return false;
				}
				// The wheels turn with the distance it is drawn moving, backwards when it moves backwards; a jump turns nothing.
				double mx = double.IsNaN(v.LastX) ? 0.0 : dx - v.LastX, mz = double.IsNaN(v.LastX) ? 0.0 : dz - v.LastZ;
				double moved = Math.Sqrt(mx * mx + mz * mz);
				if (moved > 5.0 + 80.0 * dt)
				{
					moved = 0.0;
				}
				float roll = (float)(mx * dhx + mz * dhz >= 0.0 ? moved : -moved);
				v.Spin = Mathf.Repeat(v.Spin + roll / v.Look.WheelRadius * Mathf.Rad2Deg, 360f);
				Spin(v);
				drawnMoving++;
			}
			v.LastX = dx;
			v.LastZ = dz;
			v.LastHx = dhx;
			v.LastHz = dhz;
			if (v.Blend <= 0f)
			{
				v.LastKind = kind;
			}
			return true;
		}

		/// <summary>
		/// Puts the car's middle at world (x, z) facing (hx, hz), standing on what is under its front and back wheels (looked for down from a
		/// little over <paramref name="refY"/>, the road's height or the ground), pitched to them; false if nothing is under it yet.
		/// </summary>
		private bool Stand(View v, double x, double z, double refY, double hx, double hz)
		{
			float len = (float)Math.Sqrt(hx * hx + hz * hz);
			Vector3 fwd = len > 1e-4f ? new Vector3((float)(hx / len), 0f, (float)(hz / len)) : Vector3.forward;
			Vector3 mid = Origin.Local(x, (float)refY, z);
			float axle = v.Look.Axle;
			float top = (float)Math.Max(refY, WorldGen.Height(x, z)) + 6f;
			bool hf = Ground(mid + fwd * axle, top, out float yf);
			bool hb = Ground(mid - fwd * axle, top, out float yb);
			if (!hf && !hb)
			{
				return false;
			}
			if (!hf)
			{
				yf = yb;
			}
			if (!hb)
			{
				yb = yf;
			}
			float pitch = Mathf.Atan2(yf - yb, axle * 2f) * Mathf.Rad2Deg;
			Quaternion turn = Quaternion.LookRotation(fwd, Vector3.up) * Quaternion.Euler(-pitch, 0f, 0f);
			// The wheels' bottom on the ground under the middle.
			Vector3 at = new Vector3(mid.x, (yf + yb) * 0.5f, mid.z) - turn * new Vector3(0f, v.Look.Bottom, 0f);
			v.Root.SetPositionAndRotation(at, turn);
			if (!v.Go.activeSelf)
			{
				v.Go.SetActive(true);
			}
			return true;
		}

		/// <summary>The height of the ground, road or lot under scene point <paramref name="p"/>, looked for down from <paramref name="top"/>.</summary>
		private bool Ground(Vector3 p, float top, out float y)
		{
			if (Physics.Raycast(new Vector3(p.x, top, p.z), Vector3.down, out RaycastHit hit, 24f, groundMask, QueryTriggerInteraction.Ignore))
			{
				y = hit.point.y;
				return true;
			}
			y = 0f;
			return false;
		}

		/// <summary>Lights or darkens a car's lamps (only when that changes).</summary>
		private static void Lamps(View v, bool on)
		{
			if (v.LampsOn == (on ? 1 : 0))
			{
				return;
			}
			v.LampsOn = on ? 1 : 0;
			for (int i = 0; i < v.Look.Lamps.Length; i++)
			{
				v.Renderers[v.Look.Lamps[i]].sharedMaterials = on ? v.Look.LampsLit[i] : v.Look.LampsDark[i];
			}
		}

		/// <summary>A near car casts its body's own shadows; a far one none (only when that changes).</summary>
		private static void Shadows(View v, bool on)
		{
			if (v.Shadows == on)
			{
				return;
			}
			v.Shadows = on;
			for (int i = 0; i < v.Renderers.Length; i++)
			{
				v.Renderers[i].shadowCastingMode = on ? v.Look.ShadowModes[i] : UnityEngine.Rendering.ShadowCastingMode.Off;
			}
		}

		private static void Spin(View v)
		{
			for (int i = 0; i < v.Wheels.Length; i++)
			{
				v.Wheels[i].localRotation = Quaternion.AngleAxis(v.Spin, v.Look.Axles[i]) * v.WheelRest[i];
			}
		}

		// ---- the follow-car key -------------------------------------------------------------------------------------------------

		private string followText;

		/// <summary>
		/// The follow-car key (with ShowCoords): picks the driving car nearest the player (again: the next nearest), and from then on the
		/// overlay shows its day, what it is doing and how far off it is, however far it goes. Shift with it lets go.
		/// </summary>
		private void Follow(double now)
		{
			if (Mod.FollowCarKey != null && Mod.FollowCarKey.Down && Mod.ShowCoords.Value)
			{
				if (GameInput.GetKey(KeyCode.LeftShift) || GameInput.GetKey(KeyCode.RightShift))
				{
					following = -1;
				}
				else
				{
					following = NearestDriving(following);
				}
			}
			if (following < 0 || !Mod.ShowCoords.Value)
			{
				followText = null;
				return;
			}
			byte k = live.Where(following, out double x, out double z);
			double dx = x - live.PlayerX, dz = z - live.PlayerZ;
			followText = "following " + live.Describe(following) + (k != TrafficLive.None ? "\n  " + (Math.Sqrt(dx * dx + dz * dz) / 3.3).ToString("0") + " m off at " + x.ToString("0") + ", " + z.ToString("0") : "\n  out of sight");
		}

		/// <summary>The driving car nearest the player, past <paramref name="after"/> if that is one (so pressing again steps outwards).</summary>
		private int NearestDriving(int after)
		{
			double afterD = -1.0;
			if (after >= 0 && live.Where(after, out double ax, out double az) == TrafficLive.Driving)
			{
				afterD = (ax - live.PlayerX) * (ax - live.PlayerX) + (az - live.PlayerZ) * (az - live.PlayerZ);
			}
			int best = -1;
			double bestD = double.MaxValue;
			for (int c = 0; c < live.Count; c++)
			{
				if (live.Where(c, out double x, out double z) != TrafficLive.Driving || c == after)
				{
					continue;
				}
				double d = (x - live.PlayerX) * (x - live.PlayerX) + (z - live.PlayerZ) * (z - live.PlayerZ);
				if (d > afterD && d < bestD)
				{
					best = c;
					bestD = d;
				}
			}
			return best;
		}

		// ---- the map overlay ----------------------------------------------------------------------------------------------------

		/// <summary>The map overlay's picture of the whole world (one pixel 125 units), made again every half second while it shows.</summary>
		private Texture2D map;
		private Color32[] mapPixels;
		private float nextMap;
		private const int MapSize = 256;

		/// <summary>
		/// With Debug.TrafficMap (and ShowCoords): every car in the world on a map in the top right, as the simulation has it however far off:
		/// moving cars green, cars standing in a queue or held at a junction red, the micro's round the player white, the player a yellow
		/// cross. What the plan calls the world perceived and measured carrying on outside view.
		/// </summary>
		private void OnGUI()
		{
			if (live == null || !Mod.ShowCoords.Value || !Mod.TrafficMap.Value || Event.current.type != EventType.Repaint)
			{
				return;
			}
			if (map == null)
			{
				map = new Texture2D(MapSize, MapSize, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, hideFlags = HideFlags.HideAndDontSave, wrapMode = TextureWrapMode.Clamp };
				mapPixels = new Color32[MapSize * MapSize];
			}
			if (Time.unscaledTime >= nextMap)
			{
				nextMap = Time.unscaledTime + 0.5f;
				PaintMap();
			}
			float side = Mathf.Min(360f, Screen.height * 0.4f);
			GUI.DrawTexture(new Rect(Screen.width - side - 10f, 10f, side, side), map);
		}

		private void PaintMap()
		{
			Color32 back = new Color32(0, 0, 0, 150);
			for (int i = 0; i < mapPixels.Length; i++)
			{
				mapPixels[i] = back;
			}
			double scale = MapSize / (2.0 * WorldGen.Half);
			for (int c = 0; c < live.Count; c++)
			{
				byte k = live.Where(c, out double x, out double z);
				if (k != TrafficLive.Driving)
				{
					continue;
				}
				Meso.Vehicle v = live.M.Cars[c];
				Micro.Agent a = live.Mi.Agents[c];
				bool held = a != null ? a.V < Micro.Standing : v.HeldSince >= 0.0;
				Color32 col = a != null ? new Color32(255, 255, 255, 255) : held ? new Color32(255, 60, 40, 255) : new Color32(60, 230, 90, 255);
				if (a != null && held)
				{
					col = new Color32(255, 160, 150, 255);
				}
				Dot((int)((x + WorldGen.Half) * scale), (int)((z + WorldGen.Half) * scale), col);
			}
			int px = (int)((live.PlayerX + WorldGen.Half) * scale), pz = (int)((live.PlayerZ + WorldGen.Half) * scale);
			Color32 me = new Color32(255, 230, 0, 255);
			for (int d = -3; d <= 3; d++)
			{
				Dot(px + d, pz, me);
				Dot(px, pz + d, me);
			}
			map.SetPixels32(mapPixels);
			map.Apply(false);
		}

		private void Dot(int x, int y, Color32 c)
		{
			if (x >= 0 && y >= 0 && x < MapSize && y < MapSize)
			{
				mapPixels[y * MapSize + x] = c;
			}
		}

		// ---- the pool ---------------------------------------------------------------------------------------------------------

		private View Take(int car)
		{
			Wears(car, out int look, out int paint);
			View v = spare[look].Count > 0 ? spare[look].Pop() : Make(look);
			v.Car = car;
			v.LastX = double.NaN;
			v.LastKind = TrafficLive.None;
			v.Blend = 0f;
			v.PlacedX = double.NaN;
			if (v.Paint != paint)
			{
				v.Paint = paint;
				for (int k = 0; k < v.Look.Paint.Length; k++)
				{
					Material[] shared = v.Look.PaintMaterials[k];
					Material[] mats = new Material[shared.Length];
					for (int m = 0; m < shared.Length; m++)
					{
						mats[m] = Painted(shared[m], paint);
					}
					v.Renderers[v.Look.Paint[k]].sharedMaterials = mats;
				}
			}
			return v;
		}

		private void Give(View v)
		{
			v.Go.SetActive(false);
			if (v.Light != null)
			{
				v.Light.enabled = false;
			}
			v.Car = -1;
			spare[v.LookIndex].Push(v);
		}

		private void Clear()
		{
			foreach (View v in drawn.Values)
			{
				Give(v);
			}
			drawn.Clear();
			chosen.Clear();
			chosenMoving.Clear();
		}

		/// <summary>A new car of look <paramref name="index"/>: the body's copy, its box on the Enemy layer (a kinematic body, so a hit is a wall), its headlight.</summary>
		private View Make(int index)
		{
			Look look = looks[index];
			GameObject go = new GameObject("Traffic car") { layer = layer };
			go.SetActive(false);
			go.transform.SetParent(transform, false);
			GameObject body = Instantiate(look.Template, go.transform, false);
			body.name = "Body";
			body.SetActive(true);
			BoxCollider box = go.AddComponent<BoxCollider>();
			box.center = look.Box.center;
			box.size = look.Box.size;
			Rigidbody rb = go.AddComponent<Rigidbody>();
			rb.isKinematic = true;
			rb.useGravity = false;
			rb.interpolation = RigidbodyInterpolation.None;
			Transform[] all = body.GetComponentsInChildren<Transform>(true);
			Transform[] wheels = new Transform[look.Wheels.Length];
			Quaternion[] rest = new Quaternion[look.Wheels.Length];
			for (int i = 0; i < wheels.Length; i++)
			{
				wheels[i] = all[look.Wheels[i]];
				rest[i] = wheels[i].localRotation;
			}
			Light light = null;
			if (look.HasLight && lightTemplate != null)
			{
				GameObject lamp = new GameObject("Headlight") { layer = go.layer };
				lamp.transform.SetParent(go.transform, false);
				lamp.transform.localPosition = look.LightAt;
				lamp.transform.localRotation = look.LightTurn;
				light = lamp.AddComponent<Light>();
				light.type = lightTemplate.type;
				light.range = lightTemplate.range;
				light.spotAngle = lightTemplate.spotAngle;
				light.innerSpotAngle = lightTemplate.innerSpotAngle;
				light.intensity = lightTemplate.intensity;
				light.color = lightTemplate.color;
				light.shadows = LightShadows.None;
				light.enabled = false;
			}
			return new View
			{
				Go = go,
				Root = go.transform,
				Look = look,
				LookIndex = index,
				Renderers = body.GetComponentsInChildren<MeshRenderer>(true),
				Wheels = wheels,
				WheelRest = rest,
				Light = light,
			};
		}

		private void OnDestroy()
		{
			Status = null;
			Live = null;
			if (map != null)
			{
				Destroy(map);
			}
		}
	}
}
