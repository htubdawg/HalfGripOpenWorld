# Traffic: handoff

For the agent picking up NPC traffic. Read `CLAUDE.md`, then in `docs/PLAN.md` the three traffic sections ("Traffic: the agreed plan",
"Traffic: what the studies say, and the trip model", and the older research under them), then this. Work on **`traffic-test-branch`**
(made from `deer-test-branch`; commit and push there; the owner merges).

## Where things stand

Stage 1 (timetables, no game code): the traffic's map is done; each race's day of trips (`TrafficDay.cs`) is next. Nothing is
released; `mod.json` is unchanged.

**Done and pushed:**
- The plan, the owner's answers and the studies' figures are all in `docs/PLAN.md`.
- `OpenWorld/src/Plots.cs` (pure): the house-plot decisions (`Plots.Along`) and the footprint test (`Plots.Fits`), moved out of
  `Buildings.cs`, which calls them. Traffic finds every household from the same arithmetic the drawing uses. **Confirmed to change
  nothing drawn:** meshcheck's clear, facades, bases and homes give the same counts as `deer-test-branch` (2,606 houses, wings,
  towers and churches; 274 drive houses).
- `Heap.Clear()` added.
- `OpenWorld/src/Traffic.cs` (pure): the traffic's map, fixed with the world.
  - The driving graph: drives included, tracks, paths and courses out; no junction where two roads cross more than 4 units apart in
    height (a bridge over a road).
  - Its connected parts (`Parts`, `Part`, `MainPart`, `OnMain`): households and places on a part cut off from the main one are left
    out (`CutOff`), so every trip has a route.
  - Speed limits: Pennsylvania's, lower in towns.
  - Lane offset: right-hand traffic, the trunk's outer lane.
  - Households: drawn houses (a pad beside a garden house, the kerb for terraces), drive homes, farms; cars and workers from hashes.
  - Destinations with spaces and per-purpose draw: offices in the towers, retail, shops, high-street shops, workshops, filling
    stations, markets, churches, courses' yards, the roads leaving the map.
  - `Route` (Dijkstra between two anchors, partial edges at both ends).
- **A layout fix (changes the world):** `Network.SnapJoin`. A routed road's end looked for the road whose cell it reached within 140
  units only; the trunk's smoothed line runs up to about 270 off its cells, so Westhaven's only road out (its link to the trunk)
  stopped in the fields and the whole town (376 households, 16 places) was cut off. The main road from Millbrook to the north edge
  and four farm lanes ended short the same way. Now an end that finds nothing looks out to 320. meshcheck's `backroads` fails on any
  main road dead end (it found these two before the fix). All of meshcheck passes before and after; a few later roads move.
- `tools/trafficview`: builds the map and reports it; exits 1 on a failed check; `--near <road>` lists what comes near a road (for
  finding why one is cut off). Last run:
  - 1,261 junctions, 1,451 stretches of road, 1,907 households with 3,604 cars, 273 destinations, built in about 540 ms (the layout
    7.5 s first).
  - The graph in 9 parts: the main one 569,228 units of road; 8 cut-off town streets (2,761 units) whose 110 households are left out.
  - 3 roads leave the map (the trunk at both ends, the road north), 3 edge destinations: right (the old note suspecting
    `Network.Portals` was wrong; there are only 3).
  - Routes: 2,184 of 2,184 found, 0 broken, 0.06 ms each.
  - 1.10 workers a household, lower than the survey's 1.33 because `WorkersOf` clamps workers to cars. Not changed yet: decide when
    tuning the day (the clamp may be right: a worker without a car does not drive).
- `tools/check.sh` passes against a stub HalfGripTools.dll (see below).

**Left for later (in the plan):** the eight cut-off town streets (three in Kingsbridge, five in Westhaven) are a `TownPlan` fault: a
street laid with both ends free that no cross street came to join, reached only by footpaths. Fixing it is a world change; trafficview
lists them by position and road index.

## What comes next: `TrafficDay.cs` (pure, per race; designed, not written)

The owner's decisions are in the plan. These are the agreed ones that shape it:
- rooms are the main path;
- realistic from studies;
- **roads as busy as PennDOT's counts**, not five trips per household;
- every junction **uncontrolled**: the first to arrive goes first, yield to the car on the right, never block a junction;
- right-hand traffic;
- shootable and wreckable;
- deer react to traffic, but much less than to players;
- the game's enemy cars with random skins;
- headlights.

`TrafficDay(raceSeed, startHour)`: the seed is `Weather.Seed` in the game; the start hour is `Sky.StartHourOf(seed)`, as `Herds` takes
it. It has three parts.

### 1. The day

- From the seed: a weekend day with probability 2/7, else a weekday.
- The simulated day runs from 03:00 to 03:00 (the quietest hour), 2,400 real seconds (`Traffic.DaySeconds`).
- World hour h maps to sim time w = ((h - 3 + 24) mod 24) x 100.
- Race time t maps to w = ((startHour - 3) x 100 + t) mod 2400. The day repeats.

### 2. Tours (candidate, then thinned)

**How many.** Per household with cars, tours per day:

| | Work | Shop | Errand | Social |
|---|---|---|---|---|
| Weekday | 0.86 per worker | 0.45 | 0.6 | 0.5 |
| Weekend | 0.12 | 0.75 | 0.45 | 0.85 |

