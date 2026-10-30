using System;
using ElinTogether.Helper;
using HarmonyLib;

namespace ElinTogether.Patches;

/// <summary>
///     Notes the player's own actions for the bug report (ReportContext): a few postfixes that only add a short
///     string to a list of 20 in memory, nothing sent, nothing written. Every one swallows its own failure
/// </summary>
[HarmonyPatch]
internal static class ReportContextPatch
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(Chara), nameof(Chara.SetAI))]
    internal static void OnSetAi(Chara __instance, AIAct g)
    {
        try {
            if (__instance.IsPC && g is not null) {
                ReportContext.NoteAi(g);
            }
        } catch (Exception) {
            // noexcept
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(FoodEffect), nameof(FoodEffect.Proc))]
    internal static void OnEat(Chara c, Thing food)
    {
        try {
            if (c?.IsPC == true && food is not null) {
                ReportContext.Note($"ate {ReportContext.Describe(food)}");
            }
        } catch (Exception) {
            // noexcept
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Chara), nameof(Chara.Drink))]
    internal static void OnDrink(Chara __instance, Card t)
    {
        try {
            if (__instance.IsPC && t is not null) {
                ReportContext.Note($"drank {ReportContext.Describe(t)}");
            }
        } catch (Exception) {
            // noexcept
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Chara), nameof(Chara.ShowDialog), new Type[0])]
    internal static void OnTalk(Chara __instance)
    {
        try {
            ReportContext.Note($"talked to {ReportContext.Describe(__instance)}");
        } catch (Exception) {
            // noexcept
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Chara), nameof(Chara.MoveZone), typeof(Zone), typeof(ZoneTransition))]
    internal static void OnMoveZone(Chara __instance, Zone z)
    {
        try {
            if (__instance.IsPC && z is not null) {
                ReportContext.Note($"went to {z.Name} ({z.id} #{z.uid})");
            }
        } catch (Exception) {
            // noexcept
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Chara), nameof(Chara.Die))]
    internal static void OnDie(Chara __instance, Card? origin)
    {
        try {
            if (__instance.IsPC) {
                ReportContext.Note(origin is null ? "died" : $"died, killed by {ReportContext.Describe(origin)}");
            }
        } catch (Exception) {
            // noexcept
        }
    }

    // windows opened: shops, crafting, inventories, dialogs (the layer's type is enough to tell which)
    [HarmonyPostfix]
    [HarmonyPatch(typeof(Layer), nameof(Layer.AddLayer), typeof(Layer))]
    internal static void OnAddLayer(Layer l)
    {
        try {
            if (l is not null && l.GetType().Name is not ("LayerElinTogether" or "LayerCover")) {
                ReportContext.Note($"opened {l.GetType().Name}");
            }
        } catch (Exception) {
            // noexcept
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Scene), nameof(Scene.OnUpdate))]
    internal static void OnSceneUpdate()
    {
        ReportContext.Sample();
    }
}
