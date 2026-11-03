using System.Globalization;
using System.Xml;
using CubaseInspector.Core;

namespace CubaseInspector.Infrastructure;

/// <summary>Forward-only parser for project.xml. It retains normalized records, never an XML object tree.</summary>
internal sealed class DawprojectStreamingParser
{
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
    private static readonly HashSet<string> BuiltinDevices = new(StringComparer.Ordinal)
        { "Device", "BuiltinDevice", "Compressor", "Equalizer", "Limiter", "NoiseGate" };

    private sealed class TrackContext(string id, int rowIndex, int nesting, string? contentType)
    {
        public string Id { get; } = id;
        public int RowIndex { get; } = rowIndex;
        public int Nesting { get; } = nesting;
        public string? ContentType { get; } = contentType;
    }

    private sealed class ChannelContext(int? ownerTrackIndex, string? id)
    {
        public int? OwnerTrackIndex { get; } = ownerTrackIndex;
        public string? Id { get; } = id;
        public int PluginSlot { get; set; }
    }

    private sealed class DeviceContext(int pluginIndex, string tag, string? id)
    {
        public int PluginIndex { get; } = pluginIndex;
        public string Tag { get; } = tag;
        public string? Id { get; } = id;
        public int BandIndex { get; set; }
    }

    private sealed class SendContext(string? destination, string? type)
    {
        public string? Destination { get; } = destination;
        public string? Type { get; } = type;
        public double? Volume { get; set; }
        public bool? Enabled { get; set; }
        public bool InvalidGain { get; set; }
        public bool InvalidQ { get; set; }
    }

    private sealed class AudioContext(string? trackId, int? channels, int? sampleRate, double? duration)
    {
        public string? Path { get; set; }
        public string? TrackId { get; } = trackId;
        public int? Channels { get; } = channels;
        public int? SampleRate { get; } = sampleRate;
        public double? Duration { get; } = duration;
        public bool? External { get; set; }
    }

    private sealed class NotesContext(string? trackId, string? timeUnit, bool nestedInClip)
    {
        public string? TrackId { get; } = trackId;
        public string? TimeUnit { get; } = timeUnit;
        public bool NestedInClip { get; } = nestedInClip;
    }

    private sealed class WarpsContext(string? trackId, string? id, string? timeUnit, string? contentTimeUnit,
        int sourceIndex)
    {
        public string? TrackId { get; } = trackId;
        public string? Id { get; } = id;
        public string? TimeUnit { get; } = timeUnit;
        public string? ContentTimeUnit { get; } = contentTimeUnit;
        public int SourceIndex { get; } = sourceIndex;
        public int PointIndex { get; set; }
    }

    private sealed class BandContext(string sourcePath, int bandIndex, string? trackId, string? deviceId,
        string? type, int? order)
    {
        public string SourcePath { get; } = sourcePath;
        public int BandIndex { get; } = bandIndex;
        public string? TrackId { get; } = trackId;
        public string? DeviceId { get; } = deviceId;
        public string? Type { get; } = type;
        public int? Order { get; } = order;
        public double? Frequency { get; set; }
        public string? FrequencyUnit { get; set; }
        public double? Gain { get; set; }
        public string? GainUnit { get; set; }
        public double? Q { get; set; }
        public string? QUnit { get; set; }
        public bool? Enabled { get; set; }
        public bool InvalidGain { get; set; }
        public bool InvalidQ { get; set; }
    }

    private sealed class Frame(string name)
    {
        public string Name { get; } = name;
        public bool InStructure { get; set; }
        public bool InArrangement { get; set; }
        public bool InTempoAutomation { get; set; }
        public bool ClipNestingSinceTrack { get; set; }
        public string? LaneTrackId { get; set; }
        public string? TimeUnit { get; set; }
        public TrackContext? Track { get; set; }
        public ChannelContext? Channel { get; set; }
        public DeviceContext? Device { get; set; }
        public SendContext? Send { get; set; }
        public AudioContext? Audio { get; set; }
        public NotesContext? Notes { get; set; }
        public WarpsContext? Warps { get; set; }
        public BandContext? Band { get; set; }
    }

