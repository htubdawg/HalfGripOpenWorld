# Traffic: handoff

## READ THIS FIRST: the doubled lanes and the 16 seeds' leftovers (2026-11-25, `traffic-test-branch`)

Worked through "NEXT" below, items 1 to 3. Still 0.33.0, not tried in the game. **The world changed** (four farm lanes): everyone
in a room needs this build.

**What was wrong, and the fix:**
1. **Lanes laid on top of lanes (the stop lines on another road's paving).** `Network.Throughs` routes a lane's through way from its
   dead end with `Build(..., fromStart: true)`, which builds the route's first cell as a new stretch though it is on a road. Where the
   route went straight back along the lane itself (its second cell on the lane too), that one-cell stretch snapped its far end back
   onto the same lane, and a lane ("Farm N lane on") was laid along the first one for 80 to 200 units: Farm 18, 59, 82, 103 and 114.
   Now nothing is built there (`Build`: the snapped end on the road the through way starts from, `EndsAt`). Those lanes are honest
   dead ends now, so four of them became farm drives (the later pass turns dead-end farm lanes into drives) and Farm 59's stays a
   lane (backroads: 6 lane dead ends in the country, was 4; at most 10). **meshcheck `alongside`** (new): no paved road lying along
   another (its centre within the wider one's half width for 40 units, the same way); it finds all five on the old world.
2. **A deer fled over a lane 40 units from its crossing** (meshcheck `deer`, turned up by the world change moving the groups):
   `Reactions` took the deer's side from the road's line (`DeerSide`), which past the lane's end (it carries on as the hill climb)
   clamps and put a deer standing at its own side's spot on the other side; it ran straight to a spot over the lane. Now a deer at
   one of its group's spots takes that spot's side (`SideAt`).
3. **Over the speed limit (7 seeds after the lane fix):** the profile is sampled every 10 units and the sample before a lower limit
   took the faster road's, so braking back from the next sample let a car reach the boundary at up to sqrt(v² + 2·Brake·10): 13%
   over a farm drive's 10 mph that now carries straight on from a 35 mph lane. `TrafficDay.Profile`: a sample takes the lowest limit
   starting before the next one. trafficview's notes list the first steps over (`speeding:`).
4. **Physics cars stopped for minutes behind a drawn car standing for nothing** (the leftover named below): a late car replays a stop
   the day made in a queue that is drawn elsewhere now; an early car (a physics car back early near its trip's end) is drawn where
   the day will have it, standing in a queue the day only builds later (seed 9: six physics cars behind one 45 s early).
   `TrafficLate.SkipStand`: a late or early drawn car standing with nothing there to stand for (`TrafficDrive.NothingToStandFor`:
   its lane clear for 40 ahead, its light green, nobody in or coming into the box it is at) has the rest of that stand cut from its
   lateness; it stands still all the while, so no jump. In the game `TrafficPhysics.LetStandsGo` after the movers are built (only
   with physics cars about), sent to the room as a ledger change (`TrafficShared.SendLate`). trafficview does the same each step
   ("late drawn cars let off a stand": 41 to 133 a run).
5. **The check "physics cars stopped over 90 s" changed:** it fails only a physics car that stood over 90 s **and 30 s longer than the
   day had the same car stand** over the run. After the fixes, every one left (seeds 8 and 9) stood as long as the day's own car
   (557 s against 558; 371 against 371; ...): Kingsbridge's junctions (nodes 6, 42, 43, 49) queue for minutes at the 5 pm peak on
   some seeds, and a physics car in that queue stands where the player sees why. They are reported ("in the day's own queue"). The
   long-stop notes give the day's car's stop beside each, and a car held by a car pulling in or out names it.

6. **The ledger's clock across the day's wrap** (`TrafficLate.Since`): it took an entry read more than half a day (1,200 s of sim)
   after it was made as read before it. trafficview's early entries check on seed 9 had a car 1,193 s early (the day's own car stood
   870 s on Kingsbridge street at the link, 17:40 to 02:30) read at its trip's end 1,207 s on: drawn driving again instead of back on
   the day. Now up to three quarters of a day on, a quarter back (entries are read after they are made; a room's clocks are seconds
   apart). The check's note names the first car not back on the day.
7. **New report (not failed): cars standing in one place on a trip** (trafficview, the day): the longest stand past pulling out and
   short of pulling in, and the trips with one over 300 and 600 s. Not new with these changes: the old code had 955 s on seed 1 (now
   570) and 785 on seed 9 (now 900). They are all round Kingsbridge (-2400 to -2500, -4100 to -5050) in the afternoon and evening.

**16 seeds (`TV_DRIVE_ONLY=1`, seeds 1 to 15 and 20261122): all 16 pass** (before: all 16 failed, on the stop lines at least).
Junction waiting 294 to 555 min a day; stop lines on another road's paving 0 of 2236 arms; over the limit 0; physics cars in each
other 0 to 2 (at most 6), crossing in a box 0 to 3 (at most 3); physics cars stopped over 90 s and 30 s longer than the day's car 0
on every seed (1 on seed 8 and 6 on seed 9 in the day's own queue); late drawn cars let off a stand 7 to 133 a run. The full
trafficview (parking, the ledger, contact, early entries) on seeds 1, 7, 9 and 13: all pass. (The 16 ran before item 6, which can
only change an entry read over 600 s after it was made; the full runs were after it.)

**Checks run:** `tools/check.sh` passes (stub HalfGripTools.dll); meshcheck all of it (passes, `alongside` and `deer` included);
preview (repeatable; 6 country lanes' dead ends); trafficview as above; trafficsim (639 checks pass). deersim and eventsim not run
(no change to `DeerRoom` or `EventRoom`).

**NEXT, in this order:**
1. **The day's own peak queues round Kingsbridge** (nodes 6, 42, 43, 49; seeds 8 and 9 show physics cars in them for up to 9 minutes;
   the new "standing in one place" report: a car stood 900 s on Kingsbridge street at the link on seed 9, 26 trips over 300 s):
   find what holds the head of each (`TV_DRIVE_NOTES=1`, the day's `held:` and junction waiting by edge), as was done for the
   Westhaven link before the lights. Town turn lanes or a light at node 43 may be what it needs (the owner decides on new lights).
2. Physics cars "in each other" are within the limit (at most 3 of 6) but not 0: seed 9's pairs are a physics car at the very start
   of its leg (P 0 to 64) with a drawn car; look at them with the notes.
3. The physics car that brushed the player's stopped car (seed 11, run 9 of the last handoff): not looked at.
4. Then the owner's test in the game of this branch (the lights' logic, the physics cars parking early, late cars let off a stand).

## READ THIS FIRST: traffic lights (2026-11-25, `traffic-test-branch`)

The owner: "we have no traffic control at all ... just code it and we will put the lights in later." Built on `traffic-test-branch`
(which now holds everything of `ccr-02e633e5-fytf3y` below). Not tried in the game; the version is still 0.33.0.

- **Where the jam was:** the towns' junction waiting was nearly all "no room": the Westhaven link queued back from node 5, its junction
  with the trunk, where side cars could not find the manual's gaps in the trunk's traffic. Town turn lanes would not have fixed that.
- **`TrafficSignals.cs`:** a light at every junction with a main line that is a primary road or the trunk and a side arm a secondary or
  bigger (16 on seed 1, the trunk's junctions with the links among them). Actuated: the main line green until a side car waits within
  70 of its line (22 s at least), the side green 7 to 28 s while its cars keep coming (2.5 s with none ends it), amber 3.5 s, 2 s all
  red. Left turns on green are permissive. `Yields` is false between arms on different greens.
- **The day runs the lights** (`TrafficDay.RunLights`, before `Junctions`) and records each change; `TrafficDay.Lights.State(node,
  arm, w)` reads a light at any time. A car is refused at red, and at amber if it could stop at `AmberBrake`.
- **Physics cars** (`TrafficDrive.Target`, now given the day's clock): hold at red as the day's cars do. The day's lights only see the
  day's cars, so a physics car at a side road's red with no day car to call the green gives up after `LightGiveUp` (30 s) and goes as
  at a give way. They also no longer give way to a car standing at its line (a ring of queues round Kingsbridge's centre).
  `State.Held` says what holds a car at a junction.
- **16 seeds (`TV_DRIVE_ONLY=1`):** no trip unfinished (was up to 39); junction waiting 209 to 590 min a day (was 794 to 2,578); the
  trunk's through cars stopped 3 to 12 min a day. Physics cars stopped over 90 s: 0 on 12 seeds, 1 to 6 on seeds 3, 4, 9, 11 (10 to 24
  on every seed before).
- **Branch history, so nobody repeats it:** `traffic-test-branch` = 0.33.0, then the crossing fixes reverted and re-applied, then
  `ccr-02e633e5-fytf3y` merged in (its tree exactly), then the lights (commits `9f603a2` to `e394fd3`). Work only on
  `traffic-test-branch`; never a new branch. Look at every branch's latest commit (`git for-each-ref --sort=-committerdate
  refs/remotes`) before starting.
- **Checks run:** `tools/check.sh` passes (stub HalfGripTools.dll, see the bottom of this file). trafficview over 16 seeds with
  `TV_DRIVE_ONLY=1` (the full run, parking included, was not rerun after the lights). meshcheck, preview and the sims not rerun (no
  world, event, deer or room change).

### The failures fixed (2026-11-25, `traffic-test-branch`; still 0.33.0, not tried in the game)

The owner: fix the mess before anything else; do NOT draw the lights yet. Worked through the six failures listed in the last
handoff, in order. trafficview (`TV_DRIVE_ONLY=1`) over the 16 seeds (1 to 15, 20261122), before and after; the stop lines fail on all 16 both times (6 below) (reports in the scratchpad, gone: rerun):

| | before | after |
|---|---|---|
| cars on a lane closer than a car's length | 6 seeds | 0 |
| crossing cars in a box together (the day) | 2 seeds (1, 15) | 0 |
| stopped across a trunk carriageway | 0 | 1 (seed 4, see the last row) |
| physics cars "in each other" (at most 6) | 1 to 6 | 0 to 4 |
| physics cars crossing in a box (at most 3) | 0 to 5 (5 on seed 13) | 0 to 3 |
| physics cars stopped over 90 s | 1 to 6 on seeds 3, 4, 9, 11 | 3 on seed 2 (else 0) |
| trips unfinished | 0 | 0 |
| junction waiting, min a day | 209 to 590 | 222 to 600 |
| seeds failing only the stop lines | 3 | 14 (seed 2: physics stops; seed 4: one car stopped at the very edge of a trunk box, node 33, -47 of box 47, behind a car going through) |

**What was wrong, and the fix** (`TrafficDay` unless said):
1. **Pull-outs on top of a car coming:** the forced pull-out (after 300 s) needed only a car's length clear, and on a 25 mph street the
   3 s gap could be shorter than the car behind needs to stop. Now every pull-out (clear, let in or forced) needs the car behind able to
   stop (`CanStopBehind`: a second to see it, the least gap). `PullOutsForced` is still 40 to 90 a day on the busy seeds (after 300 s
   waiting): a sign the streets near Westhaven and Kingsbridge are full at the peak.
2. **Two cars into one lane:** a car merging out of a junction lane just past a node did not see a car still coming through the node
   into that lane (it is on the lane's list only once past the node): `ComingInto`. And the mirror: just short of a node, the car just
   past it in the lane carrying on (`AheadAcross`). Both in `RoomIn`.
3. **A car stalled in a box while a crossing car comes:** three causes.
   - The next junction's stop line inside this box (node 282 and 287 on the trunk, 82 apart, boxes 45): the car was let in, then
     waited in the box for the next one. `ThroughNext`: not let in unless it could go on through the next one (its light green, nobody
     let in there across its way, room past it; out of the trunk's box, a gap at the next one too). The 45 s guard still lets it go
     (two such junctions could wait on each other: seed 5 gridlocked when it could not, and when every car waited for a gap at the
     next one), but never into the trunk's box. Where either is a trunk junction the car is let in at both at once (`Chained`), so
     nobody let in at the next one later stands in its way while it is still in the trunk's box.
   - Two cars let into one lane out from different lanes in (the trunk's outer lane and its deceleration lane into one exit) saw
     each other only once both were on it: a car let into a junction now follows any car let in there into the same lane out that is
     nearer the node (`Acceleration`, binding "merge").
   - A car with the right of way let in across a car that could not get out (the car in front past the box crawling): `HeldIn` now
     limits the other car's clearing by the car in front of it, instead of its pull-away on a free road.
   - A car whose trip ends just past the node slows to park in the box: `HeldIn` counts it as there until it parks.
4. **Physics cars in each other:**
   - A drawn car pulling out of a space across the road swings over the oncoming lane: it is now held back while a physics car is
     coming in either lane (`TrafficDrive.AcrossItsWay`, in `TrafficPhysics.OnABody` and trafficview).
   - A drawn car pulling out or in is in a physics car's way only where it is drawn (with its nose and tail counted across the lane),
     not on the lane: counted on the lane, one held back in its space just past a junction kept the physics cars waiting for it, and it
     for them.
   - A physics car tired of waiting at a junction (`HoldBreak`) still lets a car about to get there (2.5 s) go first.
   - A physics car goes into a box only if the queue past it, up to the first car standing or crawling (under 4 u/s) within 120,
     has room for it behind the cars ahead of it (those short of the node too), its back out of the box. Its old test (a stopped car
     within a car's length and 4 past the box) let it follow a moving queue in and stop with its back in the box: on seed 9, 24 cars
     stood for minutes round Kingsbridge's ring of close junctions (nodes 42, 6, 44, 2), each queue held by a car in the next box.
5. **Physics cars stopped over 90 s:** the worst (seed 7: 23 cars, 7 minutes) was a physics car ahead of its timetable standing on the
   lane at its trip's end, waiting for the day's car (minutes behind in the day's queue) to catch up. Now it drives to its trip's end
   and goes back on the timetable early: the ledger takes a negative lateness and draws the car parked at its trip's end until the day
   has it there (`TrafficLate.At`), then back on the day. trafficview checks it ("early entries"). A merging car at the head of a
   standing queue in a median lane waited for ever (seed 11): a car standing behind it is no longer a bar. What is left is physics cars
   behind a drawn late car replaying a stop the day had it make in a queue (seed 4, node 2: car 649 drawn 268 s late, standing 26
   before its trip's end) while the cars it queued behind in the day are drawn elsewhere now: it stands for no reason the player can
   see. A late car standing with nothing in front of it could be let catch up (a ledger change): not done.
6. **Stop lines on another road's paving (7 arms, every seed): not fixed, a layout fault.** At those nodes (664, 672, 686, 1258) the
   arm is clear only at its far end (79 of 80, 91 of 92, 137 of 137, 128 of 128): country lanes laid along each other. jview at node
   1258 (7796, -10404) shows one road where four Lane arms meet. Fixing it is a world change (the lanes' layout in `Network`): the
   next job (below).

**trafficview's notes** (`TV_DRIVE_NOTES=1`): overlaps say how each car pulled out and what last moved it between lanes (with its
grants and lane changes); box conflicts say when each was let in, what was braking it (`Binding`) and why the next junction would not
let it through (`NextWhy`); a car stuck at the day's end says why it can't merge (`roomWhy`); each physics car stopped over 90 s says
what held it at the moment it had stood 90 s, the car in front and the car standing past the box (drawn: its lateness and leg).

**Checks run:** `tools/check.sh` passes (stub HalfGripTools.dll); trafficview on 16 seeds with `TV_DRIVE_ONLY=1`; the full trafficview
(parking, the ledger, contact, early entries) on seeds 1, 7, 9 and 13: all pass but the stop lines. meshcheck, preview and the sims not run (no world, event, deer or room change).

**NEXT, in this order (do not ask the owner whether to fix a bug: fix it):**
1. **Fix the overlapping lanes behind the stop-line failure** (item 6). Country lanes laid along each other at nodes 664 (-5751,
   12853), 672 (-12858, -10046), 686 (1729, 3957) and 1258 (7796, -10404): only one road shows where three or four Lane arms meet.
   Start with `jview near <x> <z> 160` and `jview at` on each to see which roads they are and where they run together, then find in
   `Network` (the grown lanes and tracks, the back roads joined up: links and through ways) what lays a lane on top of another, and
   stop it (merge them, or don't build the duplicate). It changes the world: everyone in a room needs the same build. Then run
   meshcheck (all of it: `clear`, `homes`, `backroads`, `joints`, `hubs`, `poke` at least), preview, and trafficview on the 16 seeds:
   "stop lines on another road's paving" must be 0. Add a check for it (meshcheck or preview: no two roads' centre lines within
   their half widths along a stretch).
2. **The leftovers of the 16-seed run** (run 11 above): seed 2's three physics cars stopped over 90 s at Kingsbridge's cluster
   (nodes 49 and 50: `TV_DRIVE_NOTES=1`, read the `at 90 s:` notes, the head of the queue first); seed 4's car stopped at the edge of
   node 33's box (-47 of 47) behind a car going through (the day stops it a hair inside the box, or the check counts the edge);
   late drawn cars replaying a stop the day made in a queue that is gone (seeds 4 and 12 in earlier runs): let a late car standing
   with nothing in front of it catch up (a ledger change); the physics car that brushed the player's stopped car (seed 11, run 9).
3. Rerun the 16 seeds after each change: every fix here moved the day and turned up a different single failure somewhere else.
   Keep going until all 16 pass.
4. Then: the owner's test in the game of this branch (the lights' logic, the physics cars parking early). Drawing the lights and town
   turn lanes come after; the owner decides when.

**How the runs were done:** build trafficview, copy `tools/trafficview/bin/Release/net8.0` to the scratchpad, and run the copy on
the 16 seeds 4 at a time in the background (`TV_DRIVE_ONLY=1 TV_DRIVE_NOTES=1 dotnet <copy>/trafficview.dll --seed N > sN.txt`),
about 20 minutes for all 16 on this container; one line per seed from each report: junction waiting, overlaps, box conflicts, stopped
across, physics pairs, physics box crossings, stops over 90 s, the FAIL count.

## READ THIS FIRST: right of way and the trunk's junction lanes (2026-11-24, branch `ccr-02e633e5-fytf3y`)

**Branch:** `ccr-02e633e5-fytf3y`. It holds 0.33.0 (`traffic-test-branch`), plus the unfinished crossing fixes of
`traffic-highway-crossing-fixes` (re-applied), plus this work. The version is still 0.33.0: bump it with the release. Nothing here
has been tried in the game. It changes the world (the trunk is wider) and the day, so everyone in a room needs the same build.

**Read first:** `docs/TRAFFIC_RESEARCH.md`. It compares the traffic with the **PA Driver's Manual (PUB 95, 4-21)**, quoted with page
numbers, and has "What was built" with the 16-seed numbers. The manual is at
https://www.pa.gov/content/dam/copapwp-pagov/en/penndot/documents/public/dvspubsforms/bdl/bdl-manuals/pa-drivers-manual-non-commercial/english/pub%2095.pdf.
The old `dot.state.pa.us` link fails (502). The fetch tool can't reach pa.gov; `curl` in Bash can. The owner wants the manual
followed: "go read the pa drivers handbook".

### The owner's decisions (2026-11-24)
- **No diamond interchanges** (a small rural area). Ramps along the road's side and left-turn lanes in the median instead. Built.
- **Following distance 2 s at most** ("most people are tailgating assholes"). Kept at 1.5 s (`TrafficDay.Headway`).
- **Left onto the trunk from a side road:** a median lane to wait in, so nobody is held up. Built (the median merge lane).
- **Close junctions:** the agent's call. One continuous auxiliary lane on the shoulder between them; median lanes stop at each
  junction.
- **No roundabouts, ever.**
- **Add turn lanes** on the towns' roads. This is the next job, not yet started.
- The owner asked whether traffic lights, or just their logic, would help. The answer is below. No decision yet: ask before
  building any.

### What is built
- **`TrunkLanes.cs` (new, pure):**
  - Finds every side road meeting the trunk (an end on it, or an at-grade crossing).
  - Lays out per junction:
    - a deceleration lane before it on the shoulder (520, side road's side);
    - an acceleration lane after it (680);
    - a median left-turn lane for the oncoming carriageway (620);
    - a median merge lane after it for the side road's lefts (680; none at crossroads).
  - Shoulder lanes that overlap join, through junctions too. Median lanes are cut at every junction (`Cut`), the two directions
    share the median where they meet (`Share`), and merge lanes shorter than 380 are dropped.
  - Built at the end of `Network.Ensure`. Every tool project that lists `Network.cs` now lists it too.
- **The trunk is 80 across** (`Network.Classes[0].Half` 40, was 33): its outer shoulders are a lane wide. Its texture is 80 columns
  (`RoadMesh.Trunk`). `RoadMesh.TrunkPaint` paints the junction lanes over it from its own columns: a dashed lane line, an edge line
  moving out over the taper, asphalt over the median. The hub plate's clear columns and `Courses` comment were updated.
- **The traffic (`TrafficDay`):**
  - `Mover.Ix` replaces `Inner`. The lanes are 0 outer, 1 inner, 2 aux (`AuxIx`), 3 median (`MedianIx`). Keys are `LaneKey(seg,
    ix)`; drawn offsets come from `LaneValue` (`TrunkLanes.AuxLane` -1, `MedianLane` 2.136 lane widths left of the outer).
  - `JunctionLanes` (run first in `LaneChanges`) moves turners into their lane, merges cars out of aux and median lanes, and handles
    missed turns.
  - `EntryIx` / `EnterTrunk`: side-road cars come straight into the aux or median lane. `Conflict` then sees no clash with the
    trunk's through cars (out lanes differ). `TrafficDrive` skips them the same way.
  - `TurnQueueTail`: a turner holds back behind its lane's last car, never level with it.
  - `zipper` / `Zip`: a slow queue lets a car in.
  - `Miss`: a car that can't reach its turn lane within 220 goes on and is re-routed, never back along the stretch (it tries each
    road off the next node). If the only way is back, a slowed left-turner in the outer lane may cross both lanes into the median.
  - `LaneEnd`: a merging car stops at its lane's end if no gap comes.
  - `TurnAt(box)`: turn speed through a junction scales with its size (13 to 22). Before, a left off the trunk took 9 s at 13 u/s,
    longer than the evenly spaced circling cars' 8.5 s gaps, and the median queue never moved.
- **Rules:**
  - Left turns yield to oncoming traffic everywhere (`MainYields`; `MainTurnsGiveWay` is now unused).
  - `SideGap` onto the trunk is the manual's 9 / 8 / 6 s (6 s for a two-stage left into the median). Elsewhere it is the HCM's
    7.1 / 6.2 / 6.5 s, halving over 10 s of waiting (`TownPatience`), and only for cars on the arms straight into the node (`block`
    in `TooSoon`).
  - Patience no longer shrinks the margin (`GiveWayLeast` = 1.0).
  - Driveways: an 8 s gap, and no let-in above 25 mph (`SlowRoad`).
  - `CrossesTraffic`: the strict room rule for any movement across another stream.
- **Physics cars:**
  - `HoldLaneAt` only holds lanes 0 and 1.
  - `LaneAt` glides from a held lane into a junction lane the day moves to (`ChangeStart`).
- **Tools:**
  - trafficview prints the junction-lane counts (into, merged, missed).
  - meshcheck prints where `clip` fails.
  - `Network`'s drive houses keep `Half + 10` clear of the trunk (one sat on the wider trunk).
  - jview's project leaves out `TrafficShared` and `TrafficFar` (it no longer built).

### Where it stands (16 seeds, `TV_DRIVE_ONLY=1`; reports were in the scratchpad, rerun them)
- **The trunk works:** its through cars are stopped 1 to 80 min a day on 14 seeds (135 on seed 1 on the branch). **Seeds 3 and 13
  still jam at Kingsbridge** (node 3, about -3950, -8950): 227 and 700 min, when Kingsbridge's inbound link backs up past the
  median turn lane and left-turners fill both through lanes.
- **The towns jam (every seed fails):**
  - Junction waiting is 1,000 to 2,700 min a day (284 to 928 on 0.33.0), worst in Westhaven: nodes 161, 180, 233, 186 on the
    Westhaven link and its side streets.
  - So: trips unfinished at the day's end on all 16 seeds; the physics cars' "over 90 s" stops in Westhaven on 15 seeds.
  - Experiments on seed 1: gaps under about 3.5 s don't bind. Any realistic gap (HCM or the manual) tips the towns over a cliff.
    Only the old ~1 s margin plus first-come-first-served left turns (0.33.0's rule-breaking) got back to 705 min. The links are
    two-lane roads at capacity, and the day's whole demand runs in about 40 real minutes.
- **Also failing:**
  - 7 stop lines on another road's paving (inherited from the branch);
  - a few "cars on a lane closer than a car's length" (8 seeds) and "crossing cars in a box" (6);
  - "stopped across a carriageway" (3 seeds).
  - Look at these after the towns: the zipper, missed turns and merges are the likely sources.
- **Checks:** `tools/check.sh` passes (stub HalfGripTools.dll: see "This session's environment" below). meshcheck passes; 3 `clip`
  samples within its tolerance at the trunk junction at (-12172, -6294).

### Next: turn lanes in the towns (the owner's call)
- **Centre turn lanes** (manual p. 45: "TURNING FROM OR INTO A CENTER TURN LANE"). The Westhaven link and the Kingsbridge link
  (Primary through town), and probably the secondaries through towns: a centre lane for left turns from both directions, and for
  side streets' lefts to finish in before merging.
- **The same machinery as the trunk's median lanes:** a `MedianIx`-like lane on a two-lane road, zones from the junctions, drawn
  paint, `JunctionLanes` moving turners in. Main-line through cars then never wait behind a left-turner, and side-street lefts go in
  two stages.
- **It needs the roads widened through town.** That is a world change: house plots, sidewalks, lots and parks all read
  `road.Half`. Run meshcheck's `clear`, `facades`, `homes`, `lots`, `sidewalks`.
- Likely also right-turn bays where a side street's queue is long.
- Then re-run the 16 seeds and fix the remaining fails. Kingsbridge on seeds 3 and 13 should ease once the town's link flows.

### Would traffic lights help? (the owner's question)
- **Yes, at the few junctions that saturate.** No gap rule can give a side street capacity once the main road's flow leaves too few
  gaps. A signal gives each approach a fixed share of green, about 1,800 cars an hour a lane while green. Real engineers signalise
  exactly where gap acceptance fails, by the MUTCD's volume warrants; the FHWA report notes signals once peak-hour main x left-turn
  volumes pass thresholds.
- **In the sim:** a signal is a timetable on a node. Each arm green, yellow or red from the race clock (deterministic, no
  messages). Grants only to green arms, and to protected or permissive lefts as the phase allows. It fits on top of
  `TrafficDay.Junctions` (a refusal reason) and `TrafficDrive.Target` (hold at red).
- **Logic alone, with no lights drawn, would be a mistake.** Main-road cars would stop at an invisible red, which reads as the bug the
  owner hates ("cars should not be stopping because another car wants to jump in front of it"). Draw the heads if signals go in.
  The manual's signal rules are Ch. 2, pp. 8 to 9.
- **The cost:** main-line cars do stop at red. That trades the owner's "main roads get priority" for flow.
- **Recommendation:** turn lanes first (asked for). Signals only at the 2 to 4 Westhaven link junctions still saturated after that,
  and never on the trunk. Ask the owner before building any.

---

For the agent picking up NPC traffic. Read `CLAUDE.md`, then in `docs/PLAN.md` the three traffic sections ("Traffic: the agreed plan",
"Traffic: what the studies say, and the trip model", and the older research under them), then this. Work on **`traffic-test-branch`**
(made from `deer-test-branch`; commit and push there; the owner merges).

## Start here

0. **Released as 0.32.0 (PR to `main`, 2026-11-23).** The owner tried contact physics: "looks like we got it" (the tyre marks, the
   parked cars' lamps and their wheels fixed on the way). **New agent: start with "Contact physics: as built" below** (the owner's
   three decisions still at their defaults: ask), then "The owner's report on the stage 4 build" (what the owner saw, the tyre-mark fix).
1. Read `CLAUDE.md`, then the traffic sections of `docs/PLAN.md` (see above), then this file.
2. Check out `traffic-test-branch` and clone `htubdawg/HalfGripRefs` next to the repo
   (the tools need `-p:GameDir=/home/user/HalfGripRefs/game`). Build the stub HalfGripTools.dll (bottom of this file) for
   `tools/check.sh`.
3. Run `dotnet run -c Release --project tools/trafficview -p:GameDir=... [-- --seed N] [--picture out.ppm] [--closeup out.ppm [x z span
   hour]]`. It builds the map, then race N's day twice, checks the day, the parking, the lateness ledger and the physics cars, and
   exits 0. About 45 s a seed (`TV_DRIVE_ONLY=1`: only the day's and the physics cars' checks, about 18 s). 16 seeds (1 to 15,
   20261122) pass. For all 16, run the built dll in a background loop (see "This session's environment").
4. **Stage 4 (rooms) is built, not yet tried in the game:** `TrafficRoom.cs` (pure, `tools/trafficsim`) and `TrafficShared.cs`. See
   "Stage 4: the traffic in a room as built". **Next: test 50** (`docs/TESTING.md`, two players), then fix what it shows. Stage 3 was
   tried once (test 49, the owner alone in a room, 2026-11-23: "everything seems fine"; frame time not measured yet).
   **Traffic cars never get modded or custom items or bodies** (the owner, 2026-11-23): see the stage 4 section.
5. **Stage 3 was built before the owner's try:** The lateness ledger (`TrafficLate.cs`), and the physics cars: the driver's
   arithmetic (`TrafficDrive.cs`), the game side (`TrafficPhysics.cs`: the bubble, the prepared enemy car, `TrafficBody`, the
   `TakeDamage` patch), checked in trafficview (`tools/trafficview/Drive.cs`). See "Stage 3: the physics cars as built". Built with the
   defaults of the owner's four decisions (still unanswered: ask). Test 49's first report found nothing to fix; frame time with
   `TrafficPhysics` 0, 20 and 30 is still to be measured.
6. **Test 48** (`docs/TESTING.md`, stage 2's second try) is still open. The owner chose (2026-11-23) to go ahead with stage 3 without
   waiting. When its report comes (screenshots, `LogOutput.log`), fix what it shows first: stage 3's physics cars share stage 2's
   paint, poses and lamps. What changed since their first try is under "The owner's first report" and "Stage 2: second round" below.
   Nothing in the ledger changes what the game shows until something makes a car late, so test 48 is unaffected by it.

**Where to look first in their next log (test 50, both machines'):**
- `Traffic: the room's host, settling who drives each car` / `in a room, the host settles who drives each car`; `a stand-in here`;
  `handed to` / `handed here by`; `Traffic: a message could not be sent to the room's host`; `The traffic stopped` (an exception in
  the new code would show there).
- The overlay's `room:` part: `states in N (N stale)` (stale should be a small share), `claims ... lost` (a few), the client's clock.

**Where looked first in the stage 2 logs:**
- `Tile ... could not be worked out: Collection was modified`: there should be none now (fixed in `Buildings`, see below). If there
  are, another static list is shared by the tile workers.
- `Traffic: ... cars, ... trips ..., built in ... ms` (about 1.9 s on the owner's machine, on a worker, no hitch seen), `The traffic's
  cars could not be made`, `The traffic stopped`.
- With `VerboseLog`: `Traffic: look N: ... meshes, ... painted, ... lamps, ... wheels, box ..., wheels' bottom ..., radius ...`. The
  first try gave look 0: 8 meshes, 1 painted, 4 wheels, box (5.3, 3.9, 14.2), bottom -0.13, radius 0.88; look 1: 9 meshes, 2 painted,
  4 wheels, box (5.2, 3.7, 13.0). The lamps count is new: 0 would mean the lamps were not found and parked cars still glow.

**What the owner has said so far about stage 2** (first try, one race in a room alone): parked cars' lights were on; far too many
cars parked on the town's roads; a car phased through a parked car where the road had cars on both sides. Nothing yet about the
paint, the cars' height on the road, the wheels, frame time or hitting one: ask if the next report does not say.

Open with the owner:
- **Test 48** (second try): see above. Since the first try the cars also pull in and out differently and turn at junctions on curves
  ("Stage 2: second round"); test 48 asks them to watch both.
- **The cars "round the back":** about 750 of the parked cars at any hour are not drawn (kerb parking was cut to answer "way too many
  cars on the roads"). They pop up on the road when they set off. Ask whether that is noticed; the fix would be somewhere drawn
  behind the houses (back lanes or yards: a world change) or fading them in.
- **Test 47**: the highway's wrap, Westhaven's road out and Millbrook's road north in the game. No report yet; the first stage-2 log
  has no `The highway wrapped round` line (the owner did not drive into a tunnel).
- **The eight cut-off town streets**: a layout fix for later (see below). Ask before doing it: it changes the world.
- **The day of traffic** (`TrafficDay.cs`, below): the choices the design did not settle (trips beyond the map go by the road north
  only; through traffic circles the trunk; stays counted from leaving home; see "Decided while building").

**Stage 3's physics cars are built** (the owner's call, 2026-11-23, ahead of test 48's report): a real car near the player (our own
driver on the game's car physics, within 300 units), back on its timetable out of reach through the ledger, late by what held it up;
wrecks retired. The owner measures what 20 to 30 physics cars cost (`TrafficPhysics`, test 49).

## Where things stand

Stage 1 (timetables, no game code): the traffic's map and each race's day of trips (`TrafficDay.cs`) are done and checked. Stage 2 (cars
drawn in the game): built, tried once by the owner, three things fixed from that try (lamps, kerb parking, pulling in and out) and a
tile bug from stage 1; waiting for the second try. Stage 3 (physics cars near the player): the lateness ledger and the physics cars
built and checked in trafficview (`TrafficLate.cs`, `TrafficDrive.cs`, `TrafficPhysics.cs`; `tools/trafficview/Lateness.cs`,
`Drive.cs`); not yet tried in the game (test 49). Nothing is released and `mod.json` is unchanged (0.31.1). The branch also carries
two world changes (the road fix and the wrap), so everyone in a room needs the build that has them. Bump the version and the README
when the owner releases it.

**Done and pushed:**
- The plan, the owner's answers and the studies' figures are all in `docs/PLAN.md`.
- `OpenWorld/src/Plots.cs` (pure): the house-plot decisions (`Plots.Along`) and the footprint test (`Plots.Fits`), moved out of
  `Buildings.cs`, which calls them. Traffic finds every household from the same arithmetic the drawing uses. **Confirmed to change
  nothing drawn:** meshcheck's clear, facades, bases and homes give the same counts as `deer-test-branch` (2,606 houses, wings,
  towers and churches; 274 drive houses).
- `Heap.Clear()` added.
- `OpenWorld/src/Traffic.cs` (pure): the traffic's map, fixed with the world.
  - The driving graph: drives included; tracks, paths and courses left out. No junction where two roads cross more than 4 units apart
    in height (a bridge over a road).
  - Its connected parts (`Parts`, `Part`, `MainPart`, `OnMain`): households and places on a part cut off from the main one are left
    out (`CutOff`), so every trip has a route.
  - Speed limits: Pennsylvania's, lower in towns.
  - Lane offset: right-hand traffic, the trunk's outer lane.
  - Households: drawn houses (a pad beside a garden house, the kerb for terraces), drive homes, farms; cars and workers from hashes.
  - Destinations with spaces and per-purpose draw: offices in the towers, retail, shops, high-street shops, workshops, filling
    stations, markets, churches, courses' yards, the roads leaving the map.
  - `Route` (Dijkstra between two anchors, partial edges at both ends).
- **A layout fix (changes the world):** `Network.SnapJoin`.
  - A routed road's end looked for the road whose cell it reached within 140 units only. The trunk's smoothed line runs up to about 270
    off its cells, so Westhaven's only road out (its link to the trunk) stopped in the fields and the whole town (376 households,
    16 places) was cut off. The main road from Millbrook to the north edge and four farm lanes ended short the same way.
  - Now an end that finds nothing looks out to 320.
  - meshcheck's `backroads` fails on any main road dead end (it found these two before the fix). All of meshcheck passes before and
    after; a few later roads move.
  - Millbrook's road now meets the road from Northcote at a slant after a tight bend: it works, but isn't pretty (jview shows 25
    pixels of ground and 7 overlapping there).
- **The highway wraps round (changes the world; asked for by the owner):** a car that drives into the trunk's tunnel at one edge comes
  out of the other's at the same speed, in the same lane, facing the same way.
  - `Wrap.cs` (pure) pairs the two tunnels (`Network.Portal.Road`; they face opposite ways, so the move has no turn), glues them at a
    plane `Depth` (14) inside each, and gives the move (`Move`).
  - `Streamer.WrapTarget` loads the ground round the far mouth (2 tiles) once the car is within `Wrap.Preload` (700) of a tunnel.
  - `Streamer.WrapRound` (in `LateUpdate`, before the origin follow) does the jump as an origin shift that keeps the root holding
    the car, the cockpit and all three cameras (`Player` in the Tokyo dump) in place (`Origin.Shift` with a keep set). The leftover
    under a tile, plus the difference in the two roads' heights, is moved with `Origin.MoveKept`. Tyre trails are cleared.
  - `WorldScene` builds the two open tunnels: a dark floor at the road's height, walls, a roof, and a back wall at `Wrap.Length` (24)
    that is seen but not solid, so a fast car can't hit it before the once-a-frame check sees it past the plane. The road north's
    tunnel stays shut.
  - Read from the Multiplayer mod's source (`RemoteCar.FixedUpdate`): a stand-in more than 10 units from a new state snaps to it, so
    others see the car come out of the far tunnel.
  - meshcheck's `wrap` check: the tunnels paired, every lane landing in its lane, the floors clear of the ground (the edge's ground
    rises through a deeper floor), the highway event clear of both tunnels.
  - **Not yet tried in the game.**
- `tools/trafficview`: builds the map and reports it; exits 1 on a failed check; `--near <road>` lists what comes near a road (for
  finding why one is cut off). Last run:
  - 1,261 junctions, 1,451 stretches of road, 1,907 households with 3,604 cars, 273 destinations, built in about 540 ms (after the
    layout's 7.5 s).
  - The graph in 9 parts: the main one 569,228 units of road; 8 cut-off town streets (2,761 units) whose 110 households are left out.
  - 3 roads leave the map (the trunk at both ends, the road north), so 3 edge destinations. That is right: the old note suspecting
    `Network.Portals` was wrong.
  - Routes: 2,184 of 2,184 found, 0 broken, 0.05 ms each.
  - 1.10 workers a household, lower than the survey's 1.33 because `WorkersOf` clamps workers to cars. Not changed: decide when tuning
    the day (the clamp may be right, since a worker without a car does not drive).
  - The map and the day build the same in two processes: checked by hand in the second round of stage 2 (seed 7's checksum
    `039684c31fa89f51` in two runs). Run trafficview twice and compare the checksum line to check it again.
- **`OpenWorld/src/TrafficDay.cs` (pure): each race's day of trips**, checked in trafficview (`--seed`, `--picture`;
  `tools/trafficview/Pictures.cs` draws). See "`TrafficDay.cs`: as built" below. Commit `ab22a3a`.
- `tools/check.sh` passes against a stub HalfGripTools.dll (below). meshcheck builds with `TrafficDay.cs` in it; its last full run
  (all passing) was before `TrafficDay.cs`, which nothing meshcheck checks calls.

**Left for later (in the plan):** the eight cut-off town streets (three in Kingsbridge, five in Westhaven) are a `TownPlan` fault. Each
is a street laid with both ends free that no cross street came to join, reached only by footpaths. Fixing it (join such a street to
the nearest road, or don't lay it) is a world change with a meshcheck check to add. trafficview lists them by position and road index.

## `TrafficDay.cs`: as built (pure, per race)

`new TrafficDay(raceSeed)` builds the day (about 1.3 to 1.5 s: on a worker in the game); `At(car, simTime)` gives a car's place
(parked at a spot, driving with point, heading, speed and edge, or away off the map); `SimTime(startHour, raceTime)` maps a race's clock
to the day's (0 at 03:00, `Traffic.DaySeconds` long, repeating). The seed is `Weather.Seed`, the start hour `Sky.StartHourOf(seed)`, as
for `Herds`. It is not called from the game yet.

What it does, as the plan designed it (the full design is in this file as of commit 8374760, `git show 8374760:docs/TRAFFIC_HANDOFF.md`;
only the differences are spelt out here):
- **The day:** a weekend day with probability 2/7.
- **Tours:** the survey's rates per household (work per worker); a leaving hour from the purpose's table (tuned, see below), a stay,
  a place by gravity on straight-line distance, weight / (1 + (d / 3000)²) (3000, not 6000: with 6000 trips across the whole map were
  common, and a long drive spans hours of the world's clock); visits also among eight households drawn at random.
- **Thinning:** each tour kept with one probability so households drive the survey's 186 s each a day, reckoned at each path's speed
  profile plus a quarter (`Expected`), not free-flow: the survey's 59 minutes are real driving. q comes out about 0.15 on a weekday,
  0.18 at a weekend.
- **One car, one tour at a time;** none leaves after 21:00 (not 23:00); a tour waiting for its car is dropped then.
- **The simulation:** 0.25 s steps, cars in index order; a speed profile per leg (limits, bends as sqrt(8.1 R) with R over a 40-unit
  chord either side, so the drawn line's small kinks don't read as bends; turns; the stop; a backward braking pass); IDM behind the car
  in front along the car's own path (200 units). Cars brake for a slower stretch ahead as the profile allows, not by the IDM's free
  term (which trails a falling target by up to 27% over it).
- **Junctions** (all uncontrolled), by reservation, first come first served:
  - Requests sorted by the soonest a car could arrive (speeding up to its profile), the car on the right first within a second.
  - Two movements conflict when their chords round the junction cross (each arm has its lane out, then its lane in, in angle order:
    integers only), or they leave by the same lane. Same lane in never conflicts (the second follows the first).
  - A conflicting grant is no bar if it clears before this car arrives (reckoned at its turn's speed through the box) or this car
    clears, from a standstill, a second before it arrives (a gap taken).
  - Room past the box by distance only: a car let in behind a moving one that then stops would stand in the box and hold it (that
    made gridlock rings on the Kingsbridge link). Cars let in behind this one in its own lane are not counted against its room.
  - A car refused for crossing someone keeps its place: nobody later who crosses it goes first. A car refused only for want of room
    holds nobody up.
  - The 45 s guard only lets go of the order, never into a car in the box, never without room. It is used 0 to 3 times a day.
- **Pulling out:** once nothing is within 40 on the lane (or just past a short first stretch), nothing coming within 3 s and no car let
  into the lane at the junction behind; after 20 s waiting the car behind leaves a gap if it can stop for it (let in); never on top of
  another car, even after 5 minutes. Parking spots on the road are moved out of the junction boxes along their stretch.
- **Recording** every 0.5 s per leg, the final place at its end; `At` interpolates. Lane offset right of travel, `Traffic.LaneOffset`.

**Decided while building** (for the owner to confirm):
- **Every car into a trunk tunnel comes out of the other at the same moment.** Since the wrap joins the trunk's ends, they are not a way
  off the map: trips beyond the map go by the road north only, and it takes the draw the trunk's ends had (`PullOf`, so the share of
  work beyond the map stays the same; without it, three quarters of the commuters drove to Kingsbridge's towers and jammed it).
- **Through traffic circles the trunk:** 100 cars (10 until 0.32.1, which the owner found too thin), half each way, spread evenly by
  distance along it at 03:00 and driven all day through the wrap. They add about 18,000 to the trunk's count (28,000 in all, well over
  a real principal arterial's: chosen for the drive, not the counts). Flaw: at 03:00, where the day repeats, each jumps from where the day ends it to where it began.
  (Through journeys from the road north round the wrap and back were tried first: 72,000 units, 15 to 20 world hours each, mostly on
  the main roads, not the trunk.)
- **A stay is counted from leaving home** (`MinStay` 20 s at least). Driving runs in real time while the world's clock runs 36 times
  fast, so a drive spans one to three world hours; counted from arriving, every way home ran late into the night.
- **The hour tables are tuned** so the traffic counters' hours come out right (weekday against TPG 9, weekend against the survey's
  Saturday): departures lead the counts by about the length of a drive, the work peak at 5 to 7, visits end by 20.

**Checks in trafficview** (exit 1 on a FAIL). Seeds 1 to 15 and 20261122 all pass:
- the same day twice (checksum), and another seed's differs;
- no leg never started or unfinished; nothing unrouted;
- no two cars on a lane closer than a car's length (closest about 16);
- no crossing cars in a junction box together; the guard's uses reported;
- never over the limit + 5%;
- AADT-equivalent (passes a game day x 36) by class, reported against PennDOT: trunk median 4,100 to 6,600 (5,400), main roads about
  5,000 (minor arterials 5,000), secondaries 2,600 (collectors 1,800), streets and lanes about 150;
- the hours' shape within 3.5 points an hour of TPG 9 (weekday) or a Saturday (weekend); weekdays come out 1.9 to 2.7, weekends 2.3 to
  3.2. A drive spanning an hour or more of the world's clock smears every hour into the next, which no table fully undoes; the
  small hours stay a little busy (about 50 cars at 03:30 against 16 expected, mostly late ways home);
- cars moving at 03:30, 08:30, 12:30 and 17:30 against the expected (households with a car x their driving share x TPG 9's hour):
  the 5 pm peak about 300 (301 expected);
- lots' peaks against their spaces (reported: a few high-street shops get one or two more than their two spaces);
- the wrap: every crossing between the trunk's tunnels, each car's place across it off `Wrap.Move` by 0.0; nobody stopped in or within
  60 of a tunnel's mouth;
- `--picture out.ppm` writes the roads coloured by AADT-equivalent and frames at 03:30, 08:30, 12:30, 17:30 (`docs/traffic.png`,
  `docs/traffic-frames.png`, seed 7, a weekday).

Diagnostics it prints (not checks): where cars wait and stop, departures by hour and purpose, the trunk's share of the through cars'
driving, the worst leg's progress, cars on the road each hour of the run.

**Not done yet:** the relevant checks in meshcheck (the plan says meshcheck; trafficview is where they run now); what one car's
place costs (`At` scans a car's legs: fine for a few hundred cars, measure in stage 2); the deer reacting to traffic.

**Known weaknesses** (none fails a check; worth knowing before stage 2):
- **The Kingsbridge link** (the main road through the main town, a chain of junctions 16 to 220 apart) is where the queues are: most
  of the day's stopped time is on it and the streets joining it. It flows now, but it is the first place to look if a change brings
  back gridlock (trafficview prints the worst junctions and stretches).
- **Parked cars of one household share one spot** (`At` gives the home anchor's X, Z for each); stage 2 must spread them (by car
  index along the kerb or the drive). Several places' cars likewise park at one spot.
- **Drives have no lane offset** (`Traffic.LaneOffset` 0), so two cars meeting on a drive overlap when drawn; rare.
- **Legs that run past the day's end** (late ways home) are driven in the run's tail, after 03:00, without the next morning's traffic
  around them; `At` shows them in the morning (it checks w and w + 2400).
- **The circling cars jump at 03:00** (above).
- **A few high-street shops** park one or two cars over their two spaces at the peak.
- **Diagnostics left in `TrafficDay`** (`Stuck`, `WaitAt`, `StoppedOn`, `ActiveBy`, `StartsBy`, `ProfileSeconds`, `OverlapNotes`,
  `ConflictNotes`, the refusal counts): cheap, and they found every bug this session; trim them if they get in the way.
- `Traffic.WorkersOf` still clamps workers to cars (1.10 a household against the survey's 1.33); left as it is.

**For stage 2 (using the day in the game):**
- Every machine builds the same day from the room's seed, so drawing timetabled cars needs no messages. Build it on a worker at the
  race's start (about 1.4 s; it allocates the recordings, about a million floats).
- Sim time: `TrafficDay.SimTime(Sky's start hour, race time)`. Then `At(car, w)` for the cars near the player: `State` (Parked, Driving,
  Away), the point, heading (`Hx`, `Hz`), `Speed`, and `InTunnel` (hide or darken those: they are inside a tunnel's mouth).
- `X`, `Z` are world coordinates (subtract the origin to place them); `Y` is from sea level: add `Network.Lift` for the scene (mixing
  the two sank every building once). The point is the road's line plus the lane offset, not the drawn surface: sample the ground or
  the road mesh's height if they differ.
- `At` walks a car's legs: fine for the few dozen near the player; for a pool, index cars by where they are (each leg's path is a list
  of edges) rather than calling `At` for all 3,000 every frame. Measure it.
- A car on a leg past the far end of a loaded tile is fine to skip; parked cars are most of the cars at any time.

**Next (stage 2, the plan):** timetabled cars drawn in the game from a pool near the car, moved on an origin shift, no physics; build
the day on a worker at the race's start; headlights. Done as below; next is the owner's report on test 48, then stage 3.

## Stage 2: as built (first tried in the game 2026-11-22)

**The owner's first report** (one race, in a room alone) and what was done about it:
- *Car lights should be off when parked.* The enemy car's lamp materials glow (`bFata` white, `becuri` red, emission far over 1).
  Each look now finds its lamps (a material that glows, on a renderer that takes no paint) and makes dark copies; a parked car wears
  them, a driving one the lit ones. The verbose log's `Traffic: look N` line counts the lamps: if it says 0, the lamps were not found.
- *Way too many cars parked on roads in town.* About 1,000 cars stood at the kerb at any hour; now about 240 (seed 7). Kerb parking is
  only on the town streets and the secondary roads (not the main roads, not the narrow lanes), only on the stretch of the door within
  60 units of it, a household keeps only its first car at the kerb, and kerb spaces are 19 apart. The rest are parked "round the
  back": not drawn (about 700 to 800 cars at any hour). They appear on the road when they set off and vanish when they get home:
  the price of a quieter street, until there is somewhere drawn behind the houses to put them.
- *A car phasing through a parked car, the road with cars on both sides.* Never a car driving by (trafficview now checks that), always
  one pulling in or out: it slid diagonally through its neighbours, or turned round on the spot for a space across the road. Now
  (`TrafficParking.Drawn`, shared by the game and the picture) it drives along its lane until alongside its space, U-turns into the
  other lane if the space faces the other way, and curves in nose first over the last half; a second car on a house's pad stands
  beside the first (not behind it), a cottage's cars face away from the drive side by side along it, the lots fill every other bay
  in the front row first, and every space is kept 40 s either side of a stay (the next car's pull-in no longer meets the last one's
  pull-out). trafficview's new check: every 20 s of the day, every moving car where the game draws it against every parked car: 0.
- **From the log, not the traffic:** `Tile ... could not be worked out: Collection was modified` (about 25 tiles lost). When the house
  plots moved to `Plots.cs` (stage 1), `Buildings` was left with one static list of plots that the four tile workers filled at once.
  Each worker has its own now (`[ThreadStatic]`). Watch for those lines in the next log: there should be none.

**`OpenWorld/src/TrafficParking.cs` (pure):** where every parked car stands and faces. (As first built it ran after the day; since the
second round the day chooses the spaces as it runs and starts and ends each trip beside its space: see "Stage 2: second round".)
- A household's own cars, taken one after another in car order, each clear of those before: a **house** on the pad beside it, nose in,
  its tail clear of the street and its sidewalk (`Half + Shoulder + 8`, further than `Traffic.Homes`' pad, which stays as it was so
  the day does not change: the day's gravity uses the anchors' X, Z), a second car beside the first, further from the house; the other side of the house if a
  road, the water, a lot's building or a house is in the way (two houses closer than a car's width have no pad between them). The
  houses are the plots `Plots` keeps that fit, as `Buildings` draws them (their wings at the back are not counted), indexed once. A
  **cottage**: beside the drive's end, noses away from it, side by side along it, or the drive's other side. A **farm**: in the yard
  off the road towards the farm's middle, further in if the near row is taken. A home with no room off the road, and every
  **terrace**, gets a kerb space for its first car (see the owner's report above); its other cars are out of sight.
- **Lots:** the shops' and the retail park's painted bays (the same arithmetic as `Buildings.Lot` / `BayRows` / `BayRow`: if those
  change, change `TrafficParking.Spaces` with them), the front row first, every other bay before its neighbours; a row along the front of a workshop's shed; the pumps
  then the kiosk's front at a filling station; the front of a market place; across a course's yard. Spaces a road's paving, the
  water or a building reaches into are left out (none are, today). A full lot carries on at the kerb.
- **The kerb** (streets and secondary roads only): each side of each stretch cut into spaces 19 apart, clear of the junction boxes at
  its ends; spaces over a home's own parking, reaching onto another road (a junction's corner) or over water (a bridge) struck out.
  Terraces' first cars keep theirs all day (empty while they are out); everyone else at the kerb (offices, high streets, churches, a
  visitor in town, a full lot) takes the nearest free one within 60 of their door on that stretch for their stay (kept 40 s either
  side), in the order they arrive; else they are parked out of sight (not drawn).
- A kerb car's middle is far enough out that a car passing in the lane clears it by 0.4 (`LaneOffset + CarWide + 0.4`, or
  `Half - 3.5` where the road is wide enough), but never with its outer side past the sidewalk's back (where a terrace's front
  stands): on a town street two wheels are up on the sidewalk; on a village lane it touches a passing car's side. The day's traffic
  does not know about parked cars, so this keeps them apart when drawn.
- `Traffic.Destination.Lot` (new): the destination's lot, so its cars can be put on it. Nothing else in `Traffic` or `TrafficDay`
  changed but `At` (no array per call; `Place.P`, the distance along the leg); the day's checksum is unchanged.

**`OpenWorld/src/TrafficCars.cs` (the game):**
- `WorldScene` adds it after the deer; the day and its parking are built on a worker (`Task.Run`), the log line when done.
- **The bodies:** from `TrackManager.enemyCarPrefab` (the prefab asset, never instantiated: no scripts run), one look per model in its
  `CarSkinController.carModels` (two on 0.419). Every enabled `MeshRenderer` with a mesh that shows on that model (the other models
  switched off, anything inactive left out, nothing under `GunFirePoint`, `EffectsController` or `Nitroflames`), copied with its
  transforms into an inactive template with the car's own scale (1.5); the wheels are what is under a `Wheel_*` transform.
- **Paint:** the model's `carMeshRenderers` and `hoodsRenderers` get copies of their materials with `_EntireColor` the paint,
  `_AccentColor` a shade darker and `_TertiaryColor` near black (the channels `CarSkinController.EquipSkin` sets). One copy per material
  and paint, freed with the world (`WorldAssets`). The paint is a weighted pick of ten everyday colours from the car's index, so a
  household's car is the same each race. If cars come out one flat colour or the stripes look wrong, the channels mean something
  else than guessed: try the paint in `_AccentColor`.
- **Choosing:** every 0.4 s (or after 150 units driven) every car's place (`At`; a car with no trips and its home far off is skipped
  first), the parked ones' spots; the nearest 72 within reach (moving 900 units, parked 450), moving ones counted at 0.4 of their
  distance. trafficview measures it: about 0.3 to 0.7 ms in a town's middle.
- **Placing:** each frame each chosen car's `At`; driving, `TrafficParking.Drawn` (the lane point, or pulling in and out: see the
  owner's report above; up to 250 units at a trip's ends); parked, placed once per stay. Lamps lit when driving, dark when parked.
  Height: two raycasts down (the `Default` layer, where the roads, lots and buildings are, and `Floor`, the ground) at the front and
  back axles from 6 over the higher of the road line and `WorldGen.Height`, the wheels' bottom put on their mean, pitched to them; no
  hit, not drawn (ground not loaded). Wheels turn with the speed. A car in a tunnel's mouth (`InTunnel`) or away is not drawn.
- **Hits:** each car a `BoxCollider` round its body on the `Enemy` layer with a kinematic `Rigidbody`: the player hits a wall (as
  with the Multiplayer mod's stand-ins). The game's scripts that look at that layer (`AreaBlast`, `ExplosiveBarrel`,
  `PlayerResetLogic`) all check for an `EnemyController` and skip ours; the reset leaves the Enemy layer out of its ground and keeps 30
  from anything on it. The raycasts for heights leave it out.
- **Origin:** the cars hang from the world's root, which `Origin.Shift` moves, and each moving car is placed from world coordinates
  every frame, so nothing is cached in world positions.
- **Headlights:** the enemy car's `HeadLightsController.spotlight` (a spot of range 150, intensity 500) copied onto each car at the same
  place, on for the nearest 6 moving cars when `Sky.LightsOn` (new: dusk to dawn, or a black sky).
- **Clock:** `TrafficDay.SimTime(Sky.StartHourOf(seed), race time + Sky.Skipped)`: the debug weather skip moves the traffic too.
- **`Sky`** also looks for new `HeadLightsController`s every 2 s and puts every known light to the hour's state, so other players'
  stand-ins (made after the start, whose own `Start` sets their light from the race's weather) light up at night.
- **Config:** `World` / `Traffic` (on): off, none drawn. Overlay (`ShowCoords`): the `traffic:` line.
- meshcheck and jview leave `TrafficCars.cs` out (it needs `Mod`, `Sky`, `Weather`).

**Checks added to trafficview** (exit 1 on a FAIL; 16 seeds pass): parked cars overlapping, looked at every hour (0); spaces on another
road's paving, in a lot's building, in water, in a house (0 each); moving cars as drawn through a parked car, every 20 s (0); cars at
the kerb, off the road and out of sight at 12:30 and 21:30; kerb spaces made and struck out, and why; the lots' spaces kept of those laid out; the distance from a space to its trip's
end (median about 30, 90th percentile about 110, most about 350 units: a kerb space down the street); the cost of choosing the cars.
`--closeup out.ppm [x z span hour]` draws the parked cars round a place (by default the main town's middle at 12:30): the paving, the
lots, the houses, drive pads green, kerb orange, lots cyan, moving cars yellow where the game draws them (`docs/traffic-parking.png`:
seed 7, the main town's middle at 12:30; `docs/traffic-retail.png`: the retail park at 12:30).

**Known weaknesses of stage 2:**
- **Not tried in the game.** What can only be seen there: whether the copied bodies look right (paint channels, the wheels' bottom,
  the wheels' axle), the raycast heights on the drawn roads and bridges, the cost of 72 cars (each 8 to 10 renderers, the paint's
  material copies keep them batched), the kinematic box against the player's car.
- About a fifth of the parked cars are out of sight ("round the back") and pop in and out at the start and end of their trips.
- Pulling out of a nose-in pad, the car reverses out round a curve (since the second round).
- Moving cars are checked against each other as drawn (since the second round); what is left is listed there.
- The other cars past the nearest 6 have no lamps at night (the plan's glowing lamps and painted pool of light: not done).
- Parked cars are not obstacles in the day (it was made without them); kept apart only by where kerb spaces are.

## Stage 2: second round (2026-11-22, before the owner's second try)

No report from the owner had come, so this round took the stage 2 weaknesses that can be checked without the game. A new trafficview
check started it: **moving cars as drawn against each other** (every 5 s of the day, every pair, by what each was doing). On the old
code it found, over the 16 seeds, 959 meetings, 834 of them with a car pulling in or out: a car drawn coming out of a kerb space or a
bay up to 90 units along the lane from where the day had it, onto lane the day thought free, where a queue often stood (the "car
phasing through" the owner saw on a street with cars both sides was this kind, against a parked car). Now 50 with a pull (1 to 6 a
day), and the check fails over 10.

**What changed:**
- **The day chooses the parking spaces** (`TrafficParking.Take`, called by `TrafficDay` as each trip sets off; homes' spaces fixed when
  the day's cars are known). A place's space is held from setting off until the car next leaves it (+ `Clearing`, 17.5 s). The
  choices are part of the day now: plain arithmetic in the day's order, the same on every machine. `day.Parking` replaces
  `new TrafficParking(day)` (the game and trafficview use it).
- **Each trip starts and ends on the lane alongside its space** (`TrafficDay.MoveEnd`), kept out of the junction boxes; where a box
  stops it (a lot beside a junction: the Westhaven market), the car curves the rest of the way in.
- **The day holds the car still on its lane while it is drawn pulling out or in** (`Leg.PullOut`, `Leg.PullIn`, from
  `TrafficParking.PullTime`: 2 to 5 s for the curve, 2.5 s more to turn round); the cars behind wait for it. The drawing is timed
  (`Place.T`, seconds into the leg) and moves the car between its space and that one spot: from the kerb straight sideways; from a
  drive pad, cottage or farmyard along the road (or drive) to the space's own line, round a small corner, then straight up it (a
  curve cutting that corner crossed the tails of the cars in the next column); into a lot's bay off the road square and then along
  the bay. The car faces the way it moves (reversing out of a nose-in space).
- **Cottage spaces** stand at least 14 from the drive's line (they stood 9, their tails over the drive, where the day now starts and
  ends trips beside them).
- **Crossing the oncoming lane:** a space left of the car's way, or one facing back, is reached by turning across (or round
  through) the far lane. Pulling out waits, parked, for that lane to be clear (nothing within 80 behind or 40 ahead, nothing coming
  within the pull's time and 3 s, nothing let in at the junction within 300); turning in waits at its stop, but at most 15 s
  (`FarHoldMost`), then goes: a longer wait backed queues up through the junctions (tried: up to 300 s gridlocked the road past the
  retail park on seed 6, 2,084 minutes stopped). A car crossing holds back cars setting off beside it in the far lane; two waiting to
  cross to each other's side go in the order they stopped. Pulling out at an angle waits for the far lane alongside (25) to clear.
- **Arriving cars take the kerb on their own side first** (no U-turn through the oncoming lane).
- **Turning at a junction** is drawn on a curve from the lane where the car comes into the box to the lane where it leaves
  (`TrafficParking.Corner`), not along the roads' lines to the junction's middle (which jumped sideways by up to about 8 units and
  snapped round there). The day's junctions reckon movements by those chords. Drawing only.
- **The wheels** turn with the distance the car is drawn moving (`TrafficCars`), so they turn while pulling in and out too.
- trafficview: `TV_MEET=1` prints every meeting (and a pull's curve where a car goes through a parked one); the day's build time is
  judged on the second build of a run (the first makes the house index kept for later days).

**Numbers (16 seeds, 1 to 15 and 20261122):** every check passes; no moving car through a parked one; meetings with a pull 50 (834
before). Day built again in 1.25 to 1.55 s (the day and its parking; before, about 1.66 s and 0.36 s after it); the first build of a
run about 2.2 s. Stopped time is up (seed 7: 119 minutes to 199), nearly all of it the cars standing beside their spaces while they
pull in and out (about 1,340 legs, two pulls of 2 to 5 s); waiting at junctions about as before. The hours, counts by road class and
the 5 pm peak are unchanged within a few percent; cars out of sight ("round the back") as before, 720 to 830 at noon.

**Left (reported by the check, not failed):**
- About 12 meetings a day of cars on their way (193 over the 16 seeds; 125 before, as there is more queueing now): cars turning across
  each other inside a junction's box (the chords the day passes as not crossing still come within a car's width in a small box;
  drawing them on curves took a few off), and **late ways home driven after the day's end** (a leg that starts
  after 03:00, in the run's tail, without the next morning's traffic around it, then drawn into that morning: car 3505 on seed 6, leg
  3013 to 3796 s). The real fix for the second is to stop trips running past 03:00 or to drive the morning with them.
- A car crossing the far lane holds back cars setting off there, but a car already driving up the far lane does not stop for it (it
  would need a stand-in leader in that lane).
- Turning in after 15 s of waiting goes even into oncoming traffic.
- **The diagnostics** in trafficview (`TV_MEET`) and `TrafficDay` (`FarSide`, `FarEnd`, `HeldSince`) are cheap; keep them until the
  owner's report on this round.

## The owner's report on the stage 4 build (2026-11-23), and what is next: contact physics

**Read this first.** The owner tried the stage 4 build alone in a room (their log: `Traffic: the room's host, settling who drives
each car`, 10 physics bodies made, about 30 cars back on their timetables, cars 206 and 2160 retired as damaged, five origin shifts,
no warnings or exceptions from the traffic). So stage 3's path through the room works; **test 50 (two players) is still not done.**
They said three things:

1. **"A handful of cars I ran into while driving didn't have physics."** Not a failure in the log: those are the cars the bubble
   leaves drawn (walls) on purpose. `Drivable` (in `TrafficPhysics.cs`) refuses a moving car that is pulling out of its space (until
   0.5 s after `PullOut`) or into it (from `PullIn` + 3 s before the end), on the last `EndZone` + 25 = 70 units of its trip, near a
   tunnel, hidden, or in a junction's box at the moment of the choice (every 0.4 s); and `Bubble` sends a physics car back to being
   drawn on the last 45 units of its trip ("back on its timetable ... (its trip's end)", six of those in the owner's earlier log).
   In town most cars are near a trip's end or a junction, so the player meets these often. Parked cars are always walls. The fix
   is the same as for point 3 (below): a drawn car the player is about to touch becomes a physics car whatever it is doing.
2. **"When the stand-ins are swapped, it creates tire-marks from the origin to their current location, weird trail artifacts going
   off into the distance."** Fixed (not yet tried in the game): the enemy car's wheels carry `TrailRenderer`s (the game's
   `TireTrails`, which turns `emitting` on while the tyres skid), and a body made at the prefab's place (the scene's origin) or
   taken from the pool where it last was, then moved into place, drew a mark along the move. `TrafficBody.ClearTrails()` clears
   them after `Become` places a body and after a stand-in is shown at its first state. **The owner still saw the streaks after that
   (2026-11-23).** Second fix (not yet tried): the game's `EnemyCarEffectController.Update` keeps the trails emitting whenever the
   wheels are on the ground, and one clear was not enough: for a frame or two after the move the body could still be drawn from the
   rigidbody's old pose (the scene's origin for a new body), and the marks drew a new line from there. Now every placement goes
   through `TrafficBody.Place` (the transform and the rigidbody both moved: a teleport), and `ClearTrails` turns the trails off and
   keeps them empty for 0.3 s (`LateUpdate`), then back on as the game has them (on unless airborne). A stand-in whose playback
   snaps more than 10 units also jumps and clears instead of being swept there. **Still seen after that (the owner, 2026-11-23,
   with a log showing loose cars and three origin shifts; no traffic warnings).** Third fix (not yet tried): `Origin.cs` already
   switches rigidbody interpolation off over a shift because "an interpolated body would be drawn sliding the whole way for a frame",
   so the game's cars do interpolate, and `Place` had not done the same: it now holds interpolation off until the 0.3 s are up, zeroes
   a dynamic body's velocities, and keeps the body's particle systems empty over the same window. Not checked: the enemy car's
   particle modules (whether any emit by distance) and its rigidbody's interpolation mode, which the scene dumps don't list; reading
   them from the game's asset files needs a tool (UnityPy) that this session was not allowed to install. **Still seen (the owner,
   2026-11-23, log with `body model N stands`, so that build): "the tires are just painting a decal from where they were to where
   they are. Turn that crap off BEFORE you swap".** Fourth fix (not yet tried): `Place` now turns the trails off and clears them
   before the body is moved at all (it moved first and cleared after), forces `emitting` off every frame of the 0.3 s window (the
   game's effect controller turns them on when the wheels land), and `End` puts a body into the pool with its trails off and empty.
   **Still seen after 0.32.0 on the odd swapped car (the owner: "only when they get swapped").** What the game's code says (read from
   the decompile and the Tokyo dump, not guessed): `TireTrails` holds two `TrailRenderer`s (`TireTrailEffect`, material `MAT_Trail`)
   under `Wheels/Meshes/Wheel_LB` and `Wheel_RB`; the game never sets their points or width, so they draw whenever the car is on the
   ground; `EnemyCarEffectController.Update` only turns them off or on when `isAirborne` changes; the wheels move in local space
   (`CarControllerNew.Update`), so nothing lags the body; the game's own resets never clear them (they rely on the car being put 2 up,
   airborne). `WheatTrampler` and `WaterRippler` also stamp from the last place to this one, but Tokyo has no `TrampleField` or
   `WaterRippleField`, so they paint nothing here. Every swap path (`TakeBody`, `Place`, `Begin`/`BeginLoose`, the warm pool,
   `Release`, a stand-in's first state) clears the trails around the move. The hole was the end of `Place`'s window: interpolation
   and the trails came back on in the same `LateUpdate`, and an interpolated body can be drawn a frame or two from a pose it had before
   it was placed, so the trails, already on, painted from there. Fifth fix (not yet tried): interpolation comes back first and the
   trails stay off `TrailsSettle` (0.15 s) longer; and a jump guard every `LateUpdate` (after interpolation, before Unity adds the
   frame's trail points) empties the trails and keeps them off whenever the drawn body is more than 4 units from its rigidbody or a
   marked wheel moved further in a frame than the car's speed allows (origin shifts skipped). Log (`VerboseLog`): `Traffic: car N's
   tyre marks cut at a jump (...)`, once a body: if that line shows, the guard caught one.
   **Still seen after that (the owner, 2026-11-23), and the guard never fired** (verbose on, no `cut at a jump` line): no trail's wheel
   jumped while we watched, so the streak was a point the `TrailRenderer` kept from before and joined to the wheel. Sixth fix (not yet
   tried): the guard also reads each trail's own points every frame: a segment at either end longer than a car goes in a frame, or the
   head far from both ends while emitting, empties it (`Traffic: car N's tyre mark emptied (...)`, once a body); and the trails are
   emptied again right after they are turned back on.
   **The owner's next log (2026-11-23, "looks better") showed where they came from:** every trail emptied was at an origin shift, five
   with a segment exactly as long as the shift (1118 at (-500, -1000), 1031 at (-250, -1000)). `Origin.Shift` paused interpolation for
   2 frames, which at a high frame rate pass with no physics step, so an interpolated body was drawn for a frame from its pose before
   the shift and its trail kept a point there. Seventh fix (not yet tried): interpolation comes back only after two physics steps since
   the shift as well (`Origin.RestoreInterpolation`). The guard stays as a net. Parked cars are "still a little jiggly, tolerable".
   **The owner's log over 9 shifts:** no more shift-long segments, but the guard still emptied 5 marks after 3 of the shifts with the
   wheel 104 to 146 from them (a tenth of the shift): the body still drawn part of the way back for a frame, so holding interpolation
   off does not clear Unity's history of the old pose. Eighth fix (not yet tried): each traffic body keeps its marks off for
   `ShiftHold` (0.3 s) from an origin shift, then empties them and turns them back on.
   **Parked cars' wheel height (the owner, after 0.32.0: "a physics enabled car wouldn't have its suspension fully out or in, we need
   to calculate where it should be so the transition is seamless").** Built (not yet tried): `TrafficRest.cs` works out the game's car
   at rest from `CarControllerNew.FixedUpdate` (read from the decompile): per wheel a ray from one unit above its place along
   `-up + right * 0.1 x` (Unity normalises it, so it leans out), length = hit - radius - 1 - the surface's `suspensionOffset`, force
   `k (rest - length)` along the normal, capped at ten quarter-weights; at rest the forces carry the weight and turn the car about its
   centre of mass no way. Drawn cars stand at that height and pitch, the ground under each axle lowered by its surface's offset (our
   ground, verges and tracks have `HandlingModifier`s left at the default -0.1, so the game's cars sit 0.1 lower there than on roads,
   lots and sidewalks), wheels drawn where the game draws them at rest. `PlaceAsDrawn` puts a physics body at exactly the drawn pose
   and `TrafficBody.Settle` sets each wheel's `lastLength` and wheel transform to rest. trafficview checks the solver against the game's
   spring and damper law run to a standstill (24 cases, within 0.00001).
   **First try (the owner, 2026-11-23): wheels still popped out sideways, the car still bounced, and the log said `centre of mass
   (0.00, 0.00, 0.00) (set on the body)`.** Two mistakes, both from the game's code: (1) the prefab asset's rigidbody reports no centre
   of mass (the rest was first worked out from the prefab); (2) `CarControllerNew.InitializeWheels` runs in `Start`, the frame after a
   body is first switched on, and takes the wheels' places from the wheel transforms then (widened by 1.1). A body made on demand was
   placed and its wheels moved to the rest pose before its `Start`, so the controller took the moved wheels: widened twice (the pop)
   and lowered (the bounce). Fixed (not yet tried): the rest is worked out by `TrafficCars.LearnRestFrom` from the first body woken in
   the warm pool, after its `Start` (the controller's own wheel places, `Rigidbody.centerOfMass` live, mass, springs, `Physics.gravity`;
   logged `Traffic: the cars at rest on their springs (...)`), and the drawn cars' wheels are moved there then; the warm pool is kept
   topped up (`WarmBodies` spare, one a frame) so a body is put to use started; one made on the spot anyway is left alone until its
   `Start` (`settleLater`). Earlier the drawn wheels were also not widened (the prefab's places), a pop of its own.
3. **"I'd really appreciate physics when you hit parked cars, could we accomplish that without a massive performance hit or limiting
   traffic physics?"** Yes, planned below; not started. Answer the owner's decisions first if they have replied.

### Contact physics: as built (2026-11-23; not yet tried in the game, test 51)

Built as the plan below says, with the defaults of the three decisions (a knocked parked car retired once out of sight; the full brake;
`TrafficLoose` 6). **The decisions are still the owner's: ask** (they are a few lines each to change). Everyone in a room needs this
build: `TrafficRoom.Version` is 2 (the state's flags gained `FlagLoose` 8 and `FlagTouched` 16).

- **A loose car is a physics car with `TrafficBody.Loose` set**, kept in `physical` like the driven ones, so the room's code (states
  sent, hits, wrecks, `DriverChanged`, `ClearPhysics`) carries it with no second list. `LooseCount` counts them; `ClaimNearest`'s cap
  leaves them (and `looseWanted`) out, so `TrafficPhysics` keeps its 24 for the driven cars.
- **The touch test** (`TrafficCars.Contact`, each frame after the drawing, `TrafficPhysics.cs`): for each drawn car (`chosen`, its view
  active, not physical) within 30 + speed x sweep of the player and within 8 in height, `TrafficDrive.Sweeps` (pure): the player's box
  (half 7.5 by 2.7) swept along its velocity for 0.3 s (0.5 s on a room's client, for the grant's round trip) plus 2 units, bounded
  by one box in its own frame, tested on four axes against the drawn car's box (its look's `Box`, as its collider), margin 0.2. **The
  margin is small on purpose:** trafficview found every lane car in the town's narrow streets passes kerb cars 5.8 apart middle to
  middle (half a unit between bodies), so a margin of 0.5 with half-width 3 made a loose car of each one passed (2.4% of the rush's
  lane cars). A hit claims the car through the room (`room.Claim`, its id in `looseWanted`); `DriverChanged` makes it loose
  (`BecomeLoose`) instead of driven when it was wanted loose. Alone or on the host that is the same frame. At most one body is made a
  frame (`madeFrame`); `Contact` walks `chosen` with `choosing` set and takes the new loose cars off it after the walk.
- **`BecomeLoose`**: the pooled enemy car (`TakeBody`) at the drawn view's pose (0.15 up), trails cleared; parked at rest, moving at its
  drawn speed. `TrafficBody.BeginLoose` (shares `Wake` with `Begin`): no driver, no engine sound, brakes full (`LooseStep`), wheels
  straight. It keeps its leg (-1 parked), the leg's clock where it was drawn (`LooseT`), whether it was pulling in or out, the sim time.
- **Touched** (`TrafficBody.Touched`): hit by anything with a rigidbody (`OnCollisionEnter`: the player, a car, a drawn car's kinematic
  box), damaged, or, once it first stood still (0.75 s on, at rest: a parked one settled on its springs, a moving one braked to a stop),
  moved over half a unit or turned over 3 degrees from there (kept in world coordinates, so origin shifts don't count).
- **Going back** (`LooseBubble`, in the bubble's pass every 0.4 s): untouched with the player 60 away (`LooseLeave`), or `TrafficLoose`
  set to 0: back to being drawn (`LooseBack`): parked, no ledger entry unless the day had it set off meanwhile (then late on that leg
  by `LegClock - Start`, so it sets off now from its space); moving on its lane, `LateOp` by where it stopped (its place projected on
  the leg), so it is drawn where the body was; pulling in or out (off its lane), late by `LegClock - LooseT`, drawn from where it was
  made. One that stopped off its lane can't be drawn there, so it counts as touched. Touched: stays until the player is 500 away
  (`LooseGone`, past the parked cars' drawn reach), then `Retire` (a hurt one destroyed, its smoke can't be stopped; a wreck burns
  and goes as the driven ones do). Never handed to another player.
- **Rest height** (the owner, 2026-11-23: "parked cars bounce a bit when they activate due to the physics based suspension"): a body
  was put down 0.15 (loose) or 0.4 (driven) over the drawn car's pose, which has the wheels' bottoms on the ground, and dropped onto
  its springs. `LearnRest` (every 0.4 s, at the choice) measures each body model's root height over the ground from this machine's
  physics cars standing or crawling (under 3 units/s, upright, 1.2 s after placing), a running mean of up to 40; once 3 are in,
  `RestPlace` puts bodies of that model at that height over the ground under the drawn car (logged once a model: `body model N
  stands ... over the ground`). Until then the old small drop.
- **The parked (static) cars' wheels** (the owner, 2026-11-23: "the static cars CURRENTLY have their suspension COMPRESSED, the physics
  cars DO NOT. WE NEED TO UNCOMPRESS THE PARKED STATIC CAR SUSPENSION SO THE WHEELS ARE FULLY EXTENDED"): stage 2's copies took the
  wheels at the prefab's height, a loaded spring. The game draws a wheel at y = its place's car-frame y - (spring length - 0.3)
  (the Tokyo/MainMenu dump: rest length 0.7, Wheel_LF at y 0.46 with a spring of 0.33). `TrafficCars.WheelDrops` works out each wheel's
  drop to the full 0.7 and `MakeLook` lowers the copies' wheels by it (height only), with the box and the wheels' bottom moved to
  match, so a drawn car stands on extended wheels (logged once: `the parked cars' wheels let down ...`).
- **A mistake not to repeat:** a `SettleWheels` that set the physics bodies' `WheelData.lastLength` and their drawn wheels' places from
  a Start postfix (commit 8bd073b) put every wheel in the middle of the car ("unicycles"); taken out the next commit. The cause was
  not found; leave the physics car's wheels to the game.
- **Lamps** (the owner, 2026-11-23: "parked cars should not turn their lights on when their physics get switched on"): the game's
  `CarControllerNew.Update` lights the tail lamps (`becSpate`, the `_EMISSION` keyword) whenever `brakeAccel > 0`, and a loose car
  holds its brake, so it showed brake lights; the head lamps glowed too. `TrafficBody.FindLamps` (in `MakeBody`) keeps each lamp's
  glowing slots and emission colours; `Lamps(false)` sets this body's copies' `_EmissionColor` black, which the controller never
  touches, so they stay dark whatever it does with the keyword. Dark for a parked loose car and for another machine's (state loose
  with leg -1); lit again in `Wake` and `BeginRemote`. A moving car braked to a stop keeps its lamps.
- **What the others make of it:** in `OnABody` (hiding drawn cars, holding them back in their spaces) and `BuildMovers` (the drivers'
  obstacles) a loose car still in its space (`TrafficBody.InSpace`: untouched, parked; a stand-in's: its leg sent as -1) counts only
  if a drawn car would be in it: the cars driving past parked cars never stopped for them. In the road (stopped there, or knocked
  out of its space) it is an obstacle and hides drawn cars coming up behind it, as a held physics car does. No headlight when loose.
- **In a room:** states carry `FlagStopped | FlagLoose` (and `FlagTouched`), leg -1 when parked; the stand-ins elsewhere are walls,
  engine off, headlight off. A host whose player left with loose cars (`LastOp`): untouched ones drawn as the day has them, the rest
  retired.
- **The pool warmed** (`WarmPool`, from `Step`): the first 3 bodies made one a frame at the start of the race, each woken for one
  frame kinematic 5000 under the world (the Instantiate and the Awakes, Dacia Body's log lines too, paid then), then put to sleep in
  the pool. (`TrafficPhysics` or `TrafficLoose` above 0.)
- **Checked:** `tools/check.sh`; trafficview seed 7 in full (the day unchanged: `039684c31fa89f51`) and the other 15 seeds with
  `TV_DRIVE_ONLY=1`, with a new check (`tools/trafficview/Contact.cs`): `Sweeps` on nine cases worked out by hand, and every car
  driving its lane at 17:00 swept past the parked cars (0.3 s and 0.5 s): at most 2% may touch one (seed 7: 0 and 1 of 288).
  `TV_CONTACT_LIST=1` lists each touch (how far ahead and to the side). trafficsim 16 seeds (the messages' version only).
- **Not measured:** the frame cost (test 51). The fallback if it costs too much is in the plan below (kinematic with its scripts off
  until its first contact).

### Contact physics: the plan (prepared 2026-11-23, built as above)

**The idea:** keep everything as it is, and add a second, small kind of physics car, made only where the player is about to touch a
drawn car: a **loose car**, the same pooled enemy car (`TakeBody`), with no driver. It stands exactly where the drawn car was, in its
pose, at rest; a parked car keeps its brakes on (a hard shove slides it; a gentle one barely moves it), a moving car brakes to a stop
(it has no path to follow while pulling in or out). Since it is made a moment before the touch, the player feels a real car, not a
wall. Its own small cap (config `World` / `TrafficLoose`, default 6), separate from `TrafficPhysics`' 24, so nothing is taken from
the driven cars.

**When one is made** (each frame, cheap: only the drawn cars, at most 72, and only those within about 40 units): the player's car's
box, swept along its velocity for 0.3 s (plus 2 units), meets the drawn car's box. Not for a car in a tunnel or away. Also for the
drawn moving cars point 1 lists (pulling in or out, at a trip's end, in a box). Cars the bubble can drive should go on becoming
driven cars as now (this is only for what it refuses).

**When it goes back** (in the bubble's pass, every 0.4 s): the player 60 units or more from it and it **untouched** (within 0.5 units
and 3 degrees of where it was made, no damage): back to being drawn, with no ledger change for a parked car (it is still in its space)
and, for a moving car it stopped, the time it stood still as lateness (a `Late` op on its leg, the same as `HoldBack` makes). A
**touched** one stays where it was knocked while anyone can see it, then is retired from the day (`Retire`; a wreck burns and goes as
now). The owner may prefer a pushed parked car to stay "home" (see the decisions).

**Cost.** The cost of a physics car is mostly the enemy car's scripts (`CarControllerNew`'s wheels and raycasts every physics step),
not the rigidbody (a body at rest sleeps). With a 40-unit trigger only one to three cars are loose at a time even in a town, and they
go back once the player is past, so it should cost about what three more driven cars do. Two things to avoid a hitch: make the pool's
first few bodies at the start of the race (spread over frames; each new body is an `Instantiate` plus the game's `Awake`s, which is
also when Dacia Body logs its four lines), and never make more than one body in a frame. Measure it: frame time in a busy street
with loose cars on and off (test 51 below). If it costs too much, the cheaper fallback: make a loose car kinematic with its scripts
off until the player's car actually touches it (its `OnCollisionEnter`, which a kinematic body gets from a dynamic one), then dynamic;
the first contact is then a wall for one physics step.

**Code to write** (all in `TrafficPhysics.cs` / `TrafficShared.cs`; nothing in the pure files but a flag):
- `TrafficBody`: a `Loose` mode (no driver: `FixedUpdate` holds the brakes, steering 0, never `GaveUp`/`Hold` logic), where it was made
  and when, `Touched` (moved or damaged since). `ClearTrails()` after placing it.
- `TrafficCars`: a `loose` dictionary (car to body) like `physical`; the swept-box test in `Step` over the drawn cars; making (`Become`
  generalised: a parked car's pose is its drawn pose; velocity 0 parked, the drawn speed moving); going back in `Bubble`; loose cars
  count in `IsPhysical`, `OnAPhysicsCar`, `BuildMovers` (as obstacles: the driven cars stop for them) and `PhysicsLights` (never lit
  when parked).
- **In a room:** a loose car is claimed like any other (the nearest player simulates it; usually the one about to hit it), so it
  needs the room's grant before it exists: on the host (alone) that is at once; on a client it is a round trip, so trigger it a
  little earlier there (sweep 0.3 s + the client's measured round trip, say 0.5 s). The state's flags need a "loose" bit
  (`TrafficRoom.FlagLoose`, layout `Version` 2) so the stand-ins elsewhere show it parked (lamps dark). A loose car handed over or
  released goes through the room as the driven ones do (its op: none if untouched and parked; `Late` held time if it was moving;
  `Retire` if touched).
- **trafficview / trafficsim:** nothing for the room (the messages are the same); a trafficview check that no loose car would be made
  in another car's or a building's box (the parked spots are already checked against each other).

**Decisions for the owner (each with a default to build with):**
1. A parked car pushed out of its space: **default retired once out of sight** (its household goes without it for the rest of the
   race). Or: put back in its space once out of sight (the day carries on as if nothing happened).
2. How hard to brake a parked car: **default the full brake** (a gentle nudge barely moves it, a hard hit slides and spins it).
3. `TrafficLoose` default **6**, 0 turns it off (parked cars walls again, as now).

### Order of work for the next agent
1. Read `CLAUDE.md`, this file's "Start here", this section, then "Stage 4: the traffic in a room as built".
2. Contact physics is built (above). Wait for test 51's report (and test 50's, if the owner had a second player); fix what they show
   first. Ask the owner the three decisions above if not yet answered.
3. Still open from before: test 50 (two players), test 49's frame time with `TrafficPhysics` 0, 20, 30, test 48 and test 47, the
   eight cut-off town streets (a world change: ask), the owner's four stage 3 decisions (built with defaults).

## Stage 4: the traffic in a room as built (2026-11-23; not yet tried in the game)

The owner's word (2026-11-23), after test 49's first report ("everything seems fine"): go on to stage 4. Also: **traffic cars never get
modded or custom items** (the Dacia Body is a mod). Checked: they don't. Dacia Body gives its shape only to a skin whose name starts
with its own skin's; ours are "Traffic skin N/N", so it puts the stock meshes back and only logs the materials it reads (the
`Paint material ... _Cull`, `Lamp material bFata/becuri` lines, four for each new body). The physics cars get a plain `new CarStats()`,
no items, guns and modules off; the drawn cars are copies of the prefab asset, which no mod touches. Keep it so: never equip anything
on a traffic car, never take its look from the race's or the player's skins.

**`OpenWorld/src/TrafficRoom.cs` (pure, no Unity types; `tools/trafficsim` drives it):**
- Per car: driver (0 nobody: on its timetable), a generation (each change of driver), its ledger entry (`Op`: Late leg/W/D, Retire,
  OnTime). The host (never changes) settles; reliable messages come in order from each sender.
- **Claim** (to the host, with the gen seen): granted to the first claim on a car nobody drives at that gen (and not retired, and not
  from a player who left); `Grant` to everybody. A machine never drives a car before the grant reaches it (alone: at once, before
  `Claim` returns), so no car is ever driven on two machines (trafficsim checks it after every delivery). A refused claim gets no
  answer; the grant to the other clears it, or it is given up after 2 s.
- **Release** (the driver, with the entry): applied at once there, settled by the host, `Released` to everybody. **Hand** (the
  driver, to a player): the driver's car turns into a stand-in at once (`Handing`), the host grants it to the other (if that player
  is still there; else gives it back from its last state). **PlayerLeft** on the host: every car they drove given back with
  `LastOp` (the game: from its stand-in's newest state: late by where it is, or retired if hurt or off its lane).
- **Ledger** (any machine, for cars nobody drives: a car held back in its space; on the host, cars caught up): applied at once, settled
  by the host, `LedgerSet` to everybody; one the host can't take (the car is driven, or retired) is answered to the sender alone with
  the host's entry, so a provisional one is put right.
- **Hello** (a late joiner) → `Snapshot` (every car's gen, driver, entry, in parts).
- **The clock**: every machine's states carry its sim time as the room has it; a client takes the host's, the least delayed reading
  of the last 8 s (a reading is late, never early), eased at 0.2 s a second (snapped once at first, or if over 5 s off). The traffic's
  `SimNow()` is the room's clock everywhere (`LocalSim()` is the machine's own race clock).
- **States** (unreliable, ≤1100 bytes, 21 cars each): the sender's real clock, sim time and player's place, then per car: car, gen,
  leg, world x y z, rotation (4 shorts), velocity, P, health, flags (alive, hurt, stopped). Taken only from the car's driver at its
  present gen (a state from before a hand-over is dropped). Sent 15 a second while driving cars, 5 while not (the others still need
  this player's place, and the host's clock).
- **Damage** (a hit on a stand-in) to the driver, dropped if the gen moved on.
- **`Nearest`** (claim only if this player is the nearest heard from lately; same distance: the lower id) and **`HandTo`** (another
  player within 300 when this one is past it, or one under half as far when this one is over 120 away: loose, so no back and forth).
- **`Playback`**: the stand-in's states played back behind the newest by 0.12 s plus however unevenly they come (at most 0.45 s), on
  the driver's clock mapped by the least delay seen, its own clock eased (8%) never stepped; interpolated (rotation the shorter way),
  coasting 0.6 s past the newest, then held; before the first state, the first moved back along its velocity.
- A retirement's D = `Wrecked` (1) marks a wreck, so stand-ins elsewhere burn where they are rather than go (the driver's car leaves its
  list the frame it is wrecked, so no "wrecked" state is ever sent).
- Message tag 0x7A, layout version 1, the race's seed in every message (another race's are dropped). Shared handler in `EventLink`.

**The game side (`TrafficShared.cs`, edits in `TrafficPhysics.cs`, `TrafficCars.cs`, `TrafficLate.cs`, `EventLink.cs`):**
- The room is made with the day. `Pump()` each frame before the step: messages and players coming and going read (held in
  `TrafficRoom`'s inbox from the network's thread; states held while the day was built dropped), Hello once, this player's place,
  states sent, `Tick`.
- `MakePhysical` claims instead of making (`Drivable` holds the old tests; the cap counts claims waiting); `DriverChanged` makes the car
  physical on the grant (`Become`, from where it is drawn; if it can't be any more, it is given straight back). Alone this is
  synchronous, so stage 3 runs as before (`choosing` guards the chosen list).
- `Bubble` collects what to do and tells the room after the car is off the list (the room's word comes back to `DriverChanged`):
  back (`LateOp`: late, early, or on time under 0.05 s), retired, or **handed** (in a room with others: `HandTo` before the far/end
  tests, alive cars only, not at a trip's end or near a tunnel).
- **Stand-ins**: `TrafficBody` with `Remote`: kinematic, `MovePosition`/`MoveRotation` to `Play.Sample(FixedClock)` (a clock run on
  fixed steps, kept within 0.25 s of real time), its leg and place kept as a `Mover` for the drivers here (a wall when stopped or
  wrecked), smoke and wreck from the flags; made inactive on the grant to another and shown at its first state. A hand-over turns a
  physics car into a stand-in in place (`ToRemote`: it coasts at its last velocity until the new driver's states come) and a stand-in
  into a physics car (`FromRemote`: from its newest state, health and hurt kept). Hits on a stand-in go to the driver
  (`TrafficDamagePatch` → `RemoteHit`).
- Drawn cars treat stand-ins as physics cars (`IsPhysical` once shown, `OnAPhysicsCar`, held back, hidden, `BuildMovers`, headlights).
- **The ledger**: hiding is now this machine's own (`TrafficLate.hidden`, never sent; `TryGet` still reports it, so trafficview is
  unchanged: seed 7's report is the same line for line as before the change); `Apply(op)` takes the room's entries; `Settle(w, seen,
  catchUp)` catches late cars up only on the host (in a room, `seen` also counts any other player within reach), returns them, and
  the host sends them; hidden cars are shown again by each machine for itself. A car held back in its space is sent at most every 0.5
  s (each send holds it a second more). `HoldBack` returns its lateness.
- Overlay: `; room: host` or `clock +0.00 s`, players heard, stand-ins, claims (granted, lost), handed in and out, states in (stale).
  Log (`VerboseLog`): the room's part at the start, stand-ins made, hand-overs both ways, a player leaving, entries received.
- `EventLink.TrafficToHost/ToAll/ToOne` (`ctx.Net.Send(data, Net.Everyone, reliable)`). The stub HalfGripTools.dll (bottom of this
  file) now needs `Send(byte[] d, ulong to = Net.Everyone, bool reliable = true)`, `SendToHost(byte[] d, bool reliable = true)` and
  `Net.Everyone`.

**Checks:** `tools/trafficsim` (16 seeds pass, 639 checks each); trafficview seed 7 unchanged; `tools/check.sh` builds it. Test 50 in
`docs/TESTING.md` (two players).

**Known, left:**
- Pushing a car another machine drives is like pushing another player's car (a wall), as the plan agreed; shots and rams are sent.
- A car retired off its road (not wrecked) by a driver who drove away vanishes for another player between 300 and 400 from it (the
  hand-over only goes to a player within 300).
- Two machines can both think they are nearest for a moment (each knows the other's place a little late): the host takes the first
  claim, the other's is dropped; the car stays drawn on the loser.
- The bandwidth: 24 cars × 50 bytes × 15 a second ≈ 18 KB/s from each driving player to each other player. Fine for a few players;
  with many, send fewer states for cars nobody else is near.
- HalfGripTools 2.0: the static `Net` becomes `NetRoom` (`ctx.Net` unchanged); the traffic uses `HalfGripTools.Net` in `EventLink`
  and `TrafficShared` (`InRoom`, `IsHost`, `LocalId`, `HostId`, `Everyone`).

## Stage 3: the physics cars as built (2026-11-23; tried once, see test 49)

What was built, against the plan below (read the plan for the why; this is what differs and what to know).

**`OpenWorld/src/TrafficDrive.cs` (pure, no Unity types):**
- `Line(leg, p)`: the line a car steers along, its lane through `TrafficParking.Corner` (made internal), the curve stage 2 draws on.
- `Project(leg, from, x, z)`: where a car is along its leg, searched a little either side of the last answer (6 behind, 6 + v/10
  ahead per physics step; 15 and 60 once it is off its line). trafficview: 4,000 points project back exactly (worst 0.07).
- `TimeAt(leg, p)` (inverts `DistanceAt`), `LegClock`, `Lateness(leg, p, w)` (negative when early), `OnTime(leg, w)`.
- `Prepare(day, ref mover)`: the edge it is on, the junction ahead (node, arms, `ToBox` to its middle) and the junction whose box's far
  half it is in (`Back`): two junctions can be closer than their boxes (Kingsbridge), and a car leaving one is already coming to the
  next. Missing this was the last big source of crossings.
- `Target(...)`: the least of the leg's profile (`TrafficDay.Profile`, made internal, built once when the car turns physical) here and
  half a second on; a safe speed (Gipps-like, braking 8.25, 1 s gap, the day's 7 standstill) behind the nearest car on its path
  (physics or drawn, matched by edge, way and road distance) and behind anything in its lane with no path (the player's car, wrecks,
  a car knocked off, a drawn car pulling in or out, where it is drawn); stopping its **front** at the box's edge while a car whose way
  crosses its own (the day's `Crosses`) is in the box, is committed (can't stop short of it any more: always first), or is coming and
  goes first (sooner by over a second, else the car on the right, else the lower index; a drawn car always first, it gives way to
  nobody), or while its way out has no room past the box; stopping at its trip's end; and easing off (never below 4, never in or just
  before a box) once more than 40 ahead of its timetable (none on the last stretch). Waiting 8 s, it no longer gives way to cars still
  coming (only to those in the box or committed); 24 s, not to a drawn car standing in the box (the day holds it there for a car that
  is physical now). About 0.5 to 1 µs a call.
- `Overlap(...)`: two cars' bodies (14 by 5.2) overlapping, shared by the game's hiding and trafficview's check.

**`OpenWorld/src/TrafficPhysics.cs` (part of `TrafficCars`, and `TrafficBody`, and the patch):**
- **The bubble**, every choice (0.4 s): `Bubble` first (going back), then the choice, then `MakePhysical` from the chosen drawn moving
  cars, nearest first, within 300 (`PhysicsReach`), up to `World` / `TrafficPhysics` (new config, default 24, 0 to 60). Not while it
  pulls out or in, in a junction's box, within 80 of a tunnel on its path, or within `EndZone` (45) + 25 of its trip's end. Placed at its
  drawn body's pose (0.4 up), velocity heading × the day's speed.
- **Going back**: past 400 (`PhysicsLeave`), on the last 45 of its trip or near a tunnel, if alive, unhurt, upright and on its lane
  (8 units, 45 degrees): `late.Late(car, leg, w, D, 0.05)` with D = `Lateness` where it is (early too: kept, so no jump), back to the
  pool. On the last stretch it waits until D ≥ -1 (it eases off to be on time there). Otherwise it stops (`Hold`) until the player is
  400 away, then is retired (`late.Retire`); a hurt one destroyed, an unhurt one pooled.
- **Drawn cars near physics cars** (the day doesn't know one is held up): a drawn car in a physics car, or about to run into one (in
  its lane within 18 + 1.5 s of travel behind it, or bodies overlapping whichever way they face), is hidden (`late.Hide`, back once out
  of sight, the ledger's rule); a car about to pull out (or waiting at its trip's start, P ≤ 1) where a physics car stands or is coming
  (25 ahead, 45 + 18 behind) is **held back in its space** (`TrafficLate.HoldBack`: late by however long until its start, and a second),
  checked every frame it is drawn as well as at the choice (a car sets off between choices). The drivers see the drawn moving cars and
  the physics cars, prepared, every frame (`BuildMovers`).
- **The car** (`MakeBody`): `Instantiate(tm.enemyCarPrefab)` under an inactive holder; before its first waking: `EnemyController`
  off (`Start` never runs: no guns fitted, no lap looked for, no engine sound), every `EnemyModule` and `EnemyGun` off, `PitGuard` off,
  the `HeadLightsController` destroyed (its light ours: the sky's 2 s look would turn every one on), `CarSkinController.carName =
  "Traffic"` (no name taken from the race's pool) and `carSkin` our paint as a `CarSkin` (model, colours as stage 2's), `isPlayer`
  false, `tm` set. Its first activation runs the game's `Awake`s (damage visuals, tyre damage, the skin equipped), then `carStats =
  new CarStats()` (a plain car: the enemy AI's `Awake` had set the player's). A pooled car is re-skinned with `ApplySkin`.
- **`TrafficBody.FixedUpdate`**: `Project`, `Prepare`, `Target`, then steering (a point 10 to 45 ahead on `Line`, the signed angle times
  0.7 to 1, clamped to 45 degrees, smoothed) and pedals (brake in proportion over the target, the gear that pulls hardest with a 0.2 s
  gap in neutral and 0.6 s hold, the pedal in proportion under it, held on the brake when stopped). Off its line over 8 for 10 s: gives
  up and stands.
- **Damage (owner decision 1's default):** `TrafficDamagePatch`, a prefix on `EnemyController.TakeDamage(float, bool)`, which every
  hit reaches: for a car with a `TrafficBody`, `Damage(amount)`: its own health (the car's 100), smoke while it lasts; at 0 the game's
  wreck look (`damageVisuals.SetWrecked`, flames, the destroy sound) without `EnemyKilled`, `RaceStats`, lifesteal, combo or text;
  explodes after 5 s and is destroyed (decision 2's default); retired from the day.
- Engine: one of `EnemyController.engineSounds` looped (the game's enemies do the same, no pitch). Headlights: the physics cars first
  out of `MostLit` (6). Overlay: `physics N/24 (... giving way, ... stopped), made, back on time / late, wrecked, retired, hidden, held
  back`. Log (`VerboseLog`): each car made, back, wrecked, retired.

**`TrafficLate` changes:** `Late(..., least)` keeps any lateness over `least` (default 1.5 s as before), negative too; `HoldBack`; a
late car before its leg's start is drawn parked where its last trip left it (`Before`), not on the road at its start.

**trafficview's physics check** (`tools/trafficview/Drive.cs`, after the ledger's): the line and `TimeAt` round trips; then 10 minutes
from 17:00 standing where the most cars drive within 300, every step 0.05 s: the bubble as the game runs it (made, going back, held
back, hidden), the physics cars as points on their lanes with the game car's pull (6) and brakes (30) driven by the real `Target`; and
again with the player's car stopped across a lane. FAIL: more than 6 pairs of cars' bodies in each other (`MostMeetings`) or 3
crossing in a junction box, any step in the player's car, a jump over 3 going back, a car still physical stopped over 90 s (not with the
player's car in the road: they queue behind it for good). The 16 seeds: 0 to 5 pairs a run (most 0 to 2), 0 to 2 crossings, jumps
under 2 units, no long stops; about 120 cars made physical, a third of them going back late (mean 15 to 50 s). `TV_DRIVE_NOTES=1`
prints each meeting and crossing; `TV_DRIVE_CAR=N` follows car N through its junctions.

**What is left (known):**
- **Junction meetings**: cars that both commit at once to a turn or a merge still meet in the box (0 to 5 in 10 minutes at the busiest
  spot). With physics they bump rather than pass through.
- **Drawn cars are not physics cars' equals**: a drawn car never gives way, and with the cap reached (the busiest spots have 25 to 40
  cars driving within 300) cars coming up behind a physics queue are hidden. Raise the cap if the owner's frame time allows.
- A car held back in its space past its time keeps its space, but the day gave that space to the next car 17.5 s after the day's
  departure: rarely two cars in one space.
- A physics car that the game itself destroys (or that falls under the map) leaves no ledger entry: it is back on the day where the day
  has it.
- **Not in the game yet**: whether the prepared enemy car behaves (wakes without errors, stands on the road, the paint), the steering
  gain and the pedals on the real physics (they were written from the game's code, not tuned), the cost. A traffic car's own crash
  into another physics car does no damage (as the game's enemies, only the player's touch counts).
- Rooms (stage 4): built, see above.

## Stage 3: the plan (prepared 2026-11-23; built, see above)

The agreed stage (`docs/PLAN.md`, "Traffic: the agreed plan", stage 3): **within about 300 units (90 m) of the player a timetabled
car becomes a game car with our own driver** (follows the car in front, waits at junctions, can be bumped, shot and wrecked); out of
reach it goes back on its timetable, **late by what held it up** ("car c is D seconds late from t"), so traffic is not used up; a
wreck or a car knocked off its road is **retired** ("car c gone from t"). Alone (solo, or alone in a room) this machine drives every
car. Rooms (who drives which car, states sent, stand-ins) are stage 4: keep everything a room will need to agree on (lateness,
retirement) as small pure records from the start, so stage 4 only sends them.

### What the game gives (read in the 0.419 decompile, 2026-11-23; file and line as `ilspycmd -p` writes them)

- **The car:** `TrackManager.enemyCarPrefab` (stage 2 already reads it; 'EnemyPrefab'). On it (main menu dump, `Enemy_0`):
  `Rigidbody` (mass 1000), `CarControllerNew`, `EnemyController`, `CarSkinController`, `CarDamageVisuals`, `CarTireDamage`,
  `GlassDamage`, `PitGuard`, `EnemyNitro` (disabled), `TireTrails`, `WaterRippler`, `WheatTrampler`, and under it
  `EnemyCarEffectController` (smoke, fire, explosion), `HeadLightsController` with its spot light, the guns' fire points.
- **`CarControllerNew`** (`CarControllerNew.cs`) is the physics: four raycast wheels (`CastGround`, layer mask -5, skips car bodies),
  springs, grip from a `HandlingModifier` on the ground's collider if any (else 1). It is driven only through **`steeringAngleDeg`**,
  **`engineAccel`** and **`brakeAccel`** (and `handBrake`); `forwardSpeed` reads back. It needs nothing from a path or the lap.
  `Start` takes `rb` and the wheels; `tm` (TrackManager) is used for the brake lamps before the start (`tm.raceStarted`). It rights
  itself after 4 s upside down (`timeUpsideDown`, with `UnityEngine.Random`: fine, drawing only).
- **`EnemyController`** (`EnemyController.cs`) is the racing AI, on one closed lap (see "investigated after 0.17.0" in the plan): not
  usable as the driver. What matters:
  - `Awake` (`:758`) sets `carController.carStats = PlayerStats.carStats` (**the player's stats**), makes `CarDamageVisuals` and
    `CarTireDamage`. Awake runs on activation even with the component disabled.
  - `Start` (`:775`) gives the skin's stats and behaviour (`skinController.GetStatsForThisEnemyName()`), sets health from
    `carStats.health`, **fits every `EnemyModule` (the guns, rockets, nitro: `Fit`)**, then with no `pathManager` logs an error and
    disables itself. **So disable the component before the object is first activated:** Start never runs, no guns are fitted, no
    error; set `carController.carStats` from `GetStatsForThisEnemyName()` yourself after activation; health stays its default 100
    (`health` is private; `startingHealthOverride` too) unless set by reflection (`AimTest` reads it that way).
  - `TakeDamage` (`:276`, `:292`) works with the component disabled. **It is how every hit reaches a car:** the player's guns
    (`Gun.HandleEnemyCollision`, `hit.collider.GetComponent<EnemyController>()`, on the collider's own object, and `ShootSeen` through
    `CarTireDamage`), rockets, rams, spikes, area blasts (`GetComponentInParent`), and the player's crashes
    (`CarControllerNew.OnCollisionEnter` `:1078`: `ComputeCrashDamage`, then the other car's `EnemyController.TakeDamage`); an enemy
    car hit by something else within 3 s of the player touching it takes half (`EnemyCrash`).
  - **A wreck counts as killing an enemy** (`TakeDamage` at 0 health): `alive = false`, `damageVisuals.SetWrecked()`,
    `RaceStats.ReportEnemyDestroyed`, the `EnemyKilled` signal, "WRECKED" text, flames, and **the object destroyed after
    `explodeDelay`** (`DestroyAfterDelay`). `EnemyKilled` with `byPlayer` heals the player (`CarLink`, `Stat.HealPerKill`), gives ammo
    (`MagazineManager.GrantForKill`), counts in lifetime stats and feats (`LifetimeTracker`: `DriftKill`), `BossCar` and
    `ClapperFrame` listen too; hits add combo and lifesteal. **Owner decision 1 below.**
  - `FindObjectsByType<EnemyController>` is used by `AimTest` (a debug aid), `ClapperFrame` and `TutorialArena`: traffic cars will be
    in those lists. `TrackManager.enemies` (the race's list, what the Multiplayer mod's AI sync and the lap manager use) is filled
    only by `SpawnEnemies`: traffic stays out of it, as long as nothing adds it.
- **The gears and throttle** (for our driver, written in our own words, as the plan says): `EnemyController.DriveTowardsSpeed`
  (`:379`): over 0.9 of the target speed, no throttle and `carStats.brakeDeccel`; under 0.8, the gear that gives the most
  acceleration (`AccelInGear`: `carStats.torqueCurve.Evaluate(forwardSpeed / carStats.speedMultiplier * ratio) * ratio *
  carStats.engineAccel`, with `carStats.gearbox.Forward(gear)` and `ForwardCount`), changed with a neutral gap
  (`behaviour.gearChangeTime`) and a hold; between, coast. Steering (`FollowPath` `:360`): the signed angle to a point ahead times a
  gain (more for small angles), clamped to 60 degrees. All `CarStats` fields used are public.
- **`CarSkinController.Awake`** (`:285`) takes a unique enemy name from the game's `NamePool` (shared with the race's enemies:
  traffic would use names up and could appear in a wreck's stats) and equips that name's skin. Re-paint after it (stage 2's
  `_EntireColor`, `_AccentColor`, `_TertiaryColor` copies), so a car keeps its colour when it turns physical.
- **Shots push:** `Gun.ApplyTargetRecoil` and `ShootSeen` push any non-kinematic body they hit, so a physics traffic car is shoved
  by gunfire as well as by the player's car.
- **Origin:** `Origin.Shift` already moves every rigidbody (`Origin.cs`, it pauses interpolation over the shift), so physics cars
  under the world's root follow a shift. Positions the driver keeps (its target, last point) must be world doubles, as stage 2's are.

### The design (recommended; the owner's decisions below may change parts)

**Two kinds of car near the player:** stage 2's drawn bodies (everything as now) and, within the bubble, **physics cars**: an
instance of the enemy prefab, prepared as above (made under an inactive holder, `EnemyController`, `EnemyNitro`, every
`EnemyModule`, `PitGuard` disabled before the first activation; `carStats` from the skin; our paint; lamps as stage 2's), with
**`TrafficDriver`** (new MonoBehaviour) driving it. Kept in a small pool (they are dear to make: instantiate a few at the start, on
frames when nothing else is loading).

**The bubble:**
- A driving car (not parked, not in a tunnel's mouth, not pulling in or out: let the day finish those) is **made physical** when it is
  within `PhysicsReach` (300) of the player, the ground under it is loaded (stage 2's `Pose` found it), and fewer than
  `MostPhysics` (config, default 24) are physical. Nearest first. Its stage 2 body is hidden and the physics car placed at the same
  pose with velocity `heading * speed` from the day.
- **All driving cars inside the bubble are physical** (up to the cap): then the cars behind a held-up car are physics cars too and
  queue behind it for real, and nothing timetabled drives through a car that is late. A car about to be made physical where its
  timetable puts it on top of (or within 20 units behind) a physics car on its lane is made physical **held back**: given lateness so
  it appears behind (the ledger below).
- It **goes back on its timetable** beyond `PhysicsLeave` (400, the gap stops cars flicking at the edge) if it is upright, on its
  leg's path (within about 8 units of its lane, heading within 45 degrees) and not damaged; its lateness is how far behind its
  timetable it is then (the time the day had it at its present place along the leg, against the clock). Otherwise it is
  **retired**.
- Parked cars stay stage 2's kinematic boxes in stage 3 (a parked physics car still raycasts four wheels each physics step). A later
  step could make a parked car physical when hit.
- Never near a tunnel mouth (the day's own tunnel handling, `InTunnel`, and the wrap): no physics car within 60 of a mouth; one that
  gets there goes back on its timetable.

**`TrafficDriver`** (per physics car, `FixedUpdate`; its arithmetic in a pure class so trafficview can test it):
- **Where it goes:** its leg's path (`TrafficDay.Leg.Path`, `OnPath`), the lane offset, through junctions on stage 2's corner curve
  (`TrafficParking.Corner`: the same line it was drawn on). Progress P along the leg is found by projecting the car onto the path,
  searching forward from the last P.
- **Steering:** a point a speed-dependent distance ahead (about 10 + 0.6 s of travel) on that line; signed angle times a gain,
  clamped.
- **Speed:** the least of the leg's speed profile at P (`TrafficDay.Profile` is private and built at a leg's start then dropped at
  its end: expose it, or rebuild it for the leg), the IDM behind the car in front on its lane (a physics car's place, or a drawn car's
  from the day: the same parameters as the day, `a` 5, `b` 8.25, `T` 1.5 s, `s0` 7), and a stop at the junction box's edge while a
  car is in the box on a crossing chord (the day's `Crosses`) or one with right of way is closer (first come, first served, the car on
  the right first). Simple and local; it does not have to match the day exactly, only never drive through anyone and never block a
  junction.
- **Throttle, brake, gears:** as the game's idea above, our own code.
- **The trip's end:** a physics car that reaches the last stretch of its leg goes back on its timetable to pull in (stage 2 draws
  the pull), late by however late it is.

**The lateness ledger** (pure, `TrafficLate.cs` or in `TrafficDay`; what stage 4 will send): per car, a list of `(from t, late D)` and
`gone from t`. The drawn place of a late car is the day's place at `w - D` **until it next parks**; its stay there is shortened by D
(not below the day's `MinStay`, 20 s), and whatever is left over carries into the next leg. `At` for a gone car: away (not drawn,
its home's space empty). Kept small: an entry only when a car leaves the bubble late by more than a second or two.

**What this leaves, and the owner's answer (2026-11-23):** a late car back on its timetable outside the bubble can be drawn
overlapping a car the day put just behind it (the day never knew it was late). The owner: *"if a car is late to its waypoint and in
danger of overlapping other traffic, we can just despawn it on unloading or simply make it catch back up in the background when the
tile it's on is unloaded."*

**The ledger as built (`OpenWorld/src/TrafficLate.cs`, pure; `TrafficCars` draws through it):**
- `Late(car, leg, w, D)`: car is D seconds behind on that leg from sim time w (under 1.5 s: no entry). `Retire(car, w)`: gone for the
  rest of the race (drawn as away). `Hide(car, w)`: not drawn until it catches up.
- `At(car, w, out hidden)` replaces `TrafficDay.At` in `TrafficCars`: a late car is the day's place at `w - D` on its leg (new
  `TrafficDay.Driving(leg, t)`); once that trip is over it parks at the trip's end, its stay cut short (never below `MinStay`, now
  internal) and anything left over makes its next trip late. An entry made up in full is dropped there.
- `Guard(near, w)`, every choice (0.4 s): of two drawn cars that would overlap with at least one late (moving: same way, under 18
  apart along the lane, under 3 beside; or a car within 6 of a parked car's middle, in its space), **the farther from the player is
  hidden** (the one in front of them stays). An on-time car can be the one hidden (an entry with leg -1).
- `Settle(w, seen)`, every choice: **a late or hidden car catches up in the background** (its entry dropped, back on the day) once
  neither where it is drawn nor where the day has it can be seen: `TrafficCars.Seen` is a loaded tile (`Streamer.IsLoadedAt`, new)
  within the drawing reach (900 moving, 450 parked). So a car never vanishes or pops back where the player is looking, except the
  hide itself.
- The overlay's traffic line adds `late N (made, caught up, hidden, retired)` once anything is late.
- Nothing calls `Late` or `Retire` yet: that is the physics cars' job (`TrafficCars.Late`). Until then the ledger is empty and the
  game draws as before.
- In a room (stage 4), what each machine can see differs, so a catch-up (and a hide, if it should be shared) is a claim like the
  lateness itself. Alone it is local.

**trafficview's lateness check** (`tools/trafficview/Lateness.cs`): from 17:00 for 10 minutes, as stage 3 would: every driving car
300 to 900 from a pretend player made late at the start, then every 10 s those 380 to 420 out (leaving the bubble), each by 2 to 120
s; drawn as `TrafficCars` draws (loaded tiles 5 round the player, the reaches), guarded and settled every 0.4 s. Two players: one
driving with a household's car, one standing where the most cars drive. FAIL on any drawn overlap with a late car or a drawn late car
jumping. Seed 7: following, 16 made late, 6 hidden, 17 caught up (hidden on-time cars count too); standing (38 cars driving within 450), 89
made late, 106 hidden (37 within 300 of the player, which in stage 3 are physics cars, not drawn bodies; 52 at 300 to 600; 14
farther), 117 caught up, 0 overlaps, 0 jumps, 0.04 ms a choice. Its first run found two faults, both fixed: the overlap test
measured "beside" by one car's heading only (so a pair could pass one way round and fail the other), and the guard placed a car
pulling out of a lot's bay on its lane rather than where it is drawn (`TrafficParking.Drawn`). The lateness here is harsh (up to two minutes, everyone leaving the bubble); measure again with
the real lateness once the physics cars give it. Over the 16 seeds, standing: 10 to 92 made late, 7 to 140 hidden (4 to 48 of them
within 300 of the player), 0 overlaps and 0 jumps in every seed.

**Left in the ledger for later:** a hidden car stays hidden until neither of its places can be seen, so in a jam beside the player a
hidden car can stay gone for a while; a hidden on-time car (the farther of a pair) is hidden the same way. If the owner notices cars
vanishing near them, the first thing to try is hiding only cars beyond the bubble (300), since inside it stage 3's physics cars
queue for real.

### Decisions for the owner (each has a default to build with if there is no answer yet)

1. **Does wrecking a traffic car count as killing an enemy?** (heals with `HealPerKill`, gives ammo, kill stats and feats, "WRECKED",
   combo.) Default **no**: a Harmony prefix on `EnemyController.TakeDamage(float, bool)` for traffic cars keeps their own health and
   wreck (damage visuals, smoke, flames, the explosion, retire) without the `EnemyKilled` signal, `RaceStats`, lifesteal or combo.
2. **What a wreck looks like and how long it stays:** the game destroys an enemy car after `explodeDelay`. Default: the same
   (explodes and goes), retired from the day.
3. **How many physics cars and how far:** default 24 within 300 units; a config value (`World` / `TrafficPhysics`, 0 = stage 2 only)
   so the owner can measure 0, 20, 30.
4. **Parked cars pushable?** Default no (stage 3 keeps them walls).

### Checks

- trafficview (no Unity): the driver's pure parts, the ledger, the bubble's choice. **Done** (`tools/trafficview/Lateness.cs`,
  `Drive.cs`).
- In the game (test 49 in `docs/TESTING.md`, written): cars near you are physics cars (bump one: it is pushed, not a wall; shoot one: it
  smokes, then wrecks); they follow their lanes, queue, wait at junctions, pull in and out as before; nothing drives through a car
  you stopped; far cars carry on; frame time with `TrafficPhysics` 0, 20, 30 at the evening rush in Kingsbridge; the log line counts
  physics cars made, gone back late, retired.
- `tools/check.sh` builds it; meshcheck and jview leave the game-side files out as now.

### Order of work (suggested)

1. One physics car: make and prepare a prefab instance as above (log what it has), place it on a road ahead of the player with no
   driver, check it stands, can be pushed and shot, wrecks without the kill (or with, per decision 1).
2. `TrafficDriver` on that car, following one leg; then the bubble with one car; then all cars in the bubble, the cap, held back.
3. Going back on the timetable through the ledger (built: `TrafficCars.Late.Late(car, leg, w, D)` with D from where the physics car
   is on its leg, so it does not jump); retirement (`Retire`). A late car coming back into the bubble is made physical at its late
   place.
4. trafficview checks; test 49; the owner measures cost.

### Also pending

- **HalfGripTools 2.0** (no BepInEx) is coming: `HalfGripRefs/docs/HalfGripTools-2.0-port.md`. OpenWorld is last in the port order
  and not to be started until the owner says. Stage 3 uses no new loader API; stage 4's messages will (`ctx.Net`), so check the port
  plan before stage 4.
- Test 47 (the wrap) and the eight cut-off town streets, as listed under "Start here".

## This session's environment, as found

- The network allows `www.fhwa.dot.gov`, `rosap.ntl.bts.gov`, `gis.penndot.pa.gov` and `nhts.ornl.gov` (the owner added them). The
  page reader could not resolve them; `curl` works. Re-download the PDFs from the plan's links if needed (they were in the
  scratchpad).
- The Multiplayer mod's source is public: `git clone https://github.com/4l3x4nd3r0/halfgripmods` (`mods/Multiplayer/src`). Copy no code
  from it (the owner's decision).
- Decompile: `~/.dotnet/tools/ilspycmd`, with `DOTNET_ROLL_FORWARD=Major`.
- Tools need `-p:GameDir=/home/user/HalfGripRefs/game`. `tools/check.sh` needs a HalfGripTools.dll, so build a stub as
  `docs/DEER_HANDOFF.md` describes. It needs: `HalfGripMod` (abstract `Load`, virtual `Unload`), `ModContext` (`Log`, `Config`,
  `Harmony`, `Keys`, `Net`, `Folder`, `PatchAll`, `OnSceneLoaded`, `OnUnload`), `ModKey` (`Down`, `Held`), `Keys.Add(KeyCode, string,
  Controls.Section, Func<bool>)`, `ctx.Net` (`Send(data)`, `Send(data, to)`, `SendToHost`, `OnReceive(Action<ulong, byte[]>)`), the static
  `Net` (`Available`, `InRoom`, `IsHost`, `LocalId`, `HostId`, `Players`, `PlayerJoined`, `PlayerLeft`) and `NetPlayer` (`Id`, `Name`). The one used
  last (build it as netstandard2.1, assembly name `HalfGripTools`, against the game's Managed DLLs, `BepInEx.dll` and `0Harmony.dll`,
  leaving out `mscorlib`, `netstandard` and `System*`):

  ```csharp
  using System; using System.Collections.Generic; using BepInEx.Configuration; using BepInEx.Logging; using UnityEngine; using UnityEngine.SceneManagement;
  namespace HalfGripTools
  {
  	public abstract class HalfGripMod { public abstract void Load(ModContext ctx); public virtual void Unload() { } }
  	public class ModKey { public bool Down => false; public bool Held => false; }
  	public class KeyRegistry { public ModKey Add(KeyCode key, string label, Controls.Section section, Func<bool> shown) => null; }
  	public class NetPlayer { public ulong Id; public string Name; }
  	public class ModNet { public bool Send(byte[] d, ulong to = Net.Everyone, bool reliable = true) => false; public bool SendToHost(byte[] d, bool reliable = true) => false; public void OnReceive(Action<ulong, byte[]> a) { } }
  	public static class Net { public const ulong Everyone = 0UL; public static bool Available, InRoom, IsHost; public static ulong LocalId, HostId; public static IReadOnlyList<NetPlayer> Players => new List<NetPlayer>(); public static event Action<NetPlayer> PlayerJoined, PlayerLeft; }
  	public class ModContext { public ManualLogSource Log; public ConfigFile Config; public HarmonyLib.Harmony Harmony; public KeyRegistry Keys; public ModNet Net; public string Folder = ""; public void PatchAll() { } public void OnSceneLoaded(Action<Scene> a) { } public void OnUnload(Action a) { } }
  }
  ```
- The stub's project (build with `dotnet build -c Release -o out`, then pass `out/HalfGripTools.dll` to `tools/check.sh`):

  ```xml
  <Project Sdk="Microsoft.NET.Sdk">
    <PropertyGroup><TargetFramework>netstandard2.1</TargetFramework><AssemblyName>HalfGripTools</AssemblyName><NoWarn>CS0067</NoWarn></PropertyGroup>
    <ItemGroup>
      <Reference Include="<game>/HalfGrip_Data/Managed/*.dll" Exclude="<game>/HalfGrip_Data/Managed/mscorlib.dll;<game>/HalfGrip_Data/Managed/netstandard.dll;<game>/HalfGrip_Data/Managed/System*.dll" />
      <Reference Include="<game>/BepInEx/core/BepInEx.dll" /><Reference Include="<game>/BepInEx/core/0Harmony.dll" />
    </ItemGroup>
  </Project>
  ```
- The layout takes about 7.5 s to build in trafficview; a whole trafficview run with the day about 15 s; the whole meshcheck about
  1 min 45 s. A loop over many seeds outlasts a foreground command's 2 minutes: run it in the background and write each seed's report
  to a file in the scratchpad.
- `convert` (ImageMagick) turns the PPM pictures into PNG.
- trafficview's parking checks, the "through a parked car" one most of all, take most of a run now (about 20 s a seed). For 16 seeds run
  the built `tools/trafficview/bin/Release/net8.0/trafficview.dll` in a background loop (`dotnet <dll> --seed N > file`), one file a
  seed, and don't rebuild trafficview while the loop runs (it would swap the dll between seeds).
- The game's car for the traffic: `TrackManager.enemyCarPrefab` ('EnemyPrefab' in the Tokyo dump). Its structure is in the main menu
  dump (`scenedumps/0.419/MainMenu`, search `CarSkinController`): root scale 1.5, `Pivot/BodyModel` and `Pivot/BodyModel2` (the two
  `carModels`), `Wheels/Meshes/Wheel_LF..RB` (mesh on a child), `Headlights/Spot Light`, lamps `BecuriFata`/`HeadlightsCar2` (material
  `bFata`) and `BecuriSpate`/`TailLightsCar2` (`becuri`). Paint: `CarSkinController.EquipSkin` sets `_AccentColor`, `_EntireColor`,
  `_TertiaryColor` on the GMTK "Car Body Channel Paint" shader.
- Game scripts that react to the `Enemy` layer: `AreaBlast`, `ExplosiveBarrel`, `PlayerResetLogic` (all null-safe for our boxes).
- The game's decompile (0.419) was put in the scratchpad with `ilspycmd`; the Tokyo scene dump is in HalfGripRefs
  (`scenedumps/0.419/Tokyo`): the `Player` root holds `CarInterior` (INTERIOR layer), `PlayerModel` (the car) and the cameras.

## Working with the owner

- Ask for anything you need (access, files, a decision) at once, plainly. Do not guess, and do not quietly work around a refusal.
- Verify before claiming: say what you read yourself and what came from notes.
- Plain words about what the player sees.
- Show pictures you have looked at yourself.

## 0.32.1 (2026-11-24)

- **Physics cars drive into the tunnels.** The owner saw a highway car brake at a tunnel mouth: handed back to its timetable 100
  short of the tunnel, it became a loose car (on its brakes) the moment the player behind was about to touch it. Now a physics car
  is never handed back for a tunnel ahead: it drives on in (`TrafficDrive.Toward` keeps its steering point straight down the tunnel
  past the glue plane, where its line goes on at the far tunnel) and goes back on its timetable in the dark
  (`TrafficDrive.InDark`: its middle within 3 of the glue plane), early or late as it is (early is a negative lateness, drawn where it
  is), or is retired there if damaged. `TrafficCars` looks for that every frame between the choices (`Bubble(..., darkOnly)`): at
  speed a car is through the tunnel's dark well inside a choice's 0.4 s. Still no car made a physics car within `TunnelKeep` of a
  tunnel, nor handed to another player there. trafficview's physics cars do the same.
- **100 circling cars** (above). trafficview passes; the day builds in about 2.5 s instead of 1.6; the trunk's cars now wait a little
  at its uncontrolled junction with the Westhaven link (node 5, about 3 s a pass on average).

## 0.32.2 and 0.32.3 (2026-11-24)

- The frame rate lost with the 100 circling cars: two scans of a leg's whole path fixed and timings on the overlay. See
  `docs/PERF_HANDOFF.md`, "Done in 0.32.2".
- 0.32.3: this line of work (tunnels, 100 circling cars, 0.32.2) had started from `main` before PRs 50 and 51 (`main`'s 0.32.1: tyre
  marks, rest on the springs) were merged, and lacked them. `main` merged in, `main`'s tyre-mark and origin code kept. Always compare a
  branch with `main` before starting.

## 0.32.4 (2026-11-24)

- A build for measuring the frame: Unity's frame step by step, the mod's systems, the renderer's counts (`FramePerf.cs`), and
  trafficview's report of the highway as a player meets it. See `docs/PERF_HANDOFF.md`, "Done in 0.32.4". No change to the traffic.

## 0.32.5 (2026-11-24)

- The owner's 0.32.4 frame timings: the main thread's rendering (2,600 to 3,100 draw calls) is the largest cost; the traffic 0.1 to 0.2
  ms. Trees drawn in blocks, flat surfaces casting no shadows, the overlay cheaper. See `docs/PERF_HANDOFF.md`. No change to the traffic.
