using System;
using System.Collections.Generic;
using ElinTogether.Helper;
using ElinTogether.Net;
using ElinTogether.Patches;
using MessagePack;

namespace ElinTogether.Models;

// Card
[Union(100, typeof(CardGenDelta))]
[Union(101, typeof(CardDamageHpDelta))]
[Union(102, typeof(CardPlacedDelta))]
[Union(103, typeof(CardModNumDelta))]
[Union(104, typeof(CardAddThingDelta))]
[Union(105, typeof(CardRemoveThingDelta))]
[Union(106, typeof(CardOnUseDelta))]
[Union(107, typeof(CardTryStackToDelta))]
[Union(109, typeof(CardSetDirDelta))]
[Union(110, typeof(CardUidRebindDelta))]
[Union(111, typeof(CardToggleDelta))]
[Union(112, typeof(CardChargeDelta))]
[Union(113, typeof(CardIdentifyDelta))]
[Union(114, typeof(CardShrineUsedDelta))]
// Chara
[Union(200, typeof(CharaMoveDelta))]
[Union(201, typeof(CharaTickDelta))]
[Union(202, typeof(CharaMakeAllyDelta))]
[Union(203, typeof(CharaPickThingDelta))]
[Union(204, typeof(CharaDieDelta))]
[Union(205, typeof(CharaActPerformDelta))]
[Union(206, typeof(CharaAddConditionDelta))]
[Union(207, typeof(CharaReviveDelta))]
[Union(208, typeof(CharaTickConditionDelta))]
[Union(209, typeof(CharaTaskDelta))]
[Union(210, typeof(CharaBuildDelta))]
[Union(211, typeof(CharaProgressBeginDelta))]
[Union(212, typeof(CharaProgressCompleteDelta))]
[Union(213, typeof(CharaTaskCancelDelta))]
[Union(214, typeof(CharaHitFishDelta))]
[Union(215, typeof(CharaGiveGiftDelta))]
[Union(216, typeof(CharaSwitchHeldDelta))]
[Union(217, typeof(CharaRemoveFromGameDelta))]
[Union(218, typeof(CharaSleepDelta))]
[Union(219, typeof(CharaStaminaDelta))]
[Union(220, typeof(CharaMakeAllyRequestDelta))]
[Union(221, typeof(CharaEquipDelta))]
[Union(222, typeof(PartyMemberDelta))]
[Union(223, typeof(CharaFaithDelta))]
[Union(224, typeof(CharaFeatPointDelta))]
[Union(225, typeof(CharaLevelDelta))]
// Thing
[Union(300, typeof(ThingDelta))]
[Union(301, typeof(ThingRequest))]
[Union(302, typeof(CardModCurrencyDelta))]
// Zone
[Union(400, typeof(SpatialGenDelta))]
[Union(401, typeof(ZoneAddCardDelta))]
// World
[Union(500, typeof(GameDelta))]
[Union(501, typeof(WorldDateAdvanceDelta))]
[Union(504, typeof(WorldTimeReportDelta))]
[Union(505, typeof(WeatherDelta))]
[Union(506, typeof(DayDataDelta))]
[Union(502, typeof(ShippingResultDelta))]
[Union(503, typeof(BranchResourceModDelta))]
// Misc
[Union(600, typeof(OnBarterDelta))]
[Union(601, typeof(CardRendererTalkDelta))]
[Union(602, typeof(MsgSayDelta))]
[Union(603, typeof(EnemyVisibilityDelta))]
[Union(604, typeof(PingPointDelta))]
[Union(605, typeof(CombatPhaseDelta))]
[Union(606, typeof(AddRecipeDelta))]
[Union(607, typeof(CombatReadyDelta))]
[Union(608, typeof(SleepRequestDelta))]
[Union(609, typeof(SleepReadyDelta))]
[Union(610, typeof(SleepStartDelta))]
[Union(611, typeof(SleepCancelDelta))]
[Union(620, typeof(PlayerTurnDelta))]
// Inv
[Union(700, typeof(InvOwnerOnProcessDelta))]
[Union(701, typeof(InvRerollDelta))]
[Union(702, typeof(InvSaveDataDelta))]
[Union(703, typeof(InvPlaceAbilityDelta))]
// Quest
[Union(226, typeof(CharaAffinityDelta))]
[Union(227, typeof(CharaDestroyPathDelta))]
[Union(228, typeof(CharaAppearanceDelta))]
[Union(704, typeof(TradeIntentDelta))]
[Union(705, typeof(TradeStateDelta))]
[Union(800, typeof(QuestCreateDelta))]
[Union(801, typeof(QuestSetClientDelta))]
[Union(802, typeof(QuestStartDelta))]
[Union(803, typeof(QuestAcceptDelta))]
[Union(804, typeof(QuestCompleteDelta))]
[Union(805, typeof(QuestUpdateDelta))]
[Union(806, typeof(QuestChangePhaseDelta))]
[Union(807, typeof(DialogFlagDelta))]
[Union(808, typeof(StoryGiftDelta))]
[Union(809, typeof(StoryOutcomeDelta))]
[Union(810, typeof(QuestFailDelta))]
[Union(811, typeof(PersonalStateDelta))]
[Union(812, typeof(PersonalQuestDelta))]
[Union(813, typeof(QuestTakenDelta))]
[Union(814, typeof(PlayerStandingDelta))]
[Union(816, typeof(CraftFirstTimeDelta))]
[Union(817, typeof(CardActReplayDelta))]
[Union(819, typeof(QuestFollowDelta))]
[Union(821, typeof(CharaEffectRequestDelta))]
[Union(822, typeof(ZoneInvestDelta))]
[Union(823, typeof(PlayerTacticsDelta))]
[Union(824, typeof(BaseRequestDelta))]
[Union(825, typeof(BaseStateDelta))]
[Union(826, typeof(CardSettingDelta))]
[Union(827, typeof(PolicyStateDelta))]
[Union(828, typeof(CardDecayDelta))]
[Union(829, typeof(NameDelta))]
[Union(830, typeof(TileStateDelta))]
[Union(831, typeof(AgentTaskDelta))]
[Union(832, typeof(CharaReviveRequestDelta))]
[Union(833, typeof(TerrainHeightDelta))]
[Union(834, typeof(AreaStateDelta))]
[Union(835, typeof(CopyShopDelta))]
[Union(837, typeof(DuelIntentDelta))]
[Union(838, typeof(DuelStateDelta))]
[Union(840, typeof(SleepStateDelta))]
[Union(839, typeof(BillPayDelta))]
[Union(841, typeof(CharaBagDelta))]
[Union(842, typeof(DesyncReportDelta))]
[Union(843, typeof(CardPoseDelta))]
[Union(844, typeof(CodexDelta))]
[Union(845, typeof(QuestOffersDelta))]
[Union(846, typeof(QuestRerollDelta))]
[Union(847, typeof(MapBoundsDelta))]
[Union(848, typeof(CharaSettingDelta))]
[Union(849, typeof(ExtraTaxDelta))]
[Union(850, typeof(BossWinDelta))]
[Union(851, typeof(PartyExpDelta))]
[Union(852, typeof(QuestJournalDelta))]
[Union(853, typeof(ClaimZoneDelta))]
[Union(854, typeof(CardSplitStackDelta))]
[Union(855, typeof(TentZoneDelta))]
[Union(856, typeof(GateFollowDelta))]
[Union(857, typeof(CellFlagDelta))]
[Union(858, typeof(WorldPrincipalDelta))]
[Union(859, typeof(GeneMachineDelta))]
[Union(860, typeof(AutoCombatDelta))]
[Union(861, typeof(PlaylistDelta))]
[Union(862, typeof(PlayerTitleDelta))]
[Union(863, typeof(TrolleyDelta))]
[Union(871, typeof(CardItemStateDelta))]
// Act
[Union(900, typeof(ActThrowDelta))]
// Counters of the player
[Union(910, typeof(FortuneCountersDelta))]
[Union(920, typeof(ThrowGrowthDelta))]
// World map time
[Union(940, typeof(WorldTravelStepDelta))]
[Union(941, typeof(WorldMapTogetherDelta))]
[Union(960, typeof(PlayerLeadDelta))]
[Union(962, typeof(WhisperDelta))]
[Union(963, typeof(GateItemRequest))]
[Union(964, typeof(GateItemAnswer))]
[Union(965, typeof(GateZoneDelta))]
[Union(970, typeof(MarriageDelta))]
[Union(971, typeof(CharaBanishRequest))]
[Union(972, typeof(ManiGeneRequest))]
[Union(973, typeof(CharaBloomRequest))]
[Union(975, typeof(CharaEggRequest))]
[Union(976, typeof(RelicSlotRequest))]
[Union(977, typeof(MiscreationRequest))]
// Teams
[Union(980, typeof(TeamsDelta))]
[Union(981, typeof(TeamRequestDelta))]
[Union(982, typeof(TeamSpawnDelta))]
// Element
[Union(1000, typeof(ElementChangeDelta))]
public abstract class ElinDelta : EClass
{
    private static int _applyDepth;

