using System.Net;
using System.Text;

namespace FocusAI.Infrastructure.Ingestion;

/// <summary>
/// Strips markup from feed payloads without pulling a full parser into the hot
/// path. AngleSharp is reserved for <see cref="HttpContentExtractor"/>, where
/// real DOM analysis is actually needed.
/// </summary>
internal static class HtmlText
{
    public static string? ToPlainText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return null;
        }

        var builder = new StringBuilder(html.Length);
        var insideTag = false;
        var lastWasSpace = true;

        foreach (var ch in html)
        {
            switch (ch)
            {
                case '<':
                    insideTag = true;
                    // A dropped tag is a word boundary: "<p>a</p><p>b</p>" is "a b".
                    if (!lastWasSpace)
                    {
                        builder.Append(' ');
                        lastWasSpace = true;
                    }

                    break;

                case '>':
                    insideTag = false;
                    break;

                default:
                    if (insideTag)
                    {
                        break;
                    }

                    if (char.IsWhiteSpace(ch))
                    {
                        if (!lastWasSpace)
                        {
                            builder.Append(' ');
                            lastWasSpace = true;
                        }
                    }
                    else
                    {
                        builder.Append(ch);
                        lastWasSpace = false;
                    }

                    break;
            }
        }

        var text = WebUtility.HtmlDecode(builder.ToString()).Trim();
        return text.Length == 0 ? null : text;
    }
}
