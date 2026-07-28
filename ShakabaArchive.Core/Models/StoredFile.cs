namespace ShakabaArchive.Models;

/// <summary>ملف مرفوع (صورة شخصية أو وثيقة) — يُخزَّن في قاعدة البيانات ليبقى بعد نشر Render.</summary>
public class StoredFile
{
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/octet-stream";
    public byte[] Content { get; set; } = [];
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
