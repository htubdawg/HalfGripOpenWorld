using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace HalfGripOpenWorld
{
	/// <summary>
	/// The garages in the world (<see cref="GarageSites"/>): a glowing circle on the ground at each, and a garage that opens when the car
	/// stops in one with the gearbox in neutral. While it is open nothing hurts the car (the game's own god mode, which crashes, hazards
	/// and the Multiplayer mod's hits all go through) and its health is held where it was, so a garage is no heal button unless Repair is
	/// pressed. Items are fitted through <see cref="GarageRefit"/> as they are picked, ammo and nitro filled a moment after.
	///
	/// Nothing is remembered: not the save, not the Multiplayer mod's loadout. The game's own note of the race's loadout
	/// (<see cref="PlayerLoadout"/>), which a refit changes and the next race's car is fitted from, is put back as the world goes.
	/// </summary>
	internal sealed class Garage : MonoBehaviour
	{
		internal static Garage Instance;

		/// <summary>The car must be slower than this (units a second, a car is 14 long) to count as stopped.</summary>
		private const float Stopped = 1.2f;

		/// <summary>How long the car must stand stopped in neutral before the garage opens: a gear change through neutral does not open it.</summary>
		private const float Settle = 0.6f;

		/// <summary>The circle's line: this wide, inside its radius.</summary>
		private const float RingWidth = 0.9f;

		/// <summary>The loadout the race started with, kept from the first refit until it is put back; null when there is nothing to put back.</summary>
		private static List<UpgradeComponent> startLoadout;

		/// <summary>True while the garage itself changes <see cref="PlayerLoadout"/>, so its own changes are not taken for the game's.</summary>
		internal static bool Refitting;

		private TrackManager tm;
		private Material ringMaterial;
		private readonly List<GameObject> rings = new List<GameObject>();
		private GarageUi ui;

		/// <summary>The garage whose circle the car is in (open or not), or null.</summary>
		internal GarageSites.Site Here { get; private set; }

		/// <summary>The garage that is open, or null.</summary>
		internal GarageSites.Site OpenAt { get; private set; }

		/// <summary>False after the garage is closed until the car leaves the circle, moves or goes into gear, so it does not open again at once.</summary>
		internal bool Armed { get; private set; } = true;

		private float readySince = -1f;
		private bool setGodMode;
		private float heldHp;
		private UnityEngine.Object muteKey;
		private int unmuteFrame = -1;
		private float refillAt = -1f;
		private Grabber grabber;

		internal CarLink Link => tm != null ? tm.playerCarlink : null;

		internal void Setup(TrackManager manager, Shader lit)
		{
			Instance = this;
			tm = manager;
			muteKey = this;
			ui = new GarageUi(this);
			try
			{
				ringMaterial = RingMaterial(lit, new Color(0.16f, 0.86f, 1f), "OpenWorld Garage");
				foreach (GarageSites.Site site in GarageSites.All)
				{
					rings.Add(Ring(site));
				}
				Mod.Log.LogInfo("Garages: " + GarageSites.All.Count + " (" + string.Join(", ", GarageSites.All.GroupBy(s => s.Kind).Select(g => g.Count() + " " + g.Key).ToArray()) + ")");
			}
			catch (Exception e)
			{
				Mod.Log.LogWarning("The garages' circles could not be made: " + e);
			}
		}

		// ---- the circles ------------------------------------------------------------------------------------------

		/// <summary>A glowing material for marks on the ground (the garages' circles, the events' start boxes and gates).</summary>
		internal static Material RingMaterial(Shader shader, Color colour, string name)
		{
			Material m = new Material(shader) { name = name, hideFlags = HideFlags.HideAndDontSave };
			if (m.HasProperty("_BaseColor"))
			{
				m.SetColor("_BaseColor", colour);
			}
			if (m.HasProperty("_Color"))
			{
				m.SetColor("_Color", colour);
			}
			if (m.HasProperty("_EmissionColor"))
			{
				m.EnableKeyword("_EMISSION");
				m.SetColor("_EmissionColor", colour * 1.6f);
			}
			if (m.HasProperty("_Smoothness"))
			{
				m.SetFloat("_Smoothness", 0f);
			}
			// Both faces drawn: a flat ring must show from above whichever way its triangles are wound.
			if (m.HasProperty("_Cull"))
			{
				m.SetFloat("_Cull", 0f);
			}
			return m;
		}

		/// <summary>
		/// A ring on the surface, following its slope, a little over it so it never flickers into it; and four short dashes inside
		/// pointing at the middle. A child of the world's root, so the floating origin moves it with everything else.
		/// </summary>
		private GameObject Ring(GarageSites.Site site)
		{
			const int segments = 48;
			const float over = 0.14f;
			List<Vector3> v = new List<Vector3>();
			List<int> t = new List<int>();
			Vector3 middle = Origin.Local(site.X, (float)(site.Y + Network.Lift), site.Z);
			Vector3 Point(double a, double r)
			{
				double x = site.X + Math.Cos(a) * r, z = site.Z + Math.Sin(a) * r;
				return new Vector3((float)(Math.Cos(a) * r), (float)(GarageSites.SurfaceAt(site, x, z) - site.Y) + over, (float)(Math.Sin(a) * r));
			}
			void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d)
			{
				int k = v.Count;
				v.Add(a);
				v.Add(b);
				v.Add(c);
				v.Add(d);
				// Wound to face up.
				t.AddRange(new[] { k, k + 2, k + 1, k + 1, k + 2, k + 3 });
			}
			double outer = site.Radius, inner = site.Radius - RingWidth;
			for (int i = 0; i < segments; i++)
			{
				double a0 = 2.0 * Math.PI * i / segments, a1 = 2.0 * Math.PI * (i + 1) / segments;
				Quad(Point(a0, outer), Point(a1, outer), Point(a0, inner), Point(a1, inner));
			}
			for (int i = 0; i < 4; i++)
			{
				double a = Math.PI * 0.5 * i + Math.PI * 0.25, w = 0.35 / (inner - 1.5);
				Quad(Point(a - w, inner - 1.2), Point(a + w, inner - 1.2), Point(a - w * 0.4, inner - 3.2), Point(a + w * 0.4, inner - 3.2));
			}
			Mesh mesh = new Mesh { name = "Garage ring" };
			mesh.SetVertices(v);
			mesh.SetTriangles(t, 0);
			mesh.RecalculateNormals();
			mesh.RecalculateBounds();
			GameObject go = new GameObject("Garage: " + site.Name);
			go.transform.SetParent(transform, false);
			go.transform.position = middle;
			go.AddComponent<MeshFilter>().sharedMesh = mesh;
			MeshRenderer renderer = go.AddComponent<MeshRenderer>();
			renderer.sharedMaterial = ringMaterial;
			renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
			return go;
		}

		/// <summary>The scene position of a garage's middle, for the label over it.</summary>
		internal static Vector3 ScenePoint(GarageSites.Site site)
		{
			return Origin.Local(site.X, (float)(site.Y + Network.Lift), site.Z);
		}

		// ---- opening and closing -----------------------------------------------------------------------------------

		private void Update()
		{
			if (unmuteFrame >= 0 && Time.frameCount > unmuteFrame)
			{
				unmuteFrame = -1;
				GameInput.Mute(muteKey, false);
			}
			if (refillAt >= 0f && Time.realtimeSinceStartup >= refillAt)
			{
				refillAt = -1f;
				GarageRefit.RefillEffects();
			}
			CarControllerNew car = tm != null ? tm.playerCarController : null;
			CarLink link = Link;
			if (car == null || link == null)
			{
				return;
			}
			Here = Inside(car);
			float speed = car.rb != null ? car.rb.linearVelocity.magnitude : 0f;
			// Neutral: the active shifter (H-pattern, sequential, automatic, Lenco: every kind reports here) in no gear.
			bool neutral = link.sa != null && (link.sa.gear == 0 || !link.sa.inGear);
			bool ready = Here != null && speed < Stopped && neutral;
			if (OpenAt != null)
			{
				// Pushed out of the circle or knocked moving (another player's car): the garage closes.
				if (Here != OpenAt || speed > Stopped * 4f)
				{
					Close();
				}
				return;
			}
			if (!ready)
			{
				Armed = true;
				readySince = -1f;
				return;
			}
			if (!Armed)
			{
				return;
			}
			if (readySince < 0f)
			{
				readySince = Time.time;
			}
			else if (Time.time - readySince >= Settle && link.hp > 0f)
			{
				Open(Here);
			}
		}

		/// <summary>The garage whose circle the car stands in, at about the circle's height (not on a bridge over it).</summary>
		private static GarageSites.Site Inside(CarControllerNew car)
		{
			Vector3 p = car.transform.position;
			Origin.World(p, out double x, out double z);
			GarageSites.Site site = GarageSites.At(x, z);
			if (site == null || Math.Abs(p.y - (GarageSites.SurfaceAt(site, x, z) + Network.Lift)) > 6.0)
			{
				return null;
			}
			return site;
		}

		private void LateUpdate()
		{
			// God mode stops the hits; the health is held too, so nothing else moves it (regeneration, drains) while the garage is open.
			CarLink link = Link;
			if (OpenAt != null && link != null)
			{
				CarLink.godMode = true;
				heldHp = Mathf.Min(heldHp, link.MaxHp);
				link.hp = heldHp;
			}
		}

		private void Open(GarageSites.Site site)
		{
			CarLink link = Link;
			OpenAt = site;
			Armed = false;
			readySince = -1f;
			heldHp = link.hp;
			setGodMode = !CarLink.godMode;
			CarLink.godMode = true;
			unmuteFrame = -1;
			GameInput.Mute(muteKey, true);
			grabber = UnityEngine.Object.FindFirstObjectByType<Grabber>();
			if (grabber != null)
			{
				grabber.SetHandVisible(false);
			}
			Cursor.lockState = CursorLockMode.Confined;
			Cursor.visible = true;
			ui.Opened();
			Mod.Log.LogInfo("Garage open: " + site.Name + ", health " + link.hp.ToString("0") + " of " + link.MaxHp.ToString("0"));
		}

		/// <summary>Closes the garage; the controls come back on the next frame (so the Escape that closed it does not also open the pause menu).</summary>
		internal void Close()
		{
			if (OpenAt == null)
			{
				return;
			}
			OpenAt = null;
			if (setGodMode)
			{
				CarLink.godMode = false;
				setGodMode = false;
			}
			unmuteFrame = Time.frameCount;
			if (grabber != null)
			{
				grabber.SetHandVisible(true);
				if (grabber.lockHardwareCursor)
				{
					Cursor.lockState = CursorLockMode.Locked;
					Cursor.visible = false;
				}
			}
			Mod.Log.LogInfo("Garage closed");
		}

		// ---- what the garage does ----------------------------------------------------------------------------------

		/// <summary>Fits <paramref name="wanted"/> to the car (the whole loadout), and fills ammo and nitro a moment later. False if nothing changed.</summary>
		internal bool Fit(List<UpgradeComponent> wanted)
		{
			if (startLoadout == null && PlayerLoadout.Known)
			{
				startLoadout = new List<UpgradeComponent>(PlayerLoadout.Equipped);
			}
			bool done;
			Refitting = true;
			try
			{
				done = GarageRefit.Apply(wanted);
			}
			catch (Exception e)
			{
				Mod.Log.LogWarning("Garage: the refit failed: " + e);
				done = false;
			}
			finally
			{
				Refitting = false;
			}
			if (done)
			{
				refillAt = Time.realtimeSinceStartup + 0.25f;
			}
			return done;
		}

		/// <summary>A full repair: the only way the garage raises the car's health.</summary>
		internal void Repair()
		{
			CarLink link = Link;
			if (link == null)
			{
				return;
			}
			try
			{
				Effects.Refill(RefillTarget.Health, 1f);
			}
			catch (Exception e)
			{
				Mod.Log.LogWarning("Garage: the repair's refill failed: " + e.Message);
			}
			link.hp = link.MaxHp;
			heldHp = link.hp;
		}

		internal void Refill()
		{
			GarageRefit.RefillEffects();
		}

		private void OnGUI()
		{
			ui.Draw();
		}

		private void OnDestroy()
		{
			Close();
			if (unmuteFrame >= 0)
			{
				GameInput.Mute(muteKey, false);
				unmuteFrame = -1;
			}
			RestoreLoadout();
			if (ringMaterial != null)
			{
				Destroy(ringMaterial);
			}
			foreach (GameObject ring in rings)
			{
				if (ring != null)
				{
					Destroy(ring.GetComponent<MeshFilter>().sharedMesh);
				}
			}
			if (Instance == this)
			{
				Instance = null;
			}
		}

		// ---- not remembered ----------------------------------------------------------------------------------------

		/// <summary>
		/// Puts back the loadout the race started with, in the game's note of it and its stats, if a garage changed it: the next race's
		/// car (in a room, the next race starts without the menu) is fitted from that note. Runs as the world goes and before any car is fitted.
		/// </summary>
		internal static void RestoreLoadout()
		{
			if (startLoadout == null)
			{
				return;
			}
			List<UpgradeComponent> list = startLoadout;
			startLoadout = null;
			Refitting = true;
			try
			{
				PlayerStats.ResetAllUpgrades();
				foreach (UpgradeComponent item in list)
				{
					item.Apply();
				}
				PlayerLoadout.Set(list);
				Mod.Log.LogInfo("Garage: the race's own loadout is back for the next race (" + list.Count + " items)");
			}
			catch (Exception e)
			{
				Mod.Log.LogWarning("Garage: could not put the race's loadout back: " + e);
			}
			finally
			{
				Refitting = false;
			}
		}

		/// <summary>The game (the menu, a room) set the loadout itself: that one stands, there is nothing to put back.</summary>
		internal static void Forget()
		{
			if (!Refitting)
			{
				startLoadout = null;
			}
		}

		// ---- what is offered ---------------------------------------------------------------------------------------

		private static bool mpLooked;
		private static MethodInfo mpBanned;

		/// <summary>
		/// Whether the garage offers this item: every item available by normal means, the game's own and the mods', locked or not. Not the
		/// ones the game keeps out of play (<c>disabled</c>: the shop, unlocks and rewards skip them too), not consumables (used up from
		/// the inventory, no slot), and in a Multiplayer room not what the Multiplayer mod keeps out of its own garage (its
		/// <c>Loadout.Banned</c>: slow motion, rerolls, run boons and the like break a shared race).
		/// </summary>
		internal static bool Offered(UpgradeComponent u)
		{
			if (u == null || u.disabled || u.upgradeCategory == UpgradeCategory.CONSUMABLE || string.IsNullOrEmpty(u.id))
			{
				return false;
			}
			return !MultiplayerLink.InRoom || !BannedInRoom(u);
		}

		private static bool BannedInRoom(UpgradeComponent u)
		{
			if (!mpLooked)
			{
				mpLooked = true;
				Type loadout = AccessTools.TypeByName("Multiplayer.Loadout");
				mpBanned = loadout != null ? AccessTools.Method(loadout, "Banned", new[] { typeof(UpgradeComponent) }) : null;
				if (mpBanned == null)
				{
					Mod.Log.LogWarning("Garage: the Multiplayer mod's list of banned items was not found; its usual bans are applied instead.");
				}
			}
			if (mpBanned != null)
			{
				try
				{
					return (bool)mpBanned.Invoke(null, new object[] { u });
				}
				catch (Exception e)
				{
					Mod.Log.LogWarning("Garage: the Multiplayer mod's ban check failed (" + e.Message + "); its usual bans are applied instead.");
					mpBanned = null;
				}
			}
			return !(u is NO2) && (u is SlowMotion || u is RerollMapUpgrade || u is RunBoonUpgrade || u is BeerUpgrade);
		}
	}
}
