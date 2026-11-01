using System;
using System.Collections.Generic;

namespace HalfGripOpenWorld
{
	/// <summary>
	/// The sky over the Open World, painted in code: a gradient from the horizon to the top of the sky that follows the sun (blue by day,
	/// gold and red as it sets, deep blue at dusk, black at night), a glow round the sun, the sun's disc, the moon's disc with its seas,
	/// stars that turn about the pole through the night, and a layer of cloud overhead that drifts with the storms' wind and thickens
	/// under a storm. Before 0.18.0 the sky was one of the game's skyboxes, dimmed and tinted, swapped for a night one once it was dark:
	/// its painted sun stood still while the light moved, and the day and night cut over in a frame.
	///
	/// The game cannot compile a shader at run time and ships only two sky shaders (<c>Skybox/Cubemap</c> and <c>Skybox/Panoramic</c>),
	/// so the sky is a picture: an equirectangular image, <see cref="W"/> by <see cref="H"/> (a texel is about 0.35 degrees, a few screen
	/// pixels, drawn point-sampled like the world's other pixel-art textures), shown through a copy of the game's panoramic sky material.
	/// <see cref="Sky"/> owns the texture and material; this only fills an RGBA byte array, so <c>tools/skyview</c> can draw it without
	/// Unity. The sky changes slowly (the sun crosses a texel in about two seconds), so <see cref="Sky"/> has a whole picture painted on a
	/// worker about five times a second and uploads each when it is done (before 0.20.1 a few rows a frame on the main thread, a millisecond
	/// or more of every frame).
	///
	/// Since 0.20.0 the land past the far view's drawn edge is painted over it round the horizon (<see cref="Skyline"/>, lit and fogged as
	/// the far view is), so the country goes on past the meshes to where the fog really ends.
	///
	/// It only draws what <see cref="Sky"/> says (the hour, the cloud at the car), so it may use floats and <c>Math</c>; the stars and
	/// the cloud pattern come from the room's seed, so players in a room see the same ones.
	/// </summary>
	internal sealed class SkyPaint
	{
		/// <summary>The picture's size: a full turn across, from straight down to straight up from bottom to top.</summary>
		internal const int W = 1024;
		internal const int H = 512;
		private const int Half = H / 2;

		/// <summary>The cloud layer's height above the car, in game units (about 1200 m), which sets how fast it shrinks toward the horizon.</summary>
		private const float CloudHeight = 4000f;

		/// <summary>How wide one repeat of the cloud pattern is on the ground, in game units, and its size in samples.</summary>
		private const float CloudTile = 24000f;
		private const int CloudN = 256;

		/// <summary>The discs' angular radii, in degrees: larger than the real ones (half a degree), so they read at a texel of 0.35.</summary>
		private const float SunRadius = 2.0f;
		private const float MoonRadius = 2.2f;

		private const int StarCount = 2400;

		/// <summary>The rain curtains are looked for along each column's way from this far out (nearer rain is the fog's and the falling
		/// particles' job), in steps of <see cref="CurtainStep"/>, <see cref="CurtainSteps"/> of them (out to the storm field's edge).</summary>
		private const float CurtainFrom = 1500f;
		private const float CurtainStep = 900f;
		private const int CurtainSteps = 27;

		/// <summary>How thick rain is to look through: a curtain of a downpour this many units deep hides about two thirds of the sky behind it.</summary>
		private const float CurtainDensity = 0.0007f;

		/// <summary>The pole the stars turn about leans this far up from the northern horizon (the world's latitude, in degrees).</summary>
		private const float Latitude = 48f;

		/// <summary>A colour in sRGB, 0 to 1 a channel, as the texture and the fog take it.</summary>
		internal struct Rgb
		{
			internal float R, G, B;

			internal Rgb(float r, float g, float b)
			{
				R = r;
				G = g;
				B = b;
			}

			public static Rgb operator +(Rgb a, Rgb b) => new Rgb(a.R + b.R, a.G + b.G, a.B + b.B);
			public static Rgb operator *(Rgb a, float k) => new Rgb(a.R * k, a.G * k, a.B * k);
			public static Rgb operator *(Rgb a, Rgb b) => new Rgb(a.R * b.R, a.G * b.G, a.B * b.B);

			internal static Rgb Lerp(Rgb a, Rgb b, float t) => new Rgb(a.R + (b.R - a.R) * t, a.G + (b.G - a.G) * t, a.B + (b.B - a.B) * t);

			internal float Luma => 0.299f * R + 0.587f * G + 0.114f * B;
		}

		/// <summary>What the sky is to show: set by <see cref="Sky"/> each frame, taken at the start of each picture.</summary>
		internal struct State
		{
			/// <summary>Unit vectors to the sun and the moon (x east, y up, z north), from <see cref="Dir"/>; and their heights in degrees.</summary>
			internal float SunX, SunY, SunZ, SunElevation;
			internal float MoonX, MoonY, MoonZ, MoonElevation;

			/// <summary>How overcast it is at the car (<see cref="Storms.At"/>, eased): 0 clear, 1 a black sky.</summary>
			internal float Cloud;

			/// <summary>The share of the sky under cloud, 0 to 1.</summary>
			internal float Coverage;

			/// <summary>The day's and the night's haze: the fog colours of the two weathers drawn for the race, which tint the sky a little, so
			/// each race's sky is not quite the same.</summary>
			internal Rgb DayHaze, NightHaze;

			/// <summary>How far the stars have turned about the pole, in degrees (15 an hour).</summary>
			internal float StarTurn;

			/// <summary>Where the car is in the cloud pattern, in game units within a <see cref="CloudTile"/>: its world position less the wind's drift.</summary>
			internal float CloudX, CloudZ;

