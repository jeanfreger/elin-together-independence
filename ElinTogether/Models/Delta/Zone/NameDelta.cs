using ElinTogether.Net;
using ElinTogether.Patches;
using MessagePack;

namespace ElinTogether.Models;

/// <summary>
///     The name a player gives its base, or the faction: typed in a box of its own game, it only changed its own
///     copy. Seen where it changed (<see cref="NameWatch" />), told to the others
/// </summary>
[MessagePackObject]
public class NameDelta : ElinDelta
{
    public const byte OfZone = 0;
    public const byte OfFaction = 1;

    [Key(0)]
    public required byte Kind { get; init; }

    [Key(1)]
    public int ZoneUid { get; init; }

    [Key(2)]
    public string? Name { get; init; }

    // longer than any name box of the game takes
    private const int MaxName = 64;

    /// <summary>
    ///     A name of the base the sender stands on, or of the faction, of a name's length on one line (security audit
    ///     2026-10-27, item 10: any zone's name, of any length, with line breaks)
    /// </summary>
    internal override string? OnHostPermit(ElinNetHost host, Chara? sender)
    {
        if (string.IsNullOrEmpty(Name)) {
            return null;
        }

        if (Name!.Length > MaxName || System.Linq.Enumerable.Any(Name, char.IsControl)) {
            return $"a name of {Name.Length} characters, or on more than one line";
        }

        return Kind switch {
            OfFaction => null,
            OfZone when sender?.currentZone is { IsPCFaction: true } zone && zone.uid == ZoneUid => null,
            OfZone => $"the name of zone {ZoneUid}, not the base the sender stands on",
            _ => $"a name of kind {Kind}",
        };
    }

    internal override void OnHostRefused(ElinNetHost host)
    {
        var kept = Kind == OfFaction ? EClass.Home?.name : (game.spatials.Find(ZoneUid) as Zone)?.name;
        if (!string.IsNullOrEmpty(kept) && Kind is OfZone or OfFaction) {
            host.SendDeltaTo(OriginPeer, new NameDelta { Kind = Kind, ZoneUid = ZoneUid, Name = kept });
        }
    }

    protected override void OnApply(ElinNetBase net)
    {
        if (string.IsNullOrEmpty(Name)) {
            return;
        }

        if (net is ElinNetHost host) {
            if (!host.ActiveRemoteCharas.ContainsKey(OriginPeer) || host.IsAwayPeer(OriginPeer)) {
                return;
            }

            // only the host manages the base: the sender gets the name the host keeps, which puts its copy back
            if (NetSession.Instance.Rules.HostManagesBase && !host.IsZoneSession) {
                var kept = Kind == OfFaction ? EClass.Home?.name : (game.spatials.Find(ZoneUid) as Zone)?.name;
                if (!string.IsNullOrEmpty(kept)) {
                    host.SendDeltaTo(OriginPeer, new NameDelta { Kind = Kind, ZoneUid = ZoneUid, Name = kept });
                }

                return;
            }

            host.Delta.AddRemote(this);
        }

        if (Kind == OfFaction) {
            if (EClass.Home is { } home) {
                home.name = Name;
            }
        } else if (game.spatials.Find(ZoneUid) is Zone { IsPCFaction: true } zone) {
            zone.name = Name;
            zone.idPrefix = 0;
            WidgetDate.Refresh();
        }

        NameWatch.Accept();
    }
}
