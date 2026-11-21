using System.Globalization;
using ClosedXML.Excel;
using Jadeer.Exam.Data;
using Jadeer.Exam.Models;
using Jadeer.Exam.Security;
using Jadeer.Exam.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Jadeer.Exam.Controllers;

[Authorize(Roles = "ADMIN,EXAMINER")]
[Route("admin")]
public class AdminController : AppControllerBase
{
    private readonly ExamService _examService;
    private readonly AppDbContext _db;
    private readonly AuditLogger _audit;

    public AdminController(ExamService examService, AppDbContext db, AuditLogger audit)
    {
        _examService = examService;
        _db = db;
        _audit = audit;
    }

    private static int? ResolveDurationMinutes(int? durationValue, string? durationUnit)
    {
        if (durationValue == null || durationValue < 1) return null;
        if (string.Equals(durationUnit, "hours", StringComparison.OrdinalIgnoreCase))
            return durationValue * 60;
        return durationValue;
    }

    private string CandidatesRedirect(long? examId, string? query)
    {
        var url = "/admin/operations?view=create&tab=candidates";
        if (examId != null) url += "&examId=" + examId;
        if (!string.IsNullOrWhiteSpace(query))
            url += query.StartsWith('&') ? query : "&" + query;
        return url;
    }

    // ── Operations hub ──────────────────────────────────────────────

