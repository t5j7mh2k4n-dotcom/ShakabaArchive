using ShakabaArchive.Models;

namespace ShakabaArchive.Web;

public sealed record PersonBarcodeView(string Value, bool Compact = false, string? Title = null)
{
    public static PersonBarcodeView ForPerson(Person person, bool compact = false) =>
        new(EncodeRegistry(person), compact, compact ? null : "باركود كود السجل");

    public static PersonBarcodeView ForDocument(Person person) =>
        new(EncodeDocument(person)!, false, "باركود رقم الوثيقة");

    public static string EncodeRegistry(Person person)
    {
        if (!string.IsNullOrWhiteSpace(person.RegistryCode))
        {
            return $"SHK-{person.RegistryCode.Trim()}";
        }

        return $"SHK-{person.Id:D6}";
    }

    public static string? EncodeDocument(Person person)
    {
        var doc = (person.DocumentNumber ?? person.NationalId ?? "").Trim();
        if (string.IsNullOrWhiteSpace(doc))
        {
            return null;
        }

        var clean = new string(doc.Where(static c => char.IsLetterOrDigit(c) || c == '-').ToArray());
        return string.IsNullOrWhiteSpace(clean) ? null : $"DOC-{clean}";
    }
}

public sealed record PersonCodesCardView(
    string FullName,
    string RegistryBarcode,
    string? DocumentBarcode,
    string DetailsUrl)
{
    public static PersonCodesCardView ForPerson(Person person, string detailsUrl) =>
        new(
            person.FullName,
            PersonBarcodeView.EncodeRegistry(person),
            PersonBarcodeView.EncodeDocument(person),
            detailsUrl);
}
