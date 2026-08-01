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

    public MonitoringController(MonitoringService monitoringService)
    {
        _monitoringService = monitoringService;
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
        _monitoringService.LogEvent(username, dto ?? new MonitoringEventDto());
        return Ok(new { status = "logged" });
    }

    public class SnapshotRequest
    {
        public string? EventType { get; set; }
        public string? Severity { get; set; }
        public string? Details { get; set; }
        public long? ExamId { get; set; }
        public string? ImageBase64 { get; set; }
        public string? VideoBase64 { get; set; }
        public int? ConfidenceScore { get; set; }
    }

    [HttpPost("snapshot")]
    [IgnoreAntiforgeryToken]
    public IActionResult Snapshot([FromBody] SnapshotRequest? req)
    {
        var username = User.Identity?.Name;
        if (string.IsNullOrEmpty(username))
            return Unauthorized();
        req ??= new SnapshotRequest();
        var dto = new MonitoringEventDto
        {
            EventType = req.EventType ?? "CAMERA_SNAPSHOT",
            Severity = req.Severity ?? "MEDIUM",
            Details = req.Details,
            ExamId = req.ExamId,
            ConfidenceScore = req.ConfidenceScore
        };
        _monitoringService.LogSnapshot(username, dto, req.ImageBase64, req.VideoBase64);
        return Ok(new { status = "saved" });
    }
}
