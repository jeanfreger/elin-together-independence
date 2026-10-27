using System.Collections.Generic;
using ElinTogether.Helper;
using ElinTogether.Net;
using MessagePack;

namespace ElinTogether.Models;

[MessagePackObject]
public class CharaActPerformDelta : ElinDelta
{
    // builtin acts that are not instantiated
    private static readonly Dictionary<int, Act> _builtInMapping = new() {
        [ABILITY.ActWait] = ACT.Wait,
        [ABILITY.ActMelee] = ACT.Melee,
        [ABILITY.ActThrow] = ACT.Throw,
        [ABILITY.ActRanged] = ACT.Ranged,
        [ABILITY.ActKick] = ACT.Kick,
        [ABILITY.ActChat] = ACT.Chat,
        [ABILITY.ActPick] = ACT.Pick,
        [ABILITY.ActItem] = ACT.Item,
    };
    private static bool _staticMapped;

    [Key(0)]
    public required int ActId { get; init; }

    [Key(1)]
    public required RemoteCard Owner { get; init; }

    [Key(2)]
    public required RemoteCard? TargetCard { get; init; }

    [Key(3)]
    public required Position? Pos { get; init; }

    /// <summary>
    ///     The tool of an act the game builds around a held item (a rod and its ActZap). Such an act has no id of
    ///     its own to be made again from: without the tool the other side performed an empty act, the rod of a
    ///     client did nothing to the world and kept its charges
    /// </summary>
    [Key(4)]
    public RemoteCard? Tool { get; init; }

    /// <summary>
    ///     Which act of the tool: a watering can draws water or waters
    /// </summary>
    [Key(5)]
    public ToolAct ToolKind { get; init; }

    public enum ToolAct : byte
    {
        Zap,
        DrawWater,
        Water,
        ClearWater,
    }

    public static CharaActPerformDelta Create(Act act)
    {
        ApplyBuiltInMapping();

        var (tool, kind) = ToolOf(act);
        return new() {
            ActId = act.id,
            Owner = Act.CC,
            TargetCard = Act.TC,
            Pos = Act.TP,
            Tool = tool,
            ToolKind = kind,
        };
    }

    private static (Card? tool, ToolAct kind) ToolOf(Act act)
    {
        return act switch {
            ActZap zap => (zap.trait?.owner, ToolAct.Zap),
            ActDrawWater draw => (draw.waterCan?.owner, ToolAct.DrawWater),
            ActWater water => (water.waterCan?.owner, ToolAct.Water),
            ActClearWater clear => (clear.waterPot?.owner, ToolAct.ClearWater),
            _ => (null, ToolAct.Zap),
        };
    }

    private Act? ActOf(Card? tool)
    {
        return (ToolKind, tool?.trait) switch {
            (ToolAct.Zap, TraitRod rod) => new ActZap { trait = rod },
            (ToolAct.DrawWater, TraitToolWaterCan can) => new ActDrawWater { waterCan = can },
            (ToolAct.Water, TraitToolWaterCan can) => new ActWater { waterCan = can },
            (ToolAct.ClearWater, TraitToolWaterPot pot) => new ActClearWater { waterPot = pot },
            _ => null,
        };
    }

    /// <summary>
    ///     A guest acts as itself or its companions, with an act the actor has (or one of the game's own built-in acts,
    ///     or with a tool): any character, any act, a Wish nobody had, were taken (security audit 2026-10-27)
    /// </summary>
    internal override string? OnHostPermit(ElinNetHost host, Chara? sender)
    {
        if ((NoCharacter(sender) ?? NotOwned(sender, Owner, "actor")) is { } why) {
            return why;
        }

        if (Tool is not null || _builtInMapping.ContainsKey(ActId) || Owner.Find() is not Chara actor ||
            actor.elements.GetElement(ActId) is not null) {
            return null;
        }

        return $"act {ActId} is not one of {actor.uid}'s";
    }

    protected override void OnApply(ElinNetBase net)
    {
        ApplyBuiltInMapping();

        // we do not apply to ourselves
        if (Owner.Find() is not Chara { IsPC: false } chara) {
            return;
        }

        if (Tool is not null) {
            PerformWithTool(net, chara);
            return;
        }

        // reperform act
        var act = _builtInMapping.GetValueOrDefault(ActId);
        act ??= chara.elements.GetElement(ActId)?.act ?? ACT.Create(ActId);
        act.id = ActId;

        // pos compensation if high rtt
        var target = TargetCard?.Find();
        var pos = Pos;
        if (target is Chara { isDead: false, IsInActiveMap: true } targetChara &&
            pos is not null && targetChara.pos.Distance(pos) <= 2) {
            pos = targetChara.pos;
        }

        act.Perform(chara, target, pos);
    }

    private void PerformWithTool(ElinNetBase net, Chara chara)
    {
        // only with the tool in that character's hands
        var tool = Tool?.Find();
        if (tool is null || tool.GetRootCard() != chara || ActOf(tool) is not { } act) {
            return;
        }

        // a zap names its user as target while it performs: only the tile aimed at is passed on
        if (net.IsHost) {
            // a guest's game owns its bag and has already sent the charges it took (CardChargeEvent): this replay
            // takes one more, so give one back first, the count stays the guest's
            var left = tool.c_charges;
            if (ToolKind == ToolAct.Zap) {
                tool.c_charges = left + 1;
            }

            try {
                act.Perform(chara, null, Pos);
            } finally {
                if (ToolKind == ToolAct.Zap && tool.c_charges != left && !tool.isDestroyed) {
                    tool.c_charges = left;
                }
            }

            return;
        }

        // the charges left come from the host, this replay is for show (and for the watered tiles): the act is
        // given what its own check asks for, then the host's count is put back
        var charges = tool.c_charges;
        tool.c_charges = ToolKind == ToolAct.DrawWater ? 0 : charges + 1;
        try {
            act.Perform(chara, null, Pos);
        } finally {
            tool.c_charges = charges;
        }
    }

    private static void ApplyBuiltInMapping()
    {
        if (_staticMapped) {
            return;
        }

        foreach (var (k, v) in _builtInMapping) {
            v.id = k;
        }
        _staticMapped = true;
    }
}