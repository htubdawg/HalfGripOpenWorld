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

		/// <summary>Pixels along a side of a tile's ground texture: about 4 units each, the game's own chunky pixel-art scale.</summary>
		private const int Pixels = 64;

		/// <summary>Grip and rolling resistance of the farm tracks (loose dirt), as on the Highway mod's dirt roads.</summary>
		private const float TrackGrip = 0.8f;
		private const float TrackDrag = 1.6f;

		private CarControllerNew player;
		private Material material;
		private Material[] roadMaterials;
		private Material trackMaterial;
		private Material waterMaterial;
		private Material[] buildingMaterials;
		private Foliage foliage;
		private int layer;
		private int roadLayer;
		private MaterialPropertyBlock block;
		private float underwater;

		private readonly Dictionary<long, GameObject> tiles = new Dictionary<long, GameObject>();
		private readonly HashSet<long> working = new HashSet<long>();
		private readonly ConcurrentQueue<Heights> done = new ConcurrentQueue<Heights>();
		private readonly List<long> wanted = new List<long>();

		private float buildMs;
		private int built;

		/// <summary>One tile's heights, with a one-sample border all round so the normals match across tile edges.</summary>
		private sealed class Heights
		{
			internal int Tx;
			internal int Tz;
			internal float[] H;
			internal Color32[] Ground;
			internal Buildings.Data Buildings;
			internal Plant[] Trees;
			internal Plant[] Grass;
			internal RoadMeshData Road;
			internal RoadMeshData Tracks;
			internal RoadMeshData Water;
			internal string Error;
		}

		internal void Setup(CarControllerNew car, Material ground, int groundLayer, Material[] roads, Material tracks, Material water, int roadsLayer, Material[] buildings, Foliage plants)
		{
			buildingMaterials = buildings;
			foliage = plants;
			Instance = this;
			player = car;
			material = ground;
			layer = groundLayer;
			roadMaterials = roads;
			trackMaterial = tracks;
			waterMaterial = water;
			roadLayer = roadsLayer;
			block = new MaterialPropertyBlock();
		}

		internal int Loaded => tiles.Count;

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
			if (Mod.Teleport.Value && GameInput.GetKeyDown(Mod.TeleportKey.Value))
			{
				TeleportAhead(Mod.TeleportDistance.Value);
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

		/// <summary>Puts the car on the nearest road to world (x, z), facing along it.</summary>
		internal void ToRoad(double x, double z)
		{
			if (Network.NearestRoad(x, z, TileSize, out double rx, out double rz, out double dx, out double dz))
			{
				BuildAround(rx, rz, 1);
				Place(rx, rz, Quaternion.LookRotation(new Vector3((float)dx, 0f, (float)dz), Vector3.up));
			}
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
			Color32[] colours = Colours(tx, tz, h, out Cover[] covers);
			Buildings.Data buildings = HalfGripOpenWorld.Buildings.Build(tx, tz, TileSize);
			Foliage.Scatter(tx, tz, TileSize, Pixels, covers, Network.Clearance(tx, tz, TileSize, Pixels), h, side, (float)step, buildings.Footprints, out Plant[] trees, out Plant[] grass);
			return new Heights
			{
				Tx = tx,
				Tz = tz,
				H = h,
				Ground = colours,
				Buildings = buildings,
				Trees = trees,
				Grass = grass,
				Road = RoadMesh.Build(tx, tz, TileSize, false),
				Tracks = RoadMesh.Build(tx, tz, TileSize, true),
				Water = WaterMesh.Build(tx, tz, TileSize),
			};
		}

		/// <summary>
		/// The tile's ground texture: each pixel's colour from <see cref="LandUse"/>, at the pixel's centre, with the height and slope
		/// taken from the tile's own height samples (so the colours follow exactly the ground that is drawn).
		/// </summary>
		private static Color32[] Colours(int tx, int tz, float[] h, out Cover[] covers)
		{
			covers = new Cover[Pixels * Pixels];
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
					colours[pj * Pixels + pi] = new Color32(rgb.R, rgb.G, rgb.B, 255);
				}
			}
			return colours;
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
			Part(go.transform, "Tracks", data, data.Tracks, new[] { trackMaterial }, true, TrackGrip, TrackDrag);
			Part(go.transform, "Water", data, data.Water, new[] { waterMaterial }, false, 1f, 1f);
			Part(go.transform, "Buildings", data, data.Buildings?.Mesh, buildingMaterials, true, 1f, 1f);
			Trunks(go.transform, data.Trees);
			tiles[Key(data.Tx, data.Tz)] = go;
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
		/// One of the tile's parts (its roads, farm tracks or water): a mesh with a submesh per material, a collider if it is driven on,
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

		/// <summary>Moves the car <paramref name="distance"/> metres ahead (debug), building the ground there first so it lands on something.</summary>
		private void TeleportAhead(float distance)
		{
			Transform car = player.transform;
			Vector3 forward = car.forward;
			forward.y = 0f;
			if (forward.sqrMagnitude < 1e-4f)
			{
				forward = Vector3.forward;
			}
			forward.Normalize();
			Origin.World(car.position, out double x, out double z);
			x += forward.x * distance;
			z += forward.z * distance;
			WorldGen.ClampInside(ref x, ref z, 100.0);
			BuildAround(x, z, 1);
			Place(x, z, Quaternion.LookRotation(forward, Vector3.up));
			Mod.Log.LogInfo("Teleported to world (" + x.ToString("0") + ", " + z.ToString("0") + "), ground " + WorldGen.Height(x, z).ToString("0") + " m");
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
			if (!Mod.ShowCoords.Value || player == null)
			{
				return;
			}
			Origin.World(player.transform.position, out double x, out double z);
			string text = "world " + (x / 1000.0).ToString("0.000") + ", " + (z / 1000.0).ToString("0.000") + " km   height " + player.transform.position.y.ToString("0") + " m\n"
				+ "scene " + player.transform.position.x.ToString("0") + ", " + player.transform.position.z.ToString("0") + "   origin " + Origin.X.ToString("0") + ", " + Origin.Z.ToString("0") + "   shifts " + Origin.Shifts + "\n"
				+ "tiles " + tiles.Count + " loaded, " + working.Count + " in work";
			GUI.Label(new Rect(12f, 12f, 700f, 70f), text);
		}

		private void OnDestroy()
		{
			if (Instance == this)
			{
				Instance = null;
			}
			tiles.Clear();
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
