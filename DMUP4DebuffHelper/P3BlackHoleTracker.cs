namespace DMUP4DebuffHelper;

using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.System.String;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Shell;
using Lumina.Excel.Sheets;
using P3 = DMUP3BlackholeHelper;
using System;
using System.Collections.Generic;
using System.Linq;

public sealed class P3BlackHoleTracker
{
    private const float BlackHoleResolutionWindowSeconds = 4.0f;
    private const float DeathCauseFreshnessSeconds = 15.0f;
    private const double StatusPulseDebounceSeconds = 2.0;
    private const double LiveDisplayGraceSeconds = 2.5;
    private const ushort ChatGreenColorKey = 45;
    private const int NoSoundEffectId = 0;
    private const int QueuedChatDelayMs = 750;

    private static readonly IReadOnlySet<uint> BlackHoleResolutionActionIds = new HashSet<uint>
    {
        47868, // Nothingness
        48333, // Black Spark
    };

    public static readonly IReadOnlyList<ChatChannelOption> ChatChannelOptions =
    [
        new(AssignmentChatChannel.Say, "Say", "/s"),
        new(AssignmentChatChannel.Party, "Party", "/p"),
        new(AssignmentChatChannel.Alliance, "Alliance", "/alliance"),
        new(AssignmentChatChannel.FreeCompany, "Free Company", "/fc"),
        new(AssignmentChatChannel.CrossWorldLinkshell1, "Cross-world Linkshell 1", "/cwl1"),
        new(AssignmentChatChannel.CrossWorldLinkshell2, "Cross-world Linkshell 2", "/cwl2"),
        new(AssignmentChatChannel.CrossWorldLinkshell3, "Cross-world Linkshell 3", "/cwl3"),
        new(AssignmentChatChannel.CrossWorldLinkshell4, "Cross-world Linkshell 4", "/cwl4"),
        new(AssignmentChatChannel.CrossWorldLinkshell5, "Cross-world Linkshell 5", "/cwl5"),
        new(AssignmentChatChannel.CrossWorldLinkshell6, "Cross-world Linkshell 6", "/cwl6"),
        new(AssignmentChatChannel.CrossWorldLinkshell7, "Cross-world Linkshell 7", "/cwl7"),
        new(AssignmentChatChannel.CrossWorldLinkshell8, "Cross-world Linkshell 8", "/cwl8"),
    ];

    public static readonly IReadOnlyList<P3.SoundEffectOption> SoundEffectOptions =
        new[] { new P3.SoundEffectOption(NoSoundEffectId, "None") }
            .Concat(Enumerable.Range(1, 16).Select(id => new P3.SoundEffectOption(id, $"<se.{id}>")))
            .ToList();

    internal static readonly IReadOnlyDictionary<uint, P3.WatchedStatus> WatchedStatuses =
        new Dictionary<uint, P3.WatchedStatus>
        {
            [3004] = new(3004, "First in Line", P3.StatusKind.LineOrder, 10),
            [3005] = new(3005, "Second in Line", P3.StatusKind.LineOrder, 20),
            [3006] = new(3006, "Third in Line", P3.StatusKind.LineOrder, 30),
            [1604] = new(1604, "Accretion", P3.StatusKind.Accretion, 40),
            [5454] = new(5454, "Primordial Crust", P3.StatusKind.PrimordialCrust, 45),
            [5452] = new(5452, "Unbecoming", P3.StatusKind.BlackHole, 46),
            [5453] = new(5453, "Meanest Existence", P3.StatusKind.BlackHole, 47),
            [1053] = new(1053, "Earth Resistance Down II", P3.StatusKind.EarthResistance, 50),
            [2097] = new(2097, "Earth Resistance Down II", P3.StatusKind.EarthResistance, 50),
            [3372] = new(3372, "Earth Resistance Down II", P3.StatusKind.EarthResistance, 50),
        };

    private readonly Plugin plugin;
    private readonly List<P3.PartyStatusEntry> currentEntries = [];
    private readonly List<P3.PartyMemberSnapshot> currentMembers = [];
    private readonly List<P3.LocalPlayerBlackHoleAssignment> currentAssignments = [];
    private readonly List<P3.BlackHoleResolutionRecord> currentBlackHoleResolutions = [];
    private readonly List<P3.PartyDeathRecord> currentPartyDeaths = [];
    private readonly List<P3.BlackHolePullSnapshot> pullSnapshots = [];
    private readonly HashSet<string> accretionHistory = new(StringComparer.Ordinal);
    private readonly Dictionary<string, P3.PartyStatusEntry> lineHistory = new(StringComparer.Ordinal);
    private readonly Dictionary<string, P3.PartyDeathRecord> lastIncomingActionByMember = new(StringComparer.Ordinal);
    private readonly Dictionary<string, P3.PartyStatusEntry> debugKnownEntries = new(StringComparer.Ordinal);
    private readonly Dictionary<uint, string> actionNameCache = new();
    private readonly HashSet<string> capturedBlackHoleResolutionKeys = new(StringComparer.Ordinal);
    private readonly HashSet<string> deadMemberKeys = new(StringComparer.Ordinal);
    private readonly HashSet<int> observedBlackHoleWaveKeys = [];
    private readonly HashSet<string> activePrimordialCrustKeys = new(StringComparer.Ordinal);
    private readonly HashSet<string> activeEarthResistanceKeys = new(StringComparer.Ordinal);
    private readonly HashSet<int> postedNowInstructionCalloutWaveKeys = [];
    private readonly Queue<QueuedChatMessage> queuedChatMessages = [];
    private DateTime? blackHoleStartedAtUtc;
    private DateTime? lastBlackHoleSeenAtUtc;
    private DateTime? lastLiveSignalSeenAtUtc;
    private DateTime lastPrimordialCrustPulseAtUtc = DateTime.MinValue;
    private DateTime lastEarthResistancePulseAtUtc = DateTime.MinValue;
    private DateTime nextQueuedChatMessageAtUtc = DateTime.MinValue;
    private int primordialCrustPulseCount;
    private int earthPulseCount;
    private float lastKnownBlackHoleElapsedSeconds;
    private bool debugRecognizedTerritory;

