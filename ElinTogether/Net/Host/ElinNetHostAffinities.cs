using System.Collections.Generic;
using System.Linq;
using ElinTogether.Helper;
using ElinTogether.Models;

namespace ElinTogether.Net;

/// <summary>
///     Host option "Affinity per player" (<see cref="AffinityPerPlayer" />): what each resident thinks of the other
///     players, kept in the host's save. The host's own value stays where the game keeps it (Chara._affinity): a game
///     without the mod reads the world owner's affinities
/// </summary>
internal partial class ElinNetHost
{
    /// <summary>
    ///     Resident chara uid -> player chara uid -> affinity, for every player but the world owner
    /// </summary>
    [ElinGameIOProperty("affinity_per_player")]
    internal static Dictionary<int, Dictionary<int, int>> Affinities
    {
        get => field ??= [];
        set;
    }

    /// <summary>
    ///     A player joins: what the residents think of it, its own column only. Its companions and spouse keep the
    ///     value they had from before the option (the group's), written down now so that it no longer follows the
    ///     host's
    /// </summary>
    internal void TellAffinities(Chara guest, int peerId)
    {
        if (IsZoneSession || !AffinityPerPlayer.On || guest.uid == player.uidChara) {
            return;
        }

        foreach (var chara in game.cards.globalCharas.Values.Concat(_map?.charas ?? [])) {
            AffinityPerPlayer.Keep(chara, guest.uid);
        }

        var column = new Dictionary<int, int>();
        foreach (var (npc, row) in Affinities) {
            if (row.TryGetValue(guest.uid, out var value)) {
                column[npc] = value;
            }
        }

        SendDeltaTo(peerId, new PlayerAffinitiesDelta {
            Values = column,
        });
    }
}
