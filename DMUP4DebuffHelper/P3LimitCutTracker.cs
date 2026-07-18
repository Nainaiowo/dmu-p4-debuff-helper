namespace DMUP4DebuffHelper;

using FFXIVClientStructs.FFXIV.Client.Game.Character;
using System;
using System.Collections.Generic;
using System.Numerics;

public sealed class P3LimitCutTracker
{
    private const uint UltimaBlasterPreviewActionId = 47843;
    private const float ArenaCenterX = 100.0f;
    private const float ArenaCenterZ = 100.0f;
    private const float MinimumCloneDistanceFromCenter = 10.0f;
    private const float MaximumCloneDistanceFromCenter = 35.0f;
    private const float MinimumWaymarkDistanceFromCenter = 8.0f;
    private const float MaximumWaymarkAngleDifferenceDegrees = 30.0f;
    private static readonly TimeSpan DisplayFreshness = TimeSpan.FromSeconds(24);
    private static readonly TimeSpan StaleSequenceAge = TimeSpan.FromSeconds(60);

    // DMU marker preset captured from Better Deaths pull 649.
    private static readonly IReadOnlyList<WaymarkReference> ReferenceWaymarks =
    [
        new("A", 100.0f, 88.0f),
        new("B", 112.0f, 100.0f),
        new("C", 100.0f, 112.0f),
        new("D", 88.0f, 100.0f),
        new("1", 94.0f, 94.0f),
        new("2", 106.0f, 94.0f),
        new("3", 106.0f, 106.0f),
        new("4", 94.0f, 106.0f),
    ];

    private readonly HashSet<string> observedCastKeys = new(StringComparer.Ordinal);
    private Vector3? firstClonePosition;
    private Vector3? secondClonePosition;
    private DateTime? lastSeenAtUtc;
    private string newNorthLabel = string.Empty;
    private string newNorthMarkerLabel = string.Empty;
    private P3LimitCutRotation rotation = P3LimitCutRotation.Unknown;
    private int observedCloneCount;

    public P3LimitCutState CurrentState
    {
        get
        {
            var now = DateTime.UtcNow;
            var active = lastSeenAtUtc is { } lastSeen &&
                now - lastSeen <= DisplayFreshness &&
                firstClonePosition is not null;
            return active
                ? new P3LimitCutState(true, newNorthLabel, newNorthMarkerLabel, rotation, observedCloneCount)
                : P3LimitCutState.Inactive;
        }
    }

    public bool HasLiveSignal => CurrentState.IsActive;

    public unsafe void ProcessActionEffect(uint casterEntityId, ActionEffectHandler.Header* header)
    {
        if (header is null || header->ActionId != UltimaBlasterPreviewActionId)
        {
            return;
        }

        var now = DateTime.UtcNow;
        if (lastSeenAtUtc is { } lastSeen && now - lastSeen > StaleSequenceAge)
        {
            Reset();
        }

        var castKey = $"{casterEntityId:X8}:{header->GlobalSequence}";
        if (!observedCastKeys.Add(castKey) || !TryGetCasterPosition(casterEntityId, out var position))
        {
            return;
        }

        lastSeenAtUtc = now;
        observedCloneCount++;

        if (firstClonePosition is null)
        {
            firstClonePosition = position;
            var landingPosition = MirrorAcrossArenaCenter(position);
            newNorthLabel = FormatDirection(landingPosition);
            newNorthMarkerLabel = TryGetNorthMarkerLabel(landingPosition, out var markerLabel)
                ? markerLabel
                : string.Empty;
            return;
        }

        if (secondClonePosition is not null || DistanceXZ(position, firstClonePosition.Value) < 2.0f)
        {
            return;
        }

        secondClonePosition = position;
        rotation = ResolveRotation(firstClonePosition.Value, position);
    }

    public void Reset()
    {
        observedCastKeys.Clear();
        firstClonePosition = null;
        secondClonePosition = null;
        lastSeenAtUtc = null;
        newNorthLabel = string.Empty;
        newNorthMarkerLabel = string.Empty;
        rotation = P3LimitCutRotation.Unknown;
        observedCloneCount = 0;
    }

    private static bool TryGetCasterPosition(uint casterEntityId, out Vector3 position)
    {
        position = default;
        if (casterEntityId == 0)
        {
            return false;
        }

        try
        {
            var gameObject = Plugin.ObjectTable.SearchByEntityId(casterEntityId);
            if (gameObject is null || !IsUsablePosition(gameObject.Position))
            {
                return false;
            }

            position = gameObject.Position;
            return IsLikelyLimitCutClonePosition(position);
        }
        catch (Exception ex)
        {
            Plugin.Log.Debug(ex, "Could not capture DMU P3 limit cut clone position for {EntityId:X8}.", casterEntityId);
            return false;
        }
    }