    [HttpGet("operations")]
    public async Task<IActionResult> Operations(
        string? view, string? tab, string? lifecycle, long? examId, string? qtype)
    {
        if (!IsStaff) return Redirect("/dashboard");

        if (IsAdmin && !string.IsNullOrWhiteSpace(tab))
        {
            var t = tab.Trim();
            if (t == "setup") return Redirect("/admin/users?view=add");
            if (t == "followup") return Redirect("/admin/reports");
        }

        ViewBag.IsAdmin = IsAdmin;
        ViewBag.IsExaminer = !IsAdmin;
        var staffUser = CurrentUsername;
        var opsView = string.IsNullOrWhiteSpace(view) ? "list" : view.Trim();
        if (opsView is not ("list" or "create")) opsView = "list";
        ViewBag.OpsView = opsView;

        if (opsView == "list")
        {
            if (!string.IsNullOrWhiteSpace(tab))
            {
                var wizardTab = tab.Trim();
                if (wizardTab is "exam" or "questions" or "candidates")
                {
                    var redirect = $"/admin/operations?view=create&tab={wizardTab}";
                    if (examId != null) redirect += "&examId=" + examId;
                    if (!string.IsNullOrWhiteSpace(qtype)) redirect += "&qtype=" + qtype.Trim();
                    return Redirect(redirect);
                }
            }

            ViewData["ActiveNav"] = "operations";
            ViewBag.ExamSummaries = _examService.ExamSummariesForStaff(staffUser, lifecycle);
            ViewBag.LifecycleFilter = lifecycle ?? "";
            ViewBag.DefaultTab = "exam";
            ViewBag.InitialQcount = 0;
            ViewBag.WizardQuestionsDone = false;
            ViewBag.QuestionsTabUnlocked = false;
            if (IsAdmin)
                ViewBag.Examiners = await _db.Users.Where(u => u.Role == Role.EXAMINER).ToListAsync();
            return View("~/Views/Admin/Operations.cshtml");
        }

        if (string.IsNullOrWhiteSpace(tab))
            return Redirect("/admin/operations?view=create&tab=exam");

        var defaultTab = tab.Trim();
        if (defaultTab is not ("exam" or "questions" or "candidates"))
            return Redirect("/admin/operations?view=create&tab=exam");

        var staffExams = new List<Models.Exam>(_examService.ListExamsForStaff(staffUser));
        long? defaultExamId = examId;
        if (defaultExamId == null && staffExams.Count > 0)
            defaultExamId = staffExams[0].Id;
        var effectiveExamId = examId ?? defaultExamId;
        var wizardHasExam = effectiveExamId != null && _examService.GetExam(effectiveExamId.Value) != null;
        var questionsTabUnlocked = staffExams.Count > 0 || (effectiveExamId != null && wizardHasExam);
        var wizardQuestionsDone = effectiveExamId != null && _examService.IsQuestionBankStepComplete(effectiveExamId);
        var examBudgetFullyAllocated = effectiveExamId != null && _examService.IsExamBudgetFullyAllocated(effectiveExamId);

        if (defaultTab == "questions" && examId == null && staffExams.Count > 0)
            return Redirect("/admin/operations?view=create&tab=questions&examId=" + staffExams[0].Id);
        if (defaultTab == "questions" && !questionsTabUnlocked)
            return Redirect("/admin/operations?view=create&tab=exam");
        if (defaultTab == "candidates" && (effectiveExamId == null || !wizardHasExam))
            return Redirect("/admin/operations?view=create&tab=exam");

        ViewBag.DefaultTab = defaultTab;
        ViewBag.DefaultExamId = defaultExamId;
        ViewBag.WizardHasExam = wizardHasExam;
        ViewBag.QuestionsTabUnlocked = questionsTabUnlocked;
        ViewBag.WizardQuestionsDone = wizardQuestionsDone;
        ViewBag.ExamBudgetFullyAllocated = examBudgetFullyAllocated;
        ViewBag.EffectiveExamId = effectiveExamId;
        ViewData["ActiveNav"] = "operations";
        ViewBag.CurrentUsername = staffUser;

        if (effectiveExamId != null && staffExams.All(e => e.Id != effectiveExamId))
        {
            var ex = _examService.GetExam(effectiveExamId.Value);
            if (ex != null && (IsAdmin || _examService.CanManageExam(staffUser, ex.Id)))
                staffExams.Insert(0, ex);
        }

        ViewBag.Exams = staffExams;
        var examBudgets = new Dictionary<long, ExamQuestionBudget>();
        foreach (var e in staffExams)
            examBudgets[e.Id] = _examService.ExamQuestionBudget(e.Id);
        ViewBag.ExamBudgets = examBudgets;
        ViewBag.ExamSummaries = _examService.ExamSummariesForStaff(staffUser, lifecycle);
        ViewBag.LifecycleFilter = lifecycle ?? "";
        ViewBag.SelectedExamId = examId;

        if (effectiveExamId != null)
        {
            var ex = _examService.GetExam(effectiveExamId.Value);
            if (ex != null)
            {
                ViewBag.SetupExam = ex;
                ViewBag.SetupQuestionCount = _examService.CountQuestions(effectiveExamId.Value);
            }
            ViewBag.CandidateManageRows = _examService.CandidateManageRowsForExam(effectiveExamId);
            ViewBag.MailEnabled = _examService.IsExamMailEnabled();
            if (defaultTab == "questions")
                ViewBag.ExamQuestions = _examService.GetExamQuestions(effectiveExamId.Value, false);
        }
        else
        {
            ViewBag.CandidateManageRows = new List<Dictionary<string, object?>>();
        }

        ViewBag.InitialQcount = effectiveExamId != null ? _examService.CountQuestions(effectiveExamId.Value) : 0;
        ViewBag.QuestionTypeTab = string.Equals(qtype, "essay", StringComparison.OrdinalIgnoreCase) ? "essay" : "mcq";
        ViewBag.Departments = await _db.Departments.ToListAsync();
        ViewBag.Candidates = await _db.Users.Where(u => u.Role == Role.CANDIDATE).ToListAsync();
        ViewBag.Graders = await _db.Users.Where(u => u.Role == Role.GRADER).ToListAsync();
        if (IsAdmin)
            ViewBag.Examiners = await _db.Users.Where(u => u.Role == Role.EXAMINER).ToListAsync();

        return View("~/Views/Admin/Operations.cshtml");
    }

    // ── Exams ───────────────────────────────────────────────────────

    [HttpPost("exams")]
    [ValidateAntiForgeryToken]
    public IActionResult CreateExam(
        string title,
        int? durationMinutes,
        int? durationValue,
        string? durationUnit,
        long? departmentId,
        bool randomized = false,
        string? opportunityType = null,
        string? jobTitle = null,
        string? companyName = null,
        string? city = null,
        int? opportunityYear = null,
        int? totalMarks = null,
        int? passThreshold = null,
        bool practicalEnabled = false,
        int? practicalMaxMarks = null)
    {
        title = (title ?? "").Trim();
        if (title.Length == 0 || title.Length > 200)
            return Redirect("/admin/operations?view=create&tab=exam&error=title");

        if (durationMinutes == null || durationMinutes < 1)
            durationMinutes = ResolveDurationMinutes(durationValue, durationUnit);
        if (durationMinutes == null || durationMinutes < 1)
            return Redirect("/admin/operations?view=create&tab=exam&error=duration");
        if (totalMarks == null || totalMarks < 1)
            return Redirect("/admin/operations?view=create&tab=exam&error=marks");
        if (passThreshold == null || passThreshold < 1 || passThreshold > totalMarks)
            return Redirect("/admin/operations?view=create&tab=exam&error=pass");
        if (practicalEnabled && (practicalMaxMarks == null || practicalMaxMarks < 1 || practicalMaxMarks >= totalMarks))
            return Redirect("/admin/operations?view=create&tab=exam&error=practical");

        try
        {
            var exam = _examService.CreateExamForStaff(
                CurrentUsername, title, durationMinutes, randomized, departmentId,
                opportunityType, jobTitle, companyName, city, opportunityYear,
                totalMarks, passThreshold, practicalEnabled, practicalMaxMarks);
            return Redirect($"/admin/operations?view=create&tab=questions&examId={exam.Id}");
        }
        catch (KeyNotFoundException)
        {
            return Redirect("/admin/operations?view=create&tab=exam&error=department");
        }
        catch (ArgumentException ex)
        {
            if (ex.Message.Contains("إدارة", StringComparison.Ordinal))
                return Redirect("/admin/operations?view=create&tab=exam&error=no_dept");
            if (ex.Message.Contains("practical", StringComparison.OrdinalIgnoreCase))
                return Redirect("/admin/operations?view=create&tab=exam&error=practical");
            return Redirect("/admin/operations?view=create&tab=exam&error=title");
        }
    }