    internal virtual OverrideOrder Order { get; } = OverrideOrder.Stack;

    // what Order First/Last is counted by besides the type, e.g. the character the delta is about
    internal virtual int OverrideKey => 0;

    internal virtual bool RequiresGameStarted { get; } = true;

    public static bool IsApplying => _applyDepth > 0;

    // remote applying, ThingRequest is local sim which does not cunt
    public static bool IsRemoteStateLanding => IsApplying && !ThingRequest.IsReplayingIntent;

    internal int OriginPeer { get; set; }

    internal int DeferCount { get; set; }

    // came in from a guest (OriginPeer alone cannot say it: 0 is also a peer id)
    internal bool FromPeer { get; set; }

    protected virtual void OnApply(ElinNetBase net)
    {
    }

    protected virtual bool OnRefresh()
    {
        return true;
    }

    /// <summary>
    ///     On the host, for a delta a guest sent: why that guest may not do this (the character it is about is
    ///     neither its own nor one of its companions), null when it may. Council 2 on network trust (audit
    ///     2026-10-23): a reason is only logged ("would have refused") and the delta applied as before, unless the
    ///     host turned EnforceOwnership on. Never a kick: a lagging game says stale things too. The default: the
    ///     sender plays a character here
    /// </summary>
    internal virtual string? OnHostPermit(ElinNetHost host, Chara? sender)
    {
        return null;
    }

