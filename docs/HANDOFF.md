# Handoff

The one handoff: **overwrite it** at the end of each session with where things stand and what is next. Don't append history or dated
session notes; git keeps them (`git log -p docs/HANDOFF.md`). Standing decisions go in `docs/PLAN.md`, open in-game tests in
`docs/TESTING.md`, finished research in `docs/research/`.

Last written 2026-11-28 (fifth session that day).

## Where things stand

- **Branch: `traffic-rework-branch`** (the owner: stay on it). The traffic is being rebuilt from scratch: **read
  `docs/TRAFFIC_REWORK.md` first**. The old traffic is on `traffic-test-branch` (kept, not to be worked on, but **read it for what it
  already solved**; it only slid cars between spot and lane, so the ways below are new).
- **Phases 0 to 4 are done**; phase 5 (in game, alone): live traffic, parked cars, the player as a body, the nearest 60 moving cars as
  the game's real car (`TrafficPhysics`), the day's end, cars driving their ways out of spots and into them. Tested by the owner up to
  the physics cars; not tested: the day's end (item 73, not to be tested) and item 74 (contact physics, ways driven by physics cars,
  and now the new ways).
- **This session: the ways out of spots and into them fixed** (old Next 1). `TRAFFICSIM_GAME=only` passes seeds 1 to 10 (it failed
  `game ways` on 7 of them, with turns tighter than a car, half-minute ways, ways into parked and moving cars). What changed:
  - `Manoeuvre` plans with Reeds–Shepp paths (`ReedsShepp.cs`, OMPL's formulas; each word is driven through and kept only if it lands
    on the end pose) at radii 1, 1.25, 1.6 and 2.2 times `TurnRadius`, sorted by time as driven, the first clear of the parked cars, the
    houses and the water taken. Hemmed in: through a pose in the lane beside the spot (`Beside`, at least `Abreast` out); backed straight
    up 1.5 or 3 first (`Shifted`); a turn in the road in up to six full-lock moves (`Shuffled`; into a spot, a way out reversed in time).
    The lane may be joined up to `laneBack` behind the car's place and driven straight on (a word ends on its last arc, beside whatever
    is parked there): `TrafficLive.LaneBack` gives `PullRoom` less 2 at the kerb, nothing off the road; the nearest join with a clear way
    wins. Long reverse (over 1.5 car lengths) and full lock in reverse (over one) are reckoned slower when choosing (a body can't hold a
    long full-lock S backwards). `Hemmed` when nothing is clear (then the quickest, which trafficsim counts). At most 800 words built a
    plan (`Budget`), at most 8 tried between two poses (`Tried`). trafficsim prints `ways planned n, x ms each, the worst y ms`: 0.1 to
    0.8 ms on average; the worst 50 to 120 ms, but those are pauses in the runtime (the heap is big there), not planning: a plan of a
    hundred words measured 89 ms with the house and water tests off. The work itself is at most about 18 ms (800 words, ~22 µs each).
  - `TrafficParking`: an arriving car's kerb space is on its own side of the road (`Park(..., wayX, wayZ)`, from the lane it arrives on);
    a home's kerb space moves to that side too (`homeKey`); a car at home not arriving where it is drawn (the race's start, parked out of
    sight) faces the way its next drive leaves (`LeavingWay`, from its next leg's first edge).
  - `TrafficLive`: `ParkedNear` counts every held spot (a car given its spot on its way there), within 100; a cached way is checked
    again when it starts (`ClearOf`). `Beside` marks hemmed ways 60 s dearer and, for a hemmed way out of the kerb, tries places one to
    three car lengths ahead to turn there. `SweptClear` follows each micro car along its way ahead (`Micro.WayAhead`, round the bends) and
    refuses a way any moving car couldn't stop comfortably short of anywhere it is still to go (the drivers see all of it as standing
    bodies, `ManoeuvreBodies`: a car coming round a bend saw the way late and stopped in it). A car due off while still driving in
    finishes driving in first (`PullOutClear` false, the way in still drawn).
  - `Micro`: cars waving out a car that has waited stop `PullRoom` and a car's length short of its place (from a drive as from the
    kerb), and stop waving after 45 s if its way stays blocked: both were standstills (the waver standing in the way it waited for).
  - `MicroPhysics.RejoinAim` takes `meaning` (its driver wants to move): an astray body held by its driver in a queue no longer backs off
    every 2.5 s as if it had nosed into something (it backed 35 off its line and left its day).
  - trafficsim's `game block` asks for a queue of one, not two, where no car used the lane in the minute before (seed 9 at 04:11).
- Only 1,904 households reach the main road graph (PLAN.md lists it).

## Next

1. The owner tests item 74 in game (`docs/TESTING.md`, the ways added). Unknown: whether the game's controller backs as asked at
   manoeuvring speeds, and the wheelbase used for steering (`carStats.wheelbase`; Follow's turn assumes the rear axle 4.4 behind the
   middle). Watch the frame time when cars plan ways near the player (the worst plan's work is ~18 ms; Unity's collector differs).
2. After the owner tests: drop the contact physics' handling of cars pulling in or out and `Brake` if nothing needs them.
3. Later, as before: run `TrafficLive.Advance` on a worker if the frame time shows it (planning ways would go with it); phase 6 rooms
   (**nothing of rooms is in the new traffic yet**: carry over the old branch's `TrafficRoom`/`TrafficShared`, loose cars included, as
   `TRAFFIC_REWORK.md` §4 Rooms says; parking and ways are each machine's own, so the meso must never depend on them); seeds 65 to 96 now
   and then; right on red when it shows; the hole at (11145, -10111) and four end kinks.
4. Possible: a car whose trip goes back the way it faces in a street lined both sides does a multi-point turn (up to 6 moves); real
   drivers might go round the block, which the meso can't know (its routes must not depend on the parking).

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
- In game: nothing of the last three sessions tested.
