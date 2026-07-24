using Microsoft.AspNetCore.Mvc.Rendering;
using ShakabaArchive.Models;

namespace ShakabaArchive.Web.Pages.People.Events;

public class EventFormModel
{
    public EventType Type { get; set; } = EventType.Birth;
    public DateTime? EventDate { get; set; }
    public string Place { get; set; } = "الشكابة شاع الدين";
    public string Title { get; set; } = "";
    public string RelatedPersonName { get; set; } = "";
    public string Details { get; set; } = "";
    public string SourceNote { get; set; } = "";

    public List<SelectListItem> TypeOptions => EventTypeLabels.All
        .Select(t => new SelectListItem(t.Label, ((int)t.Value).ToString(), t.Value == Type))
        .ToList();
}
