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
    public string Login { get; set; } = "abohosam@shukaba.local";

    [BindProperty, Required(ErrorMessage = "أدخل كلمة المرور")]
    public string Password { get; set; } = "";

    public string? ErrorMessage { get; set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
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
        if (user.IsAdmin)
            claims.Add(new Claim(ClaimTypes.Role, "Admin"));

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity));

        return LocalRedirect(string.IsNullOrWhiteSpace(returnUrl) ? "/People" : returnUrl);
    }
}
