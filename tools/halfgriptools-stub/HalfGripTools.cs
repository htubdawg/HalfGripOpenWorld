// A stand-in for HalfGripTools.dll with only the members the Open World uses, so tools/check.sh can compile the mod where the real
// HalfGripTools is not to hand (the cloud sessions). It does nothing: never ship it. Add a member here when the mod starts using one.
//   dotnet build tools/halfgriptools-stub -c Release -o <out> [-p:GameDir=<game folder>]
//   tools/check.sh <game folder> <out>/HalfGripTools.dll
using System; using System.Collections.Generic; using BepInEx.Configuration; using BepInEx.Logging; using UnityEngine; using UnityEngine.SceneManagement;
namespace HalfGripTools
{
	public abstract class HalfGripMod { public abstract void Load(ModContext ctx); public virtual void Unload() { } }
	public class ModKey { public bool Down => false; public bool Held => false; }
	public class KeyRegistry { public ModKey Add(KeyCode key, string label, Controls.Section section, Func<bool> shown) => null; }
	public class NetPlayer { public ulong Id; public string Name; }
	public class ModNet { public bool Send(byte[] d, ulong to = Net.Everyone, bool reliable = true) => false; public bool SendToHost(byte[] d, bool reliable = true) => false; public void OnReceive(Action<ulong, byte[]> a) { } }
	public static class Net { public const ulong Everyone = 0UL; public static bool Available, InRoom, IsHost; public static ulong LocalId, HostId; public static IReadOnlyList<NetPlayer> Players => new List<NetPlayer>(); public static event Action<NetPlayer> PlayerJoined, PlayerLeft; }
	public class ModContext { public ManualLogSource Log; public ConfigFile Config; public HarmonyLib.Harmony Harmony; public KeyRegistry Keys; public ModNet Net; public string Folder = ""; public void PatchAll() { } public void OnSceneLoaded(Action<Scene> a) { } public void OnUnload(Action a) { } }
}
