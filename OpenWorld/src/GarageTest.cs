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
	/// Every gun swap therefore puts a fresh copy of that kind's gun in, which starts up for the item now fitted. The gun's held model
	/// (the 3D one raised to the camera, which lives under the camera, outside the gun) is replaced with it: the gun mods build their
	/// models into it, so one left over showed the Sniper Rifle's model on every gun after it.
	/// </summary>
	internal static class GarageTest
	{
		/// <summary>The protected field <c>Gun.Awake</c> fills in: still null on a gun that has never started up.</summary>
		private static readonly FieldInfo GunGrabber = AccessTools.Field(typeof(Gun), "grabber");

		/// <summary>An untouched copy of one of the cockpit's guns and of its held model; the gun's references into the held model point at the copy.</summary>
		private sealed class Template
		{
			internal GameObject Gun;
			internal GameObject Held;
		}

		/// <summary>The copies, by kind, under an inactive object so they never start up; and the scene they were taken in.</summary>
		private static readonly Dictionary<GunType, Template> templates = new Dictionary<GunType, Template>();
		private static GameObject holder;
		private static int holderScene = -1;

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
		/// Copies a gun the moment before it first starts up (from the prefixes on the guns' and the racks' Awake, ahead of the gun mods'
		/// own, which convert it there). Only in an Open World race with the test on, and once per kind and scene. A gun that has already
		/// started cannot give an untouched copy and is left; <see cref="CheckGuns"/> says which.
		/// </summary>
		internal static void CaptureGun(Gun gun)
		{
			if (gun == null || !Mod.GarageTest.Value || Mod.Map == null || RunState.ActiveMap != Mod.Map)
			{
				return;
			}
			try
			{
				int scene = gun.gameObject.scene.handle;
				if (holder == null || holderScene != scene)
				{
					templates.Clear();
					if (holder != null)
					{
						UnityEngine.Object.Destroy(holder);
					}
					holder = new GameObject("OpenWorldGunTemplates");
					holder.SetActive(false);
					// Made while the scene loads: put with the gun, not in whatever scene is active (the one being left).
					UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(holder, gun.gameObject.scene);
					holderScene = scene;
				}
				if (gun.transform.root == holder.transform || templates.ContainsKey(gun.RequiredGunType) || GunGrabber.GetValue(gun) != null)
				{
					return;
				}
				Template t = new Template();
				t.Gun = Copy(gun.gameObject, holder.transform);
				if (gun.aimModel != null)
				{
					t.Held = Copy(gun.aimModel.gameObject, holder.transform);
					Remap(t.Gun, gun.aimModel, t.Held.transform);
				}
				templates[gun.RequiredGunType] = t;
				Mod.Info("Garage test: copied " + gun.name + " (" + gun.RequiredGunType + ")" + (t.Held != null ? " and " + t.Held.name : ""));
			}
			catch (Exception e)
			{
				Mod.Log.LogWarning("Garage test: " + gun.name + " could not be copied: " + e);
			}
		}

		/// <summary>As the cockpit wakes (the gun activator, before it switches the fitted gun on): copies what is left and says what is missing.</summary>
		internal static void CheckGuns(GunLoadoutActivator activator)
		{
			if (!Mod.GarageTest.Value || Mod.Map == null || RunState.ActiveMap != Mod.Map)
			{
				return;
			}
			Transform root = activator.searchRoot ? activator.searchRoot : activator.transform.root;
			List<string> copied = new List<string>();
			foreach (Gun gun in root.GetComponentsInChildren<Gun>(true))
			{
				CaptureGun(gun);
				if (templates.ContainsKey(gun.RequiredGunType))
				{
					copied.Add(gun.name + " (" + gun.RequiredGunType + ")");
				}
				else
				{
					Mod.Log.LogWarning("Garage test: " + gun.name + " (" + gun.RequiredGunType + ") had already started, so swaps to that kind of gun keep its state.");
				}
			}
			Mod.Log.LogInfo("Garage test: copies of " + string.Join(", ", copied.ToArray()));
		}

		private static GameObject Copy(GameObject original, Transform parent)
		{
			GameObject copy = UnityEngine.Object.Instantiate(original, parent, false);
			copy.name = original.name;
			copy.SetActive(false);
			return copy;
		}

		/// <summary>
		/// Points every reference the components under <paramref name="root"/> hold into the tree at <paramref name="from"/> at the same
		/// object in the tree at <paramref name="to"/> (same path, same component). Unity only does this for references inside the one
		/// object it copies, and the gun and its held model are copied apart (the gun's fire point is on the held model).
		/// </summary>
		private static void Remap(GameObject root, Transform from, Transform to)
		{
			foreach (Component c in root.GetComponentsInChildren<Component>(true))
			{
				if (c == null)
				{
					continue;
				}
				for (Type type = c.GetType(); type != null && type != typeof(MonoBehaviour) && type != typeof(Component); type = type.BaseType)
				{
					foreach (FieldInfo f in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
					{
						if (!typeof(UnityEngine.Object).IsAssignableFrom(f.FieldType) || !(f.IsPublic || f.IsDefined(typeof(SerializeField), false)))
						{
							continue;
						}
						UnityEngine.Object value = f.GetValue(c) as UnityEngine.Object;
						UnityEngine.Object mapped = value == null ? null : Mapped(value, from, to);
						if (mapped != null)
						{
							f.SetValue(c, mapped);
						}
					}
				}
			}
		}

		/// <summary>The object in the <paramref name="to"/> tree standing where <paramref name="value"/> stands in the <paramref name="from"/> tree, or null.</summary>
		private static UnityEngine.Object Mapped(UnityEngine.Object value, Transform from, Transform to)
		{
			Transform at = value is Component vc ? vc.transform : value is GameObject vg ? vg.transform : null;
			if (at == null || !at.IsChildOf(from))
			{
				return null;
			}
			List<int> path = new List<int>();
			for (Transform t = at; t != from; t = t.parent)
			{
				path.Add(t.GetSiblingIndex());
			}
			Transform target = to;
			for (int i = path.Count - 1; i >= 0; i--)
			{
				if (path[i] >= target.childCount)
				{
					return null;
				}
				target = target.GetChild(path[i]);
			}
			if (value is GameObject)
			{
				return target.gameObject;
			}
			Component[] same = at.GetComponents(value.GetType());
			Component[] there = target.GetComponents(value.GetType());
			int index = Array.IndexOf(same, (Component)value);
			return index >= 0 && index < there.Length ? there[index] : null;
		}

		/// <summary>
		/// Puts a fresh copy of this kind's gun and of its held model in place of the ones there, before the refit switches the gun on.
		/// The old gun is taken out of the car first, so the refit's gun activator (which looks through the car) cannot find it again.
		/// </summary>
		private static void FreshGun(GunType type)
		{
			if (!templates.TryGetValue(type, out Template template) || template.Gun == null)
			{
				return;
			}
			Gun old = UnityEngine.Object.FindObjectsByType<Gun>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(g => g.RequiredGunType == type && g.transform.root != holder.transform);
			if (old == null)
			{
				Mod.Log.LogWarning("Garage test: no " + type + " gun in the car to replace.");
				return;
			}
			Transform oldHeld = old.aimModel;
			GameObject gun = Replace(old.gameObject, template.Gun);
			if (template.Held != null && oldHeld != null)
			{
				GameObject held = Replace(oldHeld.gameObject, template.Held);
				Remap(gun, template.Held.transform, held.transform);
			}
			Mod.Info("Garage test: a fresh " + gun.name + " in the car");
		}

		/// <summary>Swaps an object in the scene for a copy of the template, in the same place among its siblings.</summary>
		private static GameObject Replace(GameObject dead, GameObject template)
		{
			Transform parent = dead.transform.parent;
			int index = dead.transform.GetSiblingIndex();
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
			return fresh;
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