    private static bool IsUsablePosition(Vector3 position)
    {
        return float.IsFinite(position.X) &&
            float.IsFinite(position.Y) &&
            float.IsFinite(position.Z) &&
            (MathF.Abs(position.X) > 0.001f || MathF.Abs(position.Z) > 0.001f);
    }

    private static bool IsLikelyLimitCutClonePosition(Vector3 position)
    {
        var distance = DistanceFromCenter(position);
        return distance is >= MinimumCloneDistanceFromCenter and <= MaximumCloneDistanceFromCenter;
    }

    private static P3LimitCutRotation ResolveRotation(Vector3 first, Vector3 second)
    {
        var firstAngle = GetClockwiseAngleFromNorth(first);
        var secondAngle = GetClockwiseAngleFromNorth(second);
        var delta = NormalizeDegrees(secondAngle - firstAngle);
        if (MathF.Abs(delta) < 1.0f)
        {
            return P3LimitCutRotation.Unknown;
        }

        return delta > 0.0f
            ? P3LimitCutRotation.Clockwise
            : P3LimitCutRotation.CounterClockwise;
    }

    private static string FormatDirection(Vector3 position)
    {
        var angle = GetClockwiseAngleFromNorth(position);
        var index = (int)MathF.Floor((angle + 22.5f) / 45.0f) % 8;
        return index switch
        {
            0 => "N",
            1 => "NE",
            2 => "E",
            3 => "SE",
            4 => "S",
            5 => "SW",
            6 => "W",
            7 => "NW",
            _ => "Unknown",
        };
    }

    private static Vector3 MirrorAcrossArenaCenter(Vector3 position)
    {
        return new Vector3(
            ArenaCenterX + (ArenaCenterX - position.X),
            position.Y,
            ArenaCenterZ + (ArenaCenterZ - position.Z));
    }

    private static float GetClockwiseAngleFromNorth(Vector3 position)
    {
        return GetClockwiseAngleFromNorth(position.X, position.Z);
    }

    private static float GetClockwiseAngleFromNorth(float x, float z)
    {
        var dx = x - ArenaCenterX;
        var dz = z - ArenaCenterZ;
        var degrees = MathF.Atan2(dx, -dz) * 180.0f / MathF.PI;
        return degrees < 0.0f ? degrees + 360.0f : degrees;
    }

    private static bool TryGetNorthMarkerLabel(Vector3 northPosition, out string label)
    {
        label = string.Empty;

        var northAngle = GetClockwiseAngleFromNorth(northPosition);
        var bestAngleDifference = float.MaxValue;
        var bestLabel = string.Empty;

        foreach (var marker in ReferenceWaymarks)
        {
            if (DistanceFromCenter(marker.X, marker.Z) < MinimumWaymarkDistanceFromCenter)
            {
                continue;
            }

            var angleDifference = MathF.Abs(NormalizeDegrees(GetClockwiseAngleFromNorth(marker.X, marker.Z) - northAngle));
            if (angleDifference >= bestAngleDifference)
            {
                continue;
            }

            bestAngleDifference = angleDifference;
            bestLabel = marker.Label;
        }

        if (bestAngleDifference > MaximumWaymarkAngleDifferenceDegrees || string.IsNullOrWhiteSpace(bestLabel))
        {
            return false;
        }

        label = bestLabel;
        return true;
    }

    private static float NormalizeDegrees(float degrees)
    {
        while (degrees > 180.0f)
        {
            degrees -= 360.0f;
        }

        while (degrees < -180.0f)
        {
            degrees += 360.0f;
        }

        return degrees;
    }

    private static float DistanceFromCenter(Vector3 position)
    {
        return DistanceFromCenter(position.X, position.Z);
    }

    private static float DistanceFromCenter(float x, float z)
    {
        var dx = x - ArenaCenterX;
        var dz = z - ArenaCenterZ;
        return MathF.Sqrt((dx * dx) + (dz * dz));
    }

    private static float DistanceXZ(Vector3 left, Vector3 right)
    {
        var dx = left.X - right.X;
        var dz = left.Z - right.Z;
        return MathF.Sqrt((dx * dx) + (dz * dz));
    }
}

public enum P3LimitCutRotation
{
    Unknown,
    Clockwise,
    CounterClockwise,
}

public sealed record P3LimitCutState(
    bool IsActive,
    string NewNorth,
    string NewNorthMarker,
    P3LimitCutRotation Rotation,
    int ObservedCloneCount)
{
    public static P3LimitCutState Inactive { get; } = new(false, string.Empty, string.Empty, P3LimitCutRotation.Unknown, 0);
}

internal readonly record struct WaymarkReference(string Label, float X, float Z);
