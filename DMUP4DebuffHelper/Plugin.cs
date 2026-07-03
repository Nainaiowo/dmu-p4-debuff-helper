using DMUP4DebuffHelper.Windows;
using Dalamud.Game.DutyState;
using Dalamud.Game.Command;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Lumina.Excel.Sheets;
using System;
using System.Collections.Generic;
using System.Linq;

namespace DMUP4DebuffHelper;

public sealed class Plugin : IDalamudPlugin
{
    private const string ConfigCommandName = "/dmup4";
    private const string ShortHelperCommandName = "/dmup4h";
    private const string HelperCommandName = "/dmup4helper";
    private const uint DmuTerritoryId = 1363;
    private const uint BossTellStatusId = 2056;
    private static readonly TimeSpan BossTellFreshness = TimeSpan.FromSeconds(20);

    internal static readonly IReadOnlyDictionary<uint, WatchedStatus> WatchedStatuses =
        new Dictionary<uint, WatchedStatus>
        {
            [5545] = new(5545, "Compressed Water", P4Boss.NeoExdeath, P4MechanicGroup.GrandCross, 10),
            [5544] = new(5544, "Forked Lightning", P4Boss.NeoExdeath, P4MechanicGroup.GrandCross, 20),
            [5543] = new(5543, "Cursed Shriek", P4Boss.NeoExdeath, P4MechanicGroup.GrandCross, 30),
            [5546] = new(5546, "Acceleration Bomb", P4Boss.NeoExdeath, P4MechanicGroup.GrandCross, 40),
            [5548] = new(5548, "Dynamic Fluid", P4Boss.Chaos, P4MechanicGroup.Chaos, 50),
            [5547] = new(5547, "Entropy", P4Boss.Chaos, P4MechanicGroup.Chaos, 60),
            [4888] = new(4888, "Black Wound", P4Boss.NeoExdeath, P4MechanicGroup.Flood, 70),
            [4887] = new(4887, "White Wound", P4Boss.NeoExdeath, P4MechanicGroup.Flood, 80),
            [454] = new(454, "Allagan Field", P4Boss.NeoExdeath, P4MechanicGroup.Flood, 90),
            [5464] = new(5464, "Beyond Death", P4Boss.NeoExdeath, P4MechanicGroup.Flood, 100),
        };

