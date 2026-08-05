namespace Blackwall.Web.Components.Shared;

public sealed class TwitchChannelConfigForm {
    public bool IsEnabled { get; set; } = true;
    public bool IsDryRun { get; set; }
    public bool AutoAddManagers { get; set; } = true;
    public string CommandTrigger { get; set; } = "!";
}
