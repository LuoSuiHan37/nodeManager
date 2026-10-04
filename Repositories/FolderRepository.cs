using Microsoft.EntityFrameworkCore;
using NoteManager.Data;
using NoteManager.Models;
using Serilog;

namespace NoteManager.Repositories;

public class FolderRepository : IFolderRepository
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public FolderRepository(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<List<Folder>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
            return await db.Folders
                .AsNoTracking()
                .OrderBy(f => f.SortOrder)
                .ThenBy(f => f.Name)
                .ToListAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "获取文件夹列表失败");
            return [];
        }
    }

    public async Task<Folder?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
            return await db.Folders.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id, cancellationToken);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "获取文件夹失败 Id={Id}", id);
            return null;
        }
    }

    public async Task<Folder> AddAsync(Folder folder, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        db.Folders.Add(folder);
        await db.SaveChangesAsync(cancellationToken);
        return folder;
    }

    public async Task UpdateAsync(Folder folder, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var existing = await db.Folders.FirstOrDefaultAsync(f => f.Id == folder.Id, cancellationToken);
        if (existing is null)
        {
            throw new InvalidOperationException($"文件夹不存在: {folder.Id}");
        }

        existing.Name = folder.Name;
        existing.ParentId = folder.ParentId;
        existing.SortOrder = folder.SortOrder;
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(Guid id, Guid? fallbackFolderId = null, CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var folder = await db.Folders.FirstOrDefaultAsync(f => f.Id == id, cancellationToken);
        if (folder is null)
        {
            return;
        }

        var hasChildren = await db.Folders.AnyAsync(f => f.ParentId == id, cancellationToken);
        if (hasChildren)
        {
            throw new InvalidOperationException("请先删除子文件夹");
        }

        var notes = await db.Notes.Where(n => n.FolderId == id).ToListAsync(cancellationToken);
        foreach (var note in notes)
        {
            note.FolderId = fallbackFolderId;
            note.UpdatedAt = DateTime.Now;
        }

        db.Folders.Remove(folder);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> PromoteAllToRootAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        var nested = await db.Folders.Where(f => f.ParentId != null).ToListAsync(cancellationToken);
        if (nested.Count == 0)
        {
            return 0;
        }

        var maxRootSort = await db.Folders
            .Where(f => f.ParentId == null)
            .Select(f => (int?)f.SortOrder)
            .MaxAsync(cancellationToken) ?? 0;

        foreach (var folder in nested.OrderBy(f => f.SortOrder).ThenBy(f => f.CreatedAt))
        {
            maxRootSort++;
            folder.ParentId = null;
            folder.SortOrder = maxRootSort;
        }

        await db.SaveChangesAsync(cancellationToken);
        return nested.Count;
    }

    public async Task<int> GetMaxSortOrderAsync(Guid? parentId, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
            var query = db.Folders.AsNoTracking().Where(f => f.ParentId == parentId);
            if (!await query.AnyAsync(cancellationToken))
            {
                return 0;
            }

            return await query.MaxAsync(f => f.SortOrder, cancellationToken);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "获取文件夹排序失败 ParentId={ParentId}", parentId);
            return 0;
        }
    }
}
