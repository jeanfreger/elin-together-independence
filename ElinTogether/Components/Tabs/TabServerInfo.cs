using System.Collections.Generic;
using System.Linq;
using ElinTogether.Helper;
using ElinTogether.LangMod;
using ElinTogether.Models;
using ElinTogether.Net;
using HeathenEngineering.SteamworksIntegration;
using YKF;

namespace ElinTogether.Components;

/// <summary>
///     The "Server" page (hub step 7): the host's message of the day and rules as two parchment notes (the host
///     writes in place with the pencil, a guest reads), and for the host and the admins a line to everyone and a
///     restart warning. The texts reach every game with the world's rules. Friends and Ignored live in My settings
/// </summary>
internal class TabServerInfo : TabEmpBase
{
    // which note is open for writing (a page rebuild keeps it: the layer is reopened after a change)
    private static string? _writing;

    private static readonly string[] InfoIcons = ["icon_book", "icon_policy"];

    public override void OnLayout()
    {
        var pages = new List<(string, System.Action<YKVertical>)> {
            ("emp_info_motd".lang(), page => BuildNote(page, "motd.txt", "emp_info_motd", ServerInfo.Motd)),
            ("emp_ui_sub_rules".lang(), page => BuildNote(page, "rules.txt", "emp_info_rules", ServerInfo.RulesText)),
        };
        // (the host and the admins only)
        if (IsAdmin) {
            pages.Add(("emp_info_announce".lang(), BuildAnnounce));
        }

        SubTabs("info", pages);
    }

    private static bool IsHost => NetSession.Instance.Transport is ElinNetHost { IsZoneSession: false };

    private static bool IsAdmin => IsHost || (NetSession.Instance.Transport is ElinNetClient &&
                                              ModerationDelta.AdminIds.Contains(NetSession.Instance.Self?.User ?? UserData.Me));

    /// <summary>
    ///     One parchment note: the text, or for the host an empty-state with "Write one"; the host's pencil opens the
    ///     game's input field in the note itself
    /// </summary>
    private static void BuildNote(YKVertical page, string file, string titleId, string text)
    {
        var card = page.Card(titleId.lang());
        if (IsHost && _writing == file) {
            Write(card, file);
            return;
        }

        if (text.Length == 0) {
            if (IsHost) {
                TabTeam.EmptyPage(card, InfoIcons, "emp_info_none_host".lang(), "emp_info_write".lang(), () => Open(file));
            } else {
                TabTeam.EmptyPage(card, InfoIcons, "emp_info_none".lang());
            }
            return;
        }

        foreach (var line in ServerInfo.Lines(text)) {
            card.Text(line);
        }

        if (IsHost) {
            var buttons = card.ButtonRow();
            buttons.RowButton("emp_info_edit".lang(), () => Open(file));
            if (file == "rules.txt") {
                buttons.RowButton("emp_info_reload".lang(), Publish);
            }
        }
    }

    private static void Open(string file)
    {
        _writing = file;
        LayerElinTogether.Instance?.Reopen();
    }

    private static void Write(YKVertical card, string file)
    {
        var input = card.InputText(ServerInfo.ReadFile(file));
        input.field.characterLimit = ServerInfo.MaxLength;
        input.field.lineType = UnityEngine.UI.InputField.LineType.MultiLineNewline;
        input.LayoutElement().minHeight = 150f;
        input.LayoutElement().preferredHeight = 150f;
        card.TextSmall("emp_info_files".lang(), FontColor.Gray);

        var buttons = card.ButtonRow();
        buttons.RowButton("emp_info_save".lang(), () => {
            _writing = null;
            ServerInfo.WriteFile(file, input.Text);
            Publish();
        });
        buttons.RowButton("emp_info_cancel".lang(), () => {
            _writing = null;
            LayerElinTogether.Instance?.Reopen();
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

    private static void BuildAnnounce(YKVertical page)
    {
        var card = page.Card("emp_info_announce".lang());
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
}
