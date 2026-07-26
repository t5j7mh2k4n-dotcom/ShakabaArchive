using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ShakabaArchive.Data;
using ShakabaArchive.Models;

namespace ShakabaArchive.Services;

public static class ApprovalService
{
    public const int MaxApprovers = 3;

    /// <summary>الحد الأقصى لإجمالي حسابات المستخدمين (بما فيهم الأدمن) — مرتفع للتسجيل العام.</summary>
    public const int MaxUsers = 500;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public static async Task EnsureSchemaAsync(ArchiveDbContext db)
    {
        try
        {
            _ = await db.PendingChanges.AsNoTracking().Select(x => x.Id).Take(1).ToListAsync();
            return;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("PendingChanges query failed, creating if missing: " + ex.Message);
        }

        var isPostgres = db.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true;
        if (isPostgres)
        {
            // لا نحذف الجدول أبداً — حتى لا تُمسح طلبات الموافقة عند خطأ مؤقت
            await using var ddl = DatabaseService.CreateContextForSchemaChanges();
            await ddl.Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE IF NOT EXISTS "PendingChanges" (
                  "Id" SERIAL PRIMARY KEY,
                  "EntityType" integer NOT NULL,
                  "Action" integer NOT NULL,
                  "EntityId" integer NULL,
                  "PayloadJson" text NOT NULL DEFAULT '{{}}',
                  "Summary" varchar(400) NOT NULL DEFAULT '',
                  "Status" integer NOT NULL DEFAULT 0,
                  "SubmittedByUserId" integer NOT NULL,
                  "SubmittedByName" varchar(120) NOT NULL DEFAULT '',
                  "ReviewedByUserId" integer NULL,
                  "ReviewedByName" varchar(120) NULL,
                  "ReviewNote" varchar(400) NOT NULL DEFAULT '',
                  "SubmittedAt" timestamp with time zone NOT NULL DEFAULT NOW(),
                  "ReviewedAt" timestamp with time zone NULL
                );
                """);
            Console.WriteLine("PendingChanges ensured (CREATE IF NOT EXISTS).");
            return;
        }

        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS PendingChanges (
              Id INTEGER PRIMARY KEY AUTOINCREMENT,
              EntityType INTEGER NOT NULL,
              Action INTEGER NOT NULL,
              EntityId INTEGER NULL,
              PayloadJson TEXT NOT NULL DEFAULT '{{}}',
              Summary TEXT NOT NULL DEFAULT '',
              Status INTEGER NOT NULL DEFAULT 0,
              SubmittedByUserId INTEGER NOT NULL,
              SubmittedByName TEXT NOT NULL DEFAULT '',
              ReviewedByUserId INTEGER NULL,
              ReviewedByName TEXT NULL,
              ReviewNote TEXT NOT NULL DEFAULT '',
              SubmittedAt TEXT NOT NULL,
              ReviewedAt TEXT NULL
            );
            """);
    }

    /// <summary>
    /// يرسل تغييراً للموافقة. الأدمن الرئيسي والثلاثة الموافقون يُحفظ لهم مباشرة؛
    /// مدخلو البيانات ينتظرون موافقة أحد الثلاثة.
    /// </summary>
    public static async Task<(PendingChange Item, bool AppliedImmediately)> SubmitAsync(
        ArchiveDbContext db,
        AppUser user,
        ChangeEntity entityType,
        ChangeAction action,
        int? entityId,
        object payload,
        string summary)
    {
        DatabaseService.EnsureReady();
        await EnsureSchemaAsync(db);

        var item = new PendingChange
        {
            EntityType = entityType,
            Action = action,
            EntityId = entityId,
            PayloadJson = JsonSerializer.Serialize(payload, JsonOptions),
            Summary = summary.Length > 400 ? summary[..400] : summary,
            Status = ChangeStatus.Pending,
            SubmittedByUserId = user.Id,
            SubmittedByName = user.DisplayName,
            SubmittedAt = DateTime.UtcNow
        };
        db.PendingChanges.Add(item);
        await db.SaveChangesAsync();

        // المدخل (Editor) ينتظر دائماً — الأدمن/الموافق يحفظون مباشرة
        var autoApply = user.Role != UserRole.Editor && user.CanApprove;

        // الأدمن والموافقون: حفظ فوري في الأرشيف
        if (autoApply)
        {
            try
            {
                await ApplyAsync(db, item);
            }
            catch (Exception ex)
            {
                item.ReviewNote = "فشل الحفظ المباشر: " + ex.Message;
                await db.SaveChangesAsync();
                throw new InvalidOperationException(
                    "تعذر حفظ البيانات في الأرشيف: " + ex.Message, ex);
            }

            item.Status = ChangeStatus.Approved;
            item.ReviewedByUserId = user.Id;
            item.ReviewedByName = user.DisplayName;
            item.ReviewNote = "حفظ مباشر بصلاحية الموافقة";
            item.ReviewedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            return (item, true);
        }

        return (item, false);
    }

