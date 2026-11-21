using Jadeer.Exam.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Jadeer.Exam.Data;

public static class DbSeeder
{
    public static void Seed(IServiceProvider services) =>
        TrySeed(services, null);

    /// <summary>Creates schema + demo users. Retries for slow MySQL startup (Railway).</summary>
    public static bool TrySeed(IServiceProvider services, ILogger? logger)
    {
        const int maxAttempts = 20;
        Exception? last = null;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                using var scope = services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var hasher = scope.ServiceProvider.GetRequiredService<PasswordHasher<UserAccount>>();

                db.Database.EnsureCreated();
                EnsureRandomQuestionColumns(db);
                WidenAnswerColumns(db, logger);
                SeedUsersOnly(db, hasher);
                logger?.LogInformation("Database ready (attempt {Attempt}/{Max}).", attempt, maxAttempts);
                return true;
            }
            catch (Exception ex)
            {
                last = ex;
                logger?.LogWarning(ex,
                    "Database not ready (attempt {Attempt}/{Max}): {Message}",
                    attempt, maxAttempts, ex.Message);
                if (attempt < maxAttempts)
                    Thread.Sleep(TimeSpan.FromSeconds(3));
            }
        }

        logger?.LogError(last, "Database seed failed after {Max} attempts.", maxAttempts);
        return false;
    }

    public static void SeedUsersOnly(AppDbContext db, PasswordHasher<UserAccount> hasher)
    {
        SeedUser(db, hasher, "admin", "System Admin", "Admin@123", Role.ADMIN);
        SeedUser(db, hasher, "examiner", "Examiner User", "Examiner@123", Role.EXAMINER);
        SeedUser(db, hasher, "grader", "Grader User", "Grader@123", Role.GRADER);
        SeedUser(db, hasher, "candidate", "Candidate User", "Candidate@123", Role.CANDIDATE);
    }

    private static void EnsureRandomQuestionColumns(AppDbContext db)
    {
        if (db.Database.SqlQueryRaw<long>(
                "SELECT CAST(COUNT(*) AS SIGNED) AS Value FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'exams' AND COLUMN_NAME = 'RandomQuestionCount'")
            .AsEnumerable().Single() == 0)
            db.Database.ExecuteSqlRaw("ALTER TABLE exams ADD COLUMN RandomQuestionCount INT NULL");

        if (db.Database.SqlQueryRaw<long>(
                "SELECT CAST(COUNT(*) AS SIGNED) AS Value FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'exam_attempts' AND COLUMN_NAME = 'SelectedQuestionIdsJson'")
            .AsEnumerable().Single() == 0)
            db.Database.ExecuteSqlRaw("ALTER TABLE exam_attempts ADD COLUMN SelectedQuestionIdsJson LONGTEXT NULL");
    }

    /// <summary>Allow full essay/MCQ answer payloads (was VARCHAR 4000).</summary>
    private static void WidenAnswerColumns(AppDbContext db, ILogger? logger)
    {
        try
        {
            db.Database.ExecuteSqlRaw("ALTER TABLE exam_attempts MODIFY COLUMN AnswersJson LONGTEXT NULL");
            db.Database.ExecuteSqlRaw("ALTER TABLE exam_attempts MODIFY COLUMN EssayScoreJson LONGTEXT NULL");
        }
        catch (Exception ex)
        {
            logger?.LogDebug(ex, "AnswersJson column widen skipped (already longtext or unsupported).");
        }
    }

    private static void SeedUser(
        AppDbContext db,
        PasswordHasher<UserAccount> hasher,
        string username,
        string fullName,
        string password,
        Role role)
    {
        if (db.Users.Any(u => u.Username == username))
            return;

        var user = new UserAccount
        {
            Username = username,
            FullName = fullName,
            Email = $"{username}@local.test",
            Role = role,
            MfaEnabled = false
        };
        user.Password = hasher.HashPassword(user, password);
        db.Users.Add(user);
        db.SaveChanges();
    }
}
