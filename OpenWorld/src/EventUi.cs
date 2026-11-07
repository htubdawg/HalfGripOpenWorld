using System;
using System.Collections.Generic;
using UnityEngine;

namespace HalfGripOpenWorld
{
	/// <summary>
	/// The events' own windows, in the garage's look (its styles and pixel boxes, <see cref="GarageUi"/>): the course's panel, opened
	/// by stopping in a start box in neutral (its lobbies with Join, and Create with the Guns and Melee rules), and the lobby strip under
	/// the cockpit's board while in a lobby (the lobby, its state, Start for the Host and Leave), and the open lobbies panel at the top
	/// left (every lobby waiting in the room, minimised to a tab at the screen's edge with the hand).
	///
	/// The panel frees the cursor, as the garage's window does. The strip never takes input from driving: its buttons are pressed with
	/// the hand (pointed at with the mouse, or the pad, and grabbed, as the cockpit's own levers are), read from the game's
	/// <c>Grabber</c>, so they work with the hardware cursor locked.
	/// </summary>
	internal sealed partial class GarageUi
	{
		private const float PanelW = 900f;
		private const float PanelH = 640f;
		private const float StripW = 560f;
		private const float StripH = 92f;

		private const float LobbiesW = 460f;
		private const float LobbiesTab = 64f;
		private const float LobbiesTop = 120f;
		private const float LobbiesLine = 26f;
		private const int LobbiesMost = 8;

		private Grabber hand;
		private int handPressFrame = -1;

		/// <summary>The open lobbies panel minimised, for the rest of the game session (the owner's ask: remembered, not saved).</summary>
		private static bool lobbiesMinimised;
		/// <summary>0 out, 1 tucked into the left edge (negative before the first draw).</summary>
		private float lobbiesTuck = -1f;

		// ---- the panel -------------------------------------------------------------------------------------------

