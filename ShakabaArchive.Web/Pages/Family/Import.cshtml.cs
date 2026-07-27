using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ShakabaArchive.Data;
using ShakabaArchive.Models;
using ShakabaArchive.Services;

namespace ShakabaArchive.Web.Pages.Family;

public class ImportModel(ArchiveDbContext db) : PageModel
{
    public Models.Family Family { get; private set; } = null!;
    public List<Person> Results { get; private set; } = [];
    public string Q { get; private set; } = "";

    public async Task<IActionResult> OnGetAsync(string? q = null)
    {
        var user = User.CurrentAppUser();
        if (user is null) return Challenge();

        Family = await FamilyRegistryService.GetOrCreateAsync(db, user);
        Q = (q ?? "").Trim();
        if (string.IsNullOrWhiteSpace(Q))
            return Page();

        Results = await SearchAsync(Family.Id, Q);
        return Page();
    }

    public async Task<IActionResult> OnPostAttachAsync(int personId, string? q = null)
    {
        var user = User.CurrentAppUser();
        if (user is null) return Challenge();

        Family = await FamilyRegistryService.GetOrCreateAsync(db, user);
        var isAdmin = User.IsInRole("Admin") || user.IsAdmin || user.Role == UserRole.Admin;

        var (ok, message) = await FamilyRegistryService.AttachToFamilyAsync(
            db, Family.Id, personId, user, isAdmin);

        if (ok) TempData["Flash"] = message;
        else TempData["FlashError"] = message;

        return RedirectToPage(new { q });
    }

    private async Task<List<Person>> SearchAsync(int familyId, string q)
    {
        var term = q.ToLowerInvariant();
        var isAdmin = User.IsInRole("Admin")
                      || User.CurrentAppUser()?.IsAdmin == true
                      || User.CurrentAppUser()?.Role == UserRole.Admin;

        // غير المرتبطين بأسرة، أو (للأدمن) أي شخص خارج هذه الأسرة
        var query = db.People.AsNoTracking().Where(p => p.FamilyId != familyId);
        if (!isAdmin)
            query = query.Where(p => p.FamilyId == null);

        query = query.Where(p =>
            p.FullName.ToLower().Contains(term)
            || p.FirstName.ToLower().Contains(term)
            || p.FatherName.ToLower().Contains(term)
            || p.FamilyName.ToLower().Contains(term)
            || p.Phone.Contains(q)
            || p.DocumentNumber.Contains(q)
            || p.NationalId.Contains(q)
            || p.RegistryCode.Contains(q));

        return await query
            .OrderBy(p => p.RegistryCode)
            .ThenBy(p => p.FullName)
            .Take(40)
            .ToListAsync();
    }
}
