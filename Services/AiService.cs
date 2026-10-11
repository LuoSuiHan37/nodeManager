using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NoteManager.Data;
using NoteManager.Helpers;
using NoteManager.Models;
using Serilog;

namespace NoteManager.Services;

/// <summary>
/// OpenAI 兼容 API 客户端，同时负责轻量本地向量索引。
/// </summary>
public sealed class AiService : IAiService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(3) };
    private readonly ISettingsService _settingsService;
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public AiService(ISettingsService settingsService, IDbContextFactory<AppDbContext> dbFactory)
    {
        _settingsService = settingsService;
        _dbFactory = dbFactory;
    }

    public AiSettings Settings => _settingsService.Current.Ai;

    public bool IsConfigured => Settings.Enabled && !string.IsNullOrWhiteSpace(Settings.Endpoint)
        && !string.IsNullOrWhiteSpace(Settings.Model);

    public async Task<string> CompleteAsync(string instruction, string content, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var endpoint = NormalizeEndpoint(Settings.Endpoint) + "/chat/completions";
        var body = new
        {
            model = Settings.Model,
            temperature = 0.2,
            messages = new[]
            {
                new { role = "system", content = "你是 NoteManager 的笔记助手。回答准确、简洁，保留用户要求的 Markdown 格式。" },
                new { role = "user", content = instruction + "\n\n--- 笔记内容 ---\n" + content }
            }
        };

        using var request = CreateRequest(HttpMethod.Post, endpoint, body);
        using var response = await Http.SendAsync(request, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureSuccess(response, json);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString()?.Trim() ?? string.Empty;
    }

    public Task<string> SummarizeAsync(string content, CancellationToken cancellationToken = default) =>
        CompleteAsync("请用 3-5 条要点总结这篇笔记，不要添加原文没有的事实。", content, cancellationToken);

    public Task<string> RewriteAsync(string content, string style, CancellationToken cancellationToken = default) =>
        CompleteAsync($"请将这篇笔记改写为{style}。保留事实、代码和链接；只输出改写后的正文。", content, cancellationToken);

    public Task<string> GenerateTitleAsync(string content, CancellationToken cancellationToken = default) =>
        CompleteAsync("请为这篇笔记生成一个简洁标题，只输出标题，不要加引号。", content, cancellationToken);

    public Task<string> ExtractTodosAsync(string content, CancellationToken cancellationToken = default) =>
        CompleteAsync("请提取其中的待办事项，输出 Markdown 任务列表；没有待办时输出“没有发现待办事项”。", content, cancellationToken);

    public Task<string> GenerateTagsAsync(string content, CancellationToken cancellationToken = default) =>
        CompleteAsync("请生成 3-8 个适合归档的标签，只输出逗号分隔的标签，不要井号。", content, cancellationToken);

    public Task<string> SuggestFolderAsync(string content, IEnumerable<string> folderNames, CancellationToken cancellationToken = default)
    {
        var names = string.Join("、", folderNames);
        return CompleteAsync($"请从以下已有文件夹中选择最合适的一个，只输出文件夹名称：{names}", content, cancellationToken);
    }

    public async Task<int> BuildKnowledgeIndexAsync(IEnumerable<Note> notes, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        await EnsureEmbeddingTableAsync(cancellationToken);
        var count = 0;
        foreach (var note in notes.Where(n => !n.IsDeleted))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var text = note.Title + "\n" + RtfHelper.ToPlainText(note.Content);
            var vector = await CreateEmbeddingAsync(text, cancellationToken);
            await UpsertEmbeddingAsync(note.Id, Hash(text), vector, cancellationToken);
            count++;
            progress?.Report($"正在建立索引：{count}");
        }
        return count;
    }

    public async Task<string> AskKnowledgeAsync(string question, IEnumerable<Note> notes, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        await EnsureEmbeddingTableAsync(cancellationToken);
        var noteList = notes.Where(n => !n.IsDeleted).ToList();
        var query = await CreateEmbeddingAsync(question, cancellationToken);
        var stored = await ReadEmbeddingsAsync(cancellationToken);
        var selected = stored
            .Select(x => (x.NoteId, Score: Cosine(query, x.Vector)))
            .OrderByDescending(x => x.Score)
            .Take(6)
            .Join(noteList, x => x.NoteId, n => n.Id, (x, n) => n)
            .ToList();

        if (selected.Count == 0)
        {
            selected = noteList.Take(6).ToList();
        }

        var context = string.Join("\n\n", selected.Select(n => $"## {n.Title}\n{RtfHelper.ToPlainText(n.Content)}"));
        return await CompleteAsync("请根据以下笔记回答问题。只使用笔记中有依据的信息；如果找不到答案，请明确说没有找到。\n问题：" + question, context, cancellationToken);
    }

    private async Task<float[]> CreateEmbeddingAsync(string text, CancellationToken cancellationToken)
    {
        var endpoint = NormalizeEndpoint(Settings.Endpoint) + "/embeddings";
        var body = new { model = Settings.EmbeddingModel, input = text };
        using var request = CreateRequest(HttpMethod.Post, endpoint, body);
        using var response = await Http.SendAsync(request, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        EnsureSuccess(response, json);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("data")[0].GetProperty("embedding")
            .EnumerateArray().Select(x => x.GetSingle()).ToArray();
    }

    private async Task EnsureEmbeddingTableAsync(CancellationToken cancellationToken)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS NoteEmbeddings (
                NoteId TEXT NOT NULL PRIMARY KEY,
                TextHash TEXT NOT NULL,
                Vector TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            );
            """, cancellationToken);
    }

    private async Task UpsertEmbeddingAsync(Guid noteId, string hash, float[] vector, CancellationToken cancellationToken)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO NoteEmbeddings(NoteId, TextHash, Vector, UpdatedAt)
            VALUES ({0}, {1}, {2}, {3})
            ON CONFLICT(NoteId) DO UPDATE SET TextHash=excluded.TextHash, Vector=excluded.Vector, UpdatedAt=excluded.UpdatedAt;
            """, noteId.ToString(), hash, JsonSerializer.Serialize(vector), DateTime.UtcNow.ToString("O"), cancellationToken);
    }

    private async Task<List<(Guid NoteId, float[] Vector)>> ReadEmbeddingsAsync(CancellationToken cancellationToken)
    {
        var result = new List<(Guid, float[])>();
        await using var db = await _dbFactory.CreateDbContextAsync(cancellationToken);
        await using var conn = db.Database.GetDbConnection();
        await conn.OpenAsync(cancellationToken);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT NoteId, Vector FROM NoteEmbeddings";
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            if (Guid.TryParse(reader.GetString(0), out var id))
            {
                result.Add((id, JsonSerializer.Deserialize<float[]>(reader.GetString(1)) ?? []));
            }
        }
        return result;
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string endpoint, object body)
    {
        var request = new HttpRequestMessage(method, endpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
        };
        var key = Environment.GetEnvironmentVariable("NOTE_MANAGER_AI_API_KEY") ?? Settings.ApiKey;
        if (!string.IsNullOrWhiteSpace(key))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        }
        return request;
    }

    private void EnsureConfigured()
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException("请先在 AI 设置中启用并配置模型接口。");
        }
        if (!Settings.AllowCloud && !Settings.Endpoint.Contains("localhost", StringComparison.OrdinalIgnoreCase)
            && !Settings.Endpoint.Contains("127.0.0.1", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("当前已禁止云端 AI，请改用本地模型接口。");
        }
    }

    private static string NormalizeEndpoint(string endpoint) => endpoint.TrimEnd('/');

    private static void EnsureSuccess(HttpResponseMessage response, string json)
    {
        if (!response.IsSuccessStatusCode)
        {
            Log.Warning("AI 请求失败 {Status}: {Body}", response.StatusCode, json);
            throw new InvalidOperationException($"AI 请求失败（{(int)response.StatusCode}）：{json[..Math.Min(json.Length, 300)]}");
        }
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static float Cosine(float[] a, float[] b)
    {
        if (a.Length != b.Length || a.Length == 0) return 0;
        double dot = 0, aa = 0, bb = 0;
        for (var i = 0; i < a.Length; i++) { dot += a[i] * b[i]; aa += a[i] * a[i]; bb += b[i] * b[i]; }
        return aa == 0 || bb == 0 ? 0 : (float)(dot / (Math.Sqrt(aa) * Math.Sqrt(bb)));
    }
}
