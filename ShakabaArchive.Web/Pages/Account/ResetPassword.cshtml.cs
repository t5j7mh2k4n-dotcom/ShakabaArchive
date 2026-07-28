using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ShakabaArchive.Services;

namespace ShakabaArchive.Web.Pages.Account;

public class ResetPasswordModel : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string Token { get; set; } = "";

    [BindProperty, Required(ErrorMessage = "أدخل كلمة المرور الجديدة"), MinLength(6)]
    [DataType(DataType.Password)]
    public string NewPassword { get; set; } = "";

    [BindProperty, Required(ErrorMessage = "أكّد كلمة المرور")]
    [DataType(DataType.Password)]
    public string ConfirmPassword { get; set; } = "";

    public string? ErrorMessage { get; set; }
    public string? SuccessMessage { get; set; }

    public IActionResult OnGet()
    {
        if (string.IsNullOrWhiteSpace(Token))
        {
            ErrorMessage = "رابط الاستعادة غير صالح.";
            return Page();
        }

        return Page();
    }

    public IActionResult OnPost()
    {
        if (!ModelState.IsValid)
            return Page();

        if (NewPassword != ConfirmPassword)
        {
            ErrorMessage = "كلمة المرور وتأكيدها غير متطابقين.";
            return Page();
        }

        try
        {
            var (ok, error) = LocalUserService.ResetPasswordWithToken(Token, NewPassword);
            if (!ok)
            {
                ErrorMessage = error;
                return Page();
            }

            SuccessMessage = "تم تعيين كلمة المرور الجديدة. يمكنك تسجيل الدخول الآن.";
            Token = "";
            return Page();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("ResetPassword error: " + ex);
            ErrorMessage = "تعذر حفظ كلمة المرور. حاول مرة أخرى.";
            return Page();
        }
    }
}
