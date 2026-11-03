using System;
using HalfGripTools;

namespace HalfGripOpenWorld
{
	/// <summary>
	/// How <see cref="EventRoom"/>'s messages go: through HalfGripTools' <c>ctx.Net</c>, which the Multiplayer mod carries between this
	/// mod's copies in a room (bytes only reach the Open World on the other machines). In a room the room's host is the arbiter; outside
	/// one (solo, or no Multiplayer mod) this machine is, and nothing is sent: a lobby of one is a solo run.
	///
	/// The handlers are added once (<see cref="Hook"/>, in <c>Mod.Load</c>) and pass everything to the room of the world that is built
	/// now, if any. The room is watched each frame (<see cref="Watch"/>): when this machine joins or leaves a room, or the room's host
	/// changes, every lobby is forgotten (they were the old arbiter's) and the new arbiter is asked for its own.
	/// </summary>
	internal sealed class EventLink : EventRoom.ILink
	{
		/// <summary>This player's id outside a room: never a Steam id.</summary>
		private const ulong Alone = 1UL;

		private static ModContext ctx;
		private static bool hooked;

		private ulong seenHost = ulong.MaxValue;
		private ulong seenLocal = ulong.MaxValue;

		/// <summary>The room being linked, or null between worlds.</summary>
		internal static EventRoom Room;

		internal static bool InRoom => HalfGripTools.Net.InRoom;

		public ulong Local => InRoom ? HalfGripTools.Net.LocalId : Alone;

		public bool Arbiter => !InRoom || HalfGripTools.Net.IsHost;

		public void ToArbiter(byte[] data)
		{
			if (InRoom && ctx != null && !ctx.Net.SendToHost(data))
			{
				Mod.Log.LogWarning("Events: a request could not be sent to the room's host");
			}
		}

		public void ToAll(byte[] data)
		{
			if (InRoom && ctx != null)
			{
				ctx.Net.Send(data);
			}
		}

		public void ToOne(ulong to, byte[] data)
		{
			if (InRoom && ctx != null)
			{
				ctx.Net.Send(data, to);
			}
		}

		/// <summary>Adds the network's handlers, once per game session.</summary>
		internal static void Hook(ModContext context)
		{
			ctx = context;
			if (hooked)
			{
				return;
			}
			hooked = true;
			try
			{
				ctx.Net.OnReceive(Received);
				HalfGripTools.Net.PlayerJoined += p =>
				{
					if (Room != null)
					{
						Room.PlayerJoined(p.Id);
					}
				};
				HalfGripTools.Net.PlayerLeft += p =>
				{
					if (Room != null)
					{
						Room.PlayerLeft(p.Id, EventRace.Clock());
					}
				};
				Mod.Info("Events: linked to the room's messages (" + (HalfGripTools.Net.Available ? "Multiplayer found" : "no Multiplayer: solo only") + ")");
			}
			catch (Exception e)
			{
				Mod.Log.LogWarning("Events: the room's messages could not be hooked (events are solo only): " + e.Message);
			}
		}

		private static void Received(ulong from, byte[] data)
		{
			try
			{
				Room?.Receive(from, data, EventRace.Clock());
			}
			catch (Exception e)
			{
				Mod.Log.LogWarning("Events: a message could not be read: " + e.Message);
			}
		}

		/// <summary>
		/// Each frame: when the room this machine is in (or not) or its host has changed since the last look, the lobbies are forgotten
		/// and the new arbiter asked for its own. True when that happened.
		/// </summary>
		internal bool Watch(double now)
		{
			ulong host = InRoom ? HalfGripTools.Net.HostId : 0UL, local = Local;
			if (host == seenHost && local == seenLocal)
			{
				return false;
			}
			bool first = seenHost == ulong.MaxValue;
			seenHost = host;
			seenLocal = local;
			if (Room != null)
			{
				if (!first)
				{
					Room.Reset();
				}
				Room.Hello(now);
				Mod.Info("Events: " + (InRoom ? (Arbiter ? "the room's host, holding its lobbies" : "in a room, the host holds the lobbies") : "alone, a lobby of one is a solo run"));
			}
			return !first;
		}

		/// <summary>A player's name as the room has it ("You" for this player outside a room).</summary>
		internal static string Name(ulong id)
		{
			if (!InRoom)
			{
				return "You";
			}
			try
			{
				foreach (NetPlayer p in HalfGripTools.Net.Players)
				{
					if (p.Id == id)
					{
						return string.IsNullOrEmpty(p.Name) ? "Player" : p.Name;
					}
				}
			}
			catch (Exception)
			{
				// The list is a copy; a failure here only loses a name.
			}
			return "Player";
		}
	}
}
