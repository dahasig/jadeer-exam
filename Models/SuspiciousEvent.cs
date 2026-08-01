namespace Jadeer.Exam.Models;

public class SuspiciousEvent
{
    public long Id { get; set; }
    public string? Username { get; set; }
    public string? EventType { get; set; }
    public string? Severity { get; set; }
    public string? Details { get; set; }
    public long? ExamId { get; set; }
    public int? ConfidenceScore { get; set; }
    public DateTime EventTime { get; set; } = DateTime.Now;
}
