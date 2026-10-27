using System.Collections.Generic;
using ElinTogether.Helper;
using ElinTogether.Models;
using ElinTogether.Net;
using HarmonyLib;

namespace ElinTogether.Patches;

/// <summary>
///     Hunt 5 H5-09, the base's mail. The world keeper makes it (GameDate.ShipRandomPackages, FactionBranch.
///     ReceivePackages) and read the host's character and game for two things that are each player's own: the level
///     of a creature sent in a random parcel (now a player in turn, see PlayerInTurn) and the backer reward's gift
///     box (now asked by each player's own game for its own reward code, see BackerGiftDelta). Lutz's letters follow
///     the world's day count and land in the shared mailbox: the same for every player, left as they are
/// </summary>
[HarmonyPatch]
internal static class WorldMailPatch
{
    // asked once per game: the host keeps the answer on the character
    private static bool _asked;

    // the player today's random parcel is for (null: the host's character), see ParcelPlayer
    private static Chara? _parcelFor;

    /// <summary>
    ///     The creature in a random parcel is drawn at "the player's" level + 10, and the furniture that comes with
    ///     it too (TraitBaseContainer.PutChara): a player in turn
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(GameDate), nameof(GameDate.ShipRandomPackages))]
    internal static void OnShipRandomPackages()
    {
        _parcelFor = PlayerInTurn.OfWorld();
    }

    // a finalizer: a parcel made later (a coffin's) is "the player's" again, even if the game's method throws
    [HarmonyFinalizer]
    [HarmonyPatch(typeof(GameDate), nameof(GameDate.ShipRandomPackages))]
    internal static void OnShipRandomPackagesDone()
    {
        _parcelFor = null;
    }

    [HarmonyTranspiler]
    [HarmonyPatch(typeof(GameDate), nameof(GameDate.ShipRandomPackages))]
    internal static IEnumerable<CodeInstruction> OnShipLevel(IEnumerable<CodeInstruction> instructions)
    {
        return PlayerLevel(instructions);
    }

    [HarmonyTranspiler]
    [HarmonyPatch(typeof(TraitBaseContainer), nameof(TraitBaseContainer.PutChara))]
    internal static IEnumerable<CodeInstruction> OnPutCharaLevel(IEnumerable<CodeInstruction> instructions)
    {
        return PlayerLevel(instructions);
    }

    private static IEnumerable<CodeInstruction> PlayerLevel(IEnumerable<CodeInstruction> instructions)
    {
        var codes = new List<CodeInstruction>(instructions);
        var getPc = AccessTools.PropertyGetter(typeof(EClass), nameof(EClass.pc));
        var getLv = AccessTools.PropertyGetter(typeof(Card), nameof(Card.LV));
        var found = 0;
        for (var i = 0; i + 1 < codes.Count; i++) {
            if (codes[i].Calls(getPc) && codes[i + 1].Calls(getLv)) {
                codes[i].operand = AccessTools.Method(typeof(WorldMailPatch), nameof(ParcelPlayer));
                found++;
            }
        }

        if (found != 1) {
            EmpLog.Warning("World mail: the parcel reads the player's level {Count} times, not once", found);
        }

        return codes;
    }

    internal static Chara ParcelPlayer()
    {
        return _parcelFor ?? EClass.pc;
    }

    /// <summary>
    ///     In a guest's game, a day passed (WorldDateAdvanceDelta replays Player.OnAdvanceDay): from the second day
    ///     on, as the game asks for the host, a reward code here asks the host for this character's gift box
    /// </summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(Player), nameof(Player.OnAdvanceDay))]
    internal static void OnAdvanceDay()
    {
        if (_asked || NetSession.Instance.Transport is not ElinNetClient || EClass.player.stats.days < 2 ||
            EClass.pc.GetBool(BackerGiftDelta.Given) || !Steam.HasDLC(ID_DLC.BackerReward)) {
            return;
        }

        _asked = true;
        PersonalQuests.TellHost(new BackerGiftDelta());
        EmpLog.Information("Backer reward code here: asked the host for this character's gift box");
    }
}
