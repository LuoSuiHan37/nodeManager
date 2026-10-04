using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NoteManager.Helpers;
using Serilog;

namespace NoteManager.Data;

/// <summary>
/// 维护 NotesFts（SQLite FTS5）索引。
/// </summary>
public static class FtsInitializer
{
    public static async Task EnsureFtsAsync(AppDbContext db, CancellationToken cancellationToken = default)
    {
        try
        {
            await db.Database.ExecuteSqlRawAsync(
                """
                CREATE VIRTUAL TABLE IF NOT EXISTS NotesFts USING fts5(
                    NoteId UNINDEXED,
                    Title,
                    Content,
                    tokenize='unicode61 remove_diacritics 2'
                );
                """,
                cancellationToken);

            await RebuildAsync(db, cancellationToken);
            Log.Information("FTS5 索引已就绪");
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "FTS5 初始化失败，将回退 LIKE 搜索");
        }
    }

    public static async Task RebuildAsync(AppDbContext db, CancellationToken cancellationToken = default)
    {
        await db.Database.ExecuteSqlRawAsync("DELETE FROM NotesFts;", cancellationToken);

        var notes = await db.Notes.AsNoTracking()
            .Select(n => new { n.Id, n.Title, n.Content })
            .ToListAsync(cancellationToken);

        foreach (var note in notes)
        {
            await UpsertAsync(db, note.Id, note.Title, note.Content, cancellationToken);
        }
    }

    public static async Task UpsertAsync(
        AppDbContext db,
        Guid noteId,
        string title,
        string content,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var id = noteId.ToString();
            await db.Database.ExecuteSqlRawAsync(
                "DELETE FROM NotesFts WHERE NoteId = {0};",
                id);

            var plain = RtfHelper.IsRtf(content)
                ? RtfHelper.ToPlainText(content)
                : content ?? string.Empty;

            await db.Database.ExecuteSqlRawAsync(
                "INSERT INTO NotesFts(NoteId, Title, Content) VALUES ({0}, {1}, {2});",
                id,
                title ?? string.Empty,
                plain);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "更新 FTS 索引失败 NoteId={NoteId}", noteId);
        }
    }

    public static async Task DeleteAsync(AppDbContext db, Guid noteId, CancellationToken cancellationToken = default)
    {
        try
        {
            await db.Database.ExecuteSqlRawAsync(
                "DELETE FROM NotesFts WHERE NoteId = {0};",
                noteId.ToString());
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "删除 FTS 索引失败 NoteId={NoteId}", noteId);
        }
    }

    public static async Task<List<Guid>?> SearchIdsAsync(
        string connectionString,
        string keyword,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var match = ToMatchQuery(keyword);
            if (string.IsNullOrWhiteSpace(match))
            {
                return [];
            }

            await using var conn = new SqliteConnection(connectionString);
            await conn.OpenAsync(cancellationToken);
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT NoteId
                FROM NotesFts
                WHERE NotesFts MATCH $q
                ORDER BY bm25(NotesFts)
                LIMIT 200;
                """;
            cmd.Parameters.AddWithValue("$q", match);

            var ids = new List<Guid>();
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                if (Guid.TryParse(reader.GetString(0), out var id))
                {
                    ids.Add(id);
                }
            }

            return ids;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "FTS5 查询失败，准备回退 LIKE");
            return null;
        }
    }

    private static string ToMatchQuery(string keyword)
    {
        var terms = keyword
            .Trim()
            .Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (terms.Length == 0)
        {
            return string.Empty;
        }

        // 对每个词做前缀匹配，兼容中英文
        return string.Join(" AND ", terms.Select(t =>
        {
            var safe = t.Replace("\"", string.Empty);
            return $"\"{safe}\"*";
        }));
    }
}
