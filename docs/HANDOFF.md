# Handoff

The one handoff: **overwrite it** at the end of each session with where things stand and what is next. Don't append history; git keeps
it. Long-term goals and the owner's standing decisions are in `docs/PLAN.md`. Open in-game tests are in `docs/TESTING.md`. Finished
research is in `docs/research/`, for reference only.

Last written 2026-11-25.

## Where things stand

- **Branch: `traffic-test-branch`.** Work and push only there; never start a new branch. Before starting, look at every branch's
  latest commit (`git for-each-ref --sort=-committerdate refs/remotes`).
- **Version 0.33.0** (`mod.json`), plus unreleased work on top: traffic lights (logic in `TrafficSignals.cs`, drawn American style by `SignalPoles.cs` and `SignalHeads.cs`), the trunk's
  junction lanes (`TrunkLanes.cs`: deceleration and acceleration lanes on the shoulder, left-turn and merge lanes in the median),
  right of way fixes, and lanes no longer laid on lanes (`Network.Throughs`, meshcheck `alongside`). Late and early drawn cars are let
  off a stand with nothing to stand for (`TrafficLate.SkipStand`), and the median paint no longer glows (`RoadMesh.Quad`). **The world
  changed** since 0.33.0 (the trunk is wider, four farm lanes are gone), so everyone in a room needs the same build. Bump the version
  with the next release.
- None of the unreleased work has been tried in the game (`docs/TESTING.md` 58).
- Last checks: `tools/check.sh`, the whole meshcheck and preview pass. trafficview passes on all 16 seeds (`TV_DRIVE_ONLY=1`, seeds 1
  to 15 and 20261122) and in full on seeds 1, 7, 9 and 13. trafficsim passes.