    /// <summary>
    ///     For the deltas about a character: the sender must have one. Not for all: a guest that just left keeps
    ///     sending its last batch (bills, quests, the codex), which the intake lets through for an away player
    ///     (review 2026-10-23)
    /// </summary>
    protected static string? NoCharacter(Chara? sender)
    {
        return sender is null ? "the sender has no character" : null;
    }

    /// <summary>
    ///     The reason to give when <paramref name="card" /> is a character that is neither the sender nor one of
    ///     its companions or summons. A card not found is allowed: the delta does nothing with it anyway
    /// </summary>
    protected static string? NotOwned(Chara? sender, RemoteCard? card, string what)
    {
        return sender is not null && card?.Find() is Chara target && !OwnsOrCompanion(sender, target)
            ? $"{what} {target.uid} is not the sender's"
            : null;
    }

    /// <summary>
    ///     The target is the sender itself, its companion, or the summon of one of them
    /// </summary>
    internal static bool OwnsOrCompanion(Chara sender, Chara target)
    {
        // (an untagged companion reads as the party leader's, the host's: the host's own pets are never tagged, and
        // letting untagged ones through let a guest act on them; bench permit_suite 2026-10-23)
        return target.uid == sender.uid || PlayerKarma.PlayerBehind(target)?.uid == sender.uid;
    }

