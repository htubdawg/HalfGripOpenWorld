# Handoff

The one handoff: **overwrite it** at the end of each session with where things stand and what is next. Don't append history or dated
session notes; git keeps them (`git log -p docs/HANDOFF.md`). Standing decisions go in `docs/PLAN.md`, open in-game tests in
`docs/TESTING.md`, finished research in `docs/research/`.

Last written 2026-11-26 (night).

## Where things stand

- **Branch: `traffic-test-branch`.** Work and push only there; never start a new branch. Before starting, look at every branch's
  latest commit (`git for-each-ref --sort=-committerdate refs/remotes`).
- **Version 0.33.0** (`mod.json`) plus unreleased work: traffic lights (green PA boards on the masts, spans over the trunk, stop
  lines), the highway reworked (grass median, left-turn bays, K-rails; no shoulder or median lanes), Kingsbridge's left-turn lane at
  its light (flashing yellow arrow only), right of way and queue fixes, routes that know the queues (the day built twice), the race
  seed in the log, and the far traffic pop-in fix. **The world changed** since 0.33.0 (node 6's road is a lane wider) and so did the
  traffic's day: everyone in a room needs the same build. Bump the version with the next release.
- **The highway pop-in is fixed** (the owner confirmed): far cars were drawn on the Enemy layer, which the main camera doesn't draw.
  If a pop-in is reported again, read the verbose `Traffic pop-in:` lines first (`TrafficPop.cs`): a count of 0 with cars still
  popping means they are drawn but unseen (rendering). trafficview's `PopIn` check (`Highway.cs`, `TV_POPLIST=1`) covers the choosing.
- **Parked (the owner: "pick it up later"): far traffic instancing.** Instanced far cars wore the wrong paint and their tail lights
  showed through the body from the front. `TrafficInstanced` defaults to `false`, and `TrafficFar.CanInstance` never instances the
  game's paint shader (GMTK/Car Body Channel Paint) even with it on. When it's picked up, suspect the paint shader's per-instance
  colours or a pass (depth prepass) that doesn't follow the instances. Try a `MaterialPropertyBlock` with per-instance colour arrays.
- **Paused until the standing-still fix is done:** four-way stops and more lights (the owner said try them; `docs/PLAN.md`).

## Now (2026-11-26, late): half the local traffic (the owner: "use less cars until you can figure this out")

- **The owner meant the cars on the road, not the physics cars** ("the only cars that matter are the physics cars"). I first turned
  the physics cars off by default; that was wrong and is undone (`TrafficPhysics` 24, held-car extra 16, as before). **Never cut the
  physics cars to dodge a bug.**
- `Traffic.TrafficAmount = 0.5` scales `DrivingShare`: half the survey's local trips (through traffic on the highway not cut). It
  changes the day: everyone in a room needs the same build. On the owner's seed with `TV_AT` below: 1466 trips to 810, physics cars
  meeting 3 to 1 (none at node 6), stood still per trip 16.9 to 9.6 s. Back to 1 once the junctions are right; trafficview's PennDOT
  and TPG comparisons now read about half (reported, not failed).
- **Why:** the owner's report "cars stuck here" (race seed 1596659352, weekend, 11:17, at Kingsbridge's light where the Pinewick road
  comes in, node 6). Reproduced with the new `TV_AT=-2312,-4305,11.1` (stand at a place from an hour for 10 minutes): a physics car
  coming in from the Pinewick road on green turning south (arms 2>0) meets one going north (0>1) still in the box past its middle.
  `TrafficDay.Crosses` (order round the junction) says those ways don't cross; on the ground they do (the roads meet at about 60
  degrees and the road north is widened for the bay, its lane shifted toward the Pinewick road). The knocked car stays across the line
  and the queue behind it can't go. With 6 + 2 physics cars it still happened (5 meetings that run; 3 at 24 + 16).