- **Traffic lights drawn** (2026-11-25, the owner: "American style"): `SignalPoles` lays out 64 masts at the 16 junctions with lights
  (47 on the far corner, 3 sharing a pole, pedestals across the T's), `SignalHeads` draws them (mesh in `SignalMesh`) and lights each
  lamp from `day.Lights.State`. trafficview's `traffic lights drawn` line checks them (seed 1 `TV_DRIVE_ONLY=1`: ok, no other FAIL).
  The mesh was looked at outside the game (the heads, visors and backplates from a driver's eye and behind) but not yet in it. Not
  done: no lights where the traffic failed to build (the day picks the junctions), no street name signs or "signal ahead" signs, poles
  not kept off houses or trees (none checked; houses stand back from the corners).

## Next: rework the highway's layout

The owner: the highway (the trunk road) is horrible as it is, and its layout is to be completely reworked. **Ask the owner what they
want it to be before designing anything.** The questions below are a starting point.

**What the trunk is now:**
- A dual carriageway, two lanes each way, round the map. It wraps: two tunnels joined (`Wrap.cs`, built by `WorldScene`, the jump in
  `Streamer`).
- At-grade junctions with every side road. Kingsbridge's link meets it at node 3 and Westhaven's at node 5 (11316, -9288).
- `TrunkLanes.cs` adds the auxiliary lanes; `RoadMesh.TrunkPaint` paints them.
- Traffic lights' logic at the junctions with primary or bigger roads (`TrafficSignals.cs`).
- 100 cars circle it all day (`TrafficDay`); the highway event's course runs along it (`Courses`, "Highway 10.1 km, 33 gates").

**What is wrong with it** (from the owner's 0.33.0 report and the traffic work):
- Cars turning off or crossing the trunk stood across both lanes of a carriageway ("cars cutting across the entire highway holding up
  every lane"). The median lanes and lights were the answer so far.
- Left turns off and onto the trunk at nodes 3 and 5 carry the towns' commuters against busy oncoming traffic, so they have little
  capacity. Queues back up the Westhaven link at the peak.
- Side roads meeting at a slant leave a waiting car on the trunk's paving. Junction boxes were widened (`Traffic.ObliqueBox`, at most
  75), but a few slanted lanes still hit that cap.
- More circling cars are blocked on this. With 300, the side roads can hardly join the trunk at the peak (3,606 min of junction
  waiting a day, 74 trips unfinished). The owner wants ten times today's highway traffic at about 135 fps.

**Questions to settle with the owner:**
- Grade separation (overpasses, slip roads) or still at-grade? The owner ruled out diamond interchanges on 2026-11-24 ("a small rural
  area"). A complete rework may reopen that, but ask; don't assume.
- Fewer junctions on the trunk (side roads joined to each other first), right-in/right-out only, or a different route round the map?
- Is it still a ring that wraps through the tunnels?

**What a layout change touches:**
- `Network.cs` (the trunk's route, its junctions) and `TrunkLanes.cs`; `RoadMesh.cs` (the trunk's paint).
- `Wrap.cs` and `WorldScene.cs` (the tunnels); `Streamer.cs` (the wrap's jump, the trunk's ground loaded ahead).
- `Traffic.cs`, `TrafficDay.cs`, `TrafficDrive.cs`, `TrafficSignals.cs` (the graph, lanes, right of way, the circling cars).
- `Courses.cs` (the highway event), `Signs.cs`, `Herds.cs` (crossings), `Foliage.cs`, `Parks.cs`, `Plots.cs`, `TownPlan.cs` (kept off
  it).
- Checks: meshcheck `wrap`, `seastrip`, `joints`, `holes`, `hubs`, `poke`, `alongside`; preview; trafficview on the 16 seeds;
  `tools/trafficview/Highway.cs` (the circling cars as a player meets them).
- It changes the world: everyone in a room needs the same build.

## Traffic leftovers (after the highway, or if the rework doesn't cover them)

1. The day's own peak queues round Kingsbridge (nodes 6, 42, 43, 49). Seeds 8 and 9 put physics cars in them for up to 9 minutes, and
   one car stood 900 s on Kingsbridge street at the link (seed 9). Find what holds the head of each queue (`TV_DRIVE_NOTES=1`: `held:`
   and junction waiting by edge). Town turn lanes or a light at node 43 may be the answer (the owner decides on new lights).
2. Physics cars "in each other": 0 to 2 a seed (limit 6). Seed 9's pairs are a physics car at the very start of its leg with a drawn
   car.
3. A physics car brushed the player's stopped car (seed 11): not looked at.
4. Then the owner's test in the game (`docs/TESTING.md` 58). Town turn lanes come after; the owner decides when.

## How to run the checks here

- **HalfGripTools.dll** isn't here. Build the stand-in: `dotnet build tools/halfgriptools-stub -c Release -o <scratchpad>/stub`, then
  `tools/check.sh ../HalfGripRefs/game <scratchpad>/stub/HalfGripTools.dll`.
- The tools need `-p:GameDir=/home/user/HalfGripRefs/game` (the refs repo cloned next to this one).
- **16 seeds of trafficview:**
  - Build it, copy `tools/trafficview/bin/Release/net8.0` to the scratchpad, and run the copy 4 seeds at a time in the background:
    `TV_DRIVE_ONLY=1 TV_DRIVE_NOTES=1 dotnet <copy>/trafficview.dll --seed N > sN.txt`.
  - All 16 take about 20 minutes. Don't rebuild while the loop runs.
  - From each report, take one line per seed: junction waiting, overlaps, box conflicts, stopped across, physics pairs, physics box
    crossings, stops over 90 s, FAILs.
  - Every fix so far moved the day and turned up a different single failure somewhere else: rerun all 16 after each change.
- How long things take: meshcheck, all of it, about 1 min 45 s; the layout in trafficview about 7.5 s. Anything longer than 2 minutes
  goes in the background with its output written to the scratchpad.
- `convert` (ImageMagick) turns the tools' PPM pictures into PNG.
- Decompile: `~/.dotnet/tools/ilspycmd` with `DOTNET_ROLL_FORWARD=Major`. The scene dumps are in HalfGripRefs (`scenedumps/0.419`).
- The network allows `www.fhwa.dot.gov`, `rosap.ntl.bts.gov`, `gis.penndot.pa.gov`, `nhts.ornl.gov`. Use `curl` (the page reader
  can't resolve them, nor pa.gov).

## Working with the owner

- Ask for anything you need (access, files, a decision) at once, plainly. Don't guess, and don't quietly work around a refusal.
- Fix bugs without asking whether to. Ask about design choices.
- Verify before claiming: say what you read or ran yourself and what came from notes.
- Use plain words about what the player sees. Show only pictures you have looked at yourself.
- The owner tests in game and reports with screenshots and `LogOutput.log`.
