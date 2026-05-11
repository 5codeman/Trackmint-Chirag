using Microsoft.EntityFrameworkCore;
using TrackMint.NotificationService.Domain;

namespace TrackMint.NotificationService.Persistence;

public sealed class NotificationDbContext(DbContextOptions<NotificationDbContext> options) : DbContext(options)
{
    public DbSet<Notification> Notifications => Set<Notification>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Notification>(entity =>
        {
            entity.Property(x => x.Type).HasMaxLength(80);
            entity.Property(x => x.Title).HasMaxLength(160);
            entity.Property(x => x.Message).HasMaxLength(1000);
            entity.HasIndex(x => new { x.UserId, x.CreatedAtUtc });
            entity.HasIndex(x => new { x.UserId, x.IsRead });
        });
    }
}
