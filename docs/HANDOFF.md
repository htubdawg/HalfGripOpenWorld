# Handoff

The one handoff: **overwrite it** at the end of each session with where things stand and what is next. Don't append history; git keeps
it. Long-term goals and the owner's standing decisions are in `docs/PLAN.md`. Open in-game tests are in `docs/TESTING.md`. Finished
research is in `docs/research/`, for reference only.

Last written 2026-11-26.

## Where things stand

- **Branch: `traffic-test-branch`.** Work and push only there; never start a new branch. Before starting, look at every branch's
  latest commit (`git for-each-ref --sort=-committerdate refs/remotes`).
- **Version 0.33.0** (`mod.json`), plus unreleased work on top: traffic lights, right of way fixes, lanes no longer laid on lanes,
  late drawn cars let off a stand, **the highway reworked**, the queues' fixes, and now (below) **Kingsbridge's left-turn lane**, two
  room fixes and right on red measured and left off. **The world changed** since 0.33.0 (node 6's road is a lane wider; the
  traffic's day too), so everyone in a room needs the same build. Bump the version with the next release.
- None of the unreleased work has been tried in the game (`docs/TESTING.md` 58 to 62).

## Node 6's turn lane and right on red (2026-11-25, the owner: "For node 6, install the actual turn lane" and "I'd like to keep right on red when it helps")

What was built (`TurnBays.cs`, new):
- **The road**: `Network.Bays` (before `Junctions`, so mouths are cut against it) finds the T of main roads at each of
  `TurnBays.Sites` (only node 6, (-2388, -4337)) and widens the Kingsbridge link by a lane (`Road.Wide`, `WideA0..WideB1`,
  `Network.WideAt`, `Network.Widened`; `HalfAt` includes it): full from just north of node 42 (the lane junction 85 behind) to just
  past the T, tapers 92 north and 50 south, fitted between the neighbouring junctions. The road index's paving tests use the widened
  width, so the side roads' mouths meet its edge; plots, lots, towers, kerb spaces and poles stand back from it.
- **Drawn** (`RoadMesh.WideStrip`, `BayPaint`): the texture's two halves pushed out to the edges, plain asphalt in the middle at the
  texture's scale; two yellow lines (a painted median) coming together over the tapers; the bay from past node 42 to the stop line
  (43 long: two cars) with its white line dotted 20 then solid and a white left arrow; no lines across the mouths. Looked at from
  above with `JVIEW_TEX=1` (jview now textures main roads; `jview bays` lists the bays).
- **Driven** (`TrafficDay`): `Laned` (the trunk, or the way up to a bay) gives the bay its own lane key (`MedianIx`); `IntoBay` moves
  a car turning left at the bay's junction into it when there is room (it never moves back out; a car finding it full keeps to the
  through lane and tries again). Drawn cars: through lanes half the widening out (`OnPath`), the bay on the centre line (`BayLane`).
  `TrafficDrive` uses `Laned` too.
- **Lights** (`TrafficSignals`): `BayArm` per lit node; the bay's left is on `ArrowPhase` but **protected and permissive**
  (`Permissive`: it also goes on the main line's green, giving way; `State` green then, `Protected` false; `Apart` keeps it with the
  main line); the arrow is called only after a left turner has stood waiting `BayArrowDelay` (8 s). `SignalPoles` hangs three heads
  over the way in (two for the through lane, the arrow head over the bay), the stop line across the bay; `SignalHeads` shows a
  flashing yellow arrow while permissive.
- **Checks**: meshcheck `bays` (each site has its bay, a lane wider all along it, no other road meeting the widened stretch);
  trafficview reports the bay (at a light with an arrow for it, cars into it; `TV_NOBAYS=1` drops it, `TV_ARROWDELAY=s` to measure).

Also fixed on the way (found tracing the gridlocks the bay set off):
- **`Room`**: a car let in needs room to stop clear of the box behind the car in front with the standstill gap (`MinGap`, 7; it was
  4), and the room ends at the next junction's stop line (it counted the whole edge). Two cars let into node 42 one behind the other
  stood one short of node 6's line, the second in node 42's box, and nodes 42 and 6 locked each other up for the rest of the day.
- Not done: the junction's box is one size for all its ways in; widening the link pushed node 6's to 29 (the side road meets it at a
  slant), and that alone cost seed 1 about 250 minutes, so `Traffic.ClearOfOthers` keeps the road's own width: a car waiting on the
  road to Pinewick stands about 2 onto the widened shoulder in the acute corner, clear of the lane. A stop distance per way in would
  fix it properly (about 30 uses of `Node.Box`).

