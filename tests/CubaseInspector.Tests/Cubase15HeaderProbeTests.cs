using System.Buffers.Binary;
using System.Text;
using CubaseInspector.Infrastructure;
using Xunit;

namespace CubaseInspector.Tests;

/// <summary>Synthetic envelope tests; not evidence of a fully decoded Cubase project.</summary>
public sealed class Cubase15HeaderProbeTests
{
    private static byte[] Header(string magic, ulong size, string form = "NUND")
    {
        var rif2 = magic == "RIF2";
        var bytes = new byte[rif2 ? 16 : 12];
        Encoding.ASCII.GetBytes(magic).CopyTo(bytes, 0);
        if (rif2)
            BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(4, 8), size);
        else
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(4, 4), checked((uint)size));
        Encoding.ASCII.GetBytes(form).CopyTo(bytes, bytes.Length - 4);
        return bytes;
    }

    [Theory]
    [InlineData("RIFF", 0x10203040UL, 12, CprContainerVariant.Riff32)]
    [InlineData("RIF2", 0x0102030405060708UL, 16, CprContainerVariant.Rif2_64)]
    public void ReadsBigEndianSizeAndRestoresStreamPosition(string magic, ulong size, int length, CprContainerVariant variant)
    {
        using var stream = new MemoryStream(Header(magic, size));
        stream.Position = 2;
        var result = Cubase15HeaderProbe.Read(stream);
        Assert.Equal(variant, result.Variant);
        Assert.Equal(size, result.DeclaredSize);
        Assert.Equal(length, result.HeaderLength);
        Assert.Equal(length, result.ActualFileLength);
        Assert.Equal("NUND", result.FormType);
        Assert.True(result.IsCubaseOrNuendoProject);
        Assert.Equal(2, stream.Position);
    }

    [Fact]
    public void RejectsUnrelatedFormAndTruncatedRif2()
    {
        using var wav = new MemoryStream(Header("RIFF", 4, "WAVE"));
        wav.Position = 3;
        Assert.Throws<InvalidDataException>(() => Cubase15HeaderProbe.Read(wav));
        Assert.Equal(3, wav.Position);
        using var truncated = new MemoryStream(Encoding.ASCII.GetBytes("RIF2"));
        Assert.Throws<EndOfStreamException>(() => Cubase15HeaderProbe.Read(truncated));
    }

    [Fact]
    public void RecognizesRif2BackupByHeaderOnly()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".bak");
        try
        {
            File.WriteAllBytes(path, Header("RIF2", 123));
            Assert.True(CprFileIdentity.HasCprHeader(path));
            Assert.True(CprFileIdentity.IsCprOrValidBackup(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task RIF2StructureRejectsDeclaredSizeBeyondFile()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".cpr");
        try
        {
            await File.WriteAllBytesAsync(path, Header("RIF2", 123));
            var result = await new CprStructureInspector().InspectAsync(path);
            Assert.False(result.IsSuccess);
            Assert.Equal("TRUNCATED_CPR", result.Error?.Code);
        }
        finally { File.Delete(path); }
    }
}
