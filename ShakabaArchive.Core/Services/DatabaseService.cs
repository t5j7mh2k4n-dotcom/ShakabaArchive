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

        if (!CanQueryPeople(db))
        {
            EnsureTables(db);
        }
        else
        {
            UpgradeSchema(db);
        }

        SeedIfEmpty(db);
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

        var isPostgres = db.Database.IsNpgsql();
        try
        {
            if (isPostgres)
            {
                // Empty Neon DB: wipe public schema then create EF tables cleanly.
                db.Database.ExecuteSqlRaw(
                    """
                    DROP SCHEMA IF EXISTS public CASCADE;
                    CREATE SCHEMA public;
                    GRANT ALL ON SCHEMA public TO public;
                    GRANT ALL ON SCHEMA public TO neondb_owner;
                    """);
            }

            creator.CreateTables();
        }
        catch
        {
            if (isPostgres)
            {
                db.Database.ExecuteSqlRaw(
                    """
                    DROP SCHEMA IF EXISTS public CASCADE;
                    CREATE SCHEMA public;
                    GRANT ALL ON SCHEMA public TO public;
                    GRANT ALL ON SCHEMA public TO neondb_owner;
                    """);
                creator.CreateTables();
            }
            else
            {
                throw;
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
        try
        {
            db.Database.ExecuteSqlRaw("ALTER TABLE People ADD COLUMN Tribe TEXT NOT NULL DEFAULT ''");
        }
        catch { /* already exists */ }

        try
        {
            db.Database.ExecuteSqlRaw("ALTER TABLE People ADD COLUMN Neighborhood TEXT NOT NULL DEFAULT ''");
        }
        catch { /* already exists */ }

        try
        {
            db.Database.ExecuteSqlRaw("ALTER TABLE People ADD COLUMN DocumentImagePath TEXT NOT NULL DEFAULT ''");
        }
        catch { /* already exists */ }
    }

    private static void SeedIfEmpty(ArchiveDbContext db)
    {
        if (!db.People.Any())
        {
            var sample = new Person
            {
                NationalId = "0000000000",
                FullName = "سجل تجريبي — احذفه بعد البدء",
                FatherName = "—",
                MotherName = "—",
                Nationality = "سوداني",
                Gender = "ذكر",
                BirthDate = DateTime.SpecifyKind(new DateTime(1990, 1, 1), DateTimeKind.Utc),
                BirthPlace = "الشكابة شاع الدين",
                Residence = "الشكابة شاع الدين",
                Tribe = "—",
                Neighborhood = "—",
                Notes = "هذا سجل توضيحي فقط."
            };
            db.People.Add(sample);
            db.SaveChanges();

            db.LifeEvents.Add(new LifeEvent
            {
                PersonId = sample.Id,
                Type = EventType.Birth,
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
