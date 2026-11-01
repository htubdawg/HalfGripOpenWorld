using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace HalfGripOpenWorld
{
	/// <summary>
	/// The far view: the whole world drawn at low detail past the loaded tiles, from <see cref="FarGrid"/>, so the mountains, the coast,
	/// the woods and the towns are seen to the horizon instead of fog at the tiles' edge. With it on, <see cref="Sky"/> sets how far
	/// one can see from the weather (far on a clear day, close in the rain) instead of from the tile radius, and the cameras draw that far.
	///
	/// The world is cut into chunks of <see cref="ChunkSize"/> (8 by 8 tiles), each one mesh with no collider and no shadows, drawn with
	/// one material and one picture of the whole world. A chunk near the car takes every sample of the grid (50 units apart); further
	/// out every second, fourth or eighth. Where a tile is loaded the chunk leaves its cells out, so the far view never lies over or
	/// pokes through the real ground: the hole's edge is on the tile's own vertices, and a short skirt hangs from it to hide the gap
	/// where the tile's finer ground dips under the far view's straight edge. Skirts also hang round every chunk, where a chunk of one
	/// detail meets one of another. A chunk is remade when the tiles loaded in it change (in the same frame, from
	/// <see cref="Streamer"/>), or a few a frame when its detail changes.
	///
	/// Coming on, it is made over some frames before it is shown: the chunks nearest the car first, a share a frame
	/// (<see cref="WarmBudget"/>), and the picture's mipmaps on a worker. Before 0.20.1 all 256 chunks, the picture and its mipmaps were
	/// made in the one frame it came on, a hitch of tens of milliseconds in the middle of a race.
	///
	/// The sea south of the world goes on to the horizon as one flat sheet. The chunks are children of the world's root, so the floating
	/// origin moves them with everything else.
	/// </summary>
	internal sealed class FarView : MonoBehaviour
	{
		/// <summary>A chunk's side: 8 tiles, 40 samples.</summary>
		private const double ChunkSize = 2000.0;

		private const int ChunkTiles = 8;

		/// <summary>Grid samples between a chunk's vertices at each detail, and how far from the car each detail reaches (past the last, the
		/// coarsest). The finest must reach past the furthest loaded tile (TileRadius 10 and its slack, 2875), as only it can leave a tile out.</summary>
		private static readonly int[] LodStep = { 1, 2, 4, 8 };
		private static readonly double[] LodReach = { 3500.0, 6500.0, 11000.0 };

		/// <summary>Chunks remade a frame for a change of detail alone.</summary>
		private const int RemakesPerFrame = 3;

		/// <summary>While the far view is being made, before it is shown: cells made a frame (a chunk at the finest detail has 1600, at the
		/// coarsest 25), so about two of the nearest chunks a frame, all 256 in some twenty frames.</summary>
		private const int WarmBudget = 3200;

		internal static FarView Instance;

		private sealed class Chunk
		{
			internal GameObject Go;
			internal Mesh Mesh;
			internal int Lod = -1;
			internal ulong Mask;
			/// <summary>False when every cell is left out (all its tiles loaded): the chunk is then hidden.</summary>
			internal bool Any;
		}

		private Material material;
		private Material seaMaterial;
		private Texture2D texture;
		private Texture2D seaTexture;
		private int layer;
		private int chunks;
		private Chunk[] grid;
		private GameObject sea;
		private Mesh seaMesh;
		private readonly List<Camera> cameras = new List<Camera>();
		private bool shown;

		/// <summary>True once every chunk has been made (see <see cref="Warm"/>); the far view is shown only then.</summary>
		private bool built;

		/// <summary>The picture's mipmaps being made on a worker (see <see cref="Mips"/>), before the texture is made.</summary>
		private Task<byte[][]> mipping;

		private readonly List<int> order = new List<int>();

		// Reused while making a mesh.
		private readonly List<Vector3> vertices = new List<Vector3>();
		private readonly List<Vector3> normals = new List<Vector3>();
		private readonly List<Vector2> uvs = new List<Vector2>();
		private readonly List<int> triangles = new List<int>();

		/// <summary>How far the cameras draw the world (the far view's meshes), set by <see cref="Sky"/> with the fog each frame; 0 when the
		/// far view is not showing. Past it, with the skyline on, the land is painted into the sky (<see cref="Skyline"/>).</summary>
		internal static float DrawnReach;

		/// <summary>True while the far view is drawn: on in the config and its grid built. <see cref="Sky"/> then sets the fog's reach.</summary>
		internal static bool Showing => Instance != null && Instance.shown;

		/// <summary>The far view's material: the ground's look with the whole world's picture on it (set once the grid is built). Made before
		/// <see cref="Sky"/> is set up, so the wet weather darkens it as it does the tiles' ground.</summary>
		internal static Material MakeMaterial(Material ground, string name)
		{
			Material m = new Material(ground) { name = name, hideFlags = HideFlags.HideAndDontSave };
			return m;
		}

		internal void Setup(Material far, Material farSea, int groundLayer)
		{
			Instance = this;
			material = far;
			seaMaterial = farSea;
			layer = groundLayer;
			chunks = (int)Math.Round(WorldGen.Half * 2.0 / ChunkSize);
			grid = new Chunk[chunks * chunks];
			foreach (Camera camera in FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
			{
				Note(camera);
			}
		}

		/// <summary>Called by <see cref="Streamer"/> at the end of its frame's streaming, with the car at world (x, z).</summary>
		internal void Follow(double x, double z, Streamer streamer)
		{
			bool want = Mod.ShowFarView.Value && FarGrid.Ready;
			if (want && texture == null)
			{
				Prepare();
			}
			if (want && !built)
			{
				Warm(x, z, streamer);
			}
			bool on = want && texture != null && built;
			if (on != shown)
			{
				shown = on;
				if (sea != null)
				{
					sea.SetActive(on);
				}
				foreach (Chunk c in grid)
				{
					if (c != null && c.Go != null)
					{
						c.Go.SetActive(on && c.Any);
					}
				}
				Mod.Log.LogInfo("Far view " + (on ? "on (the world's grid built in " + FarGrid.BuildMs.ToString("0") + " ms)" : "off"));
			}
			if (!on)
			{
				return;
			}
			int budget = RemakesPerFrame;
			for (int cj = 0; cj < chunks; cj++)
			{
				for (int ci = 0; ci < chunks; ci++)
				{
					Chunk c = grid[cj * chunks + ci] ??= new Chunk();
					Want(ci, cj, x, z, streamer, out int lod, out ulong mask);
					if (c.Lod == lod && c.Mask == mask)
					{
						continue;
					}
					// The tiles changing must be followed at once (a hole left open shows the sky); a change of detail can wait a frame or two.
					if (c.Mask == mask && c.Lod >= 0 && budget <= 0)
					{
						continue;
					}
					if (c.Mask == mask && c.Lod >= 0)
					{
						budget--;
					}
					Remake(c, ci, cj, lod, mask);
				}
			}
		}

		/// <summary>
		/// Makes the chunks not made yet, the nearest the car first, until <see cref="WarmBudget"/> cells are spent this frame; sets
		/// <see cref="built"/> once every chunk is made. Hidden until then (<see cref="Make"/> shows a chunk only while the far view is).
		/// </summary>
		private void Warm(double x, double z, Streamer streamer)
		{
			order.Clear();
			for (int k = 0; k < grid.Length; k++)
			{
				if (grid[k] == null || grid[k].Lod < 0)
				{
					order.Add(k);
				}
			}
			order.Sort((a, b) =>
			{
				int byDistance = Distance(a % chunks, a / chunks, x, z).CompareTo(Distance(b % chunks, b / chunks, x, z));
				return byDistance != 0 ? byDistance : a.CompareTo(b);
			});
			int spent = 0, made = 0;
			foreach (int k in order)
			{
				if (spent >= WarmBudget)
				{
					break;
				}
				int ci = k % chunks, cj = k / chunks;
				Chunk c = grid[k] ??= new Chunk();
				Want(ci, cj, x, z, streamer, out int lod, out ulong mask);
				Remake(c, ci, cj, lod, mask);
				int cells = (int)Math.Round(ChunkSize / FarGrid.Step) / LodStep[lod];
				spent += cells * cells;
				made++;
			}
			built = made == order.Count;
		}

		/// <summary>How far chunk (ci, cj) is from world (x, z): 0 inside it.</summary>
		private static double Distance(int ci, int cj, double x, double z)
		{
			double x0 = -WorldGen.Half + ci * ChunkSize, z0 = -WorldGen.Half + cj * ChunkSize;
			double dx = Math.Max(0.0, Math.Max(x0 - x, x - (x0 + ChunkSize)));
			double dz = Math.Max(0.0, Math.Max(z0 - z, z - (z0 + ChunkSize)));
			return Math.Sqrt(dx * dx + dz * dz);
		}

		/// <summary>The detail chunk (ci, cj) should be drawn at with the car at world (x, z), and, at the finest, the tiles loaded in it to
		/// leave out.</summary>
		private void Want(int ci, int cj, double x, double z, Streamer streamer, out int lod, out ulong mask)
		{
			double d = Distance(ci, cj, x, z);
			lod = LodReach.Length;
			for (int k = 0; k < LodReach.Length; k++)
			{
				if (d < LodReach[k])
				{
					lod = k;
					break;
				}
			}
			mask = 0UL;
			if (lod == 0)
			{
				double x0 = -WorldGen.Half + ci * ChunkSize, z0 = -WorldGen.Half + cj * ChunkSize;
				int tx0 = (int)Math.Round(x0 / Streamer.TileSize), tz0 = (int)Math.Round(z0 / Streamer.TileSize);
				for (int b = 0; b < ChunkTiles * ChunkTiles; b++)
				{
					if (streamer.IsLoaded(tx0 + b % ChunkTiles, tz0 + b / ChunkTiles))
					{
						mask |= 1UL << b;
					}
				}
			}
		}

		/// <summary><see cref="Make"/>, a failure logged and the chunk taken as made (so it is not tried again every frame).</summary>
		private void Remake(Chunk c, int ci, int cj, int lod, ulong mask)
		{
			try
			{
				Make(c, ci, cj, lod, mask);
			}
			catch (Exception e)
			{
				Mod.Log.LogWarning("Far view: chunk " + ci + "," + cj + " could not be made: " + e.Message);
				c.Lod = lod;
				c.Mask = mask;
			}
		}

		/// <summary>
		/// Keeps a camera in <see cref="cameras"/> if it draws the world: not the cockpit's (on the INTERIOR layer, which draws only the
		/// cockpit and keeps its own short reach for depth precision), not the rear-view mirror's (the game's render budget sets its reach),
		/// not one drawing into a texture. The Tokyo scene has three that draw the world: the Main Camera, the OutsideCamera and the dev
		/// FreeFlightCamera (switched on later, its reach Tokyo's 1500, so the far view stopped at the tiles in it before 0.19.1).
		/// </summary>
		private void Note(Camera camera)
		{
			if (camera == null || cameras.Contains(camera) || camera.targetTexture != null || camera.name == "RearViewCamera" || camera.name == "InteriorCamera"
				|| camera.gameObject.layer == LayerMask.NameToLayer("INTERIOR"))
			{
				return;
			}
			cameras.Add(camera);
			Mod.Info("Far view: camera " + camera.name + " (reach " + camera.farClipPlane.ToString("0") + ") draws the far view");
		}

		private Camera[] all = new Camera[16];
		private float nextScan;

		private void LateUpdate()
		{
			// The cameras draw as far as Sky says the far view is drawn (where the fog ends, or FarViewDistance if that is nearer and the land
			// past it is painted into the sky), never less than the tiles reach.
			if (!shown)
			{
				return;
			}
			// A camera switched on or made after the world was built (the free-flight camera, another mod's) is caught within two seconds.
			if (Time.unscaledTime >= nextScan)
			{
				nextScan = Time.unscaledTime + 2f;
				if (Camera.allCamerasCount > all.Length)
				{
					all = new Camera[Camera.allCamerasCount + 8];
				}
				int count = Camera.GetAllCameras(all);
				for (int i = 0; i < count; i++)
				{
					Note(all[i]);
				}
			}
			float reach = DrawnReach > 0f ? DrawnReach : RenderSettings.fogEndDistance;
			float needed = Mathf.Max((Mod.TileRadius.Value + 0.5f) * Streamer.TileSize, reach * 1.02f + 50f);
			foreach (Camera camera in cameras)
			{
				if (camera != null && Mathf.Abs(camera.farClipPlane - needed) > 10f)
				{
					camera.farClipPlane = needed;
				}
			}
			if (Mod.ShowCoords.Value)
			{
				Camera seeing = null;
				foreach (Camera camera in cameras)
				{
					if (camera != null && camera.isActiveAndEnabled && (seeing == null || camera.depth > seeing.depth))
					{
						seeing = camera;
					}
				}
				int drawn = 0;
				foreach (Chunk c in grid)
				{
					drawn += c != null && c.Any && c.Go != null && c.Go.activeSelf ? 1 : 0;
				}
				Status = "far view: " + drawn + " chunks, fog " + RenderSettings.fogStartDistance.ToString("0") + " to " + RenderSettings.fogEndDistance.ToString("0")
					+ (seeing != null ? ", " + seeing.name + " draws to " + seeing.farClipPlane.ToString("0") : "");
			}
		}

		/// <summary>One line for the debug overlay (ShowCoords): the chunks drawn, the fog's reach, the camera's reach.</summary>
		internal static string Status;

		/// <summary>Has the picture's mipmaps made on a worker, then, once they are, makes the texture (see <see cref="Picture"/>).</summary>
		private void Prepare()
		{
			if (mipping == null)
			{
				byte[] colours = FarGrid.Colours;
				int p = FarGrid.Pixels;
				mipping = Task.Run(() => Mips(colours, p));
				return;
			}
			if (!mipping.IsCompleted)
			{
				return;
			}
			byte[][] chain = null;
			if (mipping.Status == TaskStatus.RanToCompletion)
			{
				chain = mipping.Result;
			}
			else
			{
				Mod.Log.LogWarning("Far view: the picture's mipmaps could not be made on a worker (Unity makes them): " + mipping.Exception?.GetBaseException().Message);
			}
			mipping = null;
			Picture(chain);
		}

		/// <summary>
		/// The picture's mipmaps below the full size, each half the last (rounded down, at least 1, as Unity sizes them), each texel the
		/// average of the 2 by 2 above it, as Unity's own box filter makes them. Pure; on a worker, where it cost the main thread the frame
		/// the far view came on.
		/// </summary>
		internal static byte[][] Mips(byte[] colours, int size)
		{
			List<byte[]> chain = new List<byte[]>();
			byte[] src = colours;
			int s = size;
			while (s > 1)
			{
				int d = Math.Max(1, s / 2);
				byte[] dst = new byte[d * d * 4];
				for (int j = 0; j < d; j++)
				{
					int j0 = Math.Min(s - 1, 2 * j), j1 = Math.Min(s - 1, 2 * j + 1);
					for (int i = 0; i < d; i++)
					{
						int i0 = Math.Min(s - 1, 2 * i), i1 = Math.Min(s - 1, 2 * i + 1);
						int a = (j0 * s + i0) * 4, b = (j0 * s + i1) * 4, c = (j1 * s + i0) * 4, e = (j1 * s + i1) * 4;
						int o = (j * d + i) * 4;
						for (int k = 0; k < 4; k++)
						{
							dst[o + k] = (byte)((src[a + k] + src[b + k] + src[c + k] + src[e + k] + 2) >> 2);
						}
					}
				}
				chain.Add(dst);
				src = dst;
				s = d;
			}
			return chain.ToArray();
		}

		/// <summary>Makes the world's picture into a texture (mipmapped and filtered: at a distance a point-sampled picture shimmers), with the
		/// mipmaps in <paramref name="chain"/> (Unity makes them if it is null or does not fit), and the sea.</summary>
		private void Picture(byte[][] chain)
		{
			int p = FarGrid.Pixels;
			texture = new Texture2D(p, p, TextureFormat.RGBA32, true)
			{
				name = "OpenWorld far view",
				filterMode = FilterMode.Trilinear,
				wrapMode = TextureWrapMode.Clamp,
				anisoLevel = 4,
				hideFlags = HideFlags.HideAndDontSave,
			};
			texture.SetPixelData(FarGrid.Colours, 0);
			if (chain != null && chain.Length == texture.mipmapCount - 1)
			{
				for (int m = 1; m < texture.mipmapCount; m++)
				{
					texture.SetPixelData(chain[m - 1], m);
				}
				texture.Apply(false, true);
			}
			else
			{
				texture.Apply(true, true);
			}
			material.SetTexture("_BaseMap", texture);
			material.SetTexture("_MainTex", texture);

			seaTexture = new Texture2D(4, 4, TextureFormat.RGBA32, false) { name = "OpenWorld far sea", hideFlags = HideFlags.HideAndDontSave };
			Color32[] blue = new Color32[16];
			for (int k = 0; k < blue.Length; k++)
			{
				blue[k] = new Color32(44, 79, 120, 255);
			}
			seaTexture.SetPixels32(blue);
			seaTexture.Apply(false, true);
			seaMaterial.SetTexture("_BaseMap", seaTexture);
			seaMaterial.SetTexture("_MainTex", seaTexture);
			Sea();
		}

		/// <summary>The sea past the world's south edge, out to the horizon: one flat sheet at sea level, as wide as the world.</summary>
		private void Sea()
		{
			const double depth = 40000.0;
			float y = (float)(WorldGen.SeaLevel + Network.Lift);
			float w = (float)(WorldGen.Half * 2.0), d = (float)depth;
			seaMesh = new Mesh { name = "OpenWorld far sea" };
			seaMesh.vertices = new[] { new Vector3(0f, y, 0f), new Vector3(w, y, 0f), new Vector3(0f, y, d), new Vector3(w, y, d) };
			seaMesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
			seaMesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one };
			seaMesh.triangles = new[] { 0, 2, 1, 1, 2, 3 };
			seaMesh.RecalculateBounds();
			sea = new GameObject("Far sea") { layer = layer };
			sea.transform.SetParent(transform, false);
			sea.transform.position = Origin.Local(-WorldGen.Half, 0f, -WorldGen.Half - depth);
			sea.AddComponent<MeshFilter>().sharedMesh = seaMesh;
			MeshRenderer r = sea.AddComponent<MeshRenderer>();
			r.sharedMaterial = seaMaterial;
			Quiet(r);
			// Shown with the chunks, once they are all made.
			sea.SetActive(shown);
		}

		private static void Quiet(MeshRenderer r)
		{
			r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
			r.receiveShadows = false;
			r.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
			r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
		}

		/// <summary>(Re)makes chunk (ci, cj) at detail <paramref name="lod"/>, leaving out the tiles set in <paramref name="mask"/>.</summary>
		private void Make(Chunk c, int ci, int cj, int lod, ulong mask)
		{
			c.Lod = lod;
			c.Mask = mask;
			int n = FarGrid.N;
			float[] top = FarGrid.Top;
			int s = LodStep[lod];
			int per = (int)Math.Round(ChunkSize / FarGrid.Step);
			int cells = per / s;
			int cellsPerTile = (int)Math.Round(Streamer.TileSize / FarGrid.Step) / s;
			int gi0 = ci * per, gj0 = cj * per;
			float step = (float)(FarGrid.Step * s);
			double world = WorldGen.Half * 2.0;
			double x0 = -WorldGen.Half + ci * ChunkSize, z0 = -WorldGen.Half + cj * ChunkSize;
			float skirt = FarGrid.Skirt * s;

			vertices.Clear();
			normals.Clear();
			uvs.Clear();
			triangles.Clear();

			// Is cell (a, b) of this chunk drawn? Cells outside the chunk count as not, so the chunk's rim has a skirt.
			bool Drawn(int a, int b)
			{
				if (a < 0 || b < 0 || a >= cells || b >= cells)
				{
					return false;
				}
				if (mask == 0UL || cellsPerTile <= 0)
				{
					return true;
				}
				int bit = (b / cellsPerTile) * ChunkTiles + a / cellsPerTile;
				return (mask & (1UL << bit)) == 0UL;
			}

			int side = cells + 1;
			for (int b = 0; b <= cells; b++)
			{
				for (int a = 0; a <= cells; a++)
				{
					int gi = gi0 + a * s, gj = gj0 + b * s;
					float h = top[gj * n + gi];
					vertices.Add(new Vector3(a * step, h, b * step));
					normals.Add(Normal(top, n, gi, gj, s));
					uvs.Add(new Vector2((float)((x0 + a * step + WorldGen.Half) / world), (float)((z0 + b * step + WorldGen.Half) / world)));
				}
			}
			for (int b = 0; b < cells; b++)
			{
				for (int a = 0; a < cells; a++)
				{
					if (!Drawn(a, b))
					{
						continue;
					}
					int v = b * side + a;
					triangles.Add(v);
					triangles.Add(v + side);
					triangles.Add(v + 1);
					triangles.Add(v + 1);
					triangles.Add(v + side);
					triangles.Add(v + side + 1);
					// A skirt on each edge that borders a cell not drawn (a loaded tile, or the next chunk): at the finest detail as deep as the
					// real ground dips under the edge, else deeper the coarser the detail.
					int gi = gi0 + a * s, gj = gj0 + b * s;
					if (!Drawn(a, b - 1))
					{
						Hang(v, v + 1, lod == 0 ? FarGrid.SkirtAt(gi, gj, gi + 1, gj) : skirt);
					}
					if (!Drawn(a, b + 1))
					{
						Hang(v + side, v + side + 1, lod == 0 ? FarGrid.SkirtAt(gi, gj + 1, gi + 1, gj + 1) : skirt);
					}
					if (!Drawn(a - 1, b))
					{
						Hang(v, v + side, lod == 0 ? FarGrid.SkirtAt(gi, gj, gi, gj + 1) : skirt);
					}
					if (!Drawn(a + 1, b))
					{
						Hang(v + 1, v + side + 1, lod == 0 ? FarGrid.SkirtAt(gi + 1, gj, gi + 1, gj + 1) : skirt);
					}
				}
			}

			if (c.Mesh == null)
			{
				c.Mesh = new Mesh { name = "OpenWorld far " + ci + "," + cj };
			}
			c.Mesh.Clear();
			if (vertices.Count > 65000)
			{
				c.Mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
			}
			c.Mesh.SetVertices(vertices);
			c.Mesh.SetNormals(normals);
			c.Mesh.SetUVs(0, uvs);
			c.Mesh.SetTriangles(triangles, 0);
			c.Mesh.RecalculateBounds();
			if (c.Go == null)
			{
				c.Go = new GameObject("Far " + ci + "," + cj) { layer = layer };
				c.Go.transform.SetParent(transform, false);
				c.Go.transform.position = Origin.Local(x0, 0f, z0);
				c.Go.AddComponent<MeshFilter>().sharedMesh = c.Mesh;
				MeshRenderer r = c.Go.AddComponent<MeshRenderer>();
				r.sharedMaterial = material;
				Quiet(r);
			}
			c.Any = triangles.Count > 0;
			// Hidden while the far view is being made (or is off): shown with the rest when it comes on.
			c.Go.SetActive(shown && c.Any);
		}

		/// <summary>A skirt hanging <paramref name="drop"/> under the edge between vertices i and j, seen from both sides (it may be looked
		/// at from a tile or from the next chunk), lit as the edge is.</summary>
		private void Hang(int i, int j, float drop)
		{
			int k = vertices.Count;
			Vector3 a = vertices[i], b = vertices[j];
			vertices.Add(a);
			vertices.Add(b);
			vertices.Add(a - new Vector3(0f, drop, 0f));
			vertices.Add(b - new Vector3(0f, drop, 0f));
			normals.Add(normals[i]);
			normals.Add(normals[j]);
			normals.Add(normals[i]);
			normals.Add(normals[j]);
			uvs.Add(uvs[i]);
			uvs.Add(uvs[j]);
			uvs.Add(uvs[i]);
			uvs.Add(uvs[j]);
			triangles.Add(k);
			triangles.Add(k + 1);
			triangles.Add(k + 2);
			triangles.Add(k + 1);
			triangles.Add(k + 3);
			triangles.Add(k + 2);
			triangles.Add(k);
			triangles.Add(k + 2);
			triangles.Add(k + 1);
			triangles.Add(k + 1);
			triangles.Add(k + 2);
			triangles.Add(k + 3);
		}

		/// <summary>The surface's normal at sample (i, j) from its neighbours <paramref name="s"/> samples away (the detail drawn there).</summary>
		private static Vector3 Normal(float[] top, int n, int i, int j, int s)
		{
			int il = Math.Max(0, i - s), ir = Math.Min(n - 1, i + s), jd = Math.Max(0, j - s), ju = Math.Min(n - 1, j + s);
			float hx = (top[j * n + ir] - top[j * n + il]) / ((ir - il) * (float)FarGrid.Step);
			float hz = (top[ju * n + i] - top[jd * n + i]) / ((ju - jd) * (float)FarGrid.Step);
			return new Vector3(-hx, 1f, -hz).normalized;
		}

		private void OnDestroy()
		{
			if (Instance == this)
			{
				Instance = null;
				Status = null;
			}
			foreach (Chunk c in grid ?? new Chunk[0])
			{
				if (c != null && c.Mesh != null)
				{
					Destroy(c.Mesh);
				}
			}
			if (seaMesh != null)
			{
				Destroy(seaMesh);
			}
			if (texture != null)
			{
				Destroy(texture);
			}
			if (seaTexture != null)
			{
				Destroy(seaTexture);
			}
			if (material != null)
			{
				Destroy(material);
			}
			if (seaMaterial != null)
			{
				Destroy(seaMaterial);
			}
		}
	}
}
