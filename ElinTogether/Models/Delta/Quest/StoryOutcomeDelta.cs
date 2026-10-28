using System;
using ElinTogether.Net;
using ElinTogether.Patches;
using HarmonyLib;
using MessagePack;
using UnityEngine;

namespace ElinTogether.Models;

/// <summary>
///     To the host: a dialog triggered this in the world (a fight set up, someone moving in, land claimed)
/// </summary>
[MessagePackObject]
public class StoryOutcomeDelta : ElinDelta
{
    internal override bool InSendersStory => true;

    [Key(0)]
    public required string Method { get; init; }

    /// <summary>
    ///     Who the player was talking to
    /// </summary>
    [Key(1)]
    public required RemoteCard? Target { get; init; }

    // the same outcome of the same dialog, from the same player: once in this time (a real dialog is not replayed
    // faster; a modified game repeated a reward as often as it liked)
    private const float Cooldown = 600f;
    private const int ReachTiles = 6;
    private static readonly System.Collections.Generic.Dictionary<(int Peer, string Method, int Target), float> LastRun = [];

    /// <summary>
    ///     Security audit 2026-10-27 (item 5): a modified game could run any dialog outcome on any character from
    ///     anywhere (recruit a unique into its base, repeat a reward). The sender must stand by who it talks to (unless
    ///     it plays away, in its own zones), a recruit must be one the base offers or a common resident, and the same
    ///     outcome of the same dialog runs once in ten minutes
    /// </summary>
    internal override string? OnHostPermit(ElinNetHost host, Chara? sender)
    {
        if (NoCharacter(sender) is { } none) {
            return none;
        }

        var target = FindTarget();
        var hire = target is not null && EClass.Branch?.listRecruit.Exists(h => h.chara == target) == true;
        if (!host.IsAwayPeer(OriginPeer) && target is not null && !hire &&
            (target.currentZone != sender!.currentZone || target.pos.Distance(sender.pos) > ReachTiles)) {
            return $"outcome {Method} on {target.uid}, out of the sender's reach";
        }

        if (Method == StoryOutcomePatch.Recruit && target is not null && !hire &&
            (target.IsUnique || target.IsPCFaction || target.IsPC || target.GetBool("remote_chara"))) {
            return $"recruit of {target.uid} ({target.id}), not one the base offers";
        }

        var now = UnityEngine.Time.realtimeSinceStartup;
        var key = (OriginPeer, Method, target?.uid ?? 0);
        if (LastRun.TryGetValue(key, out var last) && now - last < Cooldown) {
            return $"outcome {Method} on {target?.uid ?? 0} again after {(int)(now - last)} s";
        }

        LastRun[key] = now;
        return null;
    }

    protected override void OnApply(ElinNetBase net)
    {
        if (net is not ElinNetHost host) {
            return;
        }

        var away = host.IsAwayPeer(OriginPeer);
        if (!StoryOutcomePatch.RunsOnHost(Method, away)) {
            EmpLog.Warning("Refusing story outcome {Method} from peer {PeerIndex}", Method, OriginPeer);
            return;
        }

        var target = FindTarget();

        // travelling alone, the player ran it in its copy of the world and got what it gives there
        using (away ? QuestRewardPatch.GiveNothing(host.PlayerChara(OriginPeer)) : QuestRewardPatch.GiveTo(host, OriginPeer)) {
            using (Simulate()) {
                try {
                    Run(target, away);
                } catch (Exception ex) {
                    EmpLog.Warning(ex, "Story outcome {Method} failed", Method);
                }
            }
        }

        EmpLog.Debug("Ran story outcome {Method} for peer {PeerIndex}", Method, OriginPeer);
    }

    private void Run(Chara? target, bool away)
    {
        switch (Method) {
            case StoryOutcomePatch.Recruit:
                if (target is not null && EClass.Branch is { } branch) {
                    target.SetBool(18, true);
                    branch.Recruit(target);
                }

                return;
            case nameof(DramaOutcome.QuestExploration_AfterCrystal) when away:
                // the crystal room is on the player's map, only who goes home is about the world
                if (game.cards.globalCharas.Find("fiama") is { } fiama) {
                    fiama.MoveHome(game.StartZone);
                    fiama.RemoveEditorTag(EditorTag.AINoMove);
                }

                return;
        }

        RunGameOutcome(Method, target);
    }

    /// <summary>
    ///     The game's own code, with the dialog it expects around it
    /// </summary>
    internal static void RunGameOutcome(string method, Chara? target)
    {
        var stage = new GameObject("emp_story_outcome");
        stage.SetActive(false);
        try {
            var manager = stage.AddComponent<DramaManager>();
            manager.tg = target is null ? new Person() : new Person(target);

            var outcome = stage.AddComponent<DramaOutcome>();
            outcome.manager = manager;

            AccessTools.Method(typeof(DramaOutcome), method).Invoke(outcome, null);
        } finally {
            UnityEngine.Object.Destroy(stage);
        }
    }

    /// <summary>
    ///     Who the player was talking to. Not always a card the host handed out: someone waiting to be hired
    ///     only exists in the list of the base
    /// </summary>
    private Chara? FindTarget()
    {
        if (Target is null) {
            return null;
        }

        return Target.Find() as Chara ??
               game.cards.globalCharas.Find(Target.Uid) ??
               _map.charas.Find(c => c.uid == Target.Uid) ??
               EClass.Branch?.listRecruit.Find(hire => hire.chara?.uid == Target.Uid)?.chara;
    }
}
