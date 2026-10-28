"""Writes the official server's rule values into a copy of the bench host config, so the bench can run its tests
"on the official server settings" (plan_official_server 2026-10-28, section 1).

usage: python official_bench_config.py [--with-public] [source.cfg [target.cfg]]
  default source: dev/_tools/bench_host_config.cfg   default target: dev/_tools/bench_host_config_official.cfg
  The source is never changed. PublicGame is left as the source has it unless --with-public (a bench host should
  not open a public lobby by accident). Keys the source does not have yet are added at the end of [Server].

The values are SettingsTable.OfficialFixedValues + OfficialChosenValues of the mod (same list, same order); keep both
in step. StrictValidationMode is not written (the plan turns it on after stage 1, council pending).
In the bench the "official" limits themselves (greyed boxes, fixed rules) need OfficialServer.BenchForce, which
bench/official_preset_check.py sets by reflection.
"""
import sys
from pathlib import Path

TOOLS = Path(r"C:\Users\ciryl\Documents\ElinMods\ElinTogether\dev\_tools")

# section [Server]; the key is the config key (two differ from the entry name: SourceValidation, AskCharacter)
FIXED = {
    "ImportCharacter": "false", "ExportCharacter": "false",
    "TeamOwnStory": "true", "TeamKnowledge": "true", "TeamBoxes": "true",
    "WorldCopy": "false", "Takeover": "false", "AutoHost": "false", "HiddenServer": "false",
    "ServerAvatar": "true", "ServerRestsWhenEmpty": "true", "PublicGame": "true",
    "SameGameVersion": "true", "ContainerAccessLog": "true", "AdminExtras": "true",
    "Teams": "true", "TeamBases": "true", "PlayerKill": "false",
    # the 14 fallback boxes
    "PersonalQuests": "true", "PlayerShipping": "true", "OncePerPlayer": "true", "SharedTax": "true",
    "TownLife": "true", "TownBoards": "true", "ShopsFromEveryPlayer": "true", "NearestSpawns": "true",
    "ActivitiesFromActor": "true", "AutoCombatPerPlayer": "true", "PlayersAtBase": "true",
    "SummonsFollowMaster": "true", "GlobalChat": "true", "OwnJournal": "true",
}

CHOSEN = {
    "SourceValidation": "sources,plugins", "AskCharacter": "true", "MaxCharacters": "3",
    "DeleteCharacter": "true", "PermadeathRevive": "true", "KickKeepOutMinutes": "60",
    "PublishMods": "true", "LookModsOwn": "false", "MembersOnly": "false",
    "IndependentTravel": "true", "WorldMapTime": "PersonalPace", "WorldMapLeadCap": "24",  # (C) council pending
    "PlayerCombatTime": "true", "PlayerClock": "true", "PlayerStepPace": "true", "SharedAverageSpeed": "false",
    "SharedWorldTime": "true", "WorldKeeper": "true", "OwnSleep": "true", "NightPasses": "Half",
    "IdleMinutes": "5", "TimeStopsWhenEmpty": "true", "AbsentQuestsWait": "true", "CatchUpDays": "7",
    "LivingWorldMinutes": "0", "GuestBuild": "true", "HostManagesBase": "false",
    "Duels": "true", "GuestBouts": "true", "DuelArena": "true", "DuelBets": "false",
    "PlayerTrade": "true", "Follow": "true", "PrivateChat": "true", "GuestLandingPortal": "true",
    "GuestSkyHelm": "true", "SharedMarriages": "true", "AffinityPerPlayer": "true",
    "GuestMoongate": "false",  # (C) council pending
    "TeamApproval": "true", "TeamDeed": "true", "TeamLeaders": "true", "TeamMaxSize": "10",
    "TeamFoundCooldown": "30", "TeamPersonal": "true", "LandRules": "true", "LandAbandon": "true",
    "AbandonDays": "30", "LandsPerTeam": "2", "SiblingLands": "false", "SpawnZone": "start",
    "TeamPvp": "false", "TeamBills": "true", "TeamStory": "true", "TeamOwnGuilds": "true",
    "AutoReconnect": "true", "AutoResync": "true", "SoftRecall": "true", "AutoSave": "true",
    "EnforceOwnership": "true",
}


def write(source: Path, target: Path, with_public: bool) -> int:
    values = {**FIXED, **CHOSEN}
    if not with_public:
        values.pop("PublicGame")
    lines = source.read_text(encoding="utf-8").splitlines()
    out, section, done, last_server = [], None, set(), -1
    for line in lines:
        stripped = line.strip()
        if stripped.startswith("[") and stripped.endswith("]"):
            section = stripped[1:-1]
        elif section == "Server" and "=" in line and not stripped.startswith("#"):
            key = line.split("=", 1)[0].strip()
            if key in values:
                line = f"{key} = {values[key]}"
                done.add(key)
        out.append(line)
        if section == "Server":
            last_server = len(out)
    missing = [k for k in values if k not in done]
    if missing:
        out[last_server:last_server] = [f"{k} = {values[k]}" for k in missing]
    target.write_text("\n".join(out) + "\n", encoding="utf-8")
    print(f"{len(done)} values changed in place, {len(missing)} added -> {target}")
    return 0


if __name__ == "__main__":
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    src = Path(args[0]) if args else TOOLS / "bench_host_config.cfg"
    dst = Path(args[1]) if len(args) > 1 else TOOLS / "bench_host_config_official.cfg"
    sys.exit(write(src, dst, "--with-public" in sys.argv))
