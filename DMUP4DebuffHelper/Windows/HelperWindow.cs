using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Windowing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace DMUP4DebuffHelper.Windows;

public sealed class HelperWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private static readonly Vector4 GoldColor = new(1.0f, 0.78f, 0.18f, 1.0f);
    private static readonly Vector4 RealColor = new(0.25f, 0.85f, 1.0f, 1.0f);
    private static readonly Vector4 FakeColor = new(1.0f, 0.28f, 0.22f, 1.0f);
    private static readonly Vector4 UnknownColor = new(0.75f, 0.75f, 0.75f, 1.0f);
    private static readonly Vector4 UrgentColor = new(1.0f, 0.42f, 0.28f, 1.0f);
    private static readonly Vector4 PanelBorderColor = new(1.0f, 1.0f, 1.0f, 0.18f);
    private static readonly Vector4 PanelFillColor = new(0.02f, 0.025f, 0.03f, 0.32f);
    private static readonly Vector4 PreviewButtonColor = new(0.85f, 0.62f, 0.18f, 0.72f);
    private static readonly Vector4 PreviewButtonHoverColor = new(1.0f, 0.72f, 0.22f, 0.92f);
    private static readonly Vector4 PreviewButtonOffColor = new(0.18f, 0.18f, 0.2f, 0.78f);
    private static readonly Vector4 PreviewButtonOffHoverColor = new(0.25f, 0.25f, 0.28f, 0.92f);
    private const float HelperPadding = 10.0f;
    private const float AssignmentIconSize = 30.0f;

    public HelperWindow(Plugin plugin) : base("DMU P4 Debuff Helper###DMUP4DebuffHelper")
    {
        this.plugin = plugin;

        Size = new Vector2(390, 250);
        SizeCondition = ImGuiCond.FirstUseEver;
        BgAlpha = GetHelperBackgroundOpacity();
    }

    public void Dispose()
    {
    }

    public override void Draw()
    {
        BgAlpha = GetHelperBackgroundOpacity();
        ImGui.SetWindowFontScale(Math.Clamp(plugin.Configuration.HelperFontScale, 0.75f, 2.0f));

        var liveAssignments = plugin.IsInDmu
            ? GetOrderedAssignments(plugin.CurrentAssignments)
            : [];
        var isPreview = plugin.Configuration.PreviewWhenInactive && liveAssignments.Count == 0;
        var assignments = isPreview
            ? GetPreviewAssignments()
            : liveAssignments;

        DrawHeader(assignments.Count, isPreview);
        if (plugin.Configuration.HelperCollapsed)
        {
            return;
        }

        ImGui.Spacing();
        if (!plugin.IsInDmu && !isPreview)
        {
            ImGui.TextDisabled("Waiting for DMU.");
            return;
        }

        if (assignments.Count == 0)
        {
            ImGui.TextDisabled("Waiting for P4 debuffs.");
            return;
        }

        var nextAssignments = assignments.Take(2).ToList();
        if (nextAssignments.Any(IsUrgent))
        {
            ImGui.TextColored(UrgentColor, "Resolving soon");
        }

        DrawSection("Active", assignments, "Active");
        ImGui.Spacing();
        DrawSection("Next 2", nextAssignments, "Next");
    }

    private void DrawHeader(int assignmentCount, bool isPreview)
    {
        var isExpanded = !plugin.Configuration.HelperCollapsed;
        var titleBase = isPreview ? "P4 Debuff Helper Preview" : "P4 Debuff Helper";
        var title = assignmentCount > 0
            ? $"{titleBase} ({assignmentCount})"
            : titleBase;
        var frameHeight = ImGui.GetFrameHeight();
        var start = ImGui.GetCursorScreenPos();
        var hitSize = new Vector2(frameHeight, frameHeight);

        ImGui.InvisibleButton("##DMUP4HelperCollapse", hitSize);
        var hovered = ImGui.IsItemHovered();
        if (ImGui.IsItemClicked())
        {
            plugin.SetHelperCollapsed(isExpanded);
        }

        var drawList = ImGui.GetWindowDrawList();
        var arrowCenter = start + new Vector2(hitSize.X * 0.5f, hitSize.Y * 0.52f);
        var arrowSize = MathF.Max(5.0f, frameHeight * 0.22f);
        if (isExpanded)
        {
            drawList.AddTriangleFilled(
                arrowCenter + new Vector2(-arrowSize, -arrowSize * 0.45f),
                arrowCenter + new Vector2(arrowSize, -arrowSize * 0.45f),
                arrowCenter + new Vector2(0.0f, arrowSize * 0.65f),
                ImGui.GetColorU32(GoldColor));
        }
        else
        {
            drawList.AddTriangleFilled(
                arrowCenter + new Vector2(-arrowSize * 0.45f, -arrowSize),
                arrowCenter + new Vector2(-arrowSize * 0.45f, arrowSize),
                arrowCenter + new Vector2(arrowSize * 0.65f, 0.0f),
                ImGui.GetColorU32(GoldColor));
        }

        ImGui.SameLine(0.0f, 4.0f);
        ImGui.TextColored(GoldColor, title);
        DrawPreviewToggleButton();
        if (hovered)
        {
            ImGui.SetTooltip(isExpanded ? "Collapse P4 helper." : "Expand P4 helper.");
        }
    }

    private void DrawPreviewToggleButton()
    {
        var enabled = plugin.Configuration.PreviewWhenInactive;
        var label = enabled ? "Preview on" : "Preview off";
        var style = ImGui.GetStyle();
        var buttonWidth = MathF.Max(92.0f, ImGui.CalcTextSize(label).X + style.FramePadding.X * 2.0f + 8.0f);
        var contentRight = ImGui.GetWindowContentRegionMax().X;
        var currentLineStart = ImGui.GetCursorPosX();
        var targetX = MathF.Max(currentLineStart, contentRight - buttonWidth);

        ImGui.SameLine();
        ImGui.SetCursorPosX(targetX);
        ImGui.PushStyleColor(ImGuiCol.Button, enabled ? PreviewButtonColor : PreviewButtonOffColor);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, enabled ? PreviewButtonHoverColor : PreviewButtonOffHoverColor);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, enabled ? PreviewButtonHoverColor : PreviewButtonOffHoverColor);
        if (enabled)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.08f, 0.06f, 0.02f, 1.0f));
        }

        if (ImGui.Button(label, new Vector2(buttonWidth, 0.0f)))
        {
            plugin.SetPreviewWhenInactive(!enabled);
        }

        if (enabled)
        {
            ImGui.PopStyleColor();
        }

        ImGui.PopStyleColor(3);
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Shows sample P4 debuffs when no live DMU P4 data is available.");
        }
    }

    private List<P4DebuffAssignment> GetPreviewAssignments()
    {
        var previews = new (uint StatusId, RealityState Reality, ushort TellParam, float Time, string MemberName, int PartyIndex)[]
        {
            (5545, RealityState.Real, 1120, 6.8f, "Preview Player", 0),
            (5544, RealityState.Fake, 1119, 7.4f, "Preview Player", 0),
            (5548, RealityState.Real, 1122, 18.0f, "Preview Player", 0),
            (5547, RealityState.Fake, 1121, 23.2f, "Preview Player", 0),
        };

        var assignments = new List<P4DebuffAssignment>(previews.Length);
        foreach (var preview in previews)
        {
            if (!Plugin.WatchedStatuses.TryGetValue(preview.StatusId, out var rule))
            {
                continue;
            }

            var entry = new PartyStatusEntry(
                $"Preview:{preview.PartyIndex}:{preview.StatusId}",
                $"Preview:{preview.PartyIndex}",
                preview.MemberName,
                preview.PartyIndex,
                preview.StatusId,
                rule.Name,
                plugin.GetStatusIconId(preview.StatusId),
                preview.Time,
                true,
                rule.SortOrder,
                DateTime.UtcNow);

            assignments.Add(new P4DebuffAssignment(
                entry,
                rule,
                preview.Reality,
                preview.TellParam,
                "Preview only."));
        }

        return assignments;
    }

    private static List<P4DebuffAssignment> GetOrderedAssignments(IReadOnlyList<P4DebuffAssignment> assignments)
    {
        var ordered = assignments
            .Where(assignment => assignment.Entry.RemainingTime > 0.0f)
            .ToList();
        StableSortByTimerWithTolerance(ordered);
        return ordered;
    }

    private static void StableSortByTimerWithTolerance(List<P4DebuffAssignment> assignments)
    {
        for (var index = 1; index < assignments.Count; index++)
        {
            var current = assignments[index];
            var insertAt = index;
            while (insertAt > 0
                && current.Entry.RemainingTime + 1.0f < assignments[insertAt - 1].Entry.RemainingTime)
            {
                assignments[insertAt] = assignments[insertAt - 1];
                insertAt--;
            }

            assignments[insertAt] = current;
        }
    }

    private static void DrawSection(string label, IReadOnlyList<P4DebuffAssignment> assignments, string idSuffix)
    {
        ImGui.TextColored(GoldColor, label);
        var panelStart = ImGui.GetCursorScreenPos();
        var availableWidth = MathF.Max(160.0f, ImGui.GetContentRegionAvail().X);
        var panelWidth = availableWidth;
        var panelHeight = GetAssignmentPanelHeight(assignments, availableWidth);
        ImGui.GetWindowDrawList().AddRectFilled(
            panelStart,
            panelStart + new Vector2(panelWidth, panelHeight),
            ImGui.GetColorU32(PanelFillColor),
            6.0f);
        ImGui.GetWindowDrawList().AddRect(
            panelStart,
            panelStart + new Vector2(panelWidth, panelHeight),
            ImGui.GetColorU32(PanelBorderColor),
            6.0f);

        ImGui.SetCursorScreenPos(panelStart + new Vector2(HelperPadding, HelperPadding));
        DrawAssignmentStrip(assignments, availableWidth - HelperPadding * 2.0f, idSuffix);
        ImGui.SetCursorScreenPos(panelStart + new Vector2(0.0f, panelHeight + ImGui.GetStyle().ItemSpacing.Y));
    }

    private static float GetAssignmentPanelHeight(IReadOnlyList<P4DebuffAssignment> assignments, float availableWidth)
    {
        if (assignments.Count == 0)
        {
            return AssignmentIconSize + HelperPadding * 2.0f;
        }

        var rowHeight = GetAssignmentHeight();
        var spacingX = ImGui.GetStyle().ItemSpacing.X * 1.25f;
        var spacingY = ImGui.GetStyle().ItemSpacing.Y;
        var contentWidth = MathF.Max(1.0f, availableWidth - HelperPadding * 2.0f);
        var rowWidth = 0.0f;
        var rows = 1;
        foreach (var assignment in assignments)
        {
            var itemWidth = GetAssignmentWidth(assignment);
            if (rowWidth > 0.0f && rowWidth + spacingX + itemWidth > contentWidth)
            {
                rows++;
                rowWidth = itemWidth;
            }
            else
            {
                rowWidth += rowWidth > 0.0f ? spacingX + itemWidth : itemWidth;
            }
        }

        return HelperPadding * 2.0f + rows * rowHeight + MathF.Max(0, rows - 1) * spacingY;
    }

    private static void DrawAssignmentStrip(IReadOnlyList<P4DebuffAssignment> assignments, float availableWidth, string idSuffix)
    {
        if (assignments.Count == 0)
        {
            ImGui.TextDisabled("-");
            return;
        }

        var spacing = ImGui.GetStyle().ItemSpacing.X * 1.25f;
        var rowWidth = 0.0f;
        for (var index = 0; index < assignments.Count; index++)
        {
            var assignment = assignments[index];
            var itemWidth = GetAssignmentWidth(assignment);
            if (rowWidth > 0.0f && rowWidth + spacing + itemWidth > availableWidth)
            {
                rowWidth = 0.0f;
            }
            else if (rowWidth > 0.0f)
            {
                ImGui.SameLine(0.0f, spacing);
                rowWidth += spacing;
            }

            ImGui.PushID($"{idSuffix}{assignment.Entry.Key}{index}");
            DrawAssignment(assignment, itemWidth);
            ImGui.PopID();
            rowWidth += itemWidth;
        }
    }

    private static void DrawAssignment(P4DebuffAssignment assignment, float width)
    {
        var label = GetRealityLine(assignment);
        var timerText = FormatRemainingTime(assignment.Entry.RemainingTime);
        var labelSize = ImGui.CalcTextSize(label);
        var timerSize = ImGui.CalcTextSize(timerText);
        var start = ImGui.GetCursorPos();
        var labelColor = GetRealityColor(assignment.Reality);
        var borderColor = assignment.Reality == RealityState.Fake ? FakeColor : assignment.Reality == RealityState.Real ? GoldColor : UnknownColor;

        ImGui.BeginGroup();
        ImGui.SetCursorPosX(start.X + MathF.Max(0.0f, (width - labelSize.X) * 0.5f));
        ImGui.TextColored(labelColor, label);
        ImGui.SetCursorPosX(start.X + MathF.Max(0.0f, (width - AssignmentIconSize) * 0.5f));
        DrawStatusIconWithBorder(assignment.Entry.IconId, AssignmentIconSize, borderColor, FormatAssignmentTooltip(assignment));
        ImGui.SetCursorPosX(start.X + MathF.Max(0.0f, (width - timerSize.X) * 0.5f));
        ImGui.TextDisabled(timerText);
        ImGui.EndGroup();

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(FormatAssignmentTooltip(assignment));
        }
    }

    private static void DrawStatusIconWithBorder(uint iconId, float iconSize, Vector4 borderColor, string tooltip)
    {
        var size = new Vector2(iconSize, iconSize);
        var start = ImGui.GetCursorScreenPos();
        if (iconId == 0)
        {
            ImGui.Dummy(size);
        }
        else
        {
            try
            {
                var texture = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(iconId));
                var wrap = texture.GetWrapOrDefault();
                if (wrap is null)
                {
                    ImGui.Dummy(size);
                }
                else
                {
                    ImGui.Image(wrap.Handle, size);
                }
            }
            catch
            {
                ImGui.Dummy(size);
            }
        }

        ImGui.GetWindowDrawList().AddRect(
            start - new Vector2(1.0f),
            start + size + new Vector2(1.0f),
            ImGui.GetColorU32(borderColor),
            4.0f,
            ImDrawFlags.None,
            2.0f);

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(tooltip);
        }
    }

    private static float GetAssignmentWidth(P4DebuffAssignment assignment)
    {
        var label = GetRealityLine(assignment);
        var timerText = FormatRemainingTime(assignment.Entry.RemainingTime);
        return MathF.Max(
            AssignmentIconSize,
            MathF.Max(ImGui.CalcTextSize(label).X, ImGui.CalcTextSize(timerText).X)) + 8.0f;
    }

    private static float GetAssignmentHeight()
    {
        return ImGui.GetTextLineHeight() * 2.0f + AssignmentIconSize + ImGui.GetStyle().ItemSpacing.Y * 2.0f;
    }

    private float GetHelperBackgroundOpacity()
    {
        return Math.Clamp(plugin.Configuration.HelperBackgroundOpacity, 0.15f, 1.0f);
    }

    private static string FormatAssignmentTooltip(P4DebuffAssignment assignment)
    {
        var timerText = FormatRemainingTime(assignment.Entry.RemainingTime);
        var realityLine = assignment.Reality == RealityState.Unknown
            ? "Tell not captured."
            : GetRealityLine(assignment);
        return $"{assignment.Entry.MemberName}\n{assignment.Rule.Name}\nTimer: {timerText}\n{realityLine}\n{assignment.Instruction}";
    }

    private static string GetRealityLine(P4DebuffAssignment assignment)
    {
        return assignment.Reality switch
        {
            RealityState.Real => $"Real: {GetResolutionLabel(assignment)}",
            RealityState.Fake => $"Fake: {GetResolutionLabel(assignment)}",
            _ => "Unknown",
        };
    }

    private static string GetResolutionLabel(P4DebuffAssignment assignment)
    {
        return assignment.Rule.Id switch
        {
            5545 => assignment.Reality == RealityState.Real ? "Stack" : "Spread",
            5544 => assignment.Reality == RealityState.Real ? "Spread" : "Stack",
            5543 => assignment.Reality == RealityState.Real ? "Look away" : "Look toward",
            5546 => assignment.Reality == RealityState.Real ? "Stop" : "Move",
            5548 => assignment.Reality == RealityState.Real ? "Donut" : "Point-blank",
            5547 => assignment.Reality == RealityState.Real ? "Point-blank" : "Donut",
            454 => assignment.Reality == RealityState.Real ? "Opposite" : "Same",
            5464 => assignment.Reality == RealityState.Real ? "Same" : "Opposite",
            _ => FormatReality(assignment.Reality),
        };
    }

    private static string FormatRemainingTime(float remainingTime)
    {
        return remainingTime > 0.0f ? $"{remainingTime:0.0}s" : "-";
    }

    private static bool IsUrgent(P4DebuffAssignment assignment)
    {
        return assignment.Entry.RemainingTime > 0.0f && assignment.Entry.RemainingTime <= 10.0f;
    }

    private static Vector4 GetRealityColor(RealityState reality)
    {
        return reality switch
        {
            RealityState.Real => RealColor,
            RealityState.Fake => FakeColor,
            _ => UnknownColor,
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
}