    private static readonly IReadOnlyDictionary<ushort, RealityState> BossTellRealities =
        new Dictionary<ushort, RealityState>
        {
            [1119] = RealityState.Fake,
            [1120] = RealityState.Real,
            [1121] = RealityState.Fake,
            [1122] = RealityState.Real,
        };

    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IPartyList PartyList { get; private set; } = null!;
    [PluginService] internal static IDutyState DutyState { get; private set; } = null!;
    [PluginService] internal static IPlayerState PlayerState { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    private readonly WindowSystem windowSystem = new("DMUP4DebuffHelper");
    private readonly ConfigWindow configWindow;
    private readonly HelperWindow helperWindow;
    private readonly List<PartyStatusEntry> currentEntries = [];
    private readonly List<PartyMemberSnapshot> currentMembers = [];
    private readonly List<P4DebuffAssignment> currentAssignments = [];
    private readonly List<P4DebuffAssignment> localAssignments = [];
    private readonly List<BossTellSnapshot> currentBossTells = [];
    private readonly List<BossTellSnapshot> currentPullBossTells = [];
    private readonly List<P4DebuffRecord> currentPullDebuffRecords = [];
    private readonly List<P4PullSnapshot> pullSnapshots = [];
    private readonly Dictionary<string, PartyStatusEntry> debugKnownEntries = new(StringComparer.Ordinal);
    private readonly Dictionary<uint, string> statusNameCache = new();
    private readonly Dictionary<uint, uint> statusIconCache = new();
    private readonly Dictionary<string, BossTellSnapshot> latestBossTells = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CapturedDebuffState> capturedDebuffStates = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> activeDebuffRecordIndexes = new(StringComparer.Ordinal);
    private readonly HashSet<string> activeDebuffKeysLastFrame = new(StringComparer.Ordinal);
    private readonly HashSet<string> activeBossTellKeysLastFrame = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> debugKnownAssignments = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ushort> debugKnownBossTellParams = new(StringComparer.Ordinal);
    private DateTime? pullStartedAtUtc;
    private float lastKnownPullElapsedSeconds;
    private bool debugRecognizedTerritory;

    public Configuration Configuration { get; }

    public IReadOnlyList<PartyStatusEntry> CurrentEntries => currentEntries;

    public IReadOnlyList<P4DebuffAssignment> CurrentAssignments => currentAssignments;

    public IReadOnlyList<P4DebuffAssignment> LocalAssignments => localAssignments;

    public IReadOnlyList<BossTellSnapshot> CurrentBossTells => currentBossTells;

    public IReadOnlyList<P4DebuffRecord> CurrentPullDebuffRecords => currentPullDebuffRecords;

    public IReadOnlyList<BossTellSnapshot> CurrentPullBossTells => currentPullBossTells;

    public IReadOnlyList<P4PullSnapshot> PullSnapshots => pullSnapshots;

    public float CurrentPullElapsedSeconds => pullStartedAtUtc is not null
        ? (float)(DateTime.UtcNow - pullStartedAtUtc.Value).TotalSeconds
        : lastKnownPullElapsedSeconds;

    public bool IsInDmu { get; private set; }

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        configWindow = new ConfigWindow(this);
        helperWindow = new HelperWindow(this)
        {
            IsOpen = Configuration.ShowHelper,
        };

        windowSystem.AddWindow(configWindow);
        windowSystem.AddWindow(helperWindow);

        CommandManager.AddHandler(ConfigCommandName, new CommandInfo(OnConfigCommand)
        {
            HelpMessage = "Open the DMU P4 Debuff Helper settings window.",
        });
        CommandManager.AddHandler(ShortHelperCommandName, new CommandInfo(OnHelperCommand)
        {
            HelpMessage = "Open the DMU P4 Debuff Helper window.",
        });
        CommandManager.AddHandler(HelperCommandName, new CommandInfo(OnHelperCommand)
        {
            HelpMessage = "Open the DMU P4 Debuff Helper window.",
        });

        PluginInterface.UiBuilder.Draw += RefreshStatusSnapshot;
        PluginInterface.UiBuilder.Draw += windowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi += ToggleHelperUi;
        DutyState.DutyStarted += OnDutyReset;
        DutyState.DutyWiped += OnDutyReset;
        DutyState.DutyRecommenced += OnDutyReset;
    }

    public void Dispose()
    {
        DutyState.DutyRecommenced -= OnDutyReset;
        DutyState.DutyWiped -= OnDutyReset;
        DutyState.DutyStarted -= OnDutyReset;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleHelperUi;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        PluginInterface.UiBuilder.Draw -= windowSystem.Draw;
        PluginInterface.UiBuilder.Draw -= RefreshStatusSnapshot;
        CommandManager.RemoveHandler(HelperCommandName);
        CommandManager.RemoveHandler(ShortHelperCommandName);
        CommandManager.RemoveHandler(ConfigCommandName);

        windowSystem.RemoveAllWindows();
        configWindow.Dispose();
        helperWindow.Dispose();
    }

    public void ToggleConfigUi()
    {
        configWindow.Toggle();
    }

    public void ToggleHelperUi()
    {
        helperWindow.Toggle();
        Configuration.ShowHelper = helperWindow.IsOpen;
        SaveConfiguration();
    }

    public void OpenHelperUi()
    {
        Configuration.ShowHelper = true;
        helperWindow.IsOpen = true;
        SaveConfiguration();
    }

    public void SetShowHelper(bool enabled)
    {
        Configuration.ShowHelper = enabled;
        helperWindow.IsOpen = enabled;
        SaveConfiguration();
    }

    public void SetPreviewWhenInactive(bool enabled)
    {
        Configuration.PreviewWhenInactive = enabled;
        SaveConfiguration();
    }

    public void SetDebugChat(bool enabled)
    {
        Configuration.DebugChat = enabled;
        if (enabled)
        {
            debugRecognizedTerritory = false;
            debugKnownEntries.Clear();
        }

        SaveConfiguration();
    }

    public void SetShowOnlyWatchedStatuses(bool enabled)
    {
        Configuration.ShowOnlyWatchedStatuses = enabled;
        SaveConfiguration();
    }

    public void SetHelperCollapsed(bool collapsed)
    {
        Configuration.HelperCollapsed = collapsed;
        SaveConfiguration();
    }

