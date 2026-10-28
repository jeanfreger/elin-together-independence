using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ElinTogether.Helper;
using ElinTogether.Models;
using ElinTogether.Net;
using HarmonyLib;

namespace ElinTogether.Patches;

/// <summary>
///     The actions of these items and furniture are lambdas that only ran in the client's game, where it cannot give
///     itself a condition, change a stack or a charge, or keep what it creates: injecting did nothing and kept the
///     syringe, a furniture ticket gave the furniture for free, a well never ran dry. The wrench, the eco mark, the
///     brush and the hammer changed fields of the object on that copy only, a whip's egg was lost with its charge <br />
///     The entry the client picks is a request, the host runs it for the client's character <br />
///     What the host runs, for a guest or for itself, it tells the others where it changed fields of the objects of
///     the tile (<see cref="CardSettingDelta.Fields" />) <br />
///     Closed list: another trait goes through here only after its window and its "player" are looked at, as for
///     <c>CardOnUseDelta</c>
/// </summary>
[HarmonyPatch]
internal static class CardActReplayEvent
{
    internal static IEnumerable<MethodBase> TargetMethods()
    {
        foreach (var type in new[] {
                     typeof(TraitTicketFurniture), typeof(TraitSyringeBlood), typeof(TraitSyringeGene),
                     typeof(TraitSyringeHeaven), typeof(TraitSyringeUnicorn), typeof(TraitStethoscope), typeof(TraitLeash),
                     typeof(TraitWrench), typeof(TraitEcoMark), typeof(TraitToolBrushStrip), typeof(TraitToolHammerStrip),
                     typeof(TraitWhipLove), typeof(TraitWhipEgg), typeof(TraitDeedDivorce), typeof(TraitDeedLostRing),
                 }) {
            yield return AccessTools.DeclaredMethod(type, nameof(Trait.TrySetHeldAct));
        }

        yield return AccessTools.DeclaredMethod(typeof(TraitWell), nameof(Trait.TrySetAct));
        // the war horns of a defense quest (next wave, retreat, call for help): the waves are the host's (hunt 6 item 1)
        yield return AccessTools.DeclaredMethod(typeof(TraitCoreDefense), nameof(Trait.TrySetAct));
    }

    [HarmonyPrefix]
    internal static void Before(ActPlan p, out ActPlan.Item[] __state)
    {
        __state = p.list.ToArray();
    }

    [HarmonyPostfix]
    internal static void After(Trait __instance, ActPlan p, ActPlan.Item[] __state)
    {
        if (NetSession.Instance.Connection is not { } connection || ElinDelta.IsApplying ||
            (__instance is TraitDeedDivorce or TraitDeedLostRing && !SharedMarriages.Enabled)) {
            return;
        }

        // the entries this trait just added, in the order the host will build them
        var index = 0;
        foreach (var item in p.list.Where(i => !__state.Contains(i)).ToArray()) {
            if (item.act is DynamicAct act) {
                var (rank, own) = (index, act.onPerform);
                act.onPerform = connection is ElinNetClient client
                    ? () => Request(client, __instance, item, act, rank, own)
                    : () => {
                        // the host's own gesture, and the replay of a guest's request (it runs simulated, so it lands
                        // here too): the others are told the fields it changes
                        var before = CardSettingDelta.Snapshot(item.pos);
                        var done = own?.Invoke() ?? false;
                        CardSettingDelta.TellChanged(before);
                        return done;
                    };
            }

            index++;
        }
    }

    internal const string RetreatHorn = "actEvacDefense";

    private static bool Request(ElinNetClient client, Trait trait, ActPlan.Item item, DynamicAct act, int index,
        Func<bool>? own)
    {
        var card = trait.owner;
        if (!CardCache.Contains(card)) {
            return false;
        }

        var delta = new CardActReplayDelta {
            Card = card,
            RootCard = card.GetRootCard(),
            User = EClass.pc,
            Held = trait is not (TraitWell or TraitCoreDefense),
            Pos = item.pos,
            Target = item.tc,
            Index = index,
            Id = act.id,
            Leashed = (trait is TraitLeash && item.tc is Chara companion) ? !companion.isLeashed : null,
        };

        // the divorce asks yes or no here, the host replays the yes (SharedMarriages)
        if (trait is TraitDeedDivorce) {
            Dialog.YesNo("dialogDivorce", () => client.Delta.AddRemote(delta));
            return false;
        }

        client.Delta.AddRemote(delta);

        // the wish of a well: the host never rolls it for another player, this game does, with its own key and
        // flag, at the game's odds (4/5 x 4/5 x 3/4 x 1/10)
        // ponytail: one roll on top of what the host draws for this drink, not instead of it
        if (trait is TraitWell { polluted: false, Charges: > 0 } && !EClass.player.wellWished && EClass.rnd(21) == 0) {
            if (EClass.player.CountKeyItem("well_wish") > 0) {
                EClass.player.ModKeyItem("well_wish", -1);
                ActEffect.Proc(EffectId.Wish, EClass.pc, null,
                    50 + EClass.player.CountKeyItem("well_enhance") * 50 + EClass.player.flags.fishStolen * 50);
                EClass.player.wellWished = true;
            }
        }

        // the scope opens its window and the leash flips its bit here, where the player is: the request only keeps the
        // host's part in step. For the others the host does it all, and ends the turn as the lambda did
        if (trait is TraitLeash or TraitStethoscope) {
            return own?.Invoke() ?? false;
        }

        // the retreat evacuates the one who blew it, in its own game: the host only marks the zone won and retreated
        if (trait is TraitCoreDefense && act.id == RetreatHorn) {
            return own?.Invoke() ?? false;
        }

        // the ticket, the four tools and the lost ring deed end no turn, the whips do (as in the game)
        return trait is not (TraitTicketFurniture or TraitWrench or TraitEcoMark or TraitToolBrushStrip or TraitToolHammerStrip or
            TraitDeedLostRing);
    }
}

