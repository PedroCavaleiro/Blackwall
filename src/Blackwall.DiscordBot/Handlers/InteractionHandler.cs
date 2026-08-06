using Blackwall.DiscordBot.Services;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
// ReSharper disable NullableWarningSuppressionIsUsed

namespace Blackwall.DiscordBot.Handlers;

public sealed class InteractionHandler(
    LockdownService lockdownService,
    ILogger<InteractionHandler> logger
) {
    /// <summary>
    /// Registers the /lockdown and /unlock slash commands globally.
    /// </summary>
    public async Task RegisterCommandsAsync(DiscordSocketClient client) {
        var lockdown = new SlashCommandBuilder()
            .WithName("lockdown")
            .WithDescription("Lock down the server by denying Send Messages for @@everyone in all text channels.")
            .WithDefaultMemberPermissions(GuildPermission.ManageChannels | GuildPermission.ManageGuild)
            .Build();

        var unlock = new SlashCommandBuilder()
            .WithName("unlock")
            .WithDescription("Lift the lockdown and restore Send Messages permissions.")
            .WithDefaultMemberPermissions(GuildPermission.ManageChannels | GuildPermission.ManageGuild)
            .Build();

        await client.CreateGlobalApplicationCommandAsync(lockdown);
        await client.CreateGlobalApplicationCommandAsync(unlock);

        logger.LogInformation("Registered /lockdown and /unlock slash commands");
    }

    /// <summary>
    /// Handles incoming slash command interactions, dispatching to the appropriate command handler.
    /// </summary>
    public async Task OnInteractionCreatedAsync(SocketInteraction interaction) {
        if (interaction is not SocketSlashCommand slashCommand)
            return;

        if (slashCommand.GuildId is null) {
            await slashCommand.RespondAsync("This command can only be used in a server.", ephemeral: true);
            return;
        }

        var guildUser = slashCommand.User as SocketGuildUser;
        if (guildUser is null) {
            await slashCommand.RespondAsync("Unable to resolve your guild membership.", ephemeral: true);
            return;
        }

        if (!guildUser.GuildPermissions.Has(GuildPermission.ManageChannels)) {
            await slashCommand.RespondAsync("You need Manage Channels permission to use this command.", ephemeral: true);
            return;
        }

        switch (slashCommand.Data.Name) {
            case "lockdown":
                await HandleLockdownAsync(slashCommand);
                break;
            case "unlock":
                await HandleUnlockAsync(slashCommand);
                break;
            default:
                await slashCommand.RespondAsync("Unknown command.", ephemeral: true);
                break;
        }
    }

    /// <summary>
    /// Handles the /lockdown slash command by checking the current state and
    /// applying a lockdown across all text channels in the guild.
    /// </summary>
    private async Task HandleLockdownAsync(SocketSlashCommand command) {
        await command.DeferAsync(ephemeral: true);

        try {
            var isAlreadyLocked = await lockdownService.IsLockedDownAsync((long)command.GuildId!.Value);
            if (isAlreadyLocked) {
                await command.FollowupAsync("The server is already in lockdown.", ephemeral: true);
                return;
            }

            var count = await lockdownService.LockdownAsync(command.GuildId!.Value);
            await command.FollowupAsync(
                $"🔒 **Lockdown activated.** Denied Send Messages for @@everyone in {count} channel(s).",
                ephemeral: true);
        } catch (Exception ex) {
            logger.LogError(ex, "Failed to execute lockdown for guild {GuildId}", command.GuildId);
            await command.FollowupAsync("An error occurred while locking down the server.", ephemeral: true);
        }
    }

    /// <summary>
    /// Handles the /unlock slash command by checking the current state and
    /// lifting an active lockdown, restoring Send Messages permissions.
    /// </summary>
    private async Task HandleUnlockAsync(SocketSlashCommand command) {
        await command.DeferAsync(ephemeral: true);

        try {
            var isLocked = await lockdownService.IsLockedDownAsync((long)command.GuildId!.Value);
            if (!isLocked) {
                await command.FollowupAsync("The server is not currently in lockdown.", ephemeral: true);
                return;
            }

            var count = await lockdownService.UnlockAsync(command.GuildId!.Value);
            await command.FollowupAsync(
                $"🔓 **Lockdown lifted.** Restored Send Messages permissions in {count} channel(s).",
                ephemeral: true);
        } catch (Exception ex) {
            logger.LogError(ex, "Failed to execute unlock for guild {GuildId}", command.GuildId);
            await command.FollowupAsync("An error occurred while lifting the lockdown.", ephemeral: true);
        }
    }
}
