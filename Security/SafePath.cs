namespace Jadeer.Exam.Security;

/// <summary>
/// Resolves filesystem paths under a trusted base directory using full-path (realpath-equivalent)
/// checks to prevent path traversal.
/// </summary>
public static class SafePath
{
    public static string ResolveUnder(string baseDirectory, params string[] relativeParts)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseDirectory);

        var baseFull = Path.GetFullPath(baseDirectory);
        if (!Directory.Exists(baseFull))
        {
            Directory.CreateDirectory(baseFull);
            baseFull = Path.GetFullPath(baseDirectory);
        }

        // Prefer realpath via GetFullPath after resolving any symlinks when available.
        baseFull = ResolveExistingPath(baseFull);

        foreach (var part in relativeParts)
        {
            if (string.IsNullOrWhiteSpace(part))
            {
                throw new UnauthorizedAccessException("Empty path segment rejected.");
            }

            if (part.Contains('\0', StringComparison.Ordinal)
                || part.Contains("..", StringComparison.Ordinal)
                || part.Contains('/', StringComparison.Ordinal)
                || part.Contains('\\', StringComparison.Ordinal)
                || Path.IsPathRooted(part))
            {
                throw new UnauthorizedAccessException("Unsafe path segment rejected.");
            }
        }

        var combined = Path.GetFullPath(Path.Combine(new[] { baseFull }.Concat(relativeParts).ToArray()));
        combined = ResolveExistingPath(combined);

        if (!IsUnderBase(combined, baseFull))
        {
            throw new UnauthorizedAccessException("Path traversal detected.");
        }

        return combined;
    }

    public static bool IsUnderBase(string candidatePath, string baseDirectory)
    {
        var candidate = Path.GetFullPath(candidatePath);
        var baseFull = Path.GetFullPath(baseDirectory);
        candidate = ResolveExistingPath(candidate);
        baseFull = ResolveExistingPath(baseFull);

        var baseWithSep = baseFull.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                          + Path.DirectorySeparatorChar;
        return candidate.Equals(baseFull, StringComparison.OrdinalIgnoreCase)
               || candidate.StartsWith(baseWithSep, StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveExistingPath(string path)
    {
        try
        {
            // GetFullPath normalizes . / .. ; for existing files/dirs, resolve to the true path.
            var info = new DirectoryInfo(path);
            if (info.Exists)
            {
                return info.FullName;
            }

            var file = new FileInfo(path);
            if (file.Exists && file.Directory != null)
            {
                return Path.Combine(file.Directory.FullName, file.Name);
            }
        }
        catch
        {
            // Fall through to GetFullPath result.
        }

        return Path.GetFullPath(path);
    }
}
