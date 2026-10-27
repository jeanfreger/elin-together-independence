using ElinTogether.Helper;
using ElinTogether.LangMod;
using UnityEngine;
using YKF;

namespace ElinTogether.Components;

/// <summary>
///     This player's own options, by card: Controls, Workshop mods, Shared save folder (optional), Help
///     (council 2026-10-23)
/// </summary>
internal class TabClientConfiguration : TabEmpBase
{
    public override void OnLayout()
    {
        // Controls
        var controls = Card("controls");
        var pingKey = new EInput.KeyMap {
            action = EAction.None,
            key = KeyCode.P,
            required = true,
        };
        controls.ButtonRow().RowButton("emp_ui_ping_keymap".Loc(pingKey.key.ToString()), () => {
            var l = global::Layer.Create<Dialog>("DialogKeymap");
            l.textDetail.SetText("dialog_keymap".lang("emp_ui_ping_action".lang()));
            l.keymap = pingKey;
            l.SetOnKill(() => {
                EmpConfig.Client.PingKeybind.Value = pingKey.key;
                LayerElinTogether.Instance?.Reopen();
            });
            EMono.ui.AddLayer(l);
        }).SetTooltipLang(EmpConfig.Client.PingKeybind.Description.Description);
        controls.TextSmall("emp_ui_cl_desc_ping".lang());

        // Workshop mods: the mods of the game joined, fetched without subscribing (ModFetch): this player's own choice
        var mods = Card("mods");
        var fetch = EmpConfig.Client.FetchMods;
        var fetchGroup = mods.RowGroup();
        fetchGroup.Toggle("emp_ui_cl_fetch_mods", fetch.Value, value => fetch.Value = value).AsRow()
            .SetTooltipLang(fetch.Description.Description);
        fetchGroup.TextSmall("emp_ui_cl_desc_fetch_mods".lang());

        // ... and kept: subscribed to, on in the player's own list, so that the next join needs no restart
        // (keeping them means nothing without fetching them: greyed out until then)
        var keep = EmpConfig.Client.KeepMods;
        var keepGroup = mods.RowGroup(true);
        var keepToggle = keepGroup.Toggle("emp_ui_cl_keep_mods", keep.Value, value => keep.Value = value).AsRow();
        keepToggle.SetTooltipLang(keep.Description.Description);
        keepToggle.interactable = fetch.Value;
        keepGroup.TextSmall("emp_ui_cl_desc_keep_mods".lang());

        // Shared save folder (the depot): optional, not needed to play together
        var folder = Card("depot");
        folder.TextSmall("emp_ui_cl_desc_depot".lang());

        var depot = EmpConfig.Client.DepotPath;
        var depotGroup = folder.RowGroup();
        depotGroup.ButtonRow().RowButton("emp_ui_depot_folder".lang(), () => {
            Dialog.InputName("emp_ui_depot_folder_ask", depot.Value, (cancel, text) => {
                if (!cancel) {
                    depot.Value = text.Trim();
                    LayerElinTogether.Instance?.Reopen();
                }
            }).input.field.characterLimit = 0;
        }).SetTooltipLang(depot.Description.Description);
        depotGroup.TextSmall(depot.Value.Length > 0 ? depot.Value : "-");

        var password = EmpConfig.Client.DepotPassword;
        var passwordGroup = folder.RowGroup();
        passwordGroup.ButtonRow().RowButton("emp_ui_depot_password".lang(), () => {
            // a password or an access key is never shown again: the box opens empty. Left empty, a GitHub key is
            // kept (a depot of Elin Together Server may have no password: there, empty removes it)
            Dialog.InputName("emp_ui_depot_password_ask", "", (cancel, text) => {
                if (!cancel && !(SaveDepot.GitHub && text.Trim().Length == 0)) {
                    password.Value = text.Trim();
                    LayerElinTogether.Instance?.Reopen();
                }
            }).input.field.characterLimit = 0;
        }).SetTooltipLang(password.Description.Description);
        passwordGroup.TextSmall(password.Value.Length > 0 ? "***" : "-");

        // how a private GitHub repository becomes the depot, where it is set
        // (one text per line: a text with line breaks was measured one line high and drew over the rows above it)
        foreach (var line in "emp_ui_depot_gh_help".lang().Split('\n')) {
            folder.TextSmall(line);
        }

        // Help: something went wrong, one zip to send, with the game's own log in it
        var help = Card("help");
        help.ButtonRow().RowButton("emp_ui_report".lang(), () => {
            if (BugReport.Make() is { } path) {
                EmpPop.Information("emp_ui_report_done".Loc(path));
                Util.ShowExplorer(path);
            } else {
                EmpPop.Information("emp_ui_report_failed".lang());
            }
        });
        help.TextSmall("emp_ui_report_desc".lang());

        // the same, sent to the developers as a GitHub issue: the player writes what happened (user, 2026-10-27)
        help.ButtonRow().RowButton("emp_ui_report_send".lang(), () => {
            var input = Dialog.InputName("emp_ui_report_what", "", (cancel, text) => {
                if (cancel || text.Trim().Length < 3) {
                    return;
                }

                EmpPop.Information("emp_ui_report_sending".lang());
                BugReport.Send(text.Trim(), url => {
                    if (url is null) {
                        EmpPop.Information("emp_ui_report_page".lang());
                        return;
                    }

                    Dialog.YesNo("emp_ui_report_sent".Loc(url), () => Application.OpenURL(url));
                });
            }, Dialog.InputType.Chat);
            input.input.field.characterLimit = 3000;
            Patches.ReportBoxPatch.Enlarge(input);
        });
        help.TextSmall("emp_ui_report_send_desc".lang());
    }

    private YKVertical Card(string id)
    {
        var card = this.MakeCard();
        card.HeaderSmall($"emp_ui_cl_sec_{id}".lang());
        return card;
    }
}
