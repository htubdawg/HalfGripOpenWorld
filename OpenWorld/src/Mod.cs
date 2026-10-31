using BepInEx.Configuration;
using BepInEx.Logging;
using HalfGripTools;
using UnityEngine;

namespace HalfGripOpenWorld
{
	/// <summary>
	/// A free-roam map, Open World: a 32 km square of generated land with towns, villages and roads, streamed in tiles around the car. Like
	/// the Highway mod it has no scene of its own. It is built from code inside the Tokyo scene when an Open World race loads.
	///
	/// Two things make the size work. A floating origin (<see cref="Origin"/>) moves the whole scene back near 0,0 whenever the car gets
	/// far from it, so the physics never runs where float precision is coarse. And the terrain is a pure function of world coordinates
	/// (<see cref="WorldGen"/>), so any tile can be built at any time, in any order, the same on every machine.
	///
	/// Only settings that are local to one player are settings (how far tiles load, when the origin shifts, the debug tools). The world
	/// itself is not: every player in a room has to drive on the same ground.
	/// </summary>
	public class Mod : HalfGripMod
	{
		internal static ManualLogSource Log;
		internal static ModContext Ctx;

		internal static ConfigEntry<bool> TestMode;
		internal static ConfigEntry<float> RespawnSeconds;
		internal static ConfigEntry<int> TileRadius;
		internal static ConfigEntry<float> ShiftDistance;
		internal static ConfigEntry<int> GrassReach;
		internal static ConfigEntry<bool> ShowFarView;
		internal static ConfigEntry<float> FarReach;
		internal static ConfigEntry<bool> FarSkyline;
		internal static ConfigEntry<bool> Teleport;
		internal static ModKey TeleportKey;
		internal static ConfigEntry<bool> ShowCoords;
		internal static ConfigEntry<bool> WeatherSkip;
		internal static ModKey WeatherSkipKey;
		internal static ModKey WeatherFastKey;
		internal static ConfigEntry<bool> Verbose;

		/// <summary>The map entry the mod adds to the run config, once it exists.</summary>
		internal static MapDefinition Map;

		/// <summary>A detail line for the log: only written when Debug.VerboseLog is on.</summary>
		internal static void Info(string text)
		{
			if (Verbose != null && Verbose.Value)
			{
				Log.LogInfo(text);
			}
		}

