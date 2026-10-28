namespace ElinTogether.Common;

public static class EmpConstants
{
    /// <summary>
    ///     For debugging only
    /// </summary>
    public static readonly int LocalPort =
#if DEBUG
        // a second bench on the same PC hosts on its own port (dev/BENCH2.md)
        int.TryParse(System.Environment.GetEnvironmentVariable("EMP_LOCAL_PORT"), out var port) && port > 0 ? port :
#endif
        55556;

    /// <summary>
    ///     Max batched messages per poll
    /// </summary>
    public const int MaxBatchedMessages = 8;

    /// <summary>
    ///     Times per second for delta dispatching
    /// </summary>
    public const int DeltaDispatchFrequency = 10;

    public const string EmpThingSplitContext = "emp_split_context";

    public const string EmpThingSplitCount = "emp_split_num";
}