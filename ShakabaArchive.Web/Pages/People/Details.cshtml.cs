using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ShakabaArchive.Data;
using ShakabaArchive.Models;

namespace ShakabaArchive.Web.Pages.People;

public class DetailsModel(ArchiveDbContext db) : PageModel
{
    public Person? Person { get; private set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        Person = await db.People.AsNoTracking()
            .Include(p => p.Events.Where(e => e.Type != EventType.Divorce && e.Type != EventType.Condolence))
            .FirstOrDefaultAsync(p => p.Id == id);
        return Person is null ? NotFound() : Page();
    }
}
