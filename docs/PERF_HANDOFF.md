# Performance handoff

**Start here: "The agreed plan (2026-11-24, after 0.32.3)" just below.** It replaces "Ways out, once measured" further down. The
history under it (0.32.1 to 0.32.3) is why: the traffic's own code is about 0.1 ms a frame, so the frames go to drawing.

## The agreed plan (2026-11-24, after 0.32.3)

The owner wants **130 fps on a 265K and a 3060** (now 100 to 110 on the highway) and **ten times the highway traffic or more**, and
it must still **look like traffic** (queues, bunching). Two releases, in this order. Nothing below is built yet.

A note on the owner: answer plainly and briefly, about what changes the frame rate or what the player sees. A long explanation of the
start-of-race day build lost them (it costs no frame rate); keep to the point.

### What the research found (no code changed)

- **The game draws at low resolution** (`SmoothPixelGrid` on the Main Camera, a pixel-art downscale), so the 3060 has little to fill:
  at 100 fps the limit is most likely the CPU's culling and draw calls (main and render threads). Unity 6000.3.22f1, URP.
- **About a thousand renderers, every one casting shadows.** `TileRadius` 5 keeps 121 tiles; each has the ground and up to eight
  parts (`Streamer.Part`: Roads, Verges, Sidewalks, Tracks, Circuit, Gravel, Water, Buildings), several with a submesh per material.
  The mod never sets `shadowCastingMode` on them, and Unity's default is On, so flat ground, roads and water go into every shadow
  cascade too.
- **The rear-view mirror draws the world a second time every frame in the cockpit view.** `RearViewCamera` (scene dump: fov 28,
  far 1000, depth 0; enabled with the Main Camera by `CameraSwitch.UpdateCameraView`, off in the outside view) feeds the cockpit's
  `RearViewMirror` sprite (material `MAT_RearviewMirror`). URP gives it its own shadow pass. The game's `RenderBudget` caps its
  far clip by quality level and hides *terrain* foliage in it; our foliage (`Foliage.Draw`, `RenderParams` with no `camera`) is
  drawn in it and its shadows regardless. `SmartMirror` turns it toward enemies in `LateUpdate`.
- **The ground's texture is set through a `MaterialPropertyBlock`** (`Streamer.cs`, the tile's `renderer.SetPropertyBlock(block)`).
  A renderer with a property block is left out of URP's SRP Batcher, so 121 ground tiles take the slow path (twice with the mirror).
- **Every loaded tile gets mesh colliders** (ground and every solid part), cooked on the main thread as the tile comes: part of the
  1.3 ms a tile, about 5 tiles a second at speed. Nothing drives on a tile 400 m off. (Tree trunks' colliders were already near only,
  `TrunkReach`.) Drawn traffic cars stand on the ground by two raycasts each out to `MovingReach` 900, so they rely on far colliders.
- **Origin shifts** cost 4.5 to 8.4 ms each, about every 6 s at highway speed (`ShiftDistance` 1000).
- **Drawn traffic cars** are each a GameObject copy of the enemy car's bodies: 8 or 9 MeshRenderers, a BoxCollider and a kinematic
  Rigidbody (a wall), painted by swapping `sharedMaterials` (`TrafficCars.Make`, `Paint`). Capped at 72 (`MostDrawn`).
- **The experiment:** `TrafficDay.Circling` 1000 in trafficview (`TV_DRIVE_ONLY=1`): the day built in 10.2 s (2.6 with 100), about
  0.95 GB peak; **1,092 legs unfinished, 247 pairs of cars on a lane closer than a car's length, physics cars in each other, the
  hours' shape off by 4.8 points**: the trunk jams at the uncontrolled junction with the Westhaven link. The day sim also puts each
  direction of the trunk on one lane (`LaneOf` is per edge and way), though it has two lanes each way. So more circling cars in
  today's day sim is not the way.

### Tests the owner can make with no build (ask for the overlay's fps line in each, same seed and stretch)

1. The cockpit view against the outside view (the outside view has no mirror camera): roughly the mirror's cost.
2. `TileRadius` 3 against 5: whether draw calls are the limit.
3. `ShiftDistance` 3000 against 1000: fewer shift hitches.
4. `GrassReach` 0 and `TreeShadowReach` 0: the foliage's share.

