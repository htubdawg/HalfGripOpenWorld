# Traffic rework: the plan

Written 2026-11-26 on `traffic-rework-branch`, forked from `deer-test-branch` (Open World 0.31.1, which has no traffic code). It
replaces the traffic of `traffic-test-branch` (0.33.0 plus unreleased work) rather than repairing it. Nothing below is built yet.

## 1. What went wrong on `traffic-test-branch`

The size first: about 16,200 lines of traffic code in 13 files (`TrafficDay.cs` alone is 5,200) and 5,000 more lines in
`tools/trafficview` and `tools/trafficsim`. In its last two days, about a third of its 50 commits were fixes, reverts or
"tried and taken out". Every one of its 16 test seeds still fails somewhere.

The bugs come from the architecture, not from any one rule:

1. **Two simulations that disagree.** `TrafficDay` works out a whole day for every household ahead of time: fixed steps, a
   timetable, junctions booked by reservation. Near the player, `TrafficDrive` drives the same cars live as physics cars. The two
   don't agree: physics cars came back to their timetable 50 to 97 s early. A third system, the lateness ledger (`TrafficLate`),
   sits between them. Most of the leftover bugs live where the three meet: cars drawn through each other because one is late, cars
   held back, `Yield`, `ClaimForYield`, `Ring`, stale legs drawing a car at (0, 0), `PullsPast`, `LetsOut` and more. Each fix added
   another special case.
2. **Junction conflicts worked out from topology, not geometry.** `TrafficDay.Crosses` decides whether two movements cross from the
   order of the arms round the node. At Kingsbridge's node 6, the roads meet at about 60° and one lane is shifted for the bay, so two
   movements that do cross on the ground were let through together. That caused the owner's "cars stuck here" report. A later attempt
   that compared shapes flagged 737 junctions, nearly all of them wrongly.
