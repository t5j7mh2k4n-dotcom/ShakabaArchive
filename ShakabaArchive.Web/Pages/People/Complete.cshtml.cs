using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ShakabaArchive.Data;
using ShakabaArchive.Models;
using ShakabaArchive.Services;

namespace ShakabaArchive.Web.Pages.People;

/// <summary>إكمال بيانات سجل مرحّل ناقص — متاح لأي مستخدم مسجّل الدخول.</summary>
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

        RegistryCode = person.RegistryCode;

        if (!ModelState.IsValid)
            return Page();

        var draft = Input.ToDraft();
        draft.RegistryCode = person.RegistryCode;
        draft.HierarchyLevel = person.HierarchyLevel;
        draft.ParentPersonId = person.ParentPersonId;
        draft.ApplyTo(person);

        // إزالة علامة النقص إن اكتملت الحقول الأساسية
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
            : "تم إكمال البيانات وحفظها في السجل.";
        return RedirectToPage("/People/Details", new { id = Id });
    }

    private static bool IsStillIncomplete(Person p) =>
        string.IsNullOrWhiteSpace(p.FatherName)
        || string.IsNullOrWhiteSpace(p.FamilyName)
        || string.IsNullOrWhiteSpace(p.DocumentNumber)
        || string.IsNullOrWhiteSpace(p.Phone);
}
