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
57. Right of way, the highway's two lanes, cheaper far traffic (0.33.0; everyone in a room needs it: the day of traffic changed).
   - On the trunk at the evening rush (about 17:00): the highway's cars use both lanes, overtaking in the inner one and back in the
     outer one after; they move over at a junction where a car waits to join. No car on the trunk stops for a car from a side road
     (nor at the Westhaven link, node 5, at 11.316, -9.288, where they queued before). Cars from the side roads wait at the line for a
     gap and then go. Say if the queues on the Westhaven link or the Kingsbridge link at the peak look too long.
   - A junction of a main road with a side street in a town (Kingsbridge's centre): main-road cars go straight on without stopping
     for side-street cars.
   - Physics cars near you on the trunk keep their lane while you are near (they don't overtake each other), and never brake for a
     car in the other lane. Overtake one: it does not swerve.
   - Far traffic (past about 75 m): looks the same as before from a distance, with no shadow; it turns into the full car with turning
     wheels and its shadow as it comes nearer (within about 45 m), with no visible pop. If far cars are missing or all drawn in one
     place, set `TrafficInstanced` off and say so (the game's car shader may not take GPU instancing).
   - `ShowCoords` on: the traffic line says `(N far)`; send `LogOutput.log` from a drive along the trunk and from Kingsbridge's centre:
     its `Frame timings` lines (draw calls and shadow casters with traffic about, against 0.32.6's).
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
63. Routes that know the queues (2026-11-26, `traffic-test-branch`, not released; the traffic's day changed: everyone in a room needs
   the same build). The world is the same; only where the cars drive changed.
   - Start a race and say how long until the traffic shows (the day is now built twice: about 2.5 s more on a fast machine). Send
     `LogOutput.log`: it says how long the day took.
   - Drive round Kingsbridge in the morning (about 8:00) and the evening (about 17:00): the queues on the town's road down to the trunk
     should be shorter than before. Some cars now go round by the back lanes south of the town (east of the link) and by the road
     to the trunk further west: say if any lane or street looks busier than it should, or if cars sit still in a ring of queues
     that never moves.
   - **When reporting traffic:** the log now has a "Race seed: N" line near the race's start. With it, the day can be replayed exactly:
     send the log with the screenshot and the overlay's position and time showing.
64. Far traffic seen (2026-11-26, `traffic-test-branch`, not released). **Confirmed by the owner**: cars on the highway no longer pop in
   close; far cars were drawn on a layer the main camera doesn't show. Recheck in towns: parked cars down a long street should be
   visible from about 230 m, not appear at 75 m. If a pop-in is ever reported again, send the log: its `Traffic pop-in:` lines and the
   timings lines' `first drawn close this race N` say where to look.
65. Far traffic's paint and tail lights (2026-11-26, `traffic-test-branch`, not released). Far cars drawn instanced wore the wrong
   paint and showed their tail lights through the body; `TrafficInstanced` now defaults to off (a config file that already has it
   keeps `true`: set it to `false`). Check: a far car has the same paint as when it comes near, and a car coming toward you shows no
   red lamps.
66. A car standing in the open road (2026-11-26, `traffic-test-branch`, not released; the traffic's day changed: everyone in a room
   needs the same build). The owner's report: race seed 192843966, about 09:05, a car stood about 25 s on Kingsbridge's road north of
   the lights where the road to Pinewick goes off, waiting to turn left onto the lane west (about (-2354, -4259)), with nothing coming.
   It was giving way to cars that had already crossed and were crawling into a drive just past the turn. Now a car pulling in just past
   a junction gives it up as soon as it is out of it. Check: at that turn, and wherever cars turn across a road, a car waiting to turn
   goes once the way across is empty; say where one still stands with nothing coming, with the log and the overlay's position and time.
67. No car vanishes (2026-11-26, `traffic-test-branch`, not released; the traffic changed: everyone in a room needs the same build).
   Until now a drawn car that would run into one of the cars near you (or a car running late) was hidden on the spot. Now it slows and
   waits behind it, then pulls away, and runs late. Check, round Kingsbridge at the evening rush (about 17:00) and on the highway:
   - No car disappears in front of you, ever, except a wreck burning out. Say where one does, with the log and the overlay's position
     and time.
   - Stop in a lane, or bump a car so it stops: the cars coming up behind it slow down and queue behind it, at the same spacing as
     any other queue, and set off again when it moves. None drives into it or through it.
   - Cars pulling out of a drive or a lot wait for a car stopped by its mouth; a car arriving at a space someone is still leaving waits
     beside it.
   - The overlay's traffic line (`ShowCoords`) says `held behind another N` where it used to say `hidden N`.
68. Held cars become real cars (2026-11-26, `traffic-test-branch`, not released; the traffic changed: everyone in a room needs the same
   build). Since 67 a car played back from the day that had something in its way slowed and waited, but it couldn't negotiate a
   junction, so the real cars round you waiting on it (and it on them) locked up for minutes. Now such a car becomes a real car where it
   is (up to 16 more than `TrafficPhysics`), in a junction too, and no car waits for a car that is waiting for it. Check round
   Kingsbridge at the evening rush (about 17:00):
   - No knot of cars that never moves again. Say where cars stand over a minute and a half with nothing visibly holding them (the
     overlay's position and time, and the log).
   - Frame rate: with `ShowCoords` on, the `traffic:` line shows `physics N/24+16 (... ) made ... (... claimed for having to slow)`;
     note the fps and the `Frame timings` log lines with the most physics cars you see, against 0.33.0 in the same place.
   - A car pulling out of a drive in front of a stopped queue now goes (the queue lets it out); one pulling into a space goes even if a
     stopped car is right behind it.
