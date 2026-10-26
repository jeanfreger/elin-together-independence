using ElinTogether.Common;
using ElinTogether.Helper;
using ElinTogether.LangMod;
using ElinTogether.Net;
using UnityEngine;
using YKF;

namespace ElinTogether.Components;

internal class TabSessionInfo : TabEmpBase
{
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

        foreach (var player in NetSession.Instance.CurrentPlayers) {
            BuildPlayerLine(this, player);
        }
    }

    /// <summary>
    ///     One line per player (council 2026-10-26): name, ping (or the host marker), and that player's own buttons
    /// </summary>
    private void BuildPlayerLine(YKLayout parent, NetPeerState player)
    {
        var chara = player.FindChara();
        if (chara is null) {
            parent.Text("emp_ui_invalid_chara".Loc(player.Index, player.CharaUid));
            return;
        }

        var line = parent.Horizontal();
        line.Layout.childForceExpandWidth = false;
        line.Layout.childAlignment = TextAnchor.MiddleLeft;
        line.Layout.spacing = 10f;

        var label = player.User.Name.TagColor(PeerColorizer.GetColor(player.Index)) + "  " + chara.Name;
        if (NetSession.Instance.Rules.UseTeams) {
            label += "  " + Teams.NameOf(Teams.TeamOf(chara.uid));
        }
        // (the ping text of index 0 is the host marker: "Ping - (host)")
        label += "  " + BuildPingStat(player);
        line.Text(label).LayoutElement().flexibleWidth = 1f;

        // Kick: the host, on a guest's line. Reconnect: a guest's own line only, or the host on a guest's line
        // (IsHost also reads true on an away client, and reconnecting would drop its zone)
        if (NetSession.Instance.IsAway) {
            return;
        }
        var isHost = NetSession.Instance.Transport is ElinNetHost;
        if (!(isHost ^ player.User.IsMe)) {
            return;
        }

        line.Button("emp_ui_reconnect".lang(), () => ReconnectPlayer(player.Index)).LayoutElement().preferredWidth = 150f;
        if (isHost) {
            line.Button("emp_ui_kick".lang(), () => KickPlayer(player.Index)).LayoutElement().preferredWidth = 110f;
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
        // plain digits: ToString("P0") adds a culture-specific space and percent sign that drew as a missing glyph
        var quality = player.ConnectionQualityLocal > 0
            ? "emp_ui_quality_format".Loc((player.ConnectionQualityLocal * 100f).ToString("F0"))
            : "";

        return "emp_ui_ping_format".Loc(ping.ToString("F0"), quality);
    }
}