			/// <summary>The storms round the car (<see cref="Storms.Fill"/>), drawn where they are: dark cloud overhead and toward the
			/// horizon, rain hanging under it. Null for none.</summary>
			internal StormField Storm;

			/// <summary>The car's world position, where the storms are looked up from.</summary>
			internal float CarX, CarZ;

			/// <summary>How far one sees on the ground (the fog's end), in game units: storms further off fade into the horizon's haze.</summary>
			internal float Reach;

			/// <summary>The land past the far view's drawn edge (<see cref="Skyline"/>), painted over the sky round the horizon, lit and fogged
			/// by <see cref="Light"/>. Null for none.</summary>
			internal Skyline Land;

			internal LandLight Light;
		}

		/// <summary>
		/// How the far view's land is lit and fogged in the scene, so the land painted past its edge (<see cref="Skyline"/>) carries on from it
		/// with no seam: the far view is the game's <c>Universal Render Pipeline/Lit</c> on a white base, so a texel is its picture's colour
		/// times the ambient light from the way its slope faces plus the sun's, then Unity's linear fog by its distance, in linear colour
		/// when the game draws in linear colour.
		/// </summary>
		internal struct LandLight
		{
			/// <summary>True when the game draws in linear colour (colours mixed after taking the sRGB curve off, put back after).</summary>
			internal bool Linear;

			/// <summary>The ambient light (the scene's light probe, as it is set this moment) from east, west, up, down, north and south, in
			/// the colour space the lighting is done in.</summary>
			internal Rgb East, West, Up, Down, North, South;

			/// <summary>The unit vector toward the scene's light (the sun, or the moon at night), and its colour times its intensity.</summary>
			internal float SunX, SunY, SunZ;
			internal Rgb Sun;

			/// <summary>The far view's material's base colour (white, darker when wet), as a factor.</summary>
			internal float Tint;

			/// <summary>The scene's environment reflections' strength (the sea past the world shines like the far view's sea sheet).</summary>
			internal float Reflect;

			/// <summary>The fog's start and end (Unity's linear fog: none at the start, all at the end).</summary>
			internal float FogStart, FogEnd;
		}

		/// <summary>The picture, RGBA, bottom row first, as <c>Texture2D.SetPixelData</c> takes it. <see cref="All"/> paints into it, so it
		/// holds a whole picture only between pictures: read it only while none is being painted.</summary>
		internal readonly byte[] Pixels = new byte[W * H * 4];

		private readonly float[] rowY = new float[H];
		private readonly float[] rowR = new float[H];
		private readonly float[] cosPhi = new float[W];
		private readonly float[] sinPhi = new float[W];
		private readonly float[] cloudNoise = new float[CloudN * CloudN];
		/// <summary>The cloud pattern's values at each hundredth of the sky, low to high: the threshold for a coverage is read off it.</summary>
		private readonly float[] cloudQuantile = new float[101];
		private readonly float[] starX = new float[StarCount], starY = new float[StarCount], starZ = new float[StarCount], starMag = new float[StarCount];
		/// <summary>The stars' light in each texel of the upper half for the picture being painted, and which texels have any.</summary>
		private readonly float[] stars = new float[W * Half];
		private readonly List<int> lit = new List<int>();
		private static readonly float[] Dither = { 0f, 8f, 2f, 10f, 12f, 4f, 14f, 6f, 3f, 11f, 1f, 9f, 15f, 7f, 13f, 5f };

		// What the picture under way is painted with, worked out once at its start.
		private State s;
		private Rgb zenith, horizon, glow, sunColour, moonColour, cloudLit, cloudShadow;
		private float sunVis, moonVis, starVis, cloudMax, threshold, softness;
		private float sunInner, sunOuter, moonCos, haloCos, invMoonSin;
		private float ax, ay, az, bx, by, bz;

		// The storms for the picture under way: their colours, how far the haze reaches, and per column the rain curtains' cover and haze
		// after each step out (the rain falls straight down, so a column's curtains are the same at every height under the cloud).
		private bool storming, raining;
		private Rgb stormBase, rainColour;
		private float haze;
		private readonly float[] curtainA = new float[W * CurtainSteps];
		private readonly float[] curtainH = new float[W * CurtainSteps];
		private readonly float[] streaks = new float[W];

		// The land past the far view's edge for the picture under way, and its fog colour in the colour space it is lit in.
		private Skyline land;
		private LandLight light;
		private Rgb fogLit;

		/// <summary>The sRGB curve taken off a byte (256 values), and put back on a linear value (<see cref="Curve"/> + 1 steps from 0 to 1).</summary>
		private static readonly float[] ToLinear = new float[256];
		private const int Curve = 4095;
		private static readonly float[] ToGamma = new float[Curve + 1];

		static SkyPaint()
		{
			for (int i = 0; i < 256; i++)
			{
				ToLinear[i] = (float)Linear(i / 255.0);
			}
			for (int i = 0; i <= Curve; i++)
			{
				double v = i / (double)Curve;
				ToGamma[i] = (float)(v <= 0.0031308 ? v * 12.92 : 1.055 * Math.Pow(v, 1.0 / 2.4) - 0.055);
			}
		}

		private static double Linear(double v)
		{
			return v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
		}

		internal SkyPaint(int seed)
		{
			for (int row = 0; row < H; row++)
			{
				// The panoramic shader reads v = 1 - acos(y) / pi, so a row is a height.
				double v = (row + 0.5) / H;
				double y = Math.Cos((1.0 - v) * Math.PI);
				rowY[row] = (float)y;
				rowR[row] = (float)Math.Sqrt(Math.Max(0.0, 1.0 - y * y));
			}
			for (int col = 0; col < W; col++)
			{
				// ...and u = 0.5 - atan2(z, x) / 2 pi.
				double phi = (0.5 - (col + 0.5) / W) * 2.0 * Math.PI;
				cosPhi[col] = (float)Math.Cos(phi);
				sinPhi[col] = (float)Math.Sin(phi);
			}
			ulong salt = Noise.Mix((ulong)(uint)seed ^ 0x5C1E_5ADEUL);
			Clouds(salt);
			Stars(Noise.Mix(salt ^ 0x57A2UL));
		}

