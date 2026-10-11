using NoteManager.Models;

namespace NoteManager.Services;

public interface IAiService
{
    bool IsConfigured { get; }

    AiSettings Settings { get; }

    Task<string> CompleteAsync(string instruction, string content, CancellationToken cancellationToken = default);

    Task<string> SummarizeAsync(string content, CancellationToken cancellationToken = default);

    Task<string> RewriteAsync(string content, string style, CancellationToken cancellationToken = default);

    Task<string> GenerateTitleAsync(string content, CancellationToken cancellationToken = default);

    Task<string> ExtractTodosAsync(string content, CancellationToken cancellationToken = default);

    Task<string> GenerateTagsAsync(string content, CancellationToken cancellationToken = default);

    Task<string> SuggestFolderAsync(string content, IEnumerable<string> folderNames, CancellationToken cancellationToken = default);

    Task<int> BuildKnowledgeIndexAsync(IEnumerable<Note> notes, IProgress<string>? progress = null, CancellationToken cancellationToken = default);

    Task<string> AskKnowledgeAsync(string question, IEnumerable<Note> notes, CancellationToken cancellationToken = default);
}
