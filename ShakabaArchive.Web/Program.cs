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
         ?? Environment.GetEnvironmentVariable("POSTGRES_URL")
         ?? Environment.GetEnvironmentVariable("NEON_DATABASE_URL")
         ?? Environment.GetEnvironmentVariable("ConnectionStrings__PostgreSql");

// نفس الاتصال للأرشيف والمستخدمين — لا ملف SQLite منفصل على Render
LocalUserService.ConfigureCloud(pg);

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
    Console.Error.WriteLine(
        "WARNING: DATABASE_URL is not set. Using ephemeral SQLite — users will be wiped on every Render deploy. Add DATABASE_URL (Neon) in Render Environment.");
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

// تشخيص اتصال Neon بدون كشف كلمة المرور
app.MapGet("/health/db", () =>
{
    var (ok, mode, detail) = DatabaseService.ProbeConnection();
    var users = LocalUserService.ProbeAndRepairUsers();
    return Results.Json(new
    {
        ok = ok && users.Ok,
        mode,
        detail,
        users = users.Detail,
        usersCloud = LocalUserService.UsesCloud
    });
});

app.MapRazorPages();

// ابدأ الاستماع فوراً — ثم هيّئ قواعد البيانات لاحقاً (يمنع exit 134/139 على Free)
_ = Task.Run(async () =>
{
    try
    {
        // امنح Render وقتاً لاعتبار الحاوية سليمة عبر /health قبل ضغط الذاكرة
        await Task.Delay(3000);
        Console.WriteLine("Background DB init starting...");
        DatabaseService.Initialize();
        await Task.Delay(400);
        LocalUserService.Initialize();
        await Task.Delay(400);
        using var scope = app.Services.CreateScope();
        var archiveDb = scope.ServiceProvider.GetRequiredService<ArchiveDbContext>();
        await ApprovalService.EnsureSchemaAsync(archiveDb);
        Console.WriteLine("Background DB init completed. UsersCloud=" + LocalUserService.UsesCloud);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine("Background DB init failed: " + ex);
    }
});

app.Run();
