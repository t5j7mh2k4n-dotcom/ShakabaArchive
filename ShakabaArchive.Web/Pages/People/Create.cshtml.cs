using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ShakabaArchive.Data;
using ShakabaArchive.Models;
using ShakabaArchive.Services;

namespace ShakabaArchive.Web.Pages.People;

public class CreateModel(ArchiveDbContext db) : PageModel
{
    [BindProperty]
    public PersonInput Input { get; set; } = new();

    [BindProperty]
    public IFormFile? Document { get; set; }

    public List<SelectListItem> ParentOptions { get; private set; } = [];

    public async Task OnGetAsync(int? level = null)
    {
        if (level is >= 1 and <= 3)
            Input.HierarchyLevel = level.Value;
        await LoadParentsAsync();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await LoadParentsAsync();

        if (Input.HierarchyLevel is < 1 or > 3)
            ModelState.AddModelError(nameof(Input.HierarchyLevel), "اختر المستوى 1 أو 2 أو 3.");

        if (Input.HierarchyLevel == 1)
            Input.ParentPersonId = null;
        else if (Input.ParentPersonId is null)
            ModelState.AddModelError(nameof(Input.ParentPersonId), "اختر السجل الأب لهذا المستوى.");

        if (!ModelState.IsValid)
            return Page();

        var appUser = User.CurrentAppUser();
        if (appUser is null)
            return Challenge();

        string code;
        try
        {
            code = await PersonRegistryService.AllocateCodeAsync(
                db, Input.HierarchyLevel, Input.ParentPersonId);
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return Page();
        }

        var draft = Input.ToDraft();
        draft.RegistryCode = code;

        if (Document is { Length: > 0 })
        {
            await using var stream = Document.OpenReadStream();
            draft.DocumentImagePath = DatabaseService.SaveDocumentImage(stream, Document.FileName);
        }

        var (_, applied) = await ApprovalService.SubmitAsync(
            db,
            appUser,
            ChangeEntity.Person,
            ChangeAction.Create,
            null,
            draft,
            $"إضافة سجل أشخاص {code}: {draft.FullName}");

        if (applied)
        {
            TempData["Flash"] = $"تم حفظ السجل بالكود {code} في سجل الأشخاص.";
            return RedirectToPage("/People/Index");
        }

        TempData["Flash"] = "تم إرسال طلب الإضافة بانتظار موافقة أحد الثلاثة على صحة البيانات.";
        return RedirectToPage("/Approvals/Index");
    }

    private async Task LoadParentsAsync()
    {
        var level = Input.HierarchyLevel is >= 2 and <= 3 ? Input.HierarchyLevel - 1 : 1;
        ParentOptions = await db.People.AsNoTracking()
            .Where(p => p.HierarchyLevel == level)
            .OrderBy(p => p.RegistryCode)
            .Select(p => new SelectListItem(
                $"{p.RegistryCode} — {p.FullName}",
                p.Id.ToString(),
                Input.ParentPersonId == p.Id))
            .ToListAsync();
    }
}

public class PersonInput
{
    [Range(1, 3)]
    public int HierarchyLevel { get; set; } = 1;

    public int? ParentPersonId { get; set; }

    public string NationalId { get; set; } = "";

    [Required(ErrorMessage = "أدخل الاسم الأول"), Display(Name = "الاسم الأول")]
    public string FirstName { get; set; } = "";

    [Required(ErrorMessage = "أدخل اسم الأب")]
    public string FatherName { get; set; } = "";

    public string GrandfatherName { get; set; } = "";

    [Required(ErrorMessage = "أدخل اسم العائلة")]
    public string FamilyName { get; set; } = "";

    public string MotherName { get; set; } = "";
    public string Nationality { get; set; } = "";
    public string Gender { get; set; } = "ذكر";
    public DateTime? BirthDate { get; set; }
    public string BirthPlace { get; set; } = "الشكابة شاع الدين";
    public string Residence { get; set; } = "الشكابة شاع الدين";
    public string Tribe { get; set; } = "";
    public string Profession { get; set; } = "";
    public string Neighborhood { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Notes { get; set; } = "";
    public string DocumentImagePath { get; set; } = "";

    public string FullName =>
        Person.ComposeFullName(FirstName, FatherName, GrandfatherName, FamilyName);

    public static PersonInput From(Person p) => new()
    {
        HierarchyLevel = p.HierarchyLevel,
        ParentPersonId = p.ParentPersonId,
        NationalId = p.NationalId,
        FirstName = string.IsNullOrWhiteSpace(p.FirstName) ? p.FullName : p.FirstName,
        FatherName = p.FatherName,
        GrandfatherName = p.GrandfatherName,
        FamilyName = p.FamilyName,
        MotherName = p.MotherName,
        Nationality = p.Nationality,
        Gender = p.Gender,
        BirthDate = p.BirthDate,
        BirthPlace = p.BirthPlace,
        Residence = p.Residence,
        Tribe = p.Tribe,
        Profession = p.Profession,
        Neighborhood = p.Neighborhood,
        Phone = p.Phone,
        Notes = p.Notes,
        DocumentImagePath = p.DocumentImagePath
    };

    public PersonDraft ToDraft() => new()
    {
        HierarchyLevel = HierarchyLevel,
        ParentPersonId = ParentPersonId,
        NationalId = NationalId,
        FirstName = FirstName,
        FatherName = FatherName,
        GrandfatherName = GrandfatherName,
        FamilyName = FamilyName,
        FullName = FullName,
        MotherName = MotherName,
        Nationality = "",
        Gender = Gender,
        BirthDate = BirthDate.HasValue
            ? DateTime.SpecifyKind(BirthDate.Value.Date, DateTimeKind.Utc)
            : null,
        BirthPlace = BirthPlace,
        Residence = Residence,
        Tribe = Tribe,
        Profession = Profession,
        Neighborhood = Neighborhood,
        Phone = Phone,
        Notes = Notes,
        DocumentImagePath = DocumentImagePath
    };
}
