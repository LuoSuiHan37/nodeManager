namespace NoteManager.Services;

public interface IBackupService
{
    Task<string?> CreateBackupAsync();
}
