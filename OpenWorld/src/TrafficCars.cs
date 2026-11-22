using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace HalfGripOpenWorld
{
	/// <summary>
	/// The traffic in the game (stage 2 of the plan): every car of the race's day of traffic (<see cref="TrafficDay"/>) that is near the
	/// player drawn where its timetable puts it, driving, pulling out of and into its space, or parked (<see cref="TrafficParking"/>).
	/// The day is built on a worker from the race's seed at the start, so every machine in a room has the same cars in the same places
	/// with no messages. The cars are the game's own enemy car's bodies (its models, painted from a palette of everyday colours, the same
	/// colour for the same car every race), copied as meshes only: no physics, no AI, no guns. Each has a box on the game's Enemy layer
	/// moved with it, so hitting one is hitting a wall (the respawn already keeps clear of that layer). At night the nearest few
	/// moving cars light the road with the enemy car's own headlight.
	///
	/// Drawn from a pool, the nearest cars only (moving ones count as nearer). World positions are kept as doubles and turned into the
	/// scene's on every frame, so an origin shift needs nothing here.
	/// </summary>
	internal sealed class TrafficCars : MonoBehaviour
	{
		/// <summary>One line for the debug overlay (ShowCoords).</summary>
		internal static string Status;

		/// <summary>How far moving and parked cars are drawn (units: about 270 m and 140 m), and at most how many.</summary>
		private const float MovingReach = 900f, ParkedReach = 450f;
		private const int MostDrawn = 72;

		/// <summary>Moving cars are chosen as if this much nearer than they are, so a town's parked cars never crowd out the road's.</summary>
		private const float MovingWeight = 0.4f;

		/// <summary>How often the nearest cars are chosen again (seconds).</summary>
		private const float ChooseEvery = 0.4f;

		/// <summary>How many moving cars at most have a real headlight at night (the renderer lights only so many lights an object).</summary>
		private const int MostLit = 6;

		/// <summary>
		/// Over the first and last stretch of a trip the car goes between its space and its lane (pulling out and in): as long as the
		/// space is far from where the trip starts or ends (a kerb space down the street), at least <see cref="PullLeast"/> and at most
		/// <see cref="PullMost"/> units.
		/// </summary>
		private const double PullLeast = 30.0, PullMost = 150.0;

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
			/// <summary>Its renderers that take the paint (indices in the template's renderers), and its wheels (indices in its transforms).</summary>
			public int[] Paint;
			/// <summary>The painted renderers' own materials, which each paint's copies are made from.</summary>
			public Material[][] PaintMaterials;
			public int[] Wheels;
			/// <summary>Each wheel's axle in its parent's frame.</summary>
			public Vector3[] Axles;
			/// <summary>The body's box in the car's frame (units), the wheels' lowest point and radius.</summary>
			public Bounds Box;
			public float Bottom, WheelRadius, Axle;
			/// <summary>The enemy car's headlight, in the car's frame (none: no light).</summary>
			public bool HasLight;
			public Vector3 LightAt;
			public Quaternion LightTurn;
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
			/// <summary>Parked: the spot it was placed at (its leg), so a parked car is placed once.</summary>
			public int ParkedLeg = int.MinValue;
			public bool Placed;
		}

		private TrackManager tm;
		private System.Threading.Tasks.Task<(TrafficDay day, TrafficParking parking)> building;
		private TrafficDay day;
		private TrafficParking parking;
		private double startHour;
		private bool failed;
		private Look[] looks;
		private Light lightTemplate;
		private int layer, groundMask;
		private readonly Dictionary<(Material, int), Material> painted = new Dictionary<(Material, int), Material>();
		private readonly Dictionary<int, View> live = new Dictionary<int, View>();
		private readonly List<Stack<View>> spare = new List<Stack<View>>();
		private readonly List<(float d, int car, bool moving)> near = new List<(float, int, bool)>();
		private readonly List<int> chosen = new List<int>();
		private readonly HashSet<int> chosenMoving = new HashSet<int>();
		private readonly List<int> gone = new List<int>();
		private float nextChoose;
		private double chosenX, chosenZ;
		private int drawnMoving, drawnParked, lit;
		private float chooseMs;

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
			int seed = Weather.Seed;
			startHour = Sky.StartHourOf(seed);
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
			building = System.Threading.Tasks.Task.Run(() =>
			{
				TrafficDay d = new TrafficDay(seed);
				return (d, new TrafficParking(d));
			});
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
		/// root, laid out as in the car with the car's own scale: everything a model shows when the game puts it on, nothing that is
		/// switched off (the guns, the nitro flames) and no effects, sounds or scripts.
		/// </summary>
		private Look MakeLook(GameObject prefab, CarSkinController skin, GameObject[] models, int index)
		{
			Transform root = prefab.transform;
			GameObject template = new GameObject("Traffic car " + index) { layer = layer };
			template.SetActive(false);
			template.transform.SetParent(transform, false);
			template.transform.localScale = root.localScale;
			Dictionary<Transform, Transform> copies = new Dictionary<Transform, Transform> { [root] = template.transform };
			HashSet<MeshRenderer> paint = new HashSet<MeshRenderer>();
			HashSet<Transform> wheels = new HashSet<Transform>();
			// From the car's frame (scaled as the car is) to its parts'.
			Matrix4x4 toCar = Matrix4x4.Scale(root.localScale) * root.worldToLocalMatrix;
			bool any = false;
			Bounds box = default, wheelBox = default;
			bool anyWheel = false;
			float wheelRadius = 0f;
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
				Wheels = wheelAt.ToArray(),
				Axles = axles.ToArray(),
				Box = box,
				Bottom = anyWheel ? wheelBox.min.y : box.min.y,
				WheelRadius = Mathf.Max(0.5f, wheelRadius),
				Axle = anyWheel ? Mathf.Max(2f, wheelBox.extents.z * 0.85f) : Mathf.Max(2f, box.extents.z * 0.6f),
			};
			if (lightTemplate != null)
			{
				Matrix4x4 m = toCar * lightTemplate.transform.localToWorldMatrix;
				look.HasLight = true;
				look.LightAt = m.MultiplyPoint3x4(Vector3.zero);
				look.LightTurn = Quaternion.Inverse(root.rotation) * lightTemplate.transform.rotation;
			}
			Mod.Info("Traffic: look " + index + ": " + renderers.Length + " meshes, " + look.Paint.Length + " painted, " + look.Wheels.Length + " wheels, box " + box.size.ToString("0.0") + ", wheels' bottom " + look.Bottom.ToString("0.00") + ", radius " + look.WheelRadius.ToString("0.00"));
			return look;
		}

		/// <summary>
		/// Whether part <paramref name="t"/> shows on model <paramref name="index"/>: every object from it up to the car switched on, the
		/// models counted as the skin switches them (only the one worn), nothing under the guns or the effects. The wheel it belongs to,
		/// if it is under one.
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
			if (day == null)
			{
				if (building == null || !building.IsCompleted)
				{
					return;
				}
				if (building.IsFaulted || building.Result.day == null)
				{
					Mod.Log.LogWarning("The traffic's day could not be made (no traffic this race): " + building.Exception);
					failed = true;
					return;
				}
				day = building.Result.day;
				parking = building.Result.parking;
				building = null;
				int legs = 0;
				foreach (TrafficDay.Leg leg in day.Legs)
				{
					legs += leg.Start >= 0.0 ? 1 : 0;
				}
				Mod.Log.LogInfo("Traffic: " + day.Cars.Count + " cars, " + legs + " trips on a " + (day.Weekend ? "weekend day" : "weekday") + " from the race's seed, built in " + day.BuildMs.ToString("0") + " ms (the map "
					+ Traffic.BuildMs.ToString("0") + " ms: " + Traffic.Report + "); parking: at most " + parking.Busiest + " cars at one place, " + parking.Overflow + " stays at the kerb past a place's spaces");
			}
			if (!Mod.ShowTraffic.Value)
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
				Mod.Log.LogWarning("The traffic stopped: " + e);
				Clear();
				failed = true;
			}
		}

		/// <summary>The day's clock now: the race's, from the hour it started, moved on with the sky's debug skip.</summary>
		private double SimNow()
		{
			return TrafficDay.SimTime(startHour, tm.TimeSinceRaceStart + Sky.Skipped);
		}

		private void Step()
		{
			CarControllerNew car = tm.playerCarController;
			if (car == null)
			{
				return;
			}
			double w = SimNow();
			Origin.World(car.transform.position, out double cx, out double cz);
			double mx = cx - chosenX, mz = cz - chosenZ;
			if (Time.unscaledTime >= nextChoose || mx * mx + mz * mz > 150.0 * 150.0)
			{
				nextChoose = Time.unscaledTime + ChooseEvery;
				chosenX = cx;
				chosenZ = cz;
				Choose(w, cx, cz);
			}
			foreach (View v in live.Values)
			{
				v.Seen = false;
			}
			drawnMoving = drawnParked = lit = 0;
			float dt = Time.deltaTime;
			bool lights = Sky.LightsOn;
			foreach (int c in chosen)
			{
				if (!live.TryGetValue(c, out View v))
				{
					v = Take(c);
					live[c] = v;
				}
				v.Seen = true;
				if (!Draw(v, c, w, dt))
				{
					v.Go.SetActive(false);
					continue;
				}
				// The nearest moving cars' headlights (the chosen list is nearest first).
				bool on = lights && v.Light != null && chosenMoving.Contains(c) && lit < MostLit && v.Go.activeSelf;
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
			foreach (KeyValuePair<int, View> kv in live)
			{
				if (!kv.Value.Seen)
				{
					gone.Add(kv.Key);
				}
			}
			foreach (int c in gone)
			{
				Give(live[c]);
				live.Remove(c);
			}
			if (Mod.ShowCoords.Value)
			{
				double hour = 3.0 + w / 100.0;
				hour -= Math.Floor(hour / 24.0) * 24.0;
				Status = "traffic: " + drawnMoving + " moving and " + drawnParked + " parked drawn, " + lit + " lit, chosen in " + chooseMs.ToString("0.0") + " ms (" + (day.Weekend ? "a weekend" : "a weekday") + ", "
					+ ((int)hour).ToString("00") + ":" + ((int)((hour - Math.Floor(hour)) * 60.0)).ToString("00") + ")";
			}
		}

		/// <summary>The cars to draw: every car's place now, the nearest within reach (moving ones weighted nearer), at most <see cref="MostDrawn"/>.</summary>
		private void Choose(double w, double cx, double cz)
		{
			System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
			near.Clear();
			int n = day.Cars.Count;
			for (int c = 0; c < n; c++)
			{
				TrafficDay.Car car = day.Cars[c];
				// A car that stays at home all day (most of them) is skipped when its home is far off, without working out its day.
				double hx = car.Home.X - cx, hz = car.Home.Z - cz;
				if (car.Legs.Count == 0 && hx * hx + hz * hz > (ParkedReach + 60.0) * (ParkedReach + 60.0))
				{
					continue;
				}
				TrafficDay.Place at = day.At(c, w);
				double x, z;
				float reach;
				bool moving = at.State == TrafficDay.State.Driving;
				if (moving)
				{
					if (at.InTunnel)
					{
						continue;
					}
					x = at.X;
					z = at.Z;
					reach = MovingReach;
				}
				else if (at.State == TrafficDay.State.Parked)
				{
					TrafficParking.Spot spot = parking.Parked(c, at.Leg);
					if (spot.Away)
					{
						continue;
					}
					x = spot.X;
					z = spot.Z;
					reach = ParkedReach;
				}
				else
				{
					continue;
				}
				double dx = x - cx, dz = z - cz;
				double d2 = dx * dx + dz * dz;
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
			chooseMs = (float)watch.Elapsed.TotalMilliseconds;
		}

		/// <summary>Puts car <paramref name="c"/> where its day says now; false if it is not to be seen (away, in a tunnel).</summary>
		private bool Draw(View v, int c, double w, float dt)
		{
			TrafficDay.Place at = day.At(c, w);
			if (at.State == TrafficDay.State.Driving)
			{
				if (at.InTunnel)
				{
					return false;
				}
				double x = at.X, z = at.Z, hx = at.Hx, hz = at.Hz;
				double roadY = at.Y + Network.Lift;
				TrafficDay.Leg leg = day.Legs[at.Leg];
				// Pulling out of its space at the start of a trip, and into one at the end: between the space and the lane.
				TrafficParking.Spot spot = default;
				double k = 1.0;
				if (at.P < PullMost && !leg.Appears)
				{
					spot = parking.Before(at.Leg);
					k = at.P / PullOver(leg, 0.0, spot);
				}
				if ((k >= 1.0 || spot.Away) && leg.Length - at.P < PullMost && !leg.Vanishes)
				{
					spot = parking.Parked(c, at.Leg);
					k = (leg.Length - at.P) / PullOver(leg, leg.Length, spot);
				}
				if (k < 1.0 && !spot.Away)
				{
					k = k * k * (3.0 - 2.0 * k);
					x = spot.X + (x - spot.X) * k;
					z = spot.Z + (z - spot.Z) * k;
					float a0 = Mathf.Atan2((float)spot.Hx, (float)spot.Hz) * Mathf.Rad2Deg, a1 = Mathf.Atan2((float)hx, (float)hz) * Mathf.Rad2Deg;
					float a = Mathf.LerpAngle(a0, a1, (float)k) * Mathf.Deg2Rad;
					hx = Mathf.Sin(a);
					hz = Mathf.Cos(a);
				}
				v.ParkedLeg = int.MinValue;
				if (!Pose(v, x, z, roadY, hx, hz))
				{
					return false;
				}
				// The wheels turn with the distance driven.
				v.Spin = Mathf.Repeat(v.Spin + (float)at.Speed * dt / v.Look.WheelRadius * Mathf.Rad2Deg, 360f);
				Spin(v);
				drawnMoving++;
				return true;
			}
			if (at.State != TrafficDay.State.Parked)
			{
				return false;
			}
			if (v.ParkedLeg != at.Leg || !v.Placed)
			{
				TrafficParking.Spot spot = parking.Parked(c, at.Leg);
				if (spot.Away)
				{
					return false;
				}
				// Placed once: it hangs from the world's root, which an origin shift moves, so it stays where it is in the world.
				v.ParkedLeg = at.Leg;
				v.Placed = Pose(v, spot.X, spot.Z, WorldGen.Height(spot.X, spot.Z), spot.Hx, spot.Hz);
				if (!v.Placed)
				{
					return false;
				}
			}
			if (!v.Go.activeSelf)
			{
				v.Go.SetActive(true);
			}
			drawnParked++;
			return true;
		}

		/// <summary>How long the pull between a space and the trip's end at distance <paramref name="p"/> along it is: as far as they are apart, within limits.</summary>
		private static double PullOver(TrafficDay.Leg leg, double p, TrafficParking.Spot spot)
		{
			if (spot.Away)
			{
				return PullLeast;
			}
			TrafficDay.Place end = default;
			TrafficDay.OnPath(leg, p, ref end);
			double dx = spot.X - end.X, dz = spot.Z - end.Z;
			return Math.Max(PullLeast, Math.Min(PullMost, Math.Sqrt(dx * dx + dz * dz)));
		}

		/// <summary>
		/// Puts the car's middle at world (x, z) facing (hx, hz), standing on what is under its front and back wheels (looked for down from
		/// a little over <paramref name="refY"/>, the road's line or the ground), pitched to them; false if nothing is under it yet.
		/// </summary>
		private bool Pose(View v, double x, double z, double refY, double hx, double hz)
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

		private void Spin(View v)
		{
			for (int i = 0; i < v.Wheels.Length; i++)
			{
				v.Wheels[i].localRotation = Quaternion.AngleAxis(v.Spin, v.Look.Axles[i]) * v.WheelRest[i];
			}
		}

		// ---- the pool ---------------------------------------------------------------------------------------------------------

		private View Take(int car)
		{
			Wears(car, out int look, out int paint);
			View v = spare[look].Count > 0 ? spare[look].Pop() : Make(look);
			v.Car = car;
			v.Placed = false;
			v.ParkedLeg = int.MinValue;
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
			foreach (View v in live.Values)
			{
				Give(v);
			}
			live.Clear();
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
			// In the same order as the template's (the copy has its hierarchy), each starting with its root.
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
		}
	}
}
