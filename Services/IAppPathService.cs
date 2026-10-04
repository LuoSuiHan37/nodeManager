namespace NoteManager.Services;

public interface IAppPathService
{
    string RootDirectory { get; }
    string DatabasePath { get; }
    string SettingsPath { get; }
    string LogsDirectory { get; }
    string BackupDirectory { get; }
    void EnsureDirectories();
}