    private readonly string _path;
    private readonly CancellationToken _cancellationToken;
    private readonly List<Frame> _frames = [];
    private readonly List<TrackInfo> _tracks = [];
    private readonly List<PluginReference> _plugins = [];
    private readonly List<RoutingEdge> _routing = [];
    private readonly List<string> _warnings = [];
    private readonly List<ProjectMediaReference> _media = [];
    private readonly List<ProjectTimelineItem> _timeline = [];
    private readonly List<ProjectMidiNote> _midiNotes = [];
    private readonly List<ProjectEqBand> _eqBands = [];
    private readonly List<ProjectWarpPoint> _warpPoints = [];
    private string? _applicationName;
    private string? _applicationVersion;
    private double? _bpm;
    private int? _timeSignatureNumerator;
    private int? _timeSignatureDenominator;
    private bool _hasStructure;
    private bool _hasTempoAutomation;
    private int _warpElementIndex;
    private bool _rootSeen;

    private DawprojectStreamingParser(string path, CancellationToken cancellationToken)
    {
        _path = path;
        _cancellationToken = cancellationToken;
    }

    public static async Task<Result<ProjectSnapshot>> ParseAsync(string path, XmlReader reader,
        CancellationToken cancellationToken)
    {
        var parser = new DawprojectStreamingParser(path, cancellationToken);
        try
        {
            await parser.ReadAsync(reader);
            return parser.Build();
        }
        catch (DawprojectXmlDepthLimitException)
        { return Result<ProjectSnapshot>.Fail("XML_DEPTH_LIMIT", "project.xml exceeds the 128-level XML nesting limit."); }
        catch (DawprojectInvalidRootException)
        { return Result<ProjectSnapshot>.Fail("INVALID_XML", "Root XML element is not Project."); }
    }

    private async Task ReadAsync(XmlReader reader)
    {
        while (await reader.ReadAsync())
        {
            _cancellationToken.ThrowIfCancellationRequested();
            if (reader.NodeType == XmlNodeType.Element)
            {
                if (reader.Depth > MaxXmlDepth)
                    throw new DawprojectXmlDepthLimitException();
                var frame = StartElement(reader);
                if (reader.IsEmptyElement) EndElement(frame);
                else _frames.Add(frame);
            }
            else if (reader.NodeType == XmlNodeType.EndElement && _frames.Count > 0)
            {
                var frame = _frames[^1];
                EndElement(frame);
                _frames.RemoveAt(_frames.Count - 1);
            }
        }
    }

