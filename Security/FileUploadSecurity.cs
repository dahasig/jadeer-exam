namespace Jadeer.Exam.Security;

/// <summary>
/// MIME + extension whitelist, magic-byte checks, and lightweight malware pattern scan for uploads.
/// </summary>
public static class FileUploadSecurity
{
    public enum Category
    {
        Image,
        Excel,
        Video
    }

    private const long MaxImage = 5L * 1024 * 1024;
    private const long MaxExcel = 5L * 1024 * 1024;
    private const long MaxVideo = 8L * 1024 * 1024;

    private static readonly Dictionary<Category, HashSet<string>> Ext = new()
    {
        [Category.Image] = new(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".webp", ".gif" },
        [Category.Excel] = new(StringComparer.OrdinalIgnoreCase) { ".xlsx", ".xls" },
        [Category.Video] = new(StringComparer.OrdinalIgnoreCase) { ".webm", ".mp4" }
    };

    private static readonly Dictionary<Category, HashSet<string>> Mime = new()
    {
        [Category.Image] = new(StringComparer.OrdinalIgnoreCase)
        {
            "image/jpeg", "image/png", "image/webp", "image/gif"
        },
        [Category.Excel] = new(StringComparer.OrdinalIgnoreCase)
        {
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "application/vnd.ms-excel",
            "application/octet-stream"
        },
        [Category.Video] = new(StringComparer.OrdinalIgnoreCase)
        {
            "video/webm", "video/mp4", "application/octet-stream"
        }
    };

    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF];
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] Gif87 = "GIF87a"u8.ToArray();
    private static readonly byte[] Gif89 = "GIF89a"u8.ToArray();
    private static readonly byte[] WebpRiff = "RIFF"u8.ToArray();
    private static readonly byte[] WebpWebp = "WEBP"u8.ToArray();
    private static readonly byte[] Zip = [0x50, 0x4B, 0x03, 0x04];
    private static readonly byte[] Ole = [0xD0, 0xCF, 0x11, 0xE0];
    private static readonly byte[] Webm = [0x1A, 0x45, 0xDF, 0xA3];

    public static async Task<byte[]> ValidateAsync(IFormFile file, Category category, CancellationToken ct = default)
    {
        if (file == null || file.Length == 0)
        {
            throw new ArgumentException("file_empty");
        }

        long max = category switch
        {
            Category.Image => MaxImage,
            Category.Excel => MaxExcel,
            Category.Video => MaxVideo,
            _ => MaxImage
        };
        if (file.Length > max)
        {
            throw new ArgumentException("file_size");
        }

        var original = file.FileName?.Trim() ?? "";
        var ext = ExtensionOf(original);
        if (!Ext[category].Contains(ext))
        {
            throw new ArgumentException("file_extension");
        }

        var contentType = file.ContentType?.ToLowerInvariant() ?? "";
        if (!Mime[category].Contains(contentType))
        {
            throw new ArgumentException("file_mime");
        }

        await using var stream = file.OpenReadStream();
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, ct);
        var data = ms.ToArray();
        if (data.Length == 0)
        {
            throw new ArgumentException("file_empty");
        }

        if (!MatchesMagic(data, category, ext))
        {
            throw new ArgumentException("file_magic");
        }

        ScanForMalwareSignatures(data, category);
        return data;
    }

    public static byte[] ValidateBase64Media(byte[] data, Category category)
    {
        if (data == null || data.Length == 0)
        {
            throw new ArgumentException("file_empty");
        }

        long max = category == Category.Video ? MaxVideo : MaxImage;
        if (data.Length > max)
        {
            throw new ArgumentException("file_size");
        }

        if (category == Category.Image)
        {
            if (!(StartsWith(data, Jpeg) || StartsWith(data, Png) || StartsWith(data, Gif87)
                  || StartsWith(data, Gif89) || (StartsWith(data, WebpRiff) && ContainsAt(data, WebpWebp, 8))))
            {
                throw new ArgumentException("file_magic");
            }
        }
        else if (category == Category.Video)
        {
            if (DetectVideoExtension(data) == null)
            {
                throw new ArgumentException("file_magic");
            }
        }
        else if (!MatchesMagic(data, category, ".xlsx"))
        {
            throw new ArgumentException("file_magic");
        }

        ScanForMalwareSignatures(data, category);
        return data;
    }

    private static void ScanForMalwareSignatures(byte[] data, Category category)
    {
        var probeLen = Math.Min(data.Length, 8192);
        var probe = System.Text.Encoding.UTF8.GetString(data, 0, probeLen).ToLowerInvariant();
        if (probe.Contains("<script", StringComparison.Ordinal)
            || probe.Contains("<?php", StringComparison.Ordinal)
            || probe.Contains("powershell", StringComparison.Ordinal)
            || probe.Contains("cmd.exe", StringComparison.Ordinal)
            || probe.Contains("/bin/sh", StringComparison.Ordinal)
            || (category == Category.Excel && probe.Contains("<%", StringComparison.Ordinal)))
        {
            throw new ArgumentException("file_malware");
        }

        if (category == Category.Image && Contains(data, "MZ"u8.ToArray()) && data.Length > 2 && data[0] != 0xFF)
        {
            throw new ArgumentException("file_malware");
        }
    }

    private static bool MatchesMagic(byte[] data, Category category, string ext) =>
        category switch
        {
            Category.Image => ext.ToLowerInvariant() switch
            {
                ".jpg" or ".jpeg" => StartsWith(data, Jpeg),
                ".png" => StartsWith(data, Png),
                ".gif" => StartsWith(data, Gif87) || StartsWith(data, Gif89),
                ".webp" => StartsWith(data, WebpRiff) && ContainsAt(data, WebpWebp, 8),
                _ => false
            },
            Category.Excel => StartsWith(data, Zip) || StartsWith(data, Ole),
            Category.Video => DetectVideoExtension(data) == ext,
            _ => false
        };

    public static string? DetectVideoExtension(byte[] data)
    {
        if (StartsWith(data, Webm))
            return ".webm";

        if (data.Length >= 16 && data.AsSpan(4, 4).SequenceEqual("ftyp"u8))
        {
            var boxSize = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(data);
            if (boxSize >= 16 && boxSize <= data.Length)
                return ".mp4";
        }

        return null;
    }

    private static string ExtensionOf(string name)
    {
        var dot = name.LastIndexOf('.');
        return dot < 0 ? "" : name[dot..].ToLowerInvariant();
    }

    private static bool StartsWith(byte[] data, byte[] prefix)
    {
        if (data.Length < prefix.Length)
        {
            return false;
        }

        for (var i = 0; i < prefix.Length; i++)
        {
            if (data[i] != prefix[i])
            {
                return false;
            }
        }

        return true;
    }

    private static bool ContainsAt(byte[] data, byte[] needle, int offset)
    {
        if (data.Length < offset + needle.Length)
        {
            return false;
        }

        for (var i = 0; i < needle.Length; i++)
        {
            if (data[offset + i] != needle[i])
            {
                return false;
            }
        }

        return true;
    }

    private static bool Contains(byte[] data, byte[] needle)
    {
        for (var i = 0; i <= data.Length - needle.Length; i++)
        {
            var match = true;
            for (var j = 0; j < needle.Length; j++)
            {
                if (data[i + j] != needle[j])
                {
                    match = false;
                    break;
                }
            }

            if (match)
            {
                return true;
            }
        }

        return false;
    }
}