    /// <summary>تعديل طلب إضافة/تعديل شخص ما دام بانتظار الاعتماد.</summary>
    public static async Task<(bool Ok, string Error)> UpdatePendingPersonAsync(
        ArchiveDbContext db,
        AppUser user,
        int pendingId,
        PersonDraft draft)
    {
        var item = await db.PendingChanges.FirstOrDefaultAsync(x => x.Id == pendingId);
        if (item is null)
            return (false, "الطلب غير موجود.");
        if (item.Status != ChangeStatus.Pending)
            return (false, "لا يمكن التعديل بعد الاعتماد أو الرفض.");
        if (item.EntityType != ChangeEntity.Person)
            return (false, "هذا الطلب ليس لسجل أشخاص.");
        if (item.Action is not (ChangeAction.Create or ChangeAction.Update))
            return (false, "لا يمكن تعديل طلب الحذف.");
        if (item.SubmittedByUserId != user.Id && !user.CanApprove)
            return (false, "يمكنك تعديل طلبك فقط قبل الاعتماد.");

        draft.NormalizeDocument();
        // الحفاظ على الترميز والمستوى من المسودة السابقة إن وُجدت
        try
        {
            var previous = JsonSerializer.Deserialize<PersonDraft>(item.PayloadJson, JsonOptions);
            if (previous is not null)
            {
                if (string.IsNullOrWhiteSpace(draft.RegistryCode))
                    draft.RegistryCode = previous.RegistryCode;
                if (draft.HierarchyLevel is < 1 or > 3)
                    draft.HierarchyLevel = previous.HierarchyLevel;
                draft.ParentPersonId ??= previous.ParentPersonId;
                if (string.IsNullOrWhiteSpace(draft.PhotoPath))
                    draft.PhotoPath = previous.PhotoPath;
                if (string.IsNullOrWhiteSpace(draft.DocumentImagePath))
                    draft.DocumentImagePath = previous.DocumentImagePath;
            }
        }
        catch { /* ignore */ }

        item.PayloadJson = JsonSerializer.Serialize(draft, JsonOptions);
        item.Summary = item.Action == ChangeAction.Create
            ? $"إضافة سجل أشخاص {draft.RegistryCode}: {draft.FullName}"
            : $"تعديل سجل أشخاص {draft.RegistryCode}: {draft.FullName}";
        if (item.Summary.Length > 400)
            item.Summary = item.Summary[..400];
        item.SubmittedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return (true, "");
    }

    public static async Task<(bool Ok, string Error, PersonDraft? Draft)> GetPendingPersonDraftAsync(
        ArchiveDbContext db,
        AppUser user,
        int pendingId)
    {
        var item = await db.PendingChanges.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == pendingId);
        if (item is null)
            return (false, "الطلب غير موجود.", null);
        if (item.Status != ChangeStatus.Pending)
            return (false, "الطلب لم يعد بانتظار الاعتماد.", null);
        if (item.EntityType != ChangeEntity.Person || item.Action is ChangeAction.Delete)
            return (false, "لا يمكن تعديل هذا النوع من الطلبات.", null);
        if (item.SubmittedByUserId != user.Id && !user.CanApprove)
            return (false, "يمكنك تعديل طلبك فقط.", null);