    private Frame StartElement(XmlReader reader)
    {
        var name = reader.LocalName;
        var parent = _frames.Count == 0 ? null : _frames[^1];
        if (!_rootSeen)
        {
            _rootSeen = true;
            if (reader.Depth != 0 || name != "Project")
                throw new DawprojectInvalidRootException();
        }

        var frame = new Frame(name)
        {
            InStructure = name == "Structure" || parent?.InStructure == true,
            InArrangement = name == "Arrangement" || parent?.InArrangement == true,
            InTempoAutomation = name == "TempoAutomation" || parent?.InTempoAutomation == true,
            ClipNestingSinceTrack = name == "Track" ? false :
                parent?.ClipNestingSinceTrack == true || name is "Clip" or "ClipSlot" or "Scene",
            LaneTrackId = name == "Lanes" ? Nonblank(A(reader, "track")) ?? parent?.LaneTrackId : parent?.LaneTrackId,
            TimeUnit = Nonblank(A(reader, "timeUnit")) ?? parent?.TimeUnit,
            Track = parent?.Track,
            Channel = parent?.Channel,
            Device = parent?.Device,
            Send = parent?.Send,
            Audio = parent?.Audio,
            Notes = parent?.Notes,
            Warps = parent?.Warps,
            Band = parent?.Band
        };

        if (parent?.Name == "Project" && name == "Structure") _hasStructure = true;
        if (parent?.Name == "Project" && name == "Application")
        {
            _applicationName = A(reader, "name");
            _applicationVersion = A(reader, "version");
        }
        if (parent?.Name == "Transport")
        {
            if (name == "Tempo")
            {
                var tempo = Numeric(A(reader, "value"));
                _bpm = tempo is > 0 and < 1000 ? tempo : null;
            }
            else if (name == "TimeSignature")
            {
                var top = int.TryParse(A(reader, "numerator"), out var parsedTop) && parsedTop > 0
                    ? parsedTop : (int?)null;
                var bottom = int.TryParse(A(reader, "denominator"), out var parsedBottom) && parsedBottom > 0
                    ? parsedBottom : (int?)null;
                if (top.HasValue && bottom.HasValue)
                    (_timeSignatureNumerator, _timeSignatureDenominator) = (top, bottom);
                else (_timeSignatureNumerator, _timeSignatureDenominator) = (null, null);
            }
        }
        if (parent?.InTempoAutomation == true && name is "Point" or "RealPoint")
            _hasTempoAutomation = true;

        if (frame.InStructure && name == "Track" && (parent?.Name is "Structure" or "Track"))
            StartTrack(reader, frame, parent);
        else if (parent?.Name == "Structure" && name == "Channel")
            StartStandaloneChannel(reader, frame);

        if (frame.InStructure && name == "Channel" && parent?.Track is not null)
            StartTrackChannel(reader, frame, parent.Track);
        if (frame.InStructure && parent?.Name == "Channel" && name == "Volume")
            UpdateTrackChannel(frame.Channel, volume: Numeric(A(reader, "value")));
        if (frame.InStructure && parent?.Name == "Channel" && name == "Mute")
            UpdateTrackChannel(frame.Channel, mute: B(reader, "value"));
        if (frame.InStructure && parent?.Name == "Channel" && name == "Pan")
            UpdateTrackChannel(frame.Channel, pan: Numeric(A(reader, "value")));
        if (frame.InStructure && name == "Channel" && parent?.Track is not null)
            UpdateTrackChannel(frame.Channel, solo: B(reader, "solo"));

        if (frame.InStructure && parent?.Name == "Sends" && name == "Send" && frame.Channel is not null)
            frame.Send = new SendContext(A(reader, "destination"), A(reader, "type"));
        else if (frame.InStructure && parent?.Name == "Send" && name == "Volume" && frame.Send is not null)
            frame.Send.Volume = Numeric(A(reader, "value"));
        else if (frame.InStructure && parent?.Name == "Send" && name == "Enable" && frame.Send is not null)
            frame.Send.Enabled = B(reader, "value");

        if (frame.InStructure && parent?.Name == "Devices" && frame.Channel is not null && IsPlugin(name))
            StartPlugin(reader, frame);
        else if (frame.InStructure && frame.Device is not null && parent?.Name == frame.Device.Tag)
        {
            var plugin = _plugins[frame.Device.PluginIndex];
            if (name == "State")
            {
                var stateReference = A(reader, "path");
                if (stateReference?.Length > 4096)
                {
                    _warnings.Add("A plug-in state reference exceeds the path safety limit and was omitted.");
                    stateReference = null;
                }
                _plugins[frame.Device.PluginIndex] = plugin with { StateReference = stateReference };
            }
            else if (name == "Enabled")
                _plugins[frame.Device.PluginIndex] = plugin with { Enabled = B(reader, "value") };
        }

        if (frame.InStructure && parent?.Name == "Equalizer" && name == "Band" && frame.Device is not null)
            frame.Band = StartBand(reader, frame.Device, frame.Channel);
        else if (frame.Band is not null && parent?.Name == "Band")
        {
            switch (name)
            {
                case "Freq": frame.Band.Frequency = Numeric(A(reader, "value")); frame.Band.FrequencyUnit = A(reader, "unit"); break;
                case "Gain":
                    frame.Band.Gain = Numeric(A(reader, "value")); frame.Band.GainUnit = A(reader, "unit");
                    frame.Band.InvalidGain = A(reader, "value") is not null && frame.Band.Gain is null;
                    break;
                case "Q":
                    frame.Band.Q = Numeric(A(reader, "value")); frame.Band.QUnit = A(reader, "unit");
                    frame.Band.InvalidQ = A(reader, "value") is not null && frame.Band.Q is null;
                    break;
                case "Enabled": frame.Band.Enabled = B(reader, "value"); break;
            }
        }

        if (frame.InArrangement && name is "Clip" or "Marker") StartTimelineItem(reader, name);
        if (frame.InArrangement && name == "Audio")
            frame.Audio = new AudioContext(frame.LaneTrackId,
                PositiveInt(A(reader, "channels"), 1024), PositiveInt(A(reader, "sampleRate"), 768000),
                NonNegative(Numeric(A(reader, "duration"))));
        else if (frame.Audio is not null && parent?.Name == "Audio" && name == "File")
        {
            frame.Audio.Path = A(reader, "path");
            frame.Audio.External = B(reader, "external");
        }

        if (frame.InArrangement && name == "Notes")
        {
            var trackId = A(reader, "track") ?? frame.LaneTrackId;
            if (string.IsNullOrWhiteSpace(trackId))
                _warnings.Add("A Notes lane has no declared track reference; its note events were omitted.");
            frame.Notes = new NotesContext(trackId, frame.TimeUnit, parent?.ClipNestingSinceTrack == true);
        }
        if (frame.InArrangement && parent?.Name == "Notes" && name == "Note" && frame.Notes is { TrackId: not null } notes)
            AddMidiNote(reader, notes);

        if (frame.InArrangement && name == "Warps")
            frame.Warps = new WarpsContext(frame.LaneTrackId, A(reader, "id"), frame.TimeUnit,
                A(reader, "contentTimeUnit"), _warpElementIndex++);
        if (frame.InArrangement && parent?.Name == "Warps" && name == "Warp" && frame.Warps is { } warps)
            AddWarp(reader, warps);

        return frame;
    }

