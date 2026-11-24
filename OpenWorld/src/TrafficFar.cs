using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace HalfGripOpenWorld
{
	/// <summary>
	/// Part of <see cref="TrafficCars"/>: the drawn cars far off, cheap. A car past <see cref="FarAt"/> keeps its body (posed, for the
	/// physics cars and the touch test, which start from it) but switched off, and is drawn instead with every other far car of its look
	/// and paint in one instanced call for each of the body's parts (the meshes as they are: body, glass, lamps, wheels standing still),
	/// with no shadows, stood on the ground by raycasts every few frames. Near cars are as before, their shadows off past
	/// <see cref="ShadowAt"/>. Ten times the highway's cars puts about 25 each way within the drawn reach: as copies of the enemy car's
	/// bodies that would be 8 or 9 draw calls each and their shadows.
	/// </summary>
	internal sealed partial class TrafficCars
	{
		/// <summary>A drawn car farther than this is drawn far (instanced, no shadows); back near inside it less the band (no flicker on the edge).</summary>
		private const float FarAt = 250f, FarBand = 20f;
		/// <summary>A near drawn car casts shadows only within this (less the band to switch them back on).</summary>
		private const float ShadowAt = 150f, ShadowBand = 15f;
		/// <summary>A far moving car looks for the ground under it again after this many frames or once it has moved this far.</summary>
		private const int FarRayEvery = 8;
		private const float FarRayMove = 25f;

		/// <summary>One mesh's submesh of a look's body, where it sits in the car's frame, and how it takes its material.</summary>
		private sealed class Part
		{
			public Mesh Mesh;
			public int Sub;
			public Matrix4x4 Local;
			public Material Plain;
			/// <summary>Its index in the look's painted renderers (-1: not painted) and lamps (-1: not a lamp).</summary>
			public int PaintIndex = -1, LampIndex = -1;
		}

		/// <summary>The far cars of one look, paint and lamps (lit or dark) this frame: their matrices in the scene.</summary>
		private sealed class FarGroup
		{
			public int Look, Paint, Lamps;
			public Matrix4x4[] Cars = new Matrix4x4[16];
			public int Count;
			public Vector3 Min, Max;
		}

		private readonly Dictionary<int, FarGroup> farGroups = new Dictionary<int, FarGroup>();
		private readonly List<FarGroup> farList = new List<FarGroup>();
		private readonly Dictionary<Material, Material> instanced = new Dictionary<Material, Material>();
		private Matrix4x4[] farParts = new Matrix4x4[64];
		private int drawnFar;
		/// <summary>The player's place this frame (world), what a drawn car's distance is measured from.</summary>
		private double drawX, drawZ;

		/// <summary>Whether a drawn car is drawn now, near (its body shown) or far (instanced): what a physics or loose car is made from.</summary>
		private static bool Drawn(View v)
		{
			return v.Go.activeSelf || v.Far;
		}

		/// <summary>Whether car view <paramref name="v"/> at world (x, z) is to be drawn far, with the band round the edge.</summary>
		private bool IsFar(View v, double x, double z)
		{
			double dx = x - drawX, dz = z - drawZ;
			double edge = v.Far ? FarAt - FarBand : FarAt + FarBand;
			return dx * dx + dz * dz > edge * edge;
		}

		/// <summary>Shadows on a near car within <see cref="ShadowAt"/> as its look has them, off past it (only when that changes).</summary>
		private void NearShadows(View v, double x, double z)
		{
			double dx = x - drawX, dz = z - drawZ;
			double edge = v.Shadows ? ShadowAt + ShadowBand : ShadowAt - ShadowBand;
			bool on = dx * dx + dz * dz < edge * edge;
			if (on == v.Shadows)
			{
				return;
			}
			v.Shadows = on;
			ShadowCastingMode[] modes = v.Look.ShadowModes;
			for (int i = 0; i < v.Renderers.Length; i++)
			{
				v.Renderers[i].shadowCastingMode = on && modes != null && i < modes.Length ? modes[i] : ShadowCastingMode.Off;
			}
		}

		/// <summary>Car view <paramref name="v"/> drawn far this frame: its body off, its pose (the root, kept posed) into its look's and paint's group.</summary>
		private void AddFar(View v)
		{
			v.Far = true;
			if (v.Go.activeSelf)
			{
				v.Go.SetActive(false);
			}
			int lamps = v.LampsOn == 1 ? 1 : 0;
			int key = (v.LookIndex * 64 + v.Paint) * 2 + lamps;
			if (!farGroups.TryGetValue(key, out FarGroup g))
			{
				g = new FarGroup { Look = v.LookIndex, Paint = v.Paint, Lamps = lamps };
				farGroups[key] = g;
			}
			if (g.Count == 0)
			{
				farList.Add(g);
			}
			if (g.Count == g.Cars.Length)
			{
				Array.Resize(ref g.Cars, g.Cars.Length * 2);
			}
			Matrix4x4 m = v.Root.localToWorldMatrix;
			Vector3 p = m.GetColumn(3);
			if (g.Count == 0)
			{
				g.Min = g.Max = p;
			}
			else
			{
				g.Min = Vector3.Min(g.Min, p);
				g.Max = Vector3.Max(g.Max, p);
			}
			g.Cars[g.Count++] = m;
			drawnFar++;
		}

		/// <summary>Car view <paramref name="v"/> near this frame: its body shown (if it was far).</summary>
		private static void ShowNear(View v)
		{
			v.Far = false;
			if (!v.Go.activeSelf)
			{
				v.Go.SetActive(true);
			}
		}

		/// <summary>The far cars gathered this frame, drawn: for each group, each part of its look in one call.</summary>
		private void DrawFar()
		{
			bool instancing = Mod.TrafficInstanced.Value;
			foreach (FarGroup g in farList)
			{
				Look look = looks[g.Look];
				if (look.Parts == null)
				{
					look.Parts = PartsOf(look);
				}
				Bounds box = new Bounds((g.Min + g.Max) * 0.5f, g.Max - g.Min + new Vector3(40f, 40f, 40f));
				if (farParts.Length < g.Count)
				{
					farParts = new Matrix4x4[Math.Max(g.Count, farParts.Length * 2)];
				}
				foreach (Part part in look.Parts)
				{
					Material m = MaterialOf(look, part, g.Paint, g.Lamps == 1);
					if (m == null)
					{
						continue;
					}
					if (instancing)
					{
						m = Instanced(m);
					}
					RenderParams rp = new RenderParams(m)
					{
						worldBounds = box,
						shadowCastingMode = ShadowCastingMode.Off,
						receiveShadows = true,
						layer = layer,
					};
					for (int i = 0; i < g.Count; i++)
					{
						farParts[i] = g.Cars[i] * part.Local;
					}
					if (instancing)
					{
						for (int start = 0; start < g.Count; start += 1023)
						{
							Graphics.RenderMeshInstanced(rp, part.Mesh, part.Sub, farParts, Math.Min(1023, g.Count - start), start);
						}
					}
					else
					{
						for (int i = 0; i < g.Count; i++)
						{
							Graphics.RenderMesh(rp, part.Mesh, part.Sub, farParts[i]);
						}
					}
				}
				g.Count = 0;
			}
			farList.Clear();
		}

		/// <summary>The material a part wears for a paint and lamps: the paint's copy, the lamps lit or dark, else its own.</summary>
		private Material MaterialOf(Look look, Part part, int paint, bool lit)
		{
			if (part.PaintIndex >= 0)
			{
				Material[] own = look.PaintMaterials[part.PaintIndex];
				return part.Sub < own.Length ? Painted(own[part.Sub], paint) : null;
			}
			if (part.LampIndex >= 0)
			{
				Material[] set = lit ? look.LampsLit[part.LampIndex] : look.LampsDark[part.LampIndex];
				return part.Sub < set.Length ? set[part.Sub] : null;
			}
			return part.Plain;
		}

		/// <summary>A copy of <paramref name="m"/> with instancing on (one for each material, kept for the race): the game's own are left as they are.</summary>
		private Material Instanced(Material m)
		{
			if (m.enableInstancing)
			{
				return m;
			}
			if (instanced.TryGetValue(m, out Material found))
			{
				return found;
			}
			Material made = new Material(m) { name = m.name + " (traffic, instanced)", hideFlags = HideFlags.HideAndDontSave, enableInstancing = true };
			instanced[m] = made;
			GetComponent<WorldAssets>()?.Add(made);
			return made;
		}

		/// <summary>
		/// A look's parts from its template: every mesh's every submesh, where it sits in the car's frame (the template hangs from this
		/// object as a car's body hangs from its root, so the frame is the same). Made again once the wheels are moved to their rest.
		/// </summary>
		private Part[] PartsOf(Look look)
		{
			List<Part> parts = new List<Part>();
			MeshRenderer[] rs = look.Template.GetComponentsInChildren<MeshRenderer>(true);
			Matrix4x4 toCar = transform.worldToLocalMatrix;
			for (int k = 0; k < rs.Length; k++)
			{
				MeshFilter f = rs[k].GetComponent<MeshFilter>();
				if (f == null || f.sharedMesh == null)
				{
					continue;
				}
				Mesh mesh = f.sharedMesh;
				Material[] mats = rs[k].sharedMaterials;
				Matrix4x4 local = toCar * rs[k].transform.localToWorldMatrix;
				int paint = Array.IndexOf(look.Paint, k), lamp = Array.IndexOf(look.Lamps, k);
				for (int s = 0; s < Math.Min(mesh.subMeshCount, mats.Length); s++)
				{
					parts.Add(new Part { Mesh = mesh, Sub = s, Local = local, Plain = mats[s], PaintIndex = paint, LampIndex = lamp });
				}
			}
			return parts.ToArray();
		}
	}
}
