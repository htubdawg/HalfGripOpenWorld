using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace HalfGripOpenWorld
{
	/// <summary>
	/// Keeps the ground loaded around the player car: square tiles of TileSize metres, each a mesh with a collider, built from
	/// <see cref="WorldGen.Height"/>. The heights are worked out on worker threads (pure arithmetic, no Unity calls); the mesh and collider
	/// are made on the main thread, a few tiles a frame, nearest first. Tiles beyond the radius are destroyed.
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

		private CarControllerNew player;
		private Material material;
		private int layer;

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
			internal string Error;
		}

		internal void Setup(CarControllerNew car, Material ground, int groundLayer)
		{
			Instance = this;
			player = car;
			material = ground;
			layer = groundLayer;
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
			return new Heights { Tx = tx, Tz = tz, H = h };
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
					// Ten metres to one texture repeat, counted from the tile, so it lines up across tiles (TileSize is a multiple of 10).
					uv[v] = new Vector2(i * step / 10f, j * step / 10f);
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
			go.AddComponent<MeshFilter>().sharedMesh = mesh;
			go.AddComponent<MeshRenderer>().sharedMaterial = material;
			go.AddComponent<MeshCollider>().sharedMesh = mesh;
			HandlingModifier handling = go.AddComponent<HandlingModifier>();
			handling.gripModifier = GroundGrip;
			handling.resistanceModifier = GroundDrag;
			handling.background = true;
			go.AddComponent<TileMesh>().Mesh = mesh;
			tiles[Key(data.Tx, data.Tz)] = go;

			buildMs += (Time.realtimeSinceStartup - started) * 1000f;
			built++;
			if (built % 50 == 0)
			{
				Mod.Info("Tiles: " + built + " built, " + (buildMs / built).ToString("0.0") + " ms each on the main thread, " + tiles.Count + " loaded");
			}
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

	/// <summary>Destroys a tile's mesh with the tile (a mesh made in code is not freed with its object).</summary>
	internal sealed class TileMesh : MonoBehaviour
	{
		internal Mesh Mesh;

		private void OnDestroy()
		{
			if (Mesh != null)
			{
				Destroy(Mesh);
			}
		}
	}
}
