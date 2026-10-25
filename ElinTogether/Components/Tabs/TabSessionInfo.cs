using ElinTogether.Common;
using ElinTogether.Helper;
using ElinTogether.LangMod;
using ElinTogether.Net;
using UnityEngine;
using UnityEngine.UI;
using YKF;

namespace ElinTogether.Components;

internal class TabSessionInfo : TabEmpBase
{
    private Rect _refSize = LayerElinTogether.Instance!.Bound;

    public override void OnLayout()
    {
        BuildOverviewSection();
        BuildPlayerList();
    }

    private void BuildOverviewSection()
    {
        // a friend sees the rules of the host's world, read only, as the host last sent them (council 2026-10-23)
        if (NetSession.Instance.Transport is not ElinNetClient) {
            return;
        }

        var card = this.MakeCard();
        card.Layout.padding = new(20, 20, 8, 10);
        card.HeaderSmall("emp_ui_rules_title".lang());
        card.TextSmall("emp_ui_rules_note".lang());
        foreach (var (label, value) in SettingsTable.RuleLines(SettingsTable.FromRules(NetSession.Instance.Rules))) {
            card.Text("emp_ui_rules_line".Loc(label, value));
        }
        if (SettingsTable.LastNotice is { } notice) {
            card.TextSmall(notice);
        }
    }

    private void BuildPlayerList()
    {
        Header("emp_ui_connected_players");

        var list = Grid()
            .WithConstraintCount(2);
        list.Fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        list.Layout.cellSize = new(_refSize.width / 2.2f, _refSize.width * 0.2f);
        var players = NetSession.Instance.CurrentPlayers;
        foreach (var player in players) {
            BuildPlayerCard(list, player);
        }
    }

    private void BuildPlayerCard(YKLayout parent, NetPeerState player)
    {
        var chara = player.FindChara();
        if (chara is null) {
            Text("emp_ui_invalid_chara".Loc(player.Index, player.CharaUid));
            return;
        }

        var card = parent.MakeCard();
        var bannerGroup = card.Horizontal();
        bannerGroup.Layout.spacing = 15f;

        // portrait
        var go = new GameObject("portrait");
        go.transform.SetParent(bannerGroup.transform);
        var portrait = go.AddComponent<Portrait>();
        portrait.tooltip = new() {
            enable = false,
        };
        portrait.portrait = go.AddComponent<Image>();
        portrait.portrait.preserveAspect = true;

        var go1 = new GameObject("overlay");
        go1.transform.SetParent(go.transform);
        portrait.overlay = go1.AddComponent<Image>();
        portrait.overlay.preserveAspect = true;

        portrait.SetChara(chara);

        portrait.portrait.LayoutElement().preferredWidth = _refSize.width * 0.12f;
        portrait.portrait.rectTransform.sizeDelta = new(_refSize.width, _refSize.width * 0.16f);
        portrait.overlay.rectTransform.anchorMin =
            portrait.overlay.rectTransform.anchorMax =
                portrait.overlay.rectTransform.sizeDelta = Vector2.zero;

        // info
        var infoGroup = bannerGroup.Vertical();
        infoGroup.LayoutElement().preferredWidth = 1f;
        infoGroup.TextFlavor(player.User.Name.TagColor(PeerColorizer.GetColor(player.Index)));
        infoGroup.TextMedium(chara.Name);
        if (NetSession.Instance.Rules.UseTeams) {
            infoGroup.Text(Teams.NameOf(Teams.TeamOf(chara.uid)));
        }
        infoGroup.Text(BuildPingStat(player));

        // action buttons: only show for non-host players
        // IsHost also reads true on an away client, and reconnecting would drop its zone
        var isHost = NetSession.Instance.Transport is ElinNetHost;
        if (isHost ^ player.User.IsMe && !NetSession.Instance.IsAway) {
            var btnRow = infoGroup.Horizontal();
            btnRow.Layout.childForceExpandWidth = true;
            btnRow.Layout.childAlignment = TextAnchor.MiddleCenter;

            btnRow.Button("emp_ui_reconnect".lang(), () => ReconnectPlayer(player.Index));

            if (isHost) {
                btnRow.Button("emp_ui_kick".lang(), () => KickPlayer(player.Index));
            }
        }
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

    private static void KickPlayer(int peerIndex)
    {
        NetSession.Instance.Transport?.DisconnectPeer(peerIndex, EmpDisconnectInfo.HostKick);
        LayerElinTogether.Instance?.Reopen();
    }

    private static string BuildPingStat(NetPeerState player)
    {
        if (player.Index == 0) {
            return "emp_ui_ping_host".lang();
        }

        var ping = player.AvgPingMs > 0 ? player.AvgPingMs : player.LastPingMs;
        var quality = player.ConnectionQualityLocal > 0
            ? "emp_ui_quality_format".Loc(player.ConnectionQualityLocal.ToString("P0"))
            : "";

        return "emp_ui_ping_format".Loc(ping.ToString("F0"), quality);
    }
}