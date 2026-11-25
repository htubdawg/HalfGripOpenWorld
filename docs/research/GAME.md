# The game and the Multiplayer mod: findings

What was found out about Half Grip and the Multiplayer mod while building the Open World, moved word for word from the plan and handoffs. Reference only.

## The garage feasibility test (0.12.1): refitting a car in a race

On branch `claude/epic-goodall-zas0pe`, not merged. `Debug.GarageTest` (off by default) lists a key in Settings > Controls
(`Garage test (Open World)`, F9, through HalfGripTools' `ctx.Keys`; F8 is the game's bug report key; `mod.json` asks for tools
1.0.2). A press: reads the car's loadout (`PlayerLoadout.Equipped`), empties the car, fits the new loadout (the old one with the next
gun and steering wheel in the catalog), and 0.25 s later refills. Only the car changes, not the save or the Multiplayer loadout.
**The owner tested it in a Multiplayer room: every gun (the game's and the modded M1911, Minigun, RPG, Sniper Rifle, Railgun) and
every wheel swaps and works.** What it took, and what the garage must keep:

- **Fitting and removing** is the game's own live refit, `LoadoutRefit.Swap(off, on, loadout)` (the dev console's giveitem uses it
  in a race): stats re-applied, item prefabs spawned and removed, gun and shifter activators, sprites, body skin, `CarLink`. Car
  stats are read live, so engine, tires and body take effect at once. `Empty` removes items one at a time, `Fit` adds them.
- **Refill**: `Effects.Refill(RefillTarget.AllEffects, 1f)` (ammo, nitro, charges) and `Effects.Refill(RefillTarget.Health, 1f)`,
  the road pickups' path. Wait a frame or more after a refit: a new gun's rack only listens from its `Start`.
- **Guns are built into the cockpit**, one per kind (SmgAssembly, ShotgunAssembly, RevolverAssembly, SawedOffAssembly); a gun item
  only picks which is on, and taking it off only switches it off. A gun reads its item (capacity, magazines, blood-fed) and the gun
  mods convert it (M1911, Minigun, RPG, Sniper Rifle on the SMG; Railgun on the shotgun) once, as it starts up. So emptying the car
  also puts every gun back as built:
  - Copies of each gun and of its held model (`aimModel`, the 3D gun raised to the camera, under the camera, outside the gun)
    are taken the moment before the gun first wakes: Harmony prefixes at `Priority.First` on `NAutomaticGun`, `NPumpActionGun`,
    `NBreakActionGun`, `NRevolver` and `MagazineManager` `Awake` (the RPG and Sniper prefixes replace the SMG's Awake, and three
    of the four guns wake before `GunLoadoutActivator`). Kept under an inactive object moved into the race scene.
  - On reset, the live gun and held model are detached, unregistered from `Rig` and destroyed, and fresh copies put in their place.
  - The gun's references into its held model (fire point, the sawed-off's `barrels` array) are remapped by path, arrays and lists
    included, both when the copy is taken and on each reset.
  - The copies' sprites are recorded when taken and put back on each fresh copy: the gun mods' `Sprites.Reskin` swaps sprites by
    name across the whole scene, inactive objects included, and had put the Sniper Rifle's art on the stored SMG copy.
- **Multiplayer**: nothing new goes over the network. Others see only paint and body (`Skins` re-sends the body each second), PvP
  damage is applied on the owner's car, and HP goes as a fraction in each `Car` packet. Items stay local.

Looked into after the test (0.12.2), the two gaps the notes above named:

- **The Minigun's roof gun stayed on the car after swapping away.** The roof gun is built on the car body by `MiniHeat.Setup` and
  taken down only by `MiniHeat.Cleanup`, which the Minigun mod calls only when it unloads; `MiniHeat.OnDestroy` does not. Putting the
  SMG back as built destroys `MiniHeat` with the gun, so the roof gun was left turning on the roof (pointing ahead, its gun gone).
  Emptying now takes every Minigun roof gun off the car and runs each `MiniHeat.Cleanup` (by name: the classes are internal) before
  the guns are put back; a Minigun fitted again builds a fresh one as its gun starts. Only when an SMG copy exists (otherwise the SMG
  is not put back and the Minigun stays whole). The **remote** cannot stay: the reading of the code and the Tokyo scene dump puts
  everything the Minigun changes in the cockpit (the remote's sprites and its holder moved by `RemotePlacer`, the heat bar on the rack,
  the overheat padlock, the held model's mesh) under `SmgAssembly` or `HeldSmg`, which are replaced; to be confirmed in game (README
  test 22). The other gun mods: the Sniper Rifle's laser dot and scope overlay go in its gun's `OnDestroy`; the Railgun's charge and
  the RPG's launcher are components on the gun object; the RPG's rockets in flight, explosions and scorch marks are meant to outlive it.
- **Rockets from an RPG fitted mid-race.** The note was wrong as to the cause: the Multiplayer mod (`Cues.FindRocket`) looks up the
  RPG's rocket *type* by name once per game session, not the fitted gun at race start, and 20 times a second sends every live
  `RpgProjectile`; its Harmony prefix on `Explode` is on the type too. Its aim cue re-reads the guns every second. So an RPG fitted by
  the refit should show to the others exactly as one fitted at the start; the README's test 22 now asks for that in a room. (Others
  see the RPG's aim as an SMG's either way: the cue sends the gun's `RequiredGunType`.)

