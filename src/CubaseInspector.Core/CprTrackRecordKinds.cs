namespace CubaseInspector.Core;

/// <summary>
/// Cubase 15.0.40 serialized track/lane class names observed in the supplied
/// executable's serializer and load code. This class map identifies the
/// stored class only; it does not prove visibility, name extraction, hierarchy,
/// ownership, or cross-version coverage.
/// </summary>
public static class CprTrackRecordKinds
{
    private static readonly IReadOnlyDictionary<string, string> Kinds =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["MAudioTrackEvent"] = "Audio",
            ["MMidiTrackEvent"] = "MIDI",
            ["MInstrumentTrackEvent"] = "Instrument",
            ["MSamplerTrackEvent"] = "Sampler",
            ["MFolderTrack"] = "Folder",
            ["MFolderTrackEvent"] = "Folder",
            ["MDeviceTrackEvent"] = "Device",
            ["MMarkerTrackEvent"] = "Marker",
            ["MTempoTrackEvent"] = "Tempo",
            ["MSignatureTrackEvent"] = "Time signature",
            ["MAutomationTrackEvent"] = "Automation",
            ["MChordTrackEvent"] = "Chord",
            ["MDrumTrackEvent"] = "Drum",
            ["MVideoTrackEvent"] = "Video",
            ["MPlayRangeTrackEvent"] = "Play range",
            ["MLoudnessTrackEvent"] = "Loudness"
        };

    public static bool IsTrackRecord(string serializedClass) => Kinds.ContainsKey(serializedClass);

    public static string? KindFor(string serializedClass) =>
        Kinds.TryGetValue(serializedClass, out var kind) ? kind : null;

    public static IReadOnlyCollection<string> SerializedClasses => Kinds.Keys.ToArray();
}
