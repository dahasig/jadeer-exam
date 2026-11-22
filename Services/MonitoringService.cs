using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Jadeer.Exam.Data;
using Jadeer.Exam.Models;
using Jadeer.Exam.Security;
using Microsoft.EntityFrameworkCore;

namespace Jadeer.Exam.Services;

public class MonitoringService
{
    private readonly AppDbContext _db;
    private readonly ExamService _examService;
    private const int IncidentBucketSeconds = 120;

    public MonitoringService(AppDbContext db, ExamService examService)
    {
        _db = db;
        _examService = examService;
    }

    public void LogEvent(string username, MonitoringEventDto dto)
    {
        _db.SuspiciousEvents.Add(new SuspiciousEvent
        {
            Username = username,
            EventType = dto.EventType,
            Severity = dto.Severity,
            Details = dto.Details,
            ExamId = dto.ExamId,
            ConfidenceScore = dto.ConfidenceScore,
            EventTime = DateTime.Now
        });
        _db.SaveChanges();
    }

    public List<SuspiciousEvent> LatestEvents() =>
        _db.SuspiciousEvents.OrderByDescending(e => e.EventTime).Take(30).ToList();

    public List<SuspiciousEvent> LatestEventsForStaff(string username)
    {
        var u = _db.Users.FirstOrDefault(x => x.Username == username);
        if (u == null)
        {
            return [];
        }

        if (u.Role == Role.ADMIN)
        {
            return LatestEvents();
        }

        return _db.SuspiciousEvents.OrderByDescending(e => e.EventTime).Take(500).AsEnumerable()
            .Where(e => VisibleToStaff(username, e))
            .Take(30)
            .ToList();
    }

    public List<SuspiciousEvent> FilteredEvents(
        string? username,
        string? severity,
        string? eventType,
        DateTime? from,
        DateTime? to)
    {
        return _db.SuspiciousEvents.OrderByDescending(e => e.EventTime).Take(500).AsEnumerable()
            .Where(e => string.IsNullOrWhiteSpace(username)
                        || (e.Username != null
                            && e.Username.Contains(username, StringComparison.OrdinalIgnoreCase)))
            .Where(e => string.IsNullOrWhiteSpace(severity)
                        || string.Equals(severity, e.Severity, StringComparison.OrdinalIgnoreCase))
            .Where(e => string.IsNullOrWhiteSpace(eventType)
                        || string.Equals(eventType, e.EventType, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(eventType, NormalizeIncidentType(e.EventType),
                            StringComparison.OrdinalIgnoreCase))
            .Where(e => from == null || (e.EventTime != default && e.EventTime >= from))
            .Where(e => to == null || (e.EventTime != default && e.EventTime <= to))
            .OrderByDescending(e => e.EventTime)
            .Take(200)
            .ToList();
    }

    public List<SuspiciousEvent> FilteredEventsForStaff(
        string staffUsername,
        string? username,
        string? severity,
        string? eventType,
        DateTime? from,
        DateTime? to)
    {
        var u = _db.Users.FirstOrDefault(x => x.Username == staffUsername);
        if (u == null)
        {
            return [];
        }

        var events = FilteredEvents(username, severity, eventType, from, to);
        if (u.Role == Role.ADMIN)
        {
            return events;
        }

        return events.Where(e => VisibleToStaff(staffUsername, e)).ToList();
    }

    private bool VisibleToStaff(string staffUsername, SuspiciousEvent e) =>
        e.ExamId != null && _examService.CanManageExam(staffUsername, e.ExamId.Value);

    public string ToCsv(List<SuspiciousEvent> events)
    {
        var sb = new StringBuilder("username,eventType,severity,eventTime,details\n");
        foreach (var e in events)
        {
            sb.Append(Csv(e.Username)).Append(',')
                .Append(Csv(NormalizeIncidentType(e.EventType))).Append(',')
                .Append(Csv(e.Severity)).Append(',')
                .Append(Csv(e.EventTime == default
                    ? ""
                    : e.EventTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))).Append(',')
                .Append(Csv(BriefDetails(e.Details)))
                .Append('\n');
        }

        return sb.ToString();
    }

