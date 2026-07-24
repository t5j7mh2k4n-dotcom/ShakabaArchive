namespace ShakabaArchive.Models;

public enum EventType
{
    Birth = 0,
    Marriage = 1,
    Divorce = 2,
    Death = 3,
    Migration = 4,
    Other = 5,
    Graduation = 6,
    Engagement = 7,
    JoyCeremony = 8,
    Condolence = 9
}

public enum EventMood
{
    Joy = 0,
    Sorrow = 1,
    Neutral = 2
}

public static class EventTypeLabels
{
    public static string ToArabic(EventType type) => type switch
    {
        EventType.Birth => "مولود جديد / ميلاد",
        EventType.Marriage => "زواج",
        EventType.Divorce => "طلاق",
        EventType.Death => "وفاة",
        EventType.Migration => "هجرة / انتقال",
        EventType.Graduation => "تخرج",
        EventType.Engagement => "خطوبة",
        EventType.JoyCeremony => "مناسبة فرح عامة",
        EventType.Condolence => "عزاء / مناسبة ترح",
        EventType.Other => "مناسبة أخرى",
        _ => type.ToString()
    };

    public static EventMood MoodOf(EventType type) => type switch
    {
        EventType.Death or EventType.Condolence or EventType.Divorce => EventMood.Sorrow,
        EventType.Migration or EventType.Other => EventMood.Neutral,
        _ => EventMood.Joy
    };

    public static string MoodArabic(EventMood mood) => mood switch
    {
        EventMood.Joy => "أفراح",
        EventMood.Sorrow => "أتراح",
        _ => "عامة"
    };

    public static IReadOnlyList<(EventType Value, string Label, EventMood Mood)> All { get; } =
    [
        (EventType.Birth, "مولود جديد / ميلاد", EventMood.Joy),
        (EventType.Engagement, "خطوبة", EventMood.Joy),
        (EventType.Marriage, "زواج", EventMood.Joy),
        (EventType.Graduation, "تخرج", EventMood.Joy),
        (EventType.JoyCeremony, "مناسبة فرح عامة", EventMood.Joy),
        (EventType.Death, "وفاة", EventMood.Sorrow),
        (EventType.Migration, "هجرة / انتقال", EventMood.Neutral),
        (EventType.Other, "مناسبة أخرى", EventMood.Neutral)
    ];
}
