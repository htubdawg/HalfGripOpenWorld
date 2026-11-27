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
- **Phase 4 is built and its consistency largely tuned** (`Micro.cs`, `Driver.cs`; `docs/TRAFFIC_REWORK.md` §4, "As built (phase 4)"
  and the meso's "Consistency with the micro"). Nothing in the game calls it yet.
  - **trafficsim seeds 1 to 16: every check passes**, meso and micro round three pretend players. A day round three players runs in 2 to
    6 s. **Seeds 17 to 32: five of 16 fail** (see Next 2).
  - **Consistency** (seed 1, every junction micro against the meso alone): a trip loses **20.4 s** in meso against **22.8 s** in micro
    (it was 16.7 against 58.5). Lit lanes agree within half a second by road class; unlit lanes lose 0.3 to 0.4 s a car more in micro.
  - What closed the gap: the meso's free roads are now a lone car's run as the micro drives it (`Driver.Run`: pulling away by IDM, braking
    into the next turn), not a lane's length at its limit and a turn at 0.75 of its limit; a held car starts its turn from standing (with
    its place in the queue's room to pull away in); conflict areas timed along the same runs; merging, the micro's gap (room for the car
    behind to slow and keep its time gap); cars looked for through a short lane's junction before; no passing in a one-lane queue; routes
    cost each connector as a car drives it (slowing in, pulling away). The plans time each leg the same way (`Meso.LegTime`) and leave
    half of a car's whole day's driving spare (a car late back from one tour sets off late on the next).
  - Micro faults fixed on the way (each found by trafficsim, fixed in the model): a car taken over standing at a red crept over its line;
    a car queued behind one held at its line counted as coming (a give-way loop at node 800); two junctions decided as one judged clear at
    the current speed rather than the turn's limit (node 287); two cars out of the wrap's tunnels side by side in one meso queue placed a
    body apart at 80 u/s; a light's reason taken from the last step at the change to green (node 6's bay turned across an oncoming car);
    held-behind only for a standing car on the same lane at the same line; a car committed on the connector before is firm; a car whose
    way on can't be taken from a lane too short to change out of is looked for at the other connectors from it (nodes 573/537).
- Only 1,904 households reach the main road graph. 110 places and the houses on the eight streets joined only by footpaths are left
  out (PLAN.md lists it).

## Next

1. **What is left of the consistency gap** (`TRAFFICSIM_WORST=1` lists the lanes, `TRAFFICSIM_WHY=<lane>` what holds micro cars on one):
   - Micro over-yields: a driver waits for cars that will be stopped by a red at the junction before (lane 603 into node 47, waiting on
     cars at node 6's and node 44's lights). The fix belongs in `Micro.Clear`/`Coming`: a car whose light at an earlier junction is red
     isn't coming.
   - A slow turner in a shared lane holds up the cars behind it in micro (lanes 212, 210, 248: a right turn at 15 off a 52 road); the meso
     lets the follower go at full speed a headway later.
   - Micro's HCM saturation headway (from the fifth car of a queue) is 2.9 s against the meso's 2.0 (only about 30 queues that long a
     day): look at how the micro pulls away at a green (IDM's 1.5 m/s² and the decision at the line) before changing the meso's figure.
2. **Seeds 17 to 32** (`TRAFFICSIM_EVERYWHERE=0 dotnet run -c Release --project tools/trafficsim -- 17 16`): seeds 18, 22, 26, 28 and
   30 fail. Most are one kind: a follower overlapping its leader where their two connectors run into one lane (node 17 connectors
   107/105, 08:25 seed 18; node 458 1972/1970, 16:31 seed 26, with a box stop; node 5 18/16, 21:04 seed 28, with a box stop; two
   through cars on trunk lane 127 at 20:49, seed 28), and a box stop at node 42 (connector 235, 14:32, seed 30); seed 22 has one car not
   home. Each change to the traffic so far showed a few rare micro faults the 16 seeds hadn't: once these are fixed, make 32 seeds the check.
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
- `trafficsim` seeds 1 to 16: all checks pass. Seeds 17 to 32: five fail (Next 2).
- Full `meshcheck`: passed at the start of this session; this session changed nothing it reads (the traffic only).
- Not run in game (nothing in the game uses the lane graph, the plans, the meso or the micro yet).
