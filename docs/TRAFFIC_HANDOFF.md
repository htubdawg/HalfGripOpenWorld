# Traffic: handoff

For the agent picking up NPC traffic. Read `CLAUDE.md`, then in `docs/PLAN.md` the three traffic sections ("Traffic: the agreed plan",
"Traffic: what the studies say, and the trip model", and the older research under them), then this. Work on **`traffic-test-branch`**
(made from `deer-test-branch`; commit and push there; the owner merges).

## Start here

1. Read `CLAUDE.md`, then the traffic sections of `docs/PLAN.md` (see above), then this file.
2. Check out `traffic-test-branch` and clone `htubdawg/HalfGripRefs` next to the repo (the tools need
   `-p:GameDir=/home/user/HalfGripRefs/game`).
3. Run `dotnet run -c Release --project tools/trafficview -p:GameDir=... [-- --seed N] [--picture out.ppm]`. It builds the map, then
   race N's day of traffic twice, checks it and exits 0 (see "`TrafficDay.cs`: as built" for what it reports). About 15 s.
4. Show the owner `docs/traffic.png` and `docs/traffic-frames.png` and ask about the open points below; then stage 2 (the plan).

Open with the owner:
- **Test 47** (`docs/TESTING.md`): the highway's wrap, Westhaven's road out and Millbrook's road north in the game. The owner looked at
  the work and said it looks good, but no in-game report has come back yet. Read their `LogOutput.log` for
  `The highway wrapped round` lines when it comes.
- **The eight cut-off town streets**: a layout fix for later (see below). Ask before doing it: it changes the world.
- **The day of traffic** (`TrafficDay.cs`, below): the pictures, and the choices made that the design did not settle (trips beyond
  the map go by the road north only; through traffic circles the trunk; stays counted from leaving home; see "Decided while building").

## Where things stand

Stage 1 (timetables, no game code): the traffic's map and each race's day of trips (`TrafficDay.cs`) are done and checked; the owner
has the pictures to look at. Stage 2 (cars drawn in the game) is next. Nothing is
released and `mod.json` is unchanged. The branch also carries two world changes (the road fix and the wrap), so everyone in a room
needs the build that has them. Bump the version and the README when the owner releases it.

**Done and pushed:**
- The plan, the owner's answers and the studies' figures are all in `docs/PLAN.md`.
- `OpenWorld/src/Plots.cs` (pure): the house-plot decisions (`Plots.Along`) and the footprint test (`Plots.Fits`), moved out of
  `Buildings.cs`, which calls them. Traffic finds every household from the same arithmetic the drawing uses. **Confirmed to change
  nothing drawn:** meshcheck's clear, facades, bases and homes give the same counts as `deer-test-branch` (2,606 houses, wings,
  towers and churches; 274 drive houses).
- `Heap.Clear()` added.
- `OpenWorld/src/Traffic.cs` (pure): the traffic's map, fixed with the world.
  - The driving graph: drives included; tracks, paths and courses left out. No junction where two roads cross more than 4 units apart
    in height (a bridge over a road).
  - Its connected parts (`Parts`, `Part`, `MainPart`, `OnMain`): households and places on a part cut off from the main one are left
    out (`CutOff`), so every trip has a route.
  - Speed limits: Pennsylvania's, lower in towns.
  - Lane offset: right-hand traffic, the trunk's outer lane.
  - Households: drawn houses (a pad beside a garden house, the kerb for terraces), drive homes, farms; cars and workers from hashes.
  - Destinations with spaces and per-purpose draw: offices in the towers, retail, shops, high-street shops, workshops, filling
    stations, markets, churches, courses' yards, the roads leaving the map.
  - `Route` (Dijkstra between two anchors, partial edges at both ends).
- **A layout fix (changes the world):** `Network.SnapJoin`.
  - A routed road's end looked for the road whose cell it reached within 140 units only. The trunk's smoothed line runs up to about 270
    off its cells, so Westhaven's only road out (its link to the trunk) stopped in the fields and the whole town (376 households,
    16 places) was cut off. The main road from Millbrook to the north edge and four farm lanes ended short the same way.
  - Now an end that finds nothing looks out to 320.
  - meshcheck's `backroads` fails on any main road dead end (it found these two before the fix). All of meshcheck passes before and
    after; a few later roads move.
  - Millbrook's road now meets the road from Northcote at a slant after a tight bend: it works, but isn't pretty (jview shows 25
    pixels of ground and 7 overlapping there).
