using System.Text.Json;
using System.Reflection;
using CubaseInspector.Core;
using CubaseInspector.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(4) ?? "unknown";
if (args is ["--version"] or ["-v"])
{
    Console.WriteLine($"Cubase Project Explorer {version}");
    return 0;
}

var services = new ServiceCollection().AddCubaseInspector().BuildServiceProvider();
if (args.Length < 2)
{
    Console.WriteLine($"Cubase Project Explorer CLI {version}\nUsage:\n  --version\n  inspect <file.cpr|file.bak|file.dawproject> [--markdown|--json|--coverage-json|--tracks-csv|--plugins-csv]\n  cpr-structure <file.cpr|file.bak> [--markdown|--json]\n  cpr-plugins <file.cpr|file.bak> [--json|--csv|--markdown] [--unresolved-only]\n  diff <fileA> <fileB> [--text|--markdown|--csv|--json]\n  scan <folder>\n  audit <folder> [--json]\n  routes <structured-file> <channel-id> [--upstream]\n  io <file> [--markdown|--json]\n  plugins <file>\n  footprint <file> [--json]\n  footprint-audit <folder> [--json]\n  preflight <file> [--markdown|--json|--csv] [--skip-local-scan]\n  safe-copy <source.cpr> <new-copy.cpr>\n\nNo operation modifies the original file. Plugin deactivation is not implemented.");
    return 1;
}

