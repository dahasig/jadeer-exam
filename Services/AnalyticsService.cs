using System.Globalization;
using Jadeer.Exam.Data;
using Jadeer.Exam.Models;
using Microsoft.EntityFrameworkCore;

namespace Jadeer.Exam.Services;

using ExamEntity = global::Jadeer.Exam.Models.Exam;

/// <summary>Port of Java AnalyticsService.periodReportForStaff + KPIs.</summary>
public class AnalyticsService
{
    private readonly AppDbContext _db;
    private readonly ExamService _examService;

    public AnalyticsService(AppDbContext db, ExamService examService)
    {
        _db = db;
        _examService = examService;
    }

    public Dictionary<string, object?> PeriodReportForStaff(
        string staffUsername,
        DateTime? from,
        DateTime? to,
        long? examIdFilter,
        string? lifecycleFilter)
    {
        var exams = _examService.ListExamsForStaff(staffUsername)
            .Where(e => examIdFilter == null || e.Id == examIdFilter)
            .Where(e => string.IsNullOrWhiteSpace(lifecycleFilter)
                        || string.Equals(lifecycleFilter, _examService.LifecycleStatus(e.Id), StringComparison.OrdinalIgnoreCase))
            .ToList();

        var examById = exams.ToDictionary(e => e.Id);
        var examIds = exams.Select(e => e.Id).ToList();

        var attempts = examIds.Count == 0
            ? new List<ExamAttempt>()
            : _examService.DedupeAttemptsByCandidate(
                _db.ExamAttempts.Include(a => a.Exam).ThenInclude(ex => ex!.Department)
                    .Where(a => a.ExamId != null && examIds.Contains(a.ExamId.Value))
                    .OrderByDescending(a => a.StartedAt)
                    .ToList());

        var conductedExamIds = new LinkedHashSet<long>();
        var activeExamIds = new LinkedHashSet<long>();
        var uniqueCandidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var completedInPeriod = 0;
        var startedInPeriod = 0;
        var passCount = 0;
        var failCount = 0;
        var scoreSum = 0;
        var scoredCount = 0;

        foreach (var attempt in attempts)
        {
            var exam = attempt.Exam;
            if (exam == null || !examById.ContainsKey(exam.Id))
                continue;

            if (InPeriod(attempt.StartedAt, from, to))
            {
                startedInPeriod++;
                activeExamIds.Add(exam.Id);
            }

            if (attempt.SubmittedAt != null && InPeriod(attempt.SubmittedAt, from, to))
            {
                completedInPeriod++;
                conductedExamIds.Add(exam.Id);
                activeExamIds.Add(exam.Id);
                if (!string.IsNullOrWhiteSpace(attempt.CandidateUsername))
                    uniqueCandidates.Add(attempt.CandidateUsername.Trim().ToLowerInvariant());

                if (!_examService.IsFinalScoreReady(attempt))
                    continue;

                if (attempt.Score is int score)
                {
                    scoreSum += score;
                    scoredCount++;
                    var threshold = exam.PassThreshold > 0 ? exam.PassThreshold : 70;
                    if (score >= threshold) passCount++;
                    else failCount++;
                }
            }
            else if (attempt.SubmittedAt == null && InPeriod(attempt.StartedAt, from, to))
            {
                // اختبار قائم: مرشح بدأ ولم يسلّم بعد
                activeExamIds.Add(exam.Id);
                if (!string.IsNullOrWhiteSpace(attempt.CandidateUsername))
                    uniqueCandidates.Add(attempt.CandidateUsername.Trim().ToLowerInvariant());
            }
        }

        var examsCreatedInPeriod = 0;
        var publishedInPeriod = 0;
        foreach (var exam in exams)
        {
            if (InPeriod(exam.CreatedAt, from, to))
            {
                examsCreatedInPeriod++;
                activeExamIds.Add(exam.Id);
                if (exam.Published) publishedInPeriod++;
            }

            // اختبار منشور وقائم يدخل الإحصائيات حتى بلا فترة محددة
            if (from == null && to == null && exam.Published
                && string.Equals(_examService.LifecycleStatus(exam.Id), "IN_PROGRESS", StringComparison.OrdinalIgnoreCase))
            {
                activeExamIds.Add(exam.Id);
            }
        }

        var totalAssignments = 0;
        var departmentIds = new LinkedHashSet<long>();
        foreach (var examId in activeExamIds)
        {
            if (!examById.TryGetValue(examId, out var exam))
                continue;
            if (exam.DepartmentId is long deptId)
                departmentIds.Add(deptId);
            var allowed = _db.ExamAllowedCandidates.Where(a => a.ExamId == examId).ToList();
            totalAssignments += allowed.Count;
            foreach (var row in allowed)
            {
                if (!string.IsNullOrWhiteSpace(row.Email))
                    uniqueCandidates.Add(row.Email.Trim().ToLowerInvariant());
            }
        }

        // إن وُجدت اختبارات مطابقة للفلتر ولم تُحسب بعد (مثلاً لم يبدأ أحد)
        if (activeExamIds.Count == 0 && exams.Count > 0 && from == null && to == null)
        {
            foreach (var exam in exams)
            {
                activeExamIds.Add(exam.Id);
                if (exam.DepartmentId is long deptId)
                    departmentIds.Add(deptId);
                var allowed = _db.ExamAllowedCandidates.Count(a => a.ExamId == exam.Id);
                totalAssignments += allowed;
            }
        }

        return new Dictionary<string, object?>
        {
            ["from"] = from,
            ["to"] = to,
            ["periodLabel"] = PeriodLabel(from, to),
            ["examsConducted"] = conductedExamIds.Count,
            ["examsCreated"] = examsCreatedInPeriod,
            ["examsPublished"] = publishedInPeriod,
            ["departmentsBenefited"] = departmentIds.Count,
            ["totalAssignments"] = totalAssignments,
            ["uniqueCandidates"] = uniqueCandidates.Count,
            ["completedAttempts"] = completedInPeriod,
            ["startedAttempts"] = startedInPeriod,
            ["passCount"] = passCount,
            ["failCount"] = failCount,
            ["passRate"] = scoredCount > 0 ? Math.Round(passCount * 1000.0 / scoredCount) / 10.0 : null,
            ["averageScore"] = scoredCount > 0 ? Math.Round(scoreSum * 10.0 / scoredCount) / 10.0 : null,
            ["departmentRows"] = DepartmentBreakdown(examById, activeExamIds, attempts, from, to),
            ["examRows"] = ExamBreakdown(examById, activeExamIds, conductedExamIds, attempts, from, to)
        };
    }

