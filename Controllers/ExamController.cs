using System.Text.Json;
using Jadeer.Exam.Data;
using Jadeer.Exam.Models;
using Jadeer.Exam.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Jadeer.Exam.Controllers;

[Authorize]
[Route("exam")]
public class ExamController : AppControllerBase
{
    private readonly ExamService _examService;
    private readonly ExamSessionService _examSessionService;
    private readonly MonitoringService _monitoringService;
    private readonly AppDbContext _db;

    public ExamController(
        ExamService examService,
        ExamSessionService examSessionService,
        MonitoringService monitoringService,
        AppDbContext db)
    {
        _examService = examService;
        _examSessionService = examSessionService;
        _monitoringService = monitoringService;
        _db = db;
    }

    private bool Privileged => IsAdmin || IsExaminer;

    private static string PrecheckKey(string username, long examId) =>
        $"precheck_done_{username}_{examId}";

    private bool PrecheckDone(string username, long examId) =>
        HttpContext.Session.GetString(PrecheckKey(username, examId)) == "1";

    [HttpGet("{id:long}/precheck")]
    public async Task<IActionResult> Precheck([FromRoute(Name = "id")] long examId)
    {
        var username = CurrentUsername;
        if (!Privileged && !_examService.CanCandidateAccessExam(username, examId))
            return Redirect("/dashboard");
        if (!Privileged && !_examService.IsExamEntryOpen(examId))
            return StatusCode(403, "الدخول إلى الاختبار متاح خلال الفترة المحددة بتوقيت الرياض.");
        if (!Privileged && _examService.HasSubmittedAttempt(examId, username))
            return Redirect("/dashboard");
        if (PrecheckDone(username, examId))
            return Redirect($"/exam/{examId}");

        var exam = _examService.GetExam(examId);
        if (exam == null) return NotFound();

        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Username == username);
        var display = !string.IsNullOrWhiteSpace(user?.FullName) ? user!.FullName.Trim() : username;
        ViewBag.Exam = exam;
        ViewBag.WelcomeLine = "مرحبًا " + display;
        ViewBag.PrefillName = user?.FullName ?? "";
        ViewBag.InstructionsText =
            "1) الالتزام بتعليمات الاختبار وعدم فتح أي نافذة خارجية.\n" +
            "2) الجلوس أمام الكاميرا طوال مدة الاختبار.\n" +
            "3) يمنع الاستعانة بأي شخص أو وسيلة خارجية.\n" +
            "4) الإقرار بصحة الهوية قبل بدء الاختبار.";
        return View("~/Views/Exam/Precheck.cshtml");
    }

    [HttpPost("{id:long}/precheck")]
    [ValidateAntiForgeryToken]
    public IActionResult Precheck(
        [FromRoute(Name = "id")] long examId,
        string? agree,
        string? ackName,
        string? faceImageBase64,
        string? idCardImageBase64)
    {
        var username = CurrentUsername;
        if (!Privileged && !_examService.CanCandidateAccessExam(username, examId))
            return Redirect("/dashboard");
        if (!Privileged && !_examService.IsExamEntryOpen(examId))
            return StatusCode(403, "الدخول إلى الاختبار متاح خلال الفترة المحددة بتوقيت الرياض.");
        if (!Privileged && _examService.HasSubmittedAttempt(examId, username))
            return Redirect("/dashboard");
        if (!string.Equals(agree, "on", StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(faceImageBase64)
            || string.IsNullOrWhiteSpace(idCardImageBase64))
            return Redirect($"/exam/{examId}/precheck");

        var faceDto = new MonitoringEventDto
        {
            ExamId = examId,
            EventType = "FACE_VERIFICATION_CAPTURE",
            Severity = "LOW",
            Details = "توثيق قبل الاختبار - صورة الوجه | المقر: " + (ackName ?? username).Trim()
        };
        var idDto = new MonitoringEventDto
        {
            ExamId = examId,
            EventType = "ID_CARD_CAPTURE",
            Severity = "LOW",
            Details = "توثيق قبل الاختبار - صورة الهوية الوطنية | المقر: " + (ackName ?? username).Trim()
        };
        try
        {
            _monitoringService.LogSnapshot(username, faceDto, faceImageBase64);
            _monitoringService.LogSnapshot(username, idDto, idCardImageBase64);
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        {
            return BadRequest("تعذر حفظ صور التحقق. ارجع وأعد التقاط الصورتين.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return StatusCode(503, "تعذر حفظ صور التحقق. تواصل مع مراقب الاختبار.");
        }

        var doneDto = new MonitoringEventDto
        {
            ExamId = examId,
            EventType = "ID_VERIFICATION_SUBMITTED",
            Severity = "LOW",
            Details = "اكتمل التحقق قبل الاختبار (وجه + هوية)"
        };
        _monitoringService.LogEvent(username, doneDto);
        HttpContext.Session.SetString(PrecheckKey(username, examId), "1");
        return Redirect($"/exam/{examId}");
    }

    [HttpGet("{id:long}")]
    public IActionResult Take([FromRoute(Name = "id")] long examId)
    {
        var username = CurrentUsername;
        if (!Privileged && !_examService.CanCandidateAccessExam(username, examId))
            return Redirect("/dashboard");
        if (!Privileged && !_examService.IsExamEntryOpen(examId))
            return StatusCode(403, "الدخول إلى الاختبار متاح خلال الفترة المحددة بتوقيت الرياض.");
        if (!Privileged && _examService.HasSubmittedAttempt(examId, username))
            return Redirect("/dashboard");
        if (!Privileged && !PrecheckDone(username, examId))
            return Redirect($"/exam/{examId}/precheck");

        var exam = _examService.GetExam(examId);
        if (exam == null) return NotFound();

        ViewBag.Exam = exam;
        try
        {
            var (questions, draft, attemptId) = _examService.GetCandidateQuestions(exam, username);
            ViewBag.Questions = questions;
            ViewBag.QuestionClockId = attemptId;
            ViewBag.DraftAnswers = draft == null ? null
                : JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(draft);
        }
        catch (ArgumentException ex) when (ex.Message.StartsWith("draw_", StringComparison.Ordinal))
        {
            return Redirect("/dashboard");
        }
        ViewBag.IsEnglish = string.Equals(ViewData["PageLang"] as string, "en", StringComparison.OrdinalIgnoreCase);
        return View("~/Views/Exam/Index.cshtml");
    }

    [HttpGet("{examId:long}/questions/{questionId:long}/image")]
    public IActionResult QuestionImage(long examId, long questionId)
    {
        var username = CurrentUsername;
        if (!Privileged && !_examService.CanCandidateAccessExam(username, examId))
            return Forbid();

        var img = _examService.GetQuestionImagePayload(examId, questionId);
        if (img == null) return NotFound();
        return File(img.Data, img.MediaType);
    }

    [HttpPost("{id:long}/autosave")]
    [IgnoreAntiforgeryToken]
    public IActionResult Autosave([FromRoute(Name = "id")] long examId, [FromBody] Dictionary<string, JsonElement>? payload)
    {
        var username = CurrentUsername;
        if (!Privileged && (!_examService.CanCandidateAccessExam(username, examId)
                            || _examService.HasSubmittedAttempt(examId, username)))
            return StatusCode(403, new { status = "forbidden" });
        if (!Privileged && !PrecheckDone(username, examId))
            return StatusCode(403, new { status = "precheck_required" });

        var answers = payload != null && payload.TryGetValue("answers", out var a)
            ? a.ToString()
            : "{}";
        try
        {
            _examService.SaveDraft(examId, username, answers);
        }
        catch (ArgumentException ex) when (ex.Message == "draw_attempt_missing")
        {
            return Conflict(new { status = ex.Message });
        }
        return Ok(new { status = "saved" });
    }

    [HttpPost("{id:long}/submit")]
    [IgnoreAntiforgeryToken]
    public IActionResult Submit([FromRoute(Name = "id")] long examId, [FromBody] Dictionary<string, JsonElement>? payload)
    {
        var username = CurrentUsername;
        if (!Privileged && (!_examService.CanCandidateAccessExam(username, examId)
                            || _examService.HasSubmittedAttempt(examId, username)))
            return StatusCode(403, new { status = "forbidden" });
        if (!Privileged && !PrecheckDone(username, examId))
            return StatusCode(403, new { status = "precheck_required" });

        var answers = payload != null && payload.TryGetValue("answers", out var a)
            ? a.ToString()
            : "{}";
        ExamAttempt attempt;
        try
        {
            attempt = _examService.SubmitAttempt(examId, username, answers);
        }
        catch (ArgumentException ex) when (ex.Message == "draw_attempt_missing")
        {
            return Conflict(new { status = ex.Message });
        }
        HttpContext.Session.Remove(PrecheckKey(username, examId));
        return Ok(new { status = "submitted", score = attempt.Score });
    }

    [HttpPost("{id:long}/session/start")]
    [IgnoreAntiforgeryToken]
    public IActionResult StartSession([FromRoute(Name = "id")] long examId, [FromBody] Dictionary<string, string>? body)
    {
        var username = CurrentUsername;
        if (!Privileged && (!_examService.CanCandidateAccessExam(username, examId)
                            || _examService.HasSubmittedAttempt(examId, username)))
            return StatusCode(403, new
            {
                status = "forbidden",
                message = "غير مصرح لك بهذا الاختبار أو تم تسليمه سابقاً."
            });
        if (!Privileged && !PrecheckDone(username, examId))
            return StatusCode(403, new
            {
                status = "precheck_required",
                message = "يجب قراءة التعليمات ورفع الهوية قبل بدء الاختبار."
            });

        if (!Privileged && !_examService.IsExamEntryOpen(examId))
            return StatusCode(403, new { status = "exam_window_closed", message = "الدخول إلى الاختبار متاح خلال الفترة المحددة بتوقيت الرياض." });

        var cid = body != null && body.TryGetValue("clientSessionId", out var c) ? (c ?? "").Trim() : "";
        if (cid.Length == 0)
            return BadRequest(new { status = "error", message = "معرّف الجلسة مفقود" });

        var result = _examSessionService.StartOrResumeSession(username, examId, cid);
        if (result.Finished)
            return Conflict(new { status = "finished", message = "تم تسليم هذا الاختبار مسبقاً." });
        if (result.Conflict)
            return Conflict(new
            {
                status = "conflict",
                message = "يبدو أن هذا الاختبار مفتوح في نافذة أو جهاز آخر حديثاً. أغلِقها ثم حدّث الصفحة."
            });
        return Ok(new { status = "ok" });
    }

    [HttpPost("{id:long}/session/heartbeat")]
    [IgnoreAntiforgeryToken]
    public IActionResult Heartbeat([FromRoute(Name = "id")] long examId, [FromBody] Dictionary<string, string>? body)
    {
        var cid = body != null && body.TryGetValue("clientSessionId", out var c) ? (c ?? "").Trim() : "";
        if (cid.Length == 0)
            return BadRequest(new { status = "invalid" });
        var ok = _examSessionService.Heartbeat(CurrentUsername, examId, cid);
        if (!ok)
            return Conflict(new { status = "lost" });
        return Ok(new { status = "ok" });
    }
}