- **Tried and taken out the same day:** a stop-back per way in (cars stopping 6 short at node 6's Pinewick arm and node 42's arm 2,
  clear of the widened lanes): the moving cars still met. A crossing table from the drawn ways' shapes (bodies overlapping anywhere along
  two movements' ways): flagged 737 junctions, nearly all wrongly (cars passing on narrow roads, and the timing isn't looked at), and
  still missed node 6's pair (the bodies met with their middles 9 apart). **Next:** a proper fix is a conflict found by the two cars'
  ways **at the same time** (where each is when), or a box per way in (`Release` holding the grant until the car is clear of the
  Pinewick road's mouth). Measure with `TV_AT` on the owner's seed, then the 16 seeds.

## Fixed 2026-11-26: cars round the corner (late cars "parked at (0, 0)" in the ledger test)

That note printed an away (out of sight) parking spot, so those cars weren't drawn at the world's middle. But a car pulling out of an
out-of-sight space was drawn on its lane from its trip's first moment, and a held one went back to its unseen space and vanished, or
stood in the lane where others drove. Now (`TrafficLate`): `RoundTheCorner` (not drawn until it has set off: T 0.5), `Appearing` and
`StartBlocked` (it waits there while it would come out into a drawn car or in front of one that couldn't stop), `CanHoldBack` (only a
car still in its space is held back into it; one pulling out or waiting on its lane is held where it is), and `Yield` never draws such a
car back into its unseen space. Game (`TrafficCars.Draw`, `Seen`, the choice) and both trafficview tests the same. The ledger test now
counts cars a hold would hide ("hidden by a hold", must be 0), and Drive.cs too ("hidden by it"). Seed 1's ledger test: overlapping
because late 315 to 14 steps, hidden 0; what is left is late cars crossing at node 47. 16 seeds of `TV_DRIVE_ONLY=1` with it: no crash,
fairness unchanged (mean 24.0 s), the known FAILs.

## Fixed 2026-11-26: all the traffic vanished at once (the owner's third report: "a car vanished as I collided with it")

The log: `The traffic stopped: InvalidOperationException: Collection was modified` in `TrafficLate.SpaceTaken` (added last session).
It walked the ledger while `At` dropped an entry for a car that had caught up. .NET 8 lets a Dictionary lose entries mid-walk; the
game's Mono throws, and the throw stops all the traffic (every car gone). trafficview runs on .NET 8, so it never saw it. Fixed: the
walk goes over a copy of the keys. And the ledger is now a `StrictMap` (end of `TrafficLate.cs`) that throws on any change mid-walk on
both runtimes, so trafficview fails the way the game does. **Watch for this class of bug:** any `foreach` over a Dictionary whose body
can remove, add or set an entry (directly or through a call) passes trafficview and crashes the game, unless the map is a `StrictMap`.
The other loops over Dictionaries in the traffic code were read: they collect what to do and act after the loop.
Checked on the owner's seed (`TV_DRIVE_ONLY=1 --seed 1922639714`): the old loop with the strict map crashes trafficview with the
game's exception; the fix runs to the end (its FAILs are the known ones: right of way refused 77, one parked car touched at
(-1386, 5446), the physics cars' lines). Not yet tested in game.

## Next, first: the honest tests' failures (the owner, 2026-11-26: "stop cheating the traffic tests")

**The tests were made honest** (this session). Every allowance that let a visible fault pass is gone; each check now fails on any:
- Physics cars in each other or crossing in a junction box: none allowed (were 6 and 3 a run).
- A physics car standing over 90 s fails, wherever (it passed if the day's own car stood as long, and only cars still physical at the
  end were counted). Only the queue behind the player's stopped car is excused, found by following what each car waits on to the
  player's car (`BehindPlayer`), not by a radius. The line also says how many of the long stands the day itself has (diagnosis only).
- New: cars played back from the day drawn in each other because one is late (Drive.cs had no such check).
- The ledger test (`Lateness.cs`) no longer moves cars back along their way and skips those it would drop into another car: it stops
  cars for real (braking at the yield's rate, standing up to 2 min, "stopped by something unseen") and everyone behind must cope. Its
  overlap test is the real body box both ways (`TrafficLate.Overlap`: it was same-direction only for moving cars and a 6-unit circle
  for a parked one). Other cars are seen where they were drawn last step, as the game does (the test used where the day would have them).
- trafficview drew no parked cars in the physics run, so the game's "space still taken" rule was never exercised: now it does.
- Day level: any two cars drawn through each other fails (only those pulling in or out did, over 10); right of way refused 0 (was
  100); parked cars a lane car would sweep 0% (was 2%).

**Built (the approved plan): a held car becomes a physics car.** `TrafficCars.ClaimForYield` (after each frame's drawing) claims every
drawn car `YieldDrawn` had to slow, within reach and drivable, up to 16 over `TrafficPhysics` (`MostYieldPhysics`); a held car may be
made one in a junction's box (`TrafficDrive.Drivable(day, at, held: true)`; `yieldClaimed` carries that to `Become`). Also: a car
becomes physical at the speed it is drawn (`DrawnSpeed`, was the day's), and forgets it was held (`TrafficLate.Unheld`); a drawn car
never waits for a car waiting, through others perhaps, for it (`TrafficDrive.Ring`, `Waiting`, `heldBy`); a stopped physics car behind
a space lets its car out (`LetsOut`); a car pulling in or out goes past a standing car it isn't touching, pulling in even touching
(`PullsPast`); a drawn car waits for a car standing across its way too (`FreeAhead`; it ignored standing ones and drove through cars
standing in junctions); a held car is never drawn further than its allowed speed from where it was (`Yield`'s `TimeAt` cap); a car
pulling in waits while a late car is still in its space, from the ledger (`TrafficLate.SpaceTaken`), not only the drawn cars.
trafficview drives the same (`Drive.cs`: `MakeForYield`; `TV_NOYIELDPHYS=1` turns it off to compare).

**Numbers, 16 seeds, `TV_DRIVE_ONLY=1`, the open-road run (standing at the busiest place at 17:00):**
- Before (old, lax tests that only counted cars still physical): 14 of 16 seeds locked solid, stands up to 600 s.
- Now: no seed locks; longest stand per seed 44 to 446 s; 166 physics cars stood over 90 s (0 on 4 seeds), 98 of them where the day's
  own car also stood over 90 s (the day's slow side-road queues: the owner's "standing for no reason" report, below). Seeds 7, 9, 10,
  16 have the most (24 to 32).
- Physics cars in each other: 87 pairs over 16 seeds (old count 72, at 24 physics cars; now up to 40 at once). Crossing in a box: 12.
- Played-back cars in each other because late: 0 to 268 pair-steps a seed (seed 9 the worst).
- The ledger test (`TV_LATE_ONLY=1`, standing run): 80 to 12,818 overlapping pair-steps a seed. Seed 1 went 5,301 to 315 with the
  fixes above; the rest of the seeds weren't looked at.
- Every seed still FAILs somewhere. `tools/check.sh` compiles clean. Not tested in game; the frame cost of up to 40 physics cars is
  unmeasured (`docs/TESTING.md` 68).

**Next, in order:**
1. The ledger test's overlaps on seeds 16, 12, 5, 7, 4, 3 (`TV_LATE_ONLY=1 TV_LATE_NOTES=1`: the first 25 pairs, each car's state,
   junction, leg time). On seed 1 what's left: two late cars both reported **parked at (0, 0)** (the ledger gives a stale leg for a late
   car between trips: `TrafficLate.At`; in the game that draws a car at the world's middle), and late cars crossing at node 47.
2. The physics cars' meetings: most are at the two known places (Leftovers 1 below: node 6, the Pinewick road's way in, needs a box
   per way in; node 42, drawn cars standing half a car past the line), and a physics car made where the day has a car at its trip's
   end on the lane (the day's own overlap).
3. The long stands where the day's car doesn't stand (68 of 166): `TV_DRIVE_CHAIN=1` prints, for each car standing over 90 s at the
   end, what it waits on and so on to the head; `TV_DRIVE_CAR=n`, `TV_YIELD_CAR=n`, `TV_LATE_CAR=n` trace one car.
4. The day's own failures, now counted: right of way refused (68 on seed 1), cars drawn through each other (27 sightings on seed 1).
5. Then the owner's report of standing still (below), the four-way stops and lights.

## Fixed 2026-11-26: a car standing in the open road (the owner's second report)

Race seed 192843966 (weekday), about 09:04: car 2521 (leg 247) stood 26 s north of node 42 ((-2354, -4259), box 17) waiting to turn
left (1>2), refused **crossing** for cars 1191 and 1004 going straight south and pulling into a drive 23 past the node (their trips
end 31.2 past it). `Release` only gave a grant back at a car's length past the box (31 here); a car whose trip ends near there crawls
the last of it and may stop short, so it held the junction until it parked. Now a car whose trip ends within 30 of that point gives its
grants back once it is out of the box (`Release`, `endsShort`; the same test as `HeldIn`). Car 2521 now stands 5 s. trafficview's new
check "steps a car stood for a car out of the junction pulling in just past it" (`TrafficDay.HeldOutOfBox`) was 690 on that seed
before the fix and is 0 after. Found with the trace patch and a throwaway probe (`day.At(car, w)` every 2 s for one car, and every car
standing near a point): sim time is (hour - 3) x 100, the race's start hour is in the log's `Sky: the race starts at` line.

16 seeds (`TV_DRIVE_ONLY=1`), without the fix then with it: mean 23.3 to 24.0 s a trip, median 7.5 to 7.3, 90% 56 to 56, 99% 231 to
262, over 1 min 2074 to 2019, over 3 min 433 to 516; the new check 12,924 steps to 0. The short waits went down and the long ones up:
the side lanes waiting for a gap on the link (node 49 east lane 471 to 603 min, node 67 237 to 393, node 132 into the top ten) wait
longer, with cars going by (a visible reason). Other FAILs with the fix: seed 2 a parked car first chosen 533 from the player on the
highway (limit 600), seed 3 physics cars 7 pairs in each other round node 42 at 18.07 h (limit 6; leftover 1 below).

## Next: cars standing still for no reason (the owner's first report)

At the T south of Kingsbridge where a street from the east meets the link (node 51, (-2398, -5044)), 14:30 on a weekend: cars on the
street and the link stood 5 s or more with the junction empty. **Treat every waiting figure measured so far as suspect until this is
fixed**, including the routes' gain.

Traced on seed 5 at 14:30, what the day's rules make the cars wait for:
1. **A red light out of sight.** The link's southbound queue for the trunk light (node 8, 300 units south) has its head car at node
   132, 19 short of node 8 and refused **next**. Each car behind leaves node 69's and node 132's boxes clear, so about 10 cars spread
   over 300 units stand through each red.
2. **The junction booked from 150 units out.** The street's left turner is refused **crossing** for main-line cars granted node 51
   while still 148 to 155 away, and **gap** for cars up to 287 away. Seed 5's street averages 267 s a trip.
3. **The day waits much more than the game's driver.** In the owner's log, physics cars were "back on its timetable" 50 to 97 s
   early: `TrafficDrive` drove the same legs up to a minute and a half quicker than the day.

**The plan (told to the owner):**
1. ~~Log the race seed.~~ Done (`Weather.Pick` logs "Race seed: N ... (trafficview --seed N)").
2. **Next to build: a trafficview check for standing with no visible reason.** It finds a car stood more than a few seconds with no
   car close ahead in its lane, no red at its own stop line and nothing crossing in front of it, and reports the seconds a day and
   where. Record it per step in `Simulate` from `m.Binding`, `Asking` and the refusal reason. "Visible" means what a driver at that
   spot can see.
3. **Fix the rules behind it one by one until that check is near zero:**
   - Queue at the light's own line (node 132 / node 8: `ThroughNext`, `PairedNext`).
   - Grants don't book a junction long before the car arrives (`Asking`'s `ReachOf`, `TooSoon`, the clear-time reckoning).
   - Check the give-way look and its margins.

   Then the day and the physics cars should take the same time over a leg (make trafficview's "early" lateness near zero).
   Re-measure on 16 seeds: the routes first, then the four-way stops and lights.

**Tracing:** `git apply tools/trafficview/trace.patch` (never commit it applied), build trafficview, then
`TV_DRIVE_ONLY=1 TV_DBGNODES=51,69,132,8 TV_DBGFROM=1150 TV_DBGTO=1220 dotnet <build>/trafficview.dll --seed 5`. It prints, every 2 s,
each standing car refused at those nodes with its reason (0 light, 1 behind, 2 crossing, 3 gap, 4 room, 5 next, 6 stop) and the car
it gives way to or is blocked by. `TV_DUMPAT=t` dumps every car round the link's south end at sim time t (the box is hard-coded).
`p1`/`p2` mark the pass. trafficview also builds seed + 1, so ignore lines from that run. Sim time t is (hour - 3) x 100.

## Baselines (16 seeds, `TV_DRIVE_ONLY=1`; suspect, see above)

Stood still per trip, as committed (2 passes, half re-routing, give-up after 180 s): **mean 24.0 s, 90% 56, 99% 262, 516 trips over
3 min**, 2 FAIL lines (after the fix above; before it 23.3, 56, 231, 433). One pass gave 31.5 / 74 / 395 / 765. The day takes about 6.5 s to build here (one pass about 2.5 to 3.3 s).

Where the rest waits: side lanes joining the link and the Pinewick road, refused at their front for **crossing** and **gap**. The
worst are node 47 and node 49 from the east lanes, node 45 (the Pinewick road), node 42 from the west lane and node 51's street.
Node 6's light holds the link's cars 14 to 16 min a day.

Tried and rejected (the code or switches remain): a green arrow at node 6 (38 s a trip; `TV_ARROWDELAY`), a lead left (`TV_LEAD`),
node 6 or 8 unlit (`TV_UNLIT`: Pinewick's cars wait much longer), other light timings (`TV_MAINMIN`, `TV_SIDEMAX`), right on red
(`TV_ROR`), the southbound left lane run back through node 42 (commit 8140277, reverted: fewer cars reached the bay; the light's green
is the limit, not storage). 3 passes or a 30% re-route share gained little for more build time and more failures.

Four-way stops and lights, first numbers (seed 1 only, before the report): all-way stops at nodes 42, 44, 45, 47, 49 gave 51.6 s a
trip (worse), and lights at 42, 45, 47, 49 gave 22.4 s (99th percentile 239 to 177 s). Switches: `TV_ALLWAY`, `TV_LIT`.
`TV_WARRANTS=1` checks the MUTCD warrants: they back 4 to 6 of the 16 lights and no new light or stop. To keep a light: a fixed site
list read by both `TrafficSignals.Build` and `Signs.Lit` (so the fingerpost is hidden), and fix the one way in whose heads can't be
seen from its line. All-way stops also need `TrafficDrive` stopping, stop lines and the signs list.

## Traffic leftovers

1. Physics cars in each other round node 42 at the evening peak: seed 16 fails with 8 pairs (limit 6), first at (-2364, -4249),
   17.41 h. Seed 12 does the same at 3 passes, 30%. After the pulling-in fix, seed 3 fails with 7 (16 seeds: 40 pairs in all, 47 before
   it). Traced 2026-11-26 (`TV_DRIVE_NOTES=1`, positions added to the note): almost all are at two places, the same in every seed.
   - **Node 42, the lane west's way in:** a drawn car waiting on edge 243 at s 18 (box 17) is met by a physics car on edge 43 passing
     5 short of the node. The day stops a car with its **middle** about at the box's edge (the IDM stop at `stop = ... - Box`, P is
     the car's middle: `OnPath` puts the drawn body's centre there), so its front is half a car into the box and past the stop line;
     `TrafficDrive` stops its front at the box (`ToBox - CarLength * 0.5 - 1`). Tried: the day stopping its front at the box
     (`Nose = CarLength / 2` taken off the stop, `ReachOf`, the at-the-line tests and the lights' `toLine` and `MayGo`): node 42's
     meetings went, but over 16 seeds standing went 24.0 to 26.9 s a trip (99% 262 to 296, over 3 min 516 to 701) and pairs 40 to 46
     (seed 3 to 9). Reverted. The owner: cars stand "like a foot over the line"; those are physics cars (within 91 of the player),
     which stop their front at it. Drawn cars only stand past 91, where half a car (about 2 m) past the line is hard to see: left as is.
   - **Node 6, the Pinewick road's way in:** a physics car on edge 72 at its line (s 26 to 27, box 23, green) is met by one leaving
     north on edge 44 at s 199 to 208 (13 to 22 out). The roads meet at about 60 degrees and edge 44 is widened for the bay, its
     northbound lane half a lane out towards the Pinewick road; `Traffic.ClearOfOthers` ignores the widening on purpose (box 29
     against 23 held every way in longer). A box per way in, not per junction, would fix this without that cost; a big change (`Box` is
     read in about 60 places).
2. Physics cars in each other elsewhere: 0 to 4 a seed. At 3 passes, seed 2 had 55 behind the player's car stopped across a trunk lane
   at (11151, -9332) (`TV_PASSES=3 TV_REROUTE=0.5` to reproduce).
3. A physics car brushed the player's stopped car (seed 11): not looked at.
4. Seed 9 at one pass: physics cars wait at node 49's east lane much longer than the day's cars (they're less willing to join a queue
   past a junction).
5. Then the owner's test in the game (`docs/TESTING.md` 58 to 63).

**Signs needed** (for when there are models): stop signs at any four-way stops. "LEFT LANE MUST TURN LEFT" before turn lanes. Lane-use
arrows where lanes split.

## How to run the checks here

- **HalfGripTools.dll** isn't here. Build the stand-in with `dotnet build tools/halfgriptools-stub -c Release -o <scratchpad>/stub`,
  then run `tools/check.sh ../HalfGripRefs/game <scratchpad>/stub/HalfGripTools.dll`.
- The tools need `-p:GameDir=/home/user/HalfGripRefs/game` (the refs repo cloned next to this one).
- **16 seeds of trafficview:**
  - Build it and copy `tools/trafficview/bin/Release/net8.0` to the scratchpad.
  - Run `tools/trafficview/run16.sh <copy> <folder> [VAR=value...]`: 4 at a time, about 13 minutes at two passes. Don't rebuild
    while it runs.
  - `python3 tools/trafficview/pool.py <folder>...` pools the fairness lines (trips-weighted mean, percentiles averaged, counts
    summed), the FAIL lines, the ways in with the most standing, the road classes' counts and the re-routed trips.
  - Judge by the fairness line (`docs/PLAN.md`). Every fix so far moved the day and turned up a different failure somewhere else, so
    rerun all 16 after each change.
  - `TV_DRIVE_NOTES=1`: each run's "long stop" lines come **before** its summary line.
  - A setting that drops a feature fails its own check by design (no bay: the bays line; a light off: the signs' line).
- The fairness lines give each busy way in its refusals by reason (`TrafficDay.RefusedFor`). That answers most "why do they wait";
  the trace patch above answers the rest.
- How long things take: all of meshcheck about 1 min 45 s, the layout in trafficview about 7.5 s. Run anything over 2 minutes in the
  background with its output in the scratchpad.
- Overhead pictures of roads with markings: `JVIEW_TEX=1 JVIEW_CLEAN=1 dotnet run -c Release --project tools/jview -- at out.ppm x z span`.
  `convert` (ImageMagick) turns PPM into PNG.
- Decompile: `~/.dotnet/tools/ilspycmd` with `DOTNET_ROLL_FORWARD=Major`. The scene dumps are in HalfGripRefs (`scenedumps/0.419`).
- The network allows `www.fhwa.dot.gov`, `rosap.ntl.bts.gov`, `gis.penndot.pa.gov` and `nhts.ornl.gov`. Use `curl` (the page reader
  can't resolve them).

## Working with the owner

- Ask for anything you need (access, files, a decision) at once, plainly. Don't guess, and don't quietly work around a refusal.
- Fix bugs without asking. Ask about design choices only where the owner can answer: they judge by what players see and feel (the
  waiting, the look), not the engineering. Make the engineering calls yourself and say why.
- Use plain words: no "node", "arm", "pair", "link" or "box" with the owner. Name the place ("the light where the road to Pinewick
  leaves town") and give averages per trip.
- Verify before claiming: say what you read or ran yourself and what came from notes. Show only pictures you have looked at yourself.
- The owner tests in game and reports with screenshots and `LogOutput.log`.
