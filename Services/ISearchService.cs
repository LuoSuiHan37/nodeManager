using NoteManager.Models;

namespace NoteManager.Services;

public interface ISearchService
{
    Task<List<Note>> SearchAsync(string keyword, bool includeDeleted = false);
}
