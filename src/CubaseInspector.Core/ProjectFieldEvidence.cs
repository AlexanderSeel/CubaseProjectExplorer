namespace CubaseInspector.Core;

/// <summary>
/// The state of a single decoded field. A missing value is not automatically numeric zero.
/// Candidate CPR evidence must never be marked as a confirmed exported field.
/// </summary>
public enum ProjectFieldState
{
    Present,
    Absent,
    NotExported,
    Unknown,
    Malformed,
    Candidate
}

public sealed record ProjectFieldEvidence<T>(
    ProjectFieldState State,
    T? Value,
    EvidenceLevel Evidence,
    string Source,
    long? SourceOffset = null)
{
    public static ProjectFieldEvidence<T> Confirmed(T value, string source, long? offset = null) =>
        new(ProjectFieldState.Present, value, EvidenceLevel.Confirmed, source, offset);

    public static ProjectFieldEvidence<T> Unavailable(ProjectFieldState state, string source) =>
        state is ProjectFieldState.Present or ProjectFieldState.Candidate
            ? throw new ArgumentException("Unavailable fields cannot claim present or candidate state.", nameof(state))
            : new(state, default, EvidenceLevel.Unknown, source);

    public static ProjectFieldEvidence<T> Unverified(T? value, string source, long? offset = null) =>
        new(ProjectFieldState.Candidate, value, EvidenceLevel.Candidate, source, offset);

    // For unconstrained generic value types, default(T) may be zero even when absent.
    // Callers must check HasValue/State before observing Value.
    public bool HasValue => State is ProjectFieldState.Present or ProjectFieldState.Candidate;
    public bool IsConfirmedValue => State == ProjectFieldState.Present && Evidence == EvidenceLevel.Confirmed;
}
