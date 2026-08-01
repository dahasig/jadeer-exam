using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Localization;

namespace Jadeer.Exam.Localization;

/// <summary>
/// Loads Spring-style <c>Resources/messages_{culture}.properties</c> files
/// (Java .properties: key=value, # comments, \uXXXX escapes, {0} placeholders).
/// </summary>
public sealed class JsonStringLocalizer : IStringLocalizer
{
    private static readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, string>> Cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _resourcesPath;
    private readonly string _baseName;

    public JsonStringLocalizer(string resourcesPath, string baseName = "messages")
    {
        _resourcesPath = resourcesPath;
        _baseName = baseName;
    }

    public LocalizedString this[string name]
    {
        get
        {
            var value = GetString(name);
            return new LocalizedString(name, value ?? name, resourceNotFound: value == null);
        }
    }

    public LocalizedString this[string name, params object[] arguments]
    {
        get
        {
            var format = GetString(name) ?? name;
            string value;
            try
            {
                value = string.Format(CultureInfo.CurrentUICulture, format, arguments);
            }
            catch (FormatException)
            {
                value = format;
            }

            return new LocalizedString(name, value, resourceNotFound: GetString(name) == null);
        }
    }

    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures)
    {
        var map = LoadForCulture(CultureInfo.CurrentUICulture);
        foreach (var pair in map)
            yield return new LocalizedString(pair.Key, pair.Value, false);
    }

    private string? GetString(string name)
    {
        var culture = CultureInfo.CurrentUICulture;
        var map = LoadForCulture(culture);
        if (map.TryGetValue(name, out var value))
            return value;

        if (!culture.IsNeutralCulture)
        {
            map = LoadForCulture(culture.Parent);
            if (map.TryGetValue(name, out value))
                return value;
        }

        map = LoadForCulture(CultureInfo.GetCultureInfo("en"));
        return map.TryGetValue(name, out value) ? value : null;
    }

    private IReadOnlyDictionary<string, string> LoadForCulture(CultureInfo culture)
    {
        var key = culture.Name;
        return Cache.GetOrAdd(key, _ =>
        {
            var candidates = new[]
            {
                Path.Combine(_resourcesPath, $"{_baseName}_{culture.Name}.properties"),
                Path.Combine(_resourcesPath, $"{_baseName}_{culture.TwoLetterISOLanguageName}.properties"),
                Path.Combine(_resourcesPath, $"{_baseName}.properties")
            };

            foreach (var path in candidates)
            {
                if (File.Exists(path))
                    return ParsePropertiesFile(path);
            }

            return new Dictionary<string, string>(StringComparer.Ordinal);
        });
    }

    private static IReadOnlyDictionary<string, string> ParsePropertiesFile(string path)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var rawLine in File.ReadLines(path, Encoding.UTF8))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith('!'))
                continue;

            var sep = line.IndexOfAny(['=', ':']);
            if (sep <= 0)
                continue;

            var key = Unescape(line[..sep].Trim());
            var value = Unescape(line[(sep + 1)..].Trim());
            result[key] = value;
        }

        return result;
    }

    private static readonly Regex UnicodeEscape = new(@"\\u([0-9a-fA-F]{4})", RegexOptions.Compiled);

    private static string Unescape(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;

        var withUnicode = UnicodeEscape.Replace(input, m =>
            ((char)Convert.ToInt32(m.Groups[1].Value, 16)).ToString());

        return withUnicode
            .Replace("\\n", "\n", StringComparison.Ordinal)
            .Replace("\\t", "\t", StringComparison.Ordinal)
            .Replace("\\r", "\r", StringComparison.Ordinal)
            .Replace("\\\\", "\\", StringComparison.Ordinal);
    }
}

/// <summary>
/// Factory that always serves the shared messages_*.properties catalog
/// (ignores per-type resource names used by default resx localizers).
/// </summary>
public sealed class JsonStringLocalizerFactory : IStringLocalizerFactory
{
    private readonly string _resourcesPath;

    public JsonStringLocalizerFactory(IWebHostEnvironment env)
    {
        _resourcesPath = Path.Combine(env.ContentRootPath, "Resources");
    }

    public IStringLocalizer Create(Type resourceSource) =>
        new JsonStringLocalizer(_resourcesPath);

    public IStringLocalizer Create(string baseName, string location) =>
        new JsonStringLocalizer(_resourcesPath);
}

/*
 * Program.cs registration (add when wiring the app):
 *
 * builder.Services.AddLocalization();
 * builder.Services.AddSingleton<IStringLocalizerFactory, JsonStringLocalizerFactory>();
 * builder.Services.AddControllersWithViews()
 *     .AddViewLocalization();
 *
 * var supported = new[] { "ar", "en" };
 * app.UseRequestLocalization(new RequestLocalizationOptions()
 *     .SetDefaultCulture("ar")
 *     .AddSupportedCultures(supported)
 *     .AddSupportedUICultures(supported));
 *
 * Controllers should set:
 *   ViewData["PageLang"], ViewData["PageDir"], ViewData["AssetsVersion"],
 *   ViewData["Title"], ViewData["ActiveNav"], ViewData["LangArUrl"], ViewData["LangEnUrl"],
 *   ViewData["ShowAdminNav"], ViewData["ShowStaffNav"], ViewData["ShowGradingNav"]
 */
