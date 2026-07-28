using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ShakabaArchive.Data;
using ShakabaArchive.Models;
using ShakabaArchive.Services;

namespace ShakabaArchive.Web.Pages.Family;

public class IndexModel(ArchiveDbContext db) : PageModel
{
    public Models.Family Family { get; private set; } = null!;
    public List<Person> Members { get; private set; } = [];
    public string Q { get; private set; } = "";
    public int PendingExportCount { get; private set; }

    public async Task<IActionResult> OnGetAsync(string? q = null)
    {
        var user = User.CurrentAppUser();
        if (user is null) return Challenge();

        Family = await FamilyRegistryService.GetOrCreateAsync(db, user);
        Q = (q ?? "").Trim();

        var query = FamilyRegistryService.MembersQuery(db, Family.Id).AsNoTracking();
        if (!string.IsNullOrWhiteSpace(Q))
        {
            var term = Q.ToLowerInvariant();
            query = query.Where(p =>
                p.FullName.ToLower().Contains(term)
                || p.FirstName.ToLower().Contains(term)
                || p.FatherName.ToLower().Contains(term)
                || p.FamilyName.ToLower().Contains(term)
                || p.Phone.Contains(Q)
                || p.DocumentNumber.Contains(Q)
                || p.RegistryCode.Contains(Q));
        }

        Members = await query.OrderBy(p => p.RegistryCode).ThenBy(p => p.FullName).ToListAsync();
        PendingExportCount = await FamilyRegistryService.MembersQuery(db, Family.Id)
            .CountAsync(p => !p.IsInGeneralRegistry);

        return Page();
    }

    public async Task<IActionResult> OnPostExportAsync()
    {
        var user = User.CurrentAppUser();
        if (user is null) return Challenge();

        var family = await FamilyRegistryService.GetOrCreateAsync(db, user);
        var (_, message) = await FamilyRegistryService.ExportToGeneralAsync(db, family.Id);
        TempData["Flash"] = message;
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRenameAsync(string familyName)
    {
        var user = User.CurrentAppUser();
        if (user is null) return Challenge();

        var family = await FamilyRegistryService.GetOrCreateAsync(db, user);
        familyName = (familyName ?? "").Trim();
        if (familyName.Length >= 2)
        {
            family.Name = familyName[..Math.Min(familyName.Length, 160)];
            family.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            TempData["Flash"] = "تم تحديث اسم سجل الأسرة.";
        }

        return RedirectToPage();
    }
}
