using System.Globalization;
using System.Security.Claims;
using Jadeer.Exam.Models;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Jadeer.Exam.Controllers;

public abstract class AppControllerBase : Controller
{
    public const string LangSessionKey = "jadeer_ui_lang";
    public const string LangCookieName = "jadeer_lang";

    public override void OnActionExecuting(ActionExecutingContext context)
    {
        ApplyUiViewData();
        base.OnActionExecuting(context);
    }

    protected void ApplyUiViewData()
    {
        var lang = ResolveAndPersistLanguage();
        var culture = CultureInfo.GetCultureInfo(lang);
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;

        ViewData["PageLang"] = lang;
        ViewData["PageDir"] = lang == "ar" ? "rtl" : "ltr";
        ViewData["AssetsVersion"] = "8";
        ViewData["LangArUrl"] = BuildLangUrl("ar");
        ViewData["LangEnUrl"] = BuildLangUrl("en");

        var role = User.FindFirstValue(ClaimTypes.Role);
        ViewData["ShowAdminNav"] = role == nameof(Role.ADMIN);
        ViewData["ShowStaffNav"] = role is nameof(Role.ADMIN) or nameof(Role.EXAMINER);
        ViewData["ShowGradingNav"] = role is nameof(Role.ADMIN) or nameof(Role.EXAMINER) or nameof(Role.GRADER);
    }

    private string ResolveAndPersistLanguage()
    {
        // 1) Explicit switch via ?lang= (same as Java LocaleChangeInterceptor)
        var fromQuery = Request.Query["lang"].FirstOrDefault();
        if (fromQuery is "ar" or "en")
        {
            PersistLanguage(fromQuery);
            return fromQuery;
        }

        // 2) Session (Java SessionLocaleResolver equivalent)
        var fromSession = HttpContext.Session.GetString(LangSessionKey);
        if (fromSession is "ar" or "en")
            return fromSession;

        // 3) Cookie (survives new session)
        var fromCookie = ParseLangCookie(Request.Cookies[LangCookieName]);
        if (fromCookie is "ar" or "en")
        {
            HttpContext.Session.SetString(LangSessionKey, fromCookie);
            return fromCookie;
        }

        // 4) Default Arabic — never fall back to browser Accept-Language
        PersistLanguage("ar");
        return "ar";
    }

    private void PersistLanguage(string lang)
    {
        HttpContext.Session.SetString(LangSessionKey, lang);
        var cookieValue = CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(lang));
        Response.Cookies.Append(LangCookieName, cookieValue, new CookieOptions
        {
            HttpOnly = false,
            IsEssential = true,
            SameSite = SameSiteMode.Lax,
            Secure = Request.IsHttps,
            Expires = DateTimeOffset.UtcNow.AddYears(1),
            Path = "/"
        });
    }

    private static string? ParseLangCookie(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        if (raw is "ar" or "en")
            return raw;

        // ASP.NET format: c=ar|uic=ar
        try
        {
            var parsed = CookieRequestCultureProvider.ParseCookieValue(raw);
            var name = parsed?.UICultures?.FirstOrDefault().Value
                       ?? parsed?.Cultures?.FirstOrDefault().Value;
            if (string.IsNullOrWhiteSpace(name))
                return null;
            var two = name.Split('-', '_')[0].ToLowerInvariant();
            return two is "ar" or "en" ? two : null;
        }
        catch
        {
            return null;
        }
    }

    private string BuildLangUrl(string lang)
    {
        var path = Request.Path.Value ?? "/";
        var query = Request.Query
            .Where(kv => !string.Equals(kv.Key, "lang", StringComparison.OrdinalIgnoreCase))
            .SelectMany(kv => kv.Value.Select(v => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(v ?? "")}"))
            .ToList();
        query.Add($"lang={lang}");
        return path + "?" + string.Join("&", query);
    }

    protected string CurrentUsername => User.Identity?.Name ?? "";

    protected bool IsAdmin => User.IsInRole(nameof(Role.ADMIN));
    protected bool IsExaminer => User.IsInRole(nameof(Role.EXAMINER));
    protected bool IsGrader => User.IsInRole(nameof(Role.GRADER));
    protected bool IsCandidate => User.IsInRole(nameof(Role.CANDIDATE));
    protected bool IsStaff => IsAdmin || IsExaminer;
    protected bool IsGradingUser => IsAdmin || IsExaminer || IsGrader;

    protected string? ClientIp => HttpContext.Connection.RemoteIpAddress?.ToString();
}
