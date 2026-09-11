namespace ZombiePlague.Infrastructure;

/// <summary>
/// The action type a zombie party's unified target-selection scoring compares
/// across, per TODO.md Feature 2.
///
/// Siege was removed for a while: BesiegerCamp and the siege strategy system
/// assume a real LeaderHero at multiple points (siege engine assignment,
/// strategy selection, etc.), which a heroless bandit horde never had - letting
/// zombies besiege a town/castle risked hitting one of those hero-dependent
/// paths and hanging or crashing the whole session. TODO.md Phase 2/3
/// reintroduces it, gated strictly on the horde actually having a converted
/// hero as its real LeaderHero (see HasTurnedHero) - see
/// ZombiePartyComponent for how a zombie party gets one at all.
/// </summary>
internal enum ZombieActionType
{
	Hunt,
	RaidVillage,
	SiegeSettlement
}

/// <summary>
/// The three horde-size tiers everything in "Size Behaviour" is ultimately
/// about: Small hordes hunt, Medium hordes turn greedy and start raiding,
/// Large hordes start sieging - see ZombieBehaviorConfig.GetSizeCategory and
/// GetStrategicWeight, and MediumHordeThreshold/LargeHordeThreshold below.
/// </summary>
internal enum ZombieSizeCategory
{
	Small,
	Medium,
	Large
}

/// <summary>
/// All tunable numbers for the zombie horde AI in one place, per TODO.md's own
/// recommendation ("Keep all constants in a single configuration class for easy
/// adjustment") - scoring/threshold balance is expected to need iteration.
/// </summary>
internal static class ZombieBehaviorConfig
{
	// --- Feature 1: young horde survival ---
	public static int YoungHordeMaxTroops = 300;
	public static float YoungHordeFleeThreatMultiplier = 3f;
	public static float YoungFleeThreatRadius = 10f;
	public static float YoungHordeFleeDistance = 30f;
	public static int YoungHordeMaxHuntTroops = 15;
	public static float YoungHuntRadiusMultiplier = 0.4f;

	// --- Speed buffs (all flat campaign-map speed bonuses, all stack, applied
	// via ZombieSpeedBuffPatch - a Harmony postfix on the party speed model,
	// since MobileParty.Speed has no settable field of its own) ---
	// While under YoungHordeMaxTroops - helps a young horde both flee threats
	// and catch weak prey.
	public static float YoungHordeSpeedBonus = 1.5f;
	// While actively chasing a hunt target - adult or young horde alike,
	// anything with DefaultBehavior == EngageParty.
	public static float HuntingSpeedBonus = 3.5f;
	// While traveling to raid a village (DefaultBehavior == RaidSettlement) -
	// a genuine multiplier (applied as a factor, not a flat add) so it holds
	// regardless of how fast/slow the horde's base speed already is. Boosted
	// again per user feedback that even x3 still felt slow approaching a raid.
	public static float RaidApproachSpeedMultiplier = 6f;
	// Unconditional, applies to every zombie party at all times regardless of
	// size or behavior. Left at 0 - a general knob for later tuning.
	public static float GeneralSpeedBonus = 2f;
	// Race traits, applied via ZombieSpeedBuffPatch on DefaultPartySpeedCalculatingModel:
	// a horde that doesn't need to see and doesn't feel pain isn't slowed by
	// night or by its own wounded members.
	public static bool ZombieNightImmune = true;
	public static bool ZombieWoundedSpeedImmune = true;
	// Bug/balance fix: vanilla's "over party size" speed penalty
	// (1/(troops/partySizeLimit) - 1, a heroless tier-4 bandit clan's limit is
	// only ~80) turns catastrophic once a horde snowballs past a few hundred
	// troops - easily enough to floor speed at the game's global minimum (1),
	// which is what actually stops a big horde from ever reaching a hunt
	// target or a village to raid. The whole point of the snowball mechanic is
	// unbounded growth, so zombies are exempted from this penalty entirely.
	public static bool ZombiePartySizeSpeedImmune = true;

