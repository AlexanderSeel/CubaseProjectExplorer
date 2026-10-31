using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using CubaseInspector.Core;

namespace CubaseInspector.Infrastructure;

/// <summary>
/// Bounded structural decoder for the empirically documented Cubase 12 named-record
/// grammar. Results are candidate observations until independently validated per field.
/// The compact arrangement event encoding is intentionally out of scope here.
/// </summary>
internal static class CprNamedRecordScanner
{
    private const int MaxRecordBytes = 256 * 1024;
    private const int MaxValuesPerAnchor = 1024;
    private const int MaxNodesPerAnchor = 4096;
    private const int MaxDepth = 24;
    private const int MaxArrayItems = 4096;
    private static readonly byte[] ChannelNameAnchor =
        [0, 0, 0, 5, (byte)'N', (byte)'a', (byte)'m', (byte)'e', 0, 0, 2, 0, 6,
         0, 0, 0, 1, 0, 0, 0, 7, (byte)'S', (byte)'t', (byte)'r', (byte)'i', (byte)'n', (byte)'g', 0, 0, 8];

    public static IReadOnlyList<CprNamedRecordCandidate> Scan(string streamName, byte[] data,
        long archiveFileOffset, int maxCandidates = 128)
    {
        var results = new List<CprNamedRecordCandidate>();
        var span = data.AsSpan();
        var position = 0;
        while (results.Count < maxCandidates && position < span.Length)
        {
            var found = span[position..].IndexOf(ChannelNameAnchor);
            if (found < 0) break;
            var anchor = position + found;
            try
            {
                var parser = new Parser(data, anchor, Math.Min(data.Length, anchor + MaxRecordBytes));
                var values = new List<CprNamedRecordValue>();
                var parsed = 0;
                var next = anchor;
                while (next < parser.End && values.Count < MaxValuesPerAnchor && parsed < MaxNodesPerAnchor)
                {
                    // Each channel-like named record starts a new top-level Name/String object.
                    // Do not merge the following record after reading past its insert/strip fields.
                    if (parsed > 0 && data.AsSpan(next).StartsWith(ChannelNameAnchor)) break;
                    var recordStart = next;
                    if (!parser.TryRecord(ref next, 0, out var record)) break;
                    parsed++;
                    AddLeaves(record, record.Name, archiveFileOffset, values, MaxValuesPerAnchor);
                    if (next <= recordStart) break;
                }
                if (values.Count > 0)
                    results.Add(new CprNamedRecordCandidate(streamName, archiveFileOffset + anchor,
                        values, "Cubase 12 named-record grammar candidate; field semantics not independently verified"));
            }
            catch (InvalidDataException)
            {
                // A matching byte sequence can occur in opaque payloads; it is not enough
                // to establish that a channel object was found.
            }
            position = anchor + ChannelNameAnchor.Length;
        }
        return results;
    }

    private static void AddLeaves(Node node, string path, long archiveFileOffset,
        List<CprNamedRecordValue> output, int limit)
    {
        if (output.Count >= limit) return;
        if (node.Children is { } children)
        {
            foreach (var child in children)
            {
                var childPath = child.Name.StartsWith("[", StringComparison.Ordinal)
                    ? path + child.Name : path + "/" + child.Name;
                AddLeaves(child, childPath, archiveFileOffset, output, limit);
                if (output.Count >= limit) return;
            }
            return;
        }
        output.Add(new CprNamedRecordValue(path, node.Type,
            node.Value ?? "(no scalar value)", archiveFileOffset + node.Offset));
    }

    private sealed record Node(string Name, string Type, string? Value, int Offset,
        IReadOnlyList<Node>? Children = null);

    private sealed class Parser
    {
        private readonly byte[] data;
        private readonly int start;
        private readonly int end;
        private int _nodes;
        public int End => end;

        public Parser(byte[] data, int start, int end)
        {
            this.data = data;
            this.start = start;
            this.end = end;
        }

        public bool TryRecord(ref int position, int depth, out Node node)
        {
            node = null!;
            if (depth > MaxDepth || ++_nodes > MaxNodesPerAnchor || position < start || position > end - 6)
                return false;
            var recordOffset = position;
            if (!TryName(ref position, out var name) || position > end - 2) return false;
            var type = U16(ref position);
            switch (type)
            {
                case 1:
                    if (!Has(position, 8)) return false;
                    var integer = BinaryPrimitives.ReadInt64BigEndian(data.AsSpan(position, 8)); position += 8;
                    node = new Node(name, "i64", integer.ToString(CultureInfo.InvariantCulture), recordOffset); return true;
                case 4:
                    if (!Has(position, 8)) return false;
                    var number = BitConverter.Int64BitsToDouble(BinaryPrimitives.ReadInt64BigEndian(data.AsSpan(position, 8))); position += 8;
                    if (!double.IsFinite(number)) return false;
                    node = new Node(name, "f64", number.ToString("R", CultureInfo.InvariantCulture), recordOffset); return true;
                case 8:
                    if (!TrySizedString(ref position, out var text)) return false;
                    node = new Node(name, "string", text, recordOffset); return true;
                case 2:
                    if (position > end - 2) return false;
                    return TryContainer(name, recordOffset, ref position, depth + 1, out node);
                default: return false;
            }
        }

