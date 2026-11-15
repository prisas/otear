using System.Runtime.InteropServices;

namespace Otear.Mpv.Native;

/// <summary>
/// Converts between mpv_node and plain managed values:
/// null, string, bool, long, double, byte[], List&lt;object?&gt; and Dictionary&lt;string, object?&gt;.
/// </summary>
internal static unsafe class NodeMarshal
{
    public static object? ToManaged(MpvNode* node)
    {
        if (node == null) return null;
        switch (node->Format)
        {
            case MpvFormat.String:
            case MpvFormat.OsdString:
                return Utf8.ToManaged(node->String);
            case MpvFormat.Flag:
                return node->Flag != 0;
            case MpvFormat.Int64:
                return node->Int64;
            case MpvFormat.Double:
                return node->Double;
            case MpvFormat.NodeArray:
            {
                var list = node->List;
                var result = new List<object?>(list == null ? 0 : list->Num);
                if (list != null)
                    for (int i = 0; i < list->Num; i++)
                        result.Add(ToManaged(&list->Values[i]));
                return result;
            }
            case MpvFormat.NodeMap:
            {
                var list = node->List;
                var result = new Dictionary<string, object?>(list == null ? 0 : list->Num, StringComparer.Ordinal);
                if (list != null)
                    for (int i = 0; i < list->Num; i++)
                        result[Utf8.ToManaged(list->Keys[i]) ?? string.Empty] = ToManaged(&list->Values[i]);
                return result;
            }
            case MpvFormat.ByteArray:
            {
                var ba = node->ByteArray;
                if (ba == null || ba->Data == null) return Array.Empty<byte>();
                return new ReadOnlySpan<byte>(ba->Data, checked((int)ba->Size)).ToArray();
            }
            default:
                return null;
        }
    }

    /// <summary>Reads the data pointer of a property-change event according to its format.</summary>
    public static object? FromPropertyData(MpvFormat format, void* data)
    {
        if (data == null) return null;
        return format switch
        {
            MpvFormat.String or MpvFormat.OsdString => Utf8.ToManaged(*(byte**)data),
            MpvFormat.Flag => *(int*)data != 0,
            MpvFormat.Int64 => *(long*)data,
            MpvFormat.Double => *(double*)data,
            MpvFormat.Node => ToManaged((MpvNode*)data),
            _ => null,
        };
    }

    /// <summary>Builds a native mpv_node tree from managed values. Free it with <see cref="Free"/>.</summary>
    public static void FromManaged(object? value, MpvNode* dst)
    {
        *dst = default;
        switch (value)
        {
            case null:
                dst->Format = MpvFormat.None;
                break;
            case string s:
                dst->Format = MpvFormat.String;
                dst->String = new Utf8.NativeString(s).Pointer;
                break;
            case bool b:
                dst->Format = MpvFormat.Flag;
                dst->Flag = b ? 1 : 0;
                break;
            case int i:
                dst->Format = MpvFormat.Int64;
                dst->Int64 = i;
                break;
            case long l:
                dst->Format = MpvFormat.Int64;
                dst->Int64 = l;
                break;
            case double d:
                dst->Format = MpvFormat.Double;
                dst->Double = d;
                break;
            case float f:
                dst->Format = MpvFormat.Double;
                dst->Double = f;
                break;
            case IReadOnlyDictionary<string, object?> map:
            {
                dst->Format = MpvFormat.NodeMap;
                var list = (MpvNodeList*)NativeMemory.AllocZeroed((nuint)sizeof(MpvNodeList));
                list->Num = map.Count;
                list->Values = (MpvNode*)NativeMemory.AllocZeroed((nuint)Math.Max(1, map.Count), (nuint)sizeof(MpvNode));
                list->Keys = (byte**)NativeMemory.AllocZeroed((nuint)Math.Max(1, map.Count), (nuint)sizeof(byte*));
                int idx = 0;
                foreach (var (k, v) in map)
                {
                    list->Keys[idx] = new Utf8.NativeString(k).Pointer;
                    FromManaged(v, &list->Values[idx]);
                    idx++;
                }
                dst->List = list;
                break;
            }
            case System.Collections.IEnumerable seq:
            {
                var items = new List<object?>();
                foreach (var item in seq) items.Add(item);
                dst->Format = MpvFormat.NodeArray;
                var list = (MpvNodeList*)NativeMemory.AllocZeroed((nuint)sizeof(MpvNodeList));
                list->Num = items.Count;
                list->Values = (MpvNode*)NativeMemory.AllocZeroed((nuint)Math.Max(1, items.Count), (nuint)sizeof(MpvNode));
                for (int i = 0; i < items.Count; i++) FromManaged(items[i], &list->Values[i]);
                dst->List = list;
                break;
            }
            default:
                dst->Format = MpvFormat.String;
                dst->String = new Utf8.NativeString(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)).Pointer;
                break;
        }
    }

    /// <summary>Frees a node tree built by <see cref="FromManaged"/> (not one returned by mpv).</summary>
    public static void Free(MpvNode* node)
    {
        if (node == null) return;
        switch (node->Format)
        {
            case MpvFormat.String:
                if (node->String != null) NativeMemory.Free(node->String);
                break;
            case MpvFormat.NodeArray:
            case MpvFormat.NodeMap:
            {
                var list = node->List;
                if (list == null) break;
                for (int i = 0; i < list->Num; i++)
                {
                    Free(&list->Values[i]);
                    if (list->Keys != null && list->Keys[i] != null) NativeMemory.Free(list->Keys[i]);
                }
                NativeMemory.Free(list->Values);
                if (list->Keys != null) NativeMemory.Free(list->Keys);
                NativeMemory.Free(list);
                break;
            }
        }
        *node = default;
    }
}
