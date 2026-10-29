# Design notes

## Why 32 km needs a floating origin

Unity positions are 32-bit floats. The step between representable positions grows with distance from 0,0:

| Distance | Step | Effect on a car |
|---|---|---|
| 2 km | 0.12 mm | none |
| 5 km | 0.5 mm | none |
| 10 km | 1 mm | suspension and camera jitter begins |
| 16 km (the world's edge, from its centre) | 1 mm | jitter |
| 20 km and beyond | 2 mm and more | visible wobble, unstable contacts |

So the physics must never run far from 0,0. `Origin` moves the whole scene back by whole tiles when the car passes `ShiftDistance`,
and keeps the true position as a double offset. Only x and z move.

Shrinking the car instead does not help: precision relative to the car's own size is what matters, so a half-size car at 5 km is
like a full-size car at 10 km, and its handling would all need retuning.

## What moves on a shift

Transforms of every scene root (not screen-space canvases), then `Physics.SyncTransforms`. Interpolated rigidbodies have interpolation
switched off for two frames so they do not visibly slide. World-space particles, trail renderers and world-space line renderers are
moved point by point.

Positions the game keeps in fields (from a decompile of 0.418; every `Vector3`, `Vector3[]` and `List<Vector3>` field was checked, and
those holding world positions rather than offsets or directions are listed):

| Class | Field | Notes |
|---|---|---|
| `PathManager` | `sampledPoints`, `kdTree`, `laneSolves[].points` | the tree is rebuilt from the moved samples; `lanes` are offsets and heights, unchanged |
| `LapManager` | `lapDrivenFrom`, `projectedPathIndices` | |
| `TimeRewinder` | `frames[].pos` | the rewind item's recording |
| `RoadPickup` | `restPosition` | |
| `WheatTrampler` | `previousPosition` | on every car |
| `horizontalChicken` | `startPoint` | |
| `EnemyController` | `targetPoint` | AI |
| `FreeFlightCamera` | `targetPosition`, `homePosition` | debug camera |
| `WaypointsManager` | `processedWaypoints` | |
| `TrainPath` | `sampledPoints`, `lastPositions` | Tokyo only |
| `PlayerResetLogic` | `lowestOf` | a reference to `sampledPoints`; heights only, unchanged |
| Multiplayer `RemoteCar` | `position`, `AimPoint` | moved by `MultiplayerLink` |

Not yet handled: the Multiplayer mod's rocket ghosts keep their own last state; a rocket in flight during a shift may jump once.

## Multiplayer

The Multiplayer mod is not changed. Every position goes through its `W.Vec` / `R.Vec` (packet writer and reader), so those are
patched: a position is turned into world coordinates as it is written and back into this player's scene coordinates as it is read.
Which `Vec()` in each message is a position (protocol 7):

| Message | Vec()s | Positions |
|---|---|---|
| Car | position, velocity, aim point (when a gun is raised) | even |
| Respawned | position | even |
| AiState | (position, velocity) per AI car | even |
| Rocket | (position, velocity) per rocket | even |
| RocketEnd | position | even |
| Shot | landing points | all |

At hook time the message numbers are checked against the Multiplayer mod's `Msg` names; on a mismatch nothing is patched and a warning
is logged. Floats are good to about 1 mm at 16 km, which is plenty for drawing another car (only local physics needs better). Remote
cars are kinematic stand-ins, so one over ground that is not loaded on this machine floats; it does not fall.

## World

![Relief map of the whole 32 km world, north up, spawn in the middle](world.png)

`WorldGen.Height(x, z)`: rolling lowland (4 km gradient fBm, 90 m), mountain ranges where a 9 km mask allows (domain-warped ridged
noise, 520 m), hills (600 m, 22 m), bumps (70 m, 1.2 m), a flat pad of 250 m radius at the spawn blending out over 1.25 km, and a
700 m wall over the last 1.5 km of the edge. Each noise octave is turned 37 degrees so no lattice lines up. About 0.8 ms per tile.

## Next steps

1. **Roads.** A road graph across the world (generated once from the seed, cheap), cut into or laid on the tiles each tile touches.
2. **Surface.** Texture or vertex-colour the ground by height and slope (grass, rock, snow), and grip per surface.
3. **Landmarks and far view.** Low-detail meshes of the mountains past the loaded tiles, so the horizon is not empty in clear weather.
4. **Events.** Point-to-point sprints or time trials between places in the world, reusing the race flow.
5. **Collider baking off the main thread** (`Physics.BakeMesh`) if tile building shows in the frame time.
