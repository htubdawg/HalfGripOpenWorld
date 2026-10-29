using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace HalfGripOpenWorld
{
	/// <summary>
	/// Each Open World race gets one of the game's weathers, drawn from every map's (clear day, rainy night, snow, ash, fire, neon...).
	/// The game moves the rain, snow and ash with the player, so they work anywhere in the world; only the fog is changed, so it ends where
	/// the loaded tiles end. In a Multiplayer room the draw comes from the room's seed (the Multiplayer mod's <c>Room.Seed</c>, the same
	/// for every player), so everyone gets the same weather; solo it is random.
	/// </summary>
	internal static class Weather
	{
		private static List<WeatherProfile> pool;

		internal static void Pick()
		{
			try
			{
				List<WeatherProfile> all = Pool();
				if (all.Count == 0)
				{
					return;
				}
				int roll = MultiplayerLink.InRoom && MultiplayerLink.RoomSeed(out int seed)
					? (int)(Noise.Mix((ulong)(uint)seed ^ 0x0E7A_3EA7UL) % (ulong)all.Count)
					: UnityEngine.Random.Range(0, all.Count);
				WeatherProfile chosen = all[roll];
				AccessTools.Property(typeof(RunState), nameof(RunState.ActiveWeather)).SetValue(null, chosen);
				Mod.Log.LogInfo("Weather: " + chosen.name + (MultiplayerLink.InRoom ? " (from the room's seed)" : ""));
			}
			catch (Exception e)
			{
				Mod.Log.LogWarning("Could not pick a weather, keeping the map's own: " + e.Message);
			}
		}

		/// <summary>A copy of every map's weather (one per name), with the fog set to the tile radius.</summary>
		private static List<WeatherProfile> Pool()
		{
			if (pool != null)
			{
				return pool;
			}
			pool = new List<WeatherProfile>();
			RunConfig config = RunConfig.Instance;
			if (config == null)
			{
				return pool;
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
				pool.Add(w);
			}
			Mod.Info("Weathers to draw from: " + string.Join(", ", pool.ConvertAll(w => w.name)));
			return pool;
		}
	}
}
