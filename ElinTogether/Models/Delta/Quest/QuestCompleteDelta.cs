using ElinTogether.Helper;
using ElinTogether.Net;
using ElinTogether.Patches;
using MessagePack;

namespace ElinTogether.Models;

[MessagePackObject]
public class QuestCompleteDelta : ElinDelta
{
    internal override bool InSendersStory => true;

    [Key(1)]
    public required int Uid { get; init; }

    /// <summary>
    ///     Tells which quest it is while a quest started by a dialog on a client has no number from the host yet
    /// </summary>
    [Key(2)]
    public string? Id { get; init; }

    /// <summary>
    ///     A quest only its taker holds (see PersonalQuests): the host needs it to give the rewards
    /// </summary>
    [Key(3)]
    public LZ4Bytes? Data { get; init; }

    /// <summary>
    ///     A defense quest is paid by the wave reached, which the game keeps outside of the quest, in the game of
    ///     the player who fought
    /// </summary>
    [Key(4)]
    public int LastWave { get; set; }

    [Key(5)]
    public int Bonus { get; set; }

    protected override void OnApply(ElinNetBase net)
    {
        if (Data is not null) {
            if (net is ElinNetHost map && !map.IsAwayPeer(OriginPeer)) {
                map.CompletePersonal(OriginPeer, Data.Decompress<Quest>(), LastWave, Bonus);
            }

            return;
        }

        var quest = SharedQuests.Find(Uid, Id);
        if (quest is null || quest.isComplete) {
            return;
        }

        if (net is ElinNetHost host) {
            if (host.IsAwayPeer(OriginPeer)) {
                // completed by a player travelling alone, in its copy of the world: it got the rewards there,
                // the quest log, fame and karma are the world's
                if (!quest.IsRandomQuest && TryCompleteForWorld(host, quest, host.PlayerChara(OriginPeer))) {
                    return;
                }

                QuestCompleteEvent.CompleteQuietly(quest, true);
                host.SendDeltaToAllExcept(OriginPeer, new QuestCompleteDelta {
                    Uid = quest.uid,
                });
                return;
            }

            // the rewards drop at the feet of the player who completed it, not the host's
            try {
                using (QuestRewardPatch.GiveTo(host, OriginPeer)) {
                    using (Simulate()) {
                        // the affinity and the fame of the quest go to the player, not the host
                        using var __ = host.ActiveRemoteCharas.TryGetValue(OriginPeer, out var taker)
                            ? (System.IDisposable?)PlayerStandIn.For(host, OriginPeer, taker)
                            : null;
                        quest.Complete();
                    }
                }
            } catch (System.Exception ex) {
                // the end of a quest written for "the player" threw in this game: the game had already closed
                // the quest, but nobody was told (the message goes when Quest.Complete returns)
                EmpLog.Warning(ex, "Quest {QuestId} completion threw on the host, told as completed", quest.id);
                quest.isComplete = true;
                if (game.quests.list.Contains(quest)) {
                    QuestCompleteEvent.CompleteQuietly(quest, true);
                }

                host.Delta.AddRemote(new QuestCompleteDelta {
                    Uid = quest.uid,
                    Id = quest.id,
                });
            }

            return;
        }

        // not Quest.Complete: an away client reads as host there and would drop the rewards again
        QuestCompleteEvent.CompleteQuietly(quest, false);
    }

    /// <summary>
    ///     A story quest completed by a player travelling alone: what completing it opens up (the next quests,
    ///     who moves where) is the world's, so the host completes it too, without the rewards
    /// </summary>
    private static bool TryCompleteForWorld(ElinNetHost host, Quest quest, Chara? earner)
    {
        try {
            using (QuestRewardPatch.GiveNothing(earner)) {
                using (Simulate()) {
                    quest.Complete();
                }
            }

            return true;
        } catch (System.Exception ex) {
            EmpLog.Warning(ex, "Quest {QuestId} completion failed on the host", quest.id);
            return quest.isComplete || !game.quests.list.Contains(quest);
        }
    }
}
