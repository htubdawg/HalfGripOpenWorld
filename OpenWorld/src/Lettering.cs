using System;
using System.Collections.Generic;
using System.Text;

namespace HalfGripOpenWorld
{
	/// <summary>
	/// A pixel font for the road signs and the labelled map, drawn into plain RGBA buffers (no Unity), so the signs' lettering is made
	/// from the names themselves (<see cref="Names"/>) and the preview can draw the same letters. Mixed case like British road signs
	/// (the Transport lettering is read by the shape of whole words): capitals and ascenders 7 pixels high, x-height 5, descenders 2;
	/// letters 1 to 5 pixels wide with a pixel between them. Drawn a texel to a pixel and point-filtered, they keep the game's pixel look
	/// and stay crisp close up.
	/// </summary>
	internal static class Lettering
	{
		/// <summary>Rows in a glyph: 7 to the baseline, 2 under it.</summary>
		internal const int Rows = 9;

		/// <summary>Capital height, rows from the top to the baseline.</summary>
		internal const int Cap = 7;

		/// <summary>From one line's top to the next one's.</summary>
		internal const int Line = 11;

		/// <summary>Pixels between letters, and a space's width.</summary>
		private const int Gap = 1;
		private const int Space = 3;

		/// <summary>Each glyph's rows, top first, '#' set; rows left out at the bottom are blank. Widths come from the rows.</summary>
		private static readonly Dictionary<char, string[]> Glyphs = Make(new (char c, string rows)[]
		{
			('A', ".###.|#...#|#...#|#####|#...#|#...#|#...#"),
			('B', "####.|#...#|#...#|####.|#...#|#...#|####."),
			('C', ".###.|#...#|#....|#....|#....|#...#|.###."),
			('D', "####.|#...#|#...#|#...#|#...#|#...#|####."),
			('E', "#####|#....|#....|####.|#....|#....|#####"),
			('F', "#####|#....|#....|####.|#....|#....|#...."),
			('G', ".###.|#...#|#....|#.###|#...#|#...#|.####"),
			('H', "#...#|#...#|#...#|#####|#...#|#...#|#...#"),
			('I', "###|.#.|.#.|.#.|.#.|.#.|###"),
			('J', "..###|...#.|...#.|...#.|...#.|#..#.|.##.."),
			('K', "#...#|#..#.|#.#..|##...|#.#..|#..#.|#...#"),
			('L', "#....|#....|#....|#....|#....|#....|#####"),
			('M', "#...#|##.##|#.#.#|#.#.#|#...#|#...#|#...#"),
			('N', "#...#|##..#|#.#.#|#..##|#...#|#...#|#...#"),
			('O', ".###.|#...#|#...#|#...#|#...#|#...#|.###."),
			('P', "####.|#...#|#...#|####.|#....|#....|#...."),
			('Q', ".###.|#...#|#...#|#...#|#.#.#|#..#.|.##.#"),
			('R', "####.|#...#|#...#|####.|#.#..|#..#.|#...#"),
			('S', ".###.|#...#|#....|.###.|....#|#...#|.###."),
			('T', "#####|..#..|..#..|..#..|..#..|..#..|..#.."),
			('U', "#...#|#...#|#...#|#...#|#...#|#...#|.###."),
			('V', "#...#|#...#|#...#|#...#|#...#|.#.#.|..#.."),
			('W', "#...#|#...#|#...#|#.#.#|#.#.#|##.##|#...#"),
			('X', "#...#|#...#|.#.#.|..#..|.#.#.|#...#|#...#"),
			('Y', "#...#|#...#|.#.#.|..#..|..#..|..#..|..#.."),
			('Z', "#####|....#|...#.|..#..|.#...|#....|#####"),
			('a', ".....|.....|.###.|....#|.####|#...#|.####"),
			('b', "#....|#....|####.|#...#|#...#|#...#|####."),
			('c', "....|....|.###|#...|#...|#...|.###"),
			('d', "....#|....#|.####|#...#|#...#|#...#|.####"),
			('e', ".....|.....|.###.|#...#|#####|#....|.###."),
			('f', "..##|.#..|###.|.#..|.#..|.#..|.#.."),
			('g', ".....|.....|.####|#...#|#...#|#...#|.####|....#|.###."),
			('h', "#....|#....|####.|#...#|#...#|#...#|#...#"),
			('i', "#|.|#|#|#|#|#"),
			('j', "..#|...|..#|..#|..#|..#|..#|..#|##."),
			('k', "#...|#...|#..#|#.#.|##..|#.#.|#..#"),
			('l', "#.|#.|#.|#.|#.|#.|.#"),
			('m', ".....|.....|####.|#.#.#|#.#.#|#.#.#|#.#.#"),
			('n', ".....|.....|####.|#...#|#...#|#...#|#...#"),
			('o', ".....|.....|.###.|#...#|#...#|#...#|.###."),
			('p', ".....|.....|####.|#...#|#...#|#...#|####.|#....|#...."),
			('q', ".....|.....|.####|#...#|#...#|#...#|.####|....#|....#"),
			('r', "....|....|#.##|##..|#...|#...|#..."),
			('s', "....|....|.###|#...|.##.|...#|###."),
			('t', ".#..|.#..|###.|.#..|.#..|.#..|..##"),
			('u', ".....|.....|#...#|#...#|#...#|#...#|.####"),
			('v', ".....|.....|#...#|#...#|#...#|.#.#.|..#.."),
			('w', ".....|.....|#...#|#...#|#.#.#|#.#.#|.#.#."),
			('x', ".....|.....|#...#|.#.#.|..#..|.#.#.|#...#"),
			('y', ".....|.....|#...#|#...#|#...#|#...#|.####|....#|.###."),
			('z', ".....|.....|#####|...#.|..#..|.#...|#####"),
			('0', ".###.|#...#|#..##|#.#.#|##..#|#...#|.###."),
			('1', ".#.|##.|.#.|.#.|.#.|.#.|###"),
			('2', ".###.|#...#|....#|...#.|..#..|.#...|#####"),
			('3', "####.|....#|....#|.###.|....#|....#|####."),
			('4', "...#.|..##.|.#.#.|#..#.|#####|...#.|...#."),
			('5', "#####|#....|####.|....#|....#|#...#|.###."),
			('6', ".###.|#....|#....|####.|#...#|#...#|.###."),
			('7', "#####|....#|...#.|..#..|.#...|.#...|.#..."),
			('8', ".###.|#...#|#...#|.###.|#...#|#...#|.###."),
			('9', ".###.|#...#|#...#|.####|....#|....#|.###."),
			// The fractions of a mile on the signs: a small numerator, a slash, a small denominator.
			('\u00BD', ".#....#.|##...#..|.#..#...|.#.#.##.|...#...#|..#...#.|.#...#..|#....###"),
			('\u00BC', ".#....#.|##...#..|.#..#...|.#.#.#.#|...#.#.#|..#..###|.#.....#|#......#"),
			('\u00BE', "##....#.|.#...#..|##..#...|.#.#.#.#|##.#.#.#|..#..###|.#.....#|#......#"),
			('.', ".|.|.|.|.|.|#"),
			(',', "..|..|..|..|..|..|.#|#."),
			('\'', "#|#"),
			('-', "...|...|...|###"),
			('&', ".#...|#.#..|#.#..|.#...|#.#.#|#..#.|.##.#"),
			('(', ".#|#.|#.|#.|#.|#.|.#"),
			(')', "#.|.#|.#|.#|.#|.#|#."),
			('/', "..#|..#|.#.|.#.|.#.|#..|#.."),
			(':', ".|.|#|.|.|#"),
			('!', "#|#|#|#|#|.|#"),
			('?', ".###.|#...#|...#.|..#..|..#..|.....|..#.."),
		});

