using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace HalfGripOpenWorld
{
	/// <summary>
	/// World positions the game keeps in fields rather than in transforms. Moving the scene's transforms does not move these, so each is
	/// shifted here by hand. The list comes from a decompile of game version 0.418 (every Vector3, Vector3[] and List&lt;Vector3&gt; field
	/// that holds a position in the world, not an offset or a direction); see docs/DESIGN.md. A field that is gone in another game
	/// version is logged once and skipped.
	/// </summary>
	internal static class GameCaches
	{
		/// <summary>(type, field): the plain position fields. Values, arrays and lists are all handled.</summary>
		private static readonly (Type type, string field)[] Fields =
		{
			(typeof(LapManager), "lapDrivenFrom"),
			(typeof(LapManager), "projectedPathIndices"),
			(typeof(RoadPickup), "restPosition"),
			(typeof(WheatTrampler), "previousPosition"),
			(typeof(horizontalChicken), "startPoint"),
			(typeof(EnemyController), "targetPoint"),
			(typeof(FreeFlightCamera), "targetPosition"),
			(typeof(FreeFlightCamera), "homePosition"),
			(typeof(WaypointsManager), "processedWaypoints"),
			(typeof(TrainPath), "sampledPoints"),
			(typeof(TrainPath), "lastPositions"),
		};

		private static readonly HashSet<string> warned = new HashSet<string>();

		/// <summary>Moves every cached position by <paramref name="delta"/>. Returns how many values it moved.</summary>
		internal static int Shift(Vector3 delta)
		{
			int n = 0;
			// A list or array reached twice (two fields holding the same one) must only move once.
			HashSet<object> seen = new HashSet<object>(ReferenceComparer.Instance);
			n += ShiftTrackLines(delta, seen);
			n += ShiftRewinder(delta, seen);
			foreach ((Type type, string name) in Fields)
			{
				FieldInfo field = Field(type, name);
				if (field == null)
				{
					continue;
				}
				foreach (UnityEngine.Object o in UnityEngine.Object.FindObjectsByType(type, FindObjectsInactive.Include, FindObjectsSortMode.None))
				{
					try
					{
						n += ShiftValue(field, o, delta, seen);
					}
					catch (Exception e)
					{
						Warn(type.Name + "." + name, "could not be shifted: " + e.Message);
					}
				}
			}
			return n;
		}

		/// <summary>
		/// The track lines: their samples, the nearest-point tree built from them, and the solved lanes. The control points are transforms
		/// and have already moved. Lanes themselves are sideways offsets and heights, so they need nothing.
		/// </summary>
		private static int ShiftTrackLines(Vector3 delta, HashSet<object> seen)
		{
			int n = 0;
			FieldInfo kdTree = Field(typeof(PathManager), "kdTree");
			FieldInfo laneSolves = Field(typeof(PathManager), "laneSolves");
			foreach (PathManager path in UnityEngine.Object.FindObjectsByType<PathManager>(FindObjectsInactive.Include, FindObjectsSortMode.None))
			{
				if (path.sampledPoints != null && seen.Add(path.sampledPoints))
				{
					n += ShiftList(path.sampledPoints, delta);
					if (kdTree != null && path.sampledPoints.Count > 0)
					{
						kdTree.SetValue(path, new KDTree(path.sampledPoints));
					}
				}
				if (laneSolves != null && laneSolves.GetValue(path) is Dictionary<int, PathManager.LaneSolve> solves)
				{
					foreach (PathManager.LaneSolve solve in solves.Values)
					{
						if (solve.points != null && seen.Add(solve.points))
						{
							n += ShiftList(solve.points, delta);
						}
					}
				}
			}
			return n;
		}

		/// <summary>The rewind recorder (an item that winds the race back a few seconds) keeps the bodies' recent positions.</summary>
		private static int ShiftRewinder(Vector3 delta, HashSet<object> seen)
		{
			int n = 0;
			FieldInfo frames = Field(typeof(TimeRewinder), "frames");
			if (frames == null)
			{
				return 0;
			}
			foreach (TimeRewinder rewinder in UnityEngine.Object.FindObjectsByType<TimeRewinder>(FindObjectsInactive.Include, FindObjectsSortMode.None))
			{
				if (!(frames.GetValue(rewinder) is IList list))
				{
					continue;
				}
				foreach (object frame in list)
				{
					FieldInfo pos = frame?.GetType().GetField("pos", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
					if (pos != null && pos.GetValue(frame) is Vector3[] points && seen.Add(points))
					{
						for (int i = 0; i < points.Length; i++)
						{
							points[i] += delta;
						}
						n += points.Length;
					}
				}
			}
			return n;
		}

		private static int ShiftValue(FieldInfo field, object owner, Vector3 delta, HashSet<object> seen)
		{
			object value = field.GetValue(owner);
			switch (value)
			{
				case Vector3 v:
					field.SetValue(owner, v + delta);
					return 1;
				case Vector3[] array when seen.Add(array):
					for (int i = 0; i < array.Length; i++)
					{
						array[i] += delta;
					}
					return array.Length;
				case List<Vector3> list when seen.Add(list):
					return ShiftList(list, delta);
				default:
					return 0;
			}
		}

		private static int ShiftList(List<Vector3> list, Vector3 delta)
		{
			for (int i = 0; i < list.Count; i++)
			{
				list[i] += delta;
			}
			return list.Count;
		}

		private static FieldInfo Field(Type type, string name)
		{
			FieldInfo field = AccessTools.Field(type, name);
			if (field == null)
			{
				Warn(type.Name + "." + name, "is not in this game version, so it is not moved by an origin shift");
			}
			return field;
		}

		private static void Warn(string what, string text)
		{
			if (warned.Add(what))
			{
				Mod.Log.LogWarning(what + " " + text + ".");
			}
		}

		/// <summary>Compares by reference, so two equal lists are still two lists.</summary>
		private sealed class ReferenceComparer : IEqualityComparer<object>
		{
			internal static readonly ReferenceComparer Instance = new ReferenceComparer();

			public new bool Equals(object a, object b)
			{
				return ReferenceEquals(a, b);
			}

			public int GetHashCode(object o)
			{
				return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(o);
			}
		}
	}
}
