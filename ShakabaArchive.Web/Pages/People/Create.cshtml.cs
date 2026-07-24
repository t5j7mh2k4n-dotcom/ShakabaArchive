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

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
            return Page();

        var person = Input.ToPerson();
        if (Document is { Length: > 0 })
        {
            await using var stream = Document.OpenReadStream();
            person.DocumentImagePath = DatabaseService.SaveDocumentImage(stream, Document.FileName);
        }

        db.People.Add(person);
        await db.SaveChangesAsync();
        return RedirectToPage("Details", new { id = person.Id });
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
    public string Nationality { get; set; } = "سوداني";
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
