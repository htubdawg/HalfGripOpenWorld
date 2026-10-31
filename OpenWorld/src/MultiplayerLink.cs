using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HalfGripTools;
using HarmonyLib;
using UnityEngine;

namespace HalfGripOpenWorld
{
	/// <summary>
	/// Makes the Multiplayer mod work across the floating origin without changing it. Every player's scene is shifted by a different
	/// amount, so a position read from one player's scene means nothing in another's. Positions are therefore sent in world coordinates:
	/// this adds the local origin to each position as the Multiplayer mod writes it into a packet, and takes it off again as a packet is
	/// read. Velocities, rotations and everything else pass untouched. Nothing about the packets changes but the numbers in them.
	///
	/// The Multiplayer mod's classes are internal to it, so they are found by name and patched with this mod's own Harmony instance, the
	/// way ProcTrack reads the room seed. If they are not there (no Multiplayer mod, or a version that renamed them), nothing is patched
	/// and a line is logged; solo play is unaffected.
	///
	/// Which values in a packet are positions (Multiplayer protocol 7, read from its Wire.cs, RaceSync.cs and Cues.cs):
	///   Car        position, velocity, [aim point]      even Vec()s
	///   Respawned  position                             even
	///   AiState    (position, velocity) per AI car      even
	///   Rocket     (position, velocity) per rocket      even
	///   RocketEnd  position                             even
	///   Shot       landing point per shot               all
	/// </summary>
	internal static class MultiplayerLink
	{
		private static bool tried;
		private static Type remoteCarType;
		private static FieldInfo remotePosition;
		private static FieldInfo remoteAim;
		private static PropertyInfo readerType;
		private static FieldInfo roomActive;
		private static FieldInfo roomSeed;
		private static FieldInfo roomRace;
		private static FieldInfo roomAiGrid;

		/// <summary>The room's seed (the Multiplayer mod's <c>Room.Seed</c>), the same for every player in the room.</summary>
		internal static bool RoomSeed(out int seed)
		{
			seed = 0;
			if (roomSeed == null || !(roomSeed.GetValue(null) is int value))
			{
				return false;
			}
			seed = value;
			return true;
		}

		/// <summary>
		/// The Open World's rules on the room's race in progress (the Multiplayer mod's <c>Room.Race</c>, every player's own copy of the
		/// host's settings): a wrecked player respawns (a Demolition becomes a race, "out, watch" becomes respawn), since a race with no
		/// finish must not put anyone out for good; and no AI rivals (the race's AI count and <c>Room.AiGrid</c>, the slots the host dealt
		/// them, emptied before the Multiplayer mod spawns the cars at the scene's start). Every player does the same as the world loads,
		/// so they agree.
		/// </summary>
		internal static void RoomRules()
		{
			try
			{
				object race = roomRace != null ? roomRace.GetValue(null) : null;
				if (race == null)
				{
					return;
				}
				Type settings = race.GetType();
				FieldInfo mode = AccessTools.Field(settings, "Mode");
				FieldInfo wreck = AccessTools.Field(settings, "Wreck");
				FieldInfo ai = AccessTools.Field(settings, "Ai");
				FieldInfo demolition = AccessTools.Field(settings, "ModeDemolition");
				FieldInfo modeRace = AccessTools.Field(settings, "ModeRace");
				FieldInfo respawn = AccessTools.Field(settings, "WreckRespawn");
				if (mode == null || wreck == null || ai == null || demolition == null || modeRace == null || respawn == null)
				{
					Mod.Log.LogWarning("The Multiplayer mod's race settings have no Mode, Wreck or Ai (another version): the Open World follows the room's settings.");
					return;
				}
				if (Equals(mode.GetValue(race), demolition.GetValue(null)))
				{
					mode.SetValue(race, modeRace.GetValue(null));
				}
				wreck.SetValue(race, respawn.GetValue(null));
				ai.SetValue(race, 0);
				if (roomAiGrid != null && roomAiGrid.GetValue(null) is System.Collections.IList grid)
				{
					grid.Clear();
				}
				else
				{
					Mod.Log.LogWarning("The Multiplayer mod's Room.AiGrid is missing (another version): AI rivals the host asked for may still race.");
				}
				Mod.Info("The room's race: respawn after a wreck, no AI rivals");
			}
			catch (Exception e)
			{
				Mod.Log.LogWarning("The Open World's rules could not be set on the room's race: " + e);
			}
		}

		/// <summary>True while the Multiplayer mod has a room open (its <c>Room.Active</c>).</summary>
		internal static bool InRoom => roomActive != null && roomActive.GetValue(null) is bool active && active;

		/// <summary>Per packet being written or read: its message type and how many Vec()s it has had.</summary>
		private sealed class Count
		{
			internal int Msg = -1;
			internal int Vecs;
		}

		private static readonly ConditionalWeakTable<object, Count> counts = new ConditionalWeakTable<object, Count>();

