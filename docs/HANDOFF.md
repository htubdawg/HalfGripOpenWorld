# Handoff

The one handoff: **overwrite it** at the end of each session with where things stand and what is next. Don't append history or dated
session notes; git keeps them (`git log -p docs/HANDOFF.md`). Standing decisions go in `docs/PLAN.md`, open in-game tests in
`docs/TESTING.md`, finished research in `docs/research/`.

Last written 2026-11-26.

## Where things stand

- **Branch: `traffic-rework-branch`.** The traffic is being rebuilt from scratch: **read `docs/TRAFFIC_REWORK.md` first** (what went
  wrong, the research, the design, the phases, the owner's answers). The old traffic is on `traffic-test-branch` (kept, not to be
  worked on); `git show traffic-test-branch:<file>` to read any of it.
- **Phase 0 is done** (this branch's base). The tree is `traffic-test-branch`'s (which already held everything on `deer-test-branch`:
  the deer, 0.31.1's road pass) with the traffic simulation taken out:
  - Removed: `TrafficDay`, `TrafficDrive`, `TrafficLate`, `TrafficParking`, `TrafficCars`, `TrafficFar`, `TrafficPhysics`,
    `TrafficPop`, `TrafficRest`, `TrafficRoom`, `TrafficShared`, `tools/trafficview`, `tools/trafficsim`, the `Traffic*` config
    entries, the traffic's room messages in `EventLink`, its overlay line.
  - Kept: all the world work (the highway's grass median, left-turn bays and K-rail, Kingsbridge's widened road and bay, the wrap,
    `FramePerf`, `Plots`), `Traffic.cs` (the traffic's map, households and places: the base of phase 2), the lights' masts, heads,
    boards and stop lines.
  - New: `Junctions.cs` (each junction's arms and main line, taken out of `TrafficDay` so the lights stand without it).
    `TrafficSignals` runs a **fixed-time plan from the race clock** (`tm.TimeSinceRaceStart`, as the deer use) with an offset per light
    by integer hashing: same on every machine, no messages. Main 30 s, trunk arrow 10 s (protected), side 18 s, amber 3.5, all red 2;
    Kingsbridge's bay a permissive flashing yellow arrow. `SignalHeads` lays the masts out on a worker at the start.
  - meshcheck has a new `lights` check (moved from trafficview, rules unchanged: a mast for every way in, heads seen from the stop line,
    poles off the paving, stop lines, the signs' lit junctions the same as the lights').
- The version in `mod.json` is still 0.33.0 (the owner: versions aren't a concern for now).

## Next: phase 1, the lane graph and conflict areas

As `docs/TRAFFIC_REWORK.md` §4 and §6 say:
1. `Lanes.cs`: a lane per direction (two each way on the trunk, the bays from `TrunkLanes`/`TurnBays`), connectors through each
   junction as curves from the drawn surfaces (`Network.Point`/`Spans`, as `RoadMesh`), speed limits. Deterministic.
2. `Conflicts.cs`: every pair of connectors at a junction swept as car-wide corridors, conflict areas with entry and exit distances,
   kinds, priority from road class and the light (`Junctions.MainOf`, `TrafficSignals`), stop lines, keep-clear.
3. meshcheck: every connector on paving; every crossing pair given a conflict area, **node 6's Pinewick-road pair named** (the old
   traffic's "cars stuck here": arms 2>0 against 0>1 at about (-2312, -4305)); stop lines before the conflict areas.
4. A `jview` mode drawing connectors and conflict areas; look at node 6, node 42 and the trunk's bays.

## Checks run for phase 0

- `tools/check.sh` against HalfGripRefs' game and the stub HalfGripTools (`tools/halfgriptools-stub`): compiles clean.
- `tools/preview`: ok. `tools/meshcheck`: see the commit (the `lights` check included). `tools/jview` builds.
- Not run in game.
