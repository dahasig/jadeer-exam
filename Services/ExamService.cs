using System.Globalization;
using System.Text.Json;
using ClosedXML.Excel;
using Jadeer.Exam.Data;
using Jadeer.Exam.Models;
using Jadeer.Exam.Util;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Jadeer.Exam.Services;

using ExamEntity = global::Jadeer.Exam.Models.Exam;

public partial class ExamService
{
    public const string InstitutionalDemoTitle = "اختبار الذكاء التحليلي المؤسسي (تجربة)";
    private const string InstitutionalDemoTitleLegacy = "اختبار الذكاء التحليلي المؤسسي";

    private static readonly Comparer<(bool Submitted, DateTime? SubmittedAt, DateTime StartedAt, long Id)> CanonicalAttemptOrderComparer =
        Comparer<(bool, DateTime?, DateTime, long)>.Create((a, b) =>
        {
            var c = a.Item1.CompareTo(b.Item1);
            if (c != 0) return c;
            c = Nullable.Compare(a.Item2, b.Item2);
            if (c != 0) return c;
            c = a.Item3.CompareTo(b.Item3);
            if (c != 0) return c;
            return a.Item4.CompareTo(b.Item4);
        });

    private readonly AppDbContext _db;
    private readonly ExamSessionService _examSessionService;
    private readonly PasswordHasher<UserAccount> _passwordHasher;
    private readonly QuestionImageStorage _questionImageStorage;
    private readonly ExamNotificationService _examNotificationService;

    public ExamService(
        AppDbContext db,
        ExamSessionService examSessionService,
        PasswordHasher<UserAccount> passwordHasher,
        QuestionImageStorage questionImageStorage,
        ExamNotificationService examNotificationService)
    {
        _db = db;
        _examSessionService = examSessionService;
        _passwordHasher = passwordHasher;
        _questionImageStorage = questionImageStorage;
        _examNotificationService = examNotificationService;
    }

    private static int CompareAttempts(ExamAttempt a, ExamAttempt b) =>
        CanonicalAttemptOrderComparer.Compare(
            (a.SubmittedAt != null, a.SubmittedAt, a.StartedAt, a.Id),
            (b.SubmittedAt != null, b.SubmittedAt, b.StartedAt, b.Id));

    public List<ExamEntity> ListExams() => _db.Exams
        .Include(e => e.Department)
        .Include(e => e.AssignedExaminer)
        .Include(e => e.AssignedGrader)
        .ToList();

    public List<ExamEntity> ListExamsForStaff(string username)
    {
        var u = _db.Users.Include(x => x.Department).FirstOrDefault(x => x.Username == username);
        if (u == null) return [];
        if (u.Role == Role.ADMIN) return ListExams();
        return _db.Exams
            .Include(e => e.Department)
            .Include(e => e.AssignedExaminer)
            .Include(e => e.AssignedGrader)
            .Where(e => e.AssignedExaminerId == u.Id)
            .ToList();
    }

    public void AssignExamToExaminer(long examId, long examinerUserId)
    {
        var exam = _db.Exams.Find(examId) ?? throw new KeyNotFoundException();
        var examiner = _db.Users.Include(u => u.Department).FirstOrDefault(u => u.Id == examinerUserId)
                       ?? throw new KeyNotFoundException();
        if (examiner.Role != Role.EXAMINER)
            throw new ArgumentException("المستخدم ليس مسؤول اختبار");
        exam.AssignedExaminer = examiner;
        exam.AssignedExaminerId = examiner.Id;
        if (exam.Department == null && examiner.Department != null)
        {
            exam.Department = examiner.Department;
            exam.DepartmentId = examiner.Department.Id;
            exam.Subject = examiner.Department.Name;
        }
        _db.SaveChanges();
    }

    public ExamEntity? EnsureInstitutionalDemoExam(long? departmentId)
    {
        if (departmentId == null) return null;
        var dept = _db.Departments.Find(departmentId.Value);
        if (dept == null) return null;
        var exam = _db.Exams.AsEnumerable()
            .FirstOrDefault(e => e.DepartmentId == departmentId
                                 && e.Title != null
                                 && e.Title.Contains("الذكاء التحليلي المؤسسي", StringComparison.OrdinalIgnoreCase));
        if (exam == null)
            exam = CreateInstitutionalDemoExam(dept);
        else
            RepairInstitutionalDemoExam(exam, dept);
        return exam;
    }

    private ExamEntity CreateInstitutionalDemoExam(Department dept)
    {
        var exam = new ExamEntity
        {
            Title = InstitutionalDemoTitle,
            Subject = dept.Name,
            Department = dept,
            DepartmentId = dept.Id,
            DurationMinutes = 45,
            Randomized = true,
            OpportunityType = "TRAINING",
            City = dept.City ?? "الجبيل",
            OpportunityYear = DateTime.Now.Year,
            TotalMarks = 100,
            PassThreshold = 70,
            Published = true,
            Template = false,
            CreationMode = "DEMO"
        };
        _db.Exams.Add(exam);
        _db.SaveChanges();
        SeedInstitutionalDemoQuestions(exam);
        return exam;
    }

    private void RepairInstitutionalDemoExam(ExamEntity exam, Department dept)
    {
        var changed = false;
        if (exam.Department == null)
        {
            exam.Department = dept;
            exam.DepartmentId = dept.Id;
            changed = true;
        }
        if (exam.TotalMarks == null || exam.TotalMarks < 1)
        {
            exam.TotalMarks = 100;
            changed = true;
        }
        if (!exam.Published)
        {
            exam.Published = true;
            changed = true;
        }
        if (exam.Template)
        {
            exam.Template = false;
            changed = true;
        }
        if (changed) _db.SaveChanges();
        if (CountQuestions(exam.Id) < 1)
            SeedInstitutionalDemoQuestions(exam);
    }

    private void SeedInstitutionalDemoQuestions(ExamEntity exam)
    {
        if (CountQuestions(exam.Id) > 0) return;
        for (var i = 1; i <= 10; i++)
        {
            _db.Questions.Add(new Question
            {
                Exam = exam,
                ExamId = exam.Id,
                Text = "سؤال رقم " + i + ": ما هو أفضل إجراء مؤسسي لاتخاذ القرار الذكي؟",
                OptionA = "تحليل البيانات",
                OptionB = "التخمين",
                OptionC = "تجاهل المخاطر",
                OptionD = "إلغاء التقييم",
                CorrectOption = "A",
                Mark = 10,
                DurationSeconds = 270
            });
        }
        _db.SaveChanges();
    }

    public void RepairOrphanInstitutionalExams()
    {
        foreach (var exam in _db.Exams.Include(e => e.Department).ToList())
        {
            if (exam.Title == null
                || (!exam.Title.Contains(InstitutionalDemoTitleLegacy, StringComparison.Ordinal)
                    && !exam.Title.Contains(InstitutionalDemoTitle, StringComparison.Ordinal)))
                continue;
            if (exam.Department != null)
            {
                RepairInstitutionalDemoExam(exam, exam.Department);
                continue;
            }
            var dept = _db.Departments.FirstOrDefault();
            if (dept == null) continue;
            RepairInstitutionalDemoExam(exam, dept);
        }
    }

    public bool IsAdmin(string username)
    {
        var u = _db.Users.FirstOrDefault(x => x.Username == username);
        return u != null && u.Role == Role.ADMIN;
    }

    public long? StaffDepartmentId(string username)
    {
        var u = _db.Users.Include(x => x.Department).FirstOrDefault(x => x.Username == username);
        return u?.Department?.Id;
    }

    public List<ExamEntity> ListAvailableExamsForCandidate(string username)
    {
        var user = _db.Users.FirstOrDefault(x => x.Username == username);
        if (user == null || string.IsNullOrWhiteSpace(user.Email)) return [];
        var email = user.Email.Trim();
        var outList = new List<ExamEntity>();
        foreach (var exam in _db.Exams.ToList())
        {
            var allowed = _db.ExamAllowedCandidates.Any(a =>
                a.ExamId == exam.Id && a.Email.ToLower() == email.ToLower());
            var submitted = _db.ExamAttempts.Any(a =>
                a.ExamId == exam.Id && a.CandidateUsername == username && a.SubmittedAt != null);
            if (allowed && !submitted && exam.Published)
                outList.Add(exam);
        }
        return outList;
    }

    public bool CanCandidateAccessExam(string username, long examId)
    {
        var user = _db.Users.FirstOrDefault(x => x.Username == username);
        if (user == null || string.IsNullOrWhiteSpace(user.Email)) return false;
        var email = user.Email.Trim();
        return _db.ExamAllowedCandidates.Any(a =>
            a.ExamId == examId && a.Email.ToLower() == email.ToLower());
    }

    public bool HasSubmittedAttempt(long examId, string username) =>
        _db.ExamAttempts.Any(a => a.ExamId == examId && a.CandidateUsername == username && a.SubmittedAt != null);

    public ExamEntity? GetExam(long examId) =>
        _db.Exams.Include(e => e.Department).Include(e => e.AssignedExaminer).Include(e => e.AssignedGrader)
            .FirstOrDefault(e => e.Id == examId);

    public ExamEntity CreateExam(string title, string subject, int? durationMinutes, bool randomized)
    {
        var exam = new ExamEntity
        {
            Title = title,
            Subject = subject,
            DurationMinutes = durationMinutes,
            Randomized = randomized
        };
        _db.Exams.Add(exam);
        _db.SaveChanges();
        return exam;
    }