	// --- Morale (mission-side courage, applied via ZombieMoralePatch - a
	// Harmony patch on SandboxBattleMoraleModel, the singleplayer-campaign
	// battle morale model) ---
	// Zombies are immune to battle panic/fear entirely: CanPanicDueToMorale is
	// forced to false for zombie agents regardless of this value, since "a
	// zombie that's merely less scared" isn't the ask - this is the on/off
	// switch for that behavior, kept here instead of hardcoded so it can be
	// turned off for testing without touching the patch.
	public static bool ZombiePanicImmune = true;
	// Effective starting morale (0-100 scale) for zombie agents in a live
	// mission, in case anything besides the panic check reads it.
	public static float ZombieInitialMorale = 100f;
	// Race trait, applied via ZombieUpkeepImmunityPatch on DefaultPartyMoraleModel:
	// zombies don't eat or need pay, so the daily starvation and unpaid-wage
	// morale penalties are zeroed out for them.
	public static bool ZombieHungerWageMoraleImmune = true;
	// Race trait, applied via ZombieDisorganizedImmunityPatch on
	// DefaultPartyImpairmentModel.CanGetDisorganized - the single gate every
	// disorganized-state trigger checks first. A mindless horde has no
	// formation to break in the first place, so it is fully immune rather than
	// just getting the vanilla perks' partial (-15%/-15%, never to zero)
	// duration reduction.
	public static bool ZombieDisorganizedImmune = true;

	// --- Hero conversion (TODO.md Phase 2) ---
	// A defeated enemy hero's chance to turn instead of merely escaping:
	// finalChance = HeroTurnBaseChance + (SmallHordeMax - currentTroops) /
	// SmallHordeMax * HeroTurnTroopSizeModifier, clamped to [0,1] - a small
	// horde gets the full size bonus on top of the base chance, a horde already
	// at or past SmallHordeMax gets none (and can dip below base for hordes
	// well past it), since a big horde needs the occasional new hero far less
	// than a small one trying to survive its first fights.
	public static float HeroTurnBaseChance = 0.4f;
	public static float HeroTurnTroopSizeModifier = 0.3f;
	// Pulled forward from the AI Phase System (Phase 4, not built yet) - used
	// here only as the "small horde" reference size for the formula above.
	public static int SmallHordeMax = 300;

	// --- Debug: farsight cheat (zombie.toggle_farsight) ---
	// Flat map spotting range applied to the player's own party while enabled,
	// replacing the normal ~12-20 unit range entirely (see PlayerFarsightPatch).
	public static float FarsightSeeingRange = 300f;

	// --- Feature 2/5: raid and siege hard constraints ---
	public static int VillageRaidTroopThreshold = 150;
	public static int VillageRaidTroopThresholdHighQuality = 100;
	// Siege only ever becomes a candidate for a horde with a real LeaderHero
	// (see HasTurnedHero) - see ZombieActionType for why.
	public static int SettlementSiegeTroopThreshold = 400;
	public static float SiegeStrengthMultiplier = 1.5f;

	// Re-enabled: a hero-led ZombiePartyComponent crashed immediately on
	// SetPartyAiAction.GetActionForBesiegingSettlement (confirmed via crash
	// dump: 0xC0000005, null-read at field offset 0x18) - something vanilla's
	// siege pipeline expects from a "real" lord party was missing on ours,
	// and root-causing the exact field needed symbol-resolution tooling this
	// environment doesn't have. Workaround (see
	// ZombiePartyComponent.SwapToLordForSiege): the party is temporarily
	// swapped to an actual LordPartyComponent only while besieging, then
	// swapped back to ZombiePartyComponent (restoring IsBandit) once it
	// stops. Toggle kept in case this needs to be disabled again quickly.
	public static bool SiegeDisabledPendingInvestigation = false;

	// "High quality" horde: at least half the roster is tier 3+ (regular soldiers
	// and up, not just looters/recruits) - such a party raids sooner.
	public static int HighQualityTierMin = 3;
	public static float HighQualityShareMin = 0.5f;

	// Bug fix: the "no hostile party within InterceptionRadius of the village"
	// raid hard-constraint was an absolute veto - a single weak straggler
	// merely passing near the village (the same passerby the horde might
	// already be fruitlessly chasing) blocked raiding forever, even for a
	// horde that could crush it without slowing down. A nearby party only
	// counts as a raid-blocking threat if its troop count is at least this
	// fraction of the horde's own - large enough to actually contest a raid.
	public static float RaidBlockMinThreatRatio = 0.3f;

