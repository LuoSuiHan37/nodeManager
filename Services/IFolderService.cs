using NoteManager.Models;

namespace NoteManager.Services;

public interface IFolderService
{
    Task<List<Folder>> GetAllAsync();

    Task<Folder> GetOrCreateDefaultAsync();

    Task<int> PromoteAllFoldersToRootAsync();

    Task<Folder> CreateAsync(string name, Guid? parentId = null);

    Task RenameAsync(Guid id, string name);

    Task DeleteAsync(Guid id);
}
