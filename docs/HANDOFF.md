# Handoff

The one handoff: **overwrite it** at the end of each session with where things stand and what is next. Don't append history or dated
session notes; git keeps them (`git log -p docs/HANDOFF.md`). Standing decisions go in `docs/PLAN.md`, open in-game tests in
`docs/TESTING.md`, finished research in `docs/research/`.

Last written 2026-11-26.

## Where things stand

- **Branch: `traffic-rework-branch`** (the owner: stay on it). The traffic is being rebuilt from scratch: **read
  `docs/TRAFFIC_REWORK.md` first**. The old traffic is on `traffic-test-branch` (kept, not to be worked on).
- **Phase 0 is done**: the world from `traffic-test-branch` with the traffic simulation taken out; `Junctions.cs` and a fixed-time
  `TrafficSignals` from the race clock (see `git log -p docs/HANDOFF.md` for the details).
- **Phase 1, the lane graph and conflict areas, is in progress.** Nothing in the game uses it yet (`Lanes.Ensure` is called only by
  the tools), so the game is unchanged.
  - `Lanes.cs`: 2,978 lanes (28 bays: the trunk's from `TrunkLanes`, Kingsbridge's from `TurnBays`), 5,133 connectors, the wrap
    joining the trunk's lanes (a `Jump` connector of no length). A lane ends at its junction's box, the stop line (on a stretch too
    short for both boxes, at the lit end's drawn stop line). **Connector lines are the path of a car's rear axle**, starting 11 behind
    the stop line (where the rear axle is when the front is at the line), so a car's body lies along the line at the rear axle and its
    front swings wide on a bend, as a real car's does. A plain Bézier where it bends no tighter than radius 7; else a fillet (one arc
    tangent to both lanes, radius 7 or more); else one arc of radius 14 or more (a family car on full lock) joining the lane out
    further on (`Connector.Join`), with a swing the other way first for a hairpin; else a grid of Béziers. Turns of more than 150° at
    a junction (into an acute fork) aren't made. A bay whose junction has no left turn (a trunk crossroads split into two nodes 17
    apart) carries on into the inner lane. Builds in about 0.5 s.
  - `Conflicts.cs`: 11,269 areas. Every pair of connectors at a node, or at two nodes whose boxes touch, swept as rigid car bodies
    (14 by 5.4 and 0.3 all round) from two car lengths back along the lane in (no further than the whole car is on it) to the tail
    leaving; pairs chained through a lane (one after the other) are left out. Kinds: crossing, merging, diverging, head on (a drive is
    one car wide both ways). Who gives way: kept apart by the lights' phases (`TrafficSignals.Apart`), else a minor road to the main
    line, else a left turn to the others (PA 3322), else the right-hand rule (3321), else first come (all-way junctions, two
    junctions' boxes touching; ties to the driver on the right). `Lane.Hold`: how far back a car waits clear of every area where it
    gives way.
  - `meshcheck lanes` (`LANES_WHY=1` sorts the off-paving lines by kind); `jview lanes` (the graph over the meshes; name connectors to
    see their areas). A jview crash near drives (a track submesh past its colour table) is fixed.
- **Node 6 (the old "cars stuck here")**: with lines a real car can drive, the right off the Pinewick road (2>0) keeps to the
  junction's north-east corner and its body stays 9.5 units clear of the cars going on south (0>1). The old meeting came from the old
  traffic's paths running through the node's middle and its physics cars swinging wide off them. The check passes for a pair that has
  an area **or** keeps 2 apart, and prints which.
- `meshcheck lanes` still **FAILS** on two counts (the rest pass: every lane has a way on and is reached, every pair of crossing lines
  has an area, node 6, lit lanes end on the drawn stop lines):
  1. **1,210 connectors partly off the paving** (11,384 of 623,127 points, the line and 2.2 either side). Mostly right turns at tight
     corners: lanes into drives (the drive's mouth is verge, not ribbon), street and lane corners sharper than a right angle, where a
     line a car can drive cuts the kerb. **The world's junction corners are tighter than a real car's turning circle** (a street's
     mouth flares a few units; a real corner is about 6 m round). The radius 7 fillet already allows tighter than a real car (14).
  2. **27 lanes held back more than two car lengths** (449 held back at all, most by less): on narrow lanes and streets a car turning
     in swings over the stop line of the road it turns into, so the car waiting there must stand back.
  Both come back to how the junction corners are drawn: a question for the owner (below).
- `tools/check.sh` compiles clean (HalfGripRefs' game and the stub HalfGripTools). Every other meshcheck check passes.

## Next

1. **Ask the owner** about the junction corners: widen the drawn corners at junctions (a kerb radius near a real one, say 15 to 20
   units, and drive mouths flared), so cars can turn on the paving? Or accept lines that cut the kerb where a corner is too tight? Or
   allow tighter lines (cars turning tighter than real)?
2. Then finish phase 1: off-paving and holds to zero (or per the answer), a repeatability check (the graph built twice the same),
   and look at node 42 and the trunk's bays with `jview lanes` (`JVIEW_TEX=1`).
3. Phase 2 (population and schedules), as `docs/TRAFFIC_REWORK.md` §6.

## Checks

- `tools/check.sh`: compiles clean.
- `meshcheck lanes`: as above (FAIL on off-paving and holds).
- Full `meshcheck`: every check passes but `lanes`.
- Not run in game (nothing in the game uses the lane graph yet).
