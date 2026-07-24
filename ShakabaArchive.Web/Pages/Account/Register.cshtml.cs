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
    [BindProperty, Required]
    public string InviteCode { get; set; } = "";

    [BindProperty, Required]
    public string DisplayName { get; set; } = "";

    [BindProperty, Required, EmailAddress]
    public string Email { get; set; } = "";

    [BindProperty, Required]
    public string Phone { get; set; } = "";

    [BindProperty, Required, MinLength(6)]
    public string Password { get; set; } = "";

    public string? ErrorMessage { get; set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        try
        {
            var (ok, error, user) = LocalUserService.Register(Email, Phone, DisplayName, Password, InviteCode);
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

            return RedirectToPage("/People/Index");
        }
        catch (Exception)
        {
            ErrorMessage = "تعذر الاتصال بقاعدة البيانات مؤقتاً. انتظر نصف دقيقة ثم أعد المحاولة.";
            return Page();
        }
    }
}
