namespace ShakabaArchive.Models;

/// <summary>ملف مرفوع (صورة/وثيقة) يُحفظ في Neon حتى لا يختفي على Render.</summary>
public class MediaFile
{
    /// <summary>اسم الملف كما في PhotoPath / DocumentImagePath (مثل guid.jpg).</summary>
    public string Id { get; set; } = "";

    public string ContentType { get; set; } = "application/octet-stream";
    public byte[] Data { get; set; } = [];
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