		private static Dictionary<char, string[]> Make((char c, string rows)[] list)
		{
			Dictionary<char, string[]> glyphs = new Dictionary<char, string[]>();
			foreach ((char c, string rows) in list)
			{
				glyphs[c] = rows.Split('|');
			}
			return glyphs;
		}

		/// <summary>The glyph for <paramref name="c"/>: itself, or the letter under its accent; null for a space or anything unknown.</summary>
		private static string[] Glyph(char c)
		{
			if (Glyphs.TryGetValue(c, out string[] rows))
			{
				return rows;
			}
			string bare = c.ToString().Normalize(NormalizationForm.FormD);
			return bare.Length > 0 && bare[0] != c && Glyphs.TryGetValue(bare[0], out rows) ? rows : null;
		}

		/// <summary>The width of <paramref name="text"/> in pixels (no gap after the last letter).</summary>
		internal static int Width(string text)
		{
			int w = 0;
			for (int k = 0; k < text.Length; k++)
			{
				string[] g = Glyph(text[k]);
				w += (g == null ? Space : g[0].Length) + (k + 1 < text.Length ? Gap : 0);
			}
			return w;
		}

		/// <summary>
		/// Draws <paramref name="text"/> into an RGBA buffer <paramref name="stride"/> pixels wide, rows from the bottom (as a Unity texture's
		/// are) if <paramref name="up"/>, else from the top; (x, top) is the left of the glyphs' top row, measured the same way the rows run
		/// down from the top. Each font pixel is <paramref name="scale"/> square; pixels outside the buffer are left out.
		/// </summary>
		internal static void Draw(byte[] rgba, int stride, int height, bool up, int x, int top, string text, int scale, byte r, byte g, byte b)
		{
			int pen = x;
			foreach (char c in text)
			{
				string[] glyph = Glyph(c);
				if (glyph == null)
				{
					pen += (Space + Gap) * scale;
					continue;
				}
				for (int row = 0; row < glyph.Length; row++)
				{
					for (int col = 0; col < glyph[row].Length; col++)
					{
						if (glyph[row][col] != '#')
						{
							continue;
						}
						for (int a = 0; a < scale; a++)
						{
							for (int d = 0; d < scale; d++)
							{
								Set(rgba, stride, height, up, pen + col * scale + d, top + row * scale + a, r, g, b);
							}
						}
					}
				}
				pen += (glyph[0].Length + Gap) * scale;
			}
		}

