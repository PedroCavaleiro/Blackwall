using Blackwall.Core.Entities;
using Blackwall.Infrastructure.ValueGenerators;
using Microsoft.EntityFrameworkCore;

namespace Blackwall.Infrastructure.Persistence;

public sealed class BlackwallDbContext(DbContextOptions<BlackwallDbContext> options) : DbContext(options) {

    public DbSet<AppUser> AppUsers => Set<AppUser>();
    public DbSet<GuildInstance> GuildInstances => Set<GuildInstance>();
    public DbSet<GuildManager> GuildManagers => Set<GuildManager>();
    public DbSet<SpamConfiguration> SpamConfigurations => Set<SpamConfiguration>();
    public DbSet<GuildBlacklist> GuildBlacklists => Set<GuildBlacklist>();
    public DbSet<GuildBlacklistDomain> GuildBlacklistDomains => Set<GuildBlacklistDomain>();

    /// <inheritdoc/>
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
                  .IsRequired(false)
                  .OnDelete(DeleteBehavior.SetNull);

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

            entity.Property(e => e.MaxMessagesPerWindow)
                  .IsRequired();

            entity.Property(e => e.RateLimitWindowSeconds)
                  .IsRequired();

            entity.Property(e => e.DuplicateMessageThreshold)
                  .IsRequired();

            entity.Property(e => e.DuplicateWindowSeconds)
                  .IsRequired()
                  .HasDefaultValue(5);

            entity.Property(e => e.DuplicateCrossChannelEnabled)
                  .IsRequired()
                  .HasDefaultValue(true);

            entity.Property(e => e.MentionLimit)
                  .IsRequired();

            entity.Property(e => e.BlockInviteLinks)
                  .IsRequired();

            entity.Property(e => e.BlockSuspiciousLinks)
                  .IsRequired();

            entity.Property(e => e.LinkWhitelistMode)
                  .IsRequired()
                  .HasDefaultValue(false);

            entity.Property(e => e.IsEnabled)
                  .IsRequired()
                  .HasDefaultValue(true);

            entity.Property(e => e.IsDryRun)
                  .IsRequired()
                  .HasDefaultValue(false);

            entity.Property(e => e.Action)
                  .IsRequired()
                  .HasDefaultValue(InfractionAction.DeleteOnly);

            entity.Property(e => e.LogChannelId);

            entity.Property(e => e.MessageDeleteDays)
                  .IsRequired()
                  .HasDefaultValue(0);

            entity.Property(e => e.IsAntiRaidEnabled)
                  .IsRequired()
                  .HasDefaultValue(false);

            entity.Property(e => e.AntiRaidJoinThreshold)
                  .IsRequired()
                  .HasDefaultValue(10);

            entity.Property(e => e.AntiRaidWindowSeconds)
                  .IsRequired()
                  .HasDefaultValue(30);

            entity.Property(e => e.AntiRaidCooldownMinutes)
                  .IsRequired()
                  .HasDefaultValue(30);

            entity.Property(e => e.IsLockedDown)
                  .IsRequired()
                  .HasDefaultValue(false);

            entity.Property(e => e.AutoLockdownEnabled)
                  .IsRequired()
                  .HasDefaultValue(false);

            entity.Property(e => e.RateLimitAction);
            entity.Property(e => e.RateLimitAutoLockdown);
            entity.Property(e => e.DuplicateAction);
            entity.Property(e => e.DuplicateAutoLockdown);
            entity.Property(e => e.MentionLimitAction);
            entity.Property(e => e.MentionLimitAutoLockdown);
            entity.Property(e => e.InviteLinkAction);
            entity.Property(e => e.InviteLinkAutoLockdown);
            entity.Property(e => e.SuspiciousLinkAction);
            entity.Property(e => e.SuspiciousLinkAutoLockdown);

            entity.Property(e => e.UpdatedAtUtc);

            entity.HasMany(e => e.Blacklists)
                  .WithOne(e => e.SpamConfiguration)
                  .HasForeignKey(e => e.SpamConfigurationId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(e => e.BlacklistDomains)
                  .WithOne(e => e.SpamConfiguration)
                  .HasForeignKey(e => e.SpamConfigurationId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<GuildBlacklist>(entity => {
            entity.Property(e => e.Url)
                  .IsRequired()
                  .HasMaxLength(2048);

            entity.HasIndex(e => new { e.SpamConfigurationId, e.Url })
                  .IsUnique();
        });

        modelBuilder.Entity<GuildBlacklistDomain>(entity => {
            entity.Property(e => e.Domain)
                  .IsRequired()
                  .HasMaxLength(512);

            entity.HasIndex(e => new { e.SpamConfigurationId, e.Domain })
                  .IsUnique();
        });

    }
}