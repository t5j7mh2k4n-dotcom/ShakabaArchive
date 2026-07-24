using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ShakabaArchive.Data;
using ShakabaArchive.Models;
using ShakabaArchive.Web.Pages.People.Events;

namespace ShakabaArchive.Web.Pages.Occasions;

public class CreateModel(ArchiveDbContext db) : PageModel
{
    [BindProperty]
    public int? PersonId { get; set; }

    [BindProperty]
    public EventFormModel Input { get; set; } = new();

    public List<SelectListItem> PersonOptions { get; private set; } = [];
    public string? ErrorMessage { get; private set; }

    public async Task OnGetAsync(int? personId = null, EventType? type = null)
    {
        await LoadPeopleAsync();
        if (personId is int id)
            PersonId = id;
        if (type is EventType t)
        {
            Input.Type = t;
            Input.Title = EventTypeLabels.ToArabic(t);
        }
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await LoadPeopleAsync();

        if (PersonId is null or <= 0)
        {
            ErrorMessage = "اختر صاحب المناسبة من السجلات، أو أنشئ شخصاً أولاً.";
            return Page();
        }

        if (!await db.People.AnyAsync(p => p.Id == PersonId))
        {
            ErrorMessage = "السجل المحدد غير موجود.";
            return Page();
        }

        var person = await db.People.FirstAsync(p => p.Id == PersonId.Value);
        var mood = EventTypeLabels.MoodOf(Input.Type);
        var title = string.IsNullOrWhiteSpace(Input.Title)
            ? EventTypeLabels.ToArabic(Input.Type)
            : Input.Title.Trim();

        // مولود جديد: إنشاء سجل شخص اختياري
        if (Input.Type == EventType.Birth
            && Input.CreateChildPersonRecord
            && !string.IsNullOrWhiteSpace(Input.ChildFullName))
        {
            var child = new Person
            {
                NationalId = string.IsNullOrWhiteSpace(Input.ChildNationalId)
                    ? $"TEMP-{DateTime.UtcNow:yyyyMMddHHmmss}"
                    : Input.ChildNationalId.Trim(),
                FullName = Input.ChildFullName.Trim(),
                FatherName = person.FullName,
                MotherName = Input.MotherName.Trim(),
                Nationality = string.IsNullOrWhiteSpace(Input.ChildNationality) ? "سوداني" : Input.ChildNationality.Trim(),
                Gender = string.IsNullOrWhiteSpace(Input.ChildGender) ? "ذكر" : Input.ChildGender,
                BirthDate = Input.EventDate.HasValue
                    ? DateTime.SpecifyKind(Input.EventDate.Value.Date, DateTimeKind.Utc)
                    : null,
                BirthPlace = Input.Place.Trim(),
                Residence = person.Residence,
                Tribe = Input.ChildTribe.Trim(),
                Neighborhood = Input.ChildNeighborhood.Trim(),
                Notes = "أُضيف عبر مناسبة مولود جديد",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            db.People.Add(child);
            await db.SaveChangesAsync();

            db.LifeEvents.Add(new LifeEvent
            {
                PersonId = child.Id,
                Type = EventType.Birth,
                Mood = EventMood.Joy,
                EventDate = Input.EventDate.HasValue
                    ? DateTime.SpecifyKind(Input.EventDate.Value.Date, DateTimeKind.Utc)
                    : null,
                Place = Input.Place.Trim(),
                Title = title,
                Details = Input.Details.Trim(),
                ChildFullName = child.FullName,
                ChildGender = child.Gender,
                MotherName = child.MotherName,
                RelatedPersonName = person.FullName,
                SourceNote = Input.SourceNote.Trim(),
                CreatedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
            return RedirectToPage("/People/Details", new { id = child.Id });
        }

        db.LifeEvents.Add(new LifeEvent
        {
            PersonId = PersonId.Value,
            Type = Input.Type,
            Mood = mood,
            EventDate = Input.EventDate.HasValue
                ? DateTime.SpecifyKind(Input.EventDate.Value.Date, DateTimeKind.Utc)
                : null,
            Place = Input.Place.Trim(),
            Title = title,
            Details = Input.Details.Trim(),
            RelatedPersonName = Input.RelatedPersonName.Trim(),
            RelatedFatherName = Input.RelatedFatherName.Trim(),
            RelatedPhone = Input.RelatedPhone.Trim(),
            ChildFullName = Input.ChildFullName.Trim(),
            ChildGender = Input.ChildGender.Trim(),
            MotherName = Input.MotherName.Trim(),
            Institution = Input.Institution.Trim(),
            Specialty = Input.Specialty.Trim(),
            Degree = Input.Degree.Trim(),
            SourceNote = Input.SourceNote.Trim(),
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        return RedirectToPage("/People/Details", new { id = PersonId.Value });
    }

    private async Task LoadPeopleAsync()
    {
        PersonOptions = await db.People.AsNoTracking()
            .OrderBy(p => p.FullName)
            .Select(p => new SelectListItem(
                $"{p.FullName} — {p.NationalId}",
                p.Id.ToString(),
                PersonId == p.Id))
            .ToListAsync();
    }
}
