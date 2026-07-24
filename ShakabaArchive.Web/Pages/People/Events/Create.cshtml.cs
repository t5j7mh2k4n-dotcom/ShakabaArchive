using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ShakabaArchive.Data;
using ShakabaArchive.Models;

namespace ShakabaArchive.Web.Pages.People.Events;

public class CreateModel(ArchiveDbContext db) : PageModel
{
    [BindProperty]
    public int PersonId { get; set; }

    public string PersonName { get; set; } = "";

    [BindProperty]
    public EventFormModel Input { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(int personId)
    {
        var person = await db.People.AsNoTracking().FirstOrDefaultAsync(p => p.Id == personId);
        if (person is null) return NotFound();
        PersonId = personId;
        PersonName = person.FullName;
        Input.Title = EventTypeLabels.ToArabic(EventType.Birth);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!await db.People.AnyAsync(p => p.Id == PersonId))
            return NotFound();

        db.LifeEvents.Add(new LifeEvent
        {
            PersonId = PersonId,
            Type = Input.Type,
            EventDate = Input.EventDate,
            Place = Input.Place.Trim(),
            Title = string.IsNullOrWhiteSpace(Input.Title) ? EventTypeLabels.ToArabic(Input.Type) : Input.Title.Trim(),
            RelatedPersonName = Input.RelatedPersonName.Trim(),
            Details = Input.Details.Trim(),
            SourceNote = Input.SourceNote.Trim(),
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        return RedirectToPage("/People/Details", new { id = PersonId });
    }
}
