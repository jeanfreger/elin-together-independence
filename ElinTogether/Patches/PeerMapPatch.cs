using ElinTogether.Models;
using ElinTogether.Net;
using HarmonyLib;
using Newtonsoft.Json.Serialization;

namespace ElinTogether.Patches;

/// <summary>
///     In a session a map may have come from another game (a guest hands back the map it held, the host sends its own):
///     the game reads its "map" file with any type the text names (GameIO.jsReadGame), so a crafted one ran code in
///     the game that loaded it (security audit 2026-10-27). While a session runs, maps are read with the types of the
///     game and its mods only, as everything else a peer sends (NetTypeBinder), and a map is never bigger than the game
///     makes them
/// </summary>
[HarmonyPatch]
internal static class PeerMapPatch
{
    // the game's biggest maps are 200-odd cells a side
    private const int MaxSize = 1024;

    [HarmonyPrefix]
    [HarmonyPatch(typeof(Zone), nameof(Zone.Activate))]
    internal static void OnActivate(out ISerializationBinder? __state)
    {
        __state = null;
        if (NetSession.Instance.Connection is null) {
            return;
        }

        __state = GameIO.jsReadGame.SerializationBinder;
        GameIO.jsReadGame.SerializationBinder = NetTypeBinder.Instance;
    }

    [HarmonyFinalizer]
    [HarmonyPatch(typeof(Zone), nameof(Zone.Activate))]
    internal static void OnActivateEnd(ISerializationBinder? __state)
    {
        if (__state is not null) {
            GameIO.jsReadGame.SerializationBinder = __state;
        }
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(Map), nameof(Map.Load))]
    internal static void OnMapLoad(Map __instance)
    {
        if (NetSession.Instance.Connection is not null && __instance.Size is < 0 or > MaxSize) {
            throw new System.IO.InvalidDataException($"Map of size {__instance.Size} refused (at most {MaxSize})");
        }
    }
}
