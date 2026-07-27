using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using ShakabaArchive.Services;
using ShakabaArchive.Web.Services;

namespace ShakabaArchive.Web.Pages.Account;

public class RegisterModel(
    FirebaseAuthService firebase,
    IOptions<FirebaseOptions> firebaseOptions) : PageModel
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
    public bool FirebaseEnabled => firebaseOptions.Value.IsConfigured;

    public IActionResult OnGet()
    {
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToPage("/Family/Index");
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
            // 1) إنشاء الحساب في Firebase أولاً (إن كان مفعّلاً)
            if (firebase.IsEnabled)
            {
                var (fbOk, fbError, fbCode, _) = await firebase.SignUpAsync(Email, Password);
                if (!fbOk)
                {
                    // حساب Firebase موجود مسبقاً بنفس كلمة المرور → اربط بالأرشيف المحلي
                    if (string.Equals(fbCode, "EMAIL_EXISTS", StringComparison.OrdinalIgnoreCase))
                    {
                        var (inOk, inError, _) = await firebase.SignInAsync(Email, Password);
                        if (!inOk)
                        {
                            ErrorMessage = "هذا البريد مسجّل في Firebase بكلمة مرور مختلفة. سجّل الدخول أو استخدم بريداً آخر.";
                            return Page();
                        }
                    }
                    else
                    {
                        ErrorMessage = fbError;
                        return Page();
                    }
                }
            }

            // 2) إنشاء المستخدم المحلي (أدوار الأرشيف / ملكية السجلات)
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

            TempData["Flash"] = firebase.IsEnabled
                ? "تم إنشاء حسابك عبر Firebase. افتح سجل الأسرة وأضف أفراد أسرتك."
                : "تم إنشاء حسابك. افتح سجل الأسرة وأضف أفراد أسرتك.";
            return RedirectToPage("/Family/Index");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Register: " + ex);
            ErrorMessage = "تعذر إكمال التسجيل مؤقتاً. انتظر نصف دقيقة ثم أعد المحاولة.";
            return Page();
        }
    }
}
