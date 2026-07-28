using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using ShakabaArchive.Data;
using ShakabaArchive.Models;

namespace ShakabaArchive.Services;

/// <summary>
/// ترميز سجل الأشخاص:
/// المستوى 1: 01، 02، ...
/// المستوى 2: 01001، 01002، ... (تحت أب من المستوى 1)
/// المستوى 3: 01001001، 01001002، ... (تحت أب من المستوى 2)
/// </summary>
public static class PersonRegistryService
{
    public const int MaxLevel = 3;
    public const int Level1Width = 2;
    public const int ChildSeqWidth = 3;
    private const string SecurityAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public static async Task EnsureSecurityCodeSchemaAsync(ArchiveDbContext db)
    {
        var isPostgres = db.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true;
        if (isPostgres)
        {
            TryAlter(db, """ALTER TABLE "People" ADD COLUMN IF NOT EXISTS "SecurityCode" character varying(16) NOT NULL DEFAULT ''""");
            TryAlter(db, """
                CREATE UNIQUE INDEX IF NOT EXISTS "IX_People_SecurityCode"
                ON "People" ("SecurityCode") WHERE "SecurityCode" <> ''
                """);
        }
        else
        {
            TryAlter(db, "ALTER TABLE People ADD COLUMN SecurityCode TEXT NOT NULL DEFAULT ''");
            TryAlter(db, "CREATE UNIQUE INDEX IF NOT EXISTS IX_People_SecurityCode ON People(SecurityCode) WHERE SecurityCode <> ''");
        }

        await BackfillMissingSecurityCodesAsync(db);
    }

    /// <summary>يضمن أن للشخص رمزاً فريداً — يولّد إن كان فارغاً.</summary>
    public static async Task EnsureSecurityCodeAsync(ArchiveDbContext db, Person person, CancellationToken ct = default)
    {
        if (person is null) return;
        if (!string.IsNullOrWhiteSpace(person.SecurityCode))
            return;

        person.SecurityCode = await AllocateSecurityCodeAsync(db, ct);
        person.UpdatedAt = DateTime.UtcNow;
    }

    public static async Task<string> AllocateSecurityCodeAsync(
        ArchiveDbContext db,
        CancellationToken ct = default)
    {
        for (var attempt = 0; attempt < 60; attempt++)
        {
            var code = GenerateSecurityCode();
            var exists = await db.People.AnyAsync(p => p.SecurityCode == code, ct);
            if (!exists)
                return code;
        }

        return GenerateSecurityCode() + RandomNumberGenerator.GetInt32(10, 99);
    }

    public static async Task<string> AllocateCodeAsync(
        ArchiveDbContext db,
        int level,
        int? parentPersonId,
        CancellationToken ct = default)
    {
        if (level is < 1 or > MaxLevel)
            throw new InvalidOperationException("المستوى يجب أن يكون 1 أو 2 أو 3.");

        if (level == 1)
        {
            if (parentPersonId is not null)
                throw new InvalidOperationException("المستوى الأول لا يرتبط بأب.");

            var roots = await db.People.AsNoTracking()
                .Where(p => p.HierarchyLevel == 1)
                .Select(p => p.RegistryCode)
                .ToListAsync(ct);

            var next = 1;
            foreach (var code in roots)
            {
                if (code.Length == Level1Width && int.TryParse(code, out var n) && n >= next)
                    next = n + 1;
            }

            if (next > 99)
                throw new InvalidOperationException("تم استنفاد أكواد المستوى الأول (01–99).");

            return next.ToString($"D{Level1Width}");
        }

        if (parentPersonId is not int parentId)
            throw new InvalidOperationException("اختر السجل الأب للمستوى " + level + ".");

        var parent = await db.People.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == parentId, ct)
            ?? throw new InvalidOperationException("السجل الأب غير موجود.");

        if (parent.HierarchyLevel != level - 1)
            throw new InvalidOperationException(
                $"الأب يجب أن يكون من المستوى {level - 1} (الكود: {parent.RegistryCode}).");

        var prefix = parent.RegistryCode;
        var children = await db.People.AsNoTracking()
            .Where(p => p.ParentPersonId == parentId && p.HierarchyLevel == level)
            .Select(p => p.RegistryCode)
            .ToListAsync(ct);

        var nextSeq = 1;
        var prefixLen = prefix.Length;
        foreach (var code in children)
        {
            if (code.Length == prefixLen + ChildSeqWidth
                && code.StartsWith(prefix, StringComparison.Ordinal)
                && int.TryParse(code.AsSpan(prefixLen), out var n)
                && n >= nextSeq)
            {
                nextSeq = n + 1;
            }
        }

        if (nextSeq > 999)
            throw new InvalidOperationException("تم استنفاد أكواد الأبناء تحت هذا الأب.");

        return prefix + nextSeq.ToString($"D{ChildSeqWidth}");
    }

    public static string LevelLabel(int level) => level switch
    {
        1 => "المستوى الأول",
        2 => "المستوى الثاني",
        3 => "المستوى الثالث",
        _ => "مستوى " + level
    };

    private static async Task BackfillMissingSecurityCodesAsync(ArchiveDbContext db)
    {
        try
        {
            var missing = await db.People
                .Where(p => p.SecurityCode == null || p.SecurityCode == "")
                .OrderBy(p => p.Id)
                .Take(500)
                .ToListAsync();

            while (missing.Count > 0)
            {
                foreach (var person in missing)
                    person.SecurityCode = await AllocateSecurityCodeAsync(db);

                await db.SaveChangesAsync();
                missing = await db.People
                    .Where(p => p.SecurityCode == null || p.SecurityCode == "")
                    .OrderBy(p => p.Id)
                    .Take(500)
                    .ToListAsync();
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Backfill person security codes: " + ex.Message);
        }
    }

    private static string GenerateSecurityCode()
    {
        // 10 أحرف — رمز فردي فريد لا يتكرر
        Span<char> chars = stackalloc char[10];
        for (var i = 0; i < chars.Length; i++)
            chars[i] = SecurityAlphabet[RandomNumberGenerator.GetInt32(SecurityAlphabet.Length)];
        return new string(chars);
    }

    private static void TryAlter(ArchiveDbContext db, string sql)
    {
        try { db.Database.ExecuteSqlRaw(sql); }
        catch { /* already exists */ }
    }
}
