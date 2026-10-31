using UnityEngine;

namespace HalfGripOpenWorld
{
	/// <summary>
	/// The hand on the shifter: the frame it lets go of the car's active shifter in neutral. The garage and the events' start boxes act
	/// on that letting go, not on the gearbox merely standing in neutral, so a gear change through neutral (the hand still on the
	/// shifter) never opens a garage or signs on to an event.
	///
	/// Every kind of shifter (H-pattern, sequential, automatic, Lenco) keeps its knobs and levers, the <see cref="Grabbable"/>s the hand
	/// takes, under its own object, so the shifter is held when the grabber's held object is under the active shifter's.
	/// </summary>
	internal static class ShifterHand
	{
		private static Grabber grabber;
		private static int frame = -1;
		private static bool held;
		private static bool released;

		/// <summary>The active shifter (every kind reports here) in no gear.</summary>
		internal static bool Neutral(CarLink link)
		{
			return link.sa != null && (link.sa.gear == 0 || !link.sa.inGear);
		}

		/// <summary>True on the one frame the hand let go of the active shifter with it in neutral.</summary>
		internal static bool ReleasedInNeutral(CarLink link)
		{
			Look(link);
			return released && Neutral(link);
		}

		/// <summary>
		/// Once a frame, whoever asks first: whether the hand holds the shifter now, and whether it did the frame before. A frame nobody
		/// asked in breaks the chain, so a hold seen long ago is never taken for a letting go now.
		/// </summary>
		private static void Look(CarLink link)
		{
			if (frame == Time.frameCount)
			{
				return;
			}
			bool chained = frame == Time.frameCount - 1;
			frame = Time.frameCount;
			if (grabber == null && frame % 30 == 0)
			{
				grabber = Object.FindFirstObjectByType<Grabber>();
			}
			Grabbable g = grabber != null ? grabber.Held : null;
			bool now = g != null && link != null && link.sa != null && g.transform.IsChildOf(link.sa.transform);
			released = chained && held && !now;
			held = now;
		}
	}
}
