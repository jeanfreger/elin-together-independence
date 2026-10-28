using System.Linq;
using ElinTogether.Common;
using ElinTogether.Helper;
using ElinTogether.LangMod;
using ElinTogether.Net;
using ElinTogether.Net.Steam;
using HeathenEngineering.SteamworksIntegration;
using UnityEngine;
using UnityEngine.UI;
using YKF;

namespace ElinTogether.Components;

/// <summary>
///     The Play page of the hub (dev/PLAN_multiplayer_hub.md step 4), the same for every player: in a game, who it
///     is with and how to invite or leave; otherwise three cards (host, join, public servers), one click each. Holds
///     what the Lobby and Session tabs did (the lobby list, the depot, Start Server, Invite, Leave)
/// </summary>
internal class TabPlay : TabEmpBase
{
    public const string TabName = "emp_tab_play";

    public override void OnLayout()
    {
        if (EClass.core.IsGameStarted && NetSession.Instance.Transport != null) {
            BuildConnected();
        } else {
            BuildHost();
            BuildJoin();
            BuildPublicServers();
        }

#if DEBUG
        if (EClass.core.IsGameStarted) {
            BuildBotButtons(this.Card("Debug"));
        }
#endif
    }

    // ---------------------------------------------------------------- connected

    private void BuildConnected()
    {
        var net = NetSession.Instance;
        var players = net.CurrentPlayers.ToList();
        var host = players.FirstOrDefault(p => p.Index == 0)?.User.Name;
        var isHost = net.Transport is ElinNetHost;
        var title = isHost || string.IsNullOrEmpty(host) ? "emp_ui_play_connected".lang() : "emp_ui_play_world".Loc(host!);
        var card = this.Card(title, EmpUi.Icon("icon_LayerTravel"));

        var status = card.Horizontal();
        status.Layout.childForceExpandWidth = false;
        status.Layout.childAlignment = TextAnchor.MiddleLeft;
        status.Layout.spacing = 8f;
        StatusDot(status, net.IsAway ? new(0.85f, 0.62f, 0.2f) : new(0.45f, 0.68f, 0.32f));
        status.Text((net.IsAway ? "emp_ui_play_reconnecting" : "emp_ui_play_connected").lang());

        if (!string.IsNullOrEmpty(host)) {
            card.TextSmall("emp_ui_play_host_line".Loc(host!), FontColor.Gray);
        }
        card.TextSmall("emp_ui_play_players_line".Loc(players.Count), FontColor.Gray);
        if (EClass._zone is { } zone) {
            card.TextSmall("emp_ui_play_where".Loc(zone.NameWithLevel), FontColor.Gray);
        }

        var buttons = card.ButtonRow();
        buttons.RowButton("emp_ui_sv_invite".lang(), net.Lobby.InviteSteamOverlay);
        buttons.RowButton("emp_ui_sv_dc".lang(), DisconnectFromPanel);

        BuildRulesNote();
    }

    /// <summary>
    ///     A friend sees the rules of the host's world, read only, as the host last sent them (council 2026-10-23)
    /// </summary>
    private void BuildRulesNote()
    {
        if (NetSession.Instance.Transport is not ElinNetClient) {
            return;
        }

        var card = this.Card("emp_ui_rules_title".lang());
        card.TextSmall("emp_ui_rules_note".lang(), FontColor.Gray);
        foreach (var (label, value) in SettingsTable.RuleLines(SettingsTable.FromRules(NetSession.Instance.Rules))) {
            card.TextSmall("emp_ui_rules_line".Loc(label, value));
        }
        if (SettingsTable.LastNotice is { } notice) {
            card.TextSmall(notice);
        }
    }

