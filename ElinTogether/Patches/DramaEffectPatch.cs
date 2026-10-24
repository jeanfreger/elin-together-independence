using ElinTogether.Helper;
using ElinTogether.Models;
using ElinTogether.Net;
using HarmonyLib;

namespace ElinTogether.Patches;

/// <summary>
///     What a dialog does to the player and its companions (the healer's care) is asked of the host, see
///     <see cref="CharaEffectRequestDelta" />. This game still plays it for the words and the sound
/// </summary>
[HarmonyPatch(typeof(ActEffect), nameof(ActEffect.Proc), typeof(EffectId), typeof(int), typeof(BlessedState), typeof(Card),
    typeof(Card), typeof(ActRef))]
internal static class DramaEffectPatch
{
    [HarmonyPrefix]
    internal static void OnProc(EffectId id, Card cc, Card tc)
    {
        if (LayerDrama.Instance == null || ElinDelta.IsApplying || !CharaEffectRequestDelta.IsAllowed(id) ||
            NetSession.Instance.Connection is not ElinNetClient client ||
            (tc ?? cc) is not Chara target || !(target.IsPC || target.IsCompanionOf(EClass.pc))) {
            return;
        }

        client.Delta.AddRemote(new CharaEffectRequestDelta {
            Target = target,
            Id = id,
        });
    }
}

/// <summary>
///     The inn meal feeds the companions of the one who pays (DramaCustomSequence "_food"): in a client's game that was
///     its copies only, the host's companions stayed hungry (hunt 2 row 22). Asked of the host
/// </summary>
[HarmonyPatch(typeof(FoodEffect), nameof(FoodEffect.Proc))]
internal static class DramaMealPatch
{
    [HarmonyPrefix]
    internal static void OnProc(Chara c, Thing food, bool consume)
    {
        if (consume || food.id is not ("dish_lunch" or "_dish_lunch") || ElinDelta.IsApplying || c.IsPC || !c.IsCompanionOf(EClass.pc) ||
            NetSession.Instance.Connection is not ElinNetClient client) {
            return;
        }

        client.Delta.AddRemote(new CharaEffectRequestDelta {
            Target = c,
            Id = EffectId.HealComplete,
            Meal = true,
        });
    }
}

/// <summary>
///     A new alias and the return to the Void ask "the player" in a window: the scroll of another player read
///     here asked this game's player. Its own game asks it
/// </summary>
[HarmonyPatch(typeof(ActEffect), nameof(ActEffect.Proc), typeof(EffectId), typeof(int), typeof(BlessedState), typeof(Card),
    typeof(Card), typeof(ActRef))]
internal static class RemoteWindowEffectPatch
{
    [HarmonyPrefix]
    internal static bool OnProc(EffectId id, Card cc)
    {
        return id is not (EffectId.ChangeAlias or EffectId.ReturnVoid) || !NetSession.Instance.HasActiveConnection ||
               cc is not Chara { IsPC: false, IsRemotePlayer: true };
    }
}
