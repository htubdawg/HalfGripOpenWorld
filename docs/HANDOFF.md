# Handoff

The one handoff: **overwrite it** at the end of each session with where things stand and what is next. Don't append history or dated
session notes; git keeps them (`git log -p docs/HANDOFF.md`). Standing decisions go in `docs/PLAN.md`, open in-game tests in
`docs/TESTING.md`, finished research in `docs/research/`.

Last written 2026-11-28 (second session that day).

## Where things stand

- **Branch: `traffic-rework-branch`** (the owner: stay on it). The traffic is being rebuilt from scratch: **read
  `docs/TRAFFIC_REWORK.md` first**. The old traffic is on `traffic-test-branch` (kept, not to be worked on, but **read it for what it
  already solved**).
- **Phases 0 to 4 are done**; phase 5 (in game, alone): live traffic, parked cars, the player as a body, the nearest 24 moving cars as
  the game's real car (`TrafficPhysics`), the day's end. All tested by the owner except the day's end (item 73, not to be tested).
- **This session: the old branch's contact physics ported** (TESTING.md item 74, not tested in game). In `TrafficPhysics.cs`
  (`Contact`, `MakeContact`, `StandingBubble`, `Sweeps`): each frame after the drawing, any drawn car the player's car, swept along its
  velocity 0.3 s plus 2 units (margin 0.2), is about to touch becomes the woken enemy car on its brakes (config `World.TrafficLoose`, 6,
  apart from `TrafficPhysics`; a pool of 12 more for knocked cars). Two kinds:
  - **Standing** (parked, pulling in or out, pulled up, held in the lane for a way out: `TrafficLive.CanStand`): its car is held still
    (`TrafficLive.Freeze`: its way's clock, its wait to drive in and its hold in the lane stand still, its departure waits,
    `Meso.Held`; wound back on freezing to where it is drawn, a step behind). Untouched, the player 60 away: released, drawn again in
    place. **Knocked** (moved 0.5 or turned 3° after settling, hit by anything with a rigidbody, or damaged): **the owner, 2026-11-28:
    a hit parked car still carries on its day later.** It lies where it ended up (a body the drivers see once 1.5 off its spot) until
    the player is past sight (`LooseGone`), then is released and its car goes on from where it was held, late. Wrecked: gone from its
    day (`TrafficLive.LoseCar`, `Meso.LoseOutside`), burns and explodes as the others.
  - **Moving** (a micro car driving but not a physics car just then): a body on its brakes the micro reads (`TakeBody`, `Brake`).
    Untouched, the player 60 away: given back. Knocked: gone from its day (`Mi.LoseCar`), as the physics cars. The owner's word on
    hit parked cars may extend to these; not asked.
  - Not ported: `TrafficRest` (the worked-out rest on the springs, so the swap shows no drop or bounce). This branch puts the body at
    the learned rest height (`RestPlace`) or 0.15 up until learned; if the owner sees a parked car drop and bounce as it turns real,
    port `TrafficRest.cs` and `Settle` from the old branch.
  - trafficsim `ContactDay` (game path, every seed given): a parked car and one on its way held a minute don't move, let go don't
    jump; a held parked car doesn't set off and sets off once let go; a parked car, one on its way and a moving one knocked out of
    their day never come back and the queues still hold every car driving. Freezing jumps up to 0.5 out of sight (the drawn pose is a
    blend between steps, the way's curve is not); the checks allow 0.6.
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
- Only 1,904 households reach the main road graph (PLAN.md lists it).

## Next

1. Fix the manoeuvre failures 1 to 4 above until `TRAFFICSIM_GAME=only` passes seeds 1 to 10, then the full trafficsim on 1 to 32.
2. Then update `docs/TRAFFIC_REWORK.md` §6 phase 5 (ways out and in, contact physics) and add the ways to TESTING.md item 74 for the
   owner.
3. Later, as before: run `TrafficLive.Advance` on a worker if the frame time shows it; phase 6 rooms; seeds 65 to 96 now and then;
   right on red when it shows; the hole at (11145, -10111) and four end kinks.

## Working with the owner

- The owner doesn't know the map's place or street names and hasn't memorised the map: never ask them to decide about a place by its
  name or node number. Make the call yourself, and when you do need them, show a picture.
- **Check the old branch first.** Much of what phase 5 needs was solved on `traffic-test-branch`; the owner was angry at being offered
  designs from scratch for a solved problem. Keep answers short and do the work.
- The cars steer with the front wheels (the owner asked); say "the path is the rear axle's line" plainly if it comes up.

## Checks

- `tools/check.sh` (HalfGripRefs' game, the stub HalfGripTools built from `tools/halfgriptools-stub`): compiles (two unused-field
  warnings).
- `trafficsim`: **fails** on the game path from seed 2 (see above). Before this change seeds 1 to 32 passed.
- Tools look for `../halfgriprefs/game`; with the repo cloned as `HalfGripRefs`, a symlink `halfgriprefs -> HalfGripRefs` does it.
- In game: nothing of this session tested.
