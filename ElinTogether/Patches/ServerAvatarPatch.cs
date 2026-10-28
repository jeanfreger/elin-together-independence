using ElinTogether.Helper;
using HarmonyLib;

namespace ElinTogether.Patches;

/// <summary>
///     The server's stand-in (<see cref="ServerAvatar" />) is the game's local character and does nothing: nobody
///     sees it, nobody fights it, it cannot be hurt, killed, poisoned or made hungry. It is the same on every game
///     (the flag is on its card), the guests' included, so none of them takes it for a player to meet. Each place
///     below is a way the game makes characters notice the local one: who sees whom (the callouts and approaches of
///     the idle townspeople ask CanSee of the local character), who is hostile to whom and who picks whom as its
///     enemy (the monsters of the map), and what can happen to it
/// </summary>
[HarmonyPatch]
internal static class ServerAvatarPatch
{
    /// <summary>
    ///     Nobody sees the stand-in, and it sees nobody (a character always sees itself)
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    [HarmonyPatch(typeof(Chara), nameof(Chara.CanSee))]
    internal static bool OnCanSee(Chara __instance, Card c, ref bool __result)
    {
        if (c == __instance || (!ServerAvatar.Is(c) && !ServerAvatar.Is(__instance))) {
            return true;
        }

        __result = false;
        return false;
    }

    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    [HarmonyPatch(typeof(Chara), nameof(Chara.CanSeeLos), typeof(Card), typeof(int))]
    internal static bool OnCanSeeLos(Chara __instance, Card c, ref bool __result)
    {
        if (c == __instance || (!ServerAvatar.Is(c) && !ServerAvatar.Is(__instance))) {
            return true;
        }

        __result = false;
        return false;
    }

    /// <summary>
    ///     No monster is hostile to it and it is hostile to no one. Last, after the patches that make two players foes
    /// </summary>
    [HarmonyPostfix]
    [HarmonyPriority(Priority.Last)]
    [HarmonyPatch(typeof(Chara), nameof(Chara.IsHostile), typeof(Chara))]
    internal static void OnIsHostile(Chara __instance, Chara c, ref bool __result)
    {
        if (__result && (ServerAvatar.Is(c) || ServerAvatar.Is(__instance))) {
            __result = false;
        }
    }

    /// <summary>
    ///     It looks for no enemy (the sensors of the game ask every character of the map, the local one too)
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    [HarmonyPatch(typeof(Chara), nameof(Chara.FindNewEnemy))]
    internal static bool OnFindNewEnemy(Chara __instance, ref bool __result)
    {
        if (!ServerAvatar.Is(__instance)) {
            return true;
        }

        __result = false;
        return false;
    }

    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    [HarmonyPatch(typeof(Chara), nameof(Chara.FindNearestNewEnemy))]
    internal static bool OnFindNearestNewEnemy(Chara __instance, ref bool __result)
    {
        if (!ServerAvatar.Is(__instance)) {
            return true;
        }

        __result = false;
        return false;
    }

    /// <summary>
    ///     A monster is never given it as its enemy, whatever asked (a call for help, a blow that missed, a spell
    ///     that went astray): the target is cleared instead
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(Chara), nameof(Chara.SetEnemy))]
    internal static void OnSetEnemy(Chara __instance, ref Chara? c)
    {
        if (c is not null && (ServerAvatar.Is(c) || ServerAvatar.Is(__instance))) {
            c = null;
        }
    }

    /// <summary>
    ///     An act of war against it, or by it, is not one (GoHostile sets the enemy and turns neutrals hostile)
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    [HarmonyPatch(typeof(Chara), nameof(Chara.DoHostileAction))]
    internal static bool OnDoHostileAction(Chara __instance, Card _tg)
    {
        return !ServerAvatar.Is(_tg) && !ServerAvatar.Is(__instance);
    }

    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    [HarmonyPatch(typeof(Chara), nameof(Chara.GoHostile))]
    internal static bool OnGoHostile(Chara __instance, Card _tg)
    {
        return !ServerAvatar.Is(_tg) && !ServerAvatar.Is(__instance);
    }

    /// <summary>
    ///     It cannot be hurt
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    [HarmonyPatch(typeof(Card), nameof(Card.DamageHP), typeof(long), typeof(int), typeof(int), typeof(AttackSource), typeof(Card),
        typeof(bool), typeof(Thing), typeof(Chara), typeof(int))]
    internal static bool OnDamageHP(Card __instance)
    {
        return !ServerAvatar.Is(__instance);
    }

    /// <summary>
    ///     It cannot die. First: the prefix that tells the other games a character died checks that the original
    ///     still runs (CharaDieEvent)
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    [HarmonyPatch(typeof(Chara), nameof(Chara.Die))]
    internal static bool OnDie(Chara __instance)
    {
        return !ServerAvatar.Is(__instance);
    }

    /// <summary>
    ///     No condition sticks to it (poison, a curse, a sleep that is not wanted...). Nothing is told to the others
    ///     when the answer is none (CharaAddConditionEvent)
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    [HarmonyPatch(typeof(Chara), nameof(Chara.AddCondition), typeof(Condition), typeof(bool))]
    internal static bool OnAddCondition(Chara __instance, ref Condition? __result)
    {
        if (!ServerAvatar.Is(__instance)) {
            return true;
        }

        __result = null;
        return false;
    }

    /// <summary>
    ///     Its turns tick nothing: no hunger, no tiredness, no condition to wear off or to come
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    [HarmonyPatch(typeof(Chara), nameof(Chara.TickConditions))]
    internal static bool OnTickConditions(Chara __instance)
    {
        return !ServerAvatar.Is(__instance);
    }
}
