using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ShakabaArchive.Data;
using ShakabaArchive.Models;

namespace ShakabaArchive.Web.Pages.Reports;

public class MigrantsReportModel(ArchiveDbContext db) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string? Country { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? City { get; set; }

    public List<string> CountryOptions { get; private set; } = [];
    public List<string> CityOptions { get; private set; } = [];
    public List<MigrantCountryGroup> Groups { get; private set; } = [];
    public int TotalCount { get; private set; }
    public int CountryCount { get; private set; }
    public int CityCount { get; private set; }
    public string GeneratedAt { get; private set; } = "";

    public async Task<IActionResult> OnGetAsync()
    {
        await LoadAsync();
        return Page();
    }

    public async Task<IActionResult> OnGetCsvAsync()
    {
        await LoadAsync();

        var sb = new StringBuilder();
        sb.Append('\uFEFF');
        sb.AppendLine("م,الكود,الاسم,الهاتف,دولة المهجر,مدينة المهجر,مكان الإقامة,المهنة");

        var i = 1;
        foreach (var group in Groups)
        {
            foreach (var cityGroup in group.Cities)
            {
                foreach (var p in cityGroup.People)
                {
                    sb.Append(i++).Append(',')
                        .Append(Csv(p.RegistryCode)).Append(',')
                        .Append(Csv(p.FullName)).Append(',')
                        .Append(Csv(p.Phone)).Append(',')
                        .Append(Csv(p.MigrationCountry)).Append(',')
                        .Append(Csv(p.MigrationCity)).Append(',')
                        .Append(Csv(p.Residence)).Append(',')
                        .Append(Csv(p.Profession))
                        .AppendLine();
                }
            }
        }

        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        var fileName = $"migrants-report-{DateTime.Now:yyyyMMdd-HHmm}.csv";
        return File(bytes, "text/csv; charset=utf-8", fileName);
    }

    private async Task LoadAsync()
    {
        var allMigrants = db.People.AsNoTracking().Where(p => p.IsMigrant);

        CountryOptions = await allMigrants
            .Where(p => p.MigrationCountry != "")
            .Select(p => p.MigrationCountry)
            .Distinct()
            .OrderBy(c => c)
            .ToListAsync();

        var query = allMigrants;

        if (!string.IsNullOrWhiteSpace(Country))
        {
            var country = Country.Trim();
            query = query.Where(p => p.MigrationCountry == country);
        }

        CityOptions = await query
            .Where(p => p.MigrationCity != "")
            .Select(p => p.MigrationCity)
            .Distinct()
            .OrderBy(c => c)
            .ToListAsync();

        if (!string.IsNullOrWhiteSpace(City))
        {
            var city = City.Trim();
            query = query.Where(p => p.MigrationCity == city);
        }

        var people = await query
            .OrderBy(p => p.MigrationCountry)
            .ThenBy(p => p.MigrationCity)
            .ThenBy(p => p.FullName)
            .ToListAsync();

        TotalCount = people.Count;
        CountryCount = people
            .Select(p => p.MigrationCountry.Trim())
            .Where(c => c.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        CityCount = people
            .Select(p => $"{p.MigrationCountry.Trim()}|{p.MigrationCity.Trim()}")
            .Where(k => !k.StartsWith('|') && !k.EndsWith('|'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        Groups = people
            .GroupBy(p => string.IsNullOrWhiteSpace(p.MigrationCountry) ? "—" : p.MigrationCountry.Trim())
            .OrderBy(g => g.Key)
            .Select(g => new MigrantCountryGroup
            {
                Country = g.Key,
                Cities = g
                    .GroupBy(p => string.IsNullOrWhiteSpace(p.MigrationCity) ? "—" : p.MigrationCity.Trim())
                    .OrderBy(cg => cg.Key)
                    .Select(cg => new MigrantCityGroup
                    {
                        City = cg.Key,
                        People = cg.OrderBy(p => p.FullName).ToList()
                    })
                    .ToList()
            })
            .ToList();

        GeneratedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
    }

    private static string Csv(string? value)
    {
        value ??= "";
        if (value.Contains('"') || value.Contains(',') || value.Contains('\n'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }
}

public class MigrantCountryGroup
{
    public string Country { get; set; } = "";
    public List<MigrantCityGroup> Cities { get; set; } = [];
}

public class MigrantCityGroup
{
    public string City { get; set; } = "";
    public List<Person> People { get; set; } = [];
}
