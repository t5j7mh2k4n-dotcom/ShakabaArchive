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
    public string? Message { get; private set; }
    public string? Error { get; private set; }

    public async Task OnGetAsync()
    {
        Message = TempData["Flash"] as string;
        Error = TempData["FlashError"] as string;
        CurrentUserId = ReadUserId();
        CanReview = ResolveCanReview();

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

    public async Task<IActionResult> OnPostApproveAsync(int id, string? note)
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
            return RedirectToPage();
        }

        try
        {
            await using var db = DatabaseService.CreateContext();
            await ApprovalService.EnsureSchemaAsync(db);
            var (ok, error) = await ApprovalService.ApproveAsync(db, appUser, id, note);
            if (!ok)
                TempData["FlashError"] = error;
            else
                TempData["Flash"] = "تمت الموافقة على صحة البيانات وحفظها في الأرشيف.";
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Approve: " + ex);
            TempData["FlashError"] = "تعذر الاعتماد الآن لأن القاعدة تُجهَّز. انتظر قليلاً ثم أعد المحاولة.";
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRejectAsync(int id, string? note)
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
            return RedirectToPage();
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

        return RedirectToPage();
    }

    private bool ResolveCanReview()
    {
        if (User.IsInRole("Admin") || User.IsInRole("Approver"))
            return true;

        // الجلسة القديمة قد لا تحمل الأدوار — نعتمد قاعدة البيانات
        var fromDb = User.CurrentAppUser();
        return fromDb?.CanApprove == true;
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

                PendingCount = await db.PendingChanges.AsNoTracking()
                    .CountAsync(x => x.Status == ChangeStatus.Pending);

                IQueryable<PendingChange> query = db.PendingChanges.AsNoTracking();

                // الأدمن/الموافق: كل الطلبات — المدخل: طلباته فقط
                if (!CanReview)
                {
                    if (CurrentUserId <= 0)
                    {
                        Items = [];
                        return;
                    }

                    query = query.Where(x => x.SubmittedByUserId == CurrentUserId);
                }

                var rows = await query
                    .OrderByDescending(x => x.Status == ChangeStatus.Pending)
                    .ThenByDescending(x => x.SubmittedAt)
                    .Take(200)
                    .ToListAsync();

                Items = rows;
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

    /// <summary>يبني المستخدم من الجلسة إن تعذر قراءة Neon مؤقتاً — حتى لا يُطرد للخروج.</summary>
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
