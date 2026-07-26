using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using ShakabaArchive.Models;

namespace ShakabaArchive.Data;

public class ArchiveDbContext : DbContext, IDataProtectionKeyContext
{
    public ArchiveDbContext(DbContextOptions<ArchiveDbContext> options) : base(options)
    {
    }

    public DbSet<Person> People => Set<Person>();
    public DbSet<LifeEvent> LifeEvents => Set<LifeEvent>();
    public DbSet<PendingChange> PendingChanges => Set<PendingChange>();
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<InviteCode> InviteCodes => Set<InviteCode>();
    public DbSet<MediaFile> MediaFiles => Set<MediaFile>();
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Person>(e =>
        {
            e.HasIndex(p => p.RegistryCode).IsUnique();
            e.HasIndex(p => p.NationalId);
            e.HasIndex(p => p.DocumentNumber);
            e.HasIndex(p => p.DocumentType);
            e.HasIndex(p => p.FullName);
            e.HasIndex(p => p.FamilyName);
            e.HasIndex(p => p.Tribe);
            e.HasIndex(p => p.HierarchyLevel);
            e.HasIndex(p => p.Neighborhood);
            e.HasIndex(p => p.IsMigrant);
            e.HasIndex(p => p.MigrationCountry);
            e.HasIndex(p => p.MigrationCity);
            e.HasIndex(p => p.OwnerUserId);
            e.Property(p => p.RegistryCode).HasMaxLength(32).IsRequired();
            e.Property(p => p.DocumentType).HasMaxLength(40);
            e.Property(p => p.DocumentNumber).HasMaxLength(80);
            e.Property(p => p.NationalId).HasMaxLength(64);
            e.Property(p => p.FirstName).HasMaxLength(80).IsRequired();
            e.Property(p => p.FatherName).HasMaxLength(80);
            e.Property(p => p.GrandfatherName).HasMaxLength(80);
            e.Property(p => p.FamilyName).HasMaxLength(80);
            e.Property(p => p.FullName).HasMaxLength(320).IsRequired();
            e.Property(p => p.MotherName).HasMaxLength(120);
            e.Property(p => p.Nationality).HasMaxLength(80);
            e.Property(p => p.Gender).HasMaxLength(20);
            e.Property(p => p.BirthPlace).HasMaxLength(200);
            e.Property(p => p.Residence).HasMaxLength(200);
            e.Property(p => p.Tribe).HasMaxLength(120);
            e.Property(p => p.Profession).HasMaxLength(120);
            e.Property(p => p.Neighborhood).HasMaxLength(120);
            e.Property(p => p.Phone).HasMaxLength(40);
            e.Property(p => p.MigrationCountry).HasMaxLength(120);
            e.Property(p => p.MigrationCity).HasMaxLength(120);
            e.Property(p => p.PhotoPath).HasMaxLength(400);
            e.Property(p => p.DocumentImagePath).HasMaxLength(400);

            e.HasOne(p => p.ParentPerson)
                .WithMany(p => p.Children)
                .HasForeignKey(p => p.ParentPersonId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<LifeEvent>(e =>
        {
            e.HasOne(x => x.Person)
                .WithMany(p => p.Events)
                .HasForeignKey(x => x.PersonId)
                .OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Title).HasMaxLength(200);
            e.Property(x => x.Place).HasMaxLength(200);
            e.Property(x => x.RelatedPersonName).HasMaxLength(200);
            e.Property(x => x.RelatedFatherName).HasMaxLength(120);
            e.Property(x => x.RelatedPhone).HasMaxLength(40);
            e.Property(x => x.ChildFullName).HasMaxLength(200);
            e.Property(x => x.ChildGender).HasMaxLength(20);
            e.Property(x => x.MotherName).HasMaxLength(120);
            e.Property(x => x.Institution).HasMaxLength(200);
            e.Property(x => x.Specialty).HasMaxLength(200);
            e.Property(x => x.Degree).HasMaxLength(120);
            e.Property(x => x.SourceNote).HasMaxLength(300);
            e.HasIndex(x => x.Type);
            e.HasIndex(x => x.Mood);
        });

        modelBuilder.Entity<PendingChange>(e =>
        {
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.SubmittedAt);
            e.Property(x => x.Summary).HasMaxLength(400);
            e.Property(x => x.SubmittedByName).HasMaxLength(120);
            e.Property(x => x.ReviewedByName).HasMaxLength(120);
            e.Property(x => x.ReviewNote).HasMaxLength(400);
        });

        // نفس قاعدة الأرشيف (Neon) — حتى لا تُمسح الحسابات مع Deploy على Render
        modelBuilder.Entity<AppUser>(e =>
        {
            e.ToTable("Users");
            e.HasIndex(u => u.Email).IsUnique();
            e.HasIndex(u => u.Phone);
            e.Property(u => u.Email).HasMaxLength(160).IsRequired();
            e.Property(u => u.Phone).HasMaxLength(40).IsRequired();
            e.Property(u => u.DisplayName).HasMaxLength(120).IsRequired();
            e.Property(u => u.PasswordHash).HasMaxLength(200).IsRequired();
            e.Property(u => u.InviteCodeUsed).HasMaxLength(40);
            e.Ignore(u => u.UserName);
            e.Ignore(u => u.CanApprove);
            e.Ignore(u => u.IsEditorOnly);
        });

        modelBuilder.Entity<InviteCode>(e =>
        {
            e.ToTable("InviteCodes");
            e.HasIndex(c => c.Code).IsUnique();
            e.Property(c => c.Code).HasMaxLength(40).IsRequired();
            e.Property(c => c.Note).HasMaxLength(200);
        });

        modelBuilder.Entity<MediaFile>(e =>
        {
            e.ToTable("MediaFiles");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(80);
            e.Property(x => x.ContentType).HasMaxLength(120);
        });
    }
}
