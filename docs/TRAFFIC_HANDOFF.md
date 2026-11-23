# Traffic: handoff

For the agent picking up NPC traffic. Read `CLAUDE.md`, then in `docs/PLAN.md` the three traffic sections ("Traffic: the agreed plan",
"Traffic: what the studies say, and the trip model", and the older research under them), then this. Work on **`traffic-test-branch`**
(made from `deer-test-branch`; commit and push there; the owner merges).

## Start here

1. Read `CLAUDE.md`, then the traffic sections of `docs/PLAN.md` (see above), then this file.
2. Check out `traffic-test-branch` (the second round of stage 2 is the last commit; see "Stage 2: second round" below) and clone
   `htubdawg/HalfGripRefs` next to the repo (the tools need
   `-p:GameDir=/home/user/HalfGripRefs/game`). Build the stub HalfGripTools.dll (bottom of this file) for `tools/check.sh`.
3. Run `dotnet run -c Release --project tools/trafficview -p:GameDir=... [-- --seed N] [--picture out.ppm] [--closeup out.ppm [x z span
   hour]]`. It builds the map, then race N's day twice, checks the day and the parking and exits 0. About 25 s. 16 seeds (1 to 15,
   20261122) pass after the second round.
4. **The owner chose (2026-11-23) to go ahead to stage 3 without waiting for their second try of stage 2.** Start with "Stage 3: the
   plan" below. Test 48 (`docs/TESTING.md`) is still open: when its report comes (screenshots, `LogOutput.log`), fix what it shows
   first, since stage 3 swaps stage 2's bodies for physics cars and shares their paint, poses and lamps. Before that report, stage 3
   was planned on stage 2 never yet confirmed in the game. What changed since their first try is under "The owner's first report" and "Stage 2:
   second round" below. The second round was done without a report from the owner (none had come): it changes how cars pull in and
   out and turn at junctions, which test 48 now asks them to watch.

**Where to look first in their next log:**
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

**Next: stage 3** (the owner's call, 2026-11-23, ahead of test 48's report): a real car near the player (our own driver on the game's
car physics, within about 300 units), back on its timetable out of reach, late by what held it up; wrecks retired. The owner measures
what 20 to 30 physics cars cost. Planned in "Stage 3: the plan" below.

## Where things stand

Stage 1 (timetables, no game code): the traffic's map and each race's day of trips (`TrafficDay.cs`) are done and checked. Stage 2 (cars
drawn in the game): built, tried once by the owner, three things fixed from that try (lamps, kerb parking, pulling in and out) and a
tile bug from stage 1; waiting for the second try. Nothing is released and `mod.json` is unchanged (0.31.1). The branch also carries
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
- **Through traffic circles the trunk:** 10 cars, half each way, spread along it at 03:00 and driven all day through the wrap. They add
  about 1,800 to the trunk's count. Flaw: at 03:00, where the day repeats, each jumps from where the day ends it to where it began.
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

## Stage 3: the plan (prepared 2026-11-23; nothing of it built yet)

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

**What this leaves (say so to the owner):** a late car back on its timetable outside the bubble can be drawn overlapping a car the
day put just behind it (the day never knew it was late). Out to 900 units that can be seen. The cheap fix is to hide the farther of
two drawn moving cars closer than a car's length on one lane; the real one is to feed lateness into the day's lanes, which the day
cannot do after it is built. Measure how often first (trafficview can apply a ledger and count overlaps).

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

- trafficview (no Unity): the driver's pure parts (the point ahead on a leg at any P, through a junction's curve; the speed target:
  profile, IDM, junction hold), the ledger (a late car's place, its stay shortened, the overlap count after applying random
  lateness), the bubble's choice (which cars, held back where they would overlap).
- In the game (write test 49 in `docs/TESTING.md`): cars near you are physics cars (bump one: it is pushed, not a wall; shoot one: it
  smokes, then wrecks); they follow their lanes, queue, wait at junctions, pull in and out as before; nothing drives through a car
  you stopped; far cars carry on; frame time with `TrafficPhysics` 0, 20, 30 at the evening rush in Kingsbridge; the log line counts
  physics cars made, gone back late, retired.
- `tools/check.sh` builds it; meshcheck and jview leave the game-side files out as now.

### Order of work (suggested)

1. One physics car: make and prepare a prefab instance as above (log what it has), place it on a road ahead of the player with no
   driver, check it stands, can be pushed and shot, wrecks without the kill (or with, per decision 1).
2. `TrafficDriver` on that car, following one leg; then the bubble with one car; then all cars in the bubble, the cap, held back.
3. Going back on the timetable, the ledger and `At` with it; retirement.
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
  	public class ModNet { public bool Send(byte[] d) => false; public bool Send(byte[] d, ulong to) => false; public bool SendToHost(byte[] d) => false; public void OnReceive(Action<ulong, byte[]> a) { } }
  	public static class Net { public static bool Available, InRoom, IsHost; public static ulong LocalId, HostId; public static IReadOnlyList<NetPlayer> Players => new List<NetPlayer>(); public static event Action<NetPlayer> PlayerJoined, PlayerLeft; }
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
