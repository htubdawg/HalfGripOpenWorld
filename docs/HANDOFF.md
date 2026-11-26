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
- **Phase 1, the lane graph and conflict areas, is nearly done.** Nothing in the game uses it yet (`Lanes.Ensure` is called only by
  the tools), so the game is unchanged by it.
  - `Lanes.cs`: 2,898 lanes (26 bays: the trunk's from `TrunkLanes`, Kingsbridge's from `TurnBays`), 4,970 connectors, the wrap
    joining the trunk's lanes (a `Jump` connector of no length). A lane ends at its junction's box, the stop line (on a stretch too
    short for both boxes, at the lit end's drawn stop line). **Connector lines are the path of the car's middle**, starting 7 behind
    the stop line (`Lanes.Axle`). Every connector first tries a **fillet** (radius 3 or more, along the lanes in and out); else a plain
    Bézier; if that bends tighter than radius 3, one arc of radius 4 or more joining the lane out further on, with a swing the other
    way first for a hairpin; else a grid of Béziers. No turns over 150°. A bay whose junction has no left turn carries on into the
    inner lane. **The owner on turning radii:** "a car can go around a 90 degree turn no problem": lines fit the drawn paving.
  - `Conflicts.cs`: 10,712 areas from swept car bodies (14 by 5.4, 0.3 all round), from four car lengths back to the tail leaving.
    Kinds crossing, merging, diverging, head on; who gives way from the lights' phases, then main line, PA 3322, 3321, first come.
    `Lane.Hold`: how far back a car waits clear of every area where it gives way.
  - `Lanes.Rebuild` (tools only); `meshcheck lanes` checks the graph comes out the same built twice (it does).
  - Looked at with `jview lanes` (`JVIEW_TEX=1`): node 6 (Kingsbridge's bay), old node 42 just north of it, a trunk crossing with its
    median bays (node 480): lines on the lanes, bays' turns starting in the bays.
- **Node 6 (the old "cars stuck here")**: the right off the Pinewick road (2>0) keeps 10.5 clear of the cars going on south (0>1).
- **The world's drawing fixed where the lane graph showed it wrong (this session; world changes, all in `Network`).** The owner on
  node 681: "what even is that road". Each fix has a meshcheck check (`beside`, tighter `holes`):
  - `Unbeside`: a lane's end running in alongside the road it ends on is cut back and taken square onto it (farm links beside the
    farm's lane to the yard). `ThroughShortest` (120) and `TurnsBack`: no through way that meets a road straight away or turns back
    beside its own lane (slivers, V's). `Traffic.Driven` leaves out a road cut out whole.
  - `Unhook`: a routed line that runs past its end and hooks back is cut before it heads away (Kestrel's main road; now a hub).
  - `Meet`: road ends up to 8 apart brought to one point before joints and hubs, the move eased over 40. `Drives` moves the road
    meeting a farm's lane onto the split sample.
  - `Segments.Tee` (in `Paving`, so every "drawn over" question): at a T the road carrying on is drawn over the one ending on it,
    whichever is bigger (a street ending on a lane cut the lane across its width). `Network.TeeOver` for jview.
  - `CornerTurn` (125°): two roads alone at a place join round the bend (L corners of streets, farmyards).
  - `Squared`: routed lanes meet the road they end on square (`SquareEnd`), where they can; `Faired`: a lane's or street's tail
    kinking over 45° within its mouth's reach is smoothed from 96 back.
  - `TryHub`: two ends and a road through make a hub; a road through near its own end left to `MakeHub`'s room test. Hubs 38 to 61.
  - Totals: meshcheck holes 10,307 pixels to 2,178 (worst 1,168 to 268); joints 96 to 126; lanes 228 to 221; country lane dead ends 9
    (at most 10); the lane graph's points off the paving 1,779 to 40.
- `meshcheck lanes` still **FAILS** on two small counts:
  1. **20 connectors touching off the paving** (40 of 500,187 points; 17 of them by 1 to 3 points). The worst: node 16 (-12072,
     -5846) a right turn at a fork (7), node 89 (-2302, -2982) two streets' mouths side by side (7), node 570 (-7272, 1156) Farm 88's
     yard (5: a hub that has to leave one lane out, the yard's stub too short for its cut).
  2. **1 lane held back too far**: lane 1103 into node 280 (5743, -9552), Glenford: a street meeting the trunk at about 60°; the right
     turn in from the trunk turns about 120° and swings across the mouth of the street's other lane, so the car coming out waits 29.5
     back (two car lengths are 28). Decide: square the street onto the trunk, or let acute mouths hold three cars.
- `tools/check.sh` compiles clean; the full `meshcheck` passes every check but `lanes`; `preview` repeatable.
- `LANES_WHY=all` lists every off-paving connector; `jview beside` lists road ends close beside another road.

## Next

1. The two small `lanes` failures above (then phase 1 is done).
2. Phase 2 (population and schedules), as `docs/TRAFFIC_REWORK.md` §6: `Traffic.cs`'s households, cars and places carried over;
   activity chains per car from the race seed; a new `tools/trafficsim` that reports the demand against the surveys.

## Checks

- `tools/check.sh`: compiles clean.
- `meshcheck lanes`: as above (FAIL: 20 connectors brushing off the paving, one hold).
- Full `meshcheck`: every check passes but `lanes`.
- Not run in game (nothing in the game uses the lane graph yet).