    public void SetHelperFontScale(float scale)
    {
        Configuration.HelperFontScale = Math.Clamp(scale, 0.75f, 2.0f);
        SaveConfiguration();
    }

    public void SetHelperBackgroundOpacity(float opacity)
    {
        Configuration.HelperBackgroundOpacity = Math.Clamp(opacity, 0.15f, 1.0f);
        SaveConfiguration();
    }

    public void SaveConfiguration()
    {
        Configuration.Save();
    }

    private void OnConfigCommand(string command, string args)
    {
        ToggleConfigUi();
    }

    private void OnHelperCommand(string command, string args)
    {
        ToggleHelperUi();
    }

    private void RefreshStatusSnapshot()
    {
        IsInDmu = ClientState.TerritoryType == DmuTerritoryId;
        if (!IsInDmu)
        {
            CaptureCurrentPullSnapshot("Left DMU");
            currentEntries.Clear();
            currentMembers.Clear();
            currentAssignments.Clear();
            localAssignments.Clear();
            currentBossTells.Clear();
            latestBossTells.Clear();
            capturedDebuffStates.Clear();
            activeBossTellKeysLastFrame.Clear();
            ResetPullState();
            UpdateDebugState(inDmu: false, []);
            return;
        }

        var nextEntries = new List<PartyStatusEntry>();
        var nextMembers = new List<PartyMemberSnapshot>();
        var filterToWatched = Configuration.ShowOnlyWatchedStatuses && WatchedStatuses.Count > 0;
        var localContentId = PlayerState.ContentId;
        var localEntityId = ObjectTable.LocalPlayer?.EntityId ?? 0;
        var partyIndex = 0;
        foreach (var member in PartyList)
        {
            var memberName = member.Name.TextValue;
            var memberKey = member.ContentId != 0
                ? member.ContentId.ToString("X16")
                : $"{memberName}:{partyIndex}";
            nextMembers.Add(new PartyMemberSnapshot(
                memberKey,
                memberName,
                partyIndex,
                member.ContentId,
                member.EntityId));

            var isLocalMember =
                (localContentId != 0 && member.ContentId == localContentId)
                || (localEntityId != 0 && member.EntityId == localEntityId);
            if (!isLocalMember)
            {
                partyIndex++;
                continue;
            }

            foreach (var status in member.Statuses)
            {
                if (status.StatusId == 0)
                {
                    continue;
                }

                var isWatched = WatchedStatuses.TryGetValue(status.StatusId, out var watchedStatus);
                if (filterToWatched && !isWatched)
                {
                    continue;
                }

                nextEntries.Add(new PartyStatusEntry(
                    $"{memberKey}:{status.StatusId}",
                    memberKey,
                    memberName,
                    partyIndex,
                    status.StatusId,
                    isWatched ? watchedStatus!.Name : GetStatusName(status.StatusId),
                    GetStatusIconId(status.StatusId),
                    status.RemainingTime,
                    isWatched,
                    isWatched ? watchedStatus!.SortOrder : 10_000 + partyIndex,
                    DateTime.UtcNow));
            }

            partyIndex++;
        }

        currentMembers.Clear();
        currentMembers.AddRange(nextMembers.OrderBy(member => member.PartyIndex));

        currentEntries.Clear();
        currentEntries.AddRange(nextEntries
            .OrderBy(entry => entry.SortOrder)
            .ThenBy(entry => entry.PartyIndex)
            .ThenBy(entry => entry.MemberName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.StatusId));

        RefreshBossTellSnapshots();
        RefreshAssignments();
        RefreshLocalAssignments();
        UpdatePullTracking();
        UpdateDebugState(inDmu: true, currentEntries);
    }

