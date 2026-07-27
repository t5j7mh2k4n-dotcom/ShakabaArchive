using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ShakabaArchive.Data;
using ShakabaArchive.Models;
using ShakabaArchive.Services;
using ShakabaArchive.Web;

namespace ShakabaArchive.Web.Pages.People;

public class IndexModel(ArchiveDbContext db) : PageModel
{
    public List<Person> People { get; private set; } = [];
    public List<PendingPersonRow> PendingPeople { get; private set; } = [];
    public List<SelectListItem> BirthPlaceOptions { get; private set; } = [];
    public List<SelectListItem> SearchFieldOptions { get; private set; } = [];

    /// <summary>مفتاح = Person.Id — معلومات أسرة صاحب السجل للأدمن.</summary>
    public Dictionary<int, FamilySecurityInfo> FamilyInfoByPersonId { get; private set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Field { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? BirthPlace { get; set; }

    [BindProperty(SupportsGet = true)]
    public int? Level { get; set; }

    public async Task OnGetAsync()
    {
        try
        {
            DatabaseService.EnsureReady();
            await ApprovalService.EnsureSchemaAsync(db);
            await FamilyRegistryService.EnsureSchemaAsync(db);
        }
        catch (Exception ex)
        {
            TempData["Flash"] = "قاعدة البيانات تُجهَّز الآن، أعد التحديث بعد ثوانٍ. " + ex.Message;
            return;
        }

        var places = await db.People.AsNoTracking()
            .Where(p => p.IsInGeneralRegistry)
            .Select(p => p.BirthPlace)
            .Where(n => n != "")
            .Distinct()
            .OrderBy(n => n)
            .ToListAsync();

        BirthPlaceOptions = places
            .Select(n => new SelectListItem(n, n, n == BirthPlace))
            .ToList();

        SearchFieldOptions = PersonSearchQuery.Fields
            .Select(f => new SelectListItem(f.Label, f.Value, f.Value == (Field ?? PersonSearchQuery.FieldAll)))
            .ToList();

        IQueryable<Person> query = db.People.AsNoTracking()
            .Include(p => p.Events)
            .Where(p => p.IsInGeneralRegistry);

        if (Level is >= 1 and <= 3)
            query = query.Where(p => p.HierarchyLevel == Level);

        if (!string.IsNullOrWhiteSpace(BirthPlace))
            query = query.Where(p => p.BirthPlace == BirthPlace);

        var isPostgres = db.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true;
        query = PersonSearchQuery.Apply(query, Q, Field, isPostgres);

        People = await query
            .OrderBy(p => p.RegistryCode)
            .ThenBy(p => p.FullName)
            .ToListAsync();

        await LoadFamilySecurityInfoAsync();

        // طلبات الإضافة التي لم تُعتمد بعد — المدخل يرى طلباته فقط
        if (User.Identity?.IsAuthenticated == true)
        {
            var appUser = User.CurrentAppUser();
            var uid = appUser?.Id;
            var canSeeAll = User.IsInRole("Admin") || User.IsInRole("Approver") || appUser?.CanApprove == true;
            var pendingQuery = db.PendingChanges.AsNoTracking()
                .Where(x => x.Status == ChangeStatus.Pending
                            && x.EntityType == ChangeEntity.Person
                            && x.Action == ChangeAction.Create);
            if (!canSeeAll && uid is > 0)
                pendingQuery = pendingQuery.Where(x => x.SubmittedByUserId == uid.Value);

            var pending = await pendingQuery
                .OrderByDescending(x => x.SubmittedAt)
                .Take(50)
                .ToListAsync();

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            foreach (var item in pending)
            {
                PersonDraft? draft = null;
                try { draft = JsonSerializer.Deserialize<PersonDraft>(item.PayloadJson, options); }
                catch { /* ignore */ }

                PendingPeople.Add(new PendingPersonRow
                {
                    PendingId = item.Id,
                    Summary = item.Summary,
                    SubmittedByName = item.SubmittedByName,
                    SubmittedAt = item.SubmittedAt,
                    RegistryCode = draft?.RegistryCode ?? "—",
                    FullName = draft?.FullName ?? item.Summary,
                    DocumentType = draft?.DocumentType ?? "—",
                    DocumentNumber = draft?.DocumentNumber ?? draft?.NationalId ?? "—"
                });
            }
        }
    }

    public async Task<IActionResult> OnPostSetFamilySecurityAsync(int personId, string securityCode)
    {
        var appUser = User.CurrentAppUser();
        if (appUser is null || !(User.IsInRole("Admin") || appUser.IsAdmin || appUser.Role == UserRole.Admin))
            return Forbid();

        var person = await db.People.FirstOrDefaultAsync(p => p.Id == personId);
        if (person is null)
        {
            TempData["Flash"] = "السجل غير موجود.";
            return RedirectToPage(new { q = Q, field = Field, birthPlace = BirthPlace, level = Level });
        }

        string? ownerName = null;
        if (person.OwnerUserId is int oid)
        {
            var owner = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == oid);
            ownerName = owner?.DisplayName;
        }

        var (family, famErr) = await FamilyRegistryService.EnsureFamilyForPersonAsync(db, person, ownerName);
        if (family is null)
        {
            TempData["Flash"] = famErr;
            return RedirectToPage(new { q = Q, field = Field, birthPlace = BirthPlace, level = Level });
        }

        var (ok, error) = await FamilyRegistryService.SetSecurityCodeAsync(db, family.Id, securityCode);
        TempData["Flash"] = ok
            ? $"تم حفظ رمز أمان أسرة «{(string.IsNullOrWhiteSpace(ownerName) ? family.Name : ownerName)}»: {FamilyRegistryService.NormalizeSecurityCode(securityCode)}"
            : error;

        return RedirectToPage(new { q = Q, field = Field, birthPlace = BirthPlace, level = Level });
    }

    private async Task LoadFamilySecurityInfoAsync()
    {
        if (People.Count == 0) return;

        var familyIds = People.Where(p => p.FamilyId.HasValue).Select(p => p.FamilyId!.Value).Distinct().ToList();
        var ownerIds = People.Where(p => p.OwnerUserId.HasValue).Select(p => p.OwnerUserId!.Value).Distinct().ToList();

        var families = await db.Families.AsNoTracking()
            .Where(f => familyIds.Contains(f.Id) || ownerIds.Contains(f.OwnerUserId))
            .ToListAsync();
        var byId = families.ToDictionary(f => f.Id);
        var byOwner = families.Where(f => f.OwnerUserId > 0)
            .GroupBy(f => f.OwnerUserId)
            .ToDictionary(g => g.Key, g => g.First());

        var allOwnerIds = families.Select(f => f.OwnerUserId).Where(id => id > 0)
            .Concat(ownerIds)
            .Distinct()
            .ToList();
        var owners = await db.Users.AsNoTracking()
            .Where(u => allOwnerIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.DisplayName);

        foreach (var p in People)
        {
            ShakabaArchive.Models.Family? family = null;
            if (p.FamilyId is int fid && byId.TryGetValue(fid, out var f1))
                family = f1;
            else if (p.OwnerUserId is int oid && byOwner.TryGetValue(oid, out var f2))
                family = f2;

            var ownerName = "";
            if (family is not null && family.OwnerUserId > 0 && owners.TryGetValue(family.OwnerUserId, out var n1))
                ownerName = n1;
            else if (p.OwnerUserId is int oid2 && owners.TryGetValue(oid2, out var n2))
                ownerName = n2;

            FamilyInfoByPersonId[p.Id] = new FamilySecurityInfo
            {
                FamilyId = family?.Id,
                FamilyName = family?.Name ?? "",
                SecurityCode = family?.SecurityCode ?? "",
                OwnerName = ownerName
            };
        }
    }

    public async Task<IActionResult> OnGetSuggestAsync(string? field, string? q)
    {
        try
        {
            DatabaseService.EnsureReady();
        }
        catch
        {
            return new JsonResult(Array.Empty<SearchSuggestion>());
        }

        var isPostgres = db.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true;
        var suggestions = await PersonSearchSuggestions.GetAsync(db, field, q, isPostgres);
        return new JsonResult(suggestions);
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var appUser = User.CurrentAppUser();
        if (appUser is null || !appUser.CanApprove)
            return Forbid();

        var person = await db.People.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);
        if (person is null)
            return RedirectToPage(new { q = Q, field = Field, birthPlace = BirthPlace, level = Level });

        if (await db.People.AnyAsync(p => p.ParentPersonId == id))
        {
            TempData["Flash"] = "لا يمكن حذف سجل له أبناء في الشجرة. احذف الأبناء أولاً.";
            return RedirectToPage(new { q = Q, field = Field, birthPlace = BirthPlace, level = Level });
        }

        var (_, applied) = await ApprovalService.SubmitAsync(
            db,
            appUser,
            ChangeEntity.Person,
            ChangeAction.Delete,
            person.Id,
            PersonDraft.From(person),
            $"حذف سجل أشخاص {person.RegistryCode}: {person.FullName}");

        if (applied)
        {
            TempData["Flash"] = "تم حذف السجل من سجل الأشخاص.";
            return RedirectToPage(new { q = Q, field = Field, birthPlace = BirthPlace, level = Level });
        }

        TempData["Flash"] = "تم إرسال طلب الحذف بانتظار موافقة أحد الثلاثة على صحة البيانات.";
        return RedirectToPage("/Approvals/Index");
    }

    public class FamilySecurityInfo
    {
        public int? FamilyId { get; set; }
        public string FamilyName { get; set; } = "";
        public string SecurityCode { get; set; } = "";
        public string OwnerName { get; set; } = "";
    }

    public class PendingPersonRow
    {
        public int PendingId { get; set; }
        public string Summary { get; set; } = "";
        public string SubmittedByName { get; set; } = "";
        public DateTime SubmittedAt { get; set; }
        public string RegistryCode { get; set; } = "";
        public string FullName { get; set; } = "";
        public string DocumentType { get; set; } = "";
        public string DocumentNumber { get; set; } = "";
    }
}
