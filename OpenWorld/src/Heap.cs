using System.Collections.Generic;

namespace HalfGripOpenWorld
{
	/// <summary>A binary min-heap of (priority, item); equal priorities come out lowest item first, so the order is the same on every machine.</summary>
	internal sealed class Heap
	{
		private readonly List<(double p, int c)> items = new List<(double p, int c)>();

		internal int Count => items.Count;

		private static bool Less((double p, int c) a, (double p, int c) b)
		{
			return a.p < b.p || (a.p == b.p && a.c < b.c);
		}

		internal void Push(double p, int c)
		{
			items.Add((p, c));
			int i = items.Count - 1;
			while (i > 0)
			{
				int up = (i - 1) / 2;
				if (!Less(items[i], items[up]))
				{
					break;
				}
				(items[i], items[up]) = (items[up], items[i]);
				i = up;
			}
		}

		internal int Pop(out double p)
		{
			(double p, int c) top = items[0];
			int last = items.Count - 1;
			items[0] = items[last];
			items.RemoveAt(last);
			int i = 0;
			while (true)
			{
				int l = i * 2 + 1, r = l + 1, m = i;
				if (l < items.Count && Less(items[l], items[m]))
				{
					m = l;
				}
				if (r < items.Count && Less(items[r], items[m]))
				{
					m = r;
				}
				if (m == i)
				{
					break;
				}
				(items[i], items[m]) = (items[m], items[i]);
				i = m;
			}
			p = top.p;
			return top.c;
		}
	}
}
