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
    public DbSet<GuildBan> GuildBans => Set<GuildBan>();
    public DbSet<GuildBanSyncRule> GuildBanSyncRules => Set<GuildBanSyncRule>();
    public DbSet<GuildBannedWord> GuildBannedWords => Set<GuildBannedWord>();
    public DbSet<GuildAllowedBot> GuildAllowedBots => Set<GuildAllowedBot>();
    public DbSet<MessageAuditEvent> MessageAuditEvents => Set<MessageAuditEvent>();
    public DbSet<MessageAuditRecord> MessageAuditRecords => Set<MessageAuditRecord>();
    public DbSet<NetWatchSnareChannel> NetWatchSnareChannels => Set<NetWatchSnareChannel>();
    public DbSet<AiSentinelConfiguration> AiSentinelConfigurations => Set<AiSentinelConfiguration>();
    public DbSet<AiSentinelLog> AiSentinelLogs => Set<AiSentinelLog>();
    public DbSet<GuildModuleInstallation> GuildModuleInstallations => Set<GuildModuleInstallation>();
    public DbSet<TwitchChannelInstance> TwitchChannelInstances => Set<TwitchChannelInstance>();
    public DbSet<TwitchChannelManager> TwitchChannelManagers => Set<TwitchChannelManager>();

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
                  .HasMaxLength(32);

            entity.HasIndex(e => e.DiscordUserId)
                  .IsUnique()
                  .HasFilter("\"DiscordUserId\" IS NOT NULL");

            entity.Property(e => e.TwitchUserId)
                  .HasMaxLength(20);

            entity.HasIndex(e => e.TwitchUserId)
                  .IsUnique()
                  .HasFilter("\"TwitchUserId\" IS NOT NULL");

            entity.Property(e => e.Username)
                  .IsRequired()
                  .HasMaxLength(100);

            entity.Property(e => e.TwitchUsername)
                  .HasMaxLength(100);

            entity.Property(e => e.TwitchDisplayName)
                  .HasMaxLength(100);

            entity.Property(e => e.ActiveDisplayNameProvider)
                  .HasMaxLength(20);
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

            entity.HasOne(e => e.AiSentinelConfiguration)
                  .WithOne(e => e.GuildInstance)
                  .HasForeignKey<AiSentinelConfiguration>(e => e.GuildInstanceId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(e => e.Bans)
                  .WithOne(e => e.GuildInstance)
                  .HasForeignKey(e => e.GuildInstanceId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(e => e.BanSyncRules)
                  .WithOne(e => e.TargetGuildInstance)
                  .HasForeignKey(e => e.TargetGuildInstanceId)
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

            entity.Property(e => e.SafeBrowsingEnabled)
                  .IsRequired()
                  .HasDefaultValue(false);

            entity.Property(e => e.SafeBrowsingBlockUnsure)
                  .IsRequired()
                  .HasDefaultValue(false);

            entity.Property(e => e.IsEnabled)
                  .IsRequired()
                  .HasDefaultValue(true);

            entity.Property(e => e.IsDryRun)
                  .IsRequired()
                  .HasDefaultValue(false);

            entity.Property(e => e.LogChannelId);

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

            entity.Property(e => e.IsAccountScoringEnabled)
                  .IsRequired()
                  .HasDefaultValue(false);

            entity.Property(e => e.AutoTimeoutMediumRiskOnJoin)
                  .IsRequired()
                  .HasDefaultValue(false);

            entity.Property(e => e.AutoTimeoutHighRiskOnJoin)
                  .IsRequired()
                  .HasDefaultValue(false);

            entity.Property(e => e.AccountScoringTimeoutMinutes)
                  .IsRequired()
                  .HasDefaultValue(10);

            entity.Property(e => e.IsLockedDown)
                  .IsRequired()
                  .HasDefaultValue(false);

            entity.Property(e => e.RateLimitAction)
                  .IsRequired();

            entity.Property(e => e.RateLimitAutoLockdown)
                  .IsRequired()
                  .HasDefaultValue(false);

            entity.Property(e => e.RateLimitTimeoutMinutes)
                  .IsRequired()
                  .HasDefaultValue(10);

            entity.Property(e => e.RateLimitMessageDeleteDays)
                  .IsRequired()
                  .HasDefaultValue(0);

            entity.Property(e => e.DuplicateAction)
                  .IsRequired();

            entity.Property(e => e.DuplicateAutoLockdown)
                  .IsRequired()
                  .HasDefaultValue(false);

            entity.Property(e => e.DuplicateTimeoutMinutes)
                  .IsRequired()
                  .HasDefaultValue(10);

            entity.Property(e => e.DuplicateMessageDeleteDays)
                  .IsRequired()
                  .HasDefaultValue(0);

            entity.Property(e => e.MentionLimitAction)
                  .IsRequired();

            entity.Property(e => e.MentionLimitAutoLockdown)
                  .IsRequired()
                  .HasDefaultValue(false);

            entity.Property(e => e.MentionLimitTimeoutMinutes)
                  .IsRequired()
                  .HasDefaultValue(10);

            entity.Property(e => e.MentionLimitMessageDeleteDays)
                  .IsRequired()
                  .HasDefaultValue(0);

            entity.Property(e => e.InviteLinkAction)
                  .IsRequired();

            entity.Property(e => e.InviteLinkAutoLockdown)
                  .IsRequired()
                  .HasDefaultValue(false);

            entity.Property(e => e.InviteLinkTimeoutMinutes)
                  .IsRequired()
                  .HasDefaultValue(10);

            entity.Property(e => e.InviteLinkMessageDeleteDays)
                  .IsRequired()
                  .HasDefaultValue(0);

            entity.Property(e => e.SuspiciousLinkAction)
                  .IsRequired();

            entity.Property(e => e.SuspiciousLinkAutoLockdown)
                  .IsRequired()
                  .HasDefaultValue(false);

            entity.Property(e => e.SuspiciousLinkTimeoutMinutes)
                  .IsRequired()
                  .HasDefaultValue(10);

            entity.Property(e => e.SuspiciousLinkMessageDeleteDays)
                  .IsRequired()
                  .HasDefaultValue(0);

            entity.Property(e => e.UpdatedAtUtc);

            entity.HasMany(e => e.Blacklists)
                  .WithOne(e => e.SpamConfiguration)
                  .HasForeignKey(e => e.SpamConfigurationId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(e => e.BlacklistDomains)
                  .WithOne(e => e.SpamConfiguration)
                  .HasForeignKey(e => e.SpamConfigurationId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(e => e.BannedWords)
                  .WithOne(e => e.SpamConfiguration)
                  .HasForeignKey(e => e.SpamConfigurationId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(e => e.AllowedBots)
                  .WithOne(e => e.SpamConfiguration)
                  .HasForeignKey(e => e.SpamConfigurationId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(e => e.NetWatchSnareChannels)
                  .WithOne(e => e.SpamConfiguration)
                  .HasForeignKey(e => e.SpamConfigurationId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.Property(e => e.IsMessageAuditEnabled)
                  .IsRequired()
                  .HasDefaultValue(false);

            entity.Property(e => e.MessageAuditRetentionDays)
                  .IsRequired()
                  .HasDefaultValue(30);

            entity.Property(e => e.IsContentGuardEnabled)
                  .IsRequired()
                  .HasDefaultValue(false);

            entity.Property(e => e.ContentGuardFuzzyMatching)
                  .IsRequired()
                  .HasDefaultValue(true);

            entity.Property(e => e.ContentGuardInvisibleCharScrubbing)
                  .IsRequired()
                  .HasDefaultValue(true);

            entity.Property(e => e.ContentGuardZalgoBlocking)
                  .IsRequired()
                  .HasDefaultValue(true);

            entity.Property(e => e.ContentGuardCopypastaHashing)
                  .IsRequired()
                  .HasDefaultValue(true);

            entity.Property(e => e.ContentGuardFuzzyThreshold)
                  .IsRequired()
                  .HasDefaultValue(2);

            entity.Property(e => e.ContentGuardZalgoMaxCombining)
                  .IsRequired()
                  .HasDefaultValue(3);

            entity.Property(e => e.ContentGuardCopypastaMinLength)
                  .IsRequired()
                  .HasDefaultValue(200);

            entity.Property(e => e.ContentGuardCopypastaThreshold)
                  .IsRequired()
                  .HasDefaultValue(3);

            entity.Property(e => e.ContentGuardCopypastaWindowSeconds)
                  .IsRequired()
                  .HasDefaultValue(60);

            entity.Property(e => e.ContentGuardAction)
                  .IsRequired()
                  .HasDefaultValue(InfractionAction.DeleteOnly);

            entity.Property(e => e.ContentGuardAutoLockdown)
                  .IsRequired()
                  .HasDefaultValue(false);

            entity.Property(e => e.ContentGuardTimeoutMinutes)
                  .IsRequired()
                  .HasDefaultValue(10);

            entity.Property(e => e.ContentGuardMessageDeleteDays)
                  .IsRequired()
                  .HasDefaultValue(0);
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

        modelBuilder.Entity<GuildBan>(entity => {
            entity.Property(e => e.DiscordUserId)
                  .IsRequired();

            entity.Property(e => e.Username)
                  .HasMaxLength(200);

            entity.Property(e => e.Reason)
                  .HasMaxLength(2000);

            entity.HasIndex(e => new { e.GuildInstanceId, e.DiscordUserId })
                  .IsUnique();
        });

        modelBuilder.Entity<GuildBanSyncRule>(entity => {
            entity.Property(e => e.SourceDiscordGuildId)
                  .IsRequired();

            entity.Property(e => e.IsEnabled)
                  .IsRequired();

            entity.HasIndex(e => new { e.TargetGuildInstanceId, e.SourceDiscordGuildId })
                  .IsUnique();
        });

        modelBuilder.Entity<GuildBannedWord>(entity => {
            entity.Property(e => e.Word)
                  .IsRequired()
                  .HasMaxLength(100);

            entity.HasIndex(e => new { e.SpamConfigurationId, e.Word })
                  .IsUnique();
        });

        modelBuilder.Entity<GuildAllowedBot>(entity => {
            entity.Property(e => e.DiscordBotId)
                  .IsRequired();

            entity.Property(e => e.BotUsername)
                  .IsRequired()
                  .HasMaxLength(100);

            entity.HasIndex(e => new { e.SpamConfigurationId, e.DiscordBotId })
                  .IsUnique();
        });

        modelBuilder.Entity<MessageAuditEvent>(entity => {
            entity.Property(e => e.DiscordUserId)
                  .IsRequired();

            entity.Property(e => e.Username)
                  .IsRequired()
                  .HasMaxLength(200);

            entity.Property(e => e.ChannelName)
                  .IsRequired()
                  .HasMaxLength(200);

            entity.Property(e => e.Violations)
                  .IsRequired()
                  .HasMaxLength(500);

            entity.Property(e => e.Action)
                  .IsRequired();

            entity.Property(e => e.IsDryRun)
                  .IsRequired();

            entity.HasOne(e => e.GuildInstance)
                  .WithMany()
                  .HasForeignKey(e => e.GuildInstanceId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(e => e.Records)
                  .WithOne(e => e.Event)
                  .HasForeignKey(e => e.EventId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => new { e.GuildInstanceId, e.CreatedAtUtc });
        });

        modelBuilder.Entity<MessageAuditRecord>(entity => {
            entity.Property(e => e.DiscordMessageId)
                  .IsRequired();

            entity.Property(e => e.DiscordUserId)
                  .IsRequired();

            entity.Property(e => e.Username)
                  .IsRequired()
                  .HasMaxLength(200);

            entity.Property(e => e.ChannelName)
                  .IsRequired()
                  .HasMaxLength(200);

            entity.Property(e => e.Content)
                  .IsRequired();

            entity.Property(e => e.EmbedsJson)
                  .IsRequired();

            entity.Property(e => e.MessageTimestampUtc)
                  .IsRequired();

            entity.Property(e => e.ExpiresAtUtc)
                  .IsRequired();

            entity.HasIndex(e => e.EventId);
            entity.HasIndex(e => e.ExpiresAtUtc);
        });

        modelBuilder.Entity<NetWatchSnareChannel>(entity => {
            entity.Property(e => e.DiscordChannelId)
                  .IsRequired();

            entity.Property(e => e.ChannelName)
                  .IsRequired()
                  .HasMaxLength(200);

            entity.Property(e => e.Action)
                  .IsRequired();

            entity.Property(e => e.TimeoutMinutes)
                  .IsRequired()
                  .HasDefaultValue(60);

            entity.Property(e => e.MessageDeleteDays)
                  .IsRequired()
                  .HasDefaultValue(1);

            entity.Property(e => e.IsEnabled)
                  .IsRequired()
                  .HasDefaultValue(true);

            entity.HasIndex(e => new { e.SpamConfigurationId, e.DiscordChannelId })
                  .IsUnique();
        });

        modelBuilder.Entity<AiSentinelConfiguration>(entity => {
            entity.Property(e => e.IsEnabled)
                  .IsRequired()
                  .HasDefaultValue(false);

            entity.Property(e => e.IsDryRun)
                  .IsRequired()
                  .HasDefaultValue(true);

            entity.Property(e => e.IsTrainingMode)
                  .IsRequired()
                  .HasDefaultValue(true);

            entity.Property(e => e.Provider)
                  .IsRequired()
                  .HasDefaultValue(AiSentinelProvider.OpenAi);

            entity.Property(e => e.ApiKey)
                  .HasMaxLength(512);

            entity.Property(e => e.OllamaUrl)
                  .HasMaxLength(512);

            entity.Property(e => e.OllamaHeader1Key)
                  .HasMaxLength(100);

            entity.Property(e => e.OllamaHeader1Value)
                  .HasMaxLength(512);

            entity.Property(e => e.OllamaHeader2Key)
                  .HasMaxLength(100);

            entity.Property(e => e.OllamaHeader2Value)
                  .HasMaxLength(512);

            entity.Property(e => e.OllamaHeader3Key)
                  .HasMaxLength(100);

            entity.Property(e => e.OllamaHeader3Value)
                  .HasMaxLength(512);

            entity.Property(e => e.Model)
                  .HasMaxLength(200);

            entity.Property(e => e.Action)
                  .IsRequired()
                  .HasDefaultValue(InfractionAction.DeleteOnly);

            entity.Property(e => e.AutoLockdown)
                  .IsRequired()
                  .HasDefaultValue(false);

            entity.Property(e => e.TimeoutMinutes)
                  .IsRequired()
                  .HasDefaultValue(10);

            entity.Property(e => e.MessageDeleteDays)
                  .IsRequired()
                  .HasDefaultValue(0);

            entity.Property(e => e.UpdatedAtUtc);

            entity.HasMany(e => e.Logs)
                  .WithOne(e => e.AiSentinelConfiguration)
                  .HasForeignKey(e => e.AiSentinelConfigurationId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AiSentinelLog>(entity => {
            entity.Property(e => e.DiscordMessageId)
                  .IsRequired();

            entity.Property(e => e.DiscordUserId)
                  .IsRequired();

            entity.Property(e => e.Username)
                  .IsRequired()
                  .HasMaxLength(200);

            entity.Property(e => e.ChannelName)
                  .IsRequired()
                  .HasMaxLength(200);

            entity.Property(e => e.Content)
                  .IsRequired();

            entity.Property(e => e.EmbedsJson)
                  .IsRequired();

            entity.Property(e => e.Classification)
                  .IsRequired();

            entity.Property(e => e.Reasoning)
                  .IsRequired();

            entity.Property(e => e.Provider)
                  .IsRequired();

            entity.Property(e => e.Model)
                  .IsRequired()
                  .HasMaxLength(200);

            entity.Property(e => e.IsDryRun)
                  .IsRequired();

            entity.Property(e => e.WouldAction)
                  .IsRequired();

            entity.Property(e => e.TrainingFeedback)
                  .IsRequired()
                  .HasDefaultValue(AiSentinelTrainingFeedback.None);

            entity.Property(e => e.MessageTimestampUtc)
                  .IsRequired();

            entity.Property(e => e.ExpiresAtUtc)
                  .IsRequired();

            entity.HasIndex(e => e.AiSentinelConfigurationId);
            entity.HasIndex(e => e.ExpiresAtUtc);
            entity.HasIndex(e => new { e.AiSentinelConfigurationId, e.CreatedAtUtc });
        });

        modelBuilder.Entity<TwitchChannelInstance>(entity => {
            entity.HasIndex(e => e.TwitchUserId)
                  .IsUnique();

            entity.Property(e => e.Username)
                  .IsRequired()
                  .HasMaxLength(100);

            entity.Property(e => e.DisplayName)
                  .IsRequired()
                  .HasMaxLength(100);

            entity.Property(e => e.ProfileImageUrl)
                  .HasMaxLength(500);

            entity.Property(e => e.UpdatedAtUtc);

            entity.HasOne(e => e.OwnerUser)
                  .WithMany(e => e.OwnedTwitchChannels)
                  .HasForeignKey(e => e.OwnerUserId)
                  .IsRequired(false)
                  .OnDelete(DeleteBehavior.SetNull);

            entity.HasMany(e => e.Managers)
                  .WithOne(e => e.TwitchChannelInstance)
                  .HasForeignKey(e => e.TwitchChannelInstanceId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TwitchChannelManager>(entity => {
            entity.Property(e => e.IsAdmin)
                  .IsRequired();

            entity.HasOne(e => e.TwitchChannelInstance)
                  .WithMany(e => e.Managers)
                  .HasForeignKey(e => e.TwitchChannelInstanceId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.User)
                  .WithMany(e => e.ManagedTwitchChannels)
                  .HasForeignKey(e => e.UserId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => new { e.TwitchChannelInstanceId, e.UserId })
                  .IsUnique();
        });

        modelBuilder.Entity<GuildModuleInstallation>(entity => {
            entity.Property(e => e.ModuleName)
                  .IsRequired()
                  .HasMaxLength(200);

            entity.Property(e => e.ModuleVersion)
                  .IsRequired()
                  .HasMaxLength(50);

            entity.Property(e => e.ModuleAuthor)
                  .IsRequired()
                  .HasMaxLength(200);

            entity.Property(e => e.EntryPoint)
                  .IsRequired()
                  .HasMaxLength(500);

            entity.Property(e => e.GitUrl)
                  .HasMaxLength(1000);

            entity.Property(e => e.CanPerformActions)
                  .IsRequired();

            entity.Property(e => e.IsEnabled)
                  .IsRequired()
                  .HasDefaultValue(false);

            entity.Property(e => e.SettingsJson)
                  .IsRequired();

            entity.Property(e => e.ManifestJson)
                  .IsRequired();

            entity.Property(e => e.UpdatedAtUtc);

            entity.HasOne(e => e.GuildInstance)
                  .WithMany()
                  .HasForeignKey(e => e.GuildInstanceId)
                  .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => new { e.GuildInstanceId, e.ModuleName })
                  .IsUnique();
        });

    }
}