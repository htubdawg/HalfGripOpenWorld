# Handoff

The one handoff: **overwrite it** at the end of each session with where things stand and what is next. Don't append history or dated
session notes; git keeps them (`git log -p docs/HANDOFF.md`). Standing decisions go in `docs/PLAN.md`, open in-game tests in
`docs/TESTING.md`, finished research in `docs/research/`.

Last written 2026-11-27.

## Where things stand

- **Branch: `traffic-rework-branch`** (the owner: stay on it). The traffic is being rebuilt from scratch: **read
  `docs/TRAFFIC_REWORK.md` first**. The old traffic is on `traffic-test-branch` (kept, not to be worked on).
- **Phases 0 to 2 are done**: the world with the old traffic taken out, `Junctions.cs` and fixed-time `TrafficSignals`; the lane graph
  (`Lanes.cs`) and conflict areas (`Conflicts.cs`); each race's plans (`Schedules.cs`).
- **Phase 3, the meso headless, is done** (`Meso.cs`, run by `tools/trafficsim`). Nothing in the game calls it yet.
  - How it works: `docs/TRAFFIC_REWORK.md` §4, "As built (phase 3)". Lanes are the queues; a car waits in the lane its next connector
    leaves from; 2 s headway; storage at 16.5 units a car; junctions admit on the light and the conflict areas (cars already through,
    cars given way to that are coming); routes on the lanes' live delays through the wrap; a full lot sends a car to the nearest place
    for the same purpose with room.
  - Two things the first runs showed, both fixed:
    - A **standoff at two junctions whose boxes touch** (nodes 42/44/47, and 26/30): each car gave way to another one waiting, round
      a loop, until 300 s. Now, between two cars both held at their lines, the one held longer goes. The check against cars already
      in the junction still keeps them apart.
    - **Long late tours came home after 03:00**: two 10-minute drives across the map, planned to 02:15 on free roads, lost a couple of
      minutes at the lights. `Schedules` now keeps a tour only if its car is home by 02:30 with half its driving again to spare
      (`HeldUp`). Seed 1 keeps 658 tours (was about 577) and still plans 100% of the survey's driving.
  - Seed 1 (weekday), simulated from 03:00: all 1,364 trips made, 6 cars sent on from a full lot, none rerouted, everyone home. On
    average a trip loses **16.7 s** to the lights and junctions over its free time (worst 140 s). Peak 221 cars on the road at 16:38,
    plus 100 through. The hours still match the surveys (cars moving within 2.5 points of TPG 9, trips within 3.4 of NT-3). The worst
    queue is 53 s at a light on a primary road (node 233). The simulated day's counts are close to the plan's, except the trunk: its
    count is about two thirds of the plan's (15,300 a day against 22,300), because the through cars lose time at the lights and so
    make fewer laps. The whole day
    runs in about 0.4 s, half of it routing. From the race's start hour (13:07) it also passes.
  - 16 seeds: all checks pass (conservation every 10 s, no deadlock, every leg routed, every trip made, everyone home, repeatable).
- Only 1,904 households reach the main road graph. 110 places and the houses on the eight streets joined only by footpaths are left
  out (PLAN.md lists it).

## Next

1. **Phase 4 (micro and the boundary, headless)**, as `docs/TRAFFIC_REWORK.md` §4 and §6: `Driver.cs` (IDM following, gap acceptance
   at the conflict areas, keep-clear, lights, MOBIL on the trunk), micro areas round pretend players in trafficsim, the boundary (a car
   leaving goes into its lane's queue at its real position and time; one coming in is placed where the meso has it, behind the queue's
   last car). `Meso.Where` already gives a car's lane and distance (free speed, stopped behind the standing queue). Measure each lane's
   time and capacity in micro against meso and set meso's figures from that (`Headway`, `Jam`, the 0.75 of a connector's limit). Then
   all of §5's fault checks on 16 seeds.
2. Things to decide in the meso when micro shows them:
   - A car pulling out of a drive only waits for room on the lane, not for a gap in the moving cars.
   - Only the head of the other lane is counted when giving way, not the cars behind it.
   - Right on red (PLAN.md: "measure again in the rework"): try it in the meso and keep it only if the delay per trip drops.
   - The day's end: cars are planned home by 02:30. In the game the day wraps (the next day's plans from 03:00), so the meso must run
     on into it (phase 5).
3. Optional world polish found earlier: the worst remaining hole at (11145, -10111) (node 1241, a crossing of farm lanes); four mild
   end kinks (lanes 30, 253, 259, 277).

## Working with the owner

- The owner doesn't know the map's place or street names and hasn't memorised the map: never ask them to decide about a place by its
  name or node number. Make the call yourself (they said "figure it out yourself"), and when you do need them, show a picture.

## Checks

- `tools/check.sh` (HalfGripRefs' game, the stub HalfGripTools from `tools/halfgriptools-stub`): compiles clean.
- `trafficsim` (16 seeds): all checks pass.
- Full `meshcheck`: all checks pass.
- Not run in game (nothing in the game uses the lane graph, the plans or the meso yet; the world is unchanged this session).