    private List<Dictionary<string, object?>> DepartmentBreakdown(
        Dictionary<long, ExamEntity> examById,
        LinkedHashSet<long> activeExamIds,
        List<ExamAttempt> attempts,
        DateTime? from,
        DateTime? to)
    {
        var byDept = new Dictionary<long, Dictionary<string, object?>>();
        foreach (var examId in activeExamIds)
        {
            if (!examById.TryGetValue(examId, out var exam) || exam.Department == null)
                continue;
            var deptId = exam.Department.Id;
            if (!byDept.TryGetValue(deptId, out var row))
            {
                row = new Dictionary<string, object?>
                {
                    ["departmentId"] = deptId,
                    ["departmentName"] = exam.Department.Name ?? "—",
                    ["examCount"] = 0,
                    ["assignedCandidates"] = 0,
                    ["completedAttempts"] = 0,
                    ["passCount"] = 0,
                    ["scoredCount"] = 0
                };
                byDept[deptId] = row;
            }
            row["examCount"] = Convert.ToInt32(row["examCount"]) + 1;
            row["assignedCandidates"] = Convert.ToInt32(row["assignedCandidates"])
                + _db.ExamAllowedCandidates.Count(a => a.ExamId == examId);
        }

        foreach (var attempt in attempts)
        {
            var exam = attempt.Exam;
            if (exam?.Department == null || attempt.SubmittedAt == null)
                continue;
            if (!InPeriod(attempt.SubmittedAt, from, to))
                continue;
            if (!byDept.TryGetValue(exam.Department.Id, out var row))
                continue;
            row["completedAttempts"] = Convert.ToInt32(row["completedAttempts"]) + 1;
            if (attempt.Score is int score)
            {
                row["scoredCount"] = Convert.ToInt32(row["scoredCount"]) + 1;
                var threshold = exam.PassThreshold > 0 ? exam.PassThreshold : 70;
                if (score >= threshold)
                    row["passCount"] = Convert.ToInt32(row["passCount"]) + 1;
            }
        }

        var rows = new List<Dictionary<string, object?>>();
        foreach (var row in byDept.Values)
        {
            var scored = Convert.ToInt32(row["scoredCount"]);
            var passed = Convert.ToInt32(row["passCount"]);
            row["passRate"] = scored > 0 ? Math.Round(passed * 1000.0 / scored) / 10.0 : null;
            row.Remove("scoredCount");
            rows.Add(row);
        }
        return rows;
    }

