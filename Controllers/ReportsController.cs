using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using Jadeer.Exam.Models;
using Jadeer.Exam.Security;
using Jadeer.Exam.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Jadeer.Exam.Controllers;

/// <summary>
/// Report routes under /admin/reports (DashboardController.java equivalents).
/// </summary>
[Authorize(Roles = "ADMIN,EXAMINER")]
[Route("admin")]
public class ReportsController : AppControllerBase
{
    private readonly ExamService _examService;
    private readonly MonitoringService _monitoringService;
    private readonly AnalyticsService _analyticsService;

    public ReportsController(
        ExamService examService,
        MonitoringService monitoringService,
        AnalyticsService analyticsService)
    {
        _examService = examService;
        _monitoringService = monitoringService;
        _analyticsService = analyticsService;
        QuestPDF.Settings.License = LicenseType.Community;
    }

    [HttpGet("reports")]
    public IActionResult Reports(
        string? tab,
        string? username,
        string? severity,
        string? eventType,
        string? from,
        string? to,
        string? lifecycle,
        long? examId)
    {
        if (!IsStaff)
            return Redirect("/dashboard");

        var fromTs = ParseDateTime(from);
        var toTs = ParseDateTime(to);

        Dictionary<string, long> kpis;
        if (_examService.IsAdmin(CurrentUsername))
        {
            kpis = new Dictionary<string, long>
            {
                ["exams"] = _examService.ListExams().Count,
                ["attempts"] = _examService.ListAttempts().Count,
                ["alerts"] = _monitoringService.LatestEvents().Count,
                ["activeSessions"] = Math.Max(1, _examService.ListAttempts().Count / 2)
            };
        }
        else
        {
            kpis = new Dictionary<string, long>
            {
                ["exams"] = _examService.ListExamsForStaff(CurrentUsername).Count,
                ["attempts"] = _examService.ListAttemptsForStaff(CurrentUsername).Count,
                ["alerts"] = _monitoringService.LatestEventsForStaff(CurrentUsername).Count,
                ["activeSessions"] = Math.Max(1, _examService.ListAttemptsForStaff(CurrentUsername).Count / 2)
            };
        }

        ViewBag.Kpis = kpis;

        var rawFiltered = _monitoringService.FilteredEventsForStaff(
            CurrentUsername, username, severity, eventType, fromTs, toTs);
        if (examId != null)
        {
            rawFiltered = rawFiltered
                .Where(e => examId.Equals(e.ExamId))
                .Where(e =>
                {
                    var sev = e.Severity;
                    return sev != null && (sev.Equals("CRITICAL", StringComparison.OrdinalIgnoreCase)
                                           || sev.Equals("HIGH", StringComparison.OrdinalIgnoreCase));
                })
                .ToList();
        }

        ViewBag.Events = rawFiltered.Select(e => new Dictionary<string, object?>
        {
            ["username"] = e.Username,
            ["eventType"] = e.EventType,
            ["severity"] = e.Severity,
            ["details"] = e.Details,
            ["eventTime"] = e.EventTime,
            ["snapshotFileName"] = ExtractSnapshotFileName(e.Details),
            ["videoFileName"] = ExtractVideoFileName(e.Details),
            ["examId"] = e.ExamId
        }).ToList();

        ViewBag.FilterUsername = username ?? "";
        ViewBag.FilterSeverity = severity ?? "";
        ViewBag.FilterEventType = eventType ?? "";
        ViewBag.FilterFrom = from ?? "";
        ViewBag.FilterTo = to ?? "";
        ViewBag.RiskSummaries = new List<object>();

        var staffExams = _examService.ListExamsForStaff(CurrentUsername);
        var allReportExams = staffExams.ToList();
        if (!string.IsNullOrWhiteSpace(lifecycle))
        {
            staffExams = staffExams
                .Where(e => lifecycle.Equals(_examService.LifecycleStatus(e.Id), StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        ViewBag.ReportExams = staffExams;
        ViewBag.AllReportExams = allReportExams;
        ViewBag.LifecycleFilter = lifecycle ?? "";
        ViewBag.SelectedExamId = examId;
        ViewBag.ReportOverview = _examService.ReportOverviewForStaff(CurrentUsername, examId, fromTs, toTs);
        ViewBag.PeriodAnalytics = _analyticsService.PeriodReportForStaff(CurrentUsername, fromTs, toTs, examId, lifecycle);
        ViewBag.ExamSummaries = _examService.ExamSummariesForStaff(CurrentUsername, lifecycle);

        var pendingEssayCount = _examService.ListAttemptsForStaff(CurrentUsername)
            .Count(a => _examService.AttemptNeedsEssayGrading(a.Id));
        var criticalTodayCount = _monitoringService.FilteredEventsForStaff(
                CurrentUsername, "", "", "", fromTs, toTs)
            .Count(e => "CRITICAL".Equals(e.Severity, StringComparison.OrdinalIgnoreCase)
                        || "HIGH".Equals(e.Severity, StringComparison.OrdinalIgnoreCase));
        ViewBag.PendingEssayCount = pendingEssayCount;
        ViewBag.CriticalTodayCount = criticalTodayCount;

        ViewBag.ExamResultRows = _examService.ListAttemptsForStaff(CurrentUsername)
            .Where(a => examId == null || (a.Exam != null && examId == a.Exam.Id))
            .Where(a => fromTs == null && toTs == null
                        || (a.SubmittedAt != null && InReportPeriod(a.SubmittedAt, fromTs, toTs))
                        || InReportPeriod(a.StartedAt, fromTs, toTs))
            .Select(a =>
            {
                long? exId = a.Exam?.Id;
                var threshold = a.Exam?.PassThreshold > 0 ? a.Exam.PassThreshold : 70;
                return new Dictionary<string, object?>
                {
                    ["username"] = a.CandidateUsername,
                    ["examTitle"] = a.Exam?.Title ?? "-",
                    ["examId"] = exId,
                    ["score"] = a.Score,
                    ["passThreshold"] = threshold,
                    ["attemptId"] = a.Id,
                    ["submittedAt"] = a.SubmittedAt,
                    ["status"] = a.SubmittedAt != null ? "مكتمل" : "جارية",
                    ["notes"] = exId == null
                        ? "لا توجد ملاحظات"
                        : _monitoringService.NotesSummaryForExamCandidate(exId.Value, a.CandidateUsername)
                };
            }).ToList();

        var defaultTab = string.IsNullOrWhiteSpace(tab) ? "summary" : tab.Trim();
        if (defaultTab is not ("summary" or "results" or "events" or "stats"))
            defaultTab = "summary";
        ViewBag.DefaultTab = defaultTab;
        ViewData["ActiveNav"] = "reports";
        return View("~/Views/Admin/Reports.cshtml");
    }

    [HttpGet("reports/exam/{id:long}")]
    public IActionResult ExamReport([FromRoute(Name = "id")] long examId)
    {
        if (!IsStaff || !_examService.CanManageExam(CurrentUsername, examId))
            return Redirect("/dashboard");

        var exam = _examService.GetExam(examId);
        if (exam == null) return NotFound();

        var rows = _examService.ListAttemptsForStaff(CurrentUsername)
            .Where(a => a.SubmittedAt != null && a.Exam != null && examId == a.Exam.Id)
            .Select(a => new Dictionary<string, object?>
            {
                ["username"] = a.CandidateUsername,
                ["score"] = a.Score,
                ["objectiveScore"] = a.ObjectiveScore,
                ["essayScore"] = a.EssayScore,
                ["practicalScore"] = a.PracticalScore,
                ["passThreshold"] = a.Exam?.PassThreshold > 0 ? a.Exam.PassThreshold : 70,
                ["submittedAt"] = a.SubmittedAt,
                ["notes"] = _monitoringService.NotesSummaryForExamCandidate(examId, a.CandidateUsername)
            }).ToList();

        var rawEvents = _monitoringService.FilteredEventsForStaff(CurrentUsername, "", "", "", null, null)
            .Where(e => e.ExamId != null && examId == e.ExamId)
            .OrderBy(e => e.EventTime)
            .ToList();

        var events = rawEvents.Select(e => new
        {
            SeverityLabel = _monitoringService.ToArabicSeverity(e.Severity),
            EventTypeLabel = _monitoringService.ToArabicEventType(e.EventType),
            e.Username,
            e.Details,
            e.EventTime,
            SnapshotFileName = ExtractSnapshotFileName(e.Details),
            VideoFileName = ExtractVideoFileName(e.Details),
            e.ConfidenceScore
        }).ToList();

        var snapshotGroups = rawEvents
            .Where(e => !string.IsNullOrWhiteSpace(e.Username))
            .Select(e =>
            {
                var fileName = ExtractSnapshotFileName(e.Details);
                if (fileName == null) return null;
                return new Dictionary<string, object?>
                {
                    ["fileName"] = fileName,
                    ["videoFileName"] = ExtractVideoFileName(e.Details),
                    ["eventTime"] = e.EventTime,
                    ["eventTypeLabel"] = _monitoringService.ToArabicEventType(e.EventType),
                    ["details"] = e.Details,
                    ["username"] = e.Username
                };
            })
            .Where(x => x != null)
            .GroupBy(x => (string)x!["username"]!)
            .ToDictionary(g => g.Key, g => g.ToList());

        ViewBag.Exam = exam;
        ViewBag.ExamResultRows = rows;
        ViewBag.Events = events;
        ViewBag.SnapshotGroups = snapshotGroups;
        ViewBag.ReportExams = _examService.ListExamsForStaff(CurrentUsername);
        ViewData["ActiveNav"] = "reports";
        return View("~/Views/Admin/ExamReport.cshtml");
    }

    [HttpGet("reports/exam/{id:long}/answers")]
    public IActionResult CandidateAnswers([FromRoute(Name = "id")] long examId)
    {
        if (!IsStaff || !_examService.CanManageExam(CurrentUsername, examId))
            return Redirect("/dashboard");

        var exam = _examService.GetExam(examId);
        if (exam == null) return NotFound();

        ViewBag.Exam = exam;
        ViewBag.CandidateAnswerRows = _examService.CandidateAnswersReport(CurrentUsername, examId);
        ViewData["ActiveNav"] = "reports";
        ViewData["Title"] = "إجابات المرشحين";
        return View("~/Views/Admin/CandidateAnswers.cshtml");
    }

    [HttpGet("reports/exam/{id:long}/answers.xlsx")]
    public IActionResult ExportCandidateAnswersExcel([FromRoute(Name = "id")] long examId)
    {
        if (!IsStaff || !_examService.CanManageExam(CurrentUsername, examId))
            return StatusCode(403, "forbidden");

        var exam = _examService.GetExam(examId);
        if (exam == null) return NotFound();

        var candidates = _examService.CandidateAnswersReport(CurrentUsername, examId);
        using var workbook = new XLWorkbook();
        var summary = workbook.Worksheets.Add("Candidates");
        summary.Cell(1, 1).Value = "#";
        summary.Cell(1, 2).Value = "Username";
        summary.Cell(1, 3).Value = "Full name";
        summary.Cell(1, 4).Value = "National ID";
        summary.Cell(1, 5).Value = "Status";
        summary.Cell(1, 6).Value = "Objective";
        summary.Cell(1, 7).Value = "Essay";
        summary.Cell(1, 8).Value = "Practical";
        summary.Cell(1, 9).Value = "Total";
        summary.Cell(1, 10).Value = "Submitted at";
        var sr = 2;
        foreach (var c in candidates)
        {
            summary.Cell(sr, 1).Value = Convert.ToInt32(c.GetValueOrDefault("index") ?? 0);
            summary.Cell(sr, 2).Value = Convert.ToString(c.GetValueOrDefault("username"));
            summary.Cell(sr, 3).Value = Convert.ToString(c.GetValueOrDefault("fullName"));
            summary.Cell(sr, 4).Value = Convert.ToString(c.GetValueOrDefault("nationalId"));
            summary.Cell(sr, 5).Value = Convert.ToString(c.GetValueOrDefault("status"));
            summary.Cell(sr, 6).Value = Convert.ToString(c.GetValueOrDefault("objectiveScore"));
            summary.Cell(sr, 7).Value = Convert.ToString(c.GetValueOrDefault("essayScore"));
            summary.Cell(sr, 8).Value = Convert.ToString(c.GetValueOrDefault("practicalScore"));
            summary.Cell(sr, 9).Value = Convert.ToString(c.GetValueOrDefault("totalScore"));
            summary.Cell(sr, 10).Value = (c.GetValueOrDefault("submittedAt") as DateTime?)?.ToString("yyyy-MM-dd HH:mm") ?? "";
            sr++;
        }
        summary.Columns().AdjustToContents();

        var detail = workbook.Worksheets.Add("Full answers");
        detail.Cell(1, 1).Value = "Candidate";
        detail.Cell(1, 2).Value = "Full name";
        detail.Cell(1, 3).Value = "Q#";
        detail.Cell(1, 4).Value = "Type";
        detail.Cell(1, 5).Value = "Question";
        detail.Cell(1, 6).Value = "Answer (full text)";
        detail.Cell(1, 7).Value = "Correct option";
        detail.Cell(1, 8).Value = "Is correct";
        detail.Cell(1, 9).Value = "Marks";
        detail.Cell(1, 10).Value = "Awarded";
        var dr = 2;
        foreach (var c in candidates)
        {
            var username = Convert.ToString(c.GetValueOrDefault("username")) ?? "";
            var fullName = Convert.ToString(c.GetValueOrDefault("fullName")) ?? "";
            var answers = c.GetValueOrDefault("answers") as List<Dictionary<string, object?>> ?? [];
            foreach (var a in answers)
            {
                detail.Cell(dr, 1).Value = username;
                detail.Cell(dr, 2).Value = fullName;
                detail.Cell(dr, 3).Value = Convert.ToString(a.GetValueOrDefault("number"));
                detail.Cell(dr, 4).Value = Convert.ToString(a.GetValueOrDefault("type"));
                detail.Cell(dr, 5).Value = Convert.ToString(a.GetValueOrDefault("text"));
                detail.Cell(dr, 6).Value = Convert.ToString(a.GetValueOrDefault("answerLabel"));
                detail.Cell(dr, 7).Value = Convert.ToString(a.GetValueOrDefault("correctLabel"));
                var isCorrect = a.GetValueOrDefault("isCorrect");
                detail.Cell(dr, 8).Value = isCorrect == null ? "" : (Convert.ToBoolean(isCorrect) ? "Yes" : "No");
                detail.Cell(dr, 9).Value = Convert.ToString(a.GetValueOrDefault("mark"));
                detail.Cell(dr, 10).Value = Convert.ToString(a.GetValueOrDefault("awarded"));
                dr++;
            }
        }
        detail.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return File(stream.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"exam-{examId}-candidate-answers.xlsx");
    }

    [HttpGet("reports/attempt/{id:long}/timeline")]
    public IActionResult AttemptTimeline([FromRoute(Name = "id")] long attemptId)
    {
        var attempt = _examService.ListAttemptsForStaff(CurrentUsername)
            .FirstOrDefault(a => a.Id == attemptId);
        if (attempt?.Exam == null || !_examService.CanManageExam(CurrentUsername, attempt.Exam.Id))
            return Redirect("/admin/reports");

        var examId = attempt.Exam.Id;
        var username = attempt.CandidateUsername;
        var timeline = _monitoringService.FilteredEventsForStaff(CurrentUsername, username, "", "", null, null)
            .Where(e => e.ExamId != null && examId == e.ExamId)
            .OrderBy(e => e.EventTime)
            .Select(e => new Dictionary<string, object?>
            {
                ["eventTime"] = e.EventTime,
                ["severityLabel"] = _monitoringService.ToArabicSeverity(e.Severity),
                ["eventTypeLabel"] = _monitoringService.ToArabicEventType(e.EventType),
                ["details"] = e.Details,
                ["confidenceScore"] = e.ConfidenceScore,
                ["snapshotFileName"] = ExtractSnapshotFileName(e.Details),
                ["videoFileName"] = ExtractVideoFileName(e.Details)
            }).ToList();

        ViewBag.Attempt = attempt;
        ViewBag.TimelineRows = timeline;
        ViewData["ActiveNav"] = "reports";
        return View("~/Views/Admin/AttemptTimeline.cshtml");
    }

    [HttpGet("reports/exam/{id:long}/export.csv")]
    public IActionResult ExportExamCsv([FromRoute(Name = "id")] long examId)
    {
        if (!IsStaff || !_examService.CanManageExam(CurrentUsername, examId))
            return StatusCode(403, "forbidden");

        var rows = _examService.ListAttemptsForStaff(CurrentUsername)
            .Where(a => a.SubmittedAt != null && a.Exam != null && examId == a.Exam.Id)
            .ToList();

        var csv = new StringBuilder("candidate,objective,essay,practical,total,submittedAt,notes\n");
        foreach (var a in rows)
        {
            csv.Append(CsvField(a.CandidateUsername)).Append(',')
                .Append(CsvField(a.ObjectiveScore.ToString())).Append(',')
                .Append(CsvField(a.EssayScore.ToString())).Append(',')
                .Append(CsvField(a.PracticalScore?.ToString() ?? "")).Append(',')
                .Append(CsvField(a.Score?.ToString() ?? "")).Append(',')
                .Append(CsvField(a.SubmittedAt?.ToString() ?? "")).Append(',')
                .Append(CsvField(_monitoringService.NotesSummaryForExamCandidate(examId, a.CandidateUsername ?? "")))
                .Append('\n');
        }

        return File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv", $"exam-{examId}-results.csv");
    }

    [HttpGet("reports/export.csv")]
    public IActionResult ExportReportsCsv(string? username, string? severity, string? eventType, string? from, string? to)
    {
        if (!IsStaff) return StatusCode(403, "forbidden");
        var events = _monitoringService.FilteredEventsForStaff(
            CurrentUsername, username, severity, eventType, ParseDateTime(from), ParseDateTime(to));
        var csv = _monitoringService.ToCsv(events);
        return File(Encoding.UTF8.GetBytes(csv), "text/csv", "reports.csv");
    }

    [HttpGet("reports/analytics/export.csv")]
    public IActionResult ExportAnalyticsCsv(string? from, string? to, string? lifecycle, long? examId)
    {
        if (!IsStaff) return StatusCode(403, "forbidden");
        var report = _analyticsService.PeriodReportForStaff(
            CurrentUsername, ParseDateTime(from), ParseDateTime(to), examId, lifecycle);
        var examRows = report.TryGetValue("examRows", out var er) ? er as List<Dictionary<string, object?>> : null;
        var csv = new StringBuilder("exam,department,assigned,completed,passRate,averageScore\n");
        if (examRows != null)
        {
            foreach (var row in examRows)
            {
                csv.Append(CsvField(Convert.ToString(row.GetValueOrDefault("examTitle")))).Append(',')
                    .Append(CsvField(Convert.ToString(row.GetValueOrDefault("departmentName")))).Append(',')
                    .Append(CsvField(Convert.ToString(row.GetValueOrDefault("assignedCandidates")))).Append(',')
                    .Append(CsvField(Convert.ToString(row.GetValueOrDefault("completedAttempts")))).Append(',')
                    .Append(CsvField(Convert.ToString(row.GetValueOrDefault("passRate")))).Append(',')
                    .Append(CsvField(Convert.ToString(row.GetValueOrDefault("averageScore"))))
                    .Append('\n');
            }
        }

        return File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv", "period-analytics.csv");
    }

    [HttpGet("reports/analytics/export.xlsx")]
    public IActionResult ExportAnalyticsExcel(string? from, string? to, string? lifecycle, long? examId)
    {
        if (!IsStaff) return StatusCode(403, "forbidden");
        var report = _analyticsService.PeriodReportForStaff(
            CurrentUsername, ParseDateTime(from), ParseDateTime(to), examId, lifecycle);

        using var workbook = new XLWorkbook();
        var summary = workbook.Worksheets.Add("Summary");
        summary.Cell(1, 1).Value = "Metric";
        summary.Cell(1, 2).Value = "Value";
        var metrics = new (string Label, string Key)[]
        {
            ("Period", "periodLabel"),
            ("Exams conducted", "examsConducted"),
            ("Exams created", "examsCreated"),
            ("Departments", "departmentsBenefited"),
            ("Unique candidates", "uniqueCandidates"),
            ("Total assignments", "totalAssignments"),
            ("Completed attempts", "completedAttempts"),
            ("Started attempts", "startedAttempts"),
            ("Pass rate %", "passRate"),
            ("Average score", "averageScore"),
            ("Passed", "passCount"),
            ("Failed", "failCount")
        };
        for (var i = 0; i < metrics.Length; i++)
        {
            summary.Cell(i + 2, 1).Value = metrics[i].Label;
            summary.Cell(i + 2, 2).Value = Convert.ToString(report.GetValueOrDefault(metrics[i].Key)) ?? "";
        }
        summary.Columns().AdjustToContents();

        var examsSheet = workbook.Worksheets.Add("By exam");
        examsSheet.Cell(1, 1).Value = "Exam";
        examsSheet.Cell(1, 2).Value = "Department";
        examsSheet.Cell(1, 3).Value = "Assigned";
        examsSheet.Cell(1, 4).Value = "Completed";
        examsSheet.Cell(1, 5).Value = "Pass rate %";
        examsSheet.Cell(1, 6).Value = "Average score";
        examsSheet.Cell(1, 7).Value = "Lifecycle";
        var examRows = report.TryGetValue("examRows", out var er) ? er as List<Dictionary<string, object?>> : null;
        var r = 2;
        if (examRows != null)
        {
            foreach (var row in examRows)
            {
                examsSheet.Cell(r, 1).Value = Convert.ToString(row.GetValueOrDefault("examTitle"));
                examsSheet.Cell(r, 2).Value = Convert.ToString(row.GetValueOrDefault("departmentName"));
                examsSheet.Cell(r, 3).Value = Convert.ToString(row.GetValueOrDefault("assignedCandidates"));
                examsSheet.Cell(r, 4).Value = Convert.ToString(row.GetValueOrDefault("completedAttempts"));
                examsSheet.Cell(r, 5).Value = Convert.ToString(row.GetValueOrDefault("passRate"));
                examsSheet.Cell(r, 6).Value = Convert.ToString(row.GetValueOrDefault("averageScore"));
                examsSheet.Cell(r, 7).Value = Convert.ToString(row.GetValueOrDefault("lifecycle"));
                r++;
            }
        }
        examsSheet.Columns().AdjustToContents();

        var deptSheet = workbook.Worksheets.Add("By department");
        deptSheet.Cell(1, 1).Value = "Department";
        deptSheet.Cell(1, 2).Value = "Exams";
        deptSheet.Cell(1, 3).Value = "Assigned";
        deptSheet.Cell(1, 4).Value = "Completed";
        deptSheet.Cell(1, 5).Value = "Pass rate %";
        var deptRows = report.TryGetValue("departmentRows", out var dr) ? dr as List<Dictionary<string, object?>> : null;
        r = 2;
        if (deptRows != null)
        {
            foreach (var row in deptRows)
            {
                deptSheet.Cell(r, 1).Value = Convert.ToString(row.GetValueOrDefault("departmentName"));
                deptSheet.Cell(r, 2).Value = Convert.ToString(row.GetValueOrDefault("examCount"));
                deptSheet.Cell(r, 3).Value = Convert.ToString(row.GetValueOrDefault("assignedCandidates"));
                deptSheet.Cell(r, 4).Value = Convert.ToString(row.GetValueOrDefault("completedAttempts"));
                deptSheet.Cell(r, 5).Value = Convert.ToString(row.GetValueOrDefault("passRate"));
                r++;
            }
        }
        deptSheet.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return File(stream.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "period-analytics.xlsx");
    }

    [HttpGet("reports/analytics/export.pdf")]
    public IActionResult ExportAnalyticsPdf(string? from, string? to, string? lifecycle, long? examId)
    {
        if (!IsStaff) return StatusCode(403, "forbidden");
        var report = _analyticsService.PeriodReportForStaff(
            CurrentUsername, ParseDateTime(from), ParseDateTime(to), examId, lifecycle);
        var examRows = report.TryGetValue("examRows", out var er)
            ? er as List<Dictionary<string, object?>> ?? []
            : [];

        var pdfBytes = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Margin(30);
                page.Header().Text("Period statistics / إحصائيات الفترة")
                    .SemiBold().FontSize(16);
                page.Content().Column(col =>
                {
                    col.Spacing(8);
                    col.Item().Text($"Period: {report.GetValueOrDefault("periodLabel")}");
                    col.Item().Text($"Exams conducted: {report.GetValueOrDefault("examsConducted")} | Created: {report.GetValueOrDefault("examsCreated")}");
                    col.Item().Text($"Departments: {report.GetValueOrDefault("departmentsBenefited")} | Candidates: {report.GetValueOrDefault("uniqueCandidates")}");
                    col.Item().Text($"Assignments: {report.GetValueOrDefault("totalAssignments")} | Completed: {report.GetValueOrDefault("completedAttempts")} | Started: {report.GetValueOrDefault("startedAttempts")}");
                    col.Item().Text($"Pass rate: {Fmt(report.GetValueOrDefault("passRate"))}% | Avg score: {Fmt(report.GetValueOrDefault("averageScore"))} | Pass: {report.GetValueOrDefault("passCount")} | Fail: {report.GetValueOrDefault("failCount")}");
                    col.Item().PaddingTop(10).Text("By exam").SemiBold();
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn(3);
                            c.RelativeColumn(2);
                            c.RelativeColumn(1);
                            c.RelativeColumn(1);
                            c.RelativeColumn(1);
                            c.RelativeColumn(1);
                        });
                        table.Header(h =>
                        {
                            h.Cell().Text("Exam").SemiBold();
                            h.Cell().Text("Dept").SemiBold();
                            h.Cell().Text("Assigned").SemiBold();
                            h.Cell().Text("Done").SemiBold();
                            h.Cell().Text("Pass%").SemiBold();
                            h.Cell().Text("Avg").SemiBold();
                        });
                        foreach (var row in examRows)
                        {
                            table.Cell().Text(Convert.ToString(row.GetValueOrDefault("examTitle")) ?? "");
                            table.Cell().Text(Convert.ToString(row.GetValueOrDefault("departmentName")) ?? "");
                            table.Cell().Text(Convert.ToString(row.GetValueOrDefault("assignedCandidates")) ?? "");
                            table.Cell().Text(Convert.ToString(row.GetValueOrDefault("completedAttempts")) ?? "");
                            table.Cell().Text(Fmt(row.GetValueOrDefault("passRate")));
                            table.Cell().Text(Fmt(row.GetValueOrDefault("averageScore")));
                        }
                    });
                });
                page.Footer().AlignCenter().Text(t =>
                {
                    t.Span("Jadeer Exam — ");
                    t.CurrentPageNumber();
                    t.Span(" / ");
                    t.TotalPages();
                });
            });
        }).GeneratePdf();

        return File(pdfBytes, "application/pdf", "period-analytics.pdf");
    }

    [HttpGet("reports/exam/{id:long}/export.xlsx")]
    public IActionResult ExportExamExcel([FromRoute(Name = "id")] long examId)
    {
        if (!IsStaff || !_examService.CanManageExam(CurrentUsername, examId))
            return StatusCode(403, "forbidden");

        var exam = _examService.GetExam(examId);
        if (exam == null) return NotFound();

        var rows = _examService.ListAttemptsForStaff(CurrentUsername)
            .Where(a => a.SubmittedAt != null && a.Exam != null && examId == a.Exam.Id)
            .ToList();

        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Results");
        sheet.Cell(1, 1).Value = "Candidate";
        sheet.Cell(1, 2).Value = "Objective";
        sheet.Cell(1, 3).Value = "Essay";
        sheet.Cell(1, 4).Value = "Practical";
        sheet.Cell(1, 5).Value = "Total";
        sheet.Cell(1, 6).Value = "Submitted at";
        sheet.Cell(1, 7).Value = "Notes";
        var r = 2;
        foreach (var a in rows)
        {
            sheet.Cell(r, 1).Value = a.CandidateUsername;
            sheet.Cell(r, 2).Value = a.ObjectiveScore;
            sheet.Cell(r, 3).Value = a.EssayScore;
            sheet.Cell(r, 4).Value = a.PracticalScore;
            sheet.Cell(r, 5).Value = a.Score;
            sheet.Cell(r, 6).Value = a.SubmittedAt?.ToString("yyyy-MM-dd HH:mm") ?? "";
            sheet.Cell(r, 7).Value = _monitoringService.NotesSummaryForExamCandidate(examId, a.CandidateUsername ?? "");
            r++;
        }
        sheet.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return File(stream.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"exam-{examId}-results.xlsx");
    }

    [HttpGet("reports/exam/{id:long}/export.pdf")]
    public IActionResult ExportExamPdf([FromRoute(Name = "id")] long examId)
    {
        if (!IsStaff || !_examService.CanManageExam(CurrentUsername, examId))
            return StatusCode(403, "forbidden");

        var exam = _examService.GetExam(examId);
        if (exam == null) return NotFound();

        var rows = _examService.ListAttemptsForStaff(CurrentUsername)
            .Where(a => a.SubmittedAt != null && a.Exam != null && examId == a.Exam.Id)
            .ToList();

        var pdfBytes = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Margin(30);
                page.Header().Text(exam.Title ?? $"Exam {examId}").SemiBold().FontSize(16);
                page.Content().Table(table =>
                {
                    table.ColumnsDefinition(c =>
                    {
                        c.RelativeColumn(2);
                        c.RelativeColumn(1);
                        c.RelativeColumn(1);
                        c.RelativeColumn(1);
                        c.RelativeColumn(1);
                        c.RelativeColumn(2);
                    });
                    table.Header(h =>
                    {
                        h.Cell().Text("Candidate").SemiBold();
                        h.Cell().Text("Objective").SemiBold();
                        h.Cell().Text("Essay").SemiBold();
                        h.Cell().Text("Practical").SemiBold();
                        h.Cell().Text("Total").SemiBold();
                        h.Cell().Text("Submitted").SemiBold();
                    });
                    foreach (var a in rows)
                    {
                        table.Cell().Text(a.CandidateUsername ?? "");
                        table.Cell().Text(a.ObjectiveScore.ToString());
                        table.Cell().Text(a.EssayScore.ToString());
                        table.Cell().Text(a.PracticalScore?.ToString() ?? "");
                        table.Cell().Text(a.Score?.ToString() ?? "");
                        table.Cell().Text(a.SubmittedAt?.ToString("yyyy-MM-dd HH:mm") ?? "");
                    }
                });
                page.Footer().AlignCenter().Text(t =>
                {
                    t.Span("Jadeer Exam — ");
                    t.CurrentPageNumber();
                    t.Span(" / ");
                    t.TotalPages();
                });
            });
        }).GeneratePdf();

        return File(pdfBytes, "application/pdf", $"exam-{examId}-results.pdf");
    }

    private static string Fmt(object? value) =>
        value == null || string.IsNullOrWhiteSpace(Convert.ToString(value)) ? "—" : Convert.ToString(value)!;

    [HttpGet("reports/exam/{id:long}/media/view")]
    public IActionResult MediaView(long examId, string fileName, string? username, string? capturedAt)
    {
        if (!IsStaff || !_examService.CanManageExam(CurrentUsername, examId))
            return Redirect("/dashboard");

        var safeName = (fileName ?? "").Trim();
        if (safeName.Length == 0 || safeName.Contains("..", StringComparison.Ordinal)
            || safeName.Contains('/') || safeName.Contains('\\'))
            return Redirect("/admin/reports");

        var snapshotsDir = Path.GetFullPath(Path.Combine("data", "snapshots"));
        string target;
        try
        {
            target = SafePath.ResolveUnder(snapshotsDir, safeName);
        }
        catch
        {
            return Redirect("/admin/reports");
        }

        if (!System.IO.File.Exists(target))
            return Redirect("/admin/reports");

        ViewBag.ExamId = examId;
        ViewBag.FileName = safeName;
        ViewBag.MediaUrl = $"/admin/reports/exam/{examId}/snapshots/{safeName}";
        ViewBag.StampName = username?.Trim() ?? "";
        ViewBag.StampTime = capturedAt?.Trim() ?? "";
        ViewBag.IsVideo = safeName.EndsWith(".webm", StringComparison.OrdinalIgnoreCase);
        ViewData["ActiveNav"] = "reports";
        return View("~/Views/Admin/MediaViewer.cshtml");
    }

    [HttpGet("reports/exam/{id:long}/snapshots/{fileName}")]
    public IActionResult Snapshot(long examId, string fileName)
    {
        if (!IsStaff || !_examService.CanManageExam(CurrentUsername, examId))
            return StatusCode(403);

        var safeName = (fileName ?? "").Trim();
        if (safeName.Length == 0) return NotFound();

        var snapshotsDir = Path.GetFullPath(Path.Combine("data", "snapshots"));
        string target;
        try
        {
            target = SafePath.ResolveUnder(snapshotsDir, safeName);
        }
        catch
        {
            return NotFound();
        }

        if (!System.IO.File.Exists(target))
            return NotFound();

        var contentType = safeName.EndsWith(".webm", StringComparison.OrdinalIgnoreCase)
            ? "video/webm"
            : "image/jpeg";
        if (new FileExtensionContentTypeProvider().TryGetContentType(safeName, out var detected))
            contentType = detected;

        return PhysicalFile(target, contentType);
    }

    private static bool InReportPeriod(DateTime? ts, DateTime? from, DateTime? to)
    {
        if (ts == null) return false;
        if (from == null && to == null) return true;
        if (from != null && ts < from) return false;
        if (to != null && ts > to) return false;
        return true;
    }

    private static DateTime? ParseDateTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var dt))
            return dt;
        if (DateTime.TryParse(value, out dt))
            return dt;
        return null;
    }

    private static string? ExtractVideoFileName(string? details) =>
        ExtractMediaFileName(details, "فيديو توثيق:");

    private static string? ExtractSnapshotFileName(string? details) =>
        ExtractMediaFileName(details, "لقطة مراقبة محفوظة:");

    private static string? ExtractMediaFileName(string? details, string marker)
    {
        if (string.IsNullOrWhiteSpace(details)) return null;
        var idx = details.IndexOf(marker, StringComparison.Ordinal);
        if (idx < 0) return null;
        var tail = details[(idx + marker.Length)..].Trim();
        if (tail.Length == 0) return null;
        var pipe = tail.IndexOf('|');
        if (pipe >= 0) tail = tail[..pipe].Trim();
        var normalized = tail.Replace('\\', '/');
        var slash = normalized.LastIndexOf('/');
        var fileName = slash >= 0 ? normalized[(slash + 1)..] : normalized;
        return string.IsNullOrWhiteSpace(fileName) ? null : fileName;
    }

    private static string CsvField(string? value)
    {
        value ??= "";
        return "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}
