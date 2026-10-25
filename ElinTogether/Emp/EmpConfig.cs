using System.IO;
using System.Text.RegularExpressions;
using BepInEx.Configuration;
using ElinTogether.Net;
using ReflexCLI.Attributes;
using UnityEngine;

namespace ElinTogether;

[ConsoleCommandClassCustomizer("emp")]
internal partial class EmpConfig
{
    /// <summary>
    ///     A config file of the mod from before the council of 2026-10-23 on world map time has the checkbox
    ///     TimeJumpsTogether and no WorldMapTime: on is Travel Together, off was every step moving the date
    ///     (Shared Clock). A new install has neither and takes the default, Shared Clock
    /// </summary>
    internal static WorldMapTime? OldWorldMapTime(string configText)
    {
        if (Regex.IsMatch(configText, @"^\s*WorldMapTime\s*=", RegexOptions.Multiline)) {
            return null;
        }

        var old = Regex.Match(configText, @"^\s*TimeJumpsTogether\s*=\s*(\w+)", RegexOptions.Multiline);
        if (!old.Success) {
            return null;
        }

        return old.Groups[1].Value.Equals("true", System.StringComparison.OrdinalIgnoreCase)
            ? Net.WorldMapTime.TravelTogether
            : Net.WorldMapTime.SharedClock;
    }

