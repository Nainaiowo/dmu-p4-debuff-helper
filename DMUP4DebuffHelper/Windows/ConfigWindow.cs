using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace DMUP4DebuffHelper.Windows;

public sealed class ConfigWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private readonly Configuration configuration;
    private static readonly Vector4 ActiveTextColor = new(0.25f, 1.0f, 0.35f, 1.0f);
    private static readonly Vector4 RealTextColor = new(0.25f, 0.85f, 1.0f, 1.0f);
    private static readonly Vector4 FakeTextColor = new(1.0f, 0.65f, 0.2f, 1.0f);
    private static readonly Vector4 ErrorTextColor = new(1.0f, 0.25f, 0.25f, 1.0f);
    private static readonly Vector4 DisabledTextColor = new(0.65f, 0.65f, 0.65f, 1.0f);

    public ConfigWindow(Plugin plugin) : base("DMU P4 Debuff Helper###DMUP4DebuffConfig")
    {
        this.plugin = plugin;
        configuration = plugin.Configuration;

        Size = new Vector2(520, 360);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public void Dispose()
    {
    }

    public override void Draw()
    {
        if (!ImGui.BeginTabBar("##DMUP4DebuffTabs"))
        {
            return;
        }

        if (ImGui.BeginTabItem("Settings"))
        {
            DrawSettingsTab();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("Buff Summary"))
        {
            DrawBuffSummaryTab();
            ImGui.EndTabItem();
        }

        ImGui.EndTabBar();
    }

    private void DrawSettingsTab()
    {
        if (ImGui.Button("Open helper"))
        {
            plugin.OpenHelperUi();
        }

        var showHelper = configuration.ShowHelper;
        if (ImGui.Checkbox("Show helper window", ref showHelper))
        {
            plugin.SetShowHelper(showHelper);
        }

        var hasWatchedStatuses = Plugin.WatchedStatuses.Count > 0;
        var showOnlyWatched = configuration.ShowOnlyWatchedStatuses;
        if (!hasWatchedStatuses)
        {
            ImGui.BeginDisabled();
        }

        if (ImGui.Checkbox("Show only watched statuses", ref showOnlyWatched) && hasWatchedStatuses)
        {
            plugin.SetShowOnlyWatchedStatuses(showOnlyWatched);
        }

        if (!hasWatchedStatuses)
        {
            ImGui.EndDisabled();
            ImGui.SameLine();
            ImGui.TextDisabled("No watched P4 statuses configured yet.");
        }

        var helperFontScale = configuration.HelperFontScale;
        if (ImGui.SliderFloat("Helper font scale", ref helperFontScale, 0.75f, 2.0f, "%.2f"))
        {
            plugin.SetHelperFontScale(helperFontScale);
        }

        var helperBackgroundOpacity = configuration.HelperBackgroundOpacity;
        if (ImGui.SliderFloat("Helper background opacity", ref helperBackgroundOpacity, 0.15f, 1.0f, "%.2f"))
        {
            plugin.SetHelperBackgroundOpacity(helperBackgroundOpacity);
        }

        var debugChat = configuration.DebugChat;
        if (ImGui.Checkbox("Debug chat", ref debugChat))
        {
            plugin.SetDebugChat(debugChat);
        }

        ImGui.Separator();
        ImGui.TextWrapped("The helper only scans while you are in DMU.");
        ImGui.TextWrapped("It watches P4 party debuffs and boss tell status 2056, then tags debuffs as real or fake when the configured tell param is detected.");
        ImGui.TextWrapped("Debug chat prints boss tell params and each interpreted debuff assignment so the real/fake mapping can be verified in live pulls.");
        ImGui.TextColored(ActiveTextColor, "If a tell is not detected, the assignment stays Unknown instead of guessing.");
    }

    private void DrawBuffSummaryTab()
    {
        if (!plugin.IsInDmu)
        {
            ImGui.TextDisabled("Waiting for DMU.");
            DrawPullHistory(plugin.PullSnapshots);
            return;
        }

        if (HasCurrentPullSummary())
        {
            DrawCurrentPullSummary();
        }
        else
        {
            ImGui.TextDisabled("Waiting for P4 debuffs.");
        }

        DrawPullHistory(plugin.PullSnapshots);
    }

    private bool HasCurrentPullSummary()
    {
        return plugin.CurrentPullDebuffRecords.Count > 0 || plugin.CurrentPullBossTells.Count > 0;
    }

    private void DrawCurrentPullSummary()
    {
        var header = $"Current pull - Timer {FormatCombatTimer(plugin.CurrentPullElapsedSeconds)}###CurrentP4PullSummary";
        if (!ImGui.CollapsingHeader(header, ImGuiTreeNodeFlags.DefaultOpen))
        {
            return;
        }

        ImGui.TextWrapped("This records all party P4 debuffs seen this pull so the mechanic can be reviewed after a wipe.");
        DrawBossTellSummary(plugin.CurrentPullBossTells);
        ImGui.Separator();
        DrawDebuffRecordSummary(plugin.CurrentPullDebuffRecords, "Current");
    }

    private static void DrawPullHistory(IReadOnlyList<P4PullSnapshot> snapshots)
    {
        if (snapshots.Count == 0)
        {
            return;
        }

        ImGui.Separator();
        ImGui.TextUnformatted("Recorded pulls");
        for (var i = snapshots.Count - 1; i >= 0; i--)
        {
            DrawPullSnapshot(snapshots[i], i + 1);
        }
    }

    private static void DrawPullSnapshot(P4PullSnapshot snapshot, int pullNumber)
    {
        var header = $"Pull {pullNumber} - Timer {FormatCombatTimer(snapshot.CombatElapsedSeconds)}###P4PullSnapshot{pullNumber}";
        if (!ImGui.CollapsingHeader(header))
        {
            return;
        }

        ImGui.TextDisabled($"{snapshot.Reason} - {snapshot.CapturedAtUtc:HH:mm:ss} UTC");
        DrawBossTellSummary(snapshot.BossTells);
        ImGui.Separator();
        DrawDebuffRecordSummary(snapshot.Debuffs, $"Pull{pullNumber}");
    }

    private static void DrawBossTellSummary(IReadOnlyList<BossTellSnapshot> bossTells)
    {
        ImGui.TextUnformatted("Boss tells");
        if (bossTells.Count == 0)
        {
            ImGui.TextDisabled("No boss tells recorded.");
            return;
        }

        foreach (var tell in bossTells.OrderBy(tell => tell.SeenAtUtc))
        {
            ImGui.PushStyleColor(ImGuiCol.Text, GetRealityColor(tell.Reality));
            ImGui.BulletText($"{tell.SeenAtUtc:HH:mm:ss} - {FormatBoss(tell.Boss)} {FormatGroup(tell.Group)}: {tell.Param} ({FormatReality(tell.Reality)})");
            ImGui.PopStyleColor();
        }
    }

    private static void DrawDebuffRecordSummary(IReadOnlyList<P4DebuffRecord> records, string idSuffix)
    {
        ImGui.TextUnformatted("Debuff records");
        if (records.Count == 0)
        {
            ImGui.TextDisabled("No P4 debuffs recorded.");
            return;
        }

        var unknownCount = records.Count(record => record.Reality == RealityState.Unknown);
        if (unknownCount > 0)
        {
            ImGui.TextColored(ErrorTextColor, $"{unknownCount} debuff record(s) were Unknown because no matching boss tell was captured.");
        }

        foreach (var group in records
            .OrderBy(record => record.Group)
            .ThenBy(record => record.SeenAtUtc)
            .GroupBy(record => record.Group))
        {
            ImGui.TextUnformatted($"{FormatGroup(group.Key)} ({group.Count()})");

            if (!ImGui.BeginTable($"##P4DebuffRecordSummary{idSuffix}{group.Key}", 6, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg))
            {
                continue;
            }

            ImGui.TableSetupColumn("Player");
            ImGui.TableSetupColumn("Debuff");
            ImGui.TableSetupColumn("State");
            ImGui.TableSetupColumn("Tell");
            ImGui.TableSetupColumn("Timer");
            ImGui.TableSetupColumn("Instruction");
            ImGui.TableHeadersRow();

            foreach (var record in group
                .OrderBy(record => record.SeenAtUtc)
                .ThenBy(record => record.PartyIndex)
                .ThenBy(record => record.StatusId))
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(record.MemberName);
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(record.StatusName);
                ImGui.TableNextColumn();
                ImGui.PushStyleColor(ImGuiCol.Text, GetRealityColor(record.Reality));
                ImGui.TextUnformatted(FormatReality(record.Reality));
                ImGui.PopStyleColor();
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(record.TellParam?.ToString() ?? "--");
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(FormatTime(record.RemainingTimeAtCapture));
                ImGui.TableNextColumn();
                ImGui.TextWrapped(record.Instruction);
            }

            ImGui.EndTable();
        }
    }

    private static Vector4 GetRealityColor(RealityState reality)
    {
        return reality switch
        {
            RealityState.Real => RealTextColor,
            RealityState.Fake => FakeTextColor,
            RealityState.Unknown => ErrorTextColor,
            _ => DisabledTextColor,
        };
    }

    private static string FormatReality(RealityState reality)
    {
        return reality switch
        {
            RealityState.Real => "Real",
            RealityState.Fake => "Fake",
            _ => "Unknown",
        };
    }

    private static string FormatBoss(P4Boss boss)
    {
        return boss switch
        {
            P4Boss.NeoExdeath => "Neo Exdeath",
            P4Boss.Chaos => "Chaos",
            P4Boss.Kefka => "Kefka",
            _ => "Unknown boss",
        };
    }

    private static string FormatGroup(P4MechanicGroup group)
    {
        return group switch
        {
            P4MechanicGroup.GrandCross => "Grand Cross",
            P4MechanicGroup.Chaos => "Chaos",
            P4MechanicGroup.Flood => "Flood",
            _ => "Raw",
        };
    }

    private static string FormatTime(float remainingTime)
    {
        return remainingTime > 0.0f ? $"{remainingTime:0.0}s" : "--";
    }

    private static string FormatCombatTimer(float elapsedSeconds)
    {
        var totalSeconds = (int)MathF.Max(0.0f, elapsedSeconds);
        return $"{totalSeconds / 60:00}:{totalSeconds % 60:00}";
    }
}
