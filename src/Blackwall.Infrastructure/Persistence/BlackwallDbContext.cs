using Blackwall.Core.Entities;
using Blackwall.Infrastructure.ValueGenerators;
using Microsoft.EntityFrameworkCore;

namespace Blackwall.Infrastructure.Persistence;

public sealed class BlackwallDbContext(DbContextOptions<BlackwallDbContext> options) : DbContext(options) {

    public DbSet<AppUser> AppUsers => Set<AppUser>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes()) {
            if (!typeof(EntityBase).IsAssignableFrom(entityType.ClrType))
                continue;

            modelBuilder.Entity(entityType.ClrType, entity => {
                entity.HasKey(nameof(EntityBase.Id));

                entity.Property(nameof(EntityBase.Id))
                      .ValueGeneratedOnAdd()
                      .HasValueGenerator<SnowflakeIdGenerator>();

                entity.Property(nameof(EntityBase.CreatedAtUtc))
                      .IsRequired()
                      .ValueGeneratedOnAdd()
                      .HasValueGenerator<UtcNowGenerator>();
            });
        }

        modelBuilder.Entity<AppUser>(entity => {

            entity.Property(e => e.DiscordUserId)
                  .IsRequired()
                  .HasMaxLength(32);

            entity.HasIndex(e => e.DiscordUserId)
                  .IsUnique();

            entity.Property(e => e.Username)
                  .IsRequired()
                  .HasMaxLength(100);
        });

        modelBuilder.Entity<GuildInstance>(entity => {

            entity.Property(e => e.DiscordGuildId)
                  .IsRequired()
                  .HasMaxLength(32);

            entity.HasIndex(e => e.DiscordGuildId)
                  .IsUnique();

            entity.Property(e => e.Name)
                  .IsRequired()
                  .HasMaxLength(100);

            entity.Property(e => e.IconHash)
                  .HasMaxLength(100);

            entity.Property(e => e.UpdatedAtUtc);

            entity.HasOne(e => e.OwnerUser)
                  .WithMany(e => e.OwnedGuilds)
                  .HasForeignKey(e => e.OwnerUserId)
                  .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(e => e.SpamConfiguration)
                  .WithOne(e => e.GuildInstance)
                  .HasForeignKey<SpamConfiguration>(e => e.GuildInstanceId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<GuildManager>(entity => {

            entity.Property(e => e.DiscordRoleId)
                  .HasMaxLength(32);

            entity.Property(e => e.IsAdmin)
                  .IsRequired();

            entity.HasOne(e => e.GuildInstance)
                  .WithMany(e => e.Managers)
                  .HasForeignKey(e => e.GuildInstanceId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.User)
                  .WithMany(e => e.ManagedGuilds)
                  .HasForeignKey(e => e.UserId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => new { e.GuildInstanceId, e.UserId, e.DiscordRoleId })
                  .IsUnique();
        });

        modelBuilder.Entity<SpamConfiguration>(entity => {
            entity.ToTable("spam_configurations");

            entity.Property(e => e.MaxMessagesPerWindow)
                  .IsRequired();

            entity.Property(e => e.RateLimitWindowSeconds)
                  .IsRequired();

            entity.Property(e => e.DuplicateMessageThreshold)
                  .IsRequired();

            entity.Property(e => e.MentionLimit)
                  .IsRequired();

            entity.Property(e => e.BlockInviteLinks)
                  .IsRequired();

            entity.Property(e => e.BlockSuspiciousLinks)
                  .IsRequired();

            entity.Property(e => e.UpdatedAtUtc);
        });

    }
}