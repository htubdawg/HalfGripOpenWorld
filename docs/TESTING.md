# Test checklist

Things only the game can show, still waiting for the owner's report. Turn on `TestMode`, `ShowCoords`, `Teleport` and `VerboseLog`,
then run the open items below. Numbers carry on from the archive: tests reported on or superseded move to
`docs/archive/TESTING_DONE.md` (same numbers). Add each new test at the end with the next number, and move a test there once the
owner has reported on it.
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
58. `traffic-test-branch` after 0.33.0 (not released; everyone in a room needs the same build: four farm lanes changed). The
   highway's own items (57's first point) wait for the highway rework (`docs/HANDOFF.md`).
   - No small white glowing patches in the trunk's median (they were at (-2643, -9813) and all along it).
   - Traffic lights are drawn, American style, at the 16 junctions where a primary road or the trunk meets a secondary or bigger
     road: a grey steel mast arm for each way in, across the junction from it, with a yellow head for each lane and one on the pole
     (from a side road at a T, across the road you meet). Stopped at the line, the lights are ahead of you, not overhead. The lamp lit is the one the cars obey: red, amber, green as the cars stop and go; it glows at night. Say if
     a pole stands in a building, a tree or the road, if a head faces the wrong way, or if a car goes on a red. The log says
     `Traffic lights: 16 junctions, 48 mast arms`. The junctions where a primary road or the trunk meets a secondary or bigger road take
     turns: side-road cars wait, then several go together while the main road stops. Say if a main road seems to stop for nothing.
   - No physics car stands for minutes behind a drawn car that is stopped for no visible reason.
   - No country lane is laid on top of another (Farm lanes near (7796, -10404) and (-5751, 12853)): one road, not two in one.
59. The highway reworked (`traffic-test-branch`, not released; the world changed, everyone in a room needs the same build).
   - The highway has a grass median about 7 m wide, flush with the road: drive across it anywhere into the oncoming carriageway (it
     drives like the road). The shoulders are narrow; there are no long on/off lanes beside the carriageways any more.
   - At every side road the median is paved for the opening, and the carriageway that turns left there has a left-turn lane cut into
     the median before it (solid white line beside it, yellow on its median side, opening out over a taper). Cars turning left move
     into it and wait there, never in a through lane and never in the median elsewhere. Cars coming out of a side road wait at their
     line and cross or join in one go. Say if a car stands in the median outside a left-turn lane, or across a carriageway.
   - K-rail (a grey concrete barrier) runs down the middle of the median on five stretches, e.g. round (12229, -8922) and (4393,
     -10413). It is solid. Say if you would like more, fewer or longer ones.
   - At the junctions with lights on the highway, the left-turn lane has its own head on the mast arm with red, amber and green
     arrows. Left turns go only on the green arrow (both left-turn lanes together, every through lane red), and the arrow only comes
     up when a car waits in a left-turn lane. Say if a car turns left on a red arrow, or the arrow never comes.
   - The highway event's grid and start box are in the moved lanes (the carriageway's middle is further out now).
60. The owner's report on the highway (2026-11-25, `traffic-test-branch`, not released).
   - The paved median at the junction openings and in the left-turn lanes has the same fine asphalt grain as the lanes beside it (it
     was drawn as coarse stripes across the road).
   - Every way into a junction with lights has a white stop line across its side of the road, where the cars stop (on the highway
     across the left-turn lane too). Say if one is missing, under the road, floating, or where cars stop well past or short of it.
   - The highway's white edge line is broken across each side road's mouth. A left-turn lane's white line is dotted over its taper
     and its first stretch (where you move into it), then solid up to the junction.
   - Traffic is drawn out to about 450 m (moving) and 230 m (parked), against 270 m and 140 m before: cars should no longer pop in
     close. `ShowCoords` on: say the frame time and the traffic line's numbers on the highway and in Kingsbridge's centre.
