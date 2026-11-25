# Handoff

The one handoff: **overwrite it** at the end of each session with where things stand and what is next. Don't append history; git keeps
it. Long-term goals and the owner's standing decisions are in `docs/PLAN.md`. Open in-game tests are in `docs/TESTING.md`. Finished
research is in `docs/research/`, for reference only.

Last written 2026-11-25.

## Where things stand

- **Branch: `traffic-test-branch`.** Work and push only there; never start a new branch. Before starting, look at every branch's
  latest commit (`git for-each-ref --sort=-committerdate refs/remotes`).
- **Version 0.33.0** (`mod.json`), plus unreleased work on top: traffic lights (logic in `TrafficSignals.cs`, drawn American style by
  `SignalPoles.cs` and `SignalHeads.cs`), right of way fixes, lanes no longer laid on lanes (`Network.Throughs`, meshcheck
  `alongside`), late drawn cars let off a stand with nothing to stand for (`TrafficLate.SkipStand`), **the highway reworked**,
  and the queues' fixes (below: close junctions as pairs, the lights seeing the whole queue). **The world changed** since 0.33.0, so everyone in a room needs the same build. Bump the version with the next release.
- None of the unreleased work has been tried in the game (`docs/TESTING.md` 58 to 61). The owner has seen the reworked highway in
  a screenshot (2026-11-25) and asked for the four fixes below, then for the four lights changes after them, then for the long
  queues to be looked into.

## The queues (2026-11-25, the owner: "investigate that", then items 1 to 5)

Waits are in real seconds: the traffic runs in real time and a game hour is 100 s (`Traffic.DaySeconds` 2400), so "900 s" is 9
game hours. Traced with a throwaway copy of `TrafficDay` that logs why each car near a node is refused each step (not committed: the
patch is easy to redo, see the end). What held the heads of the queues:
- **Short stretches let cars through one at a time.** `Room` wants a car's length clear past the box; Kingsbridge's nodes 132 and 8
  are 19 apart, so each car started from a stop: about 16 cars per 100 s (580 an hour) through the pair, the queue behind it from
  17:00 past midnight (seed 9). Fixed: `TrafficDay.PairedNext`, two junctions closer than their boxes where the car has the right of
  way at the second (or it has a light, or either is the trunk) are one: the room counted is past the second and the car is let in at
  both at once (the chain the trunk already had). `TrafficDrive` sees a car on such a stretch as coming to the second
  (`Mover.Then`): seed 14's known failure at node 8 is gone.
- **The lights ended the side's green with the queue still coming.** `RunLights` saw only a car asking for that node (within its
  grant reach, not yet let in), so the car held one junction back (Westhaven's link: two cars fit between node 233 and the trunk's
  stop line at node 5) came too late: 7 to 8 s of green, 6 cars per 100 s. Now any car within `Detect` of the stop line, across a
  junction in between and let in or not. (A first try counted a car stopped a hair past its line as through: a left turner at node
  18 never called its arrow and the trunk locked up; now past only 3 units into the box.)
- A car let in behind one that parks just past the box (`Room`'s `parking`) and, in `TrafficDrive`, the way out blocked by the
  player's car round the corner (`blockedOut` now looks along the way out for obstacles): both stood across other cars' ways in the box
  once the timing moved (seeds 8 and 13).
- Tried and dropped: the main line's green running on while its cars keep coming, up to 40 s (worse on seeds 1 and 8: 439 and 468
  minutes against 386 and 346).
- **Right on red** (PA allows it after a stop): in, but off (`TrafficDay.RightOnRedOn`, trafficview `TV_ROR=1`). Over seeds 1, 8, 9:
  386, 346, 435 minutes against 417, 342, 382, and seed 1's main line held up 104 times (limit 100). Ask the owner.
- Blinking lights at night (the owner asked): not needed for these queues (the midnight one was the one-at-a-time crawl, not red
  lights; the actuated lights rest on the main line's green already). Realism only; offer later.

All 16 seeds after (`TV_DRIVE_ONLY=1`): **all pass** (before: seed 14 failed). Against the build before, all 16 seeds: junction
waiting 5,559 minutes in all against 7,347 (-24%; every seed down, 231 to 508 a day against 294 to 654); but trips with a stand over
300 s 271 against 284 and over 600 s 63 against 64: **the long stands moved, they did not go**. Down a lot on seeds 2, 6, 8, 10, 13,
14, 15; up on 1, 4, 5, 9, 16, all at Kingsbridge's node 6 (below).

**What is left: node 6's light, Kingsbridge.** Every stand over 600 s is now at node 43 ((-2509, -4169), a lane junction) feeding
node 42, which is 85 from node 6 (a light, three primaries). The stretch 42 to 6 holds two or three cars; over half the cars on it
turn left at 6 (toward Pinewick) and wait for the oncoming cars in the only lane, the cars going on stuck behind them; the side road's
cars at 42 then have no room. A left-turn lane with an arrow at node 6 is the owner's idea and fits; it needs the road widened and
painted there (`RoadMesh`, `Network.HalfAt`), the day's lanes (`TrafficDay`: a bay as on the trunk), `TrafficDrive`, `SignalPoles`'
heads and the arrow phase for a light that is not the trunk's (`TrafficSignals.Arrows`). Asked the owner how they want it (a drawn
lane, or the lights alone giving that approach its own green). At node 6 the busiest movements turn the corner (42 to 45, 45 to 47)
rather than go along the main line chosen (42 to 47).

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
- Node 6's turn lane (above); then more circling cars in steps (300 choked the side roads' joins before the rework: measure again).

## Traffic leftovers

1. Node 6 (above).
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
- Fix bugs without asking whether to. Ask about design choices.
- Verify before claiming: say what you read or ran yourself and what came from notes.
- Use plain words about what the player sees. Show only pictures you have looked at yourself.
- The owner tests in game and reports with screenshots and `LogOutput.log`.
