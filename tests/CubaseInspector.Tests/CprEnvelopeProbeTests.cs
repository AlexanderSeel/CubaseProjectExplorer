using System.Text;
using CubaseInspector.Infrastructure;
using Xunit;

namespace CubaseInspector.Tests;

public sealed class CprEnvelopeProbeTests
{
    [Theory]
    [InlineData(0, 0, 0, 4, 4, 67_108_864)]
    [InlineData(4, 0, 0, 0, 67_108_864, 4)]
    public void ReportsBothSizeInterpretationsWithoutChangingStreamPosition(
        byte b0, byte b1, byte b2, byte b3, uint expectedBigEndian, uint expectedLittleEndian)
    {
        var bytes = new byte[16];
        Encoding.ASCII.GetBytes("RIFF").CopyTo(bytes, 0);
        bytes[4] = b0;
        bytes[5] = b1;
        bytes[6] = b2;
        bytes[7] = b3;
        Encoding.ASCII.GetBytes("NUND").CopyTo(bytes, 8);
        using var stream = new MemoryStream(bytes);
        stream.Position = 5;

        var result = CprEnvelopeProbe.Inspect(stream);

        Assert.True(result.HasRiffNundSignature);
        Assert.Equal(expectedBigEndian, result.SizeFieldBigEndian);
        Assert.Equal(expectedLittleEndian, result.SizeFieldLittleEndian);
        Assert.Equal(16L, result.ActualLength);
        Assert.Equal(expectedBigEndian == 4, result.BigEndianTotalLengthMatches);
        Assert.Equal(expectedLittleEndian == 4, result.LittleEndianTotalLengthMatches);
        Assert.Equal(5L, stream.Position);
    }

    [Fact]
    public void NonNundInputIsReportedAsNotRecognized()
    {
        using var stream = new MemoryStream(new byte[12]);

        var result = CprEnvelopeProbe.Inspect(stream);

        Assert.False(result.HasRiffNundSignature);
    }
}
