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

    public static async Task<(bool Ok, string Message)> AttachToFamilyAsync(
        ArchiveDbContext db,
        int familyId,
        int personId,
        AppUser user,
        bool isAdmin)
    {
        var person = await db.People.FirstOrDefaultAsync(p => p.Id == personId);
        if (person is null)
            return (false, "السجل غير موجود.");

        if (person.FamilyId == familyId)
            return (false, "هذا الفرد موجود مسبقاً في سجل أسرتك.");

        if (person.FamilyId is int otherFamily && otherFamily != familyId && !isAdmin)
            return (false, "هذا الفرد مرتبط بسجل أسرة آخر. راجع الأدمن إن لزم.");

        person.FamilyId = familyId;
        if (person.OwnerUserId is null || isAdmin)
            person.OwnerUserId = user.Id;
        // التحويل من السجل يحفظ مباشرة في الأسرة والسجل العام — بلا موافقة
        person.IsInGeneralRegistry = true;
        person.UpdatedAt = DateTime.UtcNow;

        var family = await db.Families.FirstOrDefaultAsync(f => f.Id == familyId);
        if (family is not null)
            family.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();
        return (true, $"تم تحويل «{person.FullName}» إلى سجل أسرتك وحُفظ مباشرة.");
    }

    public static async Task<(int Exported, string Message)> ExportToGeneralAsync(
        ArchiveDbContext db,
        int familyId)
    {
        var pending = await db.People
            .Where(p => p.FamilyId == familyId && !p.IsInGeneralRegistry)
            .ToListAsync();

        if (pending.Count == 0)
            return (0, "كل أفراد الأسرة محفوظون في السجل العام مسبقاً.");

        foreach (var p in pending)
        {
            p.IsInGeneralRegistry = true;
            p.UpdatedAt = DateTime.UtcNow;
        }

        var family = await db.Families.FirstOrDefaultAsync(f => f.Id == familyId);
        if (family is not null)
            family.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();
        return (pending.Count, $"تم حفظ {pending.Count} فرداً في السجل العام مباشرة (بدون موافقة).");
    }

    private static void TryAlter(ArchiveDbContext db, string sql)
    {
        try { db.Database.ExecuteSqlRaw(sql); }
        catch { /* already exists */ }
    }
}
