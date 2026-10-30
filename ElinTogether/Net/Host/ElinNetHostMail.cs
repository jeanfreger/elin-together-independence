using System.Collections.Generic;
using ElinTogether.Helper;

namespace ElinTogether.Net;

/// <summary>
///     Host box "Mailbox" (<see cref="Mailbox" />): the letters between players, kept in the host's world save. The
///     items a letter holds left the sender's bag when it was sent and exist only here, as the game writes a thing,
///     until their recipient takes them. An older save has none
/// </summary>
internal partial class ElinNetHost
{
    /// <summary>
    ///     Letter id -> its row (<see cref="Mailbox.Row" />): recipient chara uid, sender chara uid, sender account,
    ///     when it was sent (real time, seconds since 1970), flags, gold, text, sender name, then one text per stack
    /// </summary>
    [ElinGameIOProperty("mail_letters")]
    internal static Dictionary<int, string[]> MailLetters
    {
        get => field ??= [];
        set;
    }

    /// <summary>
    ///     The last letter id ever given: a deleted letter's id is never given again
    /// </summary>
    [ElinGameIOProperty("mail_last_id")]
    internal static int MailLastId
    {
        get;
        set;
    }
}
