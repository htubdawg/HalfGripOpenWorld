using System;

namespace HalfGripOpenWorld
{
	/// <summary>
	/// The heavy part of a deer strike, made in code (the mod ships only source): a deep body thump falling in pitch, a short crunch of
	/// noise, a bonnet's panel ringing at a few clashing (inharmonic) pitches, and a rattle dying away, pushed together and softly
	/// clipped so it hits hard. Played under the game's own crash, crunch and metal-hit clips (<c>Deer.Hit</c>), which give it the
	/// game's character. A few variants (other pitches for the ring and thump) so strikes do not all sound the same.
	///
	/// No Unity types: samples only (<c>Deer</c> makes the clips; <c>tools/meshcheck</c> can write them out to listen to).
	/// </summary>
	internal static class DeerSound
	{
		internal const int Rate = 44100;
		internal const double Seconds = 0.9;
		internal const int Variants = 3;

		/// <summary>One variant's samples, mono, -1 to 1.</summary>
		internal static float[] Strike(int variant)
		{
			int n = (int)(Rate * Seconds);
			float[] o = new float[n];
			// A small fixed noise (xorshift) per variant: the same sound every time.
			uint state = 0x9E3779B9u ^ (uint)(variant * 0x85EBCA6B + 1);
			double[] ring = variant switch
			{
				0 => new[] { 287.0, 541.0, 977.0, 1613.0, 2390.0 },
				1 => new[] { 251.0, 613.0, 1049.0, 1487.0, 2711.0 },
				_ => new[] { 331.0, 497.0, 911.0, 1733.0, 2203.0 },
			};
			double[] ringDecay = { 0.3, 0.22, 0.15, 0.1, 0.07 };
			double[] ringGain = { 0.2, 0.18, 0.14, 0.1, 0.07 };
			double thumpFrom = 105.0 + variant * 9.0, thumpTo = 42.0;
			double phase = 0.0, low = 0.0, low2 = 0.0, rattleLow = 0.0;
			for (int i = 0; i < n; i++)
			{
				double t = (double)i / Rate;
				// The thump: a sine sweeping down, the body's weight.
				double f = thumpTo + (thumpFrom - thumpTo) * Math.Exp(-t / 0.06);
				phase += 2.0 * Math.PI * f / Rate;
				double thump = Math.Sin(phase) * Math.Exp(-t / 0.11) * (1.0 - Math.Exp(-t / 0.002));

				// The crunch: noise, low-passed twice, a sharp start.
				state ^= state << 13;
				state ^= state >> 17;
				state ^= state << 5;
				double white = state / (double)uint.MaxValue * 2.0 - 1.0;
				low += (white - low) * 0.35;
				low2 += (low - low2) * 0.35;
				double crunch = low2 * 2.2 * Math.Exp(-t / 0.045);

				// The panel ringing: clashing partials, each beating against a twin a hair off.
				double metal = 0.0;
				for (int k = 0; k < ring.Length; k++)
				{
					double a = ringGain[k] * Math.Exp(-t / ringDecay[k]) * (1.0 - Math.Exp(-t / 0.004));
					metal += a * (Math.Sin(2.0 * Math.PI * ring[k] * t) + 0.6 * Math.Sin(2.0 * Math.PI * ring[k] * 1.0063 * t + k));
				}

				// The rattle: noise chopped about 28 times a second, dying away.
				rattleLow += (white - rattleLow) * 0.12;
				double chop = 0.5 + 0.5 * Math.Sin(2.0 * Math.PI * (28.0 + 6.0 * variant) * t);
				double rattle = rattleLow * chop * chop * 1.4 * Math.Exp(-t / 0.1) * (t > 0.03 ? 1.0 : t / 0.03);

				double x = 1.25 * thump + crunch + metal + rattle;
				o[i] = (float)Math.Tanh(x * 1.6);
			}
			// Peak a little under full, and the last few milliseconds faded to nothing.
			float peak = 0f;
			foreach (float v in o)
			{
				peak = Math.Max(peak, Math.Abs(v));
			}
			float scale = peak > 0f ? 0.95f / peak : 1f;
			int fade = Rate / 50;
			for (int i = 0; i < n; i++)
			{
				o[i] *= scale * (i > n - fade ? (float)(n - i) / fade : 1f);
			}
			return o;
		}

		/// <summary>The samples as a 16-bit mono WAV file (to listen to outside the game).</summary>
		internal static byte[] Wav(float[] s)
		{
			byte[] b = new byte[44 + s.Length * 2];
			void Int(int at, int v) { b[at] = (byte)v; b[at + 1] = (byte)(v >> 8); b[at + 2] = (byte)(v >> 16); b[at + 3] = (byte)(v >> 24); }
			void Short(int at, int v) { b[at] = (byte)v; b[at + 1] = (byte)(v >> 8); }
			b[0] = (byte)'R'; b[1] = (byte)'I'; b[2] = (byte)'F'; b[3] = (byte)'F';
			Int(4, 36 + s.Length * 2);
			b[8] = (byte)'W'; b[9] = (byte)'A'; b[10] = (byte)'V'; b[11] = (byte)'E';
			b[12] = (byte)'f'; b[13] = (byte)'m'; b[14] = (byte)'t'; b[15] = (byte)' ';
			Int(16, 16);
			Short(20, 1);
			Short(22, 1);
			Int(24, Rate);
			Int(28, Rate * 2);
			Short(32, 2);
			Short(34, 16);
			b[36] = (byte)'d'; b[37] = (byte)'a'; b[38] = (byte)'t'; b[39] = (byte)'a';
			Int(40, s.Length * 2);
			for (int i = 0; i < s.Length; i++)
			{
				Short(44 + i * 2, (int)Math.Round(Math.Max(-1f, Math.Min(1f, s[i])) * 32767f));
			}
			return b;
		}
	}
}
