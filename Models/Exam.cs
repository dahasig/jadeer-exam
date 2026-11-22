namespace Jadeer.Exam.Models;

public class Exam
{
    public long Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;

    public long? DepartmentId { get; set; }
    public Department? Department { get; set; }

    public long? AssignedExaminerId { get; set; }
    public UserAccount? AssignedExaminer { get; set; }

    public long? AssignedGraderId { get; set; }
    public UserAccount? AssignedGrader { get; set; }

    public int? DurationMinutes { get; set; }
    public bool Randomized { get; set; } = true;
    public int? RandomQuestionCount { get; set; }
    /** TRAINING | CONTRACT | RC */
    public string? OpportunityType { get; set; }
    public string? JobTitle { get; set; }
    public string? CompanyName { get; set; }
    public string? City { get; set; }
    public int? OpportunityYear { get; set; }
    public int? TotalMarks { get; set; }
    public bool PracticalEnabled { get; set; }
    public int? PracticalMaxMarks { get; set; }
    public int PassThreshold { get; set; } = 70;
    public bool Published { get; set; }
    public bool Template { get; set; }
    /** MANUAL | TEMPLATE | DEMO */
    public string CreationMode { get; set; } = "MANUAL";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? ScheduledAt { get; set; }
    public DateTime? ClosesAt { get; set; }
    public string? Instructions { get; set; }
    public DateTime? ExaminerReminderSentAt { get; set; }
    public DateTime? ExaminerCompletionReportSentAt { get; set; }

    public List<Question> Questions { get; set; } = new();
}
