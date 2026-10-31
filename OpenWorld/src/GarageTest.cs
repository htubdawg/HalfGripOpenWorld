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
	/// A press reads the car's loadout, empties the car and fits the new loadout (the old one with the next gun and wheel), as a garage
	/// would. Emptying includes the cockpit's guns: there is one built-in gun per kind (SMG, pump shotgun, revolver, sawed-off), and
	/// taking a gun item off only switches its gun off. A gun reads its item and the gun mods convert it (the M1911, Minigun, RPG and
	/// Sniper Rifle all turn the SMG into theirs, building their models into its held model under the camera) once, when it first
	/// starts up, so each gun and its held model are put back as the scene built them, from copies taken before they started.
	/// </summary>
	internal static class GarageTest
	{
		/// <summary>The protected field <c>Gun.Awake</c> fills in: still null on a gun that has never started up.</summary>
		private static readonly FieldInfo GunGrabber = AccessTools.Field(typeof(Gun), "grabber");

		/// <summary>
		/// An untouched copy of one of the cockpit's guns and of its held model (the gun's references into the held model point at the
		/// copy), and their sprites as copied. The copies are in the scene, and the gun mods reskin by sprite name across the whole scene
		/// as their gun starts (HalfGripTools' Sprites.Reskin, which reaches inactive objects too): the Sniper Rifle's art landed on the
		/// SMG's copy and so on every SMG after it. Each fresh gun gets the sprites back.
		/// </summary>
		private sealed class Template
		{
			internal GameObject Gun;
			internal Sprite[] GunSprites;
			internal GameObject Held;
			internal Sprite[] HeldSprites;
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

		/// <summary>The three steps: read the car's loadout, empty the car, fit the new loadout (the old one with the next gun and wheel).</summary>
		private static void Swap(int step)
		{
			if (!PlayerLoadout.Known)
			{
				Mod.Log.LogWarning("Garage test: the game does not know what is fitted (the race was not started from the menu).");
				return;
			}
			List<UpgradeComponent> loadout = new List<UpgradeComponent>(PlayerLoadout.Equipped);
			List<UpgradeComponent> wanted = new List<UpgradeComponent>(loadout);
			GunUpgrade gun = Next<GunUpgrade>(wanted, step);
			ServoUpgrade wheel = Next<ServoUpgrade>(wanted, step);
			Empty(loadout);
			Fit(wanted);
			Mod.Log.LogInfo("Garage test: emptied " + loadout.Count + " item(s), fitted " + string.Join(", ", wanted.Select(u => u.id).ToArray()));
			TextController.EnqueueRandomText(1f, (gun != null ? gun.Name : "no gun") + " / " + (wheel != null ? wheel.Name : "no wheel"));
			refillAt = Time.realtimeSinceStartup + 0.25f;
		}

		/// <summary>Puts the next (or previous) item of this kind in the catalog, by id, in the loadout in place of the one there; returns it.</summary>
		private static T Next<T>(List<UpgradeComponent> loadout, int step) where T : UpgradeComponent
		{
			List<T> all = Catalog().OfType<T>().Where(u => !u.disabled).GroupBy(u => u.id).Select(g => g.First()).OrderBy(u => u.id, StringComparer.Ordinal).ToList();
			T off = loadout.OfType<T>().FirstOrDefault();
			if (all.Count == 0)
			{
				return off;
			}
			int at = off == null ? -1 : all.FindIndex(u => u.id == off.id);
			T on = all[((at < 0 ? (step > 0 ? -1 : 0) : at) + step + all.Count) % all.Count];
			int index = off == null ? -1 : loadout.IndexOf(off);
			if (index >= 0)
			{
				loadout[index] = on;
			}
			else
			{
				loadout.Add(on);
			}
			return on;
		}

		/// <summary>
		/// Takes every item off the car, then puts the cockpit's guns and their held models back as the scene built them. Taking a gun
		/// item off only switches its gun off: the gun keeps its rounds and whatever its item and the gun mods set up when it started.
		/// </summary>
		private static void Empty(List<UpgradeComponent> loadout)
		{
			List<UpgradeComponent> left = new List<UpgradeComponent>(loadout);
			for (int i = loadout.Count - 1; i >= 0; i--)
			{
				left.Remove(loadout[i]);
				LoadoutRefit.Swap(loadout[i], null, left);
			}
			TakeDownGunMods();
			foreach (GunType type in templates.Keys.ToList())
			{
				ResetGun(type);
			}
		}

		/// <summary>
		/// Takes down what a gun mod built outside its gun, which putting the gun back as built cannot reach. The Minigun builds its roof
		/// gun on the car body (and points the gun's fire point at its muzzle) and takes it down only in <c>MiniHeat.Cleanup</c>, which
		/// its own <c>OnDestroy</c> does not call: so the roof gun stayed on the car after swapping away from the Minigun. The Sniper Rifle's
		/// laser dot and scope overlay go in its <c>OnDestroy</c>, and the RPG's rockets and scorch marks are meant to outlive the gun. The
		/// Minigun's classes are internal: found by name. A Minigun fitted again builds a new roof gun as its fresh gun starts.
		/// </summary>
		private static void TakeDownGunMods()
		{
			// The Minigun rides on the SMG: with no copy of the SMG to put back, its gun and roof gun stay as they are, together.
			if (!templates.ContainsKey(GunType.SMG_RIFLE))
			{
				return;
			}
			// The roof guns first, taken off the car at once so a new one (which looks for an old one on the car) does not find them.
			Type roof = AccessTools.TypeByName("HalfGripMinigun.RoofGun");
			int removed = 0;
			if (roof != null)
			{
				foreach (UnityEngine.Object o in UnityEngine.Object.FindObjectsByType(roof, FindObjectsInactive.Include, FindObjectsSortMode.None))
				{
					if (o is Component c && c.gameObject.scene.handle == holderScene)
					{
						c.transform.SetParent(null, false);
						UnityEngine.Object.Destroy(c.gameObject);
						removed++;
					}
				}
			}
			// Then the heat meters' cleanup: the gun's fire point back off the roof, the hum stopped, the overheat padlock gone.
			Type heat = AccessTools.TypeByName("HalfGripMinigun.MiniHeat");
			MethodInfo cleanup = heat == null ? null : AccessTools.Method(heat, "Cleanup");
			if (cleanup != null)
			{
				foreach (UnityEngine.Object o in UnityEngine.Object.FindObjectsByType(heat, FindObjectsInactive.Include, FindObjectsSortMode.None))
				{
					if (o is Component c && c.transform.root != holder.transform)
					{
						cleanup.Invoke(c, null);
					}
				}
			}
			if (removed > 0)
			{
				Mod.Info("Garage test: took " + removed + " Minigun roof gun(s) off the car");
			}
		}

		/// <summary>Fits the loadout to the empty car, one item at a time, the way the refit fits one.</summary>
		private static void Fit(List<UpgradeComponent> loadout)
		{
			List<UpgradeComponent> on = new List<UpgradeComponent>();
			foreach (UpgradeComponent item in loadout)
			{
				on.Add(item);
				LoadoutRefit.Swap(null, item, on);
			}
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
				t.GunSprites = Sprites(t.Gun);
				if (gun.aimModel != null)
				{
					t.Held = Copy(gun.aimModel.gameObject, holder.transform);
					t.HeldSprites = Sprites(t.Held);
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

		private static Sprite[] Sprites(GameObject root)
		{
			return root.GetComponentsInChildren<SpriteRenderer>(true).Select(r => r.sprite).ToArray();
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
						if (!(f.IsPublic || f.IsDefined(typeof(SerializeField), false)))
						{
							continue;
						}
						if (typeof(UnityEngine.Object).IsAssignableFrom(f.FieldType))
						{
							UnityEngine.Object value = f.GetValue(c) as UnityEngine.Object;
							UnityEngine.Object mapped = value == null ? null : Mapped(value, from, to);
							if (mapped != null)
							{
								f.SetValue(c, mapped);
							}
						}
						else if (IsObjectList(f.FieldType) && f.GetValue(c) is System.Collections.IList list)
						{
							// Arrays and lists too: the sawed-off fires from its barrels, a Transform[] on its held model.
							for (int i = 0; i < list.Count; i++)
							{
								UnityEngine.Object value = list[i] as UnityEngine.Object;
								UnityEngine.Object mapped = value == null ? null : Mapped(value, from, to);
								if (mapped != null)
								{
									list[i] = mapped;
								}
							}
						}
					}
				}
			}
		}

		/// <summary>An array or List&lt;T&gt; of Unity objects, the collections Unity serializes.</summary>
		private static bool IsObjectList(Type type)
		{
			Type element = type.IsArray ? type.GetElementType() : type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>) ? type.GetGenericArguments()[0] : null;
			return element != null && typeof(UnityEngine.Object).IsAssignableFrom(element);
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
		/// Puts a fresh copy of this kind's gun and of its held model in place of the ones there. The old gun is taken out of the car
		/// first, so the refit's gun activator (which looks through the car) cannot find it again before it goes.
		/// </summary>
		private static void ResetGun(GunType type)
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
			GameObject gun = Replace(old.gameObject, template.Gun, template.GunSprites);
			if (template.Held != null && oldHeld != null)
			{
				GameObject held = Replace(oldHeld.gameObject, template.Held, template.HeldSprites);
				Remap(gun, template.Held.transform, held.transform);
			}
			Mod.Info("Garage test: " + gun.name + " back as built");
		}

		/// <summary>Swaps an object in the scene for a copy of the template, in the same place among its siblings, with the template's sprites as copied.</summary>
		private static GameObject Replace(GameObject dead, GameObject template, Sprite[] sprites)
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
			SpriteRenderer[] renderers = fresh.GetComponentsInChildren<SpriteRenderer>(true);
			if (sprites != null && renderers.Length == sprites.Length)
			{
				for (int i = 0; i < renderers.Length; i++)
				{
					renderers[i].sprite = sprites[i];
				}
			}
			else
			{
				Mod.Log.LogWarning("Garage test: " + fresh.name + " has " + renderers.Length + " sprites, its copy had " + (sprites == null ? 0 : sprites.Length) + "; its art is left as it is.");
			}
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
