# Traffic rework: the plan

Written 2026-11-26 on `traffic-rework-branch`, forked from `deer-test-branch` (Open World 0.31.1, which has no traffic code). It
replaces the traffic of `traffic-test-branch` rather than repairing it. Revised the same day after the owner's answers (section 7).
Nothing below is built yet.

**The goal (the owner):** cars are NPCs on schedules, and the world can perceive and measure them carrying on outside any player's
view. Traffic must be realistic and interactive.

## 1. What went wrong on `traffic-test-branch`

The size first: about 16,200 lines of traffic code in 13 files (`TrafficDay.cs` alone is 5,200) and 5,000 more lines in
`tools/trafficview` and `tools/trafficsim`. In its last two days, about a third of its 50 commits were fixes, reverts or
"tried and taken out". Every one of its 16 test seeds still fails somewhere.

Schedules weren't the problem. The way they were run was:

1. **The day was decided in advance, then the game tried to keep to it.** `TrafficDay` simulated the whole day at load and recorded
   it. In the game, cars were played back from that recording, and near the player `TrafficDrive` drove the same cars live as
   physics cars. The two didn't agree: physics cars came back to their timetable 50 to 97 s early. A third system, the lateness
   ledger (`TrafficLate`), patched the difference. Anything the player did (blocking a road, a crash) was a deviation from a fixed
   recording, so it had to be patched cell by cell. Most of the bugs live there: cars drawn through each other because one is late,
   cars held back, `Yield`, `ClaimForYield`, `Ring`, stale legs drawing a car at (0, 0), and more.
2. **Junction conflicts worked out from topology, not geometry.** `TrafficDay.Crosses` decides whether two movements cross from the
   order of the arms round the node. At Kingsbridge's node 6, the roads meet at about 60° and one lane is shifted for the bay, so two
   movements that do cross on the ground were let through together. That caused the owner's "cars stuck here" report.
3. **Junctions booked far ahead.** A car could hold a junction from about 150 units out, so cars stood at empty junctions.
4. **Determinism for everything.** The recorded day had to be identical on every machine, so every change to it was a world change,
   and physics cars (never deterministic) could only be bolted on through the ledger.

What carries over is the part that worked: the traffic's map, households, places and trip rates in `Traffic.cs` (NHTS and PennDOT
calibrated), and the game side (car pool, paint, far drawing, rest on the springs, signal geometry).

## 2. Research

