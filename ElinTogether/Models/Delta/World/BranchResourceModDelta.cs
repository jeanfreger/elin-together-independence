using System;
using ElinTogether.Net;
using MessagePack;

namespace ElinTogether.Models;

[MessagePackObject]
public class BranchResourceModDelta : ElinDelta
{
    [Key(0)]
    public required int ZoneUid { get; init; }

    [Key(1)]
    public required int ResourceType { get; init; }

    [Key(2)]
    public required int Amount { get; init; }

    // what one player may move of a base's resources (security audit 2026-10-27, item 6: any amount of its money,
    // knowledge or influence, up or down): far above what a real game's acts move
    private const int MaxAmount = 1000;
    private const int HourlyGain = 5000;
    private const int HourlyLoss = 1000;
    private static readonly System.Collections.Generic.Dictionary<(int Peer, int Type, bool Up), (float Since, int Moved)> Moved = [];

    internal override string? OnHostPermit(ElinNetHost host, Chara? sender)
    {
        if (NoCharacter(sender) is { } none) {
            return none;
        }

        if (host.IsAwayPeer(OriginPeer) || sender!.currentZone?.uid != ZoneUid) {
            return $"base resources of zone {ZoneUid} moved from elsewhere";
        }

        if (System.Math.Abs(Amount) > MaxAmount) {
            return $"base resource {ResourceType} moved by {Amount}";
        }

        var now = UnityEngine.Time.realtimeSinceStartup;
        var key = (OriginPeer, ResourceType, Amount > 0);
        if (!Moved.TryGetValue(key, out var moved) || now - moved.Since > 3600f) {
            moved = (now, 0);
        }

        var cap = Amount > 0 ? HourlyGain : HourlyLoss;
        if (moved.Moved + System.Math.Abs(Amount) > cap) {
            return $"base resource {ResourceType} moved by {Amount}, over the hour's {cap}";
        }

        Moved[key] = (moved.Since, moved.Moved + System.Math.Abs(Amount));
        return null;
    }

    protected override void OnApply(ElinNetBase net)
    {
        // host only
        if (net is not ElinNetHost) {
            return;
        }

        if (!Enum.IsDefined(typeof(HomeResourceType), ResourceType)) {
            EmpLog.Warning("Refusing branch resource mod from peer {PeerIndex}, {ResourceTypeRaw} undefined",
                OriginPeer, ResourceType);
            return;
        }

        if (_zone.uid != ZoneUid) {
            EmpLog.Warning("Refusing stale branch resource mod from peer {PeerIndex}, zone {ZoneUid} invalid",
                OriginPeer, ZoneUid);
            return;
        }

        var type = (HomeResourceType)ResourceType;
        if (Branch?.resources.Get(type.ToString()) is not { } resource) {
            EmpLog.Warning("Refusing branch resource mod from peer {PeerIndex}, zone {ZoneUid} invalid",
                OriginPeer, ZoneUid);
            return;
        }

        EmpLog.Debug("Mod branch resource {ResourceType} by {ResourceAmount} in zone {ZoneUid}",
            type, Amount, ZoneUid);

        using var _ = Simulate();
        resource.Mod(Amount, false);
    }
}