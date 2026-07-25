using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ShakabaArchive.Data;
using ShakabaArchive.Models;
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

        var appUser = User.CurrentAppUser();
        if (appUser is null) return Challenge();

        var person = await db.People.FindAsync(Id);
        if (person is null) return NotFound();

        var draft = PersonDraft.From(person);
        draft.NationalId = Input.NationalId;
        draft.FullName = Input.FullName;
        draft.FatherName = Input.FatherName;
        draft.MotherName = Input.MotherName;
        draft.Nationality = "";
        draft.Gender = Input.Gender;
        draft.BirthDate = Input.BirthDate.HasValue
            ? DateTime.SpecifyKind(Input.BirthDate.Value.Date, DateTimeKind.Utc)
            : null;
        draft.BirthPlace = Input.BirthPlace;
        draft.Residence = Input.Residence;
        draft.Tribe = "";
        draft.Neighborhood = Input.Neighborhood;
        draft.Phone = Input.Phone;
        draft.Notes = Input.Notes;

        if (Document is { Length: > 0 })
        {
            await using var stream = Document.OpenReadStream();
            draft.DocumentImagePath = DatabaseService.SaveDocumentImage(stream, Document.FileName);
        }

        var (_, applied) = await ApprovalService.SubmitAsync(
            db,
            appUser,
            ChangeEntity.Person,
            ChangeAction.Update,
            Id,
            draft,
            $"تعديل شخص: {draft.FullName} ({draft.NationalId})");

        if (applied)
        {
            TempData["Flash"] = "تم حفظ التعديل في الأرشيف بنجاح.";
            return RedirectToPage("/People/Details", new { id = Id });
        }

        TempData["Flash"] = "تم إرسال طلب التعديل بانتظار موافقة أحد الثلاثة على صحة البيانات.";
        return RedirectToPage("/Approvals/Index");
    }

    public async Task<IActionResult> OnPostDeleteAsync()
    {
        var appUser = User.CurrentAppUser();
        if (appUser is null) return Challenge();

        var person = await db.People.AsNoTracking().FirstOrDefaultAsync(p => p.Id == Id);
        if (person is null) return NotFound();

        var (_, applied) = await ApprovalService.SubmitAsync(
            db,
            appUser,
            ChangeEntity.Person,
            ChangeAction.Delete,
            Id,
            new { },
            $"حذف شخص: {person.FullName} ({person.NationalId})");

        if (applied)
        {
            TempData["Flash"] = "تم حذف السجل من الأرشيف.";
            return RedirectToPage("/People/Index");
        }

        TempData["Flash"] = "تم إرسال طلب الحذف بانتظار موافقة أحد الثلاثة على صحة البيانات.";
        return RedirectToPage("/Approvals/Index");
    }
}
