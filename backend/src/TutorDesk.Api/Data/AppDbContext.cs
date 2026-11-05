using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using TutorDesk.Api.Domain;

namespace TutorDesk.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options, TimeProvider? clock = null)
    : IdentityDbContext<AppUser, IdentityRole<Guid>, Guid>(options)
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public DbSet<Student> Students => Set<Student>();
    public DbSet<Subject> Subjects => Set<Subject>();
    public DbSet<Lesson> Lessons => Set<Lesson>();
    public DbSet<ProgressNote> ProgressNotes => Set<ProgressNote>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public static readonly string[] SeedSubjects =
    [
        "Mathematics", "Specialist Mathematics", "English", "Physics", "Chemistry",
        "Biology", "Science", "Vietnamese", "Computer Science", "Study Skills",
    ];

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        b.Entity<AppUser>(e =>
        {
            e.Property(u => u.FullName).HasMaxLength(120).IsRequired();
        });

        b.Entity<Student>(e =>
        {
            e.ToTable("students");
            e.Property(s => s.FirstName).HasMaxLength(60).IsRequired();
            e.Property(s => s.LastName).HasMaxLength(60).IsRequired();
            e.Property(s => s.School).HasMaxLength(120);
            e.Property(s => s.ParentName).HasMaxLength(120);
            e.Property(s => s.ParentEmail).HasMaxLength(254);
            e.Property(s => s.ParentPhone).HasMaxLength(30);
            e.Property(s => s.Notes).HasMaxLength(2000);
            e.Property(s => s.Version).IsRowVersion();
            e.Ignore(s => s.FullName);
            e.HasOne(s => s.Tutor).WithMany().HasForeignKey(s => s.TutorId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(s => new { s.TutorId, s.IsArchived });
            e.ToTable(t => t.HasCheckConstraint("ck_students_year_level", "\"YearLevel\" BETWEEN 1 AND 12"));
        });

        b.Entity<Subject>(e =>
        {
            e.ToTable("subjects");
            e.Property(s => s.Name).HasMaxLength(60).IsRequired();
            e.HasIndex(s => s.Name).IsUnique();
            e.HasData(SeedSubjects.Select((name, i) => new Subject { Id = i + 1, Name = name }));
        });

        b.Entity<Lesson>(e =>
        {
            e.ToTable("lessons");
            e.Property(l => l.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(l => l.Mode).HasConversion<string>().HasMaxLength(20);
            e.Property(l => l.Location).HasMaxLength(200);
            e.Property(l => l.CancellationReason).HasMaxLength(500);
            e.Property(l => l.Version).IsRowVersion();
            e.HasOne(l => l.Tutor).WithMany().HasForeignKey(l => l.TutorId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(l => l.Student).WithMany(s => s.Lessons).HasForeignKey(l => l.StudentId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne(l => l.Subject).WithMany().HasForeignKey(l => l.SubjectId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(l => new { l.TutorId, l.StartsAtUtc });
            e.HasIndex(l => new { l.StudentId, l.StartsAtUtc });
            e.HasIndex(l => l.SeriesId);
            e.ToTable(t =>
            {
                t.HasCheckConstraint("ck_lessons_duration",
                    "\"DurationMinutes\" BETWEEN 15 AND 240");
                t.HasCheckConstraint("ck_lessons_end_after_start",
                    "\"EndsAtUtc\" > \"StartsAtUtc\"");
            });
        });

        b.Entity<ProgressNote>(e =>
        {
            e.ToTable("progress_notes");
            e.Property(p => p.Note).HasMaxLength(2000).IsRequired();
            e.HasOne(p => p.Student).WithMany(s => s.ProgressNotes).HasForeignKey(p => p.StudentId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(p => p.Lesson).WithOne(l => l.ProgressNote)
                .HasForeignKey<ProgressNote>(p => p.LessonId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(p => p.Subject).WithMany().HasForeignKey(p => p.SubjectId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(p => new { p.StudentId, p.CreatedAtUtc });
            e.ToTable(t => t.HasCheckConstraint("ck_progress_rating",
                "\"UnderstandingRating\" IS NULL OR \"UnderstandingRating\" BETWEEN 1 AND 5"));
        });

        b.Entity<RefreshToken>(e =>
        {
            e.ToTable("refresh_tokens");
            e.Property(t => t.TokenHash).HasMaxLength(64).IsRequired();
            e.Property(t => t.CreatedByIp).HasMaxLength(64);
            e.HasIndex(t => t.TokenHash).IsUnique();
            e.HasIndex(t => t.UserId);
            e.HasOne(t => t.User).WithMany().HasForeignKey(t => t.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var now = _clock.GetUtcNow().UtcDateTime;
        foreach (var entry in ChangeTracker.Entries<IAuditable>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAtUtc = now;
                entry.Entity.UpdatedAtUtc = now;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAtUtc = now;
            }
        }
        return base.SaveChangesAsync(cancellationToken);
    }
}
