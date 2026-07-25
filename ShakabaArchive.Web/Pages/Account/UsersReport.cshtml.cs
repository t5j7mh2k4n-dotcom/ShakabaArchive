using System.Globalization;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ShakabaArchive.Models;
using ShakabaArchive.Services;

namespace ShakabaArchive.Web.Pages.Account;

public class UsersReportModel : PageModel
{
    public List<AppUser> Users { get; private set; } = [];
    public int TotalCount { get; private set; }
    public int AdminCount { get; private set; }
    public int ApproverCount { get; private set; }
    public int EditorCount { get; private set; }
    public int MaxApprovers { get; private set; } = ApprovalService.MaxApprovers;
    public string GeneratedAt { get; private set; } = "";
    public string StorageLabel { get; private set; } = "";

    public IActionResult OnGet()
    {
        if (!User.IsInRole("Admin"))
            return Forbid();

        Load();
        return Page();
    }

    public IActionResult OnGetCsv()
    {
        if (!User.IsInRole("Admin"))
            return Forbid();

        Load();
        var sb = new StringBuilder();
        sb.Append('\uFEFF'); // Excel UTF-8
        sb.AppendLine("م,الاسم,البريد,الهاتف,الدور,تاريخ الإنشاء,رمز الدعوة");

        var i = 1;
        foreach (var u in Users)
        {
            var role = RoleClaims.ToArabic(u.IsAdmin || u.Role == UserRole.Admin ? UserRole.Admin : u.Role);
            sb.Append(i++).Append(',')
                .Append(Csv(u.DisplayName)).Append(',')
                .Append(Csv(u.Email)).Append(',')
                .Append(Csv(u.Phone)).Append(',')
                .Append(Csv(role)).Append(',')
                .Append(Csv(u.CreatedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture))).Append(',')
                .Append(Csv(u.InviteCodeUsed))
                .AppendLine();
        }

        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        var fileName = $"users-report-{DateTime.Now:yyyyMMdd-HHmm}.csv";
        return File(bytes, "text/csv; charset=utf-8", fileName);
    }

    private void Load()
    {
        LocalUserService.EnsureReady();
        Users = LocalUserService.ListUsers();
        TotalCount = Users.Count;
        AdminCount = Users.Count(u => u.IsAdmin || u.Role == UserRole.Admin);
        ApproverCount = Users.Count(u => !(u.IsAdmin || u.Role == UserRole.Admin) && u.Role == UserRole.Approver);
        EditorCount = Math.Max(0, TotalCount - AdminCount - ApproverCount);
        GeneratedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        StorageLabel = LocalUserService.UsesCloud
            ? "PostgreSQL / Neon"
            : "SQLite محلي";
    }

    private static string Csv(string? value)
    {
        value ??= "";
        if (value.Contains('"') || value.Contains(',') || value.Contains('\n'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }
}
