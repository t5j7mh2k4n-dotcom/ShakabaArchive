using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ShakabaArchive.Web.Pages.People.Events;

public class CreateModel : PageModel
{
    public IActionResult OnGet(int personId, int? type = null) =>
        RedirectToPage("/Occasions/Create", new { personId, type });
}
