namespace Jadeer.Exam.Models;

public class ExamSessionState
{
    public long Id { get; set; }
    public string CandidateUsername { get; set; } = string.Empty;

    public long ExamId { get; set; }
    public Exam Exam { get; set; } = null!;

    public string ClientSessionId { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime LastHeartbeat { get; set; } = DateTime.Now;
    public bool Submitted { get; set; }
}
