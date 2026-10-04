using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NoteManager.Data;
using NoteManager.Helpers;
using NoteManager.Repositories;
using Serilog;

namespace NoteManager.Services;

/// <summary>
/// 应用级依赖注入容器。
/// </summary>
public static class AppServices
{
    public static IServiceProvider Services { get; private set; } = null!;

    public static async Task InitializeAsync()
    {
        AppPaths.EnsureBootstrapDirectories();

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File(
                path: Path.Combine(AppPaths.LogsDirectory, "log-.txt"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 14,
                shared: true)
            .CreateLogger();

        var services = new ServiceCollection();

        services.AddSingleton<IAppPathService, AppPathService>();
        services.AddSingleton<IBackupService, BackupService>();
        services.AddSingleton<INoteRepository, NoteRepository>();
        services.AddSingleton<IFolderRepository, FolderRepository>();
        services.AddSingleton<INoteService, NoteService>();
        services.AddSingleton<IFolderService, FolderService>();
        services.AddSingleton<ISearchService, SearchService>();

        // 先加载设置以确定存储路径，再注册 DbContext
        var earlySettings = new SettingsService();
        await earlySettings.LoadAsync();
        services.AddSingleton<ISettingsService>(earlySettings);

        services.AddDbContextFactory<AppDbContext>(options =>
            options.UseSqlite($"Data Source={AppPaths.DatabasePath}"));

        Services = services.BuildServiceProvider();

        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
        await using var context = await db.CreateDbContextAsync();
        await DbInitializer.InitializeAsync(context);

        Log.Information("应用服务初始化完成，存储目录: {Root}，数据库: {Path}", AppPaths.RootDirectory, AppPaths.DatabasePath);
    }

    public static T GetRequiredService<T>() where T : notnull =>
        Services.GetRequiredService<T>();
}