    internal static void Bind()
    {
        var config = EmpMod.Instance.Config;

        // what an older version of the mod left in the file, read before any Bind saves it again
        var before = File.Exists(config.ConfigFilePath) ? File.ReadAllText(config.ConfigFilePath) : "";

        Policy.Verbose = config.Bind(
            "RuntimePolicy",
            "Verbose",
#if DEBUG || true
            // enabled for beta builds
            true,
#else
            false,
#endif
            "Verbose information that may be helpful(spamming) for debugging\n" +
            "Enabled for beta testing by default\n" +
            "Debug用的信息输出\n" +
            "Beta测试版默认启用\n" +
            "デバッグ用の詳細情報を出力(大量ログ発生の可能性あり)");

        Policy.Timeout = config.Bind(
            "RuntimePolicy",
            "Timeout",
            15f,
            new ConfigDescription(
                "Timeout in seconds for any requests\n" +
                "Retry attempts will not be made after timeout\n" +
                "网络请求的最大超时\n" +
                "超时后，将不会重新请求",
                new AcceptableValueRange<float>(1f, 60f)));

        Policy.Retries = config.Bind(
            "RuntimePolicy",
            "Retries",
            1,
            new ConfigDescription(
                "Retries attempts after a failed request\n" +
                "请求失败后的重试次数",
                new AcceptableValueRange<int>(0, 5)));

        Client.DepotPath = config.Bind(
            "Client",
            "DepotPath",
            "",
            "Folder of the save depot: a folder every player of the group can reach (shared or synced), which keeps the world. Empty: no depot\n" +
            "The first player takes the world from it and hosts, every save goes back to it, the next player can take over\n" +
            "Also the address of Elin Together Server (host:55557), or a private GitHub repository: github:owner/repository\n" +
            "存档仓库文件夹：小组所有玩家都能访问的文件夹（共享或同步），用于保存世界。留空：不使用");

        Client.DepotPassword = config.Bind(
            "Client",
            "DepotPassword",
            "",
            "Password of the save depot, when it is Elin Together Server and one was set there\n" +
            "For a GitHub depot: the access key (fine-grained token, this repository only, Contents: read and write). Never share this file");

        Client.ServerAddress = config.Bind(
            "Client",
            "ServerAddress",
            "",
            "Address of the server joined last with \"Join by address\", as host:port (the port is 55556)\n" +
            "上次通过“按地址加入”连接的服务器地址，格式为 主机:端口（端口为 55556）");

        Client.FetchMods = config.Bind(
            "Client",
            "FetchMods",
            true,
            "When the game joined (or the world taken from the depot) has Workshop mods that are not loaded here, download them without subscribing,\n" +
            "restart Elin with exactly the mods of that game for one session, and come back to the game by itself\n" +
            "Your own mod list is back at the next start. Untick it to keep your own mods: you are then only told which mods differ\n" +
            "These mods are chosen by the host (or by the modlist.txt of the depot) and their code runs on this PC: only with people you trust\n" +
            "这些模组由主机（或仓库的 modlist.txt）决定，其代码会在本机运行：仅与你信任的人一起使用\n" +
            "加入的游戏（或从仓库取得的世界）有本机未加载的创意工坊模组时：不订阅直接下载，以该游戏的模组重启 Elin 一次并自动回到游戏；下次启动恢复你自己的模组列表。取消勾选则保留自己的模组，只提示差异");

        Client.KeepMods = config.Bind(
            "Client",
            "KeepMods",
            false,
            "Keep the mods of the game you join: your Steam account subscribes to them on the Workshop and they stay on in your own mod list\n" +
            "Elin still restarts once, the first time. After that, joining the same game again needs no restart, even after closing Elin\n" +
            "Off: they are fetched without subscribing and your own list is back at each start, so Elin restarts at each first join\n" +
            "To undo: unsubscribe from them on the Workshop, or switch them off in the Mod Viewer. Only with \"Fetch the mods of the game by itself\"\n" +
            "保留所加入游戏的模组：你的 Steam 账号会在创意工坊订阅它们，并在你自己的模组列表中保持启用。第一次仍会重启一次，之后再次加入同一游戏无需重启。关闭时：不订阅直接获取，每次启动后第一次加入都会重启");

        Client.PingKeybind = config.Bind(
            "Client",
            "PingKeybind",
            KeyCode.P,
            "Keybind for pinging map\n" +
            "键盘键位用于标记一处地点");

        Server.SourceValidationSet = config.Bind(
            "Server",
            "SourceValidation",
            "",
            "Source validation sets.\n" +
            "  none (or empty): skip all validation\n" +
            "  sources: validate Elin source table checksums\n" +
            "  plugins: validate plugin DLL hashes\n" +
            "  files: validate configured file hashes\n" +
            "  all: enable all checks\n" +
            "Combinations: \"source,plugin\" \"plugin,\" etc.\n" +
            "File paths: append \":path1,path2\" after flags, e.g. \"file:Data/xxx.json\"\n" +
            "File must be the last set\n" +
            "源表校验类型组合：none=跳过 source=源表 plugin=插件DLL file=文件\n" +
            "file必须放置于最后");

        Server.StrictValidationMode = config.Bind(
            "Server",
            "StrictValidationMode",
            false,
            "Server & Client integrity validation mode.\n" +
            "Set to true to block any mismatching clients from joining\n" +
            "双端数据完整性校验模式\n" +
            "开启时禁止任何不匹配的客机加入");

        Server.SharedAverageSpeed = config.Bind(
            "Server",
            "SharedAverageSpeed",
            false,
            "Share an averaged speed for all players\n" +
            "Otherwise each player will have their own speed\n" +
            "所有玩家共享平均速度\n" +
            "否则所有人按各自速度行动");

        Server.TurnBasedCombat = config.Bind(
            "Server",
            "TurnBasedCombatMode",
            true,
            "Players take turns in combat\n" +
            "战斗中玩家轮流行动");

        Server.IndependentTravel = config.Bind(
            "Server",
            "IndependentTravel",
            true,
            "Clients may travel to other zones on their own\n" +
            "The zone is simulated by that client and sent back to the host when leaving\n" +
            "客机可以独自前往其他地图，由该客机模拟，离开时回传给主机");

        Server.SameGameVersion = config.Bind(
            "Server",
            "SameGameVersion",
            false,
            "Refuse players whose version of Elin is not the host's\n" +
            "Off: they are let in and both are told; the same version of the mod is always required\n" +
            "拒绝游戏版本与主机不同的玩家");

        Server.PublicGame = config.Bind(
            "Server",
            "PublicGame",
            false,
            "Anybody who finds the game in the list of games can join it, not only the host's Steam friends and invited players\n" +
            "A game opened by itself (a depot, a shared world) still lets in only the players of its world\n" +
            "任何在游戏列表中找到该游戏的人都可以加入，而不仅是主机的 Steam 好友和受邀玩家；自动开启的游戏（仓库、共享世界）仍只允许其世界的玩家加入");

        Server.PublishMods = config.Bind(
            "Server",
            "PublishMods",
            true,
            "The list of the mods of the game is shown to the players before they join (Steam lobby) and sent when they do\n" +
            "It is the list of the world when it came from a depot (modlist.txt), else the mods of the host\n" +
            "A player who is refused, or who takes the world, is told which mods differ. Otherwise nothing is said\n" +
            "游戏的模组列表会在玩家加入前显示（Steam 大厅）并在加入时发送；世界来自仓库时使用仓库的列表（modlist.txt），否则使用主机的模组。被拒绝或取得世界的玩家会被告知哪些模组不同");

        Server.PlayerCombatTime = config.Bind(
            "Server",
            "PlayerCombatTime",
            true,
            "Combat runs on each player's own time: what fights a player only acts when that player takes a turn\n" +
            "Nobody waits for the others. Takes over the turn-based combat mode when both are on\n" +
            "战斗按各玩家自己的时间进行：与某位玩家战斗的单位只在该玩家行动时行动，同时开启时优先于回合制战斗");

        Server.PlayerClock = config.Bind(
            "Server",
            "PlayerClock",
            true,
            "Every player walks and acts on the clock of its own game, as the host does\n" +
            "Otherwise a guest's steps follow the host's game time as it arrives over the network, which makes them uneven\n" +
            "Only with combat on each player's time\n" +
            "每位玩家按自己游戏的时钟移动和行动，与主机一样；否则客机的步伐取决于经网络传来的主机时间，会不均匀。需要开启“战斗按各玩家时间进行”");

        Server.PlayerStepPace = config.Bind(
            "Server",
            "PlayerStepPace",
            true,
            "Every player walks and acts at the pace of a solo game, whatever the speed of the other players\n" +
            "Otherwise the length of a step depends on the gap between the fastest and the slowest player\n" +
            "Speed still counts in a fight. Only with combat on each player's time\n" +
            "每位玩家按单人游戏的节奏移动和行动，不受其他玩家速度影响；否则每一步的时长取决于最快与最慢玩家的差距。战斗中速度仍然有效。需要开启“战斗按各玩家时间进行”");

        Server.SharedWorldTime = config.Bind(
            "Server",
            "SharedWorldTime",
            true,
            "One date for the whole world: time passed by a player alone on another map counts for everyone, the most advanced date is the world's\n" +
            "Otherwise only the host's date counts, and a player coming back from a map of its own jumps to it\n" +
            "整个世界共用一个日期：独自在其他地图的玩家所经过的时间对所有人都有效，最靠前的日期就是世界的日期；否则只以主机的日期为准");

        Server.WorldKeeper = config.Bind(
            "Server",
            "WorldKeeper",
            true,
            "What time does to the world (weather, expired quests, taxes, salaries, letters) is done once, by one game, the same for everyone\n" +
            "Otherwise every player holding a map runs it too in its own copy: a quest expires twice, each copy has its own weather\n" +
            "时间对世界的影响（天气、任务过期、税金、工资、信件）只由一个游戏处理一次，所有人相同；否则每个持有地图的玩家也会在自己的副本中各自处理");

        Server.PlayerKill = config.Bind(
            "Server",
            "PlayerKill",
            false,
            "A player (or its companions) can kill another player's character\n" +
            "Off: a strike that would kill it leaves it at 0 hit points\n" +
            "玩家（及其同伴）可以杀死其他玩家的角色；关闭时，致命一击只会让对方的生命值降为 0");

        Server.Duels = config.Bind(
            "Server",
            "Duels",
            true,
            "A player can challenge another player to a duel from the menu on its character: nobody dies, both are healed at the end, nothing is lost\n" +
            "Potions, arrows and charges used during the duel stay spent\n" +
            "玩家可以通过对方角色的菜单发起决斗：不会有人死亡，结束时双方恢复，没有任何损失；决斗中使用的药水、箭矢和充能不会返还");

        Server.GuestBuild = config.Bind(
            "Server",
            "GuestBuild",
            true,
            "The other players may use the build mode of the base (floors, walls, furniture, mining, cutting): the game of the host builds for them, they pay\n" +
            "Otherwise only the host builds\n" +
            "其他玩家可以使用据点的建造模式（地板、墙、家具、挖掘、砍伐）：由主机的游戏代为建造，费用由该玩家支付；否则只有主机可以建造");

        Server.HostManagesBase = config.Bind(
            "Server",
            "HostManagesBase",
            false,
            "Only the host manages the base: what the other players ask of it (research, hearth skills, policies, names, settings, residents, build mode) is refused\n" +
            "Otherwise every player manages the base as the host does (leaving the base for good stays the host's)\n" +
            "只有主机可以管理据点：其他玩家对据点的操作（研究、炉灶技能、政策、命名、设置、居民、建造模式）会被拒绝；否则每位玩家都可像主机一样管理据点（永久放弃据点仍只有主机可以）");

        Server.PlayerShipping = config.Bind(
            "Server",
            "PlayerShipping",
            true,
            "Every player is paid for the goods it puts in the shipping box, with its own shipping bonus\n" +
            "Otherwise the host gets everything\n" +
            "每位玩家各自获得自己放入出货箱物品的收入与出货奖励，否则全部归主机");

        Server.PersonalQuests = config.Bind(
            "Server",
            "PersonalQuests",
            true,
            "Random quests, fame and karma belong to the player who takes the quest, 5 quests each\n" +
            "Story quests stay shared. Otherwise there is one quest log and one fame for the whole group\n" +
            "随机任务、名声与业力归接任务的玩家所有，每人5个；主线任务仍然共享");

        Server.PlayerTrade = config.Bind(
            "Server",
            "PlayerTrade",
            true,
            "Players next to each other can trade items and gold through a window both confirm\n" +
            "Otherwise the game's own menu opens the other player's bag, as for an ally\n" +
            "相邻的玩家可以通过双方确认的窗口交换物品和金币");

        // not the "ChooseCharacter" key of earlier versions: every settings file already written holds it as true,
        // and a player who comes back is no longer asked at every connection
        Server.ChooseCharacter = config.Bind(
            "Server",
            "AskCharacter",
            false,
            "Every joining player is asked which of its characters in this world to play, or to make a new one\n" +
            "Otherwise it gets the character it played last, without a question\n" +
            "每位加入的玩家都会被询问使用自己在这个世界的哪个角色或新建角色，否则直接使用上次的角色");

        Server.AutoReconnect = config.Bind(
            "Server",
            "AutoReconnect",
            true,
            "A player who loses the connection joins the same game again by itself, every 5 seconds for 3 minutes\n" +
            "Not after leaving, being kicked or being refused. Otherwise it is left on the title screen\n" +
            "掉线的玩家会自动重新加入同一个游戏（每 5 秒一次，持续 3 分钟）；主动离开、被踢出或被拒绝时不会。关闭时回到标题画面");

        Server.AutoResync = config.Bind(
            "Server",
            "AutoResync",
            true,
            "A player whose copy of the map no longer matches the host's (a missing item or character) loads the map again by itself\n" +
            "At most once every 30 seconds, never during a fight, a task or with a menu open. Otherwise the difference is only written to the log\n" +
            "玩家的地图与主机不一致（缺少物品或角色）时，会自动重新载入地图；最多每 30 秒一次，战斗中、行动中或打开菜单时不会。关闭时只写入日志");

        Server.AutoSave = config.Bind(
            "Server",
            "AutoSave",
            true,
            "The world is saved by itself every 2 minutes while another player is in the game, and when the last one leaves\n" +
            "Every 5 minutes when a save takes more than half a second. Otherwise only when the host saves\n" +
            "有其他玩家在游戏中时，世界每 2 分钟自动保存一次，最后一位玩家离开时也会保存；保存超过半秒时改为每 5 分钟。关闭时只在主机保存时保存");

        Server.SharedTax = config.Bind(
            "Server",
            "SharedTax",
            true,
            "The monthly tax is computed on the highest fame among the connected players, not always on the host's\n" +
            "Otherwise on the host's fame, as the game does\n" +
            "每月税金按在线玩家中最高的名声计算，而不总是按主机的名声。关闭时按主机的名声计算（与原版相同）");

        Server.TownLife = config.Bind(
            "Server",
            "TownLife",
            true,
            "Townspeople chat, greet, gossip and throw stones near every player, not only near the host's screen; the gossip and the stones go by the karma of the player they are about\n" +
            "Otherwise only near the host and about the host's karma, as the game does in the host's game\n" +
            "镇民在每位玩家附近都会闲聊、打招呼、议论和扔石头，而不仅在主机的画面附近；议论和石头按所针对玩家自己的业报决定。关闭时只在主机附近，并按主机的业报（与原版相同）");

        Server.OncePerPlayer = config.Bind(
            "Server",
            "OncePerPlayer",
            true,
            "What the game gives once per world is given once to each player: what a shop sells only once, and the rewards of story quests and dialogs (a share for every player)\n" +
            "Otherwise the first player who buys it or earns it is the only one who has it, as the game does\n" +
            "游戏每个世界只给一次的东西，每位玩家都能得到一次：商店的限量商品，以及剧情任务和对话的奖励（每位玩家一份）。关闭时只有第一个购买或获得的玩家拥有（与原版相同）");

        Server.TownBoards = config.Bind(
            "Server",
            "TownBoards",
            true,
            "When the game draws the offers of a town board again, the highest fame among the players on the map opens the offers that need fame, and the difficulty is that of the players present (a guest counts like the host)\n" +
            "Otherwise the board is drawn for the host's character alone, as the game does\n" +
            "游戏重新生成城镇告示板时，地图上名声最高的玩家决定需要名声的委托是否出现，难度按在场玩家计算（访客与主机同等对待）。关闭时只按主机的角色生成（与原版相同）");
        Server.ShopsFromEveryPlayer = config.Bind(
            "Server",
            "ShopsFromEveryPlayer",
            true,
            "A shop restocks with the stock bonus of the best player on the map, and its special shelves (the marriage and tape shop, Loytel's mart, the exotic merchant) follow the player who opens it\n" +
            "Otherwise the host's feats and progress decide for everyone, as the game does\n" +
            "商店补货时按地图上最强玩家的进货加成计算，特殊货架（婚礼与磁带店、洛伊特商店、异国商人）取决于打开商店的玩家。关闭时一律按主机的天赋和进度（与原版相同）");
        Server.NearestSpawns = config.Bind(
            "Server",
            "NearestSpawns",
            true,
            "The feats that change what spawns on a map (legendary chance, friend or foe of Santa and the like, evolved, big daddy, shrines) are those of the player nearest to the spawn, not always the host's\n" +
            "Otherwise the host's, as the game does\n" +
            "影响地图刷怪的特性（传说怪几率、圣诞老人等特殊怪物的敌友、进化怪、大老爹、神殿）取离刷新点最近的玩家的，而不总是主机的。关闭时按主机的特性计算（与原版相同）");

        Server.AutoCombatPerPlayer = config.Bind(
            "Server",
            "AutoCombatPerPlayer",
            true,
            "Auto combat saves from a deadly hit the player who runs it and its own companions, with its own tactics and stamina: each player's auto combat protects its own, the host's no longer protects the others\n" +
            "Otherwise the host's auto combat protects everyone and a guest's protects no one, as the game does in the host's game\n" +
            "自动战斗会以玩家自己的战术和耐力，保护进行自动战斗的玩家及其同伴免受致命一击：每位玩家的自动战斗保护自己的人，主机的不再保护其他人。关闭时主机的自动战斗保护所有人，访客的不保护任何人");

        Server.SoftRecall = config.Bind(
            "Server",
            "SoftRecall",
            true,
            "When the host walks onto the map a player holds alone, that player stays on its map without loading the whole world again; when that player walks into the map the host is on, it loads that one map only\n" +
            "On since 0.26.690 (off before). Should anything not match, the world is loaded as before. Otherwise the world is loaded at each such meeting\n" +
            "主机走进某位玩家独自所在的地图时，该玩家留在原地图上，不再重新载入整个世界；该玩家走进主机所在的地图时，只载入那一张地图。自 0.26.690 起默认开启；如有任何不一致，仍会像以前一样载入世界。关闭时每次这样相遇都会重新载入世界");

        // (off was the default while it was new: a host who never chose gets it once, then its own choice stays)
        var softOnce = config.Bind("Server", "SoftRecallSwitchedOn", false,
            "Internal: the mod turned SoftRecall on once when it became the default");
        if (!softOnce.Value) {
            softOnce.Value = true;
            Server.SoftRecall.Value = true;
        }

        Server.WorldCopy = config.Bind(
            "Server",
            "WorldCopy",
            false,
            "After each save the game makes by itself, every other player's game keeps a copy of the world on its own disk, outside its saves\n" +
            "Sent in the background, a little at a time, only what changed. Otherwise the world is on the host's PC only\n" +
            "每次自动保存后，其他玩家的游戏会在自己的硬盘上保留一份世界副本（不在存档文件夹内）；在后台一点一点发送，只发送有变化的部分。关闭时世界只存在于主机的电脑上");

        Server.Takeover = config.Bind(
            "Server",
            "Takeover",
            false,
            "When the host leaves, another player opens the world from the copy its game keeps, and the game goes on there\n" +
            "Needs \"The other players keep a copy of the world\". New, off until it has been played more. Otherwise the game ends when its host leaves\n" +
            "主机离开时，由另一位玩家用自己电脑上保留的世界副本重新开放世界，游戏在那里继续。需要开启“其他玩家保留一份世界副本”。新功能，默认关闭；关闭时主机离开游戏即结束");

        Server.AutoHost = config.Bind(
            "Server",
            "AutoHost",
            true,
            "The game opens to the other players by itself when a world is loaded, without \"Start Server\"\n" +
            "Only friends can join, and the world needs a claimed land. Otherwise the host clicks \"Start Server\"\n" +
            "读取世界时游戏自动对其他玩家开放，无需点击“启动服务器”；只有好友可以加入，且世界需要已占领的土地。关闭时由主机点击“启动服务器”");

        Server.OwnSleep = config.Bind(
            "Server",
            "OwnSleep",
            true,
            "A player who goes to bed sleeps at once, for itself, without waiting for the others\n" +
            "The night only passes for the world when every player is asleep at the same time. Otherwise everyone waits for everyone\n" +
            "玩家上床后立刻为自己睡觉，不用等别人；只有所有玩家同时睡着时，世界才会过夜。关闭时所有人互相等待");

        Server.SummonsFollowMaster = config.Bind(
            "Server",
            "SummonsFollowMaster",
            true,
            "What a player summoned goes where that player goes: along with the host, or away on its own\n" +
            "Otherwise, as in the game for the host alone, the summons of the others vanish when the host leaves the map\n" +
            "玩家召唤的生物跟随召唤者：与主机同行或独自离开时都会一起走；关闭时主机离开地图会让其他玩家的召唤物消失");

        Server.PlayersAtBase = config.Bind(
            "Server",
            "PlayersAtBase",
            true,
            "The base counts every player on its map: raids come while one of them is awake, the lucky month follows the god of any of them, and the days with a player count wherever a player is\n" +
            "Otherwise only the player whose game runs the base map counts (the host, mostly)\n" +
            "据点会计算地图上的所有玩家：只要有一人醒着就会有袭击，幸运月看任一玩家的神，与玩家共处的天数在任何玩家所在处都会计算；关闭时只计算运行据点地图的那位玩家");

        Server.Follow = config.Bind(
            "Server",
            "Follow",
            true,
            "The menu on another player's character offers Follow: your character walks after it, on the same map\n" +
            "Any key, a blow, a monster in sight or a window stops it; so does the other player leaving the map\n" +
            "其他玩家角色的菜单中提供“跟随”：你的角色会在同一地图上跟着对方走；按任意键、受到攻击、看到怪物或打开窗口都会停止，对方离开地图时也会停止");

        Server.PrivateChat = config.Bind(
            "Server",
            "PrivateChat",
            true,
            "Talk on another player's character opens a private chat with that player, wherever they are\n" +
            "Only that player gets the lines, the host passes them on without showing them\n" +
            "对其他玩家角色选择“交谈”会打开与该玩家的私聊，无论对方在哪里；只有该玩家会收到，主机只转发不显示");

        Server.GuestMoongate = config.Bind(
            "Server",
            "GuestMoongate",
            true,
            "Another player's moongate takes that player alone to a map the host downloaded, the host stays where it is\n" +
            "Otherwise a moongate does nothing for the other players\n" +
            "其他玩家使用月之门时，只有该玩家前往主机已下载的地图，主机留在原地；关闭时月之门对其他玩家无效");

        Server.GuestLandingPortal = config.Bind(
            "Server",
            "GuestLandingPortal",
            true,
            "Another player's landing portal takes that player alone to the gate zone of the survival world\n" +
            "Otherwise a landing portal does nothing for the other players\n" +
            "其他玩家使用着陆传送门时，只有该玩家前往生存模式的门所在区域；关闭时对其他玩家无效");

        Server.GuestSkyHelm = config.Bind(
            "Server",
            "GuestSkyHelm",
            true,
            "Another player's sky helm shows the host's destinations on that player's screen, its sky tickets pay, and the gate zone moves for everyone\n" +
            "Otherwise a sky helm does nothing for the other players\n" +
            "其他玩家使用天空舵时，在该玩家的屏幕上显示主机的目的地列表，用该玩家的天空船票支付，所有人的门所在区域一起改变；关闭时对其他玩家无效");
        Server.SharedMarriages = config.Bind(
            "Server",
            "SharedMarriages",
            true,
            "Every player can marry: a resident is the spouse of the character who married it, the first to propose wins, and the host keeps who in its save\n" +
            "Wedding, divorce and the lost ring deed work for that character only. Otherwise, as before, a guest's marriage stays in its own game\n" +
            "每位玩家都可以结婚：居民是与之结婚的角色的配偶，先求婚者优先，主机在存档中记录；婚礼、离婚和遗失戒指证书只对该角色有效。关闭时如以前一样，客人的婚姻只存在于其自己的游戏中");

        Server.Teams = config.Bind(
            "Server",
            "Teams",
            false,
            "Players can be in teams: the host keeps who is in which team in its save. A player in no team is a Wanderer\n" +
            "Otherwise there are no teams and everything is as before\n" +
            "玩家可以组队：主机在存档中记录谁属于哪个队伍。不属于任何队伍的玩家是流浪者；关闭时没有队伍，一切如前");

        Server.TeamBases = config.Bind(
            "Server",
            "TeamBases",
            false,
            "A base belongs to the team of the player who claimed it: the players of another team cannot open its chests, pick up its things, build there or manage it\n" +
            "Otherwise every player uses every base as before\n" +
            "据点属于认领它的玩家所在的队伍：其他队伍的玩家不能打开其箱子、拾取物品、建造或管理据点；关闭时所有玩家如前使用所有据点");

        Server.TeamApproval = config.Bind(
            "Server",
            "TeamApproval",
            true,
            "A player who asks to join a team waits for its leader, who accepts or refuses from the list of players\n" +
            "Otherwise a player joins a team at once\n" +
            "申请加入队伍的玩家需等待队长在玩家列表中接受或拒绝；关闭时玩家立即加入队伍");

        Server.TeamPersonal = config.Bind(
            "Server",
            "TeamPersonal",
            false,
            "Inside a team, a chest, door or bed a player installs on the team's land is that player's: teammates open the chest and see inside but cannot take, cannot pass the door; \"Share with team\" in its menu gives it back to the team\n" +
            "Otherwise what is on a team's land is the whole team's\n" +
            "队伍内部，玩家在队伍土地上安装的箱子、门或床归该玩家所有：队友可以打开箱子查看但不能拿取，不能通过该门；在其菜单中选择“与队伍共享”即归还队伍；关闭时队伍土地上的东西属于整个队伍");

        Server.TeamDeed = config.Bind(
            "Server",
            "TeamDeed",
            true,
            "The founder of a new team receives one land deed, once per team and once per player\n" +
            "Otherwise a team buys its deed from the merchant as in the game\n" +
            "新队伍的创建者获得一张土地证书，每个队伍一次、每位玩家一次；关闭时队伍如游戏中一样从商人处购买证书");

        Server.SpawnZone = config.Bind(
            "Server",
            "SpawnZone",
            "start",
            "Where a new player appears, and a player whose team has no base, when teams are on: start (the meadow where a new game starts), host (the host's map, as before), or the id of a zone\n" +
            "新玩家以及其队伍没有据点的玩家（启用队伍时）出现的地方：start（新游戏开始的草原）、host（主机所在地图，如前）或区域 id");

        Server.TeamPvp = config.Bind(
            "Server",
            "TeamPvp",
            false,
            "Two players of different teams are enemies to each other, outside towns and the spawn zone. Wanderers fight nobody\n" +
            "A player killed by another dies only if \"Players can kill each other\" is on. Otherwise players of different teams cannot fight\n" +
            "不同队伍的玩家在城镇和出生区域之外互为敌人，流浪者不参与；只有开启玩家可以互相击杀时才会真正死亡。关闭时不同队伍的玩家不能战斗");

        Server.GlobalChat = config.Bind(
            "Server",
            "GlobalChat",
            true,
            "What a player types in the chat is read by every player, wherever they are: on another map, on the world map, away on their own\n" +
            "Otherwise only the players on the same map as the speaker read it. Lines of townspeople and the game stay on their map either way\n" +
            "玩家输入的聊天内容所有玩家都能看到，无论身在何处（其他地图、世界地图、独自行动）；关闭时只有同一地图上的玩家能看到。居民和游戏的台词无论如何都只在本地图显示");

        Server.WorldMapTime = config.Bind(
            "Server",
            "WorldMapTime",
            WorldMapTime.SharedClock,
            "Who pays the hours of a trip on the world map. SharedClock: the world map is a map like any other, every step of anyone moves the date; the others catch up without losing hunger or quest time\n" +
            "TravelTogether: a step moves the date only when all the players are on the world map, whoever steps; otherwise the traveller pays its own turns and the date stays\n" +
            "PersonalPace: as SharedClock, but a player travelling while the others are elsewhere runs ahead of the world (its hunger and quests pay the hours, the date stays) up to WorldMapLeadCap hours; the world catches up once everyone is on the world map or sleeps together\n" +
            "世界地图上旅行的时间由谁承担。SharedClock：世界地图与其他地图相同，任何人的每一步都会推进日期，其他人追上时间但不会损失饱食度或任务时间；TravelTogether：只有所有玩家都在世界地图上时，一步才会推进日期；PersonalPace：独自旅行的玩家领先于世界（饱食度与自己的任务承担时间，日期不动），所有人在世界地图上相聚或一起睡觉时世界追上");
        if (OldWorldMapTime(before) is { } old) {
            Server.WorldMapTime.Value = old;
        }

        Server.WorldMapLeadCap = config.Bind(
            "Server",
            "WorldMapLeadCap",
            24,
            "Personal Pace: how many hours a player travelling alone may run ahead of the world; the hours past it move the date as in SharedClock\n" +
            "个人步调：独自旅行的玩家最多可以领先世界多少小时，超出的部分像共享时钟一样推进日期");

        Server.DumpSparesBelt = config.Bind(
            "Server",
            "DumpSparesBelt",
            true,
            "Auto-dump leaves what the player holds and what is in the tool belt, as it leaves the hotbar\n" +
            "Otherwise as in the game\n" +
            "自动收纳不会收走玩家手持的物品和工具腰带里的物品，就像它不会收走快捷栏一样。关闭时与原版相同");

        Server.ImportCharacter = config.Bind(
            "Server",
            "ImportCharacter",
            false,
            "A player joining can bring the character of one of its own saves: the character, its equipment, bag and gold, its fame and karma\n" +
            "Not its companions, base, quests or bank. Its save is only read, never changed\n" +
            "加入的玩家可以带来自己存档中的角色：角色本身、装备、背包、金币、声望和业力；不包括同伴、据点、任务和银行。存档只被读取，不会被修改");

        Server.DeleteCharacter = config.Bind(
            "Server",
            "DeleteCharacter",
            true,
            "In the list of its characters, a joining player can delete one of them, with what it carries (asked twice)\n" +
            "Its companions stay in the world and no longer follow anyone\n" +
            "加入的玩家可以在角色列表中删除自己的某个角色及其携带的物品（会确认两次），其同伴留在世界中");

        // off by default, the one rule that is: the checks are new and logged first ("would have refused"), a
        // refusal of a legit act would cost a player what it did (council 2, audit 2026-10-23)
        Server.EnforceOwnership = config.Bind(
            "Server",
            "EnforceOwnership",
            false,
            "What a guest's game says about a character that is neither its own nor one of its companions (its task, its damage, its skills, its party) is refused\n" +
            "Off: only written to the log. A trade, a gift to another player or an order to one's own companion is never refused\n" +
            "客机游戏发来的、关于既非其角色也非其同伴的角色的数据（任务、伤害、技能、队伍）会被拒绝；关闭时仅写入日志。交易、送给其他玩家的礼物或对自己同伴的命令永远不会被拒绝");
        Server.ActivitiesFromActor = config.Bind(
            "Server",
            "ActivitiesFromActor",
            true,
            "What a barrel brews and what a baby pet learns from its mother's milk follow the player it is about: the one nearest to the barrel, the pet's owner\n" +
            "Otherwise they follow the host's character, as for a player alone\n" +
            "酒桶酿出的酒和幼宠喝母乳的成长取决于相关的玩家（离酒桶最近的玩家、宠物的主人）；关闭时取决于主机的角色");

        Server.TravelCheckpointSeconds = config.Bind(
            "Server",
            "TravelCheckpointSeconds",
            60,
            new ConfigDescription(
                "Seconds between two checkpoints of a client travelling alone (its zone and character)\n" +
                "If it disconnects, only what happened since the last checkpoint is lost. 0 disables\n" +
                "独自行动的客机每隔多少秒回传一次进度，断线时只丢失最后一次之后的内容。0 为关闭",
                new AcceptableValueRange<int>(0, 600)));

        Dev.Listener = config.Bind(
            "Dev",
            "Listener",
            false,
            "Open the localhost debug listener for scripts/mcp (Debug builds only)\n" +
            "It executes arbitrary C#, never enable on a machine you share\n" +
            "为 scripts/mcp 打开本机调试监听（仅 Debug 构建）\n" +
            "它会执行任意 C#，不要在共用机器上开启");

        Dev.Identity = config.Bind(
            "Dev",
            "Identity",
            0,
            new ConfigDescription(
                "Local udp test identity (Debug builds only), 0 uses the Steam account\n" +
                "Lets several game instances on one Steam account join a local session as different players\n" +
                "本地UDP测试身份（仅 Debug 构建），0 为使用 Steam 账号",
                new AcceptableValueRange<int>(0, 999)));

        Dev.BotLaunchers = config.Bind(
            "Dev",
            "BotLaunchers",
            "",
            "Game copies a bot player can be started from (Debug builds only), separated by ';'\n" +
            "Empty looks for Elin*/Elin.exe, made by dev/_tools/make_lab.py, in the folder the environment variable\n" +
            "ELINTOGETHER_LAB names, else in Documents/ElinMods/_lab");

        Dev.BotAllActions = config.Bind(
            "Dev",
            "BotAllActions",
            false,
            "Bot players also accept quests and sell through the shipping chest (Debug builds only)\n" +
            "Leave off on a world you care about");

        Reload();
    }

