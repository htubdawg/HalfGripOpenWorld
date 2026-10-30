# Plan

Agreed with the owner after testing 0.4.3. Work top to bottom; each PR runs `tools/check.sh`, `tools/preview` and `tools/meshcheck`
before it goes up, and adds checks for what it fixes.

## Map API for MiniMap (0.4.6; asked for by the MiniMap mod)

Done: `MapView` (public, read by reflection; see README) gives each loaded tile's map picture (land use, water, roads by class) and
the origin. Display only, outside the plan's order; the world is unchanged.

Status: merged (PR #12), not yet tested in game. Open with the owner:
streets are drawn grey and bridges in their road's colour (world.png draws them red); change `Streamer.MapRoads` if asked. The
picture is made by `Streamer.MapPicture` from `Colours()`'s land use; no tool runs it (it needs `Streamer`), so check it in game.

## PR 1b: river water hanging over the ground (0.4.5; reported after testing 0.4.4)

On 0.4.4, 3021 of 8765 points along the rivers' water edges were over the drawn ground. Causes and fixes:

- The water level came from the land at the river's centre, smoothed; meanders put the line on slopes and smoothing lifted it
  above narrow valley floors. Now a unit under the lowest land across both banks, as cut by the rivers before it (longest first),
  never smoothed above it.
- The channel's edge was a 1.2 step; the ground's 5-unit triangles dipped under the water there. It now rises over 5, and the
  water surface is drawn far enough out to stay under the ground.
- A place's levelling sank river banks: the floodplain is now kept.
- A river's floodplain (up to 270 wide) spread across a neighbouring river or a meander's next loop, lower down. The nearest river
  now shapes the ground; others cut it through a 1 in 1 bank, and a bank up to 10 is raised beside water if needed.
- Left: 37 points (0.4%), on steep mountain bends and where a river runs right beside a lower tributary's channel.
- Roads re-routed over the new land showed a lane coming off a bridge onto a street on the floodplain: decks now ramp down over
  the banks (only the part over water is held), and mouths of streets and lanes may take up to 1.75 times their grade.

## PR 1: fixes (0.4.4: merged)

How it was done, where it differs from the notes below:

- Bridges: crossings found by intersecting road segments with river lines; each deck spans the water at the angle crossed, plus 6
  each side; `Hold` and `Seat` never move a deck; the banks beside a bridge never fill the channel. On 0.4.3, 3179 of 7351 road
  points over water were less than 4 above it (the lowest 6.1 under it); now none (`meshcheck` bridges).
- Junctions go further than the note: wherever a road's centre line runs onto a road drawn over it (bigger, or of its class and
  built earlier), at its end or in a crossing, it is cut out along that road's edges, flared, and its apron tilted to meet that
  road's surface 0.05 under it. Mouths may be up to a quarter steeper than the class grade (`meshcheck` grades).
- Farm tracks are 10 wide (were 9); the ground texture is now 128 pixels a tile (the land use stays 64) so the ruts and strip show.
  Their grip comes from a `HandlingModifier` subclass on the tile that looks the wheel's point up in the texture.
- Found on the way: a road's smoothed line could miss the router cells it was routed through, so a later route built the same
  stretch again on top of it; the cells are now marked. This changed the layout a little (fewer duplicate main roads).

1. **Bridges** (reported: a river drawn over a bridge). Likely cause: crossings are found by testing road *samples* (8 apart) for
   being inside a river's channel, so a narrow river can be stepped over with no bridge, and the road then dips below the water. A
   second suspect: `Seat` (run after `Bridges`) lowering a deck near a junction. Fix: find crossings by intersecting road segments with
   river lines; deck clear of the water over the span and its approaches; `Seat` never lowers bridge samples. Check: every road point
   over a channel at least 4 above the water.
2. **Farm tracks into the ground** (owner likes them): no mesh; the ground pressed into a shallow bed (about 0.3, soft edges); the
   ground texture paints dirt with two tyre ruts and a grass strip; no grass or trees on them; dirt grip.
3. **Dirt verges** on every paved road: a strip about 4 wide each side from the road surface down to the real ground at its outer edge,
   gravel texture, a little less grip; none on bridges. Check: no step over about 0.1 at either edge.
4. **Junctions** (rare road-on-road clipping left): a minor road ends at the major road's edge with a flat apron in the mouth;
   same-class crossings share one height. Check: `meshcheck` poke at 0.

