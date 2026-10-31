using System.Buffers.Binary;
using System.Text;
using CubaseInspector.Core;
using CubaseInspector.Infrastructure;
using Xunit;

namespace CubaseInspector.Tests;

public sealed class CprArchiveRecordScannerTests
{
    private static byte[] Join(params byte[][] parts) => parts.SelectMany(p => p).ToArray();
    private static byte[] Number(ulong number, bool wide)
    {
        var bytes = new byte[wide ? 8 : 4];
        if (wide) BinaryPrimitives.WriteUInt64BigEndian(bytes, number);
        else BinaryPrimitives.WriteUInt32BigEndian(bytes, checked((uint)number));
        return bytes;
    }

    private static byte[] Name(string name, ushort version)
    {
        var text = Encoding.ASCII.GetBytes(name + "\0");
        var serializedVersion = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(serializedVersion, version);
        return Join(Number((uint)text.Length, false), text, serializedVersion);
    }

    private static byte[] Record(string name, byte[] body, bool wide, ushort version = 1) =>
        Join(Number(wide ? ulong.MaxValue : uint.MaxValue, wide), Name(name, version),
            Number((ulong)body.Length, wide), body);

    private static byte[] Reference(ulong id, byte[] body, bool wide) =>
        Join(Number((wide ? 0x8000000000000000UL : 0x80000000UL) | id, wide),
            Number((ulong)body.Length, wide), body);

