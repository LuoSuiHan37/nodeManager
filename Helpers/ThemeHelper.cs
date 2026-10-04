using Microsoft.UI.Xaml;

namespace NoteManager.Helpers;

public static class ThemeHelper
{
    public static void ApplyTheme(string theme)
    {
        if (Application.Current is null)
        {
            return;
        }

        Application.Current.RequestedTheme = theme switch
        {
            "Light" => ApplicationTheme.Light,
            "Dark" => ApplicationTheme.Dark,
            _ => Application.Current.RequestedTheme
        };

        // System 主题：不强制覆盖，交由系统处理；若之前强制过，则恢复为跟随系统
        if (theme is "System" or "")
        {
            // WinUI Application.RequestedTheme 一旦设置后无法完美重置为跟随系统，
            // 这里选择不改动，启动时再按配置应用。
        }
    }

    public static ElementTheme ToElementTheme(string theme) => theme switch
    {
        "Light" => ElementTheme.Light,
        "Dark" => ElementTheme.Dark,
        _ => ElementTheme.Default
    };
}