### Release one: the frame rate (no change to the world; agreed item by item)

1. **Shadows off for flat things.** `ShadowCastingMode.Off` on Roads, Verges, Sidewalks, Tracks, Circuit, Gravel and Water, and on
   the ground past the car's tile and its neighbours (hills' shadows near the car kept; switch a tile's ground as the car's tile
   changes, a cheap pass over loaded tiles). Buildings and trees keep theirs (trees by `TreeShadowReach` as now). Agreed.
2. **The mirror drawn every other frame.** The owner: its reach (1000, about 300 m) is short enough already, **keep its reach and keep
   the foliage in it**; halving its rate is good. Toggle `RearViewCamera.enabled` on alternate frames, only while the game has it on
   (cockpit view: watch `CameraSwitch` so the outside view still turns it off and our toggle never turns it back on; restore it on
   our teardown). The mirror sprite keeps the last picture on the skipped frames. First check in the decompile that it really draws
   into a RenderTexture (`MAT_RearviewMirror`'s base map; the camera's `targetTexture` is set somewhere, not in the dump): if it
   draws straight to the screen instead, skipping frames would blank it, so stop and tell the owner. Also try its own shadows off
   (`UniversalAdditionalCameraData.renderShadows = false`) only if the mirror looks the same; otherwise leave them.
3. **Ground through a material per tile** instead of the property block: `new Material(material)` with `_BaseMap`/`_MainTex` set,
   `HideAndDontSave`, freed with the tile (`TileMesh` already frees the mesh and texture; add the material, see `WorldAssets`). The
   owner: **only if it does not alter the appearance**: same shader, same texture, same settings, so it should not; compare
   screenshots before and after if in doubt.
4. **Colliders only near the car, cooked off the main thread.** The owner was surprised this was not done already. Mesh colliders
   on the tiles within 2 of the car's (the trunk colliders' pattern: added as the car comes, taken off a tile further out), cooked
   on a worker with `Physics.BakeMesh(mesh.GetInstanceID(), false)` before the `MeshCollider` is added on the main thread (needs the
   mesh made first: build the tile meshes with `Mesh.AllocateWritableMeshData` on the worker and `ApplyAndDisposeWritableMeshData`
   on the main thread, or bake right after the mesh is made, on a job). What needs a far collider must stop needing it: **drawn
   traffic cars' raycasts** (`TrafficCars.cs`, `Physics.Raycast(... groundMask ...)`) take the height from `Network.Point` on the
   road / `WorldGen.Height` instead (note `Network.Lift` and `CLAUDE.md`'s height rule), past the collider ring at least; check
   anything else that raycasts the ground far off (deer use code tests; the events' gates? the respawn's "nearest road"?). Physics
   cars live within 300 to 400 units, inside 2 tiles' 500. A wrap's preload (`BuildAround(..., 1)`) must give the car's landing tile
   its colliders at once. Add a meshcheck or in-game log check that the car's tile and its neighbours always have colliders.
5. **Measuring on the overlay** (behind `ShowCoords`, logged every 10 s with `Traffic timings`): Unity's `ProfilerRecorder` for
   "Main Thread", "Render Thread" (or the CPU frame times from `FrameTimingManager` if enabled), "Batches Count", "SetPass Calls
   Count", "Shadow Casters Count", "Triangles Count". Some may read 0 in a release player; show what works, say which do not.
   Owner: "sure".

Also worth doing in this release if cheap: `ShiftDistance`'s default raised (to 2000 or 3000) if the owner's test 3 shows the hitch
going, and make the shift cheaper (`Origin.cs` walks `FindObjectsByType<Transform>(Include)` for the cockpit's interior layer each
shift; cache it).

Run `tools/check.sh`, meshcheck, trafficview; bump the version (0.33.0) and the README; add a `docs/TESTING.md` test: fps on the
highway and in Kingsbridge in the cockpit view against 0.32.3, the mirror moving smoothly enough, no car falling through a tile
(colliders near), drawn cars still on the road far off, the ground looking the same.

### Release two: dense traffic that looks like traffic

Two parts, agreed with the owner.

**A. Booked traffic** (replaces how `TrafficDay` drives the day; the owner's own idea, worked through in the conversation of
2026-11-24). Today the timetable only says when each car leaves; the day is then played through on 0.25 s steps (IDM, junction
reservations, pulling out, letting in) so cars can hold each other up, and that replay is what jams and slows at 1000 circling cars.
Instead, **plan the clashes away when the trips are made**:

- Each trip, in a fixed order (by departure time, ties by index), is given its whole drive as a plan: times through each junction
  box and along each stretch. Every car at the same spot on a road gets the same free speed (the road's profile: limit, bends,
  junctions), so a car never closes on the one ahead by itself.
- **Junction slots are booked.** A box's slot taken: the car is planned to **stop at the back of the queue** (one car length plus a
  gap behind the last car planned to stand there) and go when the car ahead has moved up plus a gap; or to slow a little on the way
  in. **Queues stay, they are planned** (the owner checked this): rush hour books many trips at once, so queues grow long by
  themselves; at night they vanish. A car planned to come up behind a slower one (a queue's tail, a car pulling in) is planned to
  slow behind it.
- Pulling out of a space, being let in, waiting to turn across the oncoming lane into a space: each is a booking rule (a gap in the
  lane's plan); any not written is lost until it is, so write them all (today's `TrafficDay` has them: `StartWaiting`, `LetInAfter`,
  `FarSide`/`FarEnd`, `PullOut`/`PullIn`).
- Booking order gives earlier trips priority; guard against odd waits (never stop for a car still far off; a slot is the box's
  occupancy only).
- **A car's place at any time is a formula of its own plan** (piecewise: cruise, slow, stop, go, with smooth accel and braking
  curves so a queue does not look mechanical). Store the plan as a few pieces per stretch, not a record every 0.5 s; then 10x or more
  costs little memory or time. Keep `Leg`'s interface (`OnPath`, `At`, the distance at a time that `TrafficDrive.cs:277` reads from
  `Rec`) so `TrafficLate`, `TrafficDrive`, `TrafficPhysics`, `TrafficParking` and `TrafficRoom` keep working: they read a timetable,
  and a plan is one.