		/// <summary>The highest the sun gets, in degrees (at noon).</summary>
		internal const float NoonElevation = 55f;

		/// <summary>
		/// Where the sun, the moon and the stars are at <paramref name="hour"/>: the sun rises in the east at 6, is south at noon and sets in
		/// the west at 18, at most <see cref="NoonElevation"/> up; the moon goes round opposite it, a little lower (as its light does in
		/// <see cref="Sky"/>); the stars turn 15 degrees an hour. <paramref name="turn"/> is the sun's way round, in degrees from rising.
		/// </summary>
		internal static void Place(ref State st, float hour, out float turn)
		{
			turn = (hour - 6f) / 24f * 360f;
			float elevation = NoonElevation * (float)Math.Sin(turn * Math.PI / 180.0);
			st.SunElevation = elevation;
			Dir(-turn, elevation, out st.SunX, out st.SunY, out st.SunZ);
			st.MoonElevation = -elevation * 0.8f;
			Dir(-turn + 180f, st.MoonElevation, out st.MoonX, out st.MoonY, out st.MoonZ);
			st.StarTurn = hour * 15f;
		}

		/// <summary>
		/// The unit vector toward a body at <paramref name="azimuth"/> and <paramref name="elevation"/> (degrees): the vector the sun's light
		/// is turned to in <see cref="Sky"/>, <c>Quaternion.Euler(0, -azimuth, 0) * (Quaternion.Euler(0, 0, elevation) * Vector3.right)</c>,
		/// written out. Azimuth 0 is east, -90 south.
		/// </summary>
		internal static void Dir(float azimuth, float elevation, out float x, out float y, out float z)
		{
			double a = azimuth * (Math.PI / 180.0), e = elevation * (Math.PI / 180.0);
			double ce = Math.Cos(e);
			x = (float)(ce * Math.Cos(a));
			y = (float)Math.Sin(e);
			z = (float)(ce * Math.Sin(a));
		}

		/// <summary>
		/// The sky's colours for a state: at the top of the sky, at the horizon (which <see cref="Sky"/> also makes the fog, so the world
		/// fades into the sky), and of the glow round the sun. They follow the sun's height through keyed colours, take a little of the
		/// race's haze, lift under the moon and go grey under cloud.
		/// </summary>
		internal static void Palette(in State st, out Rgb top, out Rgb bottom, out Rgb sunGlow)
		{
			float e = st.SunElevation;
			int k = 0;
			while (k < Keys.Length - 2 && e > Keys[k + 1])
			{
				k++;
			}
			float t = Step(Keys[k], Keys[k + 1], e);
			top = Rgb.Lerp(Zeniths[k], Zeniths[k + 1], t);
			bottom = Rgb.Lerp(Horizons[k], Horizons[k + 1], t);
			sunGlow = Rgb.Lerp(Glows[k], Glows[k + 1], t);

			float daylight = Step(-6f, 10f, e);
			bottom = Rgb.Lerp(bottom, Rgb.Lerp(st.NightHaze, st.DayHaze, daylight), 0.22f);
			top = Rgb.Lerp(top, st.DayHaze, 0.08f * daylight);

			float moonUp = Step(0f, 10f, st.MoonElevation) * (1f - daylight);
			top += new Rgb(0.010f, 0.016f, 0.035f) * moonUp;
			bottom += new Rgb(0.020f, 0.026f, 0.045f) * moonUp;

			float c = st.Cloud;
			float grey = bottom.Luma * (1f - 0.45f * c);
			bottom = Rgb.Lerp(bottom, new Rgb(grey, grey, grey * 1.04f), 0.75f * c);
			top = Rgb.Lerp(top, new Rgb(grey, grey, grey * 1.04f) * 0.85f, 0.85f * c);
			sunGlow *= 1f - 0.85f * c;
		}

		// The sky through the day, keyed by the sun's height in degrees: night, dusk, the sun on the horizon, sunset, the morning, day.
		private static readonly float[] Keys = { -18f, -8f, -2f, 4f, 12f, 30f };
		private static readonly Rgb[] Zeniths =
		{
			new Rgb(0.008f, 0.010f, 0.028f), new Rgb(0.035f, 0.045f, 0.110f), new Rgb(0.110f, 0.150f, 0.320f),
			new Rgb(0.200f, 0.320f, 0.600f), new Rgb(0.220f, 0.420f, 0.780f), new Rgb(0.180f, 0.400f, 0.800f),
		};
		private static readonly Rgb[] Horizons =
		{
			new Rgb(0.020f, 0.024f, 0.050f), new Rgb(0.110f, 0.100f, 0.180f), new Rgb(0.560f, 0.340f, 0.290f),
			new Rgb(0.900f, 0.600f, 0.380f), new Rgb(0.720f, 0.800f, 0.900f), new Rgb(0.680f, 0.800f, 0.930f),
		};
		private static readonly Rgb[] Glows =
		{
			new Rgb(0f, 0f, 0f), new Rgb(0.220f, 0.100f, 0.090f), new Rgb(0.850f, 0.360f, 0.150f),
			new Rgb(1.000f, 0.550f, 0.200f), new Rgb(0.950f, 0.800f, 0.550f), new Rgb(0.850f, 0.850f, 0.780f),
		};

		/// <summary>Paints a whole picture with <paramref name="state"/> into <see cref="Pixels"/>. No Unity calls, so <see cref="Sky"/> runs it
		/// on a worker; its <see cref="State.Land"/> and <see cref="State.Storm"/> must not change until it returns.</summary>
		internal void All(in State state)
		{
			Begin(state);
			for (int row = 0; row < H; row++)
			{
				Row(row);
			}
		}

