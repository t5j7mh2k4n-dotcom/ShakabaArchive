using System.Security.Claims;
using ShakabaArchive.Models;
using ShakabaArchive.Services;

namespace ShakabaArchive.Web;

public static class AuthExtensions
{
    public static AppUser? CurrentAppUser(this ClaimsPrincipal user)
    {
        try
        {
            var idValue = user.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(idValue, out var id))
                return null;
            return LocalUserService.FindById(id);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("CurrentAppUser: " + ex.Message);
            return null;
        }
    }

    public static bool IsApprover(this ClaimsPrincipal user) =>
        user.IsInRole("Admin") || user.IsInRole("Approver");
}
