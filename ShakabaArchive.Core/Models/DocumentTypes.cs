namespace ShakabaArchive.Models;

public static class DocumentTypes
{
    public const string NationalId = "رقم وطني";
    public const string Passport = "جواز سفر";
    public const string Nationality = "جنسية";
    public const string BirthCertificate = "شهادة ميلاد";
    public const string AgeCertificate = "شهادة تسنين";

    public static readonly string[] All =
    [
        NationalId,
        Passport,
        Nationality,
        BirthCertificate,
        AgeCertificate
    ];

    public static bool IsKnown(string? value) =>
        !string.IsNullOrWhiteSpace(value) && All.Contains(value.Trim());
}
