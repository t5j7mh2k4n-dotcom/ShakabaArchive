using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ShakabaArchive.Data;
using ShakabaArchive.Models;
using ShakabaArchive.Services;

namespace ShakabaArchive.Web.Pages.People;

/// <summary>إكمال/تعديل بيانات السجل — للمالك فقط (أو أدمن/موافق).</summary>
public class CompleteModel(ArchiveDbContext db) : PageModel
{
    [BindProperty]
    public int Id { get; set; }

    [BindProperty]
    public PersonInput Input { get; set; } = new();

    public string RegistryCode { get; private set; } = "";
    public string? Message { get; private set; }
    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var person = await db.People.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);
        if (person is null)
            return NotFound();

        if (!User.CanEditPerson(person))
        {
            TempData["FlashError"] = "يمكنك تعديل بياناتك فقط، وليس بيانات أشخاص آخرين.";
            return RedirectToPage("/People/Details", new { id });
        }

        // اربط الملكية إن كانت فارغة وتطابق الهاتف
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

        if (!User.CanEditPerson(person))
        {
            TempData["FlashError"] = "يمكنك تعديل بياناتك فقط، وليس بيانات أشخاص آخرين.";
            return RedirectToPage("/People/Details", new { id = Id });
        }

        RegistryCode = person.RegistryCode;

        if (!ModelState.IsValid)
            return Page();

        var draft = Input.ToDraft();
        draft.RegistryCode = person.RegistryCode;
        draft.HierarchyLevel = person.HierarchyLevel;
        draft.ParentPersonId = person.ParentPersonId;
        draft.ApplyTo(person);

        var appUser = User.CurrentAppUser();
        if (person.OwnerUserId is null && appUser is not null && !appUser.CanApprove)
            person.OwnerUserId = appUser.Id;

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
            ? "تم الحفظ. ما زالت بعض البيانات ناقصة — يمكنك إكمالها لاحقاً."
            : "تم حفظ بياناتك في السجل.";
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

    private static bool IsStillIncomplete(Person p) =>
        string.IsNullOrWhiteSpace(p.FatherName)
        || string.IsNullOrWhiteSpace(p.FamilyName)
        || string.IsNullOrWhiteSpace(p.DocumentNumber)
        || string.IsNullOrWhiteSpace(p.Phone);
}
