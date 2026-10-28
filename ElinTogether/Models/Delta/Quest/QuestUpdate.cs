using ElinTogether.Helper;
using ElinTogether.Net;
using ElinTogether.Patches;
using MessagePack;

namespace ElinTogether.Models;

/// <summary>
///     What a quest of the shared log holds, besides its phase, changed somewhere
/// </summary>
[MessagePackObject]
public class QuestUpdateDelta : ElinDelta
{
    internal override bool InSendersStory => true;

    [Key(0)]
    public required LZ4Bytes Data { get; init; }

    [Key(1)]
    public required int Uid { get; init; }

    /// <summary>
    ///     Tells which quest it is while a quest started by a dialog on a client has no number from the host yet
    /// </summary>
    [Key(2)]
    public string? Id { get; init; }

    protected override void OnApply(ElinNetBase net)
    {
        var quest = SharedQuests.Find(Uid, Id);
        if (quest is null || !game.quests.list.Contains(quest)) {
            return;
        }

        SharedQuests.CopyState(Data.Decompress<Quest>(), quest);
        SharedQuests.Remember(quest);

        if (net is not ElinNetHost host) {
            return;
        }

        host.SendDeltaToAllExcept(OriginPeer, new QuestUpdateDelta {
            Data = Data,
            Uid = quest.uid,
            Id = quest.id,
        });
        // hosting a zone as a client: the host of the world has to know too
        QuestAwaySync.Send(this);
    }
}
