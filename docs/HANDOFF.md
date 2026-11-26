# Handoff

The one handoff: **overwrite it** at the end of each session with where things stand and what is next. Don't append history; git keeps
it. Long-term goals and the owner's standing decisions are in `docs/PLAN.md`. Open in-game tests are in `docs/TESTING.md`. Finished
research is in `docs/research/`, for reference only.

Last written 2026-11-26 (late night).

## Where things stand

- **Branch: `traffic-test-branch`.** Work and push only there; never start a new branch. Before starting, look at every branch's
  latest commit (`git for-each-ref --sort=-committerdate refs/remotes`).
- **Version 0.33.0** (`mod.json`), plus unreleased work on top: traffic lights, right of way fixes, lanes no longer laid on lanes,
  late drawn cars let off a stand, **the highway reworked**, the queues' fixes, and now (below) **Kingsbridge's left-turn lane**, two
  room fixes and right on red measured and left off; since, node 6's arrow off (a flashing yellow only, the owner's choice). **The world changed** since 0.33.0 (node 6's road is a lane wider; the
  traffic's day too), so everyone in a room needs the same build. Bump the version with the next release.
- None of the unreleased work has been tried in the game (`docs/TESTING.md` 58 to 62).
- **Next: "The new direction" below** (routes that know the waiting, the Kingsbridge link two lanes each way).

## 2026-11-26: how the waiting is shared, node 6's arrow off, the US rules for lights and stops

**trafficview now reports the waiting per trip** ("fairness:" line, after the long stands): per trip, the seconds stood still on the
road (queues included; `TrafficDay.LegStood`) and at the front of a queue (`LegAsked`): mean, median, 90th, 99th percentile, the most,
trips over 1 and 3 minutes; then the ten ways into junctions with the most standing (`StoodFor`), each with the trips through it and
their mean. Judge every traffic change by it (the owner: spreading the waiting evenly is fine, some people waiting much longer is not).

16 seeds each (`TV_DRIVE_ONLY=1`, ~23,540 trips a build): mean stood per trip / 90% / trips over 3 min:
- before the bay (ff6280a): 30.0 s / 62 / 664;
- room fixes, no bay (`TV_NOBAYS=1`): 32.5 / 74 / 798;
- bay with its arrow after 8 s (as committed in aaf89ba): 38.2 / 105 / 1,110;
- the same with a **lead left** (`TV_LEAD=1`: the bay's own arm keeps its through green beside the arrow): 38.1 / 105 / 1,120; it can't
  help, since the arrow holds the cars coming up from the trunk either way;
- arrow after 20 s: 37.5 / 106 / 1,128;
- **no arrow** (lefts permissive only): **31.5 / 74 / 765**: the best with a bay, a little better than no bay.
The arrow's green came out of the northbound commute (cars coming up from the trunk): their wait at node 44 doubled (22 to 45 s a trip)
and the queue blocked the side streets at nodes 49 and 67. So **`TrafficSignals.BayArrowDelay` is now infinite** (no arrow; the head
shows a flashing yellow arrow on the main green). `TV_ARROWDELAY=s` measures an arrow; `TV_LEAD=1` the lead left (code kept, off).

With no arrow, seed 9 fails one line: physics cars at the evening peak (player standing at (-2380, -4348)) waiting 134 to 228 s at node
49's lane from the east (-2213, -4590) joining the link's northbound queue, against 37 to 115 s for the same cars in the day: the
physics driver is less willing to join a queue past a junction (held for no room) than the day's cars. Not looked into further (the
junction work below will move these queues). Careful with `TV_DRIVE_NOTES`: each run's "long stop" lines come **before** its summary
line, so the lines after the first run's summary belong to the second run (the player's car stopped across a lane).

**The owner (2026-11-26): "we obviously just need more left-turn lanes and shit everywhere. There is no possible way that having
uncontrolled intersections is optimal."** Four-way stops are fine ("we can make the stop sign model later"). Standing decision now.

**The US rules on the day's counts** (`TV_WARRANTS=1`, tools/trafficview/Warrants.cs: MUTCD 2009 signal warrant 1, conditions A, B and
their combination, and 2B.07 multi-way stop, at the 70% volumes for country roads; an hour's count x 36 as an hourly volume, as the
PennDOT comparison does), 4 seeds: **of the 16 lights, 4 to 6 are warranted** (nodes 3, 5, 6, 8, and 14 and 33 by condition B); the
other trunk lights have side roads with 4 to 35 trips a day; **no new light and no four-way stop is warranted** (0 to 2 a seed, never
the same junction). The waiting is not at uncontrolled junctions out in the country: it is in Kingsbridge, the queues from its two
lights (nodes 6 and 8) backing through the close side junctions (42, 44, 49, 67, 2). Put to the owner (answers below).

