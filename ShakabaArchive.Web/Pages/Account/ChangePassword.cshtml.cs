using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ShakabaArchive.Services;

namespace ShakabaArchive.Web.Pages.Account;

public class ChangePasswordModel : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string? Message { get; private set; }
    public string? Error { get; private set; }

    public void OnGet()
    {
        Message = TempData["Flash"] as string;
        Error = TempData["FlashError"] as string;
    }

    public IActionResult OnPost()
    {
        if (User.Identity?.IsAuthenticated != true)
            return Challenge();

        if (!ModelState.IsValid)
        {
            Error = "أكمل الحقول بشكل صحيح.";
            return Page();
        }

        if (Input.NewPassword != Input.ConfirmPassword)
        {
            Error = "تأكيد كلمة المرور غير مطابق.";
            return Page();
        }

        var userId = int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? id
            : 0;
        if (userId <= 0)
            return Challenge();

        var (ok, error) = LocalUserService.ChangeOwnPassword(
            userId,
            Input.CurrentPassword,
            Input.NewPassword);

        if (!ok)
        {
            Error = error;
            return Page();
        }

        TempData["Flash"] = "تم تغيير كلمة المرور بنجاح. استخدم الكلمة الجديدة في الدخول القادم.";
        return RedirectToPage();
    }

    public class InputModel
    {
        [Required(ErrorMessage = "أدخل كلمة المرور الحالية")]
        [DataType(DataType.Password)]
        public string CurrentPassword { get; set; } = "";

        [Required(ErrorMessage = "أدخل كلمة المرور الجديدة")]
        [MinLength(6, ErrorMessage = "6 أحرف على الأقل")]
        [DataType(DataType.Password)]
        public string NewPassword { get; set; } = "";

        [Required(ErrorMessage = "أعد إدخال كلمة المرور الجديدة")]
        [DataType(DataType.Password)]
        public string ConfirmPassword { get; set; } = "";
    }
}
