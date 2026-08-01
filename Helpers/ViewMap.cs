using System.Collections;
using System.Globalization;
using System.Reflection;

namespace Jadeer.Exam.Helpers;

/// <summary>
/// Safe view data access for Dictionary / anonymous / entity rows (case-insensitive keys).
/// Use this instead of dynamic property access like <c>row.Username</c> on dictionaries.
/// </summary>
public static class ViewMap
{
    public static object? Get(object? row, string key)
    {
        if (row == null || string.IsNullOrWhiteSpace(key))
            return null;

        if (row is IDictionary<string, object?> genNull)
            return FindInStringKeyedMap(genNull, key);

        if (row is IDictionary<string, object> gen)
            return FindInStringKeyedMap(gen.Select(p => new KeyValuePair<string, object?>(p.Key, p.Value)), key);

        if (row is IDictionary dict)
        {
            foreach (DictionaryEntry entry in dict)
            {
                if (entry.Key?.ToString() is string k
                    && string.Equals(k, key, StringComparison.OrdinalIgnoreCase))
                    return entry.Value;
            }
        }

        var type = row.GetType();
        var prop = type.GetProperty(key, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        if (prop != null)
            return prop.GetValue(row);

        // Nested path: exam.title
        if (key.Contains('.', StringComparison.Ordinal))
        {
            object? current = row;
            foreach (var part in key.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                current = Get(current, part);
                if (current == null) return null;
            }
            return current;
        }

        return null;
    }

    public static string Str(object? row, string key) => Txt.V(Get(row, key));

    public static T? As<T>(object? row, string key)
    {
        var value = Get(row, key);
        if (value == null) return default;
        if (value is T direct) return direct;
        try
        {
            return (T)Convert.ChangeType(value, typeof(T), CultureInfo.InvariantCulture);
        }
        catch
        {
            return default;
        }
    }

    public static bool Bool(object? row, string key)
    {
        var value = Get(row, key);
        return value switch
        {
            null => false,
            bool b => b,
            string s => bool.TryParse(s, out var p) && p,
            _ => false
        };
    }

    private static object? FindInStringKeyedMap(IEnumerable<KeyValuePair<string, object?>> map, string key)
    {
        foreach (var pair in map)
        {
            if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase))
                return pair.Value;
        }
        return null;
    }
}
