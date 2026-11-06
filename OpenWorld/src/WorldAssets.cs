using System.Collections.Generic;
using UnityEngine;

namespace HalfGripOpenWorld
{
	/// <summary>
	/// Frees the materials made for one Open World race, and their textures, when the world goes. They are made HideAndDontSave (so a
	/// scene change does not take them from under the tiles), which also keeps them from Unity's unloading: before 0.26.1 every race
	/// in a session added another set of about 40 materials and their textures.
	/// </summary>
	internal sealed class WorldAssets : MonoBehaviour
	{
		private readonly List<Material> materials = new List<Material>();

		internal void Add(params Material[] made)
		{
			if (made != null)
			{
				materials.AddRange(made);
			}
		}

		private void OnDestroy()
		{
			Free(materials);
		}

		/// <summary>
		/// Destroys each material and every texture set on it, each once. Only what the mod made (DontSave): a game asset is never
		/// destroyed, even if one were set on a material by mistake.
		/// </summary>
		internal static void Free(IEnumerable<Material> made)
		{
			HashSet<UnityEngine.Object> gone = new HashSet<UnityEngine.Object>();
			foreach (Material m in made)
			{
				if (m == null || !Made(m) || !gone.Add(m))
				{
					continue;
				}
				foreach (int id in m.GetTexturePropertyNameIDs())
				{
					Texture t = m.GetTexture(id);
					if (t != null && Made(t) && gone.Add(t))
					{
						Destroy(t);
					}
				}
				Destroy(m);
			}
		}

		private static bool Made(UnityEngine.Object o)
		{
			return (o.hideFlags & HideFlags.DontSave) != 0;
		}
	}
}
