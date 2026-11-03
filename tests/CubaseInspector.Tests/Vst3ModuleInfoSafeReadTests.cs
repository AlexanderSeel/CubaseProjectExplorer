using CubaseInspector.Infrastructure;
using Xunit;

namespace CubaseInspector.Tests;

public sealed class Vst3ModuleInfoSafeReadTests
{
    private const string Cid = "0123456789ABCDEF0123456789ABCDEF";
    private const string Json = """
    {"Classes":[{"Category":"Audio Module Class","CID":"0123456789ABCDEF0123456789ABCDEF",
      "Name":"Synth","Vendor":"Maker"}]}
    """;

    [Fact]
    public void ReadsSidecarOnlyFromRealBundleDirectory()
    {
        var root = Directory.CreateTempSubdirectory("vst-moduleinfo-");
        try
        {
            var resources = Directory.CreateDirectory(Path.Combine(root.FullName,
                "Contents", "Resources"));
            File.WriteAllText(Path.Combine(resources.FullName, "moduleinfo.json"), Json);
            var klass = Assert.Single(Vst3ModuleInfoReader.Read(root.FullName));
            Assert.Equal(Cid, klass.Cid);
            Assert.Equal("Synth", klass.Name);
        }
        finally { root.Delete(true); }
    }

    [Fact]
    public void DoesNotFollowLinkedContentsDirectory()
    {
        var root = Directory.CreateTempSubdirectory("vst-moduleinfo-linked-");
        var outside = Directory.CreateTempSubdirectory("vst-moduleinfo-external-");
        try
        {
            var resources = Directory.CreateDirectory(Path.Combine(outside.FullName, "Resources"));
            File.WriteAllText(Path.Combine(resources.FullName, "moduleinfo.json"), Json);
            try { Directory.CreateSymbolicLink(Path.Combine(root.FullName, "Contents"), outside.FullName); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                NotSupportedException or System.Security.SecurityException)
            { return; } // Symlink privilege is not guaranteed in Windows CI.
            Assert.Empty(Vst3ModuleInfoReader.Read(root.FullName));
        }
        finally
        {
            root.Delete(true);
            outside.Delete(true);
        }
    }
}
