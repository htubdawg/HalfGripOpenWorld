# Handoff

The one handoff: **overwrite it** at the end of each session with where things stand and what is next. Don't append history or dated
session notes; git keeps them (`git log -p docs/HANDOFF.md`). Standing decisions go in `docs/PLAN.md`, open in-game tests in
`docs/TESTING.md`, finished research in `docs/research/`.

Last written 2026-11-28.

## Where things stand

- **Branch: `traffic-rework-branch`** (the owner: stay on it). The traffic is being rebuilt from scratch: **read
  `docs/TRAFFIC_REWORK.md` first**. The old traffic is on `traffic-test-branch` (kept, not to be worked on, but **read it for what it
  already solved**, see below).
- **Phases 0 to 4 are done**; phase 5 (in game, alone): live traffic, parked cars, the player as a body, the nearest 24 moving cars as
  the game's real car (`TrafficPhysics`), the day's end. All tested by the owner except the day's end (item 73, not to be tested).
- **This session: driving out of spots and into them, instead of the 2.5 s slide. Pushed as work in progress (commit a2113cf);
  trafficsim fails.** What is in:
  - `Manoeuvre.cs` (new, pure): a car's way between its spot and the lane, one or two legs (forwards, reverse, backing off then
    forwards), cubic curves for the **rear axle** (front-wheel steering: the nose swings out; the middle is 4.4 ahead of the axle),
    no tighter than 14 units at the axle, clear of the cars parked round it (the cheapest clear candidate), timed from standing
    (4 m/s forwards, 2 m/s reverse, a pause at each gear change). A way in is planned as a way out driven backwards in time (headings
    kept, gears swapped), so the kinematics stay front-steered.
  - `Micro`: `PullOutAlong` (where a car pulling out comes onto the lane), `PullOutClear` (its way clear of standing cars and of cars
    that couldn't stop), `PullOutTime` (held standing, `Agent.HoldUntil`, `Why.Manoeuvre`, while its drawn copy drives out),
    `ArriveAlong` (asked once on its last lane: the game gives it its spot then and it pulls up beside it, `Agent.Dest`), `Arriving`.
  - `TrafficLive`: plans and poses the ways (`Beside` picks the place by the spot that gives the quickest way; spots given early are
    held, `early[]`); a car waiting for a gap stays drawn in its spot (it used to be drawn rolling off up the road); a car pulled up
    waits up to 20 s for its way in to clear; the ways and the rest of their paths are bodies the drivers see.
  - `TrafficCars`: the slide is gone. `TrafficPhysics`: a car held or manoeuvring isn't taken as a physics car.
  - trafficsim (`GameDay.cs`): no jump leaving or reaching a spot, no tighter than a car turns, under 30 s, not in a micro car nor a
    parked one. `TRAFFICSIM_WAYS=1` lists every way, `=bad` the failing ones.
- **What fails** (`TRAFFICSIM_GAME=only` on seeds 1 to 10; seed 1 passes except one way of 34 s):
  1. Kerb spots on the far side, facing against the car's way: a U-turn across a street is narrower than two turning radii, so no
     plan fits (fallback curves of 0.4 units). Fix: give an arriving car a kerb space on its own side (`TrafficParking.TakeKerb` tries the
     house's side first; pass the side of the lane it arrives on: a car on the right kerb faces the road's way when side is -1), and a
     three-point turn family in `Manoeuvre.Plan` for a car leaving the other way (the meso can't know spots, so its direction stays).
  2. Lot bays and pads far from the lane (55 off, E clamped 150 away): 30 s ways across the lot. Lots have no aisles known here
     (`TrafficParking.LotSpaces` has the lot's frame); decide whether to plan via the lot's mouth or keep such cars without a way.
  3. A way into a moving micro car (seeds 4, 7, 8) and into parked neighbours at the kerb (seed 7: a 19-unit kerb pitch for a 14-unit
     car is tight even for a real car).
  4. **Physics strays on seeds 5, 7, 8** (`physics stray`): caused by this change (the baseline passes); not looked into. Likely the
     manoeuvres' bodies or `Agent.Dest` against the stand-in physics bodies.
- **The owner's direction (2026-11-28): collision with parked cars and cars pulling in or out comes from the old branch's contact
  physics, not from making them physics cars.** On `traffic-test-branch`: `TrafficCars.Contact` (a drawn car the player is about to
  touch, swept along the player's velocity, becomes a loose real car just before contact), `TrafficRest` (the swap shows nothing: no
  drop onto the springs, no bounce), `TrafficPhysics.LooseBubble` (untouched: back to drawn once the player is 60 units away; touched:
  stays where knocked, retired from its day once the player is 500 away), config `TrafficLoose` (at most 6). This branch already has
  the woken enemy car, `LearnRest` and wrecks lying loose; the contact sweep and the loose bubble are what is missing. Read those
  files before designing anything; don't propose making every nearby car a physics car (the owner doesn't want it).
- Only 1,904 households reach the main road graph (PLAN.md lists it).

## Next

1. **Port the old contact physics** (above) to the new traffic: any drawn car (parked, pulling in or out, moving but not a physics
   car) the player is about to touch becomes a loose real car; a touched one leaves its day (`Micro.LoseCar` for a micro car; a parked
   one: its household goes without it) and lies as a body the drivers see (`OtherBodies`); an untouched one goes back.
2. Fix the manoeuvre failures 1 to 4 above until `TRAFFICSIM_GAME=only` passes seeds 1 to 10, then the full trafficsim on 1 to 32.
   Undecided whether to drop the drawn ways for cars far from the player (the owner didn't object to drawn ways; the old branch drew
   them).
3. Then update `docs/TRAFFIC_REWORK.md` §6 phase 5, CLAUDE.md's file table (`Manoeuvre.cs`; `TrafficLive`, `Micro`, `TrafficCars`
   rows), and add a TESTING.md item for the owner (cars pulling out and in; hitting parked cars).
4. Later, as before: run `TrafficLive.Advance` on a worker if the frame time shows it; phase 6 rooms; seeds 65 to 96 now and then;
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
