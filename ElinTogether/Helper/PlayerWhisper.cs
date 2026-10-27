using System.Linq;
using ElinTogether.Models;
using ElinTogether.Net;
using ElinTogether.Patches;
using HarmonyLib;

namespace ElinTogether.Helper;

/// <summary>
///     "Talk" on another player's character: the game's chat box, for that player alone. The line goes to it wherever it
///     is and shows like the chat, in the pop text, never in the log (user, 2026-10-24); a host option, the host
///     passes lines on without showing them (llm-council 2026-10-24)
/// </summary>
[HarmonyPatch]
internal static class PlayerWhisper
{
    internal static bool Enabled => NetSession.Instance.Rules.PrivateChat;

    private static string NameOf(int uid, string? fallback)
    {
        var state = NetSession.Instance.CurrentPlayers.FirstOrDefault(p => p.CharaUid == uid);
        // (a player without a name of its own: its character's)
        return state is null ? fallback ?? "?" : state.User.Name.IsEmpty(fallback ?? "?").TagColor(PeerColorizer.GetColor(state.Index));
    }

    /// <summary>
    ///     The game's chat box (Dialog.InputName), the hint saying who the line is for
    /// </summary>
    internal static void Open(Chara other)
    {
        var name = NameOf(other.uid, other.Name);
        if (!other.IsPlayer) {
            ChatBubbleEvent.PopChat(name, "emp_whisper_offline".lang(name));
            return;
        }

        // (Dialog.InputName for the chat, with the hint set before the window shows)
        var d = Layer.Create<Dialog>("DialogInput");
        d.inputType = Dialog.InputType.Chat;
        d.langHint = "emp_whisper_with".lang(name);
        d.input.field.characterLimit = 500;
        d.input.Text = "";
        d.onEnterInput = (cancel, text) => {
            if (!cancel && !text.IsEmpty()) {
                Send(other, text);
            }
        };
        d.list.AddButton(null, Lang.Get("ok"), () => {
            d.onEnterInput(false, d.input.Text);
            d.Close();
        });
        d.list.AddButton(null, Lang.Get("cancel"), () => d.Close());
        EClass.ui.AddLayer(d);
    }

    internal static void Send(Chara other, string text)
    {
        var name = NameOf(other.uid, other.Name);
        if (!other.IsPlayer) {
            ChatBubbleEvent.PopChat(name, "emp_whisper_offline".lang(name));
            return;
        }

        var delta = new WhisperDelta {
            FromUid = EClass.pc.uid,
            ToUid = other.uid,
            Text = text,
            Name = EClass.pc.RemoteState?.User.Name ?? EClass.pc.Name,
        };
        switch (NetSession.Instance.Transport) {
            case ElinNetHost host:
                Relay(host, delta);
                break;
            case ElinNetClient up:
                // (straight to the host, also from a map of one's own)
                up.SendWhileAway(delta);
                break;
            default:
                return;
        }

        ChatBubbleEvent.PopChat("emp_whisper_to".lang(name), text);
    }

    internal static void Relay(ElinNetHost host, WhisperDelta delta)
    {
        // (a player away from the host's map included: it is still a peer of the host)
        var peers = host.PeerIds.Where(id => host.PlayerChara(id)?.uid == delta.ToUid).ToList();
        if (peers.Count == 0 || !host.SendDeltaTo(peers[0], delta)) {
            EmpLog.Debug("Private chat line for {Uid}: that player is not connected", delta.ToUid);
        }
    }

    internal static void Receive(WhisperDelta delta)
    {
        var name = NameOf(delta.FromUid, delta.Name);
        ChatBubbleEvent.PopChat("emp_whisper_from".lang(name), delta.Text);
    }

    /// <summary>
    ///     "Talk" in the menu on another player's character
    /// </summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(ActPlan), nameof(ActPlan._Update))]
    internal static void OnUpdatePlan(ActPlan __instance)
    {
        // (runs at every frame the cursor is on the map: no copy of the list unless there is something to replace)
        if (!Enabled || !__instance.list.Exists(i => i.act is ActChat && i.tc is Chara { IsRemotePlayer: true })) {
            return;
        }

        foreach (var item in __instance.list.ToArray()) {
            if (item.act is not ActChat || item.tc is not Chara { IsRemotePlayer: true } other) {
                continue;
            }

            __instance.list.Remove(item);
            __instance.TrySetAct("emp_act_talk", () => {
                Open(other);
                return false;
            }, other, null, 2);
        }
    }

    /// <summary>
    ///     "Talk" in the window of a member of the base (LayerInteraction)
    /// </summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(LayerInteraction), nameof(LayerInteraction.GetPage))]
    internal static void OnGetPage(IInspect o, LayerInteraction.Page __result)
    {
        if (!Enabled || o is not Chara { IsRemotePlayer: true } other) {
            return;
        }

        foreach (var item in __result.items.Where(i => i.text == "tTalk".lang())) {
            item.action = () => Open(other);
        }
    }
}