		private void Begin(in State state)
		{
			s = state;
			Palette(s, out zenith, out horizon, out glow);
			float e = s.SunElevation;
			float daylight = Step(-6f, 10f, e);
			float sunset = Step(-2f, 6f, e) * (1f - Step(2f, 20f, e));
			float c = s.Cloud;

			// The disc is the brightest thing in the sky: white by day, orange low down, so it shows inside its glow.
			sunColour = Rgb.Lerp(new Rgb(1f, 1f, 0.96f), new Rgb(1f, 0.62f, 0.30f), sunset * sunset);
			sunVis = 1f - 0.9f * c;
			sunInner = (float)Math.Cos((SunRadius - 0.35f) * Math.PI / 180.0);
			sunOuter = (float)Math.Cos(SunRadius * Math.PI / 180.0);

			moonVis = Step(-1f, 3f, s.MoonElevation) * (1f - 0.6f * daylight) * (1f - 0.9f * c);
			moonColour = new Rgb(0.86f, 0.88f, 0.93f);
			moonCos = (float)Math.Cos(MoonRadius * Math.PI / 180.0);
			haloCos = (float)Math.Cos(14.0 * Math.PI / 180.0);
			invMoonSin = 1f / (float)Math.Sin(MoonRadius * Math.PI / 180.0);
			// The moon's face: across it (a, level) and up it (b), so its seas keep their places as it climbs.
			float mx = s.MoonX, my = s.MoonY, mz = s.MoonZ;
			float len = (float)Math.Sqrt(mx * mx + mz * mz);
			if (len < 1e-4f)
			{
				ax = 1f;
				ay = 0f;
				az = 0f;
			}
			else
			{
				ax = -mz / len;
				ay = 0f;
				az = mx / len;
			}
			bx = my * az - mz * ay;
			by = mz * ax - mx * az;
			bz = mx * ay - my * ax;

			starVis = (1f - Step(-14f, -4f, e)) * (1f - c) * (1f - c) * (1f - 0.35f * Step(0f, 15f, s.MoonElevation));

			cloudLit = Rgb.Lerp(new Rgb(0.080f, 0.088f, 0.115f), new Rgb(0.970f, 0.960f, 0.940f), daylight);
			cloudLit += new Rgb(0.050f, 0.055f, 0.070f) * (Step(0f, 10f, s.MoonElevation) * (1f - daylight));
			cloudLit = Rgb.Lerp(cloudLit, glow * 1.1f, sunset * 0.55f);
			cloudShadow = Rgb.Lerp(cloudLit * 0.55f, zenith, 0.2f);
			cloudLit *= 1f - 0.5f * c;
			cloudShadow *= 1f - 0.6f * c;
			cloudMax = 0.95f;
			float coverage = Math.Max(0f, Math.Min(1f, s.Coverage));
			threshold = cloudQuantile[(int)Math.Round((1f - coverage) * 100f)];
			softness = 0.06f;
			if (coverage < 0.005f)
			{
				cloudMax = 0f;
			}

			PlaceStars();
			Storming();

			land = s.Light.FogEnd > s.Light.FogStart ? s.Land : null;
			light = s.Light;
			fogLit = FogColour(light, horizon);
		}

		private static float Lin(float v)
		{
			return (float)Linear(Math.Max(0.0, Math.Min(1.0, v)));
		}

		/// <summary>
		/// One texel of land as the far view's material shows it at distance <paramref name="d"/>: its picture's colour
		/// (<paramref name="r"/>, <paramref name="g"/>, <paramref name="b"/>) lit by the ambient light from the way its normal faces and the
		/// sun on it (the sea past the world also shining), then fogged; out in sRGB, 0 to 1, with the fog's share. False when it is all
		/// fog. <paramref name="fogColour"/> is the horizon's colour in the colour space the lighting is done in. <c>tools/farview</c> lights
		/// its reference views with it.
		/// </summary>
		internal static bool Shade(in LandLight light, Rgb fogColour, byte r, byte g, byte b, bool sea, float nx, float ny, float nz, float d,
			out float lr, out float lg, out float lb, out float fog)
		{
			fog = Math.Max(0f, Math.Min(1f, (d - light.FogStart) / (light.FogEnd - light.FogStart)));
			if (fog >= 0.999f)
			{
				lr = lg = lb = 0f;
				return false;
			}
			// The ambient light from the way the slope faces, as an ambient cube.
			Rgb xs = nx > 0f ? light.East : light.West, ys = ny > 0f ? light.Up : light.Down, zs = nz > 0f ? light.North : light.South;
			float wx = nx * nx, wy = ny * ny, wz = nz * nz;
			float lambert = Math.Max(0f, nx * light.SunX + ny * light.SunY + nz * light.SunZ);
			// URP Lit with no metal: the diffuse is 0.96 of the albedo.
			float tint = light.Tint * 0.96f;
			float er = (xs.R * wx + ys.R * wy + zs.R * wz + light.Sun.R * lambert) * tint;
			float eg = (xs.G * wx + ys.G * wy + zs.G * wz + light.Sun.G * lambert) * tint;
			float eb = (xs.B * wx + ys.B * wy + zs.B * wz + light.Sun.B * lambert) * tint;
			bool linear = light.Linear;
			lr = (linear ? ToLinear[r] : r / 255f) * er;
			lg = (linear ? ToLinear[g] : g / 255f) * eg;
			lb = (linear ? ToLinear[b] : b / 255f) * eb;
			if (sea)
			{
				// The sea sheet's gloss (smoothness 0.7) seen at a glance: about three quarters of the environment, the sky at the horizon.
				float shine = 0.74f * light.Reflect;
				lr += fogColour.R * shine;
				lg += fogColour.G * shine;
				lb += fogColour.B * shine;
			}
			lr += (fogColour.R - lr) * fog;
			lg += (fogColour.G - lg) * fog;
			lb += (fogColour.B - lb) * fog;
			if (linear)
			{
				lr = ToGamma[(int)(Math.Max(0f, Math.Min(1f, lr)) * Curve)];
				lg = ToGamma[(int)(Math.Max(0f, Math.Min(1f, lg)) * Curve)];
				lb = ToGamma[(int)(Math.Max(0f, Math.Min(1f, lb)) * Curve)];
			}
			return true;
		}

