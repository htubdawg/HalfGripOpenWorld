namespace HalfGripOpenWorld
{
	/// <summary>Stand-ins for the parts of Mod the checked code reads (the real Mod needs HalfGripTools and the game).</summary>
	internal static class Mod
	{
		internal static BepInEx.Logging.ManualLogSource Log = BepInEx.Logging.Logger.CreateLogSource("meshcheck");

		internal static void Info(string s)
		{
		}

		internal static class GrassReach
		{
			internal static int Value = 2;
		}
	}

	/// <summary>Stand-in: the checks never run in an Open World scene.</summary>
	internal static class WorldScene
	{
		internal static bool Loaded => false;
	}
}
