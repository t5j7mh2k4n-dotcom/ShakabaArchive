namespace ShakabaArchive.Models;

/// <summary>رمز مؤقت لاستعادة كلمة المرور.</summary>
public class PasswordResetToken
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Token { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool Used { get; set; }
}
