using System.Collections.Generic;
using MessagePack;

namespace ElinTogether.Models;

/// <summary>
///     Net packet: Host -> Client, the characters this player has in this world, to pick one
/// </summary>
[MessagePackObject]
public class SessionCharaSelectRequest
{
    [Key(0)]
    public required List<SessionCharaEntry> Charas { get; init; }

    /// <summary>
    ///     The host lets players bring the character of one of their own saves
    /// </summary>
    [Key(1)]
    public bool AllowImport { get; init; }

    /// <summary>
    ///     Lang id of what to tell the player first, when its last answer was turned down
    /// </summary>
    [Key(2)]
    public string? Notice { get; init; }

    [Key(3)]
    public bool AllowDelete { get; init; }

    /// <summary>
    ///     The host lets players take one of these characters to one of their own solo saves (ExportCharacter)
    /// </summary>
    [Key(4)]
    public bool AllowExport { get; init; }

    /// <summary>
    ///     The player has as many characters here as the host allows (MaxCharacters): no "new character" line. An
    ///     older host sends none: the line is there and the host turns the click down as before
    /// </summary>
    [Key(5)]
    public bool AtLimit { get; init; }
}

[MessagePackObject]
public class SessionCharaEntry
{
    [Key(0)]
    public required int Uid { get; init; }

    /// <summary>
    ///     Name, race, job and level, as the host words them
    /// </summary>
    [Key(1)]
    public required string Label { get; init; }

    /// <summary>
    ///     Dead in a permadeath world, waiting for a revive at a bartender (PermadeathRevive): shown, not playable
    /// </summary>
    [Key(2)]
    public bool Dead { get; set; }

    /// <summary>
    ///     Taken to a solo save (CharaExport): which one, as the player named it. Shown, not playable, until it comes
    ///     back through the import or is brought back unchanged
    /// </summary>
    [Key(3)]
    public string? Away { get; set; }

    /// <summary>
    ///     The id of its portrait (Chara.GetIdPortrait), shown beside its name when this game has that picture
    /// </summary>
    [Key(4)]
    public string? Portrait { get; set; }
}

/// <summary>
///     Net packet: Client -> Host
/// </summary>
[MessagePackObject]
public class SessionCharaSelectResponse
{
    /// <summary>
    ///     0 for a new character
    /// </summary>
    [Key(0)]
    public required int Uid { get; init; }

    /// <summary>
    ///     Not to play that character: to delete it (asked twice on the player's side)
    /// </summary>
    [Key(1)]
    public bool Delete { get; init; }
}

/// <summary>
///     Net packet: Client -> Host, the character of one of the player's own saves, to play in this world
/// </summary>
[MessagePackObject]
public class SessionCharaImportResponse
{
    [Key(0)]
    public required LZ4Bytes Chara { get; init; }

    [Key(1)]
    public required int Fame { get; init; }

    [Key(2)]
    public required int Karma { get; init; }

    /// <summary>
    ///     Which save it comes from: one copy of each per player
    /// </summary>
    [Key(3)]
    public required string Source { get; init; }
}


/// <summary>
///     Net packet: Client -> Host, take that character to one of the player's own solo saves, or bring one that is
///     there back unchanged (council 2026-10-27, character export)
/// </summary>
[MessagePackObject]
public class SessionCharaExportRequest
{
    [Key(0)]
    public required int Uid { get; init; }

    /// <summary>
    ///     The save it goes to, as the player's list words it (for the list and the admin log only)
    /// </summary>
    [Key(1)]
    public string? Save { get; init; }

    /// <summary>
    ///     Not to take it: to bring it back as it left, the copy in the solo save can no longer come back
    /// </summary>
    [Key(2)]
    public bool Recall { get; init; }
}

/// <summary>
///     Net packet: Host -> Client, the host's own copy of the character that leaves, with its one-time token. Also
///     what the player's game keeps in ElinMP/Incoming until the solo save is loaded
/// </summary>
[MessagePackObject]
public class SessionCharaExportData
{
    [Key(0)]
    public required int Uid { get; init; }

    [Key(1)]
    public required LZ4Bytes Chara { get; init; }

    [Key(2)]
    public required int Fame { get; init; }

    [Key(3)]
    public required int Karma { get; init; }

    [Key(4)]
    public required string Token { get; init; }
}

/// <summary>
///     Net packet: Client -> Host, whether the character was written beside the solo save. Not written: the host
///     releases it, it never left
/// </summary>
[MessagePackObject]
public class SessionCharaExportResult
{
    [Key(0)]
    public required int Uid { get; init; }

    [Key(1)]
    public required string Token { get; init; }

    [Key(2)]
    public required bool Written { get; init; }
}
