using Jadeer.Exam.Data;
using Jadeer.Exam.Models;
using Microsoft.EntityFrameworkCore;

namespace Jadeer.Exam.Services;

using ExamEntity = global::Jadeer.Exam.Models.Exam;

/// <summary>
/// Notification/mail façade used by ExamService. Full SMTP wiring can be added later;
/// mail methods are no-ops until configured.
/// </summary>
public class ExamNotificationService
{
    private readonly AppDbContext _db;
    private readonly bool _mailEnabled;

    public ExamNotificationService(AppDbContext db, IConfiguration configuration)
    {
        _db = db;
        _mailEnabled = configuration.GetValue("ExamMail:Enabled", false);
    }

    public bool MailConfigured() => _mailEnabled;

    public void ValidateReadyForPublish(ExamEntity exam)
    {
        if (!MailConfigured())
        {
            return;
        }

        if (exam.ScheduledAt == null)
        {
            throw new InvalidOperationException("schedule_required");
        }
    }

    public int SendInvitationsForExam(long examId)
    {
        if (!MailConfigured())
        {
            return 0;
        }

        // Stub: actual SMTP send not wired yet.
        return 0;
    }

    public void SendInvitationToCandidate(long examId, ExamAllowedCandidate allowed)
    {
        if (!MailConfigured() || allowed == null || allowed.InvitationSentAt != null)
        {
            return;
        }

        var exam = _db.Exams.Find(examId);
        if (exam == null || !exam.Published)
        {
            return;
        }

        // Stub: mark nothing until mail is configured.
    }
}
