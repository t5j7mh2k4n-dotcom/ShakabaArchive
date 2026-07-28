namespace ShakabaArchive.Web.Services;

/// <summary>إعدادات Firebase Authentication (للتسجيل والدخول).</summary>
public class FirebaseOptions
{
    public const string SectionName = "Firebase";

    public string ApiKey { get; set; } = "";
    public string AuthDomain { get; set; } = "";
    public string ProjectId { get; set; } = "";
    public string StorageBucket { get; set; } = "";
    public string MessagingSenderId { get; set; } = "";
    public string AppId { get; set; } = "";
    public string MeasurementId { get; set; } = "";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);
}
