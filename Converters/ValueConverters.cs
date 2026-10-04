using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;
using NoteManager.Models;

namespace NoteManager.Converters;

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var flag = value is true;
        if (parameter is string p && p.Equals("Invert", StringComparison.OrdinalIgnoreCase))
        {
            flag = !flag;
        }

        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        value is Visibility.Visible;
}

public sealed class SaveStatusToTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value is SaveStatus status
            ? status switch
            {
                SaveStatus.Editing => "正在编辑",
                SaveStatus.Saving => "正在保存...",
                SaveStatus.Saved => "已保存",
                SaveStatus.Failed => "保存失败",
                _ => string.Empty
            }
            : string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => null!;
}

public sealed class DateTimeToLocalStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is DateTime dt)
        {
            return dt.ToString("yyyy-MM-dd HH:mm");
        }

        if (value is DateTime nullable && nullable != default)
        {
            return nullable.ToString("yyyy-MM-dd HH:mm");
        }

        return string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => null!;
}

public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var visible = value is not null;
        if (parameter is string p && p.Equals("Invert", StringComparison.OrdinalIgnoreCase))
        {
            visible = !visible;
        }

        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => null!;
}
