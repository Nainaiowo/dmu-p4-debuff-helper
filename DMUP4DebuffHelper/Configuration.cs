using Dalamud.Configuration;
using System;
using DMUP3BlackholeHelper;

namespace DMUP4DebuffHelper;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 3;

    public bool ShowHelper { get; set; } = true;

    public bool PreviewWhenInactive { get; set; }

    public bool DebugChat { get; set; }

    public bool HelperCollapsed { get; set; }

    public BlackHoleStrategyKind SelectedBlackHoleStrategy { get; set; } = BlackHoleStrategyKind.Standard;

    public float HelperFontScale { get; set; } = 1.0f;

    public float HelperIconScale { get; set; } = 1.0f;

    public float HelperBackgroundOpacity { get; set; } = 1.0f;

    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}
