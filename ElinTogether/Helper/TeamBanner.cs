using System.Collections.Generic;
using System.Linq;
using ElinTogether.Components;
using ElinTogether.Models;
using ElinTogether.Net;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using YKF;

namespace ElinTogether.Helper;

/// <summary>
///     Host box "Team banners" (PLAN_hub_features C): each team has an emblem (one of the game's element icons) and a
///     colour (one of 12), chosen by its leader and shown on the Teams page. The host keeps them
///     (<see cref="ElinNetHost.TeamBanners" />) and tells every game with the teams (<see cref="TeamsDelta.Banners" />)
/// </summary>
internal static class TeamBanner
{
    private const string Sheet = "Media/Graphics/Icon/Element/icon_elements";

    /// <summary>
    ///     The emblems offered: aliases of the game's elements, drawn from the sprite "ele_" + alias of its element sheet.
    ///     A name the game has no sprite for is left out at runtime (<see cref="Emblems" />)
    /// </summary>
    private static readonly string[] Aliases = [
        "STR", "END", "DEX", "PER", "LER", "WIL", "MAG", "CHA", "SPD", "LUC",
        "eleFire", "eleCold", "eleLightning", "eleDarkness", "eleMind", "elePoison", "eleNether", "eleSound",
        "eleNerve", "eleHoly", "eleChaos", "eleMagic", "eleImpact", "eleCut", "eleAcid", "eleEther",
    ];

    /// <summary>
    ///     12 colours that read on a dark page (the colour-blind safe set of <see cref="PeerColorizer" /> first)
    /// </summary>
    private static readonly int[] Palette = [
        0x0072b2, 0xe69f00, 0xcc79a7, 0x009e73, 0xd55e00, 0x56b4e9,
        0xf0e442, 0x9a6fe0, 0xe0554f, 0x8fbf3f, 0x4fd1c5, 0xb0b0b0,
    ];

    internal const int ColorCount = 12;

    // a guest's copy of the host's banners
    private static Dictionary<int, (int Icon, int Color)> _told = [];

    private static List<(int Id, Sprite Sprite)>? _emblems;

    internal static bool On => NetSession.Instance is { HasActiveConnection: true } net && EClass.pc != null &&
                               (net.Transport is ElinNetHost
                                   ? EmpConfig.Server.TeamBanner.Value && EmpConfig.Server.Teams.Value
                                   : net.Rules is { UseTeams: true, TeamBanner: true });

    private static bool KeepsBook => NetSession.Instance.Transport is not ElinNetClient;

    /// <summary>
    ///     The emblems the game really has a sprite for (id = index in <see cref="Aliases" />, kept stable)
    /// </summary>
    internal static List<(int Id, Sprite Sprite)> Emblems
    {
        get
        {
            if (_emblems is { Count: > 0 }) {
                return _emblems;
            }

            var list = new List<(int, Sprite)>();
            for (var i = 0; i < Aliases.Length; i++) {
                if (SpriteSheet.Get(Sheet, "ele_" + Aliases[i]) is { } sprite) {
                    list.Add((i, sprite));
                }
            }

            return _emblems = list;
        }
    }

    internal static Color ColorOf(int index) => ToColor(Palette[Mathf.Clamp(index, 0, ColorCount - 1)]);

    private static Color ToColor(int rgb) => new Color32((byte)((rgb >> 16) & 0xFF), (byte)((rgb >> 8) & 0xFF), (byte)(rgb & 0xFF), 255);

    private static Dictionary<int, (int Icon, int Color)> Book =>
        KeepsBook ? ElinNetHost.TeamBanners.Where(b => b.Value is { Length: 2 }).ToDictionary(b => b.Key, b => (b.Value[0], b.Value[1])) : _told;

    internal static (int Icon, int Color)? Of(int teamId) => On && Book.TryGetValue(teamId, out var banner) ? banner : null;

    /// <summary>
    ///     In the game that keeps the book: this leader sets the banner of its team. False when the rule is off, the asker
    ///     leads no team or the emblem or colour is not one offered
    /// </summary>
    internal static bool Set(Chara chara, int icon, int color)
    {
        var team = Teams.TeamOf(chara.uid);
        if (!KeepsBook || !On || team == Teams.Wanderers || Teams.LeaderOf(team) != chara.uid) {
            return false;
        }

        if (color is < 0 or >= ColorCount || Emblems.All(e => e.Id != icon)) {
            return false;
        }

        ElinNetHost.TeamBanners[team] = [icon, color];
        EmpLog.Information("Team {TeamId} banner: emblem {Icon}, colour {Color}", team, icon, color);
        return true;
    }

    internal static void Remove(int teamId) => ElinNetHost.TeamBanners.Remove(teamId);

    /// <summary>
    ///     What the host tells with the teams: none while the box is off
    /// </summary>
    internal static Dictionary<int, (int Icon, int Color)>? Told()
    {
        return EmpConfig.Server.TeamBanner.Value
            ? ElinNetHost.TeamBanners.Where(b => b.Value is { Length: 2 }).ToDictionary(b => b.Key, b => (b.Value[0], b.Value[1]))
            : null;
    }

    internal static void Learn(Dictionary<int, (int Icon, int Color)>? banners) => _told = new(banners ?? []);

    internal static void Forget() => _told = [];

