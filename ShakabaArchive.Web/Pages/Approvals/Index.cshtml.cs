using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ShakabaArchive.Data;
using ShakabaArchive.Models;
using ShakabaArchive.Services;

namespace ShakabaArchive.Web.Pages.Approvals;

public class IndexModel : PageModel
{
    public List<PendingChange> Items { get; private set; } = [];
    public bool CanReview { get; private set; }
    public int CurrentUserId { get; private set; }
    public int PendingCount { get; private set; }
    public int TotalCount { get; private set; }
    public int MissingInRegistryCount { get; private set; }
    public HashSet<int> PersonIdsInRegistry { get; private set; } = [];
    public string Filter { get; private set; } = "pending";
    public string? Message { get; private set; }
    public string? Error { get; private set; }

    public async Task OnGetAsync(string? filter = null)
    {
        Message = TempData["Flash"] as string;
        Error = TempData["FlashError"] as string;
        CurrentUserId = ReadUserId();
        CanReview = ResolveCanReview();
        Filter = NormalizeFilter(filter);

        try
        {
            await LoadItemsAsync();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Approvals OnGet: " + ex);
            Error = "تعذر تحميل الطلبات الآن. افتح /health/db?repair=1 ثم حدّث هذه الصفحة بعد 10 ثوانٍ.";
            Items = [];
        }
    }

    public async Task<IActionResult> OnPostApproveAsync(int id, string? note, string? filter = null)
    {
        var appUser = ResolveAppUser();
        if (appUser is null)
        {
            TempData["FlashError"] = "انتهت الجلسة أو تعذر قراءة الحساب. أعد الدخول ثم حاول مرة أخرى.";
            return RedirectToPage("/Account/Login");
        }

        if (!appUser.CanApprove)
        {
            TempData["FlashError"] = "ليست لديك صلاحية الموافقة.";
            return RedirectToPage(new { filter });
        }

        try
        {
            await using var db = DatabaseService.CreateContext();
            await ApprovalService.EnsureSchemaAsync(db);
            var (ok, error, createdPersonId) = await ApprovalService.ApproveAsync(db, appUser, id, note);
            if (!ok)
            {
                TempData["FlashError"] = error;
                return RedirectToPage(new { filter = filter ?? "pending" });
            }

            // بعد حفظ الشخص في السجل — فتح صفحة إضافة مناسبة مباشرة
            if (createdPersonId is int personId)
            {
                TempData["Flash"] = "تمت الموافقة وحُفظ الشخص في سجل الأشخاص. يمكنك الآن تسجيل مناسبة له.";
                return RedirectToPage("/Occasions/Create", new { personId });
            }

            TempData["Flash"] = "تمت الموافقة وحفظ الطلب بنجاح.";
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Approve: " + ex);
            TempData["FlashError"] = "تعذر الاعتماد الآن لأن القاعدة تُجهَّز. انتظر قليلاً ثم أعد المحاولة.";
        }

        return RedirectToPage(new { filter = filter ?? "pending" });
    }

    public async Task<IActionResult> OnPostRejectAsync(int id, string? note, string? filter = null)
    {
        var appUser = ResolveAppUser();
        if (appUser is null)
        {
            TempData["FlashError"] = "انتهت الجلسة أو تعذر قراءة الحساب. أعد الدخول ثم حاول مرة أخرى.";
            return RedirectToPage("/Account/Login");
        }

        if (!appUser.CanApprove)
        {
            TempData["FlashError"] = "ليست لديك صلاحية الرفض.";
            return RedirectToPage(new { filter });
        }

        try
        {
            await using var db = DatabaseService.CreateContext();
            await ApprovalService.EnsureSchemaAsync(db);
            var (ok, error) = await ApprovalService.RejectAsync(db, appUser, id, note);
            if (!ok)
                TempData["FlashError"] = error;
            else
                TempData["Flash"] = "تم رفض الطلب — لم يُطبَّق أي تغيير.";
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Reject: " + ex);
            TempData["FlashError"] = "تعذر الرفض الآن لأن القاعدة تُجهَّز. انتظر قليلاً ثم أعد المحاولة.";
        }

        return RedirectToPage(new { filter = filter ?? "pending" });
    }

