using CubaseInspector.Core;
using Microsoft.Extensions.DependencyInjection;
using System.Xml;

namespace CubaseInspector.Infrastructure;

public sealed class ProjectInspector(IEnumerable<IProjectReader> readers) : IProjectInspector
{
    public Task<Result<ProjectSnapshot>> InspectAsync(string path, CancellationToken cancellationToken = default) =>
        Task.Run(() => InspectCoreAsync(path, cancellationToken));

    private async Task<Result<ProjectSnapshot>> InspectCoreAsync(string path, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return Result<ProjectSnapshot>.Fail("FILE_NOT_FOUND", "The project file does not exist.");
        var reader = readers.FirstOrDefault(r => r.CanRead(path));
        if (reader is null) return Result<ProjectSnapshot>.Fail("UNSUPPORTED", "Only .cpr, RIFF/NUND .bak backups, and .dawproject files are supported.");
        try { return await reader.ReadAsync(path, cancellationToken); }
        catch (OperationCanceledException) { return Result<ProjectSnapshot>.Fail("CANCELLED", "Inspection cancelled."); }
        catch (IOException e) { return Result<ProjectSnapshot>.Fail("IO_ERROR", e.Message); }
        catch (UnauthorizedAccessException e) { return Result<ProjectSnapshot>.Fail("ACCESS_DENIED", e.Message); }
        catch (InvalidDataException e) { return Result<ProjectSnapshot>.Fail("INVALID_DATA", e.Message); }
        catch (XmlException e) { return Result<ProjectSnapshot>.Fail("INVALID_XML", e.Message); }
        catch (ArgumentException e) { return Result<ProjectSnapshot>.Fail("INVALID_PATH", e.Message); }
        catch (NotSupportedException e) { return Result<ProjectSnapshot>.Fail("UNSUPPORTED_DATA", e.Message); }
    }
}

public static class DependencyInjection
{
    public static IServiceCollection AddCubaseInspector(this IServiceCollection services, string? libraryDatabasePath = null)
    {
        services.AddSingleton<ICprStructureInspector, CprStructureInspector>();
        services.AddSingleton<IProjectReader, CprEvidenceReader>();
        services.AddSingleton<IProjectReader, DawprojectReader>();
        services.AddSingleton<IProjectInspector, ProjectInspector>();
        services.AddSingleton<IPluginScannerService, PluginScannerService>();
        services.AddSingleton<IFolderScanner, FolderScanner>();
        services.AddSingleton<ISafeCopyService, SafeCopyService>();
        services.AddSingleton<IProjectDiff, ProjectDiff>();
        services.AddSingleton<IReportService, ReportService>();
        services.AddSingleton<IProjectMediaPreviewService, ProjectMediaPreviewService>();
        services.AddSingleton<IProjectMediaPackageService, ProjectMediaPackageService>();
        services.AddSingleton<IProjectMediaDuplicateService, ProjectMediaDuplicateService>();
        services.AddSingleton(_ => new LibraryDocumentStore(libraryDatabasePath));
        return services;
    }
}
