using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ShakabaArchive.Data;
using ShakabaArchive.Services;

namespace ShakabaArchive.Web.Pages.People;

public class EditModel(ArchiveDbContext db) : PageModel
{
    [BindProperty]
    public int Id { get; set; }

    [BindProperty]
    public PersonInput Input { get; set; } = new();

    [BindProperty]
    public IFormFile? Document { get; set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var person = await db.People.FindAsync(id);
        if (person is null) return NotFound();
        Id = id;
        Input = PersonInput.From(person);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();

        var person = await db.People.FindAsync(Id);
        if (person is null) return NotFound();

        Input.ApplyTo(person);
        if (Document is { Length: > 0 })
        {
            await using var stream = Document.OpenReadStream();
            person.DocumentImagePath = DatabaseService.SaveDocumentImage(stream, Document.FileName);
        }

        await db.SaveChangesAsync();
        return RedirectToPage("Details", new { id = Id });
    }

    public async Task<IActionResult> OnPostDeleteAsync()
    {
        var person = await db.People.Include(p => p.Events).FirstOrDefaultAsync(p => p.Id == Id);
        if (person is null) return NotFound();
        db.People.Remove(person);
        await db.SaveChangesAsync();
        return RedirectToPage("Index");
    }
}
