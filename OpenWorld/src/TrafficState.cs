using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;

namespace HalfGripOpenWorld
{
	/// <summary>
	/// The whole state of a race's traffic (<see cref="TrafficLive"/>: its plans, the meso, the micro, the parking, the ways) as bytes, and back
	/// into a live traffic that carries on exactly as the one saved would (the rework's phase 6, rooms: <c>docs/TRAFFIC_REWORK.md</c> §4). In a
	/// room every machine runs the same simulation from the same inputs (lockstep); a player joining mid-race is given the host's state this
	/// way, and a machine found out of step is given it again.
	///
	/// <para>Written by reflection over the object graph, field by field, so a field added to the simulation is carried without anyone
	/// remembering to (trafficsim saves a traffic, loads it into a fresh one, runs both on and compares them byte for byte). Shared references
	/// and cycles keep their shape. Loading fills a traffic built the ordinary way in place, so what isn't state (the delegates the micro calls
	/// back through, a task making the next day's plans) stays as that build wired it. Lists and arrays keep their order; dictionaries and sets
	/// are written in their keys' order and rebuilt from that (none of the simulation's is ever walked, so their inner layout doesn't matter,
	/// and two equal ones write the same). No Unity
	/// types.</para>
	/// </summary>
	internal static class TrafficState
	{
		/// <summary>The layout. Raise it with any change to how the state is written (not to the simulation's fields: they are found as they are).</summary>
		internal const int Version = 1;

		private const byte Null = 0, Seen = 1, Fresh = 2, Kept = 3;

		/// <summary>
		/// The fields written for each type: every instance field down to <c>object</c>, in declaration order (the same build on every machine),
		/// but those marked <c>[NonSerialized]</c>: measurements of this machine's own time (milliseconds), not the simulation's state.
		/// </summary>
		private static readonly Dictionary<Type, FieldInfo[]> fieldsOf = new Dictionary<Type, FieldInfo[]>();

		private static FieldInfo[] Fields(Type t)
		{
			lock (fieldsOf)
			{
				if (fieldsOf.TryGetValue(t, out FieldInfo[] f))
				{
					return f;
				}
				List<FieldInfo> list = new List<FieldInfo>();
				for (Type b = t; b != null && b != typeof(object) && b != typeof(ValueType); b = b.BaseType)
				{
					FieldInfo[] own = Array.FindAll(b.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly), x => !x.IsNotSerialized);
					Array.Sort(own, (x, y) => x.MetadataToken.CompareTo(y.MetadataToken));
					list.InsertRange(0, own);
				}
				f = list.ToArray();
				fieldsOf[t] = f;
				return f;
			}
		}

		/// <summary>What isn't state: delegates (the build wires them), tasks, locks' and reflection's objects. Loading keeps what the build made.</summary>
		private static bool Skipped(Type t)
		{
			return typeof(Delegate).IsAssignableFrom(t) || typeof(System.Threading.Tasks.Task).IsAssignableFrom(t) || typeof(MemberInfo).IsAssignableFrom(t)
				|| t == typeof(System.Diagnostics.Stopwatch);
		}

		private static bool IsGeneric(Type t, Type def) => t.IsGenericType && t.GetGenericTypeDefinition() == def;

		// ---- writing --------------------------------------------------------------------------------------------------------

		private sealed class Writer
		{
			internal readonly BinaryWriter W;
			private readonly Dictionary<object, int> ids = new Dictionary<object, int>(ReferenceComparer.Instance);

			internal Writer(BinaryWriter w)
			{
				W = w;
			}

			/// <summary>For finding where two states part (trafficsim): the path to the value at byte <see cref="Find"/>, once written past it.</summary>
			internal long Find = -1;
			internal string Found;
			private readonly List<string> path = new List<string>();

			private void Field(string name, Type t, object v)
			{
				if (Find >= 0 && Found == null)
				{
					path.Add(name);
					Value(t, v);
					if (Found == null && W.BaseStream.Position > Find)
					{
						Found = string.Join("", path);
					}
					path.RemoveAt(path.Count - 1);
					return;
				}
				Value(t, v);
			}

