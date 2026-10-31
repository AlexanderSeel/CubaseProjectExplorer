namespace CubaseInspector.Core;

/// <summary>Plain-language wording for source confidence in user-facing views.</summary>
public static class EvidencePresentation
{
    public static string Label(EvidenceLevel level) => level switch
    {
        EvidenceLevel.Confirmed => "Read from project export",
        EvidenceLevel.Candidate => "Unverified CPR record",
        _ => "Not available"
    };

    public static string Explanation(EvidenceLevel level) => level switch
    {
        EvidenceLevel.Confirmed => "This value is explicitly present in the project export.",
        EvidenceLevel.Candidate => "The file contains a matching pattern, but Cubase has not confirmed what it represents.",
        _ => "This project file does not provide a reliable value. It does not mean the value is zero or absent."
    };
}
