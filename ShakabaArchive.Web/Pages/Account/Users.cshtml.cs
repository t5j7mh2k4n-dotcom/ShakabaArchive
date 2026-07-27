using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ShakabaArchive.Data;
using ShakabaArchive.Models;
using ShakabaArchive.Services;

namespace ShakabaArchive.Web.Pages.Account;

public class UsersModel(ArchiveDbContext db) : PageModel
{
    public List<UserRow> Users { get; private set; } = [];
    public int ApproverCount { get; private set; }
    public int MaxApprovers { get; private set; } = ApprovalService.MaxApprovers;
    public int MaxUsers { get; private set; } = ApprovalService.MaxUsers;
    public string StorageLabel { get; private set; } = "";
    public bool CanPersistUsers { get; private set; }
    public string? Message { get; private set; }
    public string? Error { get; private set; }

    [BindProperty(SupportsGet = true)]
    public int? EditId { get; set; }

    public UserRow? EditingUser { get; private set; }

    [BindProperty]
    public UserFormInput Form { get; set; } = new();

    public async Task OnGetAsync()
    {
        if (!User.IsInRole("Admin"))
            return;
        await LoadAsync();
        Message = TempData["Flash"] as string;
        Error = TempData["FlashError"] as string;

        if (EditId is int id)
        {
            EditingUser = Users.FirstOrDefault(u => u.Id == id);
            if (EditingUser is not null)
            {
                Form = new UserFormInput
                {
                    DisplayName = EditingUser.DisplayName,
                    Email = EditingUser.Email,
                    Phone = EditingUser.Phone,
                    Role = EditingUser.IsAdmin || EditingUser.Role == UserRole.Admin
                        ? UserRole.Admin
                        : EditingUser.Role,
                    SecurityCode = EditingUser.SecurityCode
                };
            }
        }
    }

