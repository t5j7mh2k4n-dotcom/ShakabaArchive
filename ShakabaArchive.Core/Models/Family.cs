namespace ShakabaArchive.Models;

/// <summary>سجل أسرة خاص بمستخدم — أفراده يُحفظون محلياً ثم يُصدَّرون للسجل العام.</summary>
public class Family
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int OwnerUserId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public List<Person> Members { get; set; } = [];
}
