using Jadeer.Exam.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jadeer.Exam.Controllers;

[Authorize]
public class HomeController : AppControllerBase
{
    private readonly ExamService _examService;

    public HomeController(ExamService examService)
    {
        _examService = examService;
    }

    [HttpGet("/")]
    [HttpGet("/dashboard")]
    [HttpGet("/Home/Dashboard")]
    [HttpGet("/Home/Index")]
    public IActionResult Index()
    {
        if (IsCandidate)
        {
            ViewBag.Exams = _examService.ListAvailableExamsForCandidate(CurrentUsername);
            return View("~/Views/Home/Dashboard.cshtml");
        }

        if (IsGrader)
            return Redirect("/admin/grading");

        if (IsStaff)
            return Redirect("/admin/operations");

        return Redirect("/login");
    }

    [HttpGet("/Home/Error")]
    [AllowAnonymous]
    public IActionResult Error() => Content("An error occurred.");

    // Alias used by tag helpers / conventional links
    public IActionResult Dashboard() => Index();
}