3. **Junctions booked far ahead.** A car could hold a junction from about 150 units out, so cars stood at empty junctions ("standing
   for no reason"). A queue for a light out of sight spread ten standing cars over 300 units.
4. **Determinism for everything.** The whole day had to come out identical on every machine, so it was built at load (twice, for
   rerouting), and any change to it is a world change. A physics car can never be deterministic, so all the reconciling in point 1
   was needed.
5. **The whole day simulated for the few hundred metres anyone can see.** Thousands of trips a day are worked out so that a few dozen
   cars round each player look right.

## 2. How games and simulators do it (research)

- **Open-world games (GTA, Watch Dogs, Forza Horizon, Cyberpunk 2077 and others) simulate traffic only in a bubble round each
  player.** Cars spawn on a lane graph out of sight at the bubble's edge, drive by local rules, and are removed out of sight when they
  leave it. No timetable and no global day. Density comes from per-road, per-hour targets. The Cyberpunk 2077 police talk (GDC 2025)
  describes graph-based lane discovery and asynchronous spawn-point generation for this. Unreal and Unity city-traffic projects
  split the same work into three layers run at different rates: graph built once at load, navigation on demand, motion every tick.
- **Junctions in microsimulators (PTV Vissim, SUMO) use conflict areas.** For every pair of paths through a junction that overlap
  geometrically, there is a conflict area, and a priority rule says who yields: major flow green, minor flow red. Vissim recommends
  conflict areas over hand-written priority rules because they come straight from the geometry. SUMO models internal lanes so that
  vehicles "cannot block the intersection, wait within the intersection for left turns nor collide on the intersection". Both
  warn that pure conflict areas can gridlock in congestion. The fix is keep-clear rules (don't enter unless there is room on the far
  side) plus deadlock detection.
- **Driver models.** IDM (the Intelligent Driver Model) for following, which the old branch already used. Gap acceptance for giving
  way: a minor car goes only if every priority car will clear the conflict area with a margin before it arrives. MOBIL for lane
  changes on multi-lane roads (the trunk).
- **Multiplayer.** Shared AI traffic is usually owned per vehicle. Each car is simulated on one machine (typically the nearest
  player's), its state is sent a few times a second, other machines show interpolated stand-ins, and ownership moves when another
  player is closer (Roblox network ownership, FiveM/OneSync, the DOTS traffic city netcode notes). Known trouble spots are migration
  races and duplicate spawns. Both are solved by having one authority (the host) grant ownership and spawning areas. That is what
  the deer and the events already do here.

Sources: [Cyberpunk 2077 police system, GDC 2025](https://gdcvault.com/play/1035509/Heat-MaxTac-and-Blockades-Expanding),
[UE5 traffic navigation layers](https://alina0607.github.io/ue5-traffic.html),
[V2X simulator traffic manager (spawn and despawn radius)](https://tlab-wide.github.io/V2X_E2E_Simulator/Components/TrafficManager/),
[Vissim conflict areas](https://cgi.ptvgroup.com/vision-help/VISSIM_2024_ENG/Content/5_Netzbearbeiten/Konfliktflaechen_modellieren.htm),
[TxDOT: conflict areas](https://www.txdot.gov/manuals/des/tsp/chapter-13-microsimulation-analysis/13-4-base-model-development/13-4-3-basic-terminology/13-4-3-4-conflict-areas.html),
[TxDOT: priority rules](https://www.txdot.gov/manuals/des/tsp/chapter-13-microsimulation-analysis/13-4-base-model-development/13-4-3-basic-terminology/13-4-3-5-priority-rules.html),
[SUMO intersections](https://sumo.dlr.de/docs/Simulation/Intersections.html),
[Roblox network ownership](https://create.roblox.com/docs/physics/network-ownership),
[FiveM ownership migration](https://forum.cfx.re/t/ownership-issue-with-reproduction-steps/5007873/2),
[FiveM AI traffic sync](https://forum.cfx.re/t/question-about-how-ai-traffic-is-spawned-and-synced/544718),
[DOTS traffic city netcode](https://dotstrafficcity.readthedocs.io/en/latest/_sources/netcodeEntities.rst.txt).

## 3. The new design in one paragraph

**One live simulation, only round the players, on a lane graph whose junction conflicts come from the drawn geometry.** No day
worked out ahead, no timetable, no lateness ledger. Every moving car is a lane agent driven by one driver model. A physics car is a
lane agent whose position comes from the game's car physics instead of from its lane, so there is nothing to reconcile when a car
switches between the two. Each car belongs to exactly one machine in a room, and the host grants ownership. Parked cars are static
scenery placed from the layout.

## 4. The parts

| New file | What it does | Unity? |
|---|---|---|
| `Lanes.cs` | The lane graph, built once after `Network`: a lane per direction (two each way on the trunk, bays and turn lanes from `TrunkLanes`/`TurnBays` if those are carried over), and connectors through each junction as curves taken from the drawn road surfaces (`Network.Point`/`Spans`, as `RoadMesh` does), speed limits, lane-change permissions. Deterministic (same rules as `Network`) | No |
| `Conflicts.cs` | For each junction, every pair of connectors swept as car-wide corridors. Where they overlap, a conflict area (entry and exit distance along each) and its kind (crossing, merging, diverging). Priority from road class and the light. Worked out from shapes, never from the arms' order. Stop lines and keep-clear zones | No |
| `Driver.cs` | The one driver model: IDM following the car ahead on its path (across connectors), gap acceptance at conflict areas (go only if every priority car clears the area by a margin before this car reaches it; yield to a car already in it whatever its priority), keep-clear (never enter a junction without room on the far side), light states, MOBIL lane changes on the trunk (keep right, overtake, let joiners in). Pure arithmetic, used for every moving car | No |
| `TrafficSim.cs` | The bubble: agents on lanes round each player, stepped at a fixed rate. Spawning out of sight at the bubble's edge, removal out of sight beyond it. Density from road class and hour (the PennDOT/TPG numbers become targets per lane-km). Turn choices at junctions weighted by road class and towards destinations (towns, retail, the trunk), with an occasional car pulling in or out of a drive or kerb space. Deadlock watch: a wait-for graph, and a cycle is broken by letting its oldest car go (counted and logged) | No |
| `TrafficRoom2.cs` (name to choose) | Ownership: each car has one owner machine. A machine spawns only in the area where it is the nearest player, so there are no duplicate spawns. The host grants hand-overs when another player becomes nearer (hysteresis). States sent at 10 Hz. Other machines show interpolated stand-ins. A leaver's cars go to the next-nearest player. Modelled on `DeerRoom`/`EventRoom`, over `ctx.Net` | No |
| `TrafficParked.cs` | Parked cars as scenery: drive pads, lots, kerbs, from the layout and the race seed (a simplified `TrafficParking`). A car that pulls out becomes an agent | No |
| `TrafficCars.cs` (rewritten) | The game side: the pool of copies of the enemy car's bodies (carried over: painting, wheels, far drawing on the right layers, which fixed the highway pop-in), physics cars (the game's enemy car with `Driver`) within about 150 units, kinematic agents beyond, stand-ins for cars other machines own. Hits, wrecks (not kills) | Yes |
| `Signals` | Lights run from the room clock so every machine sees the same state with no messages: fixed-time plans to start, actuation later. The drawn masts and heads (`SignalPoles`, `SignalMesh`, `SignalHeads`) carried over | Logic: no |

What carries over from `traffic-test-branch` nearly as it is: the enemy-car pool and painting, far drawing (`TrafficFar`, on the car's
own layers), rest on the springs (`TrafficRest`), the signal geometry and meshes, and the hard-won Mono lessons (no `foreach` over a
Dictionary that changes; `StrictMap`).

What is dropped: `TrafficDay`, `TrafficLate`, `TrafficDrive`, `TrafficPhysics` (yield, claim and hold logic), `TrafficPop`, the day's
households and tours, and `BuildPasses`/rerouting.

## 5. Tests (`tools/trafficsim` rewritten; no allowances)

A headless run of the real `TrafficSim` and `Driver` (the game's code, not a copy) with pretend players driving set routes, over 16
seeds and all hours. It fails on any of these:

- two cars' bodies overlapping
- two cars inside the same conflict area together on crossing paths
- a car stopped inside a junction box
- a car standing more than a few seconds with no visible reason (no car close ahead, no red, nothing in a conflict area it yields to)
- a deadlock cycle
- a spawn or removal in sight of a player
- density off target by road class and hour

Plus geometry checks in `meshcheck`: every connector on paving, every crossing pair of connectors with a conflict area (node 6's pair
named as a regression), and stop lines before the conflict areas. Plus the room checks from `trafficsim` (no car owned by two
machines, hand-overs, a late joiner, a leaver, twenty players, lossy network).

## 6. Phases (each one testable in game before the next)

0. **Base.** Decide what world work to carry over from `traffic-test-branch` (question 2 below), and port it as its own commit with
   `check.sh`, `preview`, `meshcheck` passing. No traffic yet.
1. **Lane graph and conflicts.** `Lanes`, `Conflicts`, meshcheck checks, and a `jview` mode that draws connectors and conflict areas.
   Look at node 6, node 42 and the trunk's bays.
2. **Driver and sim, headless.** `Driver`, `TrafficSim`, and the new `trafficsim` checks green on 16 seeds, alone.
3. **In game, single player.** Kinematic agents drawn from the pool, physics cars near the player, parked scenery. Frame cost measured
   with `FramePerf` against the 130 to 140 fps goal.
4. **Lights.** Fixed-time from the room clock, then actuated if wanted.
5. **Rooms.** Ownership, hand-overs, stand-ins; `trafficsim` room checks.
6. **Density up and polish.** More highway traffic in measured steps (goal: ten times), lamps at night, drives and kerbs.

Each phase updates `docs/HANDOFF.md`, `docs/PLAN.md`, CLAUDE.md's file table and the README.

## 7. Questions for the owner before building

1. **Ambient bubble instead of a whole day of households.** Cars no longer have homes and full-day tours; they appear out of sight
   round the players at the right density for the road and hour. Is that acceptable? (It is how open-world games do it, and it
   removes the root of the bugs.)
2. **What to carry over from `traffic-test-branch`.** The world work there is not part of the broken traffic: the highway rework (grass
   median, left-turn bays, K-rail: `TrunkLanes`, `TurnBays`), Kingsbridge's turn lane, the wrap (`Wrap`), `FramePerf`, `Plots`, and the
   signal masts and heads. Recommended: carry all of it over in phase 0.
3. **Out-of-sight spawning and removal.** The standing rule is "no car is ever hidden". Cars appearing and leaving only where no player
   can see (beyond the far draw distance, or behind the view), never in sight, keeps to that in spirit. Is it OK?
4. **Releases.** `main` is at 0.32.1, which already ships the old traffic, and this branch starts from 0.31.1. Plan: the rework lands as
   a new minor version (0.34.0) that replaces the old traffic on `main`, with no release until phase 3 works in game. Agreed?
5. **Standing decisions kept as they are:** right-hand PA rules, no roundabouts, headway 1.5 s, main roads never stop for side roads,
   every moving car near a player a physics car, wrecks aren't kills, no allowances in tests. Any to revisit now that the design is
   new?