		/// <summary>Sets one pixel, counted from the top-left, in a buffer whose rows run from the bottom if <paramref name="up"/>.</summary>
		internal static void Set(byte[] rgba, int stride, int height, bool up, int x, int y, byte r, byte g, byte b)
		{
			if (x < 0 || y < 0 || x >= stride || y >= height)
			{
				return;
			}
			int o = ((up ? height - 1 - y : y) * stride + x) * 4;
			rgba[o] = r;
			rgba[o + 1] = g;
			rgba[o + 2] = b;
			rgba[o + 3] = 255;
		}

		/// <summary>
		/// A distance on a sign, in miles as British signs give it: to the nearest quarter under 3 miles (a quarter at least), then whole.
		/// <paramref name="units"/> in game units, 3.3 to a real metre.
		/// </summary>
		internal static string Miles(double units)
		{
			double miles = units / 3.3 / 1609.344;
			if (miles >= 2.875)
			{
				return ((int)Math.Floor(miles + 0.5)).ToString();
			}
			int quarters = Math.Max(1, (int)Math.Floor(miles * 4.0 + 0.5));
			int whole = quarters / 4;
			string part = (quarters % 4) switch { 1 => "\u00BC", 2 => "\u00BD", 3 => "\u00BE", _ => "" };
			return whole > 0 ? whole + part : part;
		}
	}
}
