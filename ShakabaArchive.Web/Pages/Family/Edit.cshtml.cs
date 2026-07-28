using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ShakabaArchive.Data;
using ShakabaArchive.Models;
using ShakabaArchive.Services;
using ShakabaArchive.Web.Pages.People;

namespace ShakabaArchive.Web.Pages.Family;

public class EditModel(ArchiveDbContext db) : PageModel
{
    [BindProperty]
    public int Id { get; set; }

    [BindProperty]
    public PersonInput Input { get; set; } = new();

    [BindProperty]
    public IFormFile? Photo { get; set; }

    [BindProperty]
    public IFormFile? Document { get; set; }

    public string RegistryCode { get; private set; } = "";
    public string SecurityCode { get; private set; } = "";
    public Models.Family Family { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var user = User.CurrentAppUser();
        if (user is null) return Challenge();

        Family = await FamilyRegistryService.GetOrCreateAsync(db, user);
        var person = await db.People.FirstOrDefaultAsync(p => p.Id == id && p.FamilyId == Family.Id);
        if (person is null)
        {
            TempData["FlashError"] = "هذا الفرد ليس ضمن سجل أسرتك.";
            return RedirectToPage("Index");
        }

        await PersonRegistryService.EnsureSecurityCodeAsync(db, person);
        await db.SaveChangesAsync();

        Id = id;
        RegistryCode = person.RegistryCode;
        SecurityCode = person.SecurityCode;
        Input = PersonInput.From(person);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var user = User.CurrentAppUser();
        if (user is null) return Challenge();

        Family = await FamilyRegistryService.GetOrCreateAsync(db, user);
        var person = await db.People.FirstOrDefaultAsync(p => p.Id == Id && p.FamilyId == Family.Id);
        if (person is null)
        {
            TempData["FlashError"] = "هذا الفرد ليس ضمن سجل أسرتك.";
            return RedirectToPage("Index");
        }

        RegistryCode = person.RegistryCode;
        await PersonRegistryService.EnsureSecurityCodeAsync(db, person);
        SecurityCode = person.SecurityCode;
        ModelState.Remove("Input.FatherName");
        ModelState.Remove("Input.FamilyName");
        if (string.IsNullOrWhiteSpace(Input.FirstName))
            ModelState.AddModelError("Input.FirstName", "أدخل الاسم الأول.");

        if (!ModelState.IsValid)
            return Page();

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

        draft.ApplyTo(person);
        person.FamilyId = Family.Id;
        person.OwnerUserId = user.Id;
        person.IsInGeneralRegistry = true;
        person.UpdatedAt = DateTime.UtcNow;
        Family.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        TempData["Flash"] = $"تم حفظ التعديل — رمز أمان الفرد: {person.SecurityCode}";
        return RedirectToPage("Index");
    }
}
