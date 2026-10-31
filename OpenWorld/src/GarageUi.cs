using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace HalfGripOpenWorld
{
	/// <summary>
	/// What the player sees of a garage: a "GARAGE" label over each circle nearby, a line telling them how to open it while they are in
	/// one, and the garage's own window while it is open. The game's drag-and-drop garage exists only in the menu scene, so this is drawn
	/// with Unity's immediate-mode GUI, as the debug overlay is, at a size that follows the screen's height, in the game's pixel look:
	/// bevelled panels made of chunky pixels (<see cref="Px"/> units each) in the vanilla garage's colours (worn grey metal, black
	/// wells, red and the unlock bar's green for accents) with grime over them, and the m6x11 font.
	///
	/// The window: the car's slots down the left, grouped and in the order of their categories (the car, the cockpit, extras), each with
	/// what is fitted; the items that fit the chosen slot on the right, best tier first, with the pointed-at one's description under them;
	/// health with Repair, Refill for ammo and nitro, and Close along the bottom. Items are dragged onto a slot to fit them and off it to
	/// take them off, as in the game's own garage; a right click fits an item (or takes it off if it is fitted), and takes a slot's off.
	/// </summary>
	internal sealed class GarageUi
	{
		/// <summary>One of the car's slots: the category it takes, its name in the window, and what is in it.</summary>
		private sealed class Slot
		{
			public UpgradeCategory Category;
			public string Label;
			public string Group;
			public UpgradeComponent Item;
			/// <summary>Where it was drawn this frame (window units), for dropping onto it.</summary>
			public Rect Rect;
		}

		/// <summary>The slots in the order shown, under their group headings: two medium slots, one of every other kind (the game's own garage has the same).</summary>
		private static readonly (UpgradeCategory category, string label, string group)[] Layout =
		{
			(UpgradeCategory.CHASSIS, "Chassis", "Car"),
			(UpgradeCategory.TIRES, "Tires", "Car"),
			(UpgradeCategory.ENGINE, "Engine", "Car"),
			(UpgradeCategory.TRANSMISSION, "Shifter", "Car"),
			(UpgradeCategory.SERVO, "Steering wheel", "Cockpit"),
			(UpgradeCategory.GUN, "Gun", "Cockpit"),
			(UpgradeCategory.MEDIUM, "Medium slot 1", "Cockpit"),
			(UpgradeCategory.MEDIUM, "Medium slot 2", "Cockpit"),
			(UpgradeCategory.PASSIVE, "Passive", "Extras"),
			(UpgradeCategory.EXTERNAL, "Exterior", "Extras"),
		};

		private const float WindowW = 1180f;
		private const float WindowH = 800f;

		/// <summary>One of the pixel look's pixels, in window units (screen pixels at 1080 high): chunky, as the game's own UI is.</summary>
		private const int Px = 3;

		/// <summary>How far the mouse moves with the button down before a press becomes a drag (window units).</summary>
		private const float DragStart = 6f;

		private readonly Garage garage;
		private readonly List<Slot> slots = new List<Slot>();
		/// <summary>Items on the car that no slot here takes: carried through every refit untouched.</summary>
		private readonly List<UpgradeComponent> extras = new List<UpgradeComponent>();
		private readonly Dictionary<UpgradeCategory, List<UpgradeComponent>> offered = new Dictionary<UpgradeCategory, List<UpgradeComponent>>();
		private int selected;
		private Vector2 scroll;
		private UpgradeComponent hovered;
		/// <summary>The item last clicked in the list: its description stays shown when nothing is pointed at.</summary>
		private UpgradeComponent pinned;
		private string message;
		private float messageUntil;

		/// <summary>The mouse in window units, read before the item list's scroll view (inside it, positions are the list's own).</summary>
		private Vector2 mouse;
		/// <summary>The item under a held left button, and the slot it came from (-1: the list); a drag once the mouse has moved.</summary>
		private UpgradeComponent pressed;
		private int pressedFrom = -1;
		private Vector2 pressedAt;
		private bool dragging;
		private Rect listArea;

		private bool styled;
		private GUIStyle title, text, small, smallRight, faint, heading, label, prompt, detailName, detail, dropLabel;
		private GUIStyle button, panel, inset, slotOff, slotOn, slotDrop, cardOff, cardHover, cardOn, iconBox, barBack;
		private Texture2D dark, healthFill, healthLow, dropTint, grime;

		internal GarageUi(Garage owner)
		{
			garage = owner;
		}

		/// <summary>The garage has just opened: what is on the car, and what is offered for each slot (the room may have changed).</summary>
		internal void Opened()
		{
			ReadCar();
			offered.Clear();
			List<UpgradeComponent> catalog = GarageRefit.Catalog();
			foreach (var group in catalog.Where(Garage.Offered).GroupBy(u => u.id).Select(g => g.First()).GroupBy(u => u.upgradeCategory))
			{
				offered[group.Key] = group.OrderByDescending(Tier).ThenBy(u => Name(u), StringComparer.OrdinalIgnoreCase).ToList();
			}
			int gun = slots.FindIndex(s => s.Category == UpgradeCategory.GUN);
			selected = gun >= 0 ? gun : 0;
			scroll = Vector2.zero;
			hovered = null;
			pinned = null;
			message = null;
			EndPress();
			Mod.Info("Garage offers " + offered.Values.Sum(l => l.Count) + " items in " + offered.Count + " categories");
		}

		/// <summary>Sorts the car's loadout into the slots, in order; what fits none is kept aside.</summary>
		private void ReadCar()
		{
			slots.Clear();
			extras.Clear();
			foreach (var (category, text, group) in Layout)
			{
				slots.Add(new Slot { Category = category, Label = text, Group = group });
			}
			foreach (UpgradeComponent u in PlayerLoadout.Equipped)
			{
				Slot free = slots.FirstOrDefault(s => s.Category == u.upgradeCategory && s.Item == null);
				if (free != null)
				{
					free.Item = u;
				}
				else
				{
					extras.Add(u);
				}
			}
		}

		/// <summary>
		/// Puts <paramref name="item"/> (or nothing) in slot <paramref name="index"/> and refits the car. If the item is already in another
		/// slot (the two medium slots) it moves, and with <paramref name="swap"/> what was in this slot goes into the one it left.
		/// </summary>
		private void Place(int index, UpgradeComponent item, bool swap = false)
		{
			Slot slot = slots[index];
			if (slot.Item == item)
			{
				return;
			}
			UpgradeComponent before = slot.Item;
			Slot other = item == null ? null : slots.FirstOrDefault(s => s != slot && s.Item != null && s.Item.id == item.id);
			if (other != null)
			{
				other.Item = swap ? before : null;
			}
			slot.Item = item;
			List<UpgradeComponent> wanted = slots.Where(s => s.Item != null).Select(s => s.Item).Concat(extras).ToList();
			if (garage.Fit(wanted))
			{
				Say(item != null ? Name(item) + " fitted" : Name(before) + " taken off");
			}
			else
			{
				slot.Item = before;
				if (other != null)
				{
					other.Item = item;
				}
				Say("That could not be fitted here (see the log)");
			}
		}

		/// <summary>
		/// A right click on an item in the list: off if it is fitted; else into the chosen slot, or into an empty slot of its kind if the
		/// chosen one is full and there is one (the second medium slot).
		/// </summary>
		private void Toggle(UpgradeComponent item)
		{
			int fitted = slots.FindIndex(s => s.Item == item);
			if (fitted >= 0)
			{
				Place(fitted, null);
				return;
			}
			int index = selected;
			if (slots[index].Item != null)
			{
				int empty = slots.FindIndex(s => s.Category == item.upgradeCategory && s.Item == null);
				if (empty >= 0)
				{
					index = empty;
				}
			}
			if (slots[index].Category == item.upgradeCategory)
			{
				Place(index, item);
			}
		}

		/// <summary>The left button came up: a drag drops its item, a click on a list item pins its description.</summary>
		private void Release()
		{
			if (pressed == null)
			{
				return;
			}
			if (!dragging)
			{
				if (pressedFrom < 0)
				{
					pinned = pressed;
				}
				EndPress();
				return;
			}
			int onto = slots.FindIndex(s => s.Rect.Contains(mouse));
			UpgradeComponent item = pressed;
			int from = pressedFrom;
			EndPress();
			if (onto >= 0)
			{
				if (slots[onto].Category != item.upgradeCategory)
				{
					Say(Name(item) + " does not go in the " + slots[onto].Label.ToLowerInvariant() + " slot");
					return;
				}
				selected = onto;
				Place(onto, item, from >= 0);
			}
			else if (from >= 0)
			{
				// Dragged off its slot, anywhere but another slot: taken off, as in the game's garage.
				Place(from, null);
			}
		}

		private void EndPress()
		{
			pressed = null;
			pressedFrom = -1;
			dragging = false;
		}

		private void Say(string words)
		{
			message = words;
			messageUntil = Time.realtimeSinceStartup + 3f;
		}

		// ---- drawing ----------------------------------------------------------------------------------------------

		internal void Draw()
		{
			if (Event.current == null)
			{
				return;
			}
			Style();
			float scale = Mathf.Clamp(Screen.height / 1080f, 0.5f, 3f);
			Matrix4x4 before = GUI.matrix;
			GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
			float w = Screen.width / scale, h = Screen.height / scale;
			try
			{
				if (garage.OpenAt != null)
				{
					Window(w, h);
				}
				else
				{
					EndPress();
					Labels(scale);
					Prompt(w, h);
				}
			}
			catch (Exception e)
			{
				Mod.Log.LogWarning("Garage: the window could not be drawn: " + e);
				EndPress();
				garage.Close();
			}
			finally
			{
				GUI.matrix = before;
			}
		}

		/// <summary>"GARAGE" over each circle within reach in front of the camera, so they can be found from the road.</summary>
		private void Labels(float scale)
		{
			Camera cam = Camera.main;
			if (cam == null || Event.current.type != EventType.Repaint)
			{
				return;
			}
			foreach (GarageSites.Site site in GarageSites.All)
			{
				Vector3 p = Garage.ScenePoint(site) + Vector3.up * 6f;
				float distance = Vector3.Distance(cam.transform.position, p);
				if (distance > 220f)
				{
					continue;
				}
				Vector3 s = cam.WorldToScreenPoint(p);
				if (s.z <= 0f)
				{
					continue;
				}
				float x = s.x / scale, y = (Screen.height - s.y) / scale;
				Color c = GUI.color;
				GUI.color = new Color(1f, 1f, 1f, Mathf.Clamp01((220f - distance) / 60f));
				Shadowed(new Rect(x - 80f, y - 14f, 160f, 28f), "GARAGE", label);
				GUI.color = c;
			}
		}

		/// <summary>While the car is in a circle: what to do to open the garage.</summary>
		private void Prompt(float w, float h)
		{
			GarageSites.Site here = garage.Here;
			if (here == null)
			{
				return;
			}
			string words = garage.Armed
				? "<color=#e23a3a>GARAGE</color>  " + here.Name + "\nStop and shift into neutral to open it"
				: "<color=#e23a3a>GARAGE</color>  " + here.Name + "\nShift into gear or drive out of the circle to open it again";
			Rect r = new Rect(Mathf.Round(w * 0.5f - 330f), Mathf.Round(h * 0.56f), 660f, 78f);
			Box(r, panel);
			Shadowed(r, words, prompt);
		}

		private void Window(float w, float h)
		{
			Event e = Event.current;
			mouse = e.mousePosition;
			if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.Escape || e.keyCode == KeyCode.Return))
			{
				e.Use();
				EndPress();
				garage.Close();
				return;
			}
			if (pressed != null && e.rawType == EventType.MouseDrag && e.button == 0 && (mouse - pressedAt).sqrMagnitude > DragStart * DragStart)
			{
				dragging = true;
			}
			Rect win = new Rect(Mathf.Round((w - WindowW) * 0.5f), Mathf.Round((h - WindowH) * 0.5f), WindowW, WindowH);
			GUI.DrawTexture(new Rect(0f, 0f, w, h), dark);
			Box(win, panel);

			// Title and what holds while it is open.
			Shadowed(new Rect(win.x + 24f, win.y + 14f, 800f, 40f), "<color=#e23a3a>GARAGE</color>  " + garage.OpenAt.Name, title);
			if (Button(new Rect(win.xMax - 168f, win.y + 16f, 144f, 38f), "Close  [Esc]"))
			{
				EndPress();
				garage.Close();
				return;
			}
			string rules = "Drag an item onto a slot to fit it, or off its slot to take it off. Right-click an item to fit or take it off. Nothing can hurt the car while the garage is open; changes last for this race only.";
			if (MultiplayerLink.InRoom)
			{
				rules += " Items banned in Multiplayer rooms are not offered.";
			}
			GUI.Label(new Rect(win.x + 24f, win.y + 56f, WindowW - 48f, 40f), rules, faint);

			UpgradeComponent over = null;
			Slots(new Rect(win.x + 20f, win.y + 100f, 380f, WindowH - 198f), ref over);
			listArea = new Rect(win.x + 420f, win.y + 100f, WindowW - 440f, WindowH - 198f);
			Items(listArea, ref over);
			if (e.type == EventType.Repaint)
			{
				hovered = over;
			}
			Bottom(new Rect(win.x + 20f, win.yMax - 84f, WindowW - 40f, 64f));

			if (e.rawType == EventType.MouseUp && e.button == 0)
			{
				Release();
				e.Use();
			}
			Dragged();
		}

		/// <summary>The slots under their group headings, each with what is in it; while an item is dragged, the slots it fits are lit.</summary>
		private void Slots(Rect area, ref UpgradeComponent over)
		{
			Event e = Event.current;
			float y = area.y;
			string group = null;
			for (int i = 0; i < slots.Count; i++)
			{
				Slot s = slots[i];
				if (s.Group != group)
				{
					group = s.Group;
					Shadowed(new Rect(area.x + 2f, y, area.width, 24f), group.ToUpperInvariant(), heading);
					y += 26f;
				}
				Rect r = new Rect(area.x, y, area.width, 46f);
				s.Rect = r;
				bool fits = dragging && pressed.upgradeCategory == s.Category;
				bool under = r.Contains(mouse);
				Box(r, fits ? slotDrop : i == selected ? slotOn : slotOff);
				if (fits && under && e.type == EventType.Repaint)
				{
					GUI.DrawTexture(new Rect(r.x + Px * 2, r.y + Px * 2, r.width - Px * 4, r.height - Px * 4), dropTint);
				}
				if (under && s.Item != null && !dragging)
				{
					over = s.Item;
				}
				if (under && e.type == EventType.MouseDown)
				{
					if (e.button == 0)
					{
						if (selected != i)
						{
							selected = i;
							scroll = Vector2.zero;
						}
						if (s.Item != null)
						{
							pressed = s.Item;
							pressedFrom = i;
							pressedAt = mouse;
						}
						e.Use();
					}
					else if (e.button == 1)
					{
						selected = i;
						if (s.Item != null)
						{
							Place(i, null);
						}
						e.Use();
					}
				}
				bool lifted = dragging && pressedFrom == i;
				Box(new Rect(r.x + Px * 2, r.y + Px * 2, 42f - Px, 42f - Px), iconBox);
				if (!lifted)
				{
					Icon(new Rect(r.x + 8f, r.y + 8f, 30f, 30f), s.Item);
				}
				Shadowed(new Rect(r.x + 54f, r.y + 3f, r.width - 58f, 18f), s.Label, small);
				Shadowed(new Rect(r.x + 54f, r.y + 20f, r.width - 58f, 24f), s.Item != null && !lifted ? Titled(s.Item) : "<color=#b4b5ba>Empty</color>", text);
				y += 49f;
			}
		}

		/// <summary>What fits the chosen slot, best tier first; the pointed-at (or pinned, or fitted) item's description underneath.</summary>
		private void Items(Rect area, ref UpgradeComponent over)
		{
			Event e = Event.current;
			Slot slot = slots[selected];
			List<UpgradeComponent> items = offered.TryGetValue(slot.Category, out List<UpgradeComponent> list) ? list : new List<UpgradeComponent>();
			Shadowed(new Rect(area.x + 2f, area.y, area.width - 170f, 26f), slot.Label.ToUpperInvariant() + "  <color=#a4a5aa>" + items.Count + " to choose from</color>", heading);
			if (slot.Item != null && Button(new Rect(area.xMax - 160f, area.y - 4f, 160f, 32f), "Take off"))
			{
				Place(selected, null);
			}

			const float detailH = 150f;
			Rect view = new Rect(area.x, area.y + 34f, area.width, area.height - 34f - detailH - 8f);
			Box(view, inset);
			Rect inside = new Rect(view.x + Px * 2, view.y + Px * 2, view.width - Px * 4, view.height - Px * 4);
			const float cardH = 54f;
			Rect content = new Rect(0f, 0f, inside.width - 20f, Math.Max(inside.height, items.Count * (cardH + 4f)));
			bool inView = inside.Contains(mouse);
			scroll = GUI.BeginScrollView(inside, scroll, content);
			Vector2 local = e.mousePosition;
			for (int i = 0; i < items.Count; i++)
			{
				UpgradeComponent u = items[i];
				Rect r = new Rect(0f, i * (cardH + 4f), content.width, cardH);
				if (r.yMax < scroll.y || r.y > scroll.y + inside.height)
				{
					continue;
				}
				bool fitted = slot.Item == u;
				bool elsewhere = !fitted && slots.Any(s => s != slot && s.Item == u);
				bool under = inView && r.Contains(local);
				Box(r, fitted ? cardOn : under && !dragging ? cardHover : cardOff);
				if (under && !dragging)
				{
					over = u;
				}
				if (under && e.type == EventType.MouseDown)
				{
					if (e.button == 0)
					{
						pressed = u;
						pressedFrom = -1;
						pressedAt = mouse;
						e.Use();
					}
					else if (e.button == 1)
					{
						Toggle(u);
						e.Use();
					}
				}
				Box(new Rect(r.x + Px * 2, r.y + Px * 2, cardH - Px * 4, cardH - Px * 4), iconBox);
				Icon(new Rect(r.x + 10f, r.y + 10f, cardH - 20f, cardH - 20f), u);
				Shadowed(new Rect(r.x + 60f, r.y + 5f, r.width - 220f, 26f), Titled(u), text);
				Shadowed(new Rect(r.x + 60f, r.y + 29f, r.width - 220f, 20f), TierName(u) + (Unlocked(u) ? "" : "  <color=#ff7a6e>not unlocked yet</color>"), small);
				string tag = fitted ? "<color=#9ccc5a>FITTED</color>" : elsewhere ? "<color=#9ccc5a>FITTED</color> <color=#a4a5aa>(other slot)</color>" : "";
				Shadowed(new Rect(r.xMax - 180f, r.y, 168f, cardH), tag, smallRight);
			}
			GUI.EndScrollView();
			if (dragging && pressedFrom >= 0 && view.Contains(mouse) && e.type == EventType.Repaint)
			{
				GUI.DrawTexture(inside, dropTint);
				GUI.Label(inside, "Drop here to take it off", dropLabel);
			}

			Rect d = new Rect(area.x, area.yMax - detailH, area.width, detailH);
			Box(d, inset);
			UpgradeComponent shown = (dragging ? pressed : null) ?? hovered ?? pinned ?? slot.Item;
			if (shown != null)
			{
				Box(new Rect(d.x + 10f, d.y + 10f, 76f, 76f), iconBox);
				Icon(new Rect(d.x + 16f, d.y + 16f, 64f, 64f), shown);
				Shadowed(new Rect(d.x + 98f, d.y + 8f, d.width - 108f, 28f), Titled(shown), detailName);
				GUI.Label(new Rect(d.x + 98f, d.y + 38f, d.width - 108f, d.height - 44f), Describe(shown), detail);
			}
			else
			{
				GUI.Label(new Rect(d.x + 14f, d.y + 12f, d.width - 28f, d.height - 24f), "Nothing fitted here. Point at an item to read what it does; drag it onto a slot or right-click it to fit it.", faint);
			}
		}

		/// <summary>Health and Repair, Refill, and the last thing done.</summary>
		private void Bottom(Rect area)
		{
			CarLink link = garage.Link;
			float hp = link != null ? link.hp : 0f, max = link != null ? Mathf.Max(1f, link.MaxHp) : 1f;
			GUI.Label(new Rect(area.x, area.y - 2f, 300f, 22f), "Health  " + Mathf.CeilToInt(hp) + " / " + Mathf.CeilToInt(max) + "  <color=#a4a5aa>(held while open)</color>", small);
			HealthBar(new Rect(area.x, area.y + 20f, 300f, 26f), Mathf.Clamp01(hp / max));
			bool full = hp >= max - 0.5f;
			GUI.enabled = !full;
			if (Button(new Rect(area.x + 316f, area.y + 12f, 190f, 42f), full ? "Health is full" : "Repair to full"))
			{
				garage.Repair();
				Say("Repaired");
			}
			GUI.enabled = true;
			if (Button(new Rect(area.x + 520f, area.y + 12f, 230f, 42f), "Refill ammo and nitro"))
			{
				garage.Refill();
				Say("Ammo, nitro and charges filled");
			}
			string right = message != null && Time.realtimeSinceStartup < messageUntil ? "<color=#e8e8e8>" + message + "</color>" : "<color=#a4a5aa>To leave: Close, then shift into gear and drive off.</color>";
			GUI.Label(new Rect(area.x + 770f, area.y + 14f, area.width - 770f, 38f), right, smallRight);
		}

		/// <summary>Health as a row of pixel blocks, as the game's bars are, red when low.</summary>
		private void HealthBar(Rect r, float part)
		{
			Box(r, barBack);
			if (Event.current.type != EventType.Repaint)
			{
				return;
			}
			const int blocks = 20;
			float inner = r.width - Px * 4, step = inner / blocks;
			int lit = Mathf.CeilToInt(part * blocks - 0.001f);
			for (int k = 0; k < lit; k++)
			{
				GUI.DrawTexture(new Rect(Mathf.Round(r.x + Px * 2 + k * step), r.y + Px * 2, Mathf.Round(step) - Px, r.height - Px * 4), part < 0.3f ? healthLow : healthFill);
			}
		}

		/// <summary>The dragged item's picture under the mouse.</summary>
		private void Dragged()
		{
			if (!dragging || pressed == null || Event.current.type != EventType.Repaint)
			{
				return;
			}
			Rect r = new Rect(mouse.x - 28f, mouse.y - 28f, 56f, 56f);
			Box(r, iconBox);
			Icon(new Rect(r.x + 8f, r.y + 8f, 40f, 40f), pressed);
		}

		/// <summary>A pixel panel (a style with only a background) drawn on Repaint, with the grime over it.</summary>
		private void Box(Rect r, GUIStyle style)
		{
			if (Event.current.type == EventType.Repaint)
			{
				style.Draw(r, false, false, false, false);
				Grime(r);
			}
		}

		/// <summary>A button in the pixel look, grimed like the panels.</summary>
		private bool Button(Rect r, string words)
		{
			bool clicked = GUI.Button(r, words, button);
			if (Event.current.type == EventType.Repaint)
			{
				Grime(r);
			}
			return clicked;
		}

		/// <summary>
		/// Dirt over a box's face inside its outline: <see cref="grime"/> tiled at the look's pixel size and pinned to the screen, so
		/// boxes side by side do not repeat the same marks.
		/// </summary>
		private void Grime(Rect r)
		{
			Rect inside = new Rect(r.x + Px, r.y + Px, r.width - Px * 2, r.height - Px * 2);
			if (inside.width <= 0f || inside.height <= 0f)
			{
				return;
			}
			float tile = GrimeSize * Px;
			GUI.DrawTextureWithTexCoords(inside, grime, new Rect(inside.x / tile, -inside.yMax / tile, inside.width / tile, inside.height / tile));
		}

		/// <summary>The colour tags taken out, for a text's drop shadow.</summary>
		private static readonly Regex Colours = new Regex(@"</?color[^>]*>", RegexOptions.Compiled);

		/// <summary>Text with a hard one-pixel drop shadow under it, as the game's pixel lettering has.</summary>
		private void Shadowed(Rect r, string words, GUIStyle style)
		{
			if (Event.current.type != EventType.Repaint)
			{
				return;
			}
			Color c = GUI.color;
			GUI.color = new Color(0f, 0f, 0f, c.a * 0.85f);
			GUI.Label(new Rect(r.x + Px * 0.67f, r.y + Px * 0.67f, r.width, r.height), "<color=#000000>" + Colours.Replace(words, "") + "</color>", style);
			GUI.color = c;
			GUI.Label(r, words, style);
		}

		// ---- items -------------------------------------------------------------------------------------------------

		private static ItemTier Tier(UpgradeComponent u)
		{
			try
			{
				return ItemTiers.Of(u);
			}
			catch (Exception)
			{
				return u.tier;
			}
		}

		private static string Name(UpgradeComponent u)
		{
			try
			{
				return u.Name;
			}
			catch (Exception)
			{
				return u.name;
			}
		}

		/// <summary>The item's name in its tier's colour.</summary>
		private static string Titled(UpgradeComponent u)
		{
			Color c = Color.white;
			try
			{
				c = ItemTiers.Color(Tier(u));
			}
			catch (Exception)
			{
			}
			return "<color=#" + ColorUtility.ToHtmlStringRGB(c) + ">" + Escape(Name(u)) + "</color>";
		}

		private static string TierName(UpgradeComponent u)
		{
			try
			{
				return ItemTiers.Name(Tier(u));
			}
			catch (Exception)
			{
				return Tier(u).ToString();
			}
		}

		private static bool Unlocked(UpgradeComponent u)
		{
			try
			{
				return ItemUnlocks.IsUnlocked(u);
			}
			catch (Exception)
			{
				return true;
			}
		}

		/// <summary>The rich-text tags this GUI draws; the game's text has others (TextMeshPro's), which are taken out.</summary>
		private static readonly Regex Tags = new Regex(@"<(?!/?(b|i|color|size)\b)[^>]*>", RegexOptions.Compiled);

		/// <summary>The item's description as the game writes it (its stat lines included), without its first line, the name.</summary>
		private static string Describe(UpgradeComponent u)
		{
			string words;
			try
			{
				words = u.Description;
			}
			catch (Exception)
			{
				words = u.description;
			}
			words = Tags.Replace(words ?? "", "");
			int line = words.IndexOf('\n');
			return line >= 0 ? words.Substring(line + 1).Trim() : "";
		}

		private static string Escape(string s)
		{
			return Tags.Replace(s ?? "", "");
		}

		/// <summary>An item's picture fitted into the box, its shape kept; an empty slot's box stays dark.</summary>
		private void Icon(Rect box, UpgradeComponent u)
		{
			if (Event.current.type != EventType.Repaint)
			{
				return;
			}
			Sprite sprite = u != null ? u.image : null;
			if (sprite == null || sprite.texture == null)
			{
				return;
			}
			Rect px;
			try
			{
				px = sprite.textureRect;
			}
			catch (Exception)
			{
				px = sprite.rect;
			}
			Texture2D tex = sprite.texture;
			float k = Mathf.Min(box.width / px.width, box.height / px.height);
			Rect r = new Rect(box.x + (box.width - px.width * k) * 0.5f, box.y + (box.height - px.height * k) * 0.5f, px.width * k, px.height * k);
			GUI.DrawTextureWithTexCoords(r, tex, new Rect(px.x / tex.width, px.y / tex.height, px.width / tex.width, px.height / tex.height));
		}

		// ---- style -------------------------------------------------------------------------------------------------

		private static Texture2D Solid(Color c)
		{
			Texture2D t = new Texture2D(1, 1, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point };
			t.SetPixel(0, 0, c);
			t.Apply();
			return t;
		}

		/// <summary>
		/// A pixel-art box, 8 of its pixels square, each <see cref="Px"/> texels: a near-black outline with its corners cut, a light edge
		/// top and left and a dark one bottom and right (swapped when <paramref name="sunk"/>), the fill inside. Drawn as a nine-slice with a
		/// border of 3 pixels, so the edges stay the same chunky size whatever the box's size.
		/// </summary>
		private static Texture2D Bevel(Color fill, Color light, Color shade, bool sunk = false)
		{
			const int n = 8;
			Color outline = new Color(0.03f, 0.03f, 0.035f, 1f), clear = new Color(0f, 0f, 0f, 0f);
			Color top = sunk ? shade : light, bottom = sunk ? light : shade;
			Texture2D t = new Texture2D(n * Px, n * Px, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
			Color[] pixels = new Color[n * n * Px * Px];
			for (int y = 0; y < n; y++)
			{
				for (int x = 0; x < n; x++)
				{
					// y counts up from the bottom in a texture.
					bool edgeX = x == 0 || x == n - 1, edgeY = y == 0 || y == n - 1;
					Color c;
					if (edgeX && edgeY)
					{
						c = clear;
					}
					else if (edgeX || edgeY)
					{
						c = outline;
					}
					else if (y == n - 2 || x == 1)
					{
						c = y == 1 || x == n - 2 ? fill : top;
					}
					else if (y == 1 || x == n - 2)
					{
						c = bottom;
					}
					else
					{
						c = fill;
					}
					for (int j = 0; j < Px; j++)
					{
						for (int i = 0; i < Px; i++)
						{
							pixels[(y * Px + j) * n * Px + x * Px + i] = c;
						}
					}
				}
			}
			t.SetPixels(pixels);
			t.Apply();
			return t;
		}

		/// <summary>Pixels along a side of the grime texture before it repeats.</summary>
		private const int GrimeSize = 64;

		/// <summary>
		/// The grime laid over every box: dark specks and blotches, the vanilla garage's worn metal, with the odd scratch of light, each texel one of the look's
		/// pixels, repeating. Only looks: an integer hash, nothing from the world's randomness.
		/// </summary>
		private static Texture2D Dirt()
		{
			const int n = GrimeSize;
			Texture2D t = new Texture2D(n, n, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat };
			Color[] pixels = new Color[n * n];
			uint Hash(int x, int y, uint salt)
			{
				uint h = (uint)(x & (n - 1)) * 374761393u + (uint)(y & (n - 1)) * 668265263u + salt * 2246822519u;
				h = (h ^ (h >> 13)) * 1274126177u;
				return h ^ (h >> 16);
			}
			for (int y = 0; y < n; y++)
			{
				for (int x = 0; x < n; x++)
				{
					// Blotches: an 8-pixel lattice, each pixel taking the darkest of its cell and the next, so they run into each other.
					float blot = Math.Max(Hash(x / 8, y / 8, 1u) % 100u, Hash((x + 4) / 8, (y + 3) / 8, 2u) % 100u) / 100f;
					float a = blot > 0.75f ? 0.16f : blot > 0.6f ? 0.06f : 0f;
					uint speck = Hash(x, y, 3u) % 1000u;
					Color c = new Color(0f, 0f, 0f, a);
					if (speck < 90u)
					{
						c = new Color(0f, 0f, 0f, 0.22f);
					}
					else if (speck < 105u)
					{
						c = new Color(0.85f, 0.86f, 0.88f, 0.07f);
					}
					// A short scratch now and then, three pixels along.
					if (Hash(x, y, 4u) % 400u == 0u || Hash(x - 1, y, 4u) % 400u == 0u || Hash(x - 2, y, 4u) % 400u == 0u)
					{
						c = new Color(0.85f, 0.86f, 0.88f, 0.08f);
					}
					pixels[y * n + x] = c;
				}
			}
			t.SetPixels(pixels);
			t.Apply();
			return t;
		}

		/// <summary>A style that only draws <paramref name="background"/>, nine-sliced on its pixel edges.</summary>
		private static GUIStyle Panel(Texture2D background)
		{
			GUIStyle s = new GUIStyle { border = new RectOffset(Px * 3, Px * 3, Px * 3, Px * 3) };
			s.normal.background = background;
			return s;
		}

		/// <summary>The game's pixel font if it can be found (the Multiplayer mod's UI uses it too), else Unity's.</summary>
		private static Font PixelFont()
		{
			try
			{
				return Resources.FindObjectsOfTypeAll<Font>().FirstOrDefault(f => f != null && f.name.StartsWith("m6x11", StringComparison.OrdinalIgnoreCase));
			}
			catch (Exception)
			{
				return null;
			}
		}

		private void Style()
		{
			if (styled && dark != null)
			{
				return;
			}
			styled = true;
			// The vanilla garage's colours: worn mid-grey metal tiles with a near-black outline on a dark slate frame, black wells, white
			// lettering; its red (the map button, the prices) for what is chosen, its unlock bar's green for what is fitted.
			Color metal = new Color(0.25f, 0.255f, 0.27f, 1f), metalLight = new Color(0.42f, 0.425f, 0.44f, 1f), metalShade = new Color(0.17f, 0.17f, 0.185f, 1f);
			Color slate = new Color(0.12f, 0.125f, 0.145f, 0.97f), slateLight = new Color(0.27f, 0.275f, 0.30f, 1f), slateShade = new Color(0.06f, 0.06f, 0.07f, 1f);
			Color red = new Color(0.66f, 0.10f, 0.10f, 1f), redLight = new Color(0.85f, 0.22f, 0.20f, 1f), redShade = new Color(0.36f, 0.05f, 0.05f, 1f);
			Color green = new Color(0.25f, 0.38f, 0.17f, 1f), greenLight = new Color(0.56f, 0.72f, 0.32f, 1f), greenShade = new Color(0.13f, 0.20f, 0.08f, 1f);
			Color well = new Color(0.035f, 0.035f, 0.04f, 1f);
			Color white = new Color(0.94f, 0.94f, 0.95f, 1f);
			panel = Panel(Bevel(slate, slateLight, slateShade));
			inset = Panel(Bevel(new Color(0.08f, 0.08f, 0.095f, 1f), slateLight, slateShade, true));
			slotOff = Panel(Bevel(metal, metalLight, metalShade));
			slotOn = Panel(Bevel(red, redLight, redShade));
			slotDrop = Panel(Bevel(green, greenLight, greenShade));
			cardOff = Panel(Bevel(new Color(0.21f, 0.215f, 0.23f, 1f), new Color(0.36f, 0.365f, 0.38f, 1f), metalShade));
			cardHover = Panel(Bevel(new Color(0.30f, 0.305f, 0.32f, 1f), new Color(0.50f, 0.505f, 0.52f, 1f), metalShade));
			cardOn = Panel(Bevel(green, greenLight, greenShade));
			iconBox = Panel(Bevel(well, slateShade, slateLight, false));
			barBack = Panel(Bevel(well, slateLight, slateShade, true));
			dark = Solid(new Color(0f, 0f, 0f, 0.35f));
			healthFill = Solid(new Color(0.56f, 0.72f, 0.32f, 1f));
			healthLow = Solid(new Color(0.85f, 0.16f, 0.14f, 1f));
			dropTint = Solid(new Color(0.56f, 0.72f, 0.32f, 0.14f));
			grime = Dirt();
			Font font = PixelFont();

			GUIStyle Make(int size, Color colour, TextAnchor anchor = TextAnchor.UpperLeft, bool wrap = false)
			{
				GUIStyle s = new GUIStyle(GUI.skin.label) { fontSize = size, alignment = anchor, wordWrap = wrap, richText = true, clipping = TextClipping.Clip };
				s.normal.textColor = colour;
				if (font != null)
				{
					s.font = font;
				}
				return s;
			}
			title = Make(36, white, TextAnchor.MiddleLeft);
			text = Make(18, white, TextAnchor.MiddleLeft);
			// Light enough to read on the grey metal (it is drawn with a black shadow there).
			small = Make(18, new Color(0.84f, 0.85f, 0.87f), TextAnchor.MiddleLeft);
			smallRight = Make(18, new Color(0.84f, 0.85f, 0.87f), TextAnchor.MiddleRight);
			faint = Make(18, new Color(0.56f, 0.57f, 0.60f), TextAnchor.UpperLeft, true);
			heading = Make(18, redLight, TextAnchor.MiddleLeft);
			label = Make(18, new Color(0.55f, 0.92f, 1f), TextAnchor.MiddleCenter);
			prompt = Make(18, white, TextAnchor.MiddleCenter, true);
			detailName = Make(27, white, TextAnchor.MiddleLeft);
			detail = Make(18, new Color(0.84f, 0.85f, 0.87f), TextAnchor.UpperLeft, true);
			dropLabel = Make(27, greenLight, TextAnchor.MiddleCenter);

			// Buttons: a raised pixel box, lighter when pointed at, sunk when pressed, the text pushed down a pixel with it.
			button = new GUIStyle { fontSize = 18, richText = true, alignment = TextAnchor.MiddleCenter, border = new RectOffset(Px * 3, Px * 3, Px * 3, Px * 3), padding = new RectOffset(Px * 3, Px * 3, Px * 2, Px * 3) };
			if (font != null)
			{
				button.font = font;
			}
			button.normal.background = Bevel(metal, metalLight, metalShade);
			button.hover.background = Bevel(red, redLight, redShade);
			button.active.background = Bevel(redShade, redLight, red, true);
			button.normal.textColor = white;
			button.hover.textColor = white;
			button.active.textColor = new Color(1f, 0.80f, 0.78f, 1f);
			button.contentOffset = Vector2.zero;
		}
	}
}
