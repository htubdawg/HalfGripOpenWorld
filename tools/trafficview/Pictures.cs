using System;
using System.IO;
using HalfGripOpenWorld;

/// <summary>
/// The day's pictures (PPM): the roads coloured by their traffic (passes a game day x 36, as a counter's AADT), and the cars at four
/// hours (moving ones bright, parked ones faint).
/// </summary>
internal static class Pictures
{
	private const int Size = 1600;
	private const double Half = 16400.0;

	internal static void Draw(TrafficDay day, int[] passes, string path)
	{
		byte[] img = Blank();
		for (int e = 0; e < Traffic.Edges.Count; e++)
		{
			double aadt = passes[e] * 36.0;
			// 0 grey, then blue (a few hundred) through green and yellow to red (10,000 and more).
			byte r, g, b;
			if (aadt < 1.0)
			{
				r = g = b = 90;
			}
			else
			{
				double k = Math.Max(0.0, Math.Min(1.0, Math.Log10(aadt / 100.0) / 2.0));
				Ramp(k, out r, out g, out b);
			}
			Edge(img, e, r, g, b, aadt > 2000.0 ? 2 : 1);
		}
		Write(img, path);
		string stem = path.EndsWith(".ppm") ? path.Substring(0, path.Length - 4) : path;
		foreach (double hour in new[] { 3.0, 8.0, 12.0, 17.0 })
		{
			byte[] frame = Blank();
			for (int e = 0; e < Traffic.Edges.Count; e++)
			{
				Edge(frame, e, 70, 70, 70, 1);
			}
			double w = TrafficDay.SimOfHour(hour + 0.5);
			int moving = 0;
			for (int c = 0; c < day.Cars.Count; c++)
			{
				TrafficDay.Place at = day.At(c, w);
				if (at.State == TrafficDay.State.Parked)
				{
					Dot(frame, at.X, at.Z, 0, 60, 70, 110);
				}
			}
			for (int c = 0; c < day.Cars.Count; c++)
			{
				TrafficDay.Place at = day.At(c, w);
				if (at.State == TrafficDay.State.Driving)
				{
					moving++;
					Dot(frame, at.X, at.Z, 2, 255, 230, 60);
				}
			}
			Write(frame, stem + "-" + hour.ToString("00") + "h.ppm");
			Console.WriteLine("  frame at " + hour.ToString("00") + ":30: " + moving + " cars moving, written");
		}
	}

	private static void Ramp(double k, out byte r, out byte g, out byte b)
	{
		// blue -> cyan -> green -> yellow -> red
		double[][] stops = { new[] { 40.0, 80, 220 }, new[] { 40.0, 200, 220 }, new[] { 60.0, 210, 70 }, new[] { 240.0, 220, 40 }, new[] { 240.0, 40, 30 } };
		double f = k * (stops.Length - 1);
		int i = Math.Min(stops.Length - 2, (int)Math.Floor(f));
		double t = f - i;
		r = (byte)(stops[i][0] + (stops[i + 1][0] - stops[i][0]) * t);
		g = (byte)(stops[i][1] + (stops[i + 1][1] - stops[i][1]) * t);
		b = (byte)(stops[i][2] + (stops[i + 1][2] - stops[i][2]) * t);
	}

	private static byte[] Blank()
	{
		byte[] img = new byte[Size * Size * 3];
		for (int i = 0; i < img.Length; i += 3)
		{
			img[i] = 18;
			img[i + 1] = 22;
			img[i + 2] = 26;
		}
		return img;
	}

	private static void Px(double x, double z, out int i, out int j)
	{
		i = (int)((x + Half) / (2.0 * Half) * Size);
		j = (int)((Half - z) / (2.0 * Half) * Size);
	}

	private static void Put(byte[] img, int i, int j, byte r, byte g, byte b)
	{
		if (i < 0 || j < 0 || i >= Size || j >= Size)
		{
			return;
		}
		int k = (j * Size + i) * 3;
		img[k] = r;
		img[k + 1] = g;
		img[k + 2] = b;
	}

	private static void Edge(byte[] img, int e, byte r, byte g, byte b, int width)
	{
		Traffic.Edge edge = Traffic.Edges[e];
		Network.Road road = Network.Roads[edge.Road];
		for (double s = edge.Sa; s <= edge.Sb; s += 8.0)
		{
			Traffic.At(road, s, out double x, out double z, out double _);
			Px(x, z, out int i, out int j);
			for (int dj = 0; dj < width; dj++)
			{
				for (int di = 0; di < width; di++)
				{
					Put(img, i + di, j + dj, r, g, b);
				}
			}
		}
	}

	private static void Dot(byte[] img, double x, double z, int radius, byte r, byte g, byte b)
	{
		Px(x, z, out int i, out int j);
		for (int dj = -radius; dj <= radius; dj++)
		{
			for (int di = -radius; di <= radius; di++)
			{
				Put(img, i + di, j + dj, r, g, b);
			}
		}
	}

	private static void Write(byte[] img, string path)
	{
		using (FileStream f = File.Create(path))
		{
			byte[] head = System.Text.Encoding.ASCII.GetBytes("P6\n" + Size + " " + Size + "\n255\n");
			f.Write(head, 0, head.Length);
			f.Write(img, 0, img.Length);
		}
	}
}
