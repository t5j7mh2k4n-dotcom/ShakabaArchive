namespace ShakabaArchive.Models;

/// <summary>سجل شخص في موديول سجل الأشخاص — بترميز هرمي للمستويات.</summary>
public class Person
{
    public int Id { get; set; }

    /// <summary>الترميز الهرمي: المستوى 1 = 01، الثاني = 01001، الثالث = 01001001.</summary>
    public string RegistryCode { get; set; } = string.Empty;

    /// <summary>1 أو 2 أو 3.</summary>
    public int HierarchyLevel { get; set; } = 1;

    /// <summary>معرف الأب في الشجرة (للمستويين 2 و 3).</summary>
    public int? ParentPersonId { get; set; }

    public Person? ParentPerson { get; set; }
    public List<Person> Children { get; set; } = [];

    /// <summary>نوع الوثيقة: رقم وطني، جواز سفر، جنسية، شهادة ميلاد، شهادة تسنين.</summary>
    public string DocumentType { get; set; } = DocumentTypes.NationalId;

    /// <summary>رقم الوثيقة.</summary>
    public string DocumentNumber { get; set; } = string.Empty;

    /// <summary>للتوافق مع البحث القديم — يُزامن مع رقم الوثيقة.</summary>
    public string NationalId { get; set; } = string.Empty;

    public string FirstName { get; set; } = string.Empty;
    public string FatherName { get; set; } = string.Empty;
    public string GrandfatherName { get; set; } = string.Empty;
    public string FamilyName { get; set; } = string.Empty;

    /// <summary>الاسم الكامل المركّب للعرض والبحث.</summary>
    public string FullName { get; set; } = string.Empty;

    public string MotherName { get; set; } = string.Empty;
    public string Nationality { get; set; } = "";
    public string Gender { get; set; } = "ذكر";
    public DateTime? BirthDate { get; set; }
    public string BirthPlace { get; set; } = "الشكابة شاع الدين";
    public string Residence { get; set; } = "الشكابة شاع الدين";
    public string Tribe { get; set; } = string.Empty;
    public string Profession { get; set; } = string.Empty;
    public string Neighborhood { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;

    /// <summary>هل الشخص مقترب (مقيم خارج البلد)؟</summary>
    public bool IsMigrant { get; set; }

    /// <summary>دولة المهجر للمقترب.</summary>
    public string MigrationCountry { get; set; } = string.Empty;

    /// <summary>مدينة المهجر للمقترب.</summary>
    public string MigrationCity { get; set; } = string.Empty;

    public string Notes { get; set; } = string.Empty;

    /// <summary>مسار الصورة الشخصية المعروضة مع بيانات الشخص.</summary>
    public string PhotoPath { get; set; } = string.Empty;

    /// <summary>مسار صورة الوثيقة (هوية / شهادة).</summary>
    public string DocumentImagePath { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public List<LifeEvent> Events { get; set; } = [];

    public static string ComposeFullName(string first, string father, string grandfather, string family)
    {
        var parts = new[] { first, father, grandfather, family }
            .Select(x => (x ?? "").Trim())
            .Where(x => x.Length > 0 && x != "—");
        return string.Join(" ", parts);
    }

    public void RefreshFullName() =>
        FullName = ComposeFullName(FirstName, FatherName, GrandfatherName, FamilyName);
}
