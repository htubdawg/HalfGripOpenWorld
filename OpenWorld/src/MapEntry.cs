using System;
using System.Linq;
using UnityEngine;

namespace HalfGripOpenWorld
{
	/// <summary>
	/// Adds the Open World to <c>RunConfig.maps</c>. That list is where the Multiplayer mod's lobby finds its maps, and where a saved run
	/// finds its maps by name. It is kept out of the run map's draws (<see cref="KeepOutOfRuns"/>): a free-roam race has no finish.
	/// </summary>
	internal static class MapEntry
	{
		/// <summary>The scene the Open World is built in: Tokyo's.</summary>
		internal static int HostTrack;

		/// <summary>The index the Open World reports as its track, so its records do not mix with Tokyo's. Highway uses 20, ProcTrack 21 to 25.</summary>
		private const int TrackId = 30;

		internal static void Ensure()
		{
			try
			{
				RunConfig config = RunConfig.Instance;
				if (config == null || config.maps == null)
				{
					return;
				}
				if (Mod.Map == null)
				{
					MapDefinition source = config.FindMap("TokyoNeon");
					if (source == null)
					{
						Mod.Log.LogWarning("The run config has no TokyoNeon map (another game version?), the Open World is not added.");
						return;
					}
					if (source == null)
					{
						Mod.Log.LogWarning("The run config has no map to copy, the Open World is not added.");
						return;
					}
					Mod.Map = Make(source);
					Mod.Log.LogInfo("Open World map made from " + source.name + " (host track " + HostTrack + ")");
				}
				if (!config.maps.Contains(Mod.Map))
				{
					config.maps.Add(Mod.Map);
					Mod.Info("Open World added to the run config (" + config.maps.Count + " maps)");
				}
			}
			catch (Exception e)
			{
				Mod.Log.LogError("Could not add the Open World map: " + e);
			}
		}

		internal static void Remove()
		{
			RunConfig config = RunConfig.Instance;
			if (config != null && Mod.Map != null)
			{
				config.maps.RemoveAll(m => m == Mod.Map);
			}
		}

		private static MapDefinition Make(MapDefinition source)
		{
			HostTrack = source.trackIndex;
			MapDefinition map = UnityEngine.Object.Instantiate(source);
			map.hideFlags = HideFlags.HideAndDontSave;
			map.name = "OpenWorld";
			map.displayName = "Open World";
			map.conditions = string.Empty;
			map.trackIndex = TrackId;
			map.allowMirror = false;
			map.allowReverse = false;
			map.allowMirrorReverse = false;
			map.mirrorIcon = null;
			map.reverseIcon = null;
			map.mirrorReverseIcon = null;
			map.excludeFromLast = true;
			// Untimed: no time objective can be dealt for it.
			map.timeEarly = 0f;
			map.timeMid = 0f;
			map.timeLate = 0f;

			// The weather is drawn per race (Weather.Pick); this one only shows on the map list.
			return map;
		}
	}
}