		/// <summary>The course's panel: its lobbies to join, Create with the rules, Close.</summary>
		private void EventWindow(float w, float h)
		{
			EventRace race = EventRace.Instance;
			Courses.Course c = race.PanelAt;
			EventRoom room = race.Room;
			Event e = Event.current;
			if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
			{
				e.Use();
				race.ClosePanel();
				return;
			}
			Rect win = new Rect(Mathf.Round((w - PanelW) * 0.5f), Mathf.Round((h - PanelH) * 0.5f), PanelW, PanelH);
			GUI.DrawTexture(new Rect(0f, 0f, w, h), dark);
			Box(win, panel);
			Shadowed(new Rect(win.x + 24f, win.y + 14f, 640f, 40f), "<color=#ff8c1a>EVENT</color>  " + c.Name, title);
			if (Button(new Rect(win.xMax - 168f, win.y + 16f, 144f, 38f), "Close  [Esc]"))
			{
				race.ClosePanel();
				return;
			}
			GUI.Label(new Rect(win.x + 24f, win.y + 58f, PanelW - 48f, 26f), EventRace.Facts(c), small);

			// The course's lobbies.
			bool alone = !EventLink.InRoom;
			// Until the room's host has been heard its clock is not known, so nothing can be created, joined or started yet.
			bool linked = race.Linked;
			Shadowed(new Rect(win.x + 24f, win.y + 96f, 400f, 24f), "LOBBIES", heading);
			Rect list = new Rect(win.x + 20f, win.y + 124f, PanelW - 40f, 260f);
			Box(list, inset);
			List<EventRoom.Lobby> here = new List<EventRoom.Lobby>();
			foreach (EventRoom.Lobby l in room.Lobbies)
			{
				if (l.Course == race.PanelIndex)
				{
					here.Add(l);
				}
			}
			if (here.Count == 0)
			{
				string none = !linked ? "Linking to the host..." : alone ? "Outside a Multiplayer room the event is a solo run: press Start below." : "No lobby here yet. Create one below; the others in the room are told.";
				GUI.Label(new Rect(list.x + 16f, list.y + 12f, list.width - 32f, 30f), none, small);
			}
			EventRoom.Lobby racing = room.Taking(race.PanelIndex);
			float y = list.y + 8f;
			foreach (EventRoom.Lobby l in here)
			{
				if (y + 50f > list.yMax)
				{
					break;
				}
				Rect row = new Rect(list.x + 8f, y, list.width - 16f, 48f);
				Box(row, l == racing ? slotOn : slotOff);
				string state = l.State == EventRoom.LobbyState.Racing ? "Racing" : l.State == EventRoom.LobbyState.Results ? "Results" : racing != null ? "Waiting (race on)" : "Waiting";
				Shadowed(new Rect(row.x + 14f, row.y, 300f, row.height), EventLink.Name(l.Host) + "'s lobby", text);
				GUI.Label(new Rect(row.x + 320f, row.y, 90f, row.height), l.Count + " / " + c.MaxEntrants, small);
				GUI.Label(new Rect(row.x + 410f, row.y, 200f, row.height), "Guns " + (l.Guns ? "on" : "off") + "  Melee " + (l.Melee ? "on" : "off"), small);
				GUI.Label(new Rect(row.x + 610f, row.y, 130f, row.height), state, small);
				bool open = l.State != EventRoom.LobbyState.Racing && l.Count < c.MaxEntrants && linked;
				string join = l.State == EventRoom.LobbyState.Racing ? "Racing" : open ? "Join" : "Full";
				if (Button(new Rect(row.xMax - 104f, row.y + 6f, 96f, 36f), join) && open)
				{
					race.Join(l.Id);
					return;
				}
				y += 54f;
			}

			// Create.
			float cy = list.yMax + 18f;
			Shadowed(new Rect(win.x + 24f, cy, 400f, 24f), alone ? "START A SOLO RUN" : "CREATE A LOBBY", heading);
			GUI.Label(new Rect(win.x + 24f, cy + 28f, PanelW - 48f, 48f), alone
				? "Weapons do not matter alone; the rules apply in a Multiplayer room's lobbies."
				: "You will be its Host: start the race from the strip under the board once the others have joined. The others in the room are told it is open.", faint);
			float by = cy + 84f;
			if (Button(new Rect(win.x + 24f, by, 200f, 42f), "Guns: " + (race.NewGuns ? "on" : "off")))
			{
				race.NewGuns = !race.NewGuns;
			}
			if (Button(new Rect(win.x + 236f, by, 200f, 42f), "Melee: " + (race.NewMelee ? "on" : "off")))
			{
				race.NewMelee = !race.NewMelee;
			}
			string create = alone ? "Start" : "Create lobby";
			if (Button(new Rect(win.xMax - 244f, by, 220f, 42f), linked ? create : "<color=#9a9a9a>Linking...</color>") && linked)
			{
				race.Create();
				return;
			}
			string note = race.Note;
			if (note != null)
			{
				GUI.Label(new Rect(win.x + 24f, win.yMax - 40f, PanelW - 48f, 28f), "<color=#e8e8e8>" + note + "</color>", small);
			}
		}

		// ---- the strip -------------------------------------------------------------------------------------------

		/// <summary>The lobby strip under the cockpit's board while in a lobby: the lobby and its state, Start (the Host's) and Leave.</summary>
		private void Strip(float w, float h, float scale)
		{
			EventRace race = EventRace.Instance;
			if (race == null || !race.Strip(out string top, out string bottom, out bool host, out bool canStart, out string startLabel))
			{
				return;
			}
			// Under the board where the game has it, right-aligned with it; high on the right if it has not been measured yet.
			EventBoard board = EventBoard.Instance;
			float right = w - 16f, topY = Mathf.Round(h * 0.3f);
			if (board != null && board.HasHome)
			{
				right = Mathf.Min(w - 16f, board.Home.xMax / scale);
				topY = (Screen.height - board.Home.yMin) / scale + 10f;
			}
			Rect r = new Rect(Mathf.Round(right - StripW), Mathf.Round(topY), StripW, StripH);
			Box(r, panel);
			Shadowed(new Rect(r.x + 14f, r.y + 8f, StripW - 150f, 26f), top, text);
			GUI.Label(new Rect(r.x + 14f, r.y + 34f, StripW - 150f, 24f), bottom, small);
			string note = race.Note;
			GUI.Label(new Rect(r.x + 14f, r.y + 58f, StripW - 150f, 24f), note != null ? "<color=#e8e8e8>" + note + "</color>" : "<color=#d6d7da>Point the hand at a button and grab</color>", small);
			Rect leave = new Rect(r.xMax - 126f, r.y + 50f, 112f, 34f), start = new Rect(r.xMax - 126f, r.y + 10f, 112f, 34f);
			if (host && HandButton(start, canStart ? startLabel : "<color=#9a9a9a>" + startLabel + "</color>", scale) && canStart)
			{
				race.StartRace();
			}
			if (HandButton(leave, "Leave", scale))
			{
				race.LeaveLobby();
			}
			HandOnTop(r, scale);
		}

