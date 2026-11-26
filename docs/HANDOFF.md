# Handoff

The one handoff: **overwrite it** at the end of each session with where things stand and what is next. Don't append history; git keeps
it. Long-term goals and the owner's standing decisions are in `docs/PLAN.md`. Open in-game tests are in `docs/TESTING.md`. Finished
research is in `docs/research/`, for reference only.

Last written 2026-11-26 (evening).

## Where things stand

- **Branch: `traffic-test-branch`.** Work and push only there; never start a new branch. Before starting, look at every branch's
  latest commit (`git for-each-ref --sort=-committerdate refs/remotes`).
- **Version 0.33.0** (`mod.json`), plus unreleased work on top: traffic lights, right of way fixes, lanes no longer laid on lanes,
  late drawn cars let off a stand, **the highway reworked**, the queues' fixes, and now (below) **Kingsbridge's left-turn lane**, two
  room fixes and right on red measured and left off. **The world changed** since 0.33.0 (node 6's road is a lane wider; the
  traffic's day too), so everyone in a room needs the same build. Bump the version with the next release.
- None of the unreleased work has been tried in the game (`docs/TESTING.md` 58 to 62).

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

**What is next**:
1. Kingsbridge's queues: the northbound commute up the link through nodes 67, 49, 44 to node 6, and the side streets they block
   (node 67's street from the east 160 to 280 s a trip, node 42's and 49's lanes 40 to 120 s, node 2's street).
2. Then: turn lanes by a rule (a main road's way into a junction with left turners holding up the cars behind; room for
   two cars or more), `Road.Wide` made a list for two on one road; four-way stops and lights where the rules (or the owner) say, with
   Signs learning which junctions are controlled after the traffic map is built (they are laid out in `Network.Build` now, before it:
   lay out boards for every candidate and decide which to draw later; `Hidden` keeps the ground, so nothing moves).
3. The leftovers below, and seed 9's queue joining above.

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

1. Node 6's trade-off and the main-line hold-ups over the limit on 4 seeds (above).
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
- **Tracing why cars wait:** copy `OpenWorld/src/TrafficDay.cs` to the scratchpad, add a `Why` string to `Mover` set at each refusal in
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
