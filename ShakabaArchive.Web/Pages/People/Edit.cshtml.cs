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
    public IFormFile? Photo { get; set; }

    [BindProperty]
    public IFormFile? Document { get; set; }

    /// <summary>رمز أمان أسرة صاحب السجل — يعدّله الأدمن فقط.</summary>
    [BindProperty]
    public string? FamilySecurityCode { get; set; }

    public string FamilyOwnerName { get; private set; } = "";
    public string FamilyName { get; private set; } = "";
    public int? FamilyId { get; private set; }
    public bool IsAdminEditor { get; private set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var person = await db.People.FindAsync(id);
        if (person is null) return NotFound();
        if (!User.CanEditPerson(person))
        {
            TempData["FlashError"] = "يمكنك تعديل بياناتك فقط، وليس بيانات أشخاص آخرين.";
            return RedirectToPage("/People/Details", new { id });
        }

        Id = id;
        RegistryCode = person.RegistryCode;
        Input = PersonInput.From(person);
        await LoadFamilyInfoAsync(person);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var person = await db.People.FindAsync(Id);
        if (person is null) return NotFound();
        if (!User.CanEditPerson(person))
        {
            TempData["FlashError"] = "يمكنك تعديل بياناتك فقط، وليس بيانات أشخاص آخرين.";
            return RedirectToPage("/People/Details", new { id = Id });
        }

        RegistryCode = person.RegistryCode;
        await LoadFamilyInfoAsync(person);

        if (!ModelState.IsValid) return Page();

        var appUser = User.CurrentAppUser();
        if (appUser is null) return Challenge();

        IsAdminEditor = User.IsInRole("Admin") || appUser.IsAdmin || appUser.Role == UserRole.Admin;

        if (IsAdminEditor && !string.IsNullOrWhiteSpace(FamilySecurityCode))
        {
            var ownerName = FamilyOwnerName;
            if (string.IsNullOrWhiteSpace(ownerName) && person.OwnerUserId is int oid)
            {
                var owner = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == oid);
                ownerName = owner?.DisplayName ?? "";
            }

            var (family, famErr) = await FamilyRegistryService.EnsureFamilyForPersonAsync(
                db, person, ownerName);
            if (family is null)
            {
                ModelState.AddModelError(nameof(FamilySecurityCode), famErr);
                await LoadFamilyInfoAsync(person);
                return Page();
            }

            var (codeOk, codeErr) = await FamilyRegistryService.SetSecurityCodeAsync(
                db, family.Id, FamilySecurityCode);
            if (!codeOk)
            {
                ModelState.AddModelError(nameof(FamilySecurityCode), codeErr);
                await LoadFamilyInfoAsync(person);
                return Page();
            }

            FamilyId = family.Id;
            FamilyName = family.Name;
            FamilySecurityCode = family.SecurityCode;
        }

        if (person.OwnerUserId is null && !appUser.CanApprove)
            person.OwnerUserId = appUser.Id;

        var draft = Input.ToDraft();
        draft.RegistryCode = person.RegistryCode;
        draft.HierarchyLevel = person.HierarchyLevel;
        draft.ParentPersonId = person.ParentPersonId;
        draft.PhotoPath = person.PhotoPath;
        draft.DocumentImagePath = person.DocumentImagePath;

        if (Photo is { Length: > 0 })
        {
            await using var stream = Photo.OpenReadStream();
            draft.PhotoPath = DatabaseService.SaveDocumentImage(stream, Photo.FileName);
        }

        if (Document is { Length: > 0 })
        {
            await using var stream = Document.OpenReadStream();
            draft.DocumentImagePath = DatabaseService.SaveDocumentImage(stream, Document.FileName);
        }

        // المالك يحفظ مباشرة في السجل؛ الموافق/الأدمن عبر مسار الاعتماد (يُطبَّق فوراً لهم)
        if (!appUser.CanApprove)
        {
            draft.ApplyTo(person);
            person.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            TempData["Flash"] = "تم حفظ التعديل في سجل الأشخاص.";
            return RedirectToPage("/People/Details", new { id = Id });
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
            TempData["Flash"] = "تم حفظ التعديل في سجل الأشخاص" +
                                (IsAdminEditor && !string.IsNullOrWhiteSpace(FamilySecurityCode)
                                    ? $" ورمز أمان الأسرة ({FamilySecurityCode})."
                                    : ".");
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

        if (!User.CanEditPerson(person))
        {
            TempData["FlashError"] = "يمكنك تعديل بياناتك فقط، وليس بيانات أشخاص آخرين.";
            return RedirectToPage("/People/Details", new { id = Id });
        }

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

    private async Task LoadFamilyInfoAsync(Person person)
    {
        var appUser = User.CurrentAppUser();
        IsAdminEditor = User.IsInRole("Admin") || appUser?.IsAdmin == true || appUser?.Role == UserRole.Admin;

        ShakabaArchive.Models.Family? family = null;
        if (person.FamilyId is int fid)
            family = await db.Families.AsNoTracking().FirstOrDefaultAsync(f => f.Id == fid);
        else if (person.OwnerUserId is int oid)
            family = await db.Families.AsNoTracking().FirstOrDefaultAsync(f => f.OwnerUserId == oid);

        FamilyId = family?.Id;
        FamilyName = family?.Name ?? "";
        FamilySecurityCode = family?.SecurityCode ?? FamilySecurityCode ?? "";

        if (family is not null && family.OwnerUserId > 0)
        {
            var owner = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == family.OwnerUserId);
            FamilyOwnerName = owner?.DisplayName ?? "";
        }
        else if (person.OwnerUserId is int ownerId)
        {
            var owner = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == ownerId);
            FamilyOwnerName = owner?.DisplayName ?? "";
        }
        else
        {
            FamilyOwnerName = "";
        }
    }
}
