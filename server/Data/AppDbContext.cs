using Microsoft.EntityFrameworkCore;
using Server.Models;

namespace Server.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Location> Locations => Set<Location>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Shift> Shifts => Set<Shift>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Location>(entity =>
        {
            entity.HasIndex(l => l.LocationCode).IsUnique();
        });

        modelBuilder.Entity<Account>(entity =>
        {
            entity.Property(a => a.Role).HasConversion<string>();
            entity.HasIndex(a => a.Username).IsUnique();

            // UserCode only needs to be unique within a location; Sa accounts
            // have no UserCode/LocationId so they're excluded from the index.
            entity.HasIndex(a => new { a.LocationId, a.UserCode })
                .IsUnique()
                .HasFilter("UserCode IS NOT NULL");

            entity.HasOne(a => a.Location)
                .WithMany(l => l.Accounts)
                .HasForeignKey(a => a.LocationId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Shift>(entity =>
        {
            entity.HasOne(s => s.Location)
                .WithMany(l => l.Shifts)
                .HasForeignKey(s => s.LocationId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
