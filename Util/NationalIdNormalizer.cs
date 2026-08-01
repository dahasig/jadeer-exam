namespace Jadeer.Exam.Util;

public static class NationalIdNormalizer
{
    public static string Normalize(string? raw)
    {
        if (raw == null)
        {
            return "";
        }

        var sb = new System.Text.StringBuilder(raw.Length);
        foreach (var c in raw.Trim())
        {
            if (c is >= '\u0660' and <= '\u0669')
            {
                sb.Append((char)('0' + (c - '\u0660')));
            }
            else if (c is >= '\u06F0' and <= '\u06F9')
            {
                sb.Append((char)('0' + (c - '\u06F0')));
            }
            else if (!char.IsWhiteSpace(c) && c != '-' && c != '_')
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }
}
