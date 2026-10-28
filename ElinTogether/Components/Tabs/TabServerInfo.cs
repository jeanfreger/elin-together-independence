using System.Linq;
using ElinTogether.Helper;
using ElinTogether.LangMod;
using ElinTogether.Models;
using ElinTogether.Net;
using HeathenEngineering.SteamworksIntegration;
using YKF;

namespace ElinTogether.Components;

/// <summary>
///     The "Server" page (council 2026-10-28, gaps 1, 2 and 10): the host's message of the day and rules, the player's
///     own friends (online ones first) and ignore lists, and for the host and the admins a line to everyone and a
///     restart warning. The texts reach every game with the world's rules
/// </summary>
internal class TabServerInfo : TabEmpBase
{
    public override void OnLayout()
    {
        BuildTexts();
        BuildAnnounce();
        BuildFriends();
        BuildIgnored();
    }

    private static bool IsHost => NetSession.Instance.Transport is ElinNetHost { IsZoneSession: false };

    private static bool IsAdmin => IsHost || (NetSession.Instance.Transport is ElinNetClient &&
                                              ModerationDelta.AdminIds.Contains(NetSession.Instance.Self?.User ?? UserData.Me));

    private void BuildTexts()
    {
        var card = this.MakeCard();
        card.HeaderSmall("emp_info_motd".lang());
        Show(card, ServerInfo.Motd);
        var rules = this.MakeCard();
        rules.HeaderSmall("emp_info_rules".lang());
        Show(rules, ServerInfo.RulesText);

        if (!IsHost) {
            return;
        }

        var buttons = rules.ButtonRow();
        buttons.RowButton("emp_info_edit_motd".lang(), () => Edit("motd.txt", "emp_info_edit_motd"));
        buttons.RowButton("emp_info_edit_rules".lang(), () => Edit("rules.txt", "emp_info_edit_rules"));
        buttons.RowButton("emp_info_reload".lang(), Publish);
        rules.TextSmall("emp_info_files".lang());
    }

    private static void Show(YKVertical card, string text)
    {
        if (text.Length == 0) {
            card.TextSmall("emp_info_none".lang());
            return;
        }

        foreach (var line in ServerInfo.Lines(text)) {
            card.TextSmall(line);
        }
    }

    /// <summary>
    ///     The game's own input box; a bar in the text is a line break
    /// </summary>
    private static void Edit(string file, string lang)
    {
        Dialog.InputName(lang, ServerInfo.ReadFile(file).Replace("\n", "|"), (cancel, text) => {
            if (cancel) {
                return;
            }

            ServerInfo.WriteFile(file, text);
            Publish();
        });
    }

    /// <summary>
    ///     The files as they are now go to every player with the rules
    /// </summary>
    private static void Publish()
    {
        (NetSession.Instance.Transport as ElinNetHost)?.UpdateRemoteSessionRules();
        LayerElinTogether.Instance?.Reopen();
    }

    private void BuildAnnounce()
    {
        if (!IsAdmin) {
            return;
        }

        var card = this.MakeCard();
        card.HeaderSmall("emp_info_announce".lang());
        var row = card.ButtonRow();
        row.RowButton("emp_info_say".lang(), () => Dialog.InputName("emp_info_say", "", (cancel, text) => {
            if (!cancel && text.Length > 0) {
                Order("say", text);
            }
        }));
        row.RowButton("emp_info_restart_10".lang(), () => Order("restart", "10"));
        row.RowButton("emp_info_restart_cancel_button".lang(), () => Order("restart_cancel", ""));
    }

    /// <summary>
    ///     The host does it; an admin asks the host, which checks it (AdminCommandDelta)
    /// </summary>
    private static void Order(string verb, string text)
    {
        switch (NetSession.Instance.Transport) {
            case ElinNetHost host:
                _ = verb switch {
                    "say" => host.Announce(text),
                    "restart" => host.WarnRestart(int.Parse(text)),
                    _ => host.CancelRestart(),
                };
                break;
            case ElinNetClient client:
                client.Delta.AddRemote(new AdminCommandDelta { Verb = verb, Target = 0, Reason = text });
                break;
        }
    }

    private void BuildFriends()
    {
        var card = this.MakeCard();
        card.HeaderSmall("emp_info_friends".lang());
        var online = NetSession.Instance.CurrentPlayers.Where(p => !p.User.IsMe && PlayerSocial.IsFriend(p.User)).ToList();
        foreach (var player in online) {
            card.Text("emp_info_friend_online".Loc(player.User.Name, player.FindChara()?.Name ?? "?"));
        }

        // the others are known by the name they had when they were added
        foreach (var (user, name) in PlayerSocial.FriendList.Where(f => online.All(p => (ulong)p.User != f.Key)).ToList()) {
            card.TextSmall("emp_info_friend_offline".Loc(name.IsEmpty(user.ToString())));
            card.ButtonRow().RowButton("emp_ui_unfriend".lang(), () => {
                PlayerSocial.SetFriend(user, name, false);
                LayerElinTogether.Instance?.Reopen();
            });
        }

        if (PlayerSocial.FriendList.Count == 0) {
            card.TextSmall("emp_info_friends_none".lang());
        }
    }

    private void BuildIgnored()
    {
        var card = this.MakeCard();
        card.HeaderSmall("emp_info_ignored".lang());
        card.TextSmall("emp_info_ignored_note".lang());
        foreach (var (user, name) in PlayerSocial.IgnoredList.ToList()) {
            card.Text(name.IsEmpty(user.ToString()));
            card.ButtonRow().RowButton("emp_ui_unignore".lang(), () => {
                PlayerSocial.SetIgnored(user, name, false);
                LayerElinTogether.Instance?.Reopen();
            });
        }
    }
}
