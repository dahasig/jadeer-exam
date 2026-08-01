using Jadeer.Exam.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Jadeer.Exam.Data;

public static class DbSeeder
{
    public static void Seed(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hasher = scope.ServiceProvider.GetRequiredService<PasswordHasher<UserAccount>>();

        db.Database.EnsureCreated();

        SeedUser(db, hasher, "admin", "System Admin", "Admin@123", Role.ADMIN);
        SeedUser(db, hasher, "examiner", "Examiner User", "Examiner@123", Role.EXAMINER);
        SeedUser(db, hasher, "grader", "Grader User", "Grader@123", Role.GRADER);
        SeedUser(db, hasher, "candidate", "Candidate User", "Candidate@123", Role.CANDIDATE);
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
