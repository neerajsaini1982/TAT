using Microsoft.EntityFrameworkCore;
using Server.Models;

namespace Server.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Location> Locations => Set<Location>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Shift> Shifts => Set<Shift>();
    public DbSet<Availability> Availabilities => Set<Availability>();
    public DbSet<AvailabilityDay> AvailabilityDays => Set<AvailabilityDay>();
    public DbSet<ShiftAssignment> ShiftAssignments => Set<ShiftAssignment>();
    public DbSet<LocationSettings> LocationSettings => Set<LocationSettings>();
    public DbSet<EmailTemplate> EmailTemplates => Set<EmailTemplate>();
    public DbSet<TimeEntry> TimeEntries => Set<TimeEntry>();

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

        modelBuilder.Entity<Availability>(entity =>
        {
            entity.HasOne(a => a.Account)
                .WithMany()
                .HasForeignKey(a => a.AccountId)
                .OnDelete(DeleteBehavior.Cascade);

            // One submission per employee per week.
            entity.HasIndex(a => new { a.AccountId, a.WeekStartDate }).IsUnique();
        });

        modelBuilder.Entity<AvailabilityDay>(entity =>
        {
            entity.HasOne(d => d.Availability)
                .WithMany(a => a.Days)
                .HasForeignKey(d => d.AvailabilityId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(d => new { d.AvailabilityId, d.Date }).IsUnique();
        });

        modelBuilder.Entity<ShiftAssignment>(entity =>
        {
            entity.HasOne(a => a.Shift)
                .WithMany()
                .HasForeignKey(a => a.ShiftId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(a => a.Account)
                .WithMany()
                .HasForeignKey(a => a.AccountId)
                .OnDelete(DeleteBehavior.Cascade);

            // Same employee can't be assigned to the same shift twice on the same day.
            entity.HasIndex(a => new { a.ShiftId, a.AccountId, a.Date }).IsUnique();
        });

        modelBuilder.Entity<LocationSettings>(entity =>
        {
            entity.Property(s => s.TimeFormat).HasConversion<string>();
            entity.Property(s => s.DateFormat).HasConversion<string>();

            entity.HasOne(s => s.Location)
                .WithMany()
                .HasForeignKey(s => s.LocationId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(s => s.LocationId).IsUnique();
        });

        modelBuilder.Entity<EmailTemplate>(entity =>
        {
            entity.HasOne(t => t.Location)
                .WithMany()
                .HasForeignKey(t => t.LocationId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(t => new { t.LocationId, t.Key }).IsUnique();
        });

        modelBuilder.Entity<TimeEntry>(entity =>
        {
            entity.HasOne(t => t.Account)
                .WithMany()
                .HasForeignKey(t => t.AccountId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(t => t.ShiftAssignment)
                .WithMany()
                .HasForeignKey(t => t.ShiftAssignmentId)
                .OnDelete(DeleteBehavior.Cascade);

            // One clock-in per shift assignment.
            entity.HasIndex(t => t.ShiftAssignmentId).IsUnique();
        });
    }
}