		/// <summary>The horizon's colour for <see cref="Shade"/>: in linear colour if the light is.</summary>
		internal static Rgb FogColour(in LandLight light, Rgb horizon)
		{
			return light.Linear ? new Rgb(Lin(horizon.R), Lin(horizon.G), Lin(horizon.B)) : horizon;
		}

		/// <summary>
		/// The land past the far view's edge over one row of the picture (in <see cref="Skyline"/>'s band): each texel with land in it lit as
		/// the far view's material lights it (its picture's colour, the ambient light from the way its slope faces, the sun on it; the sea
		/// past the world shining as the far view's sea sheet does, at a glance a reflection of the sky at the horizon), fogged by its
		/// distance as Unity's linear fog fogs the meshes (<see cref="Shade"/>), behind the rain curtains between it and the car. Land so far
		/// into the fog that it is nearly all fog fades into the sky behind it, as the meshes stop at the cameras' reach and the sky shows
		/// past them.
		/// </summary>
		private void PaintLand(int row)
		{
			int k = row - Skyline.Lowest;
			if (land == null || k < 0 || k >= Skyline.Rows)
			{
				return;
			}
			float[] dist = land.Dist, normal = land.Normal;
			byte[] colour = land.Colour;
			int o = row * W * 4;
			int li = k * W;
			for (int col = 0; col < W; col++, o += 4, li++)
			{
				float d = dist[li];
				if (d <= 0f)
				{
					continue;
				}
				int c = li * 4;
				if (!Shade(light, fogLit, colour[c], colour[c + 1], colour[c + 2], colour[c + 3] == 0, normal[li * 3], normal[li * 3 + 1], normal[li * 3 + 2], d,
					out float lr, out float lg, out float lb, out float fog))
				{
					continue;
				}
				if (raining)
				{
					// The rain curtains between here and the land, in front of it as they are in front of the sky behind it.
					int step = Math.Min(CurtainSteps - 1, (int)((d - CurtainFrom) / CurtainStep));
					float rain = step >= 0 ? curtainA[col * CurtainSteps + step] : 0f;
					if (rain > 0.003f)
					{
						float h = curtainH[col * CurtainSteps + step] / rain;
						rain *= streaks[col];
						lr += (rainColour.R + (horizon.R - rainColour.R) * h - lr) * rain;
						lg += (rainColour.G + (horizon.G - rainColour.G) * h - lg) * rain;
						lb += (rainColour.B + (horizon.B - rainColour.B) * h - lb) * rain;
					}
				}
				// Nearly all fog: fades into the sky already painted there.
				float a = 1f - Step(0.9f, 0.999f, fog);
				if (a < 1f)
				{
					lr = Pixels[o] / 255f + (lr - Pixels[o] / 255f) * a;
					lg = Pixels[o + 1] / 255f + (lg - Pixels[o + 1] / 255f) * a;
					lb = Pixels[o + 2] / 255f + (lb - Pixels[o + 2] / 255f) * a;
				}
				float dither = (Dither[(row & 3) * 4 + (col & 3)] + 0.5f) / 16f;
				Pixels[o] = Byte(lr, dither);
				Pixels[o + 1] = Byte(lg, dither);
				Pixels[o + 2] = Byte(lb, dither);
			}
		}

		/// <summary>
		/// Gets the storms ready for the picture: their colours (the cloud's shadow side, darker, and a grey rain toward the horizon's
		/// colour), and the rain curtains along every column: stepping out from the car, the share of the sky hidden behind the rain so
		/// far, and how much of that is hazed by distance. Out to <see cref="CurtainSteps"/> steps, about 30,000 lookups a picture.
		/// </summary>
		private void Storming()
		{
			StormField f = s.Storm;
			storming = f != null && f.MaxCloud > 0.05f;
			raining = f != null && f.MaxRain > 0.02f;
			haze = Math.Max(s.Reach, 6000f) * 3.5f;
			stormBase = Rgb.Lerp(cloudShadow * 0.6f, horizon * 0.5f, 0.3f);
			rainColour = Rgb.Lerp(cloudShadow * 0.75f, horizon, 0.45f);
			if (!raining)
			{
				return;
			}
			for (int col = 0; col < W; col++)
			{
				float dx = cosPhi[col], dz = sinPhi[col];
				float transmit = 1f, hazed = 0f;
				for (int k = 0; k < CurtainSteps; k++)
				{
					float d = CurtainFrom + (k + 0.5f) * CurtainStep;
					float rain = f.RainAt(s.CarX + dx * d, s.CarZ + dz * d);
					if (rain > 0f)
					{
						float a = 1f - (float)Math.Exp(-CurtainDensity * rain * CurtainStep);
						hazed += transmit * a * Step(0f, haze, d);
						transmit *= 1f - a;
					}
					curtainA[col * CurtainSteps + k] = 1f - transmit;
					curtainH[col * CurtainSteps + k] = hazed;
				}
				// Streaks: the rain is heavier in some columns than others, a few texels wide.
				float u = col / 3f;
				int i0 = (int)Math.Floor(u);
				float t = u - i0;
				double v0 = Lattice(0x5717EAUL, 0, i0, 0), v1 = Lattice(0x5717EAUL, 0, i0 + 1, 0);
				streaks[col] = 0.7f + 0.3f * (float)(v0 + (v1 - v0) * t);
			}
		}