    public ExamEntity CreateExamForStaff(string creatorUsername, string title, int? durationMinutes, bool randomized,
        long? departmentId, string? opportunityType, string? jobTitle,
        string? companyName, string? city, int? opportunityYear,
        int? totalMarks, int? passThreshold,
        bool practicalEnabled, int? practicalMaxMarks)
    {
        var creator = _db.Users.Include(u => u.Department).FirstOrDefault(u => u.Username == creatorUsername)
                      ?? throw new KeyNotFoundException();
        var exam = new ExamEntity
        {
            Title = title,
            DurationMinutes = durationMinutes,
            Randomized = randomized,
            OpportunityType = opportunityType,
            JobTitle = jobTitle,
            CompanyName = companyName,
            City = city,
            OpportunityYear = opportunityYear,
            TotalMarks = totalMarks,
            PassThreshold = passThreshold ?? 70,
            Published = false,
            Template = false,
            CreationMode = "MANUAL"
        };
        ApplyPracticalSettings(exam, practicalEnabled, practicalMaxMarks);
        if (creator.Role == Role.ADMIN)
        {
            var dep = _db.Departments.Find(departmentId ?? 0) ?? throw new KeyNotFoundException();
            exam.Department = dep;
            exam.DepartmentId = dep.Id;
            exam.Subject = dep.Name;
        }
        else
        {
            if (creator.Department == null)
                throw new ArgumentException("مسؤول الاختبار غير مرتبط بإدارة");
            exam.Department = creator.Department;
            exam.DepartmentId = creator.Department.Id;
            exam.Subject = creator.Department.Name;
            exam.AssignedExaminer = creator;
            exam.AssignedExaminerId = creator.Id;
        }
        _db.Exams.Add(exam);
        _db.SaveChanges();
        return exam;
    }

    public void AssignGraderToExam(string staffUsername, long examId, long graderUserId)
    {
        if (!CanManageExam(staffUsername, examId))
            throw new ArgumentException("forbidden");
        var grader = _db.Users.Find(graderUserId) ?? throw new KeyNotFoundException();
        if (grader.Role != Role.GRADER)
            throw new ArgumentException("not_grader");
        var exam = _db.Exams.Find(examId) ?? throw new KeyNotFoundException();
        exam.AssignedGrader = grader;
        exam.AssignedGraderId = grader.Id;
        _db.SaveChanges();
    }

    public bool CanGradeExam(string staffUsername, long examId)
    {
        var u = _db.Users.FirstOrDefault(x => x.Username == staffUsername);
        if (u == null) return false;
        if (u.Role == Role.ADMIN) return true;
        if (u.Role == Role.EXAMINER) return CanManageExam(staffUsername, examId);
        if (u.Role == Role.GRADER)
        {
            var exam = _db.Exams.Find(examId);
            return exam?.AssignedGraderId == u.Id;
        }
        return false;
    }

    public void RepairExamsWithoutAssignedExaminer()
    {
        foreach (var exam in _db.Exams.Include(e => e.Department).Where(e => e.AssignedExaminerId == null).ToList())
        {
            if (exam.DepartmentId == null) continue;
            var examiners = _db.Users.Where(u => u.Role == Role.EXAMINER && u.DepartmentId == exam.DepartmentId).ToList();
            if (examiners.Count == 1)
            {
                exam.AssignedExaminer = examiners[0];
                exam.AssignedExaminerId = examiners[0].Id;
                _db.SaveChanges();
            }
        }
    }

    public bool CanManageExam(string staffUsername, long examId)
    {
        var u = _db.Users.FirstOrDefault(x => x.Username == staffUsername);
        if (u == null) return false;
        if (u.Role == Role.ADMIN) return true;
        var ex = _db.Exams.Find(examId);
        return ex?.AssignedExaminerId == u.Id;
    }

    public ExamQuestionBudget ExamQuestionBudget(long examId)
    {
        var exam = _db.Exams.Find(examId) ?? throw new KeyNotFoundException();
        var totalSeconds = Math.Max(0, (exam.DurationMinutes ?? 0) * 60);
        var usedSeconds = 0;
        var usedMarks = 0;
        if (exam.RandomQuestionCount is int count)
        {
            var bank = ReadBank(examId);
            (usedSeconds, usedMarks) = DrawBudget(bank, count);
            if (exam.TotalMarks is int target)
            {
                var essayMarks = bank.Where(q => q.Essay).Sum(q => q.Marks);
                var objectiveMaximum = ObjectiveBudget(target, PracticalReservedMarks(exam), bank);
                usedMarks = essayMarks + NormalizeDrawScore(bank.Count(q => !q.Essay), count, objectiveMaximum);
            }
        }
        else
        {
            foreach (var q in _db.Questions.Where(q => q.ExamId == examId))
            {
                usedSeconds += q.DurationSeconds ?? 0;
                usedMarks += q.Mark ?? 0;
            }
        }
        var totalMarks = exam.TotalMarks;
        var practicalReserved = PracticalReservedMarks(exam);
        int? remainingMarks = totalMarks != null ? totalMarks - practicalReserved - usedMarks : null;
        return new ExamQuestionBudget(
            totalSeconds,
            usedSeconds,
            Math.Max(0, totalSeconds - usedSeconds),
            totalMarks,
            practicalReserved,
            usedMarks,
            remainingMarks != null ? Math.Max(0, remainingMarks.Value) : null);
    }

    public int PracticalReservedMarks(ExamEntity? exam)
    {
        if (exam == null || !exam.PracticalEnabled || exam.PracticalMaxMarks == null || exam.PracticalMaxMarks < 1)
            return 0;
        return exam.PracticalMaxMarks.Value;
    }

    private void ApplyPracticalSettings(ExamEntity exam, bool practicalEnabled, int? practicalMaxMarks)
    {
        if (!practicalEnabled)
        {
            exam.PracticalEnabled = false;
            exam.PracticalMaxMarks = null;
            return;
        }
        if (practicalMaxMarks == null || practicalMaxMarks < 1)
            throw new ArgumentException("invalid_practical_marks");
        var total = exam.TotalMarks;
        if (total != null && practicalMaxMarks >= total)
            throw new ArgumentException("practical_too_high");
        exam.PracticalEnabled = true;
        exam.PracticalMaxMarks = practicalMaxMarks;
    }

    public void UpdateExamPracticalSettings(string staffUsername, long examId, bool practicalEnabled, int? practicalMaxMarks)
    {
        if (!CanManageExam(staffUsername, examId))
            throw new ArgumentException("forbidden");
        var exam = _db.Exams.Find(examId) ?? throw new KeyNotFoundException();
        EnsureBankEditable(exam);
        if (practicalEnabled)
        {
            ApplyPracticalSettings(exam, true, practicalMaxMarks);
            var used = ExamQuestionBudget(examId).UsedMarks;
            var total = exam.TotalMarks ?? 0;
            if (exam.RandomQuestionCount.HasValue && DrawError(exam, ReadBank(examId), complete: false) == "draw_marks")
                throw new ArgumentException("questions_exceed_online_budget");
            if (used > total - PracticalReservedMarks(exam))
                throw new ArgumentException("questions_exceed_online_budget");
        }
        else
        {
            exam.PracticalEnabled = false;
            exam.PracticalMaxMarks = null;
        }
        _db.SaveChanges();
        foreach (var attempt in _db.ExamAttempts.Include(a => a.Exam).Where(a => a.ExamId == examId).ToList())
        {
            if (!practicalEnabled)
                attempt.PracticalScore = null;
            else if (attempt.PracticalScore != null)
            {
                var max = PracticalReservedMarks(exam);
                attempt.PracticalScore = Math.Min(Math.Max(0, attempt.PracticalScore.Value), max);
            }
            RecomputeAttemptScore(attempt);
        }
        _db.SaveChanges();
    }

    public void SetPracticalScore(string staffUsername, long attemptId, int? score)
    {
        if (!CanManageAttempt(staffUsername, attemptId))
            throw new ArgumentException("forbidden");
        var attempt = _db.ExamAttempts.Include(a => a.Exam).FirstOrDefault(a => a.Id == attemptId)
                      ?? throw new KeyNotFoundException();
        var exam = attempt.Exam;
        if (exam == null || !exam.PracticalEnabled)
            throw new ArgumentException("practical_disabled");
        if (attempt.SubmittedAt == null)
            throw new ArgumentException("not_submitted");
        var max = PracticalReservedMarks(exam);
        var val = Math.Min(Math.Max(0, score ?? 0), max);
        attempt.PracticalScore = val;
        RecomputeAttemptScore(attempt);
        _db.SaveChanges();
    }

    private void RecomputeAttemptScore(ExamAttempt attempt)
    {
        var objective = attempt.ObjectiveScore;
        var essay = attempt.EssayScore;
        var practical = 0;
        var exam = attempt.Exam ?? (attempt.ExamId != null ? _db.Exams.Find(attempt.ExamId) : null);
        if (exam != null && exam.PracticalEnabled && attempt.PracticalScore != null)
            practical = attempt.PracticalScore.Value;
        // الإجمالي = مجموع المكوّنات (موضوعي + مقالي + عملي).
        // لا نقطع بـ TotalMarks هنا حتى لا تختفي درجة المقال بعد التصحيح.
        attempt.Score = objective + essay + practical;
    }

    public List<Dictionary<string, object?>> ListPracticalScoreRows(string staffUsername)
    {
        var staff = _db.Users.FirstOrDefault(u => u.Username == staffUsername);
        if (staff == null) return [];
        List<long> examIds;
        if (staff.Role == Role.GRADER)
            examIds = _db.Exams.Where(e => e.AssignedGraderId == staff.Id && e.PracticalEnabled).Select(e => e.Id).ToList();
        else if (staff.Role == Role.ADMIN)
            examIds = _db.Exams.Where(e => e.PracticalEnabled).Select(e => e.Id).ToList();
        else if (staff.Role == Role.EXAMINER)
            examIds = _db.Exams.Where(e => e.AssignedExaminerId == staff.Id && e.PracticalEnabled).Select(e => e.Id).ToList();
        else
            return [];
        if (examIds.Count == 0) return [];
        var rows = new List<Dictionary<string, object?>>();
        var attempts = DedupeAttemptsByCandidate(
            _db.ExamAttempts.Include(a => a.Exam)
                .Where(a => a.ExamId != null && examIds.Contains(a.ExamId.Value))
                .OrderByDescending(a => a.StartedAt)
                .ToList());
        foreach (var attempt in attempts)
        {
            if (attempt.SubmittedAt == null) continue;
            if (staff.Role == Role.GRADER && !CanGradeExam(staffUsername, attempt.Exam!.Id)) continue;
            var exam = attempt.Exam;
            rows.Add(new Dictionary<string, object?>
            {
                ["attemptId"] = attempt.Id,
                ["candidateUsername"] = attempt.CandidateUsername,
                ["examTitle"] = exam?.Title ?? "-",
                ["examId"] = exam?.Id,
                ["submittedAt"] = attempt.SubmittedAt,
                ["objectiveScore"] = attempt.ObjectiveScore,
                ["essayScore"] = attempt.EssayScore,
                ["practicalScore"] = attempt.PracticalScore,
                ["practicalMaxMarks"] = exam != null ? PracticalReservedMarks(exam) : 0,
                ["totalScore"] = attempt.Score
            });
        }
        return rows;
    }