## PR 2a: town buildings (0.5.0)

Done on branch `claude/epic-goodall-zas0pe`: compiled, preview and meshcheck clean (a new `lots` check), not yet tested in game.

- The Tokyo landmarks are gone (`Landmarks.cs` deleted); each village has a stone church with a tower and spire instead.
- **Centre:** in the main town's middle (within 0.16 of its radius) the plots take glass towers: 8 to 14 storeys, two or three
  stacked setback blocks, curtain-wall texture, plant rooms and masts on the roofs.
- **High street:** on the main roads through a town's core or a village's middle, terraces of 2 or more storeys with a shopfront in
  one of four colours (window, door, sign band) on the ground floor.
- **Lots** (`Town.cs`): a retail park at Kingsbridge's edge (car park with two double rows of bays, a lamp-post island, a big
  building with a glass front and a sign) and 34 businesses on the main roads at town and village edges: shops with a row of bays,
  workshops with a yard, filling stations with a canopy, pumps and a kiosk. A lot's surface is its own mesh with fine textures (the
  owner found the painted farm tracks too blocky, so nothing new is painted into the ground texture), level across and following
  the road's grade, its front meeting the foot of the verge; the ground under it is levelled. Tarmac grip.

## 0.6.0: teleport round key places, tracks as meshes, footpaths, sidewalks (asked for after 0.5.0)

Done on branch `claude/epic-goodall-zas0pe`: compiled, preview and meshcheck clean (new `ribbons` and `sidewalks` checks), not yet
tested in game.

- The debug teleport (F7, Shift+F7 back) goes round the key places (`Destinations.cs`) instead of a fixed distance ahead.
- Farm tracks are ribbons with a fine texture and dirt grip, no longer painted into the ground (which is back to 64 pixels a tile).
- Footpaths (`RoadKind.Path`) on from the dead ends of streets into the country.
- Sidewalks on town and village streets and main roads.

## 0.7.0: fixes after 0.6.0, parks and trails (PR 2b)

Done on branch `claude/epic-goodall-zas0pe`: compiled, preview and meshcheck clean (new `flooded`, `plants` and `parks` checks),
not yet tested in game.

Fixes the owner reported on 0.6.0:

- **Workshop walls in pinkish pinstripes.** The metal cladding had a rib every pixel (a unit) with no mipmaps, point-filtered: past a
  few car lengths it broke into moiré stripes (pinkish under the scene's grading). Ribs are now 4 pixels apart (a lit edge, the
  sheet, a shadow) with mipmaps; the roofs' rows (the workshop's metal roof is big and seen from afar) got mipmaps too. Not
  checkable outside the game: confirm in game.
- **Water over the highway.** No river did it (meshcheck's new `flooded` check found no road under a river's water): a lake's or
  the sea's surface is drawn flat over its cells and the cells round them, and the trunk road along the coast ran up to 4.3 under
  it (lanes 4.7, main roads 1.1; 11,962 of 1.2 million road surface samples). Every road is now raised to 1 over any water drawn
  over its paving, ramping at its grade (`Network.Dry`). If the owner meant a spot on a river, the check says there is none: ask
  for coordinates (`ShowCoords`).
- **Grass and trees in river beds.** The land use knows the floodplain, not the channel, so 89,686 of 606,633 plants on the
  tiles along the water stood in it; now none (`plants`).

Parks and trails (see DESIGN.md, World 9):

- **Parks**: whole blocks between streets near the towns' middles (three in Kingsbridge, two in Westhaven): striped lawn to the
  back of the sidewalk, no houses, tree clumps, a pond in the big ones with a path round it, paths in from the streets, benches.
  Kingsbridge Riverside: the river's east bank through the town, with a walk along it.
- **Walking trails**: footpaths round six of the eight lakes, tracing the shore, broken at rivers and roads, joined to the network;
  benches facing the water. Two lakes have none (their low shore runs far out); paths through parks are the parks' own.
- The debug teleport goes to the parks and lake trails too.

## PR 3: race courses (agreed after 0.7.0)

Three courses, free roam only: no race mode, no timing (a custom game mode comes later, separately). Each is placed after
`Places` and before any road is routed: its ground is a layer of the height function and its cells are closed to the router, so
roads go round it and only its access road joins it. Deterministic like the rest of the layout (`Rng`, `DetMath`, doubles).

