namespace CubaseInspector.App.Services;

public interface IProjectPathPicker
{
    Task<string?> PickProjectAsync();
    Task<string?> PickDatabaseAsync();
    Task<string?> PickFolderAsync();
    Task<string?> PickSaveFileAsync(string name, string extension, string description);
}
