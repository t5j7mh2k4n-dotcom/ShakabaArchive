using System.Security.Claims;
using ShakabaArchive.Models;

namespace ShakabaArchive.Web;

public static class RoleClaims
{
    public static void AddRoleClaims(List<Claim> claims, AppUser user)
    {
        if (user.IsAdmin || user.Role == UserRole.Admin)
        {
            claims.Add(new Claim(ClaimTypes.Role, "Admin"));
            claims.Add(new Claim(ClaimTypes.Role, "Approver"));
            return;
        }

        if (user.Role == UserRole.Approver)
        {
            claims.Add(new Claim(ClaimTypes.Role, "Approver"));
            return;
        }

        claims.Add(new Claim(ClaimTypes.Role, "Editor"));
    }

    public static string ToArabic(UserRole role) => role switch
    {
        UserRole.Admin => "أدمن رئيسي",
        UserRole.Approver => "موافق على صحة البيانات",
        _ => "مدخل بيانات"
    };
}
