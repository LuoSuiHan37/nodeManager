using NoteManager.Models;
using NoteManager.Repositories;
using Serilog;

namespace NoteManager.Services;

public class NoteService : INoteService
{
    private readonly INoteRepository _noteRepository;

    public NoteService(INoteRepository noteRepository)
    {
        _noteRepository = noteRepository;
    }

    public async Task<List<Note>> GetNotesAsync(NoteFilterKind filter, Guid? folderId = null, string? search = null)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(search))
            {
                var includeDeleted = filter == NoteFilterKind.Trash;
                return await _noteRepository.SearchAsync(search, includeDeleted);
            }

            return filter switch
            {
                NoteFilterKind.Favorites => await _noteRepository.GetFavoritesAsync(),
                NoteFilterKind.Recent => await _noteRepository.GetRecentAsync(),
                NoteFilterKind.Trash => await _noteRepository.GetTrashAsync(),
                NoteFilterKind.Folder when folderId.HasValue => await _noteRepository.GetByFolderAsync(folderId.Value),
                _ => await _noteRepository.GetAllActiveAsync()
            };
        }
        catch (Exception ex)
        {
            Log.Error(ex, "加载笔记列表失败 Filter={Filter}", filter);
            return [];
        }
    }

    public Task<Note?> GetAsync(Guid id) => _noteRepository.GetByIdAsync(id);

    public async Task<Note> CreateAsync(Guid? folderId = null, string? title = null)
    {
        var now = DateTime.Now;
        var note = new Note
        {
            Id = Guid.NewGuid(),
            Title = string.IsNullOrWhiteSpace(title) ? "未命名笔记" : title.Trim(),
            Content = string.Empty,
            FolderId = folderId, // 调用方应传入默认文件夹，避免未归类
            IsFavorite = false,
            IsPinned = false,
            IsDeleted = false,
            CreatedAt = now,
            UpdatedAt = now
        };

        try
        {
            return await _noteRepository.AddAsync(note);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "创建笔记失败");
            throw;
        }
    }

    public async Task SaveContentAsync(Guid id, string title, string content)
    {
        try
        {
            var note = await _noteRepository.GetByIdAsync(id);
            if (note is null)
            {
                throw new InvalidOperationException("笔记不存在");
            }

            note.Title = string.IsNullOrWhiteSpace(title) ? "未命名笔记" : title.Trim();
            note.Content = content ?? string.Empty;
            note.UpdatedAt = DateTime.Now;
            await _noteRepository.UpdateAsync(note);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "保存笔记失败 Id={Id}", id);
            throw;
        }
    }

    public async Task ToggleFavoriteAsync(Guid id)
    {
        try
        {
            var note = await _noteRepository.GetByIdAsync(id);
            if (note is null)
            {
                return;
            }

            note.IsFavorite = !note.IsFavorite;
            note.UpdatedAt = DateTime.Now;
            await _noteRepository.UpdateAsync(note);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "切换收藏失败 Id={Id}", id);
            throw;
        }
    }

    public async Task TogglePinAsync(Guid id)
    {
        try
        {
            var note = await _noteRepository.GetByIdAsync(id);
            if (note is null)
            {
                return;
            }

            note.IsPinned = !note.IsPinned;
            note.UpdatedAt = DateTime.Now;
            await _noteRepository.UpdateAsync(note);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "切换置顶失败 Id={Id}", id);
            throw;
        }
    }

    public async Task MoveToTrashAsync(Guid id)
    {
        try
        {
            await _noteRepository.SoftDeleteAsync(id);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "移入回收站失败 Id={Id}", id);
            throw;
        }
    }

    public async Task RestoreAsync(Guid id)
    {
        try
        {
            await _noteRepository.RestoreAsync(id);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "恢复笔记失败 Id={Id}", id);
            throw;
        }
    }

    public async Task PermanentDeleteAsync(Guid id)
    {
        try
        {
            await _noteRepository.PermanentDeleteAsync(id);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "永久删除失败 Id={Id}", id);
            throw;
        }
    }

    public async Task MoveToFolderAsync(Guid noteId, Guid? folderId)
    {
        try
        {
            await _noteRepository.MoveToFolderAsync(noteId, folderId);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "移动笔记失败 NoteId={NoteId}", noteId);
            throw;
        }
    }

    public Task<int> AssignUncategorizedToFolderAsync(Guid folderId)
        => _noteRepository.AssignUncategorizedToFolderAsync(folderId);
}
