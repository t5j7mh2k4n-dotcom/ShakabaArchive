using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ShakabaArchive.Data;
using ShakabaArchive.Models;

namespace ShakabaArchive.Web.Pages.Occasions;

public class IndexModel(ArchiveDbContext db) : PageModel
{
    public List<LifeEvent> Events { get; private set; } = [];

    public async Task OnGetAsync()
    {
        Events = await db.LifeEvents.AsNoTracking()
            .Include(e => e.Person)
            .OrderByDescending(e => e.EventDate)
            .ThenByDescending(e => e.Id)
            .Take(200)
            .ToListAsync();
    }
}
