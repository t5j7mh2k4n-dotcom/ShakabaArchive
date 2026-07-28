using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ShakabaArchive.Services;

namespace ShakabaArchive.Web.Pages.Account;

public class ForgotPasswordModel : PageModel
{
    [BindProperty, Required(ErrorMessage = "أدخل البريد الإلكتروني"), EmailAddress]
    public string Email { get; set; } = "";

    [BindProperty, Required(ErrorMessage = "أدخل رقم الهاتف")]
    public string Phone { get; set; } = "";

    public string? ErrorMessage { get; set; }

    public void OnGet()
    {
    }

    public IActionResult OnPost()
    {
        if (!ModelState.IsValid)
            return Page();

        try
        {
            var (ok, error, token) = LocalUserService.RequestPasswordReset(Email, Phone);
            if (!ok || string.IsNullOrWhiteSpace(token))
            {
                ErrorMessage = error;
                return Page();
            }

            return RedirectToPage("./ResetPassword", new { token });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("ForgotPassword error: " + ex);
            ErrorMessage = "تعذر معالجة الطلب. حاول مرة أخرى بعد قليل.";
            return Page();
        }
    }
}
