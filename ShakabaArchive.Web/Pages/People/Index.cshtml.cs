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
    public List<SelectListItem> BirthPlaceOptions { get; private set; } = [];

    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? BirthPlace { get; set; }

    public async Task OnGetAsync()
    {
        var places = await db.People.AsNoTracking()
            .Select(p => p.BirthPlace)
            .Where(n => n != "")
            .Distinct()
            .OrderBy(n => n)
            .ToListAsync();

        BirthPlaceOptions = places
            .Select(n => new SelectListItem(n, n, n == BirthPlace))
            .ToList();

        IQueryable<Person> query = db.People.AsNoTracking().Include(p => p.Events);

        if (!string.IsNullOrWhiteSpace(BirthPlace))
            query = query.Where(p => p.BirthPlace == BirthPlace);

        if (!string.IsNullOrWhiteSpace(Q))
        {
            var q = Q.Trim();
            query = query.Where(p =>
                p.NationalId.Contains(q) ||
                p.FullName.Contains(q) ||
                p.FatherName.Contains(q) ||
                p.BirthPlace.Contains(q) ||
                p.Neighborhood.Contains(q) ||
                p.Residence.Contains(q));
        }

        People = await query.OrderBy(p => p.FullName).ToListAsync();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        if (User.Identity?.IsAuthenticated != true)
            return Forbid();

        var person = await db.People.Include(p => p.Events).FirstOrDefaultAsync(p => p.Id == id);
        if (person is null)
            return RedirectToPage(new { q = Q, birthPlace = BirthPlace });

        db.People.Remove(person);
        await db.SaveChangesAsync();
        return RedirectToPage(new { q = Q, birthPlace = BirthPlace });
    }
}