	// --- Greedy growth (less meticulous decision-making for big hordes) ---
	// At this size, losing the occasional straggler barely matters next to the
	// growth from winning - so the horde stops being picky about risk/distance
	// and just goes after whatever fight or raid is actually available.
	// Defaults to the same value as MediumHordeThreshold below - greedy mode
	// is meant to switch on exactly when a horde becomes "medium", per
	// GetStrategicWeight/GetSizeCategory.
	public static int GreedyGrowthTroopThreshold = 150;
	// Flat hunt strength-ratio floor while greedy - looser than the dynamic
	// GetMinStrengthRatio bracket for this size; the looser of the two always
	// wins once greedy mode applies.
	public static float GreedyMinStrengthRatio = 0.5f;
	// Village raid troop threshold while greedy - lets the horde start raiding
	// as soon as it qualifies for greedy mode instead of waiting for the full
	// VillageRaidTroopThreshold.
	public static int GreedyVillageRaidTroopThreshold = 100;
	// Hunting search radius multiplier while greedy - casts a wider net instead
	// of only reacting to whatever wanders into the normal spawn radius.
	public static float GreedySearchRadiusMultiplier = 1.5f;
	// Squares the hunt distance factor while greedy (>=1 means no change) - this
	// is what makes "8 looters right next to us" beat "24 villagers much further
	// away": normally reward (troop count) dominates over a merely-linear
	// distance penalty, so a bigger-but-farther target could still win. Squaring
	// distance makes proximity the dominant factor instead, so the horde takes
	// whatever it can reach fastest rather than optimizing for the biggest catch.
	public static float GreedyHuntDistanceExponent = 2f;

	// --- Feature 2/3: scoring ---
	public static float PreyTypeBonusMultiplier = 1.5f;
	public static float IsolationPenaltyMultiplier = 0.5f;
	public static float InterceptionRadius = 10f;
	public static float VillageHearthRewardWeight = 0.2f;

	// --- Feature 2: target commitment (anti-flip-flopping) ---
	// The party's current hunt/raid/siege target gets its score multiplied by
	// this before ranking against fresh candidates - a challenger has to clear
	// the boosted score to win the ranking at all.
	public static float CommitmentBonusMultiplier = 1.5f;
	// Independent of the bonus above: even a challenger that wins the boosted
	// ranking must also beat the incumbent's own RAW score by this margin, so a
	// horde won't abandon a target it is already closing in on over a marginal
	// gain. Same default as CommitmentBonusMultiplier today, but tuned separately.
	public static float CommitmentRequiredScoreIncrease = 1.5f;

	/// <summary>Dynamic minimum myTroops/targetTroops ratio a hunt candidate must clear.</summary>
	public static float GetMinStrengthRatio(int myTroops)
	{
		if (myTroops < 100)
		{
			return 1.0f;
		}

		if (myTroops < 300)
		{
			return 0.8f;
		}

		return 0.7f;
	}

	/// <summary>
	/// How much a horde of this size cares about each action type, straight from
	/// the TODO.md table. Siege weights only ever matter for a hero-led horde -
	/// see HasTurnedHero - since it's the only kind that ever gets a
	/// SiegeSettlement candidate in the first place.
	/// </summary>
	public static float GetStrategicWeight(int myTroops, ZombieActionType type)
	{
		switch (GetSizeCategory(myTroops))
		{
			case ZombieSizeCategory.Small:
				// Small: hunting is what keeps a young horde alive and growing.
				return type switch
				{
					ZombieActionType.Hunt => 1.5f,
					ZombieActionType.RaidVillage => 0.8f,
					ZombieActionType.SiegeSettlement => 0.1f,
					_ => 0f
				};

			case ZombieSizeCategory.Medium:
				// Medium: this is also where GreedyGrowthTroopThreshold kicks
				// in (see ApplyDifficultyPreset) - raiding overtakes hunting
				// as the priority, and siege becomes a real (if secondary)
				// option for a hero-led horde.
				return type switch
				{
					ZombieActionType.Hunt => 1.0f,
					ZombieActionType.RaidVillage => 1.2f,
					ZombieActionType.SiegeSettlement => 0.5f,
					_ => 0f
				};

			default:
				// Large: this is also SettlementSiegeTroopThreshold's default
				// value - a hero-led horde this big favors sieging a
				// town/castle as much as raiding, and no longer bothers much
				// with hunting individual parties.
				return type switch
				{
					ZombieActionType.Hunt => 0.5f,
					ZombieActionType.RaidVillage => 1.5f,
					ZombieActionType.SiegeSettlement => 1.5f,
					_ => 0f
				};
		}
	}

