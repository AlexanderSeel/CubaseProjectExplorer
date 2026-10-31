using System.IO.Compression;
using System.Xml;
using CubaseInspector.Core;

namespace CubaseInspector.Infrastructure;

/// <summary>Parses DAWproject XML with a forward-only reader; never loads media or plugin binaries.</summary>
public sealed class DawprojectReader : IProjectReader
{
    private const long MaxXmlLength = 16 * 1024 * 1024;
    private const int MaxEntryCount = 5000;
    private const int MaxXmlDepth = 128;

    public bool CanRead(string path) =>
        Path.GetExtension(path).Equals(".dawproject", StringComparison.OrdinalIgnoreCase);

    public async Task<Result<ProjectSnapshot>> ReadAsync(string path, CancellationToken cancellationToken = default)
    {
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var archive = new ZipArchive(file, ZipArchiveMode.Read, leaveOpen: false);
        if (archive.Entries.Count > MaxEntryCount)
            return Result<ProjectSnapshot>.Fail("ARCHIVE_LIMIT", "Archive contains too many entries.");

        cancellationToken.ThrowIfCancellationRequested();
        var projectEntries = archive.Entries.Where(entry => entry.FullName == "project.xml").Take(2).ToArray();
        if (projectEntries.Length > 1)
            return Result<ProjectSnapshot>.Fail("DUPLICATE_PROJECT_XML", "Archive contains duplicate project.xml entries.");
        var projectEntry = projectEntries.FirstOrDefault();
        if (projectEntry is null)
            return Result<ProjectSnapshot>.Fail("NO_PROJECT_XML", "DAWproject archive has no project.xml.");
        if (projectEntry.Length > MaxXmlLength)
            return Result<ProjectSnapshot>.Fail("XML_TOO_LARGE", "project.xml exceeds the 16 MiB safety limit.");
        if (projectEntry.Length > 4096 && projectEntry.Length > Math.Max(1L, projectEntry.CompressedLength) * 200L)
            return Result<ProjectSnapshot>.Fail("ARCHIVE_RATIO_LIMIT", "project.xml compression ratio exceeds the safety limit.");

        Result<ProjectSnapshot> parsed;
        await using (var input = projectEntry.Open())
        using (var reader = XmlReader.Create(input, CreateSettings()))
            parsed = await DawprojectStreamingParser.ParseAsync(path, reader, cancellationToken);
        if (!parsed.IsSuccess || parsed.Value is not { } project) return parsed;

        var warnings = project.Warnings.ToList();
        var metadata = await ReadMetadataAsync(archive, cancellationToken, warnings);
        return Result<ProjectSnapshot>.Ok(project with { Metadata = metadata, Warnings = warnings });
    }

    private static async Task<ProjectMetadata?> ReadMetadataAsync(ZipArchive archive,
        CancellationToken cancellationToken, List<string> warnings)
    {
        var entries = archive.Entries.Where(entry =>
            string.Equals(entry.FullName, "metadata.xml", StringComparison.OrdinalIgnoreCase)).Take(2).ToArray();
        if (entries.Length > 1)
        {
            warnings.Add("Archive has duplicate metadata.xml entries; user metadata was not selected.");
            return null;
        }
        if (entries.Length == 0) return null;
        var entry = entries[0];
        if (entry.Length > MaxXmlLength ||
            (entry.Length > 4096 && entry.Length > Math.Max(1L, entry.CompressedLength) * 200L))
        {
            warnings.Add("metadata.xml exceeds the configured XML size or compression-ratio limit; user metadata was omitted.");
            return null;
        }

        try
        {
            await using var input = entry.Open();
            using var reader = XmlReader.Create(input, CreateSettings());
            string? root = null;
            var fields = new Dictionary<string, string?>(StringComparer.Ordinal);
            var advance = true;
            while (advance && await reader.ReadAsync() || !advance)
            {
                advance = true;
                cancellationToken.ThrowIfCancellationRequested();
                if (reader.Depth > MaxXmlDepth)
                {
                    warnings.Add("metadata.xml exceeds the 128-level XML nesting limit; user metadata was omitted.");
                    return null;
                }
                if (reader.NodeType != XmlNodeType.Element) continue;
                if (reader.Depth == 0)
                {
                    root = reader.LocalName;
                    continue;
                }
                if (reader.Depth != 1 || root != "MetaData") continue;
                var name = reader.LocalName;
                if (name is not ("Title" or "Artist" or "Album" or "OriginalArtist" or "Composer" or
                    "Songwriter" or "Producer" or "Arranger" or "Year" or "Genre" or "Copyright" or
                    "Website" or "Comment")) continue;
                var value = "";
                if (!reader.IsEmptyElement)
                {
                    value = await reader.ReadElementContentAsStringAsync();
                    advance = false;
                }
                fields[name] = string.IsNullOrWhiteSpace(value) ? null : value;
            }
            if (root != "MetaData")
            {
                warnings.Add("metadata.xml has an unexpected root element; user metadata was omitted.");
                return null;
            }

            string? Value(string name) => fields.GetValueOrDefault(name);
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

    private static XmlReaderSettings CreateSettings() => new()
    {
        Async = true,
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        MaxCharactersInDocument = MaxXmlLength,
        MaxCharactersFromEntities = 0,
        IgnoreComments = true
    };
}