    [HttpPost("exams/{id:long}/practical-settings")]
    [ValidateAntiForgeryToken]
    public IActionResult PracticalSettings(long id, bool practicalEnabled = false, int? practicalMaxMarks = null)
    {
        var examId = id;
        if (!IsStaff || !_examService.CanManageExam(CurrentUsername, examId))
            return Redirect("/dashboard");
        try
        {
            _examService.UpdateExamPracticalSettings(CurrentUsername, examId, practicalEnabled, practicalMaxMarks);
            return Redirect($"/admin/operations?view=create&tab=candidates&examId={examId}&practical_saved=1");
        }
        catch (ArgumentException ex)
        {
            var err = ex.Message == "draw_locked" ? "draw_locked"
                : ex.Message.Contains("budget", StringComparison.OrdinalIgnoreCase) ? "practical_budget" : "practical";
            return Redirect($"/admin/operations?view=create&tab=candidates&examId={examId}&error={err}");
        }
    }

    [HttpPost("exams/{id:long}/random-question-count")]
    [ValidateAntiForgeryToken]
    public IActionResult SetRandomQuestionCount(long id, int? randomQuestionCount)
    {
        if (!IsStaff || !_examService.CanManageExam(CurrentUsername, id))
            return Forbid();
        var url = $"/admin/operations?view=create&tab=questions&examId={id}&qtype=mcq";
        if (!ModelState.IsValid)
            return Redirect(url + "&error=draw_count");

        try
        {
            _examService.UpdateRandomQuestionCount(id, randomQuestionCount);
            return Redirect(url + "&draw_saved=1");
        }
        catch (ArgumentException ex) when (ex.Message is "draw_count" or "draw_locked")
        {
            return Redirect(url + "&error=" + ex.Message);
        }
    }

    [HttpPost("exams/{id:long}/publish")]
    [ValidateAntiForgeryToken]
    public IActionResult PublishExam(long id, string? after)
    {
        var examId = id;
        if (!IsStaff || !_examService.CanManageExam(CurrentUsername, examId))
            return Redirect("/admin/operations");
        try
        {
            var emails = _examService.PublishExam(examId);
            _audit.ContentPublished(CurrentUsername, "exam", examId);
            if (string.Equals(after, "list", StringComparison.OrdinalIgnoreCase))
                return Redirect($"/admin/operations?view=list&published=1&emails_sent={emails}");
            return Redirect($"/admin/operations?view=create&tab=candidates&examId={examId}&published=1&emails_sent={emails}");
        }
        catch (ArgumentException ex)
        {
            var err = ex.Message.StartsWith("draw_", StringComparison.Ordinal) ? ex.Message
                : ex.Message.Contains("schedule", StringComparison.OrdinalIgnoreCase) ? "schedule" : "publish";
            return Redirect($"/admin/operations?view=create&tab=candidates&examId={examId}&error={err}");
        }
    }

