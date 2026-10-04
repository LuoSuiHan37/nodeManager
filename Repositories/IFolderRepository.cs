using NoteManager.Models;

namespace NoteManager.Repositories;

public interface IFolderRepository
{
    Task<List<Folder>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<Folder?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Folder> AddAsync(Folder folder, CancellationToken cancellationToken = default);

    Task UpdateAsync(Folder folder, CancellationToken cancellationToken = default);

    Task DeleteAsync(Guid id, Guid? fallbackFolderId = null, CancellationToken cancellationToken = default);

    Task<int> GetMaxSortOrderAsync(Guid? parentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 将所有子文件夹提升到根级（与默认文件夹同级）。
    /// </summary>
    Task<int> PromoteAllToRootAsync(CancellationToken cancellationToken = default);
}
