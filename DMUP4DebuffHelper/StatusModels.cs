namespace DMUP4DebuffHelper;

using System;
using System.Collections.Generic;

public enum DmuHelperDisplayMode
{
    Empty,
    Preview,
    P3BlackHole,
    P4Debuffs,
}

public enum P4Boss
{
    Unknown,
    Kefka,
    NeoExdeath,
    Chaos,
}

public enum P4MechanicGroup
{
    Raw,
    GrandCross,
    Chaos,
    Flood,
}

public enum RealityState
{
    Unknown,
    Real,
    Fake,
}

public enum WoundColor
{
    None,
    Black,
    White,
}

public enum FloodSide
{
    None,
    Blue,
    Purple,
}

internal static class P4Flood
{
    public static FloodSide GetWoundSide(WoundColor woundColor)
    {
        return woundColor switch
        {
            WoundColor.Black => FloodSide.Blue,
            WoundColor.White => FloodSide.Purple,
            _ => FloodSide.None,
        };
    }

    public static FloodSide GetOppositeSide(FloodSide side)
    {
        return side switch
        {
            FloodSide.Blue => FloodSide.Purple,
            FloodSide.Purple => FloodSide.Blue,
            _ => FloodSide.None,
        };
    }

    public static WoundColor GetOppositeWound(WoundColor woundColor)
    {
        return woundColor switch
        {
            WoundColor.Black => WoundColor.White,
            WoundColor.White => WoundColor.Black,
            _ => WoundColor.None,
        };
    }

    public static WoundColor GetWoundColorForSide(FloodSide side)
    {
        return side switch
        {
            FloodSide.Blue => WoundColor.Black,
            FloodSide.Purple => WoundColor.White,
            _ => WoundColor.None,
        };
    }

    public static WoundColor GetStatusWoundColor(uint statusId)
    {
        return statusId switch
        {
            4888 => WoundColor.Black,
            4887 => WoundColor.White,
            _ => WoundColor.None,
        };
    }

    public static FloodSide ResolveSide(uint statusId, WoundColor woundColor)
    {
        var woundSide = GetWoundSide(woundColor);
        if (woundSide == FloodSide.None)
        {
            return FloodSide.None;
        }

        return statusId switch
        {
            454 => GetOppositeSide(woundSide),
            5464 => woundSide,
            _ => FloodSide.None,
        };
    }

    public static bool? UsesSameWound(uint statusId)
    {
        return statusId switch
        {
            454 => false,
            5464 => true,
            _ => null,
        };
    }

    public static WoundColor ResolveDestinationWound(uint statusId, WoundColor woundColor)
    {
        return statusId switch
        {
            454 => GetOppositeWound(woundColor),
            5464 => woundColor,
            _ => WoundColor.None,
        };
    }

    public static string FormatSide(FloodSide side)
    {
        return side switch
        {
            FloodSide.Blue => "Blue",
            FloodSide.Purple => "Purple",
            _ => "Unknown",
        };
    }

    public static string FormatWound(WoundColor woundColor)
    {
        return woundColor switch
        {
            WoundColor.Black => "Black",
            WoundColor.White => "White",
            _ => "Unknown",
        };
    }

    public static string FormatWoundDebuff(WoundColor woundColor)
    {
        return woundColor == WoundColor.None
            ? "Wound"
            : $"{FormatWound(woundColor)} Wound";
    }

    public static string FormatInstruction(uint statusId, WoundColor woundColor, FloodSide floodSide)
    {
        var woundText = woundColor == WoundColor.None
            ? "your Wound"
            : FormatWoundDebuff(woundColor);
        var relation = UsesSameWound(statusId) switch
        {
            true => "same as",
            false => "opposite",
            _ => null,
        };
        var destinationWound = ResolveDestinationWound(statusId, woundColor);
        if (destinationWound == WoundColor.None)
        {
            destinationWound = GetWoundColorForSide(floodSide);
        }

        var destinationText = destinationWound != WoundColor.None
            ? FormatWound(destinationWound).ToLowerInvariant()
            : null;

        return statusId switch
        {
            454 when destinationText is not null && relation is not null => $"Allagan Field: go {destinationText} ({relation} {woundText}).",
            454 => "Allagan Field: go opposite your Wound.",
            5464 when destinationText is not null && relation is not null => $"Beyond Death: go {destinationText} ({relation} {woundText}).",
            5464 => "Beyond Death: go same as your Wound.",
            4888 => "Black Wound.",
            4887 => "White Wound.",
            _ => "Tracked Flood debuff.",
        };
    }
}

public sealed record WatchedStatus(
    uint Id,
    string Name,
    P4Boss SourceBoss,
    P4MechanicGroup Group,
    int SortOrder);

public sealed record BossTellSnapshot(
    P4Boss Boss,
    P4MechanicGroup Group,
    uint StatusId,
    ushort Param,
    RealityState Reality,
    DateTime SeenAtUtc);

public sealed record PartyMemberSnapshot(
    string MemberKey,
    string MemberName,
    int PartyIndex,
    ulong ContentId,
    uint EntityId);

public sealed record PartyStatusEntry(
    string Key,
    string MemberKey,
    string MemberName,
    int PartyIndex,
    uint StatusId,
    string StatusName,
    uint IconId,
    float RemainingTime,
    bool IsWatched,
    int SortOrder,
    DateTime SeenAtUtc);

public sealed record P4DebuffAssignment(
    PartyStatusEntry Entry,
    WatchedStatus Rule,
    RealityState Reality,
    ushort? TellParam,
    string Instruction,
    WoundColor WoundColor = WoundColor.None,
    FloodSide FloodSide = FloodSide.None);

public sealed record CapturedDebuffState(
    RealityState Reality,
    ushort? TellParam,
    DateTime CapturedAtUtc);

public sealed record P4DebuffRecord(
    DateTime SeenAtUtc,
    string Key,
    string MemberKey,
    string MemberName,
    int PartyIndex,
    uint StatusId,
    string StatusName,
    P4Boss SourceBoss,
    P4MechanicGroup Group,
    RealityState Reality,
    ushort? TellParam,
    float RemainingTimeAtCapture,
    string Instruction,
    WoundColor WoundColor = WoundColor.None,
    FloodSide FloodSide = FloodSide.None);

public sealed record P4PullSnapshot(
    DateTime CapturedAtUtc,
    string Reason,
    float CombatElapsedSeconds,
    IReadOnlyList<P4DebuffRecord> Debuffs,
    IReadOnlyList<BossTellSnapshot> BossTells);
