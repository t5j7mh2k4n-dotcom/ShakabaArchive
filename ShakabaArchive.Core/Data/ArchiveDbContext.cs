using Microsoft.EntityFrameworkCore;
using ShakabaArchive.Models;

namespace ShakabaArchive.Data;

public class ArchiveDbContext : DbContext
{
    public ArchiveDbContext(DbContextOptions<ArchiveDbContext> options) : base(options)
    {
    }

    public DbSet<Person> People => Set<Person>();
    public DbSet<LifeEvent> LifeEvents => Set<LifeEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Person>(e =>
        {
            e.HasIndex(p => p.NationalId);
            e.HasIndex(p => p.Nationality);
            e.HasIndex(p => p.FullName);
            e.HasIndex(p => p.Tribe);
            e.HasIndex(p => p.Neighborhood);
            e.Property(p => p.NationalId).HasMaxLength(64).IsRequired();
            e.Property(p => p.FullName).HasMaxLength(200).IsRequired();
            e.Property(p => p.FatherName).HasMaxLength(120);
            e.Property(p => p.MotherName).HasMaxLength(120);
            e.Property(p => p.Nationality).HasMaxLength(80);
            e.Property(p => p.Gender).HasMaxLength(20);
            e.Property(p => p.BirthPlace).HasMaxLength(200);
            e.Property(p => p.Residence).HasMaxLength(200);
            e.Property(p => p.Tribe).HasMaxLength(120);
            e.Property(p => p.Neighborhood).HasMaxLength(120);
            e.Property(p => p.Phone).HasMaxLength(40);
            e.Property(p => p.DocumentImagePath).HasMaxLength(400);
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
    }
}
