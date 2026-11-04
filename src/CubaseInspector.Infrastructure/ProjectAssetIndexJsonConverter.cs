using System.Text.Json;
using System.Text.Json.Serialization;
using CubaseInspector.Core;

namespace CubaseInspector.Infrastructure;

/// <summary>
/// Versioned database representation: shared note provenance is stored once,
/// while numeric rows preserve the original values and nullable fields.
/// Legacy object-per-note indexes remain readable. This is not a CPR format.
/// </summary>
public sealed class ProjectAssetIndexJsonConverter : JsonConverter<ProjectAssetIndex>
{
    private const int MaximumRows = 250_000;
    private readonly record struct Context(string ProjectPath, string ProjectName, EvidenceLevel Evidence,
        string Warning, string Layout, string? Track, string? Part, long? TrackOffset, long? PartOffset);

    public override ProjectAssetIndex Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        try { return ReadIndex(document.RootElement, options); }
        catch (Exception exception) when (exception is InvalidOperationException or FormatException or OverflowException)
        {
            throw new JsonException("Invalid compact asset index value.", exception);
        }
    }

    private static ProjectAssetIndex ReadIndex(JsonElement root, JsonSerializerOptions options)
    {
        if (!root.TryGetProperty("v", out var version))
            return root.Deserialize<ProjectAssetIndex>(options) ?? throw new JsonException("Asset index is null.");
        if (version.GetInt32() != 2) throw new JsonException("Unsupported asset index storage version.");
        var contextsJson = Property(root, "contexts");
        if (contextsJson.GetArrayLength() > MaximumRows) throw new JsonException("Asset context limit exceeded.");
        var contexts = new List<Context>(contextsJson.GetArrayLength());
        foreach (var row in contextsJson.EnumerateArray())
        {
            if (row.ValueKind != JsonValueKind.Array || row.GetArrayLength() != 9)
                throw new JsonException("Invalid asset note context.");
            contexts.Add(new(Text(row[0]), Text(row[1]), (EvidenceLevel)row[2].GetInt32(),
                Text(row[3]), Text(row[4]), row[5].GetString(), row[6].GetString(), Long(row[7]), Long(row[8])));
        }
        IReadOnlyList<IndexedMidiNoteCandidate>? notes = null;
        // The document store omits null object properties when saving JSON.
        var notesJson = root.TryGetProperty("notes", out var savedNotes) ? savedNotes : default;
        if (notesJson.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
        {
            if (notesJson.GetArrayLength() > MaximumRows) throw new JsonException("Asset note limit exceeded.");
            var values = new List<IndexedMidiNoteCandidate>(notesJson.GetArrayLength());
            foreach (var row in notesJson.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Array || row.GetArrayLength() != 9)
                    throw new JsonException("Invalid asset note row.");
                var id = row[0].GetInt32();
                if ((uint)id >= (uint)contexts.Count) throw new JsonException("Invalid asset note context reference.");
                var context = contexts[id];
                values.Add(new(context.ProjectPath, context.ProjectName, row[1].GetInt32(), row[2].GetInt64(),
                    row[3].GetDouble(), row[4].GetDouble(), row[5].GetInt32(), row[6].GetInt32(),
                    context.Evidence, context.Warning, context.Layout, context.Track, context.Part,
                    context.TrackOffset, context.PartOffset,
                    row[7].ValueKind == JsonValueKind.Null ? null : row[7].GetInt32(),
                    row[8].ValueKind == JsonValueKind.Null ? null : row[8].GetUInt16()));
            }
            notes = values.AsReadOnly();
        }
        return new(Text(Property(root, "projectPath")), Text(Property(root, "projectName")),
            (EvidenceLevel)Property(root, "evidence").GetInt32(),
            Property(root, "mediaUses").Deserialize<IndexedProjectMediaUse[]>(options) ?? [],
            Property(root, "midiTracks").Deserialize<IndexedMidiTrack[]>(options) ?? [], notes,
            root.TryGetProperty("nativeMedia", out var media)
                ? media.Deserialize<IndexedMediaReferenceCandidate[]>(options) : null);
    }

    public override void Write(Utf8JsonWriter writer, ProjectAssetIndex value, JsonSerializerOptions options)
    {
        if (value.NativeMidiNoteCandidates is { Count: > MaximumRows })
            throw new JsonException("Asset note limit exceeded.");
        var contexts = new List<Context>();
        var ids = new Dictionary<Context, int>();
        foreach (var note in value.NativeMidiNoteCandidates ?? [])
        {
            var context = ContextFor(note);
            if (ids.TryAdd(context, contexts.Count)) contexts.Add(context);
        }
        writer.WriteStartObject();
        writer.WriteNumber("v", 2);
        writer.WriteString("projectPath", value.ProjectPath);
        writer.WriteString("projectName", value.ProjectName);
        writer.WriteNumber("evidence", (int)value.Evidence);
        writer.WritePropertyName("mediaUses"); JsonSerializer.Serialize(writer, value.MediaUses, options);
        writer.WritePropertyName("midiTracks"); JsonSerializer.Serialize(writer, value.MidiTracks, options);
        writer.WritePropertyName("nativeMedia"); JsonSerializer.Serialize(writer, value.NativeMediaReferenceCandidates, options);
        writer.WriteStartArray("contexts");
        foreach (var context in contexts)
        {
            writer.WriteStartArray();
            writer.WriteStringValue(context.ProjectPath); writer.WriteStringValue(context.ProjectName);
            writer.WriteNumberValue((int)context.Evidence); writer.WriteStringValue(context.Warning);
            writer.WriteStringValue(context.Layout); writer.WriteStringValue(context.Track); writer.WriteStringValue(context.Part);
            Number(writer, context.TrackOffset); Number(writer, context.PartOffset);
            writer.WriteEndArray();
        }
        writer.WriteEndArray();
        writer.WritePropertyName("notes");
        if (value.NativeMidiNoteCandidates is null) writer.WriteNullValue();
        else
        {
            writer.WriteStartArray();
            foreach (var note in value.NativeMidiNoteCandidates)
            {
                writer.WriteStartArray();
                writer.WriteNumberValue(ids[ContextFor(note)]); writer.WriteNumberValue(note.Observation);
                writer.WriteNumberValue(note.FileOffset); writer.WriteNumberValue(note.PositionTicks);
                writer.WriteNumberValue(note.DurationTicks); writer.WriteNumberValue(note.Pitch);
                writer.WriteNumberValue(note.Velocity); Number(writer, note.Channel); Number(writer, note.RawFlags);
                writer.WriteEndArray();
            }
            writer.WriteEndArray();
        }
        writer.WriteEndObject();
    }

    private static Context ContextFor(IndexedMidiNoteCandidate note) => new(note.ProjectPath, note.ProjectName,
        note.Evidence, note.Warning, note.SourceLayout, note.CandidateTrackName, note.CandidatePartName,
        note.CandidateTrackOffset, note.CandidatePartOffset);
    private static JsonElement Property(JsonElement value, string name) => value.TryGetProperty(name, out var field)
        ? field : throw new JsonException($"Asset index field '{name}' is missing.");
    private static string Text(JsonElement value) => value.GetString() ?? throw new JsonException("Missing asset text.");
    private static long? Long(JsonElement value) => value.ValueKind == JsonValueKind.Null ? null : value.GetInt64();
    private static void Number(Utf8JsonWriter writer, long? value)
    {
        if (value is long number) writer.WriteNumberValue(number);
        else writer.WriteNullValue();
    }
}