			internal void Value(Type t, object v)
			{
				if (t.IsPrimitive || t.IsEnum)
				{
					Primitive(t.IsEnum ? Enum.GetUnderlyingType(t) : t, v);
					return;
				}
				if (t == typeof(string))
				{
					W.Write(v != null);
					if (v != null)
					{
						W.Write((string)v);
					}
					return;
				}
				if (t.IsValueType)
				{
					foreach (FieldInfo f in Fields(t))
					{
						Field("." + f.Name, f.FieldType, f.GetValue(v));
					}
					return;
				}
				if (v == null)
				{
					W.Write(Null);
					return;
				}
				if (ids.TryGetValue(v, out int id))
				{
					W.Write(Seen);
					W.Write(id);
					return;
				}
				Type rt = v.GetType();
				if (Skipped(rt) || Skipped(t))
				{
					ids[v] = ids.Count + 1;
					W.Write(Kept);
					return;
				}
				if (rt != t)
				{
					throw new InvalidOperationException("TrafficState: a " + rt.FullName + " held as a " + t.FullName + " (only exact types are written)");
				}
				ids[v] = ids.Count + 1;
				W.Write(Fresh);
				if (rt.IsArray)
				{
					Array a = (Array)v;
					if (a.Rank != 1)
					{
						throw new InvalidOperationException("TrafficState: arrays of rank " + a.Rank + " aren't written");
					}
					Type e = rt.GetElementType();
					W.Write(a.Length);
					if (e == typeof(double))
					{
						foreach (double d in (double[])a)
						{
							W.Write(d);
						}
						return;
					}
					if (e == typeof(int))
					{
						foreach (int d in (int[])a)
						{
							W.Write(d);
						}
						return;
					}
					for (int i = 0; i < a.Length; i++)
					{
						Field("[" + i + "]", e, a.GetValue(i));
					}
					return;
				}
				if (IsGeneric(rt, typeof(List<>)))
				{
					IList l = (IList)v;
					Type e = rt.GetGenericArguments()[0];
					W.Write(l.Count);
					for (int i = 0; i < l.Count; i++)
					{
						Field("[" + i + "]", e, l[i]);
					}
					return;
				}
				if (IsGeneric(rt, typeof(Dictionary<,>)))
				{
					IDictionary d = (IDictionary)v;
					Type[] kv = rt.GetGenericArguments();
					W.Write(d.Count);
					List<DictionaryEntry> entries = new List<DictionaryEntry>(d.Count);
					foreach (DictionaryEntry en in d)
					{
						entries.Add(en);
					}
					if (typeof(IComparable).IsAssignableFrom(kv[0]))
					{
						entries.Sort((x, y) => ((IComparable)x.Key).CompareTo(y.Key));
					}
					foreach (DictionaryEntry en in entries)
					{
						Field("{key}", kv[0], en.Key);
						Field("{" + en.Key + "}", kv[1], en.Value);
					}
					return;
				}
				if (IsGeneric(rt, typeof(HashSet<>)))
				{
					Type e = rt.GetGenericArguments()[0];
					List<object> items = new List<object>();
					foreach (object o in (IEnumerable)v)
					{
						items.Add(o);
					}
					if (typeof(IComparable).IsAssignableFrom(e))
					{
						items.Sort((x, y) => ((IComparable)x).CompareTo(y));
					}
					W.Write(items.Count);
					foreach (object o in items)
					{
						Value(e, o);
					}
					return;
				}
				if (rt.Namespace != null && rt.Namespace.StartsWith("System", StringComparison.Ordinal) && rt != typeof(object))
				{
					throw new InvalidOperationException("TrafficState: no way to write a " + rt.FullName);
				}
				foreach (FieldInfo f in Fields(rt))
				{
					Field("." + f.Name, f.FieldType, f.GetValue(v));
				}
			}

			private void Primitive(Type t, object v)
			{
				switch (Type.GetTypeCode(t))
				{
					case TypeCode.Boolean: W.Write((bool)v); break;
					case TypeCode.Byte: W.Write((byte)v); break;
					case TypeCode.SByte: W.Write((sbyte)v); break;
					case TypeCode.Int16: W.Write((short)v); break;
					case TypeCode.UInt16: W.Write((ushort)v); break;
					case TypeCode.Int32: W.Write((int)v); break;
					case TypeCode.UInt32: W.Write((uint)v); break;
					case TypeCode.Int64: W.Write((long)v); break;
					case TypeCode.UInt64: W.Write((ulong)v); break;
					case TypeCode.Single: W.Write((float)v); break;
					case TypeCode.Double: W.Write((double)v); break;
					case TypeCode.Char: W.Write((ushort)(char)v); break;
					default: throw new InvalidOperationException("TrafficState: no way to write a " + t.FullName);
				}
			}
		}

