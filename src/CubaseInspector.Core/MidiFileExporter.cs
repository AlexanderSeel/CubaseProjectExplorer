using System.Buffers.Binary;

namespace CubaseInspector.Core;

/// <summary>Writes a minimal Standard MIDI File only for flat, well-typed DAWproject note lanes.</summary>
public static class MidiFileExporter
{
    public const int MaximumNotes = 100_000;
    private const int PulsesPerQuarterNote = 480;

    public static byte[] Export(ProjectSnapshot project, string trackId)
    {
        var index = ProjectAssetIndexer.Build(project);
        var matches = index.MidiTracks.Where(candidate => candidate.TrackId == trackId).Take(2).ToArray();
        if (matches.Length != 1) throw new InvalidOperationException("The selected project track has no unique confirmed MIDI export index.");
        var track = matches[0];
        if (!track.CanExportStandardMidi) throw new InvalidOperationException(track.ExportLimitation);
        var notes = (project.MidiNotes ?? []).Where(note => note.TrackId == trackId).ToArray();
        var seconds = track.TimeUnit == "seconds";
        var bpm = seconds ? 60d : project.Bpm!.Value;
        var microsecondsPerQuarter = checked((int)Math.Round(60_000_000d / bpm));
        var events = new List<MidiEvent>(notes.Length * 2 + 2)
        {
            new(0, 0, [0xFF, 0x51, 0x03,
                (byte)(microsecondsPerQuarter >> 16), (byte)(microsecondsPerQuarter >> 8), (byte)microsecondsPerQuarter])
        };
        foreach (var note in notes)
        {
            var start = ToTicks(note.Time);
            var end = ToTicks(note.Time + note.Duration);
            var velocity = ToMidiVelocity(note.Velocity, fallback: 100);
            var release = ToMidiVelocity(note.ReleaseVelocity, fallback: 0);
            events.Add(new(start, 1, [(byte)(0x90 | note.Channel), (byte)note.Key, (byte)velocity]));
            events.Add(new(end, 0, [(byte)(0x80 | note.Channel), (byte)note.Key, (byte)release]));
        }
        events.Sort((left, right) => left.Tick != right.Tick
            ? left.Tick.CompareTo(right.Tick) : left.Order.CompareTo(right.Order));
        using var trackData = new MemoryStream();
        long previousTick = 0;
        foreach (var midiEvent in events)
        {
            WriteVariableLength(trackData, checked((int)(midiEvent.Tick - previousTick)));
            trackData.Write(midiEvent.Data);
            previousTick = midiEvent.Tick;
        }
        trackData.Write([0x00, 0xFF, 0x2F, 0x00]);

        using var output = new MemoryStream();
        output.Write("MThd"u8); WriteUInt32(output, 6); WriteUInt16(output, 0); WriteUInt16(output, 1);
        WriteUInt16(output, PulsesPerQuarterNote);
        output.Write("MTrk"u8); WriteUInt32(output, checked((uint)trackData.Length));
        trackData.Position = 0; trackData.CopyTo(output);
        return output.ToArray();
    }

    private static long ToTicks(double value)
    {
        var ticks = Math.Round(value * PulsesPerQuarterNote, MidpointRounding.AwayFromZero);
        if (!double.IsFinite(ticks) || ticks < 0 || ticks > 0x0FFFFFFF)
            throw new InvalidOperationException("MIDI note timing exceeds the supported file range.");
        return (long)ticks;
    }

    private static int ToMidiVelocity(double? value, int fallback) => value is null
        ? fallback : Math.Clamp((int)Math.Round(value.Value * 127, MidpointRounding.AwayFromZero), 0, 127);

    private static void WriteVariableLength(Stream stream, int value)
    {
        Span<byte> buffer = stackalloc byte[4];
        var index = buffer.Length - 1;
        buffer[index] = (byte)(value & 0x7F);
        while ((value >>= 7) > 0) buffer[--index] = (byte)((value & 0x7F) | 0x80);
        stream.Write(buffer[index..]);
    }

    private static void WriteUInt16(Stream stream, ushort value)
    {
        Span<byte> bytes = stackalloc byte[2]; BinaryPrimitives.WriteUInt16BigEndian(bytes, value); stream.Write(bytes);
    }

    private static void WriteUInt32(Stream stream, uint value)
    {
        Span<byte> bytes = stackalloc byte[4]; BinaryPrimitives.WriteUInt32BigEndian(bytes, value); stream.Write(bytes);
    }

    private sealed record MidiEvent(long Tick, int Order, byte[] Data);
}
