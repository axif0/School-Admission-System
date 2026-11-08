using Microsoft.EntityFrameworkCore;
using SchoolAdmission.Api.Domain;

namespace SchoolAdmission.Api.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Class> Classes { get; set; }
    public DbSet<Hold> Holds { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Class table
        modelBuilder.Entity<Class>(e =>
        {
            e.ToTable("classes");
            e.HasKey(c => c.Id);
            e.Property(c => c.Id).HasColumnName("id");
            e.Property(c => c.Name).HasColumnName("name").IsRequired();
            e.Property(c => c.TotalSeats).HasColumnName("total_seats");
            e.Property(c => c.CreatedAt).HasColumnName("created_at");
        });

        // Hold table
        modelBuilder.Entity<Hold>(e =>
        {
            e.ToTable("holds");
            e.HasKey(h => h.Id);
            e.Property(h => h.Id).HasColumnName("id");
            e.Property(h => h.ClassId).HasColumnName("class_id");
            e.Property(h => h.StudentId).HasColumnName("student_id").HasMaxLength(64);
            e.Property(h => h.Status).HasColumnName("status");
            e.Property(h => h.ExpiresAt).HasColumnName("expires_at");
            e.Property(h => h.CreatedAt).HasColumnName("created_at");
            e.Property(h => h.ConfirmedAt).HasColumnName("confirmed_at");
            e.Property(h => h.CancelledAt).HasColumnName("cancelled_at");

            e.HasIndex(h => new { h.ClassId, h.Status, h.ExpiresAt })
                .HasDatabaseName("ix_holds_class_status_expires");
            e.HasIndex(h => h.StudentId)
                .HasDatabaseName("ix_holds_student_id");
        });

        base.OnModelCreating(modelBuilder);
    }
}
