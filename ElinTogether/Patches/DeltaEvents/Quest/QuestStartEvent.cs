using ElinTogether.Helper;
using ElinTogether.Models;
using ElinTogether.Net;
using HarmonyLib;

namespace ElinTogether.Patches;

[HarmonyPatch(typeof(QuestManager), nameof(QuestManager.Start), typeof(Quest))]
internal class QuestStartEvent
{
    [HarmonyPrefix]
    internal static bool OnClientStart(Quest q, ref Quest __result)
    {
        if (NetSession.Instance.Connection is not ElinNetClient client) {
            return true;
        }

        __result = q;

        if (ElinDelta.IsApplying) {
            return false;
        }

        if (EClass.game.quests.list.Exists(x => x.uid == q.uid || (q.uid < 0 && x.id == q.id))) {
            return false;
        }

        if (q.UseInstanceZone && !PersonalQuests.InstancesEnabled) {
            EmpPop.Pop("emp_ui_quest_client".lang());
            return false;
        }

        if (q.uid < 0 || !q.IsRandomQuest) {
            // a story quest, started by a dialog: the host starts it for everyone, the dialog goes on with
            // this copy in the meantime
            EClass.game.quests.list.Insert(0, q);
            SharedQuests.AwaitHost(q);
            q.UpdateJournal();

            client.Delta.AddRemote(new QuestStartDelta {
                Uid = q.uid,
                Owner = q.person.chara,
                AssignQuest = q.chara?.quest?.uid == q.uid,
                Data = LZ4Bytes.Create(q),
                Now = EClass.world.date.GetRaw(),
            });
            EmpLog.Debug("Requesting quest start {QuestUid} {QuestId}", q.uid, q.id);
            return false;
        }

        client.Delta.AddRemote(new QuestAcceptDelta {
            Uid = q.uid,
            Client = q.person.chara,
        });
        EmpLog.Debug("Requesting quest accept {QuestUid} {QuestId}", q.uid, q.id);

        return false;
    }

    [HarmonyPostfix]
    internal static void OnStart(Quest q)
    {
        if (ElinDelta.IsApplying) {
            return;
        }

        if (PersonalQuests.IsPersonal(q)) {
            // only its taker holds it: the others just lose the offer, the host is told by the taker
            PersonalQuests.OnStarted(q);
            return;
        }

        // travelling alone: the quest log is the world's, the host tells everyone
        QuestAwaySync.Send(new QuestStartDelta {
            Uid = q.uid,
            Owner = q.person.chara,
            AssignQuest = q.chara?.quest?.uid == q.uid,
            Data = LZ4Bytes.Create(q),
            Now = EClass.world.date.GetRaw(),
        });

        if (NetSession.Instance.Connection is not { } connection) {
            return;
        }

        if (connection.IsClient) {
            return;
        }

        connection.Delta.AddRemote(new QuestStartDelta {
            Uid = q.uid,
            Owner = q.person.chara,
            AssignQuest = q.chara?.quest?.uid == q.uid,
            Data = LZ4Bytes.Create(q),
            Now = EClass.world.date.GetRaw(),
        });
    }
}