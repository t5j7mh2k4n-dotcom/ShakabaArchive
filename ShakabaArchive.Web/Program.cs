using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
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

// مفاتيح الجلسة على قرص الحاوية — تجنب كسر صفحة الدخول إذا Neon نائم
// (حفظها في Neon كان يرمي خطأ قبل إنشاء الجدول)
var dpKeysPath = Path.Combine(dataRoot, "dp-keys");
Directory.CreateDirectory(dpKeysPath);
builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(dpKeysPath))
    .SetApplicationName("ShakabaArchive");

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
        options.SlidingExpiration = true;
        options.Cookie.Name = "ShakabaAuth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.Cookie.SameSite = SameSiteMode.Lax;
    });
builder.Services.AddAuthorization();

var app = builder.Build();

// جهّز جداول Neon الأساسية مبكراً دون إيقاف التشغيل إن فشلت
try
{
    AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
    using var warmDb = DatabaseService.CreateContextForSchemaChanges();
    if (warmDb.Database.CanConnect())
    {
        DatabaseService.EnsureDataProtectionKeysTable(warmDb);
        ApprovalService.EnsureSchemaAsync(warmDb).GetAwaiter().GetResult();
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine("Early schema warm-up skipped: " + ex.Message);
}

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

// تشخيص خفيف — الإصلاح الثقيل للجداول عبر /health/db?repair=1 فقط
app.MapGet("/health/db", (HttpRequest req) =>
{
    var (ok, mode, detail) = DatabaseService.ProbeConnection();
    object users = "skipped";
    if (req.Query.ContainsKey("repair"))
    {
        var repair = LocalUserService.ProbeAndRepairUsers();
        users = repair.Detail;
        ok = ok && repair.Ok;
    }

    return Results.Json(new
    {
        ok,
        mode,
        detail,
        users,
        usersCloud = LocalUserService.UsesCloud
    });
});

app.MapRazorPages();

// استماع فوري — تهيئة خفيفة جداً بعد نجاح /health (تفادي exit 139 على Free)
_ = Task.Run(async () =>
{
    try
    {
        await Task.Delay(8000);
        Console.WriteLine("Background DB init starting...");
        DatabaseService.Initialize();
        try
        {
            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ArchiveDbContext>();
            await ApprovalService.EnsureSchemaAsync(db);
            DatabaseService.EnsureDataProtectionKeysTable(db);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Schema warm-up: " + ex.Message);
        }
        Console.WriteLine("Background DB init completed. UsersCloud=" + LocalUserService.UsesCloud);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine("Background DB init failed: " + ex);
    }
});

app.Run();
