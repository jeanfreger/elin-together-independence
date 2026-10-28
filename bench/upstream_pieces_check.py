"""Pieces taken from upstream 6f0fda1, checked on a live pair (host + 1 client, 2 windows):
(1) the shared speed is refreshed at the snapshot tick, so the guest sees what the host computes;
(2) no saved remote character is the host's own pc (PreparePlayerJoin discards such an entry).
usage: python bench/upstream_pieces_check.py"""
import sys

sys.path.insert(0, r"C:\Users\ciryl\Documents\ElinMods\ElinTogether\dev\_tools")
from travel_suite import check, ev, eventually  # noqa: E402

H, A = 27551, 27552


def shared(port):
    return int(ev(port, 'return ((int)NetSession.Instance.SharedSpeed).ToString();'))


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    check("host shared speed is not 0 (a speed, or -1 when the rule is off)", eventually(lambda: shared(H) != 0, timeout=20))
    check("guest sees the same shared speed", eventually(lambda: shared(A) == shared(H), timeout=20))
    saved = ev(H, 'var p = typeof(ElinNetHost).GetProperty("SavedRemoteCharas", System.Reflection.BindingFlags.NonPublic | '
                  'System.Reflection.BindingFlags.Static); var d = p.GetValue(null) as System.Collections.IDictionary; '
                  'var sb = new System.Text.StringBuilder(); foreach (System.Collections.DictionaryEntry e in d) '
                  'sb.Append(e.Value).Append((char)44); return sb.ToString();')
    pcuid = ev(H, 'return EClass.pc.uid.ToString();')
    check(f"no saved remote chara is the host's pc ({saved} vs {pcuid})", pcuid not in saved.split(","))


if __name__ == "__main__":
    main()
