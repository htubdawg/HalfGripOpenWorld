# Handoff

The one handoff: **overwrite it** at the end of each session with where things stand and what is next. Don't append history or dated
session notes; git keeps them (`git log -p docs/HANDOFF.md`). Standing decisions go in `docs/PLAN.md`, open in-game tests in
`docs/TESTING.md`, finished research in `docs/research/`.

Last written 2026-11-27.

## Where things stand

- **Branch: `traffic-rework-branch`** (the owner: stay on it). The traffic is being rebuilt from scratch: **read
  `docs/TRAFFIC_REWORK.md` first**. The old traffic is on `traffic-test-branch` (kept, not to be worked on).
- **Phases 0 to 3 are done**: the world with the old traffic taken out, `Junctions.cs` and fixed-time `TrafficSignals`; the lane graph
  (`Lanes.cs`) and conflict areas (`Conflicts.cs`); each race's plans (`Schedules.cs`); the meso (`Meso.cs`).
- **Phase 4 is done**: `Micro.cs`, `Driver.cs`; every fault check passes on seeds 1 to 64 (`trafficsim` runs 1 to 32 by default;
  `TRAFFICSIM_EVERYWHERE=0 dotnet run -c Release --project tools/trafficsim -- 33 32` for the rest). Nothing in the game calls the
  traffic yet.
- **Consistency is settled** (seed 1, every junction micro against the meso alone): the same trips' times paired, micro 3.7 s quicker on
  average, median 1.3 s, on trips of several minutes; 72% within 10 s (lights met at other points of their cycle, routes chosen on other
  delays). The lane-by-lane table the last handoff tuned against misleads at junctions close together (the two levels book the same
  wait to different lanes), so trafficsim now pairs the trips. Its first findings were faults, not tuning, all fixed this session:
  - **Plans routed on the lane graph** (`Meso.PlanRoute`): the traffic's map routed one leg in nine back up an acute fork (node 7) the
    lanes have no turn at, and timed it for that road; the meso drove twice as far and cars came home after the day's end (seeds 47
    and 55, failing before this session). Plans now go through the wrap's tunnels too.
  - **Missed lanes** (9 on seed 1 with every junction micro, up to 71 on others; trafficsim now fails on any): cars took a connector
    landing in the wrong lane for a turn a junction or two on, with no room between to change. The micro chooses its lane ahead
    (`Pick`/`CanFollow`), the meso settles each route lane by lane (`Settle`), a turn onto a stretch too short to change on goes into
    every lane (`Lanes`), a car eases off to drop in behind one running alongside, a car parked short of a junction pulls out into the
    left-turn bay beside it.
  - **Give-way** looked for in the meso back through every short lane to 600 (as the micro does), and past an empty lane in; the zip
    courtesy only from the next car in the stream; a committed car counts as in its box when merging cars are ordered.
  - **Drives** (one car wide) take one car at a time. A junction group turning micro keeps its cars in queue order.
  - **Amber**: a meso queue still pulling away lets through the cars that couldn't stop comfortably (barely changes anything).
- Only 1,904 households reach the main road graph. 110 places and the houses on the eight streets joined only by footpaths are left
  out (PLAN.md lists it).

## Next

1. **Phase 5, in game, single player** (`docs/TRAFFIC_REWORK.md` §6): far cars drawn from the meso, near ones micro, the nearest
   physics cars; parked cars from the lots' state; the map overlay and the follow-car key; the frame cost against the 130 to 140 fps
   goal. The old game side on `traffic-test-branch` (`TrafficCars`, `TrafficFar`, `TrafficRest`, `TrafficPhysics`, the pool and paint)
   is the model to carry over; read it before writing anything.
2. Still open from phase 4 (do alongside phase 5 if they block it): a pretend player blocking a road (persistence: its queue still there
   on return, which needs a meso lane to know about a block); a physics car stood in by the driver.
3. Try seeds 65 to 96 now and then: each change so far showed a rare fault or two the earlier seeds hadn't. The plans now take about
   1.3 s a seed to build (was 0.45 s; the lane graph's router is slower than the traffic map's). In game they're built on a worker at
   the race's start; if that shows, cache the routes by their two ends.
4. Things to decide when they show: right on red (PLAN.md: measure again in the rework); the day's end (cars are planned home by 02:30,
   in the game the day wraps, phase 5).
5. Optional world polish found earlier: the worst remaining hole at (11145, -10111) (node 1241, a crossing of farm lanes); four mild
   end kinks (lanes 30, 253, 259, 277).

## Working with the owner

- The owner doesn't know the map's place or street names and hasn't memorised the map: never ask them to decide about a place by its
  name or node number. Make the call yourself (they said "figure it out yourself"), and when you do need them, show a picture.

## Checks

- `tools/check.sh` (HalfGripRefs' game, the stub HalfGripTools built from `tools/halfgriptools-stub`): compiles clean.
- `trafficsim`: seeds 1 to 32 (the default) and 33 to 64 all pass.
- `meshcheck`: all checks pass (the lane graph gained connectors: turns onto short stretches into every lane).
- Tools look for `../halfgriprefs/game`; with the repo cloned as `HalfGripRefs`, a symlink `halfgriprefs -> HalfGripRefs` does it.
- Not run in game (nothing in the game uses the lane graph, the plans, the meso or the micro yet).
