namespace NoteManager.Helpers;

/// <summary>
/// 路径约定：
/// - 配置/日志固定在 %LOCALAPPDATA%\NoteManager\
/// - 笔记数据目录可由设置 StoragePath 指定
/// </summary>
public static class AppPaths
{
    public static string BootstrapRoot { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NoteManager");

    private static string _storageRoot = BootstrapRoot;

    public static string RootDirectory => _storageRoot;

    public static string ConfigDirectory => Path.Combine(BootstrapRoot, "Config");

    public static string LogsDirectory => Path.Combine(BootstrapRoot, "Logs");

    public static string SettingsPath => Path.Combine(ConfigDirectory, "settings.json");

    public static string DataDirectory => Path.Combine(RootDirectory, "Data");

    public static string AttachmentsDirectory => Path.Combine(RootDirectory, "Attachments");

    public static string BackupDirectory => Path.Combine(RootDirectory, "Backup");

    public static string DatabasePath => Path.Combine(DataDirectory, "notes.db");

    public static void InitializeStorageRoot(string? storagePath)
    {
        if (string.IsNullOrWhiteSpace(storagePath))
        {
            _storageRoot = BootstrapRoot;
            return;
        }

        try
        {
            var full = Path.GetFullPath(storagePath.Trim());
            Directory.CreateDirectory(full);
            _storageRoot = full;
        }
        catch
        {
            _storageRoot = BootstrapRoot;
        }
    }

    public static void EnsureBootstrapDirectories()
    {
        Directory.CreateDirectory(ConfigDirectory);
        Directory.CreateDirectory(LogsDirectory);
    }

    public static void EnsureDirectories()
    {
        EnsureBootstrapDirectories();
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(AttachmentsDirectory);
        Directory.CreateDirectory(BackupDirectory);
    }
}
