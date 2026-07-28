using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using ShakabaArchive.Data;
using ShakabaArchive.Models;

namespace ShakabaArchive.Services;

/// <summary>سجل الأسرة الخاص → تصدير إلى السجل العام.</summary>
public static class FamilyRegistryService
{
    private const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

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
                        "SecurityCode" character varying(16) NOT NULL DEFAULT '',
                        "CreatedAt" timestamp with time zone NOT NULL DEFAULT NOW(),
                        "UpdatedAt" timestamp with time zone NOT NULL DEFAULT NOW()
                    );
                    CREATE UNIQUE INDEX IF NOT EXISTS "IX_Families_OwnerUserId" ON "Families" ("OwnerUserId");
                    """);
                await ddl.Database.ExecuteSqlRawAsync("""
                    ALTER TABLE "Families" ADD COLUMN IF NOT EXISTS "SecurityCode" character varying(16) NOT NULL DEFAULT '';
                    """);
                await ddl.Database.ExecuteSqlRawAsync("""
                    CREATE UNIQUE INDEX IF NOT EXISTS "IX_Families_SecurityCode"
                    ON "Families" ("SecurityCode") WHERE "SecurityCode" <> '';
                    """);
            }
            else
            {
                await db.Database.ExecuteSqlRawAsync("""
                    CREATE TABLE IF NOT EXISTS Families (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Name TEXT NOT NULL DEFAULT '',
                        OwnerUserId INTEGER NOT NULL,
                        SecurityCode TEXT NOT NULL DEFAULT '',
                        CreatedAt TEXT NOT NULL,
                        UpdatedAt TEXT NOT NULL
                    );
                    CREATE UNIQUE INDEX IF NOT EXISTS IX_Families_OwnerUserId ON Families(OwnerUserId);
                    """);
                TryAlter(db, "ALTER TABLE Families ADD COLUMN SecurityCode TEXT NOT NULL DEFAULT ''");
                TryAlter(db, "CREATE UNIQUE INDEX IF NOT EXISTS IX_Families_SecurityCode ON Families(SecurityCode)");
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

        await BackfillMissingSecurityCodesAsync(db);
    }

    public static async Task<Family> GetOrCreateAsync(ArchiveDbContext db, AppUser user)
    {
        await EnsureSchemaAsync(db);

        var family = await db.Families.FirstOrDefaultAsync(f => f.OwnerUserId == user.Id);
        if (family is not null)
        {
            if (string.IsNullOrWhiteSpace(family.SecurityCode))
            {
                family.SecurityCode = await AllocateUniqueSecurityCodeAsync(db);
                family.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync();
            }
            return family;
        }

        family = new Family
        {
            Name = string.IsNullOrWhiteSpace(user.DisplayName)
                ? "أسرتي"
                : $"أسرة {user.DisplayName.Trim()}",
            OwnerUserId = user.Id,
            SecurityCode = await AllocateUniqueSecurityCodeAsync(db),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.Families.Add(family);
        await db.SaveChangesAsync();
        return family;
    }

    public static bool VerifySecurityCode(Family family, string? enteredCode)
    {
        if (family is null || string.IsNullOrWhiteSpace(family.SecurityCode))
            return false;

        var entered = NormalizeSecurityCode(enteredCode);
        var expected = NormalizeSecurityCode(family.SecurityCode);
        return entered.Length > 0
               && string.Equals(entered, expected, StringComparison.Ordinal);
    }

    public static string NormalizeSecurityCode(string? code) =>
        (code ?? "").Trim().ToUpperInvariant().Replace(" ", "").Replace("-", "");

    /// <summary>يحدّث رمز أمان الأسرة (للأدمن) — الرمز يبقى فريداً.</summary>
    public static async Task<(bool Ok, string Error)> SetSecurityCodeAsync(
        ArchiveDbContext db,
        int familyId,
        string? newCode)
    {
        await EnsureSchemaAsync(db);
        var code = NormalizeSecurityCode(newCode);
        if (code.Length < 4)
            return (false, "رمز الأمان يجب أن يكون 4 أحرف على الأقل.");
        if (code.Length > 16)
            return (false, "رمز الأمان طويل جداً (16 حرفاً كحد أقصى).");

        var clash = await db.Families.AnyAsync(f => f.Id != familyId && f.SecurityCode == code);
        if (clash)
            return (false, "هذا الرمز مستخدم لأسرة أخرى. اختر رمزاً مختلفاً.");

        var family = await db.Families.FirstOrDefaultAsync(f => f.Id == familyId);
        if (family is null)
            return (false, "سجل الأسرة غير موجود.");

        family.SecurityCode = code;
        family.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return (true, "");
    }

    /// <summary>يضمن وجود أسرة للشخص (أو لمالكه) حتى يتمكن الأدمن من تعيين رمز أمان.</summary>
    public static async Task<(Family? Family, string Error)> EnsureFamilyForPersonAsync(
        ArchiveDbContext db,
        Person person,
        string? ownerDisplayName = null)
    {
        await EnsureSchemaAsync(db);

        if (person.FamilyId is int existingId)
        {
            var existing = await db.Families.FirstOrDefaultAsync(f => f.Id == existingId);
            if (existing is not null)
            {
                if (string.IsNullOrWhiteSpace(existing.SecurityCode))
                {
                    existing.SecurityCode = await AllocateUniqueSecurityCodeAsync(db);
                    existing.UpdatedAt = DateTime.UtcNow;
                    await db.SaveChangesAsync();
                }
                return (existing, "");
            }
        }

        if (person.OwnerUserId is not int ownerId || ownerId <= 0)
            return (null, "هذا الشخص غير مرتبط بمستخدم مسجّل. عيّن رمز الأمان من صفحة المستخدمين بعد ربط المالك.");

        var byOwner = await db.Families.FirstOrDefaultAsync(f => f.OwnerUserId == ownerId);
        if (byOwner is not null)
        {
            if (string.IsNullOrWhiteSpace(byOwner.SecurityCode))
            {
                byOwner.SecurityCode = await AllocateUniqueSecurityCodeAsync(db);
                byOwner.UpdatedAt = DateTime.UtcNow;
            }

            person.FamilyId = byOwner.Id;
            await db.SaveChangesAsync();
            return (byOwner, "");
        }

        var ownerName = string.IsNullOrWhiteSpace(ownerDisplayName)
            ? "أسرة"
            : $"أسرة {ownerDisplayName.Trim()}";
        var created = new Family
        {
            Name = ownerName,
            OwnerUserId = ownerId,
            SecurityCode = await AllocateUniqueSecurityCodeAsync(db),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.Families.Add(created);
        await db.SaveChangesAsync();
        person.FamilyId = created.Id;
        await db.SaveChangesAsync();
        return (created, "");
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

        if (!isAdmin && !person.IsInGeneralRegistry)
            return (false, "لا يمكن نقل هذا الفرد إلا من السجل العام.");

        person.FamilyId = familyId;
        if (person.OwnerUserId is null || isAdmin)
            person.OwnerUserId = user.Id;
        // التحويل من السجل يحفظ مباشرة في الأسرة والسجل العام — بلا موافقة
        person.IsInGeneralRegistry = true;
        await PersonRegistryService.EnsureSecurityCodeAsync(db, person);
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

    private static async Task BackfillMissingSecurityCodesAsync(ArchiveDbContext db)
    {
        try
        {
            var missing = await db.Families
                .Where(f => f.SecurityCode == null || f.SecurityCode == "")
                .ToListAsync();
            if (missing.Count == 0)
                return;

            foreach (var family in missing)
                family.SecurityCode = await AllocateUniqueSecurityCodeAsync(db);

            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Backfill family security codes: " + ex.Message);
        }
    }

    private static async Task<string> AllocateUniqueSecurityCodeAsync(ArchiveDbContext db)
    {
        for (var attempt = 0; attempt < 40; attempt++)
        {
            var code = GenerateSecurityCode();
            var exists = await db.Families.AnyAsync(f => f.SecurityCode == code);
            if (!exists)
                return code;
        }

        return GenerateSecurityCode() + RandomNumberGenerator.GetInt32(10, 99);
    }

    private static string GenerateSecurityCode()
    {
        Span<char> chars = stackalloc char[8];
        for (var i = 0; i < chars.Length; i++)
            chars[i] = CodeAlphabet[RandomNumberGenerator.GetInt32(CodeAlphabet.Length)];
        return new string(chars);
    }

    private static void TryAlter(ArchiveDbContext db, string sql)
    {
        try { db.Database.ExecuteSqlRaw(sql); }
        catch { /* already exists */ }
    }
}
