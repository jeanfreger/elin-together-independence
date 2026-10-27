using ElinTogether.Helper;
using ElinTogether.Net;
using ElinTogether.Patches;
using MessagePack;

namespace ElinTogether.Models;

[MessagePackObject]
public class CardOnUseDelta : ElinDelta
{
    [Key(0)]
    public required RemoteCard Card { get; init; }

    [Key(1)]
    public required RemoteCard? RootCard { get; init; }

    [Key(2)]
    public required RemoteCard User { get; init; }

    /// <summary>
    ///     Used on a tile (an empty bottle at the water's edge)
    /// </summary>
    [Key(3)]
    public Position? Pos { get; init; }

    /// <summary>
    ///     Used on another card (a dye on a piece of furniture, meat on a grave)
    /// </summary>
    [Key(4)]
    public RemoteCard? Target { get; init; }

    /// <summary>
    ///     Bait: the state its user wants, not a toggle. Its own game equipped it at once; a toggle played again
    ///     there would undo it
    /// </summary>
    [Key(5)]
    public bool? Equip { get; init; }

    internal override string? OnHostPermit(ElinNetHost host, Chara? sender)
    {
        // by the sender or its companion, out of what they hold or what lies around (a shrine, a well, a chest)
        return NoCharacter(sender) ?? NotOwned(sender, User, "user") ??
               (Card.Find()?.GetRootCard() is Chara holder && !OwnsOrCompanion(sender!, holder)
                   ? $"the used thing is held by {holder.uid}"
                   : null);
    }

    internal override void OnHostRefused(ElinNetHost host)
    {
        SendBack(host, Card);
    }

    protected override void OnApply(ElinNetBase net)
    {
        if (Card.Find() is not { isDestroyed: false } card || User.Find() is not Chara user) {
            return;
        }

        if (RootCard?.Find() != card.GetRootCard()) {
            return;
        }

        // what the use earns (a Steam achievement) is its user's, not this game's player's (H5-10)
        using var earned = PlayerAchievements.For(user);

        if (Equip is { } equip && card.trait is TraitEquipItem item) {
            if (net is ElinNetHost equipHost &&
                (!equipHost.ActiveRemoteCharas.TryGetValue(OriginPeer, out var holder) || holder != user ||
                 card.GetRootCard() != user)) {
                return;
            }

            Relay(net);

            // the one who asked already has it
            if (!user.IsPC) {
                item.EQ = equip ? card.Thing : null;
            }

            return;
        }

        // two players reaching for the same shrine: its power is given once
        if (net.IsHost && card.trait is TraitPowerStatue && !card.isOn) {
            return;
        }

        // on the host these give or change things for the user: its own doing, to be sent like any other
        if (Pos is not null) {
            Point pos = Pos!;
            if (net.IsHost && (!pos.IsValid || !card.trait.CanUse(user, pos))) {
                return;
            }

            Relay(net);
            using var _ = Simulate(net.IsHost);
            card.trait.OnUse(user, pos);
            return;
        }

        if (Target is not null) {
            if (Target.Find() is not { isDestroyed: false } target || (net.IsHost && !card.trait.CanUse(user, target))) {
                return;
            }

            Relay(net);
            using var _ = Simulate(net.IsHost);
            card.trait.OnUse(user, target);
            return;
        }

        // a box the game opens for "the player": on the host that is the host, who got what was inside
        // the one who asked stands in for the opening; what it gets travels as usual, nothing to replay
        if (net is ElinNetHost host && OpensForPlayer(card.trait) &&
            host.ActiveRemoteCharas.TryGetValue(OriginPeer, out var sender) && sender == user) {
            using var simulate = Simulate();
            using var told = MsgRelayContext.RedirectTo(user);
            using var standIn = RemoteCraft.AsCrafter(user);
            // the New Year pack's ally follows the one who opened it
            using var pets = CharaMakeAllyEvent.GiftsFor(user);
            card.trait.OnUse(user);
            return;
        }

        // these act for "the player": on the host, the host. A guest does them as its own, see GateItems
        if (net.IsHost && card.trait is TraitMoongate or TraitLandingPortal or TraitSkyHelm) {
            return;
        }

        Relay(net);
        card.trait.OnUse(user);
    }

    private static bool OpensForPlayer(Trait trait)
    {
        var type = trait.GetType();
        return type == typeof(TraitParcel) || type == typeof(TraitGiftPack) || type == typeof(TraitPlamoBox) ||
               type == typeof(TraitGachaBall) || type == typeof(TraitGiftNewYear) || type == typeof(TraitGiftJure) ||
               // the machine god's gives a window to choose from: not here
               (type == typeof(TraitGodStatue) && ((TraitGodStatue)trait).Religion.id != "machine") ||
               // a shrine blesses "the player" and its party, or gives it a recipe: on the host that was the
               // host, and every game drew a recipe of its own. The two that open a window to choose from: not here
               (type == typeof(TraitShrine) && ((TraitShrine)trait).Shrine.id is not ("material" or "armor"));
    }

    private void Relay(ElinNetBase net)
    {
        if (net.IsHost) {
            net.Delta.AddRemote(this);
        }
    }
}
