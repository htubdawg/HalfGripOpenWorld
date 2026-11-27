# Handoff

The one handoff: **overwrite it** at the end of each session with where things stand and what is next. Don't append history or dated
session notes; git keeps them (`git log -p docs/HANDOFF.md`). Standing decisions go in `docs/PLAN.md`, open in-game tests in
`docs/TESTING.md`, finished research in `docs/research/`.

Last written 2026-11-27.

## Where things stand

- **Branch: `traffic-rework-branch`** (the owner: stay on it). The traffic is being rebuilt from scratch: **read
  `docs/TRAFFIC_REWORK.md` first**. The old traffic is on `traffic-test-branch` (kept, not to be worked on).
- **Phases 0 and 1 are done**: the world with the traffic simulation taken out, `Junctions.cs` and fixed-time `TrafficSignals`; the
  lane graph (`Lanes.cs`: 2,846 lanes, 4,893 connectors, 3,056 of 3,080 turns fillets) and conflict areas (`Conflicts.cs`: 11,615).
  Nothing in the game uses them yet (only the tools call `Lanes.Ensure`).
- **Phase 2, population and schedules, is done** (`Schedules.cs`, new `tools/trafficsim`). Nothing in the game calls it yet either.
  - Each race's plans from its seed (deterministic, under `Network`'s rules): every household car's day as a chain of stops with a
    wanted leaving time and a least stay (`Leave`: a late car's next stops shift); tours from home, a third of commutes with a stop on
    the way home; places by gravity, the roads out (not the wrap's tunnels) taking the share beyond the map; thinned to the survey's
    driving share and given to the household's cars; 100 through cars circling the trunk; `At` gives a car's planned state at any
    hour on empty roads (the meso's start). A weekend 2 days in 7. Built in about 0.3 s (about 8,600 routes).
  - The weekday rates are now NHTS's purpose mix (work 0.54 a worker, shop 0.5, errand 0.62, social 0.55 a household); the old
    traffic's 0.86 a worker made work 41% of tours (now 27%, NHTS 27%). The share kept is tried six times and the one nearest the
    survey's driving used (98 to 100% on all 16 seeds).
  - Seed 1 against the surveys (local traffic at `Traffic.TrafficAmount` 0.5, as the owner set): 0.67 trips a household with a car in
    the 40-minute day (time runs 36 times fast; the survey's driving share kept, as before); peak 179 cars moving at 16:30 (survey
    about 200 at half) plus 100 through; cars moving by hour within 2.5 points of PennDOT's TPG 9 every hour, peak hour 9.4% (K 9 to
    11); trip starts by hour within 3.4 of NT-3's (they lead the cars moving by a world hour, as a drive spans one: matching the
    counters is what the player sees); a day's count at real time, median: primary 2,950 (PennDOT 5,000 at full traffic), secondary
    1,260 (1,800), trunk 22,000 (through cars); trips average 1.6 miles (NHTS 9.6, the map is 6 across); 2 of 273 places planned
    fuller than their spaces (high-street kerbs, 4 of 2 at worst); at the race's start (13:07) 3,351 cars home, 113 parked out, 18
    beyond the map, 111 on the road.
- Only 1,904 households reach the main road graph (110 places and the eight footpath-only streets' houses left out; PLAN.md lists it).

## Next

1. **Phase 3 (meso, headless)**, as `docs/TRAFFIC_REWORK.md` §4 and §6: `Meso.cs`, every car from `Schedules.At` at the start hour,
   links as queues (free-flow time, headway, storage, spill-back), junction admission from `Conflicts` and `TrafficSignals`, parking
   with capacity (a full lot sends a car on to the next), routes on the lane graph (it joins the wrap; `Traffic.Route` doesn't) on
   live link times. `trafficsim` grows conservation, the surveys from the simulated day, worst queues, deadlocks, build and run time.
2. Optional world polish found on the way: the worst remaining hole at (11145, -10111) (node 1241, a crossing of farm lanes); four
   mild end kinks (lanes 30, 253, 259, 277).

## Working with the owner

- The owner doesn't know the map's place or street names and hasn't memorised the map: never ask them to decide about a place by its
  name or node number. Make the call yourself (they said "figure it out yourself"), and when you do need them, show a picture.

## Checks

- `tools/check.sh` (HalfGripRefs' game, the stub HalfGripTools from `tools/halfgriptools-stub`): compiles clean.
- `trafficsim` (16 seeds): all checks pass.
- Full `meshcheck`: all checks pass.
- Not run in game (nothing in the game uses the lane graph or the plans yet; the world is unchanged this session).
