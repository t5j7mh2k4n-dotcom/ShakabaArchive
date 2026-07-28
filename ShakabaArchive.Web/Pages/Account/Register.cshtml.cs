using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using ShakabaArchive.Data;
using ShakabaArchive.Models;
using ShakabaArchive.Services;
using ShakabaArchive.Web.Services;

namespace ShakabaArchive.Web.Pages.Account;

public class RegisterModel(
    FirebaseAuthService firebase,
    IOptions<FirebaseOptions> firebaseOptions,
    ArchiveDbContext db) : PageModel
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
            string? idToken = null;

            // 1) إنشاء الحساب في Firebase أولاً (إن كان مفعّلاً)
            if (firebase.IsEnabled)
            {
                var (fbOk, fbError, fbCode, _, token) = await firebase.SignUpAsync(Email, Password);
                if (!fbOk)
                {
                    // حساب Firebase موجود مسبقاً بنفس كلمة المرور → اربط بالأرشيف المحلي
                    if (string.Equals(fbCode, "EMAIL_EXISTS", StringComparison.OrdinalIgnoreCase))
                    {
                        var (inOk, _, _, inToken, verified) = await firebase.SignInAsync(Email, Password);
                        if (!inOk)
                        {
                            ErrorMessage = "هذا البريد مسجّل في Firebase بكلمة مرور مختلفة. سجّل الدخول أو استخدم بريداً آخر.";
                            return Page();
                        }

                        idToken = inToken;
                        if (verified)
                        {
                            var (okExisting, errExisting, userExisting) =
                                LocalUserService.RegisterPublic(Email, Phone, DisplayName, Password);
                            if (!okExisting || userExisting is null)
                            {
                                ErrorMessage = string.IsNullOrWhiteSpace(errExisting)
                                    ? "الحساب موجود. سجّل الدخول من صفحة الدخول."
                                    : errExisting;
                                return Page();
                            }

                            await EnsureFamilyForNewUserAsync(userExisting);
                            TempData["Flash"] = "تم ربط حسابك. يمكنك الدخول الآن.";
                            return RedirectToPage("/Account/Login");
                        }
                    }
                    else
                    {
                        ErrorMessage = fbError;
                        return Page();
                    }
                }
                else
                {
                    idToken = token;
                }

                // إرسال رابط التأكيد إلى البريد
                if (!string.IsNullOrEmpty(idToken))
                {
                    var continueUrl = Url.Page("/Account/Login", null, null, Request.Scheme);
                    var (sent, sendError) = await firebase.SendEmailVerificationAsync(idToken, continueUrl);
                    if (!sent)
                    {
                        ErrorMessage = "تم إنشاء الحساب لكن تعذر إرسال رابط التأكيد: " + sendError;
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

            // 3) إنشاء سجل الأسرة فوراً: اسم صاحب الأسرة + رمز أمان ثابت
            var family = await EnsureFamilyForNewUserAsync(user);

            if (firebase.IsEnabled)
            {
                TempData["Flash"] =
                    $"تم إنشاء حسابك وأسرة «{family.Name}» برمز الأمان {family.SecurityCode}. " +
                    "افتح بريدك واضغط رابط التأكيد ثم سجّل الدخول.";
                return RedirectToPage("/Account/Login");
            }

            TempData["Flash"] =
                $"تم إنشاء حسابك وأسرة «{family.Name}» برمز الأمان {family.SecurityCode}. سجّل الدخول ثم افتح سجل الأسرة.";
            return RedirectToPage("/Account/Login");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Register: " + ex);
            ErrorMessage = "تعذر إكمال التسجيل مؤقتاً. انتظر نصف دقيقة ثم أعد المحاولة.";
            return Page();
        }
    }

    private async Task<ShakabaArchive.Models.Family> EnsureFamilyForNewUserAsync(AppUser user)
    {
        var family = await FamilyRegistryService.GetOrCreateAsync(db, user);
        var desiredName = string.IsNullOrWhiteSpace(user.DisplayName)
            ? "أسرتي"
            : $"أسرة {user.DisplayName.Trim()}";
        if (!string.Equals(family.Name, desiredName, StringComparison.Ordinal))
        {
            family.Name = desiredName;
            family.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        return family;
    }
}
