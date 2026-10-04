namespace NoteManager.Models;

/// <summary>
/// 文件夹实体，支持多级父子关系。
/// </summary>
public class Folder
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public Guid? ParentId { get; set; }

    public Folder? Parent { get; set; }

    public ICollection<Folder> Children { get; set; } = new List<Folder>();

    public ICollection<Note> Notes { get; set; } = new List<Note>();

    public DateTime CreatedAt { get; set; }

    public int SortOrder { get; set; }
}
