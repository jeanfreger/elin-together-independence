using ElinTogether.Common;
using ElinTogether.Helper;
using System.Linq;
using ElinTogether.LangMod;
using ElinTogether.Models;
using ElinTogether.Net;
using HeathenEngineering.SteamworksIntegration;
using UnityEngine;
using YKF;

namespace ElinTogether.Components;

internal class TabSessionInfo : TabEmpBase
{
    public override void OnLayout()
    {
        BuildOverviewSection();
        BuildPlayerList();
        BuildTeams();
    }

    /// <summary>
    ///     The teams (host options "Teams pay their own land" and "Teams keep their own story journal"): one line
    ///     each, the same in every game
    /// </summary>
    private void BuildTeams()
    {
        if (!NetSession.Instance.Rules.UseTeams || (!TeamBills.On && !TeamStory.On)) {
            return;
        }

        var card = this.MakeCard();
        card.HeaderSmall("emp_ui_teams_title".lang());
        foreach (var team in Teams.Ids()) {
            var line = Teams.NameOf(team);
            if (TeamBills.On) {
                line += "  " + "emp_ui_team_bills".Loc(TeamBills.UnpaidOf(team));
            }
            if (TeamStory.On) {
                line += "  " + "emp_ui_team_story".Loc(TeamStory.ChapterOf(team));
            }
            card.Text(line);
        }
    }

    private void BuildOverviewSection()
    {
        // a friend sees the rules of the host's world, read only, as the host last sent them (council 2026-10-23)
        if (NetSession.Instance.Transport is not ElinNetClient) {
            return;
        }

        var card = this.MakeCard();
        card.HeaderSmall("emp_ui_rules_title".lang());
        card.TextSmall("emp_ui_rules_note".lang());
        foreach (var (label, value) in SettingsTable.RuleLines(SettingsTable.FromRules(NetSession.Instance.Rules))) {
            card.TextSmall("emp_ui_rules_line".Loc(label, value));
        }
        if (SettingsTable.LastNotice is { } notice) {
            card.TextSmall(notice);
        }
    }

    private void BuildPlayerList()
    {
        var card = this.MakeCard();
        card.HeaderSmall("emp_ui_connected_players".lang());

        foreach (var player in NetSession.Instance.CurrentPlayers) {
            BuildPlayerLine(card, player);
        }

        BuildBannedList();
        BuildSavedCharacters();
    }

    /// <summary>
    ///     The host: the characters players made in this world, each with Delete when nobody plays it (upstream
    ///     1f932a8: a character left by someone who will not come back stayed in the world for good)
    /// </summary>
    private void BuildSavedCharacters()
    {
        if (NetSession.Instance.Transport is not ElinNetHost { IsZoneSession: false } host ||
            host.SavedCharacters() is not { Count: > 0 } saved) {
            return;
        }

        var card = this.MakeCard();
        card.HeaderSmall("emp_ui_saved_chars_title".lang());
        card.TextSmall("emp_ui_saved_chars_desc".lang());
        foreach (var (user, chara, playing) in saved.OrderBy(s => s.User)) {
            var account = UserData.Get(user).Name is { Length: > 0 } name ? name : user.ToString();
            card.Text("emp_ui_saved_chars_line".Loc(account, chara.Name, chara.LV) + (chara.isDead ? " †" : ""));
            if (playing) {
                card.TextSmall("emp_ui_saved_chars_playing".lang());
                continue;
            }

            var target = chara;
            card.ButtonRow().RowButton("emp_ui_saved_chars_delete".lang(), () => {
                Dialog.YesNo("emp_ui_saved_chars_confirm".Loc(target.Name, account), () => {
                    host.HostDeletes(user, target.uid);
                    LayerElinTogether.Instance?.Reopen();
                });
            });
        }
    }

    /// <summary>
    ///     The host: who is banned from this world, each with Unban (admin tools, council 2026-10-27)
    /// </summary>
    private void BuildBannedList()
    {
        if (NetSession.Instance.Transport is not ElinNetHost { IsZoneSession: false } || ElinNetHost.Bans.Count == 0) {
            return;
        }

        var card = this.MakeCard();
        card.HeaderSmall("emp_ui_banned_title".lang());
        foreach (var (user, why) in ElinNetHost.Bans.ToList()) {
            card.Text(why.Length > 0 ? $"{user}  ({why})" : user.ToString());
            card.ButtonRow().RowButton("emp_ui_unban".lang(), () => {
                ElinNetHost.Unban(user);
                LayerElinTogether.Instance?.Reopen();
            });
        }
    }