61. The owner's report on the traffic lights (2026-11-25, `traffic-test-branch`, not released).
   - No fingerpost stands at a junction with lights any more. Instead each way in's mast arm carries a green board (white letters
     and border, as Pennsylvania's) with the places the fingerpost gave: an arrow for each (left, ahead, right as you come in), the
     name and the miles. Stopped at the line, read it: say if an arrow points the wrong way, a place is missing, or a board hangs in
     front of a head. The log's `Road signs:` line says `green boards for the lights at 16 junctions`.
   - On the highway, the lights hang from a steel truss spanning the whole road on two poles (9 of them) instead of a single mast
     arm. Say if a pole stands in a building or the road, or the truss looks wrong.
   - The left-turn lane's head shows bold left arrows (red, amber, green), not round lamps.
   - The red lamp is a deep red, not orange; it glows a little less than the amber and green.
62. Kingsbridge's left-turn lane (2026-11-25, `traffic-test-branch`, not released; the world changed: everyone in a room needs the
   same build). Teleport to Kingsbridge and drive south down the town's link road towards the trunk, past the lane on the right, to
   the lights where the road to Pinewick goes off left (about (-2388, -4337)).
   - The road widens by a lane over the last stretch before those lights (from just before the lane junction), the two lanes moving
     out; between them two yellow lines, and up to the lights a left-turn lane in the middle: white line dotted where you move in,
     then solid, a white left arrow painted near the stop line, a stop line across the turn lane and your lane. South of the lights
     the road narrows back. Say if the paving, the verges, a sidewalk or the lines look wrong, or a building or pole stands on it.
   - The cars turning left for Pinewick move into the turn lane and wait there; the cars going on pass them.
   - The mast over your way in has a third head over the turn lane with arrows: a flashing yellow arrow while the main road is green
     (turn when there is a gap), a green arrow when a car has waited there a while, red otherwise.
   - Drive up from the trunk in the morning (around 8:00 game time): say if the queue into Kingsbridge on the link is long.
69. Phase 0 of the traffic rework (`traffic-rework-branch`, not released; the world is `traffic-test-branch`'s, the traffic is gone).
   - No traffic cars anywhere, parked or moving; no `Traffic:` lines in the log and no `traffic:` line on the overlay. The config's
     `Traffic`, `TrafficPhysics`, `TrafficLoose` and `TrafficInstanced` are no longer read (an old config file keeps them, harmless).
   - The traffic lights still stand at their 16 junctions with their green boards and stop lines. The log says `Traffic lights: 16
     junctions, ... mast arms ...` a second or so after the start (laid out on a worker: no hitch).
   - Each light cycles on its own: the main road green about 30 s, amber, a moment of all red, the side road green about 18 s; at the
     trunk's lights a green left arrow (about 10 s) between them; Kingsbridge's bay a flashing yellow arrow on the main road's green.
     Neighbouring lights are not in step. In a room both players see each light the same (within a second or so).
   - The highway as before: grass median, left-turn bays, K-rail, the wrap; Kingsbridge's widened road and its bay.
73. The traffic's day's end (`traffic-rework-branch`, not released; alone only). A day is 40 minutes of the race (100 seconds an hour),
   so a race started in the evening reached 03:00 within minutes and the traffic stopped there: the cars on the highway stood still.
   Now the traffic goes on into the next day. Turn on `WeatherSkip` (Debug) as well as `ShowCoords`; the traffic now follows the Weather
   skip key forward (F5 unless rebound: 5 minutes on, about two hours of the clock; Shift+F5 moves only the sky back, the traffic stays
   ahead until the sky catches up).
   - Press F5 a few times: the cars run on quickly for about a second each time (a fast-forward, nothing else), then drive normally; the
     lights still match the cars (none drive through a red, none wait at a green).
   - Skip to just before 03:00 (the overlay's `traffic:` line gives the traffic's clock) and drive on the highway: past 03:00 the through
     cars keep going, the line says `(day 2)` and the log has `Traffic: the day's end; day 2 begins, ...` (send it). No hitch at 03:00.
   - Skip on to the morning (05:00 to 08:00 of day 2): cars set off from homes, the roads fill as they did on day 1, and cars you saw
     parked at home are the same ones that drive away (the follow-car key, F11, shows a car's day: its stops now day 2's).
   Send `LogOutput.log`, and say if anything stands still, jumps or hitches.