	// --- Feature 4: splitting ---
	// Soft split threshold band: no split below 500, guaranteed (100% daily
	// chance) at 600+, linear chance in between.
	public static int SplitMinTroops = 500;
	public static int SplitMaxTroops = 600;
	public static float SplitFraction = 0.3f;
	public static float SplitAwayDistanceFactor = 0.8f;
	// Where the split-off party's spawn point lands relative to the parent,
	// before it's redirected away via SplitAwayDistanceFactor above.
	public static float SplitSpawnMaxDistanceFactor = 0.15f;
	public static float SplitSpawnMinDistanceFactor = 0.05f;

	// --- Wandering ---
	// How far a party looks for a fresh point when nothing else scores, and how
	// often (in-game hours) it re-rolls instead of continuing toward the last one.
	// Lowered from 6h: a bad wander point (rare navmesh dead zone) used to sit
	// unresolved for a full 6 in-game hours before retrying, which read as the
	// horde "standing around for no reason."
	public static float WanderRadiusMultiplier = 0.5f;
	public static float WanderRerollIntervalHours = 2f;

	// --- Respawn after a total defeat (see OnMobilePartyDestroyed) ---
	// The replacement horde spawns within [distance * Min, distance * Max] of
	// where the old one died.
	public static float RespawnMaxDistanceFactor = 1f;
	public static float RespawnMinDistanceFactor = 0.3f;

	// Distance smoothing added to a young horde's hunt-target score denominator,
	// so two very-close prey don't produce a wildly spiking score near distance 0.
	public static float HuntScoreDistanceSmoothing = 5f;

	// How often (real-time seconds) the debug status dump prints to screen -
	// not an AI/balance knob, just log spam pacing.
	public static float DebugTickIntervalSeconds = 10f;

	// TODO.md item 6: how often (real-time seconds) ZombieAmbientSoundMissionBehavior
	// plays a random zombie growl/moan during a mission the zombie clan is
	// fighting in. Both source clips are short single barks, not a loop, so
	// this just needs to be long enough not to sound like machine-gun grunting.
	public static float AmbientSoundIntervalSeconds = 12f;

	// When enabled, ZombieLog.Warn/Error also post an in-game chat message
	// (see ZombieLog) instead of only writing to the engine/file log - lets a
	// player surface a caught exception without digging through log files.
	public static bool DebugChatMessagesEnabled = false;

	// --- Stuck-party watchdog (bug prevention) ---
	// If a party moves less than this many map units within an hourly tick it
	// counts as stationary. After StuckThresholdHours of that in a row it is
	// forced to a fresh distant point and its AI commitment is cleared - a
	// safety net against the "just stands there" navmesh/order bug documented
	// on Wander below, in case some future path ever reintroduces it. Lowered
	// from 48h (2 full in-game days!) to something a player will not perceive
	// as the AI being broken before it self-corrects.
	public static float StuckMovementThreshold = 5f;
	public static float StuckThresholdHours = 10f;
	public static float StuckEscapeDistanceFactor = 1f;

	// --- Difficulty preset (MCM dropdown: 0=Easy, 1=Medium, 2=Hard) ---
	// Baseline ("Medium") matches every default value in this file as it stood
	// before presets were added. Applying a preset overwrites the specific
	// fields listed in ApplyDifficultyPreset below - anything tuned by hand
	// outside that set is left alone, but a hand-tuned field inside that set
	// gets clobbered the next time a preset is (re-)applied.
	public static int DifficultyPresetIndex = 1;

	// --- Starting options (initial horde spawn, see
	// ZombiePlagueCampaignBehavior.TrySpawnStartingHordes) ---
	public static int NumberOfStartingParties = 1;
	public static int StartingGroupSize = 10;
	// 0 = Patient Zero, 1 = Zombie Looter, 2 = Zombie Legionary - see ZombieIds.StartingTroopId.
	public static int StartingTroopTypeIndex = 0;
	public static float SpawnDelayDays = 2f;

	// --- Size behaviour thresholds ---
	// Reused by GetStrategicWeight's brackets below and by the "medium/large
	// horde" event notifications in ZombiePlagueCampaignBehavior. Also the
	// reference points the other size-gated behaviors below are meant to line
	// up with: greedy growth engages at Medium, sieging requires Large - see
	// GetSizeCategory.
	public static int MediumHordeThreshold = 300;
	public static int LargeHordeThreshold = 1000;

	public static ZombieSizeCategory GetSizeCategory(int myTroops)
	{
		if (myTroops < MediumHordeThreshold)
		{
			return ZombieSizeCategory.Small;
		}

		return myTroops < LargeHordeThreshold ? ZombieSizeCategory.Medium : ZombieSizeCategory.Large;
	}

