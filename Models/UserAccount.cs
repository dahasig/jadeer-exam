namespace Jadeer.Exam.Models;

public class UserAccount
{
    public long Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? NationalId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public Role Role { get; set; }

    public bool MfaEnabled { get; set; }
    public string? MfaSecret { get; set; }

    public long? DepartmentId { get; set; }
    public Department? Department { get; set; }
}
