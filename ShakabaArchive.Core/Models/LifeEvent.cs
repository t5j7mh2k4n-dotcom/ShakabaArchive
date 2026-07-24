namespace ShakabaArchive.Models;

public class LifeEvent
{
    public int Id { get; set; }
    public int PersonId { get; set; }
    public Person? Person { get; set; }

    public EventType Type { get; set; } = EventType.Other;
    public DateTime? EventDate { get; set; }
    public string Place { get; set; } = "الشكابة شاع الدين";
    public string Title { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
    public string RelatedPersonName { get; set; } = string.Empty;
    public string SourceNote { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
