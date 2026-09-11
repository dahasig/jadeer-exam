using Jadeer.Exam.Models;
using Microsoft.EntityFrameworkCore;

namespace Jadeer.Exam.Data;

using ExamEntity = global::Jadeer.Exam.Models.Exam;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<Department> Departments => Set<Department>();
    public DbSet<UserAccount> Users => Set<UserAccount>();
    public DbSet<ExamEntity> Exams => Set<ExamEntity>();
    public DbSet<Question> Questions => Set<Question>();
    public DbSet<ExamAllowedCandidate> ExamAllowedCandidates => Set<ExamAllowedCandidate>();
    public DbSet<ExamAttempt> ExamAttempts => Set<ExamAttempt>();
    public DbSet<SuspiciousEvent> SuspiciousEvents => Set<SuspiciousEvent>();
    public DbSet<ExamSessionState> ExamSessionStates => Set<ExamSessionState>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Department>(e =>
        {
            e.ToTable("departments");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(120).IsRequired();
            e.HasIndex(x => x.Name).IsUnique();
        });

        modelBuilder.Entity<UserAccount>(e =>
        {
            e.ToTable("users");
            e.HasKey(x => x.Id);
            e.Property(x => x.Username).IsRequired();
            e.HasIndex(x => x.Username).IsUnique();
            e.HasIndex(x => x.Email).IsUnique();
            e.Property(x => x.FullName).IsRequired();
            e.Property(x => x.Password).IsRequired();
            e.Property(x => x.Role).HasConversion<string>().IsRequired();
            e.HasOne(x => x.Department)
                .WithMany()
                .HasForeignKey(x => x.DepartmentId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<ExamEntity>(e =>
        {
            e.ToTable("exams");
            e.HasKey(x => x.Id);
            e.Property(x => x.Title).IsRequired();
            e.Property(x => x.Subject).IsRequired();
            e.Property(x => x.Instructions).HasMaxLength(4000);
            e.HasOne(x => x.Department)
                .WithMany()
                .HasForeignKey(x => x.DepartmentId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.AssignedExaminer)
                .WithMany()
                .HasForeignKey(x => x.AssignedExaminerId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.AssignedGrader)
                .WithMany()
                .HasForeignKey(x => x.AssignedGraderId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasMany(x => x.Questions)
                .WithOne(q => q.Exam!)
                .HasForeignKey(q => q.ExamId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Question>(e =>
        {
            e.ToTable("questions");
            e.HasKey(x => x.Id);
            e.Property(x => x.Text).HasMaxLength(2000).IsRequired();
            e.Property(x => x.Rubric).HasMaxLength(1000);
            e.Property(x => x.ImageContentType).HasMaxLength(64);
            e.Property(x => x.ImageData);
        });

        modelBuilder.Entity<ExamAllowedCandidate>(e =>
        {
            e.ToTable("exam_allowed_candidates");
            e.HasKey(x => x.Id);
            e.Property(x => x.Email).HasMaxLength(190).IsRequired();
            e.Property(x => x.FullName).HasMaxLength(200);
            e.Property(x => x.NationalId).HasMaxLength(50);
            e.HasIndex(x => new { x.ExamId, x.Email }).IsUnique()
                .HasDatabaseName("uk_exam_allowed_candidate_exam_email");
            e.HasOne(x => x.Exam)
                .WithMany()
                .HasForeignKey(x => x.ExamId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ExamAttempt>(e =>
        {
            e.ToTable("exam_attempts");
            e.HasKey(x => x.Id);
            e.Property(x => x.AnswersJson).HasColumnType("longtext");
            e.Property(x => x.EssayScoreJson).HasColumnType("longtext");
            e.HasOne(x => x.Exam)
                .WithMany()
                .HasForeignKey(x => x.ExamId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SuspiciousEvent>(e =>
        {
            e.ToTable("suspicious_events");
            e.HasKey(x => x.Id);
            e.Property(x => x.Details).HasMaxLength(2000);
        });

        modelBuilder.Entity<ExamSessionState>(e =>
        {
            e.ToTable("exam_session_state");
            e.HasKey(x => x.Id);
            e.Property(x => x.CandidateUsername).HasMaxLength(120).IsRequired();
            e.Property(x => x.ClientSessionId).HasMaxLength(64).IsRequired();
            e.HasIndex(x => new { x.CandidateUsername, x.ExamId }).IsUnique();
            e.HasOne(x => x.Exam)
                .WithMany()
                .HasForeignKey(x => x.ExamId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