		/// <summary>One row of the upper half: the gradient, the sun's glow, the stars, the discs and the cloud over them (a row of the lower
		/// half in the skyline's band: <see cref="Under"/>).</summary>
		private void Row(int row)
		{
			if (row < Half)
			{
				Under(row);
				return;
			}
			float y = rowY[row], r = rowR[row];
			// Up from the horizon colour (exactly it on the lowest row, which the fog meets) toward the top of the sky's, quickly at first.
			float f = (float)Math.Sqrt(Math.Max(0f, y - 0.005f));
			Rgb b = Rgb.Lerp(horizon, zenith, f);
			float hw = 1f - 0.6f * f;
			float fade = Step(0.03f, 0.22f, y) * cloudMax;
			float kk = CloudHeight / (y + 0.08f) * (CloudN / CloudTile);
			float ou = s.CloudX * (CloudN / CloudTile), ov = s.CloudZ * (CloudN / CloudTile);
			// Where a way up at this height meets the cloud layer, per unit of the way across (in world units); the storms fade out into the
			// horizon's last rows, which the fog meets, and the curtains are only seen low down, under the cloud.
			float kc = CloudHeight / (y + 0.08f);
			float stormFade = storming ? Step(0.004f, 0.05f, y) : 0f;
			// The storm field's sample coordinates of the car, and how far they move per unit of x or z in this row.
			float fu0 = 0f, fv0 = 0f, fk = 0f;
			if (stormFade > 0f)
			{
				fu0 = (float)((s.CarX - s.Storm.X0) / s.Storm.Spacing);
				fv0 = (float)((s.CarZ - s.Storm.Z0) / s.Storm.Spacing);
				fk = (float)(kc / s.Storm.Spacing);
			}
			float curtainFade = raining && y < 0.4f ? Step(0.004f, 0.03f, y) : 0f;
			float sx = s.SunX, sy = s.SunY, sz = s.SunZ, mx = s.MoonX, my = s.MoonY, mz = s.MoonZ;
			bool glowing = glow.R + glow.G + glow.B > 0.003f;
			int o = row * W * 4;
			int so = (row - Half) * W;
			for (int col = 0; col < W; col++)
			{
				float x = cosPhi[col] * r, z = sinPhi[col] * r;
				float cr = b.R, cg = b.G, cb = b.B;
				float ds = x * sx + y * sy + z * sz;
				float d8 = 0f;
				if (ds > 0f && glowing)
				{
					float d2 = ds * ds, d4 = d2 * d2;
					d8 = d4 * d4;
					float d16 = d8 * d8, d32 = d16 * d16, d64 = d32 * d32;
					float g = (0.20f * d2 + 0.30f * d8) * hw + 0.25f * d64;
					cr += glow.R * g;
					cg += glow.G * g;
					cb += glow.B * g;
				}
				float star = stars[so + col];
				if (star > 0f)
				{
					cr += star * 0.92f;
					cg += star * 0.95f;
					cb += star;
				}
				if (ds > sunOuter && sunVis > 0f)
				{
					float a = Math.Min(1f, (ds - sunOuter) / (sunInner - sunOuter)) * sunVis;
					cr += (sunColour.R - cr) * a;
					cg += (sunColour.G - cg) * a;
					cb += (sunColour.B - cb) * a;
				}
				if (moonVis > 0f)
				{
					float dm = x * mx + y * my + z * mz;
					if (dm > haloCos)
					{
						float h = (dm - haloCos) / (1f - haloCos);
						h *= h;
						h *= h * 0.10f * moonVis;
						cr += 0.75f * h;
						cg += 0.80f * h;
						cb += 0.95f * h;
						if (dm > moonCos)
						{
							Moon(x, y, z, ref cr, ref cg, ref cb);
						}
					}
				}
				float n = fade > 0f || storming ? CloudAt(x * kk + ou, z * kk + ov) : 0f;
				if (fade > 0f)
				{
					if (n > threshold)
					{
						float a = Math.Min(1f, (n - threshold) / softness) * fade;
						float dense = Math.Min(1f, (n - threshold) * 3f);
						float lr = cloudLit.R + (cloudShadow.R - cloudLit.R) * dense + glow.R * d8 * 0.35f;
						float lg = cloudLit.G + (cloudShadow.G - cloudLit.G) * dense + glow.G * d8 * 0.35f;
						float lb = cloudLit.B + (cloudShadow.B - cloudLit.B) * dense + glow.B * d8 * 0.35f;
						cr += (lr - cr) * a;
						cg += (lg - cg) * a;
						cb += (lb - cb) * a;
					}
				}
				if (stormFade > 0f)
				{
					// The storm's cloud where this way meets the cloud layer: its edge broken by the fair-weather pattern and a finer one, so it
					// builds out of lumps of cloud; dark in its middle, grey and lit toward its edge; hazed by distance.
					float sc = FieldCloud(fu0 + x * fk, fv0 + z * fk);
					if (sc > 0.05f)
					{
						float n2 = CloudAt(x * kk * 3.1f + ou + 37f, z * kk * 3.1f + ov + 91f);
						float m = sc * 1.3f + (n - 0.5f) * 2.2f + (n2 - 0.5f) * 1.4f;
						float a = Step(0.3f, 0.7f, m) * 0.97f * stormFade;
						if (a > 0f)
						{
							float core = Step(0.4f, 1.1f, sc + (n - 0.5f) * 1.2f + (n2 - 0.5f) * 1.0f) * (0.8f + 0.4f * n2);
							float h = Step(0f, haze, r * kc);
							float lr = cloudLit.R * 0.85f + (stormBase.R - cloudLit.R * 0.85f) * core;
							float lg = cloudLit.G * 0.85f + (stormBase.G - cloudLit.G * 0.85f) * core;
							float lb = cloudLit.B * 0.85f + (stormBase.B - cloudLit.B * 0.85f) * core;
							cr += (lr + (horizon.R - lr) * h - cr) * a;
							cg += (lg + (horizon.G - lg) * h - cg) * a;
							cb += (lb + (horizon.B - lb) * h - cb) * a;
						}
					}
				}
				if (curtainFade > 0f)
				{
					// The rain hanging under the cloud between here and where this way meets the cloud layer, in front of all of the above.
					float steps = (r * kc - CurtainFrom) / CurtainStep;
					if (steps >= 0f)
					{
						int k = Math.Min(CurtainSteps - 1, (int)steps);
						float a = curtainA[col * CurtainSteps + k];
						if (a > 0.003f)
						{
							float h = curtainH[col * CurtainSteps + k] / a;
							float lr = rainColour.R + (horizon.R - rainColour.R) * h;
							float lg = rainColour.G + (horizon.G - rainColour.G) * h;
							float lb = rainColour.B + (horizon.B - rainColour.B) * h;
							a *= streaks[col] * curtainFade;
							cr += (lr - cr) * a;
							cg += (lg - cg) * a;
							cb += (lb - cb) * a;
						}
					}
				}
				float d = (Dither[(row & 3) * 4 + (col & 3)] + 0.5f) / 16f;
				Pixels[o] = Byte(cr, d);
				Pixels[o + 1] = Byte(cg, d);
				Pixels[o + 2] = Byte(cb, d);
				Pixels[o + 3] = 255;
				o += 4;
			}
			PaintLand(row);
		}