The numbers, all 16 seeds (`TV_DRIVE_ONLY=1`), junction waiting / trips with a stand over 300 s / over 600 s:
- before today: 5,559 / 271 / 63, all pass;
- the room fixes alone (no bay, no right on red): 6,087 / 280 / 59;
- **as committed (bay with its arrow, right on red off): 7,204 / 154 / 57**; 4 seeds fail one line, main-line cars held up by a car
  giving way 102, 107, 114, 115 (limit 100; seed 16 was at 106 with the room fixes alone): mostly node 5's (a side car still in the
  box when the trunk's green comes, as before) and node 6's permissive lefts;
- the bay with permissive lefts only (no arrow): about the same as no bay (6,524 / 283 / 73 with right on red on);
- the arrow at once (no delay): gridlocked seed 1 before the room fixes; worse than the delay after.
So the bay halves the long stands but adds about 18% junction waiting: with it the southbound lefts get served, and that green
comes out of the northbound morning commute up the link (the queue now backs up through nodes 44, 49, 67).

**The owner's answers (2026-11-26)** to the questions put after this work:
1. Which is better, fewer long stands or less waiting in all: "it would matter what the average wait time is for everyone. If we
   just spread the wait time out mostly equally it's probably fine, but if it's just different people now waiting longer, that's
   dumb." So judge a change by **how the waiting is shared among the trips**, not the day's total alone. trafficview doesn't report
   that yet: add, per seed, each trip's junction waiting (mean, median, 90th and 99th percentile, the most), and who waits (by node
   and way in), then compare the build before the bay, the room fixes alone and the bay. The totals so far say the waiting got more
   even overall (stands over 300 s from 280 to 154) but that the northbound commuters on the link now wait longer: exactly the case
   the owner calls dumb if it holds up. Decide the bay's phasing from that (below).
2. Whether node 6's main line should be the busiest pair: the owner didn't follow the jargon ("node, pair, link"). **Use plain words
   with the owner**: "the junction where the road to Pinewick leaves Kingsbridge's road to the highway", "the cars coming up from the
   highway into town". Only re-ask if the measurement shows it matters, as: "most cars at that light turn the corner between the
   town and Pinewick; should the light treat that as the main road?"
3. A longer turn lane: "I only want what makes sense to have." The engineer's call: do what a real road would have and the numbers
   back.
4. More turn lanes: "they should probably go everywhere it makes sense to have one." Standing decision now (docs/PLAN.md).

**What is next** (in this order; plain words to the owner, show the numbers as averages per trip):
1. trafficview: the waiting per trip as above (one line per seed, totals over 16), to judge every change below by.
2. Node 6's light: try a through green for the cars coming down from town during the turn arrow (the left turners and the cars
   going on together, the cars coming up held for the arrow only), the arrow's delay and its longest green; keep what makes the
   waiting fairest per trip. If the commute up from the highway still pays for the turn lane, say so to the owner plainly.
3. The turn lane's length: it holds two cars. Carry the widening further north (the taper toward node 2) and let the lane run back
   through the lane junction behind it (node 42) as a centre lane, if the per-trip waiting says it helps.
4. Turn lanes everywhere they make sense: a rule instead of `TurnBays.Sites` (a lit junction off the trunk where a main-line way
   in has many left turners and room for two cars or more between junctions), lit junctions first; then unlit ones only where the
   left turners hold up the main line. The rule's T's of main roads with room: node 8 (the link's junction with the road to
   Redford; its other way in is 19 long, so one way only) and Northcote's road north meeting Millbrook's (-2885, 5832). A road can
   only be widened once now (`Road.Wide` is one stretch): make it a list for two bays on one road (node 6 and node 8 are both on the
   link). Look at each with `jview` (JVIEW_TEX=1) and keep meshcheck `bays` and `clear` passing.
5. A stop distance per way in (`Node.Box` is one per junction), so a side road meeting a widened road at a slant stops further
   back without pushing back every way in (cost seed 1 about 250 minutes when tried).

**Right on red**: measured on 16 seeds in each setting (also turning only into a gap 1.5 s more than a give way's, and counting the
cars standing at the main line's stop line, so none is let go in the all red and caught by the main line's green): it never helped
in total (6,347 against 6,087 without the bay, 6,851 against 6,524 with the permissive bay) and held main-line cars up over the limit
on more seeds. Seed by seed it helps some (6, 10, 13, 15, 16) and costs others (1, 4, 8, 12). So it is **off**
(`TrafficDay.RightOnRedOn`), its fixes kept; trafficview `TV_ROR=1` measures it. Tell the owner.


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
