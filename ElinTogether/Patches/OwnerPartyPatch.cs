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

    /// <summary>
    ///     Arriving in a town after a journey on the world map pays the party in experience (Player.MoveZone): every
    ///     player and every player's companion, wherever they were, for the local player's steps. The local player's
    ///     side (hunt 6 item 3)
    /// </summary>
    [HarmonyTranspiler]
    [HarmonyPatch(typeof(Player), nameof(Player.MoveZone))]
    internal static IEnumerable<CodeInstruction> OnTravelReward(IEnumerable<CodeInstruction> instructions)
    {
        var codes = new CodeMatcher(instructions);
        var found = 0;
        while (codes.MatchStartForward(
                   new CodeMatch(OpCodes.Call, GetPc),
                   new CodeMatch(OpCodes.Ldfld, PartyField),
                   new CodeMatch(i => i.Calls(Members))).IsValid) {
            codes.Advance(1)
                .SetAndAdvance(OpCodes.Call, AccessTools.Method(typeof(OwnerPartyPatch), nameof(Side)))
                .RemoveInstruction();
            found++;
        }

        if (found == 0) {
            EmpLog.Warning("Travel reward: Player.MoveZone reads no party, left as the game has it");
        }

        return codes.InstructionEnumeration();
    }

    /// <summary>
    ///     The journey block of Player.MoveZone, for the arrival that does not call it: the first scene of a new world
    ///     copy (ElinNetClientZone, player.zone is still null there). Same conditions and reward, this player's side
    /// </summary>
    internal static void PayTravel(Zone zone)
    {
        var player = EClass.player;
        if (EClass.pc.currentZone != zone || zone.IsRegion || player.LastTravelZone == zone) {
            return;
        }

        if (player.LastTravelZone != null && player.distanceTravel > 2) {
            var hours = EClass.world.date.GetElapsedHour(player.dateTravel);
            Msg.Say("travel", player.LastTravelZone.Name, Date.GetText(player.dateTravel, Date.TextFormat.Travel),
                (hours / 24).ToString(), (hours % 24).ToString());
            Msg.Say("travel2", player.distanceTravel.ToString(),
                (EClass.pc.party.members.Count == 1 ? "you" : "youAndCompanion").lang());
            foreach (var member in Side(EClass.pc)) {
                member.AddExp(player.distanceTravel / 3);
                member.elements.ModExp(240, 30 + player.distanceTravel * 5);
            }
        }

        player.distanceTravel = 0;
        player.dateTravel = EClass.world.date.GetRaw();
        player.uidLastTravelZone = zone.uid;
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
