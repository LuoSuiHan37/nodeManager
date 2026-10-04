using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NoteManager.Services;
using NoteManager.ViewModels;
using Serilog;

namespace NoteManager;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();
        UnhandledException += OnUnhandledException;
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            await AppServices.InitializeAsync();

            var settings = AppServices.GetRequiredService<ISettingsService>();
            var mainViewModel = new MainViewModel(
                AppServices.GetRequiredService<INoteService>(),
                AppServices.GetRequiredService<IFolderService>(),
                settings,
                AppServices.GetRequiredService<IBackupService>());

            _window = new MainWindow(mainViewModel);
            _window.Activate();
            Log.Information("主窗口已激活");
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "应用启动失败");
            Log.CloseAndFlush();
            try
            {
                _window = new Window { Title = "NoteManager Error" };
                _window.Content = new TextBlock
                {
                    Text = $"启动失败：{ex}",
                    Margin = new Thickness(24),
                    TextWrapping = TextWrapping.Wrap
                };
                _window.Activate();
            }
            catch (Exception showEx)
            {
                Log.Fatal(showEx, "无法显示错误窗口");
                Log.CloseAndFlush();
            }
        }
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        Log.Error(e.Exception, "未处理异常: {Message}", e.Message);
        Log.CloseAndFlush();
        e.Handled = true;
    }
}