- **The highway wraps round (changes the world; asked for by the owner):** a car that drives into the trunk's tunnel at one edge comes
  out of the other's at the same speed, in the same lane, facing the same way.
  - `Wrap.cs` (pure) pairs the two tunnels (`Network.Portal.Road`; they face opposite ways, so the move has no turn), glues them at a
    plane `Depth` (14) inside each, and gives the move (`Move`).
  - `Streamer.WrapTarget` loads the ground round the far mouth (2 tiles) once the car is within `Wrap.Preload` (700) of a tunnel.
  - `Streamer.WrapRound` (in `LateUpdate`, before the origin follow) does the jump as an origin shift that keeps the root holding
    the car, the cockpit and all three cameras (`Player` in the Tokyo dump) in place (`Origin.Shift` with a keep set). The leftover
    under a tile, plus the difference in the two roads' heights, is moved with `Origin.MoveKept`. Tyre trails are cleared.
  - `WorldScene` builds the two open tunnels: a dark floor at the road's height, walls, a roof, and a back wall at `Wrap.Length` (24)
    that is seen but not solid, so a fast car can't hit it before the once-a-frame check sees it past the plane. The road north's
    tunnel stays shut.
  - Read from the Multiplayer mod's source (`RemoteCar.FixedUpdate`): a stand-in more than 10 units from a new state snaps to it, so
    others see the car come out of the far tunnel.
  - meshcheck's `wrap` check: the tunnels paired, every lane landing in its lane, the floors clear of the ground (the edge's ground
    rises through a deeper floor), the highway event clear of both tunnels.
  - **Not yet tried in the game.**
- `tools/trafficview`: builds the map and reports it; exits 1 on a failed check; `--near <road>` lists what comes near a road (for
  finding why one is cut off). Last run:
  - 1,261 junctions, 1,451 stretches of road, 1,907 households with 3,604 cars, 273 destinations, built in about 540 ms (after the
    layout's 7.5 s).
  - The graph in 9 parts: the main one 569,228 units of road; 8 cut-off town streets (2,761 units) whose 110 households are left out.
  - 3 roads leave the map (the trunk at both ends, the road north), so 3 edge destinations. That is right: the old note suspecting
    `Network.Portals` was wrong.
  - Routes: 2,184 of 2,184 found, 0 broken, 0.05 ms each.
  - 1.10 workers a household, lower than the survey's 1.33 because `WorkersOf` clamps workers to cars. Not changed: decide when tuning
    the day (the clamp may be right, since a worker without a car does not drive).
  - Not checked yet: that the map itself builds the same in two processes (the day's checksum is checked twice in one process, over
    one build of the map). Run trafficview twice and compare the checksum line to check it by hand.
- **`OpenWorld/src/TrafficDay.cs` (pure): each race's day of trips**, checked in trafficview (`--seed`, `--picture`;
  `tools/trafficview/Pictures.cs` draws). See "`TrafficDay.cs`: as built" below. Commit `ab22a3a`.
- `tools/check.sh` passes against a stub HalfGripTools.dll (below). meshcheck builds with `TrafficDay.cs` in it; its last full run
  (all passing) was before `TrafficDay.cs`, which nothing meshcheck checks calls.

