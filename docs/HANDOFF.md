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
    short for both boxes, at the lit end's drawn stop line). **Connector lines are the path of the car's middle**, starting 7 behind
    the stop line (`Lanes.Axle`: where the middle is when the front is at the line). Every connector first tries a **fillet**: along
    the lane in, round one arc tangent to both lanes (radius 3 or more), along the lane out (not where the lanes' lines meet far off);
    else a plain Bézier; if that bends tighter than radius 3, one arc of radius 4 or more joining the lane out further on, with a swing
    the other way first for a hairpin; else a grid of Béziers. Turns of more than 150° at a junction (into an acute fork) aren't made.
    A bay whose junction has no left turn (a trunk crossroads split into two nodes 17 apart) carries on into the inner lane.
  - **The owner (2026-11-26) on turning radii:** "a car can go around a 90 degree turn no problem, figure it out yourself". So the
    lines fit the drawn paving (fillets down to radius 3), not a real car's turning circle; no world change for it.
  - `Conflicts.cs`: 11,153 areas. Every pair of connectors at a node, or at two nodes whose boxes touch, swept as car bodies (14 by
    5.4 and 0.3 all round, centred on the line, along it) from four car lengths back along the lane in (no further than the whole car
    is on it) to the tail leaving; pairs chained through a lane are left out. Kinds: crossing, merging, diverging, head on (a drive is
    one car wide both ways). Who gives way: kept apart by the lights' phases (`TrafficSignals.Apart`), else a minor road to the main
    line, else a left turn to the others (PA 3322), else the right-hand rule (3321), else first come (all-way junctions, two
    junctions' boxes touching; ties to the driver on the right). `Lane.Hold`: how far back a car waits clear of every area where it
    gives way.
  - `meshcheck lanes` (`LANES_WHY=1` sorts the off-paving lines by kind); `jview lanes` (the graph over the meshes; name connectors to
    see their areas). A jview crash near drives (a track submesh past its colour table) is fixed.
- **Node 6 (the old "cars stuck here")**: the right off the Pinewick road (2>0) keeps to the junction's corner and its body stays
  10.5 units clear of the cars going on south (0>1). The check passes for a pair that has an area **or** keeps 2 apart.
- **Junk lanes taken out of the world (this session; a world change).** The owner on node 681: "what even is that road". Farm-to-farm
  links routed to a farm ran in beside the farm's own lane, a lane's width of grass between, to the farmyard; and the through-way pass
  then grew a 47 to 61-unit lane out of the farmyard back alongside the link, closing a sliver of grass. Now `Network.Unbeside` cuts a
  lane's end that runs in alongside the road it ends on back to where it first comes close and takes it square onto that road; a
  lane's through way is at least `ThroughShortest` (120) long, walked or routed. 12 fewer lanes; country lane dead ends 10 (the
  allowed most: three are farmyards just past where their links join). Also `Traffic.Driven` leaves out a road cut out whole (node
  22's buried street had lanes). New meshcheck check `beside` (it finds all 7 old cases on the old world); `jview beside` lists road
  ends close beside another road (what's left are staggered crossroads, fine).
- `meshcheck lanes` still **FAILS** on one count: **110 connectors touching off the paving** (799 of 514,938 points; 57 of them by 1
  to 3 points; was 151 and 1,779). Nodes 22, 574, 366 and 681 are clean now, and node 366's over-long hold went with them. The worst
  left: node 507 (7552, -10046), nodes 1191 and 13 (12081, -4170, two nodes 15 apart), nodes 172 and 197 near (11700, -10300).
- `tools/check.sh` compiles clean; the full `meshcheck` passes every check but `lanes`.

## Next

1. The off-paving connectors left: look at nodes 507, 1191/13, 172, 197 with `jview lanes` (world drawing or connector shape), then
   the long tail (mostly right turns on narrow lanes brushing the verge).
2. Finish phase 1: a repeatability check (the graph built twice the same); look at node 42 and the trunk's bays with `jview lanes`
   (`JVIEW_TEX=1`).
3. Phase 2 (population and schedules), as `docs/TRAFFIC_REWORK.md` §6.

## Checks

- `tools/check.sh`: compiles clean.
- `meshcheck lanes`: as above (FAIL on off-paving only).
- Full `meshcheck`: every check passes but `lanes`.
- Not run in game (nothing in the game uses the lane graph yet).
