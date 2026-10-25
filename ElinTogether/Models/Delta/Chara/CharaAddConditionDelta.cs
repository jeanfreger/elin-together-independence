using ElinTogether.Helper;
using ElinTogether.Net;
using ElinTogether.Patches;
using MessagePack;

namespace ElinTogether.Models;

[MessagePackObject]
public class CharaAddConditionDelta : ElinDelta
{
    [Key(0)]
    public required RemoteCard Owner { get; init; }

    // TODO build condition type mapping
    [Key(1)]
    public required int ConditionId { get; init; }

    [Key(2)]
    public required int Power { get; init; }

    [Key(3)]
    public required bool Force { get; init; }

    [Key(4)]
    public bool Remove { get; set; }

    // what some conditions are made of (the element of an enchanted weapon, of a resistance): BaseCondition.refVal
    [Key(5)]
    public int RefVal { get; set; }

    [Key(6)]
    public int RefVal2 { get; set; }

    internal static bool IsTrapCondition(string alias)
    {
        // and the breath lost under deep water, which only the diver's own game notices, and what heavy rain does
        return alias is nameof(ConSleep) or nameof(ConBlind) or nameof(ConParalyze) or nameof(ConSuffocation) or nameof(ConConfuse);
    }

    /// <summary>
    ///     What the priestesses' blessing gives (DramaCustomSequence, step _blessing), at its default power
    /// </summary>
    internal static bool IsBlessingCondition(string alias)
    {
        return alias is nameof(ConHolyVeil) or nameof(ConEuphoric) or nameof(ConNightVision);
    }

    protected override void OnApply(ElinNetBase net)
    {
        if (net is ElinNetHost host) {
            // reject every chara add condition delta from clients, but what a trap does to the player who
            // sent it: that trap was rolled in its game only
            if (!Remove && host.ActiveRemoteCharas.TryGetValue(OriginPeer, out var sender) &&
                sources.stats.map.TryGetValue(ConditionId, out var asked)) {
                if (Owner.Find() == sender && IsTrapCondition(asked.alias)) {
                    using var _ = Simulate();
                    sender.AddCondition(Condition.Create(asked.alias, Power), Force);
                } else if (IsBlessingCondition(asked.alias) && Owner.Find() is Chara { isDead: false } target &&
                           (target == sender || target.IsCompanionOf(sender))) {
                    // the blessing of the sender's dialog for itself and its companions: its power is the game's, not
                    // asked, and it lasts as a perfume does
                    using var _ = Simulate();
                    target.AddCondition(Condition.Create(asked.alias, 100))?.SetPerfume();
                }
            }

            return;
        }

        if (Owner.Find() is not Chara chara) {
            return;
        }

        if (Remove) {
            chara.conditions.ForeachReverse(c => {
                if (c.id == ConditionId) {
                    c.Kill();
                }
            });
        } else {
            var row = sources.stats.map[ConditionId];
            chara.Stub_AddCondition(Condition.Create(row.alias, Power, c => c.SetRefVal(RefVal, RefVal2)), Force);
        }
    }
}