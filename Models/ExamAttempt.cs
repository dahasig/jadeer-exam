namespace Jadeer.Exam.Models;

public class ExamAttempt
{
    public long Id { get; set; }

    public long? ExamId { get; set; }
    public Exam? Exam { get; set; }

    public string? CandidateUsername { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.Now;
    public DateTime? SubmittedAt { get; set; }
    public int? Score { get; set; }
    public int ObjectiveScore { get; set; }
    public int EssayScore { get; set; }
    public int? PracticalScore { get; set; }
    public bool Suspicious { get; set; }
    public string? AnswersJson { get; set; }
    public string? EssayScoreJson { get; set; }
}
