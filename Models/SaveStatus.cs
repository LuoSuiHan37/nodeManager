namespace NoteManager.Models;

/// <summary>
/// 编辑器自动保存状态。
/// </summary>
public enum SaveStatus
{
    Idle,
    Editing,
    Saving,
    Saved,
    Failed
}
