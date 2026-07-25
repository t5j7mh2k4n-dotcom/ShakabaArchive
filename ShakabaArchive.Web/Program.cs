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
else
{
    // لا نوقف التشغيل هنا — إيقافه كان يفشل Deploy على Render.
    // بدون DATABASE_URL تُحفظ البيانات في SQLite مؤقت ويُمسح مع كل نشر.
    Console.Error.WriteLine(
        "WARNING: DATABASE_URL is not set. Using ephemeral SQLite — users/people will be wiped on every Render deploy. Add DATABASE_URL (Neon) in Render Environment.");
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
    options.Conventions.AuthorizePage("/Account/UsersReport");
    options.Conventions.AuthorizePage("/Account/ChangePassword");
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
