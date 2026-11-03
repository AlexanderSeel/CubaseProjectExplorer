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
        var declaration = Assert.Single(report.ClassDeclarations!);
        Assert.Equal("FutureUnknownObject", declaration.ClassName);
        Assert.Equal((ushort)0x1234, declaration.ClassVersion);
        Assert.Equal(0UL, declaration.SymbolArchiveOffset);
        Assert.Equal(1000, declaration.DeclarationFileOffset);
        Assert.Equal(inline.BodyFileOffset, declaration.AvailableAtFileOffset);
        Assert.Equal(EvidenceLevel.Candidate, declaration.Evidence);
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
        Assert.Equal(2, report.ClassDeclarations!.Count);
        Assert.Equal("BaseObject", report.ClassDeclarations[0].ClassName);
        Assert.Equal((ushort)7, report.ClassDeclarations[0].ClassVersion);
        Assert.Equal(0UL, report.ClassDeclarations[0].SymbolArchiveOffset);
        Assert.Equal(1000, report.ClassDeclarations[0].DeclarationFileOffset);
        Assert.Equal(concrete.BodyFileOffset, report.ClassDeclarations[0].AvailableAtFileOffset);
        Assert.Equal("ConcreteObject", report.ClassDeclarations[1].ClassName);
        Assert.Equal((ushort)9, report.ClassDeclarations[1].ClassVersion);
        Assert.Equal((ulong)prefix.Length, report.ClassDeclarations[1].SymbolArchiveOffset);
        Assert.Equal(concrete.BodyFileOffset, report.ClassDeclarations[1].AvailableAtFileOffset);
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
        Assert.Empty(report.ClassDeclarations!);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void RecoversEarlierDirectDeclarationsWithoutAddingAnObjectBody(bool wide, bool baseClass)
    {
        var declaration = baseClass
            ? Join(Number((wide ? ulong.MaxValue : uint.MaxValue) - 1, wide),
                Name("PreviouslyOpaque", 3), Record("Concrete", [], wide, 8))
            : Record("PreviouslyOpaque", [], wide, 3);
        var nested = declaration;
        for (var i = 0; i < 49; i++) nested = Record("Nested", nested, wide);
        var target = (ulong)(nested.Length - declaration.Length);
        var bytes = Join(nested, Reference(target, [0x11], wide));
        var report = Scan(bytes, wide);

        Assert.True(report.Partial); // The existing depth bound still applies.
        Assert.Equal(50, report.RecordsFound);
        var reference = report.Records[^1];
        Assert.Equal("PreviouslyOpaque", reference.ClassName);
        Assert.Equal((ushort)3, reference.ClassVersion);
        Assert.Equal("interned-class-reference", reference.ClassEncoding);
        Assert.Equal(target, reference.ClassSymbolArchiveOffset);
        Assert.Equal(1, reference.BodyBytes);
        Assert.Equal(1000 + nested.Length, reference.HeaderFileOffset);
        Assert.Equal(1, report.ClassCounts["PreviouslyOpaque"]);
        Assert.DoesNotContain(report.Records, record => record.HeaderFileOffset == 1000 + (long)target);
        var recovered = Assert.Single(report.ClassDeclarations!, item => item.ClassName == "PreviouslyOpaque");
        Assert.Equal((ushort)3, recovered.ClassVersion);
        Assert.Equal(target, recovered.SymbolArchiveOffset);
        Assert.Equal(1000 + (long)target, recovered.DeclarationFileOffset);
        Assert.Equal(reference.BodyFileOffset, recovered.AvailableAtFileOffset);
        Assert.True(recovered.AvailableAtFileOffset > recovered.DeclarationFileOffset);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FutureOutsideBufferNullAndChainedReferencesStayOpaque(bool wide)
    {
        var width = wide ? 8 : 4;
        var future = Reference((ulong)(width * 6), [], wide);
        var outside = Reference(0x100000, [], wide);
        var cycle = Reference((ulong)(width * 4), [], wide);
        var declaration = Record("LaterDeclaration", [], wide, 4);
        var chain = Reference((ulong)(width * 4), [], wide);
        var nullReference = Number(0, wide);
        var bytes = Join(future, outside, cycle, declaration, chain, nullReference);
        var report = Scan(bytes, wide);

        var only = Assert.Single(report.Records);
        Assert.Equal("LaterDeclaration", only.ClassName);
        Assert.Single(report.ClassDeclarations!);
        Assert.Equal(bytes.Length - declaration.Length, report.UninterpretedBytes);
        Assert.Contains("future, outside-buffer, chained and malformed", report.Grammar);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedLazyReferenceDoesNotCommitTheRecoveredDeclaration(bool wide)
    {
        var declaration = Record("PreviouslyOpaque", [], wide, 3);
        var nested = declaration;
        for (var i = 0; i < 49; i++) nested = Record("Nested", nested, wide);
        var target = (ulong)(nested.Length - declaration.Length);
        var malformed = Join(Number((wide ? 0x8000000000000000UL : 0x80000000UL) | target, wide),
            Number(wide ? ulong.MaxValue : uint.MaxValue, wide));
        var report = Scan(Join(nested, malformed), wide);

        Assert.Equal(49, report.RecordsFound);
        Assert.DoesNotContain(report.ClassDeclarations!, item => item.ClassName == "PreviouslyOpaque");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LazyDeclarationRecoveryHonorsTheSharedSymbolBudget(bool wide)
    {
        var declaration = Record("PreviouslyOpaque", [], wide, 3);
        var nested = declaration;
        for (var i = 0; i < 49; i++) nested = Record("Nested", nested, wide);
        var target = (ulong)(nested.Length - declaration.Length);
        var bytes = Join(nested, Reference(target, [], wide));
        var report = CprArchiveRecordScanner.Scan("Any", null, bytes, 0, bytes.Length, wide, maxSymbols: 49);

        Assert.True(report.Partial);
        Assert.Equal(49, report.RecordsFound);
        Assert.Equal(49, report.SymbolsFound);
        Assert.Equal(49, report.ClassDeclarations!.Count);
        Assert.DoesNotContain(report.ClassDeclarations, item => item.ClassName == "PreviouslyOpaque");
        Assert.Contains(report.LimitReasons, reason => reason.Contains("Class symbol limit", StringComparison.Ordinal));
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FullSymbolBudgetStillAllowsRecordsUsingRecognizedDeclarations(bool wide)
    {
        var declaration = Record("KnownClass", [0x11], wide, 3);
        var bytes = Join(declaration, Reference(0, [0x22], wide), Reference(0, [], wide));
        var report = CprArchiveRecordScanner.Scan("Any", null, bytes, 0, bytes.Length, wide, maxSymbols: 1);

        Assert.False(report.Partial);
        Assert.Equal(1, report.SymbolsFound);
        Assert.Equal(3, report.RecordsFound);
        Assert.Single(report.ClassDeclarations!);
        Assert.Equal(3, report.ClassCounts["KnownClass"]);
        Assert.Equal(new ushort?[] { 3, 3, 3 }, report.Records.Select(record => record.ClassVersion));
        Assert.Equal(2, report.UninterpretedBytes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FullSymbolBudgetStopsAtNextNewClassWithoutLosingEarlierReference(bool wide)
    {
        var declaration = Record("KnownClass", [], wide, 3);
        var reused = Reference(0, [], wide);
        var next = Record("NewClass", [], wide, 4);
        var bytes = Join(declaration, reused, next);
        var report = CprArchiveRecordScanner.Scan("Any", null, bytes, 0, bytes.Length, wide, maxSymbols: 1);

        Assert.True(report.Partial);
        Assert.Equal(1, report.SymbolsFound);
        Assert.Equal(2, report.RecordsFound);
        Assert.Single(report.ClassDeclarations!);
        Assert.Equal(2, report.ClassCounts["KnownClass"]);
        Assert.DoesNotContain("NewClass", report.ClassCounts.Keys);
        Assert.Equal(next.Length, report.UninterpretedBytes);
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
