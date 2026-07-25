using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ShakabaArchive.Data;
using ShakabaArchive.Models;
using ShakabaArchive.Services;

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
        var appUser = User.CurrentAppUser();
        if (appUser is null) return Challenge();

        var ev = await db.LifeEvents.AsNoTracking().FirstOrDefaultAsync(e => e.Id == Id);
        if (ev is null) return NotFound();

        var draft = new LifeEventDraft
        {
            PersonId = PersonId,
            Type = (int)Input.Type,
            Mood = (int)EventTypeLabels.MoodOf(Input.Type),
            EventDate = Input.EventDate.HasValue
                ? DateTime.SpecifyKind(Input.EventDate.Value.Date, DateTimeKind.Utc)
                : null,
            Place = Input.Place.Trim(),
            Title = string.IsNullOrWhiteSpace(Input.Title) ? EventTypeLabels.ToArabic(Input.Type) : Input.Title.Trim(),
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
            SourceNote = Input.SourceNote.Trim()
        };

        var (_, applied) = await ApprovalService.SubmitAsync(
            db,
            appUser,
            ChangeEntity.LifeEvent,
            ChangeAction.Update,
            Id,
            draft,
            $"تعديل مناسبة ({EventTypeLabels.ToArabic(Input.Type)}) — رقم {Id}");

        if (applied)
        {
            TempData["Flash"] = "تم حفظ تعديل المناسبة في الأرشيف.";
            return RedirectToPage("/People/Details", new { id = PersonId });
        }

        TempData["Flash"] = "تم إرسال طلب تعديل المناسبة بانتظار موافقة أحد الثلاثة على صحة البيانات.";
        return RedirectToPage("/Approvals/Index");
    }

    public async Task<IActionResult> OnPostDeleteAsync()
    {
        var appUser = User.CurrentAppUser();
        if (appUser is null) return Challenge();

        var ev = await db.LifeEvents.AsNoTracking().FirstOrDefaultAsync(e => e.Id == Id);
        if (ev is null) return NotFound();

        var personId = ev.PersonId;
        var (_, applied) = await ApprovalService.SubmitAsync(
            db,
            appUser,
            ChangeEntity.LifeEvent,
            ChangeAction.Delete,
            Id,
            new { },
            $"حذف مناسبة ({EventTypeLabels.ToArabic(ev.Type)}) — رقم {Id}");

        if (applied)
        {
            TempData["Flash"] = "تم حذف المناسبة من الأرشيف.";
            return RedirectToPage("/People/Details", new { id = personId });
        }

        TempData["Flash"] = "تم إرسال طلب حذف المناسبة بانتظار موافقة أحد الثلاثة على صحة البيانات.";
        return RedirectToPage("/Approvals/Index");
    }
}
