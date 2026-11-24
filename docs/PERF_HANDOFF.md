# Performance handoff (after 0.32.1)

The owner, 2026-11-24, on 0.32.1: **frame rate down from about 135 to about 100** (7.4 to 10 ms a frame: about 2.6 ms more), and the
highway "barely" busier for it. 0.32.1 changed four things (see `docs/TRAFFIC_HANDOFF.md`, "0.32.1"): 100 cars circling the trunk
instead of 10, physics cars driven into the tunnels (a cheap check every frame), a water strip trimmed, the overlay's box. Only the
first can plausibly cost 2.6 ms. Nothing is changed yet: this is what the log shows, what is suspected, and how to find out.

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

## Suspects for the 2.6 ms, most likely first

1. **Physics cars on the highway.** Each is a full copy of the game's enemy car (`TrafficPhysics`, `MakeBody`: `CarControllerNew`,
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
