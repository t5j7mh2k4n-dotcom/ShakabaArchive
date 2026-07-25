using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ShakabaArchive.Data;
using ShakabaArchive.Models;
using ShakabaArchive.Services;
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

        var appUser = User.CurrentAppUser();
        if (appUser is null)
            return Challenge();

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

        var person = await db.People.AsNoTracking().FirstAsync(p => p.Id == PersonId.Value);
        var mood = EventTypeLabels.MoodOf(Input.Type);
        var title = string.IsNullOrWhiteSpace(Input.Title)
            ? EventTypeLabels.ToArabic(Input.Type)
            : Input.Title.Trim();

        var draft = new LifeEventDraft
        {
            PersonId = PersonId.Value,
            Type = (int)Input.Type,
            Mood = (int)mood,
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
            ChildNationalId = Input.ChildNationalId.Trim(),
            ChildNationality = Input.ChildNationality.Trim(),
            ChildTribe = Input.ChildTribe.Trim(),
            ChildNeighborhood = Input.ChildNeighborhood.Trim(),
            CreateChildPerson = Input.Type == EventType.Birth && Input.CreateChildPersonRecord,
            Institution = Input.Institution.Trim(),
            Specialty = Input.Specialty.Trim(),
            Degree = Input.Degree.Trim(),
            SourceNote = Input.SourceNote.Trim()
        };

        var summary = $"إضافة مناسبة ({EventTypeLabels.ToArabic(Input.Type)}) لـ {person.FullName}";
        if (draft.CreateChildPerson && !string.IsNullOrWhiteSpace(draft.ChildFullName))
            summary += $" + مولود: {draft.ChildFullName}";

        var (_, applied) = await ApprovalService.SubmitAsync(
            db,
            appUser,
            ChangeEntity.LifeEvent,
            ChangeAction.Create,
            null,
            draft,
            summary);

        if (applied)
        {
            TempData["Flash"] = "تم حفظ المناسبة في الأرشيف بنجاح.";
            return RedirectToPage("/People/Details", new { id = PersonId });
        }

        TempData["Flash"] = "تم إرسال طلب إضافة المناسبة بانتظار موافقة أحد الثلاثة على صحة البيانات.";
        return RedirectToPage("/Approvals/Index");
    }

    private async Task LoadPeopleAsync()
    {
        PersonOptions = await db.People.AsNoTracking()
            .OrderBy(p => p.RegistryCode)
            .Select(p => new SelectListItem(
                $"{p.RegistryCode} — {p.FullName}",
                p.Id.ToString(),
                PersonId == p.Id))
            .ToListAsync();
    }
}