    public string NotesSummaryForExamCandidate(long examId, string username)
    {
        var deduped = DeduplicateIncidents(
            _db.SuspiciousEvents.OrderByDescending(e => e.EventTime).Take(500).AsEnumerable()
                .Where(e => e.ExamId == examId)
                .Where(e => e.Username != null && username.Equals(e.Username, StringComparison.OrdinalIgnoreCase))
                .ToList());
        if (deduped.Count == 0)
        {
            return "لا توجد ملاحظات";
        }

        var top = string.Join("، ",
            deduped.Take(3)
                .Select(e => ToArabicEventType(NormalizeIncidentType(e.EventType)))
                .Distinct());
        return "عدد الملاحظات: " + deduped.Count + (string.IsNullOrWhiteSpace(top) ? "" : " | أبرزها: " + top);
    }

    public string NormalizeIncidentType(string? eventType)
    {
        if (string.IsNullOrWhiteSpace(eventType))
        {
            return "";
        }

        var t = eventType.Trim();
        if (t.EndsWith("_SNAPSHOT", StringComparison.Ordinal))
        {
            t = t[..^9];
        }
        else if (t.EndsWith("_DURING", StringComparison.Ordinal))
        {
            t = t[..^7];
        }
        else if (t.EndsWith("_PRE", StringComparison.Ordinal))
        {
            t = t[..^4];
        }
        else if (t.EndsWith("_POST", StringComparison.Ordinal))
        {
            t = t[..^5];
        }

        return t == "EXTERNAL_NOISE_DETECTED" ? "AUDIO_SPIKE" : t;
    }

    public List<SuspiciousEvent> DeduplicateIncidents(List<SuspiciousEvent> events)
    {
        if (events == null || events.Count == 0)
        {
            return [];
        }

        var best = new Dictionary<string, SuspiciousEvent>();
        foreach (var e in events)
        {
            var key = IncidentKey(e);
            if (!best.TryGetValue(key, out var existing)
                || IncidentRepresentativeRank(e) > IncidentRepresentativeRank(existing))
            {
                best[key] = e;
            }
        }

        return best.Values.OrderByDescending(e => e.EventTime).ToList();
    }

    public string BriefDetails(string? details)
    {
        if (string.IsNullOrWhiteSpace(details))
        {
            return "—";
        }

        var d = details;
        d = Regex.Replace(d, @"(?i)[A-Za-z]:\\[^|]+", "[ملف]", RegexOptions.None, TimeSpan.FromMilliseconds(250));
        d = Regex.Replace(d, @"/[^|]+\.(jpg|webm|mp4|png)", "[ملف]", RegexOptions.IgnoreCase,
            TimeSpan.FromMilliseconds(250));
        var pipe = d.IndexOf(" | ", StringComparison.Ordinal);
        if (pipe > 0 && d.Length > 100)
        {
            d = d[..pipe];
        }

        return d.Length > 140 ? d[..137] + "..." : d;
    }

    private string IncidentKey(SuspiciousEvent e)
    {
        var epoch = e.EventTime == default
            ? 0L
            : new DateTimeOffset(DateTime.SpecifyKind(e.EventTime, DateTimeKind.Utc)).ToUnixTimeSeconds();
        var bucket = epoch / IncidentBucketSeconds;
        var user = e.Username?.ToLowerInvariant() ?? "";
        return user + "|" + (e.ExamId?.ToString() ?? "") + "|" + NormalizeIncidentType(e.EventType) + "|" + bucket;
    }

