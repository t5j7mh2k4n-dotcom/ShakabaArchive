using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ShakabaArchive.Services;

namespace ShakabaArchive.Web.Pages.Account;

public class LoginModel : PageModel
{
    [BindProperty, Required(ErrorMessage = "أدخل البريد أو الهاتف")]
    public string Login { get; set; } = "";

    [BindProperty, Required(ErrorMessage = "أدخل كلمة المرور")]
    public string Password { get; set; } = "";

    public string? ErrorMessage { get; set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        try
        {
            var user = LocalUserService.FindByLogin(Login);
            if (user is null || !PasswordHasher.Verify(Password, user.PasswordHash))
            {
                ErrorMessage = "البريد/الهاتف أو كلمة المرور غير صحيحة.";
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

            return LocalRedirect(string.IsNullOrWhiteSpace(returnUrl) ? "/People" : returnUrl);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Login DB error: " + ex);
            ErrorMessage = LocalUserService.UsesCloud
                ? "تعذر الاتصال بـ Neon مؤقتاً (قد تكون القاعدة نائمة). انتظر دقيقة ثم أعد المحاولة. تأكد أن DATABASE_URL كامل ويبدأ بـ postgresql://"
                : "قاعدة البيانات غير مربوطة. من Render → Environment ضع DATABASE_URL من Neon ثم Save and deploy.";
            return Page();
        }
    }
}
