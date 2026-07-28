using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ShakabaArchive.Web.Pages;

public class IndexModel : PageModel
{
    public IActionResult OnGet()
    {
        // بوابة التطبيق: الزائر يرى انضمام/دخول؛ المسجّل يُوجَّه لسجل الأسرة
        if (User.Identity?.IsAuthenticated == true)
        {
            var user = User.CurrentAppUser();
            var isAdmin = User.IsInRole("Admin") || user?.IsAdmin == true || user?.Role == Models.UserRole.Admin;
            if (!isAdmin)
                return RedirectToPage("/Family/Index");
        }

        return Page();
    }
}