		// ---- the open lobbies panel ------------------------------------------------------------------------------

		/// <summary>
		/// Every lobby waiting in the room, top left (the board is top right): one line each (course, Host, entrants and grid), there
		/// while it waits and gone when it starts or closes. Until 0.26 a lobby opening was a line in the cockpit's text queue, which
		/// drops lines when it is busy, so players missed them. Its button, pressed with the hand, slides it into the left edge, leaving
		/// a tab with the number of open lobbies; the tab brings it back. Hidden on the grid and in a race, and with no lobby open.
		/// </summary>
		private void OpenLobbies(float w, float h, float scale)
		{
			EventRace race = EventRace.Instance;
			if (race == null || EventRace.Racing)
			{
				return;
			}
			List<string> lines = race.OpenLobbies();
			if (lines.Count == 0)
			{
				return;
			}
			float target = lobbiesMinimised ? 1f : 0f;
			if (lobbiesTuck < 0f)
			{
				lobbiesTuck = target;
			}
			if (Event.current.type == EventType.Repaint)
			{
				lobbiesTuck = Mathf.MoveTowards(lobbiesTuck, target, Time.unscaledDeltaTime / 0.3f);
			}
			int shown = Mathf.Min(lines.Count, LobbiesMost);
			float full = 50f + shown * LobbiesLine + (lines.Count > shown ? 22f : 0f) + 8f, tab = 46f;
			float height = Mathf.Round(Mathf.Lerp(full, tab, lobbiesTuck));
			float x = Mathf.Round(Mathf.Lerp(16f, LobbiesTab - LobbiesW, lobbiesTuck));
			Rect r = new Rect(x, LobbiesTop, LobbiesW, height);
			Box(r, panel);
			float textW = LobbiesW - LobbiesTab - 16f;
			if (lobbiesTuck < 0.99f)
			{
				Shadowed(new Rect(r.x + 14f, r.y + 8f, textW, 28f), "OPEN LOBBIES  <size=80%>" + lines.Count + "</size>", heading);
				for (int i = 0; i < shown && 50f + (i + 1) * LobbiesLine <= height; i++)
				{
					GUI.Label(new Rect(r.x + 14f, r.y + 46f + i * LobbiesLine, textW, LobbiesLine), lines[i], small);
				}
				if (lines.Count > shown && full - 30f <= height)
				{
					GUI.Label(new Rect(r.x + 14f, r.y + 46f + shown * LobbiesLine, textW, 22f), "and " + (lines.Count - shown) + " more", faint);
				}
			}
			Rect button = new Rect(r.xMax - LobbiesTab + 6f, r.y + 6f, LobbiesTab - 12f, 34f);
			if (HandButton(button, lobbiesMinimised ? lines.Count + " >" : "<", scale))
			{
				lobbiesMinimised = !lobbiesMinimised;
				Mod.Info("Events: the open lobbies panel " + (lobbiesMinimised ? "minimised" : "brought back"));
			}
			HandOnTop(r, scale);
		}

		/// <summary>The hand's sprites, as the game shows them (the pointer, the hover hand, the holding hand under its indicator).</summary>
		private readonly List<SpriteRenderer> handSprites = new List<SpriteRenderer>();
		private Grabber handSpritesOf;

		/// <summary>
		/// The hand drawn again over <paramref name="area"/>: the game draws it as sprites in the cockpit, and Unity's immediate-mode GUI
		/// is always drawn over the scene, so the strip hid the hand pointing at its buttons. Each sprite the hand shows now is drawn
		/// where the camera puts it (its corners projected, turned as it is), only when it overlaps the strip, so nowhere else changes.
		/// </summary>
		private void HandOnTop(Rect area, float scale)
		{
			if (Event.current.type != EventType.Repaint || hand == null || !hand.enabled || hand.viewCamera == null)
			{
				return;
			}
			if (handSpritesOf != hand)
			{
				handSpritesOf = hand;
				handSprites.Clear();
				if (hand.idleSprite != null)
				{
					handSprites.Add(hand.idleSprite);
				}
				if (hand.interactibleSprite != null)
				{
					handSprites.Add(hand.interactibleSprite);
				}
				if (hand.handIndicator != null)
				{
					handSprites.AddRange(hand.handIndicator.GetComponentsInChildren<SpriteRenderer>(true));
				}
			}
			foreach (SpriteRenderer r in handSprites)
			{
				if (r != null && r.enabled && r.gameObject.activeInHierarchy && r.sprite != null)
				{
					try
					{
						DrawSprite(r, hand.viewCamera, area, scale);
					}
					catch (Exception)
					{
						// A sprite packed so tightly it has no rectangle: left as the game draws it.
					}
				}
			}
		}

