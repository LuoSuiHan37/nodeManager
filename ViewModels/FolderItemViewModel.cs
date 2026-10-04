using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using NoteManager.Models;

namespace NoteManager.ViewModels;

public partial class FolderItemViewModel : ObservableObject
{
    [ObservableProperty]
    private Guid _id;

    [ObservableProperty]
    private string _name = string.Empty;

    [ObservableProperty]
    private Guid? _parentId;

    [ObservableProperty]
    private bool _isExpanded = true;

    public ObservableCollection<FolderItemViewModel> Children { get; } = [];

    public static FolderItemViewModel FromFolder(Folder folder) => new()
    {
        Id = folder.Id,
        Name = folder.Name,
        ParentId = folder.ParentId
    };

    public override string ToString() => Name;
}
