using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HalfGripOpenWorld
{
	/// <summary>
	/// Turns the Stadium scene into the Open World when an Open World race loads. Runs on <c>sceneLoaded</c>: after every Awake and before
	/// any Start, so the lap counter, the grid and the reset logic all find the new layout.
	///
	/// The Stadium's own ground, track and decoration are hidden. The game still needs a track line (the lap counter, the reset logic and
	/// the Multiplayer mod's grid all read it), so a small loop round the spawn pad is made; lap counting and resets are then taken over
	/// in <see cref="Patches"/>, since a free-roam race has no lap.
	/// </summary>
	internal static class WorldScene
	{
		/// <summary>Radius of the token track loop round the spawn pad.</summary>
		private const float LoopRadius = 120f;

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
				throw new InvalidOperationException("this scene has no ENVIROMENT object, so it is not the Stadium scene");
			}

			Transform ground = env.transform.Find("Ground");
			int layer = ground != null ? ground.gameObject.layer : 0;
			Material source = ground != null && ground.TryGetComponent(out MeshRenderer groundRenderer) ? groundRenderer.sharedMaterial : null;

			foreach (string name in new[] { "Ground", "Track", "Decoration" })
			{
				Hide(env.transform.Find(name));
			}
			// The Stadium's hazard spots, scattered props and chicken sounds all sit on the old lap.
			foreach (GameObject go in scene.GetRootGameObjects())
			{
				if (go.name == "HazardsSpawner" || go.name == "Scattered" || go.name.StartsWith("Looping_3D_Sound_Chicken", StringComparison.Ordinal))
				{
					Hide(go.transform);
				}
			}
			Transform rain = env.transform.Find("Weather/ParticleColliderPlane");
			if (rain != null && rain.TryGetComponent(out rainPlane))
			{
				rainPlane.enabled = false;
			}

			Origin.Reset(true);
			// Free roam: no "Hold R to reset" when far from the token loop, no "Wrong way".
			PlayerResetLogic.noHints = true;
			PlayerResetLogic.noWrongWay = true;
			hintsChanged = true;

			root = new GameObject("OpenWorld");
			SceneManager.MoveGameObjectToScene(root, scene);
			Streamer streamer = root.AddComponent<Streamer>();
			streamer.Setup(tm.playerCarController, GroundMaterial(source), layer);

			Vector3 forward = PlaceGrid(tm, env);
			Rewire(tm, forward);
			streamer.BuildAround(0.0, 0.0, 2);
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

		/// <summary>The ground's material: the Stadium ground's shader in a grass colour.</summary>
		private static Material GroundMaterial(Material source)
		{
			Shader shader = source != null ? source.shader : null;
			Material m = shader != null ? new Material(shader) : null;
			if (m == null || (!m.HasProperty("_BaseColor") && !m.HasProperty("_Color")))
			{
				shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Sprites/Default");
				m = new Material(shader);
			}
			Color grass = new Color(0.36f, 0.45f, 0.24f);
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

		/// <summary>
		/// The grid on the spawn pad at 0,0: the player's slot at the middle, the others in pairs behind it (the Multiplayer mod puts the
		/// other players on these). Returns the way the grid faces.
		/// </summary>
		private static Vector3 PlaceGrid(TrackManager tm, GameObject env)
		{
			CarControllerNew car = tm.playerCarController;
			Transform container = tm.startingPositionsContainer != null ? tm.startingPositionsContainer.transform : null;
			Transform playerSlot = Traverse.Create(tm).Field("playerStart").GetValue<Transform>();
			if (playerSlot == null && container != null)
			{
				playerSlot = container.Cast<Transform>().FirstOrDefault(t => t.name == "PlayerPosition");
			}
			float lift = playerSlot != null ? car.transform.position.y - playerSlot.position.y : 0.5f;
			Vector3 forward = car.transform.forward;
			forward.y = 0f;
			forward = forward.sqrMagnitude > 1e-4f ? forward.normalized : Vector3.right;
			Vector3 right = Vector3.Cross(Vector3.up, forward);

			if (container != null)
			{
				int n = 0;
				foreach (Transform slot in container.Cast<Transform>().ToList())
				{
					if (slot.name == "PlayerPosition")
					{
						slot.position = Vector3.zero;
						continue;
					}
					slot.position = -forward * (16f * (n / 2 + 1)) + right * (n % 2 == 0 ? -6f : 6f);
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
			Vector3 target = Vector3.up * lift;
			ct.position = target;
			Rigidbody body = car.rb != null ? car.rb : car.GetComponent<Rigidbody>();
			if (body != null)
			{
				body.position = target;
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
				Vector3 at = forward * 30f + right * 10f;
				semaphore.position = new Vector3(at.x, semaphore.position.y, at.z);
			}
			return forward;
		}

		/// <summary>A token loop round the spawn pad, starting at the player's slot and heading the way the grid faces.</summary>
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
			Vector3 centre = right * LoopRadius;
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

		/// <summary>The cameras draw as far as the tiles reach (the Stadium's draw to about 1.2 km); never lowered.</summary>
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
