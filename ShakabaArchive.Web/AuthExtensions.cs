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

    /// <summary>هل يمكن للمستخدم تعديل سجل الأشخاص هذا؟ المالك فقط، أو أدمن/موافق.</summary>
    public static bool CanEditPerson(this ClaimsPrincipal user, Person person)
    {
        if (user.Identity?.IsAuthenticated != true)
            return false;

        if (user.IsInRole("Admin") || user.IsInRole("Approver"))
            return true;

        var appUser = user.CurrentAppUser();
        if (appUser is null)
            return false;

        if (appUser.CanApprove)
            return true;

        if (person.OwnerUserId is int ownerId && ownerId == appUser.Id)
            return true;

        // توافق مع سجلات قديمة بلا مالك: تطابق الهاتف
        if (person.OwnerUserId is null
            && !string.IsNullOrWhiteSpace(appUser.Phone)
            && !string.IsNullOrWhiteSpace(person.Phone)
            && string.Equals(appUser.Phone.Trim(), person.Phone.Trim(), StringComparison.Ordinal))
            return true;

        return false;
    }

    /// <summary>سجل ناقص يحتاج إكمال حقول أساسية.</summary>
    public static bool IsPersonIncomplete(Person person) =>
        (!string.IsNullOrEmpty(person.Notes)
         && person.Notes.Contains(ApprovalService.IncompleteProfileMarker, StringComparison.Ordinal))
        || string.IsNullOrWhiteSpace(person.FatherName)
        || string.IsNullOrWhiteSpace(person.FamilyName)
        || (string.IsNullOrWhiteSpace(person.DocumentNumber) && string.IsNullOrWhiteSpace(person.NationalId))
        || string.IsNullOrWhiteSpace(person.Phone);

    /// <summary>
    /// إكمال البيانات: المالك/الموافق، أو أي مستخدم مسجّل لسجل ناقص
    /// (مثل الروابط المرسلة واتساب بعد الترحيل).
    /// </summary>
    public static bool CanCompletePerson(this ClaimsPrincipal user, Person person)
    {
        if (user.Identity?.IsAuthenticated != true)
            return false;

        if (user.CanEditPerson(person))
            return true;

        return IsPersonIncomplete(person);
    }
}
