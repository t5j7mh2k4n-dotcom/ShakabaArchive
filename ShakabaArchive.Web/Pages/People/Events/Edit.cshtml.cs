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
            Details = ev.Details,
            RelatedPersonName = ev.RelatedPersonName,
            RelatedFatherName = ev.RelatedFatherName,
            RelatedPhone = ev.RelatedPhone,
            ChildFullName = ev.ChildFullName,
            ChildGender = string.IsNullOrWhiteSpace(ev.ChildGender) ? "ذكر" : ev.ChildGender,
            MotherName = ev.MotherName,
            Institution = ev.Institution,
            Specialty = ev.Specialty,
            Degree = ev.Degree,
            SourceNote = ev.SourceNote,
            CreateChildPersonRecord = false
        };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var ev = await db.LifeEvents.FindAsync(Id);
        if (ev is null) return NotFound();

        ev.Type = Input.Type;
        ev.Mood = EventTypeLabels.MoodOf(Input.Type);
        ev.EventDate = Input.EventDate.HasValue
            ? DateTime.SpecifyKind(Input.EventDate.Value.Date, DateTimeKind.Utc)
            : null;
        ev.Place = Input.Place.Trim();
        ev.Title = string.IsNullOrWhiteSpace(Input.Title) ? EventTypeLabels.ToArabic(Input.Type) : Input.Title.Trim();
        ev.Details = Input.Details.Trim();
        ev.RelatedPersonName = Input.RelatedPersonName.Trim();
        ev.RelatedFatherName = Input.RelatedFatherName.Trim();
        ev.RelatedPhone = Input.RelatedPhone.Trim();
        ev.ChildFullName = Input.ChildFullName.Trim();
        ev.ChildGender = Input.ChildGender.Trim();
        ev.MotherName = Input.MotherName.Trim();
        ev.Institution = Input.Institution.Trim();
        ev.Specialty = Input.Specialty.Trim();
        ev.Degree = Input.Degree.Trim();
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
