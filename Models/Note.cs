namespace NoteManager.Models;

/// <summary>
/// 笔记实体。
/// </summary>
public class Note
{
    public Guid Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public Guid? FolderId { get; set; }

    public Folder? Folder { get; set; }

    public bool IsFavorite { get; set; }

    public bool IsPinned { get; set; }

    public bool IsDeleted { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public DateTime? DeletedAt { get; set; }
}
