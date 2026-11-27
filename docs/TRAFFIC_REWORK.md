# Traffic rework: the plan

Written 2026-11-26 on `traffic-rework-branch`, forked from `deer-test-branch` (Open World 0.31.1, which has no traffic code). It
replaces the traffic of `traffic-test-branch` rather than repairing it. Revised the same day after the owner's answers (section 7).
Phases 0 to 3 are done (the lane graph and conflict areas, the population and its plans, the meso headless: see `docs/HANDOFF.md`);
phase 4 is built (the micro level and the boundary, every fault check passing on 64 seeds, trips taking the same time in both levels
to 1%);
phase 5's first part is built (the live traffic drawn in the game alone, parked cars, the player's car seen by the drivers, the overlay,
the follow-car key and the map; see §6); the rest is not built yet.

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
- As built (phase 2): tours from home (work, shop, errand, social; no school, as the world has none), a third of commutes with a shop or
  errand on the way home, thinned so households spend the survey's share of the day driving, then given to the household's cars (a
  tour waits up to an hour for a car still out, else is given up). A stop is left at the later of its wanted time and the arrival plus
  its least stay (`Schedules.Leave`). Day seconds from 03:00, when every car is home; each car planned home by 02:30. Routes on the
  lane graph as the meso routes a car on empty roads (`Meso.PlanRoute`, 2026-11-27: through the wrap's tunnels, and only by turns the
  lanes have; the traffic's map routed one leg in nine back up an acute fork at node 7 that no lane turns at, and timed it at the roads'
  limits, so cars came home after the day's end), on the traffic's map only where an end is off the lane graph. `Schedules.At` gives
  the planned state at any hour on empty roads, for the meso's start.
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
- As built (phase 3, `Meso.cs`): the links are the lanes. A car is queued in the lane of its carriageway (an edge one way) that its next
  connector leaves from (lanes are changed along the stretch; a bay's cars drive its through lane first), so a left turner waiting in a
  bay doesn't hold the cars going on, and on a road with one lane it does. A lane lets a car over its end no sooner than its free time,
  2 s behind the car before (1,800 an hour, HCM's saturation flow) and only with room on the next lane (its length over 16.5 units a
  car, at least one); a car stopping on a lane turns off past the standing queue. Through the junction: the light green (or amber for a
  car that reached the line moving), no conflict area shared within 1 s with a car already through it, and where it gives way (or came
  second) no car coming on the other connector before it would be through, unless that car can't go (red, no room) or has been held at
  its line less long (the standoff rule: at two junctions whose boxes touch, cars could give way to each other round a loop). Routes:
  Dijkstra over the carriageways at the lanes' live delays (smoothed over the cars leaving, or the head's wait so far) plus an eighth of
  a light's cycle; again from the lane's end for a car held 45 s by anything but a red. Fixed step of a quarter second (the whole day
  runs in about half a second headless, routes most of it). Deterministic, though it needn't be.
- Consistency with the micro (2026-11-27): a free road's time is a lone car's as a micro car drives it (`Driver.Run`: pulling away by
  IDM, braking for the turn ahead at half its comfortable braking), not the lane's length at its limit: every lane from the speed the
  car came on at to its next turn's limit (0 at its stop), every connector from the speed it reached the line at (from standing if it
  was held, with as many cars' room behind the line to pull away in as left one behind another before it). Conflict areas are timed
  along the same runs; where it merges into a stream it wants micro's gap (room for the car behind to slow to its speed and keep its
  time gap), and it looks for cars coming along the lane in and, where that is under 600, through the junction before. On a carriageway
  of one through lane no car passes another in its queue. The plans time each leg the same way (`Meso.LegTime`).
- No catching the car ahead through a junction (`Meso.Behind`): a car going onto a connector is no nearer than a standing queue's
  spacing behind the last car gone from its lane onto the same connector, or onto another while the two still share the road (their
  diverging area), anywhere from the stop line to where they part. It comes on slower if that does it, else it waits. So the head of
  a queue at a green really starts from standing, the next car follows it out about 2.6 s later rather than at the 2 s headway, and a
  car turning off slowly holds up the one behind it going on, as the micro has them (the micro, taking a junction over, finds the cars
  in its boxes where the meso has them: a fast car overtaking a slow one inside a shared stretch overlapped it there).
