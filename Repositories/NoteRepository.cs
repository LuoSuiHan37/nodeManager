using Microsoft.EntityFrameworkCore;
using NoteManager.Data;
using NoteManager.Helpers;
using NoteManager.Models;
using Serilog;

namespace NoteManager.Repositories;

public class NoteRepository : INoteRepository
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public NoteRepository(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<List<Note>> GetAllActiveAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
            return await db.Notes
                .AsNoTracking()
                .Where(n => !n.IsDeleted)
                .OrderByDescending(n => n.IsPinned)
                .ThenByDescending(n => n.UpdatedAt)
                .ToListAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "获取全部笔记失败");
            return [];
        }
    }

    public async Task<List<Note>> GetFavoritesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
            return await db.Notes
                .AsNoTracking()
                .Where(n => !n.IsDeleted && n.IsFavorite)
                .OrderByDescending(n => n.IsPinned)
                .ThenByDescending(n => n.UpdatedAt)
                .ToListAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "获取收藏笔记失败");
            return [];
        }
    }

    public async Task<List<Note>> GetRecentAsync(int take = 50, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
            return await db.Notes
                .AsNoTracking()
                .Where(n => !n.IsDeleted)
                .OrderByDescending(n => n.UpdatedAt)
                .Take(take)
                .ToListAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "获取最近笔记失败");
            return [];
        }
    }

    public async Task<List<Note>> GetTrashAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
            return await db.Notes
                .AsNoTracking()
                .Where(n => n.IsDeleted)
                .OrderByDescending(n => n.DeletedAt)
                .ToListAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "获取回收站失败");
            return [];
        }
    }

    public async Task<List<Note>> GetByFolderAsync(Guid folderId, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
            return await db.Notes
                .AsNoTracking()
                .Where(n => !n.IsDeleted && n.FolderId == folderId)
                .OrderByDescending(n => n.IsPinned)
                .ThenByDescending(n => n.UpdatedAt)
                .ToListAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "按文件夹获取笔记失败 FolderId={FolderId}", folderId);
            return [];
        }
    }

    public async Task<List<Note>> SearchAsync(string keyword, bool includeDeleted = false, CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(keyword))
            {
                return [];
            }

            var connectionString = $"Data Source={AppPaths.DatabasePath}";
            var ftsIds = await FtsInitializer.SearchIdsAsync(connectionString, keyword, cancellationToken);
            if (ftsIds is not null)
            {
                await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
                var matched = await db.Notes.AsNoTracking()
                    .Where(n => ftsIds.Contains(n.Id) && (includeDeleted || !n.IsDeleted))
                    .ToListAsync(cancellationToken);

                var map = matched.ToDictionary(n => n.Id);
                return ftsIds
                    .Where(map.ContainsKey)
                    .Select(id => map[id])
                    .OrderByDescending(n => n.IsPinned)
                    .ThenByDescending(n => n.UpdatedAt)
                    .ToList();
            }

            // FTS 不可用时回退 LIKE
            await using var likeDb = await _dbFactory.CreateDbContextAsync(cancellationToken);
            var key = keyword.Trim().ToLowerInvariant();
            var query = likeDb.Notes.AsNoTracking().AsQueryable();
            if (!includeDeleted)
            {
                query = query.Where(n => !n.IsDeleted);
            }

            return await query
                .Where(n =>
                    EF.Functions.Like(n.Title.ToLower(), $"%{key}%") ||
                    EF.Functions.Like(n.Content.ToLower(), $"%{key}%"))
                .OrderByDescending(n => n.IsPinned)
                .ThenByDescending(n => n.UpdatedAt)
                .ToListAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "搜索笔记失败 Keyword={Keyword}", keyword);
            return [];
        }
    }

    public async Task<Note?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
            return await db.Notes.AsNoTracking().FirstOrDefaultAsync(n => n.Id == id, cancellationToken);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "获取笔记失败 Id={Id}", id);
            return null;
        }
    }

    public async Task<Note> AddAsync(Note note, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        db.Notes.Add(note);
        await db.SaveChangesAsync(cancellationToken);
        await FtsInitializer.UpsertAsync(db, note.Id, note.Title, note.Content, cancellationToken);
        return note;
    }

    public async Task UpdateAsync(Note note, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var existing = await db.Notes.FirstOrDefaultAsync(n => n.Id == note.Id, cancellationToken);
        if (existing is null)
        {
            throw new InvalidOperationException($"笔记不存在: {note.Id}");
        }

        existing.Title = note.Title;
        existing.Content = note.Content;
        existing.FolderId = note.FolderId;
        existing.IsFavorite = note.IsFavorite;
        existing.IsPinned = note.IsPinned;
        existing.IsDeleted = note.IsDeleted;
        existing.UpdatedAt = note.UpdatedAt;
        existing.DeletedAt = note.DeletedAt;

        await db.SaveChangesAsync(cancellationToken);
        await FtsInitializer.UpsertAsync(db, existing.Id, existing.Title, existing.Content, cancellationToken);
    }

    public async Task SoftDeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var note = await db.Notes.FirstOrDefaultAsync(n => n.Id == id, cancellationToken);
        if (note is null)
        {
            return;
        }

        note.IsDeleted = true;
        note.DeletedAt = DateTime.Now;
        note.UpdatedAt = DateTime.Now;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task RestoreAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var note = await db.Notes.FirstOrDefaultAsync(n => n.Id == id, cancellationToken);
        if (note is null)
        {
            return;
        }

        note.IsDeleted = false;
        note.DeletedAt = null;
        note.UpdatedAt = DateTime.Now;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task PermanentDeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var note = await db.Notes.FirstOrDefaultAsync(n => n.Id == id, cancellationToken);
        if (note is null)
        {
            return;
        }

        db.Notes.Remove(note);
        await db.SaveChangesAsync(cancellationToken);
        await FtsInitializer.DeleteAsync(db, id, cancellationToken);
    }

    public async Task MoveToFolderAsync(Guid noteId, Guid? folderId, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var note = await db.Notes.FirstOrDefaultAsync(n => n.Id == noteId, cancellationToken);
        if (note is null)
        {
            return;
        }

        note.FolderId = folderId;
        note.UpdatedAt = DateTime.Now;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> AssignUncategorizedToFolderAsync(Guid folderId, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var orphans = await db.Notes
            .Where(n => n.FolderId == null && !n.IsDeleted)
            .ToListAsync(cancellationToken);

        if (orphans.Count == 0)
        {
            return 0;
        }

        var now = DateTime.Now;
        foreach (var note in orphans)
        {
            note.FolderId = folderId;
            note.UpdatedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);
        return orphans.Count;
    }
}