		/// <summary>The storm field's cloud at sample coordinates (u, v), read between samples; 0 outside it. <see cref="StormField.CloudAt"/>
		/// in floats on coordinates worked out a row at a time, as it is read for every texel of the sky.</summary>
		private float FieldCloud(float u, float v)
		{
			StormField f = s.Storm;
			int n = f.N;
			if (u < 0f || v < 0f || u >= n - 1 || v >= n - 1)
			{
				return 0f;
			}
			int i = (int)u, j = (int)v;
			float fu = u - i, fv = v - j;
			int k = j * n + i;
			float[] c = f.Cloud;
			float a = c[k], b = c[k + 1], cc = c[k + n], d = c[k + n + 1];
			float bottom = a + (b - a) * fu;
			return bottom + (cc + (d - cc) * fu - bottom) * fv;
		}

		/// <summary>The moon's disc at a direction inside it: grey, darker toward its edge, with three seas.</summary>
		private void Moon(float x, float y, float z, ref float cr, ref float cg, ref float cb)
		{
			float u = (x * ax + y * ay + z * az) * invMoonSin;
			float v = (x * bx + y * by + z * bz) * invMoonSin;
			float rr = u * u + v * v;
			if (rr >= 1f)
			{
				return;
			}
			float shade = 0.72f + 0.28f * (float)Math.Sqrt(1f - rr);
			if (Sq(u + 0.30f) + Sq(v - 0.28f) < 0.12f || Sq(u - 0.22f) + Sq(v - 0.12f) < 0.08f || Sq(u + 0.02f) + Sq(v + 0.38f) < 0.06f)
			{
				shade *= 0.80f;
			}
			float a = Math.Min(1f, (1f - rr) * 6f) * moonVis;
			cr += (moonColour.R * shade - cr) * a;
			cg += (moonColour.G * shade - cg) * a;
			cb += (moonColour.B * shade - cb) * a;
		}

		/// <summary>A row of the lower half: the horizon's colour (darker straight down), and the land past the far view in it.</summary>
		private void Under(int row)
		{
			float k = 1f - 0.25f * Step(0f, 0.6f, -rowY[row]);
			int o = row * W * 4;
			for (int col = 0; col < 4; col++)
			{
				float d = (Dither[(row & 3) * 4 + col] + 0.5f) / 16f;
				Pixels[o + col * 4] = Byte(horizon.R * k, d);
				Pixels[o + col * 4 + 1] = Byte(horizon.G * k, d);
				Pixels[o + col * 4 + 2] = Byte(horizon.B * k, d);
				Pixels[o + col * 4 + 3] = 255;
			}
			for (int col = 4; col < W; col += 4)
			{
				Buffer.BlockCopy(Pixels, o, Pixels, o + col * 4, 16);
			}
			PaintLand(row);
		}

		/// <summary>The stars' positions for the picture under way: each turned about the pole, as one texel (bright ones two), made wider
		/// toward the top of the sky, where a texel of the picture is narrower.</summary>
		private void PlaceStars()
		{
			foreach (int i in lit)
			{
				stars[i] = 0f;
			}
			lit.Clear();
			if (starVis < 0.01f)
			{
				return;
			}
			double lat = Latitude * Math.PI / 180.0;
			double kx = 0.0, ky = Math.Sin(lat), kz = Math.Cos(lat);
			double turn = s.StarTurn * Math.PI / 180.0;
			double c = Math.Cos(turn), sn = Math.Sin(turn);
			for (int i = 0; i < StarCount; i++)
			{
				double vx = starX[i], vy = starY[i], vz = starZ[i];
				// Rodrigues: v c + (k x v) s + k (k . v)(1 - c).
				double kv = kx * vx + ky * vy + kz * vz;
				double cx = ky * vz - kz * vy, cy = kz * vx - kx * vz, cz = kx * vy - ky * vx;
				double x = vx * c + cx * sn + kx * kv * (1.0 - c);
				double y = vy * c + cy * sn + ky * kv * (1.0 - c);
				double z = vz * c + cz * sn + kz * kv * (1.0 - c);
				if (y < 0.03)
				{
					continue;
				}
				float light = starMag[i] * starVis * Step(0.03f, 0.3f, (float)y);
				if (light < 0.015f)
				{
					continue;
				}
				int col = (int)Math.Floor((0.5 - Math.Atan2(z, x) / (2.0 * Math.PI)) * W);
				int row = (int)Math.Floor((1.0 - Math.Acos(Math.Min(1.0, y)) / Math.PI) * H);
				row = Math.Max(Half, Math.Min(H - 1, row));
				bool bright = starMag[i] > 0.55f;
				double across = Math.Sqrt(Math.Max(1e-4, 1.0 - y * y));
				int w = Math.Max(1, Math.Min(12, (int)Math.Round((bright ? 2.0 : 1.0) / across)));
				int h = bright ? 2 : 1;
				for (int j = 0; j < h && row + j < H; j++)
				{
					for (int q = 0; q < w; q++)
					{
						int idx = (row + j - Half) * W + (((col + q) % W) + W) % W;
						if (stars[idx] == 0f)
						{
							lit.Add(idx);
						}
						stars[idx] = Math.Max(stars[idx], light);
					}
				}
			}
		}

