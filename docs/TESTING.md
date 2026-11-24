# Test checklist

Things only the game can show. Turn on `TestMode`, `ShowCoords`, `Teleport` and `VerboseLog`, then:

Last run (0.17.0, by the owner): 1, 7 to 12, 15 to 17b and 19 to 23 good. Not run that time: 2 to 6, 13, 14, 18 and 22b.

1. The race loads on the road into Kingsbridge, just outside the town, facing it; none of Tokyo's arena, city or train is left.
2. Drive along the trunk road and into Kingsbridge: tiles, roads and ground textures appear ahead before you reach them, with no gaps
   or seams between tiles. Where roads meet, the smaller one stops at the bigger one's edge with a flared mouth, level with it (no
   lip, no flicker, nothing poking through); the same where two streets cross. More junctions and dead ends: 24.
3. Cross rivers on bridges (concrete sides and parapets), small streams too: no water over the road anywhere. Follow a river and its
   tributaries, in the lowland and up in the mountains: the water's edge meets the bank everywhere, never hanging in the air with
   ground or sky showing under it. The bridge at world 0.208, -2.517 (thousands of units, as ShowCoords shows) (a main road in a cutting over a river in a gorge): no ground
   pokes through the deck at either end. Drive into a river or the sea and check you are put back on a road.
4. The gravel verges either side of the paved roads: no step at the road's edge or where they meet the grass, a little less grip
   than the road, none on bridges. In Kingsbridge and the villages, sidewalks instead (no bump from the road, a kerb at their back,
   none in front of a car park or forecourt). A farm track, and a footpath out of the end of a town street: crisp ruts and gravel
   (not blocky), no ground showing through them or at their edges, no flicker where a path ends on a track, no grass tufts or
   trees on it, looser grip than the grass beside it.
5. In Kingsbridge: houses line the streets, none stand on a road, you cannot drive through them. In the country: woods of pines (you
   hit their trunks), grass round the car, wheat in the wheat fields. The log has `Forest trees captured: ...` (if it says simple pines
   stand in, the main menu's trees were not found). In Kingsbridge's middle, the glass towers (setbacks, plant on the roofs); along
   its main roads, shopfronts in four colours; in a village, the church and its spire.
6. The retail park at Kingsbridge's south-west edge, about 200 m north of the spawn, and a filling station or workshop at a
   village's edge: drive on and off the car park from the road with no bump at its edge, the bay lines crisp (not blocky), no grass
   or trees on it, no ground poking through it, no flicker; hit a lamp post and a canopy post (solid).
7. Teleport (F7) to Kingsbridge Park and the others: a striped lawn up to the back of the sidewalk, no houses on it, a pond with
   its water's edge meeting the bank all round (no edge hanging in the air), the path round it and the paths in from the streets
   meeting it, benches beside them (solid, standing on the ground), clumps of trees, no trees or grass in the pond. Kingsbridge
   Riverside: the walk along the river's east bank. A lake trail (teleport to `Lake trail 1`): a gravel path round the shore, no
   water over it, benches facing the lake. Grass and trees nowhere in a river's bed or a lake's shallows.
8. The workshops (teleport to `... workshop`): the metal cladding in clear vertical ribs and the roof in rows, no fine pinkish
   stripes or shimmer from near or far; the same on the retail park's walls and the farm barns' roofs.
9. The circuit (teleport to `Circuit (grid)`): drive a lap. The track, kerbs and pit lane lie flat on the ground with no lip at
   their edges and no ground or grass showing through; the kerbs are red and white blocks, a little raised; the gravel traps slow
   the car right down; the barrier is solid. The gantry over the start line faces the grid, the garages' doors face the pit lane,
   and the three grandstands face the track with their seats rising away from it. `Circuit (paddock)`: drive out of the gate onto
   the circuit road, no step where it leaves the paddock. No trees on the run-off.
10. The rally stage (teleport to `Rally stage (start)`): drive a lap. Gravel all the way, with the two worn lines down it, looser than
   tarmac; no ground showing through it and no step at its edges; forest close on both sides but no trunk on the road; no seam where
   the lap comes back onto the straight. The straw bales on the outside of the tight corners are solid and stand on the ground. The
   arches over the start and finish lines stand clear of the road. `Rally stage (service area)`: the gravel yard, vans and awnings,
   no bump onto the stage; drive out along the track at its back to the lane, where the track ends at the lane's edge (it does not
   run on across or under it).
11. The hill climb (teleport to `Hill climb (start)`): drive up. Tarmac like a lane, no ground showing through it and no step at its
   edges or where cut into the hillside; the bends and the hairpin near the top have a real radius, no kink. The start arch stands
   clear of the road; the paddock beside the start straight lies flat with no bump onto the road. Turn round at the bottom and drive
   down the lane: no seam or step where the lane runs into the climb's foot. `Hill climb (finish)`: the finish arch, then the
   turning area beside the end of the road, flat and big enough to turn round on.
12. Road signs: drive into Kingsbridge from the spawn and out to a village. A white name sign stands on the left at the edge of each
   place, facing you, its name crisp and readable from a few car lengths, not mirrored, its back plain grey; none on the trunk road.
   At a junction of main roads (teleport to a village and follow its main road out), the fingerpost stands back from the road, each
   finger pointing along its road, readable from both sides (name at the left, miles at the right), none reaching over the road;
   brown fingers on the way to the circuit, the rally stage and the hill climb. Follow the fingers to a place: the miles count down.
   Out in the country, a fingerpost at most junctions of lanes and minor roads, outside the towns and villages.
   Posts are solid, no house or tree stands in a sign. The log has a `Road signs:` line.
13. Country lanes, tracks and footpaths: follow a few out from a farm or hamlet. Most run on to another road rather than stopping
   in a field; where one was carried on, no kink or step where the old road ends and the new one begins, and its far end meets the
   road it joins with a mouth like any other junction.
14. The trunk road along the coast (east of the spawn, and west towards the edge): no sea water drawn over it anywhere; it runs on a
   low bank above the shore.
15. Past 1000 units (about 300 m, `ShiftDistance`) the log says `Origin shift 1` and the overlay's scene position jumps back near 0 while the world position keeps
   counting. Watch for a visible hitch, a jump of the car or camera, or tyre marks and smoke left behind.
