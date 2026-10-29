using System;
using System.Collections.Generic;
using UnityEngine;

namespace HalfGripOpenWorld
{
	/// <summary>
	/// Tokyo's own buildings, used as landmarks: its tall buildings and station buildings stand here and there in a town's middle, and a
	/// temple near each village's. One of each kind is found among Tokyo's decoration (hidden, but still in the scene) before it is
	/// hidden, measured, and copied (with its collider) wherever a tile's buildings call for one, scaled to the plot.
	///
	/// Where they stand is decided with the rest of the buildings (<see cref="Buildings"/>), from the sizes measured here; the kinds are
	/// the same for everyone (same game, same scene), so every player gets the same.
	/// </summary>
	internal static class Landmarks
	{
		internal sealed class Kind
		{
			public string Name;
			public GameObject Proto;
			/// <summary>Its size as it stands in Tokyo (the bounds of its renderers).</summary>
			public Vector3 Size;
			public bool Tower;
		}

		/// <summary>One building to stand: which kind, where (world x, z; scene height of its base), turned, scaled to fit a square of <see cref="Side"/>.</summary>
		internal struct Spot
		{
			public int Kind;
			public double X;
			public double Z;
			public float Y;
			public float Yaw;
			public float Side;
		}

		/// <summary>The kinds found, in a fixed order (by name), so a kind's number means the same on every machine.</summary>
		internal static readonly List<Kind> Kinds = new List<Kind>();

		/// <summary>Object names (their start) of the buildings looked for, and whether each is a tower (town) or a temple (village).</summary>
		private static readonly (string name, bool tower)[] Wanted = { ("BuildingTrain", true), ("TallBuilding3", true), ("TempleTall", false), ("Temple", false) };

		/// <summary>Finds one of each wanted building under Tokyo's decoration. Must run before the decoration is hidden.</summary>
		internal static void Capture(Transform decoration)
		{
			Kinds.Clear();
			if (decoration == null)
			{
				return;
			}
			foreach ((string name, bool tower) in Wanted)
			{
				foreach (Transform t in decoration.GetComponentsInChildren<Transform>(true))
				{
					if (!t.name.StartsWith(name, StringComparison.Ordinal) || (name == "Temple" && t.name.StartsWith("TempleTall", StringComparison.Ordinal)))
					{
						continue;
					}
					Renderer[] renderers = t.GetComponentsInChildren<Renderer>(true);
					if (renderers.Length == 0)
					{
						continue;
					}
					Bounds b = renderers[0].bounds;
					foreach (Renderer r in renderers)
					{
						b.Encapsulate(r.bounds);
					}
					if (b.size.x < 1f || b.size.z < 1f || b.size.y < 1f)
					{
						continue;
					}
					Kinds.Add(new Kind { Name = name, Proto = t.gameObject, Size = b.size, Tower = tower });
					break;
				}
			}
			Mod.Log.LogInfo("Tokyo landmarks found: " + (Kinds.Count == 0 ? "none" : string.Join(", ", Kinds.ConvertAll(k => k.Name + " (" + k.Size.x.ToString("0") + " x " + k.Size.z.ToString("0") + ", " + k.Size.y.ToString("0") + " high)"))));
		}

		/// <summary>A tower (or temple) kind from hash <paramref name="h"/>, or -1 if Tokyo had none.</summary>
		internal static int Pick(bool tower, ulong h)
		{
			int count = 0;
			foreach (Kind k in Kinds)
			{
				if (k.Tower == tower)
				{
					count++;
				}
			}
			if (count == 0)
			{
				return -1;
			}
			int n = (int)((h >> 33) % (ulong)count);
			for (int i = 0; i < Kinds.Count; i++)
			{
				if (Kinds[i].Tower == tower && n-- == 0)
				{
					return i;
				}
			}
			return -1;
		}

		/// <summary>Stands a copy of the spot's building in the tile (whose corner is world (tileX, tileZ)), its base on the ground.</summary>
		internal static void Spawn(Transform tile, double tileX, double tileZ, Spot spot)
		{
			if (spot.Kind < 0 || spot.Kind >= Kinds.Count || Kinds[spot.Kind].Proto == null)
			{
				return;
			}
			try
			{
				Kind kind = Kinds[spot.Kind];
				Transform proto = kind.Proto.transform;
				float scale = spot.Side / Mathf.Max(kind.Size.x, kind.Size.z);
				GameObject copy = UnityEngine.Object.Instantiate(kind.Proto);
				copy.name = "Landmark " + kind.Name;
				copy.SetActive(true);
				Transform t = copy.transform;
				t.SetParent(tile, false);
				t.localRotation = Quaternion.Euler(0f, spot.Yaw, 0f) * proto.rotation;
				t.localScale = proto.lossyScale * scale;
				Vector3 target = new Vector3((float)(spot.X - tileX), spot.Y, (float)(spot.Z - tileZ));
				t.localPosition = target;
				// Move it so its bounds stand centred on the spot, their bottom on the ground.
				Bounds b = Measure(copy);
				Vector3 want = tile.TransformPoint(target);
				t.position += new Vector3(want.x - b.center.x, want.y - b.min.y, want.z - b.center.z);
			}
			catch (Exception e)
			{
				Mod.Log.LogWarning("Could not stand a landmark: " + e.Message);
			}
		}

		private static Bounds Measure(GameObject go)
		{
			Renderer[] renderers = go.GetComponentsInChildren<Renderer>(true);
			Bounds b = renderers[0].bounds;
			foreach (Renderer r in renderers)
			{
				b.Encapsulate(r.bounds);
			}
			return b;
		}
	}
}