- Found with the trips paired (2026-11-27): a car giving way looks for cars coming back through the junctions before as long as the
  lanes between are short, to 600 in all, as the micro's drivers do (it looked one junction back, and not at all past an empty lane in:
  at three trunk junctions in a row a slow right turn went in front of a fast car two junctions back, which the micro, taking over,
  couldn't stop). A queue still pulling away at amber lets the cars through that couldn't stop comfortably when it came on. Routes are
  settled lane by lane (`Settle`): from the end back, each connector the one through its junction (same turn, same carriageway) that
  lands where the next turn leaves from in the fewest lane changes, a change counted only with `ChangeRoom` (twice a change at the
  limit, 500 on the trunk) on the stretch between. The search over carriageways took the cheapest connector into each, so a route could
  need a car in the outer lane of a stretch 0 long and in the inner one for its next turn: a micro car can't, and a car the meso handed
  the micro mid-junction came out in the wrong lane.

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
- As built (phase 4, `Micro.cs`, `Driver.cs`):
  - **Where.** A junction is micro if a lane into or out of it comes within 1,900 of a player (and the far end of the wrap when a player
    is near a tunnel); it goes back past 2,200. Junctions whose boxes touch (they share conflict areas, or a lane under 100 joins them)
    and the wrap's two tunnels go together. A lane is micro if the junction at its end is, so a junction's cars are all micro or all meso,
    and every hand-over is at least 1,900 from a player (sight is 1,400, the fog). A group turning micro while the meso has cars inside its
    boxes waits for them (up to 3 s): the meso spaces cars in time, not by their bodies.
  - **Boundary.** Every driving car is in exactly one lane's queue, micro or not (on a connector, the lane after it), so storage and
    conservation hold across it. A car off a meso junction onto a micro lane is placed where the meso has it (no faster than it can stop
    behind the car ahead or at its line; held back if a car changed lanes into its place since); one off a micro junction onto a meso
    lane goes into that queue at its real position and time. The meso asks the micro for room on a micro lane (the last car's position
    and the cars on their way in) and for a gap to pull out into. Turning micro, a group's cars are placed where the meso has them, spaced
    a standing gap, slowed to what can stop behind the car ahead and at their line (pulled back behind a micro car ahead of them, still
    each a standing gap behind the one before: two once landed on one spot); turning meso, they go into the queues in order.
  - **Driving.** IDM (1.5 m/s², 2 m/s² comfortable, 1.5 s, 0.75 m standing), the car ahead found along the car's own way through the
    junctions ahead (the cars on the same connector, on the other connectors off the same lane while they share it, merging onto the same
    lane first, then on the lane after), every bend and slower road within braking distance. Lanes move at the lane's speed, not the
    road's distance (a lane round the outside of a bend in the road's line runs up to half as far again).
  - **Junctions.** Within 120 of the line (or braking distance) a driver decides: the light (amber: on if it can't stop at 2 m/s²), room
    past the box for its whole body (kept clear: the last car out projected no further than braking comfortably, nor past its own line),
    and the conflict areas: no car in an area, or committed to it, while this one would be; where it gives way (or came second at a first
    come area) no car coming within 1 s of it, looked for back along each car's own route as far as 600; merging in front of a car, room
    for it to slow to this car's speed and keep its time gap. A car standing at its line and held (a light, a standing car ahead, no room)
    isn't coming. Too close to stop, it commits; committed, it still stops if the room or the areas go and it can still stop firmly.
    Two junctions whose boxes touch with a lane too short to stand on between them are decided as one at the first line. Standoffs: the
    one standing longer goes. A car waiting 15 s to merge into a stream is let in by the next car if it can slow comfortably (a zip
    merge): the next car in the stream, on the lane just before the junction, not one a junction or more back (it doesn't see the waiting
    car as the car ahead in time to slow for it; a car three junctions back braked at 4 g and still met it). A car committed to its box
    counts as in it when cars merging onto one lane are ordered (by arrival, not by which is in its box): at two junctions decided as one,
    a fast car followed a slow one that had gone in behind its time, arrived late and met it. Cars are decided one by one in index order, each seeing those already decided this step. A car committed through a
    junction whose box runs straight on into the next is firm at the next too (others give way to it there), on its lane or its
    connector.
  - **Pulling out.** A gap on the lane (no car behind made to brake hard, none ahead within a car), clear of the cars in the box of the
    junction the lane starts at (now or on their way through it), and not taking the room past that box that cars committed through
    it count on. A car waiting more than 15 s to pull out (a drive at the head of a queue that hardly empties) is let out: the cars
    coming up behind stop short of it if they can do so comfortably. A car coming in off a meso junction where a micro car already
    is (one came on beside it and moved over in front) comes in a standing gap behind it, not later (the two can run side by side the
    length of the road).
  - **Lanes.** Changes where the way on needs another lane (MOBIL's safety, finished before the stop line; a bay that opens at the junction
    behind is turned straight into), through cars passing on the trunk (MOBIL, keeping right); one that can't get over goes another way.
    A lane closer than a car's width to the one beside it (a bay's taper) counts as both. The lane is chosen ahead (`Pick`, `CanFollow`):
    of the connectors doing what its route's does (the same turn onto the same carriageway), one from which it can still follow its
    route, looking on through stretches without room to change lanes (`Meso.ChangeRoom` for each lane crossed); else one from another
    lane, moved into first. Where four junctions on the trunk come within 300 and a left turn from the inner lane after them, a car gets
    into the inner lane before the first. A car that must change with one alongside or just ahead in the lane it wants, at its speed,
    eases off (1 m/s²) to drop in behind once the stretch left is short; one could run beside it to the end. None misses its lane on 64
    seeds (trafficsim fails on one).
  - **Short stretches between junctions** (`Lanes`): a turn onto a stretch too short to change lanes on goes into each of its through
    lanes, as a driver who must turn again at once takes the lane for it (a side road met the trunk 0 short of a median opening whose
    left was from the inner lane).
  - **Drives** are one car wide (both ways down the middle): a car doesn't turn in while one is on it coming out, nor pull out while one
    comes in. A car parked short of a junction whose turn is from a left-turn bay beside it pulls out across into the bay.
  - **Consistency** (seed 1, every junction micro against the meso alone; 2026-11-27): the same trips paired, each one's time from first
    trying to set off to arriving, trips sent on from a full lot left out: micro 3.7 s quicker on average, median 1.3 s, on trips of
    several minutes; 72% within 10 s, the rest from meeting lights at other points of the cycle and routes chosen on other delays. The
    time lost by lane misleads at junctions close together: the micro holds a car at the first of two junctions decided as one, the meso
    on the short lane between, so the waiting is booked to different lanes for the same trip.
  - **What it costs.** A day round three players in 4 to 6 s headless (with every check after every step); with every junction micro, 21 s.

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

