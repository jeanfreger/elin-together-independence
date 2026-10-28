#if DEBUG
using System.Collections.Generic;
using System.Linq;
using ElinTogether.Helper;
using ReflexCLI.Attributes;
using UnityEngine;
using YKF;

namespace ElinTogether.Components;

/// <summary>
///     Bench only: one layer that shows every EmpUi piece (console command "emp.ui_demo"), so that screenshots can
///     judge them against the game's own menus before a real tab uses them
/// </summary>
internal class LayerEmpUiDemo : YKLayer<LayerCreationData>
{
    public override string Title => "Elin Together UI pieces";
    public override Rect Bound => new(Vector2.zero, new Vector2(900f, 760f));

    public override void OnLayout()
    {
        var tab = CreateTab<TabEmpUiDemo>("emp_ui_title", "emp_tab_demo");
        UIHelper.WrapTexts(tab);
        tab.RebuildLayout(true);
    }

    public override void OnAfterAddLayer()
    {
        base.OnAfterAddLayer();
        Window.SwitchContent("emp_tab_demo");
    }
}

internal class TabEmpUiDemo : TabEmpBase
{
    public override void OnLayout()
    {
        var players = this.Card("Player rows", EmpUi.Icon("icon_elements"));
        var charas = new List<Chara?> { EClass.pc };
        charas.AddRange(EClass._zone?.map?.charas?.Where(c => c != EClass.pc && !c.isDead).Take(3) ?? []);
        while (charas.Count < 4) {
            charas.Add(null);
        }

        var info = new[] {
            new PlayerRowInfo("Marc".TagColor(new Color(0.0f, 0.45f, 0.7f)), "Mage, level 24", charas[0], "Red team", 4,
                PlayerBadge.Admin),
            new PlayerRowInfo("Ana", "Warrior, level 17", charas[1], "Red team", 3, PlayerBadge.Moderator | PlayerBadge.Idle),
            new PlayerRowInfo("Joe", "Thief, level 9", charas[2], null, 2, PlayerBadge.Frozen),
            new PlayerRowInfo("Host", "Farmer, level 31", charas[3], "Open to all"),
        };
        foreach (var row in info) {
            players.PlayerRow(row, [("Whisper", () => Msg.Say("Whisper")), ("Trade", () => Msg.Say("Trade")), ("Add as friend", () => { })]);
            players.Divider();
        }

        var chips = this.Card("Chips");
        var line = chips.Horizontal();
        line.Layout.childForceExpandWidth = false;
        line.Layout.spacing = 8f;
        line.Chip("Open to all");
        line.Chip("Set by the host", FontColor.Gray);
        line.Chip("Leader", FontColor.Warning);

        var settings = this.Card("Setting rows");
        settings.ToggleRow("Teams", "Players can found and join teams in this world.", true, _ => { }, reference: false);
        settings.ToggleRow("Friendly fire", "Teammates can hurt each other.", false, _ => { }, reference: false);
        settings.DropdownSettingRow("Pace of time", "How fast the world clock runs for everyone.",
            ["Slow", "Normal", "Fast"], 2, _ => { }, reference: 1);
        settings.SliderRow("Players", "How many friends may join.", 4, 2, 8, _ => { }, v => v.ToString(), reference: 4);
        settings.Divider();
        settings.ToggleRow("Read only toggle", "A guest sees it, only the host can change it.", true, _ => { }, readOnly: true,
            setByHostText: "Set by the host");
        settings.DropdownSettingRow("Read only choice", "Same line, a dropdown.", ["Slow", "Normal", "Fast"], 1, _ => { },
            readOnly: true, setByHostText: "Set by the host");

        var empty = this.Card("Empty state");
        empty.EmptyState("No friends in this world yet. Share the invite to bring someone in.", "Invite friends",
            () => Msg.Say("Invite"));
    }
}

[ConsoleCommandClassCustomizer("emp")]
internal class EmpUiDemoCommands
{
    [ConsoleCommand("ui_demo")]
    internal static string OpenUiDemo()
    {
        YK.CreateLayer<LayerEmpUiDemo, LayerCreationData>(new(""));
        return "UI demo open";
    }
}
#endif
