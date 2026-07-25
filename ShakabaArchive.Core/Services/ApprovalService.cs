using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ShakabaArchive.Data;
using ShakabaArchive.Models;

namespace ShakabaArchive.Services;

public static class ApprovalService
{
    public const int MaxApprovers = 3;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static async Task EnsureSchemaAsync(ArchiveDbContext db)
    {
        try
        {
            _ = await db.PendingChanges.Take(1).ToListAsync();
            return;
        }
        catch
        {
            // create table
        }

        if (db.Database.IsNpgsql())
        {
            await db.Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE IF NOT EXISTS "PendingChanges" (
                  "Id" serial PRIMARY KEY,
                  "EntityType" integer NOT NULL,
                  "Action" integer NOT NULL,
                  "EntityId" integer NULL,
                  "PayloadJson" text NOT NULL DEFAULT '{}',
                  "Summary" varchar(400) NOT NULL DEFAULT '',
                  "Status" integer NOT NULL DEFAULT 0,
                  "SubmittedByUserId" integer NOT NULL,
                  "SubmittedByName" varchar(120) NOT NULL DEFAULT '',
                  "ReviewedByUserId" integer NULL,
                  "ReviewedByName" varchar(120) NULL,
                  "ReviewNote" varchar(400) NOT NULL DEFAULT '',
                  "SubmittedAt" timestamp with time zone NOT NULL,
                  "ReviewedAt" timestamp with time zone NULL
                );
                """);
        }
        else
        {
            await db.Database.ExecuteSqlRawAsync(
                """
                CREATE TABLE IF NOT EXISTS PendingChanges (
                  Id INTEGER PRIMARY KEY AUTOINCREMENT,
                  EntityType INTEGER NOT NULL,
                  Action INTEGER NOT NULL,
                  EntityId INTEGER NULL,
                  PayloadJson TEXT NOT NULL DEFAULT '{}',
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
            await ApplyAsync(db, item);
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
            db.People.Add(person);
            await db.SaveChangesAsync();
            return;
        }

        if (item.EntityId is not int updateId) return;
        var existing = await db.People.FindAsync(updateId)
                       ?? throw new InvalidOperationException("السجل غير موجود.");
        dto.ApplyTo(existing);
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
                var child = new Person
                {
                    NationalId = string.IsNullOrWhiteSpace(dto.ChildNationalId)
                        ? $"TEMP-{DateTime.UtcNow:yyyyMMddHHmmss}"
                        : dto.ChildNationalId.Trim(),
                    FullName = dto.ChildFullName.Trim(),
                    FatherName = parent.FullName,
                    MotherName = dto.MotherName?.Trim() ?? "",
                    Nationality = "",
                    Gender = string.IsNullOrWhiteSpace(dto.ChildGender) ? "ذكر" : dto.ChildGender,
                    BirthDate = dto.EventDate,
                    BirthPlace = dto.Place ?? "الشكابة شاع الدين",
                    Residence = parent.Residence,
                    Tribe = "",
                    Neighborhood = dto.ChildNeighborhood?.Trim() ?? "",
                    Notes = "أُضيف عبر مناسبة مولود جديد (بعد الاعتماد)",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
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
    public string NationalId { get; set; } = "";
    public string FullName { get; set; } = "";
    public string FatherName { get; set; } = "";
    public string MotherName { get; set; } = "";
    public string Nationality { get; set; } = "";
    public string Gender { get; set; } = "ذكر";
    public DateTime? BirthDate { get; set; }
    public string BirthPlace { get; set; } = "الشكابة شاع الدين";
    public string Residence { get; set; } = "الشكابة شاع الدين";
    public string Tribe { get; set; } = "";
    public string Neighborhood { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Notes { get; set; } = "";
    public string DocumentImagePath { get; set; } = "";

    public Person ToPerson() => new()
    {
        NationalId = NationalId.Trim(),
        FullName = FullName.Trim(),
        FatherName = FatherName.Trim(),
        MotherName = MotherName.Trim(),
        Nationality = Nationality.Trim(),
        Gender = Gender,
        BirthDate = BirthDate,
        BirthPlace = BirthPlace.Trim(),
        Residence = Residence.Trim(),
        Tribe = Tribe.Trim(),
        Neighborhood = Neighborhood.Trim(),
        Phone = Phone.Trim(),
        Notes = Notes.Trim(),
        DocumentImagePath = DocumentImagePath,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    };

    public void ApplyTo(Person p)
    {
        p.NationalId = NationalId.Trim();
        p.FullName = FullName.Trim();
        p.FatherName = FatherName.Trim();
        p.MotherName = MotherName.Trim();
        p.Nationality = Nationality.Trim();
        p.Gender = Gender;
        p.BirthDate = BirthDate;
        p.BirthPlace = BirthPlace.Trim();
        p.Residence = Residence.Trim();
        p.Tribe = Tribe.Trim();
        p.Neighborhood = Neighborhood.Trim();
        p.Phone = Phone.Trim();
        p.Notes = Notes.Trim();
        if (!string.IsNullOrWhiteSpace(DocumentImagePath))
            p.DocumentImagePath = DocumentImagePath;
    }

    public static PersonDraft From(Person p) => new()
    {
        NationalId = p.NationalId,
        FullName = p.FullName,
        FatherName = p.FatherName,
        MotherName = p.MotherName,
        Nationality = p.Nationality,
        Gender = p.Gender,
        BirthDate = p.BirthDate,
        BirthPlace = p.BirthPlace,
        Residence = p.Residence,
        Tribe = p.Tribe,
        Neighborhood = p.Neighborhood,
        Phone = p.Phone,
        Notes = p.Notes,
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
