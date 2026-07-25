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

    [BindProperty(SupportsGet = true)]
    public string? Q { get; set; }

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
        }
        catch (Exception ex)
        {
            TempData["Flash"] = "قاعدة البيانات تُجهَّز الآن، أعد التحديث بعد ثوانٍ. " + ex.Message;
            return;
        }

        var places = await db.People.AsNoTracking()
            .Select(p => p.BirthPlace)
            .Where(n => n != "")
            .Distinct()
            .OrderBy(n => n)
            .ToListAsync();

        BirthPlaceOptions = places
            .Select(n => new SelectListItem(n, n, n == BirthPlace))
            .ToList();

        IQueryable<Person> query = db.People.AsNoTracking().Include(p => p.Events);

        if (Level is >= 1 and <= 3)
            query = query.Where(p => p.HierarchyLevel == Level);

        if (!string.IsNullOrWhiteSpace(BirthPlace))
            query = query.Where(p => p.BirthPlace == BirthPlace);

        if (!string.IsNullOrWhiteSpace(Q))
        {
            var q = Q.Trim();
            query = query.Where(p =>
                p.RegistryCode.Contains(q) ||
                p.NationalId.Contains(q) ||
                p.DocumentNumber.Contains(q) ||
                p.DocumentType.Contains(q) ||
                p.FullName.Contains(q) ||
                p.FirstName.Contains(q) ||
                p.FatherName.Contains(q) ||
                p.GrandfatherName.Contains(q) ||
                p.FamilyName.Contains(q) ||
                p.MotherName.Contains(q) ||
                p.Tribe.Contains(q) ||
                p.Profession.Contains(q) ||
                p.Phone.Contains(q) ||
                p.BirthPlace.Contains(q) ||
                p.Neighborhood.Contains(q) ||
                p.Residence.Contains(q) ||
                p.Notes.Contains(q));
        }

        People = await query
            .OrderBy(p => p.RegistryCode)
            .ThenBy(p => p.FullName)
            .ToListAsync();

        // طلبات الإضافة التي لم تُعتمد بعد — تظهر هنا حتى لا يظن المستخدم أنها ضاعت
        if (User.Identity?.IsAuthenticated == true)
        {
            var pending = await db.PendingChanges.AsNoTracking()
                .Where(x => x.Status == ChangeStatus.Pending
                            && x.EntityType == ChangeEntity.Person
                            && x.Action == ChangeAction.Create)
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

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var appUser = User.CurrentAppUser();
        if (appUser is null)
            return Forbid();

        var person = await db.People.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);
        if (person is null)
            return RedirectToPage(new { q = Q, birthPlace = BirthPlace, level = Level });

        if (await db.People.AnyAsync(p => p.ParentPersonId == id))
        {
            TempData["Flash"] = "لا يمكن حذف سجل له أبناء في الشجرة. احذف الأبناء أولاً.";
            return RedirectToPage(new { q = Q, birthPlace = BirthPlace, level = Level });
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
            return RedirectToPage(new { q = Q, birthPlace = BirthPlace, level = Level });
        }

        TempData["Flash"] = "تم إرسال طلب الحذف بانتظار موافقة أحد الثلاثة على صحة البيانات.";
        return RedirectToPage("/Approvals/Index");
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
