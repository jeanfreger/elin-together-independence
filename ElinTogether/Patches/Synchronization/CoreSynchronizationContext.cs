using ElinTogether.Helper;
using ElinTogether.Models;
using ElinTogether.Net;
using HarmonyLib;

namespace ElinTogether.Patches;

[HarmonyPatch(typeof(Core), nameof(Core.Update))]
internal class CoreSynchronizationContext : SynchronizationContext
{
    [HarmonyPrefix]
    internal static void OnCoreUpdate()
    {
        // apply remote delta happened in previous updates before this update
        switch (NetSession.Instance.Connection) {
            case ElinNetHost host:
                host.WorldStateDeltaProcess();
                return;
            case ElinNetClient client:
                // GameDelta = 0f; // moved to GameSyncCtx
                client.WorldStateDeltaProcess();
                return;
        }
    }

    [HarmonyPostfix]
    internal static void OnCoreUpdateEnd()
    {
        PlayerTrade.WatchSession();
        SaveDepot.Update();
        EmpServer.Update();
        EmpAutoHost.Update();
        // (alone on a map of its own the host link is still there: the host still hears it)
        PlayerIdle.Update();
        // (alone too: the host's own team's knowledge is put in place after a load, given back to the world when the
        // box is unticked)
        TeamKnowledge.Update();
        PlayerDuel.Always();

        if (NetSession.Instance.Connection is not { } connection || !core.IsGameStarted) {
            return;
        }

        CardAddThingEvent.FlushPendingAbilityFakeCard();
        ThingRequest.InvalidateDangling();
        StoryGifts.Flush();
        ElinNetHost.GiveStoryShares();
        QuestJournalDelta.Tell();
        ClientCellSweep.Update();
        WorldHandover.Watch();
        TileStateDelta.Flush();
        PlayerTrade.Update();
        PlayerDuel.Update();
        AdventurerBout.Update();
        CardCache.Update();
        CharaSynchronizationContext.Update();
        NetProfileSynchronizationContext.Update();
        RemoteTacticsPatch.Update();
        AutoCombatPerPlayerPatch.Update();
        PlayerTitleDelta.Update();
        PersonalQuests.TellHolder();
        NameWatch.Update();
        AreaWatch.Update();
        QuestSynchronizationContext.Update();
        SleepSynchronizationContext.Update();
        connection.Delta.RefreshBuffer();
        switch (NetSession.Instance.Connection) {
            case ElinNetHost host:
                if (!EMono.scene.paused) {
                    host.Delta.AddRemoteImmediate(new GameDelta {
                        Delta = Core.gameDelta,
                        Turbo = GameSynchronizationContext.WorldTurbo,
                    });
                }

                if (CanSendDelta) {
                    CanSendDelta = false;
                    var start = System.Diagnostics.Stopwatch.GetTimestamp();
                    host.WorldStateDeltaUpdate();
                    NetMetrics.DeltaSend.Add(0, start);
                }

                return;
            case ElinNetClient client when CanSendDelta:
                CanSendDelta = false;
                var sending = System.Diagnostics.Stopwatch.GetTimestamp();
                client.WorldStateDeltaUpdate();
                NetMetrics.DeltaSend.Add(0, sending);
                return;
        }
    }
}