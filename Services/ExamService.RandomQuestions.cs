using System.Data;
using System.Globalization;
using System.Text.Json;
using Jadeer.Exam.Models;
using Microsoft.EntityFrameworkCore;
using ExamEntity = Jadeer.Exam.Models.Exam;

namespace Jadeer.Exam.Services;

public partial class ExamService
{
    public void UpdateRandomQuestionSettings(long examId, int? count, int? totalMarks, int? passThreshold, int? durationMinutes)
    {
        var exam = _db.Exams.Find(examId) ?? throw new KeyNotFoundException();
        var total = totalMarks ?? exam.TotalMarks;
        var pass = passThreshold ?? exam.PassThreshold;
        var duration = durationMinutes ?? exam.DurationMinutes;
        if (count == exam.RandomQuestionCount && total == exam.TotalMarks && pass == exam.PassThreshold
            && duration == exam.DurationMinutes)
            return;
        if (count is <= 0)
            throw new ArgumentException("draw_count");
        if (exam.Published || _db.ExamAttempts.Any(a => a.ExamId == examId))
            throw new ArgumentException("draw_locked");

        if (total is <= 0 || pass < 1 || (total.HasValue && pass > total.Value))
            throw new ArgumentException("draw_score_settings");
        if (duration is <= 0 || duration > int.MaxValue / 60)
            throw new ArgumentException("draw_duration_settings");
        var bank = ReadBank(examId);
        if (duration is int minutes && DrawBudget(bank, count ?? bank.Count).Seconds > minutes * 60)
            throw new ArgumentException("draw_time");
        if (count.HasValue && total is int configuredTotal
            && ObjectiveBudget(configuredTotal, PracticalReservedMarks(exam), bank) < 1)
            throw new ArgumentException("draw_marks");
        if (count == null && total is int fullTotal
            && bank.Sum(q => q.Marks) > fullTotal - PracticalReservedMarks(exam))
            throw new ArgumentException("draw_score_settings");

        exam.RandomQuestionCount = count;
        exam.TotalMarks = total;
        exam.PassThreshold = pass;
        exam.DurationMinutes = duration;
        _db.SaveChanges();
    }

    private static void EnsureBankEditable(ExamEntity exam)
    {
        if (exam.RandomQuestionCount.HasValue && exam.Published)
            throw new ArgumentException("draw_locked");
    }

    private record BankQuestion(bool Essay, int Marks, int Seconds);

    private List<BankQuestion> ReadBank(long examId, long? excludeId = null) =>
        _db.Questions.AsNoTracking().Where(q => q.ExamId == examId && (excludeId == null || q.Id != excludeId))
            .Select(q => new BankQuestion(q.Essay, q.Mark ?? 0, q.DurationSeconds ?? 0)).ToList();

    private static (int Seconds, int Marks) DrawBudget(IEnumerable<BankQuestion> bank, int count)
    {
        var essays = bank.Where(q => q.Essay).ToList();
        var pool = bank.Where(q => !q.Essay).ToList();
        return (
            essays.Sum(q => q.Seconds) + pool.OrderByDescending(q => q.Seconds).Take(count).Sum(q => q.Seconds),
            essays.Sum(q => q.Marks) + pool.OrderByDescending(q => q.Marks).Take(count).Sum(q => q.Marks)
        );
    }

    private string? ValidateBankQuestion(ExamEntity exam, long? questionId, int marks, int seconds, bool essay)
    {
        EnsureBankEditable(exam);
        if (exam.RandomQuestionCount == null)
            return questionId is long id
                ? ValidateQuestionBudgetForUpdate(exam.Id, id, marks, seconds)
                : ValidateQuestionBudget(exam.Id, marks, seconds);

        var bank = ReadBank(exam.Id, questionId);
        bank.Add(new BankQuestion(essay, marks, seconds));
        return DrawError(exam, bank, complete: false);
    }

    private string? DrawError(ExamEntity exam, List<BankQuestion> bank, bool complete)
    {
        if (exam.RandomQuestionCount is not int count)
            return null;

        var pool = bank.Where(q => !q.Essay).ToList();
        if (complete && pool.Count < count)
            return "draw_not_enough";
        if (pool.Select(q => q.Marks).Distinct().Take(2).Count() > 1 || pool.Any(q => q.Marks < 1))
            return "draw_equal_marks";
        if (bank.Any(q => q.Seconds < 10))
            return "draw_time";

        var (seconds, _) = DrawBudget(bank, count);
        if (seconds > (long)(exam.DurationMinutes ?? 0) * 60)
            return "draw_time";

        if (exam.TotalMarks is int total && ObjectiveBudget(total, PracticalReservedMarks(exam), bank) < 1)
            return "draw_marks";

        return null;
    }