    internal static class Dev
    {
        internal static ConfigEntry<bool> Listener { get; set; } = null!;
        internal static ConfigEntry<int> Identity { get; set; } = null!;
        internal static ConfigEntry<string> BotLaunchers { get; set; } = null!;
        internal static ConfigEntry<bool> BotAllActions { get; set; } = null!;
    }

    internal static class Policy
    {
        internal static ConfigEntry<float> Timeout { get; set; } = null!;
        internal static ConfigEntry<int> Retries { get; set; } = null!;
        internal static ConfigEntry<bool> Verbose { get; set; } = null!;
    }

    internal static class Client
    {
        internal static ConfigEntry<KeyCode> PingKeybind { get; set; } = null!;
        internal static ConfigEntry<string> DepotPath { get; set; } = null!;
        internal static ConfigEntry<string> DepotPassword { get; set; } = null!;
        internal static ConfigEntry<string> ServerAddress { get; set; } = null!;
        internal static ConfigEntry<bool> FetchMods { get; set; } = null!;
        internal static ConfigEntry<bool> KeepMods { get; set; } = null!;
    }

    internal static class Server
    {
        internal static ConfigEntry<string> SourceValidationSet { get; set; } = null!;
        internal static ConfigEntry<bool> StrictValidationMode { get; set; } = null!;
        internal static ConfigEntry<bool> SharedAverageSpeed { get; set; } = null!;
        internal static ConfigEntry<bool> TurnBasedCombat { get; set; } = null!;
        internal static ConfigEntry<bool> IndependentTravel { get; set; } = null!;
        internal static ConfigEntry<bool> SameGameVersion { get; set; } = null!;
        internal static ConfigEntry<bool> PublishMods { get; set; } = null!;
        internal static ConfigEntry<bool> PublicGame { get; set; } = null!;
        internal static ConfigEntry<bool> PlayerShipping { get; set; } = null!;
        internal static ConfigEntry<bool> GuestBuild { get; set; } = null!;
        internal static ConfigEntry<bool> PlayerKill { get; set; } = null!;
        internal static ConfigEntry<bool> HostManagesBase { get; set; } = null!;
        internal static ConfigEntry<bool> Duels { get; set; } = null!;
        internal static ConfigEntry<bool> PersonalQuests { get; set; } = null!;
        internal static ConfigEntry<bool> ChooseCharacter { get; set; } = null!;
        internal static ConfigEntry<bool> AutoReconnect { get; set; } = null!;
        internal static ConfigEntry<bool> AutoResync { get; set; } = null!;
        internal static ConfigEntry<bool> AutoSave { get; set; } = null!;
        internal static ConfigEntry<bool> WorldCopy { get; set; } = null!;
        internal static ConfigEntry<bool> Takeover { get; set; } = null!;
        internal static ConfigEntry<bool> SharedTax { get; set; } = null!;
        internal static ConfigEntry<bool> OncePerPlayer { get; set; } = null!;
        internal static ConfigEntry<bool> TownLife { get; set; } = null!;
        internal static ConfigEntry<bool> TownBoards { get; set; } = null!;
        internal static ConfigEntry<bool> ShopsFromEveryPlayer { get; set; } = null!;
        internal static ConfigEntry<bool> NearestSpawns { get; set; } = null!;
        internal static ConfigEntry<bool> AutoCombatPerPlayer { get; set; } = null!;
        internal static ConfigEntry<bool> SoftRecall { get; set; } = null!;
        internal static ConfigEntry<bool> AutoHost { get; set; } = null!;
        internal static ConfigEntry<bool> OwnSleep { get; set; } = null!;
        internal static ConfigEntry<bool> SummonsFollowMaster { get; set; } = null!;
        internal static ConfigEntry<bool> PlayersAtBase { get; set; } = null!;
        internal static ConfigEntry<bool> GlobalChat { get; set; } = null!;

