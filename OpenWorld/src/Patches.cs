using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace HalfGripOpenWorld
{
	/// <summary>The Open World has its own track index; the scene to load is Stadium's.</summary>
	[HarmonyPatch(typeof(MenuManager), "TrackIndex")]
	internal static class TrackIndexPatch
	{
		private static bool Prefix(ref int __result)
		{
			if (Mod.Map != null && RunState.Active && RunState.HasSelection && RunState.Selected.map == Mod.Map)
			{
				__result = MapEntry.HostTrack;
				return false;
			}
			return true;
		}
	}

	/// <summary>
	/// TestMode: whatever node the player picked, the race is in the Open World, with no objective. Then, for any Open World race (TestMode
	/// or a Multiplayer room), the weather is drawn: BeginRace has just set the map's own as the active weather.
	/// </summary>
	[HarmonyPatch(typeof(RunState), nameof(RunState.BeginRace))]
	internal static class TestModePatch
	{
		private static void Prefix()
		{
			if (Mod.TestMode.Value && Mod.Map != null && RunState.HasSelection && !RunState.Over)
			{
				RunState.Selected.map = Mod.Map;
				RunState.Selected.variant = TrackVariant.Normal;
				RunState.Selected.objective = RaceObjective.None;
			}
		}

		private static void Postfix()
		{
			if (Mod.Map != null && RunState.ActiveMap == Mod.Map)
			{
				Weather.Pick();
			}
		}
	}

	/// <summary>TestMode: a new run's whole map is the Open World, so the run map shows it.</summary>
	[HarmonyPatch(typeof(RunMap), nameof(RunMap.Generate))]
	internal static class TestModeMapPatch
	{
		private static void Postfix(RunMap __result)
		{
			if (!Mod.TestMode.Value || Mod.Map == null || __result == null)
			{
				return;
			}
			foreach (RunNode node in __result.nodes)
			{
				node.map = Mod.Map;
				node.variant = TrackVariant.Normal;
				node.unknownMap = false;
				node.objective = RaceObjective.None;
			}
			Mod.Log.LogInfo("TestMode: every node of the new run map is the Open World");
		}
	}

	/// <summary>
	/// The run map never deals the Open World: a free-roam race has no finish, so it would end a run. Every draw of a map goes through
	/// one of the two <c>RandomMap</c>s; the Open World is taken out of both. (The Multiplayer lobby lists <c>RunConfig.maps</c> itself,
	/// so the map is still there to pick.)
	/// </summary>
	[HarmonyPatch(typeof(RunConfig), nameof(RunConfig.RandomMap), new System.Type[0])]
	internal static class KeepOutOfRuns
	{
		private static bool Prefix(RunConfig __instance, ref MapDefinition __result)
		{
			if (Mod.Map == null || !__instance.maps.Contains(Mod.Map))
			{
				return true;
			}
			List<MapDefinition> others = __instance.maps.FindAll(m => m != null && m != Mod.Map);
			__result = others.Count > 0 ? others[Random.Range(0, others.Count)] : null;
			return false;
		}
	}

	[HarmonyPatch(typeof(RunConfig), nameof(RunConfig.RandomMap), new[] { typeof(List<MapDefinition>) })]
	internal static class KeepOutOfRunsAvoiding
	{
		private static void Prefix(ref List<MapDefinition> avoid)
		{
			// An empty list falls through to the plain draw, which KeepOutOfRuns already covers.
			if (Mod.Map != null && avoid != null && avoid.Count > 0 && !avoid.Contains(Mod.Map))
			{
				avoid = new List<MapDefinition>(avoid) { Mod.Map };
			}
		}
	}

	/// <summary>
	/// No AI rivals in solo free roam: they would only circle the token loop at the spawn. In a Multiplayer room this stays out of the
	/// way: the Multiplayer mod's own prefix on this method places the other players (and any AI cars the host asked for).
	/// </summary>
	[HarmonyPatch(typeof(TrackManager), "SpawnEnemies")]
	internal static class NoRivals
	{
		private static bool Prefix()
		{
			return !WorldScene.Loaded || MultiplayerLink.InRoom;
		}
	}

	/// <summary>
	/// No laps in free roam. The token loop's nearest sample jumps about as the car drives anywhere it likes, which the lap counter would
	/// read as laps driven (and end the race) or as driving backwards.
	/// </summary>
	[HarmonyPatch(typeof(LapManager), "LapCounter")]
	internal static class NoLaps
	{
		private static bool Prefix(int lapCount, int backwardsLaps, ref (int, int, int) __result)
		{
			if (!WorldScene.Loaded)
			{
				return true;
			}
			__result = (0, lapCount, backwardsLaps);
			return false;
		}
	}

	/// <summary>
	/// A reset (holding R) puts the car back on its wheels where it is, on the ground, facing the way it was going (or on the nearest road,
	/// if it is in water). The respawn after a wreck (<see cref="GameMode"/>'s, or the Multiplayer mod's) puts it back at the spawn. The game's own reset would carry it back to the token loop at the spawn, kilometres away.
	/// </summary>
	[HarmonyPatch(typeof(PlayerResetLogic), nameof(PlayerResetLogic.ResetOnTrack))]
	internal static class ResetInPlace
	{
		private static bool Prefix(CarControllerNew ___player)
		{
			Streamer streamer = Streamer.Instance;
			if (!WorldScene.Loaded || streamer == null || ___player == null)
			{
				return true;
			}
			Origin.World(___player.transform.position, out double x, out double z);
			WorldGen.ClampInside(ref x, ref z, 50.0);
			Vector3 heading = ___player.transform.forward;
			heading.y = 0f;
			if (heading.sqrMagnitude < 1e-4f)
			{
				heading = Vector3.forward;
			}
			if (GameMode.TakeWreck())
			{
				GameMode.ToSpawn(streamer);
				return false;
			}
			bool wet = Rivers.Water(x, z, out double level) && WorldGen.Height(x, z) - Network.Lift < level;
			// In a river, a lake or the sea: back on the nearest road instead (where it is, if there is none near).
			if (!(wet && streamer.ToRoad(x, z)))
			{
				streamer.BuildAround(x, z, 1);
				streamer.Place(x, z, Quaternion.LookRotation(heading.normalized, Vector3.up));
			}
			TextController.EnqueueRandomText(1f, "Reset.");
			return false;
		}
	}

	/// <summary>
	/// "Fell under the map" is measured from the ground under the car, not from the lowest point of the track line: the token loop is at
	/// the spawn's height and the world's valleys go well below it.
	/// </summary>
	[HarmonyPatch(typeof(PlayerResetLogic), "FellUnderMap")]
	internal static class FellUnderGround
	{
		private static bool Prefix(CarControllerNew ___player, ref bool __result)
		{
			if (!WorldScene.Loaded || ___player == null)
			{
				return true;
			}
			Rigidbody rb = ___player.rb;
			Vector3 p = ___player.transform.position;
			Origin.World(p, out double x, out double z);
			__result = rb != null && !rb.isKinematic && rb.linearVelocity.y < 0f && p.y < WorldGen.Height(x, z) - 30.0;
			return false;
		}
	}
}