    public P3BlackHoleTracker(Plugin plugin)
    {
        this.plugin = plugin;
    }

    public IReadOnlyList<P3.PartyStatusEntry> CurrentEntries => currentEntries;

    public IReadOnlyList<P3.LocalPlayerBlackHoleAssignment> CurrentAssignments => currentAssignments;

    public IReadOnlyList<P3.BlackHoleResolutionRecord> CurrentBlackHoleResolutions => currentBlackHoleResolutions;

    public IReadOnlyList<P3.PartyDeathRecord> CurrentPartyDeaths => currentPartyDeaths;

    public IReadOnlyList<P3.BlackHolePullSnapshot> PullSnapshots => pullSnapshots;

    public P3.LocalPlayerBlackHoleAssignment? LocalAssignment { get; private set; }

    public P3.BlackHoleMechanicState MechanicState { get; private set; } = P3.BlackHoleMechanicState.Inactive;

    public float CurrentPullElapsedSeconds => MechanicState.IsActive
        ? MechanicState.ElapsedSeconds
        : lastKnownBlackHoleElapsedSeconds;

    public bool HasLiveSignal
    {
        get
        {
            if (currentEntries.Count > 0 ||
                MechanicState.IsActive ||
                LocalAssignment?.HasLine == true)
            {
                return true;
            }

            return lastLiveSignalSeenAtUtc is { } seenAt &&
                (DateTime.UtcNow - seenAt).TotalSeconds <= LiveDisplayGraceSeconds;
        }
    }

    public void Refresh(bool isInDmu, bool suppressLiveForP4)
    {
        if (!isInDmu)
        {
            LeaveDmu();
            return;
        }

        if (suppressLiveForP4)
        {
            ClearLiveDisplay("P4 detected");
            UpdateDebugState(inDmu: true, []);
            return;
        }

        RefreshStatusSnapshot();
    }

    public void FlushQueuedChatMessages(DateTime now)
    {
        if (queuedChatMessages.Count == 0 || nextQueuedChatMessageAtUtc > now)
        {
            return;
        }

        var nextMessage = queuedChatMessages.Dequeue();
        SendChat(nextMessage.Channel, nextMessage.Message);
        nextQueuedChatMessageAtUtc = now.AddMilliseconds(QueuedChatDelayMs);
    }

    public void OnDutyReset()
    {
        CaptureCurrentPullSnapshot("Wipe/reset detected");
        ClearCurrentState();
        ResetBlackHoleState(resetInstructionCallouts: true);
    }

    public void ClearLiveDisplay(string reason)
    {
        CaptureCurrentPullSnapshot(reason);
        ClearCurrentState();
        ResetBlackHoleState(resetInstructionCallouts: true);
    }

    public void PrintAssignmentToChat(P3.LocalPlayerBlackHoleAssignment assignment, string pullLabel)
    {
        QueueChat(
            plugin.Configuration.AssignmentChatChannel,
            $"[DMU Helper] {pullLabel} assignment: {assignment.MemberName} - {assignment.RoleName}");
    }

    public void TestSoundEffect()
    {
        PrintSoundEffectEcho(plugin.Configuration.BlackHoleSoundEffectId);
    }

    public void ResetInstructionChatCallouts()
    {
        postedNowInstructionCalloutWaveKeys.Clear();
    }

    public unsafe void ProcessActionEffect(
        uint casterEntityId,
        ActionEffectHandler.Header* header,
        GameObjectId* targetEntityIds)
    {
        try
        {
            CapturePartyTargetActions(casterEntityId, header, targetEntityIds);
            CaptureBlackHoleResolution(casterEntityId, header, targetEntityIds);
        }
        catch (Exception ex)
        {
            Plugin.Log.Debug(ex, "Could not process action effect for P3 Black Hole.");
        }
    }

    private void LeaveDmu()
    {
        CaptureCurrentPullSnapshot("Left DMU");
        ClearCurrentState();
        ResetBlackHoleState(resetInstructionCallouts: true);
        UpdateDebugState(inDmu: false, []);
    }

    private void ClearCurrentState()
    {
        currentEntries.Clear();
        currentMembers.Clear();
        currentAssignments.Clear();
        currentBlackHoleResolutions.Clear();
        currentPartyDeaths.Clear();
        capturedBlackHoleResolutionKeys.Clear();
        lastIncomingActionByMember.Clear();
        deadMemberKeys.Clear();
        LocalAssignment = null;
        accretionHistory.Clear();
        lineHistory.Clear();
        lastLiveSignalSeenAtUtc = null;
    }