    private static int IncidentRepresentativeRank(SuspiciousEvent e)
    {
        var rank = 0;
        var type = e.EventType ?? "";
        var details = e.Details ?? "";
        if (details.Contains("لقطة مراقبة", StringComparison.Ordinal)
            || details.Contains(".jpg", StringComparison.Ordinal)
            || details.Contains(".webm", StringComparison.Ordinal)
            || details.Contains(".mp4", StringComparison.Ordinal))
        {
            rank += 10;
        }

        if (type.EndsWith("_SNAPSHOT", StringComparison.Ordinal))
        {
            rank += 5;
        }

        if (!type.EndsWith("_PRE", StringComparison.Ordinal)
            && !type.EndsWith("_POST", StringComparison.Ordinal)
            && !type.EndsWith("_DURING", StringComparison.Ordinal))
        {
            rank += 3;
        }

        return rank;
    }

    public string ToArabicEventType(string? eventType)
    {
        if (string.IsNullOrWhiteSpace(eventType))
        {
            return "حدث غير محدد";
        }

        var normalized = NormalizeIncidentType(eventType);
        var labels = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["NO_FACE_STREAK"] = "عدم ظهور الوجه",
            ["MULTIPLE_FACES"] = "وجود أكثر من وجه",
            ["STRESS_EXPRESSION"] = "مؤشر توتر",
            ["GAZE_AWAY"] = "انحراف النظر عن الشاشة",
            ["GAZE_AWAY_PROLONGED"] = "انحراف مطوّل عن الشاشة",
            ["TAB_SWITCH"] = "تبديل النافذة أو التبويب",
            ["AUDIO_SPIKE"] = "رصد صوت خارجي",
            ["COPY_ATTEMPT"] = "محاولة نسخ",
            ["PASTE_ATTEMPT"] = "محاولة لصق",
            ["RIGHT_CLICK_BLOCKED"] = "محاولة فتح القائمة اليمنى",
            ["PRINT_ATTEMPT"] = "محاولة طباعة",
            ["FULLSCREEN_EXIT"] = "الخروج من وضع ملء الشاشة",
            ["DEVTOOLS_ATTEMPT"] = "محاولة فتح أدوات المطور",
            ["CRITICAL_CORRELATED_PATTERN"] = "نمط سلوكي مريب مترابط",
            ["CRITICAL_CORRELATION"] = "نمط سلوكي مريب مترابط",
            ["CAMERA_FORCED_OFF"] = "تعطيل الكاميرا",
            ["CAMERA_RECOVERY_FAILED"] = "تعذر استعادة الكاميرا",
            ["FACE_MODEL_LOAD_FAILED"] = "تعذر تحميل نموذج التعرف على الوجه",
            ["AUDIO_MONITOR_DENIED"] = "رفض إذن الميكروفون"
        };
        return labels.GetValueOrDefault(normalized, labels.GetValueOrDefault(eventType, normalized));
    }

    public string ToArabicSeverity(string? severity)
    {
        if (string.IsNullOrWhiteSpace(severity))
        {
            return "غير محدد";
        }

        return severity.ToUpperInvariant() switch
        {
            "LOW" => "منخفض",
            "MEDIUM" => "متوسط",
            "HIGH" => "مرتفع",
            "CRITICAL" => "حرج",
            _ => severity
        };
    }

    private static string Csv(string? v)
    {
        if (v == null)
        {
            return "\"\"";
        }

        return "\"" + v.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }

    public static string SnapshotDirectory => Path.GetFullPath(Path.Combine(
        Environment.GetEnvironmentVariable("RAILWAY_VOLUME_MOUNT_PATH") ?? "data", "snapshots"));

    public List<SuspiciousEvent> EventsForExam(long examId, string? username = null) =>
        _db.SuspiciousEvents.AsNoTracking().Where(e => e.ExamId == examId
                && (username == null || e.Username == username))
            .OrderBy(e => e.EventTime).ThenBy(e => e.Id).ToList();

    public IEnumerable<string> MediaDetailsOutsideExam(long examId) =>
        _db.SuspiciousEvents.AsNoTracking()
            .Where(e => (e.ExamId == null || e.ExamId != examId) && e.Details != null
                && (e.Details.Contains("لقطة مراقبة محفوظة:") || e.Details.Contains("فيديو توثيق:")))
            .Select(e => e.Details!).AsEnumerable();

    public bool HasMediaReference(long examId, string fileName)
    {
        return _db.SuspiciousEvents.AsNoTracking()
            .Where(e => e.ExamId == examId && e.Details != null && e.Details.Contains(fileName))
            .Select(e => e.Details)
            .AsEnumerable()
            .Any(details => details!.Split(" | ").Any(part =>
            {
                if (!part.StartsWith("لقطة مراقبة محفوظة: ", StringComparison.Ordinal)
                    && !part.StartsWith("فيديو توثيق: ", StringComparison.Ordinal))
                    return false;
                return part.EndsWith("/" + fileName, StringComparison.Ordinal)
                    || part.EndsWith((char)92 + fileName, StringComparison.Ordinal);
            }));
    }

    public (string? ImageError, string? VideoError) LogSnapshot(string username, MonitoringEventDto dto,
        string? imageBase64, string? videoBase64 = null, Guid? uploadId = null, bool allowPartial = false)
    {
        var capturedAt = DateTime.Now;
        var (image, imageError) = ReadSnapshotPayload(imageBase64, FileUploadSecurity.Category.Image, allowPartial);
        var (video, videoError) = ReadSnapshotPayload(videoBase64, FileUploadSecurity.Category.Video, allowPartial);
        if (image == null && video == null)
            throw new ArgumentException(imageError ?? videoError ?? "file_empty");

        var captureId = (uploadId ?? Guid.NewGuid()).ToString("N");
        var uploadMarker = " | upload-id=" + captureId;
        if (uploadId.HasValue && _db.SuspiciousEvents.Any(e => e.Username == username && e.ExamId == dto.ExamId
            && e.Details != null && e.Details.EndsWith(uploadMarker)))
            return (imageError, videoError);

        var candidateKey = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            Encoding.UTF8.GetBytes(username))).ToLowerInvariant();
        var stem = $"{dto.ExamId}_{candidateKey}_{captureId}";
        var dir = SnapshotDirectory;
        Directory.CreateDirectory(dir);
        var details = new StringBuilder(dto.Details ?? "").Append(" | وقت الالتقاط: ")
            .Append(capturedAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));

        if (image != null)
        {
            var path = Path.Combine(dir, stem + ".jpg");
            WriteMedia(path, image);
            details.Append(" | لقطة مراقبة محفوظة: ").Append(path);
        }

        if (video != null)
        {
            var path = Path.Combine(dir, stem + FileUploadSecurity.DetectVideoExtension(video));
            WriteMedia(path, video);
            details.Append(" | فيديو توثيق: ").Append(path);
        }

        if (imageError != null)
            details.Append(" | تعذر حفظ الصورة");
        if (videoError != null)
            details.Append(" | تعذر حفظ الفيديو");
        dto.Details = details.Append(uploadMarker).ToString();
        LogEvent(username, dto);
        return (imageError, videoError);
    }

    private static void WriteMedia(string path, byte[] bytes)
    {
        if (File.Exists(path))
            return;

        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temporaryPath, bytes);
            try
            {
                File.Move(temporaryPath, path);
            }
            catch (IOException) when (File.Exists(path))
            {
                // A concurrent retry already published the file.
            }
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    private static (byte[]? Data, string? Error) ReadSnapshotPayload(string? payload,
        FileUploadSecurity.Category category, bool allowPartial)
    {
        if (string.IsNullOrWhiteSpace(payload))
            return (null, null);
        try
        {
            return (FileUploadSecurity.ValidateBase64Media(DecodeBase64Payload(payload), category), null);
        }
        catch (FormatException) when (allowPartial)
        {
            return (null, "file_encoding");
        }
        catch (ArgumentException ex) when (allowPartial)
        {
            return (null, ex.Message);
        }
    }

    private static byte[] DecodeBase64Payload(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return [];
        }

        var payload = raw.Trim();
        var comma = payload.IndexOf(',');
        if (payload.StartsWith("data:", StringComparison.Ordinal) && comma > 0)
        {
            payload = payload[(comma + 1)..];
        }

        return Convert.FromBase64String(payload);
    }
}
