using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ShakabaArchive.Services;

namespace ShakabaArchive.Web.Pages.Account;

public class RegisterModel : PageModel
{
    [BindProperty, Required(ErrorMessage = "أدخل الاسم")]
    public string DisplayName { get; set; } = "";

    [BindProperty, Required(ErrorMessage = "أدخل البريد"), EmailAddress]
    public string Email { get; set; } = "";

    [BindProperty, Required(ErrorMessage = "أدخل الهاتف")]
    public string Phone { get; set; } = "";

    [BindProperty, Required(ErrorMessage = "أدخل كلمة المرور"), MinLength(6)]
    public string Password { get; set; } = "";

    [BindProperty, Required(ErrorMessage = "أعد إدخال كلمة المرور")]
    public string ConfirmPassword { get; set; } = "";

    public string? ErrorMessage { get; set; }

    public IActionResult OnGet()
    {
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToPage("/People/Create");
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (Password != ConfirmPassword)
        {
            ErrorMessage = "تأكيد كلمة المرور غير مطابق.";
            return Page();
        }

        try
        {
            var (ok, error, user) = LocalUserService.RegisterPublic(Email, Phone, DisplayName, Password);
            if (!ok || user is null)
            {
                ErrorMessage = error;
                return Page();
            }

            var claims = new List<Claim>
            {
                new(ClaimTypes.Name, user.DisplayName),
                new(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new(ClaimTypes.Email, user.Email),
                new("phone", user.Phone)
            };
            RoleClaims.AddRoleClaims(claims, user);

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(identity));

            TempData["Flash"] = "تم إنشاء حسابك. أضف بياناتك الآن — ستُرفع لموافقة الأدمن.";
            return RedirectToPage("/People/Create");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Register: " + ex);
            ErrorMessage = "تعذر الاتصال بقاعدة البيانات مؤقتاً. انتظر نصف دقيقة ثم أعد المحاولة.";
            return Page();
        }
    }
}