		/// <summary>The star field: directions spread evenly over the sphere, most stars faint, a few bright.</summary>
		private void Stars(ulong salt)
		{
			for (int i = 0; i < StarCount; i++)
			{
				double u = U(salt, i, 0), v = U(salt, i, 1), m = U(salt, i, 2);
				double y = 2.0 * u - 1.0;
				double r = Math.Sqrt(1.0 - y * y);
				double a = 2.0 * Math.PI * v;
				starX[i] = (float)(r * Math.Cos(a));
				starY[i] = (float)y;
				starZ[i] = (float)(r * Math.Sin(a));
				starMag[i] = (float)(0.10 + 0.90 * Math.Pow(m, 7.0));
			}
		}

		/// <summary>The cloud pattern: tiling value noise in five octaves, and its quantiles, so a coverage is a share of the sky.</summary>
		private void Clouds(ulong salt)
		{
			int[] periods = { 4, 8, 16, 32, 64 };
			float norm = 0f;
			float amp = 1f;
			foreach (int _ in periods)
			{
				norm += amp;
				amp *= 0.5f;
			}
			for (int j = 0; j < CloudN; j++)
			{
				for (int i = 0; i < CloudN; i++)
				{
					float sum = 0f;
					amp = 1f;
					for (int o = 0; o < periods.Length; o++)
					{
						int p = periods[o];
						double fx = (double)i * p / CloudN, fz = (double)j * p / CloudN;
						int ix = (int)Math.Floor(fx), iz = (int)Math.Floor(fz);
						double tx = Noise.Fade(fx - ix), tz = Noise.Fade(fz - iz);
						double a = Lattice(salt, o, ix % p, iz % p), b = Lattice(salt, o, (ix + 1) % p, iz % p);
						double c = Lattice(salt, o, ix % p, (iz + 1) % p), d = Lattice(salt, o, (ix + 1) % p, (iz + 1) % p);
						double top = a + (b - a) * tx, bottom = c + (d - c) * tx;
						sum += (float)(top + (bottom - top) * tz) * amp;
						amp *= 0.5f;
					}
					cloudNoise[j * CloudN + i] = sum / norm;
				}
			}
			float[] sorted = (float[])cloudNoise.Clone();
			Array.Sort(sorted);
			for (int q = 0; q <= 100; q++)
			{
				cloudQuantile[q] = sorted[Math.Min(sorted.Length - 1, q * (sorted.Length - 1) / 100)];
			}
			// Fully covered must cover: below the lowest value.
			cloudQuantile[0] -= 1f;
		}

		/// <summary>The cloud pattern at a point in samples, read between its samples, repeating.</summary>
		private float CloudAt(float u, float v)
		{
			float fu = (float)Math.Floor(u), fv = (float)Math.Floor(v);
			float tu = u - fu, tv = v - fv;
			int i = (int)fu & (CloudN - 1), j = (int)fv & (CloudN - 1);
			int i1 = (i + 1) & (CloudN - 1), j1 = (j + 1) & (CloudN - 1);
			float a = cloudNoise[j * CloudN + i], b = cloudNoise[j * CloudN + i1];
			float c = cloudNoise[j1 * CloudN + i], d = cloudNoise[j1 * CloudN + i1];
			float top = a + (b - a) * tu, bottom = c + (d - c) * tu;
			return top + (bottom - top) * tv;
		}

		private static double Lattice(ulong salt, int octave, int ix, int iz)
		{
			unchecked
			{
				ulong h = Noise.Mix(salt ^ Noise.Mix((ulong)octave * 0x9E3779B97F4A7C15UL ^ Noise.Mix((ulong)(ix * 4096 + iz))));
				return (double)(h >> 11) * (1.0 / 9007199254740992.0);
			}
		}

		private static double U(ulong salt, int i, int j)
		{
			unchecked
			{
				ulong h = Noise.Mix(salt ^ Noise.Mix((ulong)(i * 8 + j)));
				return (double)(h >> 11) * (1.0 / 9007199254740992.0);
			}
		}

		private static byte Byte(float v, float dither)
		{
			float b = v * 255f + dither;
			return b <= 0f ? (byte)0 : b >= 255f ? (byte)255 : (byte)b;
		}

		private static float Sq(float v)
		{
			return v * v;
		}

		/// <summary>0 at or below <paramref name="a"/>, 1 at or beyond <paramref name="b"/> (which may be below a), smooth in between.</summary>
		private static float Step(float a, float b, float x)
		{
			float u = Math.Max(0f, Math.Min(1f, (x - a) / (b - a)));
			return u * u * (3f - 2f * u);
		}
	}
}