    private void EndElement(Frame frame)
    {
        if (frame.Audio is { } audio && frame.Name == "Audio")
        {
            if (_media.Count >= MaxMediaReferences) throw new InvalidDataException("Audio media-reference count exceeds safety limit.");
            if (string.IsNullOrWhiteSpace(audio.Path) || audio.Path.Length > 4096)
                _warnings.Add("An Audio element has no bounded File path; media reference omitted.");
            else _media.Add(new ProjectMediaReference(audio.Path, audio.External, audio.Channels,
                audio.SampleRate, audio.Duration, audio.TrackId));
        }
        if (frame.Send is { } send && frame.Name == "Send" && frame.Channel?.Id is { Length: > 0 } channelId &&
            !string.IsNullOrWhiteSpace(send.Destination))
        {
            AddRoute(new RoutingEdge(channelId, send.Destination!, "Send", send.Volume, send.Enabled, send.Type));
        }
        if (frame.Band is { } band && frame.Name == "Band")
        {
            if (_eqBands.Count >= MaxEqBands) throw new InvalidDataException("EQ band count exceeds safety limit.");
            if (band.Frequency is null)
                _warnings.Add($"Track {band.TrackId}, EQ band {band.BandIndex + 1}: missing or malformed frequency.");
            if (band.InvalidGain) _warnings.Add($"Track {band.TrackId}, EQ band {band.BandIndex + 1}: malformed Gain value.");
            if (band.InvalidQ) _warnings.Add($"Track {band.TrackId}, EQ band {band.BandIndex + 1}: malformed Q value.");
            _eqBands.Add(new ProjectEqBand(band.TrackId ?? "", band.DeviceId, band.BandIndex, band.Type,
                band.Order, band.Frequency, band.FrequencyUnit, band.Gain, band.GainUnit, band.Q,
                band.QUnit, band.Enabled, band.SourcePath));
        }
    }

    private void StartTrack(XmlReader reader, Frame frame, Frame? parent)
    {
        var parentTrack = parent?.Track;
        var nesting = (parentTrack?.Nesting ?? -1) + 1;
        if (nesting > MaxTrackNesting) throw new InvalidDataException("Nested DAWproject track depth exceeds the safety limit.");
        if (_tracks.Count >= MaxTrackCount) throw new InvalidDataException("Track count exceeds safety limit.");
        var id = A(reader, "id") ?? $"track-index-{_tracks.Count}";
        var index = _tracks.Count;
        _tracks.Add(new TrackInfo(id, A(reader, "name") ?? id, TrackKind(A(reader, "contentType"), null),
            null, null, null, null, null, ParentTrackId: parentTrack?.Id,
            Color: A(reader, "color"), Comment: A(reader, "comment"), Loaded: B(reader, "loaded")));
        frame.Track = new TrackContext(id, index, nesting, A(reader, "contentType"));
    }

