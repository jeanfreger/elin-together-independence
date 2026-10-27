using System;
using System.Linq;
using System.Reflection;

namespace ElinTogether.Helper;

/// <summary>
///     The tables the mod keeps in a world's save (who plays which character, bans, admins, teams...) are only
///     replaced when a save is loaded: a NEW world made from the title screen kept those of the world played before,
///     and a guest of the new world was matched to whatever character had the old uid (upstream 6f0fda1 did it for the
///     character table). Emptied when the game goes back to the title; a save loaded afterwards fills them again
/// </summary>
internal static class GameIOReset
{
    private const BindingFlags Static = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

    [ElinPreSceneInit]
    private static void OnTitle(Scene.Mode mode)
    {
        if (mode != Scene.Mode.Title) {
            return;
        }

        var cleared = 0;
        foreach (var type in EmpMod.Assembly.GetTypes()) {
            foreach (var property in type.GetProperties(Static)) {
                if (property.SetMethod is null || !property.IsDefined(typeof(ElinGameIOPropertyAttribute))) {
                    continue;
                }

                try {
                    // (each one makes itself again, empty, when read: field ??= [])
                    property.SetValue(null, null);
                    cleared++;
                } catch (Exception ex) {
                    EmpLog.Warning(ex, "Could not empty the saved table {Type}.{Property}", type.Name, property.Name);
                }
            }
        }

        EmpLog.Debug("Back to the title: {Count} saved tables emptied", cleared);
    }
}
