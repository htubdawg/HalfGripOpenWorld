using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HalfGripOpenWorld
{
	/// <summary>
	/// Turns the Tokyo scene into the Open World when an Open World race loads. Runs on <c>sceneLoaded</c>: after every Awake and before
	/// any Start, so the lap counter, the grid and the reset logic all find the new layout.
	///
	/// Tokyo is the host because its city pieces (buildings, lamps, barriers) are what the towns will be furnished with; the Forest's
	/// trees, fences and grass come from the main menu, which is loaded with every scene. Tokyo's own arena, ground, city and train are
	/// hidden. The game still needs a track line (the lap counter, the reset logic and the Multiplayer mod's grid all read it), so a small
	/// loop round the spawn is made; lap counting and resets are then taken over in <see cref="Patches"/>, since a free-roam race has no lap.
	/// </summary>
	internal static class WorldScene
	{
		/// <summary>Radius of the token track loop round the spawn.</summary>
		private const float LoopRadius = 120f;

		/// <summary>Tokyo's own pieces under ENVIROMENT that are hidden: the arena, its ground, the city, the train, the lane dividers.</summary>
		private static readonly string[] HiddenParts = { "TrackArena5", "Terrain", "Decoration", "TrainAssembly", "LaneDelimiter" };

		private static GameObject root;
		private static readonly List<GameObject> hidden = new List<GameObject>();
		private static Collider rainPlane;
		private static bool hintsChanged;

		/// <summary>True while an Open World race is loaded and built.</summary>
		internal static bool Loaded => root != null && Origin.Active;

		internal static void OnSceneLoaded(Scene scene)
		{
			Clear();
			if (Mod.Map == null || RunState.ActiveMap != Mod.Map)
			{
				return;
			}
			TrackManager tm = UnityEngine.Object.FindFirstObjectByType<TrackManager>();
			if (tm == null || tm.playerCarController == null || tm.lapManager == null || tm.lapManager.trackLine == null)
			{
				Mod.Log.LogWarning("An Open World race loaded without a track manager, player car or track line; nothing is built.");
				return;
			}
			try
			{
				Build(scene, tm);
			}
			catch (Exception e)
			{
				Mod.Log.LogError("The Open World could not be built: " + e);
			}
		}

		internal static void Unload()
		{
			if (root != null)
			{
				UnityEngine.Object.Destroy(root);
			}
			foreach (GameObject go in hidden)
			{
				if (go != null)
				{
					go.SetActive(true);
				}
			}
			if (rainPlane != null)
			{
				rainPlane.enabled = true;
			}
			Clear();
		}

		/// <summary>Forgets the last world (its objects went with their scene).</summary>
		private static void Clear()
		{
			root = null;
			hidden.Clear();
			rainPlane = null;
			Origin.Reset(false);
			if (hintsChanged)
			{
				PlayerResetLogic.noHints = false;
				PlayerResetLogic.noWrongWay = false;
				hintsChanged = false;
			}
		}

		private static void Build(Scene scene, TrackManager tm)
		{
			float started = Time.realtimeSinceStartup;
			GameObject env = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "ENVIROMENT");
			if (env == null)
			{
				throw new InvalidOperationException("this scene has no ENVIROMENT object, so it is not the Tokyo scene");
			}
			Shader lit = LitShader(scene);

			foreach (string name in HiddenParts)
			{
				Hide(env.transform.Find(name));
			}
			// Tokyo's hazard spots and scattered props sit on the old lap.
			foreach (GameObject go in scene.GetRootGameObjects())
			{
				if (go.name == "HazardsSpawner" || go.name == "Scattered")
				{
					Hide(go.transform);
				}
			}
			Transform rain = env.transform.Find("Weather/ParticleColliderPlane");
			if (rain != null && rain.TryGetComponent(out rainPlane))
			{
				rainPlane.enabled = false;
			}

			Network.Ensure();
			Mod.Log.LogInfo("World layout: " + Rivers.All.Count + " rivers, " + Rivers.LakeCount + " lakes, " + Places.All.Count + " towns, villages and farms, " + Network.Roads.Count + " roads, " + Network.BuildMs.ToString("0") + " ms");

			// The scene's 0,0 is put at the spawn, so the start is as precise as anywhere else.
			Origin.Reset(true);
			Origin.Begin(Network.SpawnX, Network.SpawnZ, Streamer.TileSize);
			// Free roam: no "Hold R to reset" when far from the token loop, no "Wrong way".
			PlayerResetLogic.noHints = true;
			PlayerResetLogic.noWrongWay = true;
			hintsChanged = true;

			int groundLayer = LayerMask.NameToLayer("Floor");
			root = new GameObject("OpenWorld");
			SceneManager.MoveGameObjectToScene(root, scene);
			Streamer streamer = root.AddComponent<Streamer>();
			Trees.Capture();
			Foliage foliage = root.AddComponent<Foliage>();
			foliage.Setup(lit);
			streamer.Setup(tm.playerCarController, GroundMaterial(lit), groundLayer >= 0 ? groundLayer : 0, RoadMesh.Materials(lit), RoadMesh.TrackMaterial(lit), WaterMaterial(lit), 0, Buildings.Materials(lit), foliage);

			PlaceGrid(tm, env, out Vector3 forward);
			Tunnels(lit);
			Rewire(tm, forward);
			streamer.BuildAround(Network.SpawnX, Network.SpawnZ, 2);
			RaiseCameras();

			Mod.Log.LogInfo("Open World built: " + (WorldGen.Half * 2.0 / 1000.0).ToString("0") + " km square, seed " + WorldGen.Seed.ToString("X") + ", " + streamer.Loaded + " tiles at the spawn, "
				+ ((Time.realtimeSinceStartup - started) * 1000f).ToString("0") + " ms");
		}

		private static void Hide(Transform t)
		{
			if (t != null && t.gameObject.activeSelf)
			{
				t.gameObject.SetActive(false);
				hidden.Add(t.gameObject);
			}
		}

		/// <summary>
		/// The scene's lit shader, taken from one of its own renderers (a shader no material in the build uses may be stripped from the
		/// game, so Shader.Find is only the fallback).
		/// </summary>
		private static Shader LitShader(Scene scene)
		{
			foreach (GameObject go in scene.GetRootGameObjects())
			{
				foreach (MeshRenderer r in go.GetComponentsInChildren<MeshRenderer>(true))
				{
					Material m = r.sharedMaterial;
					if (m != null && m.shader != null && m.shader.name == "Universal Render Pipeline/Lit")
					{
						return m.shader;
					}
				}
			}
			return Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Sprites/Default");
		}

		/// <summary>The ground's material: white, each tile's own land-use texture is set on its renderer.</summary>
		private static Material GroundMaterial(Shader shader)
		{
			Material m = new Material(shader);
			Color grass = Color.white;
			if (m.HasProperty("_BaseColor"))
			{
				m.SetColor("_BaseColor", grass);
			}
			if (m.HasProperty("_Color"))
			{
				m.SetColor("_Color", grass);
			}
			if (m.HasProperty("_Smoothness"))
			{
				m.SetFloat("_Smoothness", 0.05f);
			}
			if (m.HasProperty("_Metallic"))
			{
				m.SetFloat("_Metallic", 0f);
			}
			m.name = "OpenWorld Ground";
			m.hideFlags = HideFlags.HideAndDontSave;
			Mod.Info("Ground material: shader " + m.shader.name);
			return m;
		}

		/// <summary>Rivers, lakes and the sea: a deep, slightly glossy blue.</summary>
		private static Material WaterMaterial(Shader shader)
		{
			Material m = new Material(shader) { name = "OpenWorld Water", hideFlags = HideFlags.HideAndDontSave };
			Color water = new Color(0.17f, 0.31f, 0.47f);
			if (m.HasProperty("_BaseColor"))
			{
				m.SetColor("_BaseColor", water);
			}
			if (m.HasProperty("_Color"))
			{
				m.SetColor("_Color", water);
			}
			if (m.HasProperty("_Smoothness"))
			{
				m.SetFloat("_Smoothness", 0.75f);
			}
			return m;
		}

		/// <summary>
		/// A tunnel mouth where each road leaves the world: a concrete portal in the cutting at the road's end, dark inside, with a solid
		/// block behind the dark so nobody drives in.
		/// </summary>
		private static void Tunnels(Shader shader)
		{
			Material concrete = RoadMesh.Materials(shader)[RoadMesh.Concrete];
			Material dark = new Material(shader) { name = "OpenWorld Tunnel", hideFlags = HideFlags.HideAndDontSave };
			if (dark.HasProperty("_BaseColor"))
			{
				dark.SetColor("_BaseColor", new Color(0.02f, 0.02f, 0.03f));
			}
			if (dark.HasProperty("_Color"))
			{
				dark.SetColor("_Color", new Color(0.02f, 0.02f, 0.03f));
			}
			foreach (Network.Portal p in Network.Portals)
			{
				GameObject portal = new GameObject("Tunnel");
				portal.transform.SetParent(root.transform, false);
				Vector3 outward = new Vector3((float)p.DirX, 0f, (float)p.DirZ);
				portal.transform.SetPositionAndRotation(Origin.Local(p.X, (float)(p.Y + Network.Lift), p.Z), Quaternion.LookRotation(outward, Vector3.up));
				float width = (float)p.Half * 2f + 8f;
				const float height = 22f;
				Block(portal.transform, concrete, new Vector3(-width / 2f - 3f, height / 2f, 0f), new Vector3(6f, height + 4f, 6f));
				Block(portal.transform, concrete, new Vector3(width / 2f + 3f, height / 2f, 0f), new Vector3(6f, height + 4f, 6f));
				Block(portal.transform, concrete, new Vector3(0f, height + 4f, 0f), new Vector3(width + 12f, 8f, 6f));
				Block(portal.transform, dark, new Vector3(0f, height / 2f, 6f), new Vector3(width, height, 6f));
			}
			Mod.Info("Tunnel mouths: " + Network.Portals.Count);
		}

		private static void Block(Transform parent, Material material, Vector3 at, Vector3 size)
		{
			GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
			block.name = "Portal";
			block.transform.SetParent(parent, false);
			block.transform.localPosition = at;
			block.transform.localScale = size;
			block.GetComponent<MeshRenderer>().sharedMaterial = material;
		}

		/// <summary>
		/// The grid on the trunk road near the main town: the player in the inner lane, the other slots in pairs
		/// behind (the Multiplayer mod puts the other players on these).
		/// </summary>
		private static void PlaceGrid(TrackManager tm, GameObject env, out Vector3 forward)
		{
			CarControllerNew car = tm.playerCarController;
			Transform container = tm.startingPositionsContainer != null ? tm.startingPositionsContainer.transform : null;
			Transform playerSlot = Traverse.Create(tm).Field("playerStart").GetValue<Transform>();
			if (playerSlot == null && container != null)
			{
				playerSlot = container.Cast<Transform>().FirstOrDefault(t => t.name == "PlayerPosition");
			}
			float lift = playerSlot != null ? car.transform.position.y - playerSlot.position.y : 0.5f;
			forward = new Vector3((float)Network.SpawnDirX, 0f, (float)Network.SpawnDirZ);
			Vector3 right = Vector3.Cross(Vector3.up, forward);
			Quaternion heading = Quaternion.LookRotation(forward, Vector3.up);
			Vector3 spawn = OnRoad(Network.SpawnX, Network.SpawnZ);

			if (container != null)
			{
				int n = 0;
				foreach (Transform slot in container.Cast<Transform>().ToList())
				{
					slot.rotation = heading;
					if (slot.name == "PlayerPosition")
					{
						slot.position = spawn;
						continue;
					}
					// Pairs behind: the inner lane and the outer lane (one lane, 11 units, further right).
					Vector3 at = spawn - forward * (18f * (n / 2 + 1)) + right * (n % 2 == 0 ? 0f : 11f);
					Origin.World(at, out double wx, out double wz);
					slot.position = OnRoad(wx, wz);
					n++;
				}
			}

			Transform ct = car.transform;
			AudioListener listener = car.GetComponentInChildren<AudioListener>(true);
			bool listenerOn = listener != null && listener.enabled;
			if (listenerOn)
			{
				listener.enabled = false;
			}
			Vector3 target = spawn + Vector3.up * lift;
			ct.SetPositionAndRotation(target, heading);
			Rigidbody body = car.rb != null ? car.rb : car.GetComponent<Rigidbody>();
			if (body != null)
			{
				body.position = target;
				body.rotation = heading;
				body.linearVelocity = Vector3.zero;
				body.angularVelocity = Vector3.zero;
			}
			if (listenerOn)
			{
				listener.enabled = true;
			}
			Physics.SyncTransforms();

			Transform semaphore = env.transform.Find("RaceStarterSemaphore");
			if (semaphore != null)
			{
				// The start lights hang over the road ahead, as high above it as Tokyo's hang above its own.
				Vector3 at = spawn + forward * 30f;
				semaphore.position = new Vector3(at.x, spawn.y + semaphore.position.y, at.z);
			}
		}

		/// <summary>The scene point on the road surface (or the ground) at world (x, z).</summary>
		private static Vector3 OnRoad(double x, double z)
		{
			return Origin.Local(x, (float)(WorldGen.Height(x, z) + Network.BedDepth), z);
		}

		/// <summary>A token loop round the spawn, starting at the player's slot and heading the way the grid faces.</summary>
		private static void Rewire(TrackManager tm, Vector3 forward)
		{
			LapManager lap = tm.lapManager;
			PathManager line = lap.trackLine;
			foreach (Transform child in line.transform.Cast<Transform>().ToList())
			{
				child.SetParent(null);
				UnityEngine.Object.Destroy(child.gameObject);
			}
			Vector3 right = Vector3.Cross(Vector3.up, forward);
			Vector3 spawn = OnRoad(Network.SpawnX, Network.SpawnZ);
			Vector3 centre = spawn + right * LoopRadius;
			const int points = 16;
			for (int i = 0; i < points; i++)
			{
				float a = i * 2f * Mathf.PI / points;
				GameObject point = new GameObject("OW" + (i + 1));
				point.transform.SetParent(line.transform, true);
				point.transform.position = centre - right * (LoopRadius * Mathf.Cos(a)) + forward * (LoopRadius * Mathf.Sin(a)) + Vector3.up;
				point.transform.localScale = new Vector3(12f, 1f, 12f);
				point.AddComponent<PathControlPointEditorController>();
			}
			AccessTools.Method(typeof(PathManager), "InitializePath").Invoke(line, null);
			Traverse.Create(line).Field("snapLanesToGround").SetValue(false);
			try
			{
				line.BakeRacingLine();
			}
			catch (Exception e)
			{
				Mod.Log.LogWarning("The token loop's racing line could not be baked (" + e.Message + "); using the plain line.");
				line.RebuildPath();
			}

			lap.enemyTrackLines = new List<PathManager> { line };
			foreach (PathManager other in UnityEngine.Object.FindObjectsByType<PathManager>(FindObjectsSortMode.None))
			{
				if (other != line)
				{
					Hide(other.transform);
				}
			}
			Mod.Info("Token loop: " + line.sampledPoints.Count + " samples, " + line.trackLength.ToString("0") + " m");
		}

		/// <summary>The cameras draw as far as the tiles reach (Tokyo's draw to about 1.2 km); never lowered.</summary>
		private static void RaiseCameras()
		{
			float needed = (Mod.TileRadius.Value + 0.5f) * Streamer.TileSize;
			foreach (Camera camera in UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
			{
				if ((camera.name == "Main Camera" || camera.name == "OutsideCamera") && camera.farClipPlane < needed)
				{
					Mod.Info(camera.name + " draws to " + needed.ToString("0") + " m (was " + camera.farClipPlane.ToString("0") + ")");
					camera.farClipPlane = needed;
				}
			}
		}
	}
}
