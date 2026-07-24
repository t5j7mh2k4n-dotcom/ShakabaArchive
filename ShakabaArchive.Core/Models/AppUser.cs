namespace ShakabaArchive.Models;

/// <summary>مستخدم محلي على جهاز الأمين (SQLite).</summary>
public class AppUser
{
    public int Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public bool IsAdmin { get; set; }
    public string InviteCodeUsed { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>للتوافق مع الشاشات القديمة.</summary>
    public string UserName => Email;
}

/// <summary>رقم دعوة يمنحه الأمين للمستخدم الجديد قبل التسجيل.</summary>
public class InviteCode
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public bool IsUsed { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UsedAt { get; set; }
    public int? UsedByUserId { get; set; }
}
