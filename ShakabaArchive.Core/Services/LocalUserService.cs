using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using ShakabaArchive.Data;
using ShakabaArchive.Models;

namespace ShakabaArchive.Services;

/// <summary>
/// مستخدمون على نفس قاعدة الأرشيف (Neon/PostgreSQL أونلاين، أو SQLite محلياً فقط).
/// </summary>
public static class LocalUserService
{
    private static readonly object Gate = new();
    private static string? _forcedPostgres;

    /// <summary>true عند استخدام Neon/PostgreSQL الثابت.</summary>
    public static bool UsesCloud => IsPostgresConfigured();

    /// <summary>على Render يجب Neon وإلا تُمسح الحسابات مع كل نشر.</summary>
    public static bool CanPersistUsers =>
        IsPostgresConfigured()
        || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("RENDER"));

    public static string PersistBlockedMessage =>
        "لا يمكن حفظ المستخدمين: أضف المتغير DATABASE_URL من Neon في Render → Environment. بدونها تُمسح الحسابات فوراً مع كل نشر أو إعادة تشغيل.";

    /// <summary>يُستدعى من Program بنفس اتصال الأرشيف تماماً.</summary>
    public static void ConfigureCloud(string? postgresConnection)
    {
        lock (Gate)
        {
            _forcedPostgres = string.IsNullOrWhiteSpace(postgresConnection)
                ? null
                : postgresConnection.Trim();
        }
    }

    public static void ConfigurePath(string sqliteFilePath)
    {
        // مسار SQLite للأجهزة المحلية يمر عبر DatabaseService
        var dir = Path.GetDirectoryName(sqliteFilePath);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);
    }

    public static ArchiveDbContext CreateContext()
    {
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
        return DatabaseService.CreateContext();
    }

    private static readonly object InitGate = new();
    private static bool _initialized;

    public static void Initialize()
    {
        lock (InitGate)
        {
            if (_initialized) return;
            DatabaseService.EnsureReady();
            using var db = CreateContext();
            EnsureUserSchema(db);
            UpgradeUserColumns(db);
            if (!CanQueryNewUsers(db))
                throw new InvalidOperationException(
                    "Users tables are missing in Neon. Check DATABASE_URL permissions and redeploy.");
            SeedAdminIfEmpty(db);
            _initialized = true;
            Console.WriteLine(UsesCloud
                ? "LocalUserService: using PostgreSQL/Neon (persistent)."
                : "LocalUserService: using SQLite (ephemeral on Render).");
        }
    }

    public static void EnsureReady()
    {
        if (!_initialized)
            Initialize();
    }

    /// <summary>إصلاح جداول المستخدمين إن فشل الدخول رغم اتصال Neon.</summary>
    public static (bool Ok, string Detail) ProbeAndRepairUsers()
    {
        try
        {
            DatabaseService.EnsureReady();
            using var db = CreateContext();
            EnsureUserSchema(db);
            UpgradeUserColumns(db);
            if (!CanQueryNewUsers(db))
                return (false, "Users/InviteCodes tables still unavailable after repair");
            SeedAdminIfEmpty(db);
            lock (InitGate) { _initialized = true; }
            var count = db.Users.Count();
            return (true, $"users={count}");
        }
        catch (Exception ex)
        {
            return (false, ex.GetBaseException().Message);
        }
    }

    private static string? WriteBlockedReason()
    {
        if (CanPersistUsers) return null;
        return PersistBlockedMessage;
    }

    private static void UpgradeUserColumns(ArchiveDbContext db)
    {
        // SQLite
        try { db.Database.ExecuteSqlRaw("ALTER TABLE Users ADD COLUMN Role INTEGER NOT NULL DEFAULT 0"); } catch { }
        try { db.Database.ExecuteSqlRaw("ALTER TABLE InviteCodes ADD COLUMN AssignRole INTEGER NOT NULL DEFAULT 0"); } catch { }
        // PostgreSQL / Neon
        try { db.Database.ExecuteSqlRaw("""ALTER TABLE "Users" ADD COLUMN IF NOT EXISTS "Role" integer NOT NULL DEFAULT 0"""); } catch { }
        try { db.Database.ExecuteSqlRaw("""ALTER TABLE "InviteCodes" ADD COLUMN IF NOT EXISTS "AssignRole" integer NOT NULL DEFAULT 0"""); } catch { }
    }

    private static void EnsureUserSchema(ArchiveDbContext db)
    {
        if (CanQueryNewUsers(db))
            return;

        // عند وجود جداول الأرشيف مسبقاً CreateTables قد يفشل — ننشئ جداول المستخدمين صراحة
        try
        {
            var isPostgres = db.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true;
            if (isPostgres)
            {
                // SERIAL متوافق أكثر مع Neon من IDENTITY
                db.Database.ExecuteSqlRaw("""
                    CREATE TABLE IF NOT EXISTS "Users" (
                        "Id" SERIAL PRIMARY KEY,
                        "Email" character varying(160) NOT NULL,
                        "Phone" character varying(40) NOT NULL,
                        "DisplayName" character varying(120) NOT NULL,
                        "PasswordHash" character varying(200) NOT NULL,
                        "IsAdmin" boolean NOT NULL DEFAULT FALSE,
                        "Role" integer NOT NULL DEFAULT 0,
                        "InviteCodeUsed" character varying(40) NOT NULL DEFAULT '',
                        "CreatedAt" timestamp with time zone NOT NULL DEFAULT NOW()
                    );
                    """);
                db.Database.ExecuteSqlRaw("""CREATE UNIQUE INDEX IF NOT EXISTS "IX_Users_Email" ON "Users" ("Email");""");
                db.Database.ExecuteSqlRaw("""CREATE INDEX IF NOT EXISTS "IX_Users_Phone" ON "Users" ("Phone");""");
                db.Database.ExecuteSqlRaw("""
                    CREATE TABLE IF NOT EXISTS "InviteCodes" (
                        "Id" SERIAL PRIMARY KEY,
                        "Code" character varying(40) NOT NULL,
                        "Note" character varying(200) NOT NULL DEFAULT '',
                        "AssignRole" integer NOT NULL DEFAULT 0,
                        "IsUsed" boolean NOT NULL DEFAULT FALSE,
                        "CreatedAt" timestamp with time zone NOT NULL DEFAULT NOW(),
                        "UsedAt" timestamp with time zone NULL,
                        "UsedByUserId" integer NULL
                    );
                    """);
                db.Database.ExecuteSqlRaw("""CREATE UNIQUE INDEX IF NOT EXISTS "IX_InviteCodes_Code" ON "InviteCodes" ("Code");""");
            }
            else
            {
                var creator = db.GetService<IRelationalDatabaseCreator>();
                if (!creator.Exists())
                    creator.Create();
                try { creator.CreateTables(); }
                catch (Exception ex)
                {
                    Console.Error.WriteLine("EnsureUserSchema CreateTables: " + ex.Message);
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("EnsureUserSchema: " + ex.Message);
        }
    }

    private static bool CanQueryNewUsers(ArchiveDbContext db)
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

    private static void SeedAdminIfEmpty(ArchiveDbContext db)
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

    private static void SeedAdminIfEmptyCore(ArchiveDbContext db)
    {
        const string adminEmail = "abohosam@shakaba.local";
        const string adminPassword = "Om123456@";

        // ترقية الحساب الافتراضي القديم إن وُجد — دون إعادة تعيين كلمة المرور إن كانت موجودة
        var legacy = db.Users.FirstOrDefault(u =>
            u.Email == "admin@shakaba.local" || u.Email == "abohosam@shukaba.local");
        if (legacy is not null)
        {
            legacy.Email = adminEmail;
            legacy.Phone = legacy.Phone is "0000000000" or "" ? "0000000000" : legacy.Phone;
            if (string.IsNullOrWhiteSpace(legacy.DisplayName))
                legacy.DisplayName = "أبو حسام";
            if (string.IsNullOrWhiteSpace(legacy.PasswordHash))
                legacy.PasswordHash = PasswordHasher.Hash(adminPassword);
            legacy.IsAdmin = true;
            legacy.Role = UserRole.Admin;
            db.SaveChanges();
            return;
        }

        var existingAdmin = db.Users.FirstOrDefault(u => u.Email == adminEmail);
        if (existingAdmin is not null)
        {
            // لا نلمس كلمة المرور أبداً بعد الإنشاء — تبقى خاصة بالمستخدم
            existingAdmin.IsAdmin = true;
            existingAdmin.Role = UserRole.Admin;
            if (string.IsNullOrWhiteSpace(existingAdmin.DisplayName))
                existingAdmin.DisplayName = "أبو حسام";
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
        EnsureReady();
        using var db = CreateContext();
        // الثلاثة الموافقون فقط — الأدمن الرئيسي لا يُحسب ضمنهم
        return db.Users.Count(u => u.Role == UserRole.Approver && !u.IsAdmin);
    }

    public static int CountUsers()
    {
        EnsureReady();
        using var db = CreateContext();
        return db.Users.Count();
    }

    public static InviteCode CreateInvite(string? note = null, UserRole assignRole = UserRole.Editor)
    {
        if (WriteBlockedReason() is { } blocked)
            throw new InvalidOperationException(blocked);

        if (assignRole == UserRole.Approver)
        {
            if (CountApprovers() >= ApprovalService.MaxApprovers)
                throw new InvalidOperationException(
                    $"الحد الأقصى للموافقين على صحة البيانات هو {ApprovalService.MaxApprovers}.");
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
        EnsureReady();
        using var db = CreateContext();
        var user = db.Users.FirstOrDefault(u => u.Id == userId);
        if (user is null) return (false, "المستخدم غير موجود.");

        var result = SetUserRoleInDb(db, user, role);
        if (!result.Ok) return result;

        db.SaveChanges();
        return (true, "");
    }

    public static List<AppUser> ListUsers()
    {
        EnsureReady();
        using var db = CreateContext();
        // الأدمن الرئيسي أولاً، ثم الثلاثة الموافقون، ثم مدخلو البيانات
        return db.Users.AsNoTracking()
            .OrderByDescending(u => u.IsAdmin || u.Role == UserRole.Admin)
            .ThenByDescending(u => u.Role == UserRole.Approver)
            .ThenBy(u => u.DisplayName)
            .ToList();
    }

    public static AppUser? FindById(int id)
    {
        EnsureReady();
        using var db = CreateContext();
        return db.Users.AsNoTracking().FirstOrDefault(u => u.Id == id);
    }

    public static (bool Ok, string Error, AppUser? User) CreateUser(
        string email,
        string phone,
        string displayName,
        string password,
        UserRole role)
    {
        email = email.Trim().ToLowerInvariant();
        phone = phone.Trim();
        displayName = displayName.Trim();

        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
            return (false, "أدخل بريداً إلكترونياً صحيحاً.", null);
        if (string.IsNullOrWhiteSpace(phone) || phone.Length < 8)
            return (false, "أدخل رقم هاتف صحيحاً.", null);
        if (string.IsNullOrWhiteSpace(displayName))
            return (false, "أدخل الاسم الظاهر.", null);
        if (string.IsNullOrWhiteSpace(password) || password.Length < 6)
            return (false, "كلمة المرور يجب أن تكون 6 أحرف على الأقل.", null);

        if (WriteBlockedReason() is { } blocked)
            return (false, blocked, null);

        EnsureReady();

        if (CountUsers() >= ApprovalService.MaxUsers)
            return (false, $"تم بلوغ الحد الأقصى للمستخدمين ({ApprovalService.MaxUsers}).", null);

        if (role == UserRole.Approver && CountApprovers() >= ApprovalService.MaxApprovers)
            return (false, $"لا يمكن إضافة أكثر من {ApprovalService.MaxApprovers} موافقين على صحة البيانات.", null);

        using var db = CreateContext();
        if (db.Users.Any(u => u.Email == email))
            return (false, "هذا البريد مسجّل مسبقاً.", null);
        if (db.Users.Any(u => u.Phone == phone))
            return (false, "رقم الهاتف مسجّل مسبقاً.", null);

        var user = new AppUser
        {
            Email = email,
            Phone = phone,
            DisplayName = displayName,
            PasswordHash = PasswordHasher.Hash(password),
            IsAdmin = role == UserRole.Admin,
            Role = role,
            InviteCodeUsed = "ADMIN-ADD",
            CreatedAt = DateTime.UtcNow
        };
        db.Users.Add(user);
        db.SaveChanges();
        return (true, "", user);
    }

    /// <summary>يغيّر المستخدم كلمة مروره الخاصة بعد التحقق من الحالية.</summary>
    public static (bool Ok, string Error) ChangeOwnPassword(
        int userId,
        string currentPassword,
        string newPassword)
    {
        if (string.IsNullOrWhiteSpace(currentPassword))
            return (false, "أدخل كلمة المرور الحالية.");
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 6)
            return (false, "كلمة المرور الجديدة يجب أن تكون 6 أحرف على الأقل.");
        if (currentPassword == newPassword)
            return (false, "اختر كلمة مرور جديدة مختلفة عن الحالية.");

        EnsureReady();
        using var db = CreateContext();
        var user = db.Users.FirstOrDefault(u => u.Id == userId);
        if (user is null)
            return (false, "المستخدم غير موجود.");
        if (!PasswordHasher.Verify(currentPassword, user.PasswordHash))
            return (false, "كلمة المرور الحالية غير صحيحة.");

        user.PasswordHash = PasswordHasher.Hash(newPassword);
        db.SaveChanges();
        return (true, "");
    }

    public static (bool Ok, string Error) UpdateUser(
        int userId,
        string email,
        string phone,
        string displayName,
        string? newPassword,
        UserRole role)
    {
        email = email.Trim().ToLowerInvariant();
        phone = phone.Trim();
        displayName = displayName.Trim();

        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
            return (false, "أدخل بريداً إلكترونياً صحيحاً.");
        if (string.IsNullOrWhiteSpace(phone) || phone.Length < 8)
            return (false, "أدخل رقم هاتف صحيحاً.");
        if (string.IsNullOrWhiteSpace(displayName))
            return (false, "أدخل الاسم الظاهر.");
        if (!string.IsNullOrWhiteSpace(newPassword) && newPassword.Length < 6)
            return (false, "كلمة المرور يجب أن تكون 6 أحرف على الأقل.");

        EnsureReady();
        using var db = CreateContext();
        var user = db.Users.FirstOrDefault(u => u.Id == userId);
        if (user is null) return (false, "المستخدم غير موجود.");

        if (db.Users.Any(u => u.Id != userId && u.Email == email))
            return (false, "هذا البريد مسجّل مسبقاً.");
        if (db.Users.Any(u => u.Id != userId && u.Phone == phone))
            return (false, "رقم الهاتف مسجّل مسبقاً.");

        var roleResult = SetUserRoleInDb(db, user, role);
        if (!roleResult.Ok) return roleResult;

        user.Email = email;
        user.Phone = phone;
        user.DisplayName = displayName;
        if (!string.IsNullOrWhiteSpace(newPassword))
            user.PasswordHash = PasswordHasher.Hash(newPassword);

        db.SaveChanges();
        return (true, "");
    }

    public static (bool Ok, string Error) DeleteUser(int userId, int? currentAdminId = null)
    {
        EnsureReady();
        using var db = CreateContext();
        var user = db.Users.FirstOrDefault(u => u.Id == userId);
        if (user is null) return (false, "المستخدم غير موجود.");

        if (currentAdminId is int selfId && user.Id == selfId)
            return (false, "لا يمكنك حذف حسابك وأنت مسجّل الدخول.");

        if (user.IsAdmin || user.Role == UserRole.Admin)
        {
            var otherAdmins = db.Users.Count(u =>
                u.Id != userId && (u.IsAdmin || u.Role == UserRole.Admin));
            if (otherAdmins == 0)
                return (false, "لا يمكن حذف الأدمن الرئيسي الوحيد.");
        }

        db.Users.Remove(user);
        db.SaveChanges();
        return (true, "");
    }

    private static (bool Ok, string Error) SetUserRoleInDb(ArchiveDbContext db, AppUser user, UserRole role)
    {
        var wasAdmin = user.IsAdmin || user.Role == UserRole.Admin;
        if (wasAdmin && role != UserRole.Admin)
        {
            var otherAdmins = db.Users.Count(u =>
                u.Id != user.Id && (u.IsAdmin || u.Role == UserRole.Admin));
            if (otherAdmins == 0)
                return (false, "لا يمكن إزالة الأدمن الرئيسي الوحيد.");
        }

        if (role == UserRole.Approver)
        {
            var others = db.Users.Count(u =>
                u.Id != user.Id && u.Role == UserRole.Approver && !u.IsAdmin);
            if (others >= ApprovalService.MaxApprovers && user.Role != UserRole.Approver)
                return (false, $"لا يمكن تعيين أكثر من {ApprovalService.MaxApprovers} موافقين على صحة البيانات.");
        }

        user.Role = role;
        user.IsAdmin = role == UserRole.Admin;
        return (true, "");
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

        if (WriteBlockedReason() is { } blocked)
            return (false, blocked, null);

        EnsureReady();
        if (CountUsers() >= ApprovalService.MaxUsers)
            return (false, $"تم بلوغ الحد الأقصى للمستخدمين ({ApprovalService.MaxUsers}).", null);

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

        var role = invite.AssignRole == UserRole.Admin ? UserRole.Approver : invite.AssignRole;
        if (role == UserRole.Approver)
        {
            var approvers = db.Users.Count(u => u.Role == UserRole.Approver && !u.IsAdmin);
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

        Exception? last = null;
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            try
            {
                // إن فشل سابقاً نصلح جداول المستخدمين دون إعادة تهيئة الأرشيف بالكامل
                if (!_initialized)
                {
                    var repair = ProbeAndRepairUsers();
                    if (!repair.Ok)
                        throw new InvalidOperationException(repair.Detail);
                }

                using var db = CreateContext();
                return db.Users.AsNoTracking()
                    .FirstOrDefault(u => u.Email == key || u.Phone == phone);
            }
            catch (Exception ex)
            {
                last = ex;
                Console.Error.WriteLine($"FindByLogin attempt {attempt}/2: {ex.Message}");
                lock (InitGate) { _initialized = false; }
                if (attempt < 2)
                    Thread.Sleep(2500);
            }
        }

        throw last ?? new InvalidOperationException("تعذر الاتصال بقاعدة المستخدمين.");
    }

    private static bool IsPostgresConfigured()
    {
        return !string.IsNullOrWhiteSpace(GetPostgresConnection());
    }

    private static string? GetPostgresConnection()
    {
        if (!string.IsNullOrWhiteSpace(_forcedPostgres))
            return _forcedPostgres;

        var envPg = Environment.GetEnvironmentVariable("DATABASE_URL")
                    ?? Environment.GetEnvironmentVariable("POSTGRES_CONNECTION")
                    ?? Environment.GetEnvironmentVariable("POSTGRES_URL")
                    ?? Environment.GetEnvironmentVariable("NEON_DATABASE_URL")
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
