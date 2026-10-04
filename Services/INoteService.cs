using NoteManager.Models;

namespace NoteManager.Services;

public interface INoteService
{
    Task<List<Note>> GetNotesAsync(NoteFilterKind filter, Guid? folderId = null, string? search = null);

    Task<Note?> GetAsync(Guid id);

    Task<Note> CreateAsync(Guid? folderId = null, string? title = null);

    Task SaveContentAsync(Guid id, string title, string content);

    Task ToggleFavoriteAsync(Guid id);

    Task TogglePinAsync(Guid id);

    Task MoveToTrashAsync(Guid id);

    Task RestoreAsync(Guid id);

    Task PermanentDeleteAsync(Guid id);

    Task MoveToFolderAsync(Guid noteId, Guid? folderId);

    /// <summary>
    /// 把未归类笔记归入默认文件夹。
    /// </summary>
    Task<int> AssignUncategorizedToFolderAsync(Guid folderId);
}
