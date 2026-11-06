using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace HalfGripOpenWorld
{
	/// <summary>A tree or grass tuft: where it stands (tile-local), which model, turned and scaled.</summary>
	internal struct Plant
	{
		public Vector3 Pos;
		public float Yaw;
		public float Scale;
		public byte Model;
	}

	/// <summary>
	/// The Forest map's pine trees. They are terrain tree prototypes (<c>ForrestTree1..4</c>), loaded only while a scene that uses them
	/// is, so they are picked up when the main menu (whose terrain carries them) loads and kept alive in a hidden holder object. The way
	/// to find and keep them comes from the Highway mod's TreeSource. If none can be found, simple pines made in code stand in.
	/// </summary>
	internal static class Trees
	{
		internal sealed class Part
		{
			public Mesh Mesh;
			public Material[] Materials;
			public Matrix4x4 Local;
		}

		internal sealed class Model
		{
			public string Name;
			public float Height;
			/// <summary>How far the model reaches out from its middle (any way round), in its own units.</summary>
			public float Radius;
			public readonly List<Part> Parts = new List<Part>();
		}

		internal static readonly List<Model> Models = new List<Model>();
		private static GameObject keeper;

		/// <summary>Looks for the Forest's pines among the loaded assets; does nothing once found.</summary>
		internal static void Capture()
		{
			if (Models.Count > 0)
			{
				return;
			}
			try
			{
				List<GameObject> prefabs = new List<GameObject>();
				foreach (Terrain terrain in Resources.FindObjectsOfTypeAll<Terrain>())
				{
					if (terrain.terrainData == null)
					{
						continue;
					}
					foreach (TreePrototype proto in terrain.terrainData.treePrototypes)
					{
						if (proto.prefab != null && proto.prefab.name.StartsWith("ForrestTree", StringComparison.Ordinal) && !prefabs.Contains(proto.prefab))
						{
							prefabs.Add(proto.prefab);
						}
					}
				}
				if (prefabs.Count == 0)
				{
					foreach (GameObject go in Resources.FindObjectsOfTypeAll<GameObject>())
					{
						if (go.name.StartsWith("ForrestTree", StringComparison.Ordinal) && go.transform.parent == null && !prefabs.Contains(go))
						{
							prefabs.Add(go);
						}
					}
				}
				if (prefabs.Count == 0)
				{
					return;
				}
				prefabs.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
				keeper = new GameObject("OpenWorldTreeKeeper") { hideFlags = HideFlags.HideAndDontSave };
				keeper.SetActive(false);
				foreach (GameObject prefab in prefabs)
				{
					Model model = Read(prefab);
					if (model != null)
					{
						Models.Add(model);
					}
				}
				Mod.Log.LogInfo("Forest trees captured: " + string.Join(", ", Models.Select(m => m.Name + " (" + m.Height.ToString("0.0") + " high, " + m.Parts.Count + " parts)").ToArray()));
			}
			catch (Exception e)
			{
				Mod.Log.LogWarning("Could not read the Forest's trees: " + e.Message);
			}
		}

		private static Model Read(GameObject prefab)
		{
			Renderer[] renderers;
			LODGroup lod = prefab.GetComponent<LODGroup>();
			if (lod != null && lod.lodCount > 0)
			{
				renderers = lod.GetLODs()[0].renderers;
			}
			else
			{
				renderers = prefab.GetComponentsInChildren<MeshRenderer>(true).Where(r => !r.name.Contains("illboard")).Cast<Renderer>().ToArray();
			}
			Matrix4x4 toRoot = prefab.transform.worldToLocalMatrix;
			Model model = new Model { Name = prefab.name };
			Vector3 min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
			Vector3 max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
			foreach (Renderer r in renderers)
			{
				if (!(r is MeshRenderer mr) || !mr.TryGetComponent(out MeshFilter filter) || filter.sharedMesh == null)
				{
					continue;
				}
				Matrix4x4 m = toRoot * r.transform.localToWorldMatrix;
				Bounds b = filter.sharedMesh.bounds;
				for (int i = 0; i < 8; i++)
				{
					Vector3 corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
					Vector3 w = m.MultiplyPoint3x4(corner);
					min = Vector3.Min(min, w);
					max = Vector3.Max(max, w);
				}
				model.Parts.Add(new Part { Mesh = filter.sharedMesh, Materials = Instanced(mr.sharedMaterials), Local = m });
				// Kept alive by a hidden renderer, so the mesh and its materials outlive the menu.
				GameObject holder = new GameObject(prefab.name + "_" + filter.sharedMesh.name);
				holder.transform.SetParent(keeper.transform, false);
				holder.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
				holder.AddComponent<MeshRenderer>().sharedMaterials = mr.sharedMaterials;
			}
			model.Height = max.y - min.y;
			model.Radius = 0.5f * Mathf.Sqrt((max.x - min.x) * (max.x - min.x) + (max.z - min.z) * (max.z - min.z));
			if (model.Parts.Count == 0 || model.Height < 0.01f)
			{
				return null;
			}
			// Stand it on the ground, centred over its origin.
			Matrix4x4 shift = Matrix4x4.Translate(-new Vector3((min.x + max.x) * 0.5f, min.y, (min.z + max.z) * 0.5f));
			foreach (Part part in model.Parts)
			{
				part.Local = shift * part.Local;
			}
			return model;
		}

		/// <summary>Copies of the materials with GPU instancing on (the originals are the game's, left alone).</summary>
		internal static Material[] Instanced(Material[] materials)
		{
			Material[] copies = new Material[materials.Length];
			for (int i = 0; i < materials.Length; i++)
			{
				if (materials[i] != null)
				{
					copies[i] = new Material(materials[i]) { enableInstancing = true, hideFlags = HideFlags.HideAndDontSave };
				}
			}
			return copies;
		}

		/// <summary>Simple pines (a trunk and three stacked cones) for when the Forest's could not be found.</summary>
		internal static void Fallback(Shader shader)
		{
			if (Models.Count > 0)
			{
				return;
			}
			Material bark = Foliage.Plain(shader, new Color(0.35f, 0.25f, 0.17f));
			Material needles = Foliage.Plain(shader, new Color(0.18f, 0.34f, 0.18f));
			for (int k = 0; k < 2; k++)
			{
				Model model = new Model { Name = "Pine" + (k + 1), Height = 30f, Radius = 7f };
				model.Parts.Add(new Part { Mesh = Foliage.Cone(1.1f, 9f, 0f, 6), Materials = new[] { bark }, Local = Matrix4x4.identity });
				for (int c = 0; c < 3; c++)
				{
					float bottom = 6f + c * 7f, radius = (k == 0 ? 7f : 5.5f) - c * 1.8f;
					model.Parts.Add(new Part { Mesh = Foliage.Cone(radius, 11f, bottom, 7), Materials = new[] { needles }, Local = Matrix4x4.identity });
				}
				Models.Add(model);
			}
			Mod.Log.LogWarning("The Forest's trees were not found (the main menu has not been loaded?); simple pines stand in.");
		}
	}

	/// <summary>
	/// Trees and grass. Each tile's are worked out on its worker thread from its ground texture's own pixels (<see cref="Scatter"/>): one
	/// try per pixel, with a chance by what the ground is (dense in woods, a few in hedges and gardens, the odd one in the open). They are
	/// drawn GPU-instanced, so tens of thousands cost a few hundred draw calls. Each tile's trees are drawn in their own calls with that
	/// tile's bounds: Unity culls an instanced call only as a whole, so one list of every loaded tree (as before 0.21.1) was drawn in
	/// full every frame, behind the camera and in every shadow cascade, which near the rally stage (36 000 trees loaded) cost most of the
	/// frame. A tile's draw lists are made once, when it comes, and only moved when the origin shifts; grass is made for the tiles near
	/// the car only. Trees past <see cref="Mod.TreeShadowReach"/> tiles cast no shadows.
	/// </summary>
	internal sealed class Foliage : MonoBehaviour
	{
		/// <summary>Grass materials: lawn and pasture green, meadow, ripe wheat, upland moor.</summary>
		internal const int GrassKinds = 4;


		private static readonly ulong Salt = Noise.Mix(WorldGen.Seed + 701);

		private sealed class TileFoliage
		{
			public Transform Tile;
			public int Tx;
			public int Tz;
			public Plant[] Grass;
			/// <summary>Each tree draw's instances (as <see cref="treeDraws"/>), in the scene's coordinates as of <see cref="PlacedAt"/>.</summary>
			public Matrix4x4[][] TreeMatrices;
			/// <summary>Each grass kind's instances, made while the tile is near the car and dropped when it is not.</summary>
			public Matrix4x4[][] GrassMatrices;
			/// <summary>The tile's position the matrices were made for; when the origin shifts the tile moves and they follow.</summary>
			public Vector3 PlacedAt;
			/// <summary>Bounds round the trees and the grass, relative to the tile.</summary>
			public Bounds TreeBox;
			public Bounds GrassBox;
		}

		private readonly Dictionary<long, TileFoliage> tiles = new Dictionary<long, TileFoliage>();
		private readonly List<(Mesh mesh, int sub, Material material)> treeDraws = new List<(Mesh, int, Material)>();
		private Mesh tuft;
		private Material[] grassMaterials;
		private int carTx = int.MinValue, carTz = int.MinValue;
		private bool broken;
		/// <summary>Where each tree model's draws start in <see cref="treeDraws"/>.</summary>
		private int[] modelStart = new int[0];

		/// <summary>The most instances one call takes.</summary>
		private const int Batch = 1023;

		/// <summary>The height a tree of scale 1 is drawn at, whatever its model's own size: about 9 m.</summary>
		private const float TreeHeight = 30f;

		/// <summary>The grass made for this race (the trees' materials are the game's, or kept for the session by <see cref="Trees"/>).</summary>
		private void OnDestroy()
		{
			if (grassMaterials != null)
			{
				WorldAssets.Free(grassMaterials);
			}
			if (tuft != null)
			{
				Destroy(tuft);
			}
		}

		internal void Setup(Shader shader)
		{
			Trees.Fallback(shader);
			tuft = Tuft();
			grassMaterials = new[]
			{
				Grass(shader, new Color(0.30f, 0.48f, 0.20f)),
				Grass(shader, new Color(0.40f, 0.58f, 0.26f)),
				Grass(shader, new Color(0.80f, 0.66f, 0.30f)),
				Grass(shader, new Color(0.47f, 0.47f, 0.27f)),
			};
			modelStart = new int[Trees.Models.Count];
			for (int m = 0; m < Trees.Models.Count; m++)
			{
				Trees.Model model = Trees.Models[m];
				modelStart[m] = treeDraws.Count;
				foreach (Trees.Part part in model.Parts)
				{
					for (int sub = 0; sub < part.Mesh.subMeshCount && sub < part.Materials.Length; sub++)
					{
						treeDraws.Add((part.Mesh, sub, part.Materials[sub]));
					}
				}
			}
		}

		/// <summary>A tree's drawn height (scale 1 is <see cref="TreeHeight"/>), for its trunk collider.</summary>
		internal static float Tall(Plant p)
		{
			return p.Scale * TreeHeight;
		}

		internal void Add(long key, Transform tile, int tx, int tz, Plant[] trees, Plant[] grass)
		{
			TileFoliage t = new TileFoliage { Tile = tile, Tx = tx, Tz = tz, Grass = grass, PlacedAt = tile.position };
			t.TreeMatrices = TreeMatrices(trees, t.PlacedAt, out t.TreeBox);
			t.GrassBox = Box(grass, 1.5f, 4f);
			tiles[key] = t;
		}

		internal void Remove(long key)
		{
			tiles.Remove(key);
		}

		/// <summary>The car's tile, each frame: the grass and the trees' shadows follow it.</summary>
		internal void Follow(int tx, int tz)
		{
			carTx = tx;
			carTz = tz;
		}

		/// <summary>A tile's trees as each draw's instances, placed at the tile's position <paramref name="at"/>; and their bounds.</summary>
		private Matrix4x4[][] TreeMatrices(Plant[] trees, Vector3 at, out Bounds box)
		{
			box = new Bounds();
			if (trees == null || trees.Length == 0 || Trees.Models.Count == 0)
			{
				return null;
			}
			int[] counts = new int[treeDraws.Count];
			foreach (Plant p in trees)
			{
				int index = p.Model % Trees.Models.Count;
				int k = modelStart[index];
				foreach (Trees.Part part in Trees.Models[index].Parts)
				{
					int subs = Math.Min(part.Mesh.subMeshCount, part.Materials.Length);
					for (int sub = 0; sub < subs; sub++)
					{
						counts[k + sub]++;
					}
					k += subs;
				}
			}
			Matrix4x4[][] lists = new Matrix4x4[treeDraws.Count][];
			for (int k = 0; k < lists.Length; k++)
			{
				lists[k] = counts[k] > 0 ? new Matrix4x4[counts[k]] : null;
				counts[k] = 0;
			}
			Vector3 min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
			Vector3 max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
			foreach (Plant p in trees)
			{
				int index = p.Model % Trees.Models.Count;
				Trees.Model model = Trees.Models[index];
				float scale = p.Scale * TreeHeight / model.Height;
				Matrix4x4 place = Matrix4x4.TRS(at + p.Pos, Quaternion.Euler(0f, p.Yaw, 0f), Vector3.one * scale);
				int k = modelStart[index];
				foreach (Trees.Part part in model.Parts)
				{
					Matrix4x4 full = place * part.Local;
					int subs = Math.Min(part.Mesh.subMeshCount, part.Materials.Length);
					for (int sub = 0; sub < subs; sub++)
					{
						lists[k + sub][counts[k + sub]++] = full;
					}
					k += subs;
				}
				// The crown's reach round the trunk, with room for the waving.
				float r = model.Radius * scale + 2f;
				min = Vector3.Min(min, p.Pos - new Vector3(r, 1f, r));
				max = Vector3.Max(max, p.Pos + new Vector3(r, p.Scale * TreeHeight + 2f, r));
			}
			box.SetMinMax(min, max);
			return lists;
		}

		/// <summary>Bounds round plants, <paramref name="side"/> out to each side and <paramref name="up"/> above their feet.</summary>
		private static Bounds Box(Plant[] plants, float side, float up)
		{
			Bounds box = new Bounds();
			if (plants == null || plants.Length == 0)
			{
				return box;
			}
			Vector3 min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
			Vector3 max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
			foreach (Plant p in plants)
			{
				min = Vector3.Min(min, p.Pos);
				max = Vector3.Max(max, p.Pos);
			}
			box.SetMinMax(min - new Vector3(side, 1f, side), max + new Vector3(side, up, side));
			return box;
		}

		/// <summary>A near tile's grass as each kind's instances, placed at the tile's position <paramref name="at"/>.</summary>
		private static Matrix4x4[][] GrassMatrices(Plant[] grass, Vector3 at)
		{
			int[] counts = new int[GrassKinds];
			foreach (Plant p in grass)
			{
				counts[p.Model]++;
			}
			Matrix4x4[][] lists = new Matrix4x4[GrassKinds][];
			for (int k = 0; k < GrassKinds; k++)
			{
				lists[k] = counts[k] > 0 ? new Matrix4x4[counts[k]] : null;
				counts[k] = 0;
			}
			foreach (Plant p in grass)
			{
				lists[p.Model][counts[p.Model]++] = Matrix4x4.TRS(at + p.Pos, Quaternion.Euler(0f, p.Yaw, 0f), Vector3.one * p.Scale);
			}
			return lists;
		}

		/// <summary>Moves a tile's instances by <paramref name="delta"/> (the origin shifted; heights never change, but y costs nothing).</summary>
		private static void Move(Matrix4x4[][] lists, Vector3 delta)
		{
			if (lists == null)
			{
				return;
			}
			foreach (Matrix4x4[] list in lists)
			{
				if (list == null)
				{
					continue;
				}
				for (int i = 0; i < list.Length; i++)
				{
					list[i].m03 += delta.x;
					list[i].m13 += delta.y;
					list[i].m23 += delta.z;
				}
			}
		}

		private void LateUpdate()
		{
			if (broken)
			{
				return;
			}
			try
			{
				int reach = Mod.GrassReach.Value;
				int shadows = Mod.TreeShadowReach.Value;
				foreach (TileFoliage t in tiles.Values)
				{
					if (t.Tile == null)
					{
						continue;
					}
					Vector3 at = t.Tile.position;
					if (at != t.PlacedAt)
					{
						Move(t.TreeMatrices, at - t.PlacedAt);
						Move(t.GrassMatrices, at - t.PlacedAt);
						t.PlacedAt = at;
					}
					int near = Math.Max(Math.Abs(t.Tx - carTx), Math.Abs(t.Tz - carTz));
					if (t.TreeMatrices != null)
					{
						Bounds box = new Bounds(t.TreeBox.center + at, t.TreeBox.size);
						ShadowCastingMode cast = near <= shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
						for (int k = 0; k < treeDraws.Count; k++)
						{
							Draw(treeDraws[k].mesh, treeDraws[k].sub, treeDraws[k].material, t.TreeMatrices[k], box, cast);
						}
					}
					if (t.Grass != null && t.Grass.Length > 0 && near <= reach)
					{
						t.GrassMatrices ??= GrassMatrices(t.Grass, at);
						Bounds box = new Bounds(t.GrassBox.center + at, t.GrassBox.size);
						for (int k = 0; k < GrassKinds; k++)
						{
							Draw(tuft, 0, grassMaterials[k], t.GrassMatrices[k], box, ShadowCastingMode.Off);
						}
					}
					else
					{
						t.GrassMatrices = null;
					}
				}
			}
			catch (Exception e)
			{
				// A material that cannot be instanced: stop drawing rather than fail every frame.
				broken = true;
				Mod.Log.LogWarning("Trees and grass cannot be drawn instanced here, so they are off: " + e.Message);
			}
		}

		/// <summary>
		/// One draw's instances of a tile, with the tile's bounds, so Unity leaves the whole call out for a camera (or a shadow cascade)
		/// that cannot see the tile.
		/// </summary>
		private static void Draw(Mesh mesh, int sub, Material material, Matrix4x4[] list, Bounds box, ShadowCastingMode shadows)
		{
			if (mesh == null || material == null || list == null)
			{
				return;
			}
			RenderParams rp = new RenderParams(material)
			{
				worldBounds = box,
				shadowCastingMode = shadows,
				receiveShadows = true,
				layer = 0,
			};
			for (int start = 0; start < list.Length; start += Batch)
			{
				Graphics.RenderMeshInstanced(rp, mesh, sub, list, Math.Min(Batch, list.Length - start), start);
			}
		}

		/// <summary>
		/// A tile's trees and grass from its ground pixels: one try per pixel, a chance by the pixel's cover, a place within the pixel,
		/// a turn and a size, all from hashing the pixel's world position (so the same on every machine and every time). None on a road,
		/// in water or on a building.
		/// </summary>
		internal static void Scatter(int tx, int tz, float tileSize, int pixels, Cover[] covers, float[] clearance, float[] heights, int side, float step, List<(double x, double z, double r)> buildings, out Plant[] trees, out Plant[] grass)
		{
			List<Plant> t = new List<Plant>();
			List<Plant> g = new List<Plant>();
			float pixel = tileSize / pixels;
			double ox = tx * (double)tileSize, oz = tz * (double)tileSize;
			// The ground as drawn (its triangles), for keeping out of the water: the bilinear height plants stand on is up to a unit or
			// two higher at a channel's edge.
			TerrainGrid drawn = new TerrainGrid(ox - step, oz - step, side, heights);
			for (int pj = 0; pj < pixels; pj++)
			{
				for (int pi = 0; pi < pixels; pi++)
				{
					Cover cover = covers[pj * pixels + pi];
					ulong h = Noise.Mix(Salt ^ (ulong)(long)(tx * pixels + pi) * 0x9E3779B97F4A7C15UL ^ (ulong)(long)(tz * pixels + pj) * 0xBF58476D1CE4E5B9UL);
					double treeChance = cover == Cover.Park ? -1.0 : TreeChance(cover);
					double grassChance = GrassChance(cover);
					if (treeChance <= 0.0 && grassChance <= 0.0)
					{
						continue;
					}
					float lx = (pi + U(h, 1)) * pixel, lz = (pj + U(h, 2)) * pixel;
					double wx = ox + lx, wz = oz + lz;
					if (treeChance < 0.0)
					{
						// A park's trees stand in clumps.
						treeChance = Parks.TreeChance(wx, wz);
					}
					bool tree = U(h, 0) < treeChance;
					if (!tree && U(h, 3) >= grassChance)
					{
						continue;
					}
					// Off the roads, their verges and the farm tracks (a tree well off, its crown too; grass anywhere in its pixel, which is
					// up to 2.8 from the pixel's centre the clearance is measured at), off the buildings, and off the car parks and yards.
					if (clearance[pj * pixels + pi] < (tree ? 6f : 2.8f) || OnBuilding(wx, wz, buildings) || Town.Taken(wx, wz, tree ? 4.0 : 0.5) || Circuit.Covered(wx, wz, tree ? 6.0 : 1.0) || Rally.Covered(wx, wz, tree ? 4.0 : 0.5) || HillClimb.Covered(wx, wz, tree ? 4.0 : 0.5))
					{
						continue;
					}
					float y = Height(heights, side, step, lx, lz);
					if (Wet(wx, wz, drawn.At(wx, wz), tree ? 1.0 : 0.3))
					{
						continue;
					}
					if (tree)
					{
						t.Add(new Plant { Pos = new Vector3(lx, y - 0.3f, lz), Yaw = U(h, 4) * 360f, Scale = 0.75f + U(h, 5) * 0.6f, Model = (byte)(h >> 56) });
					}
					else
					{
						int kind = cover == Cover.Meadow ? 1 : cover == Cover.Wheat ? 2 : cover == Cover.Moor ? 3 : 0;
						float size = cover == Cover.Wheat ? 1.6f : 1f;
						g.Add(new Plant { Pos = new Vector3(lx, y - 0.1f, lz), Yaw = U(h, 4) * 360f, Scale = size * (0.8f + U(h, 5) * 0.6f), Model = (byte)kind });
					}
				}
			}
			trees = t.ToArray();
			grass = g.ToArray();
		}

		/// <summary>
		/// Whether ground at scene height <paramref name="y"/> at (x, z) is under water drawn over it, or within <paramref name="margin"/>
		/// of it: the ground's covers come from the land use, which knows the floodplain but not the channel, so river beds and the
		/// shallows round lakes were full of grass and trees standing in the water.
		/// </summary>
		private static bool Wet(double x, double z, double y, double margin)
		{
			double water = Rivers.DrawnWater(x, z);
			return !double.IsNaN(water) && water + Network.Lift > y - margin;
		}

		private static double TreeChance(Cover c)
		{
			switch (c)
			{
				case Cover.Forest: return 0.12;
				case Cover.Hedge: return 0.06;
				case Cover.Garden: return 0.025;
				case Cover.Meadow: return 0.008;
				case Cover.Grass: return 0.01;
				case Cover.Moor: return 0.004;
				case Cover.Pasture: return 0.002;
				default: return 0.0;
			}
		}

		private static double GrassChance(Cover c)
		{
			switch (c)
			{
				case Cover.Grass: return 0.7;
				case Cover.Meadow: return 0.85;
				case Cover.Pasture: return 0.8;
				case Cover.Garden: return 0.4;
				case Cover.Moor: return 0.6;
				case Cover.Wheat: return 0.95;
				case Cover.Crop: return 0.5;
				case Cover.Hedge: return 0.6;
				case Cover.Forest: return 0.2;
				case Cover.Yard: return 0.15;
				// Mown short.
				case Cover.Park: return 0.12;
				case Cover.Circuit: return 0.1;
				default: return 0.0;
			}
		}

		private static bool OnBuilding(double x, double z, List<(double x, double z, double r)> buildings)
		{
			foreach ((double bx, double bz, double r) in buildings)
			{
				if ((bx - x) * (bx - x) + (bz - z) * (bz - z) < (r + 2.0) * (r + 2.0))
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>The tile's ground height at tile-local (lx, lz), from its height samples (the grid starts one step before the tile).</summary>
		private static float Height(float[] h, int side, float step, float lx, float lz)
		{
			float gx = lx / step + 1f, gz = lz / step + 1f;
			int i = Math.Min(side - 2, (int)gx), j = Math.Min(side - 2, (int)gz);
			float fx = gx - i, fz = gz - j;
			float a = h[j * side + i], b = h[j * side + i + 1], c = h[(j + 1) * side + i], d = h[(j + 1) * side + i + 1];
			return (a + (b - a) * fx) + ((c + (d - c) * fx) - (a + (b - a) * fx)) * fz;
		}

		private static float U(ulong h, int k)
		{
			return (float)((Noise.Mix(h + (ulong)k) >> 40) * (1.0 / 16777216.0));
		}

		// ---- meshes and materials ------------------------------------------------------------------------------------

		/// <summary>A tuft of five grass blades, each a thin upright triangle drawn on both sides, about 2.5 high.</summary>
		private static Mesh Tuft()
		{
			List<Vector3> v = new List<Vector3>();
			List<Vector2> uv = new List<Vector2>();
			List<int> t = new List<int>();
			for (int k = 0; k < 5; k++)
			{
				float a = k * 1.2566f + 0.3f;
				Vector3 at = new Vector3(Mathf.Cos(a * 2.1f), 0f, Mathf.Sin(a * 2.1f)) * 0.6f;
				Vector3 across = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.35f;
				Vector3 lean = new Vector3(Mathf.Cos(a + 1.5f), 0f, Mathf.Sin(a + 1.5f)) * 0.5f;
				int i = v.Count;
				v.Add(at - across);
				v.Add(at + across);
				v.Add(at + lean + Vector3.up * (2.0f + 0.25f * k));
				uv.Add(new Vector2(0f, 0f));
				uv.Add(new Vector2(1f, 0f));
				uv.Add(new Vector2(0.5f, 1f));
				t.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 1 });
			}
			Mesh mesh = new Mesh { name = "OpenWorld Grass Tuft" };
			mesh.SetVertices(v);
			mesh.SetUVs(0, uv);
			mesh.SetTriangles(t, 0);
			// Blades lit as if they faced up, so both sides look alike.
			Vector3[] normals = new Vector3[v.Count];
			for (int k = 0; k < normals.Length; k++)
			{
				normals[k] = Vector3.up;
			}
			mesh.normals = normals;
			mesh.RecalculateBounds();
			return mesh;
		}

		/// <summary>A grass material: dark at the root, lighter to the tip.</summary>
		private static Material Grass(Shader shader, Color colour)
		{
			Texture2D t = new Texture2D(1, 4, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
			for (int y = 0; y < 4; y++)
			{
				t.SetPixel(0, y, colour * (0.7f + 0.12f * y));
			}
			t.Apply();
			Material m = Plain(shader, Color.white);
			if (m.HasProperty("_BaseMap"))
			{
				m.SetTexture("_BaseMap", t);
			}
			if (m.HasProperty("_MainTex"))
			{
				m.SetTexture("_MainTex", t);
			}
			if (m.HasProperty("_Cull"))
			{
				m.SetFloat("_Cull", 0f);
			}
			return m;
		}

		/// <summary>A plain lit material in one colour, instancing on.</summary>
		internal static Material Plain(Shader shader, Color colour)
		{
			Material m = new Material(shader) { enableInstancing = true, hideFlags = HideFlags.HideAndDontSave };
			if (m.HasProperty("_BaseColor"))
			{
				m.SetColor("_BaseColor", colour);
			}
			if (m.HasProperty("_Color"))
			{
				m.SetColor("_Color", colour);
			}
			if (m.HasProperty("_Smoothness"))
			{
				m.SetFloat("_Smoothness", 0.05f);
			}
			return m;
		}

		/// <summary>A cone (or, with radius at the tip, nothing more than a cone): <paramref name="sides"/> faces round, standing at <paramref name="bottom"/>.</summary>
		internal static Mesh Cone(float radius, float height, float bottom, int sides)
		{
			List<Vector3> v = new List<Vector3>();
			List<Vector3> n = new List<Vector3>();
			List<int> t = new List<int>();
			for (int k = 0; k < sides; k++)
			{
				float a0 = k * 2f * Mathf.PI / sides, a1 = (k + 1) * 2f * Mathf.PI / sides;
				Vector3 p0 = new Vector3(Mathf.Cos(a0) * radius, bottom, Mathf.Sin(a0) * radius);
				Vector3 p1 = new Vector3(Mathf.Cos(a1) * radius, bottom, Mathf.Sin(a1) * radius);
				Vector3 tip = new Vector3(0f, bottom + height, 0f);
				int i = v.Count;
				v.Add(p0);
				v.Add(tip);
				v.Add(p1);
				Vector3 normal = Vector3.Cross(tip - p0, p1 - p0).normalized;
				if (Vector3.Dot(normal, p0 + p1) < 0f)
				{
					normal = -normal;
				}
				n.Add(normal);
				n.Add(normal);
				n.Add(normal);
				if (Vector3.Dot(Vector3.Cross(v[i + 1] - v[i], v[i + 2] - v[i]), normal) >= 0f)
				{
					t.AddRange(new[] { i, i + 1, i + 2 });
				}
				else
				{
					t.AddRange(new[] { i, i + 2, i + 1 });
				}
			}
			Mesh mesh = new Mesh { name = "OpenWorld Cone" };
			mesh.SetVertices(v);
			mesh.SetNormals(n);
			mesh.SetTriangles(t, 0);
			mesh.RecalculateBounds();
			return mesh;
		}
	}
}
