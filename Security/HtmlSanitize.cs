using Ganss.Xss;

namespace Jadeer.Exam.Security;

/// <summary>
/// HTML sanitization via Ganss.Xss.HtmlSanitizer (parser-based; replaces vulnerable regex filters).
/// </summary>
public static class HtmlSanitize
{
    private static readonly string[] AllowedTags =
    [
        "p", "br", "b", "strong", "i", "em", "u", "ul", "ol", "li", "span", "div"
    ];

    private static readonly Lazy<HtmlSanitizer> Sanitizer = new(CreateSanitizer);

    public static string Sanitize(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return "";
        }

        // Defense-in-depth length cap before sanitization.
        var input = html.Length > 100_000 ? html[..100_000] : html;
        return Sanitizer.Value.Sanitize(input).Trim();
    }

    public static string ToPlainText(string? html)
    {
        var sanitized = Sanitize(html);
        if (string.IsNullOrEmpty(sanitized))
        {
            return "";
        }

        // Strip remaining tags with a simple non-backtracking pass after library sanitize.
        var plain = System.Text.RegularExpressions.Regex.Replace(
            sanitized, "<[^>]*>", " ", System.Text.RegularExpressions.RegexOptions.None,
            TimeSpan.FromMilliseconds(250));
        return System.Text.RegularExpressions.Regex.Replace(
            plain, @"\s+", " ", System.Text.RegularExpressions.RegexOptions.None,
            TimeSpan.FromMilliseconds(250)).Trim();
    }

    private static HtmlSanitizer CreateSanitizer()
    {
        var sanitizer = new HtmlSanitizer();
        sanitizer.AllowedTags.Clear();
        foreach (var tag in AllowedTags)
        {
            sanitizer.AllowedTags.Add(tag);
        }

        sanitizer.AllowedAttributes.Clear();
        sanitizer.AllowedCssProperties.Clear();
        sanitizer.AllowedSchemes.Clear();
        sanitizer.AllowDataAttributes = false;
        return sanitizer;
    }
}