		public override void Load(ModContext ctx)
		{
			Log = ctx.Log;
			Ctx = ctx;

			TestMode = ctx.Config.Bind("Map", "TestMode", false, "Every race you start is in the Open World, whatever the run map says. For trying it out. Off, the Open World is only reached from a Multiplayer room's map list (it is never dealt on a run map: a free-roam race has no finish).");
			RespawnSeconds = ctx.Config.Bind("Map", "RespawnSeconds", 3f, new ConfigDescription("Solo: how many seconds after a wreck the car is put back on the nearest road, repaired. (In a Multiplayer room the room's own respawn time counts.)", new AcceptableValueRange<float>(1f, 30f)));
			TileRadius = ctx.Config.Bind("World", "TileRadius", 5, new ConfigDescription("How many 250 m tiles of ground are kept loaded around the car in each direction. 5 is about 1.3 km, which the fog hides. More draws further and costs memory and frame time. Only changes your own view.", new AcceptableValueRange<int>(2, 10)));
			GrassReach = ctx.Config.Bind("World", "GrassReach", 2, new ConfigDescription("Grass is drawn on the tiles this many tiles round the car's (2 is about 600 m). 0 turns grass off. Only changes your own view (grass has no collision).", new AcceptableValueRange<int>(0, 4)));
			ShowFarView = ctx.Config.Bind("World", "FarView", true, "Draw the whole world at low detail past the loaded tiles (mountains, coast, woods and towns to the horizon), and see as far as the weather lets you: far on a clear day, close in the rain. Off, the fog ends where the loaded tiles end, as before 0.19.0. Only changes your own view.");
			FarReach = ctx.Config.Bind("World", "FarViewDistance", 14000f, new ConfigDescription("With FarView on, how far the low-detail world is drawn, in game units (the world is 32000 across). Past it, FarSkyline paints the land into the sky; with FarSkyline off, this is the furthest you can see on the clearest day. Less costs less frame time. Only changes your own view.", new AcceptableValueRange<float>(4000f, 20000f)));
			FarSkyline = ctx.Config.Bind("World", "FarSkyline", true, "With FarView on, paint the land past FarViewDistance into the sky, so on a clear day you see across the whole world (mountains 30 km off as hazy shapes on the horizon) and the fog no longer closes at the far view's edge. Off, it does, as before 0.20.0. Only changes your own view.");
			ShiftDistance = ctx.Config.Bind("World", "ShiftDistance", 1000f, new ConfigDescription("When the car is this many metres from the scene's origin, the whole scene is moved back so the car is near 0,0 again. Smaller keeps the physics more precise and shifts more often. Only changes your own game.", new AcceptableValueRange<float>(250f, 4000f)));
			Teleport = ctx.Config.Bind("Debug", "Teleport", false, "Let the Teleport key (Settings > Controls, F7 unless rebound) jump the car to the next of the world's key places (the spawn, the towns, the retail park, the villages, a filling station, the mountain pass, the circuits), and Shift with it to the one before, to test far parts of the world. Works in a Multiplayer room too, so it is off by default.");
			ShowCoords = ctx.Config.Bind("Debug", "ShowCoords", false, "Show the car's world position, the origin offset, the tile count and the sky (the time, the rain, cloud and wetness at the car, the grip, the nearest storm) in the top left corner.");
			WeatherSkip = ctx.Config.Bind("Debug", "WeatherSkip", false, "Let the Weather skip key (Settings > Controls, F5 unless rebound) move your own sky 5 minutes on (Shift: back), and the Weather fast key (F2 unless rebound) turn on and off a 10x speed for it: the time of day (an hour is 100 seconds) and the storms, to test them. Only your own view: in a Multiplayer room your sky is then out of step with the others' until the next race.");
			Verbose = ctx.Config.Bind("Debug", "VerboseLog", false, "Write the detailed log (each origin shift, tile timings) to the BepInEx log. Off, only the world's build, warnings and errors are logged.");

			MapEntry.Ensure();
			// The layout (towns, villages, roads) is pure arithmetic and takes a few hundred milliseconds: worked out in the background
			// now, so the first Open World race does not wait for it.
			System.Threading.Tasks.Task.Run(() =>
			{
				try
				{
					Network.Ensure();
				}
				catch (System.Exception e)
				{
					Log.LogError("The world layout could not be built: " + e);
					return;
				}
				try
				{
					// The far view's low-detail world, from the layout: a second or two more.
					FarGrid.Ensure();
				}
				catch (System.Exception e)
				{
					Log.LogError("The far view's world could not be built (the far view stays off): " + e);
				}
			});
			ctx.OnSceneLoaded(scene =>
			{
				MapEntry.Ensure();
				// The Forest's pines are only loaded with the main menu (and the Forest map): picked up and kept the first time they are.
				Trees.Capture();
				MultiplayerLink.Hook(ctx);
				WorldScene.OnSceneLoaded(scene);
			});
			// In the game's own Controls list (shown while their debug setting is on), so they can be rebound there and never take a key
			// the game or another mod has: the game's are F1, F3, F4, F6, F8 (its bug report), F9, F10 and F12.
			TeleportKey = ctx.Keys.Add(KeyCode.F7, "Teleport (Open World)", Controls.Section.Race, () => Teleport.Value);
			WeatherSkipKey = ctx.Keys.Add(KeyCode.F5, "Weather skip (Open World)", Controls.Section.Race, () => WeatherSkip.Value);
			WeatherFastKey = ctx.Keys.Add(KeyCode.F2, "Weather fast (Open World)", Controls.Section.Race, () => WeatherSkip.Value);
			ctx.OnUnload(MapEntry.Remove);
			ctx.PatchAll();
		}

		public override void Unload()
		{
			WorldScene.Unload();
		}
	}
}
