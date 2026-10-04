using CommunityToolkit.Mvvm.ComponentModel;
using NoteManager.Helpers;
using NoteManager.Models;

namespace NoteManager.ViewModels;

public partial class NoteItemViewModel : ObservableObject
{
    [ObservableProperty]
    private Guid _id;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _summary = string.Empty;

    [ObservableProperty]
    private DateTime _updatedAt;

    [ObservableProperty]
    private DateTime _createdAt;

    [ObservableProperty]
    private bool _isFavorite;

    [ObservableProperty]
    private bool _isPinned;

    [ObservableProperty]
    private bool _isDeleted;

    [ObservableProperty]
    private Guid? _folderId;

    public string UpdatedAtText => UpdatedAt.ToString("yyyy-MM-dd HH:mm");

    public string FavoriteMark => IsFavorite ? "★" : string.Empty;

    public string PinMark => IsPinned ? "[顶]" : string.Empty;

    public static NoteItemViewModel FromNote(Note note) => new()
    {
        Id = note.Id,
        Title = string.IsNullOrWhiteSpace(note.Title) ? "未命名笔记" : note.Title,
        Summary = MarkdownHelper.GetSummary(note.Content),
        UpdatedAt = note.UpdatedAt,
        CreatedAt = note.CreatedAt,
        IsFavorite = note.IsFavorite,
        IsPinned = note.IsPinned,
        IsDeleted = note.IsDeleted,
        FolderId = note.FolderId
    };

    public void Apply(Note note, string? plainTextOverride = null)
    {
        Id = note.Id;
        Title = string.IsNullOrWhiteSpace(note.Title) ? "未命名笔记" : note.Title;
        Summary = string.IsNullOrWhiteSpace(plainTextOverride)
            ? MarkdownHelper.GetSummary(note.Content)
            : MarkdownHelper.GetSummary(plainTextOverride);
        UpdatedAt = note.UpdatedAt;
        CreatedAt = note.CreatedAt;
        IsFavorite = note.IsFavorite;
        IsPinned = note.IsPinned;
        IsDeleted = note.IsDeleted;
        FolderId = note.FolderId;
        OnPropertyChanged(nameof(UpdatedAtText));
        OnPropertyChanged(nameof(FavoriteMark));
        OnPropertyChanged(nameof(PinMark));
    }
}
