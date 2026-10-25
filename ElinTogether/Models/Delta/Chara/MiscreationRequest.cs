using ElinTogether.Helper;
using ElinTogether.Net;
using HarmonyLib;
using MessagePack;

namespace ElinTogether.Models;

/// <summary>
///     "Upgrade" a miscreation companion (DramaOutcome.upgrade_miscreation): a guest's companion lost the feat and got
///     its genes on the guest's copy only (hunt 2 row 29). The host does the same for it: the genes are drawn from the
///     companion's number, so both games get the same ones
/// </summary>
[MessagePackObject]
public class MiscreationRequest : ElinDelta
{
    private const int FeatMiscreation = 1248;

    [Key(0)]
    public required RemoteCard Target { get; init; }

    protected override void OnApply(ElinNetBase net)
    {
        if (net is not ElinNetHost host || !host.ActiveRemoteCharas.TryGetValue(OriginPeer, out var sender) ||
            Target.Find() is not Chara { isDead: false } chara || !chara.IsCompanionOf(sender) ||
            chara.Evalue(FeatMiscreation) <= 0) {
            return;
        }

        using var simulate = Simulate();
        // (DramaOutcome.upgrade_miscreation, for this companion)
        var num = chara.Evalue(FeatMiscreation);
        chara.SetFeat(FeatMiscreation, 0);
        for (var i = 0; i < num; i++) {
            Rand.SetSeed(chara.uid + i);
            var gene = DNA.GenerateRandomGene(chara.LV + 30, chara.uid + i);
            if (gene.c_DNA.GetInvalidFeat(chara) == null && gene.c_DNA.GetInvalidAction(chara) == null) {
                gene.c_DNA.Apply(chara, false);
            }
        }

        Rand.SetSeed();
    }

    [HarmonyPatch(typeof(DramaOutcome), nameof(DramaOutcome.upgrade_miscreation))]
    internal static class Ask
    {
        [HarmonyPrefix]
        internal static void OnUpgrade()
        {
            if (!IsApplying && NetSession.Instance.Connection is ElinNetClient client &&
                EClass.pc.party.members.Find(c => !c.IsPC && c.HasElement(FeatMiscreation)) is { } chara) {
                client.Delta.AddRemote(new MiscreationRequest { Target = chara });
            }
        }
    }
}