    private void StartStandaloneChannel(XmlReader reader, Frame frame)
    {
        if (_tracks.Count >= MaxTrackCount) throw new InvalidDataException("Channel count exceeds safety limit.");
        var id = A(reader, "id") ?? $"channel-index-{_tracks.Count}";
        var index = _tracks.Count;
        var role = A(reader, "role");
        _tracks.Add(new TrackInfo(id, A(reader, "name") ?? id, role ?? "Channel", id,
            A(reader, "destination"), null, null, B(reader, "solo"),
            Color: A(reader, "color"), Comment: A(reader, "comment"),
            ChannelRole: role));
        frame.Channel = new ChannelContext(index, id);
        if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(A(reader, "destination")))
            AddRoute(new RoutingEdge(id, A(reader, "destination")!, "Output"));
    }

    private void StartTrackChannel(XmlReader reader, Frame frame, TrackContext track)
    {
        var id = A(reader, "id");
        var role = A(reader, "role");
        var old = _tracks[track.RowIndex];
        _tracks[track.RowIndex] = old with
        {
            Kind = TrackKind(track.ContentType, role),
            ChannelId = id,
            DestinationChannelId = A(reader, "destination"),
            Solo = B(reader, "solo"),
            ChannelRole = role
        };
        frame.Channel = new ChannelContext(track.RowIndex, id);
        if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(A(reader, "destination")))
            AddRoute(new RoutingEdge(id!, A(reader, "destination")!, "Output"));
    }

    private void UpdateTrackChannel(ChannelContext? channel, double? volume = null,
        double? pan = null, bool? mute = null, bool? solo = null)
    {
        if (channel?.OwnerTrackIndex is not int index) return;
        _tracks[index] = _tracks[index] with
        {
            Volume = volume ?? _tracks[index].Volume,
            Pan = pan ?? _tracks[index].Pan,
            Mute = mute ?? _tracks[index].Mute,
            Solo = solo ?? _tracks[index].Solo
        };
    }

    private void StartPlugin(XmlReader reader, Frame frame)
    {
        if (_plugins.Count >= MaxPluginCount) throw new InvalidDataException("Plugin count exceeds safety limit.");
        var tag = frame.Name;
        var id = A(reader, "id");
        var plugin = new PluginReference(A(reader, "deviceID") ?? A(reader, "pluginID") ?? id ?? $"device-{_plugins.Count}",
            A(reader, "deviceName") ?? A(reader, "name") ?? tag, A(reader, "deviceVendor") ?? A(reader, "vendor"),
            tag, frame.Channel?.OwnerTrackIndex is int trackIndex ? _tracks[trackIndex].Id : null,
            EvidenceLevel.Confirmed, A(reader, "pluginVersion"), A(reader, "deviceRole"),
            frame.Channel?.PluginSlot ?? 0, null, InstanceId: id);
        if (frame.Channel is not null) frame.Channel.PluginSlot++;
        _plugins.Add(plugin);
        frame.Device = new DeviceContext(_plugins.Count - 1, tag, id);
    }

    private BandContext StartBand(XmlReader reader, DeviceContext device, ChannelContext? channel)
    {
        var bandIndex = device.BandIndex++;
        var trackId = channel?.OwnerTrackIndex is int index ? _tracks[index].Id : null;
        var channelId = channel?.Id;
        var source = $"project.xml/Structure/Channel[{channelId}]/Devices/Equalizer[{device.Id}]/Band[{bandIndex}]";
        return new BandContext(source, bandIndex, trackId, device.Id, A(reader, "type"),
            int.TryParse(A(reader, "order"), out var order) ? order : null);
    }

    private void StartTimelineItem(XmlReader reader, string kind)
    {
        if (_timeline.Count >= MaxTimelineItems) throw new InvalidDataException("Declared timeline item count exceeds safety limit.");
        var time = Numeric(A(reader, "time"));
        if (time is not >= 0)
        {
            _warnings.Add("Malformed or missing Clip/Marker time: item omitted.");
            return;
        }
        var duration = Numeric(A(reader, "duration"));
        _timeline.Add(new ProjectTimelineItem(kind, A(reader, "name"), time.Value,
            duration is >= 0 ? duration : null, A(reader, "reference"), B(reader, "enable")));
    }

    private void AddMidiNote(XmlReader reader, NotesContext notes)
    {
        if (_midiNotes.Count >= MaxMidiNotes) throw new InvalidDataException("MIDI note count exceeds safety limit.");
        var time = Numeric(A(reader, "time"));
        var duration = Numeric(A(reader, "duration"));
        var channel = int.TryParse(A(reader, "channel"), out var parsedChannel) ? parsedChannel : (int?)null;
        var key = int.TryParse(A(reader, "key"), out var parsedKey) ? parsedKey : (int?)null;
        if (time is not >= 0 || duration is not > 0 || channel is null || key is null)
        {
            _warnings.Add("Malformed DAWproject MIDI note (time, duration, channel, or key); event omitted.");
            return;
        }
        _midiNotes.Add(new ProjectMidiNote(notes.TrackId!, time.Value, duration.Value,
            channel.Value, key.Value, Numeric(A(reader, "vel")), Numeric(A(reader, "rel")),
            notes.TimeUnit, notes.NestedInClip));
    }

    private void AddWarp(XmlReader reader, WarpsContext warps)
    {
        if (_warpPoints.Count >= MaxWarpPoints) throw new InvalidDataException("Warp point count exceeds safety limit.");
        var pointIndex = warps.PointIndex++;
        var time = Numeric(A(reader, "time"));
        var contentTime = Numeric(A(reader, "contentTime"));
        var source = $"project.xml/Warps[{warps.SourceIndex}]/Warp[{pointIndex}]";
        if (time is null || contentTime is null)
        {
            _warnings.Add($"Malformed warp coordinates at {source}; point omitted.");
            return;
        }
        _warpPoints.Add(new ProjectWarpPoint(warps.TrackId, warps.Id, pointIndex, time.Value,
            contentTime.Value, warps.TimeUnit, warps.ContentTimeUnit, source));
    }

    private void AddRoute(RoutingEdge edge)
    {
        if (_routing.Count >= MaxRoutingCount) throw new InvalidDataException("Routing count exceeds safety limit.");
        _routing.Add(edge);
    }

    private Result<ProjectSnapshot> Build()
    {
        if (!_rootSeen) return Result<ProjectSnapshot>.Fail("INVALID_XML", "Project XML is empty.");
        if (!_hasStructure) _warnings.Add("No Structure element: track, channel and routing lists are empty.");
        if (_bpm is null) _warnings.Add("No valid static transport BPM; tempo automation may require independent analysis.");
        return Result<ProjectSnapshot>.Ok(new ProjectSnapshot(_path, Path.GetFileNameWithoutExtension(_path),
            "DAWproject (structured)", _applicationName + " " + _applicationVersion,
            _bpm, EvidenceLevel.Confirmed, _tracks, _plugins, _routing, _warnings,
            _timeSignatureNumerator, _timeSignatureDenominator, _media, _timeline, null, _midiNotes,
            EqBands: _eqBands, WarpPoints: _warpPoints, HasTempoAutomation: _hasTempoAutomation));
    }

    private static bool IsPlugin(string tag) => tag.EndsWith("Plugin", StringComparison.Ordinal) || BuiltinDevices.Contains(tag);
    private static string TrackKind(string? content, string? role) => role switch
    {
        "master" => "Master", "effect" => "FX", "submix" or "group" => "Group",
        _ when content?.Contains("notes", StringComparison.OrdinalIgnoreCase) == true => "Instrument/MIDI",
        _ when content?.Contains("audio", StringComparison.OrdinalIgnoreCase) == true => "Audio",
        _ => "Track"
    };
    private static string? A(XmlReader reader, string name) => reader.GetAttribute(name);
    private static bool? B(XmlReader reader, string name) => Boolean(A(reader, name));
    private static double? Numeric(string? value) => double.TryParse(value, NumberStyles.Float,
        CultureInfo.InvariantCulture, out var parsed) && double.IsFinite(parsed) ? parsed : null;
    private static string? Nonblank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
    private static double? NonNegative(double? value) => value is >= 0 ? value : null;
    private static int? PositiveInt(string? value, int maximum) =>
        int.TryParse(value, out var parsed) && parsed is > 0 && parsed <= maximum ? parsed : null;
    private static bool? Boolean(string? value) => bool.TryParse(value, out var parsed) ? parsed : null;
    private sealed class DawprojectXmlDepthLimitException : Exception { }
    private sealed class DawprojectInvalidRootException : Exception { }
}
