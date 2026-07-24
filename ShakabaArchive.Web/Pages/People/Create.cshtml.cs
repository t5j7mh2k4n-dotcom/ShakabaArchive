using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
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

    public string? InfoMessage { get; private set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
            return Page();

        var appUser = User.CurrentAppUser();
        if (appUser is null)
            return Challenge();

        var draft = new PersonDraft
        {
            NationalId = Input.NationalId,
            FullName = Input.FullName,
            FatherName = Input.FatherName,
            MotherName = Input.MotherName,
            Nationality = "",
            Gender = Input.Gender,
            BirthDate = Input.BirthDate.HasValue
                ? DateTime.SpecifyKind(Input.BirthDate.Value.Date, DateTimeKind.Utc)
                : null,
            BirthPlace = Input.BirthPlace,
            Residence = Input.Residence,
            Tribe = "",
            Neighborhood = Input.Neighborhood,
            Phone = Input.Phone,
            Notes = Input.Notes,
            DocumentImagePath = Input.DocumentImagePath
        };

        if (Document is { Length: > 0 })
        {
            await using var stream = Document.OpenReadStream();
            draft.DocumentImagePath = DatabaseService.SaveDocumentImage(stream, Document.FileName);
        }

        await ApprovalService.SubmitAsync(
            db,
            appUser,
            ChangeEntity.Person,
            ChangeAction.Create,
            null,
            draft,
            $"إضافة شخص: {draft.FullName} ({draft.NationalId})");

        TempData["Flash"] = "تم إرسال طلب الإضافة بانتظار موافقة أحد المخولين الثلاثة.";
        return RedirectToPage("/Approvals/Index");
    }
}

public class PersonInput
{
    [Required, Display(Name = "الرقم الوطني")]
    public string NationalId { get; set; } = "";

    [Required, Display(Name = "الاسم الكامل")]
    public string FullName { get; set; } = "";

    public string FatherName { get; set; } = "";
    public string MotherName { get; set; } = "";
    public string Nationality { get; set; } = "";
    public string Gender { get; set; } = "ذكر";
    public DateTime? BirthDate { get; set; }
    public string BirthPlace { get; set; } = "الشكابة شاع الدين";
    public string Residence { get; set; } = "الشكابة شاع الدين";
    public string Tribe { get; set; } = "";
    public string Neighborhood { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Notes { get; set; } = "";
    public string DocumentImagePath { get; set; } = "";

    public static PersonInput From(Person p) => new()
    {
        NationalId = p.NationalId,
        FullName = p.FullName,
        FatherName = p.FatherName,
        MotherName = p.MotherName,
        Nationality = p.Nationality,
        Gender = p.Gender,
        BirthDate = p.BirthDate,
        BirthPlace = p.BirthPlace,
        Residence = p.Residence,
        Tribe = p.Tribe,
        Neighborhood = p.Neighborhood,
        Phone = p.Phone,
        Notes = p.Notes,
        DocumentImagePath = p.DocumentImagePath
    };

    public Person ToPerson() => new()
    {
        NationalId = NationalId.Trim(),
        FullName = FullName.Trim(),
        FatherName = FatherName.Trim(),
        MotherName = MotherName.Trim(),
        Nationality = Nationality.Trim(),
        Gender = Gender,
        BirthDate = BirthDate,
        BirthPlace = BirthPlace.Trim(),
        Residence = Residence.Trim(),
        Tribe = Tribe.Trim(),
        Neighborhood = Neighborhood.Trim(),
        Phone = Phone.Trim(),
        Notes = Notes.Trim(),
        DocumentImagePath = DocumentImagePath,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    public void ApplyTo(Person person)
    {
        person.NationalId = NationalId.Trim();
        person.FullName = FullName.Trim();
        person.FatherName = FatherName.Trim();
        person.MotherName = MotherName.Trim();
        person.Nationality = Nationality.Trim();
        person.Gender = Gender;
        person.BirthDate = BirthDate;
        person.BirthPlace = BirthPlace.Trim();
        person.Residence = Residence.Trim();
        person.Tribe = Tribe.Trim();
        person.Neighborhood = Neighborhood.Trim();
        person.Phone = Phone.Trim();
        person.Notes = Notes.Trim();
        person.UpdatedAt = DateTime.UtcNow;
    }
}
