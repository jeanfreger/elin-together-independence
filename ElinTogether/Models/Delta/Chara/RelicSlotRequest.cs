using ElinTogether.Net;
using HarmonyLib;
using MessagePack;

namespace ElinTogether.Models;

/// <summary>
///     Sorin's reward (DramaOutcome.reward_sorin) gives the player a relic slot on its body: a guest got it in its own
///     game only, the host's copy of it had none, nor the other guests' (hunt 2 row 28). The host adds it and tells
///     everyone
/// </summary>
[MessagePackObject]
public class RelicSlotRequest : ElinDelta
{
    private const int Relic = 46;

    [Key(0)]
    public required RemoteCard Player { get; init; }

    protected override void OnApply(ElinNetBase net)
    {
        if (Player.Find() is not Chara chara) {
            return;
        }

        if (net is ElinNetHost host) {
            if (!host.ActiveRemoteCharas.TryGetValue(OriginPeer, out var sender) || sender != chara) {
                return;
            }

            host.Delta.AddRemote(this);
        }

        if (chara.body.GetSlot(Relic, false) != null) {
            return;
        }

        chara.body.AddBodyPart(Relic);
        chara.body.RefreshBodyParts();
    }

    [HarmonyPatch(typeof(DramaOutcome), nameof(DramaOutcome.reward_sorin))]
    internal static class Ask
    {
        [HarmonyPrefix]
        internal static void OnReward()
        {
            if (!IsApplying && NetSession.Instance.Connection is ElinNetClient client && EClass.pc.body.GetSlot(Relic, false) == null) {
                client.Delta.AddRemote(new RelicSlotRequest { Player = EClass.pc });
            }
        }
    }
}
