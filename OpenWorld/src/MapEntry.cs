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
		/// <summary>The scene the Open World is built in: Stadium's.</summary>
		internal static int HostTrack;

		/// <summary>The index the Open World reports as its track, so its records do not mix with Stadium's. Highway uses 20, ProcTrack 21 to 25.</summary>
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
					MapDefinition source = config.FindMap("StadiumAsh") ?? config.maps.FirstOrDefault(m => m != null);
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

			if (source.weather != null)
			{
				WeatherProfile weather = UnityEngine.Object.Instantiate(source.weather);
				weather.hideFlags = HideFlags.HideAndDontSave;
				// Rain, snow and ash are particle boxes over the old lap; they would not follow the car across the world.
				weather.rain = false;
				weather.snow = false;
				weather.ash = false;
				weather.headlightsActive = false;
				weather.gripExponent = 1f;
				weather.gripMultiplier = 1f;
				// Fog that ends where the loaded tiles end, so the world fades out rather than stopping at an edge.
				float reach = Mod.TileRadius.Value * Streamer.TileSize;
				weather.fogActive = true;
				weather.fogStart = reach * 0.25f;
				weather.fogEnd = reach;
				map.weather = weather;
			}
			return map;
		}
	}
}
