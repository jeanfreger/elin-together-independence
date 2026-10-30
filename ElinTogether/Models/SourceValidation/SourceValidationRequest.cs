using System.Collections.Generic;
using MessagePack;

namespace ElinTogether.Models;

[MessagePackObject]
public class SourceValidationRequest
{
    [Key(0)]
    public required List<string> SourceNames { get; init; }

    [Key(1)]
    public required List<string> FilePaths { get; init; }

    [Key(2)]
    public int ValidationFlags { get; init; }

    /// <summary>
    ///     The mods of the game, as modlist.txt (ModList.Reference). Null: the host does not publish them
    /// </summary>
    [Key(3)]
    public string? Mods { get; init; }

    /// <summary>
    ///     The mods the host accepts on a player's side only (ClientOnlyMods, comma separated): left out of the
    ///     plugin hashes and of the comparison of the mods. An older host sends none
    /// </summary>
    [Key(4)]
    public string? ClientOnlyMods { get; init; }
}