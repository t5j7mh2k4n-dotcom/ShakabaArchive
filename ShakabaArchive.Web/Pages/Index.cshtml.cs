using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ShakabaArchive.Data;
using ShakabaArchive.Models;

namespace ShakabaArchive.Web.Pages;

public class IndexModel(ArchiveDbContext db) : PageModel
{
    public int PeopleCount { get; private set; }
    public int EventsCount { get; private set; }
    public int NationalitiesCount { get; private set; }
    public List<Person> Recent { get; private set; } = [];

    public async Task OnGetAsync()
    {
        PeopleCount = await db.People.CountAsync();
        EventsCount = await db.LifeEvents.CountAsync();
        NationalitiesCount = await db.People.Select(p => p.Nationality).Distinct().CountAsync();
        Recent = await db.People.AsNoTracking()
            .OrderByDescending(p => p.UpdatedAt)
            .Take(8)
            .ToListAsync();
    }
}
