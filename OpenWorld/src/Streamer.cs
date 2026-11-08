using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace HalfGripOpenWorld
{
	/// <summary>
	/// Keeps the ground loaded around the player car: square tiles of TileSize units (about 75 m), each a mesh with a collider, built from
	/// <see cref="WorldGen.Height"/>, with the road surfaces that fall in the tile as a second mesh (<see cref="RoadMesh"/>). Both are
	/// worked out on worker threads (pure arithmetic, no Unity calls); the meshes and colliders are made on the main thread, a few tiles
	/// a frame, nearest first. Tiles beyond the radius are destroyed.
	///
	/// It also runs the floating origin once a frame (<see cref="Origin.Follow"/>), the debug teleport and the debug overlay.
	/// </summary>
	internal sealed class Streamer : MonoBehaviour
	{
		/// <summary>Side of one tile in game units (about 75 m). The origin shifts by whole tiles.</summary>
		internal const float TileSize = 250f;

		/// <summary>Quads along a tile's side: 5 units apart, fine enough for the car on ground whose smallest bumps are 70 units across.</summary>
		private const int Quads = 50;

		/// <summary>Tiles whose heights are being worked out at once.</summary>
		private const int Workers = 4;

		/// <summary>Finished tiles turned into meshes per frame (a mesh and its collider take a few milliseconds).</summary>
		private const int BuildsPerFrame = 2;

		/// <summary>Grip and rolling resistance of the ground, 1 being tarmac: loose grass and dirt.</summary>
		private const float GroundGrip = 0.85f;
		private const float GroundDrag = 1.6f;

		internal static Streamer Instance;

		/// <summary>Pixels along a side of a tile's land use (and its map picture): about 4 units each, the game's own chunky pixel-art scale.</summary>
		internal const int Pixels = 64;

		/// <summary>Grip and rolling resistance of the farm tracks and footpaths (loose dirt and gravel), as on the Highway mod's dirt roads.</summary>
		internal const float TrackGrip = 0.8f;
		internal const float TrackDrag = 1.6f;

		/// <summary>Grip and rolling resistance of the gravel verges: a little less than tarmac's.</summary>
		private const float VergeGrip = 0.9f;
		private const float VergeDrag = 1.3f;

		/// <summary>The circuit's gravel traps: they slow a car that runs wide, as real ones do.</summary>
		private const float GravelGrip = 0.55f;
		private const float GravelDrag = 3.5f;

		private CarControllerNew player;
		private Material material;
		private Material[] roadMaterials;
		private Material vergeMaterial;
		private Material sidewalkMaterial;
		private Material[] trackMaterials;
		private Material waterMaterial;
		private Material[] buildingMaterials;
		private Material[] circuitMaterials;
		private Material gravelMaterial;
		private Foliage foliage;
		private int layer;
		private int roadLayer;
		private MaterialPropertyBlock block;
		private float underwater;

		private readonly Dictionary<long, GameObject> tiles = new Dictionary<long, GameObject>();

		/// <summary>Each loaded tile's trees, for their trunk colliders, which only the tiles round the car get (<see cref="Trunks(int, int)"/>).</summary>
		private readonly Dictionary<long, Plant[]> treesOf = new Dictionary<long, Plant[]>();
		/// <summary>The trunk colliders' object of each tile that has them (a child of the tile, destroyed with it).</summary>
		private readonly Dictionary<long, GameObject> trunks = new Dictionary<long, GameObject>();

		/// <summary>Tiles this many round the car's (in each direction) have trunk colliders: the car is always a whole tile from one without.</summary>
		private const int TrunkReach = 1;
		private readonly HashSet<long> working = new HashSet<long>();
		private readonly ConcurrentQueue<Heights> done = new ConcurrentQueue<Heights>();
		private readonly List<long> wanted = new List<long>();

		/// <summary>The map picture of each loaded tile, for <see cref="MapView"/>: added and dropped with the tile.</summary>
		private readonly Dictionary<long, Color32[]> maps = new Dictionary<long, Color32[]>();

		private float buildMs;
		private int built;

		/// <summary>One tile's heights, with a one-sample border all round so the normals match across tile edges.</summary>
		private sealed class Heights
		{
			internal int Tx;
			internal int Tz;
			internal float[] H;
			internal Color32[] Ground;
			/// <summary>The map picture, for <see cref="MapView"/>.</summary>
			internal Color32[] Map;
			internal Buildings.Data Buildings;
			internal Plant[] Trees;
			internal Plant[] Grass;
			internal RoadMeshData Road;
			internal RoadMeshData Verges;
			internal RoadMeshData Sidewalks;
			internal RoadMeshData Tracks;
			internal RoadMeshData Water;
			internal RoadMeshData CircuitTrack;
			internal RoadMeshData Gravel;
			internal string Error;
		}

		internal void Setup(CarControllerNew car, Material ground, int groundLayer, Material[] roads, Material verges, Material sidewalks, Material[] tracks, Material water, int roadsLayer, Material[] buildings, Material[] circuit, Material gravel, Foliage plants)
		{
			circuitMaterials = circuit;
			gravelMaterial = gravel;
			sidewalkMaterial = sidewalks;
			trackMaterials = tracks;
			buildingMaterials = buildings;
			foliage = plants;
			Instance = this;
			player = car;
			material = ground;
			layer = groundLayer;
			roadMaterials = roads;
			vergeMaterial = verges;
			waterMaterial = water;
			roadLayer = roadsLayer;
			block = new MaterialPropertyBlock();
		}

		internal int Loaded => tiles.Count;

		/// <summary>The far view, which leaves out the loaded tiles; null if there is none.</summary>
		internal FarView Far;

		/// <summary>True if tile (tx, tz) is built and in the scene.</summary>
		internal bool IsLoaded(int tx, int tz)
		{
			return tiles.ContainsKey(Key(tx, tz));
		}

		/// <summary>
		/// True when every tile in reach of the car is built and none is being worked out or waiting to be made: after a teleport the
		/// ring round the new place is made a few tiles a frame, each a long frame, and this is when that is over (the events' grid
		/// waits for it before syncing the clock).
		/// </summary>
		internal bool Settled
		{
			get
			{
				if (player == null || working.Count > 0 || !done.IsEmpty)
				{
					return false;
				}
				Origin.World(player.transform.position, out double x, out double z);
				int cx = TileOf(x), cz = TileOf(z), radius = Mod.TileRadius.Value;
				float reach = (radius + 0.5f) * (radius + 0.5f);
				for (int dz = -radius; dz <= radius; dz++)
				{
					for (int dx = -radius; dx <= radius; dx++)
					{
						if (dx * dx + dz * dz <= reach && InWorld(cx + dx, cz + dz) && !tiles.ContainsKey(Key(cx + dx, cz + dz)))
						{
							return false;
						}
					}
				}
				return true;
			}
		}

		/// <summary>Builds the tiles within <paramref name="radius"/> tiles of world (x, z) now, on this thread (the spawn, a teleport).</summary>
		internal void BuildAround(double x, double z, int radius)
		{
			int cx = TileOf(x);
			int cz = TileOf(z);
			for (int dz = -radius; dz <= radius; dz++)
			{
				for (int dx = -radius; dx <= radius; dx++)
				{
					int tx = cx + dx;
					int tz = cz + dz;
					long key = Key(tx, tz);
					if (!tiles.ContainsKey(key) && InWorld(tx, tz))
					{
						Make(Compute(tx, tz));
					}
				}
			}
		}

		private void Update()
		{
			if (player == null)
			{
				return;
			}
			if (Mod.Teleport.Value && Mod.TeleportKey != null && Mod.TeleportKey.Down)
			{
				bool back = GameInput.GetKey(KeyCode.LeftShift) || GameInput.GetKey(KeyCode.RightShift);
				TeleportNext(back ? -1 : 1);
			}
			// An open garage holds god mode itself and leaves it on when it closes if it was on as it opened: then this lets go after.
			if (teleportGodUntil >= 0f && Time.time >= teleportGodUntil && (Garage.Instance == null || Garage.Instance.OpenAt == null))
			{
				teleportGodUntil = -1f;
				CarLink.godMode = false;
			}
			// A car that has gone into a river, a lake or the sea is put back on dry land after a moment (nothing here floats): see Recover.
			Origin.World(player.transform.position, out double wx, out double wz);
			if (Rivers.Water(wx, wz, out double level) && player.transform.position.y < level + Network.Lift - 1.0)
			{
				underwater += Time.deltaTime;
				if (underwater > 1.5f)
				{
					underwater = 0f;
					Mod.Info("In the water: " + Recover(wx, wz, player.transform.forward));
					TextController.EnqueueRandomText(1f, "Reset.");
				}
			}
			else
			{
				underwater = 0f;
			}
		}

		/// <summary>A reset puts the car on the nearest paved road if one is this close (a tile: about 75 m, 18 car lengths).</summary>
		private const double RoadReach = 250.0;

		/// <summary>Further from a road than <see cref="RoadReach"/>, a reset looks this far round the car for a clear spot.</summary>
		private const double OpenReach = 60.0;

		/// <summary>The rings the clear spot is looked for on are this far apart (about half a car's length).</summary>
		private const double OpenStep = 6.0;

		/// <summary>Half a car's footprint, along and across, with room to spare: the car is about 14 long and 5.4 wide.</summary>
		private const double SpotAlong = 9.0;
		private const double SpotAcross = 4.5;

		/// <summary>The most the ground may rise across the footprint (a 1 in 6 slope along the car): steeper, the car would roll off.</summary>
		private const double SpotRise = 3.0;

		/// <summary>Anything solid more than this above the ground in the footprint (a wall, a roof, a car) blocks it; lot paving and kerbs do not.</summary>
		private const float SpotHeadroom = 2.0f;

		private readonly RaycastHit[] spotHits = new RaycastHit[16];

		/// <summary>
		/// Puts the car back on its wheels somewhere it can drive off from, after a reset or a dunking, near world (x, z), facing as near
		/// <paramref name="heading"/> as the place allows. On an event's grid or in its race, see below. Otherwise: where it is if that is
		/// a road, track, course or car park with room; on the nearest paved road if there is one within <see cref="RoadReach"/>; else
		/// the nearest clear spot round it (dry, flat enough, no tree, building or car on it), which is where it is if that is clear; else
		/// where it is: never to a road far off, which in the woods would throw the driver out of where they chose to drive (only out of
		/// the water, with no dry land near, does it go to the nearest road however far). Returns what it chose, for the log.
		/// </summary>
		internal string Recover(double x, double z, Vector3 heading)
		{
			heading.y = 0f;
			if (heading.sqrMagnitude < 1e-4f)
			{
				heading = Vector3.forward;
			}
			heading.Normalize();
			double hx = heading.x, hz = heading.z;
			BuildAround(x, z, 1);
			bool driveable = Driveable(x, z);
			// On the grid or in an event's race the car stays on the course and off the other roads (no short cuts, and a move off the
			// grid is out of the race): where it is if that is driven on, else the nearest clear spot, else where it is unless that is
			// in the water (then on to the nearest road, below, or it would be dunked again and again).
			if (EventRace.Racing)
			{
				if (driveable)
				{
					Place(x, z, Quaternion.LookRotation(heading, Vector3.up));
					return "in a race, in place";
				}
				if (OpenSpot(x, z, hx, hz, out double ex, out double ez))
				{
					Place(ex, ez, Quaternion.LookRotation(heading, Vector3.up));
					return "in a race, clear spot " + Math.Sqrt((ex - x) * (ex - x) + (ez - z) * (ez - z)).ToString("0") + " away";
				}
				if (!Wet(x, z))
				{
					Place(x, z, Quaternion.LookRotation(heading, Vector3.up));
					return "in a race, in place";
				}
			}
			// Already on something driven on (a road, a farm track, the circuit, the rally stage, a car park) with room: stay there.
			// The circuit and the rally stage are not paved roads of the network, so the nearest road would carry the car off them.
			if (driveable && Clear(x, z, hx, hz))
			{
				Place(x, z, Quaternion.LookRotation(heading, Vector3.up));
				return "in place, on a road";
			}
			bool road = Network.NearestRoad(x, z, TileSize, out double rx, out double rz, out double dx, out double dz, out Network.Road found);
			double away = road ? Math.Sqrt((rx - x) * (rx - x) + (rz - z) * (rz - z)) : double.MaxValue;
			if (road && away <= RoadReach)
			{
				OnRoad(rx, rz, dx, dz, found, hx, hz);
				return "road " + away.ToString("0") + " away";
			}
			if (OpenSpot(x, z, hx, hz, out double ox, out double oz))
			{
				BuildAround(ox, oz, 1);
				Place(ox, oz, Quaternion.LookRotation(heading, Vector3.up));
				double moved = Math.Sqrt((ox - x) * (ox - x) + (oz - z) * (oz - z));
				return moved < 0.5 ? "in place" : "clear spot " + moved.ToString("0") + " away";
			}
			// Only out of the water does a reset go to a road far off: in the woods the driver wants to stay in the woods.
			if (road && Wet(x, z))
			{
				OnRoad(rx, rz, dx, dz, found, hx, hz);
				return "in the water, no dry spot near, road " + away.ToString("0") + " away";
			}
			Place(x, z, Quaternion.LookRotation(heading, Vector3.up));
			return "nothing clear near, in place";
		}

		/// <summary>
		/// Puts the car on a road at (rx, rz), whose direction there is (dx, dz): turned to whichever way along it is nearer the way the
		/// car was facing, in the right-hand lane for that way (traffic keeps right; on the trunk road the carriageway's middle, not the
		/// median), and a little along the road if another car is on the spot.
		/// </summary>
		private void OnRoad(double rx, double rz, double dx, double dz, Network.Road road, double hx, double hz)
		{
			if (dx * hx + dz * hz < 0.0)
			{
				dx = -dx;
				dz = -dz;
			}
			// Right of the way the car faces is (dz, -dx); a lane is about 11 wide. Lanes and the hill climb are a single track.
			double keep = road == null ? 0.0 : road.Kind == RoadKind.Trunk ? Courses.Carriageway : road.Half >= 10.0 ? 4.0 : 0.0;
			double x = rx + dz * keep, z = rz - dx * keep;
			Quaternion rotation = Quaternion.LookRotation(new Vector3((float)dx, 0f, (float)dz), Vector3.up);
			double[] along = { 0.0, 16.0, -16.0, 32.0, -32.0 };
			foreach (double a in along)
			{
				double px = x + dx * a, pz = z + dz * a;
				if (!Blocked(px, pz, WorldGen.Height(px, pz)))
				{
					BuildAround(px, pz, 1);
					Place(px, pz, rotation);
					return;
				}
			}
			Place(x, z, rotation);
		}

		/// <summary>
		/// The nearest clear spot to world (x, z), for a car facing (hx, hz): where it is first, then on rings <see cref="OpenStep"/>
		/// apart out to <see cref="OpenReach"/>, each starting ahead of the car. Only the loaded tiles count (their trees and colliders
		/// are what is checked), which the car's own tile and those round it always are.
		/// </summary>
		private bool OpenSpot(double x, double z, double hx, double hz, out double ox, out double oz)
		{
			// Thick woods may have no gap a whole footprint wide: then one only the car's width clear of trunks (it can drive out of it).
			return OpenSpot(x, z, hx, hz, SpotAlong, out ox, out oz) || OpenSpot(x, z, hx, hz, SpotAcross, out ox, out oz);
		}

		private bool OpenSpot(double x, double z, double hx, double hz, double trees, out double ox, out double oz)
		{
			double start = Math.Atan2(hz, hx);
			for (double r = 0.0; r <= OpenReach; r += OpenStep)
			{
				int n = r == 0.0 ? 1 : Math.Max(8, (int)Math.Ceiling(2.0 * Math.PI * r / OpenStep));
				for (int k = 0; k < n; k++)
				{
					// Ahead, then alternately to either side of ahead, round to behind.
					int side = (k + 1) / 2 * (k % 2 == 0 ? 1 : -1);
					double angle = start + side * 2.0 * Math.PI / n;
					ox = x + Math.Cos(angle) * r;
					oz = z + Math.Sin(angle) * r;
					WorldGen.ClampInside(ref ox, ref oz, 50.0);
					if (Clear(ox, oz, hx, hz, trees))
					{
						return true;
					}
				}
			}
			ox = oz = 0.0;
			return false;
		}

		/// <summary>True if a car facing (hx, hz) can stand at world (x, z): loaded ground, dry, flat enough, no tree trunk near, nothing standing on it.</summary>
		private bool Clear(double x, double z, double hx, double hz, double trees = SpotAlong)
		{
			if (!IsLoaded(TileOf(x), TileOf(z)))
			{
				return false;
			}
			if (Wet(x, z))
			{
				return false;
			}
			// The centre and the footprint's four corners: (along, across), across being to the right, (hz, -hx).
			double lo = double.MaxValue, hi = double.MinValue;
			for (int i = 0; i < 5; i++)
			{
				double a = i == 0 ? 0.0 : (i < 3 ? SpotAlong : -SpotAlong);
				double b = i == 0 ? 0.0 : (i % 2 == 1 ? SpotAcross : -SpotAcross);
				double g = WorldGen.Height(x + hx * a + hz * b, z + hz * a - hx * b);
				lo = Math.Min(lo, g);
				hi = Math.Max(hi, g);
			}
			if (hi - lo > SpotRise || TreeNear(x, z, trees))
			{
				return false;
			}
			for (int i = 0; i < 5; i++)
			{
				double a = i == 0 ? 0.0 : (i < 3 ? SpotAlong : -SpotAlong);
				double b = i == 0 ? 0.0 : (i % 2 == 1 ? SpotAcross : -SpotAcross);
				double px = x + hx * a + hz * b, pz = z + hz * a - hx * b;
				if (Blocked(px, pz, WorldGen.Height(px, pz)))
				{
					return false;
				}
			}
			return true;
		}

		/// <summary>True if world (x, z) is under a river's, a lake's or the sea's water.</summary>
		private static bool Wet(double x, double z)
		{
			return Rivers.Water(x, z, out double level) && WorldGen.Height(x, z) - Network.Lift < level + 0.5;
		}

		/// <summary>The tile parts a car drives on (see <see cref="Make"/>): a reset on one leaves the car there.</summary>
		private static readonly HashSet<string> driven = new HashSet<string> { "Roads", "Sidewalks", "Tracks", "Circuit", "Gravel", "Buildings" };

		/// <summary>True if one of the tiles' driven surfaces is at ground level at world (x, z) (a bridge's deck high over the ground is not: the car is moved off it).</summary>
		private bool Driveable(double x, double z)
		{
			const float above = 4f;
			double ground = WorldGen.Height(x, z);
			Vector3 top = Origin.Local(x, (float)ground + above, z);
			int count = Physics.RaycastNonAlloc(top, Vector3.down, spotHits, above + 3f, ~0, QueryTriggerInteraction.Ignore);
			for (int i = 0; i < count; i++)
			{
				Collider c = spotHits[i].collider;
				if (c != null && driven.Contains(c.gameObject.name) && c.transform.parent != null && c.transform.parent.parent == transform)
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>True if a loaded tile's tree trunk stands within <paramref name="reach"/> of world (x, z) (from the trees' data, not their colliders, which only the tiles nearest the car have).</summary>
		private bool TreeNear(double x, double z, double reach)
		{
			int cx = TileOf(x), cz = TileOf(z);
			for (int j = cz - 1; j <= cz + 1; j++)
			{
				for (int i = cx - 1; i <= cx + 1; i++)
				{
					if (!treesOf.TryGetValue(Key(i, j), out Plant[] trees))
					{
						continue;
					}
					double bx = i * (double)TileSize - x, bz = j * (double)TileSize - z;
					foreach (Plant p in trees)
					{
						double ox = bx + p.Pos.x, oz = bz + p.Pos.z;
						double r = reach + 0.9 + 0.4 * p.Scale;
						if (ox * ox + oz * oz < r * r)
						{
							return true;
						}
					}
				}
			}
			return false;
		}

		/// <summary>
		/// True if something solid (a building, a wall, a bridge deck, another car) is more than <see cref="SpotHeadroom"/> above the
		/// ground <paramref name="ground"/> at world (x, z): a ray down from well above, stopping short of the ground, hits it. The car's own colliders do not count.
		/// </summary>
		private bool Blocked(double x, double z, double ground)
		{
			const float above = 60f;
			Vector3 top = Origin.Local(x, (float)ground + above, z);
			int count = Physics.RaycastNonAlloc(top, Vector3.down, spotHits, above - SpotHeadroom, ~0, QueryTriggerInteraction.Ignore);
			for (int i = 0; i < count; i++)
			{
				Collider c = spotHits[i].collider;
				if (c == null || (player != null && (c.attachedRigidbody == player.rb || c.transform.IsChildOf(player.transform))))
				{
					continue;
				}
				return true;
			}
			return false;
		}

		private void LateUpdate()
		{
			if (player == null)
			{
				return;
			}
			try
			{
				Origin.Follow(player.transform.position, Mod.ShiftDistance.Value, TileSize);
			}
			catch (Exception e)
			{
				Mod.Log.LogError("Origin shift failed: " + e);
			}
			try
			{
				Stream();
			}
			catch (Exception e)
			{
				Mod.Log.LogError("Tile streaming failed: " + e);
			}
			if (Far != null)
			{
				try
				{
					// After the tiles came and went this frame, so the far view's holes follow them with no frame between.
					Origin.World(player.transform.position, out double x, out double z);
					Far.Follow(x, z, this);
				}
				catch (Exception e)
				{
					Mod.Log.LogWarning("Far view failed: " + e);
				}
			}
		}

		private void Stream()
		{
			Origin.World(player.transform.position, out double x, out double z);
			int cx = TileOf(x);
			int cz = TileOf(z);
			int radius = Mod.TileRadius.Value;
			foliage?.Follow(cx, cz);

			// Far tiles go (with some slack, so a car driving along a tile edge does not make and drop the same row over and over).
			float keep = (radius + 1.5f) * (radius + 1.5f);
			List<long> drop = null;
			foreach (KeyValuePair<long, GameObject> pair in tiles)
			{
				Unkey(pair.Key, out int tx, out int tz);
				if ((tx - cx) * (tx - cx) + (tz - cz) * (tz - cz) > keep)
				{
					(drop ??= new List<long>()).Add(pair.Key);
				}
			}
			if (drop != null)
			{
				foreach (long key in drop)
				{
					Destroy(tiles[key]);
					tiles.Remove(key);
					if (maps.Remove(key))
					{
						MapView.Changed();
					}
					foliage?.Remove(key);
					treesOf.Remove(key);
					trunks.Remove(key);
				}
			}

			// The tile under the car cannot wait (if a worker has it too, its result is dropped when it comes).
			if (InWorld(cx, cz) && !tiles.ContainsKey(Key(cx, cz)))
			{
				Make(Compute(cx, cz));
			}

			// Finished heights become tiles, a few a frame; one that is no longer wanted is dropped.
			float reach = (radius + 0.5f) * (radius + 0.5f);
			for (int i = 0; i < BuildsPerFrame && done.TryDequeue(out Heights h); i++)
			{
				working.Remove(Key(h.Tx, h.Tz));
				if (h.Error != null)
				{
					Mod.Log.LogWarning("Tile " + h.Tx + "," + h.Tz + " could not be worked out: " + h.Error);
					continue;
				}
				if ((h.Tx - cx) * (h.Tx - cx) + (h.Tz - cz) * (h.Tz - cz) <= keep && !tiles.ContainsKey(Key(h.Tx, h.Tz)))
				{
					Make(h);
				}
			}

			Trunks(cx, cz);

			// New work, nearest first.
			if (working.Count >= Workers)
			{
				return;
			}
			wanted.Clear();
			for (int dz = -radius; dz <= radius; dz++)
			{
				for (int dx = -radius; dx <= radius; dx++)
				{
					int tx = cx + dx;
					int tz = cz + dz;
					long key = Key(tx, tz);
					if (dx * dx + dz * dz <= reach && InWorld(tx, tz) && !tiles.ContainsKey(key) && !working.Contains(key))
					{
						wanted.Add(key);
					}
				}
			}
			wanted.Sort((a, b) => Distance(a, cx, cz).CompareTo(Distance(b, cx, cz)));
			for (int i = 0; i < wanted.Count && working.Count < Workers; i++)
			{
				long key = wanted[i];
				Unkey(key, out int tx, out int tz);
				working.Add(key);
				Task.Run(() =>
				{
					Heights result;
					try
					{
						result = Compute(tx, tz);
					}
					catch (Exception e)
					{
						result = new Heights { Tx = tx, Tz = tz, Error = e.Message };
					}
					done.Enqueue(result);
				});
			}
		}

		/// <summary>The heights of one tile: (Quads + 3) squared samples, from one before its first edge to one past its last.</summary>
		private static Heights Compute(int tx, int tz)
		{
			int side = Quads + 3;
			double step = TileSize / Quads;
			double x0 = tx * (double)TileSize - step;
			double z0 = tz * (double)TileSize - step;
			float[] h = new float[side * side];
			for (int j = 0; j < side; j++)
			{
				for (int i = 0; i < side; i++)
				{
					h[j * side + i] = (float)WorldGen.Height(x0 + i * step, z0 + j * step);
				}
			}
			Color32[] colours = Colours(tx, tz, h, out Cover[] covers, out Color32[] map, out float[] ground);
			MapPicture(tx, tz, map, ground);
			Buildings.Data buildings = HalfGripOpenWorld.Buildings.Build(tx, tz, TileSize);
			Foliage.Scatter(tx, tz, TileSize, Pixels, covers, Network.Clearance(tx, tz, TileSize, Pixels), h, side, (float)step, buildings.Footprints, out Plant[] trees, out Plant[] grass);
			RoadMeshData circuit = CircuitMesh.Build(tx, tz, TileSize, out RoadMeshData gravel);
			RoadMeshData road = RoadMesh.Build(tx, tz, TileSize, new TerrainGrid(x0, z0, side, h), out RoadMeshData verges, out RoadMeshData sidewalks, out RoadMeshData tracks);
			return new Heights
			{
				Tx = tx,
				Tz = tz,
				H = h,
				Ground = colours,
				Map = map,
				Buildings = buildings,
				Trees = trees,
				Grass = grass,
				Road = road,
				Verges = verges,
				Sidewalks = sidewalks,
				Tracks = tracks,
				Water = WaterMesh.Build(tx, tz, TileSize),
				CircuitTrack = circuit,
				Gravel = gravel,
			};
		}

		/// <summary>
		/// The tile's ground texture: each land-use pixel's colour from <see cref="LandUse"/>, at the pixel's centre, with the height and
		/// slope taken from the tile's own height samples (so the colours follow exactly the ground that is drawn). Nothing is painted
		/// over it (tracks and paths are meshes of their own: at 4 units a pixel they were blocks). The land use is also given in
		/// <paramref name="map"/>, with each pixel's ground height from sea level in <paramref name="ground"/>, to start the tile's map
		/// picture from.
		/// </summary>
		private static Color32[] Colours(int tx, int tz, float[] h, out Cover[] covers, out Color32[] map, out float[] ground)
		{
			covers = new Cover[Pixels * Pixels];
			map = new Color32[Pixels * Pixels];
			ground = new float[Pixels * Pixels];
			int side = Quads + 3;
			float step = TileSize / Quads;
			float pixel = TileSize / Pixels;
			double lift = Network.Lift;
			Color32[] colours = new Color32[Pixels * Pixels];
			for (int pj = 0; pj < Pixels; pj++)
			{
				for (int pi = 0; pi < Pixels; pi++)
				{
					float lx = (pi + 0.5f) * pixel, lz = (pj + 0.5f) * pixel;
					// Sample grid index (the grid starts one step before the tile).
					float gx = lx / step + 1f, gz = lz / step + 1f;
					int i = Math.Min(side - 2, (int)gx), j = Math.Min(side - 2, (int)gz);
					float fx = gx - i, fz = gz - j;
					float a = h[j * side + i], b = h[j * side + i + 1], c = h[(j + 1) * side + i], d = h[(j + 1) * side + i + 1];
					float height = (a + (b - a) * fx) + ((c + (d - c) * fx) - (a + (b - a) * fx)) * fz;
					float sx = ((b - a) + (d - c)) * 0.5f / step, sz = ((c - a) + (d - b)) * 0.5f / step;
					double wx = tx * (double)TileSize + lx, wz = tz * (double)TileSize + lz;
					Cover cover = LandUse.Classify(wx, wz, height - lift, Math.Sqrt(sx * sx + sz * sz), out int crop);
					LandUse.Rgb rgb = LandUse.Colour(cover, crop, wx, wz);
					covers[pj * Pixels + pi] = cover;
					Color32 colour = new Color32(rgb.R, rgb.G, rgb.B, 255);
					map[pj * Pixels + pi] = colour;
					ground[pj * Pixels + pi] = height - (float)lift;
					colours[pj * Pixels + pi] = colour;
				}
			}
			return colours;
		}

		/// <summary>Map colours of water, lots and their buildings, and of the roads by class (<see cref="RoadKind"/>), those of the preview's map (docs/world.png).</summary>
		private static readonly Color32 MapWater = new Color32(46, 82, 128, 255);
		private static readonly Color32 MapLot = new Color32(150, 150, 155, 255);
		private static readonly Color32 MapBuilding = new Color32(95, 90, 100, 255);
		/// <summary>The circuit by its surfaces (<see cref="CircuitSurface"/>): track, kerb, gravel, pit apron; its buildings as the lots'.</summary>
		private static readonly Color32[] MapCircuit =
		{
			new Color32(0, 0, 0, 0),
			new Color32(60, 60, 64, 255),
			new Color32(200, 50, 50, 255),
			new Color32(205, 190, 150, 255),
			new Color32(120, 120, 126, 255),
		};
		private static readonly Color32[] MapRoads =
		{
			new Color32(225, 110, 40, 255),   // trunk
			new Color32(235, 190, 60, 255),   // primary
			new Color32(240, 225, 130, 255),  // secondary
			new Color32(70, 70, 74, 255),     // street
			new Color32(238, 236, 228, 255),  // lane
			new Color32(205, 195, 170, 255),  // drive
			new Color32(150, 110, 70, 255),   // track
			new Color32(196, 180, 150, 255),  // footpath
			new Color32(200, 120, 50, 255),   // rally stage
			new Color32(200, 70, 150, 255),   // hill climb
		};

		/// <summary>
		/// Paints the water and the roads onto a tile's land use (<paramref name="map"/>, from <see cref="Colours"/>), making its map
		/// picture for <see cref="MapView"/>. Only looks, for other mods' maps: nothing in the world or its ground texture depends on it.
		/// Every road is at least a pixel either side of its line, so it is still there when a minimap shrinks the picture; the bigger
		/// classes are painted last, over the smaller ones they meet.
		/// </summary>
		private static void MapPicture(int tx, int tz, Color32[] map, float[] ground)
		{
			double ox = tx * (double)TileSize, oz = tz * (double)TileSize;
			double pixel = TileSize / (double)Pixels;
			for (int k = 0; k < map.Length; k++)
			{
				double x = ox + (k % Pixels + 0.5) * pixel, z = oz + (k / Pixels + 0.5) * pixel;
				if (Rivers.Water(x, z, out double level) && level > ground[k])
				{
					map[k] = MapWater;
				}
				// Car parks, yards and forecourts, and their buildings.
				Town.Lot lot = Town.At(x, z, out bool building);
				if (lot != null)
				{
					map[k] = building ? MapBuilding : MapLot;
				}
				CircuitSurface surface = Circuit.SurfaceAt(x, z);
				if (surface != CircuitSurface.None)
				{
					map[k] = MapCircuit[(int)surface];
				}
				if (Circuit.Building(x, z))
				{
					map[k] = MapBuilding;
				}
			}
			// Pieces are listed by the tile their middle is in, so a road from a neighbouring tile may reach into this one.
			List<long> pieces = new List<long>();
			for (int j = tz - 1; j <= tz + 1; j++)
			{
				for (int i = tx - 1; i <= tx + 1; i++)
				{
					List<long> list = Network.PiecesIn(i, j, TileSize);
					if (list != null)
					{
						pieces.AddRange(list);
					}
				}
			}
			for (int kind = MapRoads.Length - 1; kind >= 0; kind--)
			{
				foreach (long piece in pieces)
				{
					Network.Road road = Network.Roads[(int)(piece >> 32)];
					if ((int)road.Kind != kind)
					{
						continue;
					}
					int k = (int)(uint)piece;
					double reach = Math.Max(road.Half, pixel);
					int p0 = Math.Max(0, (int)Math.Floor((Math.Min(road.X[k], road.X[k + 1]) - reach - ox) / pixel));
					int p1 = Math.Min(Pixels - 1, (int)Math.Floor((Math.Max(road.X[k], road.X[k + 1]) + reach - ox) / pixel));
					int q0 = Math.Max(0, (int)Math.Floor((Math.Min(road.Z[k], road.Z[k + 1]) - reach - oz) / pixel));
					int q1 = Math.Min(Pixels - 1, (int)Math.Floor((Math.Max(road.Z[k], road.Z[k + 1]) + reach - oz) / pixel));
					for (int q = q0; q <= q1; q++)
					{
						for (int p = p0; p <= p1; p++)
						{
							Lines.Project(road.X, road.Z, k, ox + (p + 0.5) * pixel, oz + (q + 0.5) * pixel, out double _, out double d);
							if (d <= reach)
							{
								map[q * Pixels + p] = MapRoads[kind];
							}
						}
					}
				}
			}
			MapGarages(ox, oz, pixel, map);
			MapEvents(ox, oz, pixel, map);
		}

		/// <summary>A garage's mark: a filled disc, a colour nothing else on the map has, in a dark ring.</summary>
		private static readonly Color32 MapGarage = new Color32(40, 220, 255, 255);
		private static readonly Color32 MapGarageRing = new Color32(16, 36, 52, 255);

		/// <summary>
		/// Marks the garages on the map picture over everything else (MiniMap draws these pictures as they are, so a mark here is a mark
		/// on its map). One mark for each group of garages near each other (<see cref="GarageSites.Marks"/>). The disc is 11 pixels
		/// round and the ring 2 more, 26 pixels (about 100 units) across, wider than the trunk road, so it still shows plainly when a
		/// minimap shrinks the picture, at the cost of hiding what is under it.
		/// </summary>
		private static void MapGarages(double ox, double oz, double pixel, Color32[] map)
		{
			const double disc = 11.0, ring = 13.0;
			foreach (GarageSites.Mark mark in GarageSites.Marks)
			{
				double cp = (mark.X - ox) / pixel, cq = (mark.Z - oz) / pixel;
				if (cp < -ring || cq < -ring || cp > Pixels + ring || cq > Pixels + ring)
				{
					continue;
				}
				for (int q = Math.Max(0, (int)Math.Floor(cq - ring)); q <= Math.Min(Pixels - 1, (int)Math.Floor(cq + ring)); q++)
				{
					for (int p = Math.Max(0, (int)Math.Floor(cp - ring)); p <= Math.Min(Pixels - 1, (int)Math.Floor(cp + ring)); p++)
					{
						double dp = p + 0.5 - cp, dq = q + 0.5 - cq, d = Math.Sqrt(dp * dp + dq * dq);
						if (d <= disc)
						{
							map[q * Pixels + p] = MapGarage;
						}
						else if (d <= ring)
						{
							map[q * Pixels + p] = MapGarageRing;
						}
					}
				}
			}
		}

		/// <summary>An event's mark: the dark outline, the events' orange ring, the squares' white and black.</summary>
		private static readonly Color32[] MapMarkColours =
		{
			default,
			new Color32(18, 18, 22, 255),
			new Color32(255, 140, 26, 255),
			new Color32(245, 245, 245, 255),
			new Color32(30, 30, 34, 255),
		};

		/// <summary>
		/// Marks each event's start box on the map picture with a chequered disc (<see cref="Courses.MapMark"/>), over the garages' marks
		/// (a box is often beside a garage). A disc near a tile's edge reaches into the next tile, which draws its own part of it.
		/// </summary>
		private static void MapEvents(double ox, double oz, double pixel, Color32[] map)
		{
			double reach = Courses.MarkReach;
			foreach (Courses.Course c in Courses.All)
			{
				double cp = (c.BoxX - ox) / pixel, cq = (c.BoxZ - oz) / pixel;
				if (cp < -reach || cq < -reach || cp > Pixels + reach || cq > Pixels + reach)
				{
					continue;
				}
				for (int q = Math.Max(0, (int)Math.Floor(cq - reach)); q <= Math.Min(Pixels - 1, (int)Math.Floor(cq + reach)); q++)
				{
					for (int p = Math.Max(0, (int)Math.Floor(cp - reach)); p <= Math.Min(Pixels - 1, (int)Math.Floor(cp + reach)); p++)
					{
						int part = Courses.MapMark(p + 0.5 - cp, q + 0.5 - cq);
						if (part != Courses.MarkNone)
						{
							map[q * Pixels + p] = MapMarkColours[part];
						}
					}
				}
			}
		}

		/// <summary>Copies a loaded tile's map picture into <paramref name="into"/>; false if the tile is not loaded. Main thread only.</summary>
		internal bool TryGetMap(int tx, int tz, Color32[] into)
		{
			if (into == null || into.Length < Pixels * Pixels || !maps.TryGetValue(Key(tx, tz), out Color32[] map))
			{
				return false;
			}
			Array.Copy(map, into, map.Length);
			return true;
		}

		private void Make(Heights data)
		{
			float started = Time.realtimeSinceStartup;
			int side = Quads + 3;
			int n = Quads + 1;
			float step = TileSize / Quads;
			Vector3[] vertices = new Vector3[n * n];
			Vector3[] normals = new Vector3[n * n];
			Vector2[] uv = new Vector2[n * n];
			for (int j = 0; j < n; j++)
			{
				for (int i = 0; i < n; i++)
				{
					int s = (j + 1) * side + (i + 1);
					int v = j * n + i;
					vertices[v] = new Vector3(i * step, data.H[s], j * step);
					normals[v] = new Vector3(data.H[s - 1] - data.H[s + 1], 2f * step, data.H[s - side] - data.H[s + side]).normalized;
					// The tile's own ground texture covers it once.
					uv[v] = new Vector2(i / (float)Quads, j / (float)Quads);
				}
			}
			int[] triangles = new int[Quads * Quads * 6];
			int t = 0;
			for (int j = 0; j < Quads; j++)
			{
				for (int i = 0; i < Quads; i++)
				{
					int a = j * n + i;
					triangles[t++] = a;
					triangles[t++] = a + n;
					triangles[t++] = a + 1;
					triangles[t++] = a + 1;
					triangles[t++] = a + n;
					triangles[t++] = a + n + 1;
				}
			}
			Mesh mesh = new Mesh { name = "OpenWorld Tile " + data.Tx + "," + data.Tz };
			mesh.vertices = vertices;
			mesh.normals = normals;
			mesh.uv = uv;
			mesh.triangles = triangles;
			mesh.RecalculateBounds();

			GameObject go = new GameObject("Tile " + data.Tx + "," + data.Tz) { layer = layer };
			go.transform.SetParent(transform, false);
			go.transform.position = Origin.Local(data.Tx * (double)TileSize, 0f, data.Tz * (double)TileSize);
			Texture2D texture = new Texture2D(Pixels, Pixels, TextureFormat.RGBA32, false)
			{
				filterMode = FilterMode.Point,
				wrapMode = TextureWrapMode.Clamp,
				name = "OpenWorld Ground " + data.Tx + "," + data.Tz,
			};
			texture.SetPixels32(data.Ground);
			texture.Apply(false, true);
			go.AddComponent<MeshFilter>().sharedMesh = mesh;
			MeshRenderer renderer = go.AddComponent<MeshRenderer>();
			renderer.sharedMaterial = material;
			block.Clear();
			block.SetTexture("_BaseMap", texture);
			block.SetTexture("_MainTex", texture);
			renderer.SetPropertyBlock(block);
			go.AddComponent<MeshCollider>().sharedMesh = mesh;
			HandlingModifier handling = go.AddComponent<HandlingModifier>();
			handling.gripModifier = GroundGrip;
			handling.resistanceModifier = GroundDrag;
			handling.background = true;
			TileMesh owned = go.AddComponent<TileMesh>();
			owned.Mesh = mesh;
			owned.Texture = texture;
			Part(go.transform, "Roads", data, data.Road, roadMaterials, true, 1f, 1f);
			Part(go.transform, "Verges", data, data.Verges, new[] { vergeMaterial }, true, VergeGrip, VergeDrag);
			Part(go.transform, "Sidewalks", data, data.Sidewalks, new[] { sidewalkMaterial }, true, 1f, 1f);
			Part(go.transform, "Tracks", data, data.Tracks, trackMaterials, true, TrackGrip, TrackDrag);
			Part(go.transform, "Circuit", data, data.CircuitTrack, circuitMaterials, true, 1f, 1f);
			Part(go.transform, "Gravel", data, data.Gravel, new[] { gravelMaterial }, true, GravelGrip, GravelDrag);
			Part(go.transform, "Water", data, data.Water, new[] { waterMaterial }, false, 1f, 1f);
			Part(go.transform, "Buildings", data, data.Buildings?.Mesh, buildingMaterials, true, 1f, 1f);
			if (data.Trees != null && data.Trees.Length > 0)
			{
				treesOf[Key(data.Tx, data.Tz)] = data.Trees;
			}
			tiles[Key(data.Tx, data.Tz)] = go;
			if (data.Map != null)
			{
				maps[Key(data.Tx, data.Tz)] = data.Map;
				MapView.Changed();
			}
			foliage?.Add(Key(data.Tx, data.Tz), go.transform, data.Tx, data.Tz, data.Trees, data.Grass);

			buildMs += (Time.realtimeSinceStartup - started) * 1000f;
			built++;
			if (built % 50 == 0)
			{
				Mod.Info("Tiles: " + built + " built, " + (buildMs / built).ToString("0.0") + " ms each on the main thread, " + tiles.Count + " loaded");
			}
		}

		/// <summary>
		/// Trunk colliders on the tiles round the car's tile (<see cref="TrunkReach"/>), taken off a tile once it is two further off.
		/// Every loaded tile had them before 0.21.1: up to 36 000 colliders near the rally stage, each tile's hundreds made on the main
		/// thread as it came and all of them moved on every origin shift. The car's own tile gets its colliders at once, the others
		/// one tile a frame.
		/// </summary>
		private void Trunks(int cx, int cz)
		{
			List<long> drop = null;
			foreach (long key in trunks.Keys)
			{
				Unkey(key, out int tx, out int tz);
				if (Math.Max(Math.Abs(tx - cx), Math.Abs(tz - cz)) > TrunkReach + 1)
				{
					(drop ??= new List<long>()).Add(key);
				}
			}
			if (drop != null)
			{
				foreach (long key in drop)
				{
					if (trunks[key] != null)
					{
						Destroy(trunks[key]);
					}
					trunks.Remove(key);
				}
			}
			long own = Key(cx, cz);
			if (!trunks.ContainsKey(own))
			{
				Trunks(own);
			}
			for (int dz = -TrunkReach; dz <= TrunkReach; dz++)
			{
				for (int dx = -TrunkReach; dx <= TrunkReach; dx++)
				{
					long key = Key(cx + dx, cz + dz);
					if (!trunks.ContainsKey(key) && treesOf.ContainsKey(key))
					{
						Trunks(key);
						return;
					}
				}
			}
		}

		/// <summary>A collider round each tree's trunk on one loaded tile (the trees themselves are drawn instanced, with no objects of their own).</summary>
		private void Trunks(long key)
		{
			if (!tiles.TryGetValue(key, out GameObject tile) || !treesOf.TryGetValue(key, out Plant[] trees))
			{
				return;
			}
			GameObject go = new GameObject("Trunks") { layer = layer };
			go.transform.SetParent(tile.transform, false);
			foreach (Plant p in trees)
			{
				float tall = Foliage.Tall(p);
				CapsuleCollider c = go.AddComponent<CapsuleCollider>();
				c.center = p.Pos + Vector3.up * (tall * 0.3f);
				c.radius = 0.9f + 0.4f * p.Scale;
				c.height = tall * 0.6f;
			}
			trunks[key] = go;
		}

		/// <summary>
		/// One of the tile's parts (its roads, verges or water): a mesh with a submesh per material, a collider if it is driven on,
		/// and its own grip if that is not tarmac's.
		/// </summary>
		private void Part(Transform tile, string name, Heights data, RoadMeshData r, Material[] materials, bool solid, float grip, float drag)
		{
			if (r == null)
			{
				return;
			}
			Mesh mesh = new Mesh { name = "OpenWorld " + name + " " + data.Tx + "," + data.Tz };
			if (r.Vertices.Length > 65000)
			{
				mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
			}
			mesh.vertices = r.Vertices;
			mesh.normals = r.Normals;
			mesh.uv = r.Uv;
			mesh.subMeshCount = r.Triangles.Length;
			for (int k = 0; k < r.Triangles.Length; k++)
			{
				mesh.SetTriangles(r.Triangles[k], k);
			}
			mesh.RecalculateBounds();
			GameObject go = new GameObject(name) { layer = roadLayer };
			go.transform.SetParent(tile, false);
			go.AddComponent<MeshFilter>().sharedMesh = mesh;
			go.AddComponent<MeshRenderer>().sharedMaterials = materials;
			if (solid)
			{
				go.AddComponent<MeshCollider>().sharedMesh = mesh;
				if (grip != 1f || drag != 1f)
				{
					HandlingModifier handling = go.AddComponent<HandlingModifier>();
					handling.gripModifier = grip;
					handling.resistanceModifier = drag;
					handling.background = true;
				}
			}
			go.AddComponent<TileMesh>().Mesh = mesh;
		}

		/// <summary>The key place the teleport went to last (-1: none yet, so the first press goes to the first).</summary>
		private int destination = -1;
		private string destinationName;
		private float destinationShown;

		/// <summary>
		/// Moves the car to the next key place (debug; <paramref name="step"/> -1 for the one before), on the road there and facing along
		/// it, building the ground there first so it lands on something.
		/// </summary>
		private void TeleportNext(int step)
		{
			List<Destinations.Place> places = Destinations.All;
			if (places.Count == 0)
			{
				return;
			}
			destination = ((destination < 0 && step < 0 ? 0 : destination + step) % places.Count + places.Count) % places.Count;
			Destinations.Place place = places[destination];
			double len = Math.Sqrt(place.Dx * place.Dx + place.Dz * place.Dz);
			double ux = len > 1e-6 ? place.Dx / len : 0.0, uz = len > 1e-6 ? place.Dz / len : 1.0;
			// Another player may be standing on the place (the spawn, a grid): the first spot along the road, then beside it, with no car.
			double x = place.X, z = place.Z;
			string offset = "";
			for (int i = 0; i < TeleportOffsets.Length; i++)
			{
				double along = TeleportOffsets[i].along, across = TeleportOffsets[i].across;
				double tx = place.X + ux * along - uz * across, tz = place.Z + uz * along + ux * across;
				WorldGen.ClampInside(ref tx, ref tz, 50.0);
				if (!GameMode.CarNear(Origin.Local(tx, 0f, tz)))
				{
					x = tx;
					z = tz;
					offset = i == 0 ? "" : ", another car there so " + along.ToString("0") + " along and " + across.ToString("0") + " across";
					break;
				}
				if (i == TeleportOffsets.Length - 1)
				{
					offset = ", cars on every spot tried so on the place itself";
				}
			}
			BuildAround(x, z, 1);
			Place(x, z, Quaternion.LookRotation(new Vector3((float)ux, 0f, (float)uz), Vector3.up));
			// A moment of god mode, so landing against a car or a wall does not hurt; released only if this set it (as the garage does).
			if (!CarLink.godMode)
			{
				CarLink.godMode = true;
				teleportGodUntil = Time.time + TeleportGod;
			}
			else if (teleportGodUntil >= 0f)
			{
				teleportGodUntil = Time.time + TeleportGod;
			}
			destinationName = (destination + 1) + "/" + places.Count + "  " + place.Name;
			destinationShown = Time.realtimeSinceStartup;
			Mod.Log.LogInfo("Teleported to " + place.Name + ": world (" + x.ToString("0") + ", " + z.ToString("0") + "), ground " + WorldGen.Height(x, z).ToString("0") + " units" + offset);
		}

		/// <summary>The spots a teleport tries in turn, along the road and across it from the place, in units (a car is about 14 long).</summary>
		private static readonly (double along, double across)[] TeleportOffsets = { (0, 0), (30, 0), (-30, 0), (60, 0), (-60, 0), (0, 15), (0, -15) };

		/// <summary>Seconds of god mode after a teleport.</summary>
		private const float TeleportGod = 2f;

		/// <summary>Game time the teleport's god mode ends; below zero when the teleport did not set it.</summary>
		private float teleportGodUntil = -1f;

		/// <summary>Puts the player car upright on the ground at world (x, z), stopped. Used by the teleport and by a reset.</summary>
		internal void Place(double x, double z, Quaternion rotation)
		{
			Vector3 at = Origin.Local(x, (float)WorldGen.Height(x, z) + 2f, z);
			player.transform.SetPositionAndRotation(at, rotation);
			player.noAirtimeUntil = Time.time + 2f;
			if (player.rb != null)
			{
				player.rb.position = at;
				player.rb.rotation = rotation;
				player.rb.linearVelocity = Vector3.zero;
				player.rb.angularVelocity = Vector3.zero;
			}
			Physics.SyncTransforms();
		}

		private void OnGUI()
		{
			// The place just teleported to, for a few seconds, mid-screen at the top.
			if (destinationName != null && Time.realtimeSinceStartup - destinationShown < 4f)
			{
				GUI.Label(new Rect(Screen.width * 0.5f - 300f, 40f, 600f, 30f), destinationName, new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 22 });
			}
			if (!Mod.ShowCoords.Value || player == null)
			{
				return;
			}
			Origin.World(player.transform.position, out double x, out double z);
			string text = "world " + (x / 1000.0).ToString("0.000") + ", " + (z / 1000.0).ToString("0.000") + " thousand units   height " + player.transform.position.y.ToString("0") + " m\n"
				+ "scene " + player.transform.position.x.ToString("0") + ", " + player.transform.position.z.ToString("0") + "   origin " + Origin.X.ToString("0") + ", " + Origin.Z.ToString("0") + "   shifts " + Origin.Shifts + "\n"
				+ "tiles " + tiles.Count + " loaded, " + working.Count + " in work"
				+ (Sky.Status != null ? "\n" + Sky.Status : "")
				+ (FarView.Showing && FarView.Status != null ? "\n" + FarView.Status : "");
			GUI.Label(new Rect(12f, 12f, 900f, 110f), text);
		}

		private void OnDestroy()
		{
			if (Instance == this)
			{
				Instance = null;
			}
			tiles.Clear();
			if (maps.Count > 0)
			{
				maps.Clear();
				MapView.Changed();
			}
		}

		private static bool InWorld(int tx, int tz)
		{
			// A tile is built if any of it is inside the world square, so the edge wall is whole.
			double min = -WorldGen.Half - TileSize;
			return tx * (double)TileSize > min && tz * (double)TileSize > min && tx * (double)TileSize < WorldGen.Half && tz * (double)TileSize < WorldGen.Half;
		}

		private static int TileOf(double v)
		{
			return (int)Math.Floor(v / TileSize);
		}

		private static long Key(int tx, int tz)
		{
			return ((long)tx << 32) | (uint)tz;
		}

		private static void Unkey(long key, out int tx, out int tz)
		{
			tx = (int)(key >> 32);
			tz = (int)(uint)key;
		}

		private static int Distance(long key, int cx, int cz)
		{
			Unkey(key, out int tx, out int tz);
			return (tx - cx) * (tx - cx) + (tz - cz) * (tz - cz);
		}
	}

	/// <summary>Destroys a tile's mesh and texture with the tile (ones made in code are not freed with their object).</summary>
	internal sealed class TileMesh : MonoBehaviour
	{
		internal Mesh Mesh;
		internal Texture2D Texture;

		private void OnDestroy()
		{
			if (Mesh != null)
			{
				Destroy(Mesh);
			}
			if (Texture != null)
			{
				Destroy(Texture);
			}
		}
	}
}
