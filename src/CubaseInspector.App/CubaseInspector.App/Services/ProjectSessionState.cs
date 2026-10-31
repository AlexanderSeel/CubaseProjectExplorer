using CommunityToolkit.Mvvm.ComponentModel;
using CubaseInspector.Core;

namespace CubaseInspector.App.Services;

public sealed class ProjectSessionState : ObservableObject
{
    private ProjectSnapshot? _selectedProject;

    public ProjectSnapshot? SelectedProject
    {
        get => _selectedProject;
        private set => SetProperty(ref _selectedProject, value);
    }

    public void Select(ProjectSnapshot? project) => SelectedProject = project;
}