    public string? ValidateQuestionBudget(long examId, int mark, int durationSeconds)
    {
        var budget = ExamQuestionBudget(examId);
        if (durationSeconds > budget.RemainingSeconds) return "time";
        if (budget.TotalMarks != null && mark > (budget.RemainingMarks ?? 0)) return "marks";
        return null;
    }

    public Question AddQuestionToExam(long examId, string text, string? optionA, string? optionB, string? optionC,
        string? optionD, string? correctOption, int? mark, bool? essayQuestion, int? durationSeconds,
        string? topic, string? difficulty, string? rubric, IFormFile? imageFile)
    {
        if (mark == null || mark < 1)
            throw new ArgumentException("درجة السؤال مطلوبة");
        var seconds = durationSeconds == null || durationSeconds < 10 ? 0 : durationSeconds.Value;
        if (seconds < 10)
            throw new ArgumentException("وقت السؤال مطلوب");
        var exam = _db.Exams.Find(examId) ?? throw new KeyNotFoundException();
        var budgetError = ValidateBankQuestion(exam, null, mark.Value, seconds, essayQuestion == true);
        if (budgetError != null)
            throw new ArgumentException(budgetError);
        var question = new Question
        {
            Exam = exam,
            ExamId = examId,
            Text = text,
            Essay = essayQuestion == true,
            Mark = mark,
            DurationSeconds = seconds,
            Topic = string.IsNullOrWhiteSpace(topic) ? null : topic.Trim(),
            Difficulty = string.IsNullOrWhiteSpace(difficulty) ? null : difficulty.Trim(),
            Rubric = string.IsNullOrWhiteSpace(rubric) ? null : rubric.Trim()
        };
        if (question.Essay)
        {
            question.OptionA = question.OptionB = question.OptionC = question.OptionD = question.CorrectOption = null;
        }
        else
        {
            question.OptionA = optionA;
            question.OptionB = optionB;
            question.OptionC = optionC;
            question.OptionD = optionD;
            question.CorrectOption = correctOption;
        }
        _db.Questions.Add(question);
        _db.SaveChanges();
        ApplyQuestionImage(question, examId, imageFile);
        _db.SaveChanges();
        return question;
    }

    public Question UpdateQuestionInExam(long examId, long questionId, string text, string? optionA, string? optionB,
        string? optionC, string? optionD, string? correctOption, int? mark, bool? essayQuestion, int? durationSeconds,
        string? topic, string? difficulty, string? rubric, IFormFile? imageFile, bool removeImage)
    {
        var question = _db.Questions.Include(q => q.Exam).FirstOrDefault(q => q.Id == questionId)
                       ?? throw new KeyNotFoundException();
        if (question.Exam == null || question.Exam.Id != examId)
            throw new ArgumentException("forbidden");
        if (mark == null || mark < 1)
            throw new ArgumentException("درجة السؤال مطلوبة");
        var seconds = durationSeconds == null || durationSeconds < 10 ? 0 : durationSeconds.Value;
        if (seconds < 10)
            throw new ArgumentException("وقت السؤال مطلوب");
        var budgetError = ValidateBankQuestion(question.Exam, questionId, mark.Value, seconds, essayQuestion == true);
        if (budgetError != null)
            throw new ArgumentException(budgetError);
        var essay = essayQuestion == true;
        question.Text = text;
        question.Essay = essay;
        if (essay)
        {
            question.OptionA = question.OptionB = question.OptionC = question.OptionD = question.CorrectOption = null;
        }
        else
        {
            question.OptionA = optionA;
            question.OptionB = optionB;
            question.OptionC = optionC;
            question.OptionD = optionD;
            question.CorrectOption = correctOption;
        }
        question.Mark = mark;
        question.DurationSeconds = seconds;
        question.Topic = string.IsNullOrWhiteSpace(topic) ? null : topic.Trim();
        question.Difficulty = string.IsNullOrWhiteSpace(difficulty) ? null : difficulty.Trim();
        question.Rubric = string.IsNullOrWhiteSpace(rubric) ? null : rubric.Trim();
        if (removeImage) ClearQuestionImage(question, examId);
        if (imageFile != null && imageFile.Length > 0) ApplyQuestionImage(question, examId, imageFile);
        _db.SaveChanges();
        return question;
    }

    public void DeleteQuestionFromExam(long examId, long questionId)
    {
        var question = _db.Questions.Include(q => q.Exam).FirstOrDefault(q => q.Id == questionId)
                       ?? throw new KeyNotFoundException();
        if (question.Exam == null || question.Exam.Id != examId)
            throw new ArgumentException("forbidden");
        EnsureBankEditable(question.Exam);
        ClearQuestionImage(question, examId);
        _db.Questions.Remove(question);
        _db.SaveChanges();
    }

    public record QuestionImagePayload(string MediaType, byte[] Data);

    public QuestionImagePayload? GetQuestionImagePayload(long? examId, long? questionId)
    {
        if (examId == null || questionId == null) return null;
        var question = _db.Questions.Include(q => q.Exam).FirstOrDefault(q => q.Id == questionId);
        if (question?.Exam == null || question.Exam.Id != examId) return null;
        if (question.ImageData is { Length: > 0 })
        {
            return new QuestionImagePayload(
                _questionImageStorage.MediaTypeFor(question.ImageFileName, question.ImageContentType),
                question.ImageData);
        }
        if (!string.IsNullOrWhiteSpace(question.ImageFileName))
        {
            var path = _questionImageStorage.ResolveFile(examId, question.ImageFileName);
            if (path != null)
            {
                try
                {
                    var data = File.ReadAllBytes(path);
                    return new QuestionImagePayload(
                        _questionImageStorage.MediaTypeFor(question.ImageFileName, question.ImageContentType),
                        data);
                }
                catch (IOException)
                {
                    return null;
                }
            }
        }
        return null;
    }

    public string? ValidateQuestionBudgetForUpdate(long examId, long questionId, int mark, int durationSeconds)
    {
        var budget = ExamQuestionBudget(examId);
        var existing = _db.Questions.Find(questionId);
        var freedSeconds = existing?.DurationSeconds ?? 0;
        var freedMarks = existing?.Mark ?? 0;
        if (durationSeconds > budget.RemainingSeconds + freedSeconds) return "time";
        if (budget.TotalMarks != null && mark > (budget.RemainingMarks ?? 0) + freedMarks) return "marks";
        return null;
    }

    private void ApplyQuestionImage(Question question, long examId, IFormFile? imageFile)
    {
        if (imageFile == null || imageFile.Length == 0) return;
        try
        {
            var stored = _questionImageStorage.ReadAndValidateAsync(imageFile).GetAwaiter().GetResult();
            question.ImageData = stored.Data;
            question.ImageContentType = stored.ContentType;
            if (question.Id != 0)
            {
                question.ImageFileName = _questionImageStorage
                    .StoreFileAsync(examId, question.Id, stored).GetAwaiter().GetResult();
            }
        }
        catch (IOException)
        {
            throw new ArgumentException("image_save");
        }
    }

    private void ClearQuestionImage(Question question, long examId)
    {
        if (question.ImageFileName != null)
            _questionImageStorage.DeleteFile(examId, question.ImageFileName);
        question.ImageFileName = null;
        question.ImageData = null;
        question.ImageContentType = null;
    }

    public string? QuestionImagePath(long examId, long questionId)
    {
        if (GetQuestionImagePayload(examId, questionId) == null) return null;
        var fileName = _db.Questions.Find(questionId)?.ImageFileName;
        return _questionImageStorage.ResolveFile(examId, fileName);
    }

    public string QuestionImageContentType(long examId, long questionId) =>
        GetQuestionImagePayload(examId, questionId)?.MediaType ?? "image/jpeg";

    public List<Question> GetExamQuestions(long examId, bool randomized)
    {
        var questions = _db.Questions.Where(q => q.ExamId == examId).ToList();
        if (randomized)
        {
            var rng = new Random();
            questions = questions.OrderBy(_ => rng.Next()).ToList();
        }
        return questions;
    }

    public ExamAttempt SaveDraft(long examId, string username, string answersJson)
    {
        var exam = _db.Exams.Find(examId) ?? throw new KeyNotFoundException();
        return exam.RandomQuestionCount == null ? SaveDraft(exam, username, answersJson)
            : WithDrawLock(examId, () => SaveDraft(exam, username, answersJson));
    }

    private ExamAttempt SaveDraft(ExamEntity exam, string username, string answersJson)
    {
        var examId = exam.Id;
        var attempt = FindOpenAttempt(examId, username);
        answersJson = RestrictDrawAnswers(exam, attempt, answersJson);
        if (attempt == null)
        {
            attempt = new ExamAttempt { Exam = exam, ExamId = examId, CandidateUsername = username };
            _db.ExamAttempts.Add(attempt);
        }
        attempt.AnswersJson = answersJson;
        _db.SaveChanges();
        DeleteOtherOpenAttempts(examId, username, attempt.Id);
        return attempt;
    }

    public ExamAttempt SubmitAttempt(long examId, string username, string answersJson)
    {
        var exam = _db.Exams.Find(examId) ?? throw new KeyNotFoundException();
        return exam.RandomQuestionCount == null ? SubmitAttempt(exam, username, answersJson)
            : WithDrawLock(examId, () => SubmitAttempt(exam, username, answersJson));
    }

