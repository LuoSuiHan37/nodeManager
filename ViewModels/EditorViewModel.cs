using Microsoft.UI.Dispatching;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NoteManager.Helpers;
using NoteManager.Models;
using NoteManager.Services;
using Serilog;

namespace NoteManager.ViewModels;

public partial class EditorViewModel : ObservableObject, IDisposable
{
    private readonly INoteService _noteService;
    private readonly ISettingsService _settingsService;
    private DebounceHelper? _debounce;
    private bool _suppressChange;
    private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();
    private Guid? _noteId;
    private int _loadGeneration;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _content = string.Empty;

    [ObservableProperty]
    private string _previewHtml = string.Empty;

    [ObservableProperty]
    private SaveStatus _saveStatus = SaveStatus.Idle;

    [ObservableProperty]
    private DateTime? _createdAt;

    [ObservableProperty]
    private DateTime? _updatedAt;

    [ObservableProperty]
    private bool _hasNote;

    [ObservableProperty]
    private bool _isFavorite;

    [ObservableProperty]
    private bool _isPinned;

    [ObservableProperty]
    private bool _isDeleted;

    /// <summary>
    /// 每次加载/清空笔记递增，UI 用它强制刷新 RichEditBox，避免串稿。
    /// </summary>
    [ObservableProperty]
    private int _documentRevision;

    public event EventHandler? NoteSaved;

    /// <summary>
    /// 由 UI 提供：保存前从 RichEditBox 读取最新 RTF，避免在 TextChanged 里同步 GetText。
    /// </summary>
    public Func<string>? ContentProvider { get; set; }

    /// <summary>
    /// 由 UI 提供：保存前读取纯文本，供列表摘要使用（比解析 RTF 更准确）。
    /// </summary>
    public Func<string>? PlainTextProvider { get; set; }

    public string? LastSavedPlainText { get; private set; }

    public Guid? CurrentNoteId => _noteId;

    public EditorViewModel(INoteService noteService, ISettingsService settingsService)
    {
        _noteService = noteService;
        _settingsService = settingsService;
        ResetDebounce();
    }

    public void ResetDebounce()
    {
        _debounce?.Dispose();
        _debounce = new DebounceHelper(_settingsService.Current.AutoSaveDelay);
    }

    public async Task LoadNoteAsync(Guid noteId)
    {
        var generation = Interlocked.Increment(ref _loadGeneration);
        try
        {
            _debounce?.Cancel();
            var note = await _noteService.GetAsync(noteId);
            if (generation != _loadGeneration)
            {
                return;
            }

            if (note is null)
            {
                Clear();
                return;
            }

            _suppressChange = true;
            _noteId = note.Id;
            Title = note.Title;
            Content = note.Content;
            PreviewHtml = string.Empty;
            CreatedAt = note.CreatedAt;
            UpdatedAt = note.UpdatedAt;
            IsFavorite = note.IsFavorite;
            IsPinned = note.IsPinned;
            IsDeleted = note.IsDeleted;
            HasNote = true;
            SaveStatus = SaveStatus.Saved;
            DocumentRevision++;
            _suppressChange = false;

            await _settingsService.UpdateAsync(s => s.LastOpenedNoteId = note.Id);
        }
        catch (Exception ex)
        {
            if (generation == _loadGeneration)
            {
                Log.Error(ex, "加载编辑器笔记失败");
                Clear();
            }
        }
    }

    public void Clear()
    {
        Interlocked.Increment(ref _loadGeneration);
        _debounce?.Cancel();
        _suppressChange = true;
        _noteId = null;
        Title = string.Empty;
        Content = string.Empty;
        PreviewHtml = string.Empty;
        CreatedAt = null;
        UpdatedAt = null;
        IsFavorite = false;
        IsPinned = false;
        IsDeleted = false;
        HasNote = false;
        SaveStatus = SaveStatus.Idle;
        DocumentRevision++;
        _suppressChange = false;
    }

    /// <summary>
    /// 正文有改动时由 UI 调用（不要在每次按键时把 RTF 写回 Content）。
    /// </summary>
    public void NotifyBodyEdited()
    {
        if (_suppressChange || !_noteId.HasValue || IsDeleted)
        {
            return;
        }

        SaveStatus = SaveStatus.Editing;
        ScheduleSave();
    }

    partial void OnTitleChanged(string value)
    {
        if (_suppressChange || !_noteId.HasValue || IsDeleted)
        {
            return;
        }

        SaveStatus = SaveStatus.Editing;
        ScheduleSave();
    }

    partial void OnContentChanged(string value)
    {
        // 内容主要由保存前 ContentProvider 拉取；这里不再因 Content 赋值触发保存，
        // 避免与 RichEditBox 往返时形成循环。
    }

    private void ScheduleSave()
    {
        _debounce?.Debounce(async _ => await SaveAsync());
    }

    /// <summary>
    /// 切换笔记前强制落盘，避免防抖被 Cancel 丢内容。
    /// </summary>
    public async Task FlushPendingSaveAsync()
    {
        _debounce?.Cancel();
        if (!_noteId.HasValue || IsDeleted)
        {
            return;
        }

        if (SaveStatus is SaveStatus.Editing or SaveStatus.Failed or SaveStatus.Saving)
        {
            await SaveAsync();
        }
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        var noteId = _noteId;
        if (!noteId.HasValue || IsDeleted)
        {
            return;
        }

        string title;
        string content;
        string plain;
        try
        {
            // RichEditBox 必须在 UI 线程读取
            (title, content, plain) = await ReadSnapshotOnUiAsync(noteId.Value);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "保存前读取编辑器内容失败");
            return;
        }

        if (_noteId != noteId)
        {
            return;
        }

        Content = content;
        LastSavedPlainText = plain;

        try
        {
            _dispatcher.TryEnqueue(() =>
            {
                if (_noteId == noteId)
                {
                    SaveStatus = SaveStatus.Saving;
                }
            });

            await _noteService.SaveContentAsync(noteId.Value, title, content);

            if (_noteId != noteId)
            {
                return;
            }

            _dispatcher.TryEnqueue(() =>
            {
                if (_noteId != noteId)
                {
                    return;
                }

                UpdatedAt = DateTime.Now;
                SaveStatus = SaveStatus.Saved;
            });

            NoteSaved?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "自动保存失败");
            _dispatcher.TryEnqueue(() =>
            {
                if (_noteId == noteId)
                {
                    SaveStatus = SaveStatus.Failed;
                }
            });
        }
    }

    private Task<(string Title, string Content, string Plain)> ReadSnapshotOnUiAsync(Guid expectedNoteId)
    {
        (string, string, string) ReadLocal()
        {
            if (_noteId != expectedNoteId)
            {
                return (Title, Content, LastSavedPlainText ?? RtfHelper.ToPlainText(Content));
            }

            var latest = ContentProvider?.Invoke() ?? Content;
            var plain = PlainTextProvider?.Invoke() ?? RtfHelper.ToPlainText(latest);
            return (Title, latest, plain);
        }

        if (_dispatcher.HasThreadAccess)
        {
            return Task.FromResult(ReadLocal());
        }

        var tcs = new TaskCompletionSource<(string, string, string)>();
        if (!_dispatcher.TryEnqueue(() =>
            {
                try
                {
                    tcs.TrySetResult(ReadLocal());
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            }))
        {
            tcs.TrySetResult((Title, Content, RtfHelper.ToPlainText(Content)));
        }

        return tcs.Task;
    }

    public void Dispose()
    {
        _debounce?.Dispose();
    }
}
