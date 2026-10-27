using System.Collections.Generic;
using System.Reflection.Emit;
using ElinTogether.Helper;
using ElinTogether.Net;
using HarmonyLib;

namespace ElinTogether.Patches;

/// <summary>
///     Hunt 5 H5-13, the events of a map that read the local player (EClass.pc) where every player is meant. Read for
///     the others of the row: a siege (ZoneEventSiege, the debug menu only) comes from the map's edge and goes for the
///     base's people (GoalSiege), a survival raid (ZoneEventRaid) comes out of the core or the void gate, the waves of
///     a quest's zone go for "the player's party" (ZoneEventQuest.AggroEnemy), which holds every player of the map
///     where it is run (ElinNetHost.RegisterPlayer makes them allies), and a subdue counts the enemies of that party:
///     the same for every player. A wedding read the local player twice
/// </summary>
[HarmonyPatch]
internal static class ZoneEventPlayersPatch
{
    /// <summary>
    ///     A wedding brings the base's people to their seats (ZoneEventWedding.OnVisit), all of the player's faction but
    ///     "the player": another player's character was taken too, moved to a seat and sent back where it stood when
    ///     the wedding ended. And the blood of the feast is a vampire's (feat 1250) when the one who marries is one: it
    ///     read the local player, the host when it came along to a guest's wedding
    /// </summary>
    [HarmonyTranspiler]
    [HarmonyPatch(typeof(ZoneEventWedding), nameof(ZoneEventWedding.OnVisit))]
    internal static IEnumerable<CodeInstruction> OnWedding(IEnumerable<CodeInstruction> instructions)
    {
        var codes = new List<CodeInstruction>(instructions);
        var getPc = AccessTools.PropertyGetter(typeof(EClass), nameof(EClass.pc));
        var reads = codes.FindAll(c => c.Calls(getPc));
        var first = reads.Count == 2 ? codes.IndexOf(reads[0]) : -1;
        if (first < 1 || !codes[first - 1].IsLdloc()) {
            EmpLog.Warning("Wedding: OnVisit reads the player {Count} times, not as expected: left as the game has it", reads.Count);
            return codes;
        }

        // value != EClass.pc -> value != (value is a player ? value : EClass.pc)
        reads[0].operand = AccessTools.Method(typeof(ZoneEventPlayersPatch), nameof(SkipIfPlayer));
        codes.Insert(first, new(OpCodes.Dup));
        reads[1].operand = AccessTools.Method(typeof(ZoneEventPlayersPatch), nameof(Married));
        return codes;
    }

    internal static Chara SkipIfPlayer(Chara chara)
    {
        return chara.IsPlayer || chara.GetBool("remote_chara") ? chara : EClass.pc;
    }

    /// <summary>
    ///     The player whose wedding it is: the one the host came along with, else the local one
    /// </summary>
    internal static Chara Married()
    {
        return NetSession.Instance.Connection is ElinNetHost host &&
               EClass._zone.events.GetEvent<ZoneEventWedding>() is { } wedding &&
               host.AccompaniedTaker(wedding.uidQuest) is { } taker
            ? taker
            : EClass.pc;
    }
}
