namespace CubaseInspector.Core;

/// <summary>
/// A bounded class-header match, not a deserialized Cubase object. Containment,
/// prefix classes and symbol references describe the candidate byte grammar only.
/// Offsets ending in FileOffset are absolute; symbol offsets are ARCH-relative.
/// </summary>
public sealed record CprClassRecordCandidate(string ClassName, long HeaderFileOffset,
    long BodyFileOffset, long BodyBytes, int Depth, long? ParentHeaderFileOffset,
    string ClassEncoding, ulong ClassToken, ulong? ClassSymbolArchiveOffset,
    ushort? ClassVersion, IReadOnlyList<string> PrefixClasses,
    EvidenceLevel Evidence = EvidenceLevel.Candidate,
    IReadOnlyList<CprBinaryFieldCandidate>? RawFields = null);

/// <summary>
/// A bounded class-symbol declaration, including base classes which have no
/// independent object body. SymbolArchiveOffset is ARCH-relative;
/// DeclarationFileOffset is absolute. Revisions are class-specific, not releases.
/// AvailableAtFileOffset records when a successful header made the declaration
/// available to callbacks; lazy recovery can occur after its original location.
/// </summary>
public sealed record CprClassDeclarationCandidate(string ClassName, ushort ClassVersion,
    ulong SymbolArchiveOffset, long DeclarationFileOffset,
    EvidenceLevel Evidence = EvidenceLevel.Candidate, long? AvailableAtFileOffset = null);

/// <summary>Anonymous raw fields from a binary-derived layout; no musical units are asserted.</summary>
public sealed record CprBinaryFieldCandidate(string Field, string StorageType, string Value,
    long FileOffset, int ByteCount, string Source, EvidenceLevel Evidence = EvidenceLevel.Candidate,
    string? RawHex = null);

public sealed record CprUninterpretedRange(long FileOffset, int ByteCount);

/// <summary>
/// Detailed structural evidence for one ARCH payload. Uninterpreted bytes include
/// scalar/opaque data and unsupported headers; they are not necessarily corrupt.
/// Separate raw-field candidates may overlap these class-scan ranges.
/// Partial reports disclose safety limits, not a percentage of musical coverage.
/// </summary>
public sealed record CprArchiveRecordInventory(string StreamName, string? ArchiveClass,
    long PayloadFileOffset, long DeclaredBytes, int BytesScanned, int RecordsFound,
    bool Partial, IReadOnlyDictionary<string, int> ClassCounts,
    IReadOnlyList<CprClassRecordCandidate> Records, long UninterpretedBytes,
    IReadOnlyList<CprUninterpretedRange> UninterpretedRanges,
    IReadOnlyList<string> LimitReasons, string Grammar, int SymbolsFound = 0,
    IReadOnlyList<CprClassDeclarationCandidate>? ClassDeclarations = null);
