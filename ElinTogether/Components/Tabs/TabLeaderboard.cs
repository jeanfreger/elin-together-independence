using System;
using System.Collections.Generic;
using ElinTogether.Helper;
using ElinTogether.LangMod;
using ElinTogether.Models;
using ElinTogether.Net;
using UnityEngine;
using YKF;

namespace ElinTogether.Components;

/// <summary>
///     The Leaderboards page (PLAN_hub_features D): four boards the host works out (level, deepest floor, duels won,
///     team wealth), the top ten of each and the player's own rank under it. A board is asked from the host when its
///     sub-tab opens
/// </summary>
internal class TabLeaderboard : TabEmpBase
{
    public const string TabName = "emp_tab_leaderboard";

    private static readonly string[] Icons = ["icon_medal", "icon_influence"];
    private static readonly string[] Keys = ["emp_ui_lb_level", "emp_ui_lb_deepest", "emp_ui_lb_duels", "emp_ui_lb_wealth"];

    private readonly YKVertical?[] _lists = new YKVertical?[Leaderboards.BoardCount];

    public override void OnLayout()
    {
        var card = this.Card("emp_ui_tab_leaderboard".lang());
        if (NetSession.Instance is not { HasActiveConnection: true, Rules.Leaderboards: true }) {
            TabTeam.EmptyPage(card, Icons, "emp_ui_leaderboard_off".lang());
            return;
        }

        var pages = new List<(string, Action<YKVertical>)>();
        for (var i = 0; i < Leaderboards.BoardCount; i++) {
            var board = i;
            pages.Add((Keys[board].lang(), page => _lists[board] = page));
        }

        SubTabs("leaderboard", pages);
        Leaderboards.Changed = Redraw;
        for (var i = 0; i < Leaderboards.BoardCount; i++) {
            Redraw(i);
        }

        Leaderboards.Ask(SubTab);
    }

    protected override void OnSubTabShown(int index)
    {
        if (NetSession.Instance is { HasActiveConnection: true, Rules.Leaderboards: true }) {
            Leaderboards.Ask(index);
        }
    }

    private void OnDestroy()
    {
        Leaderboards.Changed = null;
    }

    private void Redraw(int board)
    {
        if (_lists[board] is not { } page) {
            return;
        }

        foreach (Transform child in page.transform) {
            UnityEngine.Object.Destroy(child.gameObject);
        }

        var answer = Leaderboards.Of(board);
        if (answer is null || answer.Names.Length == 0) {
            page.Text("emp_ui_leaderboard_empty".lang(), FontColor.Gray);
        } else {
            for (var i = 0; i < answer.Names.Length; i++) {
                var uid = answer.Uids.Length > i ? answer.Uids[i] : 0;
                var mine = answer.OwnRank == i + 1;
                var row = page.Row();
                row.Text($"#{i + 1}").LayoutElement().minWidth = 48f;
                if (board != Leaderboards.Wealth) {
                    row.Portrait(Teams.FindChara(uid));
                }

                row.Text(answer.Names[i], mine ? FontColor.Good : FontColor.Default).LayoutElement().flexibleWidth = 1f;
                row.Text(answer.Scores[i].ToString("N0"), mine ? FontColor.Good : FontColor.Default);
            }

            page.Divider();
            page.KeyValue("emp_ui_lb_you".lang(),
                answer.OwnRank > 0 ? "emp_ui_lb_rank".Loc(answer.OwnRank, answer.OwnScore.ToString("N0")) : "emp_ui_lb_unranked".lang());
        }

        Relayout();
    }
}
