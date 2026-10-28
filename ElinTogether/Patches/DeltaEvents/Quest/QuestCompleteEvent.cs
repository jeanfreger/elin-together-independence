using ElinTogether.Helper;
using ElinTogether.Models;
using ElinTogether.Net;
using HarmonyLib;

namespace ElinTogether.Patches;

[HarmonyPatch(typeof(Quest), nameof(Quest.Complete))]
internal static class QuestCompleteEvent
{
    [HarmonyPrefix]
    internal static bool OnClientComplete(Quest __instance)
    {
        if (NetSession.Instance.IsHost) {
            return true;
        }

        CompleteQuietly(__instance, false);
        return false;
    }

    /// <summary>
    ///     The quest is done for the quest log, without the rewards: they go to the player who completed it,
    ///     where it completed it
    /// </summary>
    /// <param name="shared">on the host: fame and karma are the world's, whoever completed the quest</param>
    internal static void CompleteQuietly(Quest quest, bool shared)
    {
        var game = EClass.game;
        game.quests.Remove(quest);
        game.quests.completedIDs.Add(quest.id);
        game.quests.completedTypes.Add(quest.GetType().ToString());

        quest.ShowCompleteText();

        if (quest.chara?.quest?.uid == quest.uid) {
            quest.chara.quest = null;
        }

        quest.ClientZone?.completedQuests.Add(quest.uid);
        quest.isComplete = true;

        if (!shared) {
            return;
        }

        if (quest.FameOnComplete > 0) {
            EClass.player.ModFame(EClass.rndHalf(quest.FameOnComplete));
        }

        using var _ = PlayerKarma.Own();
        EClass.player.ModKarma(1);
    }

    [HarmonyPostfix]
    internal static void OnQuestComplete(Quest __instance)
    {
        if (PersonalQuests.IsPersonal(__instance)) {
            // on someone's map, that host gives the rewards; the quest is nobody else's business. Also when
            // it completes while a message from the host is applied: the kill that ends a hunt
            if (NetSession.Instance.Connection is ElinNetClient client && !PlayerStandIn.IsActive) {
                PersonalQuests.MarkTurnedIn(__instance.uid);
                client.Delta.AddRemote(new QuestCompleteDelta {
                    Uid = __instance.uid,
                    Id = __instance.id,
                    Data = LZ4Bytes.Create(__instance),
                    LastWave = __instance is QuestDefenseGame ? QuestDefenseGame.lastWave : 0,
                    Bonus = __instance is QuestDefenseGame ? QuestDefenseGame.bonus : 0,
                });
            }

            return;
        }

        if (ElinDelta.IsApplying) {
            return;
        }


        var delta = new QuestCompleteDelta {
            Uid = __instance.uid,
            Id = __instance.id,
        };

        // travelling alone: the quest log is the world's, the host tells everyone
        QuestAwaySync.Send(delta);

        NetSession.Instance.Connection?.Delta.AddRemote(delta);
    }
}

/// <summary>
///     Quests of a player away from the host (travelling alone, or hosting a zone): accepted, advanced and
///     completed in its copy of the world, reported to the host, which keeps the one quest log of the world
/// </summary>
internal static class QuestAwaySync
{
    internal static void Send(ElinDelta delta)
    {
        if (NetSession.Instance is { IsZoneAuthority: true, Transport: ElinNetClient main }) {
            main.SendWhileAway(delta);
        }
    }
}

/// <summary>
///     Quest rewards drop where the player who completed the quest stands
/// </summary>
[HarmonyPatch(typeof(Player), nameof(Player.DropReward))]
internal static class QuestRewardPatch
{
    private static Chara? _receiver;
    private static bool _nothing;

    /// <summary>
    ///     The player the host runs a quest step for, if any
    /// </summary>
    internal static Chara? Receiver => _receiver;

    /// <summary>
    ///     While the host runs a quest step for a player on its map: what the step gives is that player's
    /// </summary>
    internal static ScopeExit GiveTo(ElinNetHost host, int peerId)
    {
        _receiver = host.ActiveRemoteCharas.TryGetValue(peerId, out var receiver) ? receiver : null;
        return new() {
            OnExit = () => _receiver = null,
        };
    }

    /// <summary>
    ///     While the host repeats a quest step a player made in its own copy of the world, where it got the rewards
    /// </summary>
    /// <param name="earner">the player who made that step, and has what it gives: the others get their share</param>
    internal static ScopeExit GiveNothing(Chara? earner = null)
    {
        _nothing = true;
        _earner = earner;
        return new() {
            OnExit = () => {
                _nothing = false;
                _earner = null;
            },
        };
    }

    // the player who earned what the host drops "for nothing" (it has it already, in its own copy of the world)
    private static Chara? _earner;

    /// <summary>
    ///     The player alone on its own map whose quest step the host repeats now, if any
    /// </summary>
    internal static Chara? Earner => _nothing ? _earner : null;

    /// <summary>
    ///     Whatever was done with it above: a reward of the story is the group's, every other player gets its share
    ///     (ElinNetHost.NoteStoryReward; not in a client's game, the host's hears of it)
    /// </summary>
    [HarmonyPostfix]
    internal static void OnRewardDropped(Thing __result)
    {
        if ((NetSession.Instance.Connection is ElinNetClient && !ElinDelta.IsApplying) || TeamOwnStory.Withheld(__result)) {
            return;
        }

        ElinNetHost.NoteStoryReward(__result, _nothing ? _earner : _receiver);
    }

    [HarmonyPrefix]
    internal static bool OnDropReward(Thing t, ref Thing __result)
    {
        // (each player or team living its own story: a story step's reward its earner had already, from another
        // story or as a share, is not given again)
        if (TeamOwnStory.Withhold(t, _nothing ? _earner : _receiver)) {
            __result = t;
            return false;
        }

        if (_nothing) {
            __result = t;
            return false;
        }

        if (NetSession.Instance.Connection is ElinNetClient && !ElinDelta.IsApplying) {
            // a client cannot create things: dropped here for show, created by the host
            StoryGifts.Offer(t);
            return true;
        }

        if (_receiver is not { isDead: false, IsAliveInCurrentZone: true } receiver) {
            return true;
        }

        t.things.DestroyAll();
        EClass._zone.AddCard(t, receiver.pos);
        __result = t;
        return false;
    }
}
