# Traffic: travel studies, the trip model, the game's AI

Research behind `Traffic` and `TrafficDay` (their figures cite this file), moved word for word from the plan. Done; reference only.

## Traffic: what the studies say, and the trip model (researched 2026-11-22)

Sources (the tables used are cited below):
- FHWA, *Summary of Travel Trends: 2017 NHTS* (https://www.fhwa.dot.gov/policyinformation/documents/2017_nhts_summary_travel_trends.pdf)
- FHWA, *Summary of Travel Trends: 2022 NHTS* (https://rosap.ntl.bts.gov/view/dot/73764/dot_73764_DS1.pdf)
- PennDOT, *2023 Pennsylvania Traffic Data* (https://gis.penndot.pa.gov/BPR_PDF_FILES/Documents/Traffic/Traffic_Information/Annual_Report/2023/2023_Traffic_Information_Report.pdf)
- FHWA, Highway Statistics 2005, Table NT-3, vehicle trips by hour, 2001 NHTS (https://www.fhwa.dot.gov/policy/ohim/hs05/htm/nt3.cfm)

**Households (NHTS 2017, p. 9 and Tables 6b, 6c, 11, 16, 30, 34).** 2.55 persons, 1.88 vehicles, 1.89 drivers, 1.33 workers a
household. 1,865 vehicle trips a household a year (5.1 a day): to or from work 450 (24%), shopping 372 (20%), other family and
personal errands 434 (23%), social and recreational 410 (22%), the rest (school, church, work business) 11%. Average vehicle trip
9.6 miles (work 12.0, shopping 7.0, errands 6.9, social 10.6). Occupancy 1.18 to work, 1.82 shopping, 2.10 social. A weekday driver
spends 59 minutes driving. Rural people make slightly fewer trips than urban (3.2 against 3.5 person trips a day), 92% of them
drive, only 2.3% have no car. 2022 is 32% lower (1,262 vehicle trips a household) after the pandemic; the 2017 figures are the
normal day.

**Hours.** Person trips by start time (2017 NHTS Table 29): 10 pm to 1 am 2.3%, 1 to 6 am 1.9%, 6 to 9 am 16.6%, 9 am to 1 pm
25.4%, 1 to 4 pm 22.1%, 4 to 7 pm 22.1%, 7 to 10 pm 9.8%. Vehicle trips by hour (NT-3, 2001 NHTS), percent of the day from
midnight:
- Weekday: 0.3 0.2 0.2 0.1 0.5 1.3 3.2 7.7 5.9 4.5 5.1 5.9 6.2 6.0 6.6 7.7 7.8 8.6 7.4 5.6 4.1 3.1 1.5 0.8
- Saturday (5% more trips in all): 0.8 0.3 0.2 0.1 0.3 0.5 1.1 2.2 3.9 5.6 7.7 8.3 8.6 7.7 8.0 7.6 7.2 6.7 6.8 5.0 4.2 3.6 2.0 1.5

What the counters see on Pennsylvania's central rural collectors (PennDOT Table 350, TPG 9, percent of the day from midnight):
0.45 0.28 0.28 0.48 1.08 2.79 4.66 5.99 5.45 5.33 5.56 6.09 6.42 6.38 7.06 8.21 8.77 7.68 5.50 4.02 3.14 2.12 1.39 0.87. The
peak hour is 9 to 11% of the day (Table 385, K-factors).

**Volumes on Pennsylvania's roads** (PennDOT permanent counters by group, AADT = vehicles a day, both ways, pp. 15 to 17):
rural principal arterials 2,600 to 21,600 (median about 5,400); rural minor arterials 2,000 to 15,700 (about 5,000); rural
collectors 400 to 8,800 (about 1,800); urban minor arterials and collectors 400 to 15,600 (about 5,700). Local roads are not counted
(too many); the trip rate gives them: a house makes about 9.4 vehicle trips a day (ITE land use 210, single-family detached), so a
lane serving 20 houses carries about 190 vehicles a day.

**Our world.** 2 towns, 16 villages, 120 farms and hamlets, about 2,600 houses (from the 0.31 checks; shops and lots not separated out yet), about 280 drives: roughly 2,600 households and 4,900
cars, most of them parked at any time. The world's day lasts 40 minutes (`Sky.DaySeconds`, 100 seconds an hour): 36 times faster
than real, while a car drives in real time.

**The problem that sets the model: time runs 36 times fast, driving does not.** A real household's cars are moving about 110 minutes
of its 1,440 (1.89 drivers x 59 minutes), 7.6% of the day. Kept to that share, the roads look as Pennsylvania's counters say (worked
below); but in a 40-minute day that is about 3 minutes of driving a household, one trip or two across our country, not five. Five
trips each would put about four times as many cars on the road at once.
- Check, at the evening peak (8.5% of the day's driving in that hour): 2,600 households x 110 minutes x 0.085 / 60 = about 400 cars
  moving over the whole world. A collector at 1,800 a day carries 1,800 x 0.085 = 150 an hour; at 45 mph that is about 2 cars per
  km of road (both ways), and 400 cars over our roads comes to about the same. A principal arterial at 5,400 a day: about 5 per km.
  At 3 am: about 25 cars over the whole world.

**The trip model (proposed):**
1. **Every household and its cars** from the layout: houses in the towns and villages, farms, the homes on the drives; 1.88 cars a
   household (from the seed: some none, a few three). A car's home is its driveway, or a kerbside spot on its street in the towns.
2. **Places that draw trips**, from the layout: the retail park and the market places (shopping), the high streets' shops, filling
   stations, workshops and businesses on the main roads (work and errands), the circuit, rally service and hill climb paddock
   (social), churches (Sunday), other houses (social, visits). Each has parking: its lot's spaces, kerbside, a drive.
3. **Each race's day from the seed:** a weekday (5 in 7) or a weekend day, like the weather. Each household gets that day's tours
   from the purposes' shares and the hour tables above (a commuter out in the morning peak, home in the evening; shopping in the day;
   social in the evening), scaled so the time households spend driving is 7.6% of the day (above). Destinations by gravity (nearer
   and bigger draws more), within our 9.7 km.
4. **Through traffic** on the trunk and main roads, entering and leaving at the edges of the map (a rural principal arterial
   carries about 5,400 a day, most of it from beyond): the share that brings the trunk road up to its count.
5. **Parking is part of the timetable:** a car on a trip pulls into its destination's lot or a drive, stays parked for the trip's
   dwell (a work day, an hour's shopping, scaled to the world's clock), and drives home. Lots fill and empty with the day; driveways
   empty while their cars are out. A parked car is drawn parked (no physics until a player is near and it is about to leave).
6. Routes over the road network (fastest way by road class), junctions uncontrolled (above), the gaps and queues worked out.

**For the owner to decide:** keep the roads as busy as Pennsylvania's counters say (so each household drives once or twice in a
40-minute day; recommended: it is what the player sees), or give every household its five trips (about four times the cars on the
road at once). Either way the hours' shape is the studies'.

## Traffic: investigated after 0.17.0 (research; the plan above replaces it where they differ)

AI cars driving the roads. Feasible; the game's cars and car physics can be used as they are, but not its AI driver.

**What the game's AI is.** `EnemyController` is a racing driver on one closed lap held in a `PathManager`: every lookup wraps
with `% count` (`SampleLane`, `ResolveSegment`, the braking speeds), and the speed profile is solved over the whole lap and cached
per car type. Most of what it does is racing: weaving across three lanes, blockers and overtaking, health and wrecks, its guns,
nitro and rockets (`EnemyModule.Fit`), resetting itself onto the lap. `TrackManager.SpawnEnemies` fills a grid and registers each
car with the lap manager. The roads are the opposite: thousands of open polylines (`Network.Road`) meeting at junctions. Driving
them with `EnemyController` would mean rebuilding a `PathManager` per car as it goes, a window of road ahead; the window's two ends
join into a false hairpin, the braking worked back from it slows the car for nothing, and re-solving the profile per car costs too
much. The token loop works only because it is a closed lap.

**Reused:** the game's car prefabs (`enemyCarPrefab`, `devCars`: skins, damage visuals, smoke, engine sound, the "Enemy" layer);
`CarControllerNew`, driven through `steeringAngleDeg`, `engineAccel` and `brakeAccel`; `EnemyController`'s gear and throttle logic
(`DriveTowardsSpeed`, `AccelInGear`, `UpdateGearSelection`, about 80 lines, copied); `RemoteCar`'s smoothing as the model for
stand-ins. On a traffic car `EnemyController` stays, disabled, so player weapons (which look it up with
`GetComponentInParent<EnemyController>`) can still damage and wreck it; `EnemyGun` and the other `EnemyModule`s are switched off
so traffic never shoots. Traffic stays out of `tm.enemies`, so the lap manager and race logic never see it. `Origin.Shift` already
moves every root and rigidbody, so the cars follow a shift with nothing added.

**Written:** a `TrafficDriver` (steers for a point a little ahead in the left lane of a `Network.Road`, keeps a speed set by road
class and bends, follows the car in front); route choice at junctions from the junction graph `Signs` builds; spawning and
despawning per tile, density by road class and place. The route logic reads the roads without Unity (as `RoadMesh` does through
`Network.Point` and `Network.Spans`), so `meshcheck` can check it.

**Cost: two tiers.** Each physics car is a full rigidbody raycasting to the ground from every wheel, so only perhaps 20 to 30 near
the player (within about 300 m) are physics cars; further out cars move along the road without physics, and are despawned outside
the loaded tiles.

**In a room, control must be handed between players.** Each player loads tiles, and so ground colliders, only round their own car:
the host has no ground under a car near a far-away client, so a physics car there falls through the world. Whoever has that ground
loaded must drive it. The Multiplayer mod's AI sync cannot carry this: the host runs every AI car and sends `AiState` (up to 64 cars,
read from its source in 2026-11; "8" was wrong), its list fixed at the scene's start; the others see kinematic stand-ins
(`RemoteCar`). The design:

1. **Distant traffic on a timetable, nothing sent.** A car's position is a pure function of (room seed, car id, race clock), as
   `Storms` is, under the same determinism rules, so every machine draws the same distant traffic.
2. **Near a player, a physics car with one owner.** The owner simulates it and sends its state (about 45 bytes a car at 10 Hz);
   everyone else shows a kinematic stand-in.
3. **Owner per car, not per tile:** the nearest player, with hysteresis, the host settling disputes (everyone already gets every
   player's position 20 times a second). Hitting someone else's kinematic stand-in feels like hitting a wall; with the nearest
   player as owner, the player about to hit a car usually owns it, so the hit is real physics. Per tile would be simpler but hands
   cars over at every tile border and makes collisions worse there.
4. **Handoff:** the old owner sends a last state and "hand to X"; X spawns a physics car from it.
5. **Retirement:** a car that has been knocked about cannot rejoin the timetable; once outside every player's reach it is despawned
   for the rest of that cycle, with one short "retired" message.

This needs a mod channel between players: HalfGripTools 1.0.6 has it now (`ctx.Net`).

**Waiting on the Multiplayer mod.** No solo traffic first (the owner's call): traffic is built once, for rooms, when the
Multiplayer mod has the mod channel. Then all of it together: the driver on the game's car prefabs, per-tile spawning, the two
tiers, `meshcheck` checks on the routes, the timetable, ownership, handoff and retirement. (AI rivals for solo events are a
separate thing: they can use the game's own `EnemyController` unchanged, since a course is a closed lap, as the token loop is.)

Open questions for the owner: whether traffic should be shootable and wreckable; how dense.

## What the deer give traffic (discussed with the owner after 0.30.3; for next time)

The owner asked whether the deer make a good foundation for car traffic. The answer agreed: **they give traffic how it is placed and
how the room agrees on it, not how it acts near a player**, which is the larger part of the work. The design above still holds; the
deer change three things in it and take one blocker away.

**Carried over from the deer as they are:**
- **Positions from the seed and the clock.** `Herds.At` computes a deer from (race seed, group, race clock) with nothing simulated
  frame by frame; a traffic car's timetable is the same idea (point 1 above), and the deer show it works in a room, for late
  joiners, and on machines that have never loaded that ground.
- **Rare events settled by the host.** `DeerRoom`'s claims (the machine that caused a change claims it, the host keeps the first
  claim, a late joiner's Hello) and `tools/deersim` can be copied nearly as they are for traffic's events.
- **Drawn only near the car**, from a pool of objects (`Deer.Take`/`Give`); moved on an origin shift; hits tested in code where
  that is enough; a body thrown on fixed steps from an event (`DeerKills`), which could do for a car knocked aside far from its owner.
- **The blocker is gone.** "Waiting on the Multiplayer mod" above: `ctx.Net` now carries the events' and the deer's messages, so
  traffic's messages can go the same way.

**Changed from the design above:**
1. **Delays, not only retirement.** A car that leaves a player's reach goes back on its timetable as "car c is D seconds late from
   time t" (one claim, settled by the host, as a deer's reaction is), so traffic is not used up as players drive about. A car that is
   wrecked or knocked off its road is still retired ("car c gone from t").
2. **Queues worked out, not sent.** The timetables keep cars a safe gap apart and give junctions slots. Applying the delays lane by
   lane with a least gap between cars is pure, so a delayed car holds up the cars behind it the same way on every machine with no
   more messages.
3. **Ownership by claims.** Point 3 above (owner the nearest player, with hysteresis) can be the deer's way instead: a machine
   claims a car as it comes near, the host keeps the first claim, the owner hands it on with a claim too.

**What the deer do not cover, and why it is the bigger part:** a deer reacts once (startled, it runs, and a minute later it is calm
again: a few messages); a car near a player reacts all the time (following, waiting at a junction, swerving), so that part must be
driven on the owner's machine and its state sent, not claimed. Deer do not hold each other up; cars do (hence the queues). A traffic
car must be a real physics car (hitting a car moved in code feels like hitting a wall), and car physics cannot be made to come out
the same on every machine, so one machine owns each car near players (points 2 to 4 above).

**First steps next time:**
- Read how the game's AI racers steer (`EnemyController` in the decompile) and confirm what above can be reused for the
  `TrafficDriver` near a player.
- Find how a few extra cars' states can be sent (`ctx.Net` at 10 Hz, about 45 bytes a car) and shown as stand-ins (`RemoteCar`).
- Measure the timetables at the map's scale: a few hundred cars on routes over `Network.Roads` through the `Signs` junction graph,
  the gaps and junction slots, what `At` for one car costs; a `tools/trafficsim` like `deersim` for the claims and delays, and
  `meshcheck` checks that timetabled cars never overlap, stay in their lanes and keep to the speeds of their roads.
- Still open for the owner: whether traffic can be shot and wrecked; how dense; whether deer react to traffic cars (they could:
  a timetabled car is a known position at any time, so a dart in front of one can be worked out on every machine alike).