static int Error(AppError? error)
{
    Console.Error.WriteLine($"{error?.Code ?? "ERROR"}: {error?.Message ?? "Unknown error"}");
    return 2;
}
var inspector = services.GetRequiredService<IProjectInspector>();
var reports = services.GetRequiredService<IReportService>();
switch (args[0].ToLowerInvariant())
{
    case "cpr-plugins":
    {
        var path = args[1];
        if (!CprFileIdentity.IsCprOrValidBackup(path) || !File.Exists(path))
        {
            Console.Error.WriteLine("NOT_CPR: expected an existing .cpr project or RIFF/NUND .bak backup.");
            return 2;
        }
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.Read, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var scan = await CprPluginStreamScanner.ScanAsync(input);
        var unresolvedOnly = args.Skip(2).Contains("--unresolved-only", StringComparer.OrdinalIgnoreCase);
        var report = CprPluginDiagnosticService.Build(scan, input.Length,
            Path.GetFileName(path), unresolvedOnly);
        var mode = args.Skip(2).FirstOrDefault(arg =>
            arg is "--json" or "--csv" or "--markdown") ?? "--markdown";
        Console.Write(mode switch
        {
            "--json" => JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }),
            "--csv" => CprPluginDiagnosticService.Csv(report),
            _ => CprPluginDiagnosticService.Markdown(report)
        });
        return 0;
    }
    case "cpr-structure":
    {
        var container = await new CprStructureInspector().InspectAsync(args[1]);
        if (!container.IsSuccess || container.Value is null) return Error(container.Error);
        Console.WriteLine(args.Skip(2).Any(a => a.Equals("--json", StringComparison.OrdinalIgnoreCase))
            ? JsonSerializer.Serialize(container.Value, new JsonSerializerOptions { WriteIndented = true })
            : CprStructureInspector.ToMarkdown(container.Value));
        return 0;
    }
    case "inspect":
    {
        var result = await inspector.InspectAsync(args[1]);
        if (!result.IsSuccess || result.Value is null) return Error(result.Error);
        var mode = args.Length >= 3 ? args[2] : "--markdown";
        Console.Write(mode switch
        {
            "--json" => JsonSerializer.Serialize(result.Value, new JsonSerializerOptions { WriteIndented = true }),
            "--coverage-json" => JsonSerializer.Serialize(new
            {
                result.Value.Name,
                result.Value.Format,
                result.Value.Evidence,
                Fields = ProjectFieldCoverage.Evaluate(result.Value),
                Warning = "Field coverage describes normalized reader output, not verified Cubase completeness."
            }, new JsonSerializerOptions { WriteIndented = true }),
            "--tracks-csv" => reports.TracksCsv(result.Value),
            "--plugins-csv" => reports.PluginsCsv(result.Value),
            _ => reports.ToMarkdown(result.Value)
        });
        return 0;
    }
    case "diff" when args.Length >= 3:
    {
        var a = await inspector.InspectAsync(args[1]);
        var b = await inspector.InspectAsync(args[2]);
        if (!a.IsSuccess || a.Value is null) return Error(a.Error);
        if (!b.IsSuccess || b.Value is null) return Error(b.Error);
        var confirmed = a.Value.Evidence == EvidenceLevel.Confirmed &&
            b.Value.Evidence == EvidenceLevel.Confirmed;
        var native = CprCandidateComparisonService.Supports(a.Value) &&
            CprCandidateComparisonService.Supports(b.Value);
        if (!confirmed && !native)
        {
            Console.Error.WriteLine("INCOMPATIBLE_COMPARISON: both inputs must be confirmed DAWproject " +
                "exports or candidate native CPR/BAK files. Mixing evidence levels is unsupported.");
            return 2;
        }
        var candidate = native ? CprCandidateComparisonService.Compare(a.Value, b.Value) : null;
        IReadOnlyList<ProjectChange> changes = candidate is not null ? candidate.Changes :
            services.GetRequiredService<IProjectDiff>().Compare(a.Value, b.Value);
        var mode = args.Skip(3).FirstOrDefault(x => x.StartsWith("--", StringComparison.Ordinal)) ?? "--text";
        var exporter = new ProjectChangeReportService();
        Console.Write(mode switch
        {
            "--json" => JsonSerializer.Serialize((object?)candidate ?? changes,
                new JsonSerializerOptions { WriteIndented = true }),
            "--markdown" => candidate is not null
                ? CprCandidateComparisonService.Markdown(candidate, a.Value.FilePath, b.Value.FilePath)
                : exporter.Markdown(changes, a.Value.FilePath, b.Value.FilePath),
            "--csv" => candidate is not null
                ? CprCandidateComparisonService.Csv(candidate) : exporter.Csv(changes),
            _ => (candidate is null ? "" :
                "CPR candidate UID property-token observations only; no verified plugin instances." +
                    Environment.NewLine) +
                string.Join(Environment.NewLine, changes.Select(change =>
                    $"{change.Category}: {change.Entity} / {change.Property}: {change.Before} -> {change.After} [{change.MatchQuality}]")) +
                Environment.NewLine
        });
        return 0;
    }
    case "io":
    {
        var result = await inspector.InspectAsync(args[1]);
        if (!result.IsSuccess || result.Value is null) return Error(result.Error);
        var io = StudioIoPreflightService.Analyze(result.Value);
        Console.Write(args.Skip(2).Contains("--json", StringComparer.OrdinalIgnoreCase)
            ? JsonSerializer.Serialize(io, new JsonSerializerOptions { WriteIndented = true })
            : StudioIoPreflightService.Markdown(io));
        return 0;
    }
    case "routes" when args.Length >= 3:
    {
        var inspected = await inspector.InspectAsync(args[1]);
        if (!inspected.IsSuccess || inspected.Value is null) return Error(inspected.Error);
        var trace = new RoutingGraphService().Trace(CprSignalFlowPresentation.Create(inspected.Value), args[2],
            upstream: args.Skip(3).Any(a => a.Equals("--upstream", StringComparison.OrdinalIgnoreCase)));
        if (!trace.IsSuccess || trace.Value is null) return Error(trace.Error);
        Console.WriteLine($"{(trace.Value.Upstream ? "Upstream" : "Downstream")} routes for {trace.Value.SelectedChannelId}:");
        foreach (var edge in trace.Value.Edges)
            Console.WriteLine($"  {edge.FromChannelId} --{edge.Kind}--> {edge.TargetLabel ?? edge.ToChannelId}" +
                (edge.SourceOffset is long offset ? $" · {edge.SourcePath} · 0x{offset:X}" : ""));
        if (trace.Value.UnresolvedEndpoints.Count > 0)
            Console.WriteLine("Unresolved endpoints: " + string.Join(", ", trace.Value.UnresolvedEndpoints));
        Console.WriteLine($"Contains cycle: {trace.Value.ContainsCycle}");
        return 0;
    }
    case "audit":
    {
        var result = await services.GetRequiredService<IFolderScanner>().ScanAsync(args[1]);
        if (!result.IsSuccess || result.Value is null) return Error(result.Error);
        // File modification times are not evidence of when plugins were actually used.
        var timestamps = result.Value.Projects.Where(p => File.Exists(p.FilePath))
            .ToDictionary(p => p.FilePath,
                p => new DateTimeOffset(File.GetLastWriteTimeUtc(p.FilePath), TimeSpan.Zero),
                StringComparer.OrdinalIgnoreCase);
        var analytics = new PluginUsageAuditService();
        var audit = analytics.Analyze(result.Value, timestamps);
        Console.WriteLine(args.Skip(2).Any(a => a == "--json")
            ? JsonSerializer.Serialize(audit, new JsonSerializerOptions { WriteIndented = true })
            : analytics.ToMarkdown(audit));
        return 0;
    }
    case "preflight":
    {
        var inspected = await inspector.InspectAsync(args[1]);
        if (!inspected.IsSuccess || inspected.Value is null) return Error(inspected.Error);
        var scanner = services.GetRequiredService<IPluginScannerService>();
        IReadOnlyList<PluginCheck>? checks = null;
        string? scanError = null;
        if (!args.Skip(2).Contains("--skip-local-scan", StringComparer.OrdinalIgnoreCase))
        {
            var local = await scanner.ScanAsync();
            if (local.IsSuccess && local.Value is not null)
                checks = scanner.Check(inspected.Value, local.Value);
            else scanError = local.Error?.Code ?? "LOCAL_SCAN_FAILED";
        }
        var tool = new ProjectPreflightService();
        var report = tool.Analyze(inspected.Value, checks, scanError);
        var mode = args.Skip(2).FirstOrDefault(x => x is "--csv" or "--json") ?? "--markdown";
        Console.Write(mode switch
        {
            "--csv" => tool.Csv(report),
            "--json" => JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }),
            _ => tool.Markdown(report)
        });
        return 0;
    }
    case "footprint-audit":
    {
        var folder = await services.GetRequiredService<IFolderScanner>().ScanAsync(args[1]);
        if (!folder.IsSuccess || folder.Value is null) return Error(folder.Error);
        var scanner = services.GetRequiredService<IPluginScannerService>();
        var local = await scanner.ScanAsync();
        if (!local.IsSuccess || local.Value is null) return Error(local.Error);
        // Local bundle bytes are measured once across the entire indexed folder, never
        // summed independently per project. Candidate CPR records are not exact evidence.
        var confirmed = folder.Value.Projects.Where(p => p.Evidence == EvidenceLevel.Confirmed);
        var checks = confirmed.SelectMany(p => scanner.Check(p, local.Value)).ToArray();
        var report = LocalPluginFootprintService.Analyze(checks);
        if (args.Skip(2).Contains("--json", StringComparer.OrdinalIgnoreCase))
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                folder.Value.Partial,
                folder.Value.Discovered,
                InspectedProjects = folder.Value.Projects.Count,
                FailedProjects = folder.Value.Failures.Count,
                report
            }, new JsonSerializerOptions { WriteIndented = true }));
        }
        else
        {
            Console.WriteLine($"Inspected projects: {folder.Value.Projects.Count}; failures: {folder.Value.Failures.Count}; partial: {folder.Value.Partial}");
            Console.WriteLine(report.Notice);
            foreach (var bundle in report.Bundles)
                Console.WriteLine($"{bundle.Path}: {(bundle.Complete ? bundle.LogicalBytes.ToString() : "incomplete")} bytes, references={bundle.ConfirmedReferences}{(bundle.Warning is null ? "" : " (" + bundle.Warning + ")")}");
            Console.WriteLine($"Deduplicated complete bundle bytes: {report.CompleteLogicalBytes}; exact references: {report.ExactReferences}; excluded references: {report.ExcludedReferences}; complete: {report.Complete}");
        }
        return 0;
    }
    case "footprint":
    {
        var project = await inspector.InspectAsync(args[1]);
        if (!project.IsSuccess || project.Value is null) return Error(project.Error);
        var scanner = services.GetRequiredService<IPluginScannerService>();
        var local = await scanner.ScanAsync();
        if (!local.IsSuccess || local.Value is null) return Error(local.Error);
        var report = LocalPluginFootprintService.Analyze(scanner.Check(project.Value, local.Value));
        if (args.Skip(2).Contains("--json", StringComparer.OrdinalIgnoreCase))
            Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        else
        {
            Console.WriteLine(report.Notice);
            foreach (var bundle in report.Bundles)
                Console.WriteLine($"{bundle.Path}: {(bundle.Complete ? bundle.LogicalBytes.ToString() : "incomplete")} bytes, {bundle.Files} files, {bundle.ConfirmedReferences} confirmed references{(bundle.Warning is null ? "" : " (" + bundle.Warning + ")")}");
            Console.WriteLine($"Complete matched bundle bytes: {report.CompleteLogicalBytes}; matched references: {report.ExactReferences}; excluded references: {report.ExcludedReferences}; complete: {report.Complete}");
        }
        return 0;
    }
    case "plugins":
    {
        var project = await inspector.InspectAsync(args[1]);
        if (!project.IsSuccess || project.Value is null) return Error(project.Error);
        var scanner = services.GetRequiredService<IPluginScannerService>();
        var scan = await scanner.ScanAsync();
        if (!scan.IsSuccess || scan.Value is null) return Error(scan.Error);
        foreach (var plugin in scanner.Check(project.Value, scan.Value))
            Console.WriteLine($"{plugin.Reference.Name}: {plugin.Status} ({plugin.MatchedPath ?? "not verified"})");
        return 0;
    }
    case "scan":
    {
        var result = await services.GetRequiredService<IFolderScanner>().ScanAsync(args[1]);
        if (!result.IsSuccess || result.Value is null) return Error(result.Error);
        foreach (var project in result.Value.Projects)
            Console.WriteLine($"{project.Name}: tracks={project.Tracks.Count}, pluginRefs={project.Plugins.Count}, level={project.Evidence}");
        foreach (var failure in result.Value.Failures)
            Console.Error.WriteLine($"Skipped {failure.Path}: {failure.Error}");
        return 0;
    }
    case "safe-copy" when args.Length >= 3:
    {
        var result = await services.GetRequiredService<ISafeCopyService>().CopyUnmodifiedAsync(args[1], args[2]);
        if (!result.IsSuccess) return Error(result.Error);
        Console.WriteLine($"Unmodified copy written: {result.Value}");
        return 0;
    }
    default:
        Console.Error.WriteLine("Unknown command or missing arguments.");
        return 1;
}