/// <summary>
///     A leash tugs the companion toward the player who walks, as the game does for "the player" <br />
///     A guest's companion keeps its leash in a key of its own: the game's bit would make the host's own steps tug it
///     toward the host
/// </summary>
internal static class GuestLeash
{
    internal const string Key = "emp_leash";

    /// <summary>
    ///     After a guest's step on the host: its leashed companions that are left behind follow (Chara.cs, the move of
    ///     the player)
    /// </summary>
    internal static void Follow(Chara player)
    {
        // (a leashed companion left behind with no line at all, 2026-10-22: is this even reached)
        if (player.IsRemotePlayer && EClass._map.charas.Any(c => c.GetInt(Key) != 0 && c.Dist(player) > 2)) {
            EmpLog.Debug("Leash of {Player}: follow after a step to {@To}", player.uid, (Position?)player.pos);
        }

        if (!player.IsRemotePlayer || !EClass._zone.PetFollow || EClass._zone.IsRegion ||
            (EClass._zone.KeepAllyDistance &&
             (PlayerTacticsDelta.KeepDistanceOf(player) ?? EClass.game.config.tactics.allyKeepDistance))) {
            // (a leashed companion left behind by one of these: which one, 2026-10-19)
            if (player.IsRemotePlayer && CompanionHelper.CompanionsOf(player).Any(m => m.GetInt(Key) != 0 && m.Dist(player) > 2)) {
                EmpLog.Debug("Leash of {Player}'s companions not pulled: pets follow {PetFollow}, region {Region}, keep distance {Zone}/{Own}/{Host}",
                    player.uid, EClass._zone.PetFollow, EClass._zone.IsRegion, EClass._zone.KeepAllyDistance,
                    PlayerTacticsDelta.KeepDistanceOf(player), EClass.game.config.tactics.allyKeepDistance);
            }

            return;
        }

        foreach (var member in CompanionHelper.CompanionsOf(player)) {
            if (member.GetInt(Key) == 0) {
                continue;
            }

            if (member.host != null || member.IsDisabled || member.HasCondition<ConEntangle>() || member.IsInCombat) {
                // (a leashed companion left behind: which of the game's own reasons held it, 2026-10-11)
                if (member.Dist(player) > 2) {
                    EmpLog.Debug("Leash of {Uid} not pulled: carried {Carried}, disabled {Disabled}, entangled {Entangled}, in combat {Combat} (enemy {Enemy})",
                        member.uid, member.host != null, member.IsDisabled, member.HasCondition<ConEntangle>(), member.IsInCombat,
                        member.enemy?.uid ?? 0);
                }

                continue;
            }

            // one tug a step in the game, where every step of the player is seen. Here the steps of a guest come as
            // they come (two tiles in one message, a step whose tug was blocked): the leash pulls until the
            // companion is beside it again, a few tiles at most (it was left three tiles behind after five steps)
            var tugs = new System.Text.StringBuilder();
            for (var tug = 0; tug < 4 && member.Dist(player) > 1; tug++) {
                var was = member.pos.Copy();
                var result = member.TryMoveTowards(player.pos);
                tugs.Append($"{was.x},{was.z}>{result}>{member.pos.x},{member.pos.z} ");
                if (result is var moved and not Card.MoveResult.Success) {
                    EmpLog.Debug("Leash of {Uid} pulled in vain: {Result} from {@From} toward {@To}",
                        member.uid, moved, (Position?)member.pos, (Position?)player.pos);
                    break;
                }
            }

            if (member.Dist(player) > 2) {
                EmpLog.Debug("Leash of {Uid} pulled but still {Dist} away, at {@At}: {Tugs} (ai {Ai}, host {Host}, speed {Speed})",
                    member.uid, member.Dist(player), (Position?)member.pos, tugs.ToString(), member.ai?.GetType().Name, member.host?.uid ?? 0, member.Speed);
            }
        }

        // (a leashed one left behind that is not among this player's companions: whose it is, 2026-10-22)
        foreach (var c in EClass._map.charas) {
            if (c.GetInt(Key) != 0 && c.Dist(player) > 2 && !CompanionHelper.CompanionsOf(player).Contains(c) &&
                CompanionHelper.OwnerOf(c) is var owner && (owner == player || c.CompanionOwnerUid == player.uid)) {
                EmpLog.Debug("Leash of {Uid} not pulled: not a companion of {Player} (owner {Owner}, in party {InParty})",
                    c.uid, player.uid, owner?.uid ?? 0, c.party?.members.Contains(c) ?? false);
            }
        }
    }

    /// <summary>
    ///     A guest's step the host could not make: its leashed companions are not pulled after it
    /// </summary>
    internal static void NoteMissedStep(Chara player, Point to)
    {
        if (player.IsRemotePlayer && EClass._map.charas.Any(c => c.GetInt(Key) != 0 && c.Dist(player) > 1)) {
            EmpLog.Debug("Leash of {Player}'s companions not pulled: its step to {@To} failed from {@From}",
                player.uid, (Position?)to, (Position?)player.pos);
        }
    }
}
