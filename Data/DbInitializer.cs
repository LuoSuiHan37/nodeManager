using Microsoft.EntityFrameworkCore;
using NoteManager.Models;
using Serilog;

namespace NoteManager.Data;

public static class DbInitializer
{
    public static async Task InitializeAsync(AppDbContext db, CancellationToken cancellationToken = default)
    {
        try
        {
            await db.Database.EnsureCreatedAsync(cancellationToken);
            await FtsInitializer.EnsureFtsAsync(db, cancellationToken);

            if (!await db.Folders.AnyAsync(cancellationToken))
            {
                var defaultFolder = new Folder
                {
                    Id = Guid.NewGuid(),
                    Name = "默认文件夹",
                    ParentId = null,
                    CreatedAt = DateTime.Now,
                    SortOrder = 0
                };

                db.Folders.Add(defaultFolder);

                db.Notes.Add(new Note
                {
                    Id = Guid.NewGuid(),
                    Title = "欢迎使用 NoteManager",
                    Content = """
                              # 欢迎使用 NoteManager

                              这是你的第一条笔记。

                              ## 快速开始

                              - 左侧可以管理文件夹
                              - 中间浏览笔记列表
                              - 右侧编辑 Markdown 内容

                              ### Markdown 示例

                              **粗体** 与 *斜体*

                              > 引用一段话

                              ```csharp
                              Console.WriteLine("Hello NoteManager");
                              ```

                              [官方文档](https://learn.microsoft.com/windows/apps/winui/)
                              """,
                    FolderId = defaultFolder.Id,
                    IsFavorite = false,
                    IsPinned = true,
                    IsDeleted = false,
                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now
                });

                await db.SaveChangesAsync(cancellationToken);
                await FtsInitializer.RebuildAsync(db, cancellationToken);
                Log.Information("数据库已初始化，已创建默认文件夹和欢迎笔记");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "数据库初始化失败");
            throw;
        }
    }
}
