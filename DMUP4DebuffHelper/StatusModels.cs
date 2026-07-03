namespace DMUP4DebuffHelper;

using System;
using System.Collections.Generic;

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
    string Instruction);

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
    string Instruction);

public sealed record P4PullSnapshot(
    DateTime CapturedAtUtc,
    string Reason,
    float CombatElapsedSeconds,
    IReadOnlyList<P4DebuffRecord> Debuffs,
    IReadOnlyList<BossTellSnapshot> BossTells);
