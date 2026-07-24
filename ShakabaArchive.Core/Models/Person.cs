namespace ShakabaArchive.Models;

public class Person
{
    public int Id { get; set; }
    public string NationalId { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string FatherName { get; set; } = string.Empty;
    public string MotherName { get; set; } = string.Empty;
    /// <summary>حقل قديم — لم يعد يُعرض.</summary>
    public string Nationality { get; set; } = "";
    public string Gender { get; set; } = "ذكر";
    public DateTime? BirthDate { get; set; }
    public string BirthPlace { get; set; } = "الشكابة شاع الدين";
    public string Residence { get; set; } = "الشكابة شاع الدين";

    /// <summary>حقل قديم — لم يعد يُعرض.</summary>
    public string Tribe { get; set; } = string.Empty;

    /// <summary>الحي أو الحلة داخل الشكابة شاع الدين.</summary>
    public string Neighborhood { get; set; } = string.Empty;

    public string Phone { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;

    /// <summary>مسار نسبي لصورة الوثيقة (هوية / شهادة).</summary>
    public string DocumentImagePath { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public List<LifeEvent> Events { get; set; } = [];
}
