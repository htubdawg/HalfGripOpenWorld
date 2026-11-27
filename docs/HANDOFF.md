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
- **Phase 4 is built and every fault check passes on 32 seeds** (`Micro.cs`, `Driver.cs`; `docs/TRAFFIC_REWORK.md` §4, "As built
  (phase 4)" and the meso's "Consistency with the micro" and "No catching the car ahead"). trafficsim's default is now 32 seeds (about
  4 minutes). Nothing in the game calls the traffic yet.
- This session fixed seeds 17 to 32's faults, each in the model:
  - **Meso, no catching the car ahead through a junction** (`Meso.Behind`/`Limit`): a fast car went straight on past a slow one
    turning off along an 85-long shared stretch (node 17), and the micro, taking the junction over, found them overlapping. A car going
    onto a connector now keeps a standing queue's spacing behind the last car from its lane onto the same connector or a sibling sharing
    the road, from the stop line to where they part: slower on, else it waits.
  - **Meso bug found on the way:** `Advance` worked out a car's speed onto its connector after setting `LastExit = Now`, so every car
    counted as part of a moving queue (a head at a green went on at 12.8 u/s, not from standing; `Clear` had timed it from standing).
    Fixed: `Go` hands `Advance` the speed and run it worked out. That exposed the 2 s headway behind a head pulling away from standing
    (the next car landed on its tail), which `Behind` now covers.
  - **Micro, coming in alongside:** two through cars held side by side on the trunk now start together; the inner one came in and moved
    over in front of the other, which was put off "a little later" every step until it came in in sight. It now comes in a standing gap
    behind if the lane has room.
  - **Micro, one box decided as one:** a car committed at node 90 (whose box runs straight on into node 52's past a 21-long lane) skips
    node 52's checks, but a driver at node 52 didn't count it as committed (not on the lane just before, and the flag is cleared moving
    onto a connector). Now firm there too (`OneBox`).
  - **Micro, pull-outs:** one took the only room past node 42's box that a car committed through it counted on (`RoomForBox`); one came
    out of a drive a car's length from node 43's box into a right turn passing it (`ClearOfBox`); one at the head of a queue that
    never emptied waited 338 s and got home after the day's end (seed 22's car 32, from the last handoff). A car waiting more than 15 s
    to pull out is now let out: cars behind stop short of it if they can do so comfortably.
- **Consistency** (seed 1, every junction micro against the meso alone): a trip loses **24.2 s** in meso against **23.1 s** in micro
  (it was 20.4 against 22.8). Trunk lanes, lit or not, agree within 0.3 s. Lit primary lanes lose 11.3 s a car in meso against 9.8 s in
  micro; lit secondary lanes 16.6 against 12.8 (only 70 cars); unlit lanes lose 0.1 to 0.5 s a car less in meso.
- Only 1,904 households reach the main road graph. 110 places and the houses on the eight streets joined only by footpaths are left
  out (PLAN.md lists it).

## Next

1. **What is left of the consistency gap** (`TRAFFICSIM_WORST=1` lists the lanes, `TRAFFICSIM_WHY=<lane>` what holds micro cars on one):
   - Lit primary and secondary lanes are now slower in meso than micro (above). Look at the meso's queue discharge at a green
     (`StartSpeed`'s run, `Behind`'s spacing, the 2 s `Headway`) against the micro's: the micro's saturation headway from the fifth car
     is 2.5 to 3.1 s by seed.
   - Micro over-yields: a driver waits for cars that will be stopped by a red at the junction before (lane 603 into node 47, waiting on
     cars at node 6's and node 44's lights). The fix belongs in `Micro.Clear`/`Coming`: a car whose light at an earlier junction is red
     isn't coming.
   - Unlit lanes lose a little more in micro (0.1 to 0.5 s a car).
2. Each change to the traffic so far showed a few rare micro faults the seeds hadn't. Try seeds 33 to 64 now and then
   (`TRAFFICSIM_EVERYWHERE=0 dotnet run -c Release --project tools/trafficsim -- 33 32`).
3. Still open in phase 4: a pretend player blocking a road (persistence: its queue still there on return, which needs a meso lane to
   know about a block); a physics car stood in by the driver; the room checks belong to phase 6.
4. Things to decide when they show: right on red (PLAN.md: measure again in the rework); the day's end (cars are planned home by 02:30,
   in the game the day wraps, phase 5).
5. Optional world polish found earlier: the worst remaining hole at (11145, -10111) (node 1241, a crossing of farm lanes); four mild
   end kinks (lanes 30, 253, 259, 277).

## Working with the owner

- The owner doesn't know the map's place or street names and hasn't memorised the map: never ask them to decide about a place by its
  name or node number. Make the call yourself (they said "figure it out yourself"), and when you do need them, show a picture.

## Checks

- `tools/check.sh` (HalfGripRefs' game, the stub HalfGripTools built from `tools/halfgriptools-stub`): compiles clean.
- `trafficsim` (32 seeds, the default): all checks pass.
- `meshcheck`: not run this session; nothing it reads changed (the traffic only: `Meso.cs`, `Micro.cs`).
- Tools look for `../halfgriprefs/game`; with the repo cloned as `HalfGripRefs`, a symlink `halfgriprefs -> HalfGripRefs` does it.
- Not run in game (nothing in the game uses the lane graph, the plans, the meso or the micro yet).
