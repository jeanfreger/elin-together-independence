using System.Linq;
using ElinTogether.Models;
using ElinTogether.Net;
using HarmonyLib;
using MessagePack;

namespace ElinTogether.Helper;

/// <summary>
///     What decides the special shelves of a shop, as the player who opens it has them: the game reads the host's
/// </summary>
[MessagePackObject]
public class ShopGates
{
    /// <summary>
    ///     Married (the marriage and tape shop sells its cassettes)
    /// </summary>
    [Key(0)]
    public bool Married { get; init; }

    /// <summary>
    ///     Loytel's mart level
    /// </summary>
    [Key(1)]
    public int LoytelLv { get; init; }

    /// <summary>
    ///     Member of the merchant guild, high enough for the exotic merchant
    /// </summary>
    [Key(2)]
    public bool Exotic { get; init; }

    /// <summary>
    ///     Member of the mages guild (a magic shop offers the trial letter to those who are not)
    /// </summary>
    [Key(3)]
    public bool Mage { get; init; }
}

/// <summary>
///     Council 2026-10-23 (shops): one stock for all. At a restock the game reads "the player" for the stock bonus
///     (feat 1406) and the host's progress for the special shelves. The stock gets the bonus of the best player on
///     the map; the shelves follow the player who opens the shop (guests tell theirs with the request: their
///     progress is their game's own, the host does not keep it). Host alone: nothing changes
/// </summary>
[HarmonyPatch]
internal static class ShopStock
{
    private const int StockFeat = 1406;

    private static ShopGates? _gates;
    private static bool? _exotic;
    private static bool? _mage;
    private static bool _active;

    internal static ShopGates Local()
    {
        return new() {
            Married = EClass.player.stats.married > 0,
            LoytelLv = EClass.player.flags.loytelMartLv,
            Exotic = EClass.player.IsMerchantGuildMember && Guild.Merchant.relation.rank >= 4,
            Mage = Guild.Mage.IsMember,
        };
    }

    /// <summary>
    ///     The restock run inside is for the player who sent these (null: the host's own)
    /// </summary>
    internal static ScopeExit Opened(ShopGates? gates, Chara? opener = null)
    {
        var before = _gates;
        _gates = gates;
        // a travelling merchant's stock level is the fame of who opens it (H5-04)
        var fame = PlayerFame.As(opener);
        return new() {
            OnExit = () => {
                fame?.Dispose();
                _gates = before;
            },
        };
    }

    private static ScopeExit? Begin()
    {
        if (_active || NetSession.Instance.Connection is not ElinNetHost host ||
            !EmpConfig.Server.ShopsFromEveryPlayer.Value) {
            return null;
        }

        var best = EClass.pc;
        foreach (var chara in host.ActiveRemoteCharas.Values.Where(c => c.IsInActiveMap)) {
            if (chara.Evalue(StockFeat) > best.Evalue(StockFeat)) {
                best = chara;
            }
        }

        var gates = _gates;
        if (best == EClass.pc && gates is null) {
            return null;
        }

        _active = true;
        var stats = EClass.player.stats;
        var flags = EClass.player.flags;
        var (married, loytel) = (stats.married, flags.loytelMartLv);
        var standIn = best == EClass.pc ? null : RemoteCraft.AsCrafter(best, withFame: false);
        if (gates is not null) {
            stats.married = gates.Married ? System.Math.Max(1, married) : 0;
            // (the mart has levels 0 to 2)
            flags.loytelMartLv = System.Math.Clamp(gates.LoytelLv, 0, 2);
            _exotic = gates.Exotic;
            _mage = gates.Mage;
        }

        return new() {
            OnExit = () => {
                standIn?.Dispose();
                stats.married = married;
                flags.loytelMartLv = loytel;
                _exotic = null;
                _mage = null;
                _active = false;
            },
        };
    }

    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    [HarmonyPatch(typeof(Trait), nameof(Trait.OnBarter))]
    internal static void OnRestock(out ScopeExit? __state)
    {
        __state = Begin();
    }

    [HarmonyFinalizer]
    [HarmonyPatch(typeof(Trait), nameof(Trait.OnBarter))]
    internal static void OnRestockEnd(ScopeExit? __state)
    {
        __state?.Dispose();
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(TraitMerchantExotic), nameof(TraitMerchantExotic.ShopType), MethodType.Getter)]
    internal static void OnExoticType(ref ShopType __result)
    {
        if (_exotic is { } exotic) {
            __result = exotic ? ShopType.Exotic : ShopType.None;
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Guild), nameof(Guild.IsMember), MethodType.Getter)]
    internal static void OnIsMember(Guild __instance, ref bool __result)
    {
        if (_mage is { } mage && __instance is GuildMage) {
            __result = mage;
        }
    }
}
