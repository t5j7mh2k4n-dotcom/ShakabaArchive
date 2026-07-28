using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using ShakabaArchive.Data;
using ShakabaArchive.Models;
using ShakabaArchive.Services;
using ShakabaArchive.Web.Pages.People;

namespace ShakabaArchive.Web.Pages.Family;

public class CreateModel(ArchiveDbContext db) : PageModel
{
    [BindProperty]
    public PersonInput Input { get; set; } = new();

    [BindProperty]
    public IFormFile? Photo { get; set; }

    [BindProperty]
    public IFormFile? Document { get; set; }

    [BindProperty]
    public string SecurityCode { get; set; } = "";

    public Models.Family Family { get; private set; } = null!;
    public List<SelectListItem> ParentOptions { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync()
    {
        var user = User.CurrentAppUser();
        if (user is null) return Challenge();

        Family = await FamilyRegistryService.GetOrCreateAsync(db, user);
        Input.HierarchyLevel = 1;
        if (!string.IsNullOrWhiteSpace(Family.Name) && Family.Name.StartsWith("أسرة ", StringComparison.Ordinal))
            Input.FamilyName = Family.Name["أسرة ".Length..].Trim();
        await LoadParentsAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var user = User.CurrentAppUser();
        if (user is null) return Challenge();

        Family = await FamilyRegistryService.GetOrCreateAsync(db, user);
        Input.HierarchyLevel = 1;
        Input.ParentPersonId = null;
        await LoadParentsAsync();

        if (!FamilyRegistryService.VerifySecurityCode(Family, SecurityCode))
        {
            ModelState.AddModelError(nameof(SecurityCode), "رمز أمان الأسرة غير صحيح.");
            return Page();
        }

        ModelState.Remove("Input.FatherName");
        ModelState.Remove("Input.FamilyName");
        if (string.IsNullOrWhiteSpace(Input.FirstName))
            ModelState.AddModelError("Input.FirstName", "أدخل الاسم الأول.");

        if (!ModelState.IsValid)
            return Page();

        string code;
        try
        {
            code = await PersonRegistryService.AllocateCodeAsync(db, 1, null);
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return Page();
        }

        var draft = Input.ToDraft();
        draft.RegistryCode = code;
        draft.HierarchyLevel = 1;
        draft.ParentPersonId = null;

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

        var person = draft.ToPerson();
        person.FamilyId = Family.Id;
        person.OwnerUserId = user.Id;
        // حفظ فوري في الأسرة والسجل العام — بدون موافقة الثلاثة أو الأدمن
        person.IsInGeneralRegistry = true;
        person.SecurityCode = await PersonRegistryService.AllocateSecurityCodeAsync(db);
        person.CreatedAt = DateTime.UtcNow;
        person.UpdatedAt = DateTime.UtcNow;

        db.People.Add(person);
        Family.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        TempData["Flash"] = $"تم حفظ «{person.FullName}» — رمز الأمان: {person.SecurityCode}";
        return RedirectToPage("Index");
    }

    private Task LoadParentsAsync()
    {
        ParentOptions = [];
        ViewData["LockHierarchy"] = true;
        ViewData["AllowPartial"] = true;
        return Task.CompletedTask;
    }
}
