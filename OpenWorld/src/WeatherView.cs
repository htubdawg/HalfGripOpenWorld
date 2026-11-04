using UnityEngine;

namespace HalfGripOpenWorld
{
	/// <summary>
	/// A public view of the weather at the player's car, for other mods (Slap Pack's windscreen reads it by reflection, by type and
	/// member name). Its full name and its members' names and signatures are an API other mods rely on: do not rename or change them;
	/// a change that would alter what a member means goes up <see cref="ApiVersion"/> instead. Every member is a static property or
	/// method, as on <see cref="MapView"/>, so a mod compiled on its own finds it with <c>GetProperty</c>/<c>GetMethod</c> and needs
	/// no type of ours (a snapshot struct could not be named by another mod's code).
	///
	/// <see cref="Sky"/> writes it each frame after working out the weather at the car, so it is what the scene shows: eased, after the
	/// cut-off under which nothing falls and the rain/snow split by height, but before the particles' own budget, so a lower graphics
	/// setting never changes it. Reading it is a few field reads: nothing is worked out or allocated. Main thread.
	/// </summary>
	public static class WeatherView
	{
		private static bool live;
		private static float rain, snow, cloud, wetness, hour;
		private static Vector3 wind;

		/// <summary>The contract's version: 1. Goes up only if a member's meaning changes; new members may come without it.</summary>
		public static int ApiVersion => 1;

		/// <summary>
		/// True while an Open World race is loaded and its sky is running. False before the sky has taken over the scene's weather, on a
		/// scene change, after it stopped on an error, and outside the Open World; then every value below reads 0.
		/// </summary>
		public static bool Active => live && Origin.Active;

		/// <summary>Rain falling at the car, 0 to 1. <c>Rain + Snow</c> is at most 1; both above 0 is sleet.</summary>
		public static float Rain => Active ? rain : 0f;

		/// <summary>Snow falling at the car, 0 to 1 (on high ground, above the day's snow line).</summary>
		public static float Snow => Active ? snow : 0f;

		/// <summary>
		/// The wind the storms drift on, in world (and scene: the floating origin only moves positions) units a second, the way the air
		/// moves: x east, z north, y always 0. Its speed is about 4 to 11 units a second (a car is about 14 long), fixed for a race.
		/// </summary>
		public static Vector3 Wind => Active ? wind : Vector3.zero;

		/// <summary>Cloud over the car, 0 (clear) to 1 (overcast, dark). Not the same as rain: cloud comes before a storm and stays after.</summary>
		public static float Cloud => Active ? cloud : 0f;

		/// <summary>How wet the road is at the car, 0 to 1: it stays wet a while after a shower and dries.</summary>
		public static float Wetness => Active ? wetness : 0f;

		/// <summary>The time of day, 0 to 24 (12 is noon).</summary>
		public static float Hour => Active ? hour : 0f;

		/// <summary>
		/// The falling weather at the car in one call: false (and all 0) when <see cref="Active"/> is false. Its parameters are plain
		/// types, so another mod can bind it to a delegate of its own and poll it each frame without allocating.
		/// </summary>
		public static bool TryGetLocalWeather(out float rain, out float snow, out Vector3 wind)
		{
			bool active = Active;
			rain = active ? WeatherView.rain : 0f;
			snow = active ? WeatherView.snow : 0f;
			wind = active ? WeatherView.wind : Vector3.zero;
			return active;
		}

		internal static void Set(float rainNow, float snowNow, Vector3 windNow, float cloudNow, float wetNow, float hourNow)
		{
			rain = rainNow;
			snow = snowNow;
			wind = windNow;
			cloud = cloudNow;
			wetness = wetNow;
			hour = hourNow;
			live = true;
		}

		internal static void Clear()
		{
			live = false;
			rain = snow = cloud = wetness = hour = 0f;
			wind = Vector3.zero;
		}
	}
}