**The owner's answers (2026-11-26)**: keep node 6's flashing yellow arrow only (no green arrow); and "both, Kingsbridge first": fix
Kingsbridge's queues first, then try lights and four-way stops at more junctions, keeping each that doesn't make the per-trip waiting
worse (docs/PLAN.md).

**Kingsbridge's queues, what was tried (2026-11-26, 16 seeds each, mean stood per trip / 90% / trips over 3 min)**. trafficview now
gives each busy way in its refusals at the front by reason (light, behind, crossing, gap, room, next; `TrafficDay.RefusedFor`). On
seeds 1 and 2 the side roads at nodes 42 and 2 are refused for **room**: the southbound queue at node 6's red backs past them; node
44's northbound cars for **next** (they would stand in node 44's box waiting at node 6); at node 6 the southbound way in also waits
**behind** and **crossing**: the bay holds two cars, and the left turners past them (giving way to the northbound) block the through lane.
- as committed (no arrow): 31.5 / 74 / 765;
- node 6 unlit (`TV_UNLIT=-2388,-4337`): 29.6 / 70 / 781, but the cars from Pinewick wait 68 s a trip at node 6 (12 to 28 lit) and back
  up through node 45; more physics fails. Node 6 and 8 unlit: 28.5 / 72 / 791, the same. **The light stays** (the owner's rule: not
  some people waiting much longer; and the US rules warrant it);
- `TV_MAINMIN=35`: 33.3 / 83 / 749; `TV_MAINMIN=15`: 32.8 / 79 / 867; `TV_SIDEMAX=18`: 31.5 / 76 / 705 (more seeds fail). **Timings
  kept** (they apply to all 16 lights).
The bay's overflow is about 6 of seed 1's 380 junction minutes. What is left is the link's capacity: one lane each way through six
junctions in 600 units with a light in the middle.

**The owner on the road design (2026-11-26, late)**, after an overhead picture of the link (`jview at` with `JVIEW_TEX=1
JVIEW_CLEAN=1`, centred (-2370, -4440), span 700; and node 6 at span 220): "looks like there's plenty of space". No centre turn lane
("extremely dangerous and hard to read"). **A turn lane may run back through the junction before it, marked as a through lane there**
("we can have the left lane extend beyond a junction if its marked as a left/straight lane at the first one"). "I just want the
traffic to work and the roads to feel realistic": the engineering calls are ours, judged by the per-trip waiting. "We are gonna need a
bunch of signs eventually": keep a list of the signs each change needs (below). Both in docs/PLAN.md.

Why turn lanes at the light and not elsewhere: on seed 1 the light itself holds the cars (refused for **light** 14.6 min southbound at
node 6, 8.4 northbound, 8.1 from Pinewick), against 1.6 min of the northbound held by turning cars; the side roads wait for **room** past
the queue. So the gain is in giving the light's queues room and lanes, not in turn lanes at the small junctions.