    public async Task<IActionResult> OnPostCreateAsync()
    {
        if (!User.IsInRole("Admin"))
            return Forbid();

        try
        {
            LocalUserService.EnsureReady();
            var (ok, error, created) = LocalUserService.CreateUser(
                Form.Email,
                Form.Phone,
                Form.DisplayName,
                Form.Password ?? "",
                Form.Role);

            if (!ok || created is null)
            {
                TempData["FlashError"] = error;
            }
            else
            {
                var family = await FamilyRegistryService.GetOrCreateAsync(db, created);
                family.Name = $"أسرة {created.DisplayName.Trim()}";
                if (!string.IsNullOrWhiteSpace(Form.SecurityCode))
                {
                    var (codeOk, codeErr) = await FamilyRegistryService.SetSecurityCodeAsync(
                        db, family.Id, Form.SecurityCode);
                    if (!codeOk)
                    {
                        TempData["FlashError"] = $"أُضيف المستخدم لكن الرمز: {codeErr}";
                        return RedirectToPage();
                    }
                }
                else
                {
                    family.UpdatedAt = DateTime.UtcNow;
                    await db.SaveChangesAsync();
                }

                TempData["Flash"] =
                    $"تمت إضافة «{created.DisplayName}» — أسرة: {family.Name} — رمز الأمان: {family.SecurityCode}";
            }
        }
        catch (Exception ex)
        {
            TempData["FlashError"] = "تعذر إضافة المستخدم: " + ex.Message;
        }

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostUpdateAsync(int userId)
    {
        if (!User.IsInRole("Admin"))
            return Forbid();

        var (ok, error) = LocalUserService.UpdateUser(
            userId,
            Form.Email,
            Form.Phone,
            Form.DisplayName,
            string.IsNullOrWhiteSpace(Form.Password) ? null : Form.Password,
            Form.Role);

        if (!ok)
        {
            TempData["FlashError"] = error;
            return RedirectToPage(new { editId = userId });
        }

        var user = LocalUserService.ListUsers().FirstOrDefault(u => u.Id == userId);
        if (user is not null)
        {
            var family = await FamilyRegistryService.GetOrCreateAsync(db, user);
            family.Name = $"أسرة {user.DisplayName.Trim()}";
            if (!string.IsNullOrWhiteSpace(Form.SecurityCode))
            {
                var (codeOk, codeErr) = await FamilyRegistryService.SetSecurityCodeAsync(
                    db, family.Id, Form.SecurityCode);
                if (!codeOk)
                {
                    TempData["FlashError"] = codeErr;
                    return RedirectToPage(new { editId = userId });
                }
            }
            else
            {
                await db.SaveChangesAsync();
            }
        }

        TempData["Flash"] = "تم تعديل المستخدم ورمز أمان الأسرة بنجاح.";
        return RedirectToPage();
    }

    public IActionResult OnPostDelete(int userId)
    {
        if (!User.IsInRole("Admin"))
            return Forbid();

        var currentId = int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
            ? id
            : (int?)null;

        var (ok, error) = LocalUserService.DeleteUser(userId, currentId);
        if (!ok)
            TempData["FlashError"] = error;
        else
            TempData["Flash"] = "تم حذف المستخدم.";

        return RedirectToPage();
    }

    public IActionResult OnPostSetPermissions(int userId, bool canApprove, bool isMainAdmin)
    {
        if (!User.IsInRole("Admin"))
            return Forbid();

        var role = isMainAdmin
            ? UserRole.Admin
            : canApprove
                ? UserRole.Approver
                : UserRole.Editor;

        var (ok, error) = LocalUserService.SetUserRole(userId, role);
        if (!ok)
            TempData["FlashError"] = error;
        else
            TempData["Flash"] = "تم تحديث صلاحيات المستخدم.";

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostSetSecurityCodeAsync(int userId, string securityCode)
    {
        if (!User.IsInRole("Admin"))
            return Forbid();

        var user = LocalUserService.ListUsers().FirstOrDefault(u => u.Id == userId);
        if (user is null)
        {
            TempData["FlashError"] = "المستخدم غير موجود.";
            return RedirectToPage();
        }

        var family = await FamilyRegistryService.GetOrCreateAsync(db, user);
        if (string.IsNullOrWhiteSpace(family.Name) || family.Name == "أسرتي")
            family.Name = $"أسرة {user.DisplayName.Trim()}";

        var (ok, error) = await FamilyRegistryService.SetSecurityCodeAsync(db, family.Id, securityCode);
        if (!ok)
            TempData["FlashError"] = error;
        else
            TempData["Flash"] = $"تم تحديث رمز أمان أسرة «{user.DisplayName}» إلى {FamilyRegistryService.NormalizeSecurityCode(securityCode)}.";

        return RedirectToPage();
    }

    private async Task LoadAsync()
    {
        LocalUserService.EnsureReady();
        await FamilyRegistryService.EnsureSchemaAsync(db);

        var appUsers = LocalUserService.ListUsers();
        ApproverCount = LocalUserService.CountApprovers();
        CanPersistUsers = LocalUserService.CanPersistUsers;
        StorageLabel = LocalUserService.UsesCloud
            ? "PostgreSQL / Neon (ثابت — لا يُمسح مع النشر)"
            : "SQLite مؤقت — الحسابات تُمسح مع كل نشر (أضف DATABASE_URL)";

        // أنشئ أسرة + رمز أمان لكل مستخدم قديم لا يملكهما بعد
        foreach (var u in appUsers)
        {
            try
            {
                var family = await FamilyRegistryService.GetOrCreateAsync(db, u);
                var desiredName = string.IsNullOrWhiteSpace(u.DisplayName)
                    ? "أسرتي"
                    : $"أسرة {u.DisplayName.Trim()}";
                if (string.IsNullOrWhiteSpace(family.Name)
                    || family.Name == "أسرتي"
                    || !family.Name.Contains(u.DisplayName.Trim(), StringComparison.Ordinal))
                {
                    family.Name = desiredName;
                    family.UpdatedAt = DateTime.UtcNow;
                    await db.SaveChangesAsync();
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Ensure family for user {u.Id}: {ex.Message}");
            }
        }

        var families = await db.Families.AsNoTracking().ToListAsync();
        var byOwner = families
            .Where(f => f.OwnerUserId > 0)
            .GroupBy(f => f.OwnerUserId)
            .ToDictionary(g => g.Key, g => g.First());

        Users = appUsers.Select(u =>
        {
            byOwner.TryGetValue(u.Id, out var family);
            return new UserRow
            {
                Id = u.Id,
                DisplayName = u.DisplayName,
                Email = u.Email,
                Phone = u.Phone,
                Role = u.Role,
                IsAdmin = u.IsAdmin,
                FamilyName = family?.Name ?? "",
                SecurityCode = family?.SecurityCode ?? ""
            };
        }).ToList();
    }

    public class UserRow
    {
        public int Id { get; set; }
        public string DisplayName { get; set; } = "";
        public string Email { get; set; } = "";
        public string Phone { get; set; } = "";
        public UserRole Role { get; set; }
        public bool IsAdmin { get; set; }
        public string FamilyName { get; set; } = "";
        public string SecurityCode { get; set; } = "";
    }

    public class UserFormInput
    {
        [Required(ErrorMessage = "أدخل الاسم")]
        public string DisplayName { get; set; } = "";

        [Required(ErrorMessage = "أدخل البريد")]
        public string Email { get; set; } = "";

        [Required(ErrorMessage = "أدخل الهاتف")]
        public string Phone { get; set; } = "";

        public string? Password { get; set; }

        public UserRole Role { get; set; } = UserRole.Editor;

        public string? SecurityCode { get; set; }
    }
}
