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
	internal sealed partial class TrafficCars : MonoBehaviour
	{
		/// <summary>One line for the debug overlay (ShowCoords).</summary>
		internal static string Status;

		/// <summary>
		/// Where the traffic's frame time goes (0.32.2: the frame rate fell from about 135 to about 100 on the highway with 100 circling
		/// cars, and the log had no timings). Stopwatch ticks summed over a second for each part, then shown per frame on the overlay and
		/// logged every 10 s, both with ShowCoords only; bodies taken and given back counted over the last minute.
		/// </summary>
		internal static class Perf
		{
			/// <summary>The traffic's whole <c>Update</c>; inside it the choice (every 0.4 s, its bubble included), drawing, the per-frame bubble, contact physics and the movers; apart from it all physics cars' <c>FixedUpdate</c>s, and the bodies taken, placed and given back.</summary>
			internal enum Part { Update, Choose, Draw, Bubble, Contact, Movers, Fixed, Switch, Count }

			private static readonly long[] sum = new long[(int)Part.Count];
			private static readonly float[] ms = new float[(int)Part.Count];
			private static int frames, taken, given, bucket, shownFrames;
			private static float seconds, shownFrame, sinceLog;
			/// <summary>Bodies taken and given back in each of the last 60 seconds.</summary>
			private static readonly int[] takenIn = new int[60], givenIn = new int[60];

			internal static long Now => System.Diagnostics.Stopwatch.GetTimestamp();

			internal static void Add(Part p, long since)
			{
				sum[(int)p] += System.Diagnostics.Stopwatch.GetTimestamp() - since;
			}

			internal static void Taken() => taken++;
			internal static void Given() => given++;

			internal static void Reset()
			{
				Array.Clear(sum, 0, sum.Length);
				Array.Clear(ms, 0, ms.Length);
				Array.Clear(takenIn, 0, takenIn.Length);
				Array.Clear(givenIn, 0, givenIn.Length);
				frames = taken = given = bucket = shownFrames = 0;
				seconds = shownFrame = sinceLog = 0f;
			}

			/// <summary>Once a frame: after a second, each part's average per frame is what the overlay shows.</summary>
			internal static void Frame(float dt)
			{
				frames++;
				seconds += dt;
				if (seconds < 1f)
				{
					return;
				}
				double toMs = 1000.0 / System.Diagnostics.Stopwatch.Frequency / frames;
				for (int i = 0; i < sum.Length; i++)
				{
					ms[i] = (float)(sum[i] * toMs);
					sum[i] = 0;
				}
				shownFrame = seconds * 1000f / frames;
				shownFrames = frames;
				bucket = (bucket + 1) % takenIn.Length;
				takenIn[bucket] = taken;
				givenIn[bucket] = given;
				taken = given = 0;
				sinceLog += seconds;
				frames = 0;
				seconds = 0f;
			}

			private static int Minute(int[] counts)
			{
				int n = 0;
				foreach (int c in counts)
				{
					n += c;
				}
				return n;
			}

			/// <summary>The overlay's line (ms per frame, averaged over the last second).</summary>
			internal static string Text(int moving, int parked, int physicsLive)
			{
				return "frame " + shownFrame.ToString("0.0") + " ms (" + shownFrames + " fps); traffic " + ms[(int)Part.Update].ToString("0.00") + " ms a frame (choose "
					+ ms[(int)Part.Choose].ToString("0.00") + ", draw " + ms[(int)Part.Draw].ToString("0.00") + ", bubble " + ms[(int)Part.Bubble].ToString("0.00") + ", contact "
					+ ms[(int)Part.Contact].ToString("0.00") + ", movers " + ms[(int)Part.Movers].ToString("0.00") + "), physics steps " + ms[(int)Part.Fixed].ToString("0.00")
					+ ", bodies switched " + ms[(int)Part.Switch].ToString("0.00") + "; drawn " + moving + " moving, " + parked + " parked; physics live " + physicsLive
					+ ", bodies taken " + Minute(takenIn) + " and given back " + Minute(givenIn) + " in the last minute";
			}

			/// <summary>True every 10 s (the overlay's line is then logged too).</summary>
			internal static bool LogDue()
			{
				if (sinceLog < 10f)
				{
					return false;
				}
				sinceLog = 0f;
				return true;
			}
		}

		/// <summary>How far moving and parked cars are drawn (units: about 270 m and 140 m), and at most how many.</summary>
		private const float MovingReach = 900f, ParkedReach = 450f;
		private const int MostDrawn = 72;

		/// <summary>Moving cars are chosen as if this much nearer than they are, so a town's parked cars never crowd out the road's.</summary>
		private const float MovingWeight = 0.4f;

		/// <summary>How often the nearest cars are chosen again (seconds).</summary>
		private const float ChooseEvery = 0.4f;

		/// <summary>How many moving cars at most have a real headlight at night (the renderer lights only so many lights an object).</summary>
		private const int MostLit = 6;

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
			/// <summary>The lamps (renderers with a glowing material, not painted): their lit materials and dark copies, put on when parked.</summary>
			public int[] Lamps;
			public Material[][] LampsLit, LampsDark;
			public int[] Wheels;
			/// <summary>Each wheel's axle in its parent's frame.</summary>
			public Vector3[] Axles;
			/// <summary>The body's box in the car's frame (units), the wheels' lowest point and radius.</summary>
			public Bounds Box;
			public float Bottom, WheelRadius, Axle;
			/// <summary>Where the car stands at rest on its springs as the game's suspension has it (<see cref="TrafficRest"/>), shared by every look.</summary>
			public Rest Rest;
			/// <summary>For each of <see cref="Wheels"/>, the game's wheel it copies (its index in the controller's order, front then back).</summary>
			public int[] WheelCtl;
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
			/// <summary>Where it was drawn last frame while driving (world x, z), for turning the wheels by what it moved; NaN: not driving.</summary>
			public double LastX = double.NaN, LastZ;
			public Light Light;
			public int Car = -1;
			public bool Seen;
			public int Paint = -1;
			/// <summary>Parked: the spot it was placed at (its leg), so a parked car is placed once.</summary>
			public int ParkedLeg = int.MinValue;
			public bool Placed;
			/// <summary>Whether its lamps are lit (driving) or dark (parked); -1 not set yet.</summary>
			public int LampsOn = -1;
		}

		private TrackManager tm;
		private System.Threading.Tasks.Task<(TrafficDay day, TrafficParking parking)> building;
		private TrafficDay day;
		private TrafficParking parking;
		/// <summary>Which cars are behind their timetable or gone (stage 3 writes it; empty until then), and the drawn cars it guards.</summary>
		private TrafficLate late;
		private readonly List<TrafficLate.Near> nearCars = new List<TrafficLate.Near>();
		private double playerX, playerZ;

		/// <summary>The race's lateness ledger (null until the day is built): stage 3's physics cars put a car back on its timetable through it.</summary>
		internal TrafficLate Late => late;
		private double startHour;
		private int raceSeed;
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
			raceSeed = seed;
			Perf.Reset();
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
				return (d, d.Parking);
			});
		}

		// ---- the bodies -------------------------------------------------------------------------------------------------------

		/// <summary>
		/// The enemy car at rest on its springs (<see cref="TrafficRest"/>): its root's height over the ground and its pitch, how much a
		/// surface's offset sinks it, and for each wheel (in its controller's order, front then back, as the game's <c>wheels</c>) its spring's
		/// length and where it is drawn in its parent's frame. Worked out from the first physics body woken at the race's start
		/// (<see cref="LearnRestFrom"/>), as the game's own <c>Start</c> leaves it: the prefab can't give it (its rigidbody reports no centre
		/// of mass, and the wheels' places the suspension uses are only taken in <c>Start</c>). Until then not <see cref="Ok"/>: the wheels
		/// are drawn at the springs' full length and a physics car is put down where the drawn one is, as before.
		/// </summary>
		internal sealed class Rest
		{
			public bool Ok;
			public float Height, PitchDeg, Sink;
			public float[] Length;
			public Vector3[] Local;
		}

		/// <summary>The rest every look shares (one car, one suspension).</summary>
		private readonly Rest rest = new Rest();

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
			List<Transform> ctl = ControllerWheels(prefab);
			List<Look> made = new List<Look>();
			for (int i = 0; i < Math.Max(1, models.Length); i++)
			{
				Look look = MakeLook(prefab, skin, models, i, ctl);
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
		private Look MakeLook(GameObject prefab, CarSkinController skin, GameObject[] models, int index, List<Transform> ctl)
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
			// From the car's frame (scaled as the car is) to its parts'.
			Matrix4x4 toCar = Matrix4x4.Scale(root.localScale) * root.worldToLocalMatrix;
			bool any = false;
			Bounds box = default, wheelBox = default;
			bool anyWheel = false;
			float wheelRadius = 0f;
			// The wheels, until the rest is known (LearnRestFrom moves them there), where the game draws them at the springs' full length:
			// widened as it widens them, let down. The prefab has them where a loaded spring would put them, so a parked copy squatted.
			Dictionary<Transform, Vector3> wheelPlace = WheelPlaces(prefab);
			Dictionary<Transform, Transform> wheelOf = new Dictionary<Transform, Transform>();
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
					if (!lowered.Contains(wc))
					{
						lowered.Add(wc);
						wc.localPosition = local;
						wheelOf[wc] = wheel;
					}
					// The same move in the car's frame, for the box and the wheels' bottom.
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
			Transform[] all = template.GetComponentsInChildren<Transform>(true);
			List<int> paintAt = new List<int>(), wheelAt = new List<int>(), wheelCtl = new List<int>();
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
					wheelCtl.Add(wheelOf.TryGetValue(all[k], out Transform of) ? ctl.IndexOf(of) : -1);
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
				Rest = rest,
				WheelCtl = wheelCtl.ToArray(),
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

		/// <summary>The enemy car's wheels in its controller's order (front then back, none missing), as the game's <c>wheels</c> are.</summary>
		private static List<Transform> ControllerWheels(GameObject prefab)
		{
			List<Transform> wheels = new List<Transform>();
			CarControllerNew cc = prefab.GetComponent<CarControllerNew>();
			if (cc == null)
			{
				return wheels;
			}
			foreach (List<Transform> list in new[] { cc.frontWheels, cc.backWheels })
			{
				foreach (Transform w in list ?? new List<Transform>())
				{
					if (w != null)
					{
						wheels.Add(w);
					}
				}
			}
			return wheels;
		}

		/// <summary>
		/// Where each of the enemy car's wheels is drawn on a parked copy (in its parent's frame) until the rest is known: as the game draws
		/// it with the spring at full length, its place widened by 1.1 as <c>InitializeWheels</c> widens it and let down by (length - 0.3).
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
			foreach (Transform w in ControllerWheels(prefab))
			{
				Vector3 place = root.InverseTransformPoint(w.position);
				Vector3 drawn = new Vector3(place.x * 1.1f, place.y - (cc.suspensionRestLength - 0.3f), place.z);
				at[w] = w.parent.InverseTransformPoint(root.TransformPoint(drawn));
			}
			return at;
		}

		/// <summary>
		/// Works out the rest (<see cref="Rest"/>) from body <paramref name="b"/>, woken and started (its controller's <c>Start</c> has run),
		/// from what the game itself uses: the wheels' places as <c>InitializeWheels</c> took them, the rigidbody's mass and centre of mass
		/// (Unity's, from the car's collider), the springs, the wheel radius, the scale and the scene's gravity. Then every drawn car's wheels
		/// (and the copies' templates) are moved to where the game draws them at rest. Once a race.
		/// </summary>
		internal void LearnRestFrom(TrafficBody b)
		{
			if (rest.Ok || b == null || b.Cc == null || b.Rb == null)
			{
				return;
			}
			CarControllerNew cc = b.Cc;
			CarControllerNew.WheelData[] data = cc.wheels;
			List<Transform> wheels = new List<Transform>();
			foreach (List<Transform> list in new[] { cc.frontWheels, cc.backWheels })
			{
				foreach (Transform w in list ?? new List<Transform>())
				{
					if (w != null)
					{
						wheels.Add(w);
					}
				}
			}
			int n = wheels.Count;
			if (data == null || data.Length != n || n == 0)
			{
				Mod.Log.LogWarning("Traffic: the physics car's wheels are not as expected (" + (data == null ? "none" : data.Length.ToString()) + " for " + n + "); parked cars stay at the springs' full length");
				return;
			}
			double[] x = new double[n], y = new double[n], z = new double[n];
			for (int i = 0; i < n; i++)
			{
				if (data[i] == null)
				{
					Mod.Log.LogWarning("Traffic: the physics car's wheels are not set up yet; parked cars stay at the springs' full length");
					return;
				}
				x[i] = data[i].localPosition.x;
				y[i] = data[i].localPosition.y;
				z[i] = data[i].localPosition.z;
			}
			Transform root = b.transform;
			Vector3 com = b.Rb.centerOfMass;
			float scale = root.lossyScale.y;
			float g = -Physics.gravity.y;
			float radius = data[0].radius;
			TrafficRest.Result r = TrafficRest.Solve(x, y, z, scale, com.y, com.z, b.Rb.mass, g, data[0].springStiffness, data[0].restLength, radius);
			string numbers = "gravity " + g.ToString("0.##") + ", mass " + b.Rb.mass.ToString("0") + ", centre of mass " + com.ToString("0.000") + ", scale "
				+ scale.ToString("0.##") + ", springs " + data[0].springStiffness.ToString("0") + " of " + data[0].restLength.ToString("0.00") + ", wheels "
				+ string.Join(" ", System.Linq.Enumerable.Select(data, d => d.localPosition.ToString("0.00")));
			if (!r.Ok)
			{
				Mod.Log.LogWarning("Traffic: the cars' rest on their springs not found (" + numbers + "); parked cars stay at the springs' full length");
				return;
			}
			rest.Length = new float[n];
			rest.Local = new Vector3[n];
			for (int i = 0; i < n; i++)
			{
				rest.Length[i] = (float)r.Length[i];
				// As the game's Update draws it: the place less (length - 0.3), in the root's frame (unscaled), which the wheel's parent shares.
				Vector3 inRoot = new Vector3((float)x[i], (float)(y[i] - (r.Length[i] - 0.3)), (float)z[i]);
				rest.Local[i] = wheels[i].parent.InverseTransformPoint(root.TransformPoint(inRoot));
			}
			rest.Height = (float)r.Height;
			rest.PitchDeg = (float)(r.Pitch * 180.0 / Math.PI);
			rest.Sink = (float)r.Sink;
			rest.Ok = true;
			int moved = ApplyRest();
			Mod.Log.LogInfo("Traffic: the cars at rest on their springs (" + numbers + "): springs " + string.Join(", ", System.Linq.Enumerable.Select(rest.Length, l => l.ToString("0.000")))
				+ ", root " + rest.Height.ToString("0.000") + " over the ground, pitched " + rest.PitchDeg.ToString("0.00") + " deg; " + moved + " drawn cars' wheels moved there");
		}

		/// <summary>Every drawn car's wheels (live and spare, and the looks' templates) put where the game draws them at rest. Returns how many cars.</summary>
		private int ApplyRest()
		{
			int moved = 0;
			foreach (Look look in looks)
			{
				Transform[] all = look.Template.GetComponentsInChildren<Transform>(true);
				for (int i = 0; i < look.Wheels.Length; i++)
				{
					int c = look.WheelCtl[i];
					if (c >= 0 && c < rest.Local.Length)
					{
						all[look.Wheels[i]].localPosition = rest.Local[c];
					}
				}
			}
			foreach (View v in live.Values)
			{
				moved += RestWheels(v) ? 1 : 0;
			}
			foreach (Stack<View> s in spare)
			{
				foreach (View v in s)
				{
					RestWheels(v);
				}
			}
			return moved;
		}

		private bool RestWheels(View v)
		{
			for (int i = 0; i < v.Wheels.Length; i++)
			{
				int c = v.Look.WheelCtl[i];
				if (c >= 0 && c < rest.Local.Length)
				{
					v.Wheels[i].localPosition = rest.Local[c];
				}
			}
			// Stood again at the rest height where it is next drawn.
			v.Placed = false;
			v.ParkedLeg = int.MinValue;
			return true;
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
				late = new TrafficLate(day);
				MakeRoom(raceSeed);
				building = null;
				int legs = 0;
				foreach (TrafficDay.Leg leg in day.Legs)
				{
					legs += leg.Start >= 0.0 ? 1 : 0;
				}
				Mod.Log.LogInfo("Traffic: " + day.Cars.Count + " cars, " + legs + " trips on a " + (day.Weekend ? "weekend day" : "weekday") + " from the race's seed, built in " + day.BuildMs.ToString("0") + " ms (the map "
					+ Traffic.BuildMs.ToString("0") + " ms: " + Traffic.Report + "); parking: at most " + parking.Busiest + " cars at one place, " + parking.Overflow + " stays at the kerb past a place's spaces; up to " + MostPhysics + " physics cars within " + (PhysicsReach / 3.3f).ToString("0") + " m");
			}
			if (!Mod.ShowTraffic.Value)
			{
				Clear();
				Status = null;
				return;
			}
			Perf.Frame(Time.unscaledDeltaTime);
			long started = Perf.Now;
			try
			{
				Pump();
				Step();
			}
			catch (Exception e)
			{
				Mod.Log.LogWarning("The traffic stopped: " + e);
				Clear();
				failed = true;
			}
			Perf.Add(Perf.Part.Update, started);
			FramePerf.Add(FramePerf.Part.Traffic, started);
		}

		/// <summary>The day's clock now: the room's (the host's race clock; alone, this machine's), from the hour the race started.</summary>
		private double SimNow()
		{
			double local = LocalSim();
			return room != null ? room.ClockOf(local) : local;
		}

		/// <summary>This machine's own day clock: its race's, from the hour it started, moved on with the sky's debug skip.</summary>
		private double LocalSim()
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
			WarmPool();
			Origin.World(car.transform.position, out double cx, out double cz);
			double mx = cx - chosenX, mz = cz - chosenZ;
			if (Time.unscaledTime >= nextChoose || mx * mx + mz * mz > 150.0 * 150.0)
			{
				nextChoose = Time.unscaledTime + ChooseEvery;
				chosenX = cx;
				chosenZ = cz;
				long chose = Perf.Now;
				Choose(w, cx, cz);
				Perf.Add(Perf.Part.Choose, chose);
			}
			else
			{
				// A physics car into a tunnel goes back the frame it is in the dark (it is through it in a fraction of a choice's interval).
				long bubbled = Perf.Now;
				Bubble(w, cx, cz, true);
				Perf.Add(Perf.Part.Bubble, bubbled);
			}
			foreach (View v in live.Values)
			{
				v.Seen = false;
			}
			drawnMoving = drawnParked = lit = 0;
			long drew = Perf.Now;
			float dt = Time.deltaTime;
			bool lights = Sky.LightsOn;
			// The physics cars are the nearest: theirs are the first headlights.
			PhysicsLights(lights);
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
			Perf.Add(Perf.Part.Draw, drew);
			// Contact physics: a drawn car the player is about to touch becomes a loose car.
			long touched = Perf.Now;
			Contact(w);
			Perf.Add(Perf.Part.Contact, touched);
			long moved = Perf.Now;
			BuildMovers();
			Perf.Add(Perf.Part.Movers, moved);
			if (Mod.ShowCoords.Value)
			{
				double hour = 3.0 + w / 100.0;
				hour -= Math.Floor(hour / 24.0) * 24.0;
				Status = "traffic: " + drawnMoving + " moving and " + drawnParked + " parked drawn, " + lit + " lit, chosen in " + chooseMs.ToString("0.0") + " ms (" + (day.Weekend ? "a weekend" : "a weekday") + ", "
					+ ((int)hour).ToString("00") + ":" + ((int)((hour - Math.Floor(hour)) * 60.0)).ToString("00") + ")"
					+ (late.MadeLate + late.Retired > 0 ? "; late " + late.Count + " (made " + late.MadeLate + ", caught up " + late.CaughtUp + ", hidden " + late.HiddenCount + ", retired " + late.Retired + ")" : "")
					+ PhysicsStatus()
					+ "\n" + Perf.Text(drawnMoving, drawnParked, physical.Count);
				if (Perf.LogDue())
				{
					Mod.Log.LogInfo("Traffic timings: " + Perf.Text(drawnMoving, drawnParked, physical.Count));
				}
			}
		}

		/// <summary>The cars to draw: every car's place now, the nearest within reach (moving ones weighted nearer), at most <see cref="MostDrawn"/>.</summary>
		private void Choose(double w, double cx, double cz)
		{
			System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
			// Physics cars out of reach (or at their trip's end) go back on their timetables first, so they are chosen as drawn cars now.
			Bubble(w, cx, cz);
			LearnRest();
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
				if (IsPhysical(c))
				{
					continue;
				}
				TrafficDay.Place at = late.At(c, w, out bool hidden);
				if (hidden)
				{
					continue;
				}
				if (at.State == TrafficDay.State.Driving && AnyBodies && HoldBackFromPhysics(c, at, w))
				{
					// It stays in its space while a physics car stands where it would pull out.
					at = late.At(c, w, out hidden);
					if (hidden)
					{
						continue;
					}
				}
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
					if (moving && AnyBodies)
					{
						// The day doesn't know a physics car is held up: a drawn car where one is is hidden until out of sight.
						parking.Drawn(c, at, out double px, out double pz, out double _, out double _);
						if (HideUnderPhysics(c, px, pz, at.Hx, at.Hz, Math.Min(40.0, Math.Max(0.0, at.Speed) * 1.5), w))
						{
							continue;
						}
					}
					near.Add(((float)Math.Sqrt(d2) * (moving ? MovingWeight : 1f), c, moving));
					if (late.Count > 0)
					{
						// Guarded where it is drawn (pulling out of a lot's bay is off its lane).
						double gx = x, gz = z;
						if (moving)
						{
							parking.Drawn(c, at, out gx, out gz, out double _, out double _);
						}
						nearCars.Add(new TrafficLate.Near { Car = c, X = gx, Z = gz, Hx = at.Hx, Hz = at.Hz, Moving = moving, Distance = Math.Sqrt(d2) });
					}
				}
			}
			if (late.Count > 0)
			{
				// A late car would be drawn overlapping another: the farther of the two is hidden until it catches up.
				if (late.Guard(nearCars, w) > 0)
				{
					near.RemoveAll(e => late.TryGet(e.car, out TrafficLate.Entry le) && le.Hidden);
				}
				nearCars.Clear();
				// Late and hidden cars that can't be seen, where they are drawn or where the day has them, catch up in the background (in a
				// room late cars only on the host, out of every player's sight, and sent to everybody; hidden ones here).
				playerX = cx;
				playerZ = cz;
				SendCaughtUp(late.Settle(w, Seen, room.IsHost), w);
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
			// The nearest driving cars within reach become physics cars.
			MakePhysical(w, cx, cz);
			chooseMs = (float)watch.Elapsed.TotalMilliseconds;
		}

		/// <summary>
		/// Whether car <paramref name="c"/> at <paramref name="at"/> could be seen: within the drawn reach of the player, on a loaded tile; on
		/// a room's host, also within it of any other player (whose tiles it doesn't know).
		/// </summary>
		private bool Seen(int c, TrafficDay.Place at)
		{
			double x = at.X, z = at.Z;
			float reach = MovingReach;
			if (at.State == TrafficDay.State.Parked)
			{
				TrafficParking.Spot spot = parking.Parked(c, at.Leg);
				if (spot.Away)
				{
					return false;
				}
				x = spot.X;
				z = spot.Z;
				reach = ParkedReach;
			}
			if (OthersNear(x, z, reach))
			{
				return true;
			}
			double dx = x - playerX, dz = z - playerZ;
			if (dx * dx + dz * dz > (double)reach * reach)
			{
				return false;
			}
			return Streamer.Instance != null && Streamer.Instance.IsLoadedAt(x, z);
		}

		/// <summary>Puts car <paramref name="c"/> where its day says now; false if it is not to be seen (away, in a tunnel).</summary>
		private bool Draw(View v, int c, double w, float dt)
		{
			TrafficDay.Place at = late.At(c, w, out bool hidden);
			if (hidden)
			{
				return false;
			}
			if (at.State == TrafficDay.State.Driving && AnyBodies && HoldBackFromPhysics(c, at, w))
			{
				// Checked every frame as well as at the choice: a car sets off between choices.
				at = late.At(c, w, out hidden);
				if (hidden)
				{
					return false;
				}
			}
			if (at.State == TrafficDay.State.Driving)
			{
				if (at.InTunnel)
				{
					return false;
				}
				// On its path, or pulling out of its space at the start of a trip and into one at the end (TrafficParking.Drawn).
				parking.Drawn(c, at, out double x, out double z, out double hx, out double hz);
				double roadY = at.Y + Network.Lift;
				v.ParkedLeg = int.MinValue;
				Lamps(v, true);
				if (!Pose(v, x, z, roadY, hx, hz))
				{
					return false;
				}
				// The wheels turn with the distance it is drawn moving (pulling in or out, the day holds it still on its lane), backwards
				// when it moves backwards; a jump (a body of the pool given another car) turns nothing.
				double mx = double.IsNaN(v.LastX) ? 0.0 : x - v.LastX, mz = double.IsNaN(v.LastX) ? 0.0 : z - v.LastZ;
				double moved = Math.Sqrt(mx * mx + mz * mz);
				if (moved > 5.0 + 80.0 * dt)
				{
					moved = 0.0;
				}
				v.LastX = x;
				v.LastZ = z;
				float roll = (float)(mx * hx + mz * hz >= 0.0 ? moved : -moved);
				v.Spin = Mathf.Repeat(v.Spin + roll / v.Look.WheelRadius * Mathf.Rad2Deg, 360f);
				Spin(v);
				DrawnMover(c, at, x, z, hx, hz);
				drawnMoving++;
				return true;
			}
			if (at.State != TrafficDay.State.Parked)
			{
				return false;
			}
			// Parked: lamps off.
			v.LastX = double.NaN;
			Lamps(v, false);
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
			bool hf = Ground(mid + fwd * axle, top, out float yf, out float of);
			bool hb = Ground(mid - fwd * axle, top, out float yb, out float ob);
			if (!hf && !hb)
			{
				return false;
			}
			if (!hf)
			{
				yf = yb;
				of = ob;
			}
			if (!hb)
			{
				yb = yf;
				ob = of;
			}
			Rest rest = v.Look.Rest;
			Vector3 at;
			Quaternion turn;
			if (rest != null && rest.Ok)
			{
				// Standing as the physics car will: the game's springs read a surface's offset (the ground's, the verges' and tracks' -0.1) as
				// that much more length, so the car sits lower there; at its rest height over the ground and pitched as at rest.
				yf += of * rest.Sink;
				yb += ob * rest.Sink;
				float slope = Mathf.Atan2(yf - yb, axle * 2f) * Mathf.Rad2Deg;
				Quaternion ground = Quaternion.LookRotation(fwd, Vector3.up) * Quaternion.Euler(-slope, 0f, 0f);
				turn = ground * Quaternion.Euler(-rest.PitchDeg, 0f, 0f);
				at = new Vector3(mid.x, (yf + yb) * 0.5f, mid.z) + ground * new Vector3(0f, rest.Height, 0f);
			}
			else
			{
				float pitch = Mathf.Atan2(yf - yb, axle * 2f) * Mathf.Rad2Deg;
				turn = Quaternion.LookRotation(fwd, Vector3.up) * Quaternion.Euler(-pitch, 0f, 0f);
				// The wheels' bottom on the ground under the middle.
				at = new Vector3(mid.x, (yf + yb) * 0.5f, mid.z) - turn * new Vector3(0f, v.Look.Bottom, 0f);
			}
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
			return Ground(p, top, out y, out _);
		}

		/// <summary>As <see cref="Ground(Vector3, float, out float)"/>, with the surface's suspension offset there (its <see cref="HandlingModifier"/>'s, as the game's springs read it; 0 with none).</summary>
		private bool Ground(Vector3 p, float top, out float y, out float offset)
		{
			offset = 0f;
			if (Physics.Raycast(new Vector3(p.x, top, p.z), Vector3.down, out RaycastHit hit, 24f, groundMask, QueryTriggerInteraction.Ignore))
			{
				y = hit.point.y;
				if (hit.collider != null && hit.collider.TryGetComponent(out HandlingModifier handling))
				{
					offset = handling.Resolve(hit.point).suspensionOffset;
				}
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
			v.LastX = double.NaN;
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
			ClearPhysics();
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
			// What came for this race and was not read is of no use to the next.
			TrafficRoom.Take();
		}
	}
}
