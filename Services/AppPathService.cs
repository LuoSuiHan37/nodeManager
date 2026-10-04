using NoteManager.Helpers;

namespace NoteManager.Services;

public class AppPathService : IAppPathService
{
    public string RootDirectory => AppPaths.RootDirectory;
    public string DatabasePath => AppPaths.DatabasePath;
    public string SettingsPath => AppPaths.SettingsPath;
    public string LogsDirectory => AppPaths.LogsDirectory;
    public string BackupDirectory => AppPaths.BackupDirectory;

    public void EnsureDirectories() => AppPaths.EnsureDirectories();
}
