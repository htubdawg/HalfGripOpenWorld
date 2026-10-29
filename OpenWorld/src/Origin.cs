using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace HalfGripOpenWorld
{
	/// <summary>
	/// The floating origin. Unity keeps positions as 32-bit floats: 16 km from 0,0 a position is only good to about 1 mm, which a car's
	/// suspension, wheel contacts and camera already show as jitter. So the scene never gets that far: whenever the car is more than ShiftDistance from
	/// 0,0, everything in the scene is moved back by the same whole number of tiles, and the offset is added here. The physics always
	/// runs near 0,0; world coordinates (what the terrain and the network use) are scene coordinates plus <see cref="X"/>, <see cref="Z"/>.
	///
	/// Only x and z move. Heights stay as they are, so everything that keeps a height (the track line's lowest point, lanes, the reset
	/// logic) is still right after a shift.
	///
	/// The cockpit does not move. The game draws it as a separate rig that stands still near 0,0 (on the INTERIOR layer, seen by its own
	/// InteriorCamera) while the car outside drives about; it only shares a root object with the car. Moved along with the world it would
	/// end up kilometres out, and the hand, gun and shifter would shake with float error. So everything on that layer is left in place.
	/// </summary>
	internal static class Origin
	{
		/// <summary>World coordinates of the scene's 0,0 (metres). Doubles: they reach 16 km and must stay exact.</summary>
		internal static double X;
		internal static double Z;

		/// <summary>True while an Open World race is loaded; the network conversion only applies then.</summary>
		internal static bool Active;

		/// <summary>Number of shifts since the world loaded, for the debug overlay.</summary>
		internal static int Shifts;

		/// <summary>Called after every shift with the amount the scene moved (the negative of the change in <see cref="X"/>, <see cref="Z"/>).</summary>
		internal static event Action<Vector3> Shifted;

		private static readonly List<(Rigidbody body, RigidbodyInterpolation mode)> paused = new List<(Rigidbody, RigidbodyInterpolation)>();
		private static int restoreAt;

		internal static void Reset(bool active)
		{
			X = 0.0;
			Z = 0.0;
			Shifts = 0;
			Active = active;
			paused.Clear();
		}

		/// <summary>Starts the scene with its 0,0 at world (x, z), rounded to whole tiles, so the player can start anywhere in the world near 0,0.</summary>
		internal static void Begin(double x, double z, float step)
		{
			X = Math.Round(x / step) * step;
			Z = Math.Round(z / step) * step;
		}

		internal static Vector3 ToWorld(Vector3 local)
		{
			return new Vector3((float)(local.x + X), local.y, (float)(local.z + Z));
		}

		internal static Vector3 ToLocal(Vector3 world)
		{
			return new Vector3((float)(world.x - X), world.y, (float)(world.z - Z));
		}

		/// <summary>World x, z of a scene point, as doubles.</summary>
		internal static void World(Vector3 local, out double x, out double z)
		{
			x = local.x + X;
			z = local.z + Z;
		}

		/// <summary>The scene point of world (x, y, z).</summary>
		internal static Vector3 Local(double x, float y, double z)
		{
			return new Vector3((float)(x - X), y, (float)(z - Z));
		}

		/// <summary>Shifts the scene if <paramref name="focus"/> (the player car) is further than the limit from 0,0. Called once a frame.</summary>
		internal static void Follow(Vector3 focus, float limit, float step)
		{
			RestoreInterpolation();
			if (Mathf.Abs(focus.x) < limit && Mathf.Abs(focus.z) < limit)
			{
				return;
			}
			// Whole tiles, so the tile grid stays on whole numbers in the scene and nothing drifts from rounding.
			float dx = Mathf.Round(focus.x / step) * step;
			float dz = Mathf.Round(focus.z / step) * step;
			if (dx != 0f || dz != 0f)
			{
				Shift(dx, dz);
			}
		}

		/// <summary>Moves everything in the loaded scenes by (-dx, 0, -dz) and adds (dx, dz) to the offset.</summary>
		internal static void Shift(float dx, float dz)
		{
			Vector3 delta = new Vector3(-dx, 0f, -dz);
			float started = Time.realtimeSinceStartup;

			int interior = LayerMask.NameToLayer("INTERIOR");

			// An interpolated body would be drawn sliding the whole way for a frame: switch interpolation off until the physics has stepped.
			foreach (Rigidbody body in UnityEngine.Object.FindObjectsByType<Rigidbody>(FindObjectsSortMode.None))
			{
				if (body.interpolation != RigidbodyInterpolation.None && !InCockpit(body.transform, interior))
				{
					paused.Add((body, body.interpolation));
					body.interpolation = RigidbodyInterpolation.None;
				}
			}
			restoreAt = Time.frameCount + 2;

			int roots = 0;
			HashSet<Transform> moved = new HashSet<Transform>();
			for (int s = 0; s < SceneManager.sceneCount; s++)
			{
				Scene scene = SceneManager.GetSceneAt(s);
				if (!scene.isLoaded)
				{
					continue;
				}
				foreach (GameObject go in scene.GetRootGameObjects())
				{
					// A screen-space canvas is placed by Unity, not by its transform; a cockpit root stays.
					if ((go.TryGetComponent(out Canvas canvas) && canvas.renderMode != RenderMode.WorldSpace) || go.layer == interior)
					{
						continue;
					}
					go.transform.position += delta;
					moved.Add(go.transform);
					roots++;
				}
			}
			int kept = KeepInterior(delta, interior, moved);
			Physics.SyncTransforms();

			int particles = ShiftParticles(delta, interior);
			int lines = ShiftLines(delta, interior);
			int caches = GameCaches.Shift(delta);

			X += dx;
			Z += dz;
			Shifts++;
			try
			{
				Shifted?.Invoke(delta);
			}
			catch (Exception e)
			{
				Mod.Log.LogWarning("A shift listener failed: " + e.Message);
			}
			Mod.Info("Origin shift " + Shifts + ": moved " + roots + " roots, " + particles + " particle systems, " + lines + " lines, " + caches + " cached positions (" + kept + " cockpit objects kept in place) by (" + (-dx).ToString("0") + ", " + (-dz).ToString("0") + "); scene 0,0 is world (" + X.ToString("0") + ", " + Z.ToString("0") + "), " + ((Time.realtimeSinceStartup - started) * 1000f).ToString("0.0") + " ms");
		}

		/// <summary>
		/// Puts back the cockpit: the top of each INTERIOR-layer subtree (an interior object with no interior object above it) whose root
		/// was moved goes back by -delta, and everything under it with it. Returns how many subtrees were put back.
		/// </summary>
		private static int KeepInterior(Vector3 delta, int interior, HashSet<Transform> moved)
		{
			if (interior < 0)
			{
				Mod.Log.LogWarning("This game has no INTERIOR layer; the cockpit moves with the world.");
				return 0;
			}
			int n = 0;
			foreach (Transform t in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
			{
				if (t.gameObject.layer != interior || t.parent == null || !moved.Contains(t.root) || InCockpit(t.parent, interior))
				{
					continue;
				}
				t.position -= delta;
				n++;
			}
			return n;
		}

		/// <summary>True if <paramref name="t"/> or anything above it is on the INTERIOR layer: part of the cockpit, which stays put.</summary>
		private static bool InCockpit(Transform t, int interior)
		{
			if (interior < 0)
			{
				return false;
			}
			for (; t != null; t = t.parent)
			{
				if (t.gameObject.layer == interior)
				{
					return true;
				}
			}
			return false;
		}

		private static void RestoreInterpolation()
		{
			if (paused.Count == 0 || Time.frameCount < restoreAt)
			{
				return;
			}
			foreach ((Rigidbody body, RigidbodyInterpolation mode) in paused)
			{
				if (body != null)
				{
					body.interpolation = mode;
				}
			}
			paused.Clear();
		}

		/// <summary>Particles simulated in world space (dust, smoke, sparks) stay where they were in the world.</summary>
		private static int ShiftParticles(Vector3 delta, int interior)
		{
			int n = 0;
			ParticleSystem.Particle[] buffer = null;
			foreach (ParticleSystem ps in UnityEngine.Object.FindObjectsByType<ParticleSystem>(FindObjectsSortMode.None))
			{
				if (ps.main.simulationSpace != ParticleSystemSimulationSpace.World || ps.particleCount == 0 || InCockpit(ps.transform, interior))
				{
					continue;
				}
				if (buffer == null || buffer.Length < ps.particleCount)
				{
					buffer = new ParticleSystem.Particle[Math.Max(ps.particleCount, 256)];
				}
				int count = ps.GetParticles(buffer);
				for (int i = 0; i < count; i++)
				{
					buffer[i].position += delta;
				}
				ps.SetParticles(buffer, count);
				n++;
			}
			return n;
		}

		/// <summary>Trails (tyre marks, tracers) and world-space lines keep their points in world space.</summary>
		private static int ShiftLines(Vector3 delta, int interior)
		{
			int n = 0;
			foreach (TrailRenderer trail in UnityEngine.Object.FindObjectsByType<TrailRenderer>(FindObjectsSortMode.None))
			{
				// Arrays of exactly positionCount: SetPositions takes the array's length as the number of points.
				Vector3[] points = new Vector3[trail.positionCount];
				if (points.Length == 0 || InCockpit(trail.transform, interior))
				{
					continue;
				}
				trail.GetPositions(points);
				for (int i = 0; i < points.Length; i++)
				{
					points[i] += delta;
				}
				trail.SetPositions(points);
				n++;
			}
			foreach (LineRenderer line in UnityEngine.Object.FindObjectsByType<LineRenderer>(FindObjectsSortMode.None))
			{
				if (!line.useWorldSpace || line.positionCount == 0 || InCockpit(line.transform, interior))
				{
					continue;
				}
				Vector3[] points = new Vector3[line.positionCount];
				line.GetPositions(points);
				for (int i = 0; i < points.Length; i++)
				{
					points[i] += delta;
				}
				line.SetPositions(points);
				n++;
			}
			return n;
		}
	}
}