    [HttpPost("exams/{id:long}/schedule")]
    [ValidateAntiForgeryToken]
    public IActionResult SaveSchedule(long id, string? scheduledAt, string? instructions)
    {
        var examId = id;
        if (!IsStaff || !_examService.CanManageExam(CurrentUsername, examId))
            return Redirect("/dashboard");
        DateTime? when = null;
        if (!string.IsNullOrWhiteSpace(scheduledAt))
        {
            if (!DateTime.TryParse(scheduledAt, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var parsed)
                && !DateTime.TryParse(scheduledAt, out parsed))
                return Redirect($"/admin/operations?view=create&tab=candidates&examId={examId}&error=schedule_invalid");
            when = parsed;
        }

        try
        {
            _examService.UpdateExamSchedule(CurrentUsername, examId, when, instructions);
            return Redirect($"/admin/operations?view=create&tab=candidates&examId={examId}&schedule_saved=1");
        }
        catch
        {
            return Redirect($"/admin/operations?view=create&tab=candidates&examId={examId}&error=schedule_invalid");
        }
    }

    [HttpPost("exams/{id:long}/delete")]
    [ValidateAntiForgeryToken]
    public IActionResult DeleteExam(long id)
    {
        var examId = id;
        if (!IsStaff || !_examService.CanManageExam(CurrentUsername, examId))
            return Redirect("/admin/operations");
        _examService.DeleteExamForStaff(CurrentUsername, examId);
        return Redirect("/admin/operations?view=create&tab=exam");
    }

    [HttpPost("exams/{id:long}/assign-grader")]
    [ValidateAntiForgeryToken]
    public IActionResult AssignGrader(long id, long graderUserId, string? after)
    {
        var examId = id;
        try
        {
            _examService.AssignGraderToExam(CurrentUsername, examId, graderUserId);
            if (string.Equals(after, "candidates", StringComparison.OrdinalIgnoreCase))
                return Redirect($"/admin/operations?view=create&tab=candidates&examId={examId}&grader_assigned=1");
            return Redirect("/admin/operations?view=list&grader_assigned=1");
        }
        catch
        {
            return Redirect("/admin/operations?view=list&error=assign_grader");
        }
    }

    [HttpPost("exams/{id:long}/assign-examiner")]
    [Authorize(Roles = "ADMIN")]
    [ValidateAntiForgeryToken]
    public IActionResult AssignExaminerToExam(long id, long examinerUserId)
    {
        var examId = id;
        try
        {
            _examService.AssignExamToExaminer(examId, examinerUserId);
            return Redirect("/admin/operations?view=list&examiner_assigned=1");
        }
        catch
        {
            return Redirect("/admin/operations?view=list&error=assign_examiner");
        }
    }

    [HttpGet("exams/{id:long}/preview")]
    public IActionResult ExamPreview(long id)
    {
        var examId = id;
        if (!IsStaff || !_examService.CanManageExam(CurrentUsername, examId))
            return Redirect("/admin/operations");
        var exam = _examService.GetExam(examId);
        if (exam == null) return NotFound();
        ViewBag.Exam = exam;
        ViewBag.Questions = _examService.GetExamQuestions(examId, false);
        ViewBag.QuestionCount = _examService.CountQuestions(examId);
        ViewData["ActiveNav"] = "operations";
        return View("~/Views/Admin/ExamPreview.cshtml");
    }

    // ── Questions ───────────────────────────────────────────────────

    [HttpGet("exams/{examId:long}/questions/{questionId:long}/image")]
    public IActionResult QuestionImage(long examId, long questionId)
    {
        if (!IsStaff || !_examService.CanManageExam(CurrentUsername, examId))
            return StatusCode(403);
        var img = _examService.GetQuestionImagePayload(examId, questionId);
        if (img == null) return NotFound();
        return File(img.Data, img.MediaType);
    }

    [HttpGet("exams/{examId:long}/questions/{qid:long}/edit")]
    public IActionResult EditQuestion(long examId, long qid)
    {
        if (!IsStaff || !_examService.CanManageExam(CurrentUsername, examId))
            return Redirect("/admin/operations");
        var exam = _examService.GetExam(examId);
        var question = _examService.GetExamQuestions(examId, false).FirstOrDefault(q => q.Id == qid);
        if (exam == null || question == null) return Redirect("/admin/operations");
        ViewBag.Exam = exam;
        ViewBag.Question = question;
        ViewData["ActiveNav"] = "operations";
        return View("~/Views/Admin/QuestionEdit.cshtml");
    }

