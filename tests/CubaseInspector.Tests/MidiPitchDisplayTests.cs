using CubaseInspector.Core;
using Xunit;

namespace CubaseInspector.Tests;

public sealed class MidiPitchDisplayTests
{
    [Theory]
    [InlineData(60, "C · MIDI 60")]
    [InlineData(61, "C♯ · MIDI 61")]
    [InlineData(69, "A · MIDI 69")]
    [InlineData(127, "G · MIDI 127")]
    public void FormatsPitchClassWithoutAssumingOctaveNaming(int pitch, string expected)
    {
        Assert.Equal(expected, MidiPitchDisplay.Format(pitch));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(128)]
    public void PreservesOutOfRangeValueAsMidiNumber(int pitch)
    {
        Assert.Equal($"MIDI {pitch}", MidiPitchDisplay.Format(pitch));
    }
}