namespace HalfGripOpenWorld
{
	/// <summary>
	/// The way cars face on the grid. The game turns a grid's cars to its layout's heading (east for every map); the Multiplayer mod
	/// places every player that way too. In the Open World the grid is on a road that runs any way, so the heading is the road's.
	/// </summary>
	[HarmonyPatch(typeof(TrackManager), "LayoutHeading")]
	internal static class GridHeading
	{
		private static bool Prefix(ref Quaternion __result)
		{
			if (!WorldScene.Loaded)
			{
				return true;
			}
			__result = Quaternion.LookRotation(new Vector3((float)Network.SpawnDirX, 0f, (float)Network.SpawnDirZ), Vector3.up);
			return false;
		}
	}
}

namespace HalfGripOpenWorld
{
	/// <summary>
	/// The garage keeps an untouched copy of each of the cockpit's guns (see <see cref="GarageRefit.CaptureGun"/>), taken the moment
	/// before the gun first starts up: guns start as the scene loads, some before the gun activator wakes. First, ahead of the gun mods,
	/// which convert the gun and its rack in these same methods (the RPG's prefix replaces the SMG's Awake altogether).
	/// </summary>
	[HarmonyPatch]
	internal static class GunTemplates
	{
		private static IEnumerable<System.Reflection.MethodBase> TargetMethods()
		{
			yield return AccessTools.Method(typeof(NAutomaticGun), "Awake");
			yield return AccessTools.Method(typeof(NPumpActionGun), "Awake");
			yield return AccessTools.Method(typeof(NBreakActionGun), "Awake");
			yield return AccessTools.Method(typeof(NRevolver), "Awake");
		}

		[HarmonyPriority(Priority.First)]
		private static void Prefix(Gun __instance)
		{
			GarageRefit.CaptureGun(__instance);
		}
	}

	/// <summary>A gun's rack may wake before the gun does; the gun is copied then.</summary>
	[HarmonyPatch(typeof(MagazineManager), "Awake")]
	internal static class RackTemplates
	{
		[HarmonyPriority(Priority.First)]
		private static void Prefix(MagazineManager __instance)
		{
			GarageRefit.CaptureGun(__instance.GetComponentInParent<Gun>(true));
		}
	}

	/// <summary>As the cockpit wakes: the guns not copied yet, and the log line saying which were.</summary>
	[HarmonyPatch(typeof(GunLoadoutActivator), "Awake")]
	internal static class GunTemplatesCheck
	{
		private static void Prefix(GunLoadoutActivator __instance)
		{
			GarageRefit.CheckGuns(__instance);
		}
	}

	/// <summary>
	/// The game keeps the loadout of the race in hand in <see cref="PlayerLoadout"/>, and the next race's car is fitted from it when it
	/// is known: a garage's refit changes it, so before any car is fitted the loadout the race started with is put back (see
	/// <see cref="Garage.RestoreLoadout"/>). Garage changes last for one race.
	/// </summary>
	[HarmonyPatch(typeof(LoadoutSpawner), "Awake")]
	internal static class GarageForgets
	{
		[HarmonyPriority(Priority.First)]
		private static void Prefix()
		{
			Garage.RestoreLoadout();
		}
	}

	/// <summary>The game (the menu, a room's next race) set or dropped the loadout itself: that one stands, the garage has nothing to put back.</summary>
	[HarmonyPatch]
	internal static class GarageLoadoutSet
	{
		private static IEnumerable<System.Reflection.MethodBase> TargetMethods()
		{
			yield return AccessTools.Method(typeof(PlayerLoadout), nameof(PlayerLoadout.Set));
			yield return AccessTools.Method(typeof(PlayerLoadout), nameof(PlayerLoadout.Forget));
		}

		private static void Postfix()
		{
			Garage.Forget();
		}
	}
}