**Left for later (in the plan):** the eight cut-off town streets (three in Kingsbridge, five in Westhaven) are a `TownPlan` fault. Each
is a street laid with both ends free that no cross street came to join, reached only by footpaths. Fixing it (join such a street to
the nearest road, or don't lay it) is a world change with a meshcheck check to add. trafficview lists them by position and road index.

## `TrafficDay.cs`: as built (pure, per race)

`new TrafficDay(raceSeed)` builds the day (about 1.3 to 1.5 s: on a worker in the game); `At(car, simTime)` gives a car's place
(parked at a spot, driving with point, heading, speed and edge, or away off the map); `SimTime(startHour, raceTime)` maps a race's clock
to the day's (0 at 03:00, `Traffic.DaySeconds` long, repeating). The seed is `Weather.Seed`, the start hour `Sky.StartHourOf(seed)`, as
for `Herds`. It is not called from the game yet.

What it does, as the plan designed it (the full design is in this file as of commit 8374760, `git show 8374760:docs/TRAFFIC_HANDOFF.md`;
only the differences are spelt out here):
- **The day:** a weekend day with probability 2/7.
- **Tours:** the survey's rates per household (work per worker); a leaving hour from the purpose's table (tuned, see below), a stay,
  a place by gravity on straight-line distance, weight / (1 + (d / 3000)²) (3000, not 6000: with 6000 trips across the whole map were
  common, and a long drive spans hours of the world's clock); visits also among eight households drawn at random.
- **Thinning:** each tour kept with one probability so households drive the survey's 186 s each a day, reckoned at each path's speed
  profile plus a quarter (`Expected`), not free-flow: the survey's 59 minutes are real driving. q comes out about 0.15 on a weekday,
  0.18 at a weekend.
- **One car, one tour at a time;** none leaves after 21:00 (not 23:00); a tour waiting for its car is dropped then.
- **The simulation:** 0.25 s steps, cars in index order; a speed profile per leg (limits, bends as sqrt(8.1 R) with R over a 40-unit
  chord either side, so the drawn line's small kinks don't read as bends; turns; the stop; a backward braking pass); IDM behind the car
  in front along the car's own path (200 units). Cars brake for a slower stretch ahead as the profile allows, not by the IDM's free
  term (which trails a falling target by up to 27% over it).
- **Junctions** (all uncontrolled), by reservation, first come first served:
  - Requests sorted by the soonest a car could arrive (speeding up to its profile), the car on the right first within a second.
  - Two movements conflict when their chords round the junction cross (each arm has its lane out, then its lane in, in angle order:
    integers only), or they leave by the same lane. Same lane in never conflicts (the second follows the first).
  - A conflicting grant is no bar if it clears before this car arrives (reckoned at its turn's speed through the box) or this car
    clears, from a standstill, a second before it arrives (a gap taken).
  - Room past the box by distance only: a car let in behind a moving one that then stops would stand in the box and hold it (that
    made gridlock rings on the Kingsbridge link). Cars let in behind this one in its own lane are not counted against its room.
  - A car refused for crossing someone keeps its place: nobody later who crosses it goes first. A car refused only for want of room
    holds nobody up.
  - The 45 s guard only lets go of the order, never into a car in the box, never without room. It is used 0 to 3 times a day.
- **Pulling out:** once nothing is within 40 on the lane (or just past a short first stretch), nothing coming within 3 s and no car let
  into the lane at the junction behind; after 20 s waiting the car behind leaves a gap if it can stop for it (let in); never on top of
  another car, even after 5 minutes. Parking spots on the road are moved out of the junction boxes along their stretch.
- **Recording** every 0.5 s per leg, the final place at its end; `At` interpolates. Lane offset right of travel, `Traffic.LaneOffset`.

**Decided while building** (for the owner to confirm):
- **Every car into a trunk tunnel comes out of the other at the same moment.** Since the wrap joins the trunk's ends, they are not a way
  off the map: trips beyond the map go by the road north only, and it takes the draw the trunk's ends had (`PullOf`, so the share of
  work beyond the map stays the same; without it, three quarters of the commuters drove to Kingsbridge's towers and jammed it).
- **Through traffic circles the trunk:** 10 cars, half each way, spread along it at 03:00 and driven all day through the wrap. They add
  about 1,800 to the trunk's count. Flaw: at 03:00, where the day repeats, each jumps from where the day ends it to where it began.
  (Through journeys from the road north round the wrap and back were tried first: 72,000 units, 15 to 20 world hours each, mostly on
  the main roads, not the trunk.)
- **A stay is counted from leaving home** (`MinStay` 20 s at least). Driving runs in real time while the world's clock runs 36 times
  fast, so a drive spans one to three world hours; counted from arriving, every way home ran late into the night.
- **The hour tables are tuned** so the traffic counters' hours come out right (weekday against TPG 9, weekend against the survey's
  Saturday): departures lead the counts by about the length of a drive, the work peak at 5 to 7, visits end by 20.

**Checks in trafficview** (exit 1 on a FAIL). Seeds 1 to 15 and 20261122 all pass:
- the same day twice (checksum), and another seed's differs;
- no leg never started or unfinished; nothing unrouted;
- no two cars on a lane closer than a car's length (closest about 16);
- no crossing cars in a junction box together; the guard's uses reported;
- never over the limit + 5%;
- AADT-equivalent (passes a game day x 36) by class, reported against PennDOT: trunk median 4,100 to 6,600 (5,400), main roads about
  5,000 (minor arterials 5,000), secondaries 2,600 (collectors 1,800), streets and lanes about 150;
- the hours' shape within 3.5 points an hour of TPG 9 (weekday) or a Saturday (weekend); weekdays come out 1.9 to 2.7, weekends 2.3 to
  3.2. A drive spanning an hour or more of the world's clock smears every hour into the next, which no table fully undoes; the
  small hours stay a little busy (about 50 cars at 03:30 against 16 expected, mostly late ways home);
- cars moving at 03:30, 08:30, 12:30 and 17:30 against the expected (households with a car x their driving share x TPG 9's hour):
  the 5 pm peak about 300 (301 expected);
- lots' peaks against their spaces (reported: a few high-street shops get one or two more than their two spaces);
- the wrap: every crossing between the trunk's tunnels, each car's place across it off `Wrap.Move` by 0.0; nobody stopped in or within
  60 of a tunnel's mouth;
- `--picture out.ppm` writes the roads coloured by AADT-equivalent and frames at 03:30, 08:30, 12:30, 17:30 (`docs/traffic.png`,
  `docs/traffic-frames.png`, seed 7, a weekday).

Diagnostics it prints (not checks): where cars wait and stop, departures by hour and purpose, the trunk's share of the through cars'
driving, the worst leg's progress, cars on the road each hour of the run.

**Not done yet:** the relevant checks in meshcheck (the plan says meshcheck; trafficview is where they run now); what one car's
place costs (`At` scans a car's legs: fine for a few hundred cars, measure in stage 2); the deer reacting to traffic.

**Known weaknesses** (none fails a check; worth knowing before stage 2):
- **The Kingsbridge link** (the main road through the main town, a chain of junctions 16 to 220 apart) is where the queues are: most
  of the day's stopped time is on it and the streets joining it. It flows now, but it is the first place to look if a change brings
  back gridlock (trafficview prints the worst junctions and stretches).
- **Parked cars of one household share one spot** (`At` gives the home anchor's X, Z for each); stage 2 must spread them (by car
  index along the kerb or the drive). Several places' cars likewise park at one spot.
- **Drives have no lane offset** (`Traffic.LaneOffset` 0), so two cars meeting on a drive overlap when drawn; rare.
- **Legs that run past the day's end** (late ways home) are driven in the run's tail, after 03:00, without the next morning's traffic
  around them; `At` shows them in the morning (it checks w and w + 2400).
- **The circling cars jump at 03:00** (above).
- **A few high-street shops** park one or two cars over their two spaces at the peak.
- **Diagnostics left in `TrafficDay`** (`Stuck`, `WaitAt`, `StoppedOn`, `ActiveBy`, `StartsBy`, `ProfileSeconds`, `OverlapNotes`,
  `ConflictNotes`, the refusal counts): cheap, and they found every bug this session; trim them if they get in the way.
- `Traffic.WorkersOf` still clamps workers to cars (1.10 a household against the survey's 1.33); left as it is.

**For stage 2 (using the day in the game):**
- Every machine builds the same day from the room's seed, so drawing timetabled cars needs no messages. Build it on a worker at the
  race's start (about 1.4 s; it allocates the recordings, about a million floats).
- Sim time: `TrafficDay.SimTime(Sky's start hour, race time)`. Then `At(car, w)` for the cars near the player: `State` (Parked, Driving,
  Away), the point, heading (`Hx`, `Hz`), `Speed`, and `InTunnel` (hide or darken those: they are inside a tunnel's mouth).
- `X`, `Z` are world coordinates (subtract the origin to place them); `Y` is from sea level: add `Network.Lift` for the scene (mixing
  the two sank every building once). The point is the road's line plus the lane offset, not the drawn surface: sample the ground or
  the road mesh's height if they differ.
- `At` walks a car's legs: fine for the few dozen near the player; for a pool, index cars by where they are (each leg's path is a list
  of edges) rather than calling `At` for all 3,000 every frame. Measure it.
- A car on a leg past the far end of a loaded tile is fine to skip; parked cars are most of the cars at any time.

**Next (stage 2, the plan):** timetabled cars drawn in the game from a pool near the car, moved on an origin shift, no physics; build
the day on a worker at the race's start; headlights.

## This session's environment, as found

- The network allows `www.fhwa.dot.gov`, `rosap.ntl.bts.gov`, `gis.penndot.pa.gov` and `nhts.ornl.gov` (the owner added them). The
  page reader could not resolve them; `curl` works. Re-download the PDFs from the plan's links if needed (they were in the
  scratchpad).
- The Multiplayer mod's source is public: `git clone https://github.com/4l3x4nd3r0/halfgripmods` (`mods/Multiplayer/src`). Copy no code
  from it (the owner's decision).
- Decompile: `~/.dotnet/tools/ilspycmd`, with `DOTNET_ROLL_FORWARD=Major`.
- Tools need `-p:GameDir=/home/user/HalfGripRefs/game`. `tools/check.sh` needs a HalfGripTools.dll, so build a stub as
  `docs/DEER_HANDOFF.md` describes. It needs: `HalfGripMod` (abstract `Load`, virtual `Unload`), `ModContext` (`Log`, `Config`,
  `Harmony`, `Keys`, `Net`, `Folder`, `PatchAll`, `OnSceneLoaded`, `OnUnload`), `ModKey` (`Down`, `Held`), `Keys.Add(KeyCode, string,
  Controls.Section, Func<bool>)`, `ctx.Net` (`Send(data)`, `Send(data, to)`, `SendToHost`, `OnReceive(Action<ulong, byte[]>)`), the static
  `Net` (`Available`, `InRoom`, `IsHost`, `LocalId`, `HostId`, `Players`, `PlayerJoined`, `PlayerLeft`) and `NetPlayer` (`Id`, `Name`). The one used
  last (build it as netstandard2.1, assembly name `HalfGripTools`, against the game's Managed DLLs, `BepInEx.dll` and `0Harmony.dll`,
  leaving out `mscorlib`, `netstandard` and `System*`):

  ```csharp
  using System; using System.Collections.Generic; using BepInEx.Configuration; using BepInEx.Logging; using UnityEngine; using UnityEngine.SceneManagement;
  namespace HalfGripTools
  {
  	public abstract class HalfGripMod { public abstract void Load(ModContext ctx); public virtual void Unload() { } }
  	public class ModKey { public bool Down => false; public bool Held => false; }
  	public class KeyRegistry { public ModKey Add(KeyCode key, string label, Controls.Section section, Func<bool> shown) => null; }
  	public class NetPlayer { public ulong Id; public string Name; }
  	public class ModNet { public bool Send(byte[] d) => false; public bool Send(byte[] d, ulong to) => false; public bool SendToHost(byte[] d) => false; public void OnReceive(Action<ulong, byte[]> a) { } }
  	public static class Net { public static bool Available, InRoom, IsHost; public static ulong LocalId, HostId; public static IReadOnlyList<NetPlayer> Players => new List<NetPlayer>(); public static event Action<NetPlayer> PlayerJoined, PlayerLeft; }
  	public class ModContext { public ManualLogSource Log; public ConfigFile Config; public HarmonyLib.Harmony Harmony; public KeyRegistry Keys; public ModNet Net; public string Folder = ""; public void PatchAll() { } public void OnSceneLoaded(Action<Scene> a) { } public void OnUnload(Action a) { } }
  }
  ```
- The stub's project (build with `dotnet build -c Release -o out`, then pass `out/HalfGripTools.dll` to `tools/check.sh`):

  ```xml
  <Project Sdk="Microsoft.NET.Sdk">
    <PropertyGroup><TargetFramework>netstandard2.1</TargetFramework><AssemblyName>HalfGripTools</AssemblyName><NoWarn>CS0067</NoWarn></PropertyGroup>
    <ItemGroup>
      <Reference Include="<game>/HalfGrip_Data/Managed/*.dll" Exclude="<game>/HalfGrip_Data/Managed/mscorlib.dll;<game>/HalfGrip_Data/Managed/netstandard.dll;<game>/HalfGrip_Data/Managed/System*.dll" />
      <Reference Include="<game>/BepInEx/core/BepInEx.dll" /><Reference Include="<game>/BepInEx/core/0Harmony.dll" />
    </ItemGroup>
  </Project>
  ```
- The layout takes about 7.5 s to build in trafficview; a whole trafficview run with the day about 15 s; the whole meshcheck about
  1 min 45 s. A loop over many seeds outlasts a foreground command's 2 minutes: run it in the background and write each seed's report
  to a file in the scratchpad.
- `convert` (ImageMagick) turns the PPM pictures into PNG.
- The game's decompile (0.419) was put in the scratchpad with `ilspycmd`; the Tokyo scene dump is in HalfGripRefs
  (`scenedumps/0.419/Tokyo`): the `Player` root holds `CarInterior` (INTERIOR layer), `PlayerModel` (the car) and the cameras.

## Working with the owner

- Ask for anything you need (access, files, a decision) at once, plainly. Do not guess, and do not quietly work around a refusal.
- Verify before claiming: say what you read yourself and what came from notes.
- Plain words about what the player sees.
- Show pictures you have looked at yourself.
