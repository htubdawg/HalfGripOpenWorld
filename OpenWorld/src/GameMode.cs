using HarmonyLib;
using UnityEngine;

namespace HalfGripOpenWorld
{
	/// <summary>
	/// The Open World's own game mode: free roam that goes on until the player leaves. A race in the game ends when the car is wrecked;
	/// here a wrecked car is put back on the nearest road after a few seconds, repaired, and the drive goes on.
	///
	/// Solo, this does it all (the game's end-of-race on a wreck is held off by <see cref="NoWreckEnd"/>). In a Multiplayer room the
	/// Multiplayer mod already handles a wreck itself (it spectates or respawns by the room's settings, and tells the other players): the
	/// room's race is set to respawn as the world loads (<see cref="MultiplayerLink.ForceRespawns"/>), so nobody is put out of a race
	/// that has no finish. Either way the respawn comes through <see cref="PlayerResetLogic.ResetOnTrack"/>, which
	/// <see cref="ResetInPlace"/> sends to the nearest road after a wreck (see <see cref="TakeWreck"/>).
	///
	/// Later this is where events go: races at the courses (and elsewhere) that a player joins by driving to their start, each with its
	/// own entrants, timing and results, several running at once in different places while the rest of the room roams (docs/PLAN.md).
	/// </summary>
	internal sealed class GameMode : MonoBehaviour
	{
		internal static GameMode Instance;

		private TrackManager tm;
		private bool wrecked;
		private float respawnAt = -1f;

		internal void Setup(TrackManager trackManager)
		{
			Instance = this;
			tm = trackManager;
			if (MultiplayerLink.InRoom)
			{
				MultiplayerLink.ForceRespawns();
			}
		}

		private void OnDestroy()
		{
			if (Instance == this)
			{
				Instance = null;
			}
		}

		/// <summary>
		/// True once after each wreck, for the reset that follows it: a wreck is often against a wall or a house, or in the trees, so the
		/// car goes back on the road instead of where it is.
		/// </summary>
		internal static bool TakeWreck()
		{
			if (Instance == null || !Instance.wrecked)
			{
				return false;
			}
			Instance.wrecked = false;
			return true;
		}

		private void Update()
		{
			CarLink link = tm != null ? tm.playerCarlink : null;
			if (link == null || GameManager.raceOver)
			{
				return;
			}
			if (link.hp <= 0f)
			{
				if (!wrecked)
				{
					wrecked = true;
					if (!MultiplayerLink.InRoom)
					{
						// The Multiplayer mod shows its own call-out and times its own respawn.
						TextController.EnqueueRandomText(1.5f, "WRECKED", "TOTALLED", "SCRAPPED");
						respawnAt = Time.time + Mod.RespawnSeconds.Value;
						Mod.Info("Wrecked: respawning in " + Mod.RespawnSeconds.Value.ToString("0.#") + " s");
					}
				}
			}
			else if (respawnAt >= 0f)
			{
				// Saved (a charm, a heal) before the respawn: it is not needed.
				wrecked = false;
				respawnAt = -1f;
			}
			// Game time, not real time: the respawn waits while the game is paused.
			if (respawnAt >= 0f && Time.time >= respawnAt)
			{
				respawnAt = -1f;
				try
				{
					link.hp = link.MaxHp;
					tm.ResetPlayerOnTrack();
					TextController.EnqueueRandomText(1f, "BACK ON THE ROAD");
				}
				catch (System.Exception e)
				{
					Mod.Log.LogWarning("The respawn after a wreck failed: " + e);
				}
			}
		}
	}

	/// <summary>
	/// Solo, a wreck does not end the race (and with it the free roam): <see cref="GameMode"/> respawns the car. In a Multiplayer room the
	/// Multiplayer mod's own prefix on this method already keeps the race going.
	/// </summary>
	[HarmonyPatch(typeof(TrackManager), "OnPlayerDestroyed")]
	internal static class NoWreckEnd
	{
		private static bool Prefix()
		{
			return !WorldScene.Loaded || MultiplayerLink.InRoom;
		}
	}
}
