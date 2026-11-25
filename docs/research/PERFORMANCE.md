# Performance: findings

What was measured and found in the 0.32.1 to 0.33.0 frame-rate work, moved word for word from its handoff (the fixes themselves are in git history). Reference only.

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

## The owner's 0.32.2 drive (2026-11-24)

- Better but still 100 to 110 fps at highway speed. The log's `Traffic timings`: the traffic's own code about 0.1 ms a frame, 1 to 7
  cars drawn, physics steps 0.01 to 0.03 ms, switching 0.00 to 0.01 ms; and the frame time does not follow the physics cars (133 fps
  with 6 live, 99 with 2). **The traffic is not where the frames go.** Next: time the rest of what runs at speed (the tiles built and
  loaded, about 5 a second, with Unity's own mesh and collider work; the far view; the sky; foliage).
- Drawn cars stood too high and dropped when made physical: 0.32.2 lacked `main`'s rest on the springs (`TrafficRest.cs`). Merged in
  0.32.3.
- The owner wants 130 fps on a 265K and a 3060 and ten times the highway traffic or more.

## The owner's 0.32.4 drive (2026-11-24): "barely 80 fps driving instead of the capped 140"

The log (about 30 s of driving from the spawn, three `Frame timings` lines; the room's host alone; StadiumAsh, a clear dry day, 12:13;
21 mods loaded) says, per frame, averaged over a second:

- 86 to 110 fps (9.2 to 11.7 ms). **The main thread is what limits it**: waiting for the screen or the GPU
  (`TimeUpdate.WaitForLastPresentationAndUpdateTime`) only 0.1 to 0.6 ms. The GPU's own time is n/a in this build.
- `PostLateUpdate.FinishFrameRendering` (URP's culling and draw submission on the main thread) **3.4 to 5.9 ms**: the largest step,
  with **2,600 to 3,100 draw calls** (as many batches), 516 to 535 set-pass calls, 1.7 to 2.0 million triangles, 145 to 202 shadow
  casters.
- `Update.ScriptRunBehaviourUpdate` 2.3 to 2.4 ms, of which ours (sky, deer, traffic, the streamer's checks) about 0.2: **about 2.1 ms
  is the game's and the other mods' scripts.** `PreLateUpdate.ScriptRunBehaviourLateUpdate` 1.9 to 2.1 ms, of which ours about 1.0
  (foliage 0.8, streaming 0.01 to 0.2, far view 0.06): about 1 ms not ours.
- `PreUpdate.IMGUISendQueuedEvents` 0.06 to 0.66 ms (OnGUI: the overlay and other mods' windows). Physics 0.1 ms. The traffic 0.1 to
  0.2 ms. Tiles made 0 to 0.2 ms a frame (8 to 11 a second while driving), no hitches over twice the average.

Was 0.32.4 worse? Its timings cost nothing with `ShowCoords` off. With it on, the overlay was four lines longer and laid out on every
OnGUI call (several a frame), which costs. The drive was also on a clear day (the far view and the painted land to the horizon), where
0.32.1's was in rain and fog. Like for like, read the frame rate with `ShowCoords` off (the Steam overlay's counter).

## The owner's 0.32.5 drive and 0.32.6 (2026-11-24): "back to 135 fps"

- 134 to 139 fps (7.2 to 7.5 ms) driving with `ShowCoords` on, against the frame cap of about 140: the frame now waits 1.3 to 1.5 ms
  for the screen (`WaitForLastPresentation`), where it waited 0.1 to 0.6.
- `FinishFrameRendering` **1.4 to 1.5 ms (3.4 to 5.9 on 0.32.4)**, yet the draw calls only fell to 1,400 to 2,750 (from 2,600 to 3,100)
  and the shadow casters are about as many (58 to 193). `IMGUISendQueuedEvents` 0.02 to 0.04 (0.06 to 0.66), `ClearImmediateRenderers`
  0.02 (0.11 to 0.13). So **most of the gain was the overlay**: IMGUI repaints inside Unity's rendering step, and the long overlay laid
  out on every OnGUI call cost about 4 ms. Every reading of the frame rate with `ShowCoords` on before 0.32.5 was low by that much
  (0.32.4's longer overlay the most); with it off the game never paid it. Foliage 0.5 to 0.6 ms (0.8), the trees in blocks.
- **0.32.6: the ground casts shadows again** (hills on the land behind them), asked for by the owner. The flat surfaces (verges,
  sidewalks, tracks, the courses' surfaces, water) stay without: their shadows fell only on themselves.
- Still not ours: `ScriptRunBehaviourUpdate` 2.4 to 2.5 ms of which ours about 0.3, and about 0.8 of `LateUpdate`.

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
