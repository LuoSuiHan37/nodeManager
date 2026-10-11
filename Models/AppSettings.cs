namespace NoteManager.Models;

/// <summary>
/// 应用配置，序列化到 settings.json。
/// </summary>
public class AppSettings
{
    public string Theme { get; set; } = "System";

    /// <summary>
    /// 笔记数据保存根目录。为空则使用 %LOCALAPPDATA%\NoteManager。
    /// </summary>
    public string StoragePath { get; set; } = string.Empty;

    public Guid? LastOpenedNoteId { get; set; }

    public double WindowWidth { get; set; } = 1280;

    public double WindowHeight { get; set; } = 800;

    public double? WindowX { get; set; }

    public double? WindowY { get; set; }

    public int AutoSaveDelay { get; set; } = 800;

    public AiSettings Ai { get; set; } = new();
}