**Persistent NPCs in games.** Simulating every citizen in full is too expensive. GTA IV's studio cut fully simulated daily routines
for that reason. Games that keep a persistent population use simulation level of detail instead: full detail near the player,
cheaper rules elsewhere, time-slicing, and world-state management, so characters stay persistent without full-fidelity simulation
everywhere (Kingdom Come: Deliverance, Oblivion's Radiant AI schedules, "smart areas"). That is the same idea as graphical LOD,
applied to the simulation.

**The traffic-engineering answer: hybrid mesoscopic/microscopic simulation.** This is exactly the problem "the whole network,
detail where it matters" solves. A **mesoscopic** model (Mezzo, SUMO's meso, Aimsun meso) moves each vehicle individually along
links treated as queues with capacities: realistic travel times, queues that spill back, signals, but no lane positions. It is cheap
enough for every car on the whole network. A **microscopic** model (IDM, gap acceptance, lane changes) runs in the areas of
interest. Aimsun runs both concurrently, micro in selected areas, meso in the rest.

The literature's rules for coupling them (Burghout, Koutsopoulos and Andréasson; Gomes):
- the same network and the same route choice in both
- consistent performance (a link takes the same time in either model at the same demand)
- vehicles conserved, with their identity and characteristics, across the boundary

**Junctions: conflict areas.** Vissim and SUMO find conflicts from the geometry of each pair of paths through a junction, and a
priority rule says who yields. Keep-clear rules stop cars waiting inside the junction. Both warn about gridlock in congestion, so
add deadlock detection.

**Multiplayer.** Shared AI vehicles are usually owned per vehicle, by one machine, with ownership granted by an authority and moved
to the nearest player. That is what `DeerRoom` and `EventRoom` already do here.

## 3. The design in one paragraph

**Every car in the world is one live NPC, simulated all race long at two levels of detail, from one state.** Households and places
give each car a schedule of activities (home, work, shops, school, visits) with departure times. Away from the players, cars move
through a mesoscopic model of the whole road network: each lane is a queue with capacity and storage, and junctions admit cars by
the same conflict and priority rules as near the players. Near a player, the same cars switch to the microscopic model (IDM, conflict
areas, lane changes), and the nearest become physics cars. Nothing is recorded in advance: there is no timetable to keep to and so
nothing to reconcile. A car the player holds up is simply late, its next activities shift, and the queue behind it spills back into
the mesoscopic network and is still there when the player comes back.

## 4. How it works

### Population and schedules (`Traffic.cs` carried over, `Schedules.cs`)
- The map, households, cars and places from `Traffic.cs`, fixed with the world.
- Each race (from the race seed): each car's day as a chain of activities (home, work, school, shop, errand, visit, home) with a
  wanted departure time and a minimum stay. These are plans, not a timetable.
- The simulation starts at the race's start hour, with its state built straight from the schedules (no warm-up): a car between
  activities is parked at the one its plan has it at; a car on a trip is placed along its route as far as it would have got, lanes
  spaced by the headway; queues at red lights seeded from the light's phase. The race starts mid-day with cars already at work,
  parked at the shops and on the roads, and the simulation runs on from there.

### Mesoscopic level, the whole map (`Meso.cs`)
- Every car is an entity with its route, link and entry time.
- Each lane or link is a queue. A car can leave it no earlier than its free-flow time, no sooner than a headway behind the car ahead,
  and only if there is room on the next link (storage, so queues spill back) and the junction admits it.
- Junction admission comes from the same conflict areas and priority rules as the micro level (a movement's capacity under the
  priority flows) and the same light states. This is what keeps the two levels consistent (§2).
- It is event-driven (a car only costs work when it leaves a link), so thousands of cars cost little.
- Route choice: shortest time on current link times, updated every few minutes, with en-route rerouting round long queues.
- Parking: lots and kerbs have capacity; a full lot sends cars to the next one.
- Position at any moment is interpolated along its link, for far drawing and the map.

### Microscopic level, near each player (`Micro.cs`, `Driver.cs`)
- An area round each player (about the drawn distance) where cars are lane agents: IDM following, gap acceptance at conflict areas,
  keep-clear, lights, MOBIL lane changes on the trunk, pulling in and out of spaces.
- One driver model (`Driver.cs`) for every micro car. A physics car (the game's enemy car, nearest ones) is a micro car whose position
  comes from physics, so switching between them needs no reconciling. Every moving car near the player is a physics car, as before.
- **The boundary** (a short transition stretch on each link): a car leaving the area goes into its link's queue at its real position
  and time. A car entering is placed where the meso has it, behind the queue's last car, at a speed the queue allows. Cars are
  conserved, keep their identity and schedule, and never jump or overlap.
- Interaction: a crash, a block or a pushed car is real in micro. It slows the cars behind, which carries into meso through storage.
  A wrecked car is gone from its schedule; its household sees it.

### Lane graph and conflicts (`Lanes.cs`, `Conflicts.cs`)
- Built once after `Network`, deterministic under `Network`'s rules.
- A lane per direction (two each way on the trunk, the bays from `TrunkLanes`/`TurnBays`), connectors through each junction as curves
  from the drawn road surfaces (`Network.Point`/`Spans`, as `RoadMesh` does), speed limits.
- Every pair of connectors swept as car-wide corridors. Where they overlap, a conflict area (entry and exit distance along each) and
  its kind (crossing, merging, diverging). Priority from road class and the light. Stop lines and keep-clear zones.

### Lights
Run from the room clock with fixed-time plans, so every machine sees the same state with no messages; actuation later if wanted. The
drawn masts and heads (`SignalPoles`, `SignalMesh`, `SignalHeads`) carried over.

### Rooms (`TrafficRoom` rewritten, modelled on `DeerRoom`)
- The **host runs the whole-map meso**. The meso isn't recorded ahead of time and doesn't need to be deterministic. It sends link
  events (car c entered link l at t, about 8 bytes), batched 5 times a second, so the other machines keep the same meso picture.
  The load, from the survey's numbers (about 2,600 households; about 400 cars on the roads at the evening peak, plus about 100 on
  the highway; cars drive in real time, a junction-to-junction stretch takes them about 7 s): about 70 events a second at the peak
  for the whole map, about 600 bytes a second. Each machine is only sent the events within its far draw distance (interest
  management), a few to a few dozen a second; the debug map overlay gets a coarse snapshot every few seconds. A late joiner gets a
  full snapshot (about 500 cars at about 12 bytes, under 10 KB). For comparison, one physics car's state at 10 Hz is about 450
  bytes a second.
- Micro cars are owned per car by one machine (the nearest player's, granted by the host, handed over with hysteresis). Cars round
  two players near each other are owned by one machine, with stand-ins on the other. The owner reports boundary exits back to the
  host's meso.

### Perceived and measurable outside view
- **The debug map overlay** shows every car in the world from the meso, with queues coloured.
- **A "follow car" debug key**: pick a car, see its schedule, where it is now and its expected arrival, then drive off and come back.
- **The log** reports per-hour counts against PennDOT and TPG, trips completed, lots' occupancy and the worst queues.
- **In the world:** lots fill and empty through the day; a car you passed going to work is parked there later; a queue you cause is
  still there when you come back.

## 5. Tests (`tools/trafficsim` rewritten; no allowances)

The game's own code headless (not a copy), over 16 seeds, a full day, pretend players driving set routes.

Fails on any visible fault:
- two cars overlapping
- crossing paths inside one conflict area together
- a car stopped in a junction box
- a car standing with no visible reason
- a deadlock
- a car appearing or vanishing in sight
- a jump or overlap at the boundary
- a car lost or duplicated anywhere in the day (conservation)

Measured and reported:
- **Consistency:** each link's travel time and capacity in meso against micro at the same demand. This sets meso's parameters, so a
  car's journey takes the same time watched or not.
- **The day against the surveys:** counts by road class (PennDOT), the hours (TPG 9), trip times and waiting per trip, fairness.
- **Persistence:** cars followed from micro into meso and back arrive at their activities, lots' occupancy through the day, queues
  caused by a pretend player blocking a road still present on return.

Plus `meshcheck`: every connector on paving, every crossing pair of connectors given a conflict area (node 6's pair named), stop lines
before the conflict areas. Plus the room checks: no car owned by two machines, hand-overs, late joiner, leaver, twenty players, lossy
network.

## 6. Phases (each one checked before the next)

0. **Base.** Carry over the world work from `traffic-test-branch` (the highway rework with median, bays and K-rail, Kingsbridge's turn
   lane, `Wrap`, `FramePerf`, `Plots`, the signal masts and heads), with `check.sh`, `preview` and `meshcheck` passing. No traffic yet.
1. **Lane graph and conflict areas.** `meshcheck` checks; a `jview` mode that draws connectors and conflict areas. Look at node 6,
   node 42 and the trunk's bays.
2. **Population and schedules.** `Traffic.cs` carried over; activity chains per car; `trafficsim` reports the demand against the
   surveys.
3. **Meso, headless.** The whole day, all cars, with lights and parking. Conservation, the surveys, the worst queues, deadlocks;
   build and run time.
4. **Micro and the boundary, headless.** `Driver`, micro areas round pretend players, the boundary, physics cars stood in by the
   driver; consistency against meso; all the fault checks green on 16 seeds.
5. **In game, single player.** Far cars drawn from meso, near ones micro, the nearest physics; parked cars from the lots' state; the
   map overlay and the follow-car key; frame cost against the 130 to 140 fps goal.
6. **Rooms.** Host meso, link events, ownership, stand-ins; `trafficsim` room checks.
7. **Density and polish.** Full survey traffic, more through traffic on the highway (goal: ten times), lamps at night.

Each phase updates `docs/HANDOFF.md`, `docs/PLAN.md`, CLAUDE.md's file table and the README.

## 7. The owner's answers (2026-11-26)

1. Ambient bubble traffic (cars without homes or schedules): **unacceptable.** Cars are NPCs on schedules, perceived and measurable
   as carrying on outside view.
2. Carry over the world work from `traffic-test-branch`: **yes**.
3. Cars out of view may be culled from drawing: **yes** (they keep existing in the simulation).
4. Versions and releases: not a concern for this plan.
5. Traffic must be **realistic and interactive**. The standing decisions in `docs/PLAN.md` hold: PA rules, right-hand, no
   roundabouts, headway, right of way, every moving car near a player a physics car, wrecks aren't kills, no allowances in tests.

## Sources

- [Burghout et al., hybrid meso/micro (Mezzo + MITSIMLab)](https://intra.kth.se/polopoly_fs/1.742067.1600688968!/ctr2005_01.pdf)
- [Burghout, hybrid microscopic-mesoscopic simulation (dissertation)](https://swov.nl/nl/publicatie/hybrid-microscopic-mesoscopic-traffic-simulation-doctoral-dissertation-royal-institute)
- [Gomes, Open Traffic Models: hybrid interface conserving vehicles](https://arxiv.org/pdf/1908.04009)
- [Aimsun hybrid simulation](https://www.aimsun.com/technical-notes/hybrid-simulation/)
- [hybridPY, SUMO User Conference 2024](https://sumo.dlr.de/pdf/2024/4-3.pdf)
- [Kingdom Come: Deliverance, thousands of NPCs](https://gamedev.net/news/supporting-thousands-of-npcs-in-kingdom-come-deliverance-kingdom-come-deliverance-ii-r5526/)
- [Simulation LOD dissertation, TCD 2016](https://publications.scss.tcd.ie/theses/diss/2016/TCD-SCSS-DISSERTATION-2016-055.pdf)
- [Smart Areas: daily life in an open-world game](https://www.scitepress.org/Papers/2014/49211/)
- [GTA IV's cut NPC routines](https://www.arynews.tv/gta-4-was-going-to-be-a-much-more-realistic-game-but)
- [Vissim conflict areas](https://cgi.ptvgroup.com/vision-help/VISSIM_2024_ENG/Content/5_Netzbearbeiten/Konfliktflaechen_modellieren.htm)
- [TxDOT conflict areas](https://www.txdot.gov/manuals/des/tsp/chapter-13-microsimulation-analysis/13-4-base-model-development/13-4-3-basic-terminology/13-4-3-4-conflict-areas.html)
- [SUMO intersections](https://sumo.dlr.de/docs/Simulation/Intersections.html)
- [Roblox network ownership](https://create.roblox.com/docs/physics/network-ownership)
- [FiveM ownership migration](https://forum.cfx.re/t/ownership-issue-with-reproduction-steps/5007873/2)
