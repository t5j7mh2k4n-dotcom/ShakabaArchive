using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ShakabaArchive.Data;
using ShakabaArchive.Models;

namespace ShakabaArchive.Web.Pages.People.Events;

public class EditModel(ArchiveDbContext db) : PageModel
{
    [BindProperty]
    public int Id { get; set; }

    [BindProperty]
    public int PersonId { get; set; }

    [BindProperty]
    public EventFormModel Input { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var ev = await db.LifeEvents.FindAsync(id);
        if (ev is null) return NotFound();

        Id = ev.Id;
        PersonId = ev.PersonId;
        Input = new EventFormModel
        {
            Type = ev.Type,
            EventDate = ev.EventDate,
            Place = ev.Place,
            Title = ev.Title,
            RelatedPersonName = ev.RelatedPersonName,
            Details = ev.Details,
            SourceNote = ev.SourceNote
        };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var ev = await db.LifeEvents.FindAsync(Id);
        if (ev is null) return NotFound();

        ev.Type = Input.Type;
        ev.EventDate = Input.EventDate;
        ev.Place = Input.Place.Trim();
        ev.Title = string.IsNullOrWhiteSpace(Input.Title) ? EventTypeLabels.ToArabic(Input.Type) : Input.Title.Trim();
        ev.RelatedPersonName = Input.RelatedPersonName.Trim();
        ev.Details = Input.Details.Trim();
        ev.SourceNote = Input.SourceNote.Trim();
        await db.SaveChangesAsync();
        return RedirectToPage("/People/Details", new { id = PersonId });
    }

    public async Task<IActionResult> OnPostDeleteAsync()
    {
        var ev = await db.LifeEvents.FindAsync(Id);
        if (ev is null) return NotFound();
        var personId = ev.PersonId;
        db.LifeEvents.Remove(ev);
        await db.SaveChangesAsync();
        return RedirectToPage("/People/Details", new { id = personId });
    }
}
