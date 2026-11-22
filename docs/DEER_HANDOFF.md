# Deer: handoff

For the agent picking up the white-tailed deer. Read `CLAUDE.md`, then the deer sections at the end of `docs/PLAN.md` (from "Next:
white-tailed deer" to "Reactions (0.30.0)"), then this. Work on the branch **`deer-test-branch`** (the owner's deer branch: commit
and push there, do not start another; the owner merges). The old `claude/keen-bell-4ybqq3` holds an earlier copy; leave it.

## Where things stand (0.31.0, pushed; the owner drove with 0.30.2 for ten minutes)

| Version | What | In-game test | Owner's word |
|---|---|---|---|
| 0.28.0 | Calm deer: groups from the race's seed, grazing, walking, crossing; the painted coat | `docs/TESTING.md` 42 | coat approved; no in-game report yet |
| 0.29.0 | Hits (car damage, the body thrown) and shots; `DeerAmount` setting | 43 | "really good stuff" (from the description, not a test) |
| 0.30.0 | Reactions (notice, alert, flee away / across / freeze; shots and kills spook); the host settles reactions and kills; late joiners' Hello | 44 | as above |
| 0.30.1 | Harder strike (about 13 HP at 55 on the speedometer, at most about 22; more shake, 8% speed, nose jolt); bodies thrown lower, bounce less | 43 | asked for it |
| 0.30.2 | Strike sound: the game's own wall crash and car crunch only | 43 | asked for it (the synthesized sound was "like a pinball machine") |
| 0.30.3 | Darts over the road timed to the car (first deer on the road 1.4 s before it gets there), chosen half the time; the shot's puff red | 43, 44 | asked for it: in 0.30.2 "none reacted fast enough to be a road hazard, most ran away from the road"; the sparks yellow |
| 0.31.0 | Bucks with one classic set of antlers (eight points; the doe's body); rare fawns (the doe shrunk to about 0.6, 3 a race) | 45 | asked for it, so (one set, no procedural antlers; a fawn just a smaller doe) |

**0.31.0 done the owner's way** (`docs/PLAN.md` "A buck and a fawn"): ask for their in-game report on it (TESTING 45) and on 0.30.3
(darts timed to the car, the red
puff) and fix what it shows.

**Before that, still open:** ask the owner for their in-game report (TESTING 42 to 44, `LogOutput.log`, screenshots) and fix what it shows
before anything new. Things only the game can show, each logged:
- The car's box for hitting deer, read from its colliders (`Deer: the car's box for hitting deer is ...`, or the fallback 6.4 x 4.5 x 14).
- The gravity bodies fall at (`Physics.gravity`; meshcheck assumes 9.81 x 3.3).
- Whether hits feel early, late or missed; whether the damage and shake feel right; whether the crash sound is heavy enough
  (the owner's next idea if not: two wall crashes layered).
- How jumpy the deer are (notice at `closing speed x 11.5 s`, 45 to 200 units; flee at 40% of that, or under 14 units, or 1.6 s off)
  and how often they dart across (30%, 55% if standing in the road): constants at the top of `Deer.React` and in `Herds.ChooseWay`.
- What `DeerAmount` feels right (alone only; 1 = 160 groups, about 540 deer; above 1 capped by the 743 sites).
- In a room: both players see the same runs and bodies; a late joiner sees bodies and where groups went.

## Decisions the owner made (do not reopen)

1. **No debug key** to stand a deer in front of the car. A teleport to just short of a crossing place, to test darts, can be added later.
2. **Where deer live is drawn fresh each race, like the weather** (from `Weather.Seed`, salted), not fixed with the map.
3. **The room's host settles** every reaction and kill (the host never changes: in this game the room ends when the host leaves, there
   is no hand-off). The machine whose car, gun or kill startles a group decides the reaction from its own exact position, plays it
   at once and claims it; the host keeps the first claim per group's reaction number (and per deer for kills). This replaced the
   first plan's per-group owners with hysteresis. Each machine tests hits only for its own car and shots only for its own gun.
4. **Deer never go into water deeper than their belly** (about 2 units), nor off drops, nor along other roads; fleeing runs only
   along the legs the calm deer use (tested), crossing only at the crossing place.
5. **A hit damages the car, and deer can be shot.** "Increase the impact" (0.30.1) was taken as more damage, shake, slowing and a
   jolt; the owner has not objected, but ask if the damage is too much.
6. **Sounds: reuse the game's own clips. Never synthesize a sound** (the owner disliked the one made in code). The `HitMetal` clips
   (bullet on metal) were also dropped as too pingy.
7. **How many deer is unclear** until tested: hence `DeerAmount`.
8. Do not reshape the model (`tools/deer/deer.json` is frozen: the owner's painted coat `OpenWorld/textures/deer.png` is on its UVs).
9. **One set of antlers** for every buck, a classic eight-pointer (no procedural antlers, no sizes); **a fawn is the doe shrunk**, rare
   (no model or texture of its own, no spots).

## How it fits together (the files; `CLAUDE.md` has the table)

- Pure, no Unity, under the determinism rules: `Herds.cs` (sites, groups, the calm day, `At`), `Reactions.cs` (part of `Herds`:
  `SetReactions`, `Live` = calm day with reactions on top, flee paths, the chain of spots overridden after a flee), `DeerRoom.cs`
  (the room protocol: claims, the host settling, Hello, the message inbox), `DeerKills.cs` (bodies on fixed 1/60 s steps from the
  kill's race time; the kill's encoding).
- Unity: `Deer.cs` (drawing, poses incl. the gallop from `gather`/`extend` and the tail flag, `CarHits`, `Hit` and its damage and
  sound, `Shoot`, `Gunshot`, `React` noticing for this machine's car, the `Link` to `EventLink`), `Patches.cs` `DeerShotPatch`
  (postfix on `Gun.FireRay`), `EventLink.cs` (deer messages start with byte 0xD3, routed to `DeerRoom.Received` before the events).
- Race times in messages work because Multiplayer 1.1.0 keeps every machine's race clock on the host's.
- Checks: `meshcheck deer` (mesh, calm deer, dead bodies, 480 flees, `DeerAmount`), `tools/deersim` (the protocol; breaking the
  host's first-claim rule makes it fail), `tools/check.sh`.

## Ideas not asked for (offer, do not build unasked)

- Reactions to other players' cars are decided on their machines only (each decides for its own car): fine while all run the mod.
- Deer walk through trees in the woods (trees are per tile, not in the generator's functions).
- Fawns' spots, lying down, a debug teleport to a crossing; the antlers as a target for shots.
- A picture of a group's escape routes was drawn from a temporary dump in meshcheck (removed); adding it to `preview` would help.

## Step 2, calm deer: done as below (kept for the record)

1. **Get the mesh into the mod.** The mod ships only source (`OpenWorld/src/*.cs`, compiled at game start), so write a small
   generator in `tools/deer/` (e.g. `export_cs.py`) that writes `OpenWorld/src/DeerModel.cs` from `deer.json`: positions, normals,
   region per vertex (colours chosen in code, so summer/winter/fawn palettes are a swap), UVs, triangles, bone indices and weights,
   the bones (name, parent, offset; bind rotations are all zero) and the poses. Mark it generated. Keep it C# 10 / netstandard2.1.
   Scale: the model is in metres; multiply by 3.3 for game units (shoulder about 3.1 units, length about 6).
   Axes: model faces +Z, Y up, left = +X (Unity is left-handed: check the mirror when converting). Poses are absolute local
   rotations given as three angles, applied as the matrix Rx·Ry·Rz (`rot()` in `deer.py`). Unity's `Quaternion.Euler(x, y, z)` is
   Ry·Rx·Rz, a different order, so build each pose's rotation from the three axis rotations in the model's order.
2. **Placement from the race's seed**, under the same determinism rules as `Storms` (only `Rng`, integer hashing, `DetMath`, no
   `System.Random`/`UnityEngine.Random`/`Math.Sin`): candidate places on edges of woods and fields (`LandUse`) near quiet roads
   (`Network`: lanes and back roads), dropped where the water is deep or the ground drops; then the seed picks groups, homes,
   feeding spots and crossing places. Use only the generator's pure functions, never the loaded tiles' meshes, because a group must
   be in the same place on a machine that has not loaded its tile.
3. **Water depth** = `Rivers.DrawnWater(x, z)` + `Network.Lift` - ground (as `Foliage.Wet`, `OpenWorld/src/Foliage.cs`); allow a
   margin of a unit or two, since the drawn ground can sit that far off the pure height at a channel's edge. Heights: `WorldGen.Height`
   is a scene height (includes `Network.Lift`); road `Y`, river `W` and place `Y` are from sea level. Mixing them sank every
   building once.
4. **Calm behaviour** as a pure function of (race seed, group, race clock `TrackManager.TimeSinceRaceStart`): grazing, walking
   between spots, the odd crossing (first deer, then followers a second or more apart, one sometimes turning back). Head bobbing
   and the gait from the poses (walk: `walk1` and its left/right mirror; gallop: `gather` and `extend`; see the viewer's code).
5. **Drawing**: a `SkinnedMeshRenderer` per deer near the car (a few dozen at most), posed in code; nothing or something cheap far
   off. Deer keep world positions, so move them on `Origin.Shifted` (only x and z shift).
6. **Checks**: `meshcheck` (homes, walks and crossings never in deep water or over a drop; crossing places on the roads they cross),
   `preview` (deer homes and crossing places on the map), `tools/check.sh` before every push. Bump `mod.json` and the README when
   it ships.

## Step 3 as first planned (kept for the record)

The owner has seen the calm deer's textured look and handed step 3 on. Ask for their in-game report on calm deer (TESTING 42) if
it has not come, and fix what it shows first. Then, per `docs/PLAN.md` "Multiplayer": a reaction overrides `Herds.At` for a
deer from a race time on (a path from t); the group's owner decides it from its car; hits each machine tests for its own car; the
host settles claims; `tools/deersim` like `eventsim`. A deer needs a collider (or a test in code against the car) for hits.

## This session's environment, as found

- **Compiling**: HalfGripTools.dll is not available and building HalfGripTools was not allowed, so `tools/check.sh` was run against a
  stub of the API it uses (`ModContext`, `ModKey`, `Net`, `NetPlayer`, `HalfGripMod`, keys and config; about 20 lines, built against
  the game's DLLs). Rebuild it in the scratchpad if needed (it is not in the repo); list what `src/` uses with
  `grep -ohE "ctx\.[A-Za-z.]+|HalfGripTools\.[A-Za-z.]+" OpenWorld/src/*.cs | sort -u`.
- The tools need `-p:GameDir=/home/user/HalfGripRefs/game` (the clone's folder is `HalfGripRefs`, the default looks for lowercase).
- `ilspycmd` is at `~/.dotnet/tools/ilspycmd` (not on the PATH); `DOTNET_ROLL_FORWARD=Major`.
- `tools/jview` would not build on 0.28.0 (it compiled `Deer.cs`); `Deer.cs` is now in its exclude list, as in meshcheck's.
- Python: `pip install pillow` was enough for renders (matplotlib missing); the model tools need `scipy matplotlib pillow`.
- Useful game facts found: `CarLink.TakeCrashDamage(damage, shake, DamageKind.Crash)` (god mode and race end respected), `MaxHp` 100
  base, `CarControllerNew.crashMinImpactSpeed` 7 / `crashFullDamageSpeed` 55 / `maxCrashDamage` 24 (speedometer = units a second x
  1.8, `SpeedDashboard.SpeedToDisplay`), `Shaker.instance.KickSpring`, `SoundController.PlayRandomSoundWithPitch2D` (through the
  game's mixer), the player's car has `wallCollisionSounds` (3) and `carCollisionSounds` (4); guns fire `Gun.FireRay(origin,
  direction)` from the player's camera, one per pellet.
- Journal sites were blocked; running a local browser to look at the viewer was refused in an earlier session.

## Working with the owner

- Show pictures and look at them yourself before saying something works; the owner tests in game and reports with screenshots and
  `LogOutput.log`.
- Plain words: say what a thing does for the player, not internal names ("calm deer from the seed" confused them).
- Do only what was asked; offer the rest.
- Send the owner a sample of anything they will judge by eye or ear (a render, a sound) and look at it yourself first; they answer
  frankly ("pretty bad lol") and quickly, so a small sample saves a wasted build.
