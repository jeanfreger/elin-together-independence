using System.Collections.Generic;
using ElinTogether.Models;
using ElinTogether.Net;
using HarmonyLib;
using UnityEngine;

namespace ElinTogether.Patches;

/// <summary>
///     Council 2026-10-26 (MMO world time, stage 1): time never waits for a human who is not there. A player
///     without input of its own (keyboard, mouse, an act of its character) for NetSessionRules.IdleMinutes is away
///     from the keyboard: it no longer holds the night, the world map or the shared date. Each game watches its
///     own player; a guest tells the host when it turns idle or active again (PlayerIdleDelta), never more often
/// </summary>
[HarmonyPatch]
internal static class PlayerIdle
{
    // real time of this player's last input or act; what the network brings (deltas) is not its own
    internal static float LastInput { get; set; } = Time.realtimeSinceStartup;

    private static Vector3 _mouse;

    // a guest: what the host was last told, and over which link (a new link is told again)
    private static bool _told;
    private static ElinNetBase? _toldTo;

    // the host: the guests (peer ids of its main link) that said they are idle
    private static readonly HashSet<int> _idlePeers = [];

    private static int Minutes => NetSession.Instance.Rules.IdleMinutes;

    /// <summary>
    ///     This game's own player is away from the keyboard
    /// </summary>
    internal static bool SelfIdle => Minutes > 0 && Time.realtimeSinceStartup - LastInput > Minutes * 60f;

    /// <summary>
    ///     That player of the host's main link (0: the host itself) is away from the keyboard. Only the host of the
    ///     world knows the others; a zone session's peer ids are not those, nobody there counts as idle
    /// </summary>
    internal static bool IsIdle(int peer)
    {
        if (NetSession.Instance.Transport is not ElinNetHost) {
            return false;
        }

        return peer == 0 ? SelfIdle : Minutes > 0 && _idlePeers.Contains(peer);
    }

    internal static void Touch()
    {
        LastInput = Time.realtimeSinceStartup;
    }

    internal static void Update()
    {
        // (an unfocused window still reads the cursor passing over it: not an input of this player)
        if (Input.anyKey || (Application.isFocused && (Input.mousePosition != _mouse || Input.mouseScrollDelta != Vector2.zero))) {
            _mouse = Input.mousePosition;
            Touch();
        }

        var transport = NetSession.Instance.Transport;
        if (transport is not ElinNetHost) {
            _idlePeers.Clear();
        }

        if (transport is not ElinNetClient client || !client.IsConnected) {
            (_told, _toldTo) = (false, null);
            return;
        }

        // an older host (no rule sent) is never told: it would not know the delta
        var idle = SelfIdle;
        if (Minutes <= 0 || (idle == _told && client == _toldTo)) {
            return;
        }

        (_told, _toldTo) = (idle, client);
        EmpLog.Debug("Telling the host this player is {IdleState}", idle ? "idle" : "active");
        // (straight to the host, on its map or away alike)
        client.SendWhileAway(new PlayerIdleDelta {
            Idle = idle,
        });
    }

    internal static void OnReport(int peer, bool idle)
    {
        EmpLog.Debug("Player {PeerIndex} is {IdleState}", peer, idle ? "idle" : "active");
        if (idle) {
            _idlePeers.Add(peer);
        } else {
            _idlePeers.Remove(peer);
        }
    }

    internal static void Forget(int peer)
    {
        _idlePeers.Remove(peer);
    }

    /// <summary>
    ///     An act of this player's own character (a step, an attack, a use; the bench's acts too). Not one a delta
    ///     lands, nor its going back to no goal
    /// </summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(Chara), nameof(Chara.SetAI))]
    internal static void OnOwnAct(Chara __instance, AIAct g)
    {
        if (__instance.IsPC && g is { IsNoGoal: false } && !ElinDelta.IsApplying) {
            Touch();
        }
    }
}
