# DRAFT. Title: Independence 1.0 (Elin EA <build to fill in>)
# Tag: independence-1.0 (to confirm). Not published. The Workshop texts are the user's job.

## Independence 1.0: each player or team lives its own story, a living world, duels with bets, teams that learn and pay on their own, an official server entry

**English**

Update on top of 0.26.1082. The host and all guests must update. It is the same version on the Steam Workshop and in
the zip: use one of them, not both.

How to read this: every item marked **[Not tested between two real PCs]** was checked by automated in-game tests on one
PC (two to five game windows) but never played between two real PCs with two Steam accounts. Everything unmarked has
the same limit for Steam itself, but was also covered by the earlier real evenings or is plain game logic. Back up your
saves first (`%USERPROFILE%\AppData\LocalLow\Lafrontier\Elin`). Almost everything new is a host setting and **off by
default**: tick it in *Server Setting*.

**Install**
- **Steam Workshop:** subscribe to Elin Together, launch Elin through Steam, check it is ticked in the mod list.
- **Zip:** unzip the whole folder, run `Install.bat`, launch Elin through Steam. `Uninstall.bat` switches back.
- Elin must be on the Nightly branch, with the YK Framework mod. No official Discord yet; nobody from the project will ever ask you for money or a password.

**New: each player or team lives its own story** (host box, off by default, experimental, always on on the official server) **[Not tested between two real PCs]**
- Each player goes through the main story (Ash, the exploration of Nymelle, the dialogs) at its own pace; a team shares one story, a player without a team has its own. When the box is ticked everyone starts from the world's current progress.
- Farris only joins the first base. A player or team arriving on a Nymelle floor another one cleared carries on from there. Story rewards are paid once per player.
- The save keeps the host's own story: untick the box, or open the save without the mod, and the world goes on with it.

**New: teams learn on their own, pay their own land, keep their own story journal** (three host boxes, off by default) **[Not tested between two real PCs]**
- Recipes, crafts and the codex belong to the team (a player without a team has its own); a scroll another player reads is learnt in its own game; kills count for the killer's team; recipes given by the story reach every team.
- One monthly land bill per team, on the highest fame of its members; unpaid bills are counted per team.
- Each team keeps the furthest main-quest chapter it took part in, shown on its card.

**New: a living world** (host choice, off by default) **[Not tested between two real PCs]**
- The world's date follows real time: a day every 20, 40, 60 or 120 real minutes, whether players act or stand still. It stops when nobody plays, and a night of the world runs to the morning.
- Also new in the time settings: a map a player holds alone stops while that player is away; quests wait for absent players (their deadlines get the days the world lived meanwhile); the catch-up of a long absence can be 1, 3 or 7 days.

