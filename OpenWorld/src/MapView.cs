using UnityEngine;

namespace HalfGripOpenWorld
{
	/// <summary>
	/// A public map of the loaded country, for other mods (MiniMap reads it by reflection, by type and member name). Its full name and
	/// its members' names and signatures are an API other mods rely on: do not rename or change them. Every member is a static property
	/// or method, so it is found with <c>GetProperty</c> or <c>GetMethod</c>.
	///
	/// Each loaded tile has a map picture of <see cref="TilePixels"/> square: its land use with the water and the roads painted in by
	/// class, in the colours of docs/world.png, and each garage (one for garages close together) marked with a cyan disc in a dark ring, about 100 units across. Display only: it follows the world but nothing in the world depends on it.
	/// </summary>
	public static class MapView
	{
		private static int version;

		/// <summary>True while an Open World race is loaded.</summary>
		public static bool Active => Origin.Active;

		/// <summary>World x of the scene's 0,0: a scene position plus this is a world position.</summary>
		public static double OriginX => Origin.X;

		/// <summary>World z of the scene's 0,0.</summary>
		public static double OriginZ => Origin.Z;

		/// <summary>Side of a tile in world units. Tile (tx, tz) covers world x from tx * TileSize to (tx + 1) * TileSize, and z the same.</summary>
		public static float TileSize => Streamer.TileSize;

		/// <summary>Pixels along a side of a tile's map picture.</summary>
		public static int TilePixels => Streamer.Pixels;

		/// <summary>Goes up whenever a tile's map picture is added or removed, so a map knows when to refresh.</summary>
		public static int Version => version;

		/// <summary>
		/// Copies tile (tx, tz)'s map picture into <paramref name="into"/> (at least TilePixels x TilePixels): rows from the south edge,
		/// x running east. False when the tile is not loaded. Main thread only.
		/// </summary>
		public static bool TryGetTile(int tx, int tz, Color32[] into)
		{
			Streamer streamer = Streamer.Instance;
			return streamer != null && streamer.TryGetMap(tx, tz, into);
		}

		internal static void Changed()
		{
			version++;
		}
	}
}
