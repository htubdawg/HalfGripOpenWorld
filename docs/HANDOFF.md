# Handoff

The one handoff: **overwrite it** at the end of each session with where things stand and what is next. Don't append history or dated
session notes; git keeps them (`git log -p docs/HANDOFF.md`). Standing decisions go in `docs/PLAN.md`, open in-game tests in
`docs/TESTING.md`, finished research in `docs/research/`.

Last written 2026-11-28 (third session that day).

## Where things stand

- **Branch: `traffic-rework-branch`** (the owner: stay on it). The traffic is being rebuilt from scratch: **read
  `docs/TRAFFIC_REWORK.md` first**. The old traffic is on `traffic-test-branch` (kept, not to be worked on, but **read it for what it
  already solved**).
- **Phases 0 to 4 are done**; phase 5 (in game, alone): live traffic, parked cars, the player as a body, the nearest 24 moving cars as
  the game's real car (`TrafficPhysics`), the day's end. All tested by the owner except the day's end (item 73, not to be tested).
- **The contact physics** (TESTING.md item 74, not tested in game). In `TrafficPhysics.cs` (`Contact`, `MakeContact`,
  `StandingBubble`, `TakeByBody`, `Sweeps`): each frame after the drawing, any drawn car the player's car, swept along its velocity
  0.3 s plus 2 units (margin 0.2), is about to touch becomes the woken enemy car on its brakes (config `World.TrafficLoose`, 6, apart
  from `TrafficPhysics`; a pool of 12 more for knocked cars). Two kinds:
  - **Standing** (parked, pulling in or out, pulled up, held in the lane for a way out: `TrafficLive.CanStand`): its car is held still
    (`TrafficLive.Freeze`, `Meso.Held`). Untouched, the player 60 away: released, drawn again in place. **Knocked (this session, the
    owner: it does its trips while watched)**: when its departure comes (`TrafficLive.DueOut`: parked and due, or at once if it was on
    its way out), still upright, slow, and the player within 300, it is let go (`LetGoByBody`: the meso departs it, it comes onto the
    lane with no drawn way and no hold) and once the micro has it on the lane its body is taken (`TakeByBody`, `Micro.TakeBody(..,
    fromAside: true)`: astray at its first reading off its line), so it finds its way back with `RejoinAim` and drives on as a physics
    car. On its side when due: out of its day. Further off than 300: as before, held until the player is past sight, then its car goes
    on from its space. Knocked pulling **in**: unchanged (held until out of sight, then parks). Wrecked: out of its day.
  - **Moving** (a micro car driving but not a physics car just then): a body on its brakes the micro reads. Untouched, the player 60
    away: given back. **Knocked (this session): drives on as an ordinary physics car** (`TrafficBody.Drive`: brakes off, engine on);
    `Micro`'s astray logic brings it back to its line; only on its side, stuck or wrecked does it leave the day.
  - **`World.TrafficPhysics` default raised 24 → 60** (max 120), the owner: every moving car near the player a physics car.
  - Not ported: `TrafficRest` (the worked-out rest on the springs). If the owner sees a parked car drop and bounce as it turns real,
    port `TrafficRest.cs` and `Settle` from the old branch.
  - trafficsim `ContactDay` (game path): cars held a minute don't move, let go don't jump; a held parked car sets off once let go;
    **`ByBodyDay` (new)**: a parked car near node 6 held over its departure (stand-in body 4 off its space, turned 30°) is due, let go
    by its body, taken from aside within a minute and back on its line within a minute (seed 1: taken 1 s after due, back 9 s later);
    cars knocked out of their day stay gone with the queues whole.