    private static CprArchiveRecordInventory Scan(byte[] bytes, bool wide = false) =>
        CprArchiveRecordScanner.Scan("Arrangement1", "PArrangement", bytes, 1000, bytes.Length, wide);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RetainsUnknownClassOffsetsVersionsAndInternedInstances(bool wide)
    {
        var first = Record("FutureUnknownObject", [0x11, 0x22, 0x33], wide, 0x1234);
        var second = Reference(0, [], wide);
        var bytes = Join(first, second);
        var report = Scan(bytes, wide);

        Assert.False(report.Partial);
        Assert.Equal(2, report.RecordsFound);
        Assert.Equal(2, report.ClassCounts["FutureUnknownObject"]);
        Assert.Equal(3, report.UninterpretedBytes);
        var inline = report.Records[0];
        Assert.Equal(1000, inline.HeaderFileOffset);
        Assert.Equal(1000 + first.Length - 3, inline.BodyFileOffset);
        Assert.Equal(3, inline.BodyBytes);
        Assert.Equal((ushort)0x1234, inline.ClassVersion);
        Assert.Equal("inline-class-name", inline.ClassEncoding);
        Assert.Equal(0UL, inline.ClassSymbolArchiveOffset);
        Assert.Equal(EvidenceLevel.Candidate, inline.Evidence);
        var reference = report.Records[1];
        Assert.Equal(1000 + first.Length, reference.HeaderFileOffset);
        Assert.Equal("interned-class-reference", reference.ClassEncoding);
        Assert.Equal(0UL, reference.ClassSymbolArchiveOffset);
        Assert.Equal((ushort)0x1234, reference.ClassVersion);
        Assert.Null(reference.ParentHeaderFileOffset);
        var unknown = Assert.Single(report.UninterpretedRanges);
        Assert.Equal(inline.BodyFileOffset, unknown.FileOffset);
        Assert.Equal(3, unknown.ByteCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void KeepsContainmentSeparateFromUnknownScalarBytesAndDuplicateNames(bool wide)
    {
        var child = Record("Duplicate", [0x22], wide);
        var parent = Record("Duplicate", Join([0x11], child, [0x33]), wide);
        var sibling = Record("Duplicate", [], wide);
        var report = Scan(Join(parent, sibling), wide);

        Assert.Equal(3, report.RecordsFound);
        Assert.Equal(3, report.ClassCounts["Duplicate"]);
        Assert.Equal(3, report.UninterpretedBytes);
        Assert.Equal(3, report.UninterpretedRanges.Sum(r => r.ByteCount));
        var outer = report.Records[0];
        var inner = report.Records[1];
        Assert.Equal(1, inner.Depth);
        Assert.Equal(outer.HeaderFileOffset, inner.ParentHeaderFileOffset);
        Assert.InRange(inner.BodyFileOffset + inner.BodyBytes, outer.BodyFileOffset,
            outer.BodyFileOffset + outer.BodyBytes);
        Assert.Equal(0, report.Records[2].Depth);
        Assert.Null(report.Records[2].ParentHeaderFileOffset);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PreservesBaseClassDeclarationAndReferenceToItsSymbol(bool wide)
    {
        var prefix = Join(Number((wide ? ulong.MaxValue : uint.MaxValue) - 1, wide),
            Name("BaseObject", 7));
        var declared = Join(prefix, Record("ConcreteObject", [], wide, 9));
        var report = Scan(Join(declared, Reference(0, [], wide)), wide);

        Assert.Equal(2, report.RecordsFound);
        var concrete = report.Records[0];
        Assert.Equal("ConcreteObject", concrete.ClassName);
        Assert.Equal("BaseObject", Assert.Single(concrete.PrefixClasses));
        Assert.Equal((ulong)prefix.Length, concrete.ClassSymbolArchiveOffset);
        Assert.Equal((ushort)9, concrete.ClassVersion);
        Assert.Equal("BaseObject", report.Records[1].ClassName);
        Assert.Equal((ushort)7, report.Records[1].ClassVersion);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BaseClassPrefixRequiresFullNameRatherThanReferenceToken(bool wide)
    {
        var declaration = Record("BaseObject", [], wide);
        var invalidPrefix = Join(Number((wide ? ulong.MaxValue : uint.MaxValue) - 1, wide),
            Number(0x80000000, false));
        var derived = Record("ConcreteObject", [], wide);
        var report = Scan(Join(declaration, invalidPrefix, derived), wide);

        Assert.Equal(2, report.RecordsFound);
        Assert.Empty(report.Records[1].PrefixClasses);
        Assert.Equal(invalidPrefix.Length, report.UninterpretedBytes);
        Assert.Equal(1000 + declaration.Length + invalidPrefix.Length,
            report.Records[1].HeaderFileOffset);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RejectsOversizedBodyWithoutCommittingItsClassSymbol(bool wide)
    {
        var malformed = Join(Number(wide ? ulong.MaxValue : uint.MaxValue, wide),
            Name("Uncommitted", 1), Number(wide ? ulong.MaxValue : uint.MaxValue, wide));
        var report = Scan(Join(malformed, Reference(0, [], wide)), wide);

        Assert.Empty(report.Records);
        Assert.Equal(0, report.RecordsFound);
        Assert.Equal(malformed.Length + (wide ? 16 : 8), report.UninterpretedBytes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DoesNotReadCandidateBodyAcrossEnclosingRecordBoundary(bool wide)
    {
        var malformedChild = Join(Number(wide ? ulong.MaxValue : uint.MaxValue, wide),
            Name("EscapingChild", 1), Number(100, wide));
        var parent = Record("Parent", malformedChild, wide);
        var sibling = Record("Sibling", [], wide);
        var report = Scan(Join(parent, sibling), wide);

        Assert.Equal(new[] { "Parent", "Sibling" }, report.Records.Select(r => r.ClassName));
        Assert.Equal(malformedChild.Length, report.UninterpretedBytes);
        Assert.Equal(0, report.Records[1].Depth);
    }

    [Fact]
    public void DetailAndRangeLimitsRetainCorrectAggregateCounts()
    {
        var bytes = Join([0x11], Record("A", [], false), [0x22], Record("B", [], false));
        var report = CprArchiveRecordScanner.Scan("Any", null, bytes, 0, bytes.Length, false,
            maxRetainedRecords: 1, maxUninterpretedRanges: 1);

        Assert.True(report.Partial);
        Assert.Equal(2, report.RecordsFound);
        Assert.Single(report.Records);
        Assert.Equal(2, report.UninterpretedBytes);
        Assert.Single(report.UninterpretedRanges);
        Assert.Contains(report.LimitReasons, reason => reason.Contains("detail retention", StringComparison.Ordinal));
        Assert.Contains(report.LimitReasons, reason => reason.Contains("range retention", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void RecordBudgetStopsAndAccountsForRemainingBytes(int budget)
    {
        var first = Record("First", [], false);
        var last = Record("Last", [], false);
        var bytes = Join(first, last);
        var report = CprArchiveRecordScanner.Scan("Any", null, bytes, 0, bytes.Length, false,
            maxRecords: budget);

        Assert.True(report.Partial);
        Assert.Equal(budget, report.RecordsFound);
        Assert.Equal(budget == 0 ? bytes.Length : last.Length, report.UninterpretedBytes);
        Assert.Contains(report.LimitReasons, reason => reason.Contains("Record count limit", StringComparison.Ordinal));
    }

    [Fact]
    public void ScanAndDepthLimitsAreExplicit()
    {
        byte[] bytes = [0x11];
        for (var i = 0; i < 52; i++) bytes = Record("Nested", bytes, false);
        var report = CprArchiveRecordScanner.Scan("Any", null, bytes, 0, bytes.Length + 20L, false);

        Assert.True(report.Partial);
        Assert.Equal(49, report.RecordsFound);
        Assert.Equal(48, report.Records.Max(r => r.Depth));
        Assert.Contains(report.LimitReasons, reason => reason.Contains("Nesting depth", StringComparison.Ordinal));
        Assert.Contains(report.LimitReasons, reason => reason.Contains("byte scan budget", StringComparison.Ordinal));
        Assert.Equal(bytes.Length, report.BytesScanned);
        Assert.Equal(bytes.Length + 20L, report.DeclaredBytes);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public void SymbolBudgetStopsWithoutCommittingPartialDeclarationChains(int budget)
    {
        var first = Record("First", [], false);
        var chain = Join(Number(uint.MaxValue - 1, false), Name("BaseObject", 0),
            Record("ConcreteObject", [], false));
        var bytes = Join(first, chain, Reference(0, [], false));
        var report = CprArchiveRecordScanner.Scan("Any", null, bytes, 0, bytes.Length, false,
            maxSymbols: budget);

        Assert.True(report.Partial);
        Assert.Equal(budget == 0 ? 0 : 1, report.RecordsFound);
        Assert.Equal(budget == 0 ? 0 : 1, report.SymbolsFound);
        Assert.Equal(budget == 0 ? bytes.Length : bytes.Length - first.Length,
            report.UninterpretedBytes);
        Assert.Contains(report.LimitReasons, reason => reason.Contains("Class symbol limit", StringComparison.Ordinal));
    }

    [Fact]
    public void CancelledScanDoesNotReturnAnApparentlyCompleteInventory()
    {
        using var source = new CancellationTokenSource();
        source.Cancel();
        Assert.Throws<OperationCanceledException>(() =>
            CprArchiveRecordScanner.Scan("Any", null, [], 0, 0, false, source.Token));
    }
}
