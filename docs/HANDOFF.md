# Handoff

The one handoff: **overwrite it** at the end of each session with where things stand and what is next. Don't append history or dated
session notes; git keeps them (`git log -p docs/HANDOFF.md`). Standing decisions go in `docs/PLAN.md`, open in-game tests in
`docs/TESTING.md`, finished research in `docs/research/`.

Last written 2026-11-27.

## Where things stand

- **Branch: `traffic-rework-branch`** (the owner: stay on it). The traffic is being rebuilt from scratch: **read
  `docs/TRAFFIC_REWORK.md` first**. The old traffic is on `traffic-test-branch` (kept, not to be worked on).
- **Phases 0 to 4 are done** (the lane graph and conflict areas, each race's plans, the meso, the micro and the boundary; every fault
  check passing on seeds 1 to 64, trips taking the same time watched or not).
- **Phase 5 (in game, alone) has its first part built, not yet run in the game** (TESTING.md item 70 is what to try):
  - `TrafficLive.cs`: the race's plans, meso and micro built on a worker at the race's start hour, the first step run round the spawn
    (the junctions there turn micro before any car is drawn; done in sight, the micro placed a car 17 units off where the meso drew
    it), then stepped on the race clock on the main thread (`tm.TimeSinceRaceStart`, the same clock the lights are drawn from; the sky's
    debug skip isn't followed). Every car's pose after each meso step is kept with the one before and drawn between them, a step
    (0.25 s) behind the clock. The simulation stops at the day's end (03:00; every car is home by 02:30).
  - `TrafficCars.cs`: the old drawing carried over (the enemy car's bodies painted, lamps, the nearest six headlights at night, a
    kinematic box on the Enemy layer each, so a traffic car is a wall to hit for now); the nearest 240 (moving to 1,500 units, parked
    750), no shadows past 250. No instanced far drawing (the old one drew wrong and was off by default). The wheels stand at the springs'
    full length (the old rest-on-springs needed a woken physics body).
  - `TrafficParking.cs`: where parked cars stand (the old spots' arithmetic: drive pads, cottages, farmyards, the kerb on streets and
    secondary roads, the lots' bays), given out as the meso parks cars and back as they leave. A car pulling out or in is slid between
    its spot and the road over 2.5 s (a stand-in for driving it).
  - `MicroBodies.cs` (part of `Micro`): the player's car is a body the micro's drivers see: laid onto every lane and connector it covers,
    it is the car ahead for a driver whose way runs through it (at its speed along the lane), and a car keeps out of a junction box it
    blocks or whose way out it blocks. So a queue the player causes is real and persists.
  - Debug: the overlay's `traffic:` line (ShowCoords), the Follow traffic car key (F11, shown with ShowCoords; the car's day and where
    it is), `Debug.TrafficMap` (every car in the world on a map, queues red). Config `World.Traffic` is read again.
- **trafficsim's phase 5 checks** (`GameDay.cs`, first seed; `TRAFFICSIM_GAME=only` for just them): the game's path frame by frame
  for 10 minutes round a pretend player (poses, no jumps in sight, parked cars apart and off other roads, keeping up with the clock), and
  the player's car blocking the busiest lane into node 6 for two minutes (no car into it, a queue, cleared after). Seeds 1 to 8 pass.
  Headless cost: built in 1.2 to 2 s, the simulation about 2 ms a second of the race (half a millisecond a meso step, four a second),
  the worst frame under 15 ms. Mono in the game will be slower: measure.
- Only 1,904 households reach the main road graph. 110 places and the houses on the eight streets joined only by footpaths are left
  out (PLAN.md lists it).

## Next

1. **The owner runs TESTING.md item 70** (alone): the log's `Traffic:` line, cars on the roads, parked cars, stopping in a lane, the
   frame rate. Fix what they report first.
2. **Physics cars for the nearest** (phase 5's remaining big piece; the standing decision: every moving car near a player a physics
   car). A physics car is a micro agent whose position comes from physics: the driver model gives the throttle and steering, the
   agent's X is read back from the body (projected onto its lane), and a car pushed off its line or wrecked leaves the micro (a wreck
   is gone from its plans; its household sees it). The old `TrafficPhysics.cs`/`TrafficDrive.cs` on `traffic-test-branch` are the model
   for the body (the enemy car's prefab woken without AI or guns, the controller driven by code, rest on its springs) but not for the
   timetable logic (there is none now). Also `TrafficRest.cs` (the wheels at rest) needs a woken body.
3. If the main thread's simulation shows in the frame time, run `TrafficLive.Advance` on a worker a step ahead (poses are already
   double-buffered; the player's body would be a step old).
4. Pulling in and out driven by the micro from the spot (the old `TrafficParking.Pull`/`Corner` are the drawing's model); the day's end
   (wrap to the next day's plans at 03:00, or keep everyone home).
5. Try seeds 65 to 96 of the full trafficsim now and then (each change so far showed a rare fault the earlier seeds hadn't).
6. Things to decide when they show: right on red (PLAN.md: measure again in the rework).
7. Optional world polish found earlier: the worst remaining hole at (11145, -10111) (node 1241, a crossing of farm lanes); four mild
   end kinks (lanes 30, 253, 259, 277).

## Working with the owner

- The owner doesn't know the map's place or street names and hasn't memorised the map: never ask them to decide about a place by its
  name or node number. Make the call yourself (they said "figure it out yourself"), and when you do need them, show a picture.

## Checks

- `tools/check.sh` (HalfGripRefs' game, the stub HalfGripTools built from `tools/halfgriptools-stub`): compiles clean.
- `trafficsim`: seeds 1 to 32 (the default) pass, with the new phase 5 checks on seed 1; `TRAFFICSIM_GAME=only` passes on seeds 1 to 8.
- `meshcheck`: not affected (it compiles none of the traffic's files).
- Tools look for `../halfgriprefs/game`; with the repo cloned as `HalfGripRefs`, a symlink `halfgriprefs -> HalfGripRefs` does it.
- Not run in game yet.
