using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using ShakabaArchive.Data;
using ShakabaArchive.Models;

namespace ShakabaArchive.Services;

public sealed class AppSettings
{
    public string Provider { get; set; } = "Sqlite";
    public string SqliteFileName { get; set; } = "shakaba-archive.db";
    public string PostgreSqlConnection { get; set; } = string.Empty;
}

public static class DatabaseService
{
    private static readonly object Gate = new();
    private static AppSettings? _settings;
    private static string? _sqlitePath;
    private static string? _dataFolderOverride;
    private static string? _uploadsFolderOverride;

    public static void ConfigurePaths(string dataFolder, string? uploadsFolder = null)
    {
        lock (Gate)
        {
            _dataFolderOverride = dataFolder;
            _uploadsFolderOverride = uploadsFolder;
            Directory.CreateDirectory(dataFolder);
            if (uploadsFolder is not null)
                Directory.CreateDirectory(uploadsFolder);
            _settings = null;
            _sqlitePath = null;
        }
    }

    public static string DataFolder
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(_dataFolderOverride))
            {
                Directory.CreateDirectory(_dataFolderOverride);
                return _dataFolderOverride;
            }

            var folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ShakabaArchive");
            Directory.CreateDirectory(folder);
            return folder;
        }
    }

    public static string UploadsFolder
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(_uploadsFolderOverride))
            {
                Directory.CreateDirectory(_uploadsFolderOverride);
                return _uploadsFolderOverride;
            }

            var folder = Path.Combine(DataFolder, "uploads");
            Directory.CreateDirectory(folder);
            return folder;
        }
    }

    public static string SettingsPath => Path.Combine(DataFolder, "appsettings.json");

    public static AppSettings Settings
    {
        get
        {
            lock (Gate)
            {
                _settings ??= LoadSettings();
                return _settings;
            }
        }
    }

    public static string ProviderLabel =>
        string.Equals(Settings.Provider, "PostgreSql", StringComparison.OrdinalIgnoreCase)
            ? "PostgreSQL (أونلاين)"
            : "SQLite (محلي مجاني)";

    public static ArchiveDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ArchiveDbContext>();
        Configure(options);
        return new ArchiveDbContext(options.Options);
    }

    /// <summary>
    /// اتصال مباشر بدون -pooler لإنشاء الجداول (PgBouncer/pooler لا يدعم DDL جيداً).
    /// </summary>
    public static ArchiveDbContext CreateContextForSchemaChanges()
    {
        var options = new DbContextOptionsBuilder<ArchiveDbContext>();
        var raw = Environment.GetEnvironmentVariable("DATABASE_URL")
                  ?? Environment.GetEnvironmentVariable("POSTGRES_CONNECTION")
                  ?? Environment.GetEnvironmentVariable("POSTGRES_URL")
                  ?? Environment.GetEnvironmentVariable("NEON_DATABASE_URL");
        if (string.IsNullOrWhiteSpace(raw)
            && string.Equals(Settings.Provider, "PostgreSql", StringComparison.OrdinalIgnoreCase))
            raw = Settings.PostgreSqlConnection;

        if (!string.IsNullOrWhiteSpace(raw))
        {
            var cs = NormalizePostgresUrl(raw)
                .Replace("-pooler.", ".", StringComparison.OrdinalIgnoreCase);
            options.UseNpgsql(cs);
            return new ArchiveDbContext(options.Options);
        }

        Configure(options);
        return new ArchiveDbContext(options.Options);
    }

    public static void Configure(DbContextOptionsBuilder options)
    {
        var s = Settings;
        var envPg = Environment.GetEnvironmentVariable("DATABASE_URL")
                    ?? Environment.GetEnvironmentVariable("POSTGRES_CONNECTION");

        if (!string.IsNullOrWhiteSpace(envPg))
        {
            options.UseNpgsql(NormalizePostgresUrl(envPg));
            return;
        }

        if (string.Equals(s.Provider, "PostgreSql", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(s.PostgreSqlConnection))
        {
            options.UseNpgsql(NormalizePostgresUrl(s.PostgreSqlConnection));
            return;
        }

        _sqlitePath ??= Path.Combine(DataFolder, s.SqliteFileName);
        options.UseSqlite($"Data Source={_sqlitePath}");
    }

    private static readonly object InitGate = new();
    private static bool _initialized;

    public static void Initialize()
    {
        lock (InitGate)
        {
            if (_initialized) return;

            // Allow Unspecified DateTime values (birth/event dates) with PostgreSQL.
            AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

            EnsureSettingsFile();
            using var db = CreateContext();

            // محاولات قصيرة على Free حتى لا تُقتل العملية (exit 139)
            var ready = WaitForDatabase(db, attempts: 4, delayMs: 1500);
            if (!ready)
            {
                Console.Error.WriteLine("Database not reachable yet — will retry on first request.");
                return;
            }

            if (!CanQueryPeople(db))
                EnsureTables(db);
            else
                UpgradeSchema(db);

            if (CanQueryPeople(db))
            {
                RemoveRetiredOccasionTypes(db);
                SeedIfEmpty(db);
            }

            _initialized = true;
        }
    }

    /// <summary>فحص اتصال للتشخيص على /health/db</summary>
    public static (bool Ok, string Mode, string Detail) ProbeConnection()
    {
        try
        {
            AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);
            using var db = CreateContext();
            var mode = db.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true
                ? "PostgreSQL/Neon"
                : "SQLite";
            if (!db.Database.CanConnect())
                return (false, mode, "CanConnect returned false");
            _ = db.People.Take(1).ToList();
            return (true, mode, "connected");
        }
        catch (Exception ex)
        {
            var mode = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DATABASE_URL"))
                ? "PostgreSQL/Neon"
                : "unknown";
            return (false, mode, ex.GetBaseException().Message);
        }
    }

    public static void EnsureReady()
    {
        if (!_initialized)
            Initialize();
    }

    public static void ResetInitialization()
    {
        lock (InitGate)
            _initialized = false;
    }

    public static void EnsureDataProtectionKeysTable(ArchiveDbContext db)
    {
        try
        {
            var isPostgres = db.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true;
            if (isPostgres)
            {
                using var ddl = CreateContextForSchemaChanges();
                ddl.Database.ExecuteSqlRaw("""
                    CREATE TABLE IF NOT EXISTS "DataProtectionKeys" (
                        "Id" SERIAL PRIMARY KEY,
                        "FriendlyName" text NULL,
                        "Xml" text NULL
                    );
                    """);
            }
            else
            {
                db.Database.ExecuteSqlRaw("""
                    CREATE TABLE IF NOT EXISTS DataProtectionKeys (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        FriendlyName TEXT NULL,
                        Xml TEXT NULL
                    );
                    """);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("EnsureDataProtectionKeysTable: " + ex.Message);
        }
    }

    private static bool WaitForDatabase(ArchiveDbContext db, int attempts, int delayMs)
    {
        for (var i = 0; i < attempts; i++)
        {
            try
            {
                if (db.Database.CanConnect())
                    return true;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Database wake attempt {i + 1}/{attempts}: {ex.Message}");
            }

            Thread.Sleep(delayMs);
        }

        return false;
    }

    /// <summary>حذف الطلاق والعزاء — اكتفاءً بنوع الوفاة.</summary>
    private static void RemoveRetiredOccasionTypes(ArchiveDbContext db)
    {
        try
        {
            var retired = db.LifeEvents
                .Where(e => e.Type == EventType.Divorce || e.Type == EventType.Condolence)
                .ToList();
            if (retired.Count == 0) return;
            db.LifeEvents.RemoveRange(retired);
            db.SaveChanges();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("RemoveRetiredOccasionTypes failed: " + ex.Message);
        }
    }

    private static bool CanQueryPeople(ArchiveDbContext db)
    {
        try
        {
            _ = db.People.Any();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void EnsureTables(ArchiveDbContext db)
    {
        var creator = db.GetService<IRelationalDatabaseCreator>();
        if (!creator.Exists())
            creator.Create();

        // لا نحذف المخطط أبداً — حذف public CASCADE كان يمسح السجلات عند فشل اتصال Neon المؤقت
        try
        {
            creator.CreateTables();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("EnsureTables CreateTables: " + ex.Message);
            // إن وُجدت الجداول مسبقاً نكتفي بالترقية
            try { UpgradeSchema(db); }
            catch (Exception upEx)
            {
                Console.Error.WriteLine("EnsureTables UpgradeSchema: " + upEx.Message);
            }
        }
    }

    public static void ReloadSettings()
    {
        lock (Gate)
        {
            _settings = LoadSettings();
            _sqlitePath = null;
        }
    }

    public static void SaveSettings(AppSettings settings)
    {
        Directory.CreateDirectory(DataFolder);
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(SettingsPath, json);
        lock (Gate)
        {
            _settings = settings;
            _sqlitePath = null;
        }
    }

    public static string SaveDocumentImage(Stream content, string originalFileName)
    {
        var ext = Path.GetExtension(originalFileName);
        if (string.IsNullOrWhiteSpace(ext) || ext.Length > 8)
            ext = ".jpg";

        var allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { ".jpg", ".jpeg", ".png", ".webp", ".gif", ".pdf" };
        if (!allowed.Contains(ext))
            ext = ".jpg";

        using var ms = new MemoryStream();
        content.CopyTo(ms);
        var bytes = ms.ToArray();
        if (bytes.Length == 0)
            throw new InvalidOperationException("الملف فارغ.");
        if (bytes.Length > 5 * 1024 * 1024)
            throw new InvalidOperationException("حجم الملف أكبر من 5 ميجابايت.");

        var name = $"{Guid.NewGuid():N}{ext.ToLowerInvariant()}";
        Directory.CreateDirectory(UploadsFolder);
        var full = Path.Combine(UploadsFolder, name);
        File.WriteAllBytes(full, bytes);

        // على Render القرص مؤقت — احفظ أيضاً في Neon ليظهر من أي جهاز
        TrySaveMediaToDatabase(name, GuessContentType(ext), bytes);
        return name;
    }

    public static MediaFile? FindMediaFile(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName)
            || fileName.Contains("..")
            || fileName.Contains('/')
            || fileName.Contains('\\'))
            return null;

        try
        {
            using var db = CreateContext();
            EnsureMediaFilesTable(db);
            return db.MediaFiles.AsNoTracking().FirstOrDefault(x => x.Id == fileName);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FindMediaFile: " + ex.Message);
            return null;
        }
    }

    public static void EnsureMediaFilesTable(ArchiveDbContext db)
    {
        try
        {
            _ = db.MediaFiles.Select(x => x.Id).Take(1).ToList();
            return;
        }
        catch
        {
            // create below
        }

        try
        {
            var isPostgres = db.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true;
            if (isPostgres)
            {
                using var ddl = CreateContextForSchemaChanges();
                ddl.Database.ExecuteSqlRaw("""
                    CREATE TABLE IF NOT EXISTS "MediaFiles" (
                        "Id" character varying(80) PRIMARY KEY,
                        "ContentType" character varying(120) NOT NULL DEFAULT 'application/octet-stream',
                        "Data" bytea NOT NULL,
                        "CreatedAt" timestamp with time zone NOT NULL DEFAULT NOW()
                    );
                    """);
            }
            else
            {
                db.Database.ExecuteSqlRaw("""
                    CREATE TABLE IF NOT EXISTS MediaFiles (
                        Id TEXT PRIMARY KEY,
                        ContentType TEXT NOT NULL DEFAULT 'application/octet-stream',
                        Data BLOB NOT NULL,
                        CreatedAt TEXT NOT NULL
                    );
                    """);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("EnsureMediaFilesTable: " + ex.Message);
        }
    }

    private static void TrySaveMediaToDatabase(string id, string contentType, byte[] data)
    {
        try
        {
            var envPg = Environment.GetEnvironmentVariable("DATABASE_URL")
                        ?? Environment.GetEnvironmentVariable("POSTGRES_CONNECTION");
            var usePg = !string.IsNullOrWhiteSpace(envPg)
                        || string.Equals(Settings.Provider, "PostgreSql", StringComparison.OrdinalIgnoreCase);
            if (!usePg)
                return;

            using var db = CreateContext();
            EnsureMediaFilesTable(db);
            if (db.MediaFiles.Any(x => x.Id == id))
                return;

            db.MediaFiles.Add(new MediaFile
            {
                Id = id,
                ContentType = contentType,
                Data = data,
                CreatedAt = DateTime.UtcNow
            });
            db.SaveChanges();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("TrySaveMediaToDatabase: " + ex.Message);
        }
    }

    private static string GuessContentType(string ext) => ext.ToLowerInvariant() switch
    {
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        ".webp" => "image/webp",
        ".gif" => "image/gif",
        ".pdf" => "application/pdf",
        _ => "application/octet-stream"
    };

    public static string NormalizeConnectionString(string value) => NormalizePostgresUrl(value);

    private static string NormalizePostgresUrl(string value)
    {
        value = value.Trim().Trim('"').Trim('\'');

        // Neon يضيف channel_binding=require وقد يفشل معه Npgsql على Render
        value = value
            .Replace("&channel_binding=require", "", StringComparison.OrdinalIgnoreCase)
            .Replace("?channel_binding=require&", "?", StringComparison.OrdinalIgnoreCase)
            .Replace("?channel_binding=require", "", StringComparison.OrdinalIgnoreCase)
            .Replace("channel_binding=require", "", StringComparison.OrdinalIgnoreCase);

        // Render/Heroku/Neon style: postgres://user:pass@host/db
        if (value.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            var uri = new Uri(value);
            var userInfo = uri.UserInfo.Split(':', 2);
            var user = Uri.UnescapeDataString(userInfo[0]);
            var pass = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : "";
            var db = uri.AbsolutePath.Trim('/');
            if (string.IsNullOrWhiteSpace(db))
                db = "neondb";

            // يُفضَّل مضيف Neon الذي فيه -pooler للاتصالات من Render
            return $"Host={uri.Host};Port={(uri.Port > 0 ? uri.Port : 5432)};Database={db};Username={user};Password={pass};SSL Mode=Require;Trust Server Certificate=true;Timeout=30;Command Timeout=30;Keepalive=30;Pooling=true;Maximum Pool Size=5;Connection Idle Lifetime=60";
        }

        // إن وُضع رابط ناقص بدون postgresql:// — حاول إصلاحه إن بدا كمضيف Neon
        if (value.Contains("neon.tech", StringComparison.OrdinalIgnoreCase)
            && !value.Contains("Host=", StringComparison.OrdinalIgnoreCase)
            && !value.Contains("://", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine(
                "DATABASE_URL looks incomplete (missing postgresql://user:password@). Paste the full URI from Neon.");
        }

        return value;
    }

    private static void EnsureSettingsFile()
    {
        if (File.Exists(SettingsPath))
            return;

        var bundled = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        if (File.Exists(bundled))
        {
            File.Copy(bundled, SettingsPath);
            return;
        }

        SaveSettings(new AppSettings());
    }

    private static AppSettings LoadSettings()
    {
        EnsureSettingsFile();
        try
        {
            var json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    private static void UpgradeSchema(ArchiveDbContext db)
    {
        TryAlter(db, "ALTER TABLE People ADD COLUMN Tribe TEXT NOT NULL DEFAULT ''");
        TryAlter(db, "ALTER TABLE People ADD COLUMN Neighborhood TEXT NOT NULL DEFAULT ''");
        TryAlter(db, "ALTER TABLE People ADD COLUMN DocumentImagePath TEXT NOT NULL DEFAULT ''");
        TryAlter(db, "ALTER TABLE People ADD COLUMN RegistryCode TEXT NOT NULL DEFAULT ''");
        TryAlter(db, "ALTER TABLE People ADD COLUMN HierarchyLevel INTEGER NOT NULL DEFAULT 1");
        TryAlter(db, "ALTER TABLE People ADD COLUMN ParentPersonId INTEGER NULL");
        TryAlter(db, "ALTER TABLE People ADD COLUMN FirstName TEXT NOT NULL DEFAULT ''");
        TryAlter(db, "ALTER TABLE People ADD COLUMN GrandfatherName TEXT NOT NULL DEFAULT ''");
        TryAlter(db, "ALTER TABLE People ADD COLUMN FamilyName TEXT NOT NULL DEFAULT ''");
        TryAlter(db, "ALTER TABLE People ADD COLUMN Profession TEXT NOT NULL DEFAULT ''");
        TryAlter(db, "ALTER TABLE People ADD COLUMN PhotoPath TEXT NOT NULL DEFAULT ''");
        TryAlter(db, "ALTER TABLE People ADD COLUMN DocumentType TEXT NOT NULL DEFAULT 'رقم وطني'");
        TryAlter(db, "ALTER TABLE People ADD COLUMN DocumentNumber TEXT NOT NULL DEFAULT ''");
        TryAlter(db, "ALTER TABLE People ADD COLUMN IsMigrant INTEGER NOT NULL DEFAULT 0");
        TryAlter(db, "ALTER TABLE People ADD COLUMN MigrationCountry TEXT NOT NULL DEFAULT ''");
        TryAlter(db, "ALTER TABLE People ADD COLUMN MigrationCity TEXT NOT NULL DEFAULT ''");

        TryAlter(db, """ALTER TABLE "People" ADD COLUMN IF NOT EXISTS "RegistryCode" varchar(32) NOT NULL DEFAULT ''""");
        TryAlter(db, """ALTER TABLE "People" ADD COLUMN IF NOT EXISTS "HierarchyLevel" integer NOT NULL DEFAULT 1""");
        TryAlter(db, """ALTER TABLE "People" ADD COLUMN IF NOT EXISTS "ParentPersonId" integer NULL""");
        TryAlter(db, """ALTER TABLE "People" ADD COLUMN IF NOT EXISTS "FirstName" varchar(80) NOT NULL DEFAULT ''""");
        TryAlter(db, """ALTER TABLE "People" ADD COLUMN IF NOT EXISTS "GrandfatherName" varchar(80) NOT NULL DEFAULT ''""");
        TryAlter(db, """ALTER TABLE "People" ADD COLUMN IF NOT EXISTS "FamilyName" varchar(80) NOT NULL DEFAULT ''""");
        TryAlter(db, """ALTER TABLE "People" ADD COLUMN IF NOT EXISTS "Profession" varchar(120) NOT NULL DEFAULT ''""");
        TryAlter(db, """ALTER TABLE "People" ADD COLUMN IF NOT EXISTS "Tribe" text NOT NULL DEFAULT ''""");
        TryAlter(db, """ALTER TABLE "People" ADD COLUMN IF NOT EXISTS "PhotoPath" varchar(400) NOT NULL DEFAULT ''""");
        TryAlter(db, """ALTER TABLE "People" ADD COLUMN IF NOT EXISTS "DocumentType" varchar(40) NOT NULL DEFAULT 'رقم وطني'""");
        TryAlter(db, """ALTER TABLE "People" ADD COLUMN IF NOT EXISTS "DocumentNumber" varchar(80) NOT NULL DEFAULT ''""");
        TryAlter(db, """ALTER TABLE "People" ADD COLUMN IF NOT EXISTS "IsMigrant" boolean NOT NULL DEFAULT false""");
        TryAlter(db, """ALTER TABLE "People" ADD COLUMN IF NOT EXISTS "MigrationCountry" varchar(120) NOT NULL DEFAULT ''""");
        TryAlter(db, """ALTER TABLE "People" ADD COLUMN IF NOT EXISTS "MigrationCity" varchar(120) NOT NULL DEFAULT ''""");
        TryAlter(db, "ALTER TABLE People ADD COLUMN OwnerUserId INTEGER NULL");
        TryAlter(db, """ALTER TABLE "People" ADD COLUMN IF NOT EXISTS "OwnerUserId" integer NULL""");

        BackfillPersonRegistryFields(db);
        BackfillPersonOwners(db);

        TryAlter(db, "ALTER TABLE LifeEvents ADD COLUMN Mood INTEGER NOT NULL DEFAULT 0");
        TryAlter(db, "ALTER TABLE \"LifeEvents\" ADD COLUMN \"Mood\" integer NOT NULL DEFAULT 0");

        TryAlter(db, "ALTER TABLE LifeEvents ADD COLUMN RelatedFatherName TEXT NOT NULL DEFAULT ''");
        TryAlter(db, "ALTER TABLE \"LifeEvents\" ADD COLUMN \"RelatedFatherName\" text NOT NULL DEFAULT ''");
        TryAlter(db, "ALTER TABLE LifeEvents ADD COLUMN RelatedPhone TEXT NOT NULL DEFAULT ''");
        TryAlter(db, "ALTER TABLE \"LifeEvents\" ADD COLUMN \"RelatedPhone\" text NOT NULL DEFAULT ''");
        TryAlter(db, "ALTER TABLE LifeEvents ADD COLUMN ChildFullName TEXT NOT NULL DEFAULT ''");
        TryAlter(db, "ALTER TABLE \"LifeEvents\" ADD COLUMN \"ChildFullName\" text NOT NULL DEFAULT ''");
        TryAlter(db, "ALTER TABLE LifeEvents ADD COLUMN ChildGender TEXT NOT NULL DEFAULT ''");
        TryAlter(db, "ALTER TABLE \"LifeEvents\" ADD COLUMN \"ChildGender\" text NOT NULL DEFAULT ''");
        TryAlter(db, "ALTER TABLE LifeEvents ADD COLUMN MotherName TEXT NOT NULL DEFAULT ''");
        TryAlter(db, "ALTER TABLE \"LifeEvents\" ADD COLUMN \"MotherName\" text NOT NULL DEFAULT ''");
        TryAlter(db, "ALTER TABLE LifeEvents ADD COLUMN Institution TEXT NOT NULL DEFAULT ''");
        TryAlter(db, "ALTER TABLE \"LifeEvents\" ADD COLUMN \"Institution\" text NOT NULL DEFAULT ''");
        TryAlter(db, "ALTER TABLE LifeEvents ADD COLUMN Specialty TEXT NOT NULL DEFAULT ''");
        TryAlter(db, "ALTER TABLE \"LifeEvents\" ADD COLUMN \"Specialty\" text NOT NULL DEFAULT ''");
        TryAlter(db, "ALTER TABLE LifeEvents ADD COLUMN Degree TEXT NOT NULL DEFAULT ''");
        TryAlter(db, "ALTER TABLE \"LifeEvents\" ADD COLUMN \"Degree\" text NOT NULL DEFAULT ''");
    }

    private static void BackfillPersonRegistryFields(ArchiveDbContext db)
    {
        try
        {
            var people = db.People.Where(p => p.RegistryCode == "" || p.FirstName == "").ToList();
            if (people.Count == 0) return;

            var seq = 1;
            foreach (var p in people.OrderBy(x => x.Id))
            {
                if (string.IsNullOrWhiteSpace(p.FirstName))
                {
                    var parts = (p.FullName ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    p.FirstName = parts.Length > 0 ? parts[0] : (p.FullName ?? "");
                    if (string.IsNullOrWhiteSpace(p.FatherName) && parts.Length > 1)
                        p.FatherName = parts[1];
                    if (string.IsNullOrWhiteSpace(p.GrandfatherName) && parts.Length > 2)
                        p.GrandfatherName = parts[2];
                    if (string.IsNullOrWhiteSpace(p.FamilyName) && parts.Length > 3)
                        p.FamilyName = string.Join(" ", parts.Skip(3));
                }

                if (string.IsNullOrWhiteSpace(p.RegistryCode))
                {
                    p.HierarchyLevel = 1;
                    p.RegistryCode = seq.ToString("D2");
                    seq++;
                }

                if (string.IsNullOrWhiteSpace(p.DocumentType))
                    p.DocumentType = DocumentTypes.NationalId;
                if (string.IsNullOrWhiteSpace(p.DocumentNumber) && !string.IsNullOrWhiteSpace(p.NationalId))
                    p.DocumentNumber = p.NationalId;
                if (string.IsNullOrWhiteSpace(p.NationalId) && !string.IsNullOrWhiteSpace(p.DocumentNumber))
                    p.NationalId = p.DocumentNumber;

                p.RefreshFullName();
                if (string.IsNullOrWhiteSpace(p.FullName))
                    p.FullName = p.FirstName;
            }

            db.SaveChanges();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("BackfillPersonRegistryFields failed: " + ex.Message);
        }
    }

    private static void BackfillPersonOwners(ArchiveDbContext db)
    {
        try
        {
            // من طلبات الإضافة المعتمدة
            var links = db.PendingChanges.AsNoTracking()
                .Where(x => x.EntityType == ChangeEntity.Person
                            && x.Action == ChangeAction.Create
                            && x.EntityId != null
                            && x.SubmittedByUserId > 0)
                .Select(x => new { PersonId = x.EntityId!.Value, x.SubmittedByUserId })
                .ToList();

            foreach (var group in links.GroupBy(x => x.PersonId))
            {
                var person = db.People.FirstOrDefault(p => p.Id == group.Key && p.OwnerUserId == null);
                if (person is null) continue;
                person.OwnerUserId = group.First().SubmittedByUserId;
            }

            // بالهاتف إن تطابق مع مستخدم
            var orphans = db.People.Where(p => p.OwnerUserId == null && p.Phone != "").ToList();
            foreach (var person in orphans)
            {
                var user = db.Users.AsNoTracking()
                    .FirstOrDefault(u => u.Phone == person.Phone);
                if (user is not null)
                    person.OwnerUserId = user.Id;
            }

            if (db.ChangeTracker.HasChanges())
                db.SaveChanges();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("BackfillPersonOwners failed: " + ex.Message);
        }
    }

    private static void TryAlter(ArchiveDbContext db, string sql)
    {
        try { db.Database.ExecuteSqlRaw(sql); }
        catch { /* column may already exist or provider syntax differs */ }
    }

    private static void SeedIfEmpty(ArchiveDbContext db)
    {
        if (!db.People.Any())
        {
            var sample = new Person
            {
                RegistryCode = "01",
                HierarchyLevel = 1,
                DocumentType = DocumentTypes.NationalId,
                DocumentNumber = "0000000000",
                NationalId = "0000000000",
                FirstName = "سجل",
                FatherName = "تجريبي",
                GrandfatherName = "",
                FamilyName = "احذفه بعد البدء",
                FullName = "سجل تجريبي — احذفه بعد البدء",
                MotherName = "—",
                Nationality = "",
                Gender = "ذكر",
                BirthDate = DateTime.SpecifyKind(new DateTime(1990, 1, 1), DateTimeKind.Utc),
                BirthPlace = "الشكابة شاع الدين",
                Residence = "الشكابة شاع الدين",
                Tribe = "",
                Profession = "",
                Neighborhood = "—",
                Notes = "هذا سجل توضيحي فقط."
            };
            sample.RefreshFullName();
            db.People.Add(sample);
            db.SaveChanges();

            db.LifeEvents.Add(new LifeEvent
            {
                PersonId = sample.Id,
                Type = EventType.Birth,
                Mood = EventMood.Joy,
                EventDate = sample.BirthDate,
                Place = sample.BirthPlace,
                Title = "ميلاد",
                Details = "مناسبة ميلاد تجريبية.",
                SourceNote = "بيانات أولية"
            });
        }

        db.SaveChanges();
    }
}
