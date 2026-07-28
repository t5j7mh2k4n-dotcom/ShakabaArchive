using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ShakabaArchive.Data;
using ShakabaArchive.Models;
using ShakabaArchive.Services;

namespace ShakabaArchive.Web.Pages.People;

/// <summary>إكمال/تعديل بيانات السجل الناقصة — مع حفظ مباشر في سجل الأشخاص.</summary>
public class CompleteModel(ArchiveDbContext db) : PageModel
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
    public string? Message { get; private set; }
    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var person = await db.People.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);
        if (person is null)
            return NotFound();

        if (!User.CanCompletePerson(person))
        {
            TempData["FlashError"] = "يمكنك إكمال أو تعديل بياناتك فقط، وليس بيانات أشخاص مكتملة لآخرين.";
            return RedirectToPage("/People/Details", new { id });
        }

        await EnsureOwnershipAsync(person);

        Id = id;
        RegistryCode = person.RegistryCode;
        Input = PersonInput.From(person);
        Message = TempData["Flash"] as string;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var person = await db.People.FirstOrDefaultAsync(p => p.Id == Id);
        if (person is null)
            return NotFound();

        if (!User.CanCompletePerson(person))
        {
            TempData["FlashError"] = "يمكنك إكمال أو تعديل بياناتك فقط، وليس بيانات أشخاص مكتملة لآخرين.";
            return RedirectToPage("/People/Details", new { id = Id });
        }

        RegistryCode = person.RegistryCode;

        // إكمال تدريجي: الاسم الأول كافٍ للحفظ؛ بقية الحقول تُكمَّل لاحقاً
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

        var appUser = User.CurrentAppUser();
        ClaimOwnershipIfAppropriate(person, appUser);

        var incomplete = IsStillIncomplete(person);
        var marker = ApprovalService.IncompleteProfileMarker;
        if (!incomplete && person.Notes.Contains(marker, StringComparison.Ordinal))
        {
            person.Notes = person.Notes
                .Replace(marker, "", StringComparison.Ordinal)
                .Replace("— يرجى إكمال البيانات عبر الرابط المرسل واتساب.", "", StringComparison.Ordinal)
                .Trim();
        }
        else if (incomplete && !person.Notes.Contains(marker, StringComparison.Ordinal))
        {
            person.Notes = string.IsNullOrWhiteSpace(person.Notes)
                ? $"{marker} — يرجى إكمال البيانات عبر الرابط المرسل واتساب."
                : $"{marker} {person.Notes}";
        }

        person.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        TempData["Flash"] = incomplete
            ? "تم حفظ التعديل في سجل الأشخاص. ما زالت بعض البيانات ناقصة — يمكنك إكمالها لاحقاً."
            : "تم حفظ التعديل في سجل الأشخاص.";
        return RedirectToPage("/People/Details", new { id = Id });
    }

    private async Task EnsureOwnershipAsync(Person person)
    {
        if (person.OwnerUserId is not null)
            return;
        var appUser = User.CurrentAppUser();
        if (appUser is null || appUser.CanApprove)
            return;
        if (string.IsNullOrWhiteSpace(appUser.Phone) || appUser.Phone.Trim() != (person.Phone ?? "").Trim())
            return;

        var tracked = await db.People.FirstOrDefaultAsync(p => p.Id == person.Id);
        if (tracked is null || tracked.OwnerUserId is not null)
            return;
        tracked.OwnerUserId = appUser.Id;
        await db.SaveChangesAsync();
        person.OwnerUserId = appUser.Id;
    }

    private static void ClaimOwnershipIfAppropriate(Person person, AppUser? appUser)
    {
        if (appUser is null || appUser.CanApprove)
            return;

        if (!string.IsNullOrWhiteSpace(appUser.Phone)
            && !string.IsNullOrWhiteSpace(person.Phone)
            && string.Equals(appUser.Phone.Trim(), person.Phone.Trim(), StringComparison.Ordinal))
        {
            person.OwnerUserId = appUser.Id;
            return;
        }

        if (person.OwnerUserId is null)
            person.OwnerUserId = appUser.Id;
    }

    private static bool IsStillIncomplete(Person p) =>
        string.IsNullOrWhiteSpace(p.FatherName)
        || string.IsNullOrWhiteSpace(p.FamilyName)
        || string.IsNullOrWhiteSpace(p.DocumentNumber)
        || string.IsNullOrWhiteSpace(p.Phone);
}
