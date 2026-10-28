using System.Collections.Generic;
using ElinTogether.Common;

namespace ElinTogether.Net;

/// <summary>
///     Container access log (council 2026-10-28, see <see cref="Helper.ContainerLog" />), kept in the host's save
///     like the other tables: bounded, plain text
/// </summary>
internal partial class ElinNetHost
{
    /// <summary>
    ///     Container uid -> its last entries, one per line: date, who, signed count (put +, taken -), item
    /// </summary>
    [ElinGameIOProperty("container_log")]
    internal static Dictionary<int, string> ContainerLogs
    {
        get => field ??= [];
        set;
    }

    /// <summary>
    ///     Container uid -> its name and where it stands, for the admins' list
    /// </summary>
    [ElinGameIOProperty("container_log_names")]
    internal static Dictionary<int, string> ContainerLogNames
    {
        get => field ??= [];
        set;
    }
}