    private void RefreshStatusSnapshot()
    {
        var nextEntries = new List<P3.PartyStatusEntry>();
        var nextMembers = new List<P3.PartyMemberSnapshot>();
        var nextDeathStates = new List<(string MemberKey, string MemberName, int PartyIndex, bool IsDead)>();
        var partyIndex = 0;
        foreach (var member in Plugin.PartyList)
        {
            var memberName = member.Name.TextValue;
            var memberKey = member.ContentId != 0
                ? member.ContentId.ToString("X16")
                : $"{memberName}:{partyIndex}";
            var isDps = IsDpsJob(member.ClassJob.RowId);
            nextMembers.Add(new P3.PartyMemberSnapshot(
                memberKey,
                memberName,
                partyIndex,
                isDps,
                member.ContentId,
                member.EntityId));
            var isDead = member.GameObject?.IsDead == true ||
                (member.MaxHP > 0 && member.CurrentHP == 0);
            nextDeathStates.Add((memberKey, memberName, partyIndex, isDead));

            foreach (var status in member.Statuses)
            {
                if (!WatchedStatuses.TryGetValue(status.StatusId, out var watchedStatus))
                {
                    continue;
                }

                nextEntries.Add(new P3.PartyStatusEntry(
                    $"{memberKey}:{status.StatusId}",
                    memberKey,
                    memberName,
                    partyIndex,
                    isDps,
                    status.StatusId,
                    watchedStatus.Name,
                    watchedStatus.Kind,
                    status.RemainingTime,
                    watchedStatus.SortOrder));
            }

            partyIndex++;
        }

        if (nextEntries.Count > 0)
        {
            lastLiveSignalSeenAtUtc = DateTime.UtcNow;
        }

        foreach (var entry in nextEntries.Where(entry => entry.Kind == P3.StatusKind.Accretion))
        {
            accretionHistory.Add(entry.MemberKey);
        }

        foreach (var entry in nextEntries.Where(entry => entry.Kind == P3.StatusKind.LineOrder))
        {
            lineHistory[entry.MemberKey] = entry;
        }

        currentMembers.Clear();
        currentMembers.AddRange(nextMembers.OrderBy(member => member.PartyIndex));

        currentEntries.Clear();
        currentEntries.AddRange(nextEntries
            .OrderBy(entry => entry.SortOrder)
            .ThenBy(entry => entry.PartyIndex)
            .ThenBy(entry => entry.MemberName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.StatusId));

        currentAssignments.Clear();
        currentAssignments.AddRange(currentMembers.Select(member => BuildAssignmentForMember(member, currentEntries, lineHistory, accretionHistory)));

        LocalAssignment = BuildLocalAssignment();
        UpdateBlackHoleState(currentEntries);
        UpdatePartyDeathTimeline(nextDeathStates);
        UpdateInstructionChatCallout();
        UpdateDebugState(inDmu: true, currentEntries);
    }