		/// <summary>Objects told apart by reference, not by their own Equals (a struct's box or a list equal to another is still another).</summary>
		private sealed class ReferenceComparer : IEqualityComparer<object>
		{
			internal static readonly ReferenceComparer Instance = new ReferenceComparer();

			public new bool Equals(object a, object b) => ReferenceEquals(a, b);

			public int GetHashCode(object o) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(o);
		}

		// ---- reading --------------------------------------------------------------------------------------------------------

		private sealed class Reader
		{
			internal readonly BinaryReader R;
			private readonly List<object> objects = new List<object> { null };
			/// <summary>The build's objects already filled for another of the saved ones: never filled twice (two saved objects the build shared stay two).</summary>
			private readonly HashSet<object> used = new HashSet<object>(ReferenceComparer.Instance);

			/// <summary>The build's object to fill for a saved one of type <paramref name="t"/>, if it is one and is free.</summary>
			private bool Free(object old, Type t) => old != null && old.GetType() == t && used.Add(old);

			internal Reader(BinaryReader r)
			{
				R = r;
			}

			/// <summary>A value of type <paramref name="t"/>, filled into <paramref name="old"/> (what the fresh build holds there) where it can be.</summary>
			internal object Value(Type t, object old)
			{
				if (t.IsPrimitive || t.IsEnum)
				{
					object p = Primitive(t.IsEnum ? Enum.GetUnderlyingType(t) : t);
					return t.IsEnum ? Enum.ToObject(t, p) : p;
				}
				if (t == typeof(string))
				{
					return R.ReadBoolean() ? R.ReadString() : null;
				}
				if (t.IsValueType)
				{
					object box = old ?? Activator.CreateInstance(t);
					foreach (FieldInfo f in Fields(t))
					{
						f.SetValue(box, Value(f.FieldType, f.GetValue(box)));
					}
					return box;
				}
				byte tag = R.ReadByte();
				switch (tag)
				{
					case Null:
						return null;
					case Seen:
						return objects[R.ReadInt32()];
					case Kept:
						objects.Add(old);
						if (old != null)
						{
							used.Add(old);
						}
						return old;
					case Fresh:
						break;
					default:
						throw new InvalidDataException("TrafficState: bad tag " + tag);
				}
				if (t.IsArray)
				{
					int n = R.ReadInt32();
					Type e = t.GetElementType();
					Array a = old is Array oa && oa.Length == n && Free(old, t) ? oa : Array.CreateInstance(e, n);
					objects.Add(a);
					if (e == typeof(double))
					{
						double[] d = (double[])a;
						for (int i = 0; i < n; i++)
						{
							d[i] = R.ReadDouble();
						}
						return a;
					}
					if (e == typeof(int))
					{
						int[] d = (int[])a;
						for (int i = 0; i < n; i++)
						{
							d[i] = R.ReadInt32();
						}
						return a;
					}
					for (int i = 0; i < n; i++)
					{
						a.SetValue(Value(e, a.GetValue(i)), i);
					}
					return a;
				}
				if (IsGeneric(t, typeof(List<>)))
				{
					IList l = Free(old, t) ? (IList)old : (IList)Activator.CreateInstance(t);
					objects.Add(l);
					Type e = t.GetGenericArguments()[0];
					object[] was = new object[l.Count];
					l.CopyTo(was, 0);
					l.Clear();
					int n = R.ReadInt32();
					for (int i = 0; i < n; i++)
					{
						l.Add(Value(e, i < was.Length && !e.IsValueType ? was[i] : null));
					}
					return l;
				}
				if (IsGeneric(t, typeof(Dictionary<,>)))
				{
					IDictionary d = Free(old, t) ? (IDictionary)old : (IDictionary)Activator.CreateInstance(t);
					objects.Add(d);
					Type[] kv = t.GetGenericArguments();
					d.Clear();
					int n = R.ReadInt32();
					for (int i = 0; i < n; i++)
					{
						object k = Value(kv[0], null);
						d.Add(k, Value(kv[1], null));
					}
					return d;
				}
				if (IsGeneric(t, typeof(HashSet<>)))
				{
					object s = Free(old, t) ? old : Activator.CreateInstance(t);
					objects.Add(s);
					Type e = t.GetGenericArguments()[0];
					t.GetMethod("Clear").Invoke(s, null);
					MethodInfo add = t.GetMethod("Add");
					int n = R.ReadInt32();
					object[] one = new object[1];
					for (int i = 0; i < n; i++)
					{
						one[0] = Value(e, null);
						add.Invoke(s, one);
					}
					return s;
				}
				object o = Free(old, t) ? old : System.Runtime.Serialization.FormatterServices.GetUninitializedObject(t);
				objects.Add(o);
				foreach (FieldInfo f in Fields(t))
				{
					f.SetValue(o, Value(f.FieldType, f.GetValue(o)));
				}
				return o;
			}

