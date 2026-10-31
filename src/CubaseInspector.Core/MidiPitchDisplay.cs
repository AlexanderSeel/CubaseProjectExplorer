namespace CubaseInspector.Core;

/// <summary>Musician-friendly pitch-class label that avoids assuming a DAW octave convention.</summary>
public static class MidiPitchDisplay
{
    private static readonly string[] PitchClasses =
        ["C", "C♯", "D", "D♯", "E", "F", "F♯", "G", "G♯", "A", "A♯", "B"];

    public static string Format(int midiPitch) => midiPitch is >= 0 and <= 127
        ? $"{PitchClasses[midiPitch % PitchClasses.Length]} · MIDI {midiPitch}"
        : $"MIDI {midiPitch}";
}
