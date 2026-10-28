using System;
using System.Collections.Generic;
using System.Linq;
using ElinTogether.Common;
using ElinTogether.Helper;
using ElinTogether.LangMod;
using ElinTogether.Models;
using ElinTogether.Net;
using HeathenEngineering.SteamworksIntegration;
using UnityEngine;
using YKF;

namespace ElinTogether.Components;

/// <summary>
///     The Players page of the hub (dev/PLAN_multiplayer_hub.md step 4), the same for every player: a row per
///     player, one "..." menu with the same entries for everyone (admin entries only when permitted, as the Session
///     tab had them), then the folded host lists (saved characters, bans)
/// </summary>
internal class TabPlayers : TabEmpBase
{
    public const string TabName = "emp_tab_players";

    // the folded sections the player opened stay open when the page is rebuilt
    private static readonly HashSet<string> Opened = [];

    public override void OnLayout()
    {
        BuildPlayerList();
        BuildSavedCharacters();
        BuildBannedList();
    }

    private void BuildPlayerList()
    {
        var players = NetSession.Instance.CurrentPlayers.ToList();
        var card = this.Card("emp_ui_players_title".Loc(players.Count), EmpUi.Icon("icon_LayerPeople"));
        for (var i = 0; i < players.Count; i++) {
            if (i > 0) {
                card.Divider();
            }

            BuildPlayerLine(card, players[i]);
        }

        // nobody else: say so, and the one click that helps (invite when in a game, else the Play page)
        if (players.Count <= 1) {
            if (NetSession.Instance.HasActiveConnection) {
                card.EmptyState("emp_ui_players_alone".lang(), "emp_ui_players_alone_btn".lang(),
                    NetSession.Instance.Lobby.InviteSteamOverlay);
            } else {
                card.EmptyState("emp_ui_players_alone".lang(), "emp_ui_players_go_play".lang(),
                    () => LayerElinTogether.Instance?.Window.SwitchContent(TabPlay.TabName));
            }
        }
    }