    // delta type and peer -> when it was last logged, so a stream of the same delta is one line per window
    private static readonly Dictionary<(Type, int), float> _permitLogged = [];
    private const float PermitLogWindow = 10f;

    /// <returns>false when the delta is refused (only with EnforceOwnership on)</returns>
    private bool HostPermits(ElinNetHost host)
    {
        string? reason;
        try {
            reason = OnHostPermit(host, host.PlayerChara(OriginPeer));
        } catch (Exception ex) {
            // a check that fails says nothing: the delta goes on as it did before there were checks
            EmpLog.Debug(ex, "Permit check of {DeltaType} threw", GetType().Name);
            return true;
        }

        if (reason is null) {
            return true;
        }

        var enforce = EmpConfig.Server.EnforceOwnership.Value;
        var key = (GetType(), OriginPeer);
        var now = UnityEngine.Time.unscaledTime;
        if (!_permitLogged.TryGetValue(key, out var last) || now - last >= PermitLogWindow) {
            _permitLogged[key] = now;
            EmpLog.Warning(enforce ? "Refused {DeltaType} from peer {Peer}: {Reason}" : "Would have refused {DeltaType} from peer {Peer}: {Reason}",
                GetType().Name, OriginPeer, reason);
        }

        return !enforce;
    }

    public void Apply(ElinNetBase net)
    {
        if (FromPeer && net is ElinNetHost permitHost && !HostPermits(permitHost)) {
            return;
        }

        _applyDepth++;
        // what the host does here, it does for the player who sent it: an ally made on the way (a monster
        // ball, a mount, a tamed animal) follows that player, not the host
        var actor = CharaMakeAllyEvent.Actor;
        if (net is ElinNetHost host && host.ActiveRemoteCharas.TryGetValue(OriginPeer, out var sender)) {
            CharaMakeAllyEvent.Actor = sender;
        }

        try {
            OnApply(net);
        } finally {
            CharaMakeAllyEvent.Actor = actor;
            _applyDepth--;
        }
    }

    /// <summary>
    ///     What is done in this scope is told to no one, as if it came from another game (the copies the trade window
    ///     shows, never part of the world)
    /// </summary>
    internal static ScopeExit Quiet()
    {
        _applyDepth++;
        _quietDepth++;
        return new() {
            OnExit = () => {
                _applyDepth--;
                _quietDepth--;
            },
        };
    }

    private static int _quietDepth;

    /// <summary>
    ///     Cards made now are this game's alone (see <see cref="Quiet" />): not announced, not taken back
    /// </summary>
    internal static bool IsQuiet => _quietDepth > 0;

    internal static ScopeExit Simulate(bool active = true)
    {
        var depth = _applyDepth;
        if (active) {
            _applyDepth = 0;
        }

        return new() {
            OnExit = () => {
                if (active) {
                    _applyDepth = depth;
                }
            },
        };
    }

    public bool Refresh()
    {
        return OnRefresh();
    }

    // for harmony patches
    internal readonly struct PatchScope
    {
        private enum Kind : byte
        {
            None,
            Simulate,
            Pending,
        }

        private readonly Kind _kind;
        private readonly int _restore;

        private PatchScope(Kind kind, int restore)
        {
            _kind = kind;
            _restore = restore;
        }

        internal bool IsActive => _kind != Kind.None;

        internal static PatchScope Simulate(bool active = true)
        {
            if (!active) {
                return default;
            }

            var previous = _applyDepth;
            _applyDepth = 0;
            return new(Kind.Simulate, previous);
        }

        internal static PatchScope Pending(bool active = true)
        {
            if (!active) {
                return default;
            }

            PendingContext.Enter();
            return new(Kind.Pending, 0);
        }

        internal void Exit()
        {
            switch (_kind) {
                case Kind.Simulate:
                    _applyDepth = _restore;
                    break;
                case Kind.Pending:
                    PendingContext.Exit();
                    break;
            }
        }
    }

    internal enum OverrideOrder
    {
        Stack,
        Last,
        First,
    }
}