		/// <summary>Message numbers and the names they must have in the Multiplayer mod for the table above to hold.</summary>
		private static readonly (int number, string name, bool all)[] Positional =
		{
			(9, "Car", false),
			(12, "Respawned", false),
			(17, "AiState", false),
			(25, "Shot", true),
			(26, "Rocket", false),
			(27, "RocketEnd", false),
		};

		/// <summary>Patches the Multiplayer mod once, at the first scene load (when every mod has been compiled and loaded).</summary>
		internal static void Hook(ModContext ctx)
		{
			if (tried)
			{
				return;
			}
			tried = true;
			try
			{
				Type writer = AccessTools.TypeByName("Multiplayer.W");
				Type reader = AccessTools.TypeByName("Multiplayer.R");
				Type msg = AccessTools.TypeByName("Multiplayer.Msg");
				remoteCarType = AccessTools.TypeByName("Multiplayer.RemoteCar");
				Type room = AccessTools.TypeByName("Multiplayer.Room");
				roomActive = room != null ? AccessTools.Field(room, "Active") : null;
				roomSeed = room != null ? AccessTools.Field(room, "Seed") : null;
				roomRace = room != null ? AccessTools.Field(room, "Race") : null;
				roomAiGrid = room != null ? AccessTools.Field(room, "AiGrid") : null;
				if (writer == null || reader == null || msg == null || remoteCarType == null)
				{
					Mod.Info("No Multiplayer mod found: positions are not converted for the network.");
					return;
				}
				foreach ((int number, string name, bool _) in Positional)
				{
					if (Enum.GetName(msg, number) != name)
					{
						Mod.Log.LogWarning("The Multiplayer mod's message " + number + " is not '" + name + "' (another protocol version). Open World positions are not converted, so other players will be drawn in the wrong places far from the spawn.");
						return;
					}
				}
				ConstructorInfo ctor = AccessTools.GetDeclaredConstructors(writer).FirstOrDefault(c => c.GetParameters().Length == 1 && c.GetParameters()[0].ParameterType == msg);
				MethodInfo writeVec = AccessTools.Method(writer, "Vec", new[] { typeof(Vector3) });
				MethodInfo readVec = AccessTools.Method(reader, "Vec", Type.EmptyTypes);
				readerType = AccessTools.Property(reader, "Type");
				remotePosition = AccessTools.Field(remoteCarType, "position");
				remoteAim = AccessTools.Field(remoteCarType, "AimPoint");
				if (ctor == null || writeVec == null || readVec == null || readerType == null || remotePosition == null)
				{
					Mod.Log.LogWarning("The Multiplayer mod's packet writer or reader has changed. Open World positions are not converted, so other players will be drawn in the wrong places far from the spawn.");
					return;
				}
				ctx.Harmony.Patch(ctor, postfix: new HarmonyMethod(typeof(MultiplayerLink), nameof(WriterMade)));
				ctx.Harmony.Patch(writeVec, prefix: new HarmonyMethod(typeof(MultiplayerLink), nameof(WritingVec)));
				ctx.Harmony.Patch(readVec, postfix: new HarmonyMethod(typeof(MultiplayerLink), nameof(ReadVec)));
				Origin.Shifted += ShiftRemoteCars;
				Mod.Log.LogInfo("Multiplayer mod found: positions go over the network in world coordinates.");
			}
			catch (Exception e)
			{
				Mod.Log.LogWarning("Could not hook the Multiplayer mod: " + e.Message);
			}
		}

		private static void WriterMade(object __instance, object[] __args)
		{
			if (!Origin.Active)
			{
				return;
			}
			counts.GetOrCreateValue(__instance).Msg = Convert.ToInt32(__args[0]);
		}

		private static void WritingVec(object __instance, ref Vector3 v)
		{
			if (!Origin.Active || !counts.TryGetValue(__instance, out Count c))
			{
				return;
			}
			if (IsPosition(c.Msg, c.Vecs++))
			{
				v = Origin.ToWorld(v);
			}
		}

		private static void ReadVec(object __instance, ref Vector3 __result)
		{
			if (!Origin.Active)
			{
				return;
			}
			Count c = counts.GetOrCreateValue(__instance);
			if (c.Msg < 0)
			{
				c.Msg = Convert.ToInt32(readerType.GetValue(__instance));
			}
			if (IsPosition(c.Msg, c.Vecs++))
			{
				__result = Origin.ToLocal(__result);
			}
		}

		private static bool IsPosition(int msg, int index)
		{
			foreach ((int number, string _, bool all) in Positional)
			{
				if (number == msg)
				{
					return all || index % 2 == 0;
				}
			}
			return false;
		}

		/// <summary>A stand-in car keeps the last position it was sent (scene coordinates), which must move with the scene.</summary>
		private static void ShiftRemoteCars(Vector3 delta)
		{
			foreach (UnityEngine.Object o in UnityEngine.Object.FindObjectsByType(remoteCarType, FindObjectsSortMode.None))
			{
				remotePosition.SetValue(o, (Vector3)remotePosition.GetValue(o) + delta);
				if (remoteAim != null)
				{
					remoteAim.SetValue(o, (Vector3)remoteAim.GetValue(o) + delta);
				}
			}
		}
	}
}
