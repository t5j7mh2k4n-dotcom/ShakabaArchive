using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using ShakabaArchive.Models;
using ShakabaArchive.Services;

namespace ShakabaArchive.Web.Pages.Account;

public class InvitesModel : PageModel
{
    [BindProperty]
    public string? Note { get; set; }

    [BindProperty]
    public UserRole AssignRole { get; set; } = UserRole.Editor;

    public bool IsAdmin { get; private set; }
    public int ApproverCount { get; private set; }
    public string? LastCreatedCode { get; private set; }
    public string? Error { get; private set; }
    public List<InviteCode> Codes { get; private set; } = [];

    public void OnGet()
    {
        Load();
    }

    public IActionResult OnPost()
    {
        Load();
        if (!IsAdmin)
            return Page();

        try
        {
            var invite = LocalUserService.CreateInvite(Note, AssignRole);
            LastCreatedCode = invite.Code;
        }
        catch (Exception ex)
        {
            Error = ex.Message;
        }

        Load();
        return Page();
    }

    private void Load()
    {
        IsAdmin = User.IsInRole("Admin");
        ApproverCount = LocalUserService.CountApprovers();
        using var db = LocalUserService.CreateContext();
        Codes = db.InviteCodes.AsNoTracking()
            .OrderByDescending(c => c.CreatedAt)
            .Take(50)
            .ToList();
    }
}
