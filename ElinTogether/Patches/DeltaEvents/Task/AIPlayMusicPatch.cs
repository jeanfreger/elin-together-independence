using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Reflection.Emit;
using ElinTogether.Helper;
using HarmonyLib;

namespace ElinTogether.Patches;

[HarmonyPatch]
internal static class AIPlayMusicPatch
{
    private static readonly ConditionalWeakTable<AI_PlayMusic, Dictionary<string, int>> _songLv = new();

    internal static void NoteSongLv(AI_PlayMusic task, Dictionary<string, int>? songs)
    {
        if (songs is not null) {
            _songLv.Remove(task);
            _songLv.Add(task, songs);
        }
    }

    internal static KnownSong NewSong(AI_PlayMusic task, string idSong)
    {
        // the game gives another player's character a new song of level 0 each time; its own game knows the level
        var song = new KnownSong();
        if (_songLv.TryGetValue(task, out var songs) && songs.TryGetValue(idSong, out var lv)) {
            song.lv = lv;
        }

        return song;
    }

    [HarmonyTranspiler]
    [HarmonyPatch(typeof(AI_PlayMusic), nameof(AI_PlayMusic.Evaluate))]
    internal static IEnumerable<CodeInstruction> OnEvaluate(IEnumerable<CodeInstruction> instructions)
    {
        return new CodeMatcher(instructions)
            .MatchStartForward(
                new CodeMatch(OpCodes.Callvirt, AccessTools.PropertyGetter(typeof(Card), nameof(Card.IsPC))))
            .SetInstructionAndAdvance(
                Transpilers.EmitDelegate((Chara chara) => chara.IsPlayer))
            .InstructionEnumeration();
    }

    [HarmonyTranspiler]
    [HarmonyPatch(typeof(AI_PlayMusic), nameof(AI_PlayMusic.ThrowReward))]
    internal static IEnumerable<CodeInstruction> OnThrowReward(IEnumerable<CodeInstruction> instructions)
    {
        return new CodeMatcher(instructions)
            .MatchStartForward(
                new CodeMatch(OpCodes.Callvirt, AccessTools.PropertyGetter(typeof(Card), nameof(Card.IsPC))))
            .Repeat(cm => cm.SetInstructionAndAdvance(
                Transpilers.EmitDelegate((Chara chara) => chara.IsPlayer)))
            .InstructionEnumeration();
    }
    /// <summary>
    ///     The audience of someone who plays music: the game leaves "the player" out of it, and so are the other
    ///     players. Their characters threw coins at the musician like the people of the town (reported from a real game)
    /// </summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(Point), nameof(Point.ListWitnesses))]
    internal static void OnListWitnesses(WitnessType type, List<Chara> __result)
    {
        if (type == WitnessType.music) {
            __result.RemoveAll(c => c.IsPlayer);
        }
    }

    /// <summary>
    ///     A song the player plays is judged by its level on the copies too: the game makes a fresh one for any
    ///     character that is not "the player"
    /// </summary>
    [HarmonyPatch]
    internal static class RunPatch
    {
        private static readonly MethodBase RunBody =
            AccessTools.EnumeratorMoveNext(AccessTools.Method(typeof(AI_PlayMusic), nameof(AI_PlayMusic.Run)));

        [HarmonyTargetMethod]
        internal static MethodBase TargetMethod() => RunBody;

        // a postfix, not a transpiler: idSong is not kept on the state machine (it is a plain local of MoveNext), the
        // song is (it lives across the yields), and the playing song names its id right after it is made
        private static readonly ConditionalWeakTable<KnownSong, object> Seeded = new();
        private static readonly FieldInfo? This = AccessTools.Field(RunBody.DeclaringType, "<>4__this");
        private static readonly FieldInfo? Song = RunBody.DeclaringType!
            .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(f => f.Name.StartsWith("<song>"));

        [HarmonyPostfix]
        internal static void OnRun(object __instance)
        {
            if (This?.GetValue(__instance) is not AI_PlayMusic { playing.id: { } id } task ||
                Song?.GetValue(__instance) is not KnownSong song ||
                Seeded.TryGetValue(song, out _)) {
                return;
            }

            Seeded.Add(song, true);
            if (song.lv == 0 && _songLv.TryGetValue(task, out var songs) && songs.TryGetValue(id, out var lv)) {
                song.lv = lv;
            }
        }
    }
}
