using ElinTogether.Helper;
using ElinTogether.Net;
using ElinTogether.Patches;
using MessagePack;

namespace ElinTogether.Models;

[MessagePackObject]
public class QuestStartDelta : ElinDelta
{
    internal override bool InSendersStory => true;

    [Key(0)]
    public required int Uid { get; init; }

    [Key(1)]
    public required RemoteCard? Owner { get; init; }

    [Key(2)]
    public required bool AssignQuest { get; init; }

    [Key(3)]
    public required LZ4Bytes Data { get; init; }

    /// <summary>
    ///     The date on the sender's clock. A player travelling alone has its own, and a deadline is a date:
    ///     the receiver keeps the time that was left, on its own clock
    /// </summary>
    [Key(4)]
    public int Now { get; set; }

    protected override void OnApply(ElinNetBase net)
    {
        if (net is ElinNetHost host) {
            if (game.quests.list.Exists(q => q.uid == Uid)) {
                return;
            }

            if (!host.IsAwayPeer(OriginPeer)) {
                StartForPlayer(host);
                return;
            }

            // accepted by a player travelling alone, in its copy of the world: the quest log is everyone's

            var accepted = Rebase(Data.Decompress<Quest>());
            if (!accepted.IsRandomQuest && game.quests.list.Exists(q => q.id == accepted.id)) {
                // a story quest is started once
                return;
            }

            game.quests.globalList.RemoveAll(q => q.uid == Uid || (!accepted.IsRandomQuest && q.id == accepted.id));
            game.quests.list.Insert(0, accepted);
            SharedQuests.Remember(accepted);

            if (!accepted.IsRandomQuest) {
                // what starting a story quest sets up (the next quests, who joins) is the world's
                try {
                    using (QuestRewardPatch.GiveNothing()) {
                        using (Simulate()) {
                            accepted.Start();
                        }
                    }
                } catch (System.Exception ex) {
                    EmpLog.Warning(ex, "Quest {QuestId} start failed on the host", accepted.id);
                }
            }

            accepted.UpdateJournal();
            if (player.questTracker) {
                WidgetQuestTracker.Show();
            }

            // not back to the player who accepted it: its copy is the live one
            host.SendDeltaToAllExcept(OriginPeer, this);
            return;
        }

        if (PersonalQuests.WasTurnedIn(Uid)) {
            // taken and turned in with the same click (goods already in the bag): nothing to keep
            return;
        }

        var quest = Rebase(Data.Decompress<Quest>());

        game.quests.globalList.RemoveAll(q => q.uid == Uid);

        if (game.quests.list.Find(q => q.uid < 0 && q.id == quest.id) is { } mine) {
            // started here by a dialog, which goes on with this copy: it takes its number, and what starting
            // it changed on the host, but keeps who the dialog said it is for
            var person = mine.person;
            SharedQuests.CopyState(quest, mine);
            if (person?.chara is not null && mine.person?.chara is null) {
                mine.person = person;
            }

            mine.uid = Uid;
            SharedQuests.HostAnswered(mine.id);
            mine.UpdateJournal();
            return;
        }

        var i = game.quests.list.FindIndex(q => q.uid == Uid);
        if (i >= 0) {
            game.quests.list[i] = quest;
        } else {
            game.quests.list.Insert(0, quest);
        }

        if (Owner?.Find() is Chara owner) {
            quest.SetClient(owner, AssignQuest);
        }

        if (!PersonalQuests.IsPersonal(quest)) {
            // a personal one is still to be told to the host of the world, which keeps it for its taker
            SharedQuests.Remember(quest);
        }

        quest.UpdateJournal();
        if (player.questTracker) {
            WidgetQuestTracker.Show();
        }
    }

    /// <summary>
    ///     Started by a player on this map, a dialog mostly: the quest is everyone's, what it gives lands at
    ///     that player's feet
    /// </summary>
    private void StartForPlayer(ElinNetHost host)
    {
        var quest = game.quests.globalList.Find(q => q.uid == Uid);
        if (quest is null && Owner?.Find() is Chara { quest: { } offered } && offered.uid == Uid) {
            quest = offered;
        }

        // a story quest is one of a kind: the offer of this player may carry a number this world gave to another
        // copy of it (an offer made again, a journal that came from an older copy), it is the same quest
        if (quest is null && Uid >= 0 && Data.Decompress<Quest>() is { IsRandomQuest: false } asked) {
            quest = game.quests.globalList.Find(q => q.id == asked.id && !q.IsRandomQuest);
        }

        if (quest is null) {
            if (Uid >= 0) {
                EmpLog.Warning("Rejecting quest start, unresolved quest {QuestUid}", Uid);
                return;
            }

            // created by the dialog on the player's side, it gets its number here
            quest = Rebase(Data.Decompress<Quest>());
            if (game.quests.list.Exists(q => q.id == quest.id)) {
                return;
            }

            quest.uid = game.quests.uid++;
            if (Owner?.Find() is Chara owner) {
                quest.SetClient(owner, AssignQuest);
            }
        }

        if (quest.UseInstanceZone) {
            EmpLog.Warning("Rejecting quest start, instance zone {QuestUid} {QuestId}", Uid, quest.id);
            return;
        }

        game.quests.globalList.Remove(quest);

        using (QuestRewardPatch.GiveTo(host, OriginPeer)) {
            using (Simulate()) {
                game.quests.Start(quest);
            }
        }

        EmpLog.Debug("Started quest {QuestUid} {QuestId} for peer {PeerIndex}", quest.uid, quest.id, OriginPeer);
    }

    private Quest Rebase(Quest quest)
    {
        if (Now > 0 && quest.deadline > 0) {
            quest.deadline += EClass.world.date.GetRaw() - Now;
        }

        return quest;
    }
}
