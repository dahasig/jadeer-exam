using Jadeer.Exam.Security;

namespace Jadeer.Exam.Services;

public class QuestionImageStorage
{
    private static readonly string BaseDir = Path.Combine("data", "question-images");

    public record StoredImage(byte[] Data, string ContentType, string Extension);

    public async Task<StoredImage> ReadAndValidateAsync(IFormFile file, CancellationToken ct = default)
    {
        var data = await FileUploadSecurity.ValidateAsync(file, FileUploadSecurity.Category.Image, ct);
        var contentType = (file.ContentType ?? "image/jpeg").ToLowerInvariant();
        return new StoredImage(data, contentType, ExtensionFor(contentType));
    }

    public async Task<string> StoreFileAsync(long examId, long questionId, StoredImage image, CancellationToken ct = default)
    {
        var fileName = questionId + image.Extension;
        var dir = ExamDir(examId);
        Directory.CreateDirectory(dir);
        var target = SafePath.ResolveUnder(dir, fileName);
        await File.WriteAllBytesAsync(target, image.Data, ct);
        return fileName;
    }

    public string? ResolveFile(long? examId, string? fileName)
    {
        if (examId == null || string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        var safeName = fileName.Trim();
        if (safeName.Contains("..", StringComparison.Ordinal)
            || safeName.Contains('/', StringComparison.Ordinal)
            || safeName.Contains('\\', StringComparison.Ordinal)
            || safeName.Contains('\0'))
        {
            return null;
        }

        try
        {
            var dir = Path.GetFullPath(ExamDir(examId.Value));
            var target = SafePath.ResolveUnder(dir, safeName);
            return File.Exists(target) ? target : null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    public async Task CopyFileAsync(long? srcExamId, string? srcFileName, long? destExamId, long? destQuestionId,
        string? destExtension, CancellationToken ct = default)
    {
        if (srcExamId == null || destExamId == null || destQuestionId == null
            || string.IsNullOrWhiteSpace(srcFileName))
        {
            return;
        }

        var source = ResolveFile(srcExamId, srcFileName);
        if (source == null)
        {
            return;
        }

        var ext = !string.IsNullOrWhiteSpace(destExtension)
            ? destExtension
            : (srcFileName.Contains('.') ? srcFileName[srcFileName.LastIndexOf('.')..] : ".jpg");
        var destDir = ExamDir(destExamId.Value);
        Directory.CreateDirectory(destDir);
        var dest = SafePath.ResolveUnder(destDir, destQuestionId + ext);
        await using var srcStream = File.OpenRead(source);
        await using var dstStream = File.Create(dest);
        await srcStream.CopyToAsync(dstStream, ct);
    }

    public void DeleteFile(long? examId, string? fileName)
    {
        var path = ResolveFile(examId, fileName);
        if (path == null)
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // Best-effort cleanup.
        }
    }

    public string MediaTypeFor(string? fileName, string? contentType)
    {
        if (!string.IsNullOrWhiteSpace(contentType))
        {
            return contentType;
        }

        if (fileName == null)
        {
            return "image/jpeg";
        }

        var lower = fileName.ToLowerInvariant();
        if (lower.EndsWith(".png", StringComparison.Ordinal))
        {
            return "image/png";
        }

        if (lower.EndsWith(".webp", StringComparison.Ordinal))
        {
            return "image/webp";
        }

        if (lower.EndsWith(".gif", StringComparison.Ordinal))
        {
            return "image/gif";
        }

        return "image/jpeg";
    }

    private static string ExamDir(long examId)
    {
        var baseFull = Path.GetFullPath(BaseDir);
        Directory.CreateDirectory(baseFull);
        return SafePath.ResolveUnder(baseFull, examId.ToString());
    }

    private static string ExtensionFor(string contentType) =>
        contentType.ToLowerInvariant() switch
        {
            "image/png" => ".png",
            "image/webp" => ".webp",
            "image/gif" => ".gif",
            _ => ".jpg"
        };
}
