namespace Jadeer.Exam.Models;

public class MonitoringEventDto
{
    public string? EventType { get; set; }
    public string? Severity { get; set; }
    public string? Details { get; set; }
    public long? ExamId { get; set; }
    public int? ConfidenceScore { get; set; }
}
