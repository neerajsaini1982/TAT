using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
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
    public DbSet<ScheduledBreak> ScheduledBreaks => Set<ScheduledBreak>();
    public DbSet<TimeEntrySegment> TimeEntrySegments => Set<TimeEntrySegment>();

    // SQLite has no native "datetime with offset" column type, so EF Core
    // round-trips every DateTime as Kind=Unspecified after a read — even
    // though every DateTime in this app is always UTC (DateTime.UtcNow).
    // Left alone, that means a value just set in memory (Kind=Utc,
    // serializes with a trailing "Z") and the same value reloaded from the
    // DB (Kind=Unspecified, no "Z") serialize differently, so the client's
    // Date parsing treats one as UTC and the other as local time. Stamping
    // Kind=Utc on every read fixes it project-wide instead of field by field.
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
    }

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

        modelBuilder.Entity<ScheduledBreak>(entity =>
        {
            entity.Property(b => b.Kind).HasConversion<string>();

            entity.HasOne(b => b.Shift)
                .WithMany(s => s.ScheduledBreaks)
                .HasForeignKey(b => b.ShiftId)
                .OnDelete(DeleteBehavior.Cascade);
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

            entity.HasOne(a => a.AbsentMarkedByAccount)
                .WithMany()
                .HasForeignKey(a => a.AbsentMarkedByAccountId)
                .OnDelete(DeleteBehavior.Restrict);

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

            entity.HasOne(t => t.ClockedOutByAccount)
                .WithMany()
                .HasForeignKey(t => t.ClockedOutByAccountId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(t => t.EditedByAccount)
                .WithMany()
                .HasForeignKey(t => t.EditedByAccountId)
                .OnDelete(DeleteBehavior.Restrict);

            // One clock-in per shift assignment.
            entity.HasIndex(t => t.ShiftAssignmentId).IsUnique();
        });

        modelBuilder.Entity<TimeEntrySegment>(entity =>
        {
            entity.Property(s => s.Kind).HasConversion<string>();

            entity.HasOne(s => s.TimeEntry)
                .WithMany(t => t.Segments)
                .HasForeignKey(s => s.TimeEntryId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private sealed class UtcDateTimeConverter : ValueConverter<DateTime, DateTime>
    {
        public UtcDateTimeConverter() : base(
            v => v,
            v => DateTime.SpecifyKind(v, DateTimeKind.Utc))
        {
        }
    }
}
