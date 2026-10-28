namespace ElinTogether.Helper;

/// <summary>
///     Shipping is per player: the one box of the base remembers who put each item in, the morning sale
///     pays everyone for its own goods, see ElinNetHostShipping
/// </summary>
internal static class ShippingHelper
{
    /// <summary>
    ///     Chara uid of the player who put the item in the shipping box
    /// </summary>
    internal const string ShipperKey = "emp_shipper";

    /// <summary>
    ///     Set while the host puts an item in the box for someone else (deposit of a player travelling alone)
    /// </summary>
    internal static int? ShipperOverride { get; set; }

    /// <summary>
    ///     Host rule, off: goods are not told apart and the host is paid for everything
    /// </summary>
    internal static bool Enabled => ElinTogether.Net.NetSession.Instance.Rules.UsePlayerShipping;

    internal const int BoxDelivery = 1;
    internal const int BoxBank = 2;

    /// <summary>
    ///     The bank and the delivery box are one for the world, like the shipping box, or one for each owner
    ///     (<see cref="TeamBoxes" />): 0 for any other card
    /// </summary>
    internal static int OtherWorldBox(Card? card)
    {
        if (card is null || EClass.game?.cards is not { } cards) {
            return 0;
        }

        if (card == cards.container_deliver) {
            return BoxDelivery;
        }

        // (and the boxes of each owner, see TeamBoxes: told and mirrored like the world's)
        return card == cards.container_deposit ? BoxBank : TeamBoxes.KindOf(card);
    }

    /// <summary>
    ///     Alone away, an open box of the world shows what the host holds: every thing in it is only a picture,
    ///     tagged with the host uid of the real one and with its box + 1. A picture never stacks with a real
    ///     thing and never enters a bag: taking it asks the host for the real one, see CardAddThingEvent
    /// </summary>
    internal const string MirrorKey = "emp_box_uid";

    internal const string MirrorBoxKey = "emp_box";

    /// <summary>
    ///     On the character of a player alone away: number of the last thing it took out of a box of the world.
    ///     It travels with the bag in every checkpoint, so the host reads there whether the player has what it was
    ///     handed (an int key: Card.SetInt(string) of "the player" also writes the shared dialog flags)
    /// </summary>
    internal static readonly int TookKey = "emp_took".GetHashCode();

    /// <summary>
    ///     Set while the pictures are put in the box: nothing of it is a deposit
    /// </summary>
    internal static bool FillingMirror { get; set; }

    /// <summary>
    ///     Shipping box 0, delivery box 1, bank 2 (ShippingDeposit.Box), -1 for any other card
    /// </summary>
    internal static int WorldBoxIndex(Card? card)
    {
        if (IsShippingBox(card)) {
            return 0;
        }

        var box = OtherWorldBox(card);
        return box == 0 ? -1 : box;
    }

    internal static Thing? WorldBox(int box)
    {
        return EClass.game?.cards is not { } cards ? null :
            box == BoxDelivery ? cards.container_deliver :
            box == BoxBank ? cards.container_deposit : cards.container_shipping;
    }

    internal static bool IsShippingBox(Card? card)
    {
        return card is not null && card == EClass.game?.cards?.container_shipping;
    }

    extension(Thing thing)
    {
        internal int ShipperUid => thing.GetInt(ShipperKey);
    }
}
