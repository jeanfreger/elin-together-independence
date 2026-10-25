using ElinTogether.Common;
using ElinTogether.Helper;
using ElinTogether.LangMod;
using ElinTogether.Net;
using HeathenEngineering.SteamworksIntegration;

namespace ElinTogether.Components;

internal class TabLobbyBrowser : TabEmpBase
{
    public override void OnLayout()
    {
        BuildNetButtons();
        BuildLobbyList();
    }

    private void BuildNetButtons()
    {
        var btnGroup = Horizontal();
        btnGroup.Layout.childForceExpandWidth = true;

        if (!EClass.core.IsGameStarted) {
            // the world kept in the depot: whoever comes first takes it and hosts it
            if (SaveDepot.Enabled) {
                // held by another player whose lock says where: the same click joins their game
                var label = SaveDepot.HeldBy() is not { } who ? "emp_ui_depot_take".lang() :
                    (SaveDepot.Joinable ? "emp_ui_depot_join_btn" : "emp_ui_depot_held_btn").Loc(who);
                btnGroup.Button(label, () => {
                    LayerElinTogether.Instance?.Close();
                    SaveDepot.Take();
                });
            }

            // a server: a game nobody plays, started with -empserver, joined by its address
            btnGroup.Button("emp_ui_join_address".lang(), () => {
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

            return;
        }

        if (NetSession.Instance.Transport == null) {
            btnGroup.Button("emp_ui_sv_start".lang(), StartServerFromPanel);
            if (SaveDepot.Enabled && Game.id != SaveDepot.WorldId) {
                btnGroup.Button("emp_ui_depot_put".lang(), SaveDepot.Put);
            }

            // where "Start Server" fails
            if (EClass.player.chara?.homeBranch?.owner is null) {
                Text("emp_ui_unclaimed_zone".lang());
            }
        } else {
            btnGroup.Button("emp_ui_sv_invite".lang(), NetSession.Instance.Lobby.InviteSteamOverlay);
            btnGroup.Button("emp_ui_sv_dc".lang(), DisconnectFromPanel);
        }

#if DEBUG
        BuildBotButtons();
#endif
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
    private void BuildBotButtons()
    {
        if (NetSession.Instance.Transport is ElinNetClient) {
            return;
        }

        var botGroup = Horizontal();
        botGroup.Layout.childForceExpandWidth = true;

        botGroup.Button("emp_ui_bot_add".lang(), () => {
            EmpBotLauncher.Launch();
            LayerElinTogether.Instance?.Reopen();
        });

        var running = EmpBotLauncher.Running;
        if (running > 0) {
            botGroup.Button("emp_ui_bot_stop".Loc(running), () => {
                EmpBotLauncher.StopAll();
                LayerElinTogether.Instance?.Reopen();
            });
        }

        Toggle("emp_ui_bot_all_actions", EmpConfig.Dev.BotAllActions.Value,
                value => EmpConfig.Dev.BotAllActions.Value = value)
            .SetTooltipLang(EmpConfig.Dev.BotAllActions.Description.Description);
    }
#endif

    private void BuildLobbyList()
    {
        Spacer(5);

        var totalPlayers = Header("");

        NetSession.Instance.Lobby.GetOnlineLobbies(SetupLobbyDisplay);

        return;

        void SetupLobbyDisplay(LobbyData[] lobbies)
        {
            if (this == null) {
                return;
            }

            var total = 0;
            foreach (var lobby in lobbies) {
                var count = lobby.MemberCount;
                total += count;

                HeaderCard("emp_ui_lobby_desc".Loc(lobby.Name, lobby.GameVersion, count, lobby[EmpLobbyData.CurrentZone]));

                // what that game runs with, when its host says so: known before joining
                if (ModList.Summary(lobby[EmpLobbyData.Mods]) is { } mods) {
                    TextSmall(mods.Missing > 0
                        ? "emp_ui_mods_lobby_missing".Loc(mods.Count, mods.Missing)
                        : "emp_ui_mods_lobby".Loc(mods.Count));
                }

                // from the title screen: one click joins that game (in a game, the player leaves it first)
                if (!EClass.core.IsGameStarted) {
                    var target = lobby;
                    Horizontal().Button("emp_ui_depot_join_btn".Loc(lobby.Name), () => {
                        LayerElinTogether.Instance?.Close();
                        NetSession.Instance.Lobby.ConnectLobby(target);
                    });
                }
            }

            totalPlayers.text1.text = "emp_ui_lobby_tally".Loc(total);
            // (these lines come after the layer was laid out, see LayerElinTogether.OnLayout)
            UIHelper.WrapTexts(this);
            this.RebuildLayout(true);
        }
    }

    private void StartServerFromPanel()
    {
        NetSession.Instance.InitializeComponent<ElinNetHost>().StartServer();
        LayerElinTogether.Instance?.Reopen();
    }

    private void DisconnectFromPanel()
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
}