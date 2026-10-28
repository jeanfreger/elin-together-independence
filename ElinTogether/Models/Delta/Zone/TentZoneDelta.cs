using ElinTogether.Helper;
using ElinTogether.Net;
using MessagePack;

namespace ElinTogether.Models;

/// <summary>
///     A wrench upgrades the inside of a tent (TraitWrench.Upgrade: electricity, soil, sea bed). That is the zone of
///     the tent, not a card: nothing told it to the other games, and the game that did it alone (a guest on a map
///     of its own) lost it when the world's copy came back. "The electricity works, then stops after a while"
///     (player report, 2026-10-14). Told as values, not steps: a game that hears it twice keeps the same
/// </summary>
[MessagePackObject]
public class TentZoneDelta : ElinDelta
{
    internal static readonly int[] Upgrades = [3606, 2201, 2200];

    // what one wrench adds to each (TraitWrench.Upgrade), and the most the game lets it reach (sea bed once, soil
    // while below 10)
    private static readonly int[] Steps = [1, 2, 5];
    private static readonly int[] Tops = [1, int.MaxValue, 14];

    [Key(0)]
    public required int ZoneUid { get; init; }

    /// <summary>
    ///     The base value of each of <see cref="Upgrades" />, in that order
    /// </summary>
    [Key(1)]
    public required int[] Values { get; init; }

    /// <summary>
    ///     A tent's zone, each value what one wrench makes of the host's (security audit 2026-10-27, item 10: any value
    ///     came, any zone's electricity or soil)
    /// </summary>
    internal override string? OnHostPermit(ElinNetHost host, Chara? sender)
    {
        if (game?.spatials.Find(ZoneUid) is not Zone zone) {
            return null;
        }

        if (zone is not Zone_Tent || Values.Length != Upgrades.Length) {
            return $"tent upgrades of zone {ZoneUid}, not a tent";
        }

        // (one wrench, one upgrade)
        var changed = 0;
        for (var i = 0; i < Upgrades.Length; i++) {
            var now = zone.elements.Base(Upgrades[i]);
            if (Values[i] != now && ++changed > 1) {
                return $"tent upgrades of zone {ZoneUid}: more than one at once";
            }

            if (Values[i] < now || Values[i] > now + Steps[i] || Values[i] > System.Math.Max(now, Tops[i])) {
                return $"tent upgrade {Upgrades[i]} of zone {ZoneUid}: {now} -> {Values[i]}";
            }
        }

        return null;
    }

    internal override void OnHostRefused(ElinNetHost host)
    {
        if (game?.spatials.Find(ZoneUid) is Zone zone) {
            host.SendDeltaTo(OriginPeer, Of(zone));
        }
    }

    protected override void OnApply(ElinNetBase net)
    {
        if (net is ElinNetHost host) {
            host.SendDeltaToAllExcept(OriginPeer, this);
        }

        if (game?.spatials.Find(ZoneUid) is not Zone zone) {
            EmpLog.Warning("Tent zone {ZoneUid} is unknown here, its upgrade is not taken", ZoneUid);
            return;
        }

        for (var i = 0; i < Upgrades.Length && i < Values.Length; i++) {
            zone.elements.SetBase(Upgrades[i], Values[i]);
        }

        EmpLog.Debug("Tent zone {ZoneUid}: upgrades {Values}", ZoneUid, string.Join(",", Values));
    }

    internal static TentZoneDelta Of(Zone zone)
    {
        return new() {
            ZoneUid = zone.uid,
            Values = System.Array.ConvertAll(Upgrades, id => zone.elements.Base(id)),
        };
    }
}
