using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using ElinTogether.Helper;
using ElinTogether.Models.AI;

namespace ElinTogether.Models;

// TODO: ask Redgeoiz to rework this
internal static class RemoteCraft
{
    private static readonly ConditionalWeakTable<AI_UseCrafter, AIUseCrafterArgs> _selections = new();

    internal static Chara? ProductReceiver { get; set; }

    internal static void Attach(AI_UseCrafter act, AIUseCrafterArgs args)
    {
        _selections.Add(act, args);
    }

    internal static bool TryGet(AI_UseCrafter act, [NotNullWhen(true)] out AIUseCrafterArgs? args)
    {
        return _selections.TryGetValue(act, out args);
    }

    /// <summary>
    ///     The game asks "the player" for the feats, skills and level that shape what is crafted and how long it
    ///     takes. While the host crafts for another player, that player stands in as the local one: without it
    ///     its potions were not doubled by its own feat, and the quality came from the host's skills
    /// </summary>
    internal static ScopeExit AsCrafter(Chara crafter, bool withFame = true)
    {
        var self = EClass.player.chara;
        EClass.player.chara = crafter;
        _standIns++;
        // the level of what is made (CraftUtil, bottles, loot) follows the crafter's fame, not the host's (H5-04)
        var fame = withFame ? PlayerFame.As(crafter) : null;

        return new() {
            OnExit = () => {
                fame?.Dispose();
                EClass.player.chara = self;
                _standIns--;
            },
        };
    }

    private static int _standIns;

    /// <summary>
    ///     "The player" is another player's character for now: what this game tells of its own player waits
    /// </summary>
    internal static bool IsStandingIn => _standIns > 0;

    internal static bool IsHostRun(AIAct? act)
    {
        return act is AI_UseCrafter crafter && _selections.TryGetValue(crafter, out _);
    }
}