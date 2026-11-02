using System;
using System.Collections.Generic;
using UnityEngine;

namespace HalfGripOpenWorld
{
	/// <summary>
	/// The events' own windows, in the garage's look (its styles and pixel boxes, <see cref="GarageUi"/>): the course's panel, opened
	/// by stopping in a start box in neutral (its lobbies with Join, and Create with the Guns and Melee rules), and the lobby strip under
	/// the cockpit's board while in a lobby (the lobby, its state, Start for the Host and Leave).
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

		private Grabber hand;
		private int handPressFrame = -1;

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
				string none = alone ? "Outside a Multiplayer room the event is a solo run: press Start below." : "No lobby here yet. Create one below; the others in the room are told.";
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
				bool open = l.State != EventRoom.LobbyState.Racing && l.Count < c.MaxEntrants;
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
			if (Button(new Rect(win.xMax - 244f, by, 220f, 42f), alone ? "Start" : "Create lobby"))
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