    /// <summary>
    ///     A small lozenge in the colour of the state (the mark the settings page uses for "changed")
    /// </summary>
    private static void StatusDot(YKLayout row, Color color)
    {
        var go = new GameObject("StatusDot", typeof(RectTransform), typeof(LayoutElement));
        go.transform.SetParent(row.transform, false);
        var element = go.GetComponent<LayoutElement>();
        element.minWidth = element.preferredWidth = 18f;
        element.minHeight = element.preferredHeight = 18f;
        var dot = new GameObject("Dot", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        dot.transform.SetParent(go.transform, false);
        var image = dot.GetComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        var rt = (RectTransform)dot.transform;
        rt.anchorMin = rt.anchorMax = new(0.5f, 0.5f);
        rt.sizeDelta = new(10f, 10f);
        rt.localRotation = Quaternion.Euler(0f, 0f, 45f);
    }

    private static void DisconnectFromPanel()
    {
        var isClient = NetSession.Instance.Transport is ElinNetClient;

        // (a host whose guests would take the world over gives them a last save first)
        ElinNetHost.LeaveWithLastCopy(true, () => {
            NetSession.Instance.ResetSession();
            LayerElinTogether.Instance?.Reopen();

            if (isClient) {
                EMono.scene.Init(Scene.Mode.Title);
            }
        });
    }

    // ---------------------------------------------------------------- not connected

    private void BuildHost()
    {
        var card = this.Card("emp_ui_play_host_title".lang(), EmpUi.Icon("icon_LayerTravel"));
        if (!EClass.core.IsGameStarted) {
            // the world kept in the depot: whoever comes first takes it and hosts it
            if (SaveDepot.Enabled) {
                card.TextSmall("emp_ui_play_host_depot".lang(), FontColor.Gray);
                // held by another player whose lock says where: the same click joins their game
                var label = SaveDepot.HeldBy() is not { } who ? "emp_ui_depot_take".lang() :
                    (SaveDepot.Joinable ? "emp_ui_depot_join_btn" : "emp_ui_depot_held_btn").Loc(who);
                card.ButtonRow().RowButton(label, () => {
                    LayerElinTogether.Instance?.Close();
                    SaveDepot.Take();
                });
            } else {
                card.TextSmall("emp_ui_play_host_title_screen".lang(), FontColor.Gray);
            }

            return;
        }

        card.TextSmall("emp_ui_play_host_desc".lang(), FontColor.Gray);
        var buttons = card.ButtonRow();
        buttons.RowButton("emp_ui_sv_start".lang(), StartServerFromPanel);
        if (SaveDepot.Enabled && Game.id != SaveDepot.WorldId) {
            buttons.RowButton("emp_ui_depot_put".lang(), SaveDepot.Put);
        }

        // where "Start Server" fails
        if (EClass.player.chara?.homeBranch?.owner is null) {
            card.TextSmall("emp_ui_unclaimed_zone".lang());
        }
    }

    private void BuildJoin()
    {
        var card = this.Card("emp_ui_play_join_title".lang(), EmpUi.Icon("icon_goto"));
        if (EClass.core.IsGameStarted) {
            // (joining leaves this game: from the title screen only, as before)
            card.TextSmall("emp_ui_play_join_ingame".lang(), FontColor.Gray);
            return;
        }

        card.TextSmall("emp_ui_play_join_desc".lang(), FontColor.Gray);
        // a server: a game nobody plays, started with -empserver, joined by its address
        card.ButtonRow().RowButton("emp_ui_join_address".lang(), () => {
            var last = EmpConfig.Client.ServerAddress;
            Dialog.InputName("emp_ui_join_address_ask", last.Value.Length > 0 ? last.Value : "127.0.0.1:55556", (cancel, text) => {
                if (cancel || text.Trim().Length == 0) {
                    return;
                }

                last.Value = text.Trim();
                LayerElinTogether.Instance?.Close();
                JoinAddress(last.Value);
            }).input.field.characterLimit = 0;
        });
    }

    /// <summary>
    ///     One address for both kinds of server: Elin Together Server without a game (its world is taken and
    ///     hosted here), or a game started with -empserver (joined)
    /// </summary>
    internal static void JoinAddress(string address)
    {
        if (!SaveDepot.TakeFrom(address)) {
            NetSession.Instance.InitializeComponent<ElinNetClient>().ConnectAddress(address);
        }
    }

#if DEBUG
    /// <summary>
    ///     A second game on this machine, which joins and plays by itself
    /// </summary>
    private static void BuildBotButtons(YKVertical card)
    {
        if (NetSession.Instance.Transport is ElinNetClient) {
            return;
        }

        var botGroup = card.ButtonRow();

        botGroup.RowButton("emp_ui_bot_add".lang(), () => {
            EmpBotLauncher.Launch();
            LayerElinTogether.Instance?.Reopen();
        });

        var running = EmpBotLauncher.Running;
        if (running > 0) {
            botGroup.RowButton("emp_ui_bot_stop".Loc(running), () => {
                EmpBotLauncher.StopAll();
                LayerElinTogether.Instance?.Reopen();
            });
        }

        card.Toggle("emp_ui_bot_all_actions", EmpConfig.Dev.BotAllActions.Value,
                value => EmpConfig.Dev.BotAllActions.Value = value).AsRow()
            .SetTooltipLang(EmpConfig.Dev.BotAllActions.Description.Description);
    }
#endif

    private void BuildPublicServers()
    {
        var card = this.Card("emp_ui_play_public_title".lang(), EmpUi.Icon("icon_zones"));
        var status = card.TextSmall("emp_ui_play_searching".lang(), FontColor.Gray);
        var list = card.Vertical();
        list.Fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        list.Layout.spacing = UIHelper.RowSpacing * 2f;

        NetSession.Instance.Lobby.GetOnlineLobbies(SetupLobbyDisplay);

        return;

        void SetupLobbyDisplay(LobbyData[] lobbies)
        {
            if (this == null) {
                return;
            }

            // the project's own public game first (user, 2026-10-27), checked by its signature, not by its name
            var sorted = lobbies.Select(l => (Lobby: l, Official: OfficialServer.IsOfficial(l)))
                .OrderByDescending(l => l.Official).ToArray();
            var total = sorted.Sum(l => l.Lobby.MemberCount);
            status.text = sorted.Length == 0 ? "emp_ui_play_none".lang() : "emp_ui_lobby_tally".Loc(total);

            // on the title screen the official server always has its place, even closed: a quiet line, never a dialog
            if (!EClass.core.IsGameStarted && !sorted.Any(l => l.Official)) {
                list.TextSmall("emp_ui_official_closed".lang(), FontColor.Gray);
            }

            for (var i = 0; i < sorted.Length; i++) {
                if (i > 0) {
                    list.Divider();
                }

                BuildLobbyEntry(list, sorted[i].Lobby, sorted[i].Official);
            }

            // (these lines come after the layer was laid out, see LayerElinTogether.OnLayout)
            UIHelper.WrapTexts(this);
            this.RebuildLayout(true);
        }
    }

    /// <summary>
    ///     One game: its name (the official one with its badge), who may come in, what it runs with, and Join (from the
    ///     title screen: one click joins that game; in a game, the player leaves it first)
    /// </summary>
    private static void BuildLobbyEntry(YKLayout parent, LobbyData lobby, bool isOfficial)
    {
        var game = parent.RowGroup();
        if (isOfficial) {
            game.Chip("emp_ui_lobby_official".lang(), FontColor.Good);
        }

        game.Text("emp_ui_lobby_desc".Loc(lobby.Name, lobby.GameVersion, lobby.MemberCount, lobby[EmpLobbyData.CurrentZone]));
        // who may come in, said before trying (a lobby of an older version says nothing)
        if (isOfficial || lobby[EmpLobbyData.Public] == "1") {
            game.TextSmall("emp_ui_lobby_public".lang(), FontColor.Gray);
        } else if (lobby[EmpLobbyData.Public] == "0") {
            game.TextSmall("emp_ui_lobby_friends".lang(), FontColor.Gray);
        }

        // what that game runs with, when its host says so: known before joining
        if (ModList.Summary(lobby[EmpLobbyData.Mods]) is { } mods) {
            game.TextSmall(mods.Missing > 0
                ? "emp_ui_mods_lobby_missing".Loc(mods.Count, mods.Missing)
                : "emp_ui_mods_lobby".Loc(mods.Count), FontColor.Gray);
        }

        if (!EClass.core.IsGameStarted) {
            var target = lobby;
            game.ButtonRow().RowButton(isOfficial ? "emp_ui_lobby_official_join".lang() : "emp_ui_depot_join_btn".Loc(lobby.Name), () => {
                LayerElinTogether.Instance?.Close();
                NetSession.Instance.Lobby.ConnectLobby(target);
            });
        }
    }

    private static void StartServerFromPanel()
    {
        // (with the box "hidden server", the world starts in a second, hidden game and this one joins it)
        if (HiddenServer.TryStart(() => NetSession.Instance.InitializeComponent<ElinNetHost>().StartServer())) {
            LayerElinTogether.Instance?.Close();
            return;
        }

        NetSession.Instance.InitializeComponent<ElinNetHost>().StartServer();
        LayerElinTogether.Instance?.Reopen();
    }
}
