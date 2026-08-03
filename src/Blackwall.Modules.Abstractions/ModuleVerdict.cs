namespace Blackwall.Modules.Abstractions;

public sealed record ModuleVerdict(
    string ViolationType,
    ModuleAction Action,
    int TimeoutMinutes,
    int DeleteDays,
    bool AutoLockdown,
    string? Reason
);
