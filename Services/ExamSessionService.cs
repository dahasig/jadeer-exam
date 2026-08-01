using Jadeer.Exam.Data;
using Jadeer.Exam.Models;
using Microsoft.EntityFrameworkCore;

namespace Jadeer.Exam.Services;

using ExamEntity = global::Jadeer.Exam.Models.Exam;

public class ExamSessionService
{
    private static readonly TimeSpan SessionStaleAfter = TimeSpan.FromMinutes(5);

    private readonly AppDbContext _db;

    public ExamSessionService(AppDbContext db)
    {
        _db = db;
    }

    public record StartSessionResult(bool Ok, bool Conflict, bool Finished)
    {
        public static StartSessionResult Success() => new(true, false, false);
        public static StartSessionResult SessionConflict() => new(false, true, false);
        public static StartSessionResult AlreadyFinished() => new(false, false, true);
    }

    public async Task<StartSessionResult> StartOrResumeSessionAsync(string username, long examId, string clientSessionId,
        CancellationToken ct = default)
    {
        var exam = await _db.Exams.FindAsync([examId], ct)
                   ?? throw new KeyNotFoundException($"Exam {examId} not found");
        var existing = await _db.ExamSessionStates
            .FirstOrDefaultAsync(s => s.CandidateUsername == username && s.ExamId == examId, ct);

        var now = DateTime.Now;
        if (existing != null)
        {
            if (existing.Submitted)
            {
                return StartSessionResult.AlreadyFinished();
            }

            if (existing.ClientSessionId == clientSessionId)
            {
                existing.LastHeartbeat = now;
                await _db.SaveChangesAsync(ct);
                return StartSessionResult.Success();
            }

            var stale = existing.LastHeartbeat == default
                        || existing.LastHeartbeat < now - SessionStaleAfter;
            if (!stale)
            {
                return StartSessionResult.SessionConflict();
            }

            existing.ClientSessionId = clientSessionId;
            existing.LastHeartbeat = now;
            await _db.SaveChangesAsync(ct);
            return StartSessionResult.Success();
        }

        _db.ExamSessionStates.Add(new ExamSessionState
        {
            CandidateUsername = username,
            Exam = exam,
            ExamId = exam.Id,
            ClientSessionId = clientSessionId,
            LastHeartbeat = now
        });
        await _db.SaveChangesAsync(ct);
        return StartSessionResult.Success();
    }

    public async Task<bool> HeartbeatAsync(string username, long examId, string clientSessionId,
        CancellationToken ct = default)
    {
        var state = await _db.ExamSessionStates
            .FirstOrDefaultAsync(s => s.CandidateUsername == username && s.ExamId == examId, ct);
        if (state == null || state.Submitted || state.ClientSessionId != clientSessionId)
        {
            return false;
        }

        state.LastHeartbeat = DateTime.Now;
        await _db.SaveChangesAsync(ct);
        return true;
    }

    public async Task MarkSubmittedAsync(string username, long examId, CancellationToken ct = default)
    {
        var state = await _db.ExamSessionStates
            .FirstOrDefaultAsync(s => s.CandidateUsername == username && s.ExamId == examId, ct);
        if (state == null)
        {
            return;
        }

        state.Submitted = true;
        await _db.SaveChangesAsync(ct);
    }

    public async Task ClearSubmittedAsync(string username, long examId, CancellationToken ct = default)
    {
        var state = await _db.ExamSessionStates
            .FirstOrDefaultAsync(s => s.CandidateUsername == username && s.ExamId == examId, ct);
        if (state == null)
        {
            return;
        }

        state.Submitted = false;
        state.LastHeartbeat = DateTime.Now;
        await _db.SaveChangesAsync(ct);
    }

    public async Task ResetSessionAsync(string username, long examId, CancellationToken ct = default)
    {
        var state = await _db.ExamSessionStates
            .FirstOrDefaultAsync(s => s.CandidateUsername == username && s.ExamId == examId, ct);
        if (state == null)
        {
            return;
        }

        _db.ExamSessionStates.Remove(state);
        await _db.SaveChangesAsync(ct);
    }

    public async Task ResetAllSessionsAsync(string username, CancellationToken ct = default)
    {
        var sessions = await _db.ExamSessionStates
            .Where(s => s.CandidateUsername == username)
            .ToListAsync(ct);
        _db.ExamSessionStates.RemoveRange(sessions);
        await _db.SaveChangesAsync(ct);
    }

    // Sync wrappers matching Java method names for callers that prefer sync style.
    public StartSessionResult StartOrResumeSession(string username, long examId, string clientSessionId) =>
        StartOrResumeSessionAsync(username, examId, clientSessionId).GetAwaiter().GetResult();

    public bool Heartbeat(string username, long examId, string clientSessionId) =>
        HeartbeatAsync(username, examId, clientSessionId).GetAwaiter().GetResult();

    public void MarkSubmitted(string username, long examId) =>
        MarkSubmittedAsync(username, examId).GetAwaiter().GetResult();

    public void ClearSubmitted(string username, long examId) =>
        ClearSubmittedAsync(username, examId).GetAwaiter().GetResult();

    public void ResetSession(string username, long examId) =>
        ResetSessionAsync(username, examId).GetAwaiter().GetResult();

    public void ResetAllSessions(string username) =>
        ResetAllSessionsAsync(username).GetAwaiter().GetResult();
}