    private void RefreshBossTellSnapshots()
    {
        currentBossTells.Clear();
        var activeTellKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var gameObject in ObjectTable)
        {
            if (gameObject == null
                || !gameObject.IsValid()
                || gameObject is not ICharacter character
                || character is not IBattleChara battleChara)
            {
                continue;
            }

            var boss = ResolveBoss(character);
            if (boss is not (P4Boss.NeoExdeath or P4Boss.Chaos))
            {
                continue;
            }

            foreach (var status in battleChara.StatusList)
            {
                if (status.StatusId != BossTellStatusId)
                {
                    continue;
                }

                var param = status.Param;
                var reality = BossTellRealities.TryGetValue(param, out var knownReality)
                    ? knownReality
                    : RealityState.Unknown;
                var group = ResolveTellGroup(boss);
                var snapshot = new BossTellSnapshot(boss, group, status.StatusId, param, reality, DateTime.UtcNow);
                currentBossTells.Add(snapshot);
                latestBossTells[GetTellKey(boss, group)] = snapshot;
                var activeTellKey = $"{boss}:{group}:{param}";
                activeTellKeys.Add(activeTellKey);
                if (!activeBossTellKeysLastFrame.Contains(activeTellKey))
                {
                    currentPullBossTells.Add(snapshot);
                }

                break;
            }
        }

