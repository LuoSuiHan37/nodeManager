using Markdig;

namespace NoteManager.Helpers;

/// <summary>
/// Markdown 渲染辅助。
/// </summary>
public static class MarkdownHelper
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    public static string ToHtml(string? markdown)
    {
        var body = Markdown.ToHtml(markdown ?? string.Empty, Pipeline);
        return $$"""
                 <!DOCTYPE html>
                 <html>
                 <head>
                   <meta charset="utf-8" />
                   <style>
                     body {
                       font-family: "Segoe UI", "Microsoft YaHei UI", sans-serif;
                       font-size: 15px;
                       line-height: 1.6;
                       margin: 16px;
                       color: {{"var(--fg, #1a1a1a)"}};
                       background: transparent;
                       word-wrap: break-word;
                     }
                     pre {
                       background: #f3f3f3;
                       padding: 12px;
                       border-radius: 8px;
                       overflow-x: auto;
                     }
                     code {
                       font-family: Consolas, "Cascadia Mono", monospace;
                     }
                     blockquote {
                       margin: 0;
                       padding: 4px 12px;
                       border-left: 4px solid #0078d4;
                       color: #555;
                     }
                     a { color: #0078d4; }
                     @media (prefers-color-scheme: dark) {
                       body { color: #f3f3f3; }
                       pre { background: #2b2b2b; }
                       blockquote { color: #bbb; }
                     }
                   </style>
                 </head>
                 <body>{{body}}</body>
                 </html>
                 """;
    }

    public static string GetSummary(string? content, int maxLength = 120)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return "暂无内容";
        }

        // RTF 笔记先转纯文本；Markdown 则去掉常见标记
        var text = RtfHelper.IsRtf(content)
            ? RtfHelper.ToPlainText(content)
            : content
                .Replace("\r\n", "\n")
                .Replace('#', ' ')
                .Replace('*', ' ')
                .Replace('`', ' ')
                .Trim();

        while (text.Contains("\n\n", StringComparison.Ordinal))
        {
            text = text.Replace("\n\n", "\n", StringComparison.Ordinal);
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return "暂无内容";
        }

        if (text.Length <= maxLength)
        {
            return text;
        }

        return text[..maxLength].TrimEnd() + "…";
    }
}