    private ExamAttempt SubmitAttempt(ExamEntity exam, string username, string answersJson)
    {
        var examId = exam.Id;
        var attempt = FindOpenAttempt(examId, username);
        answersJson = RestrictDrawAnswers(exam, attempt, answersJson);
        if (attempt == null)
        {
            attempt = new ExamAttempt { Exam = exam, ExamId = examId, CandidateUsername = username };
            _db.ExamAttempts.Add(attempt);
        }
        attempt.AnswersJson = answersJson;
        attempt.SubmittedAt = DateTime.Now;
        attempt.ObjectiveScore = CalculateObjectiveScore(exam, attempt);
        RecomputeAttemptScore(attempt);
        _db.SaveChanges();
        DeleteOtherOpenAttempts(examId, username, attempt.Id);
        _examSessionService.MarkSubmitted(username, examId);
        return attempt;
    }

    public ExamAttempt? CanonicalAttemptForCandidate(List<ExamAttempt> attempts)
    {
        if (attempts == null || attempts.Count == 0) return null;
        return attempts.OrderBy(a => a, Comparer<ExamAttempt>.Create(CompareAttempts)).LastOrDefault();
    }

    public List<ExamAttempt> DedupeAttemptsByCandidate(List<ExamAttempt> attempts)
    {
        if (attempts == null || attempts.Count == 0) return [];
        var best = new Dictionary<string, ExamAttempt>();
        foreach (var attempt in attempts)
        {
            if (string.IsNullOrWhiteSpace(attempt.CandidateUsername)) continue;
            var key = (attempt.ExamId?.ToString() ?? "") + "|" + attempt.CandidateUsername.Trim().ToLowerInvariant();
            if (!best.TryGetValue(key, out var existing) || CompareAttempts(attempt, existing) > 0)
                best[key] = attempt;
        }
        return best.Values.OrderByDescending(a => a.StartedAt).ToList();
    }

    private ExamAttempt? FindOpenAttempt(long examId, string username) =>
        _db.ExamAttempts.Where(a => a.ExamId == examId && a.CandidateUsername == username && a.SubmittedAt == null)
            .AsEnumerable()
            .OrderBy(a => a, Comparer<ExamAttempt>.Create(CompareAttempts))
            .LastOrDefault();

    private void DeleteOtherOpenAttempts(long examId, string username, long keepId)
    {
        var orphans = _db.ExamAttempts
            .Where(a => a.ExamId == examId && a.CandidateUsername == username && a.SubmittedAt == null && a.Id != keepId)
            .ToList();
        _db.ExamAttempts.RemoveRange(orphans);
        _db.SaveChanges();
    }

    public List<ExamAttempt> ListAttempts() =>
        _db.ExamAttempts.Include(a => a.Exam).OrderByDescending(a => a.StartedAt).ToList();

    public List<ExamAttempt> ListAttemptsForStaff(string username)
    {
        var u = _db.Users.FirstOrDefault(x => x.Username == username);
        if (u == null) return [];
        if (u.Role == Role.ADMIN)
            return DedupeAttemptsByCandidate(ListAttempts());
        var examIds = _db.Exams.Where(e => e.AssignedExaminerId == u.Id).Select(e => e.Id).ToList();
        if (examIds.Count == 0) return [];
        return DedupeAttemptsByCandidate(
            _db.ExamAttempts.Include(a => a.Exam)
                .Where(a => a.ExamId != null && examIds.Contains(a.ExamId.Value))
                .OrderByDescending(a => a.StartedAt)
                .ToList());
    }

