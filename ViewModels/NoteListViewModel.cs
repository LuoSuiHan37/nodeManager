using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace NoteManager.ViewModels;

/// <summary>
/// 中间栏笔记列表状态（由 MainViewModel 组合使用）。
/// </summary>
public partial class NoteListViewModel : ObservableObject
{
    [ObservableProperty]
    private NoteItemViewModel? _selectedItem;

    public ObservableCollection<NoteItemViewModel> Items { get; } = [];

    public void ReplaceAll(IEnumerable<NoteItemViewModel> items)
    {
        Items.Clear();
        foreach (var item in items)
        {
            Items.Add(item);
        }
    }
}
