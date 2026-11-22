using Jadeer.Exam.Models;
using Jadeer.Exam.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jadeer.Exam.Controllers;

[Authorize]
[Route("monitoring")]
[ApiController]
public class MonitoringController : ControllerBase
{
    private readonly MonitoringService _monitoringService;

    private readonly ExamService _examService;
    private readonly ILogger<MonitoringController> _logger;

    public MonitoringController(MonitoringService monitoringService, ExamService examService,
        ILogger<MonitoringController> logger)
    {
        _monitoringService = monitoringService;
        _examService = examService;
        _logger = logger;
    }

    [HttpGet("client-config")]
    [IgnoreAntiforgeryToken]
    public IActionResult ClientConfig()
    {
        var throttle = new Dictionary<string, int>
        {
            ["NO_FACE_STREAK"] = 8000,
            ["MULTIPLE_FACES"] = 5000,
            ["STRESS_EXPRESSION"] = 10000,
            ["PERIODIC_CHECK"] = 30000
        };
        var audio = new Dictionary<string, object>
        {
            // مطابق لإعدادات جدير الأصلية مع حساسية أعلى قليلاً للتنبيه
            ["rmsThreshold"] = 0.012,
            ["baselineMargin"] = 0.004,
            ["maxAdaptiveThreshold"] = 0.040,
            ["spikeMultiplier"] = 1.30,
            ["baselineWindowMs"] = 2500,
            ["streakTrigger"] = 2,
            ["episodeGapMs"] = 6000,
            ["tickMs"] = 300
        };
        return Ok(new Dictionary<string, object>
        {
            ["snapshotThrottleMs"] = throttle,
            ["faceAnalysisIntervalMs"] = 700,
            ["audio"] = audio
        });
    }

    [HttpPost("event")]
    [IgnoreAntiforgeryToken]
    public IActionResult Event([FromBody] MonitoringEventDto dto)
    {
        var username = User.Identity?.Name;
        if (string.IsNullOrEmpty(username))
            return Unauthorized();
        dto ??= new MonitoringEventDto();
        dto.Details = RemoveMediaMarkers(dto.Details);
        _monitoringService.LogEvent(username, dto);
        return Ok(new { status = "logged" });
    }

    public class SnapshotRequest
    {
        public string? EventType { get; set; }
        public string? Severity { get; set; }
        public string? Details { get; set; }
        public Guid? UploadId { get; set; }
        public long? ExamId { get; set; }
        public string? ImageBase64 { get; set; }
        public string? VideoBase64 { get; set; }
        public int? ConfidenceScore { get; set; }
    }

    private static string? RemoveMediaMarkers(string? details) => details?
        .Replace("لقطة مراقبة محفوظة:", "", StringComparison.Ordinal)
        .Replace("فيديو توثيق:", "", StringComparison.Ordinal)
        .Replace(" | upload-id=", " | upload id=", StringComparison.Ordinal);

    [HttpPost("snapshot")]
    [IgnoreAntiforgeryToken]
    public IActionResult Snapshot([FromBody] SnapshotRequest? req)
    {
        var username = User.Identity?.Name;
        if (string.IsNullOrEmpty(username))
            return Unauthorized();
        if (req?.ExamId is not long examId)
            return BadRequest(new { status = "missing_exam" });
        if (!User.IsInRole("ADMIN") && !User.IsInRole("EXAMINER")
            && !_examService.CanCandidateAccessExam(username, examId))
            return Forbid();
        var dto = new MonitoringEventDto
        {
            EventType = req.EventType ?? "CAMERA_SNAPSHOT",
            Severity = req.Severity ?? "MEDIUM",
            Details = RemoveMediaMarkers(req.Details),
            ExamId = req.ExamId,
            ConfidenceScore = req.ConfidenceScore
        };
        try
        {
            var (imageError, videoError) = _monitoringService.LogSnapshot(username, dto, req.ImageBase64,
                req.VideoBase64, req.UploadId, allowPartial: true);
            if (imageError != null || videoError != null)
            {
                _logger.LogWarning("Monitoring upload partially saved. Image error: {ImageError}, video error: {VideoError}.",
                    imageError, videoError);
                return Ok(new { status = "partial" });
            }
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        {
            _logger.LogWarning("Monitoring upload rejected: {Reason}.",
                ex is FormatException ? "file_encoding" : ex.Message);
            return BadRequest(new { status = "invalid_media" });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning("Monitoring storage unavailable: {ErrorType}.", ex.GetType().Name);
            return StatusCode(503, new { status = "storage_unavailable" });
        }
        return Ok(new { status = "saved" });
    }
}