    private void UpdateBlackHoleState(IReadOnlyList<P3.PartyStatusEntry> entries)
    {
        var now = DateTime.UtcNow;
        var hasPrimordialCrust = entries.Any(entry => entry.StatusId == 5454);
        var hasStartMarker = entries.Any(entry => entry.Kind == P3.StatusKind.LineOrder || entry.StatusId == 5454);
        var hasMechanicMarker = entries.Any(entry =>
            entry.Kind is P3.StatusKind.LineOrder or
                P3.StatusKind.Accretion or
                P3.StatusKind.EarthResistance or
                P3.StatusKind.PrimordialCrust or
                P3.StatusKind.BlackHole);

        if (hasStartMarker && blackHoleStartedAtUtc is null)
        {
            blackHoleStartedAtUtc = now;
            lastBlackHoleSeenAtUtc = now;
            primordialCrustPulseCount = 0;
            earthPulseCount = 0;
            observedBlackHoleWaveKeys.Clear();
            activePrimordialCrustKeys.Clear();
            activeEarthResistanceKeys.Clear();
            lastPrimordialCrustPulseAtUtc = DateTime.MinValue;
            lastEarthResistancePulseAtUtc = DateTime.MinValue;
            lastKnownBlackHoleElapsedSeconds = 0.0f;
        }

        if (hasMechanicMarker)
        {
            lastBlackHoleSeenAtUtc = now;
            lastLiveSignalSeenAtUtc = now;
        }

        var currentPrimordialCrustKeys = entries
            .Where(entry => entry.StatusId == 5454)
            .Select(entry => entry.Key)
            .ToHashSet(StringComparer.Ordinal);
        var hasNewPrimordialCrust = currentPrimordialCrustKeys
            .Any(key => !activePrimordialCrustKeys.Contains(key));
        activePrimordialCrustKeys.Clear();
        activePrimordialCrustKeys.UnionWith(currentPrimordialCrustKeys);

        if (blackHoleStartedAtUtc is not null &&
            hasNewPrimordialCrust &&
            IsDebouncedPulse(now, lastPrimordialCrustPulseAtUtc))
        {
            lastPrimordialCrustPulseAtUtc = now;
            primordialCrustPulseCount = Math.Max(primordialCrustPulseCount + 1, earthPulseCount + 1);
            if (P3.BlackHoleTimeline.GetWaveByPulseCount(primordialCrustPulseCount) is { } markerWave)
            {
                observedBlackHoleWaveKeys.Add(markerWave.Key);
                CalibrateBlackHoleClock(now, markerWave.StartsAtSeconds);
            }
        }

        var currentEarthResistanceKeys = entries
            .Where(entry => entry.Kind == P3.StatusKind.EarthResistance)
            .Select(entry => entry.Key)
            .ToHashSet(StringComparer.Ordinal);
        var hasNewEarthResistance = currentEarthResistanceKeys
            .Any(key => !activeEarthResistanceKeys.Contains(key));
        activeEarthResistanceKeys.Clear();
        activeEarthResistanceKeys.UnionWith(currentEarthResistanceKeys);

        if (blackHoleStartedAtUtc is not null &&
            hasNewEarthResistance &&
            IsDebouncedPulse(now, lastEarthResistancePulseAtUtc))
        {
            lastEarthResistancePulseAtUtc = now;
            earthPulseCount++;
            if (P3.BlackHoleTimeline.GetWaveByPulseCount(earthPulseCount) is { } resolvedWave)
            {
                CalibrateBlackHoleClock(now, resolvedWave.MarkerAtSeconds);
            }
        }

        if (blackHoleStartedAtUtc is null)
        {
            MechanicState = P3.BlackHoleMechanicState.Inactive;
            return;
        }

        var elapsedSeconds = (float)(now - blackHoleStartedAtUtc.Value).TotalSeconds;
        lastKnownBlackHoleElapsedSeconds = elapsedSeconds;
        if (!hasMechanicMarker &&
            lastBlackHoleSeenAtUtc is not null &&
            (now - lastBlackHoleSeenAtUtc.Value).TotalSeconds > LiveDisplayGraceSeconds)
        {
            ResetBlackHoleState();
            return;
        }

        var timelineCurrentWave = P3.BlackHoleTimeline.GetCurrentWave(elapsedSeconds);
        var observedCurrentWave = GetObservedCurrentWave(timelineCurrentWave, elapsedSeconds);
        var currentWave = observedCurrentWave ??
            (ShouldExposeCurrentWave(timelineCurrentWave, elapsedSeconds) ? timelineCurrentWave : null);
        var nextWave = GetObservedNextWave(currentWave) ??
            (currentWave is null && timelineCurrentWave is not null
                ? timelineCurrentWave
                : P3.BlackHoleTimeline.GetNextWave(elapsedSeconds));
        var lastResolvedWave = P3.BlackHoleTimeline.GetWaveByPulseCount(earthPulseCount);
        MechanicState = new P3.BlackHoleMechanicState(
            IsActive: true,
            ElapsedSeconds: elapsedSeconds,
            CurrentWave: currentWave,
            NextWave: nextWave,
            LastResolvedWave: lastResolvedWave,
            EarthPulseCount: earthPulseCount);
    }

    private void ResetBlackHoleState(bool resetInstructionCallouts = false)
    {
        blackHoleStartedAtUtc = null;
        lastBlackHoleSeenAtUtc = null;
        primordialCrustPulseCount = 0;
        earthPulseCount = 0;
        observedBlackHoleWaveKeys.Clear();
        activePrimordialCrustKeys.Clear();
        activeEarthResistanceKeys.Clear();
        lastPrimordialCrustPulseAtUtc = DateTime.MinValue;
        lastEarthResistancePulseAtUtc = DateTime.MinValue;
        if (resetInstructionCallouts)
        {
            ResetInstructionChatCallouts();
        }

        MechanicState = P3.BlackHoleMechanicState.Inactive;
    }

    private static bool IsDebouncedPulse(DateTime now, DateTime lastPulseAtUtc)
    {
        return lastPulseAtUtc == DateTime.MinValue ||
            (now - lastPulseAtUtc).TotalSeconds >= StatusPulseDebounceSeconds;
    }

    private void CalibrateBlackHoleClock(DateTime now, float anchorElapsedSeconds)
    {
        if (blackHoleStartedAtUtc is null)
        {
            return;
        }

        blackHoleStartedAtUtc = now - TimeSpan.FromSeconds(anchorElapsedSeconds);
        lastKnownBlackHoleElapsedSeconds = anchorElapsedSeconds;
    }

    private bool ShouldExposeCurrentWave(P3.BlackHoleWave? wave, float elapsedSeconds)
    {
        if (wave is null)
        {
            return false;
        }

        if (observedBlackHoleWaveKeys.Contains(wave.Key))
        {
            return true;
        }

        return elapsedSeconds >= wave.StartsAtSeconds;
    }

    private P3.BlackHoleWave? GetObservedCurrentWave(P3.BlackHoleWave? timelineCurrentWave, float elapsedSeconds)
    {
        if (primordialCrustPulseCount > earthPulseCount &&
            P3.BlackHoleTimeline.GetWaveByPulseCount(primordialCrustPulseCount) is { } markerWave &&
            elapsedSeconds < markerWave.EndsAtSeconds)
        {
            return markerWave;
        }

        if (timelineCurrentWave is not null && observedBlackHoleWaveKeys.Contains(timelineCurrentWave.Key))
        {
            return timelineCurrentWave;
        }

        return null;
    }