16. Teleport (F7) round the key places, several of them 10,000 to 15,000 units (3 to 4.5 km) out. The car should drive exactly as at the spawn: no jitter in the suspension or camera,
   and the hand, cursor and cockpit controls as steady as at the spawn (the log's shift lines say how many cockpit objects were kept).
17. Reset (0.23.9). Hold R: (a) in a field within about 75 m of a road: on the road, in the right-hand lane, facing the way the car
   faced (on the trunk road on a carriageway, not the median); (b) in open country further from a road: where it is, upright; (c)
   in the woods or against a building far from a road: on the nearest clear patch close by (tens of metres at most, never off to a
   far road), not inside a tree or a wall; (d) on a
   road, a farm track, the circuit and the rally stage: where it is; (e) in an event's race off the course: never onto another road;
   (f) drive into a lake: back on the bank or a road. With `VerboseLog` on, each logs `Reset: ...` with what it chose.
17b. Get wrecked (into a wall at speed, or the dev console's `kill player` or `hp 0`): `WRECKED`, the car burns, and after 3 seconds (`RespawnSeconds`)
   it is at the start, on the spawn's grid slot facing up the road, at full health, driving normally (it accelerates, no flames);
   `BACK AT THE START` shows. Get wrecked far out (teleport first): the same, and the ground round the start is there. The race does
   not end. Pause while wrecked: the respawn waits. Hold R: still resets in place. In a Multiplayer room set to Demolition or "out,
   watch", with AI RIVALS above 0: no AI cars on the grid; a wrecked player respawns after the room's time at the start, the others
   see the car come back there, and nobody is put out. Two players wrecked together, or one waiting on the grid: the second lands on
   the next slot back, not on top.
18. Drive into a mountain valley more than 100 m below the spawn: no automatic "fell under the map" reset.
19. Multiplayer, two players: drive apart several kilometres, then back together. Each should see the other in the right place the
   whole time, including after teleports and resets.
20. Frame time while driving fast across new ground (the log's `Tiles: ... ms each` line with `VerboseLog`).
21. Day, night and storms (0.17.0), in a Multiplayer room. The log has `Weather: <day> by day, <night> by night (from the room's
   seed)` and `Sky: the race starts at hh:mm, a day lasts 40 minutes; storms: a ... day, wind ..., snow above ...`. With `ShowCoords`
   the overlay's last line gives the time, the sun's height, the rain, cloud, wetness and grip at the car and the nearest storm; both
   players should see the same time and, at the same place, the same storm. Turn on `WeatherSkip` and press the Weather skip key (F5) through a whole day (or toggle the Weather fast key, F2, for 10x)
   (8 presses a day): the shadows swing from east to west, the light goes gold at dusk, the sky darkens and the night sky comes in,
   the cockpit dims at night (the cabin is not lit at midnight), the headlights go on at dusk and off after dawn, and nothing jumps
   or flickers at the swaps (the light fades out and back round sunset and sunrise). Note whether the ambient light changes at
   all (the world's shadows go blue and dark at night): if it stays as bright as day, the probe override did not take. Drive
   towards the nearest storm: the sky and fog darken first, then rain starts and grows (the sound with it), the road darkens and
   shines, and the overlay's grip drops; drive out the other side: the rain stops, the road stays wet and dries over about 10
   minutes. In the rain, the drops reach the ground and splash on it,
   on the road and on buildings, on high ground and down in a valley alike, with no drops stopping or splashing in mid-air (the
   log says `Sky: rain and snow land on 1 collision plane(s)`). Teleport about (origin shifts) while it rains, and drive up and down
   a long hill in the rain: the rain stays, and the drops splash on the road round the car, uphill and downhill. Up in the northern mountains above the snow line it snows instead. Frame time with heavy rain (the graphics budget
   still cuts the rain on a low setting).
22. Garages, alone and in a Multiplayer room. The log says `Garages: 20 (...)`. The map (MiniMap) shows a big cyan dot (wider than the trunk road) at each
   filling station, workshop, the circuit's pits, the rally service area and the hill climb paddock (one dot for the three pits, and one where a filling station and a workshop face each other), and `GARAGE` floats over each
   circle within about 70 m. Drive into a circle: the line at the bottom says to stop and shift into neutral; do it and the garage
   opens when you let go of the shifter (standing in neutral still holding it does not, nor does shifting through neutral while rolling). While it is open the controls and the gun do nothing, the cursor is free,
   crashes, hazards and other players' shots do no damage, and the health shown stays put. Drag an item from the list onto a slot (the slots it fits light up), or right-click it: it is on the
   car at once (the new gun, wheel or shifter in the cockpit, the body's look outside), ammo and nitro full a moment later. Every
   gun (the game's and the modded M1911, Minigun, RPG, Sniper Rifle, Railgun) swaps and fires as at the start of a race, with its own
   held model; swapping away from the Minigun takes its roof gun off the car (seen from outside and by others) and leaves no remote,
   and fitting it again gives one roof gun, not two. In a room, an RPG fitted here fires rockets the others see fly and explode, and
   the Multiplayer mod's banned items (slow motion, rerolls) are not in the lists. Dragging a fitted item off its slot, or right-clicking it (in the slot or the list), takes it off; dragging one
   medium item onto the other medium slot swaps them; an item dropped on a slot it does not fit says so. Repair fills the health;
   Refill the ammo and nitro.
   Esc or Close closes it; it does not open again until you shift into gear or leave the circle. The next race starts with the
   loadout from the menu or the room, not the garage's (the log says `Garage: the race's own loadout is back`).
22b. Respawn at a garage (0.16.0), alone and in a Multiplayer room. The garage window's bottom right says `Respawn after a wreck: the
   start`. Press Respawn here: `A wreck comes back here`, the log says `Respawn point: <garage>`, the button reads Respawn at start
   and the bottom right `this garage`; another garage's window says `another garage`. Close, drive off, get wrecked: after the wait
   the car is in that garage's circle, facing out of the lot (along the pit lane to its exit in the pits), repaired, on its wheels,
   `BACK AT THE GARAGE`; the garage does not open until you shift into gear and back to neutral. In a room, with another player's
   car parked in that circle: you come back at the start instead. Press Respawn at start and get wrecked: back at the start. Wrecked
   in an event: still back at the last checkpoint. Start the next race (or a new run): a wreck goes back to the start again.
23. Events, solo (0.15.3; since 0.23.0 the box opens a panel first, see 30). The log says `Events: Circuit 8.6 km, 27 gates, 3 laps; Rally stage 4.9 km, 16 gates; Hill climb 1.4 km,
   5 gates`. Out of an event the cockpit's board (top right) is tucked into the screen's right edge with a sliver showing; the hand
   over the sliver slides it out with the cars in the race (`cars 1` solo, `players N` in a room, on the lap counter), and it goes
   back when the hand leaves. An orange ring is in the circuit's pit lane past the last garage, in the rally service area and in the
   hill climb paddock (beside the garage's circle), off the course, with orange `EVENT` over it from up to about 70 m, and a
   chequered disc in an orange ring on the map (MiniMap) at each, a little larger than a garage's and over the one beside it. Drive
   into one: a panel like the garage's (`EVENT  Circuit`, the length, laps, checkpoints and best, `Stop and shift into neutral to
   start`) shows under the middle of the screen; the board stays tucked away. Stop in it in neutral and let go of the shifter (holding it in neutral does nothing): the panel goes, the car is put
   on the grid (pole on the circuit, just behind the start arch elsewhere), facing up the course, and the board slides out; the
   start lights hang over the start line facing the car, and go red, amber, both, then green with 3, 2, 1, GO in the cockpit's
   text, each on time. Before green, shift into gear and floor it: the car stays on its spot (the wheels may spin, the car should
   not slide or turn, and the log has no `Origin shift ... NaN`) and leaves on the green. An `Origin shift` logged just after
   `Event: signed on` (the grid is often far enough from the box for one) must leave the car on its spot. Drive the course: the first
   orange gate (posts and a bar, no collision) stands across the road ahead with the next one beyond it, each gate passed moves the
   pair on, the board counts the checkpoints (the lap counter the lap on the circuit, with each lap's time on the board), the finish
   gate is white. Cut a corner past a gate: `MISSED A CHECKPOINT`, the run finishes with no best time. A clean run: `NEW BEST`, and
   `halfgrip.openworld.cfg` has the time under `[Best times]`; a slower one shows the gap in red. Get wrecked mid-event: after the
   respawn the car is just past the last gate passed, facing on along the course, `BACK AT THE LAST CHECKPOINT`, and the clock ran
   on. Drive far off the course: `BACK TO THE COURSE`, then `EVENT OVER` after 10 s; teleport mid-event: `EVENT OVER`. Pause: the
   clock stops. In a Multiplayer room the same works for yourself.
24. Junctions and dead ends (0.17.1). With `ShowCoords`, drive to these (world, in thousands of units, as ShowCoords shows) and look round each, from the car and from
   outside: no ground, grass or verge showing in a mouth, no wedge of ground where a road meets another at a slant (on either side),
   no sidewalk or verge lying across a mouth, no step or flicker where the surfaces meet, nothing of one road poking through
   another.
   - A street meeting a street at a slant in Westhaven (11.482, -10.303), and Kingsbridge's streets round (-1.802, -4.088).
   - Two farm lanes carrying on end to end (-9.333, -9.646): one road round the bend, no notch or seam, the verges unbroken.
   - Main roads ending in a village, their streets carrying on: Northcote (-1.486, 5.487), Kestrel (12.082, -4.155), and
     (7.710, -4.893). The main road narrows smoothly into the street, no square end standing out across it.
   - Redford's middle (-6.838, -5.281), where two main roads and two streets end at one place.
   - Villages where three or more roads end at one point (0.17.2), by the teleport: Ashby, Carrow, Elmsted, Kestrel, Millbrook
     (and Westhaven's centre). One plain asphalt plate joins them: each road stops square at its edge, the corners between them
     curved with the sidewalk or verge carried round, no ground in the middle, no road's square end across another, no step
     where a road meets the plate. At Kestrel the main road's old hook west of the village is gone (grass there).
   - A farm track meeting a lane that meets the trunk road (-12.822, 2.269): the track is under the lane's surface, not over it.
   - Dead ends: a Kingsbridge street (-2.135, -3.547) and a lane (-7.894, 4.300) end rounded off, the sidewalk or verge carried
     round the end down to the grass (no square edge standing over the ground); a farm track (-0.282, 2.578) narrows to a point
     into the grass. Drive off each end: no bump.
25. The painted sky (0.18.0). With `VerboseLog` the log says `Sky: painted sky on a copy of FS002_Rainy (Skybox/Panoramic)` (or
   another panoramic sky), and there is no `could not make the painted sky` or `painted sky stopped` warning. With `WeatherSkip`, the
   Weather fast key (F2) or skip key (F5), go through a day: by day a blue sky, paler at the horizon, with a white sun disc and a
   glow round it, standing where the shadows say the light comes from (shadows point away from it) and moving with them; the sun
   goes orange and the sky round it gold and red as it sets in the west, the disc going down behind the horizon; the sky goes deep
   blue, then black, the stars come out (small pixel squares, not stretched, also straight overhead) and turn slowly, and the moon
   (a grey disc with darker patches) rises in the east opposite where the sun set and crosses the sky. Nothing jumps: no cut between
   a day sky and a night sky. The distant ground in fog meets the sky at the horizon without a band of a different colour, at every
   hour. A few clouds by day (more on a wet day, see the log's `showery`/`wet`), which slide across slowly and stay over the same
   land as you drive (drive a kilometre: they pass overhead); under a storm the whole sky is grey cloud and the sun and stars are
   hidden. Look straight up and all round: no seam or smear in the sky (the picture's edges meet in the west), no stretched stars at
   the top. The frame rate is as before (the painting costs about 0.7 ms a frame).
26. The far view and storms seen from afar (0.19.0). The log says `Far view on (the world's grid built in ... ms)` within a few
   seconds of the race starting, and no `Far view failed` or `chunk ... could not be made` warning. On a clear day (`ShowCoords`: cloud
   near 0), from the spawn and from the mountain pass and the hill climb's finish (teleport): the country goes on past the loaded
   ground to the horizon, fading into the sky's colour with no band between: the mountains in the north with snow and rock, the woods
   standing up dark green, fields, lakes and the sea in the south going on past the coast to the horizon, the main roads as thin grey
   lines. Where the loaded ground ends (about 1,400 units, some 420 m, out with TileRadius 5) there is no gap, no sky showing through, no step, no
   flicker; buildings and trees appear there (that is expected). Drive a few kilometres fast and teleport: the far ground follows
   with no hole opening for a frame and nothing poking through the road or the ground near the car. Far mountains and coast do not
   flicker or shimmer (z-fighting at a distance would show as stripes; report a screenshot if so). Under cloud and in the rain the
   view closes in smoothly (a few hundred metres in a downpour) and opens again as it clears. With `FarView` off in the cfg, the fog
   ends at the loaded ground as before. Storms (a showery or wet day in the log; `WeatherSkip` and F2 to find one): a storm some
   way off shows as dark lumpy cloud over its place, grey at its edge, with grey streaked rain hanging under it down to the horizon;
   it grows as it comes, and it is overhead when the rain starts; one behind you is behind you (turn round). Away from storms the
   sky is as in 0.18.0. The frame rate is about as before (with `VerboseLog`, `Tiles: ... ms each` unchanged).
   In the free-flight view too (0.19.1): the country goes on past the loaded ground to the horizon. With `ShowCoords`, the last line
   says `far view: N chunks, fog A to B, <camera> draws to C` with C a little over B (thousands on a clear day), in every view.
27. The land past the far view painted into the sky (0.20.0). The log says `Sky: the land past the far view painted into the sky (N
   texels, M ms a build)` a few seconds into the race, and no `the land past the far view stopped`. On a clear day (`ShowCoords`:
   cloud near 0; its last line now `fog A to B, <camera> draws to C` with C about `FarViewDistance` and B up to 40000), from the
   highest road near the coast looking north and from the spawn: the mountains in the north stand on the horizon past where the far
   view ends, hazy and lit from the same side as the near hills, their snow lighter. Where the far view ends (14,000 units, about 4 km, out) there
   is no line, no band of sky, no step in brightness or colour; take a screenshot of it from high ground. Drive along: the far
   mountains keep their places (they do not slide with the car) and do not flicker. The near country is a little clearer than in 0.19.
   At sunset and at night the painted land darkens with the meshes. A storm between you and the mountains hides them behind its rain.
   As the rain closes in, the far mountains go first, smoothly. Look south from high ground over the sea: the sea goes on past the far
   view's edge with no seam (its shine there is a guess; report a screenshot if the far sea is brighter or darker than the near).
   With `FarSkyline=false`: the fog ends at the far view's edge as in 0.19.1. The frame rate is about as before.
28. The far view's costs (0.20.1). The sky looks and moves as in 0.20.0: the sun, clouds and storms change smoothly (a new picture
   about five times a second), nothing torn or half painted, no `the painted sky stopped` in the log. Start a race with the far view
   on: no hitch when `Far view on` shows in the log (a fraction of a second after the grid is built), and when it comes on the far
   view is there all at once, with no chunk missing and no hole near the car.
   The frame rate a little better than 0.20.0, most of all on a slow CPU.
29. The highway event (0.21.0). The log says `Events: ...; Highway 10.1 km, 33 gates`. Teleport to `Highway (start)`: the car is on the
   eastbound (south) carriageway at the west edge, facing east, the tunnel mouth behind. Turn round: an orange ring on the westbound
   carriageway between the start line and the tunnel, `EVENT` over it, and a chequered disc on the map (MiniMap) at the west end of
   the trunk road. Drive into it from the east (as a car driving west to the world's end would): the panel says `EVENT  Highway`,
   10.1 km, 33 checkpoints. Stop in neutral and let go of the shifter: the car is put on the grid on the eastbound carriageway facing
   east, the start lights over the road at the start line, red, amber, both, green. Drive east: the orange gates span both
   carriageways, posts outside the shoulders; none stands on a bridge with its respawn there. Get wrecked mid-race: back just past
   the last gate, on the eastbound carriageway, facing east. The finish gate (white) is just short of the east tunnel mouth, before
   it; the best time goes under `[Best times]` as `Highway_33427_1`.
30. Events in lobbies, alone (0.23.0). The log says `Events: Circuit 8.6 km, 27 gates, 3 laps, 12 on the grid; ...`. Teleport to
   `Circuit (paddock)` and drive into the orange box in the pit lane: the line under the screen says `EVENT  Circuit` and to stop and
   shift into neutral. Stop, neutral, let go of the shifter: the **panel** opens (the log says `Event: the Circuit's panel is open
   (alone)`), the cursor free, `START A SOLO RUN` with Guns and Melee buttons and **Start**. Esc closes it (no pause menu); drive out
   and back in to open it again. Press Start: the car goes to the grid's pole slot (the log says `Event: created a lobby at the
   Circuit` then `Event: on the grid at the Circuit, slot 1`), the lights count down about 5 s later, and the race runs as before.
   The strip under the board disappears as the car goes onto the grid and stays away through the race and the results (0.23.1):
   only the board. Race to the finish: the board shows the time and best, then about 15 s later the strip is back saying
   `WAITING: start when ready`. Point the hand at **Start**: the hand shows over the strip (0.23.1), not under it; grab to race
   again. Grab **Leave** while waiting: out of the lobby, the board tucked away.
31. Events in a room, two players (0.23.0). Both in the Open World in one Multiplayer room. Player A opens the circuit's panel:
   `No lobby here yet`; **Create lobby** with Guns off. A's car moves to a waiting spot beside the box (further along the pit
   lane); B sees `A OPENED A LOBBY AT THE CIRCUIT`, and `EVENT  1 LOBBY` over the box. B opens the panel: A's lobby, 1/12, Guns
   off Melee on, Waiting, **Join**. B joins: B's car moves to another waiting spot (not onto A's), A hears `B JOINED`, both boards
   list `A, B`, both strips `2/12`; A's strip has **Start**, B's only Leave. A grabs Start: both cars go onto the grid (A pole, B
   second, beside and behind), both lights go green at about the same moment. Race: each board shows `P1/2` or `P2/2` and changes
   when one passes the other at a gate. A finishes: B's board shows `Cut-off 0:45` counting down. If B finishes, the results show at
   once on both boards (`Results`, names and times), the log lists them; if not, at the cut-off B is out (`OUT: THE CUT-OFF`). 15
   s later both strips say waiting again. While A's lobby races, B leaves and creates a second lobby at the circuit: its Start reads
   `Race on` until A's results are done. Driving away from the course mid-race (more than 150 m for 10 s): that player is a DNF in the
   results. Send `LogOutput.log` from both.
32. Leaving a race early (0.23.2). Alone: start a solo run and drive more than 150 m off the course for 10 s (or teleport, F7):
   `EVENT OVER`, the log says `Event: left the lobby at the Circuit (out of the race)`, and the strip never comes back (the lobby is
   closed; `EVENT` over the box shows no lobby). Two players: A creates, B joins, A starts, then A drives off the course: B's strip
   (after B's race) shows `Host B`, A's never comes back, and B's results list A as DNF. Then B starts again and drives off: the
   lobby closes at once and `EVENT` over the box no longer says `RACE ON`.
33. The trees' cost (0.21.1). Teleport to `Rally stage (start)` and drive through the woods: the frame rate much better than 0.21.0,
   no hitch as tiles load or the origin shifts (`Origin shift` lines under 6 ms or so in the verbose log). Woods look as before out
   to the loaded tiles' edge, nothing popping in or out as you turn the camera (1/3, the mirror) or as you drive; trees near the car
   cast shadows, and their shadows still fall into view from trees just off screen. Hit a tree trunk: solid, at a standstill and at
   speed, on the tile you are on and the next. `TreeShadowReach=0`: no tree shadows, faster still.
34. Events with three or more players (0.23.3). Three or more in one room. Each opens the circuit's panel and joins one lobby: each
   car goes to its own waiting spot in the pit lane (two abreast, further along with each player), none on another. The Host
   starts: all go onto the grid in join order (pole, second beside and behind, third behind pole...), the lights go green together,
   each board shows `Pn/N` with N the number racing and the positions change as cars pass at gates. Let one finish, one run out the
   cut-off and one drive off the course: the results list a time, `OUT: THE CUT-OFF` and DNF, the same on every machine. Do the same
   at the hill climb's paddock (8 spots, 4 rows of two clear of the vans). Send `LogOutput.log` from every machine.
35. Guns after a garage refit (0.23.7, log 0.23.8). Teleport a few times first (F7, so the origin shifts: `Origin shift` lines in the verbose
   log), then at any garage with the shotgun fitted take it off and put it back, and fit a chassis: close the garage after each and
   the gun is still in the cockpit, grabs, aims and fires. Then drive or teleport far again, come back to a garage and swap guns
   once more: still there. With `VerboseLog` on, each refit logs `Garage: ShotgunAssembly on after a refit, grip ready, n of n
   sprites drawn, parts within` a few units; a warning `its art is away from the gun` must never appear.
36. No ash (0.24.0). Start a few races (or rooms) until the verbose log says `Weather: StadiumAsh by day` or `RedForrest by day`:
   no ash falls, anywhere, all race; the light and fog still look like that weather. Rain and snow come only with a storm (the overlay's
   `rain` above 0), never on a dry stretch.
37. Back roads (0.26.0). The log's `World layout:` line names the back road links (about 90). Drive the lanes out in the country
   (teleport to a farm, or drive north from Kingsbridge): they run on from farm to farm and village to village, through woods and
   over streams, hardly ever ending in nothing; no lane hooks round in a U back to the road beside it. A bridge on a lane over a
   stream is a covered bridge: red board walls with an open band under the eaves, a shingle roof, a gable over each portal; drive
   through it (no bump onto or off the deck, the roof well clear of the car, you hit the walls). Along the lanes and minor roads,
   now and then a house set back on a gravel drive with a mailbox by the road (solid, off the verge, standing on the ground); a farm
   at the end of a short lane has the lane as its drive. Drive up a drive: no ground through the gravel, no step where it meets the
   lane, tarmac grip on the lane and track grip on the drive. On MiniMap's map the drives are pale gravel, the lanes white.
38. Towns laid out ring by ring (0.26.0). Teleport to `Kingsbridge market place` and `Westhaven market place`: a paved square beside
   the high street, level with it (no bump driving on, no ground or grass through the paving, no flicker), the stone cross on its
   steps and the stalls solid, the shops facing it along its back with their fronts to it; no street or house on it. Drive round
   both towns: the old town's back lanes behind the high street and the short lanes to them, patches of straight terraced streets
   (crossroads in them meet level, no lip or flicker), the ring road bending round, crescents and closes past it, Westhaven's
   seafront along the top of the beach with houses on its landward side only; streets meet the roads they end on square, with
   flared mouths as before, and footpaths and alleys between streets have no step where two meet end to end.
39. Two players (0.26.4). A waits at the spawn; B teleports (F7) round to `Spawn`: B lands beside or along the road from A, never on
   top of A (the log says `another car there so 30 along...`), and a knock in the first 2 s does no damage. Both spawn and respawn
   with full health: the log's `Car spawned: health 100 of 100` (any warning `the stats are not in yet` or `Health was the game's 1%
   fallback ... filled` is worth sending). B wrecks and respawns: on A's leaderboard B is back at once, with the laps B had (the
   Multiplayer mod does this from 1.1.0; from 0.27.1 Open World no longer does it itself, and warns in the log if the Multiplayer mod
   is older). Send `LogOutput.log` from both machines.
40. Events in a room with two or more players (0.27.0; everyone needs 0.27.0). Lobby notices: A creates a lobby at a course; on B's
   screen a panel at the top left lists it (course, A, 1/12) at once, and that course's start box turns white. B joins it: the line
   reads 2/12 on both screens. The panel's `<` button, pressed with the hand, slides it into the left edge leaving a tab with the
   count; A creates or leaves while it is minimised and only the count changes; the tab brings it back. B quits to the menu and
   loads the Open World again while A's lobby waits: B's panel lists it within a few seconds of the world being built (the log's
   `Events: every lobby is here`). The grid: B teleports (F7) a few times and opens a garage just before A presses Start; both cars
   are put on the grid, the board reads `Grid 0/2`, then `Grid 1/2`, for a second or two with no lights, then the lights count down
   and go green on both screens at the same moment (record both if you can), and both clocks read about the same at each
   checkpoint. Each log has `Event: settled on the grid ... s after the teleport (tiles in, 10 frames under ... ms)` and `Event:
   green dealt at the ..., 2 of 2 entrant(s) synced on the grid; this machine's clock ... from the host's`, and with `VerboseLog`
   on, B's has `the arbiter has settled on the grid` and `synced with the arbiter: its clock ... give or take ... ms`. Send `LogOutput.log`
   from both machines with `VerboseLog` on.
41. Slow motion in an event (0.27.1; Multiplayer 1.1.0 or newer, with the Slow-Mo item fitted on C). A and B race a course; C, roaming
   elsewhere, pulls the slow-motion lever partway through: every car slows to half speed for a few seconds. A's and B's clocks on the
   board slow with their cars (they do not run on at full speed), so a run with C's slow motion in it gives about the same time as
   one without, driven the same way. The countdown's green still comes at the same moment on both screens.
42. Deer (0.28.0; calm deer only, no reaction to cars yet). With `ShowCoords` on, the overlay's `deer:` line gives the nearest
   group's crossing place, how far and which way (N is up on the map); drive there on the lanes. The log has `Deer: ... groups, ...
   deer drawn from the race's seed (... places they could live), ... ms` a second or two after the race loads. At a group: deer
   standing on the ground (hooves on it, not floating or sunk, on slopes too), the right way up and facing the way they walk,
   in the painted coat (fur, the face's eyes and white muzzle band, white throat, belly and tail underside, dark hooves; a little
   greyer on a cold race), not flat colours (the log warns if `textures\deer.png` could not be read). They graze head down, lift their heads to
   look about, take a step now and then, and every minute or two the group walks to another spot in single file; a group whose spots
   are either side of the lane crosses it one deer at a time, a second or more apart (now and then at a trot, now and then one
   turns back halfway and crosses last). Never in a river or lake, never down a steep bank, never on a road except crossing at their
   one place. Earlier and later in the day (teleport and wait, or a race that starts near dusk) more groups are out on the verges;
   at midday more are back in the woods. In a room, both players see the same deer in the same places doing the same thing at the
   same moment (compare screenshots at one spot). Turning `Deer` off in the config hides them. Send a screenshot or two close up and
   `LogOutput.log`, with the frame rate near a group if it drops.
43. Hitting and shooting deer (0.29.0; from 0.30.0 they also run, item 44). Find a group as in 42.
   - Drive into a deer slowly (under 10 on the speedometer): it dies and is pushed over but the car takes no damage; the log has
     `Deer: hit at ... on the speedometer (the car at ...): 0.0 damage`. The first hit logs `Deer: the car's box for hitting deer is ...`
     (or that a fallback box was used): send that line.
   - At about 55: the health bar drops by about 13 (of 100), the screen kicks, the game's wall crash and car crunch together (louder the
     harder), the car
     loses some speed and its nose jolts, and the deer is thrown over the bonnet and down the road, low, tumbling, lands (a small bounce
     only if it came down hard), slides and lies on its side on the ground (not sunk, not floating, on slopes too). Faster (75 or more)
     about 22 at most; a buck a little more. With god mode at a garage: no damage. A deer only ever
     hit once; driving over the body does nothing.
   - The deer is hit when the car touches it, not before or after: a deer just off the side mirror is missed, one grazing in the path
     is hit, head-on and from the side, and at high speed it is not passed through. Say if hits feel early, late or missed.
   - Shoot a deer (any gun, a shotgun too): the tracer stops at the deer, there is the gun's hit puff in red (0.30.3; was yellow sparks), and the deer falls over sideways
     away from the shot and lies there. Log: `Deer: shot at ... m`. Deer can be shot out to about 150 m (only drawn ones). Shots past or
     over a deer do not kill it.
   - The rest of a hit or shot deer's group bolts (0.30.0).
   - In a room: A hits a deer and B, near it, sees the same deer thrown and lying in the same place; the same for a shot deer. B
     cannot then hit that deer again. A player who joins after sees the bodies where they lie.
   - `DeerAmount` alone: 0 gives no deer (log `Deer: 0 groups`), 2 more groups (about 215) than 1 (160), from the next race; in a room
     the log says the standard amount is used. Say what amount feels right to drive through.
   Send `LogOutput.log` and screenshots of a thrown deer lying and of a hit's damage.
44. Deer reacting (0.30.0). Find a group as in 42 (`ShowCoords`: the overlay's `deer:` line also counts the groups near reacting).
   - Drive toward a group along its lane at a steady speed: a few seconds out (further the faster) they stop grazing and watch the car,
     heads up; nearer (about 40% of that) they bolt, the nearest deer first and the others a moment after each other, at a gallop with
     their white tails up: into the woods or the far field on their side; or, about half the time (more if one was standing in the
     road; 0.30.3), over the road at their crossing, timed so the first deer is in the road about 1.4 s before the car would get
     there and the rest follow as it arrives: a hazard (brake hard or swerve); log `... flees Across ... (the car ... s from the
     crossing, the first deer on the road in ... s)`; now and then (more at night) they freeze, staring, for
     a few seconds before running. The log: `Deer: group ... flees Away|Across|Freeze from the car at ... m (noticed at ... m)`.
   - Fled deer stand watching where they got to with their tails up, then settle and graze; a minute or two later they go about their
     day from there (no jump back to where they were).
   - Running deer never go into a river or lake, down a steep bank, or along another road; they cross only at their crossing place.
   - A car stopped near a group: they watch it but only run if it is very close (about 4 m).
   - A shot within about 75 m of a group sends it off (away, never across); so does one of them being hit or shot.
   - In a room: A drives at a group and B, watching from nearby, sees the same deer run the same way at the same time (compare
     screenshots or video); if A and B come at one group together, both see the same run. A player who joins later sees groups
     where their runs left them.
   Send `LogOutput.log` and a clip or screenshots of a group bolting, and say whether they feel too jumpy or too calm.
45. Bucks and fawns (0.31.0; in a room everyone needs 0.31.0). The log's `Deer: ... groups, ... deer (... bucks, ... fawns) drawn
   from the race's seed` counts them (about 95 bucks and 3 fawns a race at the standard amount).
   - Bucks (in the groups of two to four bucks, and some lone deer) carry antlers: one set, eight points, pale at the tips and dark
     at the base, the painted texture (`textures\antlers.png`: grain along the tines, a rough burr; without the file, a plain
     gradient, and with `VerboseLog` on the log says `The antlers' texture ... is not there`), the beams sweeping out over the ears and forward. They stay on the head in every pose: grazing (the antlers low in
     front of the forelegs, not through them or the ground), alert, walking, galloping, lying dead. They are not cut off at the edge
     of the screen when the deer's body is off it (the drawn box is bigger now), and they do not flicker.
   - Fawns are rare: now and then a family has a small deer at its tail, about 0.6 of the doe's size (no spots: the doe's coat).
     Hitting one does far less damage than a doe (about a quarter); a buck a little more than a doe.
   - Nothing else about the deer moved: the same groups in the same places as 0.30.3 for the same race seed.
   Send screenshots of a buck close up (standing and grazing) and of a fawn if you meet one, and say whether the antlers look right.
46. A road pass (0.31.1; in a room everyone needs 0.31.1: the roads changed a little).
   - No house, barn, church or tower stands on a road, a sidewalk or a track anywhere (the barn by the road at about (2465, -2964)
     with `ShowCoords` is clear of it now); terraces on bends stand behind the sidewalk.
   - Northcote's middle: the main roads meet on one plate of tarmac, no square road end across the other and no short street lying
     along the road to Oakhurst.
   - Roads that used to end side by side and overlap now run on into each other as one road round the bend: at Oakhurst where the
     Brookfield and Northcote roads meet, Farm 92's lane at about (9896, 13158), Millbrook's street and Farm 17's lane at about
     (-6847, 3797).
   - Say where a junction still looks badly joined (`ShowCoords` and a screenshot), and send `LogOutput.log`.
47. The highway wraps round, and Westhaven joined up (0.32.0; in a room everyone needs the same
   build: the roads changed a little).
   - Drive west along the trunk road to the end of the world: the tunnel mouth is open now, a dark tunnel with a floor, walls and a
     roof. Drive in at speed: a moment after the dark closes round you, you come out of the east edge's tunnel at the same speed, in
     the same lane, still heading west, the ground already there (no fall, no bump, no long freeze). The log says `The highway
     wrapped round: from the west edge's tunnel to the east's, ...`. Do it the other way too, and at full speed, and in the chase
     camera (no sweep across the map, no streak of tyre marks). The road north's tunnel is still shut.
   - Stop in a tunnel short of the dark and reverse out: nothing happens. Go in slowly: the same jump at walking pace.
   - In a room, with another player watching from near the far end's tunnel: they see you come out of it (not slide across the map).
   - Westhaven (teleport to it): its main road now runs north out of the town to a T-junction on the trunk road (before, it stopped in
     the fields). Millbrook's main road north now reaches the road from Northcote, about (-2885, 5832) with `ShowCoords`.
   - Send `LogOutput.log` and say how the jump feels.
48. Traffic drawn in the game (0.32.0; a preview: the cars cannot be pushed or wrecked yet; set
   `TrafficPhysics` to 0 for this test now that stage 3 is in).
   The log says `Traffic: ... cars, ... trips on a weekday (or weekend day) from the race's seed, built in ... ms` a second or two
   after the start (the day is made on a worker; no hitch). With `ShowCoords` on, the overlay's `traffic:` line counts the cars drawn
   (moving and parked), the lit ones and the day's hour; with `WeatherSkip` on, the skip key moves the traffic's hour with the sky.
   - Cars drive the roads in the right-hand lane at sensible speeds, slow for bends and junctions, queue behind each other and take
     turns at junctions. No car floats over or sinks into the road, on bridges, hills or in town; their wheels turn.
   - Busiest at the evening rush (about 5 to 6 pm: try the skip key), a few cars at 3 am, the trunk road busier than the lanes.
   - Parked cars stand on the drive pads beside houses (nose in), at the kerb in front of terraces and by the offices and high
     streets (facing the way traffic goes on their side), in the painted bays at the shops and the retail park, on the workshops'
     yards, at a filling station's pumps, along the front of the market places, by cottages and in farmyards. None stands on a road
     it does not belong to, in a building, in a garden wall, in the water, or on top of another car.
   - Parked cars' lamps are dark; a car's lamps come on as it sets off and go off when it has parked.
   - Town streets are not lined with parked cars: some at the kerb on the streets, none on the main roads or narrow lanes. (Many
     households' cars are "round the back", not drawn: they appear on the road when they set off. That is known.)
   - Watch a car leave and one arrive, at the kerb, on a drive pad and in a car park. It stops in its lane right beside its space for
     a few seconds while it pulls in (the cars behind wait for it), and pulls out the same way before it drives off: from the kerb
     straight sideways, from a drive pad or a bay on a curve. A space across the road means waiting for the oncoming lane to clear,
     then turning round through it. Never through a parked car, a car driving by or one coming the other way.
   - Turning at a junction, a car follows a curve from its lane into the junction to its lane out of it: no sideways jump or sudden
     swing round in the junction's middle.
   - No `Tile ... could not be worked out` lines in the log.
   - Hitting a traffic car is like hitting a wall (damage, no push); the respawn never puts you on one.
   - At night the nearest few moving cars light the road ahead; other players' cars in a room have their headlights on too now (they
     stayed dark before).
   - The car models: the game's enemy car in white, black, grey, silver, blue, red and a few rarer colours, no guns, no nitro flames.
     Say if they look wrong (the wrong colour channels, floating wheels, a wheel turning the wrong way).
   - Frame time: compare with `Traffic` off in a town at the evening rush.
   - In a room: both players see the same cars in the same places.
   - Send `LogOutput.log` (with `VerboseLog` on it also says how each car body was made) and screenshots: a town street, a car park,
     a junction with a queue, a car pulling out, night.
49. Physics traffic near you (0.32.0; stage 3 of the traffic). New config `World` /
   `TrafficPhysics` (default 24). With `ShowCoords` on, the overlay's `traffic:` line adds `physics N/24 (... giving way, ... stopped),
   made ..., back ... on time and ... late, wrecked ..., retired ..., hidden ..., held back ...`. With `VerboseLog`, the log says
   `Traffic: a physics car made`, `Traffic: car N back on its timetable ... s late (out of reach)`, `... wrecked`, `... retired`.
   - Moving cars within about 90 m are real cars: drive into the side of one gently and it is pushed (it is no longer a wall);
     parked cars and cars further off are still walls.
   - They drive in their lane, slow for bends, queue behind each other and wait their turn at junctions (the first there goes first,
     the car on the right if two arrive together). Watch a busy junction in Kingsbridge at the evening rush (about 5 pm, the skip
     key): nobody drives through anybody, nobody stops in the middle of a junction and blocks it, nothing waits for ever.
   - Stop your car across a lane: the cars behind stop behind you and wait (they do not drive round you); others carry on. Drive on and
     look back: the queue moves off.
   - Cars that come up behind a queue you made from further off either join it or are not drawn (a car must never drive through
     another). A car about to pull out of its space next to a stopped physics car waits in its space.
   - Shoot one: it smokes; keep shooting and it is wrecked (flames, but no "WRECKED" text, no heal, no ammo, no kill counted), then
     it explodes and is gone. Ram one hard: the same. Shots and rams push it.
   - Drive away from a car you held up: at about 120 m it goes back to being drawn, with no jump, and carries on late (the overlay's
     `back ... late`). A car that reaches the end of its trip near you pulls into its space as before.
   - Knock one off the road (onto its roof, into a field): it stays where it is while you are near, then is gone once you are about
     120 m away.
   - Engine sound: each physics car plays one engine loop, as the game's enemy cars do.
   - At night the nearest few have their headlights on (no more than the drawn cars had).
   - Frame time with `TrafficPhysics` 0, 20 and 30 at the evening rush in Kingsbridge: please measure and send the numbers.
   - Alone only for now: in a room, everybody's physics cars are their own (stage 4 will share them). Say what you see there.
   - Send `LogOutput.log` and screenshots of a queue behind your car, a junction, a wreck.
   - Owner's first report (2026-11-23, alone in a room): "everything seems fine". The log shows cars made physical, one wrecked, one
     retired as damaged, one off its road, ten back on time out of reach, five origin shifts. Frame time not measured yet.
50. Traffic in a room (0.32.0; stage 4 of the traffic; everyone in the room needs the same build).
   Two players (A, the room's host, and B), `ShowCoords` and `VerboseLog` on. The overlay's `traffic:` line adds `room: host` (on A) or
   `room: clock +0.00 s` (on B: what B adds to its race clock to have A's), `N players heard, stand-ins N, claims N (granted N, lost
   N), handed in N and out N, states in N (N stale)`. The log: `Traffic: the room's host, settling who drives each car` (A) or `in a
   room, the host settles who drives each car` (B); `Traffic: car N driven by <name>: a stand-in here`; `car N handed to <name>`;
   `car N handed here by <name>`; `<name> left: their cars go back on their timetables`.
   - Alone in a room (A only): everything as in 49 (A drives every car); the overlay says `room: host`, `1 players heard`.
   - A and B far apart (different towns): each drives the cars near them; nothing changes for either (no stand-ins, no hand-overs).
   - B drives up behind A in traffic: the moving cars near A are A's (B sees them as stand-ins: they move smoothly, a little behind
     where A has them, no jumps or stutter). Push one with B's car: it is a wall, as another player's car is (pushing it is A's).
   - B shoots a car A drives: it smokes for both, then is wrecked for both (flames, explodes, gone) where it is, no kill for anybody.
   - A stops across a lane: the queue behind A is the same on both screens; B drives to the back of the queue and looks.
   - A and B drive apart along a road with traffic between them: the cars near B are handed to B as A goes (the log's `handed to` on A
     and `handed here` on B); the cars carry on, with no jump, stop or flicker, on both screens.
   - B joins the room after A has driven about (late joiner): B sees A's cars as stand-ins at once, and cars A held up late where A
     left them.
   - B leaves while driving cars near A: on A they go back to being drawn (late, from where they were), with no jump.
   - The clock: B's overlay `clock` should settle within a second of the start and stay within a few hundredths afterwards; tiles
     building or a teleport on B (a long frame) moves it a little, eased, never a jump of the drawn cars.
   - A parked car about to pull out next to a car A drives waits in its space on both screens.
   - Traffic cars never carry modded items or bodies (Dacia Body's logging of their lamps and paint is only that mod reading the
     materials).
   - Send both machines' `LogOutput.log` and screenshots from both of the same queue, a hand-over and a wreck.
   - Owner's first try (2026-11-23, alone in a room): fine as in 49; some cars run into had no physics (the ones pulling in or out, at
     a trip's end or in a junction: walls by design until contact physics, item 51); tyre marks streaked from the scene's origin to
     a car as it turned physical (fixed: the trails are cleared when a body is placed). Check no streaks now.
51. Contact physics (0.32.0; the owner, 2026-11-23: works (tyre marks, lamps and the parked cars' wheels fixed); see `docs/TRAFFIC_HANDOFF.md`,
   "Contact physics: as built"). New config `World` / `TrafficLoose` (default 6). Everyone in a room needs the same build (the traffic's
   messages changed). With `ShowCoords` on, the overlay's `traffic:` line adds `loose N/6 (made N, back N, retired N)` after the
   physics part. With `VerboseLog`, the log says `Traffic: car N loose (parked)` / `(moving at N km/h)`, `loose car N back as it was
   (untouched)` / `(N s late)`, `loose car N retired (knocked)` / `(damaged)` / `(wrecked)`, and at the start three `a physics car's
   body made` lines (the pool woken ahead of need, one a frame).
   - Drive gently into a parked car (a drive, the kerb, a car park): it is a real car, barely moved (its brakes are on); it is not a
     wall. Hit one hard: it slides and spins. Shoot one: it smokes, then is wrecked (no kill counted).
   - Drive away from one you knocked: it stays where it ended up while you are near, and is gone once you are about 150 m away; drive
     back: it is not there (its household goes without it for the rest of the race), and nothing else stands in its space wrongly.
   - Drive slowly past parked cars along a narrow town street without touching them: the overlay's `made` should not go up for each
     car you pass (a few is fine). Stop next to one without touching it, drive off: after about 18 m it is drawn again where it was
     (`back as it was (untouched)`), no jump.
   - Drive into a car pulling out of its space, or pulling in, or one turning at a junction near you: it brakes to a stop where it was
     and is a real car (before, these were walls). Leave it untouched and drive away: it carries on later from where it stopped
     (`N s late`), no jump; the cars behind it waited or were not drawn (none drove through it).
   - Physics cars driving near a parked loose car (still in its space) pass it as before; one knocked into the road, they stop for.
   - No tyre-mark streaks when a car turns loose, turns physical, or a stand-in appears or jumps (second fix: the body is teleported
     and its marks kept off for 0.3 s after any move).
   - Parked (static) cars stand on fully extended wheels, not squatted; one turning loose doesn't pop up on its wheels, and no tyre
     mark is painted from anywhere to it. Physics cars have four wheels where they belong (one build put them all in the middle).
   - A parked car turning loose should no longer bob on its springs (after the first few physics cars of the race: the log says
     `body model N stands ... over the ground` once its rest height is known; before that line a small drop is still expected).
   - A parked car that turns loose keeps its lamps dark (no brake lights, no glowing head lamps), in a room on both screens. A moving
     car that brakes to a stop keeps its lights.
   - Frame time in a busy town street (Kingsbridge at about 5 pm) with `TrafficLoose` 0 and 6, nudging parked cars: please send the
     numbers. Any hitch the moment a car turns loose (the first few, most of all) is worth a note.
   - In a room (A host, B): B nudging a parked car near A: on both screens it is a real car, B's (A sees it as a stand-in, its headlight off at night).
     A car B knocked is gone for both once B is well away. On B the first touch may still be a wall for a moment (the room's grant is
     a round trip; B's sweep looks 0.5 s ahead to cover it); say if it is often.
   - Send `LogOutput.log` and a screenshot of a car you knocked out of its space.
52. Fixes from the owner's 0.32.0 drive (0.32.1).
   - Follow a highway car into a tunnel (`Teleport` to the trunk, keep close behind one): it keeps its speed into the dark, never
     braking at the mouth, and comes out of the far tunnel ahead of you. The log has `Traffic: car N back on its timetable ... (in a
     tunnel)`, and no `loose` line for it.
   - The highway is busier: 100 cars circle it (10 before). A car each way in sight most of the time; say if it is too many or too few.
   - World 7.548, -9.002 (the trunk by the sea): no strip of water along the foot of the cutting south of the road.
   - `ShowCoords`: the overlay shows every line (none cut off at the bottom), and the height is metres above the sea (about 0 on
     the trunk there, which runs at sea level, not -19).

53. The frame rate on the highway (0.32.3; see `docs/PERF_HANDOFF.md`).
   - Same room seed, route, hour and weather as the 0.32.1 drive: from the spawn east along the trunk at speed. Say the frame rate
     against the 135 before 0.32.1 and the 100 on it.
   - `ShowCoords` on: the overlay's last line is `frame N ms (F fps); traffic ... ms a frame (choose, draw, bubble, contact,
     movers), physics steps ..., bodies switched ...; drawn ...; physics live ..., bodies taken ... and given back ... in the last
     minute`, each part averaged over the last second. The same line is in the log every 10 s as `Traffic timings: ...`: send the log.
   - Drawn traffic cars stand on the ground as the physics cars do: no drop when one turns into a physics car (0.32.2 lacked `main`'s
     rest on the springs).
   - Tyre marks: watch traffic cars near you over origin shifts (the log's `Origin shift N` lines) and as cars become physics cars:
     no streak across the country, from anywhere.

54. Where the frame goes (0.32.4; see `docs/PERF_HANDOFF.md`, "Done in 0.32.4"). `ShowCoords` on (and `VerboseLog` on for the
   `Frame timings on: ...` line saying what this build of the game gives).
   - The overlay ends with four lines, averaged over the last second: `frame N ms (F fps), worst W ms, H over twice the average; GPU
     ..., render thread ..., main thread ...`; `Unity's slowest steps: ...` (six steps of Unity's frame by name, and the time between
     frames); `ours: wrap, origin, streaming, tiles made, far view, foliage, sky, deer, traffic` (ms a frame, and tiles made in the
     second); `drawn: ... draw calls, ... batches, ... set-pass calls, ...k triangles, ... shadow casters; GC ... KB a frame`. "n/a"
     where the game's build does not give a number.
   - Drive the same route as test 53 (from the spawn east along the trunk at speed), then stand still on the trunk, then in
     Kingsbridge's centre, a few seconds each. Send `LogOutput.log`: every 10 s it has `Frame timings: ...` and every step of Unity's
     frame (`Frame timings, every step of Unity's frame: ...`). A screenshot of the overlay where the frame rate is lowest helps.
   - With `ShowCoords` off the frame rate is as on 0.32.3 (the markers come out of Unity's frame).

55. The frame rate after 0.32.5 (trees drawn in blocks, flat surfaces casting no shadows, the overlay made 4 times a second).
   - `ShowCoords` off: the frame rate driving from the spawn east along the trunk, read with the Steam overlay's counter, against
     0.32.4's. Then `ShowCoords` on, the same drive, and send `LogOutput.log` (its `Frame timings` lines: the draw calls should be
     several hundred fewer, `PostLateUpdate.FinishFrameRendering` and foliage lower).
   - The woods look as before: trees everywhere they were, shadows from the near ones, none missing at block edges (every 1,000 units,
     about 300 m) or flickering as the car crosses a tile or the origin shifts.
   - Shadows: buildings, bridges, the covered bridges, trees and cars still cast them; the ground under a hill at a low sun no longer
     shadows itself (say if that is missed).
   - Optional, to see what is not ours: the same drive with only Open World and Multiplayer enabled in the mod manager.
