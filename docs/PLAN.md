# Plan

Agreed with the owner after testing 0.4.3. Work top to bottom; each PR runs `tools/check.sh`, `tools/preview` and `tools/meshcheck`
before it goes up, and adds checks for what it fixes.

## Map API for MiniMap (0.4.6; asked for by the MiniMap mod)

Done: `MapView` (public, read by reflection; see README) gives each loaded tile's map picture (land use, water, roads by class) and
the origin. Display only, outside the plan's order; the world is unchanged.

Status: PR #12 (branch `claude/epic-goodall-zas0pe`), compiled and meshcheck clean, not yet tested in game. Open with the owner:
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

## PR 2: town fabric (the Tokyo landmarks go: they do not fit the look)

Everything made in code in the same pixel style, placed from the seed with the own-side-of-the-block rule:

- **Centre** (main town): a small cluster of **skyscrapers**, 8 to 14 storeys (about twice today's tallest), stacked setback blocks,
  glass curtain-wall texture, rooftop plant.
- **High street**: terraced shops (coloured shopfronts, sign bands) with flats over, along main roads in the core.
- **Residential**: today's houses.
- **Retail park** at the main town's edge on a main road: a **shopping centre / plaza** (big low building, glass front, sign band, flat
  roof) facing a large **car park** (painted bays, lamp posts, kerbs, flat and drivable) with an entrance road.
- **Small businesses** on main roads at town and village edges: shops, workshops, a garage or filling station, each with a small car park
  or forecourt.
- **Parks**: a few town blocks and riverbanks in town: lawn, tree clumps, a pond, benches, footpaths.
- **Walking trails**: narrow gravel paths painted into the ground like the tracks, looping round lakes and through parks; drivable.

## PR 3: race circuits

Two to four closed circuits across the map, each joined to the network by an access road and with a start/finish gantry:
a **tarmac circuit** on open flat land (about 3-4 km real lap, kerbs, run-off, pit straight and building, grandstands, paddock car
park; lap shape as ProcTrack's generator: start straight, corners, a hairpin), a **rally loop** on forest tracks in the uplands, and a
**hill climb** up a mountain road to a pass. Free roam first; lap timing on a circuit later (our own HUD, not the game's race flow).
