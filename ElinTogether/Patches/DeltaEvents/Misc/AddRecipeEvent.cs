using ElinTogether.Helper;
using ElinTogether.Models;
using ElinTogether.Net;
using HarmonyLib;

namespace ElinTogether.Patches;

[HarmonyPatch]
internal class AddRecipeEvent
{
    // how deep in RecipeManager.Add: the game learns the variants of a block (pillar, bridge) by calling Add again
    private static int _depth;

    [HarmonyPrefix]
    [HarmonyPatch(typeof(RecipeManager), nameof(RecipeManager.Add))]
    internal static void OnAddRecipeBegin()
    {
        _depth++;
    }

    // a finalizer: a throw does not leave the count up
    [HarmonyFinalizer]
    [HarmonyPatch(typeof(RecipeManager), nameof(RecipeManager.Add))]
    internal static void OnAddRecipeEnd()
    {
        _depth--;
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(RecipeManager), nameof(RecipeManager.Add))]
    internal static void OnAddRecipe(string id)
    {
        // a variant learnt inside another Add: the other games learn it the same way from the outer one, and
        // told apart they counted the block twice
        if (_depth > 1 || AddRecipeDelta.IsLanding) {
            return;
        }

        // the recipe a player comes up with while harvesting, digging or mining is found when the end of its
        // own task is replayed here: the host, for whom that character is not "the player", never rolls for it
        var ownTask = CharaProgressCompleteDelta.Current?.Owner.Find() is Chara { IsPC: true };
        var learner = EClass.pc.uid;
        if (ElinDelta.IsApplying && !ownTask) {
            return;
        }

        // travelling alone, or keeping a map for visitors: the recipes are the world's, the host tells the others
        // (not our visitors again, they hear it from us below)
        EmpLog.Debug("Recipe {RecipeId} learnt here, told to the others", id);
        QuestAwaySync.Send(new AddRecipeDelta {
            RecipeId = id,
            Relayed = NetSession.Instance.Connection is ElinNetHost { IsZoneSession: true },
            Learner = learner,
        });

        var told = new AddRecipeDelta { RecipeId = id, Learner = learner };
        // teams learning on their own: a host tells only the learner's team
        if (TeamKnowledge.On && NetSession.Instance.Connection is ElinNetHost host) {
            TeamKnowledge.Tell(host, told, learner, int.MinValue);
            return;
        }

        NetSession.Instance.Connection?.Delta.AddRemote(told);
    }
}