        private bool TryContainer(string name, int recordOffset, ref int position, int depth, out Node node)
        {
            node = null!;
            var subtype = U16(ref position);
            var children = new List<Node>();
            if (subtype == 6)
            {
                if (!TryCount(ref position, out var count)) return false;
                for (var i = 0; i < count; i++)
                    if (!TryRecord(ref position, depth, out var child)) return false; else children.Add(child);
                node = new Node(name, "object", null, recordOffset, children); return true;
            }
            if (subtype == 5)
            {
                if (!TryCount(ref position, out var itemCount)) return false;
                for (var item = 0; item < itemCount; item++)
                {
                    if (!TryCount(ref position, out var count)) return false;
                    for (var i = 0; i < count; i++)
                    {
                        if (!TryRecord(ref position, depth, out var child)) return false;
                        children.Add(child with { Name = $"[{item}]/{child.Name}" });
                    }
                }
                node = new Node(name, "array", null, recordOffset, children); return true;
            }
            if (subtype is 2 or 4)
            {
                if (!TryCount(ref position, out var count)) return false;
                for (var i = 0; i < count; i++)
                {
                    string value;
                    if (subtype == 2)
                    {
                        if (!Has(position, 8)) return false;
                        value = BinaryPrimitives.ReadInt64BigEndian(data.AsSpan(position, 8)).ToString(CultureInfo.InvariantCulture); position += 8;
                    }
                    else if (!TrySizedString(ref position, out value)) return false;
                    children.Add(new Node($"[{i}]", subtype == 2 ? "i64" : "string", value, position));
                }
                node = new Node(name, subtype == 2 ? "i64-array" : "string-array", null, recordOffset, children); return true;
            }
            if (subtype == 7)
            {
                if (!TryCount(ref position, out var size) || !Has(position, size)) return false;
                position += size;
                node = new Node(name, "blob", $"{size} bytes (opaque)", recordOffset); return true;
            }
            if (subtype == 0x14)
            {
                if (!TryClassInstance(ref position, depth, out var className, children)) return false;
                node = new Node(name, "instance:" + className, null, recordOffset, children); return true;
            }
            if (subtype == 0xC9)
            {
                if (!TryCount(ref position, out var count)) return false;
                for (var i = 0; i < count; i++)
                {
                    if (position > end - 2) return false;
                    var tag = U16(ref position);
                    if (tag == 0) continue;
                    if (tag != 0x14 || !TryClassInstance(ref position, depth, out var className, children)) return false;
                    children.Add(new Node($"[{i}]/class", "class", className, position));
                }
                node = new Node(name, "instance-array", null, recordOffset, children); return true;
            }
            return false;
        }

        private bool TryClassInstance(ref int position, int depth, out string className, List<Node> children)
        {
            className = "";
            if (!TryName(ref position, out className) || !Has(position, 12)) return false;
            position += 8; // empirical uid/class metadata words; retained only as opaque structure
            if (!TryCount(ref position, out var count)) return false;
            for (var i = 0; i < count; i++)
                if (!TryRecord(ref position, depth, out var child)) return false; else children.Add(child);
            return true;
        }

        private bool TryName(ref int position, out string name)
        {
            name = "";
            if (!Has(position, 4)) return false;
            var length = U32(ref position);
            if (length is < 1 or > 256 || !Has(position, (int)length) || data[position + (int)length - 1] != 0) return false;
            var raw = data.AsSpan(position, (int)length - 1);
            foreach (var b in raw) if (b is < 32 or > 126) return false;
            name = Encoding.ASCII.GetString(raw);
            position += (int)length;
            return true;
        }

        private bool TrySizedString(ref int position, out string value)
        {
            value = "";
            if (!Has(position, 4)) return false;
            var length = U32(ref position);
            if (length > 1024 * 1024 || !Has(position, (int)length)) return false;
            var raw = data.AsSpan(position, (int)length);
            position += (int)length;
            if (raw.Length >= 3 && raw[^3] == 0xEF && raw[^2] == 0xBB && raw[^1] == 0xBF)
                raw = raw[..^3];
            while (!raw.IsEmpty && raw[^1] == 0) raw = raw[..^1];
            try { value = new UTF8Encoding(false, true).GetString(raw); }
            catch (DecoderFallbackException) { return false; }
            return !value.Any(char.IsControl);
        }

        private bool TryCount(ref int position, out int count)
        {
            count = 0;
            if (!Has(position, 4)) return false;
            var value = U32(ref position);
            if (value > MaxArrayItems) return false;
            count = (int)value; return true;
        }

        private ushort U16(ref int position) { var n = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(position, 2)); position += 2; return n; }
        private uint U32(ref int position) { var n = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(position, 4)); position += 4; return n; }
        private bool Has(int position, int length) => length >= 0 && position >= start && position <= end - length;
    }
}