- **The highway**: both lanes each way (today one), each lane its own speed, circling cars spaced on it, the trunk given priority at
  its junctions (local cars book a gap in the trunk's plan to join or cross). Then 1,000 or more circling cars cost almost nothing.
- Still from the race's seed (different every race, weekday and weekend as now, the same for everyone in a room): the trips come
  from it as today, booking is deterministic (`CLAUDE.md`'s determinism rules). A room needs the same version, as usual.
- What the owner was told it costs: queue driving a little simpler unless the curves are done well; each small interaction a rule
  to write; a big rework (about 2,400 lines of `TrafficDay`'s driving and many of trafficview's checks) and a few rounds of testing.
  What they gain: density limited only by drawing, no 3 s build, no gridlock.
- trafficview: keep the checks that still mean something (no two cars closer than a car's length on a lane, the junction boxes,
  counts by road class against PennDOT, the hours' shape, the wrap, parking, the physics cars' checks); add: queues at the peak
  (lengths at the busiest junctions), the gaps between circling cars along the trunk, the build time and memory at 1,000 and 3,000
  circling cars. Run trafficsim too (the room reads the ledger).
- Near the player nothing changes: physics cars with `TrafficDrive`, contact physics, the ledger, the room.

**B. Drawn cars made cheap to draw** (agreed). Past a near ring (say 150 units, where the GameObject cars with their box stay, for
contact physics and walls), draw cars with `Graphics.RenderMeshInstanced`: per look, one call per body mesh and material, instances
grouped by paint (a material per paint per look, as now), wheels as instances too (or left off far out); heights from the road
(`Network.Point`) and the plan, not raycasts; no shadows past a distance. Then raise `MostDrawn` from 72 to several hundred and the
moving reach on the trunk toward the fog's end. Lamps at night: the nearest few keep real lights as now; far ones emissive only.

**Not now (the owner):** fewer physics cars on the highway ("it's a racing game, you weave all over the highway"; maybe later).

## History: after 0.32.1; 0.32.2 and 0.32.3 below

The owner, 2026-11-24, on 0.32.1: **frame rate down from about 135 to about 100** (7.4 to 10 ms a frame: about 2.6 ms more), and the
highway "barely" busier for it. 0.32.1 changed four things (see `docs/TRAFFIC_HANDOFF.md`, "0.32.1"): 100 cars circling the trunk
instead of 10, physics cars driven into the tunnels (a cheap check every frame), a water strip trimmed, the overlay's box. Only the
first can plausibly cost 2.6 ms. Nothing is changed yet: this is what was found, what the log shows, and how to find out the rest.

## Found: two functions walk a car's whole route from its start

The owner's point (2026-11-24): a handful of physics cars, and thicker town traffic in this mod never cost frames, so the
implementation is at fault. It is. A town car's trip is 10 to 20 stretches of road (`TrafficDay.Seg`); **a circling car's leg is the
whole day in one piece: 6 laps of the trunk, about 250 stretches, 257,000 units, about 5,000 records.** Two functions scan a leg's
path from its first stretch on every call, so their cost grows with the leg, and the 100 circling cars are now what the player meets:

- **`TrafficParking.Corner`** (`TrafficParking.cs:619`): `int j = 1; while (j < path.Length && path[j].P0 <= p) j++;` to find the
  junction nearest `p`. Called by `TrafficParking.Drawn` (every drawn moving car, **every frame**) and by `TrafficDrive.Line`, which
  `TrafficDrive.Project` calls about 26 times (16 samples and 10 refinements) and `Steer`, `OnLane` and `Toward` once each, **per
  physics car per physics step**.
- **`TrafficDrive.NearTunnel`** (`TrafficDrive.cs`, `foreach (TrafficDay.Seg s in leg.Path)`): the whole path, every call. Called
  by `TrafficPhysics.Bubble` for each physics car and by `Drivable` for each candidate car, every choice (0.4 s), and by trafficview.

**Fix:** find the stretch with the binary search already there (`TrafficDrive.SegAt`, `TrafficDay.OnPath` uses the same) and look
only at it and its neighbours: `Corner` needs the junctions either side of `p` (stretches `k` and `k + 1`); `NearTunnel` needs the
stretches within `TunnelKeep` of `p` (walk out from `k` while `P0` is within reach). Both then cost the same whatever the leg's
length. Check: trafficview (the drive checks use both) gives the same day checksum, the same physics car results and no jumps.
Grep the rest of the traffic's per-frame code for any other scan from a path's or a record array's start (`TrafficDay.cs:2392`'s
linear scan of `Rec` is in a diagnostic pass for trafficview only, not in the game; leave it).

**Alternative or addition:** make each circling car's day a chain of one-lap legs instead of one leg, so no path is ever long. That
touches `TrafficDay`'s through traffic, the ledger (a late car moves to its next leg) and trafficview's counts; do it only if the
binary search is not enough.

The arithmetic says this waste is real but may not be all of the 2.6 ms (roughly 125 stretches walked per call, tens of calls per
physics car per step and one per drawn car per frame). So **the same build adds the timings below**, and the owner's next test says
what is left. The next build: the two scans fixed, the timings on the overlay, nothing else.

## Done in 0.32.2 (2026-11-24, the owner to test: `docs/TESTING.md` test 53)

- **The two scans fixed.** `TrafficParking.Corner` finds its junction with `TrafficDrive.SegAt` (the binary search, made internal),
  `TrafficDrive.NearTunnel` looks only at the stretches from the one at `p - TunnelKeep` (and the one before, which may end exactly
  there) to `p + TunnelKeep`. Grepped the rest of the per-frame traffic code: no other scan from a path's start (`Prepare`, `Line`,
  `Target` already start from `SegAt`; `TrafficDay`'s full walks are in the day's build or trafficview's diagnostics). trafficview
  (`TV_DRIVE_ONLY=1`): the same day checksum (62b10cf9f1c421ce), the same physics car results line for line, no jumps; the drivers'
  line 7.0 us a point projected instead of 9.7 (its test cars are local ones; a circling car's gain is larger).
- **Timings on the overlay and in the log** (`TrafficCars.Perf`, `ShowCoords` only): the frame time and fps; the traffic's whole
  `Update` a frame and inside it the choice (its full bubble included), drawing, the per-frame bubble, `Contact`, `BuildMovers`;
  all physics cars' `FixedUpdate`s (per frame); the bodies' switching (`TakeBody` with `MakeBody`, `Place` with its `SetActive`,
  `Release`, `WarmPool`); drawn moving and parked, physics live, bodies taken and given back in the last minute. Averaged over a
  second; logged every 10 s as `Traffic timings: ...`. Not timed: the game's own physics and audio for the bodies (inside Unity's
  step, not ours): if the fps is still down with small numbers here, that is where it goes (the handoff's suspect 1).
- **Tyre marks:** the fix written here was already on `main` (PRs 50 and 51, `main`'s 0.32.1: interpolation back after two physics
  steps, marks held off over a shift, a jump guard); this branch had been started from `main` before they were merged. Merged in as
  0.32.3 with `main`'s version kept. The owner saw no tyre marks on 0.32.2.

## The owner's 0.32.2 drive (2026-11-24)

- Better but still 100 to 110 fps at highway speed. The log's `Traffic timings`: the traffic's own code about 0.1 ms a frame, 1 to 7
  cars drawn, physics steps 0.01 to 0.03 ms, switching 0.00 to 0.01 ms; and the frame time does not follow the physics cars (133 fps
  with 6 live, 99 with 2). **The traffic is not where the frames go.** Next: time the rest of what runs at speed (the tiles built and
  loaded, about 5 a second, with Unity's own mesh and collider work; the far view; the sky; foliage).
- Drawn cars stood too high and dropped when made physical: 0.32.2 lacked `main`'s rest on the springs (`TrafficRest.cs`). Merged in
  0.32.3.
- The owner wants 130 fps on a 265K and a 3060 and ten times the highway traffic or more.

## What the log shows (`LogOutput.log` of the drive, 0.32.1)

- One solo drive (the room's host alone) from the spawn (-3010, -5887) east along the trunk, at up to 168 km/h, to the east tunnel and
  through the wrap. 23 origin shifts, about 18,000 units of trunk: about half its length. Weather ForestRainyDay, "a showery day"
  (fog and rain on the way). Race starts 14:29, a weekend day.
- Traffic day built in 2,992 ms on the worker (3,704 cars: 3,604 households' and **100 circling, cars 3604 to 3703**).
- **78 physics cars went back on their timetable, 55 of them circling cars**, every one "out of reach" (none at a tunnel, none loose,
  none retired). So on that drive each circling car met became a physics car (within 300 units) for a few seconds and was given back
  at 400. With 10 circling cars the same drive would have made about 5. Local cars: 22 (in and near the towns the trunk passes).
- The physics cars' pool grew to **14 bodies** ("a physics car's body made (14 in use or spare)"), all made early in the drive.
- No warnings or exceptions from the mod. 1.3 to 1.4 ms per tile built on the main thread, as before. Origin shifts 4.5 to 8.4 ms (as
  before). A Scene Dump mod wrote a 2 MB dump once at the start (a one-off hitch, not the frame rate).
- The log has no timings for the traffic's frame cost, and no frame rates. **Where the 135 and the 100 were read is not known**
  (on the highway, in a town, standing, at what hour and weather): the first thing to ask.

## Why the highway can look barely busier

- 100 cars on about 34,300 units of trunk (trafficview), 50 each way: **one about every 690 units each way** (about 210 m). At 168 km/h
  against 85 km/h oncoming, an oncoming car passes about every 3 s and is in view for a few seconds; same-way cars are overtaken
  about every 9 s. Moving cars are drawn within 900 units (`TrafficCars.MovingReach`), the nearest 72 (`MostDrawn`, moving weighted
  nearer), and nothing past that (the far view has no cars). In fog and rain fewer still are seen.
- They may bunch: the trunk's uncontrolled junction with the Westhaven link (node 5, at 11316, -9288) stops them (46.8 min stopped on
  the trunk's edge 40 over the day in trafficview, against 0.8 min of waiting there with 10). A platoon then long gaps reads as
  "empty". Not measured yet: trafficview should print the gaps between circling cars along the trunk at a few hours.
- The circling cars all go at the trunk's free speed, so at the owner's speed the same-way ones are slow obstacles, not traffic.

## Other suspects for what is left after the fix, most likely first

1. **Physics cars on the highway** (their own cost, apart from the scans above). Each is a full copy of the game's enemy car (`TrafficPhysics`, `MakeBody`: `CarControllerNew`,
   rigidbody, wheels, colliders, its audio sources, tyre trails and particles; its AI, guns and `PitGuard` switched off), driven by
   `TrafficBody.FixedUpdate` (`TrafficDrive.Project`, `Target`, `Steer`). Before 0.32.1 there were almost none on the trunk; now one
   to several are live most of the way, and one is **made and released every few seconds**: `SetActive(true)` on a whole car (its
   renderers, colliders, audio and rigidbody waking), placed with interpolation held off, then `SetActive(false)` at 400. Both the
   steady cost (physics and audio per car) and the hitches of switching are possible.
2. **More drawn moving cars near the trunk.** Each drawn car is a copy of the enemy car's bodies (8 or 9 meshes, 1 or 2 painted,
   lamps), stood on the ground by two raycasts a frame, swept against the player for contact physics each frame. More draw calls
   and shadow casters along the highway than before.
3. Smaller, per frame and per car: `Contact` (a sweep per drawn car), `BuildMovers`, the new `Bubble(..., darkOnly)` every frame
   (a binary search per physics car, plus `wrecks.RemoveAll` with a lambda each frame: a little garbage), `Choose` every 0.4 s over
   all 3,704 cars (0.5 ms a choice in the owner's earlier screenshot; it does not grow with the circling cars much).
4. Not a frame cost: the circling legs are long (257,000 units each, recorded every 0.5 s for the day): memory and the 1 s more to
   build the day on the worker, at the start only.

## How to find out

Owner tests that need no new build (same room seed, so the same day and the same cars; same route, hour and weather; frame rate on the
trunk and in Kingsbridge's centre):

1. `TrafficPhysics` 0 (no physics cars) and `TrafficLoose` 0, then each back to its default (24, 6). If the frame rate comes back with
   physics off, suspect 1 it is.
2. `Traffic` off: the floor (all traffic's cost together).
3. Drive the same stretch at the 85 km/h the circling cars do, and stand still beside the trunk: tells the switching (making and
   releasing bodies) from the steady cost.

What to add in code (a build for measuring, kept behind `ShowCoords`):

- Timings on the overlay, averaged over a second: the traffic's `Update` (drawing, `Contact`, `BuildMovers`, the per-frame bubble),
  `Choose`, all `TrafficBody.FixedUpdate`s together, and the time spent making and releasing bodies; counts: drawn moving and parked,
  physics cars live, made and released per minute. A Stopwatch around each, as `chooseMs` already is.
- trafficview: the gaps between circling cars along the trunk (each way) at 06:00, 12:00, 17:00, and how many are within 900 of a
  player driving the trunk at 168 km/h and at 85 km/h, averaged over a run: what "busier" would have to mean in numbers.
- A build with `Circling` 10 to compare like for like, if the configs above don't settle it.

## Ways out, once measured (to choose with the owner)

- **Physics only where it can matter on the trunk:** a car on the far carriageway, or a same-way car the player is not closing on,
  stays drawn (contact physics already makes a car real just before it is touched). This alone would cut most of the 55 makes.
- **Cheaper switching:** keep a few bodies live but parked out of the way (kinematic, renderers and audio off) instead of
  `SetActive` each time; or strip the engine audio, trails and particles from traffic bodies until they are hit.
- **Cheaper drawn cars:** fewer meshes per drawn car far off (one combined mesh past, say, 300 units), no shadows past a distance,
  GPU instancing of the bodies.
- **More cars seen for the same cost:** fewer, better spread circling cars (even gaps kept, some faster), or the trunk's junction with
  the Westhaven link given priority to the trunk so platoons don't form; drawn cars to the fog's end on the trunk only.
- Or back to fewer circling cars (say 30 to 40) if the measurements say the cost is per car.
