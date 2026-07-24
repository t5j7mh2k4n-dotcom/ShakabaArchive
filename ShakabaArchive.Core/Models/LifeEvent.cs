namespace ShakabaArchive.Models;

public class LifeEvent
{
    public int Id { get; set; }
    public int PersonId { get; set; }
    public Person? Person { get; set; }

    public EventType Type { get; set; } = EventType.Other;
    public EventMood Mood { get; set; } = EventMood.Joy;
    public DateTime? EventDate { get; set; }
    public string Place { get; set; } = "الشكابة شاع الدين";
    public string Title { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;

    /// <summary>الطرف الآخر / الزوج / ذو صلة.</summary>
    public string RelatedPersonName { get; set; } = string.Empty;
    public string RelatedFatherName { get; set; } = string.Empty;
    public string RelatedPhone { get; set; } = string.Empty;

    /// <summary>بيانات مولود جديد إن وُجدت.</summary>
    public string ChildFullName { get; set; } = string.Empty;
    public string ChildGender { get; set; } = string.Empty;
    public string MotherName { get; set; } = string.Empty;

    /// <summary>بيانات تخرج.</summary>
    public string Institution { get; set; } = string.Empty;
    public string Specialty { get; set; } = string.Empty;
    public string Degree { get; set; } = string.Empty;

    public string SourceNote { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
