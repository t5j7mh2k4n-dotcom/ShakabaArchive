using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using ShakabaArchive.Data;
using ShakabaArchive.Models;

namespace ShakabaArchive.Services;

/// <summary>
/// مستخدمون محلياً (SQLite) عند العمل على الجهاز،
/// أو على Neon/PostgreSQL عند النشر أونلاين.
/// </summary>
public static class LocalUserService
{
    private static readonly object Gate = new();
    private static string? _dbPath;

    public static string DatabasePath
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(_dbPath))
                return _dbPath;

            var folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ShakabaArchive");
            Directory.CreateDirectory(folder);
            return Path.Combine(folder, "users-local.db");
        }
    }

    public static bool UsesCloud => IsPostgresConfigured();

    public static void ConfigurePath(string sqliteFilePath)
    {
        lock (Gate)
        {
            var dir = Path.GetDirectoryName(sqliteFilePath);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);
            _dbPath = sqliteFilePath;
        }
    }

    public static UsersDbContext CreateContext()
    {
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
        var options = new DbContextOptionsBuilder<UsersDbContext>();
        Configure(options);
        return new UsersDbContext(options.Options);
    }

    public static void Configure(DbContextOptionsBuilder options)
    {
        if (IsPostgresConfigured())
        {
            var conn = GetPostgresConnection()!;
            options.UseNpgsql(DatabaseService.NormalizeConnectionString(conn));
            return;
        }

        options.UseSqlite($"Data Source={DatabasePath}");
    }

    public static void Initialize()
    {
        using var db = CreateContext();
        EnsureUserSchema(db);
        UpgradeUserColumns(db);
        SeedAdminIfEmpty(db);
    }

    private static void UpgradeUserColumns(UsersDbContext db)
    {
        // SQLite
        try { db.Database.ExecuteSqlRaw("ALTER TABLE Users ADD COLUMN Role INTEGER NOT NULL DEFAULT 0"); } catch { }
        try { db.Database.ExecuteSqlRaw("ALTER TABLE InviteCodes ADD COLUMN AssignRole INTEGER NOT NULL DEFAULT 0"); } catch { }
        // PostgreSQL / Neon
        try { db.Database.ExecuteSqlRaw("""ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "Role" integer NOT NULL DEFAULT 0"""); } catch { }
        try { db.Database.ExecuteSqlRaw("""ALTER TABLE "InviteCodes" ADD COLUMN IF NOT EXISTS "AssignRole" integer NOT NULL DEFAULT 0"""); } catch { }
    }

    private static void EnsureUserSchema(UsersDbContext db)
    {
        var creator = db.GetService<IRelationalDatabaseCreator>();
        if (!creator.Exists())
            creator.Create();

        if (!CanQueryNewUsers(db))
        {
            // جدول Users القديم على Neon قد يكون بصيغة قديمة — نستبدله بجداول المستخدمين فقط
            if (IsPostgresConfigured())
            {
                try
                {
                    db.Database.ExecuteSqlRaw("""DROP TABLE IF EXISTS "InviteCodes" CASCADE;""");
                    db.Database.ExecuteSqlRaw("""DROP TABLE IF EXISTS "Users" CASCADE;""");
                }
                catch { /* ignore */ }
            }

            if (!creator.HasTables() || !CanQueryNewUsers(db))
            {
                try
                {
                    creator.CreateTables();
                }
                catch
                {
                    if (IsPostgresConfigured())
                    {
                        db.Database.ExecuteSqlRaw("""DROP TABLE IF EXISTS "InviteCodes" CASCADE;""");
                        db.Database.ExecuteSqlRaw("""DROP TABLE IF EXISTS "Users" CASCADE;""");
                        creator.CreateTables();
                    }
                    else throw;
                }
            }
        }
    }

    private static bool CanQueryNewUsers(UsersDbContext db)
    {
        try
        {
            _ = db.Users.Select(u => u.Email).Take(1).ToList();
            _ = db.InviteCodes.Take(1).ToList();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void SeedAdminIfEmpty(UsersDbContext db)
    {
        try
        {
            SeedAdminIfEmptyCore(db);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("SeedAdminIfEmpty failed: " + ex.Message);
        }
    }

    private static void SeedAdminIfEmptyCore(UsersDbContext db)
    {
        const string adminEmail = "abohosam@shukaba.local";
        const string adminPassword = "Om123456@";

        // ترقية الحساب الافتراضي القديم إن وُجد
        var legacy = db.Users.FirstOrDefault(u => u.Email == "admin@shakaba.local");
        if (legacy is not null)
        {
            legacy.Email = adminEmail;
            legacy.Phone = legacy.Phone is "0000000000" or "" ? "0000000000" : legacy.Phone;
            legacy.DisplayName = "أبو حسام";
            legacy.PasswordHash = PasswordHasher.Hash(adminPassword);
            legacy.IsAdmin = true;
            legacy.Role = UserRole.Admin;
            db.SaveChanges();
            return;
        }

        var existingAdmin = db.Users.FirstOrDefault(u => u.Email == adminEmail);
        if (existingAdmin is not null)
        {
            existingAdmin.PasswordHash = PasswordHasher.Hash(adminPassword);
            existingAdmin.IsAdmin = true;
            existingAdmin.Role = UserRole.Admin;
            existingAdmin.DisplayName = string.IsNullOrWhiteSpace(existingAdmin.DisplayName)
                ? "أبو حسام"
                : existingAdmin.DisplayName;
            db.SaveChanges();
            return;
        }

        if (db.Users.Any())
            return;

        db.Users.Add(new AppUser
        {
            Email = adminEmail,
            Phone = "0000000000",
            DisplayName = "أبو حسام",
            PasswordHash = PasswordHasher.Hash(adminPassword),
            IsAdmin = true,
            Role = UserRole.Admin,
            InviteCodeUsed = "ADMIN",
            CreatedAt = DateTime.UtcNow
        });

        db.InviteCodes.Add(new InviteCode
        {
            Code = GenerateCode(),
            Note = "رقم تجريبي — مدخل مؤقت",
            AssignRole = UserRole.Editor,
            CreatedAt = DateTime.UtcNow
        });

        db.SaveChanges();
    }

    public static string GenerateCode()
    {
        var n = Random.Shared.Next(10000000, 99999999);
        return n.ToString();
    }

    public static int CountApprovers()
    {
        using var db = CreateContext();
        return db.Users.Count(u => u.Role == UserRole.Approver || u.Role == UserRole.Admin || u.IsAdmin);
    }

    public static InviteCode CreateInvite(string? note = null, UserRole assignRole = UserRole.Editor)
    {
        if (assignRole == UserRole.Approver || assignRole == UserRole.Admin)
        {
            var count = CountApprovers();
            // عند إنشاء دعوة لمخوّل: نحسب الحاليين فقط؛ الاعتماد الفعلي عند التسجيل/التعيين
            if (assignRole == UserRole.Approver && count >= ApprovalService.MaxApprovers)
                throw new InvalidOperationException($"الحد الأقصى للمخولين بالحفظ هو {ApprovalService.MaxApprovers}.");
        }

        using var db = CreateContext();
        string code;
        do
        {
            code = GenerateCode();
        } while (db.InviteCodes.Any(c => c.Code == code));

        var invite = new InviteCode
        {
            Code = code,
            Note = note?.Trim() ?? "",
            AssignRole = assignRole == UserRole.Admin ? UserRole.Approver : assignRole,
            CreatedAt = DateTime.UtcNow
        };
        db.InviteCodes.Add(invite);
        db.SaveChanges();
        return invite;
    }

    public static (bool Ok, string Error) SetUserRole(int userId, UserRole role)
    {
        using var db = CreateContext();
        var user = db.Users.FirstOrDefault(u => u.Id == userId);
        if (user is null) return (false, "المستخدم غير موجود.");

        if (role is UserRole.Approver or UserRole.Admin)
        {
            var others = db.Users.Count(u =>
                u.Id != userId && (u.Role == UserRole.Approver || u.Role == UserRole.Admin || u.IsAdmin));
            if (others >= ApprovalService.MaxApprovers && user.Role is not (UserRole.Approver or UserRole.Admin) && !user.IsAdmin)
                return (false, $"لا يمكن تعيين أكثر من {ApprovalService.MaxApprovers} مخولين بالحفظ.");
        }

        user.Role = role;
        user.IsAdmin = role == UserRole.Admin;
        db.SaveChanges();
        return (true, "");
    }

    public static List<AppUser> ListUsers()
    {
        using var db = CreateContext();
        return db.Users.AsNoTracking().OrderByDescending(u => u.Role).ThenBy(u => u.DisplayName).ToList();
    }

    public static AppUser? FindById(int id)
    {
        using var db = CreateContext();
        return db.Users.AsNoTracking().FirstOrDefault(u => u.Id == id);
    }

    public static (bool Ok, string Error, AppUser? User) Register(
        string email,
        string phone,
        string displayName,
        string password,
        string inviteCode)
    {
        email = email.Trim().ToLowerInvariant();
        phone = phone.Trim();
        displayName = displayName.Trim();
        inviteCode = inviteCode.Trim();

        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
            return (false, "أدخل بريداً إلكترونياً صحيحاً.", null);
        if (string.IsNullOrWhiteSpace(phone) || phone.Length < 8)
            return (false, "أدخل رقم هاتف صحيحاً.", null);
        if (string.IsNullOrWhiteSpace(displayName))
            return (false, "أدخل الاسم الظاهر.", null);
        if (string.IsNullOrWhiteSpace(password) || password.Length < 6)
            return (false, "كلمة المرور يجب أن تكون 6 أحرف على الأقل.", null);
        if (string.IsNullOrWhiteSpace(inviteCode))
            return (false, "أدخل رقم الدعوة للمستخدم الجديد.", null);

        using var db = CreateContext();
        var invite = db.InviteCodes.FirstOrDefault(c => c.Code == inviteCode);
        if (invite is null)
            return (false, "رقم الدعوة غير صحيح.", null);
        if (invite.IsUsed)
            return (false, "رقم الدعوة مستخدم مسبقاً.", null);

        if (db.Users.Any(u => u.Email == email))
            return (false, "هذا البريد مسجّل مسبقاً.", null);
        if (db.Users.Any(u => u.Phone == phone))
            return (false, "رقم الهاتف مسجّل مسبقاً.", null);

        var role = invite.AssignRole;
        if (role is UserRole.Approver or UserRole.Admin)
        {
            var approvers = db.Users.Count(u => u.Role == UserRole.Approver || u.Role == UserRole.Admin || u.IsAdmin);
            if (approvers >= ApprovalService.MaxApprovers)
                role = UserRole.Editor;
        }

        var user = new AppUser
        {
            Email = email,
            Phone = phone,
            DisplayName = displayName,
            PasswordHash = PasswordHasher.Hash(password),
            IsAdmin = role == UserRole.Admin,
            Role = role == UserRole.Admin ? UserRole.Admin : role,
            InviteCodeUsed = inviteCode,
            CreatedAt = DateTime.UtcNow
        };
        db.Users.Add(user);
        invite.IsUsed = true;
        invite.UsedAt = DateTime.UtcNow;
        db.SaveChanges();

        invite.UsedByUserId = user.Id;
        db.SaveChanges();
        return (true, "", user);
    }

    public static AppUser? FindByLogin(string emailOrPhone)
    {
        var key = emailOrPhone.Trim().ToLowerInvariant();
        var phone = emailOrPhone.Trim();
        using var db = CreateContext();
        return db.Users.AsNoTracking()
            .FirstOrDefault(u => u.Email == key || u.Phone == phone);
    }

    private static bool IsPostgresConfigured()
    {
        return !string.IsNullOrWhiteSpace(GetPostgresConnection());
    }

    private static string? GetPostgresConnection()
    {
        var envPg = Environment.GetEnvironmentVariable("DATABASE_URL")
                    ?? Environment.GetEnvironmentVariable("POSTGRES_CONNECTION")
                    ?? Environment.GetEnvironmentVariable("ConnectionStrings__PostgreSql");
        if (!string.IsNullOrWhiteSpace(envPg))
            return envPg;

        var s = DatabaseService.Settings;
        if (string.Equals(s.Provider, "PostgreSql", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(s.PostgreSqlConnection))
            return s.PostgreSqlConnection;

        return null;
    }
}
