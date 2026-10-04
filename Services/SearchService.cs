using NoteManager.Models;
using NoteManager.Repositories;
using Serilog;

namespace NoteManager.Services;

public class SearchService : ISearchService
{
    private readonly INoteRepository _noteRepository;

    public SearchService(INoteRepository noteRepository)
    {
        _noteRepository = noteRepository;
    }

    public async Task<List<Note>> SearchAsync(string keyword, bool includeDeleted = false)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(keyword))
            {
                return [];
            }

            return await _noteRepository.SearchAsync(keyword, includeDeleted);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "搜索服务失败");
            return [];
        }
    }
}