    public string CreateCandidateAccount(string? fullName, string? email, string? nationalId)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(nationalId))
            return "invalid";
        var normalizedEmail = email.Trim().ToLowerInvariant();
        var normalizedNationalId = NationalIdNormalizer.Normalize(nationalId);
        if (string.IsNullOrWhiteSpace(normalizedNationalId)) return "invalid";

        var byEmail = _db.Users.FirstOrDefault(u => u.Email == normalizedEmail);
        if (byEmail != null)
            return byEmail.Role == Role.CANDIDATE ? "exists_candidate" : "email_taken";
        var byUsername = _db.Users.FirstOrDefault(u => u.Username == normalizedEmail);
        if (byUsername != null)
            return byUsername.Role == Role.CANDIDATE ? "exists_candidate" : "email_taken";

        var byNationalId = _db.Users.FirstOrDefault(u =>
            u.NationalId == normalizedNationalId && u.Role == Role.CANDIDATE);
        if (byNationalId != null)
        {
            var otherEmail = byNationalId.Email ?? byNationalId.Username;
            if (otherEmail != null && string.Equals(normalizedEmail, otherEmail, StringComparison.OrdinalIgnoreCase))
                return "exists_candidate";
            return "national_id_exists";
        }

        var u = new UserAccount
        {
            Username = normalizedEmail,
            FullName = string.IsNullOrWhiteSpace(fullName) ? normalizedEmail : fullName.Trim(),
            Email = normalizedEmail,
            NationalId = normalizedNationalId,
            Role = Role.CANDIDATE
        };
        u.Password = _passwordHasher.HashPassword(u, normalizedNationalId);
        _db.Users.Add(u);
        _db.SaveChanges();
        return "ok";
    }

    public string CreateStaffUser(string? fullName, string? email, string? nationalId, long? departmentId, Role role)
    {
        if (role == Role.CANDIDATE || string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(nationalId))
            return "invalid";
        var normalizedEmail = email.Trim().ToLowerInvariant();
        var normalizedNationalId = NationalIdNormalizer.Normalize(nationalId);
        if (string.IsNullOrWhiteSpace(normalizedNationalId)) return "invalid";

        if (_db.Users.Any(u => u.Email == normalizedEmail) || _db.Users.Any(u => u.Username == normalizedEmail))
            return "email_taken";
        if (_db.Users.Any(u => u.NationalId == normalizedNationalId && u.Role == role))
            return "national_id_exists";

        var u = new UserAccount
        {
            Username = normalizedEmail,
            FullName = string.IsNullOrWhiteSpace(fullName) ? normalizedEmail : fullName.Trim(),
            Email = normalizedEmail,
            NationalId = normalizedNationalId,
            Role = role
        };
        u.Password = _passwordHasher.HashPassword(u, normalizedNationalId);
        if (role == Role.EXAMINER && departmentId != null)
        {
            u.Department = _db.Departments.Find(departmentId);
            u.DepartmentId = u.Department?.Id;
        }
        _db.Users.Add(u);
        _db.SaveChanges();
        return "ok";
    }

    public void DeleteAttemptById(long attemptId)
    {
        var attempt = _db.ExamAttempts.Include(a => a.Exam).FirstOrDefault(a => a.Id == attemptId)
                      ?? throw new KeyNotFoundException();
        var username = attempt.CandidateUsername;
        var examId = attempt.ExamId;
        _db.ExamAttempts.Remove(attempt);
        _db.SaveChanges();
        if (examId != null && username != null)
        {
            if (!_db.ExamAttempts.Any(a => a.ExamId == examId && a.CandidateUsername == username && a.SubmittedAt != null))
                _examSessionService.ResetSession(username, examId.Value);
        }
    }

    public void ResetCandidateExamForRetake(string staffUsername, long examId, string? candidateEmail)
    {
        if (!CanManageExam(staffUsername, examId))
            throw new ArgumentException("غير مصرح");
        if (string.IsNullOrWhiteSpace(candidateEmail)) return;
        var username = candidateEmail.Trim().ToLowerInvariant();
        var attempts = _db.ExamAttempts.Where(a => a.ExamId == examId && a.CandidateUsername == username).ToList();
        _db.ExamAttempts.RemoveRange(attempts);
        _db.SaveChanges();
        _examSessionService.ResetSession(username, examId);
    }

    public void UnassignCandidateFromExam(string staffUsername, long examId, string candidateEmail)
    {
        ResetCandidateExamForRetake(staffUsername, examId, candidateEmail);
        var email = candidateEmail.Trim().ToLowerInvariant();
        var allowed = _db.ExamAllowedCandidates.FirstOrDefault(a =>
            a.ExamId == examId && a.Email.ToLower() == email);
        if (allowed != null)
        {
            _db.ExamAllowedCandidates.Remove(allowed);
            _db.SaveChanges();
        }
    }

    public void DeleteCandidateAccount(string staffUsername, long userId, string currentUsername)
    {
        var target = _db.Users.Find(userId) ?? throw new KeyNotFoundException();
        if (target.Role != Role.CANDIDATE)
            throw new ArgumentException("not_candidate");
        if (target.Username != null && target.Username.Equals(currentUsername, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("self_delete");
        var username = target.Username;
        var attempts = _db.ExamAttempts.Where(a => a.CandidateUsername == username).ToList();
        _db.ExamAttempts.RemoveRange(attempts);
        _db.SaveChanges();
        _examSessionService.ResetAllSessions(username);
        if (target.Email != null)
        {
            var allowed = _db.ExamAllowedCandidates.Where(a => a.Email.ToLower() == target.Email.ToLower()).ToList();
            _db.ExamAllowedCandidates.RemoveRange(allowed);
            _db.SaveChanges();
        }
        _db.Users.Remove(target);
        _db.SaveChanges();
    }

    public List<Dictionary<string, object?>> CandidateManageRowsForExam(long? examId)
    {
        if (examId == null) return [];
        var rows = new List<Dictionary<string, object?>>();
        foreach (var allowed in _db.ExamAllowedCandidates.Where(a => a.ExamId == examId).ToList())
        {
            var email = allowed.Email;
            var user = _db.Users.FirstOrDefault(u => u.Email == email);
            var attempts = _db.ExamAttempts.Where(a => a.ExamId == examId && a.CandidateUsername == email).ToList();
            var latest = CanonicalAttemptForCandidate(attempts);
            rows.Add(new Dictionary<string, object?>
            {
                ["email"] = email,
                ["fullName"] = allowed.FullName,
                ["userId"] = user?.Id,
                ["attemptId"] = latest?.Id,
                ["submitted"] = latest?.SubmittedAt != null,
                ["score"] = latest?.Score,
                ["objectiveScore"] = latest?.ObjectiveScore,
                ["essayScore"] = latest?.EssayScore,
                ["practicalScore"] = latest?.PracticalScore,
                ["invitationSent"] = allowed.InvitationSentAt != null,
                ["reminderSent"] = allowed.ReminderSentAt != null,
                ["needsEssayGrading"] = latest != null && latest.SubmittedAt != null && AttemptNeedsEssayGrading(latest.Id),
                ["statusKey"] = latest == null ? "not_started" : latest.SubmittedAt != null ? "done" : "in_progress"
            });
        }
        return rows;
    }

    public bool RevertUnusedPublishedExam(string staffUsername, long examId)
    {
        if (!CanManageExam(staffUsername, examId))
            return false;

        return _db.Exams.Where(e => e.Id == examId && e.Published
                && !_db.ExamAllowedCandidates.Any(candidate => candidate.ExamId == e.Id)
                && !_db.ExamAttempts.Any(attempt => attempt.ExamId == e.Id)
                && !_db.ExamSessionStates.Any(session => session.ExamId == e.Id))
            .ExecuteUpdate(row => row.SetProperty(e => e.Published, false)) == 1;
    }

    public bool DeleteDraftExamForStaff(string staffUsername, long examId)
    {
        if (!CanManageExam(staffUsername, examId))
            return false;

        return _db.Exams.Where(e => e.Id == examId && !e.Published
                && !_db.ExamAttempts.Any(a => a.ExamId == e.Id)
                && !_db.ExamSessionStates.Any(session => session.ExamId == e.Id))
            .ExecuteDelete() == 1;
    }

    public bool CanManageAttempt(string staffUsername, long attemptId)
    {
        var u = _db.Users.FirstOrDefault(x => x.Username == staffUsername);
        if (u == null) return false;
        var attempt = _db.ExamAttempts.Include(a => a.Exam).FirstOrDefault(a => a.Id == attemptId);
        if (attempt?.Exam == null) return false;
        var examId = attempt.Exam.Id;
        if (u.Role == Role.ADMIN) return true;
        if (u.Role == Role.EXAMINER) return CanManageExam(staffUsername, examId);
        if (u.Role == Role.GRADER) return CanGradeExam(staffUsername, examId);
        return false;
    }

    public int ImportQuestionsFromExcel(long examId, IFormFile file)
    {
        var exam = _db.Exams.Find(examId) ?? throw new KeyNotFoundException();
        EnsureBankEditable(exam);
        var pendingBank = exam.RandomQuestionCount.HasValue ? ReadBank(examId) : null;
        var imported = 0;
        try
        {
            using var stream = file.OpenReadStream();
            using var workbook = new XLWorkbook(stream);
            var sheet = workbook.Worksheet(1);
            var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 0;
            for (var i = 2; i <= lastRow; i++)
            {
                var row = sheet.Row(i);
                string Cell(int c) => row.Cell(c).GetFormattedString()?.Trim() ?? "";
                var text = Cell(1);
                var optionA = Cell(2);
                var optionB = Cell(3);
                var optionC = Cell(4);
                var optionD = Cell(5);
                var correctOption = Cell(6).ToUpperInvariant();
                var markRaw = Cell(7);
                var essayRaw = Cell(8);
                var durationRaw = Cell(9);
                var topicRaw = Cell(10);
                var difficultyRaw = Cell(11);
                var rubricRaw = Cell(12);

                var essay = essayRaw.Equals("true", StringComparison.OrdinalIgnoreCase)
                            || essayRaw == "1"
                            || essayRaw.Equals("yes", StringComparison.OrdinalIgnoreCase)
                            || essayRaw.Equals("مقالي", StringComparison.OrdinalIgnoreCase)
                            || essayRaw.Equals("essay", StringComparison.OrdinalIgnoreCase);

                if (string.IsNullOrWhiteSpace(text)) continue;
                if (!essay)
                {
                    if (string.IsNullOrWhiteSpace(optionA) || string.IsNullOrWhiteSpace(optionB)
                        || string.IsNullOrWhiteSpace(optionC) || string.IsNullOrWhiteSpace(optionD))
                        continue;
                    if (correctOption is not ("A" or "B" or "C" or "D"))
                        correctOption = "A";
                }

                var mark = 1;
                try { if (!string.IsNullOrWhiteSpace(markRaw)) mark = int.Parse(markRaw, CultureInfo.InvariantCulture); }
                catch { mark = 1; }

                int? durationSeconds = null;
                try
                {
                    if (!string.IsNullOrWhiteSpace(durationRaw))
                    {
                        var d = int.Parse(durationRaw, CultureInfo.InvariantCulture);
                        if (d >= 10) durationSeconds = d;
                    }
                }
                catch { durationSeconds = null; }

                var seconds = durationSeconds == null || durationSeconds < 10 ? 0 : durationSeconds.Value;
                if (seconds < 10) continue;
                if (pendingBank == null && ValidateQuestionBudget(examId, mark, seconds) != null) continue;
                pendingBank?.Add(new BankQuestion(essay, mark, seconds));

                var question = new Question
                {
                    Exam = exam,
                    ExamId = examId,
                    Text = text,
                    Essay = essay,
                    Mark = mark,
                    DurationSeconds = seconds,
                    Topic = string.IsNullOrWhiteSpace(topicRaw) ? null : topicRaw,
                    Difficulty = string.IsNullOrWhiteSpace(difficultyRaw) ? "متوسط" : difficultyRaw,
                    Rubric = string.IsNullOrWhiteSpace(rubricRaw) ? null : rubricRaw
                };
                if (essay)
                {
                    question.OptionA = question.OptionB = question.OptionC = question.OptionD = question.CorrectOption = null;
                }
                else
                {
                    question.OptionA = optionA;
                    question.OptionB = optionB;
                    question.OptionC = optionC;
                    question.OptionD = optionD;
                    question.CorrectOption = correctOption;
                }
                _db.Questions.Add(question);
                imported++;
            }
            if (pendingBank != null)
            {
                var error = DrawError(exam, pendingBank, complete: false);
                if (error != null)
                    throw new InvalidOperationException(error);
            }
            _db.SaveChanges();
        }
        catch (Exception ex) when (ex is IOException or ArgumentException)
        {
            throw new ArgumentException("تعذر قراءة ملف Excel", ex);
        }
        return imported;
    }

    /// <summary>Export all exam questions to Excel (same columns as import).</summary>
    public byte[] ExportQuestionsToExcel(long examId)
    {
        var questions = _db.Questions.Where(q => q.ExamId == examId).OrderBy(q => q.Id).ToList();
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add("Questions");
        sheet.Cell(1, 1).Value = "text";
        sheet.Cell(1, 2).Value = "optionA";
        sheet.Cell(1, 3).Value = "optionB";
        sheet.Cell(1, 4).Value = "optionC";
        sheet.Cell(1, 5).Value = "optionD";
        sheet.Cell(1, 6).Value = "correctOption";
        sheet.Cell(1, 7).Value = "mark";
        sheet.Cell(1, 8).Value = "essay";
        sheet.Cell(1, 9).Value = "durationSeconds";
        sheet.Cell(1, 10).Value = "topic";
        sheet.Cell(1, 11).Value = "difficulty";
        sheet.Cell(1, 12).Value = "rubric";
        sheet.Row(1).Style.Font.Bold = true;

        var r = 2;
        foreach (var q in questions)
        {
            sheet.Cell(r, 1).Value = q.Text;
            sheet.Cell(r, 2).Value = q.OptionA ?? "";
            sheet.Cell(r, 3).Value = q.OptionB ?? "";
            sheet.Cell(r, 4).Value = q.OptionC ?? "";
            sheet.Cell(r, 5).Value = q.OptionD ?? "";
            sheet.Cell(r, 6).Value = q.CorrectOption ?? "";
            sheet.Cell(r, 7).Value = q.Mark ?? 0;
            sheet.Cell(r, 8).Value = q.Essay ? "true" : "false";
            sheet.Cell(r, 9).Value = q.DurationSeconds ?? 0;
            sheet.Cell(r, 10).Value = q.Topic ?? "";
            sheet.Cell(r, 11).Value = q.Difficulty ?? "";
            sheet.Cell(r, 12).Value = q.Rubric ?? "";
            r++;
        }
        sheet.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }

    public int ImportExamCandidatesFromExcel(long examId, IFormFile file)
    {
        var exam = _db.Exams.Find(examId) ?? throw new KeyNotFoundException();
        var imported = 0;
        try
        {
            using var stream = file.OpenReadStream();
            using var workbook = new XLWorkbook(stream);
            var sheet = workbook.Worksheet(1);
            var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 0;
            for (var i = 2; i <= lastRow; i++)
            {
                var row = sheet.Row(i);
                var email = (row.Cell(1).GetFormattedString() ?? "").Trim().ToLowerInvariant();
                var fullName = (row.Cell(2).GetFormattedString() ?? "").Trim();
                var nationalId = (row.Cell(3).GetFormattedString() ?? "").Trim();
                if (string.IsNullOrWhiteSpace(email)) continue;
                if (_db.ExamAllowedCandidates.Any(a => a.ExamId == examId && a.Email.ToLower() == email)) continue;
                _db.ExamAllowedCandidates.Add(new ExamAllowedCandidate
                {
                    Exam = exam,
                    ExamId = examId,
                    Email = email,
                    FullName = string.IsNullOrWhiteSpace(fullName) ? null : fullName,
                    NationalId = string.IsNullOrWhiteSpace(nationalId) ? null : nationalId
                });
                imported++;
            }
            _db.SaveChanges();
        }
        catch (Exception ex) when (ex is IOException or ArgumentException)
        {
            throw new ArgumentException("تعذر قراءة ملف Excel", ex);
        }
        return imported;
    }

    public int ImportExamCandidatesFromExcelForStaff(string staffUsername, long examId, IFormFile file)
    {
        var staff = _db.Users.Include(u => u.Department).FirstOrDefault(u => u.Username == staffUsername);
        if (staff == null) return 0;
        if (staff.Role == Role.ADMIN) return ImportExamCandidatesFromExcel(examId, file);
        var exam = _db.Exams.Include(e => e.Department).FirstOrDefault(e => e.Id == examId)
                   ?? throw new KeyNotFoundException();
        if (staff.Department == null || exam.Department == null
            || staff.Department.Id != exam.Department.Id)
            return 0;
        var imported = 0;
        try
        {
            using var stream = file.OpenReadStream();
            using var workbook = new XLWorkbook(stream);
            var sheet = workbook.Worksheet(1);
            var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 0;
            for (var i = 2; i <= lastRow; i++)
            {
                var row = sheet.Row(i);
                var email = (row.Cell(1).GetFormattedString() ?? "").Trim().ToLowerInvariant();
                var fullName = (row.Cell(2).GetFormattedString() ?? "").Trim();
                var nationalId = (row.Cell(3).GetFormattedString() ?? "").Trim();
                if (string.IsNullOrWhiteSpace(email)) continue;
                if (_db.ExamAllowedCandidates.Any(a => a.ExamId == examId && a.Email.ToLower() == email)) continue;
                var candidate = _db.Users.FirstOrDefault(u => u.Email == email);
                _db.ExamAllowedCandidates.Add(new ExamAllowedCandidate
                {
                    Exam = exam,
                    ExamId = examId,
                    Email = email,
                    FullName = string.IsNullOrWhiteSpace(fullName) ? candidate?.FullName : fullName,
                    NationalId = string.IsNullOrWhiteSpace(nationalId) ? candidate?.NationalId : nationalId
                });
                imported++;
            }
            _db.SaveChanges();
        }
        catch (Exception ex) when (ex is IOException or ArgumentException)
        {
            throw new ArgumentException("تعذر قراءة ملف Excel", ex);
        }
        return imported;
    }

    public void AllowCandidateForExam(long examId, string email) =>
        AllowCandidateForExam(examId, email, null, null);

    public bool AllowCandidateForExam(long examId, string? email, string? fullName, string? nationalId)
    {
        if (string.IsNullOrWhiteSpace(email)) return false;
        var normalizedEmail = email.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalizedEmail)) return false;
        if (_db.ExamAllowedCandidates.Any(a => a.ExamId == examId && a.Email.ToLower() == normalizedEmail))
            return false;
        var exam = _db.Exams.Find(examId) ?? throw new KeyNotFoundException();
        var user = EnsureCandidateUser(normalizedEmail, fullName, nationalId);
        var allowed = new ExamAllowedCandidate
        {
            Exam = exam,
            ExamId = examId,
            Email = normalizedEmail
        };
        if (user != null)
        {
            allowed.FullName = user.FullName;
            allowed.NationalId = user.NationalId;
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(fullName)) allowed.FullName = fullName.Trim();
            if (!string.IsNullOrWhiteSpace(nationalId)) allowed.NationalId = nationalId.Trim();
        }
        _db.ExamAllowedCandidates.Add(allowed);
        _db.SaveChanges();
        _examNotificationService.SendInvitationToCandidate(examId, allowed);
        return true;
    }

    public string AllowCandidateForExamByStaff(string staffUsername, long examId, string email) =>
        AllowCandidateForExamByStaff(staffUsername, examId, email, null, null);

    public string AllowCandidateForExamByStaff(string staffUsername, long examId, string? email,
        string? fullName, string? nationalId)
    {
        var staff = _db.Users.FirstOrDefault(u => u.Username == staffUsername);
        if (staff == null || string.IsNullOrWhiteSpace(email)) return "invalid";
        if (staff.Role == Role.ADMIN)
            return AllowCandidateForExam(examId, email, fullName, nationalId) ? "ok" : "duplicate";
        if (!CanManageExam(staffUsername, examId)) return "forbidden";
        return AllowCandidateForExam(examId, email.Trim().ToLowerInvariant(), fullName, nationalId)
            ? "ok" : "duplicate";
    }

    private UserAccount? EnsureCandidateUser(string normalizedEmail, string? fullName, string? nationalId)
    {
        var existing = _db.Users.FirstOrDefault(u => u.Email == normalizedEmail);
        if (existing != null) return existing;
        if (string.IsNullOrWhiteSpace(nationalId)) return null;
        var normalizedNationalId = NationalIdNormalizer.Normalize(nationalId);
        if (string.IsNullOrWhiteSpace(normalizedNationalId)
            || _db.Users.Any(u => u.Username == normalizedEmail)
            || _db.Users.Any(u => u.NationalId == normalizedNationalId && u.Role == Role.CANDIDATE))
            return null;
        var u = new UserAccount
        {
            Username = normalizedEmail,
            Email = normalizedEmail,
            FullName = string.IsNullOrWhiteSpace(fullName) ? normalizedEmail : fullName.Trim(),
            NationalId = normalizedNationalId,
            Role = Role.CANDIDATE
        };
        u.Password = _passwordHasher.HashPassword(u, normalizedNationalId);
        _db.Users.Add(u);
        _db.SaveChanges();
        return u;
    }

    public bool AttemptHasEssayQuestions(long attemptId)
    {
        var attempt = _db.ExamAttempts.Include(a => a.Exam).FirstOrDefault(a => a.Id == attemptId);
        if (attempt?.Exam == null) return false;
        return _db.Questions.Any(q => q.ExamId == attempt.Exam.Id && q.Essay);
    }

    public bool AttemptNeedsEssayGrading(long attemptId)
    {
        var attempt = _db.ExamAttempts.Include(a => a.Exam).FirstOrDefault(a => a.Id == attemptId);
        if (attempt?.SubmittedAt == null || attempt.Exam == null) return false;
        var examId = attempt.Exam.Id;
        var essayQuestions = _db.Questions.Where(q => q.ExamId == examId && q.Essay).ToList();
        if (essayQuestions.Count == 0) return false;
        var awarded = ParseEssayAwarded(attempt.EssayScoreJson);
        foreach (var q in essayQuestions)
        {
            if (!awarded.ContainsKey(q.Id.ToString(CultureInfo.InvariantCulture)))
                return true;
        }
        return awarded.Count == 0;
    }

    public bool IsFinalScoreReady(ExamAttempt? attempt)
    {
        if (attempt?.SubmittedAt == null) return false;
        if (AttemptNeedsEssayGrading(attempt.Id)) return false;
        var exam = attempt.Exam ?? (attempt.ExamId != null ? _db.Exams.Find(attempt.ExamId) : null);
        return exam == null || !exam.PracticalEnabled || attempt.PracticalScore != null;
    }

    public List<Dictionary<string, object?>> ListEssayGradingQueue(string staffUsername)
    {
        var staff = _db.Users.FirstOrDefault(u => u.Username == staffUsername);
        if (staff == null) return [];
        List<long> examIds;
        if (staff.Role == Role.GRADER)
            examIds = _db.Exams.Where(e => e.AssignedGraderId == staff.Id).Select(e => e.Id).ToList();
        else if (staff.Role == Role.ADMIN)
            examIds = _db.Exams.Select(e => e.Id).ToList();
        else if (staff.Role == Role.EXAMINER)
            examIds = _db.Exams.Where(e => e.AssignedExaminerId == staff.Id).Select(e => e.Id).ToList();
        else
            return [];
        if (examIds.Count == 0) return [];
        var rows = new List<Dictionary<string, object?>>();
        foreach (var attempt in DedupeAttemptsByCandidate(
                     _db.ExamAttempts.Include(a => a.Exam)
                         .Where(a => a.ExamId != null && examIds.Contains(a.ExamId.Value))
                         .OrderByDescending(a => a.StartedAt).ToList()))
        {
            if (attempt.SubmittedAt == null || !AttemptNeedsEssayGrading(attempt.Id)) continue;
            if (staff.Role == Role.GRADER && !CanGradeExam(staffUsername, attempt.Exam!.Id)) continue;
            rows.Add(new Dictionary<string, object?>
            {
                ["attemptId"] = attempt.Id,
                ["candidateUsername"] = attempt.CandidateUsername,
                ["examTitle"] = attempt.Exam?.Title ?? "-",
                ["examId"] = attempt.Exam?.Id,
                ["submittedAt"] = attempt.SubmittedAt,
                ["objectiveScore"] = attempt.ObjectiveScore
            });
        }
        return rows;
    }

    public List<Dictionary<string, object?>> EssayReviewRows(long attemptId)
    {
        var attempt = _db.ExamAttempts.Include(a => a.Exam).FirstOrDefault(a => a.Id == attemptId)
                      ?? throw new KeyNotFoundException();
        var examId = attempt.Exam!.Id;
        var answers = ParseAnswersMap(attempt.AnswersJson);
        var awarded = ParseEssayAwarded(attempt.EssayScoreJson);
        return _db.Questions.Where(q => q.ExamId == examId && q.Essay).AsEnumerable().Select(q =>
        {
            var idKey = q.Id.ToString(CultureInfo.InvariantCulture);
            return new Dictionary<string, object?>
            {
                ["questionId"] = q.Id,
                ["text"] = q.Text,
                ["mark"] = q.Mark ?? 0,
                ["answer"] = answers.TryGetValue(idKey, out var ans) ? ans?.ToString() ?? "" : "",
                ["awarded"] = awarded.GetValueOrDefault(idKey, 0),
                ["rubric"] = q.Rubric,
                ["topic"] = q.Topic,
                ["difficulty"] = q.Difficulty
            };
        }).ToList();
    }

    /// <summary>
    /// Full per-candidate answers report for an exam (MCQ + essay text).
    /// Includes submitted attempts; assigned candidates without an attempt appear empty.
    /// </summary>
    public List<Dictionary<string, object?>> CandidateAnswersReport(string staffUsername, long examId)
    {
        if (!CanManageExam(staffUsername, examId))
            return [];

        var exam = GetExam(examId);
        if (exam == null) return [];

        var questions = _db.Questions.Where(q => q.ExamId == examId)
            .OrderBy(q => q.Id)
            .ToList();

        var attempts = DedupeAttemptsByCandidate(
                _db.ExamAttempts.Include(a => a.Exam)
                    .Where(a => a.ExamId == examId)
                    .OrderByDescending(a => a.StartedAt)
                    .ToList())
            .Where(a => a.SubmittedAt != null || !string.IsNullOrWhiteSpace(a.AnswersJson))
            .ToList();

        var byUser = attempts
            .Where(a => !string.IsNullOrWhiteSpace(a.CandidateUsername))
            .ToDictionary(a => a.CandidateUsername!.Trim().ToLowerInvariant(), a => a, StringComparer.OrdinalIgnoreCase);

        var allowed = _db.ExamAllowedCandidates.Where(a => a.ExamId == examId).ToList();
        var candidateKeys = new LinkedHashSet();
        foreach (var a in allowed)
        {
            var key = !string.IsNullOrWhiteSpace(a.Email) ? a.Email.Trim().ToLowerInvariant() : null;
            // Prefer matching attempt usernames; allowed list is email-based
            if (key != null) candidateKeys.Add(key);
        }
        foreach (var u in byUser.Keys)
            candidateKeys.Add(u);

        // If no allowed list, just use attempts
        if (candidateKeys.Count == 0)
        {
            foreach (var a in attempts)
                if (!string.IsNullOrWhiteSpace(a.CandidateUsername))
                    candidateKeys.Add(a.CandidateUsername.Trim().ToLowerInvariant());
        }

        var rows = new List<Dictionary<string, object?>>();
        var index = 0;
        foreach (var key in candidateKeys)
        {
            index++;
            byUser.TryGetValue(key, out var attempt);
            // Also try match by username containing email local-part or exact username
            if (attempt == null)
            {
                attempt = attempts.FirstOrDefault(a =>
                    string.Equals(a.CandidateUsername, key, StringComparison.OrdinalIgnoreCase));
            }

            var allowedRow = allowed.FirstOrDefault(a =>
                string.Equals(a.Email?.Trim(), key, StringComparison.OrdinalIgnoreCase)
                || string.Equals(a.NationalId?.Trim(), key, StringComparison.OrdinalIgnoreCase));

            var displayName = allowedRow?.FullName
                              ?? attempt?.CandidateUsername
                              ?? key;
            var username = attempt?.CandidateUsername ?? allowedRow?.Email ?? key;

            var answers = ParseAnswersMap(attempt?.AnswersJson);
            var awarded = ParseEssayAwarded(attempt?.EssayScoreJson);
            var answerRows = new List<Dictionary<string, object?>>();
            var qNum = 0;
            foreach (var q in QuestionsForReport(questions, attempt))
            {
                qNum++;
                var idKey = q.Id.ToString(CultureInfo.InvariantCulture);
                var raw = answers.TryGetValue(idKey, out var v) ? v?.ToString()?.Trim() ?? "" : "";
                string? selectedLabel = null;
                var isCorrect = (bool?)null;
                if (!q.Essay)
                {
                    selectedLabel = OptionLabel(q, raw);
                    if (!string.IsNullOrEmpty(raw) && !string.IsNullOrEmpty(q.CorrectOption))
                        isCorrect = raw.Equals(q.CorrectOption.Trim(), StringComparison.OrdinalIgnoreCase);
                }

                answerRows.Add(new Dictionary<string, object?>
                {
                    ["number"] = qNum,
                    ["questionId"] = q.Id,
                    ["text"] = q.Text,
                    ["type"] = q.Essay ? "ESSAY" : "MCQ",
                    ["mark"] = q.Mark ?? 0,
                    ["answer"] = raw,
                    ["answerLabel"] = q.Essay ? raw : (selectedLabel ?? (string.IsNullOrEmpty(raw) ? "—" : raw)),
                    ["correctOption"] = q.Essay ? null : q.CorrectOption,
                    ["correctLabel"] = q.Essay ? null : OptionLabel(q, q.CorrectOption),
                    ["isCorrect"] = isCorrect,
                    ["awarded"] = q.Essay
                        ? (awarded.TryGetValue(idKey, out var essayPts) ? essayPts : (int?)null)
                        : (isCorrect == true ? (q.Mark ?? 0) : (isCorrect == false ? 0 : null)),
                    ["optionA"] = q.OptionA,
                    ["optionB"] = q.OptionB,
                    ["optionC"] = q.OptionC,
                    ["optionD"] = q.OptionD
                });
            }

            rows.Add(new Dictionary<string, object?>
            {
                ["index"] = index,
                ["attemptId"] = attempt?.Id,
                ["username"] = username,
                ["fullName"] = displayName,
                ["nationalId"] = allowedRow?.NationalId,
                ["submittedAt"] = attempt?.SubmittedAt,
                ["startedAt"] = attempt?.StartedAt,
                ["status"] = attempt?.SubmittedAt != null ? "SUBMITTED" : (attempt != null ? "IN_PROGRESS" : "NOT_STARTED"),
                ["objectiveScore"] = attempt?.ObjectiveScore,
                ["essayScore"] = attempt?.EssayScore,
                ["practicalScore"] = attempt?.PracticalScore,
                ["totalScore"] = attempt?.Score,
                ["answers"] = answerRows
            });
        }

        return rows;
    }

    private static string? OptionLabel(Question q, string? optionCode)
    {
        if (string.IsNullOrWhiteSpace(optionCode)) return null;
        var code = optionCode.Trim();
        var text = code.ToUpperInvariant() switch
        {
            "A" => q.OptionA,
            "B" => q.OptionB,
            "C" => q.OptionC,
            "D" => q.OptionD,
            _ => null
        };
        return string.IsNullOrWhiteSpace(text) ? code : $"{code}) {text}";
    }

    private sealed class LinkedHashSet : IEnumerable<string>
    {
        private readonly Dictionary<string, byte> _map = new(StringComparer.OrdinalIgnoreCase);
        public int Count => _map.Count;
        public void Add(string item) => _map[item] = 0;
        public IEnumerator<string> GetEnumerator() => _map.Keys.GetEnumerator();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }

    public void GradeEssayAttempt(long attemptId, Dictionary<long, int> inputScores)
    {
        var attempt = _db.ExamAttempts.Include(a => a.Exam).FirstOrDefault(a => a.Id == attemptId)
                      ?? throw new KeyNotFoundException();
        var examId = attempt.Exam!.Id;
        var awarded = new Dictionary<string, int>();
        var essayTotal = 0;
        var essayQuestions = _db.Questions.Where(q => q.ExamId == examId && q.Essay).ToList();
        foreach (var q in essayQuestions)
        {
            var max = q.Mark ?? 0;
            // مطابقة مرنة للمفتاح (long / string)
            var val = 0;
            if (!inputScores.TryGetValue(q.Id, out val))
            {
                foreach (var kv in inputScores)
                {
                    if (kv.Key == q.Id) { val = kv.Value; break; }
                }
            }
            if (val < 0) val = 0;
            if (val > max) val = max;
            awarded[q.Id.ToString(CultureInfo.InvariantCulture)] = val;
            essayTotal += val;
        }
        attempt.EssayScore = essayTotal;
        RecomputeAttemptScore(attempt);
        try { attempt.EssayScoreJson = JsonSerializer.Serialize(awarded); }
        catch { attempt.EssayScoreJson = "{}"; }
        _db.ExamAttempts.Update(attempt);
        _db.SaveChanges();
    }

    private int CalculateObjectiveScore(ExamEntity exam, ExamAttempt attempt)
    {
        var answers = ParseAnswersMap(attempt.AnswersJson);
        var query = _db.Questions.AsNoTracking().Where(q => q.ExamId == exam.Id && !q.Essay);
        var ids = SelectedIds(attempt);
        if (ids != null)
            query = query.Where(q => ids.Contains(q.Id));

        var questions = query.Select(q => new { q.Id, q.Mark, q.CorrectOption }).ToList();
        var rawScore = questions.Sum(q => answers.TryGetValue(q.Id.ToString(CultureInfo.InvariantCulture), out var value)
            && q.CorrectOption != null
            && string.Equals(value?.ToString()?.Trim(), q.CorrectOption.Trim(), StringComparison.OrdinalIgnoreCase)
            ? q.Mark ?? 0 : 0);
        var rawMaximum = questions.Sum(q => q.Mark ?? 0);
        if (ids == null || exam.TotalMarks is not int total)
            return Math.Min(rawScore, rawMaximum);

        var essayMarks = _db.Questions.Where(q => q.ExamId == exam.Id && q.Essay && ids.Contains(q.Id))
            .Sum(q => q.Mark ?? 0);
        var objectiveMaximum = total - PracticalReservedMarks(exam) - essayMarks;
        return NormalizeDrawScore(rawScore, rawMaximum, objectiveMaximum);
    }

    private static Dictionary<string, object?> ParseAnswersMap(string? answersJson)
    {
        if (string.IsNullOrWhiteSpace(answersJson)) return new();
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, object?>>(answersJson) ?? new();
        }
        catch { return new(); }
    }

    private static Dictionary<string, int> ParseEssayAwarded(string? essayScoreJson)
    {
        if (string.IsNullOrWhiteSpace(essayScoreJson)) return new();
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, int>>(essayScoreJson) ?? new();
        }
        catch { return new(); }
    }

    public int CountQuestions(long examId) => _db.Questions.Count(q => q.ExamId == examId);

    public bool IsQuestionBankStepComplete(long? examId) =>
        examId != null && CountQuestions(examId.Value) >= 1;

    public bool IsExamBudgetFullyAllocated(long? examId)
    {
        if (examId == null || CountQuestions(examId.Value) < 1) return false;
        var exam = _db.Exams.Find(examId.Value);
        if (exam == null) return false;
        if (exam.RandomQuestionCount != null)
            return DrawError(exam, ReadBank(exam.Id), complete: true) == null;
        var budget = ExamQuestionBudget(examId.Value);
        if (exam.DurationMinutes is > 0 && budget.RemainingSeconds > 0) return false;
        if (exam.TotalMarks is > 0 && budget.RemainingMarks is > 0) return false;
        return true;
    }

    public bool HasAssignedCandidates(long? examId) =>
        examId != null && _db.ExamAllowedCandidates.Count(a => a.ExamId == examId) > 0;

    public bool IsExamMailEnabled() => _examNotificationService.MailConfigured();

    public void UpdateExamSchedule(string staffUsername, long examId, DateTime? scheduledAt, string? instructions)
    {
        if (!CanManageExam(staffUsername, examId))
            throw new ArgumentException("forbidden");
        var exam = _db.Exams.Find(examId) ?? throw new KeyNotFoundException();
        exam.ScheduledAt = scheduledAt;
        exam.Instructions = instructions?.Trim();
        _db.SaveChanges();
    }

    public int PublishExam(long examId)
    {
        var exam = _db.Exams.Find(examId) ?? throw new KeyNotFoundException();
        if (CountQuestions(examId) < 1 && exam.RandomQuestionCount == null)
            throw new ArgumentException("لا يمكن اعتماد اختبار بلا أسئلة");
        var error = exam.RandomQuestionCount == null ? null : DrawError(exam, ReadBank(examId), complete: true);
        if (error != null)
            throw new ArgumentException(error);
        _examNotificationService.ValidateReadyForPublish(exam);
        exam.Published = true;
        _db.SaveChanges();
        return _examNotificationService.SendInvitationsForExam(examId);
    }

    public List<ExamEntity> ListTemplateExams() =>
        _db.Exams.Where(e => e.Template).ToList();

    public ExamEntity CloneFromTemplate(long templateId, string creatorUsername, string title, int? durationMinutes,
        bool randomized, long? departmentId, string? opportunityType, string? jobTitle,
        string? companyName, string? city, int? opportunityYear,
        int? totalMarks, int? passThreshold,
        bool practicalEnabled, int? practicalMaxMarks)
    {
        var template = _db.Exams.Find(templateId) ?? throw new KeyNotFoundException();
        var usePractical = template.PracticalEnabled || practicalEnabled;
        var practicalMarks = template.PracticalMaxMarks ?? practicalMaxMarks;
        var exam = CreateExamForStaff(creatorUsername, title, durationMinutes, randomized, departmentId,
            opportunityType, jobTitle, companyName, city, opportunityYear, totalMarks, passThreshold,
            usePractical, practicalMarks);
        exam.CreationMode = "TEMPLATE";
        exam.RandomQuestionCount = template.RandomQuestionCount;
        exam.TotalMarks = template.TotalMarks ?? totalMarks;
        exam.PassThreshold = template.PassThreshold > 0 ? template.PassThreshold : 70;
        if (template.PracticalEnabled)
        {
            exam.PracticalEnabled = true;
            exam.PracticalMaxMarks = template.PracticalMaxMarks;
        }
        _db.SaveChanges();
        foreach (var src in _db.Questions.Where(q => q.ExamId == templateId).ToList())
        {
            var q = new Question
            {
                Exam = exam,
                ExamId = exam.Id,
                Text = src.Text,
                OptionA = src.OptionA,
                OptionB = src.OptionB,
                OptionC = src.OptionC,
                OptionD = src.OptionD,
                CorrectOption = src.CorrectOption,
                Mark = src.Mark,
                Essay = src.Essay,
                DurationSeconds = src.DurationSeconds,
                Topic = src.Topic,
                Difficulty = src.Difficulty,
                Rubric = src.Rubric
            };
            _db.Questions.Add(q);
            _db.SaveChanges();
            if (src.ImageData is { Length: > 0 })
            {
                q.ImageData = src.ImageData;
                q.ImageContentType = src.ImageContentType;
                try
                {
                    var ext = src.ImageContentType != null && src.ImageContentType.Contains("png", StringComparison.Ordinal) ? ".png"
                        : src.ImageContentType != null && src.ImageContentType.Contains("webp", StringComparison.Ordinal) ? ".webp"
                        : src.ImageContentType != null && src.ImageContentType.Contains("gif", StringComparison.Ordinal) ? ".gif"
                        : ".jpg";
                    var stored = new QuestionImageStorage.StoredImage(src.ImageData, src.ImageContentType ?? "image/jpeg", ext);
                    q.ImageFileName = _questionImageStorage.StoreFileAsync(exam.Id, q.Id, stored).GetAwaiter().GetResult();
                    _db.SaveChanges();
                }
                catch (IOException)
                {
                    _db.SaveChanges();
                }
            }
            else if (!string.IsNullOrWhiteSpace(src.ImageFileName))
            {
                try
                {
                    var ext = src.ImageFileName.Contains('.')
                        ? src.ImageFileName[src.ImageFileName.LastIndexOf('.')..]
                        : ".jpg";
                    _questionImageStorage.CopyFileAsync(templateId, src.ImageFileName, exam.Id, q.Id, ext)
                        .GetAwaiter().GetResult();
                    q.ImageFileName = q.Id + ext;
                    _db.SaveChanges();
                }
                catch (IOException)
                {
                    // Question saved without image if copy fails.
                }
            }
        }
        return exam;
    }

    public string LifecycleStatus(long examId)
    {
        var assigned = _db.ExamAllowedCandidates.Count(a => a.ExamId == examId);
        var attempts = _db.ExamAttempts.Where(a => a.ExamId == examId).ToList();
        var inProgress = attempts.Count(a => a.SubmittedAt == null);
        if (inProgress > 0) return "IN_PROGRESS";
        var completed = attempts.Count(a => a.SubmittedAt != null);
        if (assigned > 0 && completed >= assigned) return "FINISHED";
        if (attempts.Count == 0) return "NOT_STARTED";
        return completed > 0 ? "IN_PROGRESS" : "NOT_STARTED";
    }

    public List<Dictionary<string, object?>> ExamSummariesForStaff(string staffUsername, string? lifecycleFilter)
    {
        return ListExamsForStaff(staffUsername).Select(exam =>
        {
            var lifecycle = LifecycleStatus(exam.Id);
            var assigned = _db.ExamAllowedCandidates.Count(a => a.ExamId == exam.Id);
            var attempts = DedupeAttemptsByCandidate(_db.ExamAttempts.Where(a => a.ExamId == exam.Id).ToList());
            var hasNoAttemptsOrSessions = attempts.Count == 0
                && !_db.ExamSessionStates.Any(session => session.ExamId == exam.Id);
            var completed = attempts.Count(a => a.SubmittedAt != null);
            var active = attempts.Count(a => a.SubmittedAt == null);
            var notStarted = Math.Max(0, assigned - attempts.Count);
            var assignedExaminer = exam.AssignedExaminer;
            var assignedGrader = exam.AssignedGrader;
            return new Dictionary<string, object?>
            {
                ["exam"] = exam,
                ["questionCount"] = CountQuestions(exam.Id),
                ["lifecycle"] = lifecycle,
                ["lifecycleLabel"] = LifecycleLabel(lifecycle),
                ["canDeleteDraft"] = !exam.Published && hasNoAttemptsOrSessions,
                ["canRevertToDraft"] = exam.Published && assigned == 0 && hasNoAttemptsOrSessions,
                ["assignedCount"] = assigned,
                ["completedCount"] = completed,
                ["activeCount"] = active,
                ["notStartedCount"] = notStarted,
                ["assignedExaminerName"] = assignedExaminer != null
                    ? (assignedExaminer.FullName ?? assignedExaminer.Email) : null,
                ["assignedExaminerId"] = assignedExaminer?.Id,
                ["assignedGraderName"] = assignedGrader != null
                    ? (assignedGrader.FullName ?? assignedGrader.Email) : null,
                ["assignedGraderId"] = assignedGrader?.Id
            };
        }).Where(row => string.IsNullOrWhiteSpace(lifecycleFilter)
                        || string.Equals(lifecycleFilter, row["lifecycle"] as string, StringComparison.OrdinalIgnoreCase))
        .ToList();
    }

    public Dictionary<string, object?> ReportOverviewForStaff(string staffUsername, long? examId,
        DateTime? from, DateTime? to)
    {
        var overview = new Dictionary<string, object?>();
        var exams = examId != null
            ? ListExamsForStaff(staffUsername).Where(e => e.Id == examId).ToList()
            : ListExamsForStaff(staffUsername);
        var totalCandidates = 0;
        var completed = 0;
        var notCompleted = 0;
        var candidateRows = new List<Dictionary<string, object?>>();
        foreach (var exam in exams)
        {
            var assigned = _db.ExamAllowedCandidates.Count(a => a.ExamId == exam.Id);
            var attempts = DedupeAttemptsByCandidate(_db.ExamAttempts.Where(a => a.ExamId == exam.Id).ToList());
            var periodFilter = from != null || to != null;
            if (!periodFilter) totalCandidates += assigned;
            foreach (var a in attempts)
            {
                var submittedInPeriod = a.SubmittedAt != null && InReportPeriod(a.SubmittedAt, from, to);
                var startedInPeriod = InReportPeriod(a.StartedAt, from, to);
                if (periodFilter && !submittedInPeriod && !startedInPeriod) continue;
                if (periodFilter) totalCandidates++;
                candidateRows.Add(new Dictionary<string, object?>
                {
                    ["username"] = a.CandidateUsername,
                    ["examId"] = exam.Id,
                    ["examTitle"] = exam.Title,
                    ["score"] = a.Score,
                    ["objectiveScore"] = a.ObjectiveScore,
                    ["essayScore"] = a.EssayScore,
                    ["practicalScore"] = a.PracticalScore,
                    ["passThreshold"] = exam.PassThreshold > 0 ? exam.PassThreshold : 70,
                    ["submitted"] = a.SubmittedAt != null,
                    ["status"] = a.SubmittedAt != null ? "مكتمل" : "جاري",
                    ["attemptId"] = a.Id
                });
                if (a.SubmittedAt != null)
                {
                    if (!periodFilter || submittedInPeriod) completed++;
                    else notCompleted++;
                }
                else notCompleted++;
            }
            if (!periodFilter)
            {
                var completedForExam = attempts.Count(a => a.SubmittedAt != null);
                notCompleted += Math.Max(0, assigned - completedForExam);
            }
        }
        overview["totalCandidates"] = totalCandidates;
        overview["completedCount"] = completed;
        overview["notCompletedCount"] = notCompleted;
        overview["candidateRows"] = candidateRows;
        return overview;
    }

    private static bool InReportPeriod(DateTime? ts, DateTime? from, DateTime? to)
    {
        if (ts == null) return false;
        if (from == null && to == null) return true;
        if (from != null && ts < from) return false;
        if (to != null && ts > to) return false;
        return true;
    }

    private static string LifecycleLabel(string? code) => code switch
    {
        null => "—",
        "FINISHED" => "منتهي",
        "IN_PROGRESS" => "جاري",
        "NOT_STARTED" => "لم يبدأ بعد",
        _ => code
    };
}
