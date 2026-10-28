using System.Collections.Generic;
using ElinTogether.Helper;
using ElinTogether.Net;
using ElinTogether.Patches;
using MessagePack;

namespace ElinTogether.Models;

/// <summary>
///     What a dialog remembers (what was said, what was chosen), the same for every player, like the quest log
/// </summary>
[MessagePackObject]
public class DialogFlagDelta : ElinDelta
{
    internal override bool InSendersStory => true;

    [Key(0)]
    public required string Id { get; init; }

    [Key(1)]
    public required int Value { get; init; }

    // what one player's dialogs may move in an hour (security audit 2026-10-27, item 5)
    private const int HourlyInfluence = 50;
    private const int HourlyGuildExp = 10000;
    private const int HourlyKeyItems = 30;
    private const int MaxExtraTax = 1_000_000;
    private static readonly Dictionary<(int Peer, string What), (float Since, int Used)> Budgets = [];

    /// <summary>
    ///     Security audit 2026-10-27 (item 5): what the story remembers is the group's, and a modified game could set any
    ///     of it for everyone (the debt to 0, a guild rank, a town's influence, key items, any extra tax). Checked
    ///     against the host's own value: the debt only goes down by gold just paid, the extra tax stays in the game's
    ///     range, a guild rank one step at a time, a guild's exp, a town's influence and key items within an hour's
    ///     budget. Story flags and dialog choices are left as they are (a refusal there could break the story)
    /// </summary>
    internal override string? OnHostPermit(ElinNetHost host, Chara? sender)
    {
        var old = DialogFlagSync.ValueOf(Id) ?? 0;
        var change = Value - old;
        if (Id == "p:debt") {
            if (change > 0) {
                return $"the debt raised {old} -> {Value}";
            }

            var paid = change < 0 ? CardModCurrencyDelta.TakeSpent(OriginPeer, -change) : 0;
            return paid < -change ? $"the debt lowered {old} -> {Value} with {paid} gold paid" : null;
        }

        if (Id == "p:extraTax") {
            return Value is < 0 or > MaxExtraTax ? $"an extra tax of {Value}" : null;
        }

        if (Id.StartsWith("g:") && Id.EndsWith(":r")) {
            return change > 1 ? $"a guild rank {old} -> {Value}" : null;
        }

        if (Id.StartsWith("g:") && Id.EndsWith(":e")) {
            return change > 0 ? Spend("guild exp", change, HourlyGuildExp) : null;
        }

        if (Id.StartsWith("z:")) {
            return change > 0 ? Spend("influence", change, HourlyInfluence) : null;
        }

        if (Id.StartsWith("k:")) {
            return change != 0 ? Spend("key items", System.Math.Abs(change), HourlyKeyItems) : null;
        }

        return null;
    }

    private string? Spend(string what, int amount, int cap)
    {
        var now = UnityEngine.Time.realtimeSinceStartup;
        var key = (OriginPeer, what);
        if (!Budgets.TryGetValue(key, out var used) || now - used.Since > 3600f) {
            used = (now, 0);
        }

        if (used.Used + amount > cap) {
            return $"{what} +{amount} over the hour's {cap} ({Id})";
        }

        Budgets[key] = (used.Since, used.Used + amount);
        return null;
    }

    /// <summary>
    ///     The sender is told the value the host keeps (its game would stay apart, and never say it again)
    /// </summary>
    internal override void OnHostRefused(ElinNetHost host)
    {
        if (DialogFlagSync.ValueOf(Id) is { } kept) {
            host.SendDeltaTo(OriginPeer, new DialogFlagDelta { Id = Id, Value = kept });
        }
    }

    protected override void OnApply(ElinNetBase net)
    {
        DialogFlagSync.Learn(Id, Value);

        if (net is not ElinNetHost host) {
            return;
        }

        host.SendDeltaToAllExcept(OriginPeer, this);
        // hosting a zone as a client: the host of the world has to know too
        QuestAwaySync.Send(this);
    }
}
