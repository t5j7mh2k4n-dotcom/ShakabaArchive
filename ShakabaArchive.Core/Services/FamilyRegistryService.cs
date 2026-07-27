using Microsoft.EntityFrameworkCore;
using ShakabaArchive.Data;
using ShakabaArchive.Models;

namespace ShakabaArchive.Services;

/// <summary>سجل الأسرة الخاص → تصدير إلى السجل العام.</summary>
public static class FamilyRegistryService
{
    public static async Task EnsureSchemaAsync(ArchiveDbContext db)
    {
        var isPostgres = db.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true;
        try
        {
            if (isPostgres)
            {
                using var ddl = DatabaseService.CreateContextForSchemaChanges();
                await ddl.Database.ExecuteSqlRawAsync("""
                    CREATE TABLE IF NOT EXISTS "Families" (
                        "Id" serial PRIMARY KEY,
                        "Name" character varying(160) NOT NULL DEFAULT '',
                        "OwnerUserId" integer NOT NULL,
                        "CreatedAt" timestamp with time zone NOT NULL DEFAULT NOW(),
                        "UpdatedAt" timestamp with time zone NOT NULL DEFAULT NOW()
                    );
                    CREATE UNIQUE INDEX IF NOT EXISTS "IX_Families_OwnerUserId" ON "Families" ("OwnerUserId");
                    """);
            }
            else
            {
                await db.Database.ExecuteSqlRawAsync("""
                    CREATE TABLE IF NOT EXISTS Families (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Name TEXT NOT NULL DEFAULT '',
                        OwnerUserId INTEGER NOT NULL,
                        CreatedAt TEXT NOT NULL,
                        UpdatedAt TEXT NOT NULL
                    );
                    CREATE UNIQUE INDEX IF NOT EXISTS IX_Families_OwnerUserId ON Families(OwnerUserId);
                    """);
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("EnsureFamiliesTable: " + ex.Message);
        }

        TryAlter(db, "ALTER TABLE People ADD COLUMN FamilyId INTEGER NULL");
        TryAlter(db, "ALTER TABLE People ADD COLUMN IsInGeneralRegistry INTEGER NOT NULL DEFAULT 1");
        TryAlter(db, """ALTER TABLE "People" ADD COLUMN IF NOT EXISTS "FamilyId" integer NULL""");
        TryAlter(db, """ALTER TABLE "People" ADD COLUMN IF NOT EXISTS "IsInGeneralRegistry" boolean NOT NULL DEFAULT true""");
    }

    public static async Task<Family> GetOrCreateAsync(ArchiveDbContext db, AppUser user)
    {
        await EnsureSchemaAsync(db);

        var family = await db.Families.FirstOrDefaultAsync(f => f.OwnerUserId == user.Id);
        if (family is not null)
            return family;

        family = new Family
        {
            Name = string.IsNullOrWhiteSpace(user.DisplayName)
                ? "أسرتي"
                : $"أسرة {user.DisplayName.Trim()}",
            OwnerUserId = user.Id,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.Families.Add(family);
        await db.SaveChangesAsync();
        return family;
    }

    public static IQueryable<Person> MembersQuery(ArchiveDbContext db, int familyId) =>
        db.People.Where(p => p.FamilyId == familyId);

    public static async Task<(int Exported, string Message)> ExportToGeneralAsync(
        ArchiveDbContext db,
        int familyId)
    {
        var pending = await db.People
            .Where(p => p.FamilyId == familyId && !p.IsInGeneralRegistry)
            .ToListAsync();

        if (pending.Count == 0)
            return (0, "لا توجد أفراد جدد بانتظار التصدير — كل أفراد الأسرة في السجل العام مسبقاً.");

        foreach (var p in pending)
        {
            p.IsInGeneralRegistry = true;
            p.UpdatedAt = DateTime.UtcNow;
        }

        var family = await db.Families.FirstOrDefaultAsync(f => f.Id == familyId);
        if (family is not null)
            family.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();
        return (pending.Count, $"تم تصدير {pending.Count} فرداً إلى السجل العام للشكابة شاع الدين.");
    }

    private static void TryAlter(ArchiveDbContext db, string sql)
    {
        try { db.Database.ExecuteSqlRaw(sql); }
        catch { /* already exists */ }
    }
}
