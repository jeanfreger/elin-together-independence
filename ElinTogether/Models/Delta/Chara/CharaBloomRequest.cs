using ElinTogether.Helper;
using ElinTogether.Net;
using HarmonyLib;
using MessagePack;

namespace ElinTogether.Models;

/// <summary>
///     "Bloom" a companion at a priest (DramaCustomSequence "_bloom"): a guest paid the zone's 10 influence and its
///     companion got the feat on its own copies only (hunt 2 row 23). The host does it, for the guest's companion
/// </summary>
[MessagePackObject]
public class CharaBloomRequest : ElinDelta
{
    private const int FeatBloom = 1273;

    [Key(0)]
    public required RemoteCard Target { get; init; }

    protected override void OnApply(ElinNetBase net)
    {
        if (net is not ElinNetHost host || !host.ActiveRemoteCharas.TryGetValue(OriginPeer, out var sender) ||
            Target.Find() is not Chara { isDead: false } target || !target.IsCompanionOf(sender) || !target.CanBloom() ||
            EClass._zone.influence < 10) {
            return;
        }

        using var simulate = Simulate();
        EClass._zone.ModInfluence(-10);
        target.SetFeat(FeatBloom, 1, true);
        target.feat += 10;
    }

    [HarmonyPatch(typeof(Chara), nameof(Chara.SetFeat))]
    internal static class Ask
    {
        [HarmonyPrefix]
        internal static void OnSetFeat(Chara __instance, int id)
        {
            if (id == FeatBloom && !IsApplying && !__instance.IsPC && __instance.IsCompanionOf(EClass.pc) &&
                __instance.CanBloom() && NetSession.Instance.Connection is ElinNetClient client) {
                client.Delta.AddRemote(new CharaBloomRequest { Target = __instance });
            }
        }
    }
}
