namespace Jadeer.Exam.Models;

public class Question
{
    public long Id { get; set; }

    public long? ExamId { get; set; }
    public Exam? Exam { get; set; }

    public string Text { get; set; } = string.Empty;
    public string? OptionA { get; set; }
    public string? OptionB { get; set; }
    public string? OptionC { get; set; }
    public string? OptionD { get; set; }
    public string? CorrectOption { get; set; }
    public int? Mark { get; set; }
    public bool Essay { get; set; }
    public int? DurationSeconds { get; set; }
    public string? Topic { get; set; }
    public string? Difficulty { get; set; }
    public string? Rubric { get; set; }

    public string? ImageFileName { get; set; }
    public byte[]? ImageData { get; set; }
    public string? ImageContentType { get; set; }

    public bool HasImage =>
        !string.IsNullOrWhiteSpace(ImageFileName) || !string.IsNullOrWhiteSpace(ImageContentType);
}
