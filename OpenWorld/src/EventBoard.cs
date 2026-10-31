using System;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace HalfGripOpenWorld
{
	/// <summary>
	/// The cockpit's board at the top right of the screen (the game's leaderboard, <see cref="StatusInfo"/>, and the lap counter beside
	/// it), put to the events' use. In an event it shows the event (<see cref="EventRace"/> sets <see cref="Text"/> and
	/// <see cref="Lap"/> each frame) and stands where the game has it. Out of one it has nothing to time in free roam, so it is tucked
	/// into the right edge of the screen with a sliver showing; the hand over that sliver (or over the board while it is out) brings it
	/// back, showing the game's own list of the cars in the race: in a Multiplayer room, the players.
	///
	/// The board is in the cockpit, which the floating origin leaves where it is; it is moved along the view camera's right, worked out
	/// each frame on the screen, so it tucks the same at any field of view or head position.
	/// </summary>
	internal sealed class EventBoard : MonoBehaviour
	{
		internal static EventBoard Instance;

		/// <summary>The board's text for this frame, or null when there is no event to show (the board tucks away).</summary>
		internal string Text;

		/// <summary>The lap counter's text for this frame, or null for the count of cars in the race.</summary>
		internal string Lap;

		/// <summary>How much of the board's width stays on the screen when it is tucked away, as a fraction of the screen's width.</summary>
		private const float Sliver = 0.025f;

		/// <summary>How far round the board (or the sliver) the hand still counts as over it, in pixels at 1080 high.</summary>
		private const float HoverMargin = 30f;

		/// <summary>Tucking or bringing it out takes about this long.</summary>
		private const float SlideSeconds = 0.3f;

		private StatusInfo status;
		private TextMeshPro statusText;
		private Transform board;
		private Vector3 boardHome;
		private LapCounter counter;
		private TextMeshPro counterText;
		private Transform counterT;
		private Vector3 counterHome;
		private Renderer[] renderers;
		private Grabber grabber;
		private TrackManager tm;
		private float nextLook;
		private bool showingEvent;
		private string shownText;
		private string shownLap;

		/// <summary>0 out where the game has it, 1 tucked away.</summary>
		private float tuck = 1f;

		private void Awake()
		{
			Instance = this;
		}

		/// <summary>The board and the lap counter, once the cockpit is there (looked for once a second until it is).</summary>
		private bool Find()
		{
			if (board != null && counterT != null)
			{
				return true;
			}
			if (Time.unscaledTime < nextLook)
			{
				return false;
			}
			nextLook = Time.unscaledTime + 1f;
			try
			{
				tm ??= FindFirstObjectByType<TrackManager>();
				status = FindFirstObjectByType<StatusInfo>();
				counter = FindFirstObjectByType<LapCounter>();
				if (status == null || counter == null)
				{
					return false;
				}
				statusText = Traverse.Create(status).Field("statusText").GetValue<TextMeshPro>() ?? status.GetComponent<TextMeshPro>();
				// The leaderboard's group: its backing sprite and the text.
				board = status.transform.parent != null ? status.transform.parent : status.transform;
				boardHome = board.localPosition;
				counterT = counter.transform;
				counterHome = counterT.localPosition;
				counterText = counter.GetComponent<TextMeshPro>();
				Renderer[] a = board.GetComponentsInChildren<Renderer>(true), b = counterT.GetComponentsInChildren<Renderer>(true);
				renderers = new Renderer[a.Length + b.Length];
				a.CopyTo(renderers, 0);
				b.CopyTo(renderers, a.Length);
				// The lap counter counts laps of the token loop, which mean nothing in free roam: it shows what this sets instead.
				counter.enabled = false;
				Mod.Info("Event board: the cockpit's leaderboard and lap counter found");
				return true;
			}
			catch (Exception e)
			{
				Mod.Log.LogWarning("Event board: the cockpit's leaderboard could not be taken over: " + e.Message);
				board = null;
				return false;
			}
		}

		private void LateUpdate()
		{
			if (!Find() || statusText == null)
			{
				return;
			}
			try
			{
				Show();
				Slide();
			}
			catch (Exception e)
			{
				Mod.Log.LogWarning("Event board: " + e);
				board = null;
			}
		}

		/// <summary>The event's text on the board while there is one; the game's leaderboard otherwise.</summary>
		private void Show()
		{
			bool inEvent = Text != null;
			if (inEvent)
			{
				status.enabled = false;
				if (Text != shownText || !showingEvent)
				{
					shownText = Text;
					statusText.text = Text;
				}
			}
			else if (showingEvent)
			{
				// The game's panel writes only when its text changes from what it last wrote: forget that, so it writes again.
				Traverse.Create(status).Field("shownStatus").SetValue(null);
				status.enabled = true;
				shownText = null;
			}
			showingEvent = inEvent;
			string lap = Lap;
			if (lap == null)
			{
				int cars = tm != null && tm.lapManager != null && tm.lapManager.leaderboard != null ? tm.lapManager.leaderboard.Count : 1;
				lap = "<size=75%>" + (MultiplayerLink.InRoom ? "players" : "cars") + "</size> " + Math.Max(1, cars);
			}
			if (counterText != null && lap != shownLap)
			{
				shownLap = lap;
				counterText.text = lap;
			}
		}

		/// <summary>
		/// Out while an event is shown or the hand is over the board; tucked into the screen's right edge otherwise. Each frame the board
		/// is put back where the game has it, its outline on the screen measured, and it is moved right far enough that only a sliver
		/// of it is left on the screen, times how far tucked it is.
		/// </summary>
		private void Slide()
		{
			grabber ??= FindFirstObjectByType<Grabber>();
			Camera cam = grabber != null && grabber.viewCamera != null ? grabber.viewCamera : Camera.main;
			board.localPosition = boardHome;
			counterT.localPosition = counterHome;
			if (cam == null || !Outline(cam, out Rect home, out Vector3 middle))
			{
				return;
			}
			Vector3 right = cam.transform.right;
			float perUnit = cam.WorldToScreenPoint(middle + right).x - cam.WorldToScreenPoint(middle).x;
			if (perUnit <= 1e-3f)
			{
				return;
			}
			float travel = Mathf.Max(0f, Screen.width * (1f - Sliver) - home.xMin);
			Rect now = home;
			now.x += travel * tuck;
			float margin = HoverMargin * Screen.height / 1080f;
			Rect reach = Rect.MinMaxRect(now.xMin - margin, now.yMin - margin, Mathf.Max(now.xMax, Screen.width) + margin, now.yMax + margin);
			bool hand = grabber != null && grabber.Held == null && reach.Contains(grabber.cursor);
			float target = showingEvent || hand ? 0f : 1f;
			tuck = Mathf.MoveTowards(tuck, target, Time.unscaledDeltaTime / SlideSeconds);
			if (tuck > 0f)
			{
				Vector3 shift = right * (travel / perUnit * tuck);
				board.position += shift;
				counterT.position += shift;
			}
		}

		/// <summary>The board's and the lap counter's outline on the screen (pixels, from the bottom left), and their middle in the world.</summary>
		private bool Outline(Camera cam, out Rect rect, out Vector3 middle)
		{
			rect = default;
			middle = default;
			bool any = false;
			Bounds all = default;
			foreach (Renderer r in renderers)
			{
				if (r == null || !r.enabled || !r.gameObject.activeInHierarchy || r.bounds.size.sqrMagnitude <= 0f)
				{
					continue;
				}
				if (!any)
				{
					all = r.bounds;
					any = true;
				}
				else
				{
					all.Encapsulate(r.bounds);
				}
			}
			if (!any)
			{
				return false;
			}
			middle = all.center;
			float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
			for (int i = 0; i < 8; i++)
			{
				Vector3 corner = all.center + Vector3.Scale(all.extents, new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f));
				Vector3 sp = cam.WorldToScreenPoint(corner);
				if (sp.z <= 0f)
				{
					return false;
				}
				x0 = Mathf.Min(x0, sp.x);
				y0 = Mathf.Min(y0, sp.y);
				x1 = Mathf.Max(x1, sp.x);
				y1 = Mathf.Max(y1, sp.y);
			}
			rect = Rect.MinMaxRect(x0, y0, x1, y1);
			return true;
		}

		private void OnDestroy()
		{
			if (board != null)
			{
				board.localPosition = boardHome;
			}
			if (counterT != null)
			{
				counterT.localPosition = counterHome;
			}
			if (status != null)
			{
				status.enabled = true;
			}
			if (counter != null)
			{
				counter.enabled = true;
			}
			if (Instance == this)
			{
				Instance = null;
			}
		}
	}
}