        var draft = JsonSerializer.Deserialize<PersonDraft>(item.PayloadJson, JsonOptions);
        return draft is null
            ? (false, "بيانات الطلب غير صالحة.", null)
            : (true, "", draft);
    }

    public static async Task<(bool Ok, string Error, int? CreatedPersonId)> ApproveAsync(
        ArchiveDbContext db,
        AppUser reviewer,
        int pendingId,
        string? note = null)
    {
        if (!reviewer.CanApprove)
            return (false, "ليست لديك صلاحية الموافقة على صحة البيانات.", null);

        var item = await db.PendingChanges.FirstOrDefaultAsync(x => x.Id == pendingId);
        if (item is null)
            return (false, "الطلب غير موجود.", null);
        if (item.Status != ChangeStatus.Pending)
            return (false, "تمت مراجعة هذا الطلب مسبقاً.", null);
        if (item.SubmittedByUserId == reviewer.Id && !reviewer.IsAdmin)
            return (false, "لا يمكن اعتماد طلبك بنفسك — يوافق أحد الثلاثة الآخرين أو الأدمن الرئيسي.", null);

        try
        {
            await ApplyAsync(db, item);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Approve Apply failed: " + ex);
            return (false, "تعذر حفظ البيانات في السجل: " + ex.GetBaseException().Message, null);
        }

        item.Status = ChangeStatus.Approved;
        item.ReviewedByUserId = reviewer.Id;
        item.ReviewedByName = reviewer.DisplayName;
        item.ReviewNote = note?.Trim() ?? "";
        item.ReviewedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();

        int? createdPersonId = item.EntityType == ChangeEntity.Person
                               && item.Action == ChangeAction.Create
                               && item.EntityId is int pid
            ? pid
            : null;

        return (true, "", createdPersonId);
    }

    public static async Task<(bool Ok, string Error)> RejectAsync(
        ArchiveDbContext db,
        AppUser reviewer,
        int pendingId,
        string? note = null)
    {
        if (!reviewer.CanApprove)
            return (false, "ليست لديك صلاحية الرفض.");

        var item = await db.PendingChanges.FirstOrDefaultAsync(x => x.Id == pendingId);
        if (item is null)
            return (false, "الطلب غير موجود.");
        if (item.Status != ChangeStatus.Pending)
            return (false, "تمت مراجعة هذا الطلب مسبقاً.");

        // رفض تسجيل حساب = حذف الحساب من النظام
        if (item.EntityType == ChangeEntity.UserAccount && item.EntityId is int userId)
        {
            var account = await db.Users.FirstOrDefaultAsync(u => u.Id == userId);
            if (account is not null && !account.IsAdmin && account.Role != UserRole.Admin)
            {
                db.Users.Remove(account);
                await db.SaveChangesAsync();
            }
        }

        item.Status = ChangeStatus.Rejected;
        item.ReviewedByUserId = reviewer.Id;
        item.ReviewedByName = reviewer.DisplayName;
        item.ReviewNote = note?.Trim() ?? "مرفوض";
        item.ReviewedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return (true, "");
    }

    /// <summary>ينشئ طلب موافقة لتسجيل حساب عام، ويستكمل الطلبات الناقصة للحسابات الحالية.</summary>
    public static async Task EnsureUserRegistrationPendingsAsync(ArchiveDbContext db)
    {
        await EnsureSchemaAsync(db);

        var publicUsers = await db.Users.AsNoTracking()
            .Where(u => u.InviteCodeUsed == "PUBLIC" || u.InviteCodeUsed == "PUBLIC-OK")
            .Select(u => new { u.Id, u.DisplayName, u.Email, u.Phone, u.InviteCodeUsed, u.CreatedAt })
            .ToListAsync();

        foreach (var u in publicUsers)
        {
            var exists = await db.PendingChanges.AnyAsync(p =>
                p.EntityType == ChangeEntity.UserAccount
                && p.EntityId == u.Id
                && p.Action == ChangeAction.Create);

            if (exists)
                continue;

            var status = u.InviteCodeUsed == "PUBLIC-OK"
                ? ChangeStatus.Approved
                : ChangeStatus.Pending;

            db.PendingChanges.Add(new PendingChange
            {
                EntityType = ChangeEntity.UserAccount,
                Action = ChangeAction.Create,
                EntityId = u.Id,
                PayloadJson = JsonSerializer.Serialize(new
                {
                    u.Email,
                    u.Phone,
                    u.DisplayName
                }, JsonOptions),
                Summary = $"تسجيل حساب جديد: {u.DisplayName} — {u.Email}",
                Status = status,
                SubmittedByUserId = u.Id,
                SubmittedByName = u.DisplayName,
                SubmittedAt = u.CreatedAt.Kind == DateTimeKind.Unspecified
                    ? DateTime.SpecifyKind(u.CreatedAt, DateTimeKind.Utc)
                    : u.CreatedAt.ToUniversalTime(),
                ReviewedAt = status == ChangeStatus.Approved ? DateTime.UtcNow : null,
                ReviewNote = status == ChangeStatus.Approved ? "اعتماد تلقائي لحساب سابق" : ""
            });
        }

        if (db.ChangeTracker.HasChanges())
            await db.SaveChangesAsync();
    }

    public static async Task SubmitUserRegistrationAsync(ArchiveDbContext db, AppUser user)
    {
        await EnsureSchemaAsync(db);

        var exists = await db.PendingChanges.AnyAsync(p =>
            p.EntityType == ChangeEntity.UserAccount
            && p.EntityId == user.Id
            && p.Action == ChangeAction.Create
            && p.Status == ChangeStatus.Pending);

        if (exists)
            return;

        db.PendingChanges.Add(new PendingChange
        {
            EntityType = ChangeEntity.UserAccount,
            Action = ChangeAction.Create,
            EntityId = user.Id,
            PayloadJson = JsonSerializer.Serialize(new
            {
                user.Email,
                user.Phone,
                user.DisplayName
            }, JsonOptions),
            Summary = $"تسجيل حساب جديد: {user.DisplayName} — {user.Email}",
            Status = ChangeStatus.Pending,
            SubmittedByUserId = user.Id,
            SubmittedByName = user.DisplayName,
            SubmittedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private static async Task ApplyAsync(ArchiveDbContext db, PendingChange item)
    {
        switch (item.EntityType)
        {
            case ChangeEntity.Person:
                await ApplyPersonAsync(db, item);
                break;
            case ChangeEntity.LifeEvent:
                await ApplyLifeEventAsync(db, item);
                break;
            case ChangeEntity.UserAccount:
                if (item.EntityId is int uid)
                {
                    var account = await db.Users.FirstOrDefaultAsync(u => u.Id == uid);
                    if (account is not null && account.InviteCodeUsed == "PUBLIC")
                    {
                        account.InviteCodeUsed = "PUBLIC-OK";
                        await db.SaveChangesAsync();
                    }
                }
                break;
        }
    }

    private static async Task ApplyPersonAsync(ArchiveDbContext db, PendingChange item)
    {
        if (item.Action == ChangeAction.Delete)
        {
            if (item.EntityId is not int id) return;
            var person = await db.People.Include(p => p.Events).FirstOrDefaultAsync(p => p.Id == id);
            if (person is not null)
            {
                db.People.Remove(person);
                await db.SaveChangesAsync();
            }
            return;
        }

        var dto = DeserializePersonDraft(item.PayloadJson)
                  ?? throw new InvalidOperationException("بيانات الشخص غير صالحة.");

        if (item.Action == ChangeAction.Create)
        {
            // إن وُجد السجل مسبقاً (محاولة سابقة نجحت جزئياً) لا نكرّر الإضافة
            if (item.EntityId is int existingId)
            {
                var already = await db.People.AsNoTracking().AnyAsync(p => p.Id == existingId);
                if (already)
                    return;
            }

            var person = await BuildPersonForCreateAsync(db, dto, item.SubmittedByUserId);
            db.People.Add(person);
            await db.SaveChangesAsync();
            item.EntityId = person.Id;
            item.Summary = $"إضافة سجل أشخاص {person.RegistryCode}: {person.FullName}";
            return;
        }

        if (item.EntityId is not int updateId) return;
        var existing = await db.People.FindAsync(updateId)
                       ?? throw new InvalidOperationException("السجل غير موجود.");
        var keepCode = existing.RegistryCode;
        var keepLevel = existing.HierarchyLevel;
        var keepParent = existing.ParentPersonId;
        dto.ApplyTo(existing);
        // الترميز الهرمي لا يتغير عند التعديل
        existing.RegistryCode = keepCode;
        existing.HierarchyLevel = keepLevel;
        existing.ParentPersonId = keepParent;
        if (existing.BirthDate is { } ebd)
            existing.BirthDate = DateTime.SpecifyKind(ebd.Date, DateTimeKind.Utc);
        existing.RefreshFullName();
        existing.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
    }

    private static PersonDraft? DeserializePersonDraft(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json is "{}" or "{{}}")
            return null;
        try
        {
            return JsonSerializer.Deserialize<PersonDraft>(json, JsonOptions);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("DeserializePersonDraft: " + ex.Message);
            try
            {
                return JsonSerializer.Deserialize<PersonDraft>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>يبني مسودة من ملخص الطلب إن فسدت حمولة JSON.</summary>
    private static PersonDraft? DraftFromSummary(string? summary)
    {
        if (string.IsNullOrWhiteSpace(summary))
            return null;

        // أشكال شائعة: "إضافة سجل أشخاص 03: الاسم الكامل"
        var text = summary.Trim();
        var colon = text.IndexOf(':');
        if (colon < 0 || colon >= text.Length - 1)
            return null;

        var name = text[(colon + 1)..].Trim();
        if (name.Length < 2)
            return null;

        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return new PersonDraft
        {
            HierarchyLevel = 1,
            ParentPersonId = null,
            FirstName = parts.ElementAtOrDefault(0) ?? name,
            FatherName = parts.ElementAtOrDefault(1) ?? "",
            GrandfatherName = parts.Length >= 4 ? parts[2] : "",
            FamilyName = parts.Length >= 4
                ? string.Join(" ", parts.Skip(3))
                : parts.ElementAtOrDefault(2) ?? "",
            FullName = name,
            Gender = "ذكر",
            BirthPlace = "الشكابة شاع الدين",
            Residence = "الشكابة شاع الدين"
        };
    }

    public const string IncompleteProfileMarker = "[بيانات غير مكتملة]";

    private static bool IsIncompleteDraft(PersonDraft dto) =>
        string.IsNullOrWhiteSpace(dto.FatherName)
        || string.IsNullOrWhiteSpace(dto.FamilyName)
        || string.IsNullOrWhiteSpace(dto.DocumentNumber) && string.IsNullOrWhiteSpace(dto.NationalId)
        || string.IsNullOrWhiteSpace(dto.Phone);

    private static void MarkIncomplete(Person person, bool incomplete)
    {
        if (!incomplete)
            return;
        if (person.Notes.Contains(IncompleteProfileMarker, StringComparison.Ordinal))
            return;
        var note = $"{IncompleteProfileMarker} — يرجى إكمال البيانات عبر الرابط المرسل واتساب.";
        person.Notes = string.IsNullOrWhiteSpace(person.Notes) ? note : $"{note} {person.Notes}";
    }

    private static PersonDraft EnsureMinimalDraft(PersonDraft? dto, PendingChange item, AppUser? submitter)
    {
        dto ??= DraftFromSummary(item.Summary) ?? new PersonDraft();

        if (string.IsNullOrWhiteSpace(dto.FirstName) && string.IsNullOrWhiteSpace(dto.FullName))
        {
            var fromSummary = DraftFromSummary(item.Summary);
            if (fromSummary is not null)
            {
                dto.FirstName = fromSummary.FirstName;
                dto.FatherName = fromSummary.FatherName;
                dto.GrandfatherName = fromSummary.GrandfatherName;
                dto.FamilyName = fromSummary.FamilyName;
                dto.FullName = fromSummary.FullName;
            }
        }

        if (string.IsNullOrWhiteSpace(dto.FirstName) && string.IsNullOrWhiteSpace(dto.FullName))
        {
            var fallback = !string.IsNullOrWhiteSpace(item.SubmittedByName)
                ? item.SubmittedByName.Trim()
                : $"سجل مؤقت #{item.Id}";
            dto.FirstName = fallback;
            dto.FullName = fallback;
        }

        if (string.IsNullOrWhiteSpace(dto.Phone) && submitter is not null)
            dto.Phone = submitter.Phone ?? "";

        if (string.IsNullOrWhiteSpace(dto.Gender))
            dto.Gender = "ذكر";
        if (string.IsNullOrWhiteSpace(dto.BirthPlace))
            dto.BirthPlace = "الشكابة شاع الدين";
        if (string.IsNullOrWhiteSpace(dto.Residence))
            dto.Residence = "الشكابة شاع الدين";

        dto.HierarchyLevel = 1;
        dto.ParentPersonId = null;
        dto.NormalizeDocument();
        return dto;
    }

    private static async Task<Person> BuildPersonForCreateAsync(
        ArchiveDbContext db,
        PersonDraft dto,
        int? ownerUserId = null)
    {
        var person = dto.ToPerson();
        person.HierarchyLevel = 1;
        person.ParentPersonId = null;
        person.OwnerUserId = ownerUserId is > 0 ? ownerUserId : null;

        person.RegistryCode = await PersonRegistryService.AllocateCodeAsync(db, 1, null);

        if (person.BirthDate is { } bd)
            person.BirthDate = DateTime.SpecifyKind(bd.Date, DateTimeKind.Utc);

        person.CreatedAt = DateTime.UtcNow;
        person.UpdatedAt = DateTime.UtcNow;
        person.RefreshFullName();
        if (string.IsNullOrWhiteSpace(person.FullName) && !string.IsNullOrWhiteSpace(dto.FullName))
            person.FullName = dto.FullName.Trim();
        if (string.IsNullOrWhiteSpace(person.FullName))
            person.FullName = string.IsNullOrWhiteSpace(person.FirstName) ? "بدون اسم" : person.FirstName;
        if (string.IsNullOrWhiteSpace(person.FirstName))
            person.FirstName = person.FullName;

        MarkIncomplete(person, IsIncompleteDraft(dto));
        return person;
    }

    private static async Task<Person?> FindExactPersonMatchAsync(ArchiveDbContext db, PersonDraft dto)
    {
        var name = (dto.FullName ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(dto.FirstName))
            name = Person.ComposeFullName(dto.FirstName, dto.FatherName, dto.GrandfatherName, dto.FamilyName);

        var doc = (dto.DocumentNumber ?? dto.NationalId ?? "").Trim();
        var phone = (dto.Phone ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name) && string.IsNullOrWhiteSpace(phone) && string.IsNullOrWhiteSpace(doc))
            return null;

        if (!string.IsNullOrWhiteSpace(phone))
        {
            var byPhone = await db.People.AsNoTracking()
                .FirstOrDefaultAsync(p => p.Phone == phone);
            if (byPhone is not null)
                return byPhone;
        }

        if (string.IsNullOrWhiteSpace(name))
            return null;

        if (!string.IsNullOrWhiteSpace(doc))
        {
            return await db.People.AsNoTracking()
                .FirstOrDefaultAsync(p =>
                    p.FullName == name
                    && (p.DocumentNumber == doc || p.NationalId == doc));
        }

        return await db.People.AsNoTracking()
            .FirstOrDefaultAsync(p =>
                p.FullName == name
                && p.FirstName == dto.FirstName.Trim());
    }

    public sealed record MigrateReminder(int PersonId, string Name, string Phone, bool Incomplete);

    public sealed record MigrateResult(
        int Migrated,
        int Linked,
        int AlreadyInRegistry,
        int Failed,
        IReadOnlyList<string> Errors,
        IReadOnlyList<MigrateReminder> Reminders);

    /// <summary>عدد الطلبات المعتمدة التي لم تُحفظ بعد في سجل الأشخاص (شخص أو حساب).</summary>
    public static async Task<int> CountApprovedPersonsMissingFromRegistryAsync(ArchiveDbContext db)
    {
        await EnsureSchemaAsync(db);
        var approved = await db.PendingChanges.AsNoTracking()
            .Where(x => x.Status == ChangeStatus.Approved
                        && x.Action == ChangeAction.Create
                        && (x.EntityType == ChangeEntity.Person || x.EntityType == ChangeEntity.UserAccount))
            .Select(x => new { x.Id, x.EntityId, x.EntityType, x.PayloadJson, x.Summary, x.SubmittedByName })
            .ToListAsync();

        var missing = 0;
        foreach (var row in approved)
        {
            if (row.EntityType == ChangeEntity.Person
                && row.EntityId is int id
                && await db.People.AsNoTracking().AnyAsync(p => p.Id == id))
                continue;

            if (row.EntityType == ChangeEntity.UserAccount)
            {
                var phone = ExtractPhoneFromUserPayload(row.PayloadJson);
                if (!string.IsNullOrWhiteSpace(phone)
                    && await db.People.AsNoTracking().AnyAsync(p => p.Phone == phone))
                    continue;
            }

            missing++;
        }

        return missing;
    }

    private static string ExtractPhoneFromUserPayload(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
            if (doc.RootElement.TryGetProperty("phone", out var p))
                return p.GetString()?.Trim() ?? "";
            if (doc.RootElement.TryGetProperty("Phone", out var p2))
                return p2.GetString()?.Trim() ?? "";
        }
        catch { /* ignore */ }
        return "";
    }

    private static (string DisplayName, string Email, string Phone) ExtractUserPayload(string json, PendingChange item)
    {
        var name = item.SubmittedByName;
        var email = "";
        var phone = "";
        try
        {
            using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
            var root = doc.RootElement;
            if (root.TryGetProperty("displayName", out var dn) || root.TryGetProperty("DisplayName", out dn))
                name = dn.GetString() ?? name;
            if (root.TryGetProperty("email", out var em) || root.TryGetProperty("Email", out em))
                email = em.GetString() ?? "";
            if (root.TryGetProperty("phone", out var ph) || root.TryGetProperty("Phone", out ph))
                phone = ph.GetString() ?? "";
        }
        catch { /* ignore */ }

        if (string.IsNullOrWhiteSpace(name) && item.Summary.Contains(':'))
            name = item.Summary[(item.Summary.IndexOf(':') + 1)..].Split('—')[0].Trim();

        return (name.Trim(), email.Trim(), phone.Trim());
    }

    /// <summary>ترحيل المعتمدة إلى السجل حتى لو كانت البيانات ناقصة.</summary>
    public static async Task<MigrateResult> RepairApprovedPersonCreatesAsync(ArchiveDbContext db)
    {
        DatabaseService.EnsureReady();
        await EnsureSchemaAsync(db);

        var stuck = await db.PendingChanges
            .Where(x => x.Status == ChangeStatus.Approved
                        && x.Action == ChangeAction.Create
                        && (x.EntityType == ChangeEntity.Person || x.EntityType == ChangeEntity.UserAccount))
            .OrderBy(x => x.Id)
            .ToListAsync();

        var migrated = 0;
        var linked = 0;
        var already = 0;
        var failed = 0;
        var errors = new List<string>();
        var reminders = new List<MigrateReminder>();

        foreach (var item in stuck)
        {
            try
            {
                AppUser? submitter = null;
                try
                {
                    submitter = await db.Users.AsNoTracking()
                        .FirstOrDefaultAsync(u => u.Id == item.SubmittedByUserId);
                }
                catch { /* ignore */ }

                if (item.EntityType == ChangeEntity.UserAccount)
                {
                    var (ok, person, wasNew) = await MigrateUserAccountToPersonAsync(db, item, submitter);
                    if (!ok || person is null)
                    {
                        failed++;
                        errors.Add($"#{item.Id}: تعذر إنشاء سجل من الحساب.");
                        continue;
                    }

                    if (wasNew) migrated++;
                    else already++;

                    reminders.Add(new MigrateReminder(
                        person.Id,
                        person.FullName,
                        person.Phone,
                        person.Notes.Contains(IncompleteProfileMarker, StringComparison.Ordinal)));
                    continue;
                }

                if (item.EntityId is int id && await db.People.AnyAsync(p => p.Id == id))
                {
                    already++;
                    var existing = await db.People.AsNoTracking().FirstAsync(p => p.Id == id);
                    reminders.Add(new MigrateReminder(
                        existing.Id,
                        existing.FullName,
                        existing.Phone,
                        existing.Notes.Contains(IncompleteProfileMarker, StringComparison.Ordinal)));
                    continue;
                }

                item.EntityId = null;
                var dto = EnsureMinimalDraft(
                    DeserializePersonDraft(item.PayloadJson),
                    item,
                    submitter);

                var match = await FindExactPersonMatchAsync(db, dto);
                if (match is not null)
                {
                    item.EntityId = match.Id;
                    item.Summary = $"إضافة سجل أشخاص {match.RegistryCode}: {match.FullName}";
                    linked++;
                    reminders.Add(new MigrateReminder(
                        match.Id,
                        match.FullName,
                        string.IsNullOrWhiteSpace(match.Phone) ? dto.Phone : match.Phone,
                        true));
                    continue;
                }

                var personRow = await BuildPersonForCreateAsync(db, dto, item.SubmittedByUserId);
                db.People.Add(personRow);
                await db.SaveChangesAsync();
                item.EntityId = personRow.Id;
                item.Summary = $"إضافة سجل أشخاص {personRow.RegistryCode}: {personRow.FullName}";
                item.ReviewNote = IsIncompleteDraft(dto)
                    ? "تم الترحيل إلى السجل (بيانات غير مكتملة)"
                    : "تم الترحيل إلى السجل";
                migrated++;
                reminders.Add(new MigrateReminder(
                    personRow.Id,
                    personRow.FullName,
                    personRow.Phone,
                    IsIncompleteDraft(dto)));
            }
            catch (Exception ex)
            {
                failed++;
                var msg = ex.GetBaseException().Message;
                if (msg.Length > 120)
                    msg = msg[..120] + "…";
                errors.Add($"#{item.Id}: {msg}");
                Console.Error.WriteLine($"Repair pending #{item.Id}: {ex}");
            }
        }

        if (db.ChangeTracker.HasChanges())
            await db.SaveChangesAsync();

        return new MigrateResult(migrated, linked, already, failed, errors, reminders);
    }

    private static async Task<(bool Ok, Person? Person, bool WasNew)> MigrateUserAccountToPersonAsync(
        ArchiveDbContext db,
        PendingChange item,
        AppUser? submitter)
    {
        var (displayName, _, phone) = ExtractUserPayload(item.PayloadJson, item);
        if (string.IsNullOrWhiteSpace(phone) && submitter is not null)
            phone = submitter.Phone ?? "";
        if (string.IsNullOrWhiteSpace(displayName) && submitter is not null)
            displayName = submitter.DisplayName;

        if (string.IsNullOrWhiteSpace(displayName))
            displayName = $"مستخدم #{item.SubmittedByUserId}";

        if (!string.IsNullOrWhiteSpace(phone))
        {
            var existing = await db.People.FirstOrDefaultAsync(p => p.Phone == phone);
            if (existing is not null)
            {
                // اربط الطلب بسجل الشخص إن أمكن عبر ملاحظة فقط
                return (true, existing, false);
            }
        }

        var dto = EnsureMinimalDraft(new PersonDraft
        {
            FirstName = displayName.Split(' ', 2)[0],
            FatherName = displayName.Contains(' ') ? displayName[(displayName.IndexOf(' ') + 1)..] : "",
            FullName = displayName,
            Phone = phone,
            Notes = "أُنشئ من تسجيل حساب معتمد"
        }, item, submitter);

        var person = await BuildPersonForCreateAsync(db, dto, item.SubmittedByUserId);
        db.People.Add(person);
        await db.SaveChangesAsync();
        return (true, person, true);
    }

    /// <summary>ترحيل طلب واحد معتمد إلى السجل (شخص أو حساب).</summary>
    public static async Task<(bool Ok, string Error, int? PersonId, bool Incomplete)> MigrateOneApprovedPersonAsync(
        ArchiveDbContext db,
        int pendingId)
    {
        DatabaseService.EnsureReady();
        await EnsureSchemaAsync(db);

        var item = await db.PendingChanges.FirstOrDefaultAsync(x => x.Id == pendingId);
        if (item is null)
            return (false, "الطلب غير موجود.", null, false);
        if (item.Status == ChangeStatus.Rejected)
            return (false, "لا يمكن ترحيل طلب مرفوض.", null, false);

        AppUser? submitter = null;
        try
        {
            submitter = await db.Users.AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == item.SubmittedByUserId);
        }
        catch { /* ignore */ }

        if (item.EntityType == ChangeEntity.UserAccount)
        {
            var (ok, person, _) = await MigrateUserAccountToPersonAsync(db, item, submitter);
            if (!ok || person is null)
                return (false, "تعذر إنشاء السجل من الحساب.", null, false);
            return (true, "", person.Id, person.Notes.Contains(IncompleteProfileMarker, StringComparison.Ordinal));
        }

        if (item.EntityType != ChangeEntity.Person || item.Action != ChangeAction.Create)
            return (false, "هذا الطلب لا يُرحَّل إلى سجل الأشخاص.", null, false);

        if (item.EntityId is int existingId && await db.People.AnyAsync(p => p.Id == existingId))
        {
            var p = await db.People.AsNoTracking().FirstAsync(x => x.Id == existingId);
            return (true, "موجود مسبقاً في السجل.", existingId,
                p.Notes.Contains(IncompleteProfileMarker, StringComparison.Ordinal));
        }

        item.EntityId = null;
        var dto = EnsureMinimalDraft(DeserializePersonDraft(item.PayloadJson), item, submitter);
        var match = await FindExactPersonMatchAsync(db, dto);
        if (match is not null)
        {
            item.EntityId = match.Id;
            item.Status = ChangeStatus.Approved;
            item.ReviewedAt ??= DateTime.UtcNow;
            await db.SaveChangesAsync();
            return (true, "تم الربط بسجل موجود.", match.Id, true);
        }

        try
        {
            var person = await BuildPersonForCreateAsync(db, dto, item.SubmittedByUserId);
            db.People.Add(person);
            await db.SaveChangesAsync();
            item.EntityId = person.Id;
            item.Status = ChangeStatus.Approved;
            item.ReviewedAt ??= DateTime.UtcNow;
            item.Summary = $"إضافة سجل أشخاص {person.RegistryCode}: {person.FullName}";
            item.ReviewNote = IsIncompleteDraft(dto)
                ? "تم الترحيل إلى السجل (بيانات غير مكتملة)"
                : "تم الترحيل إلى السجل";
            await db.SaveChangesAsync();
            return (true, "", person.Id, IsIncompleteDraft(dto));
        }
        catch (Exception ex)
        {
            return (false, ex.GetBaseException().Message, null, false);
        }
    }

    private static async Task ApplyLifeEventAsync(ArchiveDbContext db, PendingChange item)
    {
        if (item.Action == ChangeAction.Delete)
        {
            if (item.EntityId is not int id) return;
            var ev = await db.LifeEvents.FindAsync(id);
            if (ev is not null)
            {
                db.LifeEvents.Remove(ev);
                await db.SaveChangesAsync();
            }
            return;
        }

        var dto = JsonSerializer.Deserialize<LifeEventDraft>(item.PayloadJson, JsonOptions)
                  ?? throw new InvalidOperationException("بيانات المناسبة غير صالحة.");

        if (item.Action == ChangeAction.Create)
        {
            // optional nested child person create
            if (dto.CreateChildPerson && !string.IsNullOrWhiteSpace(dto.ChildFullName) && dto.Type == (int)EventType.Birth)
            {
                var parent = await db.People.FindAsync(dto.PersonId)
                             ?? throw new InvalidOperationException("صاحب المناسبة غير موجود.");
                var childLevel = Math.Min(PersonRegistryService.MaxLevel, parent.HierarchyLevel + 1);
                var childCode = await PersonRegistryService.AllocateCodeAsync(db, childLevel, parent.Id);
                var child = new Person
                {
                    RegistryCode = childCode,
                    HierarchyLevel = childLevel,
                    ParentPersonId = parent.Id,
                    NationalId = string.IsNullOrWhiteSpace(dto.ChildNationalId)
                        ? ""
                        : dto.ChildNationalId.Trim(),
                    FirstName = dto.ChildFullName.Trim(),
                    FatherName = parent.FirstName,
                    GrandfatherName = parent.FatherName,
                    FamilyName = parent.FamilyName,
                    MotherName = dto.MotherName?.Trim() ?? "",
                    Nationality = "",
                    Gender = string.IsNullOrWhiteSpace(dto.ChildGender) ? "ذكر" : dto.ChildGender,
                    BirthDate = dto.EventDate,
                    BirthPlace = dto.Place ?? "الشكابة شاع الدين",
                    Residence = parent.Residence,
                    Tribe = parent.Tribe,
                    Profession = "",
                    Neighborhood = dto.ChildNeighborhood?.Trim() ?? "",
                    Notes = "أُضيف عبر مناسبة مولود جديد (بعد الاعتماد)",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                child.RefreshFullName();
                db.People.Add(child);
                await db.SaveChangesAsync();

                db.LifeEvents.Add(dto.ToLifeEvent(child.Id));
                await db.SaveChangesAsync();
                return;
            }

            db.LifeEvents.Add(dto.ToLifeEvent(dto.PersonId));
            await db.SaveChangesAsync();
            return;
        }

        if (item.EntityId is not int updateId) return;
        var existing = await db.LifeEvents.FindAsync(updateId)
                       ?? throw new InvalidOperationException("المناسبة غير موجودة.");
        dto.ApplyTo(existing);
        await db.SaveChangesAsync();
    }
}

public class PersonDraft
{
    public string RegistryCode { get; set; } = "";
    public int HierarchyLevel { get; set; } = 1;
    public int? ParentPersonId { get; set; }
    public string NationalId { get; set; } = "";
    public string FirstName { get; set; } = "";
    public string FatherName { get; set; } = "";
    public string GrandfatherName { get; set; } = "";
    public string FamilyName { get; set; } = "";
    public string FullName { get; set; } = "";
    public string MotherName { get; set; } = "";
    public string Nationality { get; set; } = "";
    public string Gender { get; set; } = "ذكر";
    public DateTime? BirthDate { get; set; }
    public string BirthPlace { get; set; } = "الشكابة شاع الدين";
    public string Residence { get; set; } = "الشكابة شاع الدين";
    public string Tribe { get; set; } = "";
    public string Profession { get; set; } = "";
    public string Neighborhood { get; set; } = "";
    public string Phone { get; set; } = "";
    public bool IsMigrant { get; set; }
    public string MigrationCountry { get; set; } = "";
    public string MigrationCity { get; set; } = "";
    public string Notes { get; set; } = "";
    public string PhotoPath { get; set; } = "";
    public string DocumentType { get; set; } = DocumentTypes.NationalId;
    public string DocumentNumber { get; set; } = "";
    public string DocumentImagePath { get; set; } = "";

    public void NormalizeDocument()
    {
        DocumentType = string.IsNullOrWhiteSpace(DocumentType)
            ? DocumentTypes.NationalId
            : DocumentType.Trim();
        DocumentNumber = DocumentNumber.Trim();
        // توافق البحث القديم
        NationalId = DocumentNumber;
    }

    public Person ToPerson()
    {
        NormalizeDocument();
        var p = new Person
        {
            RegistryCode = RegistryCode.Trim(),
            HierarchyLevel = HierarchyLevel is >= 1 and <= 3 ? HierarchyLevel : 1,
            ParentPersonId = ParentPersonId,
            DocumentType = DocumentType,
            DocumentNumber = DocumentNumber,
            NationalId = NationalId,
            FirstName = FirstName.Trim(),
            FatherName = FatherName.Trim(),
            GrandfatherName = GrandfatherName.Trim(),
            FamilyName = FamilyName.Trim(),
            MotherName = MotherName.Trim(),
            Nationality = Nationality.Trim(),
            Gender = Gender,
            BirthDate = BirthDate,
            BirthPlace = BirthPlace.Trim(),
            Residence = Residence.Trim(),
            Tribe = Tribe.Trim(),
            Profession = Profession.Trim(),
            Neighborhood = Neighborhood.Trim(),
            Phone = Phone.Trim(),
            IsMigrant = IsMigrant,
            MigrationCountry = IsMigrant ? MigrationCountry.Trim() : "",
            MigrationCity = IsMigrant ? MigrationCity.Trim() : "",
            Notes = Notes.Trim(),
            PhotoPath = PhotoPath,
            DocumentImagePath = DocumentImagePath,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        p.RefreshFullName();
        if (string.IsNullOrWhiteSpace(p.FullName) && !string.IsNullOrWhiteSpace(FullName))
            p.FullName = FullName.Trim();
        return p;
    }

    public void ApplyTo(Person p)
    {
        NormalizeDocument();
        p.DocumentType = DocumentType;
        p.DocumentNumber = DocumentNumber;
        p.NationalId = NationalId;
        p.FirstName = FirstName.Trim();
        p.FatherName = FatherName.Trim();
        p.GrandfatherName = GrandfatherName.Trim();
        p.FamilyName = FamilyName.Trim();
        p.MotherName = MotherName.Trim();
        p.Nationality = Nationality.Trim();
        p.Gender = Gender;
        p.BirthDate = BirthDate;
        p.BirthPlace = BirthPlace.Trim();
        p.Residence = Residence.Trim();
        p.Tribe = Tribe.Trim();
        p.Profession = Profession.Trim();
        p.Neighborhood = Neighborhood.Trim();
        p.Phone = Phone.Trim();
        p.IsMigrant = IsMigrant;
        p.MigrationCountry = IsMigrant ? MigrationCountry.Trim() : "";
        p.MigrationCity = IsMigrant ? MigrationCity.Trim() : "";
        p.Notes = Notes.Trim();
        p.RefreshFullName();
        if (!string.IsNullOrWhiteSpace(PhotoPath))
            p.PhotoPath = PhotoPath;
        if (!string.IsNullOrWhiteSpace(DocumentImagePath))
            p.DocumentImagePath = DocumentImagePath;
    }

    public static PersonDraft From(Person p) => new()
    {
        RegistryCode = p.RegistryCode,
        HierarchyLevel = p.HierarchyLevel,
        ParentPersonId = p.ParentPersonId,
        DocumentType = string.IsNullOrWhiteSpace(p.DocumentType) ? DocumentTypes.NationalId : p.DocumentType,
        DocumentNumber = string.IsNullOrWhiteSpace(p.DocumentNumber) ? p.NationalId : p.DocumentNumber,
        NationalId = p.NationalId,
        FirstName = p.FirstName,
        FatherName = p.FatherName,
        GrandfatherName = p.GrandfatherName,
        FamilyName = p.FamilyName,
        FullName = p.FullName,
        MotherName = p.MotherName,
        Nationality = p.Nationality,
        Gender = p.Gender,
        BirthDate = p.BirthDate,
        BirthPlace = p.BirthPlace,
        Residence = p.Residence,
        Tribe = p.Tribe,
        Profession = p.Profession,
        Neighborhood = p.Neighborhood,
        Phone = p.Phone,
        IsMigrant = p.IsMigrant,
        MigrationCountry = p.MigrationCountry,
        MigrationCity = p.MigrationCity,
        Notes = p.Notes,
        PhotoPath = p.PhotoPath,
        DocumentImagePath = p.DocumentImagePath
    };
}

public class LifeEventDraft
{
    public int PersonId { get; set; }
    public int Type { get; set; }
    public int Mood { get; set; }
    public DateTime? EventDate { get; set; }
    public string Place { get; set; } = "الشكابة شاع الدين";
    public string Title { get; set; } = "";
    public string Details { get; set; } = "";
    public string RelatedPersonName { get; set; } = "";
    public string RelatedFatherName { get; set; } = "";
    public string RelatedPhone { get; set; } = "";
    public string ChildFullName { get; set; } = "";
    public string ChildGender { get; set; } = "";
    public string MotherName { get; set; } = "";
    public string ChildNationalId { get; set; } = "";
    public string ChildNationality { get; set; } = "سوداني";
    public string ChildTribe { get; set; } = "";
    public string ChildNeighborhood { get; set; } = "";
    public bool CreateChildPerson { get; set; }
    public string Institution { get; set; } = "";
    public string Specialty { get; set; } = "";
    public string Degree { get; set; } = "";
    public string SourceNote { get; set; } = "";

    public LifeEvent ToLifeEvent(int personId) => new()
    {
        PersonId = personId,
        Type = (EventType)Type,
        Mood = (EventMood)Mood,
        EventDate = EventDate,
        Place = Place.Trim(),
        Title = Title.Trim(),
        Details = Details.Trim(),
        RelatedPersonName = RelatedPersonName.Trim(),
        RelatedFatherName = RelatedFatherName.Trim(),
        RelatedPhone = RelatedPhone.Trim(),
        ChildFullName = ChildFullName.Trim(),
        ChildGender = ChildGender.Trim(),
        MotherName = MotherName.Trim(),
        Institution = Institution.Trim(),
        Specialty = Specialty.Trim(),
        Degree = Degree.Trim(),
        SourceNote = SourceNote.Trim(),
        CreatedAt = DateTime.UtcNow
    };

    public void ApplyTo(LifeEvent e)
    {
        e.Type = (EventType)Type;
        e.Mood = (EventMood)Mood;
        e.EventDate = EventDate;
        e.Place = Place.Trim();
        e.Title = Title.Trim();
        e.Details = Details.Trim();
        e.RelatedPersonName = RelatedPersonName.Trim();
        e.RelatedFatherName = RelatedFatherName.Trim();
        e.RelatedPhone = RelatedPhone.Trim();
        e.ChildFullName = ChildFullName.Trim();
        e.ChildGender = ChildGender.Trim();
        e.MotherName = MotherName.Trim();
        e.Institution = Institution.Trim();
        e.Specialty = Specialty.Trim();
        e.Degree = Degree.Trim();
        e.SourceNote = SourceNote.Trim();
    }
}
