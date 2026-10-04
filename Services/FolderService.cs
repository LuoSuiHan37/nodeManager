using NoteManager.Models;
using NoteManager.Repositories;
using Serilog;

namespace NoteManager.Services;

public class FolderService : IFolderService
{
    private readonly IFolderRepository _folderRepository;

    public FolderService(IFolderRepository folderRepository)
    {
        _folderRepository = folderRepository;
    }

    public const string DefaultFolderName = "默认文件夹";

    public Task<List<Folder>> GetAllAsync() => _folderRepository.GetAllAsync();

    public async Task<Folder> GetOrCreateDefaultAsync()
    {
        var all = await _folderRepository.GetAllAsync();
        var existing = all
            .Where(f => f.ParentId is null)
            .OrderBy(f => f.SortOrder)
            .ThenBy(f => f.CreatedAt)
            .FirstOrDefault(f => f.Name == DefaultFolderName)
            ?? all.Where(f => f.ParentId is null).OrderBy(f => f.SortOrder).ThenBy(f => f.CreatedAt).FirstOrDefault();

        if (existing is not null)
        {
            return existing;
        }

        return await CreateAsync(DefaultFolderName);
    }

    public Task<int> PromoteAllFoldersToRootAsync()
        => _folderRepository.PromoteAllToRootAsync();

    public async Task<Folder> CreateAsync(string name, Guid? parentId = null)
    {
        try
        {
            // 产品约定：文件夹一律根级，与「默认文件夹」同级
            parentId = null;
            var sort = await _folderRepository.GetMaxSortOrderAsync(parentId) + 1;
            var folder = new Folder
            {
                Id = Guid.NewGuid(),
                Name = string.IsNullOrWhiteSpace(name) ? "新建文件夹" : name.Trim(),
                ParentId = parentId,
                CreatedAt = DateTime.Now,
                SortOrder = sort
            };

            return await _folderRepository.AddAsync(folder);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "创建文件夹失败");
            throw;
        }
    }

    public async Task RenameAsync(Guid id, string name)
    {
        try
        {
            var folder = await _folderRepository.GetByIdAsync(id);
            if (folder is null)
            {
                throw new InvalidOperationException("文件夹不存在");
            }

            folder.Name = string.IsNullOrWhiteSpace(name) ? folder.Name : name.Trim();
            await _folderRepository.UpdateAsync(folder);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "重命名文件夹失败 Id={Id}", id);
            throw;
        }
    }

    public async Task DeleteAsync(Guid id)
    {
        try
        {
            var defaults = await GetOrCreateDefaultAsync();
            if (defaults.Id == id)
            {
                throw new InvalidOperationException("不能删除默认文件夹");
            }

            await _folderRepository.DeleteAsync(id, fallbackFolderId: defaults.Id);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "删除文件夹失败 Id={Id}", id);
            throw;
        }
    }
}
