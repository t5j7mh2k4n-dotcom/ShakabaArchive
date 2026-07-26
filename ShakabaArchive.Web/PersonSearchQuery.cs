using Microsoft.EntityFrameworkCore;
using ShakabaArchive.Models;

namespace ShakabaArchive.Web;

public static class PersonSearchQuery
{
    public const string FieldAll = "all";

    public static IReadOnlyList<(string Value, string Label)> Fields { get; } =
    [
        (FieldAll, "كل البيانات"),
        ("fullName", "الاسم"),
        ("registryCode", "الكود"),
        ("documentNumber", "رقم الوثيقة"),
        ("documentType", "نوع الوثيقة"),
        ("nationality", "الجنسية"),
        ("gender", "النوع"),
        ("birthPlace", "مكان الميلاد"),
        ("residence", "الإقامة"),
        ("neighborhood", "الحي"),
        ("migrationCountry", "دولة المهجر"),
        ("migrationCity", "مدينة المهجر"),
        ("tribe", "القبيلة"),
        ("profession", "المهنة"),
        ("phone", "الهاتف"),
        ("motherName", "اسم الأم"),
        ("notes", "ملاحظات")
    ];

    public static IQueryable<Person> Apply(
        IQueryable<Person> query,
        string? searchText,
        string? field,
        bool isPostgres)
    {
        if (string.IsNullOrWhiteSpace(searchText))
        {
            return query;
        }

        var tokens = searchText
            .Trim()
            .Split([' ', '،', ',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static t => t.Length > 0)
            .ToArray();

        if (tokens.Length == 0)
        {
            return query;
        }

        var normalizedField = string.IsNullOrWhiteSpace(field) ? FieldAll : field.Trim();

        foreach (var token in tokens)
        {
            var current = token;
            var pattern = $"%{EscapeLike(current)}%";
            query = isPostgres
                ? ApplyPostgresToken(query, normalizedField, pattern)
                : ApplySqliteToken(query, normalizedField, current);
        }

        return query;
    }

    private static IQueryable<Person> ApplyPostgresToken(
        IQueryable<Person> query,
        string field,
        string pattern) =>
        field switch
        {
            "fullName" => query.Where(p =>
                EF.Functions.ILike(p.FullName, pattern) ||
                EF.Functions.ILike(p.FirstName, pattern) ||
                EF.Functions.ILike(p.FatherName, pattern) ||
                EF.Functions.ILike(p.GrandfatherName, pattern) ||
                EF.Functions.ILike(p.FamilyName, pattern) ||
                EF.Functions.ILike(p.MotherName, pattern)),

            "registryCode" => query.Where(p => EF.Functions.ILike(p.RegistryCode, pattern)),

            "documentNumber" => query.Where(p =>
                EF.Functions.ILike(p.DocumentNumber, pattern) ||
                EF.Functions.ILike(p.NationalId, pattern)),

            "documentType" => query.Where(p => EF.Functions.ILike(p.DocumentType, pattern)),

            "nationality" => query.Where(p => EF.Functions.ILike(p.Nationality, pattern)),

            "gender" => query.Where(p => EF.Functions.ILike(p.Gender, pattern)),

            "birthPlace" => query.Where(p => EF.Functions.ILike(p.BirthPlace, pattern)),

            "residence" => query.Where(p => EF.Functions.ILike(p.Residence, pattern)),

            "neighborhood" => query.Where(p => EF.Functions.ILike(p.Neighborhood, pattern)),

            "migrationCountry" => query.Where(p =>
                p.IsMigrant && EF.Functions.ILike(p.MigrationCountry, pattern)),

            "migrationCity" => query.Where(p =>
                p.IsMigrant && EF.Functions.ILike(p.MigrationCity, pattern)),

            "tribe" => query.Where(p => EF.Functions.ILike(p.Tribe, pattern)),

            "profession" => query.Where(p => EF.Functions.ILike(p.Profession, pattern)),

            "phone" => query.Where(p => EF.Functions.ILike(p.Phone, pattern)),

            "motherName" => query.Where(p => EF.Functions.ILike(p.MotherName, pattern)),

            "notes" => query.Where(p => EF.Functions.ILike(p.Notes, pattern)),

            _ => query.Where(p =>
                EF.Functions.ILike(p.RegistryCode, pattern) ||
                EF.Functions.ILike(p.NationalId, pattern) ||
                EF.Functions.ILike(p.DocumentNumber, pattern) ||
                EF.Functions.ILike(p.DocumentType, pattern) ||
                EF.Functions.ILike(p.FullName, pattern) ||
                EF.Functions.ILike(p.FirstName, pattern) ||
                EF.Functions.ILike(p.FatherName, pattern) ||
                EF.Functions.ILike(p.GrandfatherName, pattern) ||
                EF.Functions.ILike(p.FamilyName, pattern) ||
                EF.Functions.ILike(p.MotherName, pattern) ||
                EF.Functions.ILike(p.Nationality, pattern) ||
                EF.Functions.ILike(p.Gender, pattern) ||
                EF.Functions.ILike(p.Profession, pattern) ||
                EF.Functions.ILike(p.Phone, pattern) ||
                EF.Functions.ILike(p.BirthPlace, pattern) ||
                EF.Functions.ILike(p.Neighborhood, pattern) ||
                EF.Functions.ILike(p.Residence, pattern) ||
                EF.Functions.ILike(p.MigrationCountry, pattern) ||
                EF.Functions.ILike(p.MigrationCity, pattern) ||
                EF.Functions.ILike(p.Tribe, pattern) ||
                EF.Functions.ILike(p.Notes, pattern))
        };

    private static IQueryable<Person> ApplySqliteToken(
        IQueryable<Person> query,
        string field,
        string current) =>
        field switch
        {
            "fullName" => query.Where(p =>
                p.FullName.Contains(current) ||
                p.FirstName.Contains(current) ||
                p.FatherName.Contains(current) ||
                p.GrandfatherName.Contains(current) ||
                p.FamilyName.Contains(current) ||
                p.MotherName.Contains(current)),

            "registryCode" => query.Where(p => p.RegistryCode.Contains(current)),

            "documentNumber" => query.Where(p =>
                p.DocumentNumber.Contains(current) ||
                p.NationalId.Contains(current)),

            "documentType" => query.Where(p => p.DocumentType.Contains(current)),

            "nationality" => query.Where(p => p.Nationality.Contains(current)),

            "gender" => query.Where(p => p.Gender.Contains(current)),

            "birthPlace" => query.Where(p => p.BirthPlace.Contains(current)),

            "residence" => query.Where(p => p.Residence.Contains(current)),

            "neighborhood" => query.Where(p => p.Neighborhood.Contains(current)),

            "migrationCountry" => query.Where(p =>
                p.IsMigrant && p.MigrationCountry.Contains(current)),

            "migrationCity" => query.Where(p =>
                p.IsMigrant && p.MigrationCity.Contains(current)),

            "tribe" => query.Where(p => p.Tribe.Contains(current)),

            "profession" => query.Where(p => p.Profession.Contains(current)),

            "phone" => query.Where(p => p.Phone.Contains(current)),

            "motherName" => query.Where(p => p.MotherName.Contains(current)),

            "notes" => query.Where(p => p.Notes.Contains(current)),

            _ => query.Where(p =>
                p.RegistryCode.Contains(current) ||
                p.NationalId.Contains(current) ||
                p.DocumentNumber.Contains(current) ||
                p.DocumentType.Contains(current) ||
                p.FullName.Contains(current) ||
                p.FirstName.Contains(current) ||
                p.FatherName.Contains(current) ||
                p.GrandfatherName.Contains(current) ||
                p.FamilyName.Contains(current) ||
                p.MotherName.Contains(current) ||
                p.Nationality.Contains(current) ||
                p.Gender.Contains(current) ||
                p.Profession.Contains(current) ||
                p.Phone.Contains(current) ||
                p.BirthPlace.Contains(current) ||
                p.Neighborhood.Contains(current) ||
                p.Residence.Contains(current) ||
                p.MigrationCountry.Contains(current) ||
                p.MigrationCity.Contains(current) ||
                p.Tribe.Contains(current) ||
                p.Notes.Contains(current))
        };

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
}
