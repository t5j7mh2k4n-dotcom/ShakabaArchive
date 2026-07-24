using Microsoft.AspNetCore.Mvc.Rendering;
using ShakabaArchive.Models;

namespace ShakabaArchive.Web.Pages.People.Events;

public class EventFormModel
{
    public EventType Type { get; set; } = EventType.Birth;
    public DateTime? EventDate { get; set; } = DateTime.Today;
    public string Place { get; set; } = "الشكابة شاع الدين";
    public string Title { get; set; } = "";
    public string Details { get; set; } = "";

    public string RelatedPersonName { get; set; } = "";
    public string RelatedFatherName { get; set; } = "";
    public string RelatedPhone { get; set; } = "";

    public string ChildFullName { get; set; } = "";
    public string ChildGender { get; set; } = "ذكر";
    public string MotherName { get; set; } = "";
    public string ChildNationalId { get; set; } = "";
    public string ChildNationality { get; set; } = "سوداني";
    public string ChildTribe { get; set; } = "";
    public string ChildNeighborhood { get; set; } = "";
    public bool CreateChildPersonRecord { get; set; } = true;

    public string Institution { get; set; } = "";
    public string Specialty { get; set; } = "";
    public string Degree { get; set; } = "";

    public string SourceNote { get; set; } = "";

    public List<SelectListItem> TypeOptions => EventTypeLabels.All
        .Select(t => new SelectListItem(
            $"{t.Label} ({EventTypeLabels.MoodArabic(t.Mood)})",
            ((int)t.Value).ToString(),
            t.Value == Type))
        .ToList();
}