    [HttpPost("exams/{examId:long}/questions/{qid:long}/edit")]
    [ValidateAntiForgeryToken]
    public IActionResult EditQuestion(long examId, long qid,
        string text, string? optionA, string? optionB, string? optionC, string? optionD,
        string? correctOption, int mark, bool essayQuestion = false,
        int? durationSeconds = null, string? topic = null, string? difficulty = null,
        string? rubric = null, IFormFile? image = null, bool removeImage = false)
    {
        if (!_examService.CanManageExam(CurrentUsername, examId))
            return Redirect("/dashboard");
        var qtype = essayQuestion ? "essay" : "mcq";
        try
        {
            if (image is { Length: > 0 })
                FileUploadSecurity.ValidateAsync(image, FileUploadSecurity.Category.Image).GetAwaiter().GetResult();
            _examService.UpdateQuestionInExam(
                examId, qid, text, optionA, optionB, optionC, optionD, correctOption,
                mark, essayQuestion, durationSeconds, topic, difficulty, rubric, image, removeImage);
            return Redirect($"/admin/operations?view=create&tab=questions&examId={examId}&qtype={qtype}&updated=1");
        }
        catch (ArgumentException ex)
        {
            var code = MapQuestionError(ex.Message);
            return Redirect($"/admin/exams/{examId}/questions/{qid}/edit?error={code}");
        }
        catch
        {
            return Redirect($"/admin/exams/{examId}/questions/{qid}/edit?error=save");
        }
    }

    [HttpPost("exams/{examId:long}/questions/{qid:long}/delete")]
    [ValidateAntiForgeryToken]
    public IActionResult DeleteQuestion(long examId, long qid)
    {
        if (!_examService.CanManageExam(CurrentUsername, examId))
            return Redirect("/dashboard");
        try
        {
            _examService.DeleteQuestionFromExam(examId, qid);
        }
        catch (ArgumentException ex) when (ex.Message == "draw_locked")
        {
            return Redirect($"/admin/operations?view=create&tab=questions&examId={examId}&error=draw_locked");
        }
        var count = _examService.CountQuestions(examId);
        return Redirect($"/admin/operations?view=create&tab=questions&examId={examId}&deleted=1&qcount={count}");
    }

    [HttpPost("question-bank")]
    [ValidateAntiForgeryToken]
    public IActionResult AddQuestion(
        long examId, string text,
        string? optionA, string? optionB, string? optionC, string? optionD,
        string? correctOption, int mark, bool essayQuestion = false,
        int? durationSeconds = null, string? topic = null, string? difficulty = null,
        string? rubric = null, IFormFile? image = null)
    {
        if (!_examService.CanManageExam(CurrentUsername, examId))
            return Redirect("/dashboard");
        var qtype = essayQuestion ? "essay" : "mcq";
        var redirectBase = $"/admin/operations?view=create&tab=questions&examId={examId}&qtype={qtype}";
        if (durationSeconds == null || durationSeconds < 10)
            return Redirect(redirectBase + "&error=duration");
        if (essayQuestion)
        {
            if (string.IsNullOrWhiteSpace(rubric))
                return Redirect(redirectBase + "&error=fields");
        }
        else if (string.IsNullOrWhiteSpace(optionA) || string.IsNullOrWhiteSpace(optionB)
                 || string.IsNullOrWhiteSpace(correctOption))
        {
            return Redirect(redirectBase + "&error=fields");
        }

        try
        {
            if (image is { Length: > 0 })
                FileUploadSecurity.ValidateAsync(image, FileUploadSecurity.Category.Image).GetAwaiter().GetResult();
            _examService.AddQuestionToExam(
                examId, text, optionA, optionB, optionC, optionD, correctOption, mark,
                essayQuestion, durationSeconds, topic, difficulty, rubric, image);
            var count = _examService.CountQuestions(examId);
            return Redirect($"{redirectBase}&added=1&qcount={count}");
        }
        catch (ArgumentException ex)
        {
            return Redirect(redirectBase + "&error=" + MapQuestionError(ex.Message));
        }
    }

    [HttpPost("question-bank/import-excel")]
    [ValidateAntiForgeryToken]
    public IActionResult ImportQuestionsExcel(long examId, IFormFile? file)
    {
        if (file is { Length: > 0 })
        {
            if (!_examService.CanManageExam(CurrentUsername, examId))
                return Redirect("/dashboard");
            try
            {
                FileUploadSecurity.ValidateAsync(file, FileUploadSecurity.Category.Excel).GetAwaiter().GetResult();
                _examService.ImportQuestionsFromExcel(examId, file);
                var count = _examService.CountQuestions(examId);
                return Redirect($"/admin/operations?view=create&tab=questions&examId={examId}&qcount={count}");
            }
            catch (Exception ex) when (ex.Message is "draw_locked" or "draw_time" or "draw_marks" or "draw_equal_marks")
            {
                return Redirect($"/admin/operations?view=create&tab=questions&examId={examId}&error={ex.Message}");
            }
            catch
            {
                return Redirect($"/admin/operations?view=create&tab=questions&examId={examId}&error=file");
            }
        }

        return Redirect($"/admin/operations?view=create&tab=questions&examId={examId}");
    }

