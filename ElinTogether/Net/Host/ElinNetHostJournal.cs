using System.Collections.Generic;

namespace ElinTogether.Net;

internal partial class ElinNetHost
{
    /// <summary>
    ///     Host box "Activity journal" (<see cref="Helper.ActivityJournal" />): the ring of the world's last events,
    ///     number -> the entry as text. Empty in a save older than the box
    /// </summary>
    [ElinGameIOProperty("activity_journal")]
    internal static Dictionary<int, string> ActivityJournalBook
    {
        get => field ??= [];
        set;
    }

    /// <summary>
    ///     Key 0: the next entry number. Player character uid -> its deepest danger level the journal has seen
    /// </summary>
    [ElinGameIOProperty("activity_journal_state")]
    internal static Dictionary<int, int> ActivityJournalState
    {
        get => field ??= [];
        set;
    }
}
