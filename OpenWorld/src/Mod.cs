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
		internal static ConfigEntry<int> TileRadius;
		internal static ConfigEntry<float> ShiftDistance;
		internal static ConfigEntry<int> GrassReach;
		internal static ConfigEntry<bool> Teleport;
		internal static ConfigEntry<KeyCode> TeleportKey;
		internal static ConfigEntry<float> TeleportDistance;
		internal static ConfigEntry<bool> ShowCoords;
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
			TileRadius = ctx.Config.Bind("World", "TileRadius", 5, new ConfigDescription("How many 250 m tiles of ground are kept loaded around the car in each direction. 5 is about 1.3 km, which the fog hides. More draws further and costs memory and frame time. Only changes your own view.", new AcceptableValueRange<int>(2, 10)));
			GrassReach = ctx.Config.Bind("World", "GrassReach", 2, new ConfigDescription("Grass is drawn on the tiles this many tiles round the car's (2 is about 600 m). 0 turns grass off. Only changes your own view (grass has no collision).", new AcceptableValueRange<int>(0, 4)));
			ShiftDistance = ctx.Config.Bind("World", "ShiftDistance", 1000f, new ConfigDescription("When the car is this many metres from the scene's origin, the whole scene is moved back so the car is near 0,0 again. Smaller keeps the physics more precise and shifts more often. Only changes your own game.", new AcceptableValueRange<float>(250f, 4000f)));
			Teleport = ctx.Config.Bind("Debug", "Teleport", false, "Let TeleportKey jump the car TeleportDistance metres ahead, to test far parts of the world. Works in a Multiplayer room too, so it is off by default.");
			TeleportKey = ctx.Config.Bind("Debug", "TeleportKey", KeyCode.F7, "The key for Teleport.");
			TeleportDistance = ctx.Config.Bind("Debug", "TeleportDistance", 5000f, "How far one Teleport jumps, in metres.");
			ShowCoords = ctx.Config.Bind("Debug", "ShowCoords", false, "Show the car's world position, the origin offset and the tile count in the top left corner.");
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
			ctx.OnUnload(MapEntry.Remove);
			ctx.PatchAll();
		}

		public override void Unload()
		{
			WorldScene.Unload();
		}
	}
}
