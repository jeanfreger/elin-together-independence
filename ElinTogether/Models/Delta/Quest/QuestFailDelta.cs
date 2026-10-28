using ElinTogether.Helper;
using ElinTogether.Net;
using ElinTogether.Patches;
using MessagePack;

namespace ElinTogether.Models;

/// <summary>
///     A quest failed (expired, mostly): gone from the one quest log, for everyone
/// </summary>
[MessagePackObject]
public class QuestFailDelta : ElinDelta
{
    internal override bool InSendersStory => true;

    [Key(0)]
    public required int Uid { get; init; }

    /// <summary>
    ///     Security audit 2026-10-27 (item 5): a failure costs everyone karma and fame. A guest on the host's map never
    ///     says one (the host fails it at the same hour, QuestFailEvent): only a player away in its own zones may
    /// </summary>
    internal override string? OnHostPermit(ElinNetHost host, Chara? sender)
    {
        return host.IsAwayPeer(OriginPeer) ? null : $"quest {Uid} failed by a guest on the host's map";
    }

    protected override void OnApply(ElinNetBase net)
    {
        var quest = SharedQuests.Find(Uid, null);
        if (quest is null || !game.quests.list.Contains(quest)) {
            return;
        }

        if (net is ElinNetHost) {
            // failed in the copy of a player travelling alone: what a failure costs is the world's, and
            // everyone has to know
            using (Simulate()) {
                quest.Fail();
            }

            return;
        }

        QuestFailEvent.FailQuietly(quest);
    }
}
