using NoteManager.Models;

namespace NoteManager.Services;

public interface ISettingsService
{
    AppSettings Current { get; }

    Task LoadAsync();

    Task SaveAsync();

    Task UpdateAsync(Action<AppSettings> update);
}
