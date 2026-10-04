using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace NoteManager.ViewModels;

/// <summary>
/// 左侧文件夹树状态（由 MainViewModel 组合使用）。
/// </summary>
public partial class FolderViewModel : ObservableObject
{
    [ObservableProperty]
    private FolderItemViewModel? _selectedFolder;

    public ObservableCollection<FolderItemViewModel> Roots { get; } = [];

    public void ReplaceTree(IEnumerable<FolderItemViewModel> roots)
    {
        Roots.Clear();
        foreach (var root in roots)
        {
            Roots.Add(root);
        }
    }
}
