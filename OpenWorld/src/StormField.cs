using System;

namespace HalfGripOpenWorld
{
	/// <summary>
	/// The storms over a wide square round the car at one moment, on a coarse grid: how overcast and how hard it rains at each point
	/// (<see cref="Storms.Fill"/>). The painted sky (<see cref="SkyPaint"/>) looks storms up here, thousands of times a picture, to draw
	/// them where they are: their dark cloud overhead and toward the horizon, and the rain hanging under it in curtains. One
	/// <see cref="Storms.At"/> costs about 8 µs, far too much for every texel; a lookup here is a few nanoseconds.
	///
	/// Filled on a worker thread and then handed over whole; nothing writes to one once it is handed over, so the painter can read it while
	/// the next is filled. No Unity types (<c>tools/skyview</c> builds one by hand).
	/// </summary>
	internal sealed class StormField
	{
		/// <summary>Samples along a side.</summary>
		internal readonly int N;

		/// <summary>Units between samples.</summary>
		internal readonly double Spacing;

		/// <summary>World x and z of the first sample (the south-west corner).</summary>
		internal readonly double X0, Z0;

		/// <summary>Per sample, row by row from the south: the cloud (0 clear, 1 a black sky) and the rain (0 dry, 1 a downpour).</summary>
		internal readonly float[] Cloud, Rain;

		/// <summary>The most cloud and rain anywhere in the field: 0 lets the painter skip the lookups.</summary>
		internal float MaxCloud, MaxRain;

		/// <summary>An empty field of <paramref name="n"/> samples a side, <paramref name="spacing"/> apart, centred on world (cx, cz).</summary>
		internal StormField(int n, double spacing, double cx, double cz)
		{
			N = n;
			Spacing = spacing;
			X0 = cx - (n - 1) * 0.5 * spacing;
			Z0 = cz - (n - 1) * 0.5 * spacing;
			Cloud = new float[n * n];
			Rain = new float[n * n];
		}

		/// <summary>Half the side, from the middle to an edge.</summary>
		internal double Reach => (N - 1) * 0.5 * Spacing;

		/// <summary>The cloud at world (x, z), read between the samples; 0 outside the field.</summary>
		internal float CloudAt(double x, double z)
		{
			return Read(Cloud, x, z);
		}

		/// <summary>The rain at world (x, z), read between the samples; 0 outside the field.</summary>
		internal float RainAt(double x, double z)
		{
			return Read(Rain, x, z);
		}

		private float Read(float[] values, double x, double z)
		{
			double u = (x - X0) / Spacing, v = (z - Z0) / Spacing;
			if (u < 0.0 || v < 0.0 || u >= N - 1 || v >= N - 1)
			{
				return 0f;
			}
			int i = (int)u, j = (int)v;
			float fu = (float)(u - i), fv = (float)(v - j);
			int k = j * N + i;
			float a = values[k], b = values[k + 1], c = values[k + N], d = values[k + N + 1];
			float bottom = a + (b - a) * fu, top = c + (d - c) * fu;
			return bottom + (top - bottom) * fv;
		}

		/// <summary>Works out <see cref="MaxCloud"/> and <see cref="MaxRain"/> once the field is filled.</summary>
		internal void Measure()
		{
			float cloud = 0f, rain = 0f;
			for (int k = 0; k < Cloud.Length; k++)
			{
				cloud = Math.Max(cloud, Cloud[k]);
				rain = Math.Max(rain, Rain[k]);
			}
			MaxCloud = cloud;
			MaxRain = rain;
		}
	}
}
