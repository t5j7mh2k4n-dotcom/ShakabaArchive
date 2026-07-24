using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ShakabaArchive.Models;
using ShakabaArchive.Services;

namespace ShakabaArchive.Web.Pages.Account;

public class UsersModel : PageModel
{
    public List<AppUser> Users { get; private set; } = [];
    public int ApproverCount { get; private set; }
    public int MaxApprovers { get; private set; } = ApprovalService.MaxApprovers;
    public string? Message { get; private set; }
    public string? Error { get; private set; }

    public void OnGet()
    {
        if (!User.IsInRole("Admin"))
            return;
        Load();
        Message = TempData["Flash"] as string;
        Error = TempData["FlashError"] as string;
    }

    public IActionResult OnPostSetRole(int userId, UserRole role)
    {
        if (!User.IsInRole("Admin"))
            return Forbid();

        var (ok, error) = LocalUserService.SetUserRole(userId, role);
        if (!ok)
            TempData["FlashError"] = error;
        else
            TempData["Flash"] = "تم تحديث صلاحية المستخدم.";

        return RedirectToPage();
    }

    public IActionResult OnPostSetPermissions(int userId, bool canApprove, bool isMainAdmin)
    {
        if (!User.IsInRole("Admin"))
            return Forbid();

        var role = isMainAdmin
            ? UserRole.Admin
            : canApprove
                ? UserRole.Approver
                : UserRole.Editor;

        var (ok, error) = LocalUserService.SetUserRole(userId, role);
        if (!ok)
            TempData["FlashError"] = error;
        else
            TempData["Flash"] = "تم تحديث صلاحيات المستخدم.";

        return RedirectToPage();
    }

    private void Load()
    {
        Users = LocalUserService.ListUsers();
        ApproverCount = LocalUserService.CountApprovers();
    }
}
