namespace Blackwall.Web.Components.Shared;

public sealed class TwitchChannelConfigForm {
    public bool IsEnabled { get; set; } = true;
    public bool IsDryRun { get; set; }
    public string CommandTrigger { get; set; } = "!";
}