			private object Primitive(Type t)
			{
				switch (Type.GetTypeCode(t))
				{
					case TypeCode.Boolean: return R.ReadBoolean();
					case TypeCode.Byte: return R.ReadByte();
					case TypeCode.SByte: return R.ReadSByte();
					case TypeCode.Int16: return R.ReadInt16();
					case TypeCode.UInt16: return R.ReadUInt16();
					case TypeCode.Int32: return R.ReadInt32();
					case TypeCode.UInt32: return R.ReadUInt32();
					case TypeCode.Int64: return R.ReadInt64();
					case TypeCode.UInt64: return R.ReadUInt64();
					case TypeCode.Single: return R.ReadSingle();
					case TypeCode.Double: return R.ReadDouble();
					case TypeCode.Char: return (char)R.ReadUInt16();
					default: throw new InvalidDataException("TrafficState: no way to read a " + t.FullName);
				}
			}
		}

		// ---- the traffic ----------------------------------------------------------------------------------------------------

		/// <summary>The traffic's whole state, uncompressed (for comparing two traffics byte for byte).</summary>
		internal static byte[] Raw(TrafficLive live)
		{
			using (MemoryStream m = new MemoryStream(1 << 20))
			using (BinaryWriter w = new BinaryWriter(m))
			{
				w.Write(Version);
				w.Write(live.Seed);
				w.Write(live.Start);
				new Writer(w).Value(typeof(TrafficLive), live);
				w.Flush();
				return m.ToArray();
			}
		}

		/// <summary>Where in the traffic's state byte <paramref name="at"/> of <see cref="Raw"/> lies, as a path of fields (trafficsim's).</summary>
		internal static string PathAt(TrafficLive live, long at)
		{
			using (MemoryStream m = new MemoryStream(1 << 20))
			using (BinaryWriter w = new BinaryWriter(m))
			{
				w.Write(Version);
				w.Write(live.Seed);
				w.Write(live.Start);
				Writer wr = new Writer(w) { Find = at };
				wr.Value(typeof(TrafficLive), live);
				return wr.Found ?? "?";
			}
		}

		/// <summary>The traffic's whole state, compressed (what a room sends a late joiner).</summary>
		internal static byte[] Save(TrafficLive live)
		{
			byte[] raw = Raw(live);
			using (MemoryStream m = new MemoryStream(raw.Length / 4))
			{
				using (DeflateStream z = new DeflateStream(m, CompressionLevel.Fastest, true))
				{
					z.Write(raw, 0, raw.Length);
				}
				return m.ToArray();
			}
		}

		/// <summary>
		/// A traffic carrying on from <see cref="Save"/>'s bytes: built the ordinary way from the same seed and start (so the micro's call-backs
		/// are wired), then every field filled from the bytes. A worker's job (the build takes about a second).
		/// </summary>
		internal static TrafficLive Load(byte[] saved)
		{
			byte[] raw;
			using (MemoryStream src = new MemoryStream(saved))
			using (DeflateStream z = new DeflateStream(src, CompressionMode.Decompress))
			using (MemoryStream m = new MemoryStream(saved.Length * 4))
			{
				z.CopyTo(m);
				raw = m.ToArray();
			}
			using (MemoryStream m = new MemoryStream(raw))
			using (BinaryReader r = new BinaryReader(m))
			{
				if (r.ReadInt32() != Version)
				{
					throw new InvalidDataException("TrafficState: another layout");
				}
				int seed = r.ReadInt32();
				double start = r.ReadDouble();
				TrafficLive live = TrafficLive.Fresh(seed, start);
				new Reader(r).Value(typeof(TrafficLive), live);
				if (m.Position != m.Length)
				{
					throw new InvalidDataException("TrafficState: " + (m.Length - m.Position) + " bytes left over");
				}
				return live;
			}
		}
	}
}
