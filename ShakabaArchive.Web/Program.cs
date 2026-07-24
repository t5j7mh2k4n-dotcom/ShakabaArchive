using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using ShakabaArchive.Data;
using ShakabaArchive.Services;

var builder = WebApplication.CreateBuilder(args);

var dataRoot = Path.Combine(builder.Environment.ContentRootPath, "App_Data");
var uploads = Path.Combine(builder.Environment.WebRootPath, "uploads");
DatabaseService.ConfigurePaths(dataRoot, uploads);

// المستخدمون دائماً على الجهاز (SQLite محلي)
LocalUserService.ConfigurePath(
    Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ShakabaArchive",
        "users-local.db"));

var pg = builder.Configuration.GetConnectionString("PostgreSql")
         ?? Environment.GetEnvironmentVariable("DATABASE_URL")
         ?? Environment.GetEnvironmentVariable("POSTGRES_CONNECTION")
         ?? Environment.GetEnvironmentVariable("ConnectionStrings__PostgreSql");
if (!string.IsNullOrWhiteSpace(pg))
{
    DatabaseService.SaveSettings(new AppSettings
    {
        Provider = "PostgreSql",
        PostgreSqlConnection = pg,
        SqliteFileName = "shakaba-archive.db"
    });
}

builder.Services.AddDbContext<ArchiveDbContext>(options => DatabaseService.Configure(options));
builder.Services.AddRazorPages(options =>
{
    options.Conventions.AuthorizeFolder("/People");
    options.Conventions.AllowAnonymousToPage("/People/Index");
    options.Conventions.AllowAnonymousToPage("/People/Details");
    options.Conventions.AuthorizeFolder("/Occasions");
    options.Conventions.AllowAnonymousToPage("/Occasions/Index");
    options.Conventions.AuthorizeFolder("/Approvals");
    options.Conventions.AuthorizePage("/Account/Invites");
    options.Conventions.AuthorizePage("/Account/Users");
});
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.LogoutPath = "/Account/Logout";
        options.AccessDeniedPath = "/Account/Login";
        options.ExpireTimeSpan = TimeSpan.FromDays(14);
    });
builder.Services.AddAuthorization();

var app = builder.Build();
try
{
    LocalUserService.Initialize();
}
catch (Exception ex)
{
    Console.Error.WriteLine("LocalUserService.Initialize failed: " + ex);
}

try
{
    DatabaseService.Initialize();
}
catch (Exception ex)
{
    Console.Error.WriteLine("DatabaseService.Initialize failed: " + ex);
}

try
{
    using var scope = app.Services.CreateScope();
    var archiveDb = scope.ServiceProvider.GetRequiredService<ArchiveDbContext>();
    await ApprovalService.EnsureSchemaAsync(archiveDb);
}
catch (Exception ex)
{
    Console.Error.WriteLine("ApprovalService.EnsureSchemaAsync failed: " + ex);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.MapRazorPages();
app.Run();
