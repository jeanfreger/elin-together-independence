using System.Collections.Generic;
using ElinTogether.Models;
using ElinTogether.Net;
using HarmonyLib;
using UnityEngine;

namespace ElinTogether.Helper;

/// <summary>
///     A teammate's character sheet (host box "Team character sheets"): the host answers a teammate's request with a
///     snapshot of the character (what <see cref="CharaExport" /> ships) and the asker opens the game's own sheet on a
///     throwaway copy that no zone ever holds. Read-only: the copy cannot be tracked, equipped or dropped from
/// </summary>
[HarmonyPatch]
internal static class TeamSheet
{
    private const float MinGap = 2f;

    // asker's chara uid -> when its last request was taken
    private static readonly Dictionary<int, float> _last = [];

    // the copy the open sheet shows, and what the host told of its standing
    private static Chara? _shown;
    private static int _karma, _fame, _deposit;

    internal static bool On => NetSession.Instance is { InGame: true } net && EClass.pc != null &&
                               (net.Transport is ElinNetHost
                                   ? EmpConfig.Server.TeamSheets.Value && EmpConfig.Server.Teams.Value
                                   : net.Rules is { UseTeams: true, TeamSheets: true });

    /// <summary>
    ///     The "Sheet" button: the host reads its own data, a guest asks the host
    /// </summary>
    internal static void Ask(int uid)
    {
        if (!On) {
            return;
        }

        switch (NetSession.Instance.Transport) {
            case ElinNetHost host:
                if (Answer(host, EClass.pc, uid) is { } answer) {
                    Show(answer);
                }

                break;
            case ElinNetClient client:
                client.Delta.AddRemote(new CharaSheetDelta { Kind = CharaSheetDelta.Request, Uid = uid });
                break;
        }
    }

    /// <summary>
    ///     Host: a guest's request
    /// </summary>
    internal static void OnRequest(ElinNetHost host, int peer, int uid)
    {
        if (host.IsZoneSession || host.PlayerChara(peer) is not { } asker) {
            return;
        }

        if (Answer(host, asker, uid) is { } answer) {
            host.SendDeltaTo(peer, answer);
        }
    }

    /// <summary>
    ///     Host: the snapshot of a teammate for that asker, null (and a log line) when refused
    /// </summary>
    private static CharaSheetDelta? Answer(ElinNetHost host, Chara asker, int uid)
    {
        if (!On) {
            return Refuse(asker, uid, "the box is off");
        }

        var team = Teams.TeamOf(asker.uid);
        if (team == Teams.Wanderers || Teams.TeamOf(uid) != team) {
            return Refuse(asker, uid, "not in the same team");
        }

        if (Teams.FindChara(uid) is not { } target) {
            return Refuse(asker, uid, "character not found");
        }

        var now = Time.unscaledTime;
        if (_last.TryGetValue(asker.uid, out var last) && now - last < MinGap) {
            return Refuse(asker, uid, "too many requests");
        }

        _last[asker.uid] = now;
        try {
            var (karma, fame) = StandingOf(host, target);
            return new() {
                Kind = CharaSheetDelta.Answer,
                Uid = uid,
                Data = CharaExport.Pack(target, null!).Bytes,
                Karma = karma,
                Fame = fame,
                Deposit = DepositOf(target),
            };
        } catch (System.Exception ex) {
            EmpLog.Warning(ex, "Could not pack the sheet of {Uid} for {Asker}", uid, asker.uid);
            return null;
        }
    }

    private static CharaSheetDelta? Refuse(Chara asker, int uid, string why)
    {
        EmpLog.Warning("Refused the character sheet of {Uid} to {Asker}: {Why}", uid, asker.uid, why);
        return null;
    }

    private static (int Karma, int Fame) StandingOf(ElinNetHost host, Chara target)
    {
        return target == EClass.pc
            ? (EClass.player.karma, EClass.player.fame)
            : (host.KarmaOf(target), host.FameOf(target) ?? 0);
    }

    // what the sheet's "bank" adds up to: the world's box, then the player's own and its team's with team boxes on
    private static int DepositOf(Chara target)
    {
        var world = EClass.game.cards.container_deposit;
        var total = world.GetCurrency();
        if (TeamBoxes.BankOf(target) is { } own && own != world) {
            total += own.GetCurrency();
        }

        var team = Teams.TeamOf(target.uid);
        if (team != Teams.Wanderers && TeamBoxes.BankOfTeam(team) is { } bank && bank != world) {
            total += bank.GetCurrency();
        }

        return total;
    }

    /// <summary>
    ///     Asker: the sheet of the answer, on a copy never added to a zone
    /// </summary>
    internal static void Show(CharaSheetDelta answer)
    {
        if (answer.Data.Length == 0) {
            return;
        }

        try {
            var chara = new LZ4Bytes { Bytes = answer.Data }.Decompress<Chara>();
            _shown = chara;
            _karma = answer.Karma;
            _fame = answer.Fame;
            _deposit = answer.Deposit;
            EClass.ui.AddLayer<LayerChara>().SetChara(chara);
        } catch (System.Exception ex) {
            _shown = null;
            EmpLog.Warning(ex, "Could not open the sheet of {Uid}", answer.Uid);
        }
    }

    private static bool IsShown(Card? card) => _shown != null && card == _shown;

    // ---- the window

    [HarmonyPostfix]
    [HarmonyPatch(typeof(WindowChara), nameof(WindowChara.RefreshProfile))]
    private static void OnProfile(WindowChara __instance)
    {
        if (!IsShown(__instance.chara)) {
            return;
        }

        __instance.textKarma.text = _karma.ToString();
        __instance.textFame.text = _fame.ToFormat();
        __instance.textDeposit.text = Lang._currency(_deposit, showUnit: true);
    }

    // read-only: the sheet's own buttons act on the copy
    [HarmonyPrefix]
    [HarmonyPatch(typeof(WidgetTracker), nameof(WidgetTracker.Toggle))]
    private static bool OnToggle()
    {
        return _shown == null || LayerChara.Instance?.windowChara?.chara != _shown;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(Chara), nameof(Chara.TryEquip))]
    private static bool OnEquip(Chara __instance)
    {
        return !IsShown(__instance);
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(Chara), nameof(Chara.DropThing))]
    private static bool OnDrop(Chara __instance)
    {
        return !IsShown(__instance);
    }
}
