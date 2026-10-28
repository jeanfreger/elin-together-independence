using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ElinTogether.Helper;

/// <summary>
///     Elin is built to run once per machine: Unity creates a named mutex ("...SingleInstanceMutex...") at start and a
///     second Elin.exe waits on it before its engine even starts (-batchmode does not skip it, bench 2026-10-28). A
///     hidden server (HiddenServer) is a second Elin on the player's machine, so the game that starts it lets go of
///     its own handle on that mutex first: the mutex disappears with its last handle, and the new process finds the
///     way free. Nothing of the game folder is copied or patched, so a game update changes nothing here. <br />
///     Only this process's handles are looked at, the ones of the right kind ("Mutant") first and by name, and every
///     failure is a log line and false: a player whose Windows answers differently just keeps the classic hosting
/// </summary>
internal static class SingleInstance
{
    private const int SystemExtendedHandleInformation = 64;
    private const int ObjectNameInformation = 1;
    private const int ObjectTypeInformation = 2;
    private const uint StatusInfoLengthMismatch = 0xC0000004;
    private const uint StatusBufferOverflow = 0x80000005;
    private const uint StatusBufferTooSmall = 0xC0000023;
    private const string MutexName = "SingleInstanceMutex";

    // SYSTEM_HANDLE_TABLE_ENTRY_INFO_EX on a 64-bit process: object, pid, handle, access, backtrace, type index,
    // attributes, reserved
    private const int HeaderSize = 16;
    private const int EntrySize = 40;
    private const int PidOffset = 8;
    private const int HandleOffset = 16;
    private const int TypeIndexOffset = 30;  // after access (24, 4 bytes) and the backtrace index (28, 2 bytes)

    private static bool _done;

    [DllImport("ntdll.dll")]
    private static extern uint NtQuerySystemInformation(int informationClass, IntPtr buffer, int length, out int returned);

    [DllImport("ntdll.dll")]
    private static extern uint NtQueryObject(IntPtr handle, int informationClass, IntPtr buffer, int length, out int returned);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    /// <summary>
    ///     This process lets go of its Unity single-instance mutex. False when it could not (not Windows, not found, any
    ///     error), never throws. True also when it was done before
    /// </summary>
    internal static bool Release()
    {
        if (_done) {
            return true;
        }

        try {
            if (Environment.OSVersion.Platform != PlatformID.Win32NT || IntPtr.Size != 8) {
                EmpLog.Information("Single instance: not a 64-bit Windows, the mutex is left alone");
                return false;
            }

            var handle = Find();
            if (handle == IntPtr.Zero) {
                return false;
            }

            if (!CloseHandle(handle)) {
                EmpLog.Warning("Single instance: the mutex handle {Handle} could not be closed (error {Error})",
                    handle.ToInt64(), Marshal.GetLastWin32Error());
                return false;
            }

            _done = true;
            EmpLog.Information("Single instance: this process let go of its mutex (handle {Handle}), another Elin can start",
                handle.ToInt64());
            return true;
        } catch (Exception ex) {
            EmpLog.Warning(ex, "Single instance: the mutex could not be released");
            return false;
        }
    }

    /// <summary>
    ///     The handle of this process on the Unity mutex, zero when there is none
    /// </summary>
    private static IntPtr Find()
    {
        var table = IntPtr.Zero;
        try {
            var size = 1 << 20;
            uint status;
            do {
                if (table != IntPtr.Zero) {
                    Marshal.FreeHGlobal(table);
                }

                table = Marshal.AllocHGlobal(size);
                status = NtQuerySystemInformation(SystemExtendedHandleInformation, table, size, out var returned);
                // (the table grows while it is read: a margin)
                if (status == StatusInfoLengthMismatch) {
                    size = Math.Max(size * 2, returned + (1 << 16));
                }
            } while (status == StatusInfoLengthMismatch && size < (1 << 30));

            if (status != 0) {
                EmpLog.Warning("Single instance: the handle table could not be read (status {Status:X})", status);
                return IntPtr.Zero;
            }

            var count = Marshal.ReadInt64(table);
            var pid = (long)Process.GetCurrentProcess().Id;
            var mine = new List<(IntPtr Handle, int Type)>();
            for (long i = 0; i < count; i++) {
                var entry = table + HeaderSize + (int)(i * EntrySize);
                if (Marshal.ReadInt64(entry, PidOffset) == pid) {
                    mine.Add((new IntPtr(Marshal.ReadInt64(entry, HandleOffset)), Marshal.ReadInt16(entry, TypeIndexOffset)));
                }
            }

            // the type index is the system's: one handle per index is asked its type (a type is safe to ask, a name
            // is not: the name of a pipe can wait for ever, so a name is asked only of mutexes)
            var mutantIndexes = new HashSet<int>();
            var seen = new HashSet<int>();
            foreach (var (handle, type) in mine) {
                if (seen.Add(type) && Query(handle, ObjectTypeInformation) == "Mutant") {
                    mutantIndexes.Add(type);
                }
            }

            var names = new List<string>();
            foreach (var (handle, type) in mine) {
                if (!mutantIndexes.Contains(type)) {
                    continue;
                }

                var name = Query(handle, ObjectNameInformation);
                if (name is not null && name.Contains(MutexName)) {
                    return handle;
                }

                names.Add(name ?? "?");
            }

            EmpLog.Warning("Single instance: no mutex named like {Name} among {Count} mutexes of this process ({Names})",
                MutexName, names.Count, string.Join(", ", names));
            return IntPtr.Zero;
        } finally {
            if (table != IntPtr.Zero) {
                Marshal.FreeHGlobal(table);
            }
        }
    }

    /// <summary>
    ///     The type name or the name of an object, null when the system does not say
    /// </summary>
    private static string? Query(IntPtr handle, int information)
    {
        var size = 1024;
        var buffer = Marshal.AllocHGlobal(size);
        try {
            var status = NtQueryObject(handle, information, buffer, size, out var returned);
            if (status is StatusInfoLengthMismatch or StatusBufferOverflow or StatusBufferTooSmall && returned > size) {
                Marshal.FreeHGlobal(buffer);
                size = returned;
                buffer = Marshal.AllocHGlobal(size);
                status = NtQueryObject(handle, information, buffer, size, out returned);
            }

            if (status != 0) {
                return null;
            }

            // UNICODE_STRING: length in bytes, maximum length, padding, pointer to the characters
            var length = Marshal.ReadInt16(buffer);
            var chars = Marshal.ReadIntPtr(buffer, 8);
            return length == 0 || chars == IntPtr.Zero ? "" : Marshal.PtrToStringUni(chars, length / 2);
        } finally {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