**What is next: superseded.** The junction-by-junction plan (node 6's lanes, the Pinewick road split, bays at nodes 49 and 67) is
replaced by the direction in the next section ("The new direction"). Its southbound half was tried and reverted (the section after).

**Signs needed** (for when there are models): stop signs at four-way stops; "LEFT LANE MUST TURN LEFT" (and right) before node 6's
lanes; lane-use arrows (straight; straight/right; left only) at nodes 42, 44 and 6, on the road or overhead.

## The new direction (the owner, 2026-11-26, late night): the routes and the road, not junction by junction

**Why.** Every junction fix so far moved the queue to the next junction (and turned up a different single failure each time). Two
causes, both checked in the code:
1. **Routes never react to waiting.** `Traffic.Route` is Dijkstra on each edge's free-flow `Time`: no junction delay, no lights, no
   left turns across traffic, no queues; chosen once per trip. Every trip whose empty-road quickest way uses the Kingsbridge link takes
   it however long the queue, so room added at one junction fills and the queue moves on.
2. **The link is too small for what the map sends down it.** One lane each way, six junctions in about 600 units (180 m), a light in
   the middle: the main town's way to the trunk. The waiting is almost all there (nodes 2, 42, 6, 44, 49, 67). The southbound try showed
   the light's green, not room to queue, is the limit.

The owner put to it: "Do we just need to act on a wider area instead of addressing each junction individually?" Agreed. Then: building
the day twice is fine ("the load-time is already very short"), and **"we can just go ahead and add another lane as well"**: the link
gets two lanes each way. Both are now standing decisions (docs/PLAN.md). Discussed only; nothing built yet.

**1. Routes that know the waiting: the day built twice.**
- Pass 1 as today. From it, learn the delay per junction movement (node, way in, way out) **per hour** (the queues are at the peaks: a
  driver at 08:00 sees 08:00's queue, at 14:00 a clear road). `TrafficDay` already keeps per-way-in standing (`StoodFor`, `WaitAt`,
  `LegStood`); it needs it by movement and hour.
- Pass 2: re-route with those delays added to the route cost (`Traffic.Route` takes a delay lookup; a turn's cost needs the edge
  before, so Dijkstra over (node, way in) states or a turn penalty at relaxation). **Only part of the trips re-route** (those whose
  pass-1 trip waited long, or a fixed share by hash), or everybody piles onto the same side route; if two passes still swing, a third
  averaging the delays (method of successive averages). Keep each pass deterministic (same seed, ties by index), so rooms agree.
- **Only the routes change**: the same tours, places, times and parking.
- **Cut-throughs**: a small extra cost on streets and lanes so cutting through housing streets must save real time; check trafficview's
  PennDOT counts by road class don't drain off the main roads.
- **Time it**: the day's build alone, before and after (not measured yet: trafficview's minutes per seed are mostly its checks). The
  day is built on a worker at race start (`TrafficCars`).
- Judge by the whole map's fairness line on 16 seeds (as committed: 31.5 s a trip / 90% 74 / 765 over 3 min), plus how many trips
  changed route and where the traffic went.

**2. The Kingsbridge link two lanes each way.** From the town (node 2, (-2290, -4139)) down to the trunk, through nodes 42, 6, 44,
49, 67 (check where it meets the trunk; `jview` road listing near a point). Notes:
- Road 3 'Kingsbridge link' is a Primary. The cleanest is a whole-road property (two lanes each way: `Half` wider by a lane each side,
  `Traffic.LaneOffset` and the lanes as the trunk's two-lane logic: `TwoLane`, `twoLaneEdge`, `LaneAt`, overtaking and keeping right),
  rather than another `Wide` stretch. `twoLaneEdge` today is `Kind == Trunk`: make it a road flag. The paint: a dashed white lane line
  each side of a double yellow (no median, no centre turn lane: the owner); the texture or `RoadMesh` lines.
- **The world changes**: a wider road moves plots, houses, lots and parking (`Widened`, `Plots`, `Town`, `TrafficParking`). Expect every
  day to change; run meshcheck (all of it), preview and the 16 seeds.
- Node 6's bay: with two through lanes the left-turn bay sits inside them (the road widened again) or the inner lane becomes left-only
  at the light. Decide when building (measure).
- Lights on a two-lane way in: `SignalPoles` hangs a head per lane (it does on the trunk); a span where the heads reach past 30.
- Which other roads, if any, get the same rule: the generator should give any road this busy the capacity real engineers would, not
  just this one. Measure after routes (item 1) shows how much traffic really needs it.

Order: the routes first (cheaper, and shows how much the road matters), then the lane; or both, measured apart (16 seeds each) so each
change's effect is known.

## 2026-11-26 (night): the southbound left lane through the junction before the light, tried and reverted

Item 1's southbound half was built (commit 8140277, reverted in the next commit; the code is there to bring back): `Network.Bay` let a
bay run back through the junction behind (`TurnBays.Bay.Through`) when the second junction behind leaves room, its widening's taper
(`ShortTaper`) just past node 2; at node 6 the bay became 72 to 198 along road 3 (was 155 to 198), node 42 (136) inside it, about six
cars. `TrafficDay`: `bayEdge` on every edge the bay overlaps; `IntoBay` lets a car in on the edge before node 42 if it goes straight at
node 42 and left at node 6; `OutLaneOf` keeps a bay car in the bay across node 42 (both the request and the chained request).
`RoadMesh.BayPaint`: the bay line dotted up to past node 42, then solid; a straight-ahead arrow before node 42 (`AheadArrow`; `BayArrow`
takes the arrow's parts and front). Looked at with jview: right.

**16 seeds (`TV_DRIVE_ONLY=1`): worse.** As committed 31.5 s a trip / 90% 74 / 765 over 3 min (reproduced exactly); with the lane 32.8 /
78 / 849, and seeds 4 (right of way refused 117, limit 100), 5 and 7 (physics at the evening peak) fail. Node 6 southbound stood 518 to
652 min, node 44 northbound 384 to 484, node 6 from Pinewick 181 to 243; only node 2's street better (1,108 to 1,000). The refusals at
node 6's front hardly changed (light 216 to 226): the extra waiting is in the queue. **Fewer cars got into the bay (614 to 562 over the
16 days)**: the bay fills from the north, so the left turners coming from node 42's lane (and those stuck behind the bay's short start
at 72) can't get in, and wait to turn left in the through lane, holding up the cars going straight. The light's capacity, not storage,
is the limit: the southbound way in is refused for **light** far more than anything else.

