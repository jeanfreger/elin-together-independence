using System;
using System.Collections.Generic;
using System.Linq;
using ElinTogether.Helper;
using ElinTogether.Net;
using MessagePack;

namespace ElinTogether.Models;

/// <summary>
///     A click of the build mode by a player who does not simulate the map (council 5): the game makes the task
///     and has it done at once by the invisible "agent". That player's game sends the task here instead of doing
///     it on its own copy of the map; the game that keeps the map does it, with the sender as "the player" (what
///     is mined goes to the sender's bag), and the new terrain comes back as for anything done there
///     (<see cref="TileStateDelta" />) <br />
///     The gold of the click (10 a tile) is already a request of its own (CardModCurrencyDelta): a task that
///     cannot be done any more gives it back. The materials of a build are taken here, once, from the sender's
///     bag and then from the stock of the map, as the game does for the local player
/// </summary>
[MessagePackObject]
public class AgentTaskDelta : ElinDelta
{
    // ActionMode.CostMoney of the build mode's gestures
    private const int MaxPaid = 10;

    [Key(0)]
    public required TaskArgsBase Args { get; init; }

    /// <summary>
    ///     The gold this tile cost the sender (ladders, stock items and forced tiles are free)
    /// </summary>
    [Key(1)]
    public int Paid { get; init; }

    /// <summary>
    ///     This game's own agent finishing a task of the build mode, in a game that does not keep the map: sent
    ///     in place of doing it
    /// </summary>
    internal static bool TrySend(TaskDesignation task)
    {
        if (IsApplying || NetSession.Instance.Connection is not ElinNetClient client || task.owner is not { IsAgent: true } ||
            task is TaskBuild { held: not null }) {
            return false;
        }

        // refused before the click (RemoteBuildModePatch): nothing was paid, nothing is done
        if (scene.actionMode.IsRoofEditMode() || !NetSession.Instance.Rules.GuestsBuild) {
            return true;
        }

        TaskArgsBase? args = task switch {
            TaskBuild build => TaskBuildArgs.Create(build),
            TaskMine mine => TaskMineArgs.Create(mine),
            TaskDig dig => TaskDigArgs.Create(dig),
            TaskCut cut => TaskCutArgs.Create(cut),
            _ => null,
        };
        if (args is null) {
            return false;
        }

        // what the click's summary counted for each valid tile (HitSummary, paid right after the tiles are processed)
        var summary = screen.tileSelector.summary;
        client.Delta.AddRemote(new AgentTaskDelta {
            Args = args,
            Paid = summary.countValid > 0 ? summary.money / summary.countValid : 0,
        });
        return true;
    }

    protected override void OnApply(ElinNetBase net)
    {
        if (net is not ElinNetHost host || !NetSession.Instance.Rules.GuestsBuild ||
            !host.ActiveRemoteCharas.TryGetValue(OriginPeer, out var sender) || !Teams.CanTouch(sender, _zone) ||
            Args.CreateSubAct() is not TaskDesignation task || !task.pos.IsValid) {
            return;
        }

        // read again from this game's map: two clicks on the same tile, or a map that changed meanwhile
        // as this game's own gesture: what it makes and changes must reach everyone; "the player" is the sender
        using var simulate = Simulate();
        using var asSender = RemoteCraft.AsCrafter(sender);
        var can = task switch {
            // a second click on the tile the first one already built: nothing to do, nothing to take
            TaskBuild build => build.recipe?.source is not null &&
                               !build.recipe.HasSameTile(build.pos, build.recipe._dir, build.altitude, build.bridgeHeight) &&
                               TakeMaterials(build.recipe),
            TaskMine => task.pos.HasBlock,
            TaskCut => task.pos.HasObj || task.pos.HasDecal,
            TaskDig => true,
            _ => false,
        };
        if (!can) {
            // (given back only what was really paid for it on a server that enforces trust: a failing task told
            // to have cost 10 farmed 10 gold each; security audit 2026-10-27, item 6)
            var refund = TrustEnforced ? (int)CardModCurrencyDelta.TakeSpent(OriginPeer, Math.Min(Paid, MaxPaid)) : Math.Min(Paid, MaxPaid);
            if (refund > 0) {
                sender.ModCurrency(refund);
            }

            return;
        }

        var agent = player.Agent;
        task.owner = agent;
        agent.pos.Set(task.pos);
        task.OnProgressComplete();
    }

    /// <summary>
    ///     HitSummary.Execute for one tile: all the ingredients are there or nothing is taken
    /// </summary>
    private static bool TakeMaterials(Recipe recipe)
    {
        // an item of the stock or of the bag is the product itself: that very item, not another of the same kind
        // (RefreshThing falls back on the id), and the build takes it (RecipeCard.Build)
        if (recipe.UseStock) {
            if (recipe.ingredients.Count == 0) {
                return false;
            }

            var uid = recipe.ingredients[0].uid;
            var item = pc.things.Find(uid) ?? _map.Stocked.Find(uid);
            if (item is null || item.isDestroyed || item.Num <= 0) {
                return false;
            }

            recipe.ingredients[0].thing = item;
            if (!recipe.VirtualBlock) {
                return true;
            }
        }

        // what was sent is the sender's word: as many ingredients as the recipe asks, none for less than it asks
        var asked = recipe.source.GetIngredients();
        if (!recipe.UseStock && recipe.RequireIngredients &&
            (recipe.ingredients.Count != asked.Count || recipe.ingredients.Where((ing, i) => ing.req < asked[i].req).Any())) {
            return false;
        }

        if (recipe.GetIdThing() == "deco") {
            return true;
        }

        var things = new List<Thing>();
        foreach (var ingredient in recipe.ingredients) {
            var thing = recipe.UseStock ? recipe.ingredients[0].RefreshThing() : ingredient.RefreshThing();
            if (thing is null || thing.isDestroyed || thing.Num < ingredient.req) {
                return false;
            }

            things.Add(thing);
        }

        for (var i = 0; i < things.Count; i++) {
            things[i].ModNum(-recipe.ingredients[i].req);
        }

        return true;
    }
}