    private P3.BlackHoleWave? GetObservedNextWave(P3.BlackHoleWave? currentWave)
    {
        if (currentWave is not null)
        {
            return P3.BlackHoleTimeline.GetWaveByPulseCount(GetWavePulseNumber(currentWave) + 1);
        }

        var nextPulseCount = Math.Max(primordialCrustPulseCount, earthPulseCount) + 1;
        return P3.BlackHoleTimeline.GetWaveByPulseCount(nextPulseCount);
    }

    private static int GetWavePulseNumber(P3.BlackHoleWave wave)
    {
        var index = P3.BlackHoleTimeline.Waves
            .Select((timelineWave, timelineIndex) => new { timelineWave, timelineIndex })
            .FirstOrDefault(entry => entry.timelineWave.Key == wave.Key)
            ?.timelineIndex;
        return index is null ? 0 : index.Value + 1;
    }

    private void UpdatePartyDeathTimeline(IReadOnlyList<(string MemberKey, string MemberName, int PartyIndex, bool IsDead)> deathStates)
    {
        var currentMemberKeys = deathStates
            .Select(state => state.MemberKey)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var staleKey in deadMemberKeys.Where(key => !currentMemberKeys.Contains(key)).ToList())
        {
            deadMemberKeys.Remove(staleKey);
        }

        var shouldRecordDeaths = blackHoleStartedAtUtc is not null ||
            currentBlackHoleResolutions.Count > 0 ||
            lineHistory.Count > 0 ||
            accretionHistory.Count > 0;

