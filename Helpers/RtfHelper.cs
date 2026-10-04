using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace NoteManager.Helpers;

/// <summary>
/// RTF 检测与纯文本提取（列表摘要 / 搜索用）。
/// </summary>
public static partial class RtfHelper
{
    private static readonly HashSet<string> DestinationWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "fonttbl", "colortbl", "stylesheet", "info", "pict", "object", "xmlnstbl",
        "header", "footer", "headerf", "footerf", "footnote", "annotation",
        "listtable", "listoverridetable", "rsidtbl", "generator",
        "latentstyles", "themedata", "colorschememapping", "datastore", "datafield"
    };

    static RtfHelper()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static bool IsRtf(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        var trimmed = content.AsSpan().TrimStart();
        return trimmed.StartsWith(@"{\rtf", StringComparison.OrdinalIgnoreCase);
    }

    public static string ToPlainText(string? content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return string.Empty;
        }

        if (!IsRtf(content))
        {
            return content;
        }

        var ansi = Encoding.GetEncoding(1252);
        var sb = new StringBuilder(Math.Min(content.Length, 4096));
        var ignoreStack = new Stack<bool>(16);
        var ignoring = false;
        var hexBytes = new List<byte>(8);
        var i = 0;

        void FlushHex()
        {
            if (hexBytes.Count == 0)
            {
                return;
            }

            if (!ignoring)
            {
                try
                {
                    sb.Append(ansi.GetString(hexBytes.ToArray()));
                }
                catch
                {
                    // ignore undecodable bytes
                }
            }

            hexBytes.Clear();
        }

        while (i < content.Length)
        {
            var c = content[i];

            if (c == '{')
            {
                FlushHex();
                ignoreStack.Push(ignoring);
                i++;

                var dest = PeekDestination(content, i);
                if (dest.IsDestination)
                {
                    ignoring = true;
                    i = dest.IndexAfterControl;
                }

                continue;
            }

            if (c == '}')
            {
                FlushHex();
                if (ignoreStack.Count > 0)
                {
                    ignoring = ignoreStack.Pop();
                }

                i++;
                continue;
            }

            if (c == '\\' && i + 1 < content.Length)
            {
                var next = content[i + 1];
                if (next is '\\' or '{' or '}')
                {
                    FlushHex();
                    if (!ignoring)
                    {
                        sb.Append(next);
                    }

                    i += 2;
                    continue;
                }

                if (next == '\'')
                {
                    if (i + 3 < content.Length
                        && byte.TryParse(content.AsSpan(i + 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
                    {
                        hexBytes.Add(b);
                        i += 4;
                        continue;
                    }
                }

                FlushHex();

                if (next is 'u' or 'U')
                {
                    var j = i + 2;
                    var sign = 1;
                    if (j < content.Length && content[j] == '-')
                    {
                        sign = -1;
                        j++;
                    }

                    var start = j;
                    while (j < content.Length && char.IsDigit(content[j]))
                    {
                        j++;
                    }

                    if (j > start
                        && int.TryParse(content.AsSpan(start, j - start), NumberStyles.Integer, CultureInfo.InvariantCulture, out var code))
                    {
                        var value = sign * code;
                        if (value < 0)
                        {
                            value += 65536;
                        }

                        if (!ignoring && value is >= char.MinValue and <= char.MaxValue)
                        {
                            sb.Append((char)value);
                        }

                        i = j;
                        if (i < content.Length && content[i] == '?')
                        {
                            i++;
                        }

                        if (i + 3 < content.Length && content[i] == '\\' && content[i + 1] == '\'')
                        {
                            i += 4;
                        }

                        continue;
                    }
                }

                var wordStart = i + 1;
                var wordEnd = wordStart;
                while (wordEnd < content.Length && char.IsLetter(content[wordEnd]))
                {
                    wordEnd++;
                }

                var word = content[wordStart..wordEnd];
                var numStart = wordEnd;
                var numEnd = numStart;
                if (numEnd < content.Length && content[numEnd] == '-')
                {
                    numEnd++;
                }

                while (numEnd < content.Length && char.IsDigit(content[numEnd]))
                {
                    numEnd++;
                }

                if (word.Equals("ansicpg", StringComparison.OrdinalIgnoreCase)
                    && numEnd > numStart
                    && int.TryParse(content.AsSpan(numStart, numEnd - numStart), out var cpg))
                {
                    try
                    {
                        ansi = Encoding.GetEncoding(cpg);
                    }
                    catch
                    {
                        ansi = Encoding.GetEncoding(1252);
                    }
                }

                if (!ignoring)
                {
                    if (word is "par" or "line" or "page")
                    {
                        sb.Append('\n');
                    }
                    else if (word == "tab")
                    {
                        sb.Append('\t');
                    }
                    else if (word == "emdash")
                    {
                        sb.Append('—');
                    }
                    else if (word == "endash")
                    {
                        sb.Append('–');
                    }
                    else if (word == "bullet")
                    {
                        sb.Append("• ");
                    }
                    else if (word is "lquote" or "rquote")
                    {
                        sb.Append('\'');
                    }
                    else if (word is "ldblquote" or "rdblquote")
                    {
                        sb.Append('"');
                    }
                }

                i = numEnd;
                if (i < content.Length && content[i] == ' ')
                {
                    i++;
                }

                continue;
            }

            FlushHex();
            if (!ignoring && !char.IsControl(c))
            {
                sb.Append(c);
            }

            i++;
        }

        FlushHex();

        var text = CollapseSpaces().Replace(sb.ToString(), " ");
        text = CollapseNewlines().Replace(text, "\n").Trim();
        return text;
    }

    private static (bool IsDestination, int IndexAfterControl) PeekDestination(string content, int index)
    {
        if (index >= content.Length)
        {
            return (false, index);
        }

        if (content[index] == '\\' && index + 1 < content.Length && content[index + 1] == '*')
        {
            var j = index + 2;
            while (j < content.Length && content[j] == ' ')
            {
                j++;
            }

            if (j < content.Length && content[j] == '\\')
            {
                j++;
                while (j < content.Length && char.IsLetter(content[j]))
                {
                    j++;
                }
            }

            return (true, j);
        }

        if (content[index] == '\\')
        {
            var wordStart = index + 1;
            var wordEnd = wordStart;
            while (wordEnd < content.Length && char.IsLetter(content[wordEnd]))
            {
                wordEnd++;
            }

            var word = content[wordStart..wordEnd];
            if (DestinationWords.Contains(word))
            {
                return (true, wordEnd);
            }
        }

        return (false, index);
    }

    [GeneratedRegex(@"[ \t\f\v]+")]
    private static partial Regex CollapseSpaces();

    [GeneratedRegex(@"\n{2,}")]
    private static partial Regex CollapseNewlines();
}