- **Driving out of spots and into them (last session, still failing, see Next 1)**: `Manoeuvre.cs`, `Micro.PullOutAlong`/`PullOutClear`/
  `PullOutTime`/`ArriveAlong`/`Arriving`, `TrafficLive.Beside`. Failures on `TRAFFICSIM_GAME=only` seeds 1 to 10 (seed 1: one way of
  34 s):
  1. Kerb spots on the far side, facing against the car's way: a U-turn across a street is narrower than two turning radii, so no
     plan fits. Fix: give an arriving car a kerb space on its own side (`TrafficParking.TakeKerb` tries the house's side first; pass the
     side of the lane it arrives on), and a three-point turn family in `Manoeuvre.Plan` for a car leaving the other way.
  2. Lot bays and pads far from the lane (55 off): 30 s ways across the lot. Plan via the lot's mouth (`TrafficParking.LotSpaces` has
     the lot's frame) or keep such cars without a way.
  3. A way into a moving micro car (seeds 4, 7, 8) and into parked neighbours at the kerb (seed 7: a 19-unit kerb pitch is tight).
  4. Physics strays on seeds 5, 7, 8 (`physics stray`), caused by the ways; not looked into.
  5. `game block` on seed 9 (04:11, no queue formed: 1 standing): new on this run, not looked into (night traffic, or the ways).
- Only 1,904 households reach the main road graph (PLAN.md lists it).

## Next

1. **Cars pulling out of a space or into one are physics cars too** (the owner; the rest of the old Next 1 is done). Now
   `TakeNearest` skips them (`live.Manoeuvring`, `HoldUntil`) because the body only steers along `Micro.WayAhead`. Plan: a body in
   `bodies` that is not yet `TakeBody`'d (the agent is held in the lane meanwhile, `HoldUntil`; the micro mustn't read a body at the
   kerb, it would go astray) drives the way's path on the way's clock (`Manoeuvre.At` at `M.Now - manSince`, the rear axle's line;
   pure pursuit forwards, and backwards on a reverse leg with the pursuit point behind; pedals from the along-path error to where the
   way has it), then at the way's end `TakeBody` and `WayAhead` as now. A way in ends with the body released to the drawn parked car.
   A stand-in in trafficsim for it. Then nothing moving near the player is ever a drawn car, and loose cars are only parked ones (drop
   the loose-car handling of cars pulling in or out, and the moving contact car on its brakes, `Brake`, once nothing needs it).
2. Fix the manoeuvre failures 1 to 4 above until `TRAFFICSIM_GAME=only` passes seeds 1 to 10, then the full trafficsim on 1 to 32.
3. Then update `docs/TRAFFIC_REWORK.md` §6 phase 5 (ways out and in, contact physics) and add the ways to TESTING.md item 74 for the
   owner.
4. Later, as before: run `TrafficLive.Advance` on a worker if the frame time shows it; phase 6 rooms (**nothing of rooms is in the new
   traffic yet**: carry over the old branch's `TrafficRoom`/`TrafficShared`, loose cars included, as `TRAFFIC_REWORK.md` §4 Rooms says); seeds 65 to 96 now and then;
   right on red when it shows; the hole at (11145, -10111) and four end kinks.

## Working with the owner

- The owner doesn't know the map's place or street names and hasn't memorised the map: never ask them to decide about a place by its
  name or node number. Make the call yourself, and when you do need them, show a picture.
- **Check the old branch first.** Much of what phase 5 needs was solved on `traffic-test-branch`; the owner was angry at being offered
  designs from scratch for a solved problem. Keep answers short and do the work.
- The cars steer with the front wheels (the owner asked); say "the path is the rear axle's line" plainly if it comes up.

## Checks

- `tools/check.sh` (HalfGripRefs' game, the stub HalfGripTools built from `tools/halfgriptools-stub`): compiles (one unused-field
  warning).
- `trafficsim` `TRAFFICSIM_GAME=only` seeds 1 to 10: the contact checks and `ByBodyDay` pass (seeds 3 and 9 have no parked car near node 6
  leaving within half an hour, so `ByBodyDay` is skipped there); `game ways`, `physics stray` (5, 7, 8) and seed 9's `game block` fail (see above). Before the ways, seeds 1 to 32 passed.
- Tools look for `../halfgriprefs/game`; with the repo cloned as `HalfGripRefs`, a symlink `halfgriprefs -> HalfGripRefs` does it.
- In game: nothing of this session tested.
