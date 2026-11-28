# Handoff

The one handoff: **overwrite it** at the end of each session with where things stand and what is next. Don't append history or dated
session notes; git keeps them (`git log -p docs/HANDOFF.md`). Standing decisions go in `docs/PLAN.md`, open in-game tests in
`docs/TESTING.md`, finished research in `docs/research/`.

Last written 2026-11-28 (fourth session that day).

## Where things stand

- **Branch: `traffic-rework-branch`** (the owner: stay on it). The traffic is being rebuilt from scratch: **read
  `docs/TRAFFIC_REWORK.md` first**. The old traffic is on `traffic-test-branch` (kept, not to be worked on, but **read it for what it
  already solved**).
- **Phases 0 to 4 are done**; phase 5 (in game, alone): live traffic, parked cars, the player as a body, the nearest 60 moving cars as
  the game's real car (`TrafficPhysics`), the day's end. All tested by the owner except the day's end (item 73, not to be tested) and
  item 74 (contact physics, and now ways driven by physics cars).
- **This session: cars pulling out of a space or into one are physics cars** (old Next 1; the owner: only near a player, within 300
  units like every physics car, let go past 400). `TrafficPhysics.Ways` and the `waying` set: such a body isn't read by the micro (at
  the kerb it would be astray); it drives the way on its clock with `TrafficBody.WayDrive` from `Manoeuvre.Follow` (pure pursuit for the
  rear axle on each leg, forwards or reverse, speed for where the clock has it, stopping at the gear change; starts on `ClockLeg(t)` when
  taken mid-way). While it is more than 1.5 behind the clock, the clock waits (`TrafficLive.WindBack`: the way's start, the micro's hold
  in the lane, the bodies the drivers see). Way out driven: `Micro.TakeBody` on the lane, an ordinary physics car. A physics car arriving
  keeps its body: pulled up it stands (`TrafficLive.PulledUp`), then drives the way in, then is let go to the drawn parked car. Knocked off
  the way (`Manoeuvre.OffWay`: over 4 off, or turned and 1.5 off, or turned right round), on its side, or held 30 s: a knocked standing
  contact car (`Knocked`: on a way out `LetGoByBody` then `TakeByBody`, astray, finding its way back; on a way in held until out of sight).
  Wrecked: out of its day. `TakeNearest` takes drawn cars on a way too (`TrafficLive.WaysNow`, `WayOf`).
  - The contact physics still handles cars pulling in or out when no body was free (pool empty); the moving contact car on its brakes
    (`Brake`) stays for drawn moving cars. Not dropped yet: check in game first that nothing near the player is ever drawn moving.
  - Not tested in game. Unknown: whether the game's controller backs as asked at manoeuvring speeds (negative `engineAccel`, as
    `Rejoin` uses), and the wheelbase used for the steering angle (`carStats.wheelbase`, Follow's turn assumes the rear axle 4.4 behind
    the middle). Pure pursuit closes the loop, so a wrong wheelbase should only make it wobble.
  - trafficsim `PhysicsDay`: way stand-ins (rear-axle kinematics through `Follow`), taken anywhere in the micro round the player for
    coverage (`wayNear`, test only); checks `physics ways`: none off its way, stuck, or in a car. Held agents are no longer taken as
    ordinary bodies there (as the game), which cleared the old `physics stray` on seeds 5, 7, 8.
- **Driving out of spots and into them: the planned ways still fail** (`Manoeuvre.cs`, `Micro.PullOutAlong`/`PullOutClear`/
  `PullOutTime`/`ArriveAlong`/`Arriving`, `TrafficLive.Beside`). On `TRAFFICSIM_GAME=only` seeds 1 to 10: `game ways` fails on 1, 2, 3, 4,
  7, 8, 10 (tight turns, over 30 s, into micro cars on 4, 7, 8, into parked cars on 7); `physics ways` fails on 4 and 7 only from those
  same bad ways (bodies following them into a car; on seed 7 three turned off tight ones). Causes, as before:
  1. Kerb spots on the far side, facing against the car's way: a U-turn across a street is narrower than two turning radii, so no
     plan fits. Fix: give an arriving car a kerb space on its own side (`TrafficParking.TakeKerb` tries the house's side first; pass the
     side of the lane it arrives on), and a three-point turn family in `Manoeuvre.Plan` for a car leaving the other way.
  2. Lot bays and pads far from the lane (55 off): 30 s ways across the lot. Plan via the lot's mouth (`TrafficParking.LotSpaces` has
     the lot's frame) or keep such cars without a way.
  3. A way into a moving micro car (seeds 4, 7, 8) and into parked neighbours at the kerb (seed 7: a 19-unit kerb pitch is tight).
  4. `game block` on seed 9 (04:11, no queue formed: 1 standing): not looked into (night traffic, or the ways).
- Only 1,904 households reach the main road graph (PLAN.md lists it).

## Next

1. Fix the manoeuvre failures 1 to 3 above until `TRAFFICSIM_GAME=only` passes seeds 1 to 10, then the full trafficsim on 1 to 32.
2. Then update `docs/TRAFFIC_REWORK.md` §6 phase 5 (ways out and in, ways driven by physics cars, contact physics); TESTING.md item 74
   already lists the ways for the owner.
3. After the owner tests: drop the contact physics' handling of cars pulling in or out and `Brake` if nothing needs them.
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
- `trafficsim` `TRAFFICSIM_GAME=only` seeds 1 to 10: the contact checks, `ByBodyDay` and the physics checks pass but `physics ways` on 4
  and 7; `game ways` and seed 9's `game block` fail (see above); 5 and 6 pass everything. Before the ways, seeds 1 to 32 passed.
- The stub HalfGripTools: `dotnet build tools/halfgriptools-stub/HalfGripTools.csproj -c Release -p:GameDir=<game> -o <dir>`.
- Tools look for `../halfgriprefs/game`; with the repo cloned as `HalfGripRefs`, a symlink `halfgriprefs -> HalfGripRefs` does it.
- In game: nothing of the last two sessions tested.
