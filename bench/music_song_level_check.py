"""Host/guest audit item 1: a guest's music is judged at the level of its song on the host. Needs 2 windows
(host 27551 + guest 27552). usage: python bench/music_song_level_check.py

What it proves: (1) AI_PlayMusic.Run (its iterator) carries the mod's transpiler on the host; (2) the guest's
AIPlayMusicArgs carries the levels of its known songs (SongLv), and the host's side (NewSong, what replaces the game's
"new KnownSong()") gives a song of that level to a replayed task and level 0 to a task without the levels.
Not tested: a full concert (mistake rolls are random).
"""
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent))
sys.path.insert(0, r"C:\Users\ciryl\Documents\ElinMods\ElinTogether\dev\_tools")
from travel_suite import check, ev  # noqa: E402

H, A = 27551, 27552
T = lambda n: f'HarmonyLib.AccessTools.TypeByName("{n}")'
PATCH = T("ElinTogether.Patches.AIPlayMusicPatch")
ARGS = T("ElinTogether.Models.AI.AIPlayMusicArgs")


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    ok = total = 0

    def verify(label, good):
        nonlocal ok, total
        total += 1
        ok += bool(good)
        check(label, good)

    mn = ('var m = HarmonyLib.AccessTools.EnumeratorMoveNext(HarmonyLib.AccessTools.Method(typeof(AI_PlayMusic), "Run")); '
          'return string.Join(",", HarmonyLib.Harmony.GetPatchInfo(m).Transpilers.Select(t => t.PatchMethod.Name));')
    got = ev(H, mn)
    verify(f"host: Run carries the song-level transpiler ({got})", "OnRun" in got)

    # the guest knows a song at level 77: its args carry it
    got = ev(A, 'EClass.player.knownSongs["bench_song"] = new KnownSong { lv = 77 }; '
                'var t = new AI_PlayMusic { tool = ThingGen.Create("lute") }; '
                f'var a = HarmonyLib.AccessTools.Method({ARGS}, "Create").Invoke(null, new object[] {{ t }}); '
                'var d = (System.Collections.Generic.Dictionary<string, int>)a.GetType().GetProperty("SongLv").GetValue(a); '
                'return d["bench_song"].ToString();')
    verify(f"guest: the args carry the song level ({got})", got == "77")

    # the host: a task given the levels yields a song of that level, one without them level 0
    got = ev(H, 'var t = new AI_PlayMusic(); '
                'var d = new System.Collections.Generic.Dictionary<string, int> { ["bench_song"] = 77 }; '
                f'HarmonyLib.AccessTools.Method({PATCH}, "NoteSongLv").Invoke(null, new object[] {{ t, d }}); '
                f'var s1 = (KnownSong)HarmonyLib.AccessTools.Method({PATCH}, "NewSong").Invoke(null, new object[] {{ t, "bench_song" }}); '
                f'var s2 = (KnownSong)HarmonyLib.AccessTools.Method({PATCH}, "NewSong").Invoke(null, new object[] {{ new AI_PlayMusic(), "bench_song" }}); '
                'return s1.lv + "," + s2.lv;')
    verify(f"host: NewSong gives level 77 with the levels, 0 without ({got})", got == "77,0")
    print(f"{ok}/{total} verifications OK")


if __name__ == "__main__":
    main()