	// --- Weak party reinforcement (balance) ---
	// A straggler party this small (a lone split-off, or the last survivors of
	// a lost fight) is one bad encounter from extinction and would otherwise
	// just wander until it dies - a slow trickle of fresh, strong troops keeps
	// it a real (if minor) threat instead. Patient Zero specifically: already
	// the "can this handle itself against a small looter band" benchmark troop
	// (see zombie_patient_zero in zombie_troops.xml), so it is the natural
	// reinforcement pick for a party this weak.
	public static int WeakPartyReinforceMaxTroops = 5;

	/// <summary>
	/// Applies a curated bundle of values for one of the three MCM difficulty
	/// presets. Deliberately narrow: only the fields most responsible for how
	/// fast/aggressive/dangerous the plague feels are touched, everything else
	/// (scoring internals, splitting geometry, watchdog timings, ...) is left
	/// for manual tuning regardless of difficulty.
	/// </summary>
	public static void ApplyDifficultyPreset(int index)
	{
		DifficultyPresetIndex = index;
		ZombieLog.Info("ApplyDifficultyPreset: preset index " + index + " (" + (index switch { 0 => "Easy", 2 => "Hard", _ => "Medium" }) + ")");
		switch (index)
		{
			case 0: // Easy
				HuntingSpeedBonus = 2.5f;
				RaidApproachSpeedMultiplier = 4f;
				GeneralSpeedBonus = 1f;
				HeroTurnBaseChance = 0.25f;
				HeroTurnTroopSizeModifier = 0.2f;
				GreedyGrowthTroopThreshold = 200;
				SplitMinTroops = 700;
				SplitMaxTroops = 900;
				VillageRaidTroopThreshold = 200;
				SettlementSiegeTroopThreshold = 600;
				StartingGroupSize = 6;
				break;

			case 2: // Hard
				HuntingSpeedBonus = 4.5f;
				RaidApproachSpeedMultiplier = 8f;
				GeneralSpeedBonus = 3f;
				HeroTurnBaseChance = 0.55f;
				HeroTurnTroopSizeModifier = 0.4f;
				GreedyGrowthTroopThreshold = 100;
				SplitMinTroops = 350;
				SplitMaxTroops = 450;
				VillageRaidTroopThreshold = 100;
				SettlementSiegeTroopThreshold = 300;
				StartingGroupSize = 14;
				break;

			default: // Medium (baseline)
				HuntingSpeedBonus = 3.5f;
				RaidApproachSpeedMultiplier = 6f;
				GeneralSpeedBonus = 2f;
				HeroTurnBaseChance = 0.4f;
				HeroTurnTroopSizeModifier = 0.3f;
				// Same as MediumHordeThreshold - greedy engages exactly at "medium" here.
				GreedyGrowthTroopThreshold = 150;
				SplitMinTroops = 500;
				SplitMaxTroops = 600;
				VillageRaidTroopThreshold = 150;
				SettlementSiegeTroopThreshold = 400;
				StartingGroupSize = 10;
				break;
		}
	}

	// --- Frustrated raider (opportunistic pillaging) ---
	// A horde at or above this size that has failed to land a hunt (its target
	// escaped rather than died) FrustrationAbandonThreshold+ times within the
	// last FrustrationWindowDays gives up hunting for a while and raids the
	// nearest healthy village instead - bypassing the normal
	// VillageRaidTroopThreshold hard constraint - but only while no hostile
	// party is within FrustrationRaidCancelRadius; one showing up mid-raid
	// cancels it and the horde flees instead.
	public static int FrustrationTroopThreshold = 80;
	public static int FrustrationAbandonThreshold = 15;
	public static float FrustrationWindowDays = 7f;
	public static float FrustrationRaidCancelRadius = 15f;

	// --- Settlement debuffs (shared by village raids today, city sieges later) ---
	public static float VillageDebuffDurationDays = 7f;
	public static float ProsperityPenaltyPercent = 10f;
	public static bool BlockRecruitmentEnabled = true;

	// --- Village raids: prosperity/Hearth converted into fresh zombies on raid completion ---
	public static float ProsperityToZombieMultiplier = 0.05f;
	public static int MinZombiesGained = 2;
	public static int MaxZombiesGained = 10;
	public static float ZombieGainVariancePercent = 20f;
}
