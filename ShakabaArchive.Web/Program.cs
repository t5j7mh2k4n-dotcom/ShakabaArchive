using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using ShakabaArchive.Data;
using ShakabaArchive.Services;

var builder = WebApplication.CreateBuilder(args);

var dataRoot = Path.Combine(builder.Environment.ContentRootPath, "App_Data");
var uploads = Path.Combine(builder.Environment.WebRootPath, "uploads");
Directory.CreateDirectory(dataRoot);
Directory.CreateDirectory(uploads);
DatabaseService.ConfigurePaths(dataRoot, uploads);

LocalUserService.ConfigurePath(
    Path.Combine(dataRoot, "users-local.db"));

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
    Console.WriteLine("Database: PostgreSQL/Neon (persistent) — users and archive will survive deploys.");
}
else if (builder.Environment.IsProduction()
         || !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("RENDER")))
{
    throw new InvalidOperationException(
        "DATABASE_URL is required in Production/Render. Without it, users are stored in temporary SQLite and deleted on every deploy.");
}
else
{
    Console.WriteLine("Database: local SQLite (dev only).");
}

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

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

app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

// صحة سريعة لـ Render قبل اكتمال تهيئة قاعدة البيانات
app.MapGet("/health", () => Results.Ok("ok"));

app.MapRazorPages();

// ابدأ الاستماع فوراً — ثم هيّئ قواعد البيانات في الخلفية (يمنع قتل العملية على Free)
_ = Task.Run(async () =>
{
    try
    {
        await Task.Delay(500);
        Console.WriteLine("Background DB init starting...");
        LocalUserService.Initialize();
        DatabaseService.Initialize();
        using var scope = app.Services.CreateScope();
        var archiveDb = scope.ServiceProvider.GetRequiredService<ArchiveDbContext>();
        await ApprovalService.EnsureSchemaAsync(archiveDb);
        Console.WriteLine("Background DB init completed.");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine("Background DB init failed: " + ex);
    }
});

app.Run();
