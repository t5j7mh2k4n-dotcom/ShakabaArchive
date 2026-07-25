using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ShakabaArchive.Data;
using ShakabaArchive.Models;
using ShakabaArchive.Services;

namespace ShakabaArchive.Web.Pages.Approvals;

public class IndexModel(ArchiveDbContext db) : PageModel
{
    public List<PendingChange> Items { get; private set; } = [];
    public bool CanReview { get; private set; }
    public int CurrentUserId { get; private set; }
    public string? Message { get; private set; }
    public string? Error { get; private set; }

    public async Task OnGetAsync()
    {
        Message = TempData["Flash"] as string;
        try
        {
            await LoadAsync();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Approvals OnGet: " + ex);
            Error = "قاعدة البيانات تُجهَّز الآن. انتظر 20 ثانية ثم حدّث الصفحة.";
            Items = [];
        }
    }

    public async Task<IActionResult> OnPostApproveAsync(int id, string? note)
    {
        var appUser = User.CurrentAppUser();
        if (appUser is null) return Challenge();

        var (ok, error) = await ApprovalService.ApproveAsync(db, appUser, id, note);
        if (!ok)
            TempData["FlashError"] = error;
        else
            TempData["Flash"] = "تمت الموافقة على صحة البيانات وحفظها في الأرشيف.";

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostRejectAsync(int id, string? note)
    {
        var appUser = User.CurrentAppUser();
        if (appUser is null) return Challenge();

        var (ok, error) = await ApprovalService.RejectAsync(db, appUser, id, note);
        if (!ok)
            TempData["FlashError"] = error;
        else
            TempData["Flash"] = "تم رفض الطلب — لم يُطبَّق أي تغيير.";

        return RedirectToPage();
    }

    private async Task LoadAsync()
    {
        DatabaseService.EnsureReady();
        await ApprovalService.EnsureSchemaAsync(db);

        var appUser = User.CurrentAppUser();
        CanReview = appUser?.CanApprove == true;
        CurrentUserId = appUser?.Id ?? 0;
        Error ??= TempData["FlashError"] as string;

        Exception? last = null;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                Items = await db.PendingChanges.AsNoTracking()
                    .OrderByDescending(x => x.Status == ChangeStatus.Pending)
                    .ThenByDescending(x => x.SubmittedAt)
                    .Take(100)
                    .ToListAsync();
                return;
            }
            catch (Exception ex)
            {
                last = ex;
                Console.Error.WriteLine($"Approvals Load attempt {attempt}/3: {ex.Message}");
                DatabaseService.ResetInitialization();
                await Task.Delay(1500 * attempt);
                DatabaseService.EnsureReady();
                await ApprovalService.EnsureSchemaAsync(db);
            }
        }

        throw last ?? new InvalidOperationException("تعذر تحميل طلبات الموافقة.");
    }
}
