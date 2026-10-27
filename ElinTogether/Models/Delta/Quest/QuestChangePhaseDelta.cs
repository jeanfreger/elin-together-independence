using System;
using ElinTogether.Helper;
using ElinTogether.Net;
using ElinTogether.Patches;
using MessagePack;

namespace ElinTogether.Models;

[MessagePackObject]
public class QuestChangePhaseDelta : ElinDelta
{
    [Key(0)]
    public required int Uid { get; init; }

    [Key(1)]
    public required int Modifier { get; init; }

    /// <summary>
    ///     Tells which quest it is while a quest started by a dialog on a client has no number from the host yet
    /// </summary>
    [Key(2)]
    public string? Id { get; init; }

    /// <summary>
    ///     The phase the sender was moving on from, -1 when it does not matter
    /// </summary>
    [Key(3)]
    public int From { get; set; } = -1;

    protected override void OnApply(ElinNetBase net)
    {
        var quest = SharedQuests.Find(Uid, Id);

        // already there: what the phase triggers is not to happen twice
        if (quest is null || quest.phase == Modifier) {
            return;
        }

        if (net is not ElinNetHost host) {
            // what the phase triggers happened where the step was made, and on the host: a client, even one
            // travelling alone in its own copy of the world, only takes note
            quest.phase = Modifier;

            // the task of the phase left behind is over, as the game drops it after a step (Quest.CompleteTask):
            // kept, the kills replayed here completed it again and pushed the quest to phases it does not have
            if (quest is QuestSequence && quest.task is { } task && (quest is QuestGuild || task.IsComplete())) {
                quest.task = null;
            }

            quest.UpdateJournal();
            return;
        }

        var away = host.IsAwayPeer(OriginPeer);
        // a player alone on its map moves on with a copy of the journal that can be late: its steps forward are
        // the world's, but a step to a phase the world already passed took everyone back (the Vernis quest back to
        // phase 3 after the boss, 2026-10-12: the mine boss does not come back, the quest could not be finished)
        if (From >= 0 && quest.phase != From && (!away || Modifier <= quest.phase)) {
            // someone else moved the quest on in the meantime
            EmpLog.Debug("Ignoring stale quest phase {QuestId} {From}->{Phase}, at {Current}", quest.id, From, Modifier, quest.phase);
            host.SendDeltaTo(OriginPeer, new QuestChangePhaseDelta {
                Uid = quest.uid,
                Modifier = quest.phase,
            });
            return;
        }

        try {
            // what the phase triggers in the world happens here, for everyone; what it gives goes to the player
            // who made the step, who already has it when travelling alone
            using (away ? QuestRewardPatch.GiveNothing(host.PlayerChara(OriginPeer)) : QuestRewardPatch.GiveTo(host, OriginPeer)) {
                using (Simulate()) {
                    quest.ChangePhase(Modifier);
                }
            }
        } catch (Exception ex) {
            // the trigger was written for the map of the player who made the step: the phase still is everyone's
            EmpLog.Warning(ex, "Quest {QuestId} phase {Phase} trigger failed on the host", quest.id, Modifier);
            quest.phase = Modifier;
            host.Delta.AddRemote(new QuestChangePhaseDelta {
                Uid = quest.uid,
                Modifier = Modifier,
            });
            // (the teams' story journal: the game's step threw before TeamStory could see it)
            if (quest is QuestMain && host.PlayerChara(OriginPeer) is { } stepper) {
                TeamStory.Record(stepper, Modifier, away);
            }
        }
    }
}
