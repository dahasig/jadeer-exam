using Jadeer.Exam.Data;
using Jadeer.Exam.Models;
using Jadeer.Exam.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Jadeer.Exam.Controllers;

[Authorize(Roles = "ADMIN,EXAMINER,GRADER")]
[Route("admin")]
public class GradingController : AppControllerBase
{
    private readonly ExamService _examService;
    private readonly AppDbContext _db;

    public GradingController(ExamService examService, AppDbContext db)
    {
        _examService = examService;
        _db = db;
    }

    [HttpGet("grading")]
    public async Task<IActionResult> Grading()
    {
        if (!IsGradingUser)
            return Redirect("/dashboard");

        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Username == CurrentUsername);
        ViewBag.GradingRows = _examService.ListEssayGradingQueue(CurrentUsername);
        ViewBag.PracticalRows = _examService.ListPracticalScoreRows(CurrentUsername);
        ViewBag.GraderOnly = IsGrader && !IsAdmin && !IsExaminer;
        ViewBag.IsGrader = user?.Role == Role.GRADER;
        ViewData["ActiveNav"] = "grading";
        return View("~/Views/Admin/GraderDashboard.cshtml");
    }

    [HttpGet("attempts/{id:long}/grade-essay")]
    public async Task<IActionResult> GradeEssay(long id)
    {
        var attemptId = id;
        if (!_examService.CanManageAttempt(CurrentUsername, attemptId))
            return Redirect("/admin/grading");
        if (!_examService.AttemptHasEssayQuestions(attemptId))
            return Redirect(IsGrader && !IsStaff ? "/admin/grading" : "/admin/operations");

        var attempt = await _db.ExamAttempts.Include(a => a.Exam)
            .FirstOrDefaultAsync(a => a.Id == attemptId);
        if (attempt == null)
            return Redirect("/admin/grading");

        ViewBag.Attempt = attempt;
        ViewBag.EssayRows = _examService.EssayReviewRows(attemptId);
        ViewBag.GraderOnly = IsGrader && !IsAdmin && !IsExaminer;
        ViewData["ActiveNav"] = IsGrader && !IsStaff ? "grading" : "operations";
        return View("~/Views/Admin/EssayGrading.cshtml");
    }

    [HttpPost("attempts/{id:long}/grade-essay")]
    [ValidateAntiForgeryToken]
    public IActionResult GradeEssay(long id, IFormCollection form)
    {
        var attemptId = id;
        if (!_examService.CanManageAttempt(CurrentUsername, attemptId))
            return Redirect("/admin/grading");

        var scores = new Dictionary<long, int>();
        foreach (var key in form.Keys)
        {
            if (key == null || !key.StartsWith("score_", StringComparison.Ordinal))
                continue;
            if (!long.TryParse(key["score_".Length..], out var qid))
                continue;
            if (int.TryParse(form[key], out var score))
                scores[qid] = score;
        }

        _examService.GradeEssayAttempt(attemptId, scores);
        return Redirect("/admin/grading?graded=1");
    }

    [HttpPost("attempts/{id:long}/practical-score")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> PracticalScore(long id, int? practicalScore, string? after)
    {
        var attemptId = id;
        if (!_examService.CanManageAttempt(CurrentUsername, attemptId))
            return Redirect("/admin/grading?error=practical");

        try
        {
            _examService.SetPracticalScore(CurrentUsername, attemptId, practicalScore);
        }
        catch (ArgumentException)
        {
            return Redirect("/admin/grading?error=practical");
        }

        if (string.Equals(after, "candidates", StringComparison.OrdinalIgnoreCase))
        {
            var attempt = await _db.ExamAttempts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == attemptId);
            var examId = attempt?.ExamId;
            if (examId != null)
                return Redirect($"/admin/operations?view=create&tab=candidates&examId={examId}&practical_graded=1");
        }

        return Redirect("/admin/grading?practical_graded=1");
    }
}
