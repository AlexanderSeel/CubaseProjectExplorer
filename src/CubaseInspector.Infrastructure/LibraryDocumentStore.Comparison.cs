using System.Security.Cryptography;
using System.Text.Json;
using CubaseInspector.Core;

namespace CubaseInspector.Infrastructure;

public sealed class LibraryTrackMatchesDocument
{
    public string Id { get; set; } = "";
    public string BeforePath { get; set; } = "";
    public string AfterPath { get; set; } = "";
    public string BeforeFingerprint { get; set; } = "";
    public string AfterFingerprint { get; set; } = "";
    public DateTime SavedUtc { get; set; }
    public string Provenance { get; set; } = "User-specified track matches";
    public IReadOnlyList<TrackMatchDecision> Decisions { get; set; } = [];
}

public sealed partial class LibraryDocumentStore
{
    public async Task SaveTrackMatchesAsync(ProjectSnapshot before, ProjectSnapshot after,
        IReadOnlyList<TrackMatchDecision> decisions, CancellationToken ct = default)
    {
        ProjectTrackMatching.Validate(before, after, decisions);
        if (before.Evidence != EvidenceLevel.Confirmed || after.Evidence != EvidenceLevel.Confirmed)
            throw new ArgumentException("Track matching requires two structured exports.");
        await _store.Upsert(new LibraryTrackMatchesDocument
        {
            Id = ComparisonPairId(before, after),
            BeforePath = Path.GetFullPath(before.FilePath), AfterPath = Path.GetFullPath(after.FilePath),
            BeforeFingerprint = ComparisonFingerprint(before), AfterFingerprint = ComparisonFingerprint(after),
            SavedUtc = DateTime.UtcNow, Decisions = decisions.ToArray()
        }, patchIfUpdate: false, cancellationToken: ct);
    }

    public async Task<LibraryTrackMatchesDocument?> GetTrackMatchesAsync(ProjectSnapshot before,
        ProjectSnapshot after, CancellationToken ct = default)
    {
        var saved = await _store.Get<LibraryTrackMatchesDocument>(ComparisonPairId(before, after), cancellationToken: ct);
        var beforeFingerprint = ComparisonFingerprint(before);
        var afterFingerprint = ComparisonFingerprint(after);
        if (saved is null || saved.BeforeFingerprint != beforeFingerprint || saved.AfterFingerprint != afterFingerprint)
        {
            var reversed = await _store.Get<LibraryTrackMatchesDocument>(ComparisonPairId(after, before), cancellationToken: ct);
            if (reversed is null || reversed.BeforeFingerprint != afterFingerprint || reversed.AfterFingerprint != beforeFingerprint)
                return null;
            try { ProjectTrackMatching.Validate(after, before, reversed.Decisions); }
            catch (ArgumentException) { return null; }
            var inverse = reversed.Decisions.Where(decision => decision.AfterIndex.HasValue)
                .ToDictionary(decision => decision.AfterIndex!.Value, decision => decision.BeforeIndex);
            saved = new LibraryTrackMatchesDocument
            {
                Id = ComparisonPairId(before, after), BeforePath = reversed.AfterPath, AfterPath = reversed.BeforePath,
                BeforeFingerprint = beforeFingerprint, AfterFingerprint = afterFingerprint,
                SavedUtc = reversed.SavedUtc, Provenance = reversed.Provenance,
                Decisions = Enumerable.Range(0, before.Tracks.Count).Select(index =>
                    new TrackMatchDecision(index, inverse.TryGetValue(index, out var target) ? target : null)).ToArray()
            };
        }
        // Database records are validated before use, including one-to-one constraints.
        try { ProjectTrackMatching.Validate(before, after, saved.Decisions); }
        catch (ArgumentException) { return null; }
        return saved;
    }

    private static string ComparisonPairId(ProjectSnapshot before, ProjectSnapshot after) =>
        StableId(JsonSerializer.Serialize(new[] { Path.GetFullPath(before.FilePath), Path.GetFullPath(after.FilePath) }));

    private static string ComparisonFingerprint(ProjectSnapshot project)
    {
        // Only parsed data used by comparison is fingerprinted. No audio/state
        // payload is opened or hashed, and changes to that data are not detected.
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new
        {
            Schema = 1, project.Format, project.CreatorVersion, project.Evidence, project.Bpm,
            project.Tracks, project.Plugins, project.Routing
        });
        return Convert.ToHexString(SHA256.HashData(bytes));
    }
}
