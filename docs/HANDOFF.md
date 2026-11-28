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
- **Parking counted against the cars** (seed 1, a weekday; seeds 2 and 3 the same within a few): drawn spaces at each kind of place
  against the most cars its plans park there at once over the day (`Schedules.At` every 2 day seconds):

  | | places | drawn lot bays | kerb spaces on their stretch | meso's count (`Destination.Spaces`) | most at once | places short | cars over |
  |---|---|---|---|---|---|---|---|
  | High street | 169 | 0 | 1,620 | 338 | 141 | 87 | 108 |
  | Offices | 51 | 0 | 616 | 510 | 111 | 40 | 90 |
  | Church | 16 | 0 | 134 | 160 | 11 | 8 | 9 |
  | Shop, workshop, filling, market, retail | 37 | 244 | 934 | 352 | 54 | 0 | 0 |

  Homes: 1,904 households, 3,593 cars, all home at night: 2,613 own spaces, 252 at the kerb, **728 with none** ("round the back").
  So: demand at a place is small (a few cars), but high streets, offices and churches have no lot and about half stand on roads with no
  kerb parking (main roads); the meso's counts are invented (it parks 338 at high streets where nothing is drawn). The kerb column counts
  every pitch on the stretch, houses and blocked spots ignored, so the real shortfall is larger. The owner's point: the fix is parking
  that exists (a car park at every high street, office and church sized to what it needs; 728 home spaces; the meso's count the bays
  drawn), not cars rerouted round it. That changes the world's layout (a version for rooms). **Not started: the owner stopped it.**
- **This session's code is on the branch `traffic-rework-vanish`** (commit 84ff73e, one commit on top of this handoff), not on
  `traffic-rework-branch`: its kerb reroute fails seed 7, so it is kept apart until the parking is built. Take from it what is worth
  keeping (below); drop the kerb reroute (`Meso.Redirect`'s kerb part, `TrafficParking.KerbNear`) once places and homes have drawn
  spaces. Its trafficsim state: `TRAFFICSIM_GAME=only` seeds 1 to 6 and 8 to 10 passed with the hold-back (since taken out, not rerun);
  seed 7 failed (ways out and in hitting cars on the street by node 6). What it holds:
  - trafficsim `GameDay.cs`: a "game vanish" check: a car going from drawn to `None` or back within 400 units of the player
    (`TRAFFICSIM_VANISH` to change it) fails, the wrap's tunnels apart, with the car's state; every vanish anywhere tallied by cause.
    Before any fix: seeds 1 to 6, 8, 9 pass it, seed 7 fails (10 vanished, 2 appeared).
  - trafficsim `ParkingCount.cs`, `TRAFFICSIM_PARKING=1`: the table above.
  - Worth keeping from the fix: a micro car sent on from its stop (a full lot) stays the micro's car where it stands with a new route
    (`Meso.Resend`, from `Micro.Arrive`; `Meso.PathOn`: on along its lane, else routed from the lane's end). The old full-lot redirect
    re-entered it from the target's road, on another lane or the other carriageway: a 35-unit jump in sight. And a car parked in no
    drawn space held from setting off while a player is within 1,600 (`Meso.Unseen`), so it never comes out of nothing.
  - Tried and dropped: sending cars with no drawn space to the nearest free kerb (`Meso.Redirect`, `TrafficParking.KerbNear`, reserved
    and roomy spaces): vanishing near the player went, but the extra kerb parking on the busy streets by node 6 broke the ways in and
    out (cars backing into the car behind; seed 7 never passed; each fix moved the failures). A hold-back for the cars behind a car
    about to back into a space (it first held the parker itself and jammed the street) and a pause of the stray give-up timer while
    queued (with the shove test counting queue time) went with it.

## Next

0. **The vanishing cars** (above; the code so far on `traffic-rework-vanish`): ask the owner before starting anything. The owner's direction so far: the parking has to exist for
   the cars (count it, build it), not be worked round.
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
