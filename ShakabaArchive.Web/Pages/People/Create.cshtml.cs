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
    public IFormFile? Photo { get; set; }

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

        var (item, applied) = await ApprovalService.SubmitAsync(
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

        TempData["Flash"] = "تم حفظ الطلب مؤقتاً بانتظار الاعتماد. يمكنك تعديله قبل موافقة أحد الثلاثة.";
        return RedirectToPage("/People/EditPending", new { id = item.Id });
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

public class PersonInput : IValidatableObject
{
    [Range(1, 3)]
    public int HierarchyLevel { get; set; } = 1;

    public int? ParentPersonId { get; set; }

    public string DocumentType { get; set; } = DocumentTypes.NationalId;
    public string DocumentNumber { get; set; } = "";

    [Required(ErrorMessage = "أدخل الاسم الأول"), Display(Name = "الاسم الأول")]
    public string FirstName { get; set; } = "";

    [Required(ErrorMessage = "أدخل اسم الأب")]
    public string FatherName { get; set; } = "";

    public string GrandfatherName { get; set; } = "";

    [Required(ErrorMessage = "أدخل اسم العائلة")]
    public string FamilyName { get; set; } = "";

    public string MotherName { get; set; } = "";
    public string Gender { get; set; } = "ذكر";
    public DateTime? BirthDate { get; set; }
    public string BirthPlace { get; set; } = "الشكابة شاع الدين";
    public string Residence { get; set; } = "الشكابة شاع الدين";
    public string Tribe { get; set; } = "";
    public string Profession { get; set; } = "";
    public string Neighborhood { get; set; } = "";
    public string Phone { get; set; } = "";
    public bool IsMigrant { get; set; }

    [Display(Name = "دولة المهجر")]
    public string MigrationCountry { get; set; } = "";

    [Display(Name = "مدينة المهجر")]
    public string MigrationCity { get; set; } = "";

    public string Notes { get; set; } = "";
    public string PhotoPath { get; set; } = "";
    public string DocumentImagePath { get; set; } = "";

    public string FullName =>
        Person.ComposeFullName(FirstName, FatherName, GrandfatherName, FamilyName);

    public static PersonInput From(Person p) => new()
    {
        HierarchyLevel = p.HierarchyLevel,
        ParentPersonId = p.ParentPersonId,
        DocumentType = string.IsNullOrWhiteSpace(p.DocumentType) ? DocumentTypes.NationalId : p.DocumentType,
        DocumentNumber = string.IsNullOrWhiteSpace(p.DocumentNumber) ? p.NationalId : p.DocumentNumber,
        FirstName = string.IsNullOrWhiteSpace(p.FirstName) ? p.FullName : p.FirstName,
        FatherName = p.FatherName,
        GrandfatherName = p.GrandfatherName,
        FamilyName = p.FamilyName,
        MotherName = p.MotherName,
        Gender = p.Gender,
        BirthDate = p.BirthDate,
        BirthPlace = p.BirthPlace,
        Residence = p.Residence,
        Tribe = p.Tribe,
        Profession = p.Profession,
        Neighborhood = p.Neighborhood,
        Phone = p.Phone,
        IsMigrant = p.IsMigrant,
        MigrationCountry = p.MigrationCountry,
        MigrationCity = p.MigrationCity,
        Notes = p.Notes,
        PhotoPath = p.PhotoPath,
        DocumentImagePath = p.DocumentImagePath
    };

    public static PersonInput FromDraft(PersonDraft d) => new()
    {
        HierarchyLevel = d.HierarchyLevel,
        ParentPersonId = d.ParentPersonId,
        DocumentType = string.IsNullOrWhiteSpace(d.DocumentType) ? DocumentTypes.NationalId : d.DocumentType,
        DocumentNumber = string.IsNullOrWhiteSpace(d.DocumentNumber) ? d.NationalId : d.DocumentNumber,
        FirstName = d.FirstName,
        FatherName = d.FatherName,
        GrandfatherName = d.GrandfatherName,
        FamilyName = d.FamilyName,
        MotherName = d.MotherName,
        Gender = d.Gender,
        BirthDate = d.BirthDate,
        BirthPlace = d.BirthPlace,
        Residence = d.Residence,
        Tribe = d.Tribe,
        Profession = d.Profession,
        Neighborhood = d.Neighborhood,
        Phone = d.Phone,
        IsMigrant = d.IsMigrant,
        MigrationCountry = d.MigrationCountry,
        MigrationCity = d.MigrationCity,
        Notes = d.Notes,
        PhotoPath = d.PhotoPath,
        DocumentImagePath = d.DocumentImagePath
    };

    public PersonDraft ToDraft() => new()
    {
        HierarchyLevel = HierarchyLevel,
        ParentPersonId = ParentPersonId,
        DocumentType = DocumentType,
        DocumentNumber = DocumentNumber,
        NationalId = DocumentNumber,
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
        IsMigrant = IsMigrant,
        MigrationCountry = IsMigrant ? MigrationCountry.Trim() : "",
        MigrationCity = IsMigrant ? MigrationCity.Trim() : "",
        Notes = Notes,
        PhotoPath = PhotoPath,
        DocumentImagePath = DocumentImagePath
    };

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!IsMigrant)
            yield break;

        if (string.IsNullOrWhiteSpace(MigrationCountry))
            yield return new ValidationResult("أدخل دولة المهجر للمغترب.", [nameof(MigrationCountry)]);

        if (string.IsNullOrWhiteSpace(MigrationCity))
            yield return new ValidationResult("أدخل مدينة المهجر للمغترب.", [nameof(MigrationCity)]);
    }
}
