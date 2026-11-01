namespace HalfGripOpenWorld
{
	/// <summary>
	/// The land past the far view's drawn edge, as the painted sky shows it: for each column of <see cref="SkyPaint"/>'s picture and each
	/// row in a band round the horizon, the first land a ray that way meets beyond the distance the far view's meshes are drawn to, with
	/// its distance, colour and slope. <see cref="SkyPaint"/> lights and fogs those texels as the far view's material is lit and fogged, so
	/// on a clear day the mountains 20 or 30 km off stand on the horizon as hazy shapes, where before 0.20.0 the fog closed at the meshes'
	/// edge and the world stopped there.
	///
	/// The sky is drawn behind everything, so the painted land needs no depth: anything nearer, drawn by the meshes, covers it. Built from
	/// a little inside the drawn edge, so as the edge moves (the weather closing in) no gap opens before the next build. The sky is seen as
	/// from infinitely far away, so the painting is right only for the point it was built from: it is built again on a worker whenever the
	/// eye has moved a little (<see cref="Sky"/>), which at 10 km out is well under a texel.
	///
	/// This half is the data the painted sky reads (no Unity types and nothing of the generator, so <c>tools/skyview</c> takes
	/// <see cref="SkyPaint"/> without the world); <c>SkylineBuild.cs</c> fills it from <see cref="FarGrid"/>. Display only.
	/// </summary>
	internal sealed partial class Skyline
	{
		/// <summary>Rows each side of the horizon looked at: 48 rows of 0.35 degrees, about 17 degrees up and down (the highest peak seen from
		/// the sea at a drawn distance of 4000 is under that).</summary>
		internal const int Band = 48;

		/// <summary>Rows in the band, from <see cref="Lowest"/> up.</summary>
		internal const int Rows = Band * 2;

		/// <summary>The picture's row of the band's first row.</summary>
		internal const int Lowest = SkyPaint.H / 2 - Band;

		/// <summary>How far each texel's land is (0 for none), row by row of the band from the lowest, a column a value.</summary>
		internal readonly float[] Dist = new float[SkyPaint.W * Rows];

		/// <summary>The land's colour at each texel (RGBA, sRGB as the picture has it; alpha 255 for land, 0 for the sea past the world).</summary>
		internal readonly byte[] Colour = new byte[SkyPaint.W * Rows * 4];

		/// <summary>The land's normal at each texel, x, y, z.</summary>
		internal readonly float[] Normal = new float[SkyPaint.W * Rows * 3];

		/// <summary>Where it was built from: world x and z, the eye's scene height, and the distance it starts at.</summary>
		internal double X, Z, Eye, From;

		/// <summary>Texels with land in them, and how long the build took, for the log.</summary>
		internal int Hits;
		internal double Ms;
	}
}
