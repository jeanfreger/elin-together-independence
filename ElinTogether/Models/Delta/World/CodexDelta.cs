using ElinTogether.Helper;
using ElinTogether.Net;
using MessagePack;

namespace ElinTogether.Models;

public enum CodexKind : byte
{
    /// <summary>
    ///     Cards put in the codex (Num below zero: taken out)
    /// </summary>
    Card,

    Kill,
    Weakspot,
    Spawn,

    /// <summary>
    ///     This creature gave its card once
    /// </summary>
    CardDrop,
}

/// <summary>
///     The codex is the world's, one for everyone like the recipes: it lives in the Player object, which a guest
///     gets again from the host at every world copy. What a guest collected, killed or learnt only counted in its
///     own copy and was gone at the next one. Who tells whom: see CodexPatch
/// </summary>
[MessagePackObject]
public class CodexDelta : ElinDelta
{
    [Key(0)]
    public required string Id { get; init; }

    [Key(1)]
    public required int Num { get; init; }

    /// <summary>
    ///     Passed on by a player who keeps a map, alone or for visitors: those of that map know already
    /// </summary>
    [Key(2)]
    public bool Relayed { get; init; }

    [Key(3)]
    public CodexKind Kind { get; init; }

    /// <summary>
    ///     The character of the player who collected, killed or learnt it (host option "Teams learn on their own":
    ///     only its team's codex changes). 0 from an older game
    /// </summary>
    [Key(4)]
    public int Learner { get; init; }

    internal override string? OnHostPermit(ElinNetHost host, Chara? sender)
    {
        // a creature of the game (security audit 2026-10-27, item 10: any id went into every team's codex)
        return string.IsNullOrEmpty(Id) || !EClass.sources.charas.map.ContainsKey(Id)
            ? $"codex entry {Id} is no creature of the game"
            : TeamKnowledge.CheckLearner(Learner, sender);
    }

    protected override void OnApply(ElinNetBase net)
    {
        if (string.IsNullOrEmpty(Id) || Num == 0 || Num is > 9999 or < -9999 || Kind > CodexKind.CardDrop || player?.codex is not { } codex) {
            return;
        }

        // who: the sender itself on the host of the world, else as told (the visitor of a zone the sender keeps)
        var learner = net is ElinNetHost { IsZoneSession: false } world && !Relayed && world.ActiveRemoteCharas.TryGetValue(OriginPeer, out var sender)
            ? sender.uid
            : Learner;

        // teams learning on their own: the host keeps it in the learner's team's book, and changes its own codex only
        // for its own team (a guest is told only its team's)
        if (net is not ElinNetHost) {
            Change(codex.GetOrCreate(Id), Kind, Num);
        } else {
            TeamKnowledge.Noted(learner, Kind, Id, Num);
            if (TeamKnowledge.Shares(learner, pc.uid)) {
                Change(codex.GetOrCreate(Id), Kind, Num);
            }
        }

        switch (net) {
            // the game that keeps a map for visitors: they hear it from here (not a kill: they saw it, the blow
            // was played again in their game), the world from its own link
            case ElinNetHost { IsZoneSession: true } zone:
                if (Kind != CodexKind.Kill) {
                    TeamKnowledge.Tell(zone, this, learner, OriginPeer);
                }

                if (NetSession.Instance.Transport is ElinNetClient main) {
                    main.SendWhileAway(new CodexDelta { Id = Id, Num = Num, Kind = Kind, Relayed = true, Learner = learner });
                }

                break;
            // the players of that map know already: the others, on this map, hear it now; those alone elsewhere
            // get the codex with their next world copy
            case ElinNetHost host when Relayed:
                TeamKnowledge.Tell(host, this, learner, OriginPeer, true);
                break;
            case ElinNetHost host:
                TeamKnowledge.Tell(host, this, learner, OriginPeer);
                break;
        }
    }

    /// <summary>
    ///     That change on that creature of a codex; false for a kind this game does not know
    /// </summary>
    internal static bool Change(CodexCreature creature, CodexKind kind, int num)
    {
        switch (kind) {
            case CodexKind.Card:
                // never below zero: two players took the last card out at the same moment
                creature.numCard = System.Math.Max(0, creature.numCard + num);
                return true;
            case CodexKind.Kill:
                creature.kills = System.Math.Max(0, creature.kills + num);
                return true;
            case CodexKind.Weakspot:
                creature.weakspot = System.Math.Max(0, creature.weakspot + num);
                return true;
            case CodexKind.Spawn:
                creature.spawns = System.Math.Max(0, creature.spawns + num);
                return true;
            case CodexKind.CardDrop:
                creature.droppedCard = true;
                return true;
            default:
                return false;
        }
    }
}
