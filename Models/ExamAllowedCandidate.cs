namespace Jadeer.Exam.Models;

public class ExamAllowedCandidate
{
    public long Id { get; set; }

    public long ExamId { get; set; }
    public Exam Exam { get; set; } = null!;

    public string Email { get; set; } = string.Empty;
    public string? FullName { get; set; }
    public string? NationalId { get; set; }
    public DateTime? InvitationSentAt { get; set; }
    public DateTime? ReminderSentAt { get; set; }
}