    [HttpGet("exams/{id:long}/questions.xlsx")]
    public IActionResult ExportQuestionsExcel([FromRoute(Name = "id")] long examId)
    {
        if (!IsStaff || !_examService.CanManageExam(CurrentUsername, examId))
            return StatusCode(403, "forbidden");
        if (_examService.GetExam(examId) == null)
            return NotFound();

        var bytes = _examService.ExportQuestionsToExcel(examId);
        return File(bytes,
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"exam-{examId}-questions.xlsx");
    }

    private static string MapQuestionError(string? msg)
    {
        if (string.IsNullOrEmpty(msg)) return "fields";
        if (msg is "draw_locked" or "draw_time" or "draw_marks" or "draw_equal_marks") return msg;
        if (msg.Contains("time", StringComparison.OrdinalIgnoreCase) || msg.Contains("وقت", StringComparison.Ordinal))
            return "time";
        if (msg.Contains("marks", StringComparison.OrdinalIgnoreCase) || msg.Contains("درجة", StringComparison.Ordinal))
            return "marks";
        if (msg.Contains("image", StringComparison.OrdinalIgnoreCase)) return "image_type";
        return "fields";
    }

    // ── Candidates ──────────────────────────────────────────────────

    [HttpPost("exam-candidates/import-excel")]
    [ValidateAntiForgeryToken]
    public IActionResult ImportCandidatesExcel(long examId, IFormFile? file)
    {
        if (!IsStaff) return Redirect("/dashboard");
        if (file is { Length: > 0 })
        {
            if (!_examService.CanManageExam(CurrentUsername, examId))
                return Redirect("/dashboard");
            try
            {
                FileUploadSecurity.ValidateAsync(file, FileUploadSecurity.Category.Excel).GetAwaiter().GetResult();
                _examService.ImportExamCandidatesFromExcelForStaff(CurrentUsername, examId, file);
            }
            catch
            {
                return Redirect(CandidatesRedirect(examId, "error=file"));
            }
        }

        return Redirect(CandidatesRedirect(examId, null));
    }

    [HttpPost("exam-candidates/assign")]
    [ValidateAntiForgeryToken]
    public IActionResult AssignCandidate(long examId, string email, string? fullName, string? nationalId)
    {
        if (!IsStaff) return Redirect("/dashboard");
        if (!_examService.CanManageExam(CurrentUsername, examId))
            return Redirect(CandidatesRedirect(examId, "error=assign_forbidden"));
        var result = _examService.AllowCandidateForExamByStaff(CurrentUsername, examId, email, fullName, nationalId);
        return result switch
        {
            "ok" => Redirect(CandidatesRedirect(examId, "assigned=1")),
            "duplicate" => Redirect(CandidatesRedirect(examId, "error=assign_duplicate")),
            _ => Redirect(CandidatesRedirect(examId, "error=assign_" + result))
        };
    }

    [HttpPost("exam-candidates/reset")]
    [ValidateAntiForgeryToken]
    public IActionResult ResetCandidate(long examId, string email)
    {
        if (!IsStaff) return Redirect("/dashboard");
        _examService.ResetCandidateExamForRetake(CurrentUsername, examId, email);
        return Redirect(CandidatesRedirect(examId, "reset=1"));
    }

    [HttpPost("exam-candidates/unassign")]
    [ValidateAntiForgeryToken]
    public IActionResult UnassignCandidate(long examId, string email)
    {
        if (!IsStaff) return Redirect("/dashboard");
        _examService.UnassignCandidateFromExam(CurrentUsername, examId, email);
        return Redirect(CandidatesRedirect(examId, "unassigned=1"));
    }

    [HttpPost("candidate-accounts")]
    [ValidateAntiForgeryToken]
    public IActionResult CreateCandidate(
        string fullName, string email, string nationalId,
        long? examId = null, bool assignToExam = true)
    {
        if (!IsStaff) return Redirect("/dashboard");
        try
        {
            _examService.CreateCandidateAccount(fullName, email, nationalId);
            if (assignToExam && examId != null && _examService.CanManageExam(CurrentUsername, examId.Value))
            {
                var result = _examService.AllowCandidateForExamByStaff(CurrentUsername, examId.Value, email, fullName, nationalId);
                if (result == "ok")
                    return Redirect(CandidatesRedirect(examId, "candidate_added=1&assigned=1"));
            }

            return Redirect(CandidatesRedirect(examId, "candidate_added=1"));
        }
        catch (ArgumentException ex)
        {
            return Redirect(CandidatesRedirect(examId, "error=" + (ex.Message.Contains("email", StringComparison.OrdinalIgnoreCase) ? "candidate_email" : "candidate_invalid")));
        }
    }

