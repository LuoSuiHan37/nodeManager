using NoteManager.Helpers;
using Serilog;

namespace NoteManager.Services;

public class BackupService : IBackupService
{
    public async Task<string?> CreateBackupAsync()
    {
        try
        {
            AppPaths.EnsureDirectories();
            if (!File.Exists(AppPaths.DatabasePath))
            {
                return null;
            }

            var fileName = $"notes-backup-{DateTime.Now:yyyyMMdd-HHmmss}.db";
            var target = Path.Combine(AppPaths.BackupDirectory, fileName);

            await using var source = File.Open(AppPaths.DatabasePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            await using var destination = File.Create(target);
            await source.CopyToAsync(destination);

            Log.Information("已创建数据库备份: {Path}", target);
            return target;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "创建备份失败");
            return null;
        }
    }
}
