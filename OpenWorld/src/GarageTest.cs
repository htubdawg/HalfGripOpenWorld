using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace HalfGripOpenWorld
{
	/// <summary>
	/// A test of the garage the world may get: GarageTestKey fits the next gun and the next steering wheel to the car where it stands,
	/// then fills the ammo, the effects and the health. It goes through the game's own live refit (<see cref="LoadoutRefit.Swap"/>,
	/// which the dev console's giveitem uses in a race), so it shows whether that holds up outside the console, in a Multiplayer room
	/// too, before any garage is built on it. Only the car changes: the save and the Multiplayer loadout keep what was fitted, so the
	/// next race starts with the old fit.
	/// </summary>
	internal static class GarageTest
	{
		/// <summary>When the refill is due: the new gun's magazines only listen for a refill from their Start, the frame after the swap.</summary>
		private static float refillAt = -1f;

		/// <summary>Every frame of an Open World race (from the streamer).</summary>
		internal static void Tick()
		{
			if (refillAt >= 0f && Time.realtimeSinceStartup >= refillAt)
			{
				refillAt = -1f;
				Refill();
			}
			if (!Mod.GarageTest.Value || !GameInput.GetKeyDown(Mod.GarageTestKey.Value))
			{
				return;
			}
			int step = GameInput.GetKey(KeyCode.LeftShift) || GameInput.GetKey(KeyCode.RightShift) ? -1 : 1;
			try
			{
				Swap(step);
			}
			catch (Exception e)
			{
				Mod.Log.LogWarning("Garage test: the swap failed: " + e);
			}
		}

		private static void Swap(int step)
		{
			if (!PlayerLoadout.Known)
			{
				Mod.Log.LogWarning("Garage test: the game does not know what is fitted (the race was not started from the menu).");
				return;
			}
			List<UpgradeComponent> loadout = new List<UpgradeComponent>(PlayerLoadout.Equipped);
			string gun = Next<GunUpgrade>(loadout, step);
			string wheel = Next<ServoUpgrade>(loadout, step);
			TextController.EnqueueRandomText(1f, gun + " / " + wheel);
			refillAt = Time.realtimeSinceStartup + 0.25f;
		}

		/// <summary>Swaps the fitted item of this kind for the next (or previous) one in the catalog, by id; returns the name of what is fitted now.</summary>
		private static string Next<T>(List<UpgradeComponent> loadout, int step) where T : UpgradeComponent
		{
			List<T> all = Catalog().OfType<T>().Where(u => !u.disabled).GroupBy(u => u.id).Select(g => g.First()).OrderBy(u => u.id, StringComparer.Ordinal).ToList();
			T off = loadout.OfType<T>().FirstOrDefault();
			if (all.Count == 0)
			{
				return off != null ? off.Name : "nothing";
			}
			int at = off == null ? -1 : all.FindIndex(u => u.id == off.id);
			int to = ((at < 0 ? (step > 0 ? -1 : 0) : at) + step + all.Count) % all.Count;
			T on = all[to];
			if (on == off)
			{
				return on.Name;
			}
			if (off != null)
			{
				loadout.Remove(off);
			}
			loadout.Add(on);
			LoadoutRefit.Swap(off, on, loadout);
			Mod.Log.LogInfo("Garage test: " + (off != null ? off.id : "nothing") + " -> " + on.id + " (" + (to + 1) + " of " + all.Count + " " + typeof(T).Name + ")");
			return on.Name;
		}

		private static List<UpgradeComponent> Catalog()
		{
			ItemCatalog catalog = ItemCatalog.Instance;
			if (catalog != null && catalog.items != null)
			{
				return catalog.items;
			}
			UpgradeMapper mapper = UpgradeMapper.Resolve();
			return mapper == null ? new List<UpgradeComponent>() : mapper.allUpgrades;
		}

		/// <summary>What a garage would give: a full tank of every effect (ammo, nitro, charges) and a full repair, the road pickups' way.</summary>
		private static void Refill()
		{
			try
			{
				float effects = Effects.Refill(RefillTarget.AllEffects, 1f);
				float health = Effects.Refill(RefillTarget.Health, 1f);
				Mod.Log.LogInfo("Garage test: refilled (effects " + effects.ToString("0.##") + ", health " + health.ToString("0.##") + ")");
			}
			catch (Exception e)
			{
				Mod.Log.LogWarning("Garage test: the refill failed: " + e);
			}
		}
	}
}