**New: duels**
- Every player can fight adventurers like the host (the adventurer leaves town, the win is recorded, the reward is the adventurer's). Off: the guest fights only in its own game.
- **Arena** (host box, off by default): a duel with the host is fought in a zone made for it. **[Not tested between two real PCs]**
- **Gold bets** (host box, off by default): the challenger names a stake (none, 100, 1,000 or the smaller purse); the winner takes both; no winner gives the stakes back; giving up loses yours. **[Not tested between two real PCs]**

**New: characters**
- **Take a character out to a solo save** (host box, off by default, needs "bring a character from your saves"): it plays there and comes back once with its bag, gold and fame; meanwhile it is away from the world. The save is backed up first. **[Not tested between two real PCs]**
- A limit of characters per player (3 by default) and, for the host, a list of everybody's characters with Delete.
- **Your own journal** (on by default): each player keeps its own message history and records (digs, deaths, nights slept).
- The official server never accepts imported characters.

**New: the official server and bug reports**
- The title screen lists the Elin Together account's public game first, marked official, with its player count (100 places). Its entry is signed, so a game that only calls itself "Elin Together" is not shown as official. **[Not tested between two real PCs: the list itself was not seen, Debug lobbies are private]**
- **Report a bug** (Client Settings → Help): a real text box (Ctrl+Enter sends) and, with your logs, a real video of the last 30 seconds of your screen. The public GitHub issue has your words, your Steam name, the versions and the video; the logs are kept privately. **[Video tested on one PC]**

**Security for public servers**
- Checked on the host's side, and refused when the check fails: dialog outcomes, shared quests (stepped and failed honestly), story memory and gifts (an hourly budget per player), personal quest rewards (the terms the host drew), base resources, town investments, task refunds, world time and knowledge messages, bubbles, pings and private lines in the sender's own name.
- Only the game that simulates a zone may declare its boss dead. A player is hosted on another's map only by a teammate or a vouched player. Imports only for vouched players. Operator orders find players by Steam id first.
- The crash, code and save protections always apply; the other checks apply on a public or dedicated server, and write to the log only in a normal game with friends unless you turn them on. **[Not tested with real strangers]**

**Host and guest, no difference** (hunts 5 and 6)
- A defense quest's war horns sound for a guest; a relocation deed moves the base for everyone; a guest's world-map journey is paid when it walks into the host's town; the militant feat and the void boss floor read the player's own side; Nasu's fairy dungeon and stairs look for the Nasu of a player who is there; a meal quest asks for a dish of the taker's cooking; saved party setups and the character sheet's total feat stay the player's own; the base's mail is each player's; a wedding seats the base's people; an achievement goes to the player it is about; encounters are rolled by the visitor's own game; a new nefia is drawn for a player in turn.

**Reliability and speed**
- A thing taken for a guest is never lost; a map nobody holds is never left so; a travel that never ends gets a deadline and a way back; messages lost while loading a map get the map reloaded; a player refused by a map's holder stays where it is; away players still receive the zones the world makes and the achievements they earn; a saved character that is the host's or another peer's is not handed out; saved tables are emptied at the title so a new world does not inherit the last one's players and bans.
- Smaller snapshots (only the characters that changed), fewer game-time messages, fewer copies of every message. Load measured with up to 50 simulated players. **[Not tested with many real players: at most three]**
- Steam: a new link of the same account replaces the old one; a link lost on this side is closed. **[Not tested between two real PCs]**

**Not changed**: the Workshop and zip need the same version for everyone; Mac and Linux zips are not provided; the mod's settings texts are in English, Japanese and Chinese.

Credit unchanged. Testers of this fork: lemiwinks, nardole, 梦天冰, fartcreature.

---

**简体中文**

基于 0.26.1082 的更新。房主和所有客人都需要更新。Steam 创意工坊和压缩包是同一个版本：请二选一，不要两者并用。

如何阅读：标有 **[未在两台真实电脑之间测试]** 的项目，只在一台电脑上由游戏内自动测试验证过（两到五个游戏窗口），从未在使用两个 Steam 账号的两台真实电脑之间玩过。请先备份存档（`%USERPROFILE%\AppData\LocalLow\Lafrontier\Elin`）。几乎所有新功能都是主机设置，**默认关闭**：在"服务器设置"里勾选。

**安装**
- **Steam 创意工坊：** 订阅 Elin Together，通过 Steam 启动 Elin，在模组列表中确认已勾选。
- **压缩包：** 解压整个文件夹，运行 `Install.bat`，通过 Steam 启动 Elin。`Uninstall.bat` 可以切换回去。
- Elin 必须在 Nightly 分支，并安装 YK Framework 模组。目前没有官方 Discord；项目成员绝不会向你索要钱财或密码。

**新功能：每个玩家或队伍各自经历剧情**（主机选项，默认关闭，实验性，官方服务器上始终开启）**[未在两台真实电脑之间测试]**
- 每位玩家按自己的节奏经历主线剧情（阿什、尼梅尔的探索、对话）；一个队伍共享一条剧情，没有队伍的玩家有自己的剧情。勾选时所有人从世界当前的进度开始。
- 法里斯只加入第一个据点。到达已被他人通过的尼梅尔楼层的玩家或队伍从那里继续。剧情奖励每位玩家只领一次。
- 存档保留主机自己的剧情：取消勾选或不带模组打开存档，世界沿用它。

**新功能：队伍各自学习、各自缴纳土地税、各自保留剧情日志**（三个主机选项，默认关闭）**[未在两台真实电脑之间测试]**
- 配方、制作记录和图鉴归队伍所有（没有队伍的玩家有自己的一套）；别人读的卷轴只在其自己的游戏里学会；击杀算在击杀者的队伍里；剧情奖励的配方会给到每个队伍。
- 每个队伍每月一张土地税单，按成员的最高名声计算；未付税单按队伍计数。
- 每个队伍记录自己参与过的主线任务最远章节，显示在队伍卡片上。

**新功能：活的世界**（主机选项，默认关闭）**[未在两台真实电脑之间测试]**
- 世界的日期跟随现实时间：每 20、40、60 或 120 现实分钟一天，不论玩家行动还是静止。没有人玩时停止，世界的夜晚会一直过到早晨。
- 时间设置里还新增：独自占着一张地图的玩家离开时，那张地图停止；任务会等待不在的玩家（期限加上世界在这期间经过的天数）；长时间离开后的追赶可选 1、3 或 7 天。

**新功能：决斗**
- 每位玩家都能像主机一样与冒险者决斗（冒险者离开城镇，胜利被记录，奖励来自冒险者）。关闭：客人只在自己的游戏里战斗。
- **竞技场**（主机选项，默认关闭）：与主机的决斗在专门创建的区域进行。**[未在两台真实电脑之间测试]**
- **金币赌注**（主机选项，默认关闭）：发起方指定赌注（无、100、1,000 或两人中较少的一方全部金币）；胜者全拿；没有胜者时退还；认输则失去自己的赌注。**[未在两台真实电脑之间测试]**

**新功能：角色**
- **把角色带到单人存档**（主机选项，默认关闭，需要先允许"带来存档角色"）：角色在那里游玩，之后带着背包、金币和名声回来一次；期间它在世界中标记为外出。存档会先备份。**[未在两台真实电脑之间测试]**
- 每名玩家的角色数有上限（默认 3）；主机可以看到所有人的角色列表并删除。
- **自己的日志**（默认开启）：每位玩家保留自己的消息记录和个人纪录（挖掘、死亡、睡眠次数）。
- 官方服务器不接受导入的角色。

**新功能：官方服务器与问题报告**
- 标题画面把 Elin Together 账号的公共游戏排在第一位并标为官方，显示在线人数（100 个名额）。条目带有签名，所以仅仅自称"Elin Together"的游戏不会显示为官方。**[未在两台真实电脑之间测试：游戏列表本身没有看到，Debug 大厅是私有的]**
- **报告问题**（客户端设置 → 帮助）：真正的文本框（Ctrl+Enter 发送），连同日志发送屏幕最后 30 秒的视频。GitHub 上的公开报告包含你写的内容、Steam 名称、版本和视频；日志只私下保存。**[视频已在一台电脑上测试]**

**公共服务器的安全**
- 由主机检查，检查失败就拒绝：对话结果、共享任务（如实推进和失败）、剧情记忆和礼物（每名玩家每小时有额度）、个人任务奖励（主机抽取的条件）、据点资源、城镇投资、任务退款、世界时间和知识消息、以发送者自己名义的气泡、标记和私聊。
- 只有模拟某个区域的游戏才能宣布该区域的首领死亡。玩家只有被队友或已被担保的玩家接待，才能出现在别人的地图上。导入角色仅限已被担保的玩家。管理员命令优先按 Steam ID 查找玩家。
- 防崩溃、防代码执行和防存档破坏的保护始终生效；其他检查在公共或专用服务器上生效，在与朋友的普通游戏中除非开启否则只写入日志。**[未与真实陌生人测试]**

**主机与客人没有区别**（第 5、6 轮排查）
- 防御任务的战角对客人也会响起；搬迁地契让所有人的据点一起搬迁；客人在世界地图上的旅程在走进主机城镇时付费；好战专长和虚空首领层读取玩家自己一方；纳苏的妖精地下城和楼梯会找在场玩家的纳苏；餐点任务要求接任务者亲手做的菜；保存的队伍配置和角色面板的总专长属于玩家自己；据点的信件归每位玩家；婚礼安排据点的居民；成就归属于所涉及的玩家；遭遇由到访者自己的游戏来掷骰；新的 Nefia 轮流为各玩家生成。

**可靠性与速度**
- 为客人取走的东西不会再丢失；没有人掌管的地图不会一直无人掌管；永不结束的旅行有期限和返回的办法；载入地图时丢失的消息会让地图重新载入；被地图持有者拒绝的玩家留在原地；离开的玩家仍会收到世界生成的区域和自己获得的成就；属于主机或其他玩家的已保存角色不会被发给别人；标题画面会清空已保存的表，新世界不再继承上一个世界的玩家和封禁。
- 更小的快照（只含有变化的角色）、更少的游戏时间消息、每条消息少复制一次。负载最多用 50 名模拟玩家测量。**[未与多名真实玩家测试：最多三人]**
- Steam：同一账号的新连接会替换旧连接；本端已丢失的连接会被关闭。**[未在两台真实电脑之间测试]**

**不变**：创意工坊和压缩包要求所有人版本一致；不提供 Mac 和 Linux 压缩包；Mod 的设置文本有英文、日文和中文。

鸣谢不变。本分支的测试者：lemiwinks, nardole, 梦天冰, fartcreature。