		/// <summary>One of the hand's sprites drawn on the GUI where the camera shows it, if it overlaps <paramref name="area"/>.</summary>
		private static void DrawSprite(SpriteRenderer r, Camera cam, Rect area, float scale)
		{
			Sprite sprite = r.sprite;
			Texture2D tex = sprite.texture;
			Rect tr = sprite.textureRect;
			float ppu = sprite.pixelsPerUnit;
			Vector2 min = (sprite.textureRectOffset - sprite.pivot) / ppu, max = min + tr.size / ppu;
			Vector2 Gui(float x, float y)
			{
				Vector3 s = cam.WorldToScreenPoint(r.transform.TransformPoint(new Vector3(x, y, 0f)));
				return new Vector2(s.x / scale, (Screen.height - s.y) / scale);
			}
			Vector3 depth = cam.WorldToScreenPoint(r.transform.position);
			if (depth.z <= 0f || tex == null)
			{
				return;
			}
			Vector2 p0 = Gui(min.x, min.y), p1 = Gui(max.x, min.y), p2 = Gui(max.x, max.y), p3 = Gui(min.x, max.y);
			Rect bounds = Rect.MinMaxRect(Mathf.Min(p0.x, p1.x, p2.x, p3.x), Mathf.Min(p0.y, p1.y, p2.y, p3.y), Mathf.Max(p0.x, p1.x, p2.x, p3.x), Mathf.Max(p0.y, p1.y, p2.y, p3.y));
			if (!bounds.Overlaps(area))
			{
				return;
			}
			Vector2 centre = (p0 + p2) * 0.5f;
			float width = (p1 - p0).magnitude, height = (p3 - p0).magnitude;
			float angle = Mathf.Atan2(p1.y - p0.y, p1.x - p0.x) * Mathf.Rad2Deg;
			// The sprite's rows run up; the GUI's down: drawn with its top at p3's side (the texture coordinates are flipped by the GUI).
			Rect uv = new Rect(tr.x / tex.width, tr.y / tex.height, tr.width / tex.width, tr.height / tex.height);
			if (r.flipX)
			{
				uv = new Rect(uv.xMax, uv.y, -uv.width, uv.height);
			}
			if (r.flipY)
			{
				uv = new Rect(uv.x, uv.yMax, uv.width, -uv.height);
			}
			Matrix4x4 before = GUI.matrix;
			Color colour = GUI.color;
			GUI.matrix = before * Matrix4x4.TRS(centre, Quaternion.Euler(0f, 0f, angle), Vector3.one) * Matrix4x4.TRS(-centre, Quaternion.identity, Vector3.one);
			GUI.color = r.color;
			GUI.DrawTextureWithTexCoords(new Rect(centre.x - width * 0.5f, centre.y - height * 0.5f, width, height), tex, uv);
			GUI.color = colour;
			GUI.matrix = before;
		}

		/// <summary>
		/// A button pressed with the hand: lit while the hand points at it, pressed on the frame the hand grabs there (once a frame). With
		/// no hand to be found it is an ordinary button for the mouse.
		/// </summary>
		private bool HandButton(Rect r, string words, float scale)
		{
			hand ??= UnityEngine.Object.FindFirstObjectByType<Grabber>();
			if (hand == null)
			{
				return Button(r, words);
			}
			Vector2 at = new Vector2(hand.cursor.x / scale, (Screen.height - hand.cursor.y) / scale);
			bool over = hand.Held == null && r.Contains(at);
			if (Event.current.type != EventType.Repaint)
			{
				return false;
			}
			button.Draw(r, new GUIContent(words), over, over && GameInput.Hand(0), false, false);
			Grime(r);
			if (over && GameInput.HandDown(0) && handPressFrame != Time.frameCount)
			{
				handPressFrame = Time.frameCount;
				return true;
			}
			return false;
		}
	}
}
