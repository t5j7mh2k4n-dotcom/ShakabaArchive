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

    public string RegistryCode { get; private set; } = "";

    [BindProperty]
    public PersonInput Input { get; set; } = new();

    [BindProperty]
    public IFormFile? Document { get; set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var person = await db.People.FindAsync(id);
        if (person is null) return NotFound();
        Id = id;
        RegistryCode = person.RegistryCode;
        Input = PersonInput.From(person);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var person = await db.People.FindAsync(Id);
        if (person is null) return NotFound();
        RegistryCode = person.RegistryCode;

        if (!ModelState.IsValid) return Page();

        var appUser = User.CurrentAppUser();
        if (appUser is null) return Challenge();

        var draft = Input.ToDraft();
        draft.RegistryCode = person.RegistryCode;
        draft.HierarchyLevel = person.HierarchyLevel;
        draft.ParentPersonId = person.ParentPersonId;

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
            $"تعديل سجل أشخاص {person.RegistryCode}: {draft.FullName}");

        if (applied)
        {
            TempData["Flash"] = "تم حفظ التعديل في سجل الأشخاص.";
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

        var hasChildren = await db.People.AnyAsync(p => p.ParentPersonId == Id);
        if (hasChildren)
        {
            TempData["FlashError"] = "لا يمكن حذف سجل له أبناء في الشجرة. احذف الأبناء أولاً.";
            return RedirectToPage(new { id = Id });
        }

        var (_, applied) = await ApprovalService.SubmitAsync(
            db,
            appUser,
            ChangeEntity.Person,
            ChangeAction.Delete,
            Id,
            new { },
            $"حذف سجل أشخاص {person.RegistryCode}: {person.FullName}");

        if (applied)
        {
            TempData["Flash"] = "تم حذف السجل من سجل الأشخاص.";
            return RedirectToPage("/People/Index");
        }

        TempData["Flash"] = "تم إرسال طلب الحذف بانتظار موافقة أحد الثلاثة على صحة البيانات.";
        return RedirectToPage("/Approvals/Index");
    }
}
