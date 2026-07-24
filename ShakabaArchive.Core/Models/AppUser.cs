namespace ShakabaArchive.Models;

/// <summary>مستخدم النظام (محلي أو Neon حسب الإعداد).</summary>
public class AppUser
{
    public int Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public bool IsAdmin { get; set; }
    public UserRole Role { get; set; } = UserRole.Editor;
    public string InviteCodeUsed { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public string UserName => Email;
    public bool CanApprove => IsAdmin || Role is UserRole.Admin or UserRole.Approver;
    public bool IsEditorOnly => !CanApprove;
}

/// <summary>رقم دعوة يمنحه الأمين للمستخدم الجديد قبل التسجيل.</summary>
public class InviteCode
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
    public UserRole AssignRole { get; set; } = UserRole.Editor;
    public bool IsUsed { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UsedAt { get; set; }
    public int? UsedByUserId { get; set; }
}
