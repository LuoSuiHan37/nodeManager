using NoteManager.Models;

namespace NoteManager.Repositories;

public interface INoteRepository
{
    Task<List<Note>> GetAllActiveAsync(CancellationToken cancellationToken = default);

    Task<List<Note>> GetFavoritesAsync(CancellationToken cancellationToken = default);

    Task<List<Note>> GetRecentAsync(int take = 50, CancellationToken cancellationToken = default);

    Task<List<Note>> GetTrashAsync(CancellationToken cancellationToken = default);

    Task<List<Note>> GetByFolderAsync(Guid folderId, CancellationToken cancellationToken = default);

    Task<List<Note>> SearchAsync(string keyword, bool includeDeleted = false, CancellationToken cancellationToken = default);

    Task<Note?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Note> AddAsync(Note note, CancellationToken cancellationToken = default);

    Task UpdateAsync(Note note, CancellationToken cancellationToken = default);

    Task SoftDeleteAsync(Guid id, CancellationToken cancellationToken = default);

    Task RestoreAsync(Guid id, CancellationToken cancellationToken = default);

    Task PermanentDeleteAsync(Guid id, CancellationToken cancellationToken = default);

    Task MoveToFolderAsync(Guid noteId, Guid? folderId, CancellationToken cancellationToken = default);

    Task<int> AssignUncategorizedToFolderAsync(Guid folderId, CancellationToken cancellationToken = default);
}
