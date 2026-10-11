namespace NoteManager.Models;

/// <summary>
/// AI 配置。Endpoint 使用 OpenAI 兼容 API 格式，例如 https://api.openai.com/v1
/// 或 http://localhost:11434/v1。
/// </summary>
public class AiSettings
{
    public bool Enabled { get; set; }

    public string Endpoint { get; set; } = "https://api.openai.com/v1";

    public string Model { get; set; } = "gpt-4o-mini";

    public string EmbeddingModel { get; set; } = "text-embedding-3-small";

    /// <summary>
    /// 推荐使用 NOTE_MANAGER_AI_API_KEY 环境变量；此字段用于兼容简单配置场景。
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    public bool AllowCloud { get; set; } = true;
}
