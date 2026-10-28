using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using ElinTogether.Helper;
using ElinTogether.Net;
using HarmonyLib;

namespace ElinTogether.Patches;

/// <summary>
///     Nasu's fairy dungeon opens its locked stairs, and plays its boss scene, for a party with the fairy Nasu
///     (Zone_DungeonFairy.CanUnlockExit, OnGenerateMap, TraitStairsLocked.OnUse). The game looks for her in the one
///     party Elin knows, which holds every player's companions: another player's Nasu, far away, opened the stairs
///     here (hunt 6 item 4). The Nasu of a player who is here: the local player's, or one standing on this map
/// </summary>
[HarmonyPatch]
internal static class FairyDungeonPatch
{
    internal static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.PropertyGetter(typeof(Zone_DungeonFairy), nameof(Zone_DungeonFairy.CanUnlockExit));
        yield return AccessTools.DeclaredMethod(typeof(Zone_DungeonFairy), nameof(Zone_DungeonFairy.OnGenerateMap));
        yield return AccessTools.DeclaredMethod(typeof(TraitStairsLocked), nameof(TraitStairsLocked.OnUse), [typeof(Chara)]);
    }

    [HarmonyTranspiler]
    internal static IEnumerable<CodeInstruction> OnFind(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
    {
        // party.Find(id) -> Here(party, id), same stack
        var find = AccessTools.Method(typeof(Party), nameof(Party.Find), [typeof(string)]);
        var codes = new List<CodeInstruction>(instructions);
        var found = 0;
        foreach (var code in codes) {
            if (code.Calls(find)) {
                code.opcode = OpCodes.Call;
                code.operand = AccessTools.Method(typeof(FairyDungeonPatch), nameof(Here));
                found++;
            }
        }

        if (found == 0) {
            EmpLog.Warning("Fairy dungeon: {Method} looks for no party member, left as the game has it", __originalMethod.Name);
        }

        return codes;
    }

    internal static Chara? Here(Party party, string id)
    {
        if (NetSession.Instance.Connection is null) {
            return party.Find(id);
        }

        // the local player's own, even before it stands on the map it is making (OnGenerateMap), or one that is here
        return party.members.Find(m => m.id == id &&
                                       (CompanionHelper.OwnerOf(m) == EClass.pc || m.IsAliveInCurrentZone));
    }
}
