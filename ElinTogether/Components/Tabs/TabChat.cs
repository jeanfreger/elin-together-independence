using System.Collections.Generic;
using System.Linq;
using ElinTogether.Helper;
using ElinTogether.LangMod;
using ElinTogether.Net;
using UnityEngine;
using UnityEngine.UI;
using YKF;

namespace ElinTogether.Components;

/// <summary>
///     The Chat tab (user 2026-10-30): the lines this game was shown since it joined (ChatLog), filtered by channel, and
///     a box to write to everyone, to one's team or to one player. A line that arrives is added under the others; the
///     tab is never built again for it, so a line being typed keeps its text and its focus
/// </summary>
internal class TabChat : TabEmpBase
{
    public const string TabName = "emp_tab_chat";

    // kept over reopenings while the game runs, never stored
    private static int _filter = -1;
    private static string _draft = "";
    private static int _target;

    private YKVertical? _list;
    private ScrollRect? _scroll;
    private int _shown = -1;
    private int _count;

    public override void OnLayout()
    {
        if (Waits()) {
            return;
        }

        var card = this.Card("emp_ui_tab_chat".lang());
        if (!NetSession.Instance.InGame) {
            TabTeam.EmptyPage(card, ["icon_menu_16"], "emp_ui_chat_off".lang());
            return;
        }

        card.DropdownRow("emp_ui_chat_show".lang(), [..Channels().Select(c => ChannelName(c))], i => {
            _filter = Channels()[i];
            Refill();
        }, Mathf.Max(0, Channels().IndexOf(_filter)));

        _list = card.ScrollList(330f);
        _scroll = _list.GetComponentInParent<ScrollRect>();
        Refill();

        BuildWriter(card);
    }

    // the filters this player has: a team filter only in a team
    private static List<int> Channels()
    {
        var list = new List<int> { -1, ChatLog.All };
        if (InTeam()) {
            list.Add(ChatLog.Team);
        }

        list.Add(ChatLog.Private);
        list.Add(ChatLog.Server);
        return list;
    }

    private static bool InTeam() => NetSession.Instance.Rules.UseTeams && Teams.TeamOf(EClass.pc.uid) != Teams.Wanderers;

    private static string ChannelName(int channel) => channel switch {
        ChatLog.All => (NetSession.Instance.Rules.GlobalChat ? "emp_ui_chat_everyone" : "emp_ui_chat_map").lang(),
        ChatLog.Team => "emp_ui_chat_team".lang(),
        ChatLog.Private => "emp_ui_chat_private".lang(),
        ChatLog.Server => "emp_ui_chat_server".lang(),
        _ => "emp_ui_chat_all".lang(),
    };

    // where a line can go: everyone (or this map), the team, then each other player online when private lines are on
    private static List<(string Label, Chara? To, bool Team)> Targets()
    {
        var list = new List<(string, Chara?, bool)> { (ChannelName(ChatLog.All), null, false) };
        if (InTeam()) {
            list.Add(("emp_ui_chat_team".lang(), null, true));
        }

        if (NetSession.Instance.Rules.PrivateChat) {
            foreach (var p in NetSession.Instance.VisiblePlayers.Where(p => p.CharaUid != EClass.pc.uid).OrderBy(p => p.User.Name)) {
                if (Teams.FindChara(p.CharaUid) is { } other) {
                    list.Add(("emp_ui_chat_to".Loc(p.User.Name.IsEmpty(other.Name)), other, false));
                }
            }
        }

        return list;
    }

    private void BuildWriter(YKLayout card)
    {
        var targets = Targets();
        _target = Mathf.Clamp(_target, 0, targets.Count - 1);
        var row = card.Horizontal();
        row.Layout.childForceExpandWidth = false;
        row.Layout.childAlignment = TextAnchor.MiddleLeft;
        row.Layout.spacing = 8f;

        var to = row.Dropdown([..targets.Select(t => t.Label)], i => _target = i, _target);
        to.LayoutElement().preferredWidth = 170f;
        to.LayoutElement().flexibleWidth = 0f;

        var box = row.Vertical();
        box.LayoutElement().flexibleWidth = 1f;
        var input = box.InputText(_draft);
        input.field.characterLimit = 500;
        input.AllowAnyText();
        input.StyleAsSearch("emp_ui_chat_hint".lang());
        input.field.onValueChanged.AddListener(text => _draft = text);

        void Send()
        {
            var text = input.field.text.Trim();
            if (text.Length == 0) {
                return;
            }

            var target = Targets().ElementAtOrDefault(_target);
            if (target.Team) {
                ChatLog.SendTeam(text);
            } else if (target.To is { } other) {
                PlayerWhisper.Send(other, text);
            } else {
                // (the way the game's own chat box sends a line: the player says it on the map, the mod passes it on)
                ActionMode.Adv.OnEnterChat(text);
            }

            _draft = "";
            input.field.text = "";
            input.field.ActivateInputField();
        }

        // (Enter sends, as in the game's chat box)
        input.field.onEndEdit.AddListener(_ => {
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) {
                Send();
            }
        });
        row.RowButton("emp_ui_chat_send".lang(), Send).LayoutElement().flexibleWidth = 0f;
    }

    private void Refill()
    {
        if (_list == null) {
            return;
        }

        foreach (Transform child in _list.transform) {
            Destroy(child.gameObject);
        }

        _count = 0;
        _shown = ChatLog.Version;
        foreach (var line in ChatLog.Entries.Where(Shows)) {
            AddLine(line);
        }

        if (_count == 0) {
            _list.Text("emp_ui_chat_empty".lang(), FontColor.Gray);
        }

        ToBottom();
    }

    private static bool Shows(ChatLog.Line line) => _filter < 0 || line.Channel == _filter;

    private void AddLine(ChatLog.Line line)
    {
        var tag = line.Channel switch {
            ChatLog.Team => "emp_ui_chat_team".lang(),
            ChatLog.Private => "emp_ui_chat_private".lang(),
            ChatLog.Server => "emp_ui_chat_server".lang(),
            _ => "",
        };
        var head = tag.Length > 0 ? $"[{tag}] ".TagColor(ChatLog.ColorOf(line.Channel)) : "";
        _list!.Text($"{line.Time.TagColor(new Color(0.45f, 0.4f, 0.32f))}  {head}{line.Who}: {line.Text}");
        _count++;
    }

    private void ToBottom()
    {
        if (_list != null) {
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)_list.transform);
        }

        if (_scroll != null) {
            _scroll.verticalNormalizedPosition = 0f;
        }
    }

    // a line that arrived while the tab is open: added under the others (the newest at the bottom, scrolled to)
    private void Update()
    {
        if (_list == null) {
            return;
        }

        // (shown: every line is read)
        ChatLog.MarkSeen();
        if (_shown == ChatLog.Version) {
            return;
        }

        var entries = ChatLog.Entries;
        var added = Mathf.Clamp(ChatLog.Version - _shown, 0, entries.Count);
        _shown = ChatLog.Version;
        if (_count == 0) {
            Refill();
            return;
        }

        foreach (var line in entries.Skip(entries.Count - added).Where(Shows)) {
            AddLine(line);
        }

        ToBottom();
    }
}
