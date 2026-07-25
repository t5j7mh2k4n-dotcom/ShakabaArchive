using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ShakabaArchive.Models;

namespace ShakabaArchive.Services;

public static class ArchiveExportService
{
    public static async Task ExportJsonAsync(string filePath, CancellationToken ct = default)
    {
        await using var db = DatabaseService.CreateContext();
        var people = await db.People.AsNoTracking()
            .Include(p => p.Events)
            .OrderBy(p => p.FullName)
            .ToListAsync(ct);

        var payload = people.Select(p => new
        {
            p.NationalId,
            p.FullName,
            p.FatherName,
            p.MotherName,
            p.Nationality,
            p.Gender,
            BirthDate = p.BirthDate?.ToString("yyyy-MM-dd"),
            p.BirthPlace,
            p.Residence,
            p.Tribe,
            p.Neighborhood,
            p.Phone,
            p.Notes,
            p.PhotoPath,
            p.DocumentImagePath,
            Events = p.Events.Select(e => new
            {
                Type = EventTypeLabels.ToArabic(e.Type),
                EventDate = e.EventDate?.ToString("yyyy-MM-dd"),
                e.Place,
                e.Title,
                e.Details,
                e.RelatedPersonName,
                e.SourceNote
            })
        });

        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });
        await File.WriteAllTextAsync(filePath, json, Encoding.UTF8, ct);
    }
}
