using System.Text.Json;
using NoteManager.Helpers;
using NoteManager.Models;
using Serilog;

namespace NoteManager.Services;

public class SettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly SemaphoreSlim _lock = new(1, 1);

    public AppSettings Current { get; private set; } = new();

    public async Task LoadAsync()
    {
        await _lock.WaitAsync();
        try
        {
            AppPaths.EnsureBootstrapDirectories();
            if (!File.Exists(AppPaths.SettingsPath))
            {
                Current = new AppSettings
                {
                    StoragePath = AppPaths.BootstrapRoot
                };
                await WriteFileAsync(Current);
                AppPaths.InitializeStorageRoot(Current.StoragePath);
                AppPaths.EnsureDirectories();
                return;
            }

            var json = await File.ReadAllTextAsync(AppPaths.SettingsPath);
            Current = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
            if (string.IsNullOrWhiteSpace(Current.StoragePath))
            {
                Current.StoragePath = AppPaths.BootstrapRoot;
            }

            AppPaths.InitializeStorageRoot(Current.StoragePath);
            AppPaths.EnsureDirectories();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "读取配置失败，已回退默认配置");
            Current = new AppSettings { StoragePath = AppPaths.BootstrapRoot };
            AppPaths.InitializeStorageRoot(Current.StoragePath);
            try
            {
                await WriteFileAsync(Current);
            }
            catch (Exception writeEx)
            {
                Log.Error(writeEx, "写入默认配置失败");
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveAsync()
    {
        await _lock.WaitAsync();
        try
        {
            await WriteFileAsync(Current);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "保存配置失败");
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task UpdateAsync(Action<AppSettings> update)
    {
        await _lock.WaitAsync();
        try
        {
            update(Current);
            await WriteFileAsync(Current);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "更新配置失败");
        }
        finally
        {
            _lock.Release();
        }
    }

    private static async Task WriteFileAsync(AppSettings settings)
    {
        AppPaths.EnsureBootstrapDirectories();
        var json = JsonSerializer.Serialize(settings, JsonOptions);
        await File.WriteAllTextAsync(AppPaths.SettingsPath, json);
    }
}