### 3a: tarmac circuit (0.8.0)

- **Site:** the 200-unit grid scored for a box of about 4000 x 2500: flat, no river or lake in it (plus a margin), low, 1.5 km
  real or more from towns and villages, out of the mountains; the best is taken. Measure first with `tools/preview`; if no site
  fits, a shorter lap (about 2 to 2.5 km real) rather than a bridge.
- **Lap:** ProcTrack's generator ported to doubles and `DetMath` (start straight, corners, a hairpin), about 3 km real (10,000
  units), 40 wide; seeds tried in order until it fits the site. Its own `RoadKind.Circuit` (and map colour).
- **Ground:** levelled to a best-fit plane (at most 2%), banks back to the land.

Status: done on branch `claude/epic-goodall-zas0pe` (0.8.0): compiled, preview and meshcheck clean (a new `circuit` check), not
yet tested in game.

- **Lap.** A ring squashed to the box is about as long as the lap, so with dents most laps overflowed the box: each lap is now
  scaled to the target length and then down until it fits (as short as 7600 units, 2.3 km, the plan's fallback). The sine waves
  over the whole lap faded out near the corners in 200 units and folded kinks into them (most laps failed the radius checks):
  long straights other than the start straight are bent instead by an offset a sin²(πt) along each, no tighter than a 600 radius.
  A corner is slow, medium or (3 in 10) a fast sweeper of 300 to 700; right-handers down to a 130 radius (their inside is the
  outside of the loop, where only the band is). Of up to 40 laps that fit, the one with the most corners both ways is kept
  (`Character`). This world: 2.88 km, 9 corners (3 right), start straight 422 m, hairpin 18 m radius, ground tilted 0.3%, worst
  cut or fill 20 m.
- **Meshes** (`CircuitMesh.cs`): track (white edge lines, a worn racing line), kerbs rising 0.12 to their outer edge, skirts down
  under the grass, pit lane and paddock in one sheet, paint (start line, grid boxes, pit lane line); gravel traps a mesh of their
  own with low grip and high drag (0.55, 3.5).
- **Buildings** (in `Buildings.cs`): armco outside the loop (none along the pits), pit wall, a row of garages with roller doors and a
  glass floor over them, the start gantry, three grandstands (rows of seats stepping up, back wall, roof on posts). All set into the
  plane as far as it falls across them.
- **Land use** `Cover.Circuit` (mown grass, no trees, short grass) over its ground; nothing grows on its surfaces or buildings; the
  map pictures draw it. The teleport goes to the grid and the paddock.
- The circuit road is held level for its first 60 units out of the gate (its bed and banks rose through the paddock's edge).
- Found on the way: a filling station lot beside a road on an embankment sagged at its front (the ground in front of a lot is held
  0.4 over the roads' ground, which fell away down the bank); lots are no longer placed where that cap would bite.
- Not done from the list below: a paddock car park behind the pits (the paddock is open tarmac), `RoadKind.Circuit` (not needed: the
  circuit is not a road).

- **Furniture:** red and white kerbs on the corners, grass and gravel run-off outside the bends (no trees or buildings), pit lane and
  pit building on the start straight, a start/finish gantry, two or three grandstands, a paddock car park behind the pits.
- **Access:** a minor road from the paddock gate to the network; nothing else crosses the lap. Teleport to the grid and paddock.

### 3b: rally loop and hill climb (0.9.0)

- **Rally loop:** gravel, about 16 wide, through upland woods, about 5 km real: ring waypoints routed in turn by A* at the rally
  class's grade (about 12%), its own cells dear so it never crosses itself; built before lanes and tracks so they keep off it,
  joined to the network by a track. Track ribbon, bed and dirt grip; start and finish arches, a service area, tape or hay bales
  on the worst corners.
- **Hill climb:** a new narrow tarmac road (not an existing one) with switchbacks from a valley floor to a col in the northern
  mountains, about 3 to 4 km real, routed on a finer local grid (about 25) so hairpins have a real radius (about 50); start at the
  bottom joined to a lane or minor road, finish line and a turning area at the top.

### Checks

`preview` draws the courses and reports site, length, tightest corner, steepest grade. New `meshcheck` checks: lap closed and never
near itself, circuit grade and cross-fall, nothing in the circuit's fence or run-off, kerbs on the surface, hairpin radii, rally and
hill-climb grades.
