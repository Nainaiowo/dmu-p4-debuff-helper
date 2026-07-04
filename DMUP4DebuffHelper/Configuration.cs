using Dalamud.Configuration;
using DMUP3BlackholeHelper;
using System;

namespace DMUP4DebuffHelper;

public enum AssignmentChatChannel
{
    Say,
    Party,
    Alliance,
    FreeCompany,
    CrossWorldLinkshell1,
    CrossWorldLinkshell2,
    CrossWorldLinkshell3,
    CrossWorldLinkshell4,
    CrossWorldLinkshell5,
    CrossWorldLinkshell6,
    CrossWorldLinkshell7,
    CrossWorldLinkshell8,
}

public sealed record ChatChannelOption(AssignmentChatChannel Channel, string Label, string Command);

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 2;

    public bool ShowHelper { get; set; } = true;

    public bool PreviewWhenInactive { get; set; }

    public bool DebugChat { get; set; }

    public bool ShowOnlyWatchedStatuses { get; set; }

    public bool HelperCollapsed { get; set; }

    public bool PostBlackHoleInstructionsToChat { get; set; }

    public BlackHoleStrategyKind SelectedBlackHoleStrategy { get; set; } = BlackHoleStrategyKind.Standard;

    public int BlackHoleSoundEffectId { get; set; } = 1;

    public AssignmentChatChannel AssignmentChatChannel { get; set; } = AssignmentChatChannel.Party;

    public float HelperFontScale { get; set; } = 1.0f;

    public float HelperIconScale { get; set; } = 1.0f;

    public float HelperBackgroundOpacity { get; set; } = 1.0f;

    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}
