using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace HalfGripOpenWorld
{
	/// <summary>
	/// The Open World's own game mode: free roam that goes on until the player leaves. A race in the game ends when the car is wrecked;
	/// here a wrecked car is put back at the spawn after a few seconds, repaired, and the drive goes on. No AI rivals, solo
	/// (<see cref="NoRivals"/>) or in a room.
	///
	/// Solo, this does it all (the game's end-of-race on a wreck is held off by <see cref="NoWreckEnd"/>). In a Multiplayer room the
	/// Multiplayer mod already handles a wreck itself (it spectates or respawns by the room's settings, and tells the other players): the
	/// room's race is set to respawn, with no AI, as the world loads (<see cref="MultiplayerLink.RoomRules"/>), so nobody is put out of
	/// a race that has no finish. Either way the respawn comes through <see cref="PlayerResetLogic.ResetOnTrack"/>, which
	/// <see cref="ResetInPlace"/> sends to the spawn after a wreck (see <see cref="TakeWreck"/>).
	///
	/// A garage can be made the respawn point instead (its window's Respawn here, <see cref="SetRespawn"/>). That lasts for this race
	/// only: it is kept on this component, which goes with the scene, so every race starts with the spawn again and nothing is saved.
	///
	/// The events at the courses are <see cref="EventRace"/>'s: solo time trials for now; a wreck in one goes back to its last gate.
	/// Later, events a room runs together, each with its own entrants, timing and results, several at once in different places while
	/// the rest of the room roams (docs/PLAN.md).
	/// </summary>
	internal sealed class GameMode : MonoBehaviour
	{
		internal static GameMode Instance;

		private TrackManager tm;
		private bool wrecked;
		private float respawnAt = -1f;
		/// <summary>The garage a wreck goes back to, or null for the spawn.</summary>
		private GarageSites.Site respawnGarage;
		/// <summary>Where the last respawn put the car, for the call-out after it (set by <see cref="ToSpawn"/>).</summary>
		private string backAt;

		internal void Setup(TrackManager trackManager)
		{
			Instance = this;
			tm = trackManager;
			if (MultiplayerLink.InRoom)
			{
				MultiplayerLink.RoomRules();
			}
		}

		private void OnDestroy()
		{
			if (Instance == this)
			{
				Instance = null;
			}
		}

		/// <summary>Grid slots tried for a respawn (<see cref="WorldScene.GridSlot"/>): enough for a full room all wrecked at once.</summary>
		private const int RespawnSlots = 24;

		/// <summary>How near another car may be to a slot for it to count as taken (a car is about 14 long).</summary>
		private const float SlotClear = 8f;

		/// <summary>The garage the player made the respawn point this race, or null: the spawn.</summary>
		internal static GarageSites.Site RespawnGarage => Instance != null ? Instance.respawnGarage : null;

		/// <summary>Makes a garage the respawn point for the rest of this race (null: back to the spawn).</summary>
		internal static void SetRespawn(GarageSites.Site site)
		{
			if (Instance == null)
			{
				return;
			}
			Instance.respawnGarage = site;
			Mod.Log.LogInfo(site != null ? "Respawn point: " + site.Name : "Respawn point: the start");
		}

		/// <summary>True once after each wreck, for the reset that follows it: that one goes to the spawn (<see cref="ToSpawn"/>).</summary>
		internal static bool TakeWreck()
		{
			if (Instance == null || !Instance.wrecked)
			{
				return false;
			}
			Instance.wrecked = false;
			return true;
		}

		/// <summary>
		/// Puts the car at the spawn, facing up the road: on the first grid slot no other car is on (another player respawned or waiting
		/// there), so respawns never land on top of each other. If a garage is the respawn point, in its circle instead, facing out of
		/// it; at the spawn after all if another car stands there.
		/// </summary>
		internal static void ToSpawn(Streamer streamer)
		{
			// In an event: back on the course at its last gate instead.
			if (EventRace.Respawn(streamer))
			{
				CallOut("BACK AT THE LAST CHECKPOINT");
				return;
			}
			// Every other car in the scene: the other players' stand-ins (the Multiplayer mod adds them to the game's own list).
			List<EnemyController> others = Instance != null && Instance.tm != null ? Traverse.Create(Instance.tm).Field("enemies").GetValue<List<EnemyController>>() : null;
			GarageSites.Site garage = RespawnGarage;
			if (garage != null)
			{
				if (!Taken(others, Origin.Local(garage.X, 0f, garage.Z)))
				{
					streamer.BuildAround(garage.X, garage.Z, 1);
					streamer.Place(garage.X, garage.Z, Quaternion.LookRotation(new Vector3((float)garage.Hx, 0f, (float)garage.Hz), Vector3.up));
					// Standing still in the circle, maybe in neutral: the garage waits for the shifter to be taken and let go (ShifterHand).
					CallOut("BACK AT THE GARAGE");
					return;
				}
				Mod.Info("Respawn: another car is in " + garage.Name + "'s circle, back at the start instead");
			}
			CallOut("BACK AT THE START");
			double x = Network.SpawnX, z = Network.SpawnZ;
			for (int n = 0; n < RespawnSlots; n++)
			{
				WorldScene.GridSlot(n, out double sx, out double sz);
				if (!Taken(others, Origin.Local(sx, 0f, sz)))
				{
					x = sx;
					z = sz;
					break;
				}
			}
			streamer.BuildAround(x, z, 1);
			streamer.Place(x, z, Quaternion.LookRotation(new Vector3((float)Network.SpawnDirX, 0f, (float)Network.SpawnDirZ), Vector3.up));
		}

		/// <summary>Notes where this respawn put the car, for the call-out <see cref="Update"/> shows after it.</summary>
		private static void CallOut(string words)
		{
			if (Instance != null)
			{
				Instance.backAt = words;
			}
		}

		private static bool Taken(List<EnemyController> others, Vector3 slot)
		{
			if (others == null)
			{
				return false;
			}
			foreach (EnemyController car in others)
			{
				if (car == null || !car.gameObject.activeInHierarchy)
				{
					continue;
				}
				Vector3 d = car.transform.position - slot;
				d.y = 0f;
				if (d.sqrMagnitude < SlotClear * SlotClear)
				{
					return true;
				}
			}
			return false;
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
					backAt = null;
					tm.ResetPlayerOnTrack();
					TextController.EnqueueRandomText(1f, backAt ?? "BACK AT THE START");
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