    /// <summary>
    ///     A folded card: its title on a button, its body shown when opened
    /// </summary>
    private YKVertical Fold(string id, string title, out YKVertical body)
    {
        var card = this.MakeCard();
        UIButton? head = null;
        var inner = card.Vertical();
        inner.Fitter.verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
        inner.Layout.spacing = UIHelper.RowSpacing * 2f;
        string Label() => (Opened.Contains(id) ? "v " : "> ") + title;
        head = card.Button(Label(), () => {
            if (!Opened.Add(id)) {
                Opened.Remove(id);
            }

            head!.mainText.text = Label();
            inner.gameObject.SetActive(Opened.Contains(id));
            Relayout();
        });
        // (the button was added after the body: it goes first)
        head.transform.SetAsFirstSibling();
        head.LayoutElement().flexibleWidth = 1f;
        inner.gameObject.SetActive(Opened.Contains(id));
        body = inner;
        return card;
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

        Fold("saved", "emp_ui_saved_chars_title".lang(), out var body);
        body.TextSmall("emp_ui_saved_chars_desc".lang(), FontColor.Gray);
        foreach (var (user, chara, playing) in saved.OrderBy(s => s.User)) {
            var account = UserData.Get(user).Name is { Length: > 0 } name ? name : user.ToString();
            var row = body.Row();
            row.TextColumn("emp_ui_saved_chars_line".Loc(account, chara.Name, chara.LV) + (chara.isDead ? " †" : ""),
                playing ? "emp_ui_saved_chars_playing".lang() : null);
            if (playing) {
                continue;
            }

            var target = chara;
            row.RowButton("emp_ui_saved_chars_delete".lang(), () => {
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

        Fold("banned", "emp_ui_banned_title".lang(), out var body);
        foreach (var (user, why) in ElinNetHost.Bans.ToList()) {
            var row = body.Row();
            row.Text(why.Length > 0 ? $"{user}  ({why})" : user.ToString()).LayoutElement().flexibleWidth = 1f;
            row.RowButton("emp_ui_unban".lang(), () => {
                ElinNetHost.Unban(user);
                LayerElinTogether.Instance?.Reopen();
            });
        }
    }

    /// <summary>
    ///     One row per player (council 2026-10-26): portrait, name, character and level, team, ping, badges, and the
    ///     "..." menu
    /// </summary>
    private static void BuildPlayerLine(YKLayout parent, NetPeerState player)
    {
        var chara = player.FindChara();
        if (chara is null) {
            parent.Text("emp_ui_invalid_chara".Loc(player.Index, player.CharaUid));
            return;
        }

        // (a player whose Steam name is empty is shown by the name of the character)
        var name = (string.IsNullOrWhiteSpace(player.User.Name) ? chara.Name : player.User.Name).TagColor(PeerColorizer.GetColor(player.Index));
        if (player.User.IsMe) {
            name += "  " + "emp_ui_player_you".lang();
        }

        var detail = "emp_ui_player_detail".Loc(chara.Name, chara.LV);
        if (player.Index == 0) {
            detail = "emp_ui_player_host".lang() + "  " + detail;
        }

        var badges = PlayerBadge.None;
        // away from the keyboard: time no longer waits for it (council 2026-10-26), said here only, no popup
        if (player.Idle) {
            badges |= PlayerBadge.Idle;
        }
        if (ModerationDelta.AdminIds.Contains(player.User)) {
            badges |= PlayerBadge.Admin;
        } else if (ModerationDelta.ModeratorIds.Contains(player.User)) {
            badges |= PlayerBadge.Moderator;
        }
        if (ModerationDelta.FrozenIds.Contains(player.User)) {
            badges |= PlayerBadge.Frozen;
        }

        // (the host's own line has no ping: index 0)
        var ping = player.Index == 0 ? -1 : EmpUi.PingBarsFor(player.AvgPingMs > 0 ? player.AvgPingMs : player.LastPingMs);
        var team = NetSession.Instance.Rules.UseTeams ? Teams.NameOf(Teams.TeamOf(chara.uid)) : null;
        parent.PlayerRow(new(name, detail, chara, team, ping, badges), ActionsFor(player, chara));
    }

    /// <summary>
    ///     The "..." entries of a player's row: the same for everyone, the admin ones only when permitted
    /// </summary>
    private static List<(string Label, Action Act)> ActionsFor(NetPeerState player, Chara chara)
    {
        var actions = new List<(string, Action)>();
        // away: the connection is being rebuilt, nothing to do to anyone
        if (NetSession.Instance.IsAway) {
            return actions;
        }

        var isHost = NetSession.Instance.Transport is ElinNetHost;
        // Reconnect: a guest's own line only, or the host on a guest's line
        // (IsHost also reads true on an away client, and reconnecting would drop its zone)
        if (isHost ^ player.User.IsMe) {
            actions.Add(("emp_ui_reconnect".lang(), () => ReconnectPlayer(player.Index)));
        }

        // what a player may do to another, with the host's switches (the same entries as in the game's own menu)
        if (!player.User.IsMe) {
            var other = player.User;
            var otherName = other.Name;
            var here = chara.IsAliveInCurrentZone;
            if (PlayerWhisper.Enabled) {
                actions.Add(("emp_ui_act_whisper".lang(), () => PlayerWhisper.Open(chara)));
            }
            if (PlayerTrade.Enabled && here) {
                actions.Add(("emp_act_trade".lang(), () => PlayerTrade.Invite(chara.uid)));
            }
            if (PlayerDuel.Enabled && here && !PlayerDuel.InDuel) {
                actions.Add(("emp_act_duel".lang(), () => PlayerDuel.Challenge(chara.uid)));
            }

            actions.Add(((PlayerSocial.IsFriend(other) ? "emp_ui_unfriend" : "emp_ui_friend").lang(), () => {
                PlayerSocial.SetFriend(other, otherName, !PlayerSocial.IsFriend(other));
                LayerElinTogether.Instance?.Reopen();
            }));
            actions.Add(((PlayerSocial.IsIgnored(other) ? "emp_ui_unignore" : "emp_ui_ignore").lang(), () => {
                PlayerSocial.SetIgnored(other, otherName, !PlayerSocial.IsIgnored(other));
                LayerElinTogether.Instance?.Reopen();
            }));

            // a leader's invitation, as the Team page offers it (the host checks the same again)
            if (EClass.pc is { } pc && Teams.LedBy(pc.uid) != Teams.Wanderers && Teams.TeamOf(chara.uid) != Teams.TeamOf(pc.uid) &&
                Teams.InviteOf(chara.uid) == Teams.Wanderers) {
                var uid = chara.uid;
                actions.Add(("emp_ui_team_invite".Loc(Teams.PlayerName(uid)), () => Teams.Ask(TeamAsk.Invite, uid)));
            }
        }

        AddAdminActions(actions, player);
        return actions;
    }

    /// <summary>
    ///     Moderation: the host and the admins, on the line of a player who is neither the host nor an admin
    /// </summary>
    private static void AddAdminActions(List<(string, Action)> actions, NetPeerState player)
    {
        var isHost = NetSession.Instance.Transport is ElinNetHost;
        ulong target = player.User;
        var self = NetSession.Instance.Self;
        ulong me = self?.User ?? UserData.Me;
        var mine = self is not null ? player.Index == self.Index : player.User.IsMe;
        var admin = isHost || ModerationDelta.AdminIds.Contains(me);
        var extras = isHost ? ElinNetHost.AdminExtrasOn : NetSession.Instance.Rules.AdminExtras;
        var moderator = !admin && extras && ModerationDelta.ModeratorIds.Contains(me);
        if (mine && admin) {
            if (extras) {
                actions.Add(("emp_ui_set_jail".lang(), () => Order("setjail", 0, player.Index)));
            }
            if (ContainerLog.On) {
                actions.Add(("emp_ui_container_logs".lang(), () => ContainerLog.Ask(0)));
            }
        }

        if (player.Index == 0 || mine || ModerationDelta.AdminIds.Contains(target) || !(admin || moderator) ||
            (moderator && ModerationDelta.ModeratorIds.Contains(target))) {
            return;
        }

        actions.Add(("emp_ui_kick".lang(), () => Order("kick", target, player.Index)));
        actions.Add(((ModerationDelta.IsMuted(target) ? "emp_ui_unmute" : "emp_ui_mute").lang(),
            () => Order(ModerationDelta.IsMuted(target) ? "unmute" : "mute", target, player.Index)));
        if (!admin) {
            return;
        }

        actions.Add(("emp_ui_ban".lang(), () => Order("ban", target, player.Index)));
        if (isHost) {
            actions.Add(("emp_ui_make_admin".lang(), () => {
                (NetSession.Instance.Transport as ElinNetHost)?.MakeAdmin(target, true);
                LayerElinTogether.Instance?.Reopen();
            }));
        }

        if (!extras) {
            return;
        }

        var held = ModerationDelta.FrozenIds.Contains(target);
        actions.Add(((held ? "emp_ui_unfreeze" : "emp_ui_freeze").lang(), () => Order(held ? "unfreeze" : "freeze", target, player.Index)));
        actions.Add(("emp_ui_jail".lang(), () => Order("jail", target, player.Index)));
        if (isHost) {
            var mod = ModerationDelta.ModeratorIds.Contains(target);
            actions.Add(((mod ? "emp_ui_unmake_mod" : "emp_ui_make_mod").lang(), () => {
                (NetSession.Instance.Transport as ElinNetHost)?.MakeModerator(target, !mod);
                LayerElinTogether.Instance?.Reopen();
            }));
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
                    case "freeze":
                        host.Freeze(target);
                        break;
                    case "jail":
                        host.Freeze(target, true);
                        break;
                    case "unfreeze":
                        host.Release(target);
                        break;
                    case "setjail":
                        host.SetJail(EClass._zone);
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
}