    /// <summary>
    ///     One line per player (council 2026-10-26): name, ping (or the host marker), and under it that player's own
    ///     buttons
    /// </summary>
    private void BuildPlayerLine(YKLayout parent, NetPeerState player)
    {
        var chara = player.FindChara();
        if (chara is null) {
            parent.Text("emp_ui_invalid_chara".Loc(player.Index, player.CharaUid));
            return;
        }

        var label = player.User.Name.TagColor(PeerColorizer.GetColor(player.Index)) + "  " + chara.Name;
        if (NetSession.Instance.Rules.UseTeams) {
            label += "  " + Teams.NameOf(Teams.TeamOf(chara.uid));
        }
        // (the ping text of index 0 is the host marker: "Ping - (host)")
        label += "  " + BuildPingStat(player);
        // away from the keyboard: time no longer waits for it (council 2026-10-26), said here only, no popup
        if (player.Idle) {
            label += "  " + "emp_ui_player_idle".lang();
        }
        if (ModerationDelta.AdminIds.Contains(player.User)) {
            label += "  " + "emp_ui_admin_tag".lang();
        }
        parent.Text(label);

        // Kick: the host, on a guest's line. Reconnect: a guest's own line only, or the host on a guest's line
        // (IsHost also reads true on an away client, and reconnecting would drop its zone)
        if (NetSession.Instance.IsAway) {
            return;
        }
        var isHost = NetSession.Instance.Transport is ElinNetHost;
        if (isHost ^ player.User.IsMe) {
            parent.ButtonRow().RowButton("emp_ui_reconnect".lang(), () => ReconnectPlayer(player.Index));
        }

        // moderation: the host and the admins, on the line of a player who is neither the host nor an admin
        ulong target = player.User;
        var self = NetSession.Instance.Self;
        ulong me = self?.User ?? UserData.Me;
        var mine = self is not null ? player.Index == self.Index : player.User.IsMe;
        if (player.Index == 0 || mine || ModerationDelta.AdminIds.Contains(target) ||
            !(isHost || ModerationDelta.AdminIds.Contains(me))) {
            return;
        }

        var row = parent.ButtonRow();
        row.RowButton("emp_ui_kick".lang(), () => Order("kick", target, player.Index));
        row.RowButton((ModerationDelta.IsMuted(target) ? "emp_ui_unmute" : "emp_ui_mute").lang(),
            () => Order(ModerationDelta.IsMuted(target) ? "unmute" : "mute", target, player.Index));
        row.RowButton("emp_ui_ban".lang(), () => Order("ban", target, player.Index));
        if (isHost) {
            row.RowButton("emp_ui_make_admin".lang(), () => {
                (NetSession.Instance.Transport as ElinNetHost)?.MakeAdmin(target, true);
                LayerElinTogether.Instance?.Reopen();
            });
        }
    }

    /// <summary>
    ///     The host does it; an admin asks the host, which checks it (AdminCommandDelta)
    /// </summary>
    private static void Order(string verb, ulong target, int peerIndex)
    {
        switch (NetSession.Instance.Transport) {
            case ElinNetHost host:
                switch (verb) {
                    case "kick":
                        host.Kick(peerIndex);
                        break;
                    case "ban":
                        host.Ban(target);
                        break;
                    case "mute":
                        host.Mute(target);
                        break;
                    case "unmute":
                        host.Unmute(target);
                        break;
                }

                break;
            case ElinNetClient client:
                client.Delta.AddRemote(new AdminCommandDelta { Verb = verb, Target = target });
                break;
        }

        LayerElinTogether.Instance?.Reopen();
    }

    private static void ReconnectPlayer(int peerIndex)
    {
        switch (NetSession.Instance.Transport) {
            case ElinNetHost host:
                host.RequestClientReconnect(peerIndex);
                break;
            case ElinNetClient client:
                client.ReconnectSelf();
                break;
        }
    }

    private static string BuildPingStat(NetPeerState player)
    {
        if (player.Index == 0) {
            return "emp_ui_ping_host".lang();
        }

        var ping = player.AvgPingMs > 0 ? player.AvgPingMs : player.LastPingMs;
        // plain digits: ToString("P0") adds a culture-specific space and percent sign that drew as a missing glyph
        var quality = player.ConnectionQualityLocal > 0
            ? "emp_ui_quality_format".Loc((player.ConnectionQualityLocal * 100f).ToString("F0"))
            : "";

        return "emp_ui_ping_format".Loc(ping.ToString("F0"), quality);
    }
}