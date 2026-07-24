using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ShakabaArchive.Data;
using ShakabaArchive.Models;

namespace ShakabaArchive.Web.Pages.People;

public class IndexModel(ArchiveDbContext db) : PageModel
{
    public List<Person> People { get; private set; } = [];
    public List<SelectListItem> NationalityOptions { get; private set; } = [];

    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Nationality { get; set; }

    public async Task OnGetAsync()
    {
        var nats = await db.People.AsNoTracking()
            .Select(p => p.Nationality)
            .Where(n => n != "")
            .Distinct()
            .OrderBy(n => n)
            .ToListAsync();

        NationalityOptions = nats
            .Select(n => new SelectListItem(n, n, n == Nationality))
            .ToList();

        IQueryable<Person> query = db.People.AsNoTracking().Include(p => p.Events);

        if (!string.IsNullOrWhiteSpace(Nationality))
            query = query.Where(p => p.Nationality == Nationality);

        if (!string.IsNullOrWhiteSpace(Q))
        {
            var q = Q.Trim();
            query = query.Where(p =>
                p.NationalId.Contains(q) ||
                p.FullName.Contains(q) ||
                p.FatherName.Contains(q) ||
                p.Nationality.Contains(q) ||
                p.Tribe.Contains(q) ||
                p.Neighborhood.Contains(q) ||
                p.Residence.Contains(q));
        }

        People = await query.OrderBy(p => p.FullName).ToListAsync();
    }
}
