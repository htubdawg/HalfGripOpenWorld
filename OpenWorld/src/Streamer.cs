using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace HalfGripOpenWorld
{
	/// <summary>
	/// Keeps the ground loaded around the player car: square tiles of TileSize metres, each a mesh with a collider, built from
	/// <see cref="WorldGen.Height"/>, with the road surfaces that fall in the tile as a second mesh (<see cref="RoadMesh"/>). Both are
	/// worked out on worker threads (pure arithmetic, no Unity calls); the meshes and colliders are made on the main thread, a few tiles
	/// a frame, nearest first. Tiles beyond the radius are destroyed.
	///
	/// It also runs the floating origin once a frame (<see cref="Origin.Follow"/>), the debug teleport and the debug overlay.
	/// </summary>
	internal sealed class Streamer : MonoBehaviour
	{
		/// <summary>Side of one tile in metres. The origin shifts by whole tiles.</summary>
		internal const float TileSize = 250f;

		/// <summary>Quads along a tile's side: 5 m apart, fine enough for the car on ground whose smallest bumps are 70 m across.</summary>
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
			// A car that has gone into a river, a lake or the sea is put back on the nearest road after a moment (nothing here floats).
			Origin.World(player.transform.position, out double wx, out double wz);
			if (Rivers.Water(wx, wz, out double level) && player.transform.position.y < level + Network.Lift - 1.0)
			{
				underwater += Time.deltaTime;
				if (underwater > 1.5f)
				{
					underwater = 0f;
					ToRoad(wx, wz);
					TextController.EnqueueRandomText(1f, "Reset.");
				}
			}
			else
			{
				underwater = 0f;
			}
		}

		/// <summary>Puts the car on the nearest road to world (x, z), facing along it. False (and the car left alone) if no road is near.</summary>
		internal bool ToRoad(double x, double z)
		{
			if (!Network.NearestRoad(x, z, TileSize, out double rx, out double rz, out double dx, out double dz))
			{
				return false;
			}
			BuildAround(rx, rz, 1);
			Place(rx, rz, Quaternion.LookRotation(new Vector3((float)dx, 0f, (float)dz), Vector3.up));
			return true;
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
			Trunks(go.transform, data.Trees);
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

		/// <summary>A collider round each tree's trunk (the trees themselves are drawn instanced, with no objects of their own).</summary>
		private void Trunks(Transform tile, Plant[] trees)
		{
			if (trees == null || trees.Length == 0)
			{
				return;
			}
			GameObject go = new GameObject("Trunks") { layer = layer };
			go.transform.SetParent(tile, false);
			foreach (Plant p in trees)
			{
				float tall = Foliage.Tall(p);
				CapsuleCollider c = go.AddComponent<CapsuleCollider>();
				c.center = p.Pos + Vector3.up * (tall * 0.3f);
				c.radius = 0.9f + 0.4f * p.Scale;
				c.height = tall * 0.6f;
			}
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
			BuildAround(place.X, place.Z, 1);
			Place(place.X, place.Z, Quaternion.LookRotation(new Vector3((float)place.Dx, 0f, (float)place.Dz), Vector3.up));
			destinationName = (destination + 1) + "/" + places.Count + "  " + place.Name;
			destinationShown = Time.realtimeSinceStartup;
			Mod.Log.LogInfo("Teleported to " + place.Name + ": world (" + place.X.ToString("0") + ", " + place.Z.ToString("0") + "), ground " + WorldGen.Height(place.X, place.Z).ToString("0") + " m");
		}

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
			string text = "world " + (x / 1000.0).ToString("0.000") + ", " + (z / 1000.0).ToString("0.000") + " km   height " + player.transform.position.y.ToString("0") + " m\n"
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
