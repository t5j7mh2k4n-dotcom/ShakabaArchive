namespace ShakabaArchive.Models;

public enum EventType
{
    Birth = 0,
    Marriage = 1,
    Divorce = 2,
    Death = 3,
    Migration = 4,
    Other = 5
}

public static class EventTypeLabels
{
    public static string ToArabic(EventType type) => type switch
    {
        EventType.Birth => "ميلاد",
        EventType.Marriage => "زواج",
        EventType.Divorce => "طلاق",
        EventType.Death => "وفاة",
        EventType.Migration => "هجرة / انتقال",
        EventType.Other => "مناسبة أخرى",
        _ => type.ToString()
    };

    public static IReadOnlyList<(EventType Value, string Label)> All { get; } =
    [
        (EventType.Birth, "ميلاد"),
        (EventType.Marriage, "زواج"),
        (EventType.Divorce, "طلاق"),
        (EventType.Death, "وفاة"),
        (EventType.Migration, "هجرة / انتقال"),
        (EventType.Other, "مناسبة أخرى")
    ];
}
