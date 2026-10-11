using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NoteManager.Helpers;
using NoteManager.Models;
using NoteManager.Services;
using NoteManager.ViewModels;
using Serilog;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI;
using WinRT.Interop;

namespace NoteManager;

public sealed partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; }

    private readonly IAiService _aiService;
    private readonly INoteService _noteService;
    private readonly IFolderService _folderService;

    private bool _suppressEditorEvents;
    private bool _suppressFontSizeEvent;
    private bool _suppressNoteListSelection;
    private bool _isLoading = true;
    private Guid? _dragNoteId;
    private int _loadedDocumentRevision = -1;

    public MainWindow(MainViewModel viewModel)
    {
        ViewModel = viewModel;
        _aiService = AppServices.GetRequiredService<IAiService>();
        _noteService = AppServices.GetRequiredService<INoteService>();
        _folderService = AppServices.GetRequiredService<IFolderService>();
        InitializeComponent();
        try { AppWindow.SetIcon("Assets/AppIcon.ico"); } catch { }
        ApplyWindowBounds();
        Closed += OnClosed;
        Activated += OnActivated;
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.StatusMessage))
            {
                DispatcherQueue.TryEnqueue(() => StatusText.Text = ViewModel.StatusMessage);
            }
        };
    }

    private async void OnActivated(object sender, WindowActivatedEventArgs args)
    {
        Activated -= OnActivated;
        try
        {
            Log.Information("OnActivated begin");
            await ViewModel.InitializeAsync();
            BindLists();
            ViewModel.Editor.PropertyChanged += Editor_PropertyChanged;
            ViewModel.Editor.ContentProvider = ReadEditorRtf;
            ViewModel.Editor.PlainTextProvider = ReadEditorPlain;
            _isLoading = false;
            if (ViewModel.Notes.Count > 0)
            {
                _suppressNoteListSelection = true;
                NoteList.SelectedItem = ViewModel.Notes[0];
                _suppressNoteListSelection = false;
                await ViewModel.SelectNoteCommand.ExecuteAsync(ViewModel.Notes[0]);
                RefreshEditorChrome();
            }
            StatusText.Text = ViewModel.StatusMessage;
            RefreshStoragePathText();
            Log.Information("OnActivated end");
        }
        catch (Exception ex)
        {
            _isLoading = false;
            Log.Error(ex, "Window init failed");
            StatusText.Text = "初始化失败";
        }
    }

    private void RefreshStoragePathText()
    {
        StoragePathText.Text = AppPaths.RootDirectory;
    }

    private void BindLists()
    {
        NoteList.ItemsSource = ViewModel.Notes;
        RebuildFolderTree();
    }

    private void RebuildFolderTree()
    {
        FolderTree.RootNodes.Clear();
        foreach (var root in ViewModel.Folders)
        {
            FolderTree.RootNodes.Add(CreateFolderNode(root));
        }
    }

    private static TreeViewNode CreateFolderNode(FolderItemViewModel folder)
    {
        var node = new TreeViewNode
        {
            Content = folder,
            // 无子级时不展开，避免显示无意义的折叠箭头
            IsExpanded = folder.Children.Count > 0
        };
        foreach (var child in folder.Children)
        {
            node.Children.Add(CreateFolderNode(child));
        }
        return node;
    }

    private void Editor_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(EditorViewModel.Title)
            or nameof(EditorViewModel.HasNote) or nameof(EditorViewModel.IsDeleted)
            or nameof(EditorViewModel.CreatedAt) or nameof(EditorViewModel.UpdatedAt)
            or nameof(EditorViewModel.SaveStatus)
            or nameof(EditorViewModel.DocumentRevision))
        {
            DispatcherQueue.TryEnqueue(RefreshEditorChrome);
        }
    }

    private void RefreshEditorChrome()
    {
        try
        {
            _suppressEditorEvents = true;
            TitleBox.Text = ViewModel.Editor.Title ?? string.Empty;
            var canEdit = ViewModel.Editor.HasNote && !ViewModel.Editor.IsDeleted;
            EditorBox.IsReadOnly = !canEdit;
            EditorBox.IsEnabled = true;
            TitleBox.IsEnabled = canEdit;
            RestoreButton.Visibility = ViewModel.Editor.IsDeleted ? Visibility.Visible : Visibility.Collapsed;
            CreatedAtText.Text = ViewModel.Editor.CreatedAt is DateTime c
                ? UiTexts.CreatedPrefix + c.ToString("yyyy-MM-dd HH:mm")
                : UiTexts.CreatedPrefix + "-";
            UpdatedAtText.Text = ViewModel.Editor.UpdatedAt is DateTime u
                ? UiTexts.UpdatedPrefix + u.ToString("yyyy-MM-dd HH:mm")
                : UiTexts.UpdatedPrefix + "-";
            SaveStatusText.Text = ViewModel.Editor.SaveStatus switch
            {
                SaveStatus.Editing => UiTexts.Editing,
                SaveStatus.Saving => UiTexts.Saving,
                SaveStatus.Saved => UiTexts.Saved,
                SaveStatus.Failed => UiTexts.SaveFailed,
                _ => string.Empty
            };

            // 仅在加载新笔记时回灌文档，编辑过程绝不 SetText
            if (ViewModel.Editor.DocumentRevision != _loadedDocumentRevision)
            {
                _loadedDocumentRevision = ViewModel.Editor.DocumentRevision;
                LoadEditorDocument(ViewModel.Editor.HasNote ? ViewModel.Editor.Content : string.Empty);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "RefreshEditorChrome failed");
        }
        finally
        {
            _suppressEditorEvents = false;
        }
    }

    private void LoadEditorDocument(string? content)
    {
        try
        {
            EditorBox.IsReadOnly = false;
            if (string.IsNullOrEmpty(content))
            {
                EditorBox.Document.SetText(TextSetOptions.None, string.Empty);
                return;
            }

            if (RtfHelper.IsRtf(content))
            {
                EditorBox.Document.SetText(TextSetOptions.FormatRtf, content);
            }
            else
            {
                EditorBox.Document.SetText(TextSetOptions.None, content);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "加载富文本失败，回退纯文本");
            EditorBox.Document.SetText(TextSetOptions.None, RtfHelper.ToPlainText(content));
        }
        finally
        {
            EditorBox.IsReadOnly = !ViewModel.Editor.HasNote || ViewModel.Editor.IsDeleted;
        }
    }

    private string ReadEditorRtf()
    {
        try
        {
            EditorBox.Document.GetText(TextGetOptions.FormatRtf, out var rtf);
            return rtf ?? string.Empty;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "读取编辑器 RTF 失败");
            EditorBox.Document.GetText(TextGetOptions.UseCrlf, out var plain);
            return plain ?? string.Empty;
        }
    }

    private string ReadEditorPlain()
    {
        try
        {
            EditorBox.Document.GetText(TextGetOptions.UseCrlf, out var plain);
            return (plain ?? string.Empty).TrimEnd('\r', '\n');
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "读取编辑器纯文本失败");
            return RtfHelper.ToPlainText(ReadEditorRtf());
        }
    }

    private ITextSelection? GetEditorSelection()
    {
        if (!ViewModel.Editor.HasNote || ViewModel.Editor.IsDeleted || EditorBox.IsReadOnly)
        {
            return null;
        }

        return EditorBox.Document.Selection;
    }

    private static void EnsureSelectionHasText(ITextSelection selection)
    {
        selection.GetText(TextGetOptions.None, out var text);
        if (!string.IsNullOrEmpty(text))
        {
            return;
        }

        var start = selection.StartPosition;
        selection.SetText(TextSetOptions.None, "文本");
        selection.StartPosition = start;
        selection.EndPosition = start + 2;
    }

    /// <summary>
    /// 读取选区/光标处「字符真实字号」，避免沿用上一次的待输入格式。
    /// 混合字号时返回 null。
    /// </summary>
    private double? GetActualSelectionFontSize()
    {
        var selection = EditorBox.Document.Selection;
        var start = selection.StartPosition;
        var end = selection.EndPosition;

        EditorBox.Document.GetText(TextGetOptions.None, out var allText);
        var length = allText?.Length ?? 0;
        if (length <= 0)
        {
            return Math.Round(selection.CharacterFormat.Size);
        }

        // 光标：读右侧字符，否则读左侧字符（真实字形格式）
        if (start == end)
        {
            if (start < length)
            {
                return Math.Round(EditorBox.Document.GetRange(start, start + 1).CharacterFormat.Size);
            }

            if (start > 0)
            {
                return Math.Round(EditorBox.Document.GetRange(start - 1, start).CharacterFormat.Size);
            }

            return Math.Round(selection.CharacterFormat.Size);
        }

        var first = Math.Round(EditorBox.Document.GetRange(start, start + 1).CharacterFormat.Size);
        var last = Math.Round(EditorBox.Document.GetRange(Math.Max(start, end - 1), end).CharacterFormat.Size);
        if (Math.Abs(first - last) > 0.5)
        {
            return null; // 混合字号
        }

        return first;
    }

    private int FindFontSizeComboIndex(double size)
    {
        for (var i = 0; i < FontSizeCombo.Items.Count; i++)
        {
            if (TryGetComboDouble(FontSizeCombo.Items[i], out var d) && Math.Abs(d - size) < 0.5)
            {
                return i;
            }
        }

        return -1;
    }

    private static bool TryGetComboDouble(object? item, out double value)
    {
        switch (item)
        {
            case double d:
                value = d;
                return true;
            case float f:
                value = f;
                return true;
            case int i:
                value = i;
                return true;
            default:
                return double.TryParse(item?.ToString(), out value);
        }
    }

    private void SyncFontSizeComboFromSelection()
    {
        if (_suppressEditorEvents)
        {
            return;
        }

        try
        {
            var actual = GetActualSelectionFontSize();
            _suppressFontSizeEvent = true;

            // 先清空，保证之后即使再选同一字号也会触发 SelectionChanged
            FontSizeCombo.SelectedIndex = -1;

            if (actual is double size)
            {
                var index = FindFontSizeComboIndex(size);
                if (index >= 0)
                {
                    FontSizeCombo.SelectedIndex = index;
                }
            }
        }
        catch
        {
            // 选区瞬时状态可能不可用
        }
        finally
        {
            DispatcherQueue.TryEnqueue(() => _suppressFontSizeEvent = false);
        }
    }

    private void ApplyFontSizeFromCombo(bool force)
    {
        if (!force && (_suppressFontSizeEvent || _suppressEditorEvents))
        {
            return;
        }

        if (_suppressEditorEvents)
        {
            return;
        }

        var selection = GetEditorSelection();
        if (selection is null || !TryGetComboDouble(FontSizeCombo.SelectedItem, out var size))
        {
            return;
        }

        // 对选区直接写入真实字号（含「选中同一字号再次应用」）
        selection.CharacterFormat.Size = (float)size;
        ViewModel.Editor.NotifyBodyEdited();
        SyncFontSizeComboFromSelection();
    }



    private void ApplyWindowBounds()
    {
        try
        {
            // 窗口默认占屏幕工作区约 70%，并居中显示
            var hwnd = WindowNative.GetWindowHandle(this);
            var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
            var workArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary).WorkArea;

            var width = Math.Max(960, (int)(workArea.Width * 0.7));
            var height = Math.Max(640, (int)(workArea.Height * 0.7));
            width = Math.Min(width, workArea.Width);
            height = Math.Min(height, workArea.Height);

            var x = workArea.X + (workArea.Width - width) / 2;
            var y = workArea.Y + (workArea.Height - height) / 2;

            AppWindow.MoveAndResize(new RectInt32(x, y, width, height));
        }
        catch
        {
            AppWindow.Resize(new SizeInt32(1280, 800));
        }
    }

    private async void OnClosed(object sender, WindowEventArgs args)
    {
        try
        {
            var pos = AppWindow.Position;
            var size = AppWindow.Size;
            await AppServices.GetRequiredService<ISettingsService>().UpdateAsync(s =>
            {
                s.WindowWidth = size.Width;
                s.WindowHeight = size.Height;
                s.WindowX = pos.X;
                s.WindowY = pos.Y;
            });
        }
        catch { }
        finally
        {
            ViewModel.Dispose();
        }
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput)
        {
            ViewModel.SearchText = sender.Text;
            _ = RefreshNotesUiAsync();
        }
    }

    private async Task RefreshNotesUiAsync()
    {
        await ViewModel.RefreshNotesAsync(keepSelection: true);
        NoteList.ItemsSource = null;
        NoteList.ItemsSource = ViewModel.Notes;
        StatusText.Text = ViewModel.StatusMessage;
    }

    private async Task RefreshFoldersUiAsync()
    {
        await ViewModel.RefreshFoldersAsync();
        RebuildFolderTree();
    }

    private async void ShowAll_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.ShowAllCommand.ExecuteAsync(null);
        await RefreshNotesUiAsync();
    }

    private async void ShowFavorites_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.ShowFavoritesCommand.ExecuteAsync(null);
        await RefreshNotesUiAsync();
    }

    private async void ShowRecent_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.ShowRecentCommand.ExecuteAsync(null);
        await RefreshNotesUiAsync();
    }

    private async void ShowTrash_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.ShowTrashCommand.ExecuteAsync(null);
        await RefreshNotesUiAsync();
    }

    private async void CreateNote_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.CreateNoteCommand.ExecuteAsync(null);
        await RefreshNotesUiAsync();
        RefreshEditorChrome();
    }

    private async void NoteList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoading || _suppressNoteListSelection) return;
        if (NoteList.SelectedItem is NoteItemViewModel note)
        {
            // 切换前先落盘当前笔记，再加载目标笔记
            await ViewModel.Editor.FlushPendingSaveAsync();
            await ViewModel.SelectNoteCommand.ExecuteAsync(note);
            RefreshEditorChrome();
        }
    }

    private async void FolderTree_ItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
    {
        if (_isLoading) return;
        FolderItemViewModel? folder = args.InvokedItem as FolderItemViewModel
            ?? (args.InvokedItem as TreeViewNode)?.Content as FolderItemViewModel;
        if (folder is null) return;
        await ViewModel.SelectFolderCommand.ExecuteAsync(folder.Id);
        await RefreshNotesUiAsync();
    }

    private void NoteList_DragItemsStarting(object sender, DragItemsStartingEventArgs e)
    {
        if (e.Items.FirstOrDefault() is NoteItemViewModel note)
        {
            _dragNoteId = note.Id;
            e.Data.SetText(note.Id.ToString());
            e.Data.RequestedOperation = DataPackageOperation.Move;
        }
    }

    private void FolderTree_DragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = _dragNoteId.HasValue ? DataPackageOperation.Move : DataPackageOperation.None;
        if (e.OriginalSource is FrameworkElement { DataContext: TreeViewNode node })
        {
            FolderTree.SelectedNode = node;
        }
    }

    private async void FolderTree_Drop(object sender, DragEventArgs e)
    {
        try
        {
            if (!_dragNoteId.HasValue) return;
            var noteId = _dragNoteId.Value;
            _dragNoteId = null;

            Guid? folderId = null;
            if (FolderTree.SelectedNode?.Content is FolderItemViewModel selected)
            {
                folderId = selected.Id;
            }
            else if (e.OriginalSource is FrameworkElement fe)
            {
                var node = fe.DataContext as TreeViewNode;
                if (node?.Content is FolderItemViewModel folder)
                {
                    folderId = folder.Id;
                }
            }

            if (folderId is null)
            {
                StatusText.Text = UiTexts.SelectFolderFirst;
                return;
            }

            var service = AppServices.GetRequiredService<INoteService>();
            await service.MoveToFolderAsync(noteId, folderId);
            await RefreshNotesUiAsync();
            StatusText.Text = "已移动笔记";
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Drag drop move failed");
            StatusText.Text = "移动失败";
        }
    }

    private async void CreateFolder_Click(object sender, RoutedEventArgs e)
    {
        var box = new TextBox { Text = UiTexts.NewFolder };
        var dialog = Dialog(UiTexts.NewFolder, box, UiTexts.Create);
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await ViewModel.CreateFolderCommand.ExecuteAsync(box.Text);
            await RefreshFoldersUiAsync();
        }
    }

    private async void RenameFolder_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedFolderId is null)
        {
            StatusText.Text = UiTexts.SelectFolderFirst;
            return;
        }

        var current = FlattenFolders(ViewModel.Folders).FirstOrDefault(f => f.Id == ViewModel.SelectedFolderId);
        var box = new TextBox { Text = current?.Name ?? string.Empty };
        var dialog = Dialog(UiTexts.RenameFolder, box, UiTexts.Save);
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await ViewModel.RenameFolderCommand.ExecuteAsync((ViewModel.SelectedFolderId.Value, box.Text));
            await RefreshFoldersUiAsync();
        }
    }

    private async void DeleteFolder_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedFolderId is null)
        {
            StatusText.Text = UiTexts.SelectFolderFirst;
            return;
        }

        var dialog = Dialog(UiTexts.DeleteFolder, new TextBlock { Text = UiTexts.DeleteFolderHint }, UiTexts.Delete);
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await ViewModel.DeleteFolderCommand.ExecuteAsync(ViewModel.SelectedFolderId.Value);
            await RefreshFoldersUiAsync();
            await RefreshNotesUiAsync();
        }
    }

    private async void ToggleFavorite_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.ToggleFavoriteCommand.ExecuteAsync(null);
        await RefreshNotesUiAsync();
    }

    private async void TogglePin_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.TogglePinCommand.ExecuteAsync(null);
        await RefreshNotesUiAsync();
    }

    private async void RestoreNote_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.RestoreNoteCommand.ExecuteAsync(null);
        await RefreshNotesUiAsync();
        RefreshEditorChrome();
    }

    private async void DeleteNote_Click(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.Editor.HasNote) return;
        var trash = ViewModel.Editor.IsDeleted;
        var dialog = Dialog(trash ? UiTexts.DeleteForever : UiTexts.DeleteNote,
            new TextBlock { Text = trash ? UiTexts.DeleteForeverHint : UiTexts.MoveToTrashHint },
            trash ? UiTexts.DeleteForever : UiTexts.MoveToTrash);
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await ViewModel.DeleteNoteCommand.ExecuteAsync(null);
            await RefreshNotesUiAsync();
            RefreshEditorChrome();
        }
    }

    private async void MoveNote_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedNote is null) return;
        var folders = FlattenFolders(ViewModel.Folders).ToList();
        if (folders.Count == 0)
        {
            StatusText.Text = UiTexts.NoFolders;
            return;
        }

        var combo = new ComboBox
        {
            ItemsSource = folders.Select(f => f.Name).ToList(),
            SelectedIndex = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = UiTexts.TargetFolder });
        panel.Children.Add(combo);
        var dialog = Dialog(UiTexts.MoveNote, panel, UiTexts.Move);
        if (await dialog.ShowAsync() == ContentDialogResult.Primary && combo.SelectedIndex >= 0)
        {
            await ViewModel.MoveSelectedNoteCommand.ExecuteAsync(folders[combo.SelectedIndex].Id);
            await RefreshNotesUiAsync();
        }
    }

    private async void ChangeStorage_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FolderPicker();
            picker.FileTypeFilter.Add("*");
            InitializeWithWindow.Initialize(picker, WindowNative.GetWindowHandle(this));

            var folder = await picker.PickSingleFolderAsync();
            if (folder is null)
            {
                return;
            }

            var newRoot = folder.Path;
            if (string.Equals(newRoot, AppPaths.RootDirectory, StringComparison.OrdinalIgnoreCase))
            {
                StatusText.Text = "保存位置未变化";
                return;
            }

            var confirm = Dialog(
                "更改保存位置",
                new TextBlock
                {
                    Text = $"将把笔记数据迁移到：\n{newRoot}\n\n完成后需要重启应用。",
                    TextWrapping = TextWrapping.Wrap
                },
                "迁移并保存");

            if (await confirm.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }

            var oldDb = AppPaths.DatabasePath;
            var newDataDir = Path.Combine(newRoot, "Data");
            Directory.CreateDirectory(newDataDir);
            Directory.CreateDirectory(Path.Combine(newRoot, "Attachments"));
            Directory.CreateDirectory(Path.Combine(newRoot, "Backup"));

            if (File.Exists(oldDb))
            {
                var newDb = Path.Combine(newDataDir, "notes.db");
                File.Copy(oldDb, newDb, overwrite: true);
                foreach (var suffix in new[] { "-wal", "-shm" })
                {
                    var src = oldDb + suffix;
                    if (File.Exists(src))
                    {
                        File.Copy(src, newDb + suffix, overwrite: true);
                    }
                }
            }

            await AppServices.GetRequiredService<ISettingsService>()
                .UpdateAsync(s => s.StoragePath = newRoot);

            AppPaths.InitializeStorageRoot(newRoot);
            RefreshStoragePathText();
            StatusText.Text = "已更改保存位置，请重启应用";

            await Dialog(
                "需要重启",
                new TextBlock { Text = "保存位置已更新。请关闭并重新打开 NoteManager 以使数据库生效。" },
                "知道了").ShowAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "更改保存位置失败");
            StatusText.Text = "更改保存位置失败";
        }
    }

    private async void OpenStorage_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            AppPaths.EnsureDirectories();
            await Windows.System.Launcher.LaunchFolderPathAsync(AppPaths.RootDirectory);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "打开保存目录失败");
            StatusText.Text = "打开目录失败";
        }
    }

    private async void AiSettings_Click(object sender, RoutedEventArgs e)
    {
        var settings = _aiService.Settings;
        var enabled = new CheckBox { Content = "启用 AI", IsChecked = settings.Enabled };
        var endpoint = new TextBox { Header = "接口地址（OpenAI 兼容）", Text = settings.Endpoint, PlaceholderText = "例如 http://localhost:11434/v1" };
        var model = new TextBox { Header = "对话模型", Text = settings.Model };
        var embedding = new TextBox { Header = "Embedding 模型", Text = settings.EmbeddingModel };
        var apiKey = new PasswordBox { Header = "API Key（也可使用环境变量 NOTE_MANAGER_AI_API_KEY）", Password = settings.ApiKey };
        var cloud = new CheckBox { Content = "允许发送内容到云端服务", IsChecked = settings.AllowCloud, IsThreeState = false };
        var panel = new StackPanel { Spacing = 10, Width = 520 };
        panel.Children.Add(new TextBlock { Text = "支持 OpenAI、Ollama、LM Studio 等 OpenAI 兼容接口。涉及密钥的笔记请优先使用本地模型。", TextWrapping = TextWrapping.Wrap, Opacity = 0.75 });
        panel.Children.Add(enabled); panel.Children.Add(endpoint); panel.Children.Add(model); panel.Children.Add(embedding); panel.Children.Add(apiKey); panel.Children.Add(cloud);
        var dialog = Dialog("AI 设置", panel, "保存");
        dialog.CloseButtonText = "取消";
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        await AppServices.GetRequiredService<ISettingsService>().UpdateAsync(s =>
        {
            s.Ai.Enabled = enabled.IsChecked == true;
            s.Ai.Endpoint = endpoint.Text.Trim();
            s.Ai.Model = model.Text.Trim();
            s.Ai.EmbeddingModel = embedding.Text.Trim();
            s.Ai.ApiKey = apiKey.Password.Trim();
            s.Ai.AllowCloud = cloud.IsChecked != false;
        });
        StatusText.Text = "AI 设置已保存";
    }

    private async void ShowAiAssistant_Click(object sender, RoutedEventArgs e)
    {
        await ViewModel.Editor.FlushPendingSaveAsync();
        if (!ViewModel.Editor.HasNote || ViewModel.Editor.IsDeleted)
        {
            StatusText.Text = "请先选择一篇可编辑的笔记";
            return;
        }

        var operation = new ComboBox { Header = "操作", Width = 300, SelectedIndex = 0 };
        operation.Items.Add("总结当前笔记");
        operation.Items.Add("改写当前笔记");
        operation.Items.Add("生成标题");
        operation.Items.Add("提取待办");
        operation.Items.Add("生成标签");
        operation.Items.Add("推荐文件夹");
        operation.Items.Add("建立全库知识索引");
        operation.Items.Add("向我的笔记提问");
        var prompt = new TextBox { Header = "补充要求 / 问题（可选）", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 90, PlaceholderText = "例如：用更简洁的技术文档风格改写" };
        var panel = new StackPanel { Spacing = 12, Width = 520 };
        panel.Children.Add(new TextBlock { Text = "AI 会处理当前编辑器中的内容。生成结果会先展示，确认后才写入笔记。", TextWrapping = TextWrapping.Wrap, Opacity = 0.75 });
        panel.Children.Add(operation); panel.Children.Add(prompt);
        var dialog = Dialog("AI 助手", panel, "执行");
        dialog.CloseButtonText = "取消";
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        try
        {
            var choice = operation.SelectedItem?.ToString() ?? string.Empty;
            var content = ReadEditorPlain();
            string result;
            if (choice == "建立全库知识索引")
            {
                var notes = await _noteService.GetNotesAsync(NoteFilterKind.All);
                var count = await _aiService.BuildKnowledgeIndexAsync(notes);
                StatusText.Text = $"已建立 {count} 条笔记的知识索引";
                return;
            }
            if (choice == "向我的笔记提问")
            {
                var notes = await _noteService.GetNotesAsync(NoteFilterKind.All);
                result = await _aiService.AskKnowledgeAsync(prompt.Text.Trim(), notes);
            }
            else
            {
                result = choice switch
                {
                    "总结当前笔记" => await _aiService.SummarizeAsync(content),
                    "改写当前笔记" => await _aiService.RewriteAsync(content, string.IsNullOrWhiteSpace(prompt.Text) ? "清晰、专业" : prompt.Text.Trim()),
                    "生成标题" => await _aiService.GenerateTitleAsync(content),
                    "提取待办" => await _aiService.ExtractTodosAsync(content),
                    "生成标签" => await _aiService.GenerateTagsAsync(content),
                    "推荐文件夹" => await _aiService.SuggestFolderAsync(content, (await _folderService.GetAllAsync()).Select(f => f.Name)),
                    _ => throw new InvalidOperationException("未知 AI 操作")
                };
            }

            var resultBox = new TextBox { Text = result, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, IsReadOnly = true, Height = 300, Width = 600 };
            var resultDialog = Dialog("AI 结果", resultBox, choice is "生成标题" ? "设置标题" : "插入到正文");
            resultDialog.CloseButtonText = "关闭";
            if (await resultDialog.ShowAsync() == ContentDialogResult.Primary)
            {
                if (choice == "生成标题")
                {
                    TitleBox.Text = result.Trim().Trim('"');
                }
                else
                {
                    var selection = EditorBox.Document.Selection;
                    selection.SetText(TextSetOptions.None, "\n\n" + result.Trim() + "\n");
                    ViewModel.Editor.NotifyBodyEdited();
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "AI 操作失败");
            await Dialog("AI 操作失败", new TextBlock { Text = ex.Message, TextWrapping = TextWrapping.Wrap }, "知道了").ShowAsync();
        }
    }

    private void TitleBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_suppressEditorEvents || !ViewModel.Editor.HasNote) return;
        ViewModel.Editor.Title = TitleBox.Text;
    }

    private void EditorBox_TextChanged(object sender, RoutedEventArgs e)
    {
        // 不要在 TextChanged 里 GetText(RTF)，会重入导致后续无法输入
        if (_suppressEditorEvents || !ViewModel.Editor.HasNote || ViewModel.Editor.IsDeleted)
        {
            return;
        }

        ViewModel.Editor.NotifyBodyEdited();
    }

    private void EditorBox_SelectionChanged(object sender, RoutedEventArgs e)
    {
        SyncFontSizeComboFromSelection();
    }

    private void FormatBold_Click(object sender, RoutedEventArgs e)
    {
        var selection = GetEditorSelection();
        if (selection is null) return;
        EnsureSelectionHasText(selection);
        selection.CharacterFormat.Bold = FormatEffect.Toggle;
        ViewModel.Editor.NotifyBodyEdited();
    }

    private void FormatItalic_Click(object sender, RoutedEventArgs e)
    {
        var selection = GetEditorSelection();
        if (selection is null) return;
        EnsureSelectionHasText(selection);
        selection.CharacterFormat.Italic = FormatEffect.Toggle;
        ViewModel.Editor.NotifyBodyEdited();
    }

    private void FormatUnderline_Click(object sender, RoutedEventArgs e)
    {
        var selection = GetEditorSelection();
        if (selection is null) return;
        EnsureSelectionHasText(selection);
        selection.CharacterFormat.Underline = selection.CharacterFormat.Underline == UnderlineType.None
            ? UnderlineType.Single
            : UnderlineType.None;
        ViewModel.Editor.NotifyBodyEdited();
    }

    private void FontSizeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ApplyFontSizeFromCombo(force: false);
    }

    private void FontSizeCombo_DropDownClosed(object sender, object e)
    {
        // 关闭下拉时强制再应用一次：解决「当前显示已是 20，再选 20 不触发变更」的问题
        ApplyFontSizeFromCombo(force: true);
    }

    private void FormatColor_Click(object sender, RoutedEventArgs e)
    {
        var selection = GetEditorSelection();
        if (selection is null || sender is not FrameworkElement { Tag: string hex })
        {
            return;
        }

        selection.GetText(TextGetOptions.None, out var selected);
        if (string.IsNullOrEmpty(selected))
        {
            EnsureSelectionHasText(selection);
        }

        selection.CharacterFormat.ForegroundColor = ParseColor(hex);
        ViewModel.Editor.NotifyBodyEdited();
    }

    private void FormatHeading_Click(object sender, RoutedEventArgs e)
    {
        var selection = GetEditorSelection();
        if (selection is null) return;
        EnsureSelectionHasText(selection);
        selection.CharacterFormat.Size = 24;
        selection.CharacterFormat.Bold = FormatEffect.On;
        _suppressFontSizeEvent = true;
        FontSizeCombo.SelectedItem = 24d;
        DispatcherQueue.TryEnqueue(() => _suppressFontSizeEvent = false);
        ViewModel.Editor.NotifyBodyEdited();
    }

    private void FormatList_Click(object sender, RoutedEventArgs e)
    {
        var selection = GetEditorSelection();
        if (selection is null) return;

        selection.ParagraphFormat.ListType = selection.ParagraphFormat.ListType == MarkerType.None
            ? MarkerType.Bullet
            : MarkerType.None;
        ViewModel.Editor.NotifyBodyEdited();
    }

    private void FormatClear_Click(object sender, RoutedEventArgs e)
    {
        var selection = GetEditorSelection();
        if (selection is null) return;

        selection.CharacterFormat.Bold = FormatEffect.Off;
        selection.CharacterFormat.Italic = FormatEffect.Off;
        selection.CharacterFormat.Underline = UnderlineType.None;
        selection.CharacterFormat.Size = 16;
        selection.CharacterFormat.ForegroundColor = ParseColor("#1A1A1A");
        selection.ParagraphFormat.ListType = MarkerType.None;
        _suppressFontSizeEvent = true;
        FontSizeCombo.SelectedItem = 16d;
        DispatcherQueue.TryEnqueue(() => _suppressFontSizeEvent = false);
        ViewModel.Editor.NotifyBodyEdited();
    }

    private static Color ParseColor(string hex)
    {
        hex = hex.TrimStart('#');
        if (hex.Length != 6)
        {
            return Colors.Black;
        }

        var r = Convert.ToByte(hex[..2], 16);
        var g = Convert.ToByte(hex[2..4], 16);
        var b = Convert.ToByte(hex[4..6], 16);
        return Color.FromArgb(255, r, g, b);
    }

    private ContentDialog Dialog(string title, object content, string primary) => new()
    {
        Title = title,
        Content = content,
        PrimaryButtonText = primary,
        CloseButtonText = UiTexts.Cancel,
        DefaultButton = ContentDialogButton.Primary,
        XamlRoot = RootGrid.XamlRoot
    };

    private static IEnumerable<FolderItemViewModel> FlattenFolders(IEnumerable<FolderItemViewModel> roots)
    {
        foreach (var root in roots)
        {
            yield return root;
            foreach (var child in FlattenFolders(root.Children))
            {
                yield return child;
            }
        }
    }
}