Not tried: the northbound right lane (item 1's other half). It needs a road widened on one side only (today's widening is symmetric:
`Network.HalfAt`, `Point`, the cuts, `RoadMesh`, and every user of `Widened` assume the centre line is the middle). With what the
southbound half showed (storage doesn't add green), it is unlikely to pay for that work; ask the owner before starting it.

## The owner's lights report (2026-11-25)

From a screenshot at the Kingsbridge - Fenwick primary road's T on the trunk (node 10, (1053, -10213)), stopped on the primary:
- **No signs in front of the lights; green PA signs on the masts instead.** A fingerpost stood in front of the side road's pole.
  Now `Signs.Lit` (the lights' rule on road classes alone: 3 or 4 ways, the second best a primary or better, the third a secondary or
  better; the lights themselves are only known once a day is built, long after the signs) marks a lit junction. Its fingerpost is
  still laid out but `Hidden` (not drawn, its ground kept): **dropping it outright let one house in, which moved every day of traffic
  and turned 4 more seeds of trafficview red** (Kingsbridge's queues, leftovers 1 and 2), so the world is left exactly as it was.
  Its places go on a green board per way in (`Signs.Overheads`: white letters, border and 7 px arrows on green, left then ahead then
  right as the drivers see them, at most 5 lines, 0.12 a pixel), which `SignalPoles.Board` matches to each mast and `SignalMesh.Board`
  hangs under the arm: beside the pole if it fits, else past the last head (the arm made longer), on a span in the widest gap.
  trafficview fails a lit junction with a fingerpost or a hidden one with no lights (0 and 0: the rule agrees with all 16).
- **Spans** (`Mast.Span`): where the heads reach past 30 (the trunk's), a level box truss on two poles across the whole road (87
  units) if the far verge is clear: 9 of the 16 trunk ways in. No other pole stands on a span's far pole.
- **Arrows**: the bay head's arrows were drawn but too thin to read; now bold, filling the lens. The heads in the screenshot were the
  side road's (one lane, no bay), which have round lamps like any single-lane way in: ask the owner if they want arrows there too.
- **Red**: `(1, 0, 0.03)` glowing 1.9 (was `(1, 0.10, 0.06)` at 3.2, which went orange on screen).
- `SignalMesh` moved out of `SignalHeads.cs` into `SignalMesh.cs` (meshcheck compiles it now). The meshes were looked at with a
  throwaway software renderer from the stop lines (not committed); not yet seen in the game.

## The owner's highway report (2026-11-25)

From a screenshot of a lit junction on the trunk (around (1267, -10269)):
- **The paved median looked wrong** (coarse stripes across the road): `RoadMesh.Paint` stretched one texture column across the
  openings and bays, so each row's grain ran right across. Now `RoadMesh.Pave` cuts the asphalt into strips under 10 wide, each
  sampling the texture's plain columns 4 to 13 at a texel a unit, as the lanes beside it.
- **Stop lines at the lights**: `SignalPoles.StopLine` (pure) gives each way in's line: 2 wide (24 in), its far edge a unit inside the
  box's edge (`TrafficDrive` holds a car's front a unit short of the box), across the cars' side of the road (centre line to a unit
  short of the edge; on the trunk from the median's yellow line, or from the bay's far side where there is a bay, to the edge line).
  `SignalMesh.StopLine` draws it into the mast's mesh (white palette texel), so it shows with the mast. trafficview fails a way in with
  a mast and no stop line. A way in with no mast (none now) would get no line. Where the edge is shorter than the box (Kingsbridge's
  node 8, 19 past node 132) the line runs back along the same road, inside node 132's box, where the cars stop.