    /// <summary>
    ///     The team's banner in a row: its emblem tinted with its colour, then a chip of that colour. Nothing when the
    ///     box is off or the team has none
    /// </summary>
    internal static void Draw(YKLayout row, int teamId, float size = 26f)
    {
        if (Of(teamId) is not { } banner) {
            return;
        }

        var color = ColorOf(banner.Color);
        if (Emblems.FirstOrDefault(e => e.Id == banner.Icon).Sprite is { } sprite) {
            var icon = row.CardIcon(null, size);
            icon.sprite = sprite;
            icon.color = color;
            icon.enabled = true;
        }

        Swatch(row, color, size * 0.35f, size, false, null);
    }

    /// <summary>
    ///     A small clickable square: a flat colour, or an emblem sprite tinted with the colour
    /// </summary>
    internal static void Swatch(YKLayout row, Color color, float width, float height, bool selected, System.Action? click, Sprite? sprite = null)
    {
        var go = new GameObject("Swatch", typeof(RectTransform), typeof(LayoutElement), typeof(Image));
        go.transform.SetParent(row.transform, false);
        var element = go.GetComponent<LayoutElement>();
        (element.minWidth, element.preferredWidth, element.minHeight, element.preferredHeight, element.flexibleWidth) = (width, width, height, height, 0f);
        var image = go.GetComponent<Image>();
        if (sprite != null) {
            image.sprite = sprite;
            image.preserveAspect = true;
            image.color = selected ? color : new(color.r, color.g, color.b, 0.5f);
        } else {
            image.color = color;
        }

        if (selected) {
            var outline = go.AddComponent<Outline>();
            outline.effectColor = Color.white;
            outline.effectDistance = new(2f, -2f);
        }

        if (click != null) {
            go.AddComponent<Button>().onClick.AddListener(() => click());
        } else {
            image.raycastTarget = false;
        }
    }

    /// <summary>
    ///     The picker: the emblems, then the 12 colours; a click asks the host (checked there against the team's leader)
    /// </summary>
    internal static void BuildPicker(YKLayout card, int teamId)
    {
        var current = Of(teamId) ?? (Emblems.FirstOrDefault().Id, 0);
        var colour = ColorOf(current.Color);
        card.TextSmall("emp_ui_team_banner_emblem".lang(), FontColor.Gray);
        YKLayout row = card.Row();
        var n = 0;
        foreach (var (id, sprite) in Emblems) {
            if (n++ % 9 == 0 && n > 1) {
                row = card.Row();
            }

            var emblem = id;
            Swatch(row, colour, 30f, 30f, id == current.Icon, () => Teams.Ask(TeamAsk.Banner, emblem, current.Color), sprite);
        }

        card.TextSmall("emp_ui_team_banner_color".lang(), FontColor.Gray);
        row = card.Row();
        for (var c = 0; c < ColorCount; c++) {
            var index = c;
            Swatch(row, ColorOf(c), 24f, 24f, c == current.Color, () => Teams.Ask(TeamAsk.Banner, current.Icon, index));
        }
    }

    /// <summary>
    ///     A dark rectangle with one dot per land of the team, placed by the lands' map position; the name of the land
    ///     under the pointer is said on the line below
    /// </summary>
    internal static void BuildLandMap(YKLayout card, int teamId)
    {
        var lands = Teams.LandsOf(teamId).Select(uid => EClass.game.spatials.Find(uid)).OfType<Zone>().ToList();
        if (lands.Count == 0) {
            card.TextSmall("emp_ui_team_lands_none".lang(), FontColor.Gray);
            return;
        }

        var frame = new GameObject("LandMap", typeof(RectTransform), typeof(LayoutElement), typeof(Image));
        frame.transform.SetParent(card.transform, false);
        var element = frame.GetComponent<LayoutElement>();
        element.minHeight = element.preferredHeight = 150f;
        element.flexibleWidth = 1f;
        frame.GetComponent<Image>().color = new(0.07f, 0.08f, 0.1f, 1f);
        frame.GetComponent<Image>().raycastTarget = false;

        var hover = card.TextSmall("emp_ui_team_lands_hover".lang(), FontColor.Gray);
        var (minX, maxX) = (lands.Min(z => z.x), lands.Max(z => z.x));
        var (minY, maxY) = (lands.Min(z => z.y), lands.Max(z => z.y));
        var tint = Of(teamId) is { } banner ? ColorOf(banner.Color) : new Color(0.9f, 0.9f, 0.9f);
        foreach (var zone in lands) {
            // (inside a margin of 10 %; one land, or lands in a line, sit in the middle)
            var fx = maxX > minX ? (zone.x - minX) / (float)(maxX - minX) : 0.5f;
            var fy = maxY > minY ? (zone.y - minY) / (float)(maxY - minY) : 0.5f;
            var dot = new GameObject("Land", typeof(RectTransform), typeof(Image), typeof(EventTrigger));
            dot.transform.SetParent(frame.transform, false);
            dot.GetComponent<Image>().color = tint;
            var rt = (RectTransform)dot.transform;
            rt.anchorMin = rt.anchorMax = new(0.1f + fx * 0.8f, 0.9f - fy * 0.8f);
            rt.sizeDelta = new(12f, 12f);
            var name = zone.Name;
            var trigger = dot.GetComponent<EventTrigger>();
            AddTrigger(trigger, EventTriggerType.PointerEnter, () => hover.text = name);
            AddTrigger(trigger, EventTriggerType.PointerExit, () => hover.text = "emp_ui_team_lands_hover".lang());
        }
    }

    private static void AddTrigger(EventTrigger trigger, EventTriggerType type, System.Action act)
    {
        var entry = new EventTrigger.Entry { eventID = type };
        entry.callback.AddListener(_ => act());
        trigger.triggers.Add(entry);
    }
}
