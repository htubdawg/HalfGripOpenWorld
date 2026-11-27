# Handoff

The one handoff: **overwrite it** at the end of each session with where things stand and what is next. Don't append history or dated
session notes; git keeps them (`git log -p docs/HANDOFF.md`). Standing decisions go in `docs/PLAN.md`, open in-game tests in
`docs/TESTING.md`, finished research in `docs/research/`.

Last written 2026-11-27.

## Where things stand

- **Branch: `traffic-rework-branch`** (the owner: stay on it). The traffic is being rebuilt from scratch: **read
  `docs/TRAFFIC_REWORK.md` first**. The old traffic is on `traffic-test-branch` (kept, not to be worked on).
- **Phase 0 is done**: the world from `traffic-test-branch` with the traffic simulation taken out; `Junctions.cs` and a fixed-time
  `TrafficSignals` from the race clock.
- **Phase 1, the lane graph and conflict areas, is done.** The full `meshcheck` passes, `lanes` included. Nothing in the game uses
  the graph yet (`Lanes.Ensure` is called only by the tools), so the game itself is unchanged by it; the world changes below do show.
  - `Lanes.cs`: 2,846 lanes (26 bays), 4,893 connectors; `Conflicts.cs`: 11,615 areas. Lines are the path of the car's middle from 7
    behind the stop line (see CLAUDE.md's table for the whole design).
  - **The fillet was never on until this session.** `Arc`'s solve of `Q0 + u h0 = P1 - v h1` had `v`'s sign wrong, so 9 of about
    4,100 turns took the fillet and the rest fell back to Béziers and arcs. Fixed: 3,056 of 3,080 turns are fillets now; meshcheck
    `lanes` fails if fewer than half are. A right turn's fillet cuts no more than `RightCut` (1.5) inside the lanes' corner (a wide
    arc at a shallow fork ran over the kerb's corner, node 16).
  - Node 6 (the old "cars stuck here"): the right off the Pinewick road (2>0) keeps 10.5 clear of the cars going on south (0>1).
- **World changes this session** (each found through the lane graph; all deterministic, so the world differs from the last build):
  - `SquareEnd` (TownPlan): a street is squared from 45 off the road's middle **and 35 past its edge**: on the trunk (40 to its
    edge) the curve fell right at the edge and the right turn in swung across the mouth (Glenford, node 280; the hold of 29.5 there
    is now 23.5 at most anywhere).
  - `Unhook`: an unhooked end eases into its point from 40 back instead of kinking up to 20° (Kestrel's main road, where the Harrow
    road meets it; a sliver of ground in that mouth). meshcheck `beside` now fails on a routed road's end kinking over 18° at one
    sample (4 mild ones of 12 to 15° remain, worst 15: lanes 30, 253, 259, 277, ends faired or squared; left alone).
  - `EndSnap` 30 → 40: a link meeting a farm's lane up to 40 from its end goes to that end (a stub at node 573; 50 lanes' worth of
    stubs and junctions joined up).
  - `Unbeside` looks at a second road at the end point when the first it hits isn't the one run alongside (Farm 88's yard: the link to
    Farm 104 ran back beside the yard lane; that was the map's worst hole, 268 pixels).
  - `UnpavedMouths` (new, after `Junctions`): a track's, path's or drive's end on another such ribbon takes that ribbon's slope across
    its width, fading over 24 (a footpath meeting a track climbing 1 in 10 at (-7226, 3381) had its edge 0.14 under the track's bed).
  - Totals: holes 2,178 → 1,345 pixels (worst 268 → 113, at (11145, -10111)); joints 126 → 129; hubs 61 → 64; country lane dead ends
    9 → 6; tracks 9 → 5 points off the bed (worst 0.76 → 0.32); ribbons worst 0.02.
- Tools: meshcheck names the failing checks on its last line (some checks' own lines don't say FAIL); `ribbons` names its worst
  sample's road and place; jview's `meet` mode works again (it broke when `Network.Meet()` was added).

## Next

1. **Phase 2 (population and schedules)**, as `docs/TRAFFIC_REWORK.md` §6: `Traffic.cs`'s households, cars and places carried over;
   activity chains per car from the race seed; a new `tools/trafficsim` that reports the demand against the surveys.
2. Optional world polish found on the way: the worst remaining hole at (11145, -10111) (node 1241, a crossing of farm lanes: `jview
   lanes` there showed two right turns brushing it before the fillet fix); the four mild end kinks above.

## Working with the owner

- The owner doesn't know the map's place or street names and hasn't memorised the map: never ask them to decide about a place by its
  name or node number. Make the call yourself (they said "figure it out yourself"), and when you do need them, show a picture.

## Checks

- `tools/check.sh` (HalfGripRefs' game, the stub HalfGripTools from `tools/halfgriptools-stub`): compiles clean.
- Full `meshcheck`: all checks pass.
- `preview`: repeatable; tile times 13.7 ms (town), 35.6 ms (country). Map looked at.
- Not run in game (nothing in the game uses the lane graph yet; the world changes are small road-end fixes).
