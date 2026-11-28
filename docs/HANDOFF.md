# Handoff

The one handoff: **overwrite it** at the end of each session with where things stand and what is next. Don't append history or dated
session notes; git keeps them (`git log -p docs/HANDOFF.md`). Standing decisions go in `docs/PLAN.md`, open in-game tests in
`docs/TESTING.md`, finished research in `docs/research/`.

Last written 2026-11-28.

## Where things stand

- **Branch: `traffic-rework-branch`** (the owner: stay on it). The traffic is being rebuilt from scratch: **read
  `docs/TRAFFIC_REWORK.md` first**. The old traffic is on `traffic-test-branch` (kept, not to be worked on).
- **Phases 0 to 4 are done** (the lane graph and conflict areas, each race's plans, the meso, the micro and the boundary; every fault
  check passing on seeds 1 to 64, trips taking the same time watched or not).
- **Phase 5 (in game, alone)**: the first part and the physics cars are built and tested in game by the owner (TESTING.md items 70,
  71, 72, all in the archive: all fine, a pushed car driving back to its line confirmed). In short: `TrafficLive` builds the plans, meso
  and micro on a worker at the race's start hour and steps them on the race clock; `TrafficCars` draws the nearest 240; `TrafficParking`
  stands parked cars; `MicroBodies` makes the player's car a body the drivers see; `MicroPhysics`/`TrafficPhysics` make the nearest 24
  micro cars the game's enemy car, steered along `Micro.WayAhead`, finding their way back when knocked off their line, leaving the day
  (lying as a body in the road) when wrecked, on their side or stuck. Details in TRAFFIC_REWORK.md §6, phase 5.
- **New this session: the day's end** (not yet tested in game: TESTING.md item 73). A day is 40 minutes of the race (100 s an hour),
  so a race started in the evening reached 03:00 within minutes and `Advance` stopped there: the through cars stood still on the highway.
  Now:
  - `Schedules.DaySeed(raceSeed, day)` (day 0 the race's own seed) and `Schedules.Shifted(car, offset)` (a day's stops moved on).
  - `Meso.Day`: each car's plans as the meso drives them (a copy of `Plans.Cars`; the race's `Plans` are never changed). `Meso.NextDay`
    at 03:00 gives each household car its next day: begun at once if it is home for the night, else when it gets home (`Park` begins
    it), a gone car stays gone; the through cars circle on. Every seed has the same cars in the same order (each household's own), so
    identities, home spaces and queues carry over (it throws if not). A car home late rests `MinStay` before setting off again.
  - `TrafficLive` starts the next day's `Schedules.Build` on a worker 300 day seconds (5 minutes of the race) before the end and applies
    it at 03:00 (`DayIndex`, `Today` for the overlay's weekday/weekend). `TrafficParking` now reads `Meso.Day`.
  - The traffic follows the weather's debug skip forward (`TrafficLive.SkipAhead` from `Sky.Skipped`: run through at 20 steps a frame,
    5 minutes in about a second; `Meso.RaceStart` moved by as much so the simulated lights stay the drawn ones). Not back.
  - Overlay: `traffic: hh:mm on a weekday (day 2), ...`; log: `Traffic: the day's end; day 2 begins, ...`.
  - trafficsim: the first seed's game path also runs from 01:30 for 8 minutes over the day's end (`DayEndChecks`): the next day begun,
    every car home for the night in it, cars setting off and arriving after 03:00, every through car still moving. (It waits for the
    worker before 03:00: its clock runs far faster than the worker; the game has 5 minutes.)
- What the stand-in can't tell (physics cars): the controller's exact response, the drawn-to-physics swap's look, the cost of 24 woken
  enemy cars; the owner found them fine in game.
- Only 1,904 households reach the main road graph. 110 places and the houses on the eight streets joined only by footpaths are left
  out (PLAN.md lists it).

## Next

1. The owner tests item 73 (the day's end, with the skip). If the skip's catch-up shows (20 steps a frame), lower `MostSteps` for it.
2. Pulling in and out driven by the micro from the spot instead of slid (`TrafficCars.Place`'s 2.5 s blend is a stand-in; a nose-in lot
   bay must be backed out of, a kerb spot pulled out of forwards; a physics car would then drive into its spot). The micro already has
   pulling out onto a lane (`CanPullOut`, the 15 s let-out) and turning in; what's missing is the path between spot and lane.
3. If the main thread's simulation shows in the frame time, run `TrafficLive.Advance` on a worker a step ahead (poses are already
   double-buffered; the bodies' readings and the player's body would be a step old).
4. Parked cars as physics cars when the player is about to hit one (the old `TrafficLoose`), if the owner wants them pushable.
5. Phase 6, rooms (TRAFFIC_REWORK.md §4 Rooms): the host's meso, link events, per-car ownership of micro and physics cars. Later days'
   plans are already the same on every machine (`DaySeed`).
6. Try seeds 65 to 96 of the full trafficsim now and then (each change so far showed a rare fault the earlier seeds hadn't).
7. Things to decide when they show: right on red (PLAN.md: measure again in the rework).
8. Optional world polish found earlier: the worst remaining hole at (11145, -10111) (node 1241, a crossing of farm lanes); four mild
   end kinks (lanes 30, 253, 259, 277).

## Working with the owner

- The owner doesn't know the map's place or street names and hasn't memorised the map: never ask them to decide about a place by its
  name or node number. Make the call yourself (they said "figure it out yourself"), and when you do need them, show a picture.

## Checks

- `tools/check.sh` (HalfGripRefs' game, the stub HalfGripTools built from `tools/halfgriptools-stub`): compiles clean (one old warning).
- `trafficsim`: seeds 1 to 32 (the default) pass, with the phase 5 checks on seed 1; `TRAFFICSIM_GAME=only` passes on seeds 1 to 8
  with the rejoin (shoved cars back on their line in 13.5 to 16 s; on seed 7 the shoved car parks at its stop instead; seeds 1 to 24
  passed before it).
- `meshcheck`: not affected (it compiles none of the traffic's files).
- Tools look for `../halfgriprefs/game`; with the repo cloned as `HalfGripRefs`, a symlink `halfgriprefs -> HalfGripRefs` does it.
- In game: phase 5 tested by the owner (items 70, 71, 72): all good, the push-off fix confirmed.
