using System.Collections.ObjectModel;
using Microsoft.UI.Dispatching;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NoteManager.Helpers;
using NoteManager.Models;
using NoteManager.Services;
using Serilog;

namespace NoteManager.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly INoteService _noteService;
    private readonly IFolderService _folderService;
    private readonly ISettingsService _settingsService;
    private readonly IBackupService _backupService;
    private CancellationTokenSource? _searchCts;
    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private NoteFilterKind _currentFilter = NoteFilterKind.All;

    [ObservableProperty]
    private Guid? _selectedFolderId;

    [ObservableProperty]
    private NoteItemViewModel? _selectedNote;

    [ObservableProperty]
    private string _statusMessage = "就绪";

    [ObservableProperty]
    private string _theme = "System";

    [ObservableProperty]
    private bool _isBusy;

    public ObservableCollection<NoteItemViewModel> Notes { get; } = [];

    public ObservableCollection<FolderItemViewModel> Folders { get; } = [];

    public EditorViewModel Editor { get; }

    public MainViewModel(
        INoteService noteService,
        IFolderService folderService,
        ISettingsService settingsService,
        IBackupService backupService)
    {
        _noteService = noteService;
        _folderService = folderService;
        _settingsService = settingsService;
        _backupService = backupService;
        Theme = settingsService.Current.Theme;
        Editor = new EditorViewModel(noteService, settingsService);
        Editor.NoteSaved += OnNoteSaved;
    }

    public async Task InitializeAsync()
    {
        try
        {
            IsBusy = true;
            StatusMessage = "正在加载...";
            await RefreshFoldersAsync();

            // 历史误挂在子级的文件夹提升到与默认文件夹同级
            var promoted = await _folderService.PromoteAllFoldersToRootAsync();
            if (promoted > 0)
            {
                Log.Information("已将 {Count} 个文件夹提升到根级", promoted);
                await RefreshFoldersAsync();
            }

            // 历史未归类笔记自动放入默认文件夹
            var defaultFolder = await _folderService.GetOrCreateDefaultAsync();
            var moved = await _noteService.AssignUncategorizedToFolderAsync(defaultFolder.Id);
            if (moved > 0)
            {
                Log.Information("已将 {Count} 条未归类笔记放入默认文件夹", moved);
                await RefreshFoldersAsync();
            }

            await RefreshNotesAsync();

            var lastId = _settingsService.Current.LastOpenedNoteId;
            if (lastId.HasValue)
            {
                var match = Notes.FirstOrDefault(n => n.Id == lastId.Value);
                if (match is not null)
                {
                    await SelectNoteAsync(match);
                }
            }

            StatusMessage = promoted > 0
                ? $"已将 {promoted} 个文件夹调整为同级目录"
                : moved > 0
                    ? $"已整理 {moved} 条笔记到默认文件夹"
                    : "就绪";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "MainViewModel 初始化失败");
            StatusMessage = "加载失败";
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        _ = DebouncedSearchAsync(value);
    }

    private async Task DebouncedSearchAsync(string value)
    {
        _searchCts?.Cancel();
        _searchCts = new CancellationTokenSource();
        var token = _searchCts.Token;

        try
        {
            await Task.Delay(250, token);
            await RefreshNotesAsync();
        }
        catch (OperationCanceledException)
        {
            // ignored
        }
    }

    [RelayCommand]
    private async Task ShowAllAsync()
    {
        CurrentFilter = NoteFilterKind.All;
        SelectedFolderId = null;
        await RefreshNotesAsync();
    }

    [RelayCommand]
    private async Task ShowFavoritesAsync()
    {
        CurrentFilter = NoteFilterKind.Favorites;
        SelectedFolderId = null;
        await RefreshNotesAsync();
    }

    [RelayCommand]
    private async Task ShowRecentAsync()
    {
        CurrentFilter = NoteFilterKind.Recent;
        SelectedFolderId = null;
        await RefreshNotesAsync();
    }

    [RelayCommand]
    private async Task ShowTrashAsync()
    {
        CurrentFilter = NoteFilterKind.Trash;
        SelectedFolderId = null;
        await RefreshNotesAsync();
    }

    [RelayCommand]
    private async Task SelectFolderAsync(Guid folderId)
    {
        CurrentFilter = NoteFilterKind.Folder;
        SelectedFolderId = folderId;
        await RefreshNotesAsync();
    }

    [RelayCommand]
    private async Task CreateNoteAsync()
    {
        try
        {
            // 在文件夹视图中新建 → 当前文件夹；否则归入默认文件夹
            Guid? folderId = CurrentFilter == NoteFilterKind.Folder ? SelectedFolderId : null;
            if (!folderId.HasValue)
            {
                folderId = (await _folderService.GetOrCreateDefaultAsync()).Id;
            }

            var note = await _noteService.CreateAsync(folderId);
            await RefreshNotesAsync();
            var item = Notes.FirstOrDefault(n => n.Id == note.Id) ?? NoteItemViewModel.FromNote(note);
            await SelectNoteAsync(item);

            StatusMessage = "已新建笔记";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "新建笔记失败");
            StatusMessage = "新建笔记失败";
        }
    }

    [RelayCommand]
    private async Task SelectNoteAsync(NoteItemViewModel? note)
    {
        if (note is null)
        {
            return;
        }

        SelectedNote = note;
        await Editor.LoadNoteAsync(note.Id);
    }

    [RelayCommand]
    private async Task ToggleFavoriteAsync()
    {
        if (SelectedNote is null)
        {
            return;
        }

        try
        {
            await _noteService.ToggleFavoriteAsync(SelectedNote.Id);
            await RefreshNotesAsync(keepSelection: true);
            StatusMessage = "已更新收藏状态";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "切换收藏失败");
            StatusMessage = "操作失败";
        }
    }

    [RelayCommand]
    private async Task TogglePinAsync()
    {
        if (SelectedNote is null)
        {
            return;
        }

        try
        {
            await _noteService.TogglePinAsync(SelectedNote.Id);
            await RefreshNotesAsync(keepSelection: true);
            StatusMessage = "已更新置顶状态";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "切换置顶失败");
            StatusMessage = "操作失败";
        }
    }

    [RelayCommand]
    private async Task DeleteNoteAsync()
    {
        if (SelectedNote is null)
        {
            return;
        }

        try
        {
            if (CurrentFilter == NoteFilterKind.Trash || SelectedNote.IsDeleted)
            {
                await _noteService.PermanentDeleteAsync(SelectedNote.Id);
                StatusMessage = "已永久删除";
            }
            else
            {
                await _noteService.MoveToTrashAsync(SelectedNote.Id);
                StatusMessage = "已移入回收站";
            }

            Editor.Clear();
            SelectedNote = null;
            await RefreshNotesAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "删除笔记失败");
            StatusMessage = "删除失败";
        }
    }

    [RelayCommand]
    private async Task RestoreNoteAsync()
    {
        if (SelectedNote is null)
        {
            return;
        }

        try
        {
            await _noteService.RestoreAsync(SelectedNote.Id);
            StatusMessage = "已恢复笔记";
            Editor.Clear();
            SelectedNote = null;
            await RefreshNotesAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "恢复笔记失败");
            StatusMessage = "恢复失败";
        }
    }

    [RelayCommand]
    private async Task CreateFolderAsync(string? name)
    {
        try
        {
            var folderName = string.IsNullOrWhiteSpace(name) ? "新建文件夹" : name.Trim();
            // 顶部「+」始终在根级创建，不挂到当前选中文件夹下
            await _folderService.CreateAsync(folderName, parentId: null);
            await RefreshFoldersAsync();
            StatusMessage = "已创建文件夹";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "创建文件夹失败");
            StatusMessage = "创建文件夹失败";
        }
    }

    [RelayCommand]
    private async Task RenameFolderAsync((Guid Id, string Name) args)
    {
        try
        {
            await _folderService.RenameAsync(args.Id, args.Name);
            await RefreshFoldersAsync();
            StatusMessage = "已重命名文件夹";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "重命名文件夹失败");
            StatusMessage = "重命名失败";
        }
    }

    [RelayCommand]
    private async Task DeleteFolderAsync(Guid folderId)
    {
        try
        {
            await _folderService.DeleteAsync(folderId);
            if (SelectedFolderId == folderId)
            {
                SelectedFolderId = null;
                CurrentFilter = NoteFilterKind.All;
            }

            await RefreshFoldersAsync();
            await RefreshNotesAsync();
            StatusMessage = "已删除文件夹";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "删除文件夹失败");
            StatusMessage = ex.Message.Contains("子文件夹") ? "请先删除子文件夹" : "删除文件夹失败";
        }
    }

    [RelayCommand]
    private async Task MoveSelectedNoteAsync(Guid? folderId)
    {
        if (SelectedNote is null)
        {
            return;
        }

        try
        {
            await _noteService.MoveToFolderAsync(SelectedNote.Id, folderId);
            await RefreshNotesAsync(keepSelection: true);
            StatusMessage = "已移动笔记";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "移动笔记失败");
            StatusMessage = "移动失败";
        }
    }

    [RelayCommand]
    private async Task CreateBackupAsync()
    {
        var path = await _backupService.CreateBackupAsync();
        StatusMessage = path is null ? "备份失败" : "已创建备份";
    }

    [RelayCommand]
    private async Task SetThemeAsync(string theme)
    {
        Theme = theme;
        await _settingsService.UpdateAsync(s => s.Theme = theme);
        ThemeHelper.ApplyTheme(theme);
        StatusMessage = "主题已更新";
    }

    
    private Task RunOnUiAsync(Action action)
    {
        if (_dispatcher.HasThreadAccess)
        {
            action();
            return Task.CompletedTask;
        }

        var tcs = new TaskCompletionSource();
        _dispatcher.TryEnqueue(() =>
        {
            try
            {
                action();
                tcs.SetResult();
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });
        return tcs.Task;
    }

    public async Task RefreshNotesAsync(bool keepSelection = false)
    {
        try
        {
            var selectedId = SelectedNote?.Id;
            var notes = await _noteService.GetNotesAsync(CurrentFilter, SelectedFolderId, SearchText);
            await RunOnUiAsync(() =>
            {
                Notes.Clear();
                foreach (var note in notes)
                {
                    Notes.Add(NoteItemViewModel.FromNote(note));
                }

                if (keepSelection && selectedId.HasValue)
                {
                    SelectedNote = Notes.FirstOrDefault(n => n.Id == selectedId.Value);
                }
            });

            if (keepSelection && SelectedNote is not null)
            {
                await Editor.LoadNoteAsync(SelectedNote.Id);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "刷新笔记列表失败");
            StatusMessage = "刷新列表失败";
        }
    }

    public async Task RefreshFoldersAsync()
    {
        try
        {
            var all = await _folderService.GetAllAsync();
            await RunOnUiAsync(() =>
            {
                var lookup = all.ToDictionary(f => f.Id, FolderItemViewModel.FromFolder);
                foreach (var item in lookup.Values)
                {
                    item.Children.Clear();
                }

                Folders.Clear();
                foreach (var folder in all.OrderBy(f => f.SortOrder).ThenBy(f => f.Name))
                {
                    var vm = lookup[folder.Id];
                    if (folder.ParentId.HasValue && lookup.TryGetValue(folder.ParentId.Value, out var parent))
                    {
                        parent.Children.Add(vm);
                    }
                    else
                    {
                        Folders.Add(vm);
                    }
                }
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "刷新文件夹失败");
            StatusMessage = "刷新文件夹失败";
        }
    }

    private async void OnNoteSaved(object? sender, EventArgs e)
    {
        try
        {
            // 只更新当前条目，避免 Notes.Clear 打断 ListView 选中/编辑状态
            var selectedId = SelectedNote?.Id ?? Editor.CurrentNoteId;
            if (!selectedId.HasValue)
            {
                return;
            }

            var note = await _noteService.GetAsync(selectedId.Value);
            if (note is null)
            {
                return;
            }

            var plain = Editor.LastSavedPlainText;
            await RunOnUiAsync(() =>
            {
                var item = Notes.FirstOrDefault(n => n.Id == selectedId.Value);
                item?.Apply(note, plain);
                if (SelectedNote?.Id == selectedId.Value && SelectedNote is not null)
                {
                    SelectedNote.Apply(note, plain);
                }
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "保存后刷新失败");
        }
    }

    public void Dispose()
    {
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        Editor.NoteSaved -= OnNoteSaved;
        Editor.Dispose();
    }
}