    /// <summary>ترحيل الطلبات المعتمدة إلى سجل الأشخاص بزر يدوي.</summary>
    public async Task<IActionResult> OnPostMigrateApprovedAsync(string? filter = null)
    {
        var appUser = ResolveAppUser();
        if (appUser is null || !appUser.CanApprove)
        {
            TempData["FlashError"] = "ليست لديك صلاحية ترحيل الطلبات.";
            return RedirectToPage(new { filter = filter ?? "approved" });
        }

        try
        {
            await using var db = DatabaseService.CreateContext();
            await ApprovalService.EnsureSchemaAsync(db);
            var result = await ApprovalService.RepairApprovedPersonCreatesAsync(db);

            if (result.Migrated == 0 && result.Linked == 0)
            {
                if (result.Failed > 0)
                {
                    TempData["FlashError"] =
                        $"تعذر ترحيل {result.Failed} طلب. " +
                        string.Join(" | ", result.Errors.Take(3));
                }
                else
                {
                    TempData["Flash"] = "كل طلبات إضافة الأشخاص المعتمدة موجودة مسبقاً في السجل. (موافقة الحساب وحده لا تُنشئ سجل شخص)";
                }
            }
            else
            {
                TempData["Flash"] =
                    $"تم الترحيل إلى السجل: جديد {result.Migrated}" +
                    (result.Linked > 0 ? $"، مربوط {result.Linked}" : "") +
                    (result.Failed > 0 ? $"، فشل {result.Failed}" : "") +
                    ". افتح سجل الأشخاص للتأكد.";
                if (result.Failed > 0 && result.Errors.Count > 0)
                    TempData["FlashError"] = string.Join(" | ", result.Errors.Take(3));
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("MigrateApproved: " + ex);
            TempData["FlashError"] = "تعذر الترحيل الآن: " + ex.GetBaseException().Message;
        }

        return RedirectToPage(new { filter = filter ?? "approved" });
    }

    public async Task<IActionResult> OnPostMigrateOneAsync(int id, string? filter = null)
    {
        var appUser = ResolveAppUser();
        if (appUser is null || !appUser.CanApprove)
        {
            TempData["FlashError"] = "ليست لديك صلاحية الترحيل.";
            return RedirectToPage(new { filter = filter ?? "approved" });
        }

        try
        {
            await using var db = DatabaseService.CreateContext();
            var (ok, error, personId) = await ApprovalService.MigrateOneApprovedPersonAsync(db, id);
            if (!ok)
            {
                TempData["FlashError"] = error;
                return RedirectToPage(new { filter = filter ?? "approved" });
            }

            TempData["Flash"] = string.IsNullOrWhiteSpace(error)
                ? "تم حفظ الشخص في سجل الأشخاص."
                : error;
            if (personId is int pid)
                return RedirectToPage("/People/Details", new { id = pid });
        }
        catch (Exception ex)
        {
            TempData["FlashError"] = "تعذر الترحيل: " + ex.GetBaseException().Message;
        }

        return RedirectToPage(new { filter = filter ?? "approved" });
    }

    private static string NormalizeFilter(string? filter) =>
        filter is "all" or "approved" or "rejected" ? filter : "pending";

    private bool ResolveCanReview()
    {
        if (User.IsInRole("Admin") || User.IsInRole("Approver"))
            return true;
        return User.CurrentAppUser()?.CanApprove == true;
    }

    private async Task LoadItemsAsync()
    {
        Exception? last = null;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                await using var db = DatabaseService.CreateContext();
                await ApprovalService.EnsureSchemaAsync(db);
                if (CanReview)
                {
                    await ApprovalService.EnsureUserRegistrationPendingsAsync(db);
                    MissingInRegistryCount =
                        await ApprovalService.CountApprovedPersonsMissingFromRegistryAsync(db);
                }

                PendingCount = await db.PendingChanges.AsNoTracking()
                    .CountAsync(x => x.Status == ChangeStatus.Pending);
                TotalCount = await db.PendingChanges.AsNoTracking().CountAsync();

                IQueryable<PendingChange> query = db.PendingChanges.AsNoTracking();

                if (!CanReview)
                {
                    if (CurrentUserId <= 0)
                    {
                        Items = [];
                        return;
                    }

                    query = query.Where(x => x.SubmittedByUserId == CurrentUserId);
                }

                query = Filter switch
                {
                    "approved" => query.Where(x => x.Status == ChangeStatus.Approved),
                    "rejected" => query.Where(x => x.Status == ChangeStatus.Rejected),
                    "all" => query,
                    _ => query.Where(x => x.Status == ChangeStatus.Pending)
                };

                Items = await query
                    .OrderByDescending(x => x.SubmittedAt)
                    .ThenByDescending(x => x.Id)
                    .Take(200)
                    .ToListAsync();

                var entityIds = Items
                    .Where(x => x.EntityType == ChangeEntity.Person && x.EntityId is > 0)
                    .Select(x => x.EntityId!.Value)
                    .Distinct()
                    .ToList();
                if (entityIds.Count > 0)
                {
                    PersonIdsInRegistry = (await db.People.AsNoTracking()
                            .Where(p => entityIds.Contains(p.Id))
                            .Select(p => p.Id)
                            .ToListAsync())
                        .ToHashSet();
                }

                return;
            }
            catch (Exception ex)
            {
                last = ex;
                Console.Error.WriteLine($"Approvals Load attempt {attempt}/3: {ex.Message}");
                await Task.Delay(800 * attempt);
            }
        }

        throw last ?? new InvalidOperationException("تعذر تحميل طلبات الموافقة.");
    }

    private int ReadUserId() =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;

    private AppUser? ResolveAppUser()
    {
        var fromDb = User.CurrentAppUser();
        if (fromDb is not null)
            return fromDb;

        if (User.Identity?.IsAuthenticated != true)
            return null;

        var id = ReadUserId();
        if (id <= 0)
            return null;

        var role = User.IsInRole("Admin")
            ? UserRole.Admin
            : User.IsInRole("Approver")
                ? UserRole.Approver
                : UserRole.Editor;

        return new AppUser
        {
            Id = id,
            DisplayName = User.Identity.Name ?? "",
            Email = User.FindFirstValue(ClaimTypes.Email) ?? "",
            Phone = User.FindFirstValue("phone") ?? "",
            IsAdmin = role == UserRole.Admin,
            Role = role
        };
    }
}
