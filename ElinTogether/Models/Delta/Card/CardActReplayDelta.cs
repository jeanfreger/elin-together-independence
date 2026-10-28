using ElinTogether.Helper;
using ElinTogether.Net;
using ElinTogether.Patches;
using HarmonyLib;
using MessagePack;

namespace ElinTogether.Models;

/// <summary>
///     A menu or held-item action whose lambda only ran in the client's game: it could not give itself a condition,
///     change a stack or a charge, or keep what it created. The host builds the same list of actions for the
///     client's character and runs the entry that was picked <br />
///     Closed list of traits, see <see cref="CardActReplayEvent" />
/// </summary>
[MessagePackObject]
public class CardActReplayDelta : ElinDelta
{
    /// <summary>
    ///     The item or the furniture that offers the action
    /// </summary>
    [Key(0)]
    public required RemoteCard Card { get; init; }

    [Key(1)]
    public required RemoteCard? RootCard { get; init; }

    [Key(2)]
    public required RemoteCard User { get; init; }

    /// <summary>
    ///     Held by the user (<c>TrySetHeldAct</c>), otherwise a menu entry on a piece of furniture (<c>TrySetAct</c>)
    /// </summary>
    [Key(3)]
    public required bool Held { get; init; }

    /// <summary>
    ///     The tile the plan was made for
    /// </summary>
    [Key(4)]
    public required Position Pos { get; init; }

    /// <summary>
    ///     The target of the entry, null when it has none
    /// </summary>
    [Key(5)]
    public RemoteCard? Target { get; init; }

    /// <summary>
    ///     Rank among the entries this item added, and its text: the host finds the entry again by one or the other
    ///     (the text is in each player's own language)
    /// </summary>
    [Key(6)]
    public int Index { get; init; }

    [Key(7)]
    public string? Id { get; init; }

    /// <summary>
    ///     Leash: the state wanted, not a toggle
    /// </summary>
    [Key(8)]
    public bool? Leashed { get; init; }

    protected override void OnApply(ElinNetBase net)
    {
        if (net is not ElinNetHost host || !host.ActiveRemoteCharas.TryGetValue(OriginPeer, out var sender) ||
            User.Find() != sender || Card.Find() is not Thing { isDestroyed: false } card ||
            RootCard?.Find() != card.GetRootCard()) {
            return;
        }

        // only the entries of the closed list, held by the sender or within its reach (a well, a defense's core)
        var root = card.GetRootCard();
        if (Held
                ? card.trait is not (TraitTicketFurniture or TraitSyringe or TraitStethoscope or TraitLeash or TraitWrench or
                    TraitEcoMark or TraitToolBrushStrip or TraitToolHammerStrip or TraitWhipLove or TraitDeedDivorce or
                    TraitDeedLostRing) ||
                  (card.trait is TraitDeedDivorce or TraitDeedLostRing && !SharedMarriages.Enabled) ||
                  root != sender
                : card.trait is not (TraitWell or TraitCoreDefense) || root.pos.Distance(sender.pos) > 2) {
            return;
        }

        Point pos = Pos;
        if (!pos.IsValid || sender.pos.Distance(pos) > 2) {
            return;
        }

        var target = Target?.Find();
        if (Target is not null && target is not { isDestroyed: false }) {
            return;
        }

        using var simulate = Simulate();

        // ZoneEventDefenseGame.Horn_Retreat without its evacuation, which reads "the player" (here the host's own
        // return): the one who blew it evacuates in its own game (CardActReplayEvent), the zone is won and retreated
        if (card.trait is TraitCoreDefense && Id == CardActReplayEvent.RetreatHorn) {
            if (_zone.events.GetEvent<ZoneEventDefenseGame>() is { CanRetreat: true, retreated: false, instance: { } instance } defense) {
                defense.retreated = true;
                instance.status = ZoneInstance.Status.Success;
            }

            host.SendQuestZoneStateHere();
            return;
        }

        // these two open a window or flip a bit in the client's own game, as for the host: the host only keeps
        // the part of the world that is its own
        if (card.trait is TraitStethoscope) {
            card.ModCharge(-1);
            if (card.c_charges <= 0) {
                card.Destroy();
            }

            return;
        }

        if (card.trait is TraitLeash) {
            if (target is Chara companion && companion.IsCompanionOf(sender) && Leashed is { } leashed) {
                companion.SetInt(GuestLeash.Key, leashed ? 1 : 0);
            }

            return;
        }

        using var told = MsgRelayContext.RedirectTo(sender);
        // the sender stands in as "the player", and what it costs in karma (the egg whip) is taken from it, not the host
        // (with one karma for all, the host's own moves as before)
        using var standIn = PersonalQuests.Enabled ? PlayerStandIn.For(host, OriginPeer, sender) : RemoteCraft.AsCrafter(sender);

        var plan = new ActPlan { input = ActInput.RightMouse };
        plan.pos.Set(pos);
        plan.dist = sender.pos.Distance(pos);
        AccessTools.Field(typeof(ActPlan), "_canInteractNeighbor")
            .SetValue(plan, plan.dist == 0 || (plan.dist == 1 && sender.CanInteractTo(pos)));
        if (Held) {
            card.trait.TrySetHeldAct(plan);
        } else {
            card.trait.TrySetAct(plan);
        }

        var item = plan.list.Find(i => i.tc == target && i.act.ID == Id) ??
                   ((uint)Index < (uint)plan.list.Count && plan.list[Index].tc == target ? plan.list[Index] : null);
        // a well's wish is the player's own (its key, its once-a-day flag, its dialog): never rolled here with the
        // host's, the client's game rolls it, see CardActReplayEvent
        var wished = EClass.player.wellWished;
        EClass.player.wellWished = true;
        try {
            using (card.trait is TraitDeedDivorce ? SharedMarriages.SaidYes() : new ScopeExit()) {
                item?.act.Perform();
            }
        } finally {
            EClass.player.wellWished = wished;
        }

        // what the horn changed (the wave, the call for help) offers other horns in the players' games
        if (card.trait is TraitCoreDefense) {
            host.SendQuestZoneStateHere();
        }

        // a well sets its charges directly, nothing else sends them (the holy one counts in the host's player)
        if (card.trait is TraitWell { IsHoly: false }) {
            host.Delta.AddRemote(new CardChargeDelta {
                Card = card,
                Charges = card.c_charges,
            });
        }
    }
}