- **No solid lines where drivers cross them**: the trunk's white edge line is paved over across each side road's mouth (the side
  road's half width plus 8 each way, both sides at a crossroads); a bay's white line is dotted (3 on, 9 off) over the taper and 60 more,
  solid from there to the junction. The lane dashes still run through the junctions (dashed, as a guide).
- **Traffic popped in too near**: `TrafficCars.MovingReach` 900 to 1500 (450 m), `ParkedReach` 450 to 750 (230 m), `MostDrawn` 160 to
  240; trafficview's copies (`Lateness`, `Highway`) follow. Far cars are instanced, so the cost should be small: ask the owner for
  the frame line (`docs/TESTING.md` 60).
- Looked at from above with `JVIEW_TEX=1` (jview draws texture columns without grain, so it can't show the first fix): the edge line's
  gap at the Westhaven link (11316, -9288) and the dotted taper east of it.

## The highway reworked (2026-11-25)

The owner's brief: no overpasses or underpasses; lights where needed are fine; "the median is not meant to be used as a lane"; the
on/off lanes and the bots using the median as a turning lane were the problem; dedicated left-turn lanes with left-turn arrows at
lights; K-rails here and there; a wide grass median in places; the median mostly open so players can cross into the oncoming traffic;
keep the general route and the wrap. Standing decisions are in `docs/PLAN.md`.

What was done (same 80 units across, so the land, routes and other roads are unchanged):
- **Cross-section** (`TrunkLanes`' constants, used by `Traffic.LaneOffset`, `Courses.Carriageway`, the spawn, the texture): a grass
  median 24 wide, flush with the road (drawn in the texture, so it drives like the road), a shoulder of 2 and a yellow line, two
  lanes of 11, a shoulder of 4 (was: a concrete median 10 wide and shoulders a lane wide for the shoulder lanes).
- **No shoulder lanes, no median merge lanes.** `TrunkLanes.Zones` holds only left-turn bays: one per junction and carriageway that
  turns left there, back 520 from the junction's mouth, never past the junction before it, on its own side of the centre line.
  `RoadMesh.TrunkPaint` paves the median at each junction's opening and each bay (taper at its far end, white line beside the inner
  lane, yellow on the median side; lines lifted over the bay's asphalt so they don't fight).
- **Traffic** (`TrafficDay`, `TrafficDrive`): a car turning left moves into the bay and waits there; a car from a side road joins or
  crosses in one go (its gap: `LeftGap` or `AcrossGap`), never in two stages; no more `AuxIx`/`EntryIx`.
- **Left arrows** (`TrafficSignals`): at lights whose main line is the trunk, a protected arrow phase (steps 3 to 5 of 9) for both
  bays together, only when a car waits in one (`ArrowMin` 6 s, `ArrowMax` 20 s); the trunk's lefts go only on it. `State` and
  `Apart` take the movement (in and out arm). `SignalPoles` hangs the bay's head only where there is a left to make (`Mast.Arrow`);
  `SignalHeads` draws it with arrow lenses and lights them from the arrow's state. Physics cars held at an arrow with no day car to
  call it go after `LightGiveUp`, as on a side road.
- **K-rails** (`TrunkLanes.Rails`, `RoadMesh.KRail`): Jersey profile, concrete, in the road mesh (so solid), on about half the stretches
  with room for one 450 clear of junctions and 60 clear of bays: 5 rails, 1.4 km in all, 14% of the trunk (listed by meshcheck
  `median`, e.g. round (12229, -8922) and (4393, -10413)).
- **Checks:** meshcheck `median` (new); jview `JVIEW_TEX=1` draws the trunk's markings. Looked at from above: the Westhaven link's
  junction (11316, -9288) and the rail at (12229, -8922).


Checks after the rework (2026-11-25): `tools/check.sh` builds; the whole meshcheck passes (the new `median` too; `faces` caught the
K-rails' first, two-sided faces); preview passes. trafficview `TV_DRIVE_ONLY=1` on the 16 seeds: 15 pass, **seed 14 fails one line**,
4 physics pairs crossing in a box (limit 3), all at Kingsbridge's node 8 (see the leftovers). Junction waiting 294 to 654 min a day
(seed 1: 436, against 406 before the rework); 84 to 122 cars a day use a left-turn bay; none merge out of the median any more (224 did
on seed 1 before).

Also fixed: a physics car let go on its green no longer ignores a car on the other phase that is too close to stop for its amber
(`TrafficDrive`, the `Apart` skip): seed 15's box crossings went from 4 to 2.

Possible next steps (ask the owner):
- More or longer K-rails, a wider median in places (the width is constant now; varying it changes `Network.HalfAt` and everything
  under it), rumble strips, a turn arrow painted in each bay.
- Right-turn bays at the lit junctions (none now: a right turner slows in the outer lane).
- More circling cars in steps (300 choked the side roads' joins before the rework: measure again).

## Traffic leftovers

1. With node 6's arrow off all 16 seeds pass but seed 9 (the physics cars' queue joining at node 49, above); the main-line hold-ups
   over the limit went with the arrow.
2. Physics cars "in each other": 0 to 4 a seed (limit 6).
3. A physics car brushed the player's stopped car (seed 11): not looked at.
4. Then the owner's test in the game (`docs/TESTING.md` 58 and 59).

## How to run the checks here

- **HalfGripTools.dll** isn't here. Build the stand-in: `dotnet build tools/halfgriptools-stub -c Release -o <scratchpad>/stub`, then
  `tools/check.sh ../HalfGripRefs/game <scratchpad>/stub/HalfGripTools.dll`.
- The tools need `-p:GameDir=/home/user/HalfGripRefs/game` (the refs repo cloned next to this one).
- **16 seeds of trafficview:**
  - Build it, copy `tools/trafficview/bin/Release/net8.0` to the scratchpad, and run the copy 4 seeds at a time in the background:
    `TV_DRIVE_ONLY=1 TV_DRIVE_NOTES=1 dotnet <copy>/trafficview.dll --seed N > sN.txt`.
  - All 16 take about 20 minutes. Don't rebuild while the loop runs.
  - From each report, take one line per seed: junction waiting, overlaps, box conflicts, stopped across, physics pairs, physics box
    crossings, stops over 90 s, FAILs.
  - Every fix so far moved the day and turned up a different single failure somewhere else: rerun all 16 after each change.
- **Comparing builds:** run 16 seeds into a folder per setting (the env switches: `TV_NOBAYS`, `TV_LEAD`, `TV_ARROWDELAY`, `TV_UNLIT`,
  `TV_MAINMIN`, `TV_SIDEMIN`, `TV_SIDEMAX`, `TV_ROR`, `TV_WARRANTS`) and pool the "fairness:" lines: trips-weighted mean, the
  percentiles averaged over seeds, the over-1/3-minute counts summed, the FAIL lines; and the ways in by node (a short Python script
  over the reports did it; it was in the scratchpad, not kept). A setting that drops a feature fails its own check by design (no bay:
  the bays line; a light off: the signs' line).
- **Tracing why cars wait:** the fairness lines now give each busy way in its refusals by reason, which answers most of it. copy `OpenWorld/src/TrafficDay.cs` to the scratchpad, add a `Why` string to `Mover` set at each refusal in
  `Junctions` (red, order, cross, gap, room, stuck) and a dump after `Junctions(requests, refused)` in `Simulate` of the movers on
  edges touching chosen nodes every N steps; build it with trafficview's sources in a scratch project. It answered every question here.
- How long things take: meshcheck, all of it, about 1 min 45 s; the layout in trafficview about 7.5 s. Anything longer than 2 minutes
  goes in the background with its output written to the scratchpad.
- `convert` (ImageMagick) turns the tools' PPM pictures into PNG.
- Decompile: `~/.dotnet/tools/ilspycmd` with `DOTNET_ROLL_FORWARD=Major`. The scene dumps are in HalfGripRefs (`scenedumps/0.419`).
- The network allows `www.fhwa.dot.gov`, `rosap.ntl.bts.gov`, `gis.penndot.pa.gov`, `nhts.ornl.gov`. Use `curl` (the page reader
  can't resolve them, nor pa.gov).

## Working with the owner

- Ask for anything you need (access, files, a decision) at once, plainly. Don't guess, and don't quietly work around a refusal.
- Fix bugs without asking whether to. Ask about design choices, but only ones the owner can answer: they judge by what players see
  and feel (the waiting, the look), not the engineering. Where it is an engineering call ("what makes sense"), make it and say why.
- Plain words: no "node", "arm", "pair", "link", "box" with the owner; name the place ("the light where the road to Pinewick leaves
  town") and give averages per trip.
- Verify before claiming: say what you read or ran yourself and what came from notes.
- Use plain words about what the player sees. Show only pictures you have looked at yourself.
- The owner tests in game and reports with screenshots and `LogOutput.log`.