## 20 players: investigated, held off (Multiplayer caps a room at 6)

The cap is the Multiplayer mod's, not the Open World's:

- `Room.MaxPlayers` is a `const` 6. The Steam lobby is created with that limit, a 7th player is turned away, and the lobby screen's
  member and result rows are arrays of that size. A `const` is compiled into every place it is used, so no other mod can raise it:
  only a change to the Multiplayer mod can (its owner's repo).
- Its grid: the host deals slots from `Enumerable.Range(0, 6)`, and a race scene gets 6 slots (the player's and 5 more, extra ones
  added behind the last if the scene has fewer). The AI slider is also `6 - players`. All of that would need to grow with the cap.
- The Open World is ready for more: `WorldScene.GridSlot` gives any number of slots along the spawn's road (20 cars is 10 rows, about
  55 m), and the respawn already uses 24. The scene's own start-position objects (that the Multiplayer mod reads) number what Tokyo
  has; the Open World would add more if the Multiplayer mod asked for more than 6.
- Traffic: every player sends its car to every other player 20 times a second (about 70 bytes a packet), so with 20 players each
  sends and receives about 19 x 20 x 70 = 27 KB/s, which Steam's relay handles. Unknown until tried: 19 stand-in cars with full
  physics on each machine, and the host's share of reliable messages.

So 20 players needs, in the Multiplayer mod: `MaxPlayers` raised (or made a host setting), the lobby screen's lists made to scroll, and
the grid slots and their dealing sized from it. Worth asking its owner for, along with the mod channel below.

## Game 0.419 (0.26.1; the jMods audit)

Made for game 0.419 and HalfGripTools 1.0.6. The world is unchanged, so the same seed gives the same ground, roads and places.

- 0.419 changed only steering, speedrun and stats code, and the mod touches none of it. Every game member the mod names by string
  (Harmony targets, `Traverse` and `AccessTools` fields, `GameCaches`) is in 0.419's `Assembly-CSharp.dll`.
- `Weather.Pool` cached an empty pool when it ran before the run config existed, which left every later race of the session on the
  map's own weather. An empty pool is now looked for again at the next race.
- `MapEntry.Ensure` had the same missing-map check twice: the second copy is gone.
- Every Open World race made a new set of about 40 materials and their textures (ground, roads, verges, sidewalks, tracks, water,
  buildings, the circuit, gravel, the tunnel mouths' dark, the grass) and never freed them: made HideAndDontSave, they outlive the
  scene and Unity's unloading, so memory grew with each race of a session. `WorldAssets` (on the world's root) and
  `Foliage.OnDestroy` now free them, and their textures, with the world; only what the mod made (DontSave) is ever destroyed. The
  tunnel mouths use the roads' concrete instead of making a second set of road materials.
- The world was described as 32 km (20 miles) square, its tiles as 250 m and the settings' reaches in metres, all of them game
  units read as metres. At 3.3 units to the metre it is about 9.7 km (6 miles) across, a tile about 75 m. Corrected in `mod.json`,
  the README, the config descriptions, the build's log line, the ShowCoords overlay (thousands of units, not km), TESTING.md and
  the comments; CLAUDE.md now says so. Nothing in the world changed.
- Read through for the audit: every file that runs in the game and no tool runs (`Origin`, `MultiplayerLink` against the
  Multiplayer mod's protocol 8 source, `Streamer`, `WorldScene`, `Garage`, `GarageRefit`, `EventRace`, `EventBoard`, `EventLink`,
  `ShifterHand`, `GameMode`, `Patches`, `Sky`, `FarView`, `Foliage`, `Weather`, `MapEntry`). Nothing else found that needs
  changing; the generator is left to preview and meshcheck.
- Checks: every file compiled against HalfGripRefs' 0.419 `game/` (C# 10, netstandard2.1), with a stand-in for the HalfGripTools
  members the mod uses in place of the DLL, because building HalfGripTools was refused in that session; so `tools/check.sh` itself
  did not run. eventsim, preview and meshcheck pass (meshcheck and jview also build with the change).

## The game's car for the traffic, and the Enemy layer

- The game's car for the traffic: `TrackManager.enemyCarPrefab` ('EnemyPrefab' in the Tokyo dump). Its structure is in the main menu
  dump (`scenedumps/0.419/MainMenu`, search `CarSkinController`): root scale 1.5, `Pivot/BodyModel` and `Pivot/BodyModel2` (the two
  `carModels`), `Wheels/Meshes/Wheel_LF..RB` (mesh on a child), `Headlights/Spot Light`, lamps `BecuriFata`/`HeadlightsCar2` (material
  `bFata`) and `BecuriSpate`/`TailLightsCar2` (`becuri`). Paint: `CarSkinController.EquipSkin` sets `_AccentColor`, `_EntireColor`,
  `_TertiaryColor` on the GMTK "Car Body Channel Paint" shader.
- Game scripts that react to the `Enemy` layer: `AreaBlast`, `ExplosiveBarrel`, `PlayerResetLogic` (all null-safe for our boxes).
- The game's decompile (0.419) was put in the scratchpad with `ilspycmd`; the Tokyo scene dump is in HalfGripRefs
  (`scenedumps/0.419/Tokyo`): the `Player` root holds `CarInterior` (INTERIOR layer), `PlayerModel` (the car) and the cameras.
