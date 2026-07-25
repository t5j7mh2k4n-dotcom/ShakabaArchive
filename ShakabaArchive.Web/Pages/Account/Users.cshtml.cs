using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
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

    [BindProperty(SupportsGet = true)]
    public int? EditId { get; set; }

    public AppUser? EditingUser { get; private set; }

    [BindProperty]
    public UserFormInput Form { get; set; } = new();

    public void OnGet()
    {
        if (!User.IsInRole("Admin"))
            return;
        Load();
        Message = TempData["Flash"] as string;
        Error = TempData["FlashError"] as string;

        if (EditId is int id)
        {
            EditingUser = Users.FirstOrDefault(u => u.Id == id);
            if (EditingUser is not null)
            {
                Form = new UserFormInput
                {
                    DisplayName = EditingUser.DisplayName,
                    Email = EditingUser.Email,
                    Phone = EditingUser.Phone,
                    Role = EditingUser.IsAdmin || EditingUser.Role == UserRole.Admin
                        ? UserRole.Admin
                        : EditingUser.Role
                };
            }
        }
    }

    public IActionResult OnPostCreate()
    {
        if (!User.IsInRole("Admin"))
            return Forbid();

        try
        {
            LocalUserService.EnsureReady();
            var (ok, error, created) = LocalUserService.CreateUser(
                Form.Email,
                Form.Phone,
                Form.DisplayName,
                Form.Password ?? "",
                Form.Role);

            if (!ok)
                TempData["FlashError"] = error;
            else
                TempData["Flash"] = $"تمت إضافة المستخدم «{created?.DisplayName}» بنجاح ويظهر في القائمة أدناه.";
        }
        catch (Exception ex)
        {
            TempData["FlashError"] = "تعذر إضافة المستخدم: " + ex.Message;
        }

        return RedirectToPage();
    }

    public IActionResult OnPostUpdate(int userId)
    {
        if (!User.IsInRole("Admin"))
            return Forbid();

        var (ok, error) = LocalUserService.UpdateUser(
            userId,
            Form.Email,
            Form.Phone,
            Form.DisplayName,
            string.IsNullOrWhiteSpace(Form.Password) ? null : Form.Password,
            Form.Role);

        if (!ok)
        {
            TempData["FlashError"] = error;
            return RedirectToPage(new { editId = userId });
        }

        TempData["Flash"] = "تم تعديل المستخدم بنجاح.";
        return RedirectToPage();
    }

    public IActionResult OnPostDelete(int userId)
    {
        if (!User.IsInRole("Admin"))
            return Forbid();

        var currentId = int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? id
            : (int?)null;

        var (ok, error) = LocalUserService.DeleteUser(userId, currentId);
        if (!ok)
            TempData["FlashError"] = error;
        else
            TempData["Flash"] = "تم حذف المستخدم.";

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

    public class UserFormInput
    {
        [Required(ErrorMessage = "أدخل الاسم")]
        public string DisplayName { get; set; } = "";

        [Required(ErrorMessage = "أدخل البريد")]
        public string Email { get; set; } = "";

        [Required(ErrorMessage = "أدخل الهاتف")]
        public string Phone { get; set; } = "";

        public string? Password { get; set; }

        public UserRole Role { get; set; } = UserRole.Editor;
    }
}
