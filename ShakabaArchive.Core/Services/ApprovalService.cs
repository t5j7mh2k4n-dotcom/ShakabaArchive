using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ShakabaArchive.Data;
using ShakabaArchive.Models;

namespace ShakabaArchive.Services;

public static class ApprovalService
{
    public const int MaxApprovers = 3;

    /// <summary>الحد الأقصى لإجمالي حسابات المستخدمين (بما فيهم الأدمن).</summary>
    public const int MaxUsers = 20;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static async Task EnsureSchemaAsync(ArchiveDbContext db)
    {
        try
        {
            _ = await db.PendingChanges.AsNoTracking().Select(x => new { x.Id, x.Summary, x.Status }).Take(1).ToListAsync();
            return;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("PendingChanges query failed, repairing: " + ex.Message);
        }

        var isPostgres = db.Database.ProviderName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true;
        if (isPostgres)
        {
            await using var ddl = DatabaseService.CreateContextForSchemaChanges();
            // دائماً أعد الإنشاء إن فشل الاستعلام — الجداول التالفة كانت السبب الأشيع
            await ddl.Database.ExecuteSqlRawAsync("""DROP TABLE IF EXISTS "PendingChanges" CASCADE;""");
            await ddl.Database.ExecuteSqlRawAsync("""DROP TABLE IF EXISTS pendingchanges CASCADE;""");
            await ddl.Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE "PendingChanges" (
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
            Console.WriteLine("PendingChanges table recreated via direct Neon connection.");
            return;
        }

        await db.Database.ExecuteSqlRawAsync("""DROP TABLE IF EXISTS PendingChanges;""");
        await db.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE PendingChanges (
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

        // الأدمن والموافقون: حفظ فوري في الأرشيف
        if (user.CanApprove)
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

    public static async Task<(bool Ok, string Error)> ApproveAsync(
        ArchiveDbContext db,
        AppUser reviewer,
        int pendingId,
        string? note = null)
    {
        if (!reviewer.CanApprove)
            return (false, "ليست لديك صلاحية الموافقة على صحة البيانات.");

        var item = await db.PendingChanges.FirstOrDefaultAsync(x => x.Id == pendingId);
        if (item is null)
            return (false, "الطلب غير موجود.");
        if (item.Status != ChangeStatus.Pending)
            return (false, "تمت مراجعة هذا الطلب مسبقاً.");
        if (item.SubmittedByUserId == reviewer.Id && !reviewer.IsAdmin)
            return (false, "لا يمكن اعتماد طلبك بنفسك — يوافق أحد الثلاثة الآخرين أو الأدمن الرئيسي.");

        try
        {
            await ApplyAsync(db, item);
        }
        catch (Exception ex)
        {
            return (false, "تعذر تطبيق التعديل: " + ex.Message);
        }

        item.Status = ChangeStatus.Approved;
        item.ReviewedByUserId = reviewer.Id;
        item.ReviewedByName = reviewer.DisplayName;
        item.ReviewNote = note?.Trim() ?? "";
        item.ReviewedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return (true, "");
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

        item.Status = ChangeStatus.Rejected;
        item.ReviewedByUserId = reviewer.Id;
        item.ReviewedByName = reviewer.DisplayName;
        item.ReviewNote = note?.Trim() ?? "مرفوض";
        item.ReviewedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
        return (true, "");
    }

    private static async Task ApplyAsync(ArchiveDbContext db, PendingChange item)
    {
        if (item.EntityType == ChangeEntity.Person)
            await ApplyPersonAsync(db, item);
        else
            await ApplyLifeEventAsync(db, item);
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

        var dto = JsonSerializer.Deserialize<PersonDraft>(item.PayloadJson, JsonOptions)
                  ?? throw new InvalidOperationException("بيانات الشخص غير صالحة.");

        if (item.Action == ChangeAction.Create)
        {
            var person = dto.ToPerson();
            if (string.IsNullOrWhiteSpace(person.RegistryCode))
            {
                person.RegistryCode = await PersonRegistryService.AllocateCodeAsync(
                    db, person.HierarchyLevel, person.ParentPersonId);
            }

            person.RefreshFullName();
            db.People.Add(person);
            await db.SaveChangesAsync();
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
        existing.RefreshFullName();
        existing.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync();
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
