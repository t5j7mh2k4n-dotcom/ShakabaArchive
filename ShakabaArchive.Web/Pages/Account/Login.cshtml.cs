using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ShakabaArchive.Services;
using ShakabaArchive.Web.Services;

namespace ShakabaArchive.Web.Pages.Account;

public class LoginModel(FirebaseAuthService firebase) : PageModel
{
    [BindProperty, Required(ErrorMessage = "أدخل البريد أو الهاتف")]
    public string Login { get; set; } = "";

    [BindProperty, Required(ErrorMessage = "أدخل كلمة المرور")]
    public string Password { get; set; } = "";

    public string? ErrorMessage { get; set; }
    public string? SuccessMessage { get; set; }
    public string? ReturnUrl { get; set; }

    /// <summary>يظهر زر إعادة إرسال رابط التأكيد عندما يكون البريد غير مؤكد.</summary>
    public bool NeedsEmailVerification { get; set; }

    public void OnGet(string? returnUrl = null)
    {
        ReturnUrl = returnUrl;
        SuccessMessage = TempData["Flash"] as string;
        try
        {
            if (LocalUserService.UsesCloud)
                _ = LocalUserService.ProbeAndRepairUsers();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Login warm-up: " + ex.Message);
        }
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        ReturnUrl = returnUrl;
        try
        {
            var login = Login.Trim();
            var user = LocalUserService.FindByLogin(login);

            var authenticated = false;

            // دخول بالبريد عبر Firebase إن كان مفعّلاً
            if (firebase.IsEnabled && login.Contains('@', StringComparison.Ordinal))
            {
                var (fbOk, _, _, _, verified) = await firebase.SignInAsync(login, Password);
                if (fbOk)
                {
                    if (!verified)
                    {
                        NeedsEmailVerification = true;
                        ErrorMessage =
                            "لم يتم تأكيد بريدك بعد. افتح الإيميل واضغط رابط التأكيد، أو أعد إرسال الرابط من الزر أدناه.";
                        return Page();
                    }

                    user ??= LocalUserService.FindByLogin(login);
                    if (user is not null)
                        authenticated = true;
                    else
                    {
                        ErrorMessage = "الحساب موجود في Firebase لكن غير مربوط بالأرشيف. سجّل من صفحة إنشاء حساب أو راجع الأدمن.";
                        return Page();
                    }
                }
                // إن فشل Firebase (مثلاً حساب قديم محلي فقط) نتابع التحقق المحلي أدناه
            }

            // احتياطي: التحقق المحلي (هاتف أو مستخدمون قديمون بدون Firebase)
            if (!authenticated)
            {
                if (user is null || !PasswordHasher.Verify(Password, user.PasswordHash))
                {
                    ErrorMessage = "البريد/الهاتف أو كلمة المرور غير صحيحة.";
                    return Page();
                }
            }

            var claims = new List<Claim>
            {
                new(ClaimTypes.Name, user!.DisplayName),
                new(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new(ClaimTypes.Email, user.Email),
                new("phone", user.Phone)
            };
            RoleClaims.AddRoleClaims(claims, user);

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(identity));

            if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
                return LocalRedirect(returnUrl);

            if (user.IsAdmin || user.Role == Models.UserRole.Admin)
                return RedirectToPage("/People/Index");

            return RedirectToPage("/Family/Index");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Login DB error: " + ex);
            var hint = ex.GetBaseException().Message;
            if (hint.Length > 160)
                hint = hint[..160] + "…";
            ErrorMessage = LocalUserService.UsesCloud
                ? $"تعذر تجهيز حسابات Neon. انتظر 20 ثانية ثم أعد المحاولة. التفاصيل: {hint}"
                : "قاعدة البيانات غير مربوطة. من Render → Environment ضع DATABASE_URL من Neon ثم Save and deploy.";
            return Page();
        }
    }

    public async Task<IActionResult> OnPostResendVerificationAsync()
    {
        ReturnUrl = null;
        Login = (Login ?? "").Trim();
        if (!firebase.IsEnabled || !Login.Contains('@', StringComparison.Ordinal))
        {
            ErrorMessage = "إعادة إرسال رابط التأكيد متاحة للبريد الإلكتروني فقط.";
            return Page();
        }

        if (string.IsNullOrWhiteSpace(Password))
        {
            ErrorMessage = "أدخل كلمة المرور ثم اضغط إعادة إرسال رابط التأكيد.";
            NeedsEmailVerification = true;
            return Page();
        }

        var (ok, error, _, idToken, verified) = await firebase.SignInAsync(Login, Password);
        if (!ok || string.IsNullOrEmpty(idToken))
        {
            ErrorMessage = string.IsNullOrWhiteSpace(error)
                ? "تعذر التحقق من الحساب لإعادة إرسال الرابط."
                : error;
            NeedsEmailVerification = true;
            return Page();
        }

        if (verified)
        {
            SuccessMessage = "بريدك مؤكد بالفعل. يمكنك تسجيل الدخول الآن.";
            return Page();
        }

        var continueUrl = Url.Page("/Account/Login", null, null, Request.Scheme);
        var (sent, sendError) = await firebase.SendEmailVerificationAsync(idToken, continueUrl);
        if (!sent)
        {
            ErrorMessage = sendError;
            NeedsEmailVerification = true;
            return Page();
        }

        SuccessMessage = "أُرسل رابط تأكيد جديد إلى بريدك. افتحه ثم سجّل الدخول.";
        NeedsEmailVerification = true;
        return Page();
    }
}
