using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace HalfGripOpenWorld
{
	/// <summary>
	/// A test of the garage the world may get: the Garage test key (Settings > Controls, F9 by default) fits the next gun and the next steering wheel to the car where it stands,
	/// then fills the ammo, the effects and the health. It goes through the game's own live refit (<see cref="LoadoutRefit.Swap"/>,
	/// which the dev console's giveitem uses in a race), so it shows whether that holds up outside the console, in a Multiplayer room
	/// too, before any garage is built on it. Only the car changes: the save and the Multiplayer loadout keep what was fitted, so the
	/// next race starts with the old fit.
	///
	/// The refit alone is not enough for guns. The cockpit has one gun per kind (SMG, pump shotgun, revolver, sawed-off), all built in;
	/// the refit only switches which one is on. A gun reads its item (capacity, magazines, blood-fed) and the gun mods convert it (the
	/// M1911, Minigun, RPG and Sniper Rifle all turn the SMG into theirs) once, when it first starts up. So a swap kept the last gun's
	/// rounds and set-up: after the BloodDrainer the SMG had no magazines for any SMG after it, and no modded gun was ever converted.
	/// Every gun swap therefore puts a fresh copy of that kind's gun in, which starts up for the item now fitted.
	/// </summary>
	internal static class GarageTest
	{
		/// <summary>The protected field <c>Gun.Awake</c> fills in: still null on a gun that has never started up.</summary>
		private static readonly FieldInfo GunGrabber = AccessTools.Field(typeof(Gun), "grabber");

		/// <summary>Untouched copies of the cockpit's guns, by kind, under an inactive object so they never start up.</summary>
		private static readonly Dictionary<GunType, GameObject> templates = new Dictionary<GunType, GameObject>();
		private static GameObject holder;

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
			if (!Mod.GarageTest.Value || Mod.GarageTestKey == null || !Mod.GarageTestKey.Down)
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
			if (on is GunUpgrade gun)
			{
				FreshGun(gun.gunType);
			}
			LoadoutRefit.Swap(off, on, loadout);
			Mod.Log.LogInfo("Garage test: " + (off != null ? off.id : "nothing") + " -> " + on.id + " (" + (to + 1) + " of " + all.Count + " " + typeof(T).Name + ")");
			return on.Name;
		}

		/// <summary>
		/// Called as the cockpit wakes (before the gun activator switches the fitted gun on): copies every gun that has not started up.
		/// Only in an Open World race with the test on. A gun that has already started cannot give an untouched copy; it is logged, and
		/// swaps to that kind keep its state.
		/// </summary>
		internal static void CaptureGuns(GunLoadoutActivator activator)
		{
			templates.Clear();
			if (holder != null)
			{
				UnityEngine.Object.Destroy(holder);
			}
			holder = null;
			if (!Mod.GarageTest.Value || Mod.Map == null || RunState.ActiveMap != Mod.Map)
			{
				return;
			}
			try
			{
				Transform root = activator.searchRoot ? activator.searchRoot : activator.transform.root;
				holder = new GameObject("OpenWorldGunTemplates");
				holder.SetActive(false);
				List<string> taken = new List<string>();
				foreach (Gun gun in root.GetComponentsInChildren<Gun>(true))
				{
					if (templates.ContainsKey(gun.RequiredGunType))
					{
						continue;
					}
					if (GunGrabber.GetValue(gun) != null)
					{
						Mod.Log.LogWarning("Garage test: " + gun.name + " (" + gun.RequiredGunType + ") had already started, so swaps to that kind of gun keep its state.");
						continue;
					}
					GameObject copy = UnityEngine.Object.Instantiate(gun.gameObject, holder.transform, false);
					copy.name = gun.gameObject.name;
					copy.SetActive(false);
					templates[gun.RequiredGunType] = copy;
					taken.Add(gun.name + " (" + gun.RequiredGunType + ")");
				}
				Mod.Log.LogInfo("Garage test: copies of " + string.Join(", ", taken.ToArray()));
			}
			catch (Exception e)
			{
				Mod.Log.LogWarning("Garage test: the guns could not be copied: " + e);
			}
		}

		/// <summary>
		/// Puts a fresh copy of this kind's gun in the cockpit in place of the one there, before the refit switches it on. The old one is
		/// taken out of the car first, so the refit's gun activator (which looks through the car) cannot find it again before it goes.
		/// </summary>
		private static void FreshGun(GunType type)
		{
			if (!templates.TryGetValue(type, out GameObject template) || template == null)
			{
				return;
			}
			Gun old = UnityEngine.Object.FindObjectsByType<Gun>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(g => g.RequiredGunType == type && g.transform.root != holder.transform);
			if (old == null)
			{
				Mod.Log.LogWarning("Garage test: no " + type + " gun in the car to replace.");
				return;
			}
			Transform parent = old.transform.parent;
			int index = old.transform.GetSiblingIndex();
			GameObject dead = old.gameObject;
			dead.SetActive(false);
			foreach (Component c in dead.GetComponentsInChildren<Component>(true))
			{
				Rig.Unregister(c);
			}
			dead.transform.SetParent(null, false);
			UnityEngine.Object.Destroy(dead);
			GameObject fresh = UnityEngine.Object.Instantiate(template, parent, false);
			fresh.name = template.name;
			fresh.transform.SetSiblingIndex(index);
			Mod.Info("Garage test: a fresh " + fresh.name + " in the car");
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
