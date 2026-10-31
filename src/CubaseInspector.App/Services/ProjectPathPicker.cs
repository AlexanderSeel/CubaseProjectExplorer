using Windows.Storage.Pickers;

namespace CubaseInspector.App.Services;

/// <summary>Uses Uno's native picker implementation for the Skia Desktop host.</summary>
public sealed class ProjectPathPicker : IProjectPathPicker
{
    public async Task<string?> PickDatabaseAsync()
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder };
        picker.FileTypeFilter.Add(".db");
        picker.FileTypeFilter.Add(".sqlite");
        picker.FileTypeFilter.Add(".sqlite3");
        return (await picker.PickSingleFileAsync())?.Path;
    }

    public async Task<string?> PickProjectAsync()
    {
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.ComputerFolder
        };
        picker.FileTypeFilter.Add(".cpr");
        picker.FileTypeFilter.Add(".bak");
        picker.FileTypeFilter.Add(".dawproject");
        return (await picker.PickSingleFileAsync())?.Path;
    }

    public async Task<string?> PickFolderAsync()
    {
        var picker = new FolderPicker
        {
            SuggestedStartLocation = PickerLocationId.ComputerFolder
        };
        picker.FileTypeFilter.Add("*");
        return (await picker.PickSingleFolderAsync())?.Path;
    }

    public async Task<string?> PickSaveFileAsync(string name, string extension, string description)
    {
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = name
        };
        picker.FileTypeChoices.Add(description, [extension]);
        return (await picker.PickSaveFileAsync())?.Path;
    }
}
