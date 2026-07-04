using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using P3 = DMUP3BlackholeHelper;

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

    public ConfigWindow(Plugin plugin) : base("DMU Helper###DMUP4DebuffConfig")
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
        if (!ImGui.BeginTabBar("##DMUHelperTabs"))
        {
            return;
        }

        if (ImGui.BeginTabItem("Settings"))
        {
            DrawSettingsTab();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("Review"))
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

        var showPreview = configuration.PreviewWhenInactive;
        if (ImGui.Checkbox("Preview when inactive", ref showPreview))
        {
            plugin.SetPreviewWhenInactive(showPreview);
        }

        var helperFontScale = configuration.HelperFontScale;
        if (ImGui.SliderFloat("Helper font scale", ref helperFontScale, 0.75f, 2.0f, "%.2f"))
        {
            plugin.SetHelperFontScale(helperFontScale);
        }

        var helperIconScale = configuration.HelperIconScale;
        if (ImGui.SliderFloat("Helper icon scale", ref helperIconScale, 0.75f, 3.0f, "%.2f"))
        {
            plugin.SetHelperIconScale(helperIconScale);
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
        ImGui.TextUnformatted("P3 Black Hole");
        DrawStrategySetting();
        DrawBlackHoleChatSettings();

        ImGui.Separator();
        ImGui.TextUnformatted("P4 Debuffs");
        DrawP4Settings();

        ImGui.Separator();
        ImGui.TextWrapped("The helper only scans while you are in DMU. P4 debuffs take display priority and clear stale P3 live display data when P4 starts.");
        ImGui.TextWrapped("P4 watches local debuffs and boss tell status 2056, then tags debuffs as real or fake when the tell param is detected.");
        ImGui.TextColored(ActiveTextColor, "If a P4 tell is not detected, the assignment stays Unknown instead of guessing.");
    }

    private void DrawStrategySetting()
    {
        var selectedOption = P3.BlackHoleStrategy.GetOption(configuration.SelectedBlackHoleStrategy);

        ImGui.SetNextItemWidth(180.0f);
        if (!ImGui.BeginCombo("Black Hole strat", selectedOption.Label))
        {
            return;
        }

        foreach (var option in P3.BlackHoleStrategy.Options)
        {
            var isSelected = option.Kind == selectedOption.Kind;
            if (ImGui.Selectable(option.Label, isSelected))
            {
                plugin.SetSelectedBlackHoleStrategy(option.Kind);
            }

            if (isSelected)
            {
                ImGui.SetItemDefaultFocus();
            }
        }

        ImGui.EndCombo();
    }

    private void DrawBlackHoleChatSettings()
    {
        var postInstructionsToChat = configuration.PostBlackHoleInstructionsToChat;
        if (ImGui.Checkbox("Post my BH job to chat", ref postInstructionsToChat))
        {
            plugin.SetPostBlackHoleInstructionsToChat(postInstructionsToChat);
        }

        if (!postInstructionsToChat)
        {
            return;
        }

        DrawChatChannelSetting();
        DrawSoundEffectSetting();
    }

    private void DrawChatChannelSetting()
    {
        var selectedChannel = P3BlackHoleTracker.GetChatChannelOption(configuration.AssignmentChatChannel);
        ImGui.SetNextItemWidth(180.0f);
        if (!ImGui.BeginCombo("Chat channel", selectedChannel.Label))
        {
            return;
        }

        foreach (var option in P3BlackHoleTracker.ChatChannelOptions)
        {
            var isSelected = option.Channel == selectedChannel.Channel;
            if (ImGui.Selectable(option.Label, isSelected))
            {
                plugin.SetAssignmentChatChannel(option.Channel);
            }

            if (isSelected)
            {
                ImGui.SetItemDefaultFocus();
            }
        }

        ImGui.EndCombo();
    }

    private void DrawSoundEffectSetting()
    {
        var selectedSound = P3BlackHoleTracker.SoundEffectOptions.FirstOrDefault(option => option.Id == configuration.BlackHoleSoundEffectId) ??
            P3BlackHoleTracker.SoundEffectOptions[0];

        ImGui.SetNextItemWidth(180.0f);
        if (ImGui.BeginCombo("Mechanic alert", selectedSound.Label))
        {
            foreach (var option in P3BlackHoleTracker.SoundEffectOptions)
            {
                var isSelected = option.Id == selectedSound.Id;
                if (ImGui.Selectable(option.Label, isSelected))
                {
                    plugin.SetBlackHoleSoundEffectId(option.Id);
                }

                if (isSelected)
                {
                    ImGui.SetItemDefaultFocus();
                }
            }

            ImGui.EndCombo();
        }

        ImGui.SameLine();
        var hasSound = selectedSound.Id != 0;
        if (!hasSound)
        {
            ImGui.BeginDisabled();
        }

        if (ImGui.Button("Test sound") && hasSound)
        {
            plugin.TestBlackHoleSoundEffect();
        }

        if (!hasSound)
        {
            ImGui.EndDisabled();
        }
    }

    private void DrawP4Settings()
    {
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
    }

    private void DrawBuffSummaryTab()
    {
        DrawP3Review();
        ImGui.Separator();
        ImGui.TextUnformatted("P4 Debuffs");

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

    private void DrawP3Review()
    {
        ImGui.TextUnformatted("P3 Black Hole");
        if (HasCurrentP3Summary())
        {
            DrawCurrentP3Summary();
        }
        else
        {
            ImGui.TextDisabled("Waiting for P3 Black Hole data.");
        }

        DrawP3PullHistory(plugin.P3BlackHole.PullSnapshots);
    }

    private bool HasCurrentP3Summary()
    {
        return plugin.P3BlackHole.CurrentAssignments.Any(assignment => assignment.HasLine) ||
            plugin.P3BlackHole.CurrentBlackHoleResolutions.Count > 0 ||
            plugin.P3BlackHole.CurrentPartyDeaths.Count > 0;
    }

    private void DrawCurrentP3Summary()
    {
        var header = $"Current P3 - Timer {FormatCombatTimer(plugin.P3BlackHole.CurrentPullElapsedSeconds)}###CurrentP3PullSummary";
        if (!ImGui.CollapsingHeader(header, ImGuiTreeNodeFlags.DefaultOpen))
        {
            return;
        }

        DrawP3AssignmentSummary(plugin.P3BlackHole.CurrentAssignments, "Current");
        DrawP3ResolutionSummary(plugin.P3BlackHole.CurrentBlackHoleResolutions, "Current");
        DrawP3DeathSummary(plugin.P3BlackHole.CurrentPartyDeaths, "Current");
    }

    private static void DrawP3PullHistory(IReadOnlyList<P3.BlackHolePullSnapshot> snapshots)
    {
        if (snapshots.Count == 0)
        {
            return;
        }

        ImGui.TextUnformatted("Recorded P3 pulls");
        for (var i = snapshots.Count - 1; i >= 0; i--)
        {
            var snapshot = snapshots[i];
            var header = $"P3 Pull {i + 1} - Timer {FormatCombatTimer(snapshot.CombatElapsedSeconds)}###P3PullSnapshot{i + 1}";
            if (!ImGui.CollapsingHeader(header))
            {
                continue;
            }

            ImGui.TextDisabled($"{snapshot.Reason} - {snapshot.CapturedAtUtc:HH:mm:ss} UTC");
            DrawP3AssignmentSummary(snapshot.Assignments, $"Pull{i + 1}");
            DrawP3ResolutionSummary(snapshot.Resolutions, $"Pull{i + 1}");
            DrawP3DeathSummary(snapshot.Deaths, $"Pull{i + 1}");
        }
    }

    private static void DrawP3AssignmentSummary(IReadOnlyList<P3.LocalPlayerBlackHoleAssignment> assignments, string idSuffix)
    {
        var lineAssignments = assignments
            .Where(assignment => assignment.HasLine)
            .OrderBy(assignment => assignment.PartyIndex)
            .ToList();
        ImGui.TextUnformatted("Assignments");
        if (lineAssignments.Count == 0)
        {
            ImGui.TextDisabled("No line assignments recorded.");
            return;
        }

        if (!ImGui.BeginTable($"##P3Assignments{idSuffix}", 3, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg))
        {
            return;
        }

        ImGui.TableSetupColumn("Player");
        ImGui.TableSetupColumn("Line");
        ImGui.TableSetupColumn("Role");
        ImGui.TableHeadersRow();

        foreach (var assignment in lineAssignments)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(assignment.MemberName);
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(assignment.LineName);
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(assignment.RoleName);
        }

        ImGui.EndTable();
    }

    private static void DrawP3ResolutionSummary(IReadOnlyList<P3.BlackHoleResolutionRecord> resolutions, string idSuffix)
    {
        ImGui.TextUnformatted("Black Hole hits");
        if (resolutions.Count == 0)
        {
            ImGui.TextDisabled("No Black Hole hit records.");
            return;
        }

        foreach (var resolution in resolutions.OrderBy(resolution => resolution.SeenAtUtc))
        {
            var hits = string.Join(", ", resolution.Hits.OrderBy(hit => hit.PartyIndex).Select(hit => hit.MemberName));
            ImGui.BulletText($"{resolution.Wave.Label}: {resolution.ActionName} -> {hits}");
        }
    }

    private static void DrawP3DeathSummary(IReadOnlyList<P3.PartyDeathRecord> deaths, string idSuffix)
    {
        ImGui.TextUnformatted("Deaths");
        if (deaths.Count == 0)
        {
            ImGui.TextDisabled("No P3 deaths recorded.");
            return;
        }

        if (!ImGui.BeginTable($"##P3Deaths{idSuffix}", 4, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg))
        {
            return;
        }

        ImGui.TableSetupColumn("Time");
        ImGui.TableSetupColumn("Player");
        ImGui.TableSetupColumn("Cause");
        ImGui.TableSetupColumn("Wave");
        ImGui.TableHeadersRow();

        foreach (var death in deaths.OrderBy(death => death.CombatElapsedSeconds))
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(FormatCombatTimer(death.CombatElapsedSeconds));
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(death.MemberName);
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(death.ActionName);
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(death.Wave?.Label ?? "--");
        }

        ImGui.EndTable();
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
