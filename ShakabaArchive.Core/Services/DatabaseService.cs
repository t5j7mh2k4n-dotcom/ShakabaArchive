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

    public static void Initialize()
    {
        // Allow Unspecified DateTime values (birth/event dates) with PostgreSQL.
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

        EnsureSettingsFile();
        using var db = CreateContext();

        // محاولات قصيرة فقط — الطبقة المجانية على Render محدودة الذاكرة والوقت
        var ready = WaitForDatabase(db, attempts: 3, delayMs: 1000);
        if (!ready || !CanQueryPeople(db))
            EnsureTables(db);
        else
            UpgradeSchema(db);

        if (CanQueryPeople(db))
        {
            RemoveRetiredOccasionTypes(db);
            SeedIfEmpty(db);
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

        var name = $"{Guid.NewGuid():N}{ext.ToLowerInvariant()}";
        var full = Path.Combine(UploadsFolder, name);
        using (var fs = File.Create(full))
            content.CopyTo(fs);
        return name;
    }

    public static string NormalizeConnectionString(string value) => NormalizePostgresUrl(value);

    private static string NormalizePostgresUrl(string value)
    {
        // Render/Heroku style: postgres://user:pass@host/db
        if (value.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            var uri = new Uri(value);
            var userInfo = uri.UserInfo.Split(':', 2);
            var user = Uri.UnescapeDataString(userInfo[0]);
            var pass = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : "";
            var db = uri.AbsolutePath.Trim('/');
            return $"Host={uri.Host};Port={(uri.Port > 0 ? uri.Port : 5432)};Database={db};Username={user};Password={pass};SSL Mode=Require;Trust Server Certificate=true";
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

        BackfillPersonRegistryFields(db);

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
