using System.Globalization;
using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;
using CubaseInspector.Core;

namespace CubaseInspector.Infrastructure;

/// <summary>Parses openly specified DAWproject project.xml data; never loads audio or plugin binaries.</summary>
public sealed class DawprojectReader : IProjectReader
{
    private const long MaxXmlLength = 16 * 1024 * 1024;
    private const int MaxEntryCount = 5000;
    private const int MaxTrackCount = 100000;
    private const int MaxPluginCount = 100000;
    private const int MaxRoutingCount = 200000;
    private const int MaxMediaReferences = 100000;
    private const int MaxTimelineItems = 150000;
    private const int MaxMidiNotes = 1000000;
    private const int MaxEqBands = 100000;
    private const int MaxWarpPoints = 150000;
    private const int MaxTrackNesting = 128;
    private const int MaxXmlDepth = 128;
    public bool CanRead(string path) => Path.GetExtension(path).Equals(".dawproject", StringComparison.OrdinalIgnoreCase);

    public async Task<Result<ProjectSnapshot>> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, true);
        using var archive = new ZipArchive(file, ZipArchiveMode.Read, leaveOpen: false);
        if (archive.Entries.Count > MaxEntryCount)
            return Result<ProjectSnapshot>.Fail("ARCHIVE_LIMIT", "Archive contains too many entries.");
        // Only project.xml and metadata.xml are opened, each with bounded XML size,
        // ratio and depth checks. Media and opaque state payloads are never opened;
        // their declared sizes must not reject otherwise bounded metadata inspection.
        cancellationToken.ThrowIfCancellationRequested();
        // The archive is never extracted. Do not accept ambiguous duplicate project.xml entries.
        var candidates = archive.Entries.Where(e => e.FullName == "project.xml").Take(2).ToList();
        if (candidates.Count > 1)
            return Result<ProjectSnapshot>.Fail("DUPLICATE_PROJECT_XML", "Archive contains duplicate project.xml entries.");
        var entry = candidates.FirstOrDefault();
        if (entry is null) return Result<ProjectSnapshot>.Fail("NO_PROJECT_XML", "DAWproject archive has no project.xml.");
        if (entry.Length > MaxXmlLength) return Result<ProjectSnapshot>.Fail("XML_TOO_LARGE", "project.xml exceeds the 16 MiB safety limit.");
        if (entry.Length > 4096 && entry.Length > Math.Max(1L, entry.CompressedLength) * 200L)
            return Result<ProjectSnapshot>.Fail("ARCHIVE_RATIO_LIMIT", "project.xml compression ratio exceeds the safety limit.");
        if (!await HasSafeXmlDepthAsync(entry, cancellationToken))
            return Result<ProjectSnapshot>.Fail("XML_DEPTH_LIMIT", "project.xml exceeds the 128-level XML nesting limit.");
        using var input = entry.Open();
        using var reader = XmlReader.Create(input, new XmlReaderSettings
        {
            Async = true, DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
            MaxCharactersInDocument = MaxXmlLength, MaxCharactersFromEntities = 0,
            IgnoreComments = true
        });
        var xml = await XDocument.LoadAsync(reader, LoadOptions.None, cancellationToken);
        if (xml.Root?.Name.LocalName != "Project")
            return Result<ProjectSnapshot>.Fail("INVALID_XML", "Root XML element is not Project.");
        var root = xml.Root;
        var transport = Child(root, "Transport");
        var tempoText = Attr(Child(transport, "Tempo"), "value");
        double? bpm = double.TryParse(tempoText, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedBpm)
            && parsedBpm is > 0 and < 1000 ? parsedBpm : null;
        var timeSignature = Child(transport, "TimeSignature");
        // DAWproject XSD declares xs:int, not a power-of-two denominator restriction.
        var numerator = int.TryParse(Attr(timeSignature, "numerator"), out var top) && top > 0
            ? top : (int?)null;
        var denominator = int.TryParse(Attr(timeSignature, "denominator"), out var bottom) && bottom > 0
            ? bottom : (int?)null;
        if (!numerator.HasValue || !denominator.HasValue)
            numerator = denominator = null;
        var tracks = new List<TrackInfo>();
        var plugins = new List<PluginReference>();
        var routing = new List<RoutingEdge>();
        var warnings = new List<string>();
        var media = new List<ProjectMediaReference>();
        var midiNotes = new List<ProjectMidiNote>();
        var eqBands = new List<ProjectEqBand>();
        var warpPoints = new List<ProjectWarpPoint>();
        // The published DAWproject schema places audio file references in the
        // arrangement timeline. Never open, extract or resolve these paths.
        foreach (var audio in root.Descendants().Where(x => x.Name.LocalName == "Audio"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (media.Count >= MaxMediaReferences)
                throw new InvalidDataException("Audio media-reference count exceeds safety limit.");
            var source = Attr(Child(audio, "File"), "path");
            if (string.IsNullOrWhiteSpace(source) || source.Length > 4096)
            {
                warnings.Add("An Audio element has no bounded File path; media reference omitted.");
                continue;
            }
            var rate = int.TryParse(Attr(audio, "sampleRate"), out var parsedRate) &&
                parsedRate is > 0 and <= 768000 ? parsedRate : (int?)null;
            var channels = int.TryParse(Attr(audio, "channels"), out var parsedChannels) &&
                parsedChannels is > 0 and <= 1024 ? parsedChannels : (int?)null;
            var duration = Numeric(Attr(audio, "duration"));
            var mediaTrackId = audio.Ancestors().Where(element => element.Name.LocalName == "Lanes")
                .Select(element => Attr(element, "track")).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
            media.Add(new ProjectMediaReference(source, Boolean(Attr(Child(audio, "File"), "external")),
                channels, rate, duration is >= 0 ? duration : null, mediaTrackId));
        }
        var timeline = new List<ProjectTimelineItem>();
        foreach (var element in root.Descendants().Where(x =>
            x.Name.LocalName is "Clip" or "Marker"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (timeline.Count >= MaxTimelineItems)
                throw new InvalidDataException("Declared timeline item count exceeds safety limit.");
            var time = Numeric(Attr(element, "time"));
            if (time is not >= 0)
            {
                warnings.Add("Malformed or missing Clip/Marker time: item omitted.");
                continue;
            }
            var duration = Numeric(Attr(element, "duration"));
            timeline.Add(new ProjectTimelineItem(
                element.Name.LocalName, Attr(element, "name"), time.Value,
                duration is >= 0 ? duration : null,
                Attr(element, "reference"), Boolean(Attr(element, "enable"))));
        }
        foreach (var notesElement in root.Descendants().Where(x => x.Name.LocalName == "Notes"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var trackId = Attr(notesElement, "track") ?? notesElement.Ancestors()
                .Where(element => element.Name.LocalName == "Lanes")
                .Select(element => Attr(element, "track")).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
            if (string.IsNullOrWhiteSpace(trackId))
            {
                warnings.Add("A Notes lane has no declared track reference; its note events were omitted.");
                continue;
            }
            var timeUnit = notesElement.AncestorsAndSelf().Select(element => Attr(element, "timeUnit"))
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
            var nestedInClip = notesElement.Ancestors().TakeWhile(element => element.Name.LocalName != "Track")
                .Any(element => element.Name.LocalName is "Clip" or "ClipSlot" or "Scene");
            foreach (var note in notesElement.Elements().Where(x => x.Name.LocalName == "Note"))
            {
                if (midiNotes.Count >= MaxMidiNotes)
                    throw new InvalidDataException("MIDI note count exceeds safety limit.");
                var time = Numeric(Attr(note, "time"));
                var duration = Numeric(Attr(note, "duration"));
                var channel = int.TryParse(Attr(note, "channel"), out var parsedChannel) ? parsedChannel : (int?)null;
                var key = int.TryParse(Attr(note, "key"), out var parsedKey) ? parsedKey : (int?)null;
                if (time is not >= 0 || duration is not > 0 || channel is null || key is null)
                {
                    warnings.Add("Malformed DAWproject MIDI note (time, duration, channel, or key); event omitted.");
                    continue;
                }
                midiNotes.Add(new ProjectMidiNote(trackId, time.Value, duration.Value,
                    channel.Value, key.Value, Numeric(Attr(note, "vel")), Numeric(Attr(note, "rel")),
                    timeUnit, nestedInClip));
            }
        }
        var warpIndex = 0;
        foreach (var warps in root.Descendants().Where(element => element.Name.LocalName == "Warps"))
        {
            var trackId = warps.Ancestors().Where(element => element.Name.LocalName == "Lanes")
                .Select(element => Attr(element, "track")).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
            var timeUnit = warps.AncestorsAndSelf().Select(element => Attr(element, "timeUnit"))
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
            var pointIndex = 0;
            foreach (var point in warps.Elements().Where(element => element.Name.LocalName == "Warp"))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (warpPoints.Count >= MaxWarpPoints) throw new InvalidDataException("Warp point count exceeds safety limit.");
                var time = Numeric(Attr(point, "time"));
                var contentTime = Numeric(Attr(point, "contentTime"));
                var sourcePath = $"project.xml/Warps[{warpIndex}]/Warp[{pointIndex}]";
                if (time is null || contentTime is null)
                    warnings.Add($"Malformed warp coordinates at {sourcePath}; point omitted.");
                else warpPoints.Add(new ProjectWarpPoint(trackId, Attr(warps, "id"), pointIndex,
                    time.Value, contentTime.Value, timeUnit, Attr(warps, "contentTimeUnit"), sourcePath));
                pointIndex++;
            }
            warpIndex++;
        }
        var structure = Child(root, "Structure");
        if (structure is null) warnings.Add("No Structure element: track, channel and routing lists are empty.");
        if (bpm is null) warnings.Add("No valid static transport BPM; tempo automation may require independent analysis.");

        if (structure is not null)
        {
            foreach (var item in structure.Elements()) Visit(item, 0, null);
        }
        var application = Child(root, "Application");
        var metadata = await ReadMetadataAsync(archive, cancellationToken, warnings);
        return Result<ProjectSnapshot>.Ok(new ProjectSnapshot(path, Path.GetFileNameWithoutExtension(path),
            "DAWproject (structured)", Attr(application, "name") + " " + Attr(application, "version"),
            bpm, EvidenceLevel.Confirmed, tracks, plugins, routing, warnings,
            numerator, denominator, media, timeline, metadata, midiNotes, EqBands: eqBands, WarpPoints: warpPoints,
            HasTempoAutomation: root.Descendants().Any(element => element.Name.LocalName == "TempoAutomation" &&
                element.Descendants().Any(child => child.Name.LocalName is "Point" or "RealPoint"))));

        void Visit(XElement item, int nesting, string? parentTrackId)
        {
            if (nesting > MaxTrackNesting)
                throw new InvalidDataException("Nested DAWproject track depth exceeds the safety limit.");
            if (item.Name.LocalName == "Track")
            {
                if (tracks.Count >= MaxTrackCount) throw new InvalidDataException("Track count exceeds safety limit.");
                var id = Attr(item, "id") ?? $"track-index-{tracks.Count}";
                var channel = Child(item, "Channel");
                var channelId = Attr(channel, "id");
                var name = Attr(item, "name") ?? id;
                var role = Attr(channel, "role");
                var content = Attr(item, "contentType");
                var kind = role switch
                {
                    "master" => "Master", "effect" => "FX", "submix" or "group" => "Group",
                    _ when content?.Contains("notes", StringComparison.OrdinalIgnoreCase) == true => "Instrument/MIDI",
                    _ when content?.Contains("audio", StringComparison.OrdinalIgnoreCase) == true => "Audio",
                    _ => "Track"
                };
                tracks.Add(new TrackInfo(id, name, kind, channelId,
                    Attr(channel, "destination"), Numeric(Attr(Child(channel, "Volume"), "value")),
                    Boolean(Attr(Child(channel, "Mute"), "value")), Boolean(Attr(channel, "solo")),
                    ParentTrackId: parentTrackId,
                    Color: Attr(item, "color"), Comment: Attr(item, "comment"),
                    Loaded: Boolean(Attr(item, "loaded")),
                    Pan: Numeric(Attr(Child(channel, "Pan"), "value")), ChannelRole: role));
                if (channel is not null) VisitChannel(channel, id);
                foreach (var nested in item.Elements().Where(e => e.Name.LocalName == "Track")) Visit(nested, nesting + 1, id);
            }
            else if (item.Name.LocalName == "Channel")
            {
                if (tracks.Count >= MaxTrackCount) throw new InvalidDataException("Channel count exceeds safety limit.");
                var id = Attr(item, "id") ?? $"channel-index-{tracks.Count}";
                tracks.Add(new TrackInfo(id, Attr(item, "name") ?? id,
                    Attr(item, "role") ?? "Channel", id, Attr(item, "destination"),
                    Numeric(Attr(Child(item, "Volume"), "value")),
                    Boolean(Attr(Child(item, "Mute"), "value")), Boolean(Attr(item, "solo")),
                    Color: Attr(item, "color"), Comment: Attr(item, "comment"),
                    Pan: Numeric(Attr(Child(item, "Pan"), "value")), ChannelRole: Attr(item, "role")));
                VisitChannel(item, id);
            }
        }

        void VisitChannel(XElement channel, string trackId)
        {
            var channelId = Attr(channel, "id");
            var destination = Attr(channel, "destination");
            if (!string.IsNullOrWhiteSpace(channelId) && !string.IsNullOrWhiteSpace(destination))
            {
                if (routing.Count >= MaxRoutingCount) throw new InvalidDataException("Routing count exceeds safety limit.");
                routing.Add(new RoutingEdge(channelId, destination, "Output"));
            }
            var sends = Child(channel, "Sends");
            if (sends is not null)
                foreach (var send in sends.Elements().Where(e => e.Name.LocalName == "Send"))
                {
                    var target = Attr(send, "destination");
                    if (!string.IsNullOrWhiteSpace(channelId) && !string.IsNullOrWhiteSpace(target))
                    {
                        if (routing.Count >= MaxRoutingCount) throw new InvalidDataException("Routing count exceeds safety limit.");
                        routing.Add(new RoutingEdge(channelId, target, "Send",
                            Numeric(Attr(Child(send, "Volume"), "value")),
                            Boolean(Attr(Child(send, "Enable"), "value")),
                            Attr(send, "type")));
                    }
                }
            var devices = Child(channel, "Devices");
            if (devices is null) return;
            var slot = 0;
            foreach (var device in devices.Elements())
            {
                var tag = device.Name.LocalName;
                if (!tag.EndsWith("Plugin", StringComparison.Ordinal) && !new[]
                    { "Device", "BuiltinDevice", "Compressor", "Equalizer", "Limiter", "NoiseGate" }.Contains(tag))
                    continue;
                if (plugins.Count >= MaxPluginCount) throw new InvalidDataException("Plugin count exceeds safety limit.");
                var stateReference = Attr(Child(device, "State"), "path");
                if (stateReference?.Length > 4096)
                {
                    warnings.Add("A plug-in state reference exceeds the path safety limit and was omitted.");
                    stateReference = null;
                }
                plugins.Add(new PluginReference(
                    Attr(device, "deviceID") ?? Attr(device, "pluginID") ?? Attr(device, "id") ?? $"device-{plugins.Count}",
                    Attr(device, "deviceName") ?? Attr(device, "name") ?? tag,
                    Attr(device, "deviceVendor") ?? Attr(device, "vendor"),
                    tag, trackId, EvidenceLevel.Confirmed,
                    Attr(device, "pluginVersion"),
                    Attr(device, "deviceRole"), slot++, Boolean(Attr(Child(device, "Enabled"), "value")),
                    InstanceId: Attr(device, "id"), StateReference: stateReference));
                if (tag == "Equalizer")
                {
                    var bandIndex = 0;
                    foreach (var band in device.Elements().Where(element => element.Name.LocalName == "Band"))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (eqBands.Count >= MaxEqBands) throw new InvalidDataException("EQ band count exceeds safety limit.");
                        var frequency = Child(band, "Freq");
                        var gain = Child(band, "Gain");
                        var q = Child(band, "Q");
                        if (Numeric(Attr(frequency, "value")) is null)
                            warnings.Add($"Track {trackId}, EQ band {bandIndex + 1}: missing or malformed frequency.");
                        foreach (var parameter in new[] { gain, q })
                            if (Attr(parameter, "value") is { } value && Numeric(value) is null)
                                warnings.Add($"Track {trackId}, EQ band {bandIndex + 1}: malformed {parameter!.Name.LocalName} value.");
                        eqBands.Add(new ProjectEqBand(trackId, Attr(device, "id"), bandIndex,
                            Attr(band, "type"), int.TryParse(Attr(band, "order"), out var order) ? order : null,
                            Numeric(Attr(frequency, "value")), Attr(frequency, "unit"),
                            Numeric(Attr(gain, "value")), Attr(gain, "unit"),
                            Numeric(Attr(q, "value")), Attr(q, "unit"), Boolean(Attr(Child(band, "Enabled"), "value")),
                            $"project.xml/Structure/Channel[{channelId}]/Devices/Equalizer[{Attr(device, "id")}]/Band[{bandIndex}]"));
                        bandIndex++;
                    }
                }
            }
        }
    }

    private static XElement? Child(XElement? parent, string name) => parent?.Elements().FirstOrDefault(x => x.Name.LocalName == name);
    private static string? Attr(XElement? el, string name) => (string?)el?.Attribute(name);
    private static double? Numeric(string? value) => double.TryParse(value, NumberStyles.Float,
        CultureInfo.InvariantCulture, out var x) && double.IsFinite(x) ? x : null;
    private static bool? Boolean(string? value) => bool.TryParse(value, out var b) ? b : null;

    private static async Task<ProjectMetadata?> ReadMetadataAsync(ZipArchive archive,
        CancellationToken cancellationToken, List<string> warnings)
    {
        var matches = archive.Entries.Where(entry =>
            string.Equals(entry.FullName, "metadata.xml", StringComparison.OrdinalIgnoreCase)).Take(2).ToArray();
        if (matches.Length > 1)
        {
            warnings.Add("Archive has duplicate metadata.xml entries; user metadata was not selected.");
            return null;
        }
        if (matches.Length == 0) return null;
        var entry = matches[0];
        if (entry.Length > MaxXmlLength ||
            (entry.Length > 4096 && entry.Length > Math.Max(1L, entry.CompressedLength) * 200L))
        {
            warnings.Add("metadata.xml exceeds the configured XML size or compression-ratio limit; user metadata was omitted.");
            return null;
        }
        try
        {
            if (!await HasSafeXmlDepthAsync(entry, cancellationToken))
            {
                warnings.Add("metadata.xml exceeds the 128-level XML nesting limit; user metadata was omitted.");
                return null;
            }
            using var input = entry.Open();
            using var reader = XmlReader.Create(input, new XmlReaderSettings
            {
                Async = true, DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
                MaxCharactersInDocument = MaxXmlLength, MaxCharactersFromEntities = 0, IgnoreComments = true
            });
            var xml = await XDocument.LoadAsync(reader, LoadOptions.None, cancellationToken);
            if (xml.Root?.Name.LocalName != "MetaData")
            {
                warnings.Add("metadata.xml has an unexpected root element; user metadata was omitted.");
                return null;
            }
            string? Value(string name)
            {
                var value = Child(xml.Root, name)?.Value;
                return string.IsNullOrWhiteSpace(value) ? null : value;
            }
            return new ProjectMetadata(Value("Title"), Value("Artist"), Value("Album"),
                Value("OriginalArtist"), Value("Composer"), Value("Songwriter"), Value("Producer"),
                Value("Arranger"), Value("Year"), Value("Genre"), Value("Copyright"),
                Value("Website"), Value("Comment"));
        }
        catch (XmlException)
        {
            warnings.Add("metadata.xml is malformed; user metadata was omitted while project.xml data remains available.");
            return null;
        }
    }

    private static async Task<bool> HasSafeXmlDepthAsync(ZipArchiveEntry entry,
        CancellationToken cancellationToken)
    {
        using var input = entry.Open();
        using var reader = XmlReader.Create(input, new XmlReaderSettings
        {
            Async = true, DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
            MaxCharactersInDocument = MaxXmlLength, MaxCharactersFromEntities = 0,
            IgnoreComments = true
        });
        while (await reader.ReadAsync())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (reader.Depth > MaxXmlDepth) return false;
        }
        return true;
    }
}