    [HttpPost("attempts/{id:long}/delete")]
    [ValidateAntiForgeryToken]
    public IActionResult DeleteAttempt(long id, long? examId, string? back)
    {
        var attemptId = id;
        if (!IsStaff || !_examService.CanManageAttempt(CurrentUsername, attemptId))
            return Redirect("/admin/reports");
        _examService.DeleteAttemptById(attemptId);
        if (string.Equals(back, "candidates", StringComparison.OrdinalIgnoreCase) && examId != null)
            return Redirect(CandidatesRedirect(examId, "reset=1"));
        return Redirect($"/admin/reports?tab=results&examId={examId}&reset=1");
    }

    // ── Users / departments ─────────────────────────────────────────

    [Authorize(Roles = "ADMIN")]
    [HttpGet("users")]
    public async Task<IActionResult> Users(string? view)
    {
        var usersView = string.IsNullOrWhiteSpace(view) ? "add" : view.Trim();
        if (usersView is not ("add" or "browse")) usersView = "add";

        ViewBag.UsersView = usersView;
        ViewBag.CurrentUsername = CurrentUsername;
        var departments = await _db.Departments.ToListAsync();
        ViewBag.Departments = departments;

        var examiners = await _db.Users.Include(u => u.Department)
            .Where(u => u.Role == Role.EXAMINER).ToListAsync();
        ViewBag.Examiners = examiners;

        var examinerCountByDept = new Dictionary<long, int>();
        var firstExaminerIdByDept = new Dictionary<long, long>();
        foreach (var d in departments)
        {
            var deptExaminers = examiners.Where(e => e.DepartmentId == d.Id).ToList();
            examinerCountByDept[d.Id] = deptExaminers.Count;
            if (deptExaminers.Count > 0)
                firstExaminerIdByDept[d.Id] = deptExaminers[0].Id;
        }

        ViewBag.ExaminerCountByDept = examinerCountByDept;
        ViewBag.FirstExaminerIdByDept = firstExaminerIdByDept;
        ViewData["ActiveNav"] = "users";
        return View("~/Views/Admin/Users.cshtml");
    }

