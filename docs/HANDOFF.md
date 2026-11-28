# Handoff

The one handoff: **overwrite it** at the end of each session with where things stand and what is next. Don't append history or dated
session notes; git keeps them (`git log -p docs/HANDOFF.md`). Standing decisions go in `docs/PLAN.md`, open in-game tests in
`docs/TESTING.md`, finished research in `docs/research/`.

Last written 2026-11-28 (seventh session that day).

## Where things stand

- **Branch: `traffic-rework-branch`** (the owner: stay on it). The traffic is being rebuilt from scratch: **read
  `docs/TRAFFIC_REWORK.md` first**. The old traffic is on `traffic-test-branch` (kept, not to be worked on, but **read it for what it
  already solved**).
- **Phases 0 to 5 are built**; phase 5's in-game tests: the owner tested up to the physics cars; not tested: the day's end (item 73, not
  to be tested) and item 74 (contact physics, ways driven by physics cars, the Reeds–Shepp ways), and the frame cost.
- **This session: phase 6 (rooms) begun.** The rooms design is revised (`TRAFFIC_REWORK.md` §4 Rooms): instead of the host's meso
  sending link events and micro cars run on different machines, **every machine runs the same whole simulation in lockstep, the host
  settling each step's inputs in one bundle** (players' places and bodies, readings, takes and the game's other changes); physics cars
  and loose cars taken through the bundles (the old room's claim and hand-over rules); stand-ins from the old room's states and
  `Playback`; a late joiner sent the whole state; a state hash now and then to catch a machine out of step. Reason: the simulation is
  pure and already deterministic, so lockstep reuses all of it, where the first plan split it across the network. Done so far:
  - `DetMath.Atan2`, `Asin`, `Acos` (`Rng.cs`, exact to about 1e-15), used for all of `Manoeuvre`'s and `ReedsShepp`'s trig (the only
    C library maths in the simulation; `Network.Atan2`'s polynomial is too rough for the words' end checks).
  - `TrafficState.cs`: the whole `TrafficLive` saved by reflection (about 3 MB raw, 600 to 730 KB compressed, ~0.1 s) and loaded into a
    fresh build (`TrafficLive.Fresh`, ~1 s). Timing fields and the next day's task are `[NonSerialized]`; dictionaries and sets written
    in key order. trafficsim `TRAFFICSIM_STATE=only` (`StateDay.cs`): saved mid-race and at 02:30, loaded, run side by side 10 or 5
    minutes: byte for byte the same on seeds 1 to 10. On a failure it names the field path where the two part (`TrafficState.PathAt`).
- Only 1,904 households reach the main road graph (PLAN.md lists it).
- **Open bug, the owner saw it in game (0.33.0): a car vanished in front of them at a junction.** Cause found: a car arriving at its
  stop with no space the game can draw (`TrafficParking.Spot.Away`: a lot full and no kerb by it, a stop on a road with no kerb parking
  (only Secondary and Street have it), a household's car "round the back") pulls up in the lane and stops being drawn on the spot. The
  mirror: a car parked "round the back" (728 of them from the race's start) appears out of nothing when it sets off. On seed 1, 174
  cars vanished on the map in 10 minutes of race (99 at high streets, 32 at homes, 29 at offices, 7 off the map's edge); seed 7 had 10
  within 400 units of the pretend player and 2 appearing.
- **Why trafficsim passed:** `GameDay.cs` skipped any car whose pose turned `None`, so a drawn car disappearing was never checked
  (the jump check also let a jump over 120 units through). The owner asked whether the tests were being cheated: they had this hole.
- **Nothing of this session is pushed or committed** (the owner stopped the work; it lived in the container only). What was written,
  to redo:
  - trafficsim `GameDay.cs`: a "game vanish" check: a car going from drawn to `None` or back within 400 units of the player
    (`TRAFFICSIM_VANISH` to change it) fails, the wrap's tunnels apart, with the car's state (mode, micro or meso, spot away, the stop's
    kind); every vanish anywhere on the map tallied by cause; the jump check counting jumps over 120 near the player too. Before any
    fix: seeds 1 to 6, 8, 9 pass it, seed 7 fails (10 vanished, 2 appeared).
  - The fix begun: `Meso.Room` and `Meso.KerbNear` hooks set by `TrafficLive`; in `Meso.Arrive` a car with no drawable space tries the
    next place with room, else is sent to the nearest free kerb space (`TrafficParking.KerbNear`: its own stretch end to end, then the
    stretches round it a junction at a time, within 1,500 units; `Vehicle.KerbAt`, `Park(..., kerbAt, kerb)`), carrying on from the
    lane it stopped in (`Meso.SendOn`; re-entering from the target's road put it on another lane, even the other carriageway: a 35-unit
    jump; the old full-lot redirect has the same flaw). Taken kerb spaces kept in cells (`KerbBlocked`): on a bend two keys stood in one
    another. Result on seed 7: no vanish near the player, 7 sent on to a kerb, none parked without a space; still failing there: a car
    jumping 4.5 a frame 1,200 from the player as the micro takes it over mid-lane after it was sent on (car 3327: meso on lane 255,
    micro placed it at 293.7), physics overlap (175 frames), 8 bodies strayed, 1 car appearing from a "round the back" spot as it left.
  - Moving the race's start "round the back" cars to the kerb (`Meso.ParkAtKerb`) filled the streets: ways out and in into parked and
    micro cars, parked pairs overlapping, physics overlap, slow frames (worst 178 ms). Switched off.

## Next

0. **The vanishing cars** (above). The owner has not said to fix it yet: ask before starting. Options given to the owner: push only the
   vanish check (fails on seed 7, as it should), finish the fix until seeds 1 to 10 pass, or leave it. A car must never be put out of
   the drawing in sight: arrivals sent on to a drawn space; departures from an away spot need a drawn start (or the spot never away
   near a player).
1. The owner tests item 74 in game (`docs/TESTING.md`). Unknown: whether the game's controller backs as asked at manoeuvring speeds,
   and the wheelbase used for steering. Watch the frame time when cars plan ways near the player.
2. After that test (don't change `TrafficPhysics` under it): **the inputs as bundles**. `TrafficLive` takes every change from outside
   through one queue applied at the start of a step (`Mi.Read`, `TakeBody`, `GiveBody`, `LoseCar`, `Freeze`, `LetGoByBody`,
   `TookBody`, `WindBack`, `SkipAhead`, the players' places and bodies, `OtherBodies`); a change's result (`TakeBody`'s bool) is known
   only after that step, so `TrafficPhysics` waits for it (alone: one step, as the micro already reads bodies a step behind). The
   micro's `PX`/`PZ` then take every player. trafficsim's physics checks must still pass.
3. Then `TrafficRoom` (pure, modelled on `DeerRoom` and the old `TrafficRoom.cs` on `traffic-test-branch`: messages, the host closing
   bundles, clients stepping on them, Hello and the state for a late joiner, the hash, states and `Playback` for stand-ins, `Damage`)
   and a trafficsim room check: pretend machines with delays, drops, a stalling machine and a late joiner, every machine's state hash the
   host's at every step. Then `EventLink` carries its messages (the old branch's tag 0x7A) and `TrafficCars` draws stand-ins.
4. Later, as before: drop the contact physics' handling of cars pulling in or out and `Brake` if nothing needs them; `TrafficLive.Advance`
   on a worker if the frame time shows it; seeds 65 to 96 now and then; right on red when it shows; the hole at (11145, -10111) and four
   end kinks; perhaps going round the block instead of a multi-point turn.

## Working with the owner

- The owner doesn't know the map's place or street names and hasn't memorised the map: never ask them to decide about a place by its
  name or node number. Make the call yourself, and when you do need them, show a picture.
- **Check the old branch first.** Much of what phase 5 needs was solved on `traffic-test-branch`; the owner was angry at being offered
  designs from scratch for a solved problem. Keep answers short and do the work.
- The cars steer with the front wheels (the owner asked); say "the path is the rear axle's line" plainly if it comes up.

## Checks

- `tools/check.sh` (HalfGripRefs' game, the stub HalfGripTools built from `tools/halfgriptools-stub`): compiles (one unused-field
  warning).
- `trafficsim` `TRAFFICSIM_GAME=only` seeds 1 to 10: all checks pass. `TRAFFICSIM_WAYS=bad` lists bad ways, `=1` every way.
- The full trafficsim on 1 to 32: all checks pass (5.5 minutes).
- The stub HalfGripTools: `dotnet build tools/halfgriptools-stub/HalfGripTools.csproj -c Release -p:GameDir=<game> -o <dir>`.
- Tools look for `../halfgriprefs/game`; with the repo cloned as `HalfGripRefs`, a symlink `halfgriprefs -> HalfGripRefs` does it.
- `TRAFFICSIM_STATE=only` seeds 1 to 10: all checks pass (about 2 minutes a seed).
- In game: nothing of the last four sessions tested; the owner's 0.33.0 log (this session) shows contact cars knocked at -2416, -3278 and
  nothing about the car that vanished (the cause isn't logged).