The game's own code headless (not a copy), over 32 seeds, a full day, pretend players driving set routes.

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

0. **Base (done 2026-11-26).** Carry over the world work from `traffic-test-branch` (the highway rework with median, bays and K-rail, Kingsbridge's turn
   lane, `Wrap`, `FramePerf`, `Plots`, the signal masts and heads), with `check.sh`, `preview` and `meshcheck` passing. No traffic yet.
1. **Lane graph and conflict areas (done 2026-11-27).** `meshcheck` checks; a `jview` mode that draws connectors and conflict areas. Look at node 6,
   node 42 and the trunk's bays.
2. **Population and schedules (done 2026-11-27).** `Traffic.cs` carried over; activity chains per car (`Schedules.cs`); `trafficsim`
   checks them and reports the demand against the surveys.
3. **Meso, headless (done 2026-11-27).** The whole day, all cars, with lights and parking. Conservation, the surveys, the worst queues,
   deadlocks; build and run time.
4. **Micro and the boundary, headless (done 2026-11-27; a block's persistence and stand-in physics cars left for phase 5).** `Driver`, micro areas round pretend players, the
   boundary, physics cars stood in by the driver; consistency against meso (trips to 1%); all the fault checks green on 64 seeds.
5. **In game, single player.** Far cars drawn from meso, near ones micro, the nearest physics; parked cars from the lots' state; the
   map overlay and the follow-car key; frame cost against the 130 to 140 fps goal.
   First part built (2026-11-27, not yet run in the game): `TrafficLive` builds the plans, the meso and the micro on a worker at the
   race's start hour (its first step round the spawn, so the junctions there turn micro before anything is drawn: the micro placed a car
   17 units off where the meso drew it when they flipped in sight) and steps them on the race clock on the main thread (about 2 ms a
   second of the race headless, half a millisecond a step); every car drawn smoothly between its last two steps' poses, a step behind.
   `TrafficCars` draws the nearest 240 (the old drawing carried over: the enemy car's bodies painted, lamps, headlights, a kinematic
   box each). `TrafficParking` stands parked cars in their spots (the old arithmetic; given out as the meso parks them); a car pulling
   out or in is slid between its spot and the road over 2.5 s, a stand-in. The player's car is a body the micro's drivers see
   (`MicroBodies`): they stop and queue behind it and keep out of a box it blocks or whose way out it blocks; trafficsim checks a block
   of two minutes at node 6 (no car into it, a queue, cleared after). Debug: the overlay's line, the follow-car key (F11), the map
   (`TrafficMap`). Still to do: physics cars for the nearest, pulling in and out driven rather than slid, the day's end (the
   simulation stops at 03:00), the frame cost measured in the game.
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
