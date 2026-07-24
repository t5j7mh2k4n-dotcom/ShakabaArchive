using Microsoft.EntityFrameworkCore;
using ShakabaArchive.Models;

namespace ShakabaArchive.Data;

/// <summary>قاعدة المستخدمين المحلية على الجهاز (SQLite) — منفصلة عن أرشيف Neon.</summary>
public class UsersDbContext : DbContext
{
    public UsersDbContext(DbContextOptions<UsersDbContext> options) : base(options)
    {
    }

    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<InviteCode> InviteCodes => Set<InviteCode>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AppUser>(e =>
        {
            e.HasIndex(u => u.Email).IsUnique();
            e.HasIndex(u => u.Phone);
            e.Property(u => u.Email).HasMaxLength(160).IsRequired();
            e.Property(u => u.Phone).HasMaxLength(40).IsRequired();
            e.Property(u => u.DisplayName).HasMaxLength(120).IsRequired();
            e.Property(u => u.PasswordHash).HasMaxLength(200).IsRequired();
            e.Property(u => u.InviteCodeUsed).HasMaxLength(40);
            e.Ignore(u => u.UserName);
        });

        modelBuilder.Entity<InviteCode>(e =>
        {
            e.HasIndex(c => c.Code).IsUnique();
            e.Property(c => c.Code).HasMaxLength(40).IsRequired();
            e.Property(c => c.Note).HasMaxLength(200);
        });
    }
}