    [Authorize(Roles = "ADMIN")]
    [HttpPost("departments")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateDepartment(string name, string? city, string? sector)
    {
        name = (name ?? "").Trim();
        if (name.Length > 0 && !await _db.Departments.AnyAsync(d => d.Name == name))
        {
            _db.Departments.Add(new Department { Name = name, City = city, Sector = sector });
            await _db.SaveChangesAsync();
        }

        return Redirect("/admin/users?view=add");
    }

    [Authorize(Roles = "ADMIN")]
    [HttpPost("departments/{id:long}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteDepartment(long id)
    {
        var departmentId = id;
        if (await _db.Exams.AnyAsync(e => e.DepartmentId == departmentId))
            return Redirect("/admin/users?view=browse&msg=dept_in_use");

        var users = await _db.Users.Where(u => u.DepartmentId == departmentId).ToListAsync();
        foreach (var u in users)
            u.DepartmentId = null;
        var dept = await _db.Departments.FindAsync(departmentId);
        if (dept != null) _db.Departments.Remove(dept);
        await _db.SaveChangesAsync();
        return Redirect("/admin/users?view=browse");
    }

    [Authorize(Roles = "ADMIN")]
    [HttpPost("departments/assign-examiner")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AssignExaminer(long departmentId, long examinerUserId)
    {
        var examiner = await _db.Users.FirstOrDefaultAsync(u => u.Id == examinerUserId && u.Role == Role.EXAMINER);
        var dept = await _db.Departments.FindAsync(departmentId);
        if (examiner == null || dept == null)
            return Redirect("/admin/users?view=add&error=link_invalid");
        examiner.DepartmentId = dept.Id;
        examiner.Department = dept;
        await _db.SaveChangesAsync();
        return Redirect("/admin/users?view=browse&examiner_linked=1");
    }

    [Authorize(Roles = "ADMIN")]
    [HttpPost("examiner/{id:long}/unassign-department")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UnassignExaminerDepartment(long id)
    {
        var userId = id;
        var examiner = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId && u.Role == Role.EXAMINER);
        if (examiner != null)
        {
            examiner.DepartmentId = null;
            examiner.Department = null;
            await _db.SaveChangesAsync();
        }

        return Redirect("/admin/users?view=browse");
    }

    [Authorize(Roles = "ADMIN")]
    [HttpPost("users")]
    [ValidateAntiForgeryToken]
    public IActionResult CreateUser(
        string fullName, string email, string nationalId,
        long? departmentId, string role)
    {
        Enum.TryParse<Role>(role, true, out var parsedRole);
        if (parsedRole == Role.CANDIDATE)
            return Redirect("/admin/operations?view=create&tab=candidates");

        try
        {
            if (parsedRole == Role.EXAMINER)
            {
                _examService.CreateStaffUser(fullName, email, nationalId, departmentId, Role.EXAMINER);
                return Redirect("/admin/users?view=add&examiner_added=1");
            }

            if (parsedRole == Role.GRADER)
            {
                _examService.CreateStaffUser(fullName, email, nationalId, null, Role.GRADER);
                return Redirect("/admin/users?view=add&grader_added=1");
            }

            _examService.CreateStaffUser(fullName, email, nationalId, departmentId, parsedRole);
            return Redirect("/admin/users?view=browse&staff_added=1");
        }
        catch (ArgumentException ex)
        {
            var prefix = parsedRole == Role.GRADER ? "grader" : "examiner";
            var err = ex.Message.Contains("email", StringComparison.OrdinalIgnoreCase) ? $"{prefix}_email_taken"
                : ex.Message.Contains("national", StringComparison.OrdinalIgnoreCase) ? $"{prefix}_national_id"
                : $"{prefix}_invalid";
            return Redirect($"/admin/users?view=add&error={err}");
        }
    }

    [Authorize(Roles = "ADMIN")]
    [HttpPost("users/{id:long}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteUser(long id, long? examId)
    {
        var userId = id;
        var user = await _db.Users.FindAsync(userId);
        if (user == null) return Redirect("/admin/users?view=browse");
        if (user.Username == CurrentUsername)
            return Redirect("/admin/users?view=browse");

        if (user.Role == Role.CANDIDATE)
        {
            _examService.DeleteCandidateAccount(CurrentUsername, userId, CurrentUsername);
            return examId != null
                ? Redirect(CandidatesRedirect(examId, null))
                : Redirect("/admin/operations?view=create&tab=candidates");
        }

        _db.Users.Remove(user);
        await _db.SaveChangesAsync();
        return Redirect("/admin/users?view=browse");
    }

    // ── Templates ───────────────────────────────────────────────────

    [HttpGet("templates/question-bank.xlsx")]
    public IActionResult QuestionBankTemplate()
    {
        if (!IsStaff) return StatusCode(403);
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Questions");
        ws.Cell(1, 1).Value = "text";
        ws.Cell(1, 2).Value = "optionA";
        ws.Cell(1, 3).Value = "optionB";
        ws.Cell(1, 4).Value = "optionC";
        ws.Cell(1, 5).Value = "optionD";
        ws.Cell(1, 6).Value = "correctOption";
        ws.Cell(1, 7).Value = "mark";
        ws.Cell(1, 8).Value = "essay";
        ws.Cell(1, 9).Value = "durationSeconds";
        ws.Cell(1, 10).Value = "topic";
        ws.Cell(1, 11).Value = "difficulty";
        ws.Cell(1, 12).Value = "rubric";
        return ExcelFile(wb, "question-bank-template.xlsx");
    }

    [HttpGet("templates/exam-candidates.xlsx")]
    public IActionResult ExamCandidatesTemplate()
    {
        if (!IsStaff) return StatusCode(403);
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Candidates");
        ws.Cell(1, 1).Value = "email";
        ws.Cell(1, 2).Value = "fullName";
        ws.Cell(1, 3).Value = "nationalId";
        return ExcelFile(wb, "exam-candidates-template.xlsx");
    }

    [Authorize(Roles = "ADMIN")]
    [HttpGet("templates/users.xlsx")]
    public IActionResult UsersTemplate()
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Users");
        ws.Cell(1, 1).Value = "email";
        ws.Cell(1, 2).Value = "fullName";
        ws.Cell(1, 3).Value = "nationalId";
        ws.Cell(1, 4).Value = "role";
        ws.Cell(1, 5).Value = "department";
        return ExcelFile(wb, "users-template.xlsx");
    }

    private static FileContentResult ExcelFile(XLWorkbook wb, string fileName)
    {
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return new FileContentResult(
            ms.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")
        {
            FileDownloadName = fileName
        };
    }

}
