using Microsoft.EntityFrameworkCore;
using ShakabaArchive.Data;
using ShakabaArchive.Models;

namespace ShakabaArchive.Web;

public sealed record SearchSuggestion(string Value, string Label, int? PersonId = null);

internal sealed record PersonValueRow(int Id, string FullName, string Value);

public static class PersonSearchSuggestions
{
    private const int DefaultLimit = 20;

    public static async Task<IReadOnlyList<SearchSuggestion>> GetAsync(
        ArchiveDbContext db,
        string? field,
        string? query,
        bool isPostgres,
        int limit = DefaultLimit)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Trim().Length == 0)
        {
            return [];
        }

        var term = query.Trim();
        var normalizedField = string.IsNullOrWhiteSpace(field) ? PersonSearchQuery.FieldAll : field.Trim();
        var filtered = PersonSearchQuery.Apply(db.People.AsNoTracking(), term, normalizedField, isPostgres);

        return normalizedField switch
        {
            "fullName" => await SuggestPeopleAsync(
                filtered, limit,
                q => q.Select(p => new PersonValueRow(p.Id, p.FullName, p.FullName))),

            "registryCode" => await SuggestPeopleAsync(
                filtered, limit,
                q => q.Select(p => new PersonValueRow(p.Id, p.FullName, p.RegistryCode))),

            "documentNumber" => await SuggestDocumentNumbersAsync(filtered, limit),

            "phone" => await SuggestPeopleAsync(
                filtered, limit,
                q => q.Select(p => new PersonValueRow(p.Id, p.FullName, p.Phone))),

            "documentType" => await SuggestDistinctAsync(filtered, limit, q => q.Select(p => p.DocumentType)),
            "nationality" => await SuggestDistinctAsync(filtered, limit, q => q.Select(p => p.Nationality)),
            "gender" => await SuggestDistinctAsync(filtered, limit, q => q.Select(p => p.Gender)),
            "birthPlace" => await SuggestDistinctAsync(filtered, limit, q => q.Select(p => p.BirthPlace)),
            "residence" => await SuggestDistinctAsync(filtered, limit, q => q.Select(p => p.Residence)),
            "neighborhood" => await SuggestDistinctAsync(filtered, limit, q => q.Select(p => p.Neighborhood)),
            "migrationCountry" => await SuggestDistinctAsync(filtered, limit, q => q.Where(p => p.IsMigrant).Select(p => p.MigrationCountry)),
            "migrationCity" => await SuggestDistinctAsync(filtered, limit, q => q.Where(p => p.IsMigrant).Select(p => p.MigrationCity)),
            "tribe" => await SuggestDistinctAsync(filtered, limit, q => q.Select(p => p.Tribe)),
            "profession" => await SuggestDistinctAsync(filtered, limit, q => q.Select(p => p.Profession)),
            "motherName" => await SuggestDistinctAsync(filtered, limit, q => q.Select(p => p.MotherName)),
            "notes" => await SuggestDistinctAsync(filtered, limit, q => q.Select(p => p.Notes)),

            _ => await SuggestAllAsync(filtered, limit)
        };
    }

    private static async Task<IReadOnlyList<SearchSuggestion>> SuggestPeopleAsync(
        IQueryable<Person> query,
        int limit,
        Func<IQueryable<Person>, IQueryable<PersonValueRow>> project)
    {
        var rows = await project(query.OrderBy(p => p.FullName).Take(limit * 4))
            .ToListAsync();

        return rows
            .Where(r => !string.IsNullOrWhiteSpace(r.Value))
            .GroupBy(r => r.Value.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var first = g.First();
                return new SearchSuggestion(first.Value.Trim(), $"{first.Value.Trim()} — {first.FullName}", first.Id);
            })
            .Take(limit)
            .ToList();
    }

    private static async Task<IReadOnlyList<SearchSuggestion>> SuggestDocumentNumbersAsync(
        IQueryable<Person> query,
        int limit)
    {
        var rows = await query
            .OrderBy(p => p.FullName)
            .Take(limit * 4)
            .Select(p => new { p.Id, p.FullName, p.DocumentNumber, p.NationalId })
            .ToListAsync();

        return rows
            .Select(r => new PersonValueRow(
                r.Id,
                r.FullName,
                string.IsNullOrWhiteSpace(r.DocumentNumber) ? r.NationalId : r.DocumentNumber))
            .Where(r => !string.IsNullOrWhiteSpace(r.Value))
            .GroupBy(r => r.Value.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var first = g.First();
                return new SearchSuggestion(first.Value.Trim(), $"{first.Value.Trim()} — {first.FullName}", first.Id);
            })
            .Take(limit)
            .ToList();
    }

    private static async Task<IReadOnlyList<SearchSuggestion>> SuggestDistinctAsync(
        IQueryable<Person> query,
        int limit,
        Func<IQueryable<Person>, IQueryable<string>> selector)
    {
        var values = await selector(query)
            .Where(v => v != "")
            .Distinct()
            .OrderBy(v => v)
            .Take(limit)
            .ToListAsync();

        return values
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => new SearchSuggestion(v.Trim(), v.Trim()))
            .ToList();
    }

    private static async Task<IReadOnlyList<SearchSuggestion>> SuggestAllAsync(
        IQueryable<Person> query,
        int limit)
    {
        var merged = new List<SearchSuggestion>();
        merged.AddRange(await SuggestPeopleAsync(query, limit, q => q.Select(p => new PersonValueRow(p.Id, p.FullName, p.FullName))));
        merged.AddRange(await SuggestPeopleAsync(query, limit, q => q.Select(p => new PersonValueRow(p.Id, p.FullName, p.Phone))));
        merged.AddRange(await SuggestDistinctAsync(query, limit, q => q.Select(p => p.Residence)));
        merged.AddRange(await SuggestDistinctAsync(query, limit, q => q.Select(p => p.Neighborhood)));
        merged.AddRange(await SuggestDistinctAsync(query, limit, q => q.Select(p => p.BirthPlace)));
        merged.AddRange(await SuggestDocumentNumbersAsync(query, limit));

        return merged
            .GroupBy(s => s.Value, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .Take(limit)
            .ToList();
    }
}
