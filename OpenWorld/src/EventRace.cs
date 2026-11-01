using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace HalfGripOpenWorld
{
	/// <summary>
	/// Solo events at the courses (<see cref="Courses"/>): a glowing box beside each course near its start, with a panel like the
	/// garage's (<see cref="GarageUi"/>) offering the event while the car is in it; stopping in it and letting go of the shifter in neutral
	/// (<see cref="ShifterHand"/>) signs the car on and puts
	/// it on the grid behind the start line, held there until the game's own start lights (moved over the line) count it down, then the
	/// clock runs through the checkpoint gates in order to the finish. The next gate stands across the road, lit, only while an event
	/// runs. The cockpit's board at the top right (<see cref="EventBoard"/>) shows the event once it has begun. A missed gate is called out and the clock runs on, but that run sets no best time. A wreck puts the car
	/// back at the last gate passed (<see cref="Respawn"/>), not the spawn. The best time of each course is kept in the mod's own config
	/// file, never the game's save.
	///
	/// Nothing here needs the Multiplayer mod: in a room an event is the player's own time trial, which the others do not see. Events
	/// that a room runs together are the next step (docs/PLAN.md).
	/// </summary>
	internal sealed class EventRace : MonoBehaviour
	{
		internal static EventRace Instance;

		private enum State
		{
			Idle,
			Countdown,
			Running,
			Finished,
		}

		/// <summary>As the garage's: slower than this counts as stopped.</summary>
		private const float Stopped = 1.2f;

		/// <summary>
		/// The countdown, in seconds from being put on the grid: the red light at <see cref="ReadyAt"/>, the amber one a second later,
		/// both a second after that, green at <see cref="GoAt"/>. The car settles onto the road before the first light.
		/// </summary>
		private const float ReadyAt = 1f;
		private const float GoAt = ReadyAt + 3f;

		/// <summary>On the grid the car is held where it was put; a move further than this from there (the reset after a wreck) gives the event up.</summary>
		private const double OffGrid = 30.0;

		/// <summary>How long the green light stays on after the start.</summary>
		private const float GreenSeconds = 3f;

		/// <summary>The event is given up when the car is further than this from the course for <see cref="OffCourseSeconds"/> (or jumps there).</summary>
		private const double OffCourse = 500.0;
		private const float OffCourseSeconds = 10f;

		/// <summary>A move further than this in one frame, not made by <see cref="Respawn"/>, is a teleport: the event is given up.</summary>
		private const double Jump = 150.0;

		/// <summary>How many gates past the next one a crossing is looked for: crossing one of them misses the gates before it.</summary>
		private const int LookAhead = 3;

		/// <summary>How long the result stays on the screen after the finish.</summary>
		private const float ResultSeconds = 10f;

		/// <summary>Gate posts: this far outside the road's edge, this tall over the road.</summary>
		private const float PostOut = 3f;
		private const float PostHeight = 16f;

		private TrackManager tm;
		private Material boxMaterial;
		private Material gateMaterial;
		private Material finishMaterial;
		private readonly List<GameObject> boxes = new List<GameObject>();
		private GameObject[] gates;

		private State state = State.Idle;
		private Courses.Course course;
		private Courses.Course here;
		private bool armed = true;
		private float countdownAt;
		private int countdownShown;
		private float greenUntil = -1f;
		private RaceStarter lights;
		private float lightsHeight;
		private float startedAt;
		private int next;
		private int missed;
		private int lap;
		private float lapStartedAt;
		private float lastLap;
		private float finishTime;
		private float finishedAt;
		private bool newBest;
		private float previousBest;
		private string flash;
		private float flashAt = -1f;
		private float lastLapShown = -1f;
		private float offSince = -1f;
		private float offCheckAt;
		private bool havePrev;
		private double prevX, prevZ;
		private int placedFrame = -1;
		private int shownNext = -1;
		private Courses.Course shownCourse;
		private Rigidbody held;
		private double heldX, heldZ;

		private static readonly Dictionary<string, ConfigEntry<float>> bests = new Dictionary<string, ConfigEntry<float>>();

		/// <summary>True while an event's clock runs: the reset after a wreck goes to its last gate.</summary>
		internal static bool Running => Instance != null && Instance.state == State.Running;

		internal void Setup(TrackManager manager, Shader lit)
		{
			Instance = this;
			tm = manager;
			try
			{
				boxMaterial = Garage.RingMaterial(lit, new Color(1f, 0.55f, 0.1f), "OpenWorld Event box");
				gateMaterial = Garage.RingMaterial(lit, new Color(1f, 0.55f, 0.1f), "OpenWorld Event gate");
				finishMaterial = Garage.RingMaterial(lit, new Color(0.95f, 0.95f, 0.95f), "OpenWorld Event finish");
				foreach (Courses.Course c in Courses.All)
				{
					boxes.Add(Box(c));
				}
				gates = new[] { GateMarker("Event gate (next)"), GateMarker("Event gate (after)") };
				// The race's start lights: WorldScene hung them over the road at the spawn, as high over it as Tokyo's (the road there is at 0.5).
				lights = FindFirstObjectByType<RaceStarter>();
				lightsHeight = lights != null ? lights.transform.position.y - 0.5f : 0f;
				gameObject.AddComponent<EventBoard>();
				List<string> names = new List<string>();
				foreach (Courses.Course c in Courses.All)
				{
					names.Add(c.Name + " " + (c.Distance / 3300.0).ToString("0.0") + " km, " + c.Gates.Count + " gates" + (c.Laps > 1 ? ", " + c.Laps + " laps" : ""));
				}
				Mod.Log.LogInfo("Events: " + string.Join("; ", names.ToArray()));
			}
			catch (Exception e)
			{
				Mod.Log.LogWarning("The events' start boxes and gates could not be made: " + e);
			}
		}

		// ---- the marks -----------------------------------------------------------------------------------------------

		/// <summary>
		/// The start box: a ring on the road, following its grade, a little over it so it never flickers into it, and a chevron inside
		/// pointing the way the event goes. A child of the world's root, so the floating origin moves it with everything else.
		/// </summary>
		private GameObject Box(Courses.Course c)
		{
			const int segments = 48;
			const float over = 0.2f, width = 0.9f;
			List<Vector3> v = new List<Vector3>();
			List<int> t = new List<int>();
			Vector3 Point(double ax, double az)
			{
				double x = c.BoxX + ax, z = c.BoxZ + az;
				return new Vector3((float)ax, (float)(c.BoxSurface(x, z) - c.BoxY) + over, (float)az);
			}
			void Quad(Vector3 a, Vector3 b, Vector3 p, Vector3 d)
			{
				int k = v.Count;
				v.Add(a);
				v.Add(b);
				v.Add(p);
				v.Add(d);
				t.AddRange(new[] { k, k + 2, k + 1, k + 1, k + 2, k + 3 });
			}
			double outer = c.BoxRadius, inner = c.BoxRadius - width;
			for (int i = 0; i < segments; i++)
			{
				double a0 = 2.0 * Math.PI * i / segments, a1 = 2.0 * Math.PI * (i + 1) / segments;
				Quad(Point(Math.Cos(a0) * outer, Math.Sin(a0) * outer), Point(Math.Cos(a1) * outer, Math.Sin(a1) * outer), Point(Math.Cos(a0) * inner, Math.Sin(a0) * inner), Point(Math.Cos(a1) * inner, Math.Sin(a1) * inner));
			}
			// The chevron: two bars meeting at a point ahead of the middle. Forward is (Dx, Dz), right (Dz, -Dx).
			double fx = c.BoxDx, fz = c.BoxDz, rx = c.BoxDz, rz = -c.BoxDx;
			double tip = inner * 0.55, back = -inner * 0.15, side = inner * 0.5, bar = 0.8;
			foreach (int s in new[] { -1, 1 })
			{
				double bx = fx * back + rx * side * s, bz = fz * back + rz * side * s;
				double px = fx * tip, pz = fz * tip;
				Quad(Point(bx, bz), Point(px, pz), Point(bx - fx * bar, bz - fz * bar), Point(px - fx * bar, pz - fz * bar));
			}
			Mesh mesh = new Mesh { name = "Event box" };
			mesh.SetVertices(v);
			mesh.SetTriangles(t, 0);
			mesh.RecalculateNormals();
			mesh.RecalculateBounds();
			GameObject go = new GameObject("Event start: " + c.Name);
			go.transform.SetParent(transform, false);
			go.transform.position = Origin.Local(c.BoxX, (float)(c.BoxY + Network.Lift), c.BoxZ);
			go.AddComponent<MeshFilter>().sharedMesh = mesh;
			MeshRenderer renderer = go.AddComponent<MeshRenderer>();
			renderer.sharedMaterial = boxMaterial;
			renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
			return go;
		}

		/// <summary>A gate: two posts and a bar across, no collider (a car drives through it), hidden until an event shows it.</summary>
		private GameObject GateMarker(string name)
		{
			GameObject go = new GameObject(name);
			go.transform.SetParent(transform, false);
			for (int i = 0; i < 3; i++)
			{
				GameObject part = GameObject.CreatePrimitive(PrimitiveType.Cube);
				DestroyImmediate(part.GetComponent<Collider>());
				part.name = i == 2 ? "Bar" : "Post";
				part.transform.SetParent(go.transform, false);
				MeshRenderer renderer = part.GetComponent<MeshRenderer>();
				renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
			}
			go.SetActive(false);
			return go;
		}

		/// <summary>Puts a gate marker across the road at gate g: its posts stand from the ground beside the road to over the road.</summary>
		private void ShowGate(GameObject marker, Courses.Gate g, bool finish)
		{
			double half = g.Half - Courses.GateMargin + PostOut;
			double rx = g.Dz, rz = -g.Dx;
			float road = (float)(g.Y + Network.Lift), top = road + PostHeight;
			Quaternion facing = Quaternion.LookRotation(new Vector3((float)g.Dx, 0f, (float)g.Dz), Vector3.up);
			Material material = finish ? finishMaterial : gateMaterial;
			for (int i = 0; i < 2; i++)
			{
				int s = i == 0 ? -1 : 1;
				double px = g.X + rx * half * s, pz = g.Z + rz * half * s;
				float bottom = Mathf.Min((float)WorldGen.Height(px, pz), road) - 1f;
				Transform post = marker.transform.GetChild(i);
				post.SetPositionAndRotation(Origin.Local(px, (bottom + top) * 0.5f, pz), facing);
				post.localScale = new Vector3(1.2f, top - bottom, 1.2f);
				post.GetComponent<MeshRenderer>().sharedMaterial = material;
			}
			Transform bar = marker.transform.GetChild(2);
			bar.SetPositionAndRotation(Origin.Local(g.X, top - 1f, g.Z), facing);
			bar.localScale = new Vector3((float)(half * 2.0) + 1.2f, 2f, 0.6f);
			bar.GetComponent<MeshRenderer>().sharedMaterial = material;
			marker.SetActive(true);
		}

		/// <summary>The next gate and the one after it lit while an event runs (the finish line white); none otherwise.</summary>
		private void ShowGates()
		{
			if (gates == null)
			{
				return;
			}
			bool on = (state == State.Countdown || state == State.Running) && course != null;
			// Placed once each time the next gate changes: the floating origin moves them with the world's root after that.
			Courses.Course showing = on ? course : null;
			if (showing == shownCourse && next == shownNext)
			{
				return;
			}
			shownCourse = showing;
			shownNext = next;
			for (int k = 0; k < gates.Length; k++)
			{
				int n = next + k;
				if (on && n < course.Gates.Count)
				{
					ShowGate(gates[k], course.Gates[n], n == course.Gates.Count - 1);
				}
				else if (gates[k].activeSelf)
				{
					gates[k].SetActive(false);
				}
			}
		}

		// ---- the event -------------------------------------------------------------------------------------------------

		private void Update()
		{
			CarControllerNew car = tm != null ? tm.playerCarController : null;
			CarLink link = tm != null ? tm.playerCarlink : null;
			if (car == null || link == null || GameManager.raceOver)
			{
				return;
			}
			Vector3 p = car.transform.position;
			Origin.World(p, out double x, out double z);
			float speed = car.rb != null ? car.rb.linearVelocity.magnitude : 0f;
			here = InBox(p, x, z);
			try
			{
				switch (state)
				{
					case State.Idle:
					case State.Finished:
						if (state == State.Finished && Time.time - finishedAt > ResultSeconds)
						{
							state = State.Idle;
						}
						Waiting(car, link, speed);
						break;
					case State.Countdown:
						Counting(link, x, z);
						break;
					case State.Running:
						Timing(link, x, z);
						break;
				}
				if (greenUntil >= 0f && Time.time > greenUntil)
				{
					greenUntil = -1f;
					Lights(false, false, false);
				}
			}
			catch (Exception e)
			{
				Mod.Log.LogWarning("Event: " + e);
				Stop(null);
			}
			prevX = x;
			prevZ = z;
			havePrev = true;
			Board();
		}

		/// <summary>The course whose start box the car stands in, at about the box's height (not on a bridge over it).</summary>
		private static Courses.Course InBox(Vector3 p, double x, double z)
		{
			Courses.Course c = Courses.BoxAt(x, z);
			if (c == null || Math.Abs(p.y - (c.BoxSurface(x, z) + Network.Lift)) > 6.0)
			{
				return null;
			}
			return c;
		}

		/// <summary>Stopped in a start box and the shifter let go in neutral: the car is signed on and put on the grid.</summary>
		private void Waiting(CarControllerNew car, CarLink link, float speed)
		{
			bool released = ShifterHand.ReleasedInNeutral(link);
			bool neutral = ShifterHand.Neutral(link);
			bool garage = Garage.Instance != null && Garage.Instance.OpenAt != null;
			bool ready = here != null && speed < Stopped && neutral && !garage && link.hp > 0f;
			if (!ready)
			{
				// Out of the box or moving again: the next stop in it signs on.
				if (here == null || speed > Stopped * 3f)
				{
					armed = true;
				}
				return;
			}
			if (!armed)
			{
				return;
			}
			// Letting go of the shifter in neutral signs on, so a gear change through neutral does not.
			if (released && Streamer.Instance != null)
			{
				course = here;
				armed = false;
				next = 0;
				ToGrid(Streamer.Instance);
				state = State.Countdown;
				countdownAt = Time.time;
				countdownShown = -1;
				LightsToStart();
				Say(Title(course), 1f);
				Mod.Log.LogInfo("Event: signed on at the " + course.Name + ", on the grid");
			}
		}

		/// <summary>Puts the car on the course's grid spot, stopped, facing up the course, and holds it there.</summary>
		private void ToGrid(Streamer streamer)
		{
			streamer.BuildAround(course.GridX, course.GridZ, 1);
			streamer.Place(course.GridX, course.GridZ, Quaternion.LookRotation(new Vector3((float)course.GridDx, 0f, (float)course.GridDz), Vector3.up));
			placedFrame = Time.frameCount;
			havePrev = false;
			Hold(tm.playerCarController, course.GridX, course.GridZ);
		}

		/// <summary>
		/// The countdown on the grid: red, amber, both, then green and the clock starts, with 3, 2, 1, GO called out. The car is held on
		/// its spot until green (<see cref="Hold"/>), so there is no jump start: the gear can be put in and the engine revved, and it
		/// goes on the green.
		/// </summary>
		private void Counting(CarLink link, double x, double z)
		{
			float since = Time.time - countdownAt;
			double dx = x - course.GridX, dz = z - course.GridZ;
			bool moved = placedFrame != Time.frameCount && dx * dx + dz * dz > OffGrid * OffGrid;
			if (moved || link.hp <= 0f)
			{
				Mod.Log.LogInfo("Event: " + (moved ? "moved off the grid" : "wrecked") + " before the start at the " + course.Name);
				Stop("EVENT OVER");
				return;
			}
			int step = since < ReadyAt ? -1 : (int)Math.Floor(since - ReadyAt);
			if (step != countdownShown && step >= 0 && step < 3)
			{
				countdownShown = step;
				Lights(true, step >= 1, false);
				Say((3 - step).ToString("0"), 0.8f);
			}
			if (since >= GoAt)
			{
				Release();
				Lights(false, false, true);
				greenUntil = Time.time + GreenSeconds;
				Say("GO!", 1f);
				state = State.Running;
				startedAt = Time.time;
				lapStartedAt = startedAt;
				lap = 1;
				missed = 0;
				next = 0;
				lastLapShown = -1f;
				offSince = -1f;
				havePrev = false;
			}
		}

		/// <summary>
		/// A call-out in the cockpit's text, at once: the game's queue takes a new line only when it holds one or none and types each
		/// out letter by letter, so a countdown queued behind a longer line was late or lost. Cleared first, it shows on time.
		/// </summary>
		private static void Say(string text, float seconds)
		{
			TextController.ClearAll();
			TextController.EnqueueRandomText(seconds, text);
		}

		/// <summary>
		/// Holds the car on its grid spot, world (<paramref name="x"/>, <paramref name="z"/>), until green (<see cref="FixedUpdate"/>):
		/// the suspension still works (it settles onto the road) and the wheels still turn. The game itself holds its grid by leaving the
		/// car's controller off until the start, but that controller is the suspension too. Not by the rigidbody's constraints either: a
		/// frozen axis zeroes the body's inertia about it, and the controller's tyre forces divide by that inertia, which made the car's
		/// position NaN (and the floating origin with it).
		/// </summary>
		private void Hold(CarControllerNew car, double x, double z)
		{
			held = car != null ? car.rb : null;
			heldX = x;
			heldZ = z;
		}

		/// <summary>Lets the held car go: at green, and whenever the event ends before it.</summary>
		private void Release()
		{
			held = null;
		}

		/// <summary>
		/// Each physics step while held, the car is put back on its spot and loses its speed across the ground and about the vertical,
		/// keeping its height, pitch and roll: whatever the engine pushed it in one step is undone in the next. The spot is kept in world
		/// coordinates and turned into the scene's each step, so a floating origin shift on the grid does not move it.
		/// </summary>
		private void FixedUpdate()
		{
			if (held == null)
			{
				return;
			}
			if (state != State.Countdown)
			{
				Release();
				return;
			}
			Vector3 p = held.position;
			if (float.IsNaN(p.x) || float.IsNaN(p.y) || float.IsNaN(p.z))
			{
				Release();
				return;
			}
			held.position = Origin.Local(heldX, p.y, heldZ);
			Vector3 v = held.linearVelocity, w = held.angularVelocity;
			held.linearVelocity = new Vector3(0f, v.y, 0f);
			held.angularVelocity = new Vector3(w.x, 0f, w.z);
		}

		/// <summary>The game's start lights, moved over the course's start line and turned to face the grid.</summary>
		private void LightsToStart()
		{
			if (lights == null)
			{
				return;
			}
			try
			{
				course.At(course.StartS, out double sx, out double sz, out double sy, out double dx, out double dz);
				Transform t = lights.transform;
				t.position = Origin.Local(sx, (float)(sy + Network.Lift) + lightsHeight, sz);
				// A sprite shows its face against its forward: turn the lights about the vertical so the green one's forward runs
				// up the course, whichever way the scene had them.
				Transform face = lights.go != null ? lights.go.transform : t;
				Vector3 f = face.forward;
				f.y = 0f;
				if (f.sqrMagnitude > 1e-4f)
				{
					t.rotation = Quaternion.FromToRotation(f.normalized, new Vector3((float)dx, 0f, (float)dz)) * t.rotation;
				}
			}
			catch (Exception e)
			{
				Mod.Log.LogWarning("Event: the start lights could not be moved: " + e.Message);
			}
		}

		private void Lights(bool ready, bool set, bool go)
		{
			if (lights == null)
			{
				return;
			}
			if (lights.ready != null)
			{
				lights.ready.enabled = ready;
			}
			if (lights.set != null)
			{
				lights.set.enabled = set;
			}
			if (lights.go != null)
			{
				lights.go.enabled = go;
			}
		}

		/// <summary>The clock running: gates crossed in order, a missed one called out, the event given up off the course.</summary>
		private void Timing(CarLink link, double x, double z)
		{
			float now = Time.time - startedAt;
			if (havePrev && placedFrame != Time.frameCount && placedFrame != Time.frameCount - 1)
			{
				double dx = x - prevX, dz = z - prevZ;
				if (dx * dx + dz * dz > Jump * Jump)
				{
					Stop("EVENT OVER");
					return;
				}
				for (int j = next; j < Math.Min(course.Gates.Count, next + LookAhead + 1); j++)
				{
					if (Courses.Crossed(course.Gates[j], prevX, prevZ, x, z))
					{
						Passed(j, now);
						break;
					}
				}
				if (state != State.Running)
				{
					return;
				}
			}
			// Off the course: checked twice a second (the whole centre line is looked at).
			if (Time.time >= offCheckAt)
			{
				offCheckAt = Time.time + 0.5f;
				if (course.Distance2(x, z) > OffCourse * OffCourse)
				{
					if (offSince < 0f)
					{
						offSince = Time.time;
						Say("BACK TO THE COURSE", 2f);
					}
					else if (Time.time - offSince > OffCourseSeconds)
					{
						Stop("EVENT OVER");
					}
				}
				else
				{
					offSince = -1f;
				}
			}
		}

		private void Passed(int j, float now)
		{
			Courses.Gate g = course.Gates[j];
			if (j > next)
			{
				int n = j - next;
				missed += n;
				Say(n == 1 ? "MISSED A CHECKPOINT" : "MISSED " + n + " CHECKPOINTS", 1.5f);
			}
			next = j + 1;
			if (next >= course.Gates.Count)
			{
				Finish(now);
				return;
			}
			if (g.LapEnd > 0)
			{
				lastLap = Time.time - lapStartedAt;
				lapStartedAt = Time.time;
				lap = g.LapEnd + 1;
				lastLapShown = Time.time;
				Say(lap == course.Laps ? "LAST LAP" : "LAP " + lap, 1f);
			}
			else
			{
				Flash("CP " + next + "  " + Clock(now));
			}
		}

		private void Finish(float now)
		{
			finishTime = now;
			finishedAt = Time.time;
			state = State.Finished;
			ConfigEntry<float> best = Best(course);
			previousBest = best != null ? best.Value : 0f;
			newBest = missed == 0 && best != null && (best.Value <= 0f || now < best.Value);
			if (newBest)
			{
				best.Value = now;
			}
			Say(missed > 0 ? "FINISH (MISSED " + missed + ")" : newBest ? "NEW BEST" : "FINISH", 3f);
			Mod.Log.LogInfo("Event: finished the " + course.Name + " in " + Clock(now) + (missed > 0 ? ", " + missed + " checkpoints missed (no best time)" : newBest ? ", a new best" : ", best " + Clock(previousBest)));
		}

		/// <summary>Gives the event up, with a call-out if <paramref name="text"/> is given.</summary>
		private void Stop(string text)
		{
			if (text != null)
			{
				Say(text, 1.5f);
			}
			if (course != null)
			{
				Mod.Log.LogInfo("Event: given up at the " + course.Name);
			}
			Release();
			Lights(false, false, false);
			state = State.Idle;
			course = null;
			next = 0;
		}

		/// <summary>
		/// The reset after a wreck in an event: the car goes back on the course just past the last gate passed (the grid if none
		/// yet), facing along it; the clock runs on. False when no event is running (the reset goes to the spawn).
		/// </summary>
		internal static bool Respawn(Streamer streamer)
		{
			EventRace e = Instance;
			if (e == null || e.state != State.Running || e.course == null)
			{
				return false;
			}
			Courses.Course c = e.course;
			double x, z, dx, dz;
			if (e.next == 0)
			{
				x = c.GridX;
				z = c.GridZ;
				dx = c.GridDx;
				dz = c.GridDz;
			}
			else
			{
				c.At(c.StartS + c.Gates[e.next - 1].Along + Courses.RespawnPast, out x, out z, out double _, out dx, out dz);
				// On the carriageway the way the course goes (the highway's centre line is its median).
				x += dz * c.Keep;
				z -= dx * c.Keep;
			}
			streamer.BuildAround(x, z, 1);
			streamer.Place(x, z, Quaternion.LookRotation(new Vector3((float)dx, 0f, (float)dz), Vector3.up));
			e.placedFrame = Time.frameCount;
			e.havePrev = false;
			return true;
		}

		// ---- best times ----------------------------------------------------------------------------------------------

		/// <summary>The course's best time, in seconds (0 for none), in the mod's config file under [Best times].</summary>
		private static ConfigEntry<float> Best(Courses.Course c)
		{
			if (bests.TryGetValue(c.Key, out ConfigEntry<float> entry))
			{
				return entry;
			}
			try
			{
				entry = Mod.Ctx.Config.Bind("Best times", c.Key, 0f, "Your best time in seconds at the " + c.Name + " event (" + (c.Distance / 3300.0).ToString("0.0") + " km" + (c.Laps > 1 ? ", " + c.Laps + " laps" : "") + "). 0 for none; set it to 0 to start again.");
			}
			catch (Exception e)
			{
				Mod.Log.LogWarning("Event: the best time could not be read: " + e.Message);
				entry = null;
			}
			bests[c.Key] = entry;
			return entry;
		}

		// ---- on the board --------------------------------------------------------------------------------------------

		private static string Title(Courses.Course c)
		{
			return c.Name.ToUpperInvariant() + (c.Laps > 1 ? "  " + c.Laps + " LAPS" : "");
		}

		/// <summary>A time as m:ss.cc.</summary>
		internal static string Clock(float seconds)
		{
			if (seconds < 0f)
			{
				seconds = 0f;
			}
			int cs = (int)Math.Floor(seconds * 100f);
			return (cs / 6000) + ":" + (cs / 100 % 60).ToString("00") + "." + (cs % 100).ToString("00");
		}

		private void Flash(string text)
		{
			flash = text;
			flashAt = Time.time;
		}

		/// <summary>The best time's colour, as the game's own timing panel shows a best lap.</summary>
		private const string Purple = "<color=#A020F0>";
		private const string Red = "<color=#FF4040>";
		private const string Green = "<color=#00FF00>";

		/// <summary>
		/// What the cockpit's board shows (<see cref="EventBoard"/>): the countdown, the clock with the checkpoints and the best, and the result; nothing otherwise (the board tucks away and shows the
		/// players under the hand). The lap counter beside it shows the lap, or the checkpoint on a course of one lap.
		/// </summary>
		private void Board()
		{
			EventBoard board = EventBoard.Instance;
			if (board == null)
			{
				return;
			}
			string text = null, lapLine = null;
			switch (state)
			{
				case State.Countdown:
				{
					float since = Time.time - countdownAt;
					string count = since < ReadyAt ? "Ready" : Red + (3 - (int)Math.Floor(since - ReadyAt)).ToString("0") + "</color>";
					text = course.Name + "\n" + Info(course) + "\n" + BestLine(course) + "\n" + count;
					lapLine = Laps(course, 0);
					break;
				}
				case State.Running:
				{
					float now = Time.time - startedAt;
					string third = lastLapShown >= 0f && Time.time - lastLapShown < 4f ? "Lap " + Clock(lastLap)
						: flash != null && Time.time - flashAt < 3f ? flash
						: "CP " + next + "/" + course.Gates.Count + (missed > 0 ? " " + Red + "-" + missed + "</color>" : "");
					text = course.Name + "\n" + (Time.time < greenUntil ? Green + Clock(now) + "</color>" : Clock(now)) + "\n" + third + "\n" + BestLine(course);
					lapLine = Laps(course, next);
					break;
				}
				case State.Finished:
				{
					string gap = missed > 0 ? Red + missed + " missed</color>" : newBest ? Green + "NEW BEST</color>" : previousBest > 0f ? Red + "+" + (finishTime - previousBest).ToString("0.00") + "s</color>" : "";
					text = "Finish\n" + Clock(finishTime) + "\n" + gap + "\n" + BestLine(course);
					lapLine = "<size=75%>finished</size>";
					break;
				}
			}
			board.Text = text;
			board.Lap = lapLine;
		}

		/// <summary>
		/// The panel offering the event while the car is in a start box and none is running, drawn by <see cref="GarageUi"/> in the
		/// garage's look: the course, its length, laps, checkpoints and best time, and what to do. Null when there is nothing to offer.
		/// </summary>
		internal string Prompt()
		{
			if (here == null || (state != State.Idle && state != State.Finished))
			{
				return null;
			}
			string what = Info(here) + ", " + here.Gates.Count + " checkpoints     " + BestLine(here);
			string how = !armed ? "Drive out of the box to start it again"
				: "Stop and shift into neutral to start";
			return "<color=#ff8c1a>EVENT</color>  " + here.Name + "\n" + what + "\n" + how;
		}

		/// <summary>The scene position of a course's start box, for the label over it.</summary>
		internal static Vector3 ScenePoint(Courses.Course c)
		{
			return Origin.Local(c.BoxX, (float)(c.BoxY + Network.Lift), c.BoxZ);
		}

		/// <summary>The lap counter's line: the lap on the circuit, the checkpoint on a course of one lap.</summary>
		private string Laps(Courses.Course c, int passed)
		{
			if (c.Laps > 1)
			{
				return "<size=75%>lap</size> " + Math.Min(Math.Max(1, lap), c.Laps) + " / " + c.Laps;
			}
			return "<size=75%>cp</size> " + passed + " / " + c.Gates.Count;
		}

		private static string BestLine(Courses.Course c)
		{
			ConfigEntry<float> best = Best(c);
			return "Best " + (best != null && best.Value > 0f ? Purple + Clock(best.Value) + "</color>" : "-");
		}

		/// <summary>The course's length (in real km) and laps.</summary>
		private static string Info(Courses.Course c)
		{
			return (c.Distance / 3300.0).ToString("0.0") + " km" + (c.Laps > 1 ? " " + c.Laps + " laps" : "");
		}

		private void LateUpdate()
		{
			ShowGates();
		}

		private void OnDestroy()
		{
			Release();
			foreach (Material m in new[] { boxMaterial, gateMaterial, finishMaterial })
			{
				if (m != null)
				{
					Destroy(m);
				}
			}
			foreach (GameObject box in boxes)
			{
				if (box != null)
				{
					Destroy(box.GetComponent<MeshFilter>().sharedMesh);
				}
			}
			if (Instance == this)
			{
				Instance = null;
			}
		}
	}
}
