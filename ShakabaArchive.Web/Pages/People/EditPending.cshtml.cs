using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using ShakabaArchive.Data;
using ShakabaArchive.Models;
using ShakabaArchive.Services;
using ShakabaArchive.Web;

namespace ShakabaArchive.Web.Pages.People;

public class EditPendingModel(ArchiveDbContext db) : PageModel
{
    [BindProperty]
    public int Id { get; set; }

    public string RegistryCode { get; private set; } = "";

    [BindProperty]
    public PersonInput Input { get; set; } = new();

    [BindProperty]
    public IFormFile? Photo { get; set; }

    [BindProperty]
    public IFormFile? Document { get; set; }

    public string? Message { get; private set; }
    public string? Error { get; private set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var appUser = User.CurrentAppUser();
        if (appUser is null) return Challenge();

        var (ok, error, draft) = await ApprovalService.GetPendingPersonDraftAsync(db, appUser, id);
        if (!ok || draft is null)
        {
            TempData["FlashError"] = error;
            return RedirectToPage("/Approvals/Index");
        }

        Id = id;
        RegistryCode = draft.RegistryCode;
        Input = PersonInput.FromDraft(draft);
        Message = TempData["Flash"] as string;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var appUser = User.CurrentAppUser();
        if (appUser is null) return Challenge();

        if (!ModelState.IsValid)
        {
            var (_, _, existing) = await ApprovalService.GetPendingPersonDraftAsync(db, appUser, Id);
            RegistryCode = existing?.RegistryCode ?? "";
            return Page();
        }

        var draft = Input.ToDraft();

        if (Photo is { Length: > 0 })
        {
            await using var stream = Photo.OpenReadStream();
            draft.PhotoPath = DatabaseService.SaveDocumentImage(stream, Photo.FileName);
        }

        if (Document is { Length: > 0 })
        {
            await using var stream = Document.OpenReadStream();
            draft.DocumentImagePath = DatabaseService.SaveDocumentImage(stream, Document.FileName);
        }

        var (ok, error) = await ApprovalService.UpdatePendingPersonAsync(db, appUser, Id, draft);
        if (!ok)
        {
            Error = error;
            RegistryCode = draft.RegistryCode;
            return Page();
        }

        TempData["Flash"] = "تم تحديث الطلب. ما زال بانتظار موافقة أحد الثلاثة.";
        return RedirectToPage("/Approvals/Index");
    }
}