**When they leave.** Start hours come from tables of 24 weights (sample the hour from the table, the minute uniformly):
- work out: peaks 7 to 8;
- shop: 9 to 19;
- errand: 7 to 18;
- social: 17 to 21.

Weekends are shifted later. Tune them until the total matches PennDOT's TPG 9 hourly profile (the plan has it).

**How long they stay** (world hours, 100 s each): work 8.5 ± 1, shop 0.3 to 1, errand 0.15 to 0.5, social 1 to 3.

**Where they go.** Gravity on straight-line distance: weight / (1 + (d / 6000)^2). Social visits go to other households (pick among
about 8 drawn at random, by weight).

**Thinning.** Keep each tour with one probability q, so total free-flow driving time equals
`Traffic.DrivingShare x DaySeconds x households with cars`, about 183 s a household. This keeps the hours' shape and the purpose
mix, and gives the counts' density.

**Through traffic.** Between portals: about 110 a game day on the trunk (5,400 AADT / 36, less local traffic), fewer on main roads.
Hours from PennDOT TPG 4: 0.79 0.57 0.51 0.63 1.21 2.53 3.95 5.09 5.20 5.51 6.02 6.39 6.71 6.88 7.33 7.91 7.99 7.02 5.24 4.03
3.18 2.38 1.73 1.20 (percent, from midnight).

**One car, one tour at a time.** A tour that would start before its car is home waits for the car to get back plus 60 s. Drop out
legs after 23:00.

### 3. The simulation

Fixed step 0.25 s, cars in index order, doubles with + - * / and `Math.Sqrt` only, so it is deterministic.

**Paths.** Each leg's path is its route's lanes, as (edge, dir) with road s from and to.

**Speed profile.** Built once per leg, every 10 units:
- the road limit;
- bends: sqrt(8.1 x R), from the three-point circle at road samples (8.1 is 0.25 g in units);
- turns: no limit when the angle between lanes is under 20 degrees, 13 units a second (about 9 mph) when sharp;
- a stop at the end.

Then a backward pass with braking b = 8.25 (2.5 m/s²).

**Car following.** IDM (Intelligent Driver Model): a = 5, b = 8.25, T = 1.5 s, s0 = 7, length 14. The leader is the nearest car ahead
along the car's own path within 200 units, found from per-lane sorted lists rebuilt each step, with key edge x 2 + dir.

**Junctions.** First come, first served, by reservation:
- **Request:** a car within a reach of its next node asks for it. The reach is v²/2b + 1.5v + the node's box, at least 40.
- **Order:** requests sorted by ETA, then index. Adjacent pairs with ETAs within 1 s swap so that the car on the right goes first.
- **Grant:** a car is granted if it is compatible with every car granted or in the box. Compatible means the same in-lane and out-lane,
  or the other car clears before this one arrives with 0.5 s to spare.
- **No blocking:** a car is granted only if its out-lane has room past the box: the last car on it is beyond box + 14 + 4, or moving.
- **Keep the order:** a refused request also blocks the later requests that conflict with it.
- **Waiting:** an ungranted car stops at the box's edge, treated as a leader at rest.
- **Release:** a grant ends when the car is box + 14 past the node.
- **Deadlock guard:** a car waiting over 45 s is forced through. Count these; there should be none.

**Parking.**
- A leg starts at its anchor once the lane is clear: nothing within 40 ahead or behind, and nothing arriving within 3 s; force it after
  60 s.
- It ends stopped at the anchor's s; the car is then parked at the anchor's spot.
- The return leg leaves at arrival + dwell.
- Count each lot's peak against its spaces.

**Recording.** Every 0.5 s, per leg: (path index, s). `At(car, t)` interpolates and gives the edge, dir, s and the world point:
the line point plus the right normal of travel times `Traffic.LaneOffset`. A car that is not on a leg is parked at its anchor's spot.

### Checks (add to trafficview, then the relevant ones to meshcheck)

- The same day twice (checksum).
- No two cars on a lane closer than a car length.
- No conflicting cars in a junction box together; forced grants counted (expect 0).
- Speeds never over the limit plus 5%.
- AADT-equivalent per road class (vehicles past a point per game day x 36) against PennDOT: trunk about 5,400; rural principal and
  minor arterials 2,000 to 15,000; collectors 400 to 8,800; lanes a few hundred.
- The hourly share against TPG 9.
- Cars moving at the 5 pm peak (about 400 expected) and at 3 am (about 25).
- Lots' peaks against their spaces.
- Time to build the day (aim under 2 s, on a worker in the game).

Then a map picture (roads coloured by volume) and frames at 03:00, 08:00, 12:00 and 17:00 for the owner, looked at first. That
finishes stage 1; stages 2 to 4 are in the plan.

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
  `Net` (`Available`, `InRoom`, `IsHost`, `LocalId`, `HostId`, `Players`, `PlayerJoined`, `PlayerLeft`) and `NetPlayer` (`Id`, `Name`).
- The layout takes about 7 s to build in trafficview.

## Working with the owner

- Ask for anything you need (access, files, a decision) at once, plainly. Do not guess, and do not quietly work around a refusal.
- Verify before claiming: say what you read yourself and what came from notes.
- Plain words about what the player sees.
- Show pictures you have looked at yourself.