    private static int ObjectiveBudget(int totalMarks, int practicalMarks, IEnumerable<BankQuestion> bank) =>
        totalMarks - practicalMarks - bank.Where(q => q.Essay).Sum(q => q.Marks);

    // Round down to keep the displayed integer from crossing a passing threshold prematurely.
    private static int NormalizeDrawScore(int rawScore, int rawMaximum, int targetMaximum) =>
        rawMaximum <= 0 || targetMaximum <= 0 ? 0
            : (int)((long)Math.Clamp(rawScore, 0, rawMaximum) * targetMaximum / rawMaximum);

    public (List<Question> Questions, string? Draft) GetCandidateQuestions(ExamEntity exam, string username)
    {
        if (exam.RandomQuestionCount is not int count)
            return (GetExamQuestions(exam.Id, exam.Randomized), null);
        if (!exam.Published)
            throw new ArgumentException("draw_not_published");

        return WithDrawLock(exam.Id, () => DrawCandidateQuestions(exam.Id, username, count));
    }

    // A draw, draft save and submission must see a consistent attempt for this exam.
    private T WithDrawLock<T>(long examId, Func<T> operation)
    {
        var strategy = _db.Database.CreateExecutionStrategy();
        return strategy.Execute(() =>
        {
            try
            {
                using var transaction = _db.Database.BeginTransaction(IsolationLevel.ReadCommitted);
                _db.Exams.FromSqlInterpolated($"SELECT * FROM exams WHERE Id = {examId} FOR UPDATE")
                    .AsNoTracking().AsEnumerable().Single();
                var result = operation();
                transaction.Commit();
                return result;
            }
            catch
            {
                _db.ChangeTracker.Clear();
                throw;
            }
        });
    }

    private (List<Question> Questions, string? Draft) DrawCandidateQuestions(long examId, string username, int count)
    {
        if (HasSubmittedAttempt(examId, username))
            throw new ArgumentException("draw_submitted");

        var attempt = FindOpenAttempt(examId, username);
        if (attempt == null)
        {
            var bank = _db.Questions.AsNoTracking().Where(q => q.ExamId == examId)
                .OrderBy(q => q.Id).Select(q => new { q.Id, q.Essay }).ToList();
            var pool = bank.Where(q => !q.Essay).Select(q => q.Id).ToArray();
            if (pool.Length < count)
                throw new ArgumentException("draw_not_enough");

            Random.Shared.Shuffle(pool.AsSpan());
            var selectedIds = pool.Take(count).Concat(bank.Where(q => q.Essay).Select(q => q.Id)).ToArray();
            attempt = new ExamAttempt
            {
                ExamId = examId,
                CandidateUsername = username,
                SelectedQuestionIdsJson = JsonSerializer.Serialize(selectedIds)
            };
            _db.ExamAttempts.Add(attempt);
            _db.SaveChanges();
        }

        var ids = SelectedIds(attempt) ?? throw new ArgumentException("draw_attempt_missing");
        var questionsById = _db.Questions.AsNoTracking()
            .Where(q => q.ExamId == examId && ids.Contains(q.Id)).ToDictionary(q => q.Id);
        if (ids.Any(id => !questionsById.ContainsKey(id)))
            throw new ArgumentException("draw_attempt_missing");
        var questions = ids.Select(id => questionsById[id]).ToList();
        return (questions, attempt.AnswersJson);
    }

    private static long[]? SelectedIds(ExamAttempt? attempt) =>
        attempt?.SelectedQuestionIdsJson is string json ? JsonSerializer.Deserialize<long[]>(json) : null;

    private static IEnumerable<Question> QuestionsForReport(IEnumerable<Question> questions, ExamAttempt? attempt)
    {
        var ids = SelectedIds(attempt);
        if (ids == null)
            return questions;
        var byId = questions.ToDictionary(q => q.Id);
        return ids.Where(byId.ContainsKey).Select(id => byId[id]);
    }

    private static string RestrictDrawAnswers(ExamEntity exam, ExamAttempt? attempt, string json)
    {
        if (exam.RandomQuestionCount == null)
            return json;
        var ids = SelectedIds(attempt) ?? throw new ArgumentException("draw_attempt_missing");
        var allowed = ids.Select(id => id.ToString(CultureInfo.InvariantCulture)).ToHashSet();
        return JsonSerializer.Serialize(ParseAnswersMap(json).Where(pair => allowed.Contains(pair.Key))
            .ToDictionary(pair => pair.Key, pair => pair.Value));
    }
}