    private List<Dictionary<string, object?>> ExamBreakdown(
        Dictionary<long, ExamEntity> examById,
        LinkedHashSet<long> activeExamIds,
        LinkedHashSet<long> conductedExamIds,
        List<ExamAttempt> attempts,
        DateTime? from,
        DateTime? to)
    {
        // اعرض الاختبارات النشطة والقائمة وليس المكتملة فقط
        var ids = new LinkedHashSet<long>(activeExamIds);
        foreach (var id in conductedExamIds) ids.Add(id);

        var rows = new List<Dictionary<string, object?>>();
        foreach (var examId in ids)
        {
            if (!examById.TryGetValue(examId, out var exam))
                continue;

            var completed = 0;
            var passed = 0;
            var scored = 0;
            var scoreSum = 0;
            foreach (var attempt in attempts)
            {
                if (attempt.Exam == null || attempt.Exam.Id != examId)
                    continue;
                if (attempt.SubmittedAt == null || !InPeriod(attempt.SubmittedAt, from, to))
                    continue;
                completed++;
                if (!_examService.IsFinalScoreReady(attempt))
                    continue;
                if (attempt.Score is int score)
                {
                    scored++;
                    scoreSum += score;
                    var threshold = exam.PassThreshold > 0 ? exam.PassThreshold : 70;
                    if (score >= threshold) passed++;
                }
            }

            rows.Add(new Dictionary<string, object?>
            {
                ["examId"] = examId,
                ["examTitle"] = exam.Title,
                ["departmentName"] = exam.Department?.Name ?? "—",
                ["assignedCandidates"] = _db.ExamAllowedCandidates.Count(a => a.ExamId == examId),
                ["completedAttempts"] = completed,
                ["passCount"] = passed,
                ["lifecycle"] = _examService.LifecycleStatus(examId),
                ["passRate"] = scored > 0 ? Math.Round(passed * 1000.0 / scored) / 10.0 : null,
                ["averageScore"] = scored > 0 ? Math.Round(scoreSum * 10.0 / scored) / 10.0 : null
            });
        }

        return rows
            .OrderBy(r => Convert.ToString(r["examTitle"]) ?? "", StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static bool InPeriod(DateTime? ts, DateTime? from, DateTime? to)
    {
        if (ts == null) return false;
        if (from == null && to == null) return true;
        if (from != null && ts < from) return false;
        if (to != null && ts > to) return false;
        return true;
    }

    private static string PeriodLabel(DateTime? from, DateTime? to)
    {
        if (from == null && to == null) return "all";
        if (from != null && to != null)
            return from.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                   + " — "
                   + to.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        if (from != null) return "from:" + from.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return "to:" + to!.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    /// <summary>Preserves insertion order like Java LinkedHashSet.</summary>
    private sealed class LinkedHashSet<T> : IEnumerable<T> where T : notnull
    {
        private readonly Dictionary<T, byte> _map = new();
        public LinkedHashSet() { }
        public LinkedHashSet(IEnumerable<T> items)
        {
            foreach (var item in items)
                _map[item] = 0;
        }
        public int Count => _map.Count;
        public void Add(T item) => _map[item] = 0;
        public IEnumerator<T> GetEnumerator() => _map.Keys.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
