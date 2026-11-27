# Handoff

The one handoff: **overwrite it** at the end of each session with where things stand and what is next. Don't append history or dated
session notes; git keeps them (`git log -p docs/HANDOFF.md`). Standing decisions go in `docs/PLAN.md`, open in-game tests in
`docs/TESTING.md`, finished research in `docs/research/`.

Last written 2026-11-27.

## Where things stand

- **Branch: `traffic-rework-branch`** (the owner: stay on it). The traffic is being rebuilt from scratch: **read
  `docs/TRAFFIC_REWORK.md` first**. The old traffic is on `traffic-test-branch` (kept, not to be worked on).
- **Phases 0 to 3 are done**: the world with the old traffic taken out, `Junctions.cs` and fixed-time `TrafficSignals`; the lane graph
  (`Lanes.cs`) and conflict areas (`Conflicts.cs`); each race's plans (`Schedules.cs`); the meso (`Meso.cs`).
- **Phase 4 is built** (`Micro.cs`, `Driver.cs`; how it works: `docs/TRAFFIC_REWORK.md` §4, "As built (phase 4)"). Nothing in the game
  calls it yet. `trafficsim` runs every seed's day with the micro level round three pretend players (one standing at Kingsbridge's
  light, node 6; two driving the lane graph at random, through the wrap too) and fails on every visible fault: bodies overlapping, two
  cars in one crossing/merging/head-on area past their lines, standing in a box, standing 10 s with nothing holding it or round a loop,
  held 300 s, changing level in sight, a jump, conservation across both levels.
  - **The 16 seeds (1 to 16): every check passes**, meso and micro. A day round three players runs in 4 to 6 s.
  - **Not yet robust: on seeds 17 to 32, three fail**: seed 22 one car not home at the day's end; seed 25 a crossing at node 280
    (a car at 13 u/s on connector 1195 into a through car at 53 u/s on 1190, 06:11) and a box stop at node 42 (connector 240, 15:57);
    seed 28 a crossing at node 89 (connectors 461/463, 15:48; node 89 and 126 are two junctions close together). Run
    `TRAFFICSIM_EVERYWHERE=0 dotnet run -c Release --project tools/trafficsim -- 17 16` to see them; `TRAFFICSIM_FAULTS=<file>` lists
    every fault and `TRAFFICSIM_TRACE=<cars>` with `TRAFFICSIM_TRACE_AT=<from>,<to>` (day seconds; a game day is 2,400 s, an hour 100 s)
    prints those cars every step. Every fault found this session had a cause worth fixing in the model, never in the check.
  - **Consistency (§5) is measured, not yet tuned.** Seed 1 with every junction micro (its fault checks pass too) against the meso alone:
    a trip loses **58.5 s** in micro against **16.7 s** in meso; micro's saturated headway at the lights is about **3.2 s** against the
    meso's 2.0; on unlit lanes micro loses 1 to 4 s a car where meso loses under half a second (slowing for the turns ahead and pulling
    away, which the meso's free time leaves out). The meso is optimistic: a car watched takes longer than one not watched.
- Fixed on the way (both levels): the wrap's jump connectors had no join, so the meso put through cars coming out of a tunnel at the far
  end of the next lane (`Lanes.JoinWrap` sets it now); a car off a connector now enters the next lane where its front is (`Meso.JoinFrom`)
  rather than at the lane's start; a lane's hold now also keeps a car waiting at its red clear of cars crossing on the other green
  (`Conflicts.Holds`; meshcheck passes).
- Only 1,904 households reach the main road graph. 110 places and the houses on the eight streets joined only by footpaths are left
  out (PLAN.md lists it).

## Next

1. **Consistency**: set the meso's figures from the micro (§4: `Headway`, `Jam`, the 0.75 of a connector's limit, and a loss for slowing
   into turns), or find why the micro is slow (its 3.2 s discharge is high against HCM's ~2 s: check how it is measured, IDM's 1.5 m/s²,
   the decision distance at a green). Then both levels' delays should agree within a few seconds a trip.
2. The three faults on seeds 17 to 32 (above), then run 32 seeds or more as the check.
3. Still open in phase 4: a pretend player blocking a road (persistence: its queue still there on return, which needs a meso lane to know
   about a block); a physics car stood in by the driver; the room checks belong to phase 6.
4. Things to decide when they show: right on red (PLAN.md: measure again in the rework); the day's end (cars are planned home by 02:30,
   in the game the day wraps, phase 5).
5. Optional world polish found earlier: the worst remaining hole at (11145, -10111) (node 1241, a crossing of farm lanes); four mild
   end kinks (lanes 30, 253, 259, 277).

## Working with the owner

- The owner doesn't know the map's place or street names and hasn't memorised the map: never ask them to decide about a place by its
  name or node number. Make the call yourself (they said "figure it out yourself"), and when you do need them, show a picture.

## Checks

- `tools/check.sh` (HalfGripRefs' game, the stub HalfGripTools from `tools/halfgriptools-stub`): compiles clean.
- `trafficsim` seeds 1 to 16: all checks pass (seeds 17 to 32: three fail, above).
- Full `meshcheck`: all checks pass.
- Not run in game (nothing in the game uses the lane graph, the plans, the meso or the micro yet; the world is unchanged but the waiting
  holds, which only the traffic reads).
