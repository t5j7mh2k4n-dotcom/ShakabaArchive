namespace ShakabaArchive.Models;

public enum UserRole
{
    /// <summary>مدخل بيانات — لا يُحفظ إلا بعد موافقة أحد الثلاثة.</summary>
    Editor = 0,
    /// <summary>أحد الثلاثة الموافقين على صحة البيانات والحفظ.</summary>
    Approver = 1,
    /// <summary>الأدمن الرئيسي — يضيف المستخدمين والثلاثة الموافقين.</summary>
    Admin = 2
}

public enum ChangeAction
{
    Create = 0,
    Update = 1,
    Delete = 2
}

public enum ChangeEntity
{
    Person = 0,
    LifeEvent = 1,
    /// <summary>تسجيل حساب مستخدم جديد عبر الموقع.</summary>
    UserAccount = 2
}

public enum ChangeStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2
}

public class PendingChange
{
    public int Id { get; set; }
    public ChangeEntity EntityType { get; set; }
    public ChangeAction Action { get; set; }
    public int? EntityId { get; set; }
    public string PayloadJson { get; set; } = "{}";
    public string Summary { get; set; } = string.Empty;
    public ChangeStatus Status { get; set; } = ChangeStatus.Pending;
    public int SubmittedByUserId { get; set; }
    public string SubmittedByName { get; set; } = string.Empty;
    public int? ReviewedByUserId { get; set; }
    public string? ReviewedByName { get; set; }
    public string ReviewNote { get; set; } = string.Empty;
    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ReviewedAt { get; set; }
}
