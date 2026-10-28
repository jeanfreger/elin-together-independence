using ElinTogether.Net;
using HarmonyLib;
using UnityEngine;

namespace ElinTogether.Patches;

/// <summary>
///     Council 2026-10-26 (MMO world time, stage 6), "Living world" (NetSessionRules.LivingWorldMinutes): the clock
///     of the world follows real time, a game day per that many real minutes, whether the players act or stand
///     still. The players' turns still drive their own acts, they no longer move the date: the minute the game
///     gives every few turns (GameUpdater) is not counted. What a player does on purpose still does (a night, a
///     trip on the world map, waiting). <br />
///     One clock, the host's: its minutes go to everyone as they did (WorldDateAdvanceDelta, one minute each, so
///     every player lives them). A player holding a map of its own away from the host lives them there too, minute by
///     minute, instead of catching up (see <see cref="Follow" />). The clock stands still while nobody plays
///     (EmptyWorld, stage 3) and while the night screen of the host's game is open (the night moves the date itself)
/// </summary>
[HarmonyPatch]
internal static class LivingWorld
{
    // a long frame (a hitch) gives a few minutes at most, not an hour at once
    private const int MaxMinutesPerFrame = 5;

    // a map of its own lives the world's clock while that close behind it; further (it was loading, asleep, away
    // from the keyboard) it catches up as before
    private const int FollowMinutes = 60;

    // minutes of real time not yet given to the date
    private static float _minutes;

    // a map of its own: the date of the world (minutes) the host told it last
    private static int _follow;

    // inside GameUpdater.Update, whose only date change is the minute of the turns
    private static bool _turnClock;

    private static int MinutesPerDay => NetSession.Instance.Rules.LivingWorldMinutes;

    /// <summary>
    ///     This game keeps the world's clock: the host of the world
    /// </summary>
    internal static bool Keeps => MinutesPerDay > 0 && NetSession.Instance is { Transport: ElinNetHost { IsZoneSession: false }, IsAway: false };

    /// <summary>
    ///     This game holds a map of its own away from the host and follows the world's clock (one date for the world)
    /// </summary>
    private static bool Follows => MinutesPerDay > 0 &&
                                   NetSession.Instance is { IsZoneAuthority: true, Transport: ElinNetClient, Rules.UseSharedWorldTime: true };

    /// <summary>
    ///     The minute GameDate.AdvanceMin is about to add is the turns' one, which the real-time clock replaces here
    /// </summary>
    internal static bool RefusesTurnMinute(int minutes)
    {
        return minutes == 1 && _turnClock && (Keeps || Follows);
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(GameUpdater), nameof(GameUpdater.Update))]
    internal static void OnUpdaterStart()
    {
        _turnClock = true;
    }

    [HarmonyFinalizer]
    [HarmonyPatch(typeof(GameUpdater), nameof(GameUpdater.Update))]
    internal static void OnUpdaterEnd()
    {
        _turnClock = false;
    }

    /// <summary>
    ///     A map of its own heard a minute of the world's clock: true when it lives it here (Update), false when it
    ///     catches up as before (not living world, too far behind, not that minute)
    /// </summary>
    internal static bool Follow(int minutes, int target)
    {
        if (minutes != 1 || !Follows) {
            return false;
        }

        var behind = target - EClass.world.date.GetRaw();
        if (behind is <= 0 or > FollowMinutes) {
            return behind <= 0;
        }

        _follow = System.Math.Max(_follow, target);
        return true;
    }

    internal static void Update()
    {
        var (keeps, follows) = (Keeps, Follows);
        if (!keeps) {
            _minutes = 0f;
        }

        if (!follows) {
            _follow = 0;
        }

        if (!keeps && !follows || !EClass.core.IsGameStarted || EClass.game is not { isLoading: false, activeZone: not null } ||
            EClass.scene.mode != Scene.Mode.Zone || EClass.ui.GetLayer<LayerSleep>() is not null) {
            return;
        }

        if (follows) {
            FollowOn();
            return;
        }

        // nobody plays: the world waits for someone (council 2026-10-26, stage 3)
        if (EmptyWorld.Frozen) {
            _minutes = 0f;
            return;
        }

        _minutes += Mathf.Min(Time.unscaledDeltaTime, 1f) * Date.DayToken / (MinutesPerDay * 60f);
        var now = System.Math.Min((int)_minutes, MaxMinutesPerFrame);
        _minutes = Mathf.Min(_minutes - now, MaxMinutesPerFrame);
        for (var i = 0; i < now; i++) {
            EClass.world.date.AdvanceMin(1);
        }
    }

    private static void FollowOn()
    {
        var behind = _follow - EClass.world.date.GetRaw();
        if (behind <= 0) {
            _follow = 0;
            return;
        }

        // away from the keyboard its map stands still (EmptyWorld) and catches up, as a map nobody lives; so does
        // one that fell far behind
        if (behind > FollowMinutes || EmptyWorld.Frozen) {
            WorldDateAdvanceEvent.CatchUpNextFrame(_follow);
            _follow = 0;
            return;
        }

        for (var i = System.Math.Min(behind, MaxMinutesPerFrame); i > 0; i--) {
            EClass.world.date.AdvanceMin(1);
        }
    }
}