        activeBossTellKeysLastFrame.Clear();
        foreach (var key in activeTellKeys)
        {
            activeBossTellKeysLastFrame.Add(key);
        }
    }

    private void RefreshAssignments()
    {
        currentAssignments.Clear();
        PruneCapturedDebuffStates();
        var localMember = GetLocalPartyMember();
        if (localMember is null)
        {
            UpdateCurrentPullDebuffRecords();
            return;
        }

        foreach (var entry in currentEntries.Where(entry => entry.IsWatched && entry.MemberKey == localMember.MemberKey))
        {
            if (!WatchedStatuses.TryGetValue(entry.StatusId, out var rule))
            {
                continue;
            }

            var capturedState = GetOrUpdateCapturedDebuffState(entry, rule);

            currentAssignments.Add(new P4DebuffAssignment(
                entry,
                rule,
                capturedState.Reality,
                capturedState.TellParam,
                GetInstruction(entry, rule, capturedState.Reality)));
        }

        UpdateCurrentPullDebuffRecords();
    }

    private void RefreshLocalAssignments()
    {
        localAssignments.Clear();
        var localMember = GetLocalPartyMember();
        if (localMember is null)
        {
            return;
        }

        localAssignments.AddRange(currentAssignments
            .Where(assignment => assignment.Entry.MemberKey == localMember.MemberKey));
    }

    private PartyMemberSnapshot? GetLocalPartyMember()
    {
        var localContentId = PlayerState.ContentId;
        var localMember = currentMembers.FirstOrDefault(member => member.ContentId != 0 && member.ContentId == localContentId);
        if (localMember is not null)
        {
            return localMember;
        }

        var localEntityId = ObjectTable.LocalPlayer?.EntityId ?? 0;
        return localEntityId != 0
            ? currentMembers.FirstOrDefault(member => member.EntityId == localEntityId)
            : null;
    }

    private void UpdateCurrentPullDebuffRecords()
    {
        var activeKeys = currentAssignments
            .Select(assignment => assignment.Entry.Key)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var staleKey in activeDebuffKeysLastFrame.Where(key => !activeKeys.Contains(key)).ToList())
        {
            activeDebuffRecordIndexes.Remove(staleKey);
        }

        foreach (var assignment in currentAssignments)
        {
            if (!activeDebuffRecordIndexes.TryGetValue(assignment.Entry.Key, out var recordIndex))
            {
                currentPullDebuffRecords.Add(CreateDebuffRecord(assignment));
                activeDebuffRecordIndexes[assignment.Entry.Key] = currentPullDebuffRecords.Count - 1;
                continue;
            }

            var existingRecord = currentPullDebuffRecords[recordIndex];
            if (existingRecord.Reality == RealityState.Unknown && assignment.Reality != RealityState.Unknown)
            {
                currentPullDebuffRecords[recordIndex] = CreateDebuffRecord(assignment) with
                {
                    SeenAtUtc = existingRecord.SeenAtUtc,
                    RemainingTimeAtCapture = existingRecord.RemainingTimeAtCapture,
                };
            }
        }

        activeDebuffKeysLastFrame.Clear();
        foreach (var key in activeKeys)
        {
            activeDebuffKeysLastFrame.Add(key);
        }
    }

    private static P4DebuffRecord CreateDebuffRecord(P4DebuffAssignment assignment)
    {
        return new P4DebuffRecord(
            DateTime.UtcNow,
            assignment.Entry.Key,
            assignment.Entry.MemberKey,
            assignment.Entry.MemberName,
            assignment.Entry.PartyIndex,
            assignment.Entry.StatusId,
            assignment.Rule.Name,
            assignment.Rule.SourceBoss,
            assignment.Rule.Group,
            assignment.Reality,
            assignment.TellParam,
            assignment.Entry.RemainingTime,
            assignment.Instruction);
    }

    private void PruneCapturedDebuffStates()
    {
        if (capturedDebuffStates.Count == 0)
        {
            return;
        }

        var activeKeys = currentEntries
            .Where(entry => entry.IsWatched)
            .Select(entry => entry.Key)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var key in capturedDebuffStates.Keys.ToArray())
        {
            if (!activeKeys.Contains(key))
            {
                capturedDebuffStates.Remove(key);
            }
        }
    }

    private CapturedDebuffState GetOrUpdateCapturedDebuffState(PartyStatusEntry entry, WatchedStatus rule)
    {
        if (!capturedDebuffStates.TryGetValue(entry.Key, out var capturedState))
        {
            capturedState = CaptureDebuffState(rule);
            capturedDebuffStates[entry.Key] = capturedState;
            return capturedState;
        }

        if (capturedState.Reality != RealityState.Unknown || !TryGetRelevantTell(rule, out var tell))
        {
            return capturedState;
        }

        capturedState = new CapturedDebuffState(tell!.Reality, tell.Param, DateTime.UtcNow);
        capturedDebuffStates[entry.Key] = capturedState;
        return capturedState;
    }

    private CapturedDebuffState CaptureDebuffState(WatchedStatus rule)
    {
        return TryGetRelevantTell(rule, out var tell)
            ? new CapturedDebuffState(tell!.Reality, tell.Param, DateTime.UtcNow)
            : new CapturedDebuffState(RealityState.Unknown, null, DateTime.UtcNow);
    }

    private static P4Boss ResolveBoss(ICharacter character)
    {
        var name = character.Name.TextValue;
        if (string.Equals(name, "Neo Exdeath", StringComparison.OrdinalIgnoreCase))
        {
            return P4Boss.NeoExdeath;
        }

        if (string.Equals(name, "Chaos", StringComparison.OrdinalIgnoreCase))
        {
            return P4Boss.Chaos;
        }

        if (string.Equals(name, "Kefka", StringComparison.OrdinalIgnoreCase))
        {
            return P4Boss.Kefka;
        }

        return P4Boss.Unknown;
    }

    private P4MechanicGroup ResolveTellGroup(P4Boss boss)
    {
        if (boss == P4Boss.Chaos)
        {
            return P4MechanicGroup.Chaos;
        }

        return HasActiveFloodDebuffs()
            ? P4MechanicGroup.Flood
            : P4MechanicGroup.GrandCross;
    }

    private bool TryGetRelevantTell(WatchedStatus rule, out BossTellSnapshot? tell)
    {
        tell = null;
        var key = GetTellKey(rule.SourceBoss, rule.Group);
        if (!latestBossTells.TryGetValue(key, out var candidate))
        {
            return false;
        }

        if (DateTime.UtcNow - candidate.SeenAtUtc > BossTellFreshness)
        {
            return false;
        }

        tell = candidate;
        return true;
    }

    private bool HasActiveFloodDebuffs()
    {
        return currentEntries.Any(entry => entry.IsWatched
            && WatchedStatuses.TryGetValue(entry.StatusId, out var rule)
            && rule.Group == P4MechanicGroup.Flood);
    }

    private static string GetTellKey(P4Boss boss, P4MechanicGroup group)
    {
        return $"{boss}:{group}";
    }

    private string GetInstruction(PartyStatusEntry entry, WatchedStatus rule, RealityState reality)
    {
        return rule.Group switch
        {
            P4MechanicGroup.GrandCross => GetGrandCrossInstruction(rule.Id, reality),
            P4MechanicGroup.Chaos => GetChaosInstruction(rule.Id, reality),
            P4MechanicGroup.Flood => GetFloodInstruction(entry, rule.Id, reality),
            _ => "Tracked status.",
        };
    }

    private static string GetGrandCrossInstruction(uint statusId, RealityState reality)
    {
        return statusId switch
        {
            5545 => reality switch
            {
                RealityState.Real => "Stack with your assigned group.",
                RealityState.Fake => "Spread away from your assigned group.",
                _ => "Water: real stacks, fake spreads.",
            },
            5544 => reality switch
            {
                RealityState.Real => "Spread away from your assigned group.",
                RealityState.Fake => "Stack with your assigned group.",
                _ => "Lightning: real spreads, fake stacks.",
            },
            5543 => reality switch
            {
                RealityState.Real => "Stand out and have players look away from you.",
                RealityState.Fake => "Stand center so players can look at you.",
                _ => "Shriek: real look away, fake look at.",
            },
            5546 => reality switch
            {
                RealityState.Real => "Stop moving and stop actions when it resolves.",
                RealityState.Fake => "Keep moving when it resolves.",
                _ => "Bomb: real stillness, fake motion.",
            },
            _ => "Tracked Grand Cross debuff.",
        };
    }

    private static string GetChaosInstruction(uint statusId, RealityState reality)
    {
        return statusId switch
        {
            5548 => reality switch
            {
                RealityState.Real => "Resolve as delayed donut.",
                RealityState.Fake => "Resolve as delayed point-blank AoE.",
                _ => "Dynamic Fluid: real donut, fake point-blank.",
            },
            5547 => reality switch
            {
                RealityState.Real => "Resolve as delayed point-blank AoE.",
                RealityState.Fake => "Resolve as delayed donut.",
                _ => "Entropy: real point-blank, fake donut.",
            },
            _ => "Tracked Chaos debuff.",
        };
    }

    private string GetFloodInstruction(PartyStatusEntry entry, uint statusId, RealityState reality)
    {
        var woundColor = GetWoundColor(entry.MemberKey);
        var woundText = woundColor switch
        {
            WoundColor.Black => "Black Wound",
            WoundColor.White => "White Wound",
            _ => "your Wound",
        };

        return statusId switch
        {
            454 => reality switch
            {
                RealityState.Real => $"Allagan Field: stand opposite {woundText}.",
                RealityState.Fake => $"Allagan Field: stand same as {woundText}.",
                _ => "Allagan Field: real opposite Wound, fake same Wound.",
            },
            5464 => reality switch
            {
                RealityState.Real => $"Beyond Death: stand same as {woundText}.",
                RealityState.Fake => $"Beyond Death: stand opposite {woundText}.",
                _ => "Beyond Death: real same Wound, fake opposite Wound.",
            },
            4888 => "Black Wound: use with Allagan Field or Beyond Death.",
            4887 => "White Wound: use with Allagan Field or Beyond Death.",
            _ => "Tracked Flood debuff.",
        };
    }

    private WoundColor GetWoundColor(string memberKey)
    {
        foreach (var entry in currentEntries)
        {
            if (!string.Equals(entry.MemberKey, memberKey, StringComparison.Ordinal))
            {
                continue;
            }

            if (entry.StatusId == 4888)
            {
                return WoundColor.Black;
            }

            if (entry.StatusId == 4887)
            {
                return WoundColor.White;
            }
        }

        return WoundColor.None;
    }

    private string GetStatusName(uint statusId)
    {
        if (statusNameCache.TryGetValue(statusId, out var cachedName))
        {
            return cachedName;
        }

        var statusName = $"Status {statusId}";
        try
        {
            var status = DataManager.GetExcelSheet<Status>()?.GetRowOrDefault(statusId);
            var sheetName = status?.Name.ExtractText();
            if (!string.IsNullOrWhiteSpace(sheetName))
            {
                statusName = sheetName;
            }
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Could not load status name for {StatusId}.", statusId);
        }

        statusNameCache[statusId] = statusName;
        return statusName;
    }

    internal uint GetStatusIconId(uint statusId)
    {
        if (statusIconCache.TryGetValue(statusId, out var cachedIconId))
        {
            return cachedIconId;
        }

        var iconId = 0u;
        try
        {
            var status = DataManager.GetExcelSheet<Status>()?.GetRowOrDefault(statusId);
            iconId = status?.Icon ?? 0u;
        }
        catch (Exception ex)
        {
            Log.Debug(ex, "Could not load status icon for {StatusId}.", statusId);
        }

        statusIconCache[statusId] = iconId;
        return iconId;
    }

    private void UpdateDebugState(bool inDmu, IReadOnlyList<PartyStatusEntry> entries)
    {
        if (!Configuration.DebugChat)
        {
            debugRecognizedTerritory = inDmu;
            debugKnownEntries.Clear();
            debugKnownAssignments.Clear();
            debugKnownBossTellParams.Clear();
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

        foreach (var tell in currentBossTells)
        {
            var key = GetTellKey(tell.Boss, tell.Group);
            if (debugKnownBossTellParams.TryGetValue(key, out var knownParam) && knownParam == tell.Param)
            {
                continue;
            }

            PrintDebug($"{FormatBoss(tell.Boss)} {FormatGroup(tell.Group)} tell {tell.Param} ({FormatReality(tell.Reality)}).");
            debugKnownBossTellParams[key] = tell.Param;
        }

        var nextEntries = entries.ToDictionary(entry => entry.Key, StringComparer.Ordinal);
        foreach (var entry in nextEntries.Values)
        {
            if (!debugKnownEntries.ContainsKey(entry.Key))
            {
                PrintDebug($"{entry.MemberName} gained {entry.StatusName} ({entry.StatusId}).");
            }
        }

        foreach (var entry in debugKnownEntries.Values)
        {
            if (!nextEntries.ContainsKey(entry.Key))
            {
                PrintDebug($"{entry.MemberName} lost {entry.StatusName} ({entry.StatusId}).");
            }
        }

        var nextAssignments = currentAssignments.ToDictionary(
            assignment => assignment.Entry.Key,
            assignment => $"{FormatReality(assignment.Reality)}:{assignment.TellParam}",
            StringComparer.Ordinal);
        foreach (var assignment in currentAssignments)
        {
            var value = nextAssignments[assignment.Entry.Key];
            if (debugKnownAssignments.TryGetValue(assignment.Entry.Key, out var knownValue) && knownValue == value)
            {
                continue;
            }

            var tellText = assignment.TellParam is { } tellParam ? $" tell {tellParam}" : " no tell";
            PrintDebug($"{assignment.Entry.MemberName}: {assignment.Rule.Name} = {FormatReality(assignment.Reality)} ({tellText}). {assignment.Instruction}");
        }

        debugRecognizedTerritory = inDmu;
        debugKnownEntries.Clear();
        foreach (var entry in nextEntries.Values)
        {
            debugKnownEntries[entry.Key] = entry;
        }

        debugKnownAssignments.Clear();
        foreach (var (key, value) in nextAssignments)
        {
            debugKnownAssignments[key] = value;
        }
    }

    private static void PrintDebug(string message)
    {
        ChatGui.Print($"[DMU P4 Debuff Helper] {message}");
    }

    private void OnDutyReset(IDutyStateEventArgs args)
    {
        if (args.TerritoryType.RowId != DmuTerritoryId)
        {
            return;
        }

        CaptureCurrentPullSnapshot("Wipe/reset detected");
        currentEntries.Clear();
        currentMembers.Clear();
        currentAssignments.Clear();
        localAssignments.Clear();
        currentBossTells.Clear();
        latestBossTells.Clear();
        capturedDebuffStates.Clear();
        activeBossTellKeysLastFrame.Clear();
        ResetPullState();
    }

    private void UpdatePullTracking()
    {
        if (!HasP4Record())
        {
            return;
        }

        pullStartedAtUtc ??= DateTime.UtcNow;
        lastKnownPullElapsedSeconds = CurrentPullElapsedSeconds;
    }

    private void CaptureCurrentPullSnapshot(string reason)
    {
        if (!HasP4Record())
        {
            return;
        }

        pullSnapshots.Add(new P4PullSnapshot(
            DateTime.UtcNow,
            reason,
            CurrentPullElapsedSeconds,
            currentPullDebuffRecords.ToList(),
            currentPullBossTells.ToList()));
    }

    private bool HasP4Record()
    {
        return currentPullDebuffRecords.Count > 0 ||
            currentPullBossTells.Count > 0 ||
            currentAssignments.Count > 0 ||
            currentBossTells.Count > 0;
    }

    private void ResetPullState()
    {
        currentPullDebuffRecords.Clear();
        currentPullBossTells.Clear();
        activeDebuffRecordIndexes.Clear();
        activeDebuffKeysLastFrame.Clear();
        activeBossTellKeysLastFrame.Clear();
        pullStartedAtUtc = null;
        lastKnownPullElapsedSeconds = 0.0f;
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