        foreach (var state in deathStates.OrderBy(state => state.PartyIndex))
        {
            if (!state.IsDead)
            {
                deadMemberKeys.Remove(state.MemberKey);
                continue;
            }

            if (!deadMemberKeys.Add(state.MemberKey) || !shouldRecordDeaths)
            {
                continue;
            }

            currentPartyDeaths.Add(CreatePartyDeathRecord(state.MemberKey, state.MemberName, state.PartyIndex));
        }
    }

    private P3.PartyDeathRecord CreatePartyDeathRecord(string memberKey, string memberName, int partyIndex)
    {
        var now = DateTime.UtcNow;
        var elapsedSeconds = blackHoleStartedAtUtc is not null
            ? (float)(now - blackHoleStartedAtUtc.Value).TotalSeconds
            : lastKnownBlackHoleElapsedSeconds;
        var wave = P3.BlackHoleTimeline.GetCurrentWave(elapsedSeconds) ??
            P3.BlackHoleTimeline.GetNearestWaveByMarkerTime(elapsedSeconds, 8.0f);

        if (!lastIncomingActionByMember.TryGetValue(memberKey, out var cause) ||
            (now - cause.SeenAtUtc).TotalSeconds > DeathCauseFreshnessSeconds)
        {
            return new P3.PartyDeathRecord(
                now,
                elapsedSeconds,
                memberKey,
                memberName,
                partyIndex,
                0,
                "Unknown",
                string.Empty,
                wave);
        }

        return new P3.PartyDeathRecord(
            now,
            elapsedSeconds,
            memberKey,
            memberName,
            partyIndex,
            cause.ActionId,
            cause.ActionName,
            cause.GlobalSequence,
            cause.Wave ?? wave);
    }

    private unsafe void CapturePartyTargetActions(
        uint casterEntityId,
        ActionEffectHandler.Header* header,
        GameObjectId* targetEntityIds)
    {
        if (!plugin.IsInDmu ||
            header is null ||
            targetEntityIds is null ||
            header->NumTargets == 0 ||
            !BlackHoleResolutionActionIds.Contains(header->ActionId) ||
            IsPartyEntity(casterEntityId))
        {
            return;
        }

        lastLiveSignalSeenAtUtc = DateTime.UtcNow;

        var now = DateTime.UtcNow;
        var elapsedSeconds = blackHoleStartedAtUtc is not null
            ? (float)(now - blackHoleStartedAtUtc.Value).TotalSeconds
            : lastKnownBlackHoleElapsedSeconds;
        var wave = P3.BlackHoleTimeline.GetCurrentWave(elapsedSeconds) ??
            P3.BlackHoleTimeline.GetNearestWaveByMarkerTime(elapsedSeconds, 8.0f);

        for (var i = 0; i < header->NumTargets; i++)
        {
            var member = FindPartyMemberByTargetId(targetEntityIds[i]);
            if (member is null)
            {
                continue;
            }

            lastIncomingActionByMember[member.MemberKey] = new P3.PartyDeathRecord(
                now,
                elapsedSeconds,
                member.MemberKey,
                member.MemberName,
                member.PartyIndex,
                header->ActionId,
                GetActionName(header->ActionId),
                $"{header->GlobalSequence}",
                wave);
        }
    }

    private unsafe void CaptureBlackHoleResolution(
        uint casterEntityId,
        ActionEffectHandler.Header* header,
        GameObjectId* targetEntityIds)
    {
        if (!plugin.IsInDmu ||
            !MechanicState.IsActive ||
            header is null ||
            targetEntityIds is null ||
            header->NumTargets == 0 ||
            IsPartyEntity(casterEntityId))
        {
            return;
        }

        var wave = P3.BlackHoleTimeline.GetNearestWaveByMarkerTime(
            MechanicState.ElapsedSeconds,
            BlackHoleResolutionWindowSeconds);
        if (wave is null)
        {
            return;
        }

        var expectedAssignments = GetExpectedAssignmentsForWave(wave);
        if (expectedAssignments.Count == 0)
        {
            return;
        }

        var hitAssignments = new List<P3.LocalPlayerBlackHoleAssignment>();
        for (var i = 0; i < header->NumTargets; i++)
        {
            var member = FindPartyMemberByTargetId(targetEntityIds[i]);
            if (member is null)
            {
                continue;
            }

            var assignment = currentAssignments.FirstOrDefault(currentAssignment => currentAssignment.MemberKey == member.MemberKey);
            if (assignment is not null && hitAssignments.All(hit => hit.MemberKey != assignment.MemberKey))
            {
                hitAssignments.Add(assignment);
            }
        }

        if (hitAssignments.Count == 0)
        {
            return;
        }

        lastLiveSignalSeenAtUtc = DateTime.UtcNow;
        observedBlackHoleWaveKeys.Add(wave.Key);
        CalibrateBlackHoleClock(DateTime.UtcNow, wave.MarkerAtSeconds);

        var resolutionKey = BuildResolutionKey(wave, header, hitAssignments);
        if (!capturedBlackHoleResolutionKeys.Add(resolutionKey))
        {
            return;
        }

        var expectedKeys = expectedAssignments.Select(assignment => assignment.MemberKey).ToHashSet(StringComparer.Ordinal);
        var hits = hitAssignments
            .Select((assignment, hitOrder) => new P3.BlackHoleResolutionHit(
                assignment.MemberKey,
                assignment.MemberName,
                assignment.PartyIndex,
                assignment.RoleName,
                hitOrder,
                expectedKeys.Contains(assignment.MemberKey)))
            .OrderBy(hit => hit.PartyIndex)
            .ToList();

        currentBlackHoleResolutions.Add(new P3.BlackHoleResolutionRecord(
            DateTime.UtcNow,
            wave,
            header->ActionId,
            $"{header->GlobalSequence}",
            GetActionName(header->ActionId),
            hits));

        if (plugin.Configuration.DebugChat)
        {
            var hitNames = string.Join(", ", hits.Select(hit => hit.MemberName));
            PrintDebug($"Captured {GetActionName(header->ActionId)} ({header->ActionId}) for {wave.Label}: {hitNames}.");
        }
    }

    private IReadOnlyList<P3.LocalPlayerBlackHoleAssignment> GetExpectedAssignmentsForWave(P3.BlackHoleWave wave)
    {
        var waveInstructions = P3.BlackHoleStrategy.Instructions
            .Where(instruction => instruction.IsForWave(wave))
            .ToList();
        if (waveInstructions.Count == 0)
        {
            return [];
        }

        return currentAssignments
            .Where(assignment => waveInstructions.Any(instruction => instruction.Role.Matches(assignment, plugin.Configuration.SelectedBlackHoleStrategy)))
            .OrderBy(assignment => assignment.PartyIndex)
            .ToList();
    }

    private P3.PartyMemberSnapshot? FindPartyMemberByTargetId(GameObjectId targetId)
    {
        return currentMembers.FirstOrDefault(member => TargetMatchesMember(targetId, member));
    }

    private static bool TargetMatchesMember(GameObjectId targetId, P3.PartyMemberSnapshot member)
    {
        if (targetId.ObjectId != 0 && member.EntityId == targetId.ObjectId)
        {
            return true;
        }

        if (targetId.Id <= uint.MaxValue)
        {
            var shortId = (uint)targetId.Id;
            return shortId != 0 && member.EntityId == shortId;
        }

        return false;
    }

    private bool IsPartyEntity(uint entityId)
    {
        return entityId != 0 && currentMembers.Any(member => member.EntityId == entityId);
    }

    private string GetActionName(uint actionId)
    {
        if (actionNameCache.TryGetValue(actionId, out var cachedName))
        {
            return cachedName;
        }

        var actionName = $"Action {actionId}";
        try
        {
            var action = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Action>()?.GetRowOrDefault(actionId);
            var sheetName = action?.Name.ExtractText();
            if (!string.IsNullOrWhiteSpace(sheetName))
            {
                actionName = sheetName;
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Debug(ex, "Could not load action name for {ActionId}.", actionId);
        }

        actionNameCache[actionId] = actionName;
        return actionName;
    }

    private static unsafe string BuildResolutionKey(
        P3.BlackHoleWave wave,
        ActionEffectHandler.Header* header,
        IReadOnlyList<P3.LocalPlayerBlackHoleAssignment> hitAssignments)
    {
        var hitKey = string.Join(
            ",",
            hitAssignments
                .Select(assignment => assignment.MemberKey)
                .Order(StringComparer.Ordinal));
        return $"{wave.Key}:{header->ActionId}:{header->GlobalSequence}:{hitKey}";
    }

    private void UpdateInstructionChatCallout()
    {
        if (!plugin.Configuration.PostBlackHoleInstructionsToChat)
        {
            return;
        }

        var assignment = LocalAssignment;
        if (assignment is null || !assignment.HasLine)
        {
            return;
        }

        if (GetNowCalloutWave(assignment) is { } currentWave)
        {
            TryPostInstructionChatCallout("NOW", currentWave, assignment);
        }
    }

    private P3.BlackHoleWave? GetNowCalloutWave(P3.LocalPlayerBlackHoleAssignment assignment)
    {
        if (!MechanicState.IsActive || primordialCrustPulseCount <= 0)
        {
            return null;
        }

        var observedWave = P3.BlackHoleTimeline.GetWaveByPulseCount(primordialCrustPulseCount);
        if (observedWave is null ||
            !observedBlackHoleWaveKeys.Contains(observedWave.Key) ||
            postedNowInstructionCalloutWaveKeys.Contains(observedWave.Key))
        {
            return null;
        }

        return GetInstructionsForWave(assignment, observedWave).Count > 0
            ? observedWave
            : null;
    }

    private void TryPostInstructionChatCallout(
        string header,
        P3.BlackHoleWave wave,
        P3.LocalPlayerBlackHoleAssignment assignment)
    {
        var instructions = GetInstructionsForWave(assignment, wave);
        if (instructions.Count == 0)
        {
            return;
        }

        postedNowInstructionCalloutWaveKeys.Add(wave.Key);

        var lines = BuildInstructionCalloutLines(header, wave, instructions);
        for (var i = 0; i < lines.Count; i++)
        {
            if (i == 0)
            {
                PrintEcho(CreateGreenText(lines[i]));
                PrintSoundEffectEcho(plugin.Configuration.BlackHoleSoundEffectId);
                continue;
            }

            PrintEcho(lines[i]);
        }
    }

    private IReadOnlyList<P3.BlackHoleInstruction> GetInstructionsForWave(
        P3.LocalPlayerBlackHoleAssignment assignment,
        P3.BlackHoleWave wave)
    {
        return P3.BlackHoleStrategy.GetInstructionsFor(assignment, plugin.Configuration.SelectedBlackHoleStrategy)
            .Where(instruction => instruction.IsForWave(wave))
            .OrderBy(instruction => instruction.Tether)
            .ToList();
    }

    private static IReadOnlyList<string> BuildInstructionCalloutLines(
        string header,
        P3.BlackHoleWave wave,
        IReadOnlyList<P3.BlackHoleInstruction> instructions)
    {
        var lines = new List<string>
        {
            $"[DMU Helper] {header}: {wave.Label}",
        };

        lines.AddRange(instructions.Select(instruction => $"Tether {instruction.Tether}: {instruction.Action}"));
        return lines;
    }

    private static int ClampSoundEffectId(int soundEffectId)
    {
        return Math.Clamp(soundEffectId, SoundEffectOptions[0].Id, SoundEffectOptions[^1].Id);
    }

    private static unsafe void PrintSoundEffectEcho(int soundEffectId)
    {
        var id = ClampSoundEffectId(soundEffectId);
        if (id == NoSoundEffectId)
        {
            return;
        }

        var uiModule = UIModule.Instance();
        var shellModule = RaptureShellModule.Instance();
        if (uiModule == null || shellModule == null)
        {
            Plugin.Log.Debug("Could not send sound effect echo because the UI shell is unavailable.");
            return;
        }

        using var command = new Utf8String($"/echo <se.{id}>");
        shellModule->ExecuteCommandInner(&command, uiModule);
    }

    private static void PrintEcho(string message)
    {
        Plugin.ChatGui.Print(new XivChatEntry
        {
            Type = XivChatType.Echo,
            Message = message,
        });
    }

    private static void PrintEcho(SeString message)
    {
        Plugin.ChatGui.Print(new XivChatEntry
        {
            Type = XivChatType.Echo,
            Message = message,
        });
    }

    private void QueueChat(AssignmentChatChannel channel, string message)
    {
        queuedChatMessages.Enqueue(new QueuedChatMessage(GetChatChannelOption(channel).Channel, SanitizeChatText(message)));
    }

    private static unsafe void SendChat(AssignmentChatChannel channel, string message)
    {
        try
        {
            var uiModule = UIModule.Instance();
            var shellModule = RaptureShellModule.Instance();
            if (uiModule == null || shellModule == null)
            {
                Plugin.Log.Warning("Could not send DMU helper chat message because the UI shell is unavailable.");
                Plugin.ChatGui.Print("[DMU Helper] Could not send chat message.");
                return;
            }

            using var command = new Utf8String($"{GetChatChannelOption(channel).Command} {SanitizeChatText(message)}");
            shellModule->ExecuteCommandInner(&command, uiModule);
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "Could not send DMU helper chat message.");
            Plugin.ChatGui.Print("[DMU Helper] Could not send chat message.");
        }
    }

    public static ChatChannelOption GetChatChannelOption(AssignmentChatChannel channel)
    {
        return ChatChannelOptions.FirstOrDefault(option => option.Channel == channel) ??
            ChatChannelOptions.First(option => option.Channel == AssignmentChatChannel.Party);
    }

    private static string SanitizeChatText(string message)
    {
        return message
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();
    }

    private static SeString CreateGreenText(string message)
    {
        return new SeStringBuilder()
            .AddUiForeground(message, ChatGreenColorKey)
            .Build();
    }

    private void UpdateDebugState(bool inDmu, IReadOnlyList<P3.PartyStatusEntry> entries)
    {
        if (!plugin.Configuration.DebugChat)
        {
            debugRecognizedTerritory = inDmu;
            debugKnownEntries.Clear();
            foreach (var entry in entries)
            {
                debugKnownEntries[entry.Key] = entry;
            }

            return;
        }

        if (inDmu && !debugRecognizedTerritory)
        {
            PrintDebug("Recognized Dancing Mad Ultimate.");
        }
        else if (!inDmu && debugRecognizedTerritory)
        {
            PrintDebug("Left Dancing Mad Ultimate.");
        }

        var nextEntries = entries.ToDictionary(entry => entry.Key, StringComparer.Ordinal);
        foreach (var entry in nextEntries.Values)
        {
            if (!debugKnownEntries.ContainsKey(entry.Key))
            {
                PrintDebug($"{entry.MemberName} gained {entry.StatusName}.");
            }
        }

        foreach (var entry in debugKnownEntries.Values)
        {
            if (!nextEntries.ContainsKey(entry.Key))
            {
                PrintDebug($"{entry.MemberName} lost {entry.StatusName}.");
            }
        }

        debugRecognizedTerritory = inDmu;
        debugKnownEntries.Clear();
        foreach (var entry in nextEntries.Values)
        {
            debugKnownEntries[entry.Key] = entry;
        }
    }

    private static void PrintDebug(string message)
    {
        Plugin.ChatGui.Print($"[DMU Helper] {message}");
    }

    private P3.LocalPlayerBlackHoleAssignment? BuildLocalAssignment()
    {
        var localContentId = Plugin.PlayerState.ContentId;
        var localMember = currentMembers.FirstOrDefault(member => member.ContentId != 0 && member.ContentId == localContentId);
        if (localMember is null)
        {
            return null;
        }

        return currentAssignments.FirstOrDefault(assignment => assignment.MemberKey == localMember.MemberKey) ??
            BuildAssignmentForMember(localMember, currentEntries, lineHistory, accretionHistory);
    }

    private static P3.LocalPlayerBlackHoleAssignment BuildAssignmentForMember(
        P3.PartyMemberSnapshot member,
        IReadOnlyList<P3.PartyStatusEntry> entries,
        IReadOnlyDictionary<string, P3.PartyStatusEntry> lineEntriesByMember,
        IReadOnlyCollection<string> accretionMemberKeys)
    {
        var currentLineEntry = entries
            .Where(entry => entry.MemberKey == member.MemberKey && entry.Kind == P3.StatusKind.LineOrder)
            .OrderBy(entry => entry.SortOrder)
            .FirstOrDefault();
        lineEntriesByMember.TryGetValue(member.MemberKey, out var historicalLineEntry);
        var lineEntry = currentLineEntry ?? historicalLineEntry;

        if (lineEntry is null)
        {
            return new P3.LocalPlayerBlackHoleAssignment(
                member.MemberKey,
                member.MemberName,
                member.PartyIndex,
                member.IsDps,
                accretionMemberKeys.Contains(member.MemberKey),
                P3.LineGroup.None,
                0,
                "No line debuff",
                0.0f);
        }

        return new P3.LocalPlayerBlackHoleAssignment(
            member.MemberKey,
            member.MemberName,
            member.PartyIndex,
            member.IsDps,
            accretionMemberKeys.Contains(member.MemberKey),
            GetLineGroup(lineEntry.StatusId),
            lineEntry.StatusId,
            lineEntry.StatusName,
            currentLineEntry?.RemainingTime ?? 0.0f);
    }

    private void CaptureCurrentPullSnapshot(string reason)
    {
        if (!HasP3Record())
        {
            return;
        }

        var now = DateTime.UtcNow;
        var combatElapsedSeconds = blackHoleStartedAtUtc is not null
            ? (float)(now - blackHoleStartedAtUtc.Value).TotalSeconds
            : lastKnownBlackHoleElapsedSeconds;
        pullSnapshots.Add(new P3.BlackHolePullSnapshot(
            DateTime.UtcNow,
            reason,
            combatElapsedSeconds,
            currentEntries.ToList(),
            currentMembers.ToList(),
            currentAssignments.ToList(),
            currentBlackHoleResolutions.ToList(),
            MechanicState,
            currentPartyDeaths.ToList()));
    }

    private bool HasP3Record()
    {
        return currentEntries.Count > 0 ||
            currentBlackHoleResolutions.Count > 0 ||
            currentPartyDeaths.Count > 0 ||
            accretionHistory.Count > 0 ||
            lineHistory.Count > 0 ||
            blackHoleStartedAtUtc is not null;
    }

    private static P3.LineGroup GetLineGroup(uint statusId)
    {
        return statusId switch
        {
            3004 => P3.LineGroup.First,
            3005 => P3.LineGroup.Second,
            3006 => P3.LineGroup.Third,
            _ => P3.LineGroup.None,
        };
    }

    private static bool IsDpsJob(uint classJobId)
    {
        return classJobId is
            2 or 4 or 5 or 7 or 26 or 29 or
            20 or 22 or 23 or 25 or 27 or 30 or 31 or 34 or 35 or 36 or 38 or 39 or 41 or 42;
    }

    private readonly record struct QueuedChatMessage(
        AssignmentChatChannel Channel,
        string Message);
}
