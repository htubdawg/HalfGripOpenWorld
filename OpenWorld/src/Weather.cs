using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace HalfGripOpenWorld
{
	/// <summary>
	/// Each Open World race gets a look for the day and a look for the night, drawn from the game's weathers (the day: the Stadium's ash,
	/// the fiery factory, the Forest's rainy day and red forest, the mountain day; the night: the rainy Stadium and Forest nights, the snowy
	/// mountain night, the factory night). <see cref="Sky"/> blends between them as the race's clock goes round, and brings the rain and
	/// snow from <see cref="Storms"/>, so the copies here have their own rain, snow, rain sounds and grip taken out. Only the fog is
	/// changed besides, so it ends where the loaded tiles end.
	///
	/// The draws, the storms and the time of day all come from the room's seed (the Multiplayer mod's <c>Room.Seed</c>, the same for every
	/// player and new each race), so everyone in a room gets the same sky. Outside a room (TestMode) the seed is random.
	/// </summary>
	internal static class Weather
	{
		private static List<WeatherProfile> day;
		private static List<WeatherProfile> night;

		/// <summary>The seed this race's sky comes from.</summary>
		internal static int Seed;

		/// <summary>The look for the night, blended to by <see cref="Sky"/> (the day's is the active weather). Null if none could be drawn.</summary>
		internal static WeatherProfile Night;

		internal static void Pick()
		{
			try
			{
				bool fromRoom = MultiplayerLink.InRoom && MultiplayerLink.RoomSeed(out Seed);
				if (!fromRoom)
				{
					Seed = UnityEngine.Random.Range(int.MinValue, int.MaxValue);
				}
				Night = null;
				Pool();
				if (day.Count == 0)
				{
					return;
				}
				WeatherProfile chosen = day[(int)(Noise.Mix((ulong)(uint)Seed ^ 0x0E7A_3EA7UL) % (ulong)day.Count)];
				if (night.Count > 0)
				{
					Night = night[(int)(Noise.Mix((ulong)(uint)Seed ^ 0x0E7A_2167UL) % (ulong)night.Count)];
				}
				AccessTools.Property(typeof(RunState), nameof(RunState.ActiveWeather)).SetValue(null, chosen);
				Mod.Log.LogInfo("Weather: " + chosen.name + " by day, " + (Night != null ? Night.name : "none") + " by night" + (fromRoom ? " (from the room's seed)" : " (not in a room: a random seed)"));
			}
			catch (Exception e)
			{
				Mod.Log.LogWarning("Could not pick a weather, keeping the map's own: " + e.Message);
			}
		}

		/// <summary>
		/// A copy of every map's weather (one per name), sorted into day and night, with the fog set to the tile radius and the rain, snow,
		/// rain sounds and grip changes taken out (the storms bring those).
		/// </summary>
		private static void Pool()
		{
			if (day != null)
			{
				return;
			}
			day = new List<WeatherProfile>();
			night = new List<WeatherProfile>();
			RunConfig config = RunConfig.Instance;
			if (config == null)
			{
				return;
			}
			HashSet<string> seen = new HashSet<string>();
			float reach = Mod.TileRadius.Value * Streamer.TileSize;
			foreach (MapDefinition map in config.maps)
			{
				if (map == null || map == Mod.Map || map.weather == null || !seen.Add(map.weather.name))
				{
					continue;
				}
				WeatherProfile w = UnityEngine.Object.Instantiate(map.weather);
				w.name = map.weather.name;
				w.hideFlags = HideFlags.HideAndDontSave;
				// Fog that ends where the loaded tiles end, so the world fades out rather than stopping at an edge. A weather whose own fog
				// is thicker keeps it.
				w.fogActive = true;
				w.fogEnd = map.weather.fogActive ? Mathf.Min(map.weather.fogEnd, reach) : reach;
				w.fogStart = Mathf.Min(map.weather.fogActive ? map.weather.fogStart : reach * 0.25f, w.fogEnd * 0.5f);
				w.rain = false;
				w.snow = false;
				w.gripExponent = 1f;
				w.gripMultiplier = 1f;
				if (w.ambientalSounds != null)
				{
					w.ambientalSounds = Array.FindAll(w.ambientalSounds, clip => clip != null && !IsRain(clip));
				}
				// Tokyo's rainy night has no sky of its own (a flat colour), so it is left out: the sky has to be one that can be dimmed.
				if (w.skyboxMaterial == null)
				{
					continue;
				}
				bool dark = w.headlightsActive || w.name.IndexOf("Night", StringComparison.OrdinalIgnoreCase) >= 0;
				(dark ? night : day).Add(w);
			}
			Mod.Info("Weathers to draw from: by day " + string.Join(", ", day.ConvertAll(w => w.name)) + "; by night " + string.Join(", ", night.ConvertAll(w => w.name)));
		}

		/// <summary>True for the rain loops (RainOnCar), which <see cref="Sky"/> plays itself as hard as it is raining.</summary>
		internal static bool IsRain(AudioClip clip)
		{
			return clip.name.IndexOf("Rain", StringComparison.OrdinalIgnoreCase) >= 0;
		}

		/// <summary>The game's rain loop, from any map's weather: null if there is none.</summary>
		internal static AudioClip RainClip()
		{
			RunConfig config = RunConfig.Instance;
			if (config == null)
			{
				return null;
			}
			foreach (MapDefinition map in config.maps)
			{
				if (map == null || map.weather == null || map.weather.ambientalSounds == null)
				{
					continue;
				}
				foreach (AudioClip clip in map.weather.ambientalSounds)
				{
					if (clip != null && IsRain(clip))
					{
						return clip;
					}
				}
			}
			return null;
		}
	}
}
