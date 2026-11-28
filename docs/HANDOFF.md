# Handoff

The one handoff: **overwrite it** at the end of each session with where things stand and what is next. Don't append history or dated
session notes; git keeps them (`git log -p docs/HANDOFF.md`). Standing decisions go in `docs/PLAN.md`, open in-game tests in
`docs/TESTING.md`, finished research in `docs/research/`.

Last written 2026-11-28.

## Where things stand

- **Branch: `traffic-rework-branch`** (the owner: stay on it). The traffic is being rebuilt from scratch: **read
  `docs/TRAFFIC_REWORK.md` first**. The old traffic is on `traffic-test-branch` (kept, not to be worked on).
- **Phases 0 to 4 are done** (the lane graph and conflict areas, each race's plans, the meso, the micro and the boundary; every fault
  check passing on seeds 1 to 64, trips taking the same time watched or not).
- **Phase 5 (in game, alone): the first part and the physics cars are built, neither run in the game yet** (TESTING.md items 70 and 71):
  - `TrafficLive.cs`: the race's plans, meso and micro built on a worker at the race's start hour, the first step run round the spawn,
    then stepped on the race clock on the main thread (`tm.TimeSinceRaceStart`, the lights' clock). Every car drawn between its last two
    steps' poses, a step (0.25 s) behind the clock. Stops at the day's end (03:00).
  - `TrafficCars.cs`: the drawn cars (the enemy car's meshes painted, lamps, the nearest six headlights at night, a kinematic box each);
    the nearest 240. `TrafficParking.cs`: parked cars in their spots; a car pulling out or in slid over 2.5 s (a stand-in).
  - `MicroBodies.cs`: the player's car is a body the micro's drivers see (they queue behind it, keep out of a box it blocks).
  - **Physics cars (new)**: `MicroPhysics.cs` (part of `Micro`, pure) and `TrafficPhysics.cs` (part of `TrafficCars`). The nearest 24
    drawn micro cars within 300 units (config `World.TrafficPhysics`) become the enemy car woken without AI or guns (the old traffic's
    warm pool, skin, lamps and tyre-mark guards carried over), let go past 400. Each stays its micro agent: it decides as every car does,
    but its X and V are read back from the body every micro step (its middle projected onto its lane or connector; back as well as on,
    never into the car behind; held at the stop line while it hasn't decided to go). The reading is stamped with the time the drawn cars
    are shown at (now less a meso step), so the micro sees the body where the player sees it beside them. The body is pedalled from the
    driver's acceleration (plus the hill and about a unit a second² of the game's rolling resistance) and steered by pure pursuit along
    `Micro.WayAhead` (the bicycle model on `carStats.wheelbase`). A body more than 14 off its line or turned over 60° from it for a
    second is **astray** and drives back to its line, keeping its place in its queue and its day (`Micro.RejoinAim`, `TrafficBody.Rejoin`:
    at 8 units a second towards a point its distance off further along its way, full lock when that point is well off to one side, backing
    round with the wheels the other way when it is behind, backing off something it has nosed into; the driver's braking still holds it).
    The owner's first test: a car pushed off the road made no attempt to get back, even pushed back on (it had left its day after 2.5 s).
    A body on its side 4 s, astray 30 s getting no nearer (3 units, a turn the wrong way counted as distance), or wrecked
    (`TrafficDamagePatch`: no kill) leaves the day (`Micro.LoseCar`, `Meso.Mode.Gone`) and lies as a loose car on its brakes, given to the
    micro as a body in the road (`TrafficLive.OtherBodies`), until the player is past sight (1,500). A loose car pushed back onto the road
    stays out of its day (bringing it back would mean giving it its plans again).
  - Fixed on the way: a lane change looked only at cars, not bodies (the player's car, a wreck): a through car moved right into a
    wreck's lane 120 short of it at highway speed (`BodiesLetChange`: none alongside, none ahead needing more than safe braking, and
    MOBIL's gain counts them); two cars the race's first picture put on one connector less than a car apart are spaced when their junction turns
    micro (`FlipIn`; the physics check found it at the spawn); `Micro.Events` cleared each frame in the game (a day's would pile up).
  - Debug: the overlay's `traffic:` line (now ending with `physics n/24 (made, finding their way back, loose, gone from their day)`),
    F11 follow car, the map; the log says `Traffic: car n knocked off its line, finding its way back`, `back on its line` and why a car
    left its day (wrecked, on its side, stuck off its line).
- **trafficsim's phase 5 checks** (`GameDay.cs`, `PhysicsDay.cs`; first seed, `TRAFFICSIM_GAME=only` for just them): the game's path
  frame by frame for 10 minutes; the player's car blocking a lane into node 6 (now put down once the spot is clear: it landed on a car on
  seeds 6 and 15 before); and the physics cars driven by stand-in bodies (acceleration 0.2 s late, drag, pure pursuit; `TRAFFICSIM_PHYS=
  lag,drag`): no body in another car, none straying, none standing for nothing, conservation, a shove off the road (25 out, turned across) finding
  its way back (about 14 s; or to its stop) with the shared `RejoinAim`, a body on its side leaving the day, a wreck in its lane sliding to
  a stop and nothing driving into it. Seeds 1 to 24 pass, and seeds 1 to 3 with a sluggish body (0.6 s, 1.5).
- What the stand-in can't tell: how the game's controller answers (grip, the springs, gears are not modelled: the engine force is set
  directly), whether the swap from drawn to physics shows (the body is put at the drawn pose 0.15 up until each model's rest height is
  learned), and the cost of 24 woken enemy cars.
- Only 1,904 households reach the main road graph. 110 places and the houses on the eight streets joined only by footpaths are left
  out (PLAN.md lists it).

## Next

1. **The owner ran items 70 and 71 once** (all fine but the one report: a car pushed off the road never tried to get back, now fixed,
   not yet seen in game). Next in game: push a car off the road and watch it come back (the game's controller backing up with negative
   `engineAccel` is untested), the rest of 70 and 71. Fix what they report first. If the physics cars drive badly, the knobs are in `TrafficBody.Steer` (look-ahead, gain) and `Pedals` (the rolling
   allowance); the log line `Traffic: physics car bodies: mass, engine, brakes, wheelbase` says what the controller has.
2. If the main thread's simulation shows in the frame time, run `TrafficLive.Advance` on a worker a step ahead (poses are already
   double-buffered; the bodies' readings and the player's body would be a step old).
3. Pulling in and out driven by the micro from the spot (the old `TrafficParking.Pull`/`Corner` are the drawing's model; a physics car
   would then drive into its spot); the day's end (wrap to the next day's plans at 03:00, or keep everyone home).
4. Parked cars as physics cars when the player is about to hit one (the old `TrafficLoose`), if the owner wants them pushable.
5. Phase 6, rooms (TRAFFIC_REWORK.md §4 Rooms): the host's meso, link events, per-car ownership of micro and physics cars.
6. Try seeds 65 to 96 of the full trafficsim now and then (each change so far showed a rare fault the earlier seeds hadn't).
7. Things to decide when they show: right on red (PLAN.md: measure again in the rework).
8. Optional world polish found earlier: the worst remaining hole at (11145, -10111) (node 1241, a crossing of farm lanes); four mild
   end kinks (lanes 30, 253, 259, 277).

## Working with the owner

- The owner doesn't know the map's place or street names and hasn't memorised the map: never ask them to decide about a place by its
  name or node number. Make the call yourself (they said "figure it out yourself"), and when you do need them, show a picture.

## Checks

- `tools/check.sh` (HalfGripRefs' game, the stub HalfGripTools built from `tools/halfgriptools-stub`): compiles clean (one old warning).
- `trafficsim`: seeds 1 to 32 (the default) pass, with the phase 5 checks on seed 1; `TRAFFICSIM_GAME=only` passes on seeds 1 to 24.
- `meshcheck`: not affected (it compiles none of the traffic's files).
- Tools look for `../halfgriprefs/game`; with the repo cloned as `HalfGripRefs`, a symlink `halfgriprefs -> HalfGripRefs` does it.
- Not run in game yet.
