using System.Collections.Generic;
using System.Reflection.Emit;
using ElinTogether.Helper;
using ElinTogether.Net;
using EModding.Helper;
using HarmonyLib;

namespace ElinTogether.Patches;

/// <summary>
///     Hunt 6 item 8, what reads "the player's party" (EClass.pc.party, the one party Elin knows, which holds every
///     player and every player's companions, wherever they are) where one player's side is meant: that player and
///     its own companions. A concert's party guests (ZoneEventMusic) skip any member's kind on purpose: a unique
///     companion of a player far away must not be made twice, so it keeps the whole party
/// </summary>
[HarmonyPatch]
internal static class OwnerPartyPatch
{
    private static readonly System.Reflection.MethodInfo GetPc = AccessTools.PropertyGetter(typeof(EClass), nameof(EClass.pc));
    private static readonly System.Reflection.FieldInfo PartyField = AccessTools.Field(typeof(Chara), nameof(Chara.party));
    private static readonly System.Reflection.MethodInfo Members = AccessTools.PropertyGetter(typeof(Party), nameof(Party.members));

    /// <summary>
    ///     The militant feat (1419) of the party raises the strength, endurance and dexterity of its members while
    ///     enemies of "the player" stand around (ElementContainerCard.ValueBonus): every player's companions' feats
    ///     added up, counted against the host's enemies. The member's own player's side, against that player's enemies
    /// </summary>
    [HarmonyTranspiler]
    [HarmonyPatch(typeof(ElementContainerCard), nameof(ElementContainerCard.ValueBonus))]
    internal static IEnumerable<CodeInstruction> OnValueBonus(IEnumerable<CodeInstruction> instructions)
    {
        return new CodeMatcher(instructions)
            // foreach (var member in EClass.pc.party.members) -> in Side(this)
            .MatchStartForward(
                new CodeMatch(OpCodes.Call, GetPc),
                new CodeMatch(OpCodes.Ldfld, PartyField),
                new CodeMatch(i => i.Calls(Members)))
            .EnsureValid("ElementContainerCard.ValueBonus EClass.pc.party.members")
            .SetAndAdvance(OpCodes.Ldarg_0, null)
            .SetAndAdvance(OpCodes.Call, AccessTools.Method(typeof(OwnerPartyPatch), nameof(SideOfOwner)))
            .RemoveInstruction()
            // chara.IsHostile(EClass.pc) -> chara.IsHostile(Player(this))
            .MatchStartForward(
                new CodeMatch(OpCodes.Call, GetPc),
                new CodeMatch(i => i.Calls(AccessTools.Method(typeof(Chara), nameof(Chara.IsHostile), [typeof(Chara)]))))
            .EnsureValid("ElementContainerCard.ValueBonus IsHostile(EClass.pc)")
            .SetAndAdvance(OpCodes.Ldarg_0, null)
            .Insert(new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(OwnerPartyPatch), nameof(PlayerOf))))
            .InstructionEnumeration();
    }

    /// <summary>
    ///     The boss floor of the void sets every hostile on "the player's party" (Zone_Void.OnGenerateMap): on a
    ///     companion of a player far away. The side of the player making the map, the one who walks in
    /// </summary>
    [HarmonyTranspiler]
    [HarmonyPatch(typeof(Zone_Void), nameof(Zone_Void.OnGenerateMap))]
    internal static IEnumerable<CodeInstruction> OnVoidBoss(IEnumerable<CodeInstruction> instructions)
    {
        return new CodeMatcher(instructions)
            .MatchStartForward(
                new CodeMatch(OpCodes.Call, GetPc),
                new CodeMatch(OpCodes.Ldfld, PartyField),
                new CodeMatch(i => i.Calls(Members)))
            .EnsureValid("Zone_Void.OnGenerateMap EClass.pc.party.members")
            .Advance(1)
            .SetAndAdvance(OpCodes.Call, AccessTools.Method(typeof(OwnerPartyPatch), nameof(Side)))
            .RemoveInstruction()
            .InstructionEnumeration();
    }

    internal static Chara PlayerOf(ElementContainerCard elements)
    {
        return NetSession.Instance.Connection is not null && elements.owner is Chara chara &&
               CompanionHelper.OwnerOf(chara) is { } player
            ? player
            : EClass.pc;
    }

    internal static List<Chara> SideOfOwner(ElementContainerCard elements)
    {
        return Side(PlayerOf(elements));
    }

    /// <summary>
    ///     That player and its own companions. Alone: the party as the game has it
    /// </summary>
    internal static List<Chara> Side(Chara player)
    {
        if (NetSession.Instance.Connection is null || player.party is not { } party) {
            return player.party?.members ?? EClass.pc.party.members;
        }

        var side = CompanionHelper.CompanionsOf(player);
        if (party.members.Contains(player)) {
            side.Insert(0, player);
        }

        return side;
    }
}
