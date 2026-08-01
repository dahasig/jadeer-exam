namespace Jadeer.Exam.Helpers;

/// <summary>
/// Port of the Thymeleaf <c>@txt</c> utility bean used in RC-Exam templates.
/// Controllers may also expose richer formatters later; these cover view needs.
/// </summary>
public static class Txt
{
    public static string V(object? value) =>
        value == null || (value is string s && string.IsNullOrWhiteSpace(s))
            ? string.Empty
            : value.ToString() ?? string.Empty;

    public static string Person(string? fullName, string? email)
    {
        var name = string.IsNullOrWhiteSpace(fullName) ? null : fullName.Trim();
        var mail = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
        if (name != null && mail != null) return $"{name} ({mail})";
        return name ?? mail ?? string.Empty;
    }

    public static string Line(params object?[] parts)
    {
        var bits = parts
            .Select(V)
            .Where(x => !string.IsNullOrWhiteSpace(x));
        return string.Join(" — ", bits);
    }

    public static string ScoreCssClass(object? score, object? threshold)
    {
        if (!TryDecimal(score, out var sc)) return "score-na";
        var pass = TryDecimal(threshold, out var th) ? th : 70m;
        return sc >= pass ? "score-pass" : "score-fail";
    }

    /// <summary>
    /// Builds the exam card subtitle (opportunity / job / company / city / year).
    /// Expects an object with those property names (dynamic or typed).
    /// </summary>
    public static string OpportunityLine(dynamic? exam)
    {
        if (exam == null) return string.Empty;
        try
        {
            string? type = exam.opportunityType?.ToString();
            string? job = exam.jobTitle?.ToString();
            string? company = exam.companyName?.ToString();
            string? city = exam.city?.ToString();
            object? year = exam.opportunityYear;
            return Line(type, job, company, city, year);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static bool TryDecimal(object? value, out decimal result)
    {
        result = 0;
        if (value == null) return false;
        return decimal.TryParse(value.ToString(), out result);
    }
}
