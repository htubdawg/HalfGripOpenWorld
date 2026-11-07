using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace HalfGripOpenWorld
{
	/// <summary>
	/// The events at the courses (<see cref="Courses"/>), raced in lobbies (<see cref="EventRoom"/>): a glowing box beside each course
	/// near its start, "EVENT" over it. Stopping in it and letting go of the shifter in neutral (<see cref="ShifterHand"/>) opens the
	/// course's panel (<see cref="GarageUi"/>, in the garage's look, the cursor freed): its lobbies to join, and Create with the lobby's
	/// Guns and Melee rules. Joining moves the car just clear of the box to one of the course's waiting spots, so the box is free for the
	/// next player. While in a lobby a strip under the cockpit's board shows it, with Start (the Host's, while the course is free) and
	/// Leave, pressed with the hand (pointed at and grabbed, as the cockpit's own controls are), so it never takes over from driving.
	///
	/// On the Start every entrant's car is put on its own grid slot (dealt in join order) and held there, and the game's start lights
	/// (moved over the line) count down to the start time the arbiter dealt on the race clock, which is the same moment on every machine.
	/// Then each entrant's clock runs from their own green through the checkpoint gates in order to the finish; each gate passed goes to
	/// the arbiter, which sends the standings back, so the board shows the place among the entrants. The next two gates stand across
	/// the road, lit. A missed gate is called out and the clock runs on (that run sets no best time). A wreck puts the car back at the
	/// last gate passed (<see cref="Respawn"/>). The race ends when everybody has finished or is out, 45 s after the first finisher, or at
	/// the course's time limit; its results stand on the board, then the lobby waits again. A clean run keeps the course's best time, in
	/// the mod's own config file, never the game's save.
	///
	/// Outside a room (solo, or no Multiplayer mod) the same lobbies run on this machine alone: a lobby of one is a solo run, and the
	/// panel's Start creates it and starts at once.
	/// </summary>
	internal sealed class EventRace : MonoBehaviour
	{
		internal static EventRace Instance;

		private enum State
		{
			/// <summary>In no lobby.</summary>
			Idle,
			/// <summary>In a lobby waiting for its Start.</summary>
			Lobby,
			/// <summary>On the grid, held, until green.</summary>
			Grid,
			Running,
			/// <summary>Finished or out, until the lobby waits again.</summary>
			Done,
		}

		/// <summary>As the garage's: slower than this counts as stopped.</summary>
		private const float Stopped = 1.2f;

		/// <summary>On the grid the car is held where it was put; a move further than this from there (the reset after a wreck) is out of the race.</summary>
		private const double OffGrid = 30.0;

		/// <summary>How long the green light stays on after the start.</summary>
		private const float GreenSeconds = 3f;

		/// <summary>The race is given up when the car is further than this from the course for <see cref="OffCourseSeconds"/> (or jumps there).</summary>
		private const double OffCourse = 500.0;
		private const float OffCourseSeconds = 10f;

		/// <summary>A move further than this in one frame, not made by <see cref="Respawn"/>, is a teleport: the race is given up.</summary>
		private const double Jump = 150.0;

		/// <summary>How many gates past the next one a crossing is looked for: crossing one of them misses the gates before it.</summary>
		private const int LookAhead = 3;

		/// <summary>Gate posts: this far outside the road's edge, this tall over the road.</summary>
		private const float PostOut = 3f;
		private const float PostHeight = 16f;

		/// <summary>The panel closes when the car is knocked moving faster than this.</summary>
		private const float Knocked = Stopped * 4f;

		private TrackManager tm;
		private Material boxMaterial;
		private Material gateMaterial;
		private Material finishMaterial;
		private readonly List<GameObject> boxes = new List<GameObject>();
		private GameObject[] gates;

		private EventRoom room;
		private EventLink link;

		private State state = State.Idle;
		private Courses.Course course;
		private Courses.Course here;
		private int hereIndex = -1;
		private bool armed = true;
		/// <summary>The lobby and race this machine last put its car on the grid for: a new Start is a new pair.</summary>
		private int raceLobby = -1;
		private int race = -1;
		private int slot;
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
		private bool finished;
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

		private UnityEngine.Object muteKey;
		private int unmuteFrame = -1;
		private Grabber grabber;
		private string note;
		private float noteUntil;
		/// <summary>The course whose waiting spot the car goes to on the next frame, after joining or creating a lobby there.</summary>
		private Courses.Course toWait;

		private static readonly Dictionary<string, ConfigEntry<float>> bests = new Dictionary<string, ConfigEntry<float>>();

		/// <summary>True while a race's clock runs: the reset after a wreck goes to its last gate.</summary>
		internal static bool Running => Instance != null && Instance.state == State.Running;

		/// <summary>True on the grid and while the race runs: a reset (holding R) then keeps the car on the course (see <see cref="Streamer.Recover"/>).</summary>
		internal static bool Racing => Instance != null && (Instance.state == State.Grid || Instance.state == State.Running);

		/// <summary>The course whose panel is open, or null.</summary>
		internal Courses.Course PanelAt { get; private set; }
		internal int PanelIndex { get; private set; } = -1;

		/// <summary>The rules a lobby created from the panel will have.</summary>
		internal bool NewGuns = true;
		internal bool NewMelee = true;

		internal EventRoom Room => room;
		internal ulong Local => link != null ? link.Local : 0UL;

		/// <summary>The race clock (<c>TrackManager.TimeSinceRaceStart</c>), the same moment on every machine in a room: every lobby's times are on it.</summary>
		internal static double Clock()
		{
			EventRace e = Instance;
			return e != null && e.tm != null ? e.tm.TimeSinceRaceStart : 0.0;
		}

		/// <summary>The last refusal or note, for the panel and the strip while it is fresh; null otherwise.</summary>
		internal string Note => note != null && Time.unscaledTime < noteUntil ? note : null;

		internal void Setup(TrackManager manager, Shader lit)
		{
			Instance = this;
			tm = manager;
			muteKey = this;
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
				List<Courses.Course> all = Courses.All;
				EventRoom.CourseInfo[] infos = new EventRoom.CourseInfo[all.Count];
				for (int i = 0; i < all.Count; i++)
				{
					Courses.Course c = all[i];
					infos[i] = new EventRoom.CourseInfo(c.Name, c.Gates.Count, c.MaxEntrants, c.TimeLimit);
					names.Add(c.Name + " " + (c.Distance / 3300.0).ToString("0.0") + " km, " + c.Gates.Count + " gates" + (c.Laps > 1 ? ", " + c.Laps + " laps" : "") + ", " + c.MaxEntrants + " on the grid");
				}
				link = new EventLink();
				room = new EventRoom(link, infos);
				room.Changed += Changed;
				room.Refused += Refused;
				EventLink.Room = room;
				link.Watch(Clock());
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
			bool on = (state == State.Grid || state == State.Running) && course != null;
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


		// ---- following the lobby -------------------------------------------------------------------------------------

		private void Update()
		{
			if (room == null)
			{
				return;
			}
			if (unmuteFrame >= 0 && Time.frameCount > unmuteFrame)
			{
				unmuteFrame = -1;
				GameInput.Mute(muteKey, false);
			}
			double now = Clock();
			if (link.Watch(now) && state != State.Idle)
			{
				// Another room (or none): the lobby this car was in is gone with its arbiter.
				EndLocal();
				state = State.Idle;
			}
			room.Tick(now);
			CarControllerNew car = tm != null ? tm.playerCarController : null;
			CarLink carLink = tm != null ? tm.playerCarlink : null;
			if (car == null || carLink == null || GameManager.raceOver)
			{
				return;
			}
			Vector3 p = car.transform.position;
			Origin.World(p, out double x, out double z);
			float speed = car.rb != null ? car.rb.linearVelocity.magnitude : 0f;
			here = InBox(p, x, z, out hereIndex);
			// Asked every frame, so the hand on the shifter is followed whatever the event is doing.
			bool released = ShifterHand.ReleasedInNeutral(carLink);
			try
			{
				EventRoom.Lobby mine = room.Mine;
				// Joined or created (the panel's buttons act inside OnGUI): to a waiting spot, unless the race has already begun (a solo run).
				if (toWait != null)
				{
					if (mine != null && mine.State == EventRoom.LobbyState.Waiting)
					{
						ToWaiting(toWait);
					}
					toWait = null;
				}
				if (PanelAt != null && (here != PanelAt || speed > Knocked || mine != null || carLink.hp <= 0f))
				{
					ClosePanel();
				}
				if (mine == null)
				{
					if (state != State.Idle)
					{
						EndLocal();
						state = State.Idle;
					}
					Offer(carLink, speed, released);
				}
				else
				{
					Follow(mine, mine.In(link.Local), carLink, x, z, now);
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
				EndLocal();
			}
			prevX = x;
			prevZ = z;
			havePrev = true;
			Board(now);
		}

		/// <summary>The course whose start box the car stands in, at about the box's height (not on a bridge over it).</summary>
		private static Courses.Course InBox(Vector3 p, double x, double z, out int index)
		{
			index = -1;
			Courses.Course c = Courses.BoxAt(x, z);
			if (c == null || Math.Abs(p.y - (c.BoxSurface(x, z) + Network.Lift)) > 6.0)
			{
				return null;
			}
			index = Courses.All.IndexOf(c);
			return c;
		}

		/// <summary>In no lobby: stopped in a start box and the shifter let go in neutral opens the course's panel.</summary>
		private void Offer(CarLink carLink, float speed, bool released)
		{
			bool garage = Garage.Instance != null && Garage.Instance.OpenAt != null;
			bool ready = here != null && speed < Stopped && ShifterHand.Neutral(carLink) && !garage && carLink.hp > 0f && PanelAt == null;
			if (!ready)
			{
				// Out of the box or moving again: the next stop in it opens the panel.
				if (here == null || speed > Stopped * 3f)
				{
					armed = true;
				}
				return;
			}
			// Letting go of the shifter in neutral opens it, so a gear change through neutral does not.
			if (armed && released)
			{
				armed = false;
				OpenPanel(here, hereIndex);
			}
		}

		/// <summary>In a lobby: the car follows what the lobby does (waiting, the grid at a new Start, the race, the results).</summary>
		private void Follow(EventRoom.Lobby mine, EventRoom.Entrant me, CarLink carLink, double x, double z, double now)
		{
			course = Courses.All[mine.Course];
			switch (mine.State)
			{
				case EventRoom.LobbyState.Waiting:
					if (state == State.Grid || state == State.Running)
					{
						EndLocal();
					}
					state = State.Lobby;
					break;
				case EventRoom.LobbyState.Racing:
					if (me != null && me.Run == EventRoom.Run.Running)
					{
						if (mine.Id != raceLobby || mine.Race != race)
						{
							raceLobby = mine.Id;
							race = mine.Race;
							slot = me.Slot;
							ToGrid();
						}
						if (state == State.Grid)
						{
							Counting(mine, carLink, x, z, now);
						}
						else if (state == State.Running)
						{
							Timing(carLink, x, z, now);
						}
					}
					else if (state != State.Done)
					{
						// Finished, or out (the cut-off, the time limit, given up): what was on the course goes.
						EndLocal();
						state = State.Done;
					}
					break;
				case EventRoom.LobbyState.Results:
					if (state != State.Done)
					{
						EndLocal();
						state = State.Done;
					}
					break;
			}
		}

		/// <summary>A new race of this car's lobby: the car on its grid slot, held, the lights over the start line.</summary>
		private void ToGrid()
		{
			Streamer streamer = Streamer.Instance;
			Courses.Spot s = course.Slots[Math.Max(0, Math.Min(course.Slots.Count - 1, slot))];
			if (streamer != null)
			{
				streamer.BuildAround(s.X, s.Z, 1);
				streamer.Place(s.X, s.Z, Quaternion.LookRotation(new Vector3((float)s.Dx, 0f, (float)s.Dz), Vector3.up));
			}
			GameMode.FixFallbackHealth(tm.playerCarlink, "on the grid");
			placedFrame = Time.frameCount;
			havePrev = false;
			heldX = s.X;
			heldZ = s.Z;
			held = tm.playerCarController != null ? tm.playerCarController.rb : null;
			state = State.Grid;
			countdownShown = -1;
			next = 0;
			missed = 0;
			finished = false;
			newBest = false;
			LightsToStart();
			Say(Title(course), 1f);
			Mod.Log.LogInfo("Event: on the grid at the " + course.Name + ", slot " + (slot + 1));
		}

		/// <summary>
		/// The countdown to the lobby's start time on the race clock: red, amber, both, then green and this car's clock starts, with 3,
		/// 2, 1, GO called out. The car is held on its slot until green (<see cref="FixedUpdate"/>), so there is no jump start.
		/// </summary>
		private void Counting(EventRoom.Lobby mine, CarLink carLink, double x, double z, double now)
		{
			double dx = x - heldX, dz = z - heldZ;
			bool moved = placedFrame != Time.frameCount && dx * dx + dz * dz > OffGrid * OffGrid;
			if (moved || carLink.hp <= 0f)
			{
				Mod.Log.LogInfo("Event: " + (moved ? "moved off the grid" : "wrecked") + " before the start at the " + course.Name);
				// Out first: when this machine is the arbiter the race may end inside the request, and its call-out reads the state.
				Out("OUT OF THE RACE");
				LeaveLobby();
				return;
			}
			double toGo = mine.StartAt - now;
			int step = toGo > 3.0 ? -1 : 2 - (int)Math.Floor(Math.Max(0.0, toGo));
			if (step != countdownShown && step >= 0 && step < 3)
			{
				countdownShown = step;
				Lights(true, step >= 1, false);
				Say((3 - step).ToString("0"), 0.8f);
			}
			if (toGo <= 0.0)
			{
				held = null;
				Lights(false, false, true);
				greenUntil = Time.time + GreenSeconds;
				Say("GO!", 1f);
				state = State.Running;
				// From this car's own green (a late frame's lateness taken off), so the room's latency never counts.
				startedAt = Time.time + (float)Math.Max(-1.0, toGo);
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

		/// <summary>The room's news (a lobby opening, a race on): queued behind whatever is showing, never over a countdown.</summary>
		private void News(string text)
		{
			if (state == State.Grid)
			{
				return;
			}
			TextController.EnqueueRandomText(2f, text);
		}

		/// <summary>Out of the race (still in the lobby): the car let go, the gates and lights off.</summary>
		private void Out(string text)
		{
			if (text != null)
			{
				Say(text, 1.5f);
			}
			EndLocal();
			state = State.Done;
		}

		/// <summary>What this car had on the course goes: the hold, the lights, the gates.</summary>
		private void EndLocal()
		{
			held = null;
			Lights(false, false, false);
			greenUntil = -1f;
			if (state == State.Grid || state == State.Running)
			{
				state = State.Done;
			}
		}

		/// <summary>
		/// Each physics step while held, the car is put back on its slot and loses its speed across the ground and about the vertical,
		/// keeping its height, pitch and roll: whatever the engine pushed it in one step is undone in the next. The slot is kept in world
		/// coordinates and turned into the scene's each step, so a floating origin shift on the grid does not move it. Not by the
		/// rigidbody's constraints: a frozen axis zeroes the body's inertia about it, and the tyre forces divide by that inertia, which
		/// made the car's position NaN (and the floating origin with it).
		/// </summary>
		private void FixedUpdate()
		{
			if (held == null)
			{
				return;
			}
			if (state != State.Grid)
			{
				held = null;
				return;
			}
			Vector3 p = held.position;
			if (float.IsNaN(p.x) || float.IsNaN(p.y) || float.IsNaN(p.z))
			{
				held = null;
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

		/// <summary>The clock running: gates crossed in order (each sent to the arbiter), a missed one called out, the race given up off the course.</summary>
		private void Timing(CarLink carLink, double x, double z, double now)
		{
			float clock = Time.time - startedAt;
			if (havePrev && placedFrame != Time.frameCount && placedFrame != Time.frameCount - 1)
			{
				double dx = x - prevX, dz = z - prevZ;
				if (dx * dx + dz * dz > Jump * Jump)
				{
					GiveUp(now);
					return;
				}
				for (int j = next; j < Math.Min(course.Gates.Count, next + LookAhead + 1); j++)
				{
					if (Courses.Crossed(course.Gates[j], prevX, prevZ, x, z))
					{
						Passed(j, clock, now);
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
						GiveUp(now);
					}
				}
				else
				{
					offSince = -1f;
				}
			}
		}

		/// <summary>
		/// Off the course too long, or a teleport: out of the race (a DNF in the others' results) and out of the lobby, as Leave. The Host
		/// passes to the next in; a lobby left empty closes (the owner's ask after 0.23.1: leaving a race early had kept the lobby, the
		/// strip coming back with the player still its Host).
		/// </summary>
		private void GiveUp(double now)
		{
			Mod.Log.LogInfo("Event: given up at the " + course.Name);
			Out("EVENT OVER");
			LeaveLobby();
		}

		private void Passed(int j, float clock, double now)
		{
			Courses.Gate g = course.Gates[j];
			if (j > next)
			{
				int n = j - next;
				missed += n;
				Say(n == 1 ? "MISSED A CHECKPOINT" : "MISSED " + n + " CHECKPOINTS", 1.5f);
			}
			next = j + 1;
			room.Passed(race, j, clock, missed, now);
			if (next >= course.Gates.Count)
			{
				Finish(clock);
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
				Flash("CP " + next + "  " + Clock(clock));
			}
		}

		private void Finish(float clock)
		{
			finishTime = clock;
			finished = true;
			ConfigEntry<float> best = Best(course);
			previousBest = best != null ? best.Value : 0f;
			newBest = missed == 0 && best != null && (best.Value <= 0f || clock < best.Value);
			if (newBest)
			{
				best.Value = clock;
			}
			EventRoom.Lobby mine = room.Mine;
			int place = mine != null ? EventRoom.Position(mine, link.Local) : 0;
			bool alone = mine == null || mine.Entrants.Count < 2;
			Say(missed > 0 ? "FINISH (MISSED " + missed + ")" : !alone && place > 0 ? Ordinal(place) + (newBest ? "  NEW BEST" : "") : newBest ? "NEW BEST" : "FINISH", 3f);
			Mod.Log.LogInfo("Event: finished the " + course.Name + " in " + Clock(clock) + (alone ? "" : ", " + Ordinal(place).ToLowerInvariant()) + (missed > 0 ? ", " + missed + " checkpoints missed (no best time)" : newBest ? ", a new best" : ", best " + Clock(previousBest)));
			Out(null);
		}

		/// <summary>
		/// The reset after a wreck in a race: the car goes back on the course just past the last gate passed (its grid slot if none
		/// yet), facing along it; the clock runs on. False when no race is running (the reset goes to the spawn).
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
				Courses.Spot s = c.Slots[Math.Max(0, Math.Min(c.Slots.Count - 1, e.slot))];
				x = s.X;
				z = s.Z;
				dx = s.Dx;
				dz = s.Dz;
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

		// ---- the room's news -----------------------------------------------------------------------------------------

		/// <summary>A lobby was sent: this car moved to a waiting spot when it joined, and the room told what happened.</summary>
		private void Changed(EventRoom.Lobby l, EventRoom.Why why, ulong who)
		{
			if (why == EventRoom.Why.Snapshot || l.Course < 0 || l.Course >= Courses.All.Count)
			{
				return;
			}
			Courses.Course c = Courses.All[l.Course];
			ulong me = link.Local;
			bool mine = l.In(me) != null;
			string name = EventLink.Name(who), at = "AT THE " + c.Name.ToUpperInvariant();
			switch (why)
			{
				case EventRoom.Why.Created:
					if (who == me)
					{
						toWait = c;
						Mod.Log.LogInfo("Event: created a lobby at the " + c.Name + " (guns " + (l.Guns ? "on" : "off") + ", melee " + (l.Melee ? "on" : "off") + ")");
					}
					else
					{
						News(name.ToUpperInvariant() + " OPENED A LOBBY " + at);
						Mod.Info("Event: " + name + " opened a lobby at the " + c.Name);
					}
					break;
				case EventRoom.Why.Joined:
					if (who == me)
					{
						toWait = c;
						Mod.Log.LogInfo("Event: joined " + EventLink.Name(l.Host) + "'s lobby at the " + c.Name);
					}
					else if (mine)
					{
						News(name.ToUpperInvariant() + " JOINED");
					}
					break;
				case EventRoom.Why.Left:
					if (mine && who != me)
					{
						News(name.ToUpperInvariant() + " LEFT");
					}
					break;
				case EventRoom.Why.Started:
					if (!mine)
					{
						News("RACE ON " + at);
					}
					Mod.Log.LogInfo("Event: a race started at the " + c.Name + " with " + l.Count + " entrant(s)" + (mine ? ", this car among them" : ""));
					break;
				case EventRoom.Why.Ended:
					EventRoom.Entrant mineAtEnd = l.Entrants.Find(e => e.Id == me);
					if (mineAtEnd != null)
					{
						if ((state == State.Running || state == State.Grid) && mineAtEnd.Run == EventRoom.Run.Dnf)
						{
							Say("OUT: " + (l.CutoffAt >= 0.0 && Clock() >= l.CutoffAt ? "THE CUT-OFF" : "TIME LIMIT"), 2f);
						}
						else
						{
							News("RESULTS");
						}
						List<string> lines = new List<string>();
						int place = 0;
						foreach (EventRoom.Entrant e in EventRoom.Standings(l))
						{
							lines.Add(++place + " " + EventLink.Name(e.Id) + " " + (e.Run == EventRoom.Run.Finished ? Clock(e.Time) + (e.Missed > 0 ? " (" + e.Missed + " missed)" : "") : "DNF"));
						}
						Mod.Log.LogInfo("Event: results at the " + c.Name + ": " + string.Join(", ", lines.ToArray()));
					}
					break;
			}
		}

		private void Refused(EventRoom.Refusal why)
		{
			string text;
			switch (why)
			{
				case EventRoom.Refusal.Full:
					text = "That lobby is full";
					break;
				case EventRoom.Refusal.RaceOn:
					text = "That lobby is racing: join when its results are done";
					break;
				case EventRoom.Refusal.NotHost:
					text = "Only the Host can start";
					break;
				case EventRoom.Refusal.CourseTaken:
					text = "Another lobby is racing on the course";
					break;
				case EventRoom.Refusal.NotWaiting:
					text = "The race is already on";
					break;
				case EventRoom.Refusal.NoLobby:
					text = "That lobby is gone";
					break;
				default:
					text = "No such course";
					break;
			}
			Notify(text);
			Mod.Info("Event: refused: " + text);
		}

		private void Notify(string text)
		{
			note = text;
			noteUntil = Time.unscaledTime + 4f;
		}

		/// <summary>
		/// Moves the car just clear of the box to the course's first waiting spot no other car is on, stopped, facing out. A course
		/// has a spot for every grid slot, but several lobbies there may wait at once: when every spot is taken the car stays where
		/// it is rather than be put on another car (the two would be thrown apart).
		/// </summary>
		private void ToWaiting(Courses.Course c)
		{
			Streamer streamer = Streamer.Instance;
			if (streamer == null || c.Waits.Count == 0)
			{
				return;
			}
			Courses.Spot spot = default;
			bool free = false;
			foreach (Courses.Spot s in c.Waits)
			{
				if (!GameMode.CarNear(Origin.Local(s.X, 0f, s.Z)))
				{
					spot = s;
					free = true;
					break;
				}
			}
			if (!free)
			{
				Notify("Every waiting spot is taken: wait here");
				Mod.Info("Event: every waiting spot at the " + c.Name + " is taken; the car stays where it is");
				return;
			}
			streamer.BuildAround(spot.X, spot.Z, 1);
			streamer.Place(spot.X, spot.Z, Quaternion.LookRotation(new Vector3((float)spot.Dx, 0f, (float)spot.Dz), Vector3.up));
			placedFrame = Time.frameCount;
			havePrev = false;
		}

		// ---- the panel and the strip ---------------------------------------------------------------------------------

		/// <summary>Opens the course's panel: driving muted, the cursor freed and the hand hidden, as the garage's window does.</summary>
		private void OpenPanel(Courses.Course c, int index)
		{
			PanelAt = c;
			PanelIndex = index;
			note = null;
			unmuteFrame = -1;
			GameInput.Mute(muteKey, true);
			grabber = FindFirstObjectByType<Grabber>();
			if (grabber != null)
			{
				grabber.SetHandVisible(false);
			}
			Cursor.lockState = CursorLockMode.Confined;
			Cursor.visible = true;
			Mod.Log.LogInfo("Event: the " + c.Name + "'s panel is open (" + (EventLink.InRoom ? "in a room" : "alone") + ")");
		}

		/// <summary>Closes the panel; the controls come back on the next frame (so the Escape that closed it does not also open the pause menu).</summary>
		internal void ClosePanel()
		{
			if (PanelAt == null)
			{
				return;
			}
			PanelAt = null;
			PanelIndex = -1;
			unmuteFrame = Time.frameCount;
			if (grabber != null)
			{
				grabber.SetHandVisible(true);
				if (grabber.lockHardwareCursor)
				{
					Cursor.lockState = CursorLockMode.Locked;
					Cursor.visible = false;
				}
			}
		}

		/// <summary>From the panel: joins a lobby.</summary>
		internal void Join(int lobby)
		{
			ClosePanel();
			room.Join(lobby, Clock());
		}

		/// <summary>From the panel: creates a lobby at its course with the rules chosen; alone (no room), starts it at once: a solo run.</summary>
		internal void Create()
		{
			int index = PanelIndex;
			ClosePanel();
			if (index < 0)
			{
				return;
			}
			room.Create(index, NewGuns, NewMelee, Clock());
			if (!EventLink.InRoom)
			{
				room.Start(Clock());
			}
		}

		/// <summary>From the strip: the Host starts the lobby's race.</summary>
		internal void StartRace()
		{
			room.Start(Clock());
		}

		/// <summary>From the strip: leaves the lobby (in its race, a DNF).</summary>
		internal void LeaveLobby()
		{
			EventRoom.Lobby mine = room.Mine;
			if (mine != null)
			{
				Mod.Log.LogInfo("Event: left the lobby at the " + Courses.All[mine.Course].Name + (state == State.Running || state == State.Grid ? " (out of the race)" : ""));
			}
			room.Leave(Clock());
			EndLocal();
		}

		/// <summary>What "EVENT" over a course's box says: whether a race is on there or lobbies are waiting.</summary>
		internal string BoxLabel(int index)
		{
			if (room == null)
			{
				return "EVENT";
			}
			if (room.Taking(index) != null)
			{
				return "EVENT  RACE ON";
			}
			int waiting = 0;
			foreach (EventRoom.Lobby l in room.Lobbies)
			{
				waiting += l.Course == index ? 1 : 0;
			}
			return waiting == 0 ? "EVENT" : "EVENT  " + waiting + (waiting == 1 ? " LOBBY" : " LOBBIES");
		}

		/// <summary>
		/// The line offering the panel while the car is in a start box and in no lobby, drawn by <see cref="GarageUi"/>: the course, its
		/// length, laps, checkpoints, grid and best time, what is on there, and what to do. Null when there is nothing to offer.
		/// </summary>
		internal string Prompt()
		{
			if (here == null || PanelAt != null || room == null || room.Mine != null)
			{
				return null;
			}
			string what = Info(here) + ", " + here.Gates.Count + " checkpoints     " + BestLine(here);
			string how = !armed ? "Drive out of the box to open it again" : "Stop and shift into neutral for the event's lobbies";
			return "<color=#ff8c1a>" + BoxLabel(hereIndex) + "</color>  " + here.Name + "\n" + what + "\n" + how;
		}

		/// <summary>The course's facts for the panel: length, laps, checkpoints, grid, time limit and best.</summary>
		internal static string Facts(Courses.Course c)
		{
			int limit = (int)c.TimeLimit;
			return Info(c) + ", " + c.Gates.Count + " checkpoints, " + c.MaxEntrants + " on the grid, time limit " + limit / 60 + ":" + (limit % 60).ToString("00") + "     " + BestLine(c);
		}

		/// <summary>The scene position of a course's start box, for the label over it.</summary>
		internal static Vector3 ScenePoint(Courses.Course c)
		{
			return Origin.Local(c.BoxX, (float)(c.BoxY + Network.Lift), c.BoxZ);
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

		/// <summary>A time left as m:ss.</summary>
		private static string Left(double seconds)
		{
			int s = (int)Math.Ceiling(Math.Max(0.0, seconds));
			return s / 60 + ":" + (s % 60).ToString("00");
		}

		internal static string Ordinal(int n)
		{
			int teen = n % 100;
			string suffix = teen >= 11 && teen <= 13 ? "TH" : n % 10 == 1 ? "ST" : n % 10 == 2 ? "ND" : n % 10 == 3 ? "RD" : "TH";
			return n + suffix;
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
		/// What the cockpit's board shows (<see cref="EventBoard"/>): the lobby (its entrants), the countdown, the clock with the
		/// checkpoints and the place, and the standings or results; nothing out of a lobby (the board tucks away and shows the players
		/// under the hand). The lap counter beside it shows the lap, or the checkpoint on a course of one lap.
		/// </summary>
		private void Board(double now)
		{
			EventBoard board = EventBoard.Instance;
			if (board == null)
			{
				return;
			}
			EventRoom.Lobby mine = room.Mine;
			string text = null, lapLine = null;
			if (mine != null && course != null)
			{
				int n = mine.Entrants.Count;
				bool alone = n < 2;
				switch (state)
				{
					case State.Lobby:
					{
						List<string> names = new List<string>();
						foreach (EventRoom.Entrant e in mine.Entrants)
						{
							names.Add(EventLink.Name(e.Id));
						}
						string taken = room.Taking(mine.Course) != null ? Red + "Race on: wait</color>" : "Waiting";
						text = course.Name + "\n<size=70%>" + string.Join(", ", names.ToArray()) + "</size>\n" + BestLine(course) + "\n" + taken;
						lapLine = "<size=75%>lobby</size> " + mine.Count + " / " + course.MaxEntrants;
						break;
					}
					case State.Grid:
					{
						double toGo = mine.StartAt - now;
						string count = toGo > 3.0 ? "Ready" : Red + Math.Max(1, (int)Math.Ceiling(toGo)).ToString("0") + "</color>";
						text = course.Name + "\n" + Info(course) + "\n" + (alone ? BestLine(course) : "Slot " + (slot + 1) + " of " + n) + "\n" + count;
						lapLine = Laps(course, 0);
						break;
					}
					case State.Running:
					{
						float clock = Time.time - startedAt;
						string third = lastLapShown >= 0f && Time.time - lastLapShown < 4f ? "Lap " + Clock(lastLap)
							: flash != null && Time.time - flashAt < 3f ? flash
							: "CP " + next + "/" + course.Gates.Count + (missed > 0 ? " " + Red + "-" + missed + "</color>" : "");
						string fourth = mine.CutoffAt >= 0.0 ? Red + "Cut-off " + Left(mine.CutoffAt - now) + "</color>"
							: alone ? BestLine(course) : "Limit " + Left(mine.EndsAt - now);
						string head = alone ? course.Name : course.Name + "  P" + EventRoom.Position(mine, link.Local) + "/" + n;
						text = head + "\n" + (Time.time < greenUntil ? Green + Clock(clock) + "</color>" : Clock(clock)) + "\n" + third + "\n" + fourth;
						lapLine = Laps(course, next);
						break;
					}
					case State.Done:
					{
						text = alone ? Result() : Standings(mine, now);
						lapLine = mine.State == EventRoom.LobbyState.Results ? "<size=75%>results</size>" : "<size=75%>finished</size>";
						break;
					}
				}
			}
			board.Text = text;
			board.Lap = lapLine;
		}

		/// <summary>A run of one: the time against the best.</summary>
		private string Result()
		{
			if (!finished)
			{
				return course.Name + "\n" + Red + "Out</color>\n\n" + BestLine(course);
			}
			string gap = missed > 0 ? Red + missed + " missed</color>" : newBest ? Green + "NEW BEST</color>" : previousBest > 0f ? Red + "+" + (finishTime - previousBest).ToString("0.00") + "s</color>" : "";
			return "Finish\n" + Clock(finishTime) + "\n" + gap + "\n" + BestLine(course);
		}

		/// <summary>The standings: this car's place and time, then the first three (finish times, or gates passed, DNF for those out).</summary>
		private string Standings(EventRoom.Lobby l, double now)
		{
			List<EventRoom.Entrant> order = EventRoom.Standings(l);
			int place = EventRoom.Position(l, link.Local);
			string head = (l.State == EventRoom.LobbyState.Results ? "Results" : l.CutoffAt >= 0.0 ? "Cut-off " + Left(l.CutoffAt - now) : "Limit " + Left(l.EndsAt - now))
				+ "  " + (finished ? "P" + place + " " + Clock(finishTime) : Red + "DNF</color>");
			List<string> lines = new List<string> { head };
			for (int i = 0; i < Math.Min(3, order.Count); i++)
			{
				EventRoom.Entrant e = order[i];
				string what = e.Run == EventRoom.Run.Finished ? Clock(e.Time) : e.Run == EventRoom.Run.Running ? "CP " + e.Passed : "DNF";
				string name = EventLink.Name(e.Id);
				if (name.Length > 12)
				{
					name = name.Substring(0, 12);
				}
				lines.Add("<size=80%>" + (i + 1) + " " + name + "  " + what + "</size>");
			}
			return string.Join("\n", lines.ToArray());
		}

		/// <summary>
		/// The strip under the board while in a lobby, for <see cref="GarageUi"/>: its two lines, and whether Start shows (to the Host)
		/// and can be pressed (the course free). False out of a lobby, and from the Start until the lobby waits again: on the grid and
		/// in the race nothing should be on the screen but the board (the owner's ask after 0.23.0).
		/// </summary>
		internal bool Strip(out string top, out string bottom, out bool host, out bool canStart, out string startLabel)
		{
			top = bottom = startLabel = null;
			host = canStart = false;
			EventRoom.Lobby mine = room != null ? room.Mine : null;
			if (mine == null || mine.Course < 0 || mine.Course >= Courses.All.Count || mine.State != EventRoom.LobbyState.Waiting)
			{
				return false;
			}
			Courses.Course c = Courses.All[mine.Course];
			host = mine.Host == link.Local;
			bool taken = room.Taking(mine.Course) != null && room.Taking(mine.Course) != mine;
			double now = Clock();
			string what;
			switch (mine.State)
			{
				case EventRoom.LobbyState.Racing:
					what = now < mine.StartAt ? "GRID  " + Left(mine.StartAt - now) : "RACING";
					break;
				case EventRoom.LobbyState.Results:
					what = "RESULTS";
					break;
				default:
					what = taken ? "<color=#e23a3a>RACE ON</color> (another lobby)" : host ? "WAITING: start when ready" : "WAITING for " + EventLink.Name(mine.Host);
					break;
			}
			top = "<color=#ff8c1a>" + c.Name.ToUpperInvariant() + "</color>  Host " + EventLink.Name(mine.Host) + "   " + mine.Count + "/" + c.MaxEntrants;
			bottom = "Guns " + (mine.Guns ? "on" : "off") + "  Melee " + (mine.Melee ? "on" : "off") + "   " + what;
			canStart = host && mine.State == EventRoom.LobbyState.Waiting && !taken;
			startLabel = taken ? "Race on" : "Start";
			return true;
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
			held = null;
			ClosePanel();
			if (unmuteFrame >= 0)
			{
				GameInput.Mute(muteKey, false);
				unmuteFrame = -1;
			}
			if (EventLink.Room == room)
			{
				EventLink.Room = null;
			}
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
