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
	/// with Unity's immediate-mode GUI, as the debug overlay is, at a size that follows the screen's height.
	///
	/// The window: the car's slots down the left, grouped and in the order of their categories (the car, the cockpit, extras), each with
	/// what is fitted; the items that fit the chosen slot on the right, best tier first, with the hovered one's description under them;
	/// health with Repair, Refill for ammo and nitro, and Close along the bottom.
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
		private const float WindowH = 740f;

		private readonly Garage garage;
		private readonly List<Slot> slots = new List<Slot>();
		/// <summary>Items on the car that no slot here takes: carried through every refit untouched.</summary>
		private readonly List<UpgradeComponent> extras = new List<UpgradeComponent>();
		private readonly Dictionary<UpgradeCategory, List<UpgradeComponent>> offered = new Dictionary<UpgradeCategory, List<UpgradeComponent>>();
		private int selected;
		private Vector2 scroll;
		private UpgradeComponent hovered;
		private string message;
		private float messageUntil;

		private bool styled;
		private GUIStyle title, text, small, smallRight, faint, heading, button, slotButton, card, prompt, label, detailName, detail;
		private Texture2D panel, dark, slotOn, slotOff, cardOn, cardOff, healthBack, healthFill, white;

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
			message = null;
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

		/// <summary>Puts <paramref name="item"/> (or nothing) in the chosen slot and refits the car; a medium item already in the other slot moves.</summary>
		private void Choose(UpgradeComponent item)
		{
			Slot slot = slots[selected];
			if (slot.Item == item)
			{
				return;
			}
			UpgradeComponent before = slot.Item;
			Slot other = item == null ? null : slots.FirstOrDefault(s => s != slot && s.Item != null && s.Item.id == item.id);
			if (other != null)
			{
				other.Item = null;
			}
			slot.Item = item;
			List<UpgradeComponent> wanted = slots.Where(s => s.Item != null).Select(s => s.Item).Concat(extras).ToList();
			if (garage.Fit(wanted))
			{
				Say(item != null ? Name(item) + " fitted" : slot.Label + " left empty");
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
					Labels(scale);
					Prompt(w, h);
				}
			}
			catch (Exception e)
			{
				Mod.Log.LogWarning("Garage: the window could not be drawn: " + e);
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
				GUI.Label(new Rect(x - 80f, y - 14f, 160f, 28f), "GARAGE", label);
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
				? "<b>GARAGE</b>  " + here.Name + "\nStop and shift into neutral to open it"
				: "<b>GARAGE</b>  " + here.Name + "\nShift into gear or drive out of the circle to open it again";
			Rect r = new Rect(w * 0.5f - 330f, h * 0.70f, 660f, 74f);
			GUI.DrawTexture(r, panel);
			GUI.Label(r, words, prompt);
		}

		private void Window(float w, float h)
		{
			Event e = Event.current;
			if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.Escape || e.keyCode == KeyCode.Return))
			{
				e.Use();
				garage.Close();
				return;
			}
			Rect win = new Rect(Mathf.Round((w - WindowW) * 0.5f), Mathf.Round((h - WindowH) * 0.5f), WindowW, WindowH);
			GUI.DrawTexture(new Rect(0f, 0f, w, h), dark);
			GUI.DrawTexture(win, panel);

			// Title and what holds while it is open.
			GUI.Label(new Rect(win.x + 24f, win.y + 14f, 800f, 40f), "GARAGE  <color=#7fd8ff>" + garage.OpenAt.Name + "</color>", title);
			if (GUI.Button(new Rect(win.xMax - 164f, win.y + 16f, 140f, 36f), "Close  [Esc]", button))
			{
				garage.Close();
				return;
			}
			string rules = "Pick a slot on the left, then the item to fit in it. Nothing can hurt the car while the garage is open. Changes last for this race only.";
			if (MultiplayerLink.InRoom)
			{
				rules += " Items banned in Multiplayer rooms are not offered.";
			}
			GUI.Label(new Rect(win.x + 24f, win.y + 56f, WindowW - 48f, 24f), rules, faint);

			Slots(new Rect(win.x + 20f, win.y + 92f, 380f, WindowH - 190f));
			Items(new Rect(win.x + 420f, win.y + 92f, WindowW - 440f, WindowH - 190f));
			Bottom(new Rect(win.x + 20f, win.yMax - 84f, WindowW - 40f, 64f));
		}

		/// <summary>The slots under their group headings, each with what is in it.</summary>
		private void Slots(Rect area)
		{
			float y = area.y;
			string group = null;
			for (int i = 0; i < slots.Count; i++)
			{
				Slot s = slots[i];
				if (s.Group != group)
				{
					group = s.Group;
					GUI.Label(new Rect(area.x, y, area.width, 24f), group.ToUpperInvariant(), heading);
					y += 26f;
				}
				Rect r = new Rect(area.x, y, area.width, 46f);
				GUI.DrawTexture(r, i == selected ? slotOn : slotOff);
				if (GUI.Button(r, GUIContent.none, slotButton))
				{
					selected = i;
					scroll = Vector2.zero;
				}
				Icon(new Rect(r.x + 4f, r.y + 3f, 40f, 40f), s.Item);
				GUI.Label(new Rect(r.x + 52f, r.y + 2f, r.width - 56f, 18f), s.Label, small);
				GUI.Label(new Rect(r.x + 52f, r.y + 19f, r.width - 56f, 26f), s.Item != null ? Titled(s.Item) : "<color=#8a8a8a>Empty</color>", text);
				y += 49f;
			}
		}

		/// <summary>What fits the chosen slot, best tier first; the hovered (or fitted) item's description underneath.</summary>
		private void Items(Rect area)
		{
			Slot slot = slots[selected];
			List<UpgradeComponent> items = offered.TryGetValue(slot.Category, out List<UpgradeComponent> list) ? list : new List<UpgradeComponent>();
			GUI.Label(new Rect(area.x, area.y, area.width - 170f, 26f), slot.Label.ToUpperInvariant() + "  <color=#8a8a8a>" + items.Count + " to choose from</color>", heading);
			if (slot.Item != null && GUI.Button(new Rect(area.xMax - 160f, area.y - 2f, 160f, 28f), "Leave empty", button))
			{
				Choose(null);
			}

			const float detailH = 150f;
			Rect view = new Rect(area.x, area.y + 32f, area.width, area.height - 32f - detailH - 8f);
			const float cardH = 54f;
			Rect content = new Rect(0f, 0f, view.width - 20f, Math.Max(view.height, items.Count * (cardH + 4f)));
			UpgradeComponent over = null;
			scroll = GUI.BeginScrollView(view, scroll, content);
			for (int i = 0; i < items.Count; i++)
			{
				UpgradeComponent u = items[i];
				Rect r = new Rect(0f, i * (cardH + 4f), content.width, cardH);
				if (r.yMax < scroll.y || r.y > scroll.y + view.height)
				{
					continue;
				}
				bool fitted = slot.Item == u;
				bool elsewhere = !fitted && slots.Any(s => s != slot && s.Item == u);
				GUI.DrawTexture(r, fitted ? cardOn : cardOff);
				if (r.Contains(Event.current.mousePosition))
				{
					over = u;
				}
				if (GUI.Button(r, GUIContent.none, card))
				{
					Choose(u);
				}
				Icon(new Rect(r.x + 6f, r.y + 5f, 44f, 44f), u);
				GUI.Label(new Rect(r.x + 60f, r.y + 5f, r.width - 220f, 26f), Titled(u), text);
				GUI.Label(new Rect(r.x + 60f, r.y + 29f, r.width - 220f, 20f), TierName(u) + (Unlocked(u) ? "" : "  <color=#b08a50>not unlocked yet</color>"), small);
				string tag = fitted ? "<color=#7fd8ff>FITTED</color>" : elsewhere ? "<color=#8a8a8a>in the other slot</color>" : "";
				GUI.Label(new Rect(r.xMax - 160f, r.y, 150f, cardH), tag, smallRight);
			}
			GUI.EndScrollView();
			if (Event.current.type == EventType.Repaint)
			{
				hovered = over;
			}

			Rect d = new Rect(area.x, area.yMax - detailH, area.width, detailH);
			GUI.DrawTexture(d, slotOff);
			UpgradeComponent shown = hovered ?? slot.Item;
			if (shown != null)
			{
				Icon(new Rect(d.x + 10f, d.y + 10f, 64f, 64f), shown);
				GUI.Label(new Rect(d.x + 86f, d.y + 8f, d.width - 96f, 28f), Titled(shown), detailName);
				GUI.Label(new Rect(d.x + 86f, d.y + 38f, d.width - 96f, d.height - 44f), Describe(shown), detail);
			}
			else
			{
				GUI.Label(new Rect(d.x + 12f, d.y + 10f, d.width - 24f, d.height - 20f), "Nothing fitted here. Point at an item to read what it does, click it to fit it.", faint);
			}
		}

		/// <summary>Health and Repair, Refill, and the last thing done.</summary>
		private void Bottom(Rect area)
		{
			CarLink link = garage.Link;
			float hp = link != null ? link.hp : 0f, max = link != null ? Mathf.Max(1f, link.MaxHp) : 1f;
			Rect bar = new Rect(area.x, area.y + 22f, 300f, 22f);
			GUI.Label(new Rect(area.x, area.y - 2f, 300f, 22f), "Health  " + Mathf.CeilToInt(hp) + " / " + Mathf.CeilToInt(max) + "  <color=#8a8a8a>(held while open)</color>", small);
			GUI.DrawTexture(bar, healthBack);
			GUI.DrawTexture(new Rect(bar.x + 2f, bar.y + 2f, (bar.width - 4f) * Mathf.Clamp01(hp / max), bar.height - 4f), healthFill);
			bool full = hp >= max - 0.5f;
			GUI.enabled = !full;
			if (GUI.Button(new Rect(area.x + 316f, area.y + 14f, 190f, 38f), full ? "Health is full" : "Repair to full", button))
			{
				garage.Repair();
				Say("Repaired");
			}
			GUI.enabled = true;
			if (GUI.Button(new Rect(area.x + 520f, area.y + 14f, 230f, 38f), "Refill ammo and nitro", button))
			{
				garage.Refill();
				Say("Ammo, nitro and charges filled");
			}
			string right = message != null && Time.realtimeSinceStartup < messageUntil ? "<color=#7fd8ff>" + message + "</color>" : "<color=#8a8a8a>To leave: Close, then shift into gear and drive off.</color>";
			GUI.Label(new Rect(area.x + 770f, area.y + 14f, area.width - 770f, 38f), right, smallRight);
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
			if (styled && panel != null)
			{
				return;
			}
			styled = true;
			panel = Solid(new Color(0.07f, 0.08f, 0.10f, 0.94f));
			dark = Solid(new Color(0f, 0f, 0f, 0.35f));
			slotOff = Solid(new Color(0.13f, 0.14f, 0.17f, 1f));
			slotOn = Solid(new Color(0.12f, 0.30f, 0.38f, 1f));
			cardOff = Solid(new Color(0.11f, 0.12f, 0.15f, 1f));
			cardOn = Solid(new Color(0.10f, 0.26f, 0.33f, 1f));
			healthBack = Solid(new Color(0.02f, 0.02f, 0.03f, 1f));
			healthFill = Solid(new Color(0.85f, 0.22f, 0.20f, 1f));
			white = Solid(new Color(1f, 1f, 1f, 0.10f));
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
			title = Make(30, Color.white, TextAnchor.MiddleLeft);
			text = Make(19, Color.white, TextAnchor.MiddleLeft);
			small = Make(15, new Color(0.75f, 0.77f, 0.80f), TextAnchor.MiddleLeft);
			smallRight = Make(15, new Color(0.75f, 0.77f, 0.80f), TextAnchor.MiddleRight);
			faint = Make(16, new Color(0.62f, 0.64f, 0.68f), TextAnchor.UpperLeft, true);
			heading = Make(17, new Color(0.50f, 0.85f, 1f), TextAnchor.MiddleLeft);
			label = Make(20, new Color(0.55f, 0.92f, 1f), TextAnchor.MiddleCenter);
			prompt = Make(21, Color.white, TextAnchor.MiddleCenter, true);
			detailName = Make(22, Color.white, TextAnchor.MiddleLeft);
			detail = Make(17, new Color(0.85f, 0.86f, 0.88f), TextAnchor.UpperLeft, true);

			button = new GUIStyle(GUI.skin.button) { fontSize = 18, richText = true };
			if (font != null)
			{
				button.font = font;
			}
			// The slot and card buttons are invisible: their look is drawn under them, the button only takes the click (and lights on hover).
			slotButton = new GUIStyle();
			slotButton.hover.background = white;
			card = new GUIStyle();
			card.hover.background = white;
		}
	}
}
