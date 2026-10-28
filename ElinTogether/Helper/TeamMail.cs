using System.Collections.Generic;
using System.Linq;
using ElinTogether.LangMod;
using ElinTogether.Models;
using ElinTogether.Net;
using HarmonyLib;

namespace ElinTogether.Helper;

/// <summary>
///     Council 2026-10-28 (dev/COUNCIL_2026-10-28_team_boxes.md, parcels), user request of the same day. The game keeps
///     one list of parcels for the world (World.SendPackage) and drops them in the mail box of whichever base simulates
///     its day first. A parcel sent for a player carries that player's character uid (<see cref="ParcelKey" />) and
///     shows its name (always, a label only). With the teams' boxes on, a tagged parcel is delivered only into a base of
///     the recipient's unit (its team's land), into a mail box that player owns first, else one owned by nobody, else
///     into the unit's own delivery box (also where the unit has no base). Untagged parcels, and the box off: the
///     game's own delivery. A mail box is owned by whoever installed it (<see cref="OwnerKey" />, 0: everyone) and the
///     owner changes it from the item's menu
/// </summary>
[HarmonyPatch]
internal static class TeamMail
{
    internal const string ParcelKey = "emp_parcel_to";
    internal const string OwnerKey = "emp_mail_owner";

    // the parcels of other units, taken out of the list for one base's delivery
    private static readonly List<Thing> _held = [];

    /// <summary>
    ///     World.SendPackage for a parcel that is that player's
    /// </summary>
    internal static void Send(int charaUid, Thing parcel)
    {
        if (charaUid > 0) {
            parcel.SetInt(ParcelKey, charaUid);
        }

        EClass.world.SendPackage(parcel);
    }

    internal static void Send(Chara player, Thing parcel)
    {
        Send(player.uid, parcel);
    }

    // ---- the label

    [HarmonyPostfix]
    [HarmonyPatch(typeof(Thing), nameof(Thing.GetName))]
    private static void OnName(Thing __instance, ref string __result)
    {
        if (__instance.GetInt(ParcelKey) is var uid and > 0 &&
            EClass.game?.cards?.globalCharas.Find(uid) is { } chara) {
            __result += " " + "emp_parcel_for".Loc(chara.NameSimple);
        }
    }

    // ---- the delivery

    [HarmonyPrefix]
    [HarmonyPatch(typeof(FactionBranch), nameof(FactionBranch.ReceivePackages))]
    private static void BeforeReceive(FactionBranch __instance)
    {
        _held.Clear();
        if (!TeamBoxes.On || !Teams.BasesOwned || EClass.player.simulatingZone || EClass.game?.cards?.listPackage is not { Count: > 0 } list) {
            return;
        }

        var here = Teams.TeamOfZone(__instance.owner);
        foreach (var parcel in list.ToArray()) {
            if (parcel.GetInt(ParcelKey) is not (var uid and > 0)) {
                continue;
            }

            var unit = StoryOwner.Of(uid);
            if (unit > 0 && Teams.HasBase(unit)) {
                if (unit != here) {
                    list.Remove(parcel);
                    _held.Add(parcel);
                }

                continue;
            }

            // no base of its own: the unit's delivery box
            list.Remove(parcel);
            TeamBoxes.Box(unit, 1).AddThing(parcel);
            EmpLog.Information("A parcel for player {Uid} went to the delivery box of {Owner}", uid, StoryOwner.Name(unit));
        }
    }

    // a finalizer: what was held comes back even if the game's method returned early or threw
    [HarmonyFinalizer]
    [HarmonyPatch(typeof(FactionBranch), nameof(FactionBranch.ReceivePackages))]
    private static void AfterReceive()
    {
        if (_held.Count > 0) {
            EClass.game.cards.listPackage.AddRange(_held);
            _held.Clear();
        }
    }

    // which mail box of the base takes this parcel: its recipient's, else one nobody owns, else the unit's delivery box
    [HarmonyPrefix]
    [HarmonyPatch(typeof(FactionBranch), nameof(FactionBranch.PutInMailBox))]
    private static bool OnPutInMailBox(Thing t, bool outside)
    {
        if (outside || !TeamBoxes.On || !Teams.BasesOwned || t.GetInt(ParcelKey) is not (var uid and > 0)) {
            return true;
        }

        var boxes = EClass._map.props.installed.Things.Where(b => b.trait is TraitMailPost && !b.things.IsFull(t)).ToList();
        var box = boxes.FirstOrDefault(b => b.GetInt(OwnerKey) == uid) ?? boxes.FirstOrDefault(b => b.GetInt(OwnerKey) == 0);
        (box ?? TeamBoxes.Box(StoryOwner.Of(uid), 1)).AddCard(t);
        return false;
    }

    // ---- the owner of a mail box

    /// <summary>
    ///     The player who installs a mail box owns it (host: its own install, or the guest's as it applies)
    /// </summary>
    internal static void Claim(Card card, int charaUid)
    {
        if (card.trait is not TraitMailPost || card.GetInt(OwnerKey) != 0 || charaUid <= 0 || !TeamBoxes.On) {
            return;
        }

        card.SetInt(OwnerKey, charaUid);
        NetSession.Instance.Connection?.Delta.AddRemote(CardSettingDelta.Create(card, CardSettingDelta.MailOwner));
    }

    /// <summary>
    ///     The host's word on a change of owner: the current owner (or anyone, while it has none) hands it to everyone
    ///     or to a player of this world
    /// </summary>
    internal static bool MayChange(Card card, Chara sender, int to)
    {
        var now = card.GetInt(OwnerKey);
        return (now == 0 || now == sender.uid) && (to == 0 || ElinNetHost.PlayerCharaUids().Contains(to));
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(TraitContainer), nameof(TraitContainer.TrySetAct))]
    private static void OnMenu(TraitContainer __instance, ActPlan p)
    {
        if (__instance is not TraitMailPost || !TeamBoxes.On || !__instance.owner.IsInstalled) {
            return;
        }

        var card = __instance.owner;
        var now = card.GetInt(OwnerKey);
        if (now != 0 && now != EClass.pc.uid) {
            return;
        }

        var name = now == 0 ? "emp_mailbox_everyone".lang() : EClass.game.cards.globalCharas.Find(now)?.NameSimple ?? "?";
        p.TrySetAct("emp_mailbox_owner".Loc(name), () => {
            var team = Teams.TeamOfZone(EClass._zone);
            var players = (team > 0 ? Teams.MembersOf(team) : ElinNetHost.PlayerCharaUids()).Append(EClass.pc.uid).Where(uid => !ServerAvatar.IsUid(uid)).Prepend(0).Distinct().ToList();
            var next = players[(players.IndexOf(now) + 1) % players.Count];
            card.SetInt(OwnerKey, next);
            NetSession.Instance.Connection?.Delta.AddRemote(CardSettingDelta.Create(card, CardSettingDelta.MailOwner));
            Msg.Say("emp_mailbox_owner".Loc(next == 0 ? "emp_mailbox_everyone".lang() : EClass.game.cards.globalCharas.Find(next)?.NameSimple ?? "?"));
            return false;
        }, card);
    }
}
