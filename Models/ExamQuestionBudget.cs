namespace Jadeer.Exam.Models;

public record ExamQuestionBudget(
    int TotalSeconds,
    int UsedSeconds,
    int RemainingSeconds,
    int? TotalMarks,
    int PracticalReservedMarks,
    int UsedMarks,
    int? RemainingMarks);