        internal static ConfigEntry<bool> Follow { get; set; } = null!;
        internal static ConfigEntry<bool> PrivateChat { get; set; } = null!;
        internal static ConfigEntry<bool> GuestMoongate { get; set; } = null!;
        internal static ConfigEntry<bool> GuestLandingPortal { get; set; } = null!;
        internal static ConfigEntry<bool> GuestSkyHelm { get; set; } = null!;
        internal static ConfigEntry<bool> SharedMarriages { get; set; } = null!;
        internal static ConfigEntry<bool> Teams { get; set; } = null!;
        internal static ConfigEntry<bool> TeamBases { get; set; } = null!;
        internal static ConfigEntry<bool> TeamApproval { get; set; } = null!;
        internal static ConfigEntry<bool> TeamDeed { get; set; } = null!;
        internal static ConfigEntry<bool> TeamPersonal { get; set; } = null!;
        internal static ConfigEntry<string> SpawnZone { get; set; } = null!;

        internal static ConfigEntry<bool> TeamPvp { get; set; } = null!;
        internal static ConfigEntry<WorldMapTime> WorldMapTime { get; set; } = null!;
        internal static ConfigEntry<int> WorldMapLeadCap { get; set; } = null!;
        internal static ConfigEntry<bool> DumpSparesBelt { get; set; } = null!;
        internal static ConfigEntry<bool> ImportCharacter { get; set; } = null!;

        internal static ConfigEntry<bool> DeleteCharacter { get; set; } = null!;
        internal static ConfigEntry<bool> EnforceOwnership { get; set; } = null!;
        internal static ConfigEntry<bool> PlayerTrade { get; set; } = null!;
        internal static ConfigEntry<bool> PlayerCombatTime { get; set; } = null!;
        internal static ConfigEntry<bool> PlayerClock { get; set; } = null!;
        internal static ConfigEntry<bool> PlayerStepPace { get; set; } = null!;
        internal static ConfigEntry<bool> SharedWorldTime { get; set; } = null!;
        internal static ConfigEntry<bool> WorldKeeper { get; set; } = null!;
        internal static ConfigEntry<bool> ActivitiesFromActor { get; set; } = null!;
        internal static ConfigEntry<int> TravelCheckpointSeconds { get; set; } = null!;
    }
}