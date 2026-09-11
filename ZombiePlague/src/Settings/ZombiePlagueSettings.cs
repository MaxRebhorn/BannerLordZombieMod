using MCM.Abstractions.Attributes;
using MCM.Abstractions.Attributes.v2;
using MCM.Abstractions.Base.Global;
using MCM.Common;
using ZombiePlague.Infrastructure;

namespace ZombiePlague.Settings;

/// <summary>
/// MCM (Mod Configuration Menu) settings screen for every tunable number in
/// ZombieBehaviorConfig. MCM auto-discovers any loaded AttributeGlobalSettings&lt;T&gt;
/// subclass with a public parameterless constructor - no manual registration
/// needed in SubModule.cs.
///
/// Every property here is a thin pass-through to the matching
/// ZombieBehaviorConfig field (which had to become a mutable `static` field
/// rather than `const` for this to be possible at all - see that file). This
/// way every existing call site across the mod keeps reading
/// ZombieBehaviorConfig.X unchanged; MCM is just another way to write into
/// the same single source of truth, and whatever the user last set survives
/// exactly like any other MCM mod's settings (its own JSON file under
/// Documents/Mount and Blade II Bannerlord/Configs/ModSettings).
///
/// Groups (in display order):
///   0 General - difficulty preset, starting spawn options, and the
///     small/medium/large horde troop thresholds (the "which size is this
///     horde" knobs everything else keys off of).
///   1 Settlement Debuffs - shared village/city raid debuff knobs.
///   2 Village Raids - prosperity-to-zombie conversion (Feature A).
///   3 Balance - raw strength/speed/morale/hero-conversion/scoring numbers.
///   4 AI - decision thresholds/behavior toggles (young horde survival,
///     raid/siege constraints, target commitment, wandering, stuck
///     watchdog, frustrated raider, greedy growth, splitting, respawn).
///   5 Debug - dev/testing toggles.
/// </summary>
public sealed class ZombiePlagueSettings : AttributeGlobalSettings<ZombiePlagueSettings>
{
	public override string Id => "ZombiePlague_v1";
	public override string DisplayName => "Zombie Plague";
	public override string FolderName => "ZombiePlague";
	public override string FormatType => "json2";

	// --- General: difficulty ---

	[SettingPropertyDropdown("Difficulty Preset", Order = 0, RequireRestart = false, HintText = "Applies a bundle of Balance/AI values tuned for that difficulty. You can still fine-tune individual sliders afterwards - just note that re-selecting a preset (even the same one) re-applies its values and overwrites those specific sliders again.")]
	[SettingPropertyGroup("General", GroupOrder = 0)]
	public Dropdown<string> DifficultyPreset
	{
		get => new Dropdown<string>(new[] { "Easy", "Medium", "Hard" }, ZombieBehaviorConfig.DifficultyPresetIndex);
		set => ZombieBehaviorConfig.ApplyDifficultyPreset(value.SelectedIndex);
	}

	// --- General: starting spawn options ---

	[SettingPropertyInteger("Number Of Starting Parties", 1, 10, Order = 1, RequireRestart = false, HintText = "How many zombie hordes spawn at random towns/villages when the initial spawn triggers.")]
	[SettingPropertyGroup("General", GroupOrder = 0)]
	public int NumberOfStartingParties
	{
		get => ZombieBehaviorConfig.NumberOfStartingParties;
		set => ZombieBehaviorConfig.NumberOfStartingParties = value;
	}

	[SettingPropertyInteger("Starting Group Size", 1, 100, Order = 2, RequireRestart = false, HintText = "Troop count of each starting horde.")]
	[SettingPropertyGroup("General", GroupOrder = 0)]
	public int StartingGroupSize
	{
		get => ZombieBehaviorConfig.StartingGroupSize;
		set => ZombieBehaviorConfig.StartingGroupSize = value;
	}

	[SettingPropertyDropdown("Starting Troop Type", Order = 3, RequireRestart = false, HintText = "Which troop the starting horde(s) are made of.")]
	[SettingPropertyGroup("General", GroupOrder = 0)]
	public Dropdown<string> StartingTroopType
	{
		get => new Dropdown<string>(new[] { "Patient Zero", "Zombie Looter", "Zombie Legionary" }, ZombieBehaviorConfig.StartingTroopTypeIndex);
		set => ZombieBehaviorConfig.StartingTroopTypeIndex = value.SelectedIndex;
	}

	[SettingPropertyFloatingInteger("Spawn Delay (days)", 0f, 30f, Order = 4, RequireRestart = false, HintText = "In-game days after a new campaign starts before the initial zombie horde(s) spawn.")]
	[SettingPropertyGroup("General", GroupOrder = 0)]
	public float SpawnDelayDays
	{
		get => ZombieBehaviorConfig.SpawnDelayDays;
		set => ZombieBehaviorConfig.SpawnDelayDays = value;
	}

	// --- General: horde-size troop thresholds ---

	[SettingPropertyInteger("Small Horde Max", 1, 2000, Order = 5, RequireRestart = false, HintText = "Reference horde size used by the hero-turn-chance formula (see Balance) - a horde at or under this size gets the full size bonus to its turn chance.")]
	[SettingPropertyGroup("General", GroupOrder = 0)]
	public int SmallHordeMax
	{
		get => ZombieBehaviorConfig.SmallHordeMax;
		set => ZombieBehaviorConfig.SmallHordeMax = value;
	}

	[SettingPropertyInteger("Medium Horde Threshold", 1, 5000, Order = 6, RequireRestart = false, HintText = "Troop count (per party) at which a horde is considered 'medium' - shifts its strategic priorities toward raiding/sieging, and triggers the 'first medium horde' notification.")]
	[SettingPropertyGroup("General", GroupOrder = 0)]
	public int MediumHordeThreshold
	{
		get => ZombieBehaviorConfig.MediumHordeThreshold;
		set => ZombieBehaviorConfig.MediumHordeThreshold = value;
	}

	[SettingPropertyInteger("Large Horde Threshold", 1, 10000, Order = 7, RequireRestart = false, HintText = "Troop count (per party) at which a horde is considered 'large' - shifts its strategic priorities further toward raiding/sieging over hunting, and triggers the 'first large horde' notification. Sieging (see Settlement Siege Troop Threshold in AI) is meant to line up with this.")]
	[SettingPropertyGroup("General", GroupOrder = 0)]
	public int LargeHordeThreshold
	{
		get => ZombieBehaviorConfig.LargeHordeThreshold;
		set => ZombieBehaviorConfig.LargeHordeThreshold = value;
	}

	[SettingPropertyInteger("Weak Party Reinforce Max Troops", 0, 50, Order = 8, RequireRestart = false, HintText = "A zombie party at or under this troop count gains 1 free Patient Zero every day, so a tiny straggler (a split-off, or the survivors of a lost fight) has a chance to recover instead of just wandering until it dies. Set to 0 to disable.")]
	[SettingPropertyGroup("General", GroupOrder = 0)]
	public int WeakPartyReinforceMaxTroops
	{
		get => ZombieBehaviorConfig.WeakPartyReinforceMaxTroops;
		set => ZombieBehaviorConfig.WeakPartyReinforceMaxTroops = value;
	}

	// --- Settlement Debuffs (shared by village raids today, city sieges later) ---

	[SettingPropertyFloatingInteger("Village Debuff Duration (days)", 0.5f, 60f, Order = 0, RequireRestart = false, HintText = "How many in-game days a raided settlement's debuff (prosperity decay + recruitment block) lasts before expiring.")]
	[SettingPropertyGroup("Settlement Debuffs", GroupOrder = 1)]
	public float VillageDebuffDurationDays
	{
		get => ZombieBehaviorConfig.VillageDebuffDurationDays;
		set => ZombieBehaviorConfig.VillageDebuffDurationDays = value;
	}

	[SettingPropertyFloatingInteger("Prosperity Penalty (%)", 0f, 100f, Order = 1, RequireRestart = false, HintText = "Percentage of a debuffed settlement's remaining prosperity/Hearth stripped away on each daily tick, floored to a whole number.")]
	[SettingPropertyGroup("Settlement Debuffs", GroupOrder = 1)]
	public float ProsperityPenaltyPercent
	{
		get => ZombieBehaviorConfig.ProsperityPenaltyPercent;
		set => ZombieBehaviorConfig.ProsperityPenaltyPercent = value;
	}

	[SettingPropertyBool("Block Recruitment While Debuffed", Order = 2, RequireRestart = false, HintText = "While a settlement is debuffed, its notables offer zero volunteers for recruitment.")]
	[SettingPropertyGroup("Settlement Debuffs", GroupOrder = 1)]
	public bool BlockRecruitmentEnabled
	{
		get => ZombieBehaviorConfig.BlockRecruitmentEnabled;
		set => ZombieBehaviorConfig.BlockRecruitmentEnabled = value;
	}

	// --- Village Raids ---

	[SettingPropertyFloatingInteger("Prosperity-To-Zombies Multiplier", 0f, 2f, Order = 0, RequireRestart = false, HintText = "Base zombie gain from a completed raid is (village prosperity/Hearth / 100) * this multiplier, before variance and clamping.")]
	[SettingPropertyGroup("Village Raids", GroupOrder = 2)]
	public float ProsperityToZombieMultiplier
	{
		get => ZombieBehaviorConfig.ProsperityToZombieMultiplier;
		set => ZombieBehaviorConfig.ProsperityToZombieMultiplier = value;
	}

	[SettingPropertyInteger("Minimum Zombies Gained", 0, 100, Order = 1, RequireRestart = false, HintText = "Floor on the number of zombies gained from raiding a village, regardless of how small its prosperity was.")]
	[SettingPropertyGroup("Village Raids", GroupOrder = 2)]
	public int MinZombiesGained
	{
		get => ZombieBehaviorConfig.MinZombiesGained;
		set => ZombieBehaviorConfig.MinZombiesGained = value;
	}

	[SettingPropertyInteger("Maximum Zombies Gained", 0, 200, Order = 2, RequireRestart = false, HintText = "Ceiling on the number of zombies gained from raiding a single village, regardless of how large its prosperity was.")]
	[SettingPropertyGroup("Village Raids", GroupOrder = 2)]
	public int MaxZombiesGained
	{
		get => ZombieBehaviorConfig.MaxZombiesGained;
		set => ZombieBehaviorConfig.MaxZombiesGained = value;
	}

	[SettingPropertyFloatingInteger("Zombie Gain Variance (%)", 0f, 100f, Order = 3, RequireRestart = false, HintText = "Random +/- swing applied to the base zombie gain before it's clamped to Min/Max above.")]
	[SettingPropertyGroup("Village Raids", GroupOrder = 2)]
	public float ZombieGainVariancePercent
	{
		get => ZombieBehaviorConfig.ZombieGainVariancePercent;
		set => ZombieBehaviorConfig.ZombieGainVariancePercent = value;
	}

	// --- Balance: speed buffs ---

	[SettingPropertyFloatingInteger("Young Horde Speed Bonus", 0f, 10f, Order = 10, RequireRestart = false, HintText = "Flat campaign-map speed bonus while the horde is still 'young' (under Young Horde Max Troops).")]
	[SettingPropertyGroup("Balance", GroupOrder = 3)]
	public float YoungHordeSpeedBonus
	{
		get => ZombieBehaviorConfig.YoungHordeSpeedBonus;
		set => ZombieBehaviorConfig.YoungHordeSpeedBonus = value;
	}

	[SettingPropertyFloatingInteger("Hunting Speed Bonus", 0f, 10f, Order = 11, RequireRestart = false, HintText = "Flat speed bonus while actively chasing a hunt target.")]
	[SettingPropertyGroup("Balance", GroupOrder = 3)]
	public float HuntingSpeedBonus
	{
		get => ZombieBehaviorConfig.HuntingSpeedBonus;
		set => ZombieBehaviorConfig.HuntingSpeedBonus = value;
	}

	[SettingPropertyFloatingInteger("Raid Approach Speed Multiplier", 1f, 15f, Order = 12, RequireRestart = false, HintText = "Speed multiplier (not flat bonus) while traveling to raid a village.")]
	[SettingPropertyGroup("Balance", GroupOrder = 3)]
	public float RaidApproachSpeedMultiplier
	{
		get => ZombieBehaviorConfig.RaidApproachSpeedMultiplier;
		set => ZombieBehaviorConfig.RaidApproachSpeedMultiplier = value;
	}

	[SettingPropertyFloatingInteger("General Speed Bonus", 0f, 10f, Order = 13, RequireRestart = false, HintText = "Flat speed bonus applied to every zombie party at all times, regardless of size or behavior.")]
	[SettingPropertyGroup("Balance", GroupOrder = 3)]
	public float GeneralSpeedBonus
	{
		get => ZombieBehaviorConfig.GeneralSpeedBonus;
		set => ZombieBehaviorConfig.GeneralSpeedBonus = value;
	}

	[SettingPropertyBool("Zombies Immune To Night Speed Penalty", Order = 14, RequireRestart = false, HintText = "Zombies don't need to see, so night doesn't slow them down.")]
	[SettingPropertyGroup("Balance", GroupOrder = 3)]
	public bool ZombieNightImmune
	{
		get => ZombieBehaviorConfig.ZombieNightImmune;
		set => ZombieBehaviorConfig.ZombieNightImmune = value;
	}

	[SettingPropertyBool("Zombies Immune To Wounded Speed Penalty", Order = 15, RequireRestart = false, HintText = "Zombies don't feel pain, so having wounded members doesn't slow the party down.")]
	[SettingPropertyGroup("Balance", GroupOrder = 3)]
	public bool ZombieWoundedSpeedImmune
	{
		get => ZombieBehaviorConfig.ZombieWoundedSpeedImmune;
		set => ZombieBehaviorConfig.ZombieWoundedSpeedImmune = value;
	}

	[SettingPropertyBool("Zombies Immune To Over-Size Speed Penalty", Order = 16, RequireRestart = false, HintText = "Without this, a horde much bigger than its nominal party size limit would grind to the game's minimum speed - defeating the whole point of unbounded growth.")]
	[SettingPropertyGroup("Balance", GroupOrder = 3)]
	public bool ZombiePartySizeSpeedImmune
	{
		get => ZombieBehaviorConfig.ZombiePartySizeSpeedImmune;
		set => ZombieBehaviorConfig.ZombiePartySizeSpeedImmune = value;
	}

	// --- Balance: morale ---

	[SettingPropertyBool("Zombies Immune To Battle Panic", Order = 20, RequireRestart = false, HintText = "Zombies never panic/flee mid-battle from low morale.")]
	[SettingPropertyGroup("Balance", GroupOrder = 3)]
	public bool ZombiePanicImmune
	{
		get => ZombieBehaviorConfig.ZombiePanicImmune;
		set => ZombieBehaviorConfig.ZombiePanicImmune = value;
	}

	[SettingPropertyFloatingInteger("Zombie Initial Morale", 0f, 100f, Order = 21, RequireRestart = false, HintText = "Effective starting morale for zombie agents in a live mission.")]
	[SettingPropertyGroup("Balance", GroupOrder = 3)]
	public float ZombieInitialMorale
	{
		get => ZombieBehaviorConfig.ZombieInitialMorale;
		set => ZombieBehaviorConfig.ZombieInitialMorale = value;
	}

	[SettingPropertyBool("Zombies Immune To Hunger/Wage Morale Penalty", Order = 22, RequireRestart = false, HintText = "Zombies don't eat or need pay, so starvation/unpaid-wage morale penalties never apply.")]
	[SettingPropertyGroup("Balance", GroupOrder = 3)]
	public bool ZombieHungerWageMoraleImmune
	{
		get => ZombieBehaviorConfig.ZombieHungerWageMoraleImmune;
		set => ZombieBehaviorConfig.ZombieHungerWageMoraleImmune = value;
	}

	[SettingPropertyBool("Zombies Immune To Disorganization", Order = 23, RequireRestart = false, HintText = "A mindless horde has no formation to break, so it's fully immune to the disorganized state.")]
	[SettingPropertyGroup("Balance", GroupOrder = 3)]
	public bool ZombieDisorganizedImmune
	{
		get => ZombieBehaviorConfig.ZombieDisorganizedImmune;
		set => ZombieBehaviorConfig.ZombieDisorganizedImmune = value;
	}

	// --- Balance: hero conversion ---

	[SettingPropertyFloatingInteger("Hero Turn Base Chance", 0f, 1f, Order = 30, RequireRestart = false, HintText = "Base chance (0-1) that a defeated enemy hero turns into a zombie instead of merely escaping.")]
	[SettingPropertyGroup("Balance", GroupOrder = 3)]
	public float HeroTurnBaseChance
	{
		get => ZombieBehaviorConfig.HeroTurnBaseChance;
		set => ZombieBehaviorConfig.HeroTurnBaseChance = value;
	}

	[SettingPropertyFloatingInteger("Hero Turn Troop Size Modifier", 0f, 1f, Order = 31, RequireRestart = false, HintText = "Extra turn chance a small horde (under Small Horde Max, see General) gets on top of the base chance.")]
	[SettingPropertyGroup("Balance", GroupOrder = 3)]
	public float HeroTurnTroopSizeModifier
	{
		get => ZombieBehaviorConfig.HeroTurnTroopSizeModifier;
		set => ZombieBehaviorConfig.HeroTurnTroopSizeModifier = value;
	}

	// --- Balance: scoring ---

	[SettingPropertyFloatingInteger("Prey Type Bonus Multiplier", 0.1f, 5f, Order = 40, RequireRestart = false, HintText = "Reward multiplier for hunting caravans/villagers over other prey types.")]
	[SettingPropertyGroup("Balance", GroupOrder = 3)]
	public float PreyTypeBonusMultiplier
	{
		get => ZombieBehaviorConfig.PreyTypeBonusMultiplier;
		set => ZombieBehaviorConfig.PreyTypeBonusMultiplier = value;
	}

	[SettingPropertyFloatingInteger("Isolation Penalty Multiplier", 0.05f, 1f, Order = 41, RequireRestart = false, HintText = "Reward penalty multiplier applied when a target is not isolated (a hostile party is nearby).")]
	[SettingPropertyGroup("Balance", GroupOrder = 3)]
	public float IsolationPenaltyMultiplier
	{
		get => ZombieBehaviorConfig.IsolationPenaltyMultiplier;
		set => ZombieBehaviorConfig.IsolationPenaltyMultiplier = value;
	}

	[SettingPropertyFloatingInteger("Interception Radius", 1f, 100f, Order = 42, RequireRestart = false, HintText = "Radius used by the isolation check to look for other hostile parties near a target.")]
	[SettingPropertyGroup("Balance", GroupOrder = 3)]
	public float InterceptionRadius
	{
		get => ZombieBehaviorConfig.InterceptionRadius;
		set => ZombieBehaviorConfig.InterceptionRadius = value;
	}

	[SettingPropertyFloatingInteger("Village Hearth Reward Weight", 0f, 2f, Order = 43, RequireRestart = false, HintText = "How much a village's Hearth stat contributes to its raid reward score, alongside its militia.")]
	[SettingPropertyGroup("Balance", GroupOrder = 3)]
	public float VillageHearthRewardWeight
	{
		get => ZombieBehaviorConfig.VillageHearthRewardWeight;
		set => ZombieBehaviorConfig.VillageHearthRewardWeight = value;
	}

	[SettingPropertyFloatingInteger("Hunt Score Distance Smoothing", 0f, 50f, Order = 44, RequireRestart = false, HintText = "Smoothing added to a young horde's hunt-target score denominator so very-close prey doesn't spike the score near distance 0.")]
	[SettingPropertyGroup("Balance", GroupOrder = 3)]
	public float HuntScoreDistanceSmoothing
	{
		get => ZombieBehaviorConfig.HuntScoreDistanceSmoothing;
		set => ZombieBehaviorConfig.HuntScoreDistanceSmoothing = value;
	}

	// --- AI: young horde survival ---

	[SettingPropertyInteger("Young Horde Max Troops", 1, 500, Order = 0, RequireRestart = false, HintText = "Below this troop count a horde is 'young': it flees threats instead of fighting and only hunts very weak prey.")]
	[SettingPropertyGroup("AI", GroupOrder = 4)]
	public int YoungHordeMaxTroops
	{
		get => ZombieBehaviorConfig.YoungHordeMaxTroops;
		set => ZombieBehaviorConfig.YoungHordeMaxTroops = value;
	}

	[SettingPropertyFloatingInteger("Young Horde Flee Threat Multiplier", 1f, 20f, Order = 1, RequireRestart = false, HintText = "How much stronger a nearby hostile party needs to be (relative to the horde) before a young horde flees it.")]
	[SettingPropertyGroup("AI", GroupOrder = 4)]
	public float YoungHordeFleeThreatMultiplier
	{
		get => ZombieBehaviorConfig.YoungHordeFleeThreatMultiplier;
		set => ZombieBehaviorConfig.YoungHordeFleeThreatMultiplier = value;
	}

	[SettingPropertyFloatingInteger("Young Flee Threat Radius", 1f, 100f, Order = 2, RequireRestart = false, HintText = "How far away a threatening party can be and still trigger a young horde to flee.")]
	[SettingPropertyGroup("AI", GroupOrder = 4)]
	public float YoungFleeThreatRadius
	{
		get => ZombieBehaviorConfig.YoungFleeThreatRadius;
		set => ZombieBehaviorConfig.YoungFleeThreatRadius = value;
	}

	[SettingPropertyFloatingInteger("Young Horde Flee Distance", 1f, 200f, Order = 3, RequireRestart = false, HintText = "How far a young horde runs when fleeing a threat.")]
	[SettingPropertyGroup("AI", GroupOrder = 4)]
	public float YoungHordeFleeDistance
	{
		get => ZombieBehaviorConfig.YoungHordeFleeDistance;
		set => ZombieBehaviorConfig.YoungHordeFleeDistance = value;
	}

	[SettingPropertyInteger("Young Horde Max Hunt Troops", 1, 200, Order = 4, RequireRestart = false, HintText = "A young horde will only hunt prey parties with at most this many troops.")]
	[SettingPropertyGroup("AI", GroupOrder = 4)]
	public int YoungHordeMaxHuntTroops
	{
		get => ZombieBehaviorConfig.YoungHordeMaxHuntTroops;
		set => ZombieBehaviorConfig.YoungHordeMaxHuntTroops = value;
	}

	[SettingPropertyFloatingInteger("Young Hunt Radius Multiplier", 0.05f, 3f, Order = 5, RequireRestart = false, HintText = "A young horde's hunting search radius, as a fraction of the normal (adult) search radius.")]
	[SettingPropertyGroup("AI", GroupOrder = 4)]
	public float YoungHuntRadiusMultiplier
	{
		get => ZombieBehaviorConfig.YoungHuntRadiusMultiplier;
		set => ZombieBehaviorConfig.YoungHuntRadiusMultiplier = value;
	}

	// --- AI: raid / siege hard constraints ---

	[SettingPropertyInteger("Village Raid Troop Threshold", 1, 2000, Order = 50, RequireRestart = false, HintText = "Minimum troop count for a horde to raid a village at all.")]
	[SettingPropertyGroup("AI", GroupOrder = 4)]
	public int VillageRaidTroopThreshold
	{
		get => ZombieBehaviorConfig.VillageRaidTroopThreshold;
		set => ZombieBehaviorConfig.VillageRaidTroopThreshold = value;
	}

	[SettingPropertyInteger("Village Raid Troop Threshold (High Quality)", 1, 2000, Order = 51, RequireRestart = false, HintText = "Lower troop threshold used when at least half the horde is tier 3+ troops.")]
	[SettingPropertyGroup("AI", GroupOrder = 4)]
	public int VillageRaidTroopThresholdHighQuality
	{
		get => ZombieBehaviorConfig.VillageRaidTroopThresholdHighQuality;
		set => ZombieBehaviorConfig.VillageRaidTroopThresholdHighQuality = value;
	}

	[SettingPropertyInteger("Settlement Siege Troop Threshold", 1, 5000, Order = 52, RequireRestart = false, HintText = "Minimum troop count for a hero-led horde to besiege a town/castle at all. Meant to line up with Large Horde Threshold (General) - sieging is the 'large horde' behavior.")]
	[SettingPropertyGroup("AI", GroupOrder = 4)]
	public int SettlementSiegeTroopThreshold
	{
		get => ZombieBehaviorConfig.SettlementSiegeTroopThreshold;
		set => ZombieBehaviorConfig.SettlementSiegeTroopThreshold = value;
	}

	[SettingPropertyFloatingInteger("Siege Strength Multiplier", 0.5f, 10f, Order = 53, RequireRestart = false, HintText = "The horde must outnumber the settlement's garrison+militia by at least this factor to besiege it.")]
	[SettingPropertyGroup("AI", GroupOrder = 4)]
	public float SiegeStrengthMultiplier
	{
		get => ZombieBehaviorConfig.SiegeStrengthMultiplier;
		set => ZombieBehaviorConfig.SiegeStrengthMultiplier = value;
	}

	[SettingPropertyBool("Siege Disabled (Safety Toggle)", Order = 54, RequireRestart = false, HintText = "Emergency off-switch for the whole siege feature, independent of everything else.")]
	[SettingPropertyGroup("AI", GroupOrder = 4)]
	public bool SiegeDisabledPendingInvestigation
	{
		get => ZombieBehaviorConfig.SiegeDisabledPendingInvestigation;
		set
		{
			ZombieBehaviorConfig.SiegeDisabledPendingInvestigation = value;
			ZombieLog.Info("MCM: Siege Disabled (Safety Toggle) set to " + value);
		}
	}

	[SettingPropertyInteger("High Quality Tier Minimum", 1, 6, Order = 55, RequireRestart = false, HintText = "Troop tier considered 'high quality' for the high-quality raid threshold above.")]
	[SettingPropertyGroup("AI", GroupOrder = 4)]
	public int HighQualityTierMin
	{
		get => ZombieBehaviorConfig.HighQualityTierMin;
		set => ZombieBehaviorConfig.HighQualityTierMin = value;
	}

	[SettingPropertyFloatingInteger("High Quality Share Minimum", 0f, 1f, Order = 56, RequireRestart = false, HintText = "Fraction of the roster that must be high-quality tier for the horde to count as a 'high quality' horde.")]
	[SettingPropertyGroup("AI", GroupOrder = 4)]
	public float HighQualityShareMin
	{
		get => ZombieBehaviorConfig.HighQualityShareMin;
		set => ZombieBehaviorConfig.HighQualityShareMin = value;
	}

	[SettingPropertyFloatingInteger("Raid Block Min Threat Ratio", 0f, 2f, Order = 57, RequireRestart = false, HintText = "A nearby hostile party only blocks a raid if its troop count is at least this fraction of the horde's own.")]
	[SettingPropertyGroup("AI", GroupOrder = 4)]
	public float RaidBlockMinThreatRatio
	{
		get => ZombieBehaviorConfig.RaidBlockMinThreatRatio;
		set => ZombieBehaviorConfig.RaidBlockMinThreatRatio = value;
	}

	// --- AI: target commitment (anti flip-flopping) ---

	[SettingPropertyFloatingInteger("Commitment Bonus Multiplier", 1f, 5f, Order = 80, RequireRestart = false, HintText = "The horde's current target's score is multiplied by this before ranking against fresh candidates.")]
	[SettingPropertyGroup("AI", GroupOrder = 4)]
	public float CommitmentBonusMultiplier
	{
		get => ZombieBehaviorConfig.CommitmentBonusMultiplier;
		set => ZombieBehaviorConfig.CommitmentBonusMultiplier = value;
	}

	[SettingPropertyFloatingInteger("Commitment Required Score Increase", 1f, 5f, Order = 81, RequireRestart = false, HintText = "A challenger must also beat the current target's raw score by this margin to actually replace it.")]
	[SettingPropertyGroup("AI", GroupOrder = 4)]
	public float CommitmentRequiredScoreIncrease
	{
		get => ZombieBehaviorConfig.CommitmentRequiredScoreIncrease;
		set => ZombieBehaviorConfig.CommitmentRequiredScoreIncrease = value;
	}

	// --- AI: wandering ---

	[SettingPropertyFloatingInteger("Wander Radius Multiplier", 0.05f, 5f, Order = 100, RequireRestart = false, HintText = "How far a party looks for a fresh point to wander to, as a multiple of the normal spawn radius.")]
	[SettingPropertyGroup("AI", GroupOrder = 4)]
	public float WanderRadiusMultiplier
	{
		get => ZombieBehaviorConfig.WanderRadiusMultiplier;
		set => ZombieBehaviorConfig.WanderRadiusMultiplier = value;
	}

	[SettingPropertyFloatingInteger("Wander Reroll Interval (hours)", 0.1f, 24f, Order = 101, RequireRestart = false, HintText = "How often (in-game hours) a wandering party re-rolls its target point instead of continuing toward the last one.")]
	[SettingPropertyGroup("AI", GroupOrder = 4)]
	public float WanderRerollIntervalHours
	{
		get => ZombieBehaviorConfig.WanderRerollIntervalHours;
		set => ZombieBehaviorConfig.WanderRerollIntervalHours = value;
	}

	// --- AI: stuck-party watchdog ---

	[SettingPropertyFloatingInteger("Stuck Movement Threshold", 0.5f, 50f, Order = 130, RequireRestart = false, HintText = "If a party moves less than this many map units within an hourly tick, it counts as stationary.")]
	[SettingPropertyGroup("AI", GroupOrder = 4)]
	public float StuckMovementThreshold
	{
		get => ZombieBehaviorConfig.StuckMovementThreshold;
		set => ZombieBehaviorConfig.StuckMovementThreshold = value;
	}

	[SettingPropertyFloatingInteger("Stuck Threshold (hours)", 1f, 72f, Order = 131, RequireRestart = false, HintText = "After this many in-game hours of being stationary in a row, a party is force-escaped to a fresh point. Actively besieging parties are exempt.")]
	[SettingPropertyGroup("AI", GroupOrder = 4)]
	public float StuckThresholdHours
	{
		get => ZombieBehaviorConfig.StuckThresholdHours;
		set => ZombieBehaviorConfig.StuckThresholdHours = value;
	}

	[SettingPropertyFloatingInteger("Stuck Escape Distance Factor", 0.1f, 3f, Order = 132, RequireRestart = false, HintText = "How far the forced escape point is, relative to the normal spawn radius.")]
	[SettingPropertyGroup("AI", GroupOrder = 4)]
	public float StuckEscapeDistanceFactor
	{
		get => ZombieBehaviorConfig.StuckEscapeDistanceFactor;
		set => ZombieBehaviorConfig.StuckEscapeDistanceFactor = value;
	}

	// --- AI: frustrated raider ---

	[SettingPropertyInteger("Frustration Troop Threshold", 1, 5000, Order = 140, RequireRestart = false, HintText = "Minimum horde size for the 'frustrated raider' opportunistic-pillaging fallback to ever trigger.")]
	[SettingPropertyGroup("AI", GroupOrder = 4)]
	public int FrustrationTroopThreshold
	{
		get => ZombieBehaviorConfig.FrustrationTroopThreshold;
		set => ZombieBehaviorConfig.FrustrationTroopThreshold = value;
	}

	[SettingPropertyInteger("Frustration Abandon Threshold", 1, 100, Order = 141, RequireRestart = false, HintText = "Number of failed hunt chases (within the frustration window) needed before the horde gives up hunting and raids the nearest village instead.")]
	[SettingPropertyGroup("AI", GroupOrder = 4)]
	public int FrustrationAbandonThreshold
	{
		get => ZombieBehaviorConfig.FrustrationAbandonThreshold;
		set => ZombieBehaviorConfig.FrustrationAbandonThreshold = value;
	}

	[SettingPropertyFloatingInteger("Frustration Window (days)", 0.5f, 30f, Order = 142, RequireRestart = false, HintText = "Sliding window (in-game days) over which failed hunt chases are counted toward the abandon threshold above.")]
	[SettingPropertyGroup("AI", GroupOrder = 4)]
	public float FrustrationWindowDays
	{
		get => ZombieBehaviorConfig.FrustrationWindowDays;
		set => ZombieBehaviorConfig.FrustrationWindowDays = value;
	}

	[SettingPropertyFloatingInteger("Frustration Raid Cancel Radius", 1f, 100f, Order = 143, RequireRestart = false, HintText = "If a hostile party comes within this radius during an opportunistic raid, the horde cancels it and flees.")]
	[SettingPropertyGroup("AI", GroupOrder = 4)]
	public float FrustrationRaidCancelRadius
	{
		get => ZombieBehaviorConfig.FrustrationRaidCancelRadius;
		set => ZombieBehaviorConfig.FrustrationRaidCancelRadius = value;
	}

	// --- AI: greedy growth ---

	[SettingPropertyInteger("Greedy Growth Troop Threshold", 1, 5000, Order = 210, RequireRestart = false, HintText = "Troop count at which the horde stops being picky about risk/distance and just goes after whatever is available. Meant to line up with Medium Horde Threshold (General) - greedy mode is the 'medium horde' behavior.")]
	[SettingPropertyGroup("AI", GroupOrder = 4)]
	public int GreedyGrowthTroopThreshold
	{
		get => ZombieBehaviorConfig.GreedyGrowthTroopThreshold;
		set => ZombieBehaviorConfig.GreedyGrowthTroopThreshold = value;
	}

	[SettingPropertyFloatingInteger("Greedy Min Strength Ratio", 0.05f, 2f, Order = 211, RequireRestart = false, HintText = "Flat hunt strength-ratio floor while greedy (looser than the normal dynamic bracket).")]
	[SettingPropertyGroup("AI", GroupOrder = 4)]
	public float GreedyMinStrengthRatio
	{
		get => ZombieBehaviorConfig.GreedyMinStrengthRatio;
		set => ZombieBehaviorConfig.GreedyMinStrengthRatio = value;
	}

	[SettingPropertyInteger("Greedy Village Raid Troop Threshold", 1, 2000, Order = 212, RequireRestart = false, HintText = "Village raid troop threshold used once the horde is greedy - usually lower than the normal one.")]
	[SettingPropertyGroup("AI", GroupOrder = 4)]
	public int GreedyVillageRaidTroopThreshold
	{
		get => ZombieBehaviorConfig.GreedyVillageRaidTroopThreshold;
		set => ZombieBehaviorConfig.GreedyVillageRaidTroopThreshold = value;
	}

	[SettingPropertyFloatingInteger("Greedy Search Radius Multiplier", 0.5f, 5f, Order = 213, RequireRestart = false, HintText = "Hunting search radius multiplier while greedy - casts a wider net than the normal spawn radius.")]
	[SettingPropertyGroup("AI", GroupOrder = 4)]
	public float GreedySearchRadiusMultiplier
	{
		get => ZombieBehaviorConfig.GreedySearchRadiusMultiplier;
		set => ZombieBehaviorConfig.GreedySearchRadiusMultiplier = value;
	}

	[SettingPropertyFloatingInteger("Greedy Hunt Distance Exponent", 1f, 5f, Order = 214, RequireRestart = false, HintText = "Exponent applied to hunt distance while greedy - higher values make proximity dominate over target size.")]
	[SettingPropertyGroup("AI", GroupOrder = 4)]
	public float GreedyHuntDistanceExponent
	{
		get => ZombieBehaviorConfig.GreedyHuntDistanceExponent;
		set => ZombieBehaviorConfig.GreedyHuntDistanceExponent = value;
	}

	// --- AI: splitting ---

	[SettingPropertyInteger("Split Min Troops", 1, 10000, Order = 220, RequireRestart = false, HintText = "Below this troop count a horde never splits.")]
	[SettingPropertyGroup("AI", GroupOrder = 4)]
	public int SplitMinTroops
	{
		get => ZombieBehaviorConfig.SplitMinTroops;
		set => ZombieBehaviorConfig.SplitMinTroops = value;
	}

	[SettingPropertyInteger("Split Max Troops", 1, 10000, Order = 221, RequireRestart = false, HintText = "At or above this troop count a horde is guaranteed to split every day.")]
	[SettingPropertyGroup("AI", GroupOrder = 4)]
	public int SplitMaxTroops
	{
		get => ZombieBehaviorConfig.SplitMaxTroops;
		set => ZombieBehaviorConfig.SplitMaxTroops = value;
	}

	[SettingPropertyFloatingInteger("Split Fraction", 0.05f, 0.9f, Order = 222, RequireRestart = false, HintText = "Fraction of the horde's troops that leave with the newly split-off party.")]
	[SettingPropertyGroup("AI", GroupOrder = 4)]
	public float SplitFraction
	{
		get => ZombieBehaviorConfig.SplitFraction;
		set => ZombieBehaviorConfig.SplitFraction = value;
	}

	[SettingPropertyFloatingInteger("Split Away Distance Factor", 0.1f, 3f, Order = 223, RequireRestart = false, HintText = "How far the split-off party gets redirected away from its parent, relative to the spawn radius.")]
	[SettingPropertyGroup("AI", GroupOrder = 4)]
	public float SplitAwayDistanceFactor
	{
		get => ZombieBehaviorConfig.SplitAwayDistanceFactor;
		set => ZombieBehaviorConfig.SplitAwayDistanceFactor = value;
	}

	[SettingPropertyFloatingInteger("Split Spawn Max Distance Factor", 0.01f, 1f, Order = 224, RequireRestart = false, HintText = "Upper bound for where the split-off party initially spawns relative to its parent.")]
	[SettingPropertyGroup("AI", GroupOrder = 4)]
	public float SplitSpawnMaxDistanceFactor
	{
		get => ZombieBehaviorConfig.SplitSpawnMaxDistanceFactor;
		set => ZombieBehaviorConfig.SplitSpawnMaxDistanceFactor = value;
	}

	[SettingPropertyFloatingInteger("Split Spawn Min Distance Factor", 0.01f, 1f, Order = 225, RequireRestart = false, HintText = "Lower bound for where the split-off party initially spawns relative to its parent.")]
	[SettingPropertyGroup("AI", GroupOrder = 4)]
	public float SplitSpawnMinDistanceFactor
	{
		get => ZombieBehaviorConfig.SplitSpawnMinDistanceFactor;
		set => ZombieBehaviorConfig.SplitSpawnMinDistanceFactor = value;
	}

	// --- AI: respawn after total defeat ---

	[SettingPropertyFloatingInteger("Respawn Max Distance Factor", 0.01f, 3f, Order = 230, RequireRestart = false, HintText = "Upper bound for how far a replacement horde spawns from where the old one died.")]
	[SettingPropertyGroup("AI", GroupOrder = 4)]
	public float RespawnMaxDistanceFactor
	{
		get => ZombieBehaviorConfig.RespawnMaxDistanceFactor;
		set => ZombieBehaviorConfig.RespawnMaxDistanceFactor = value;
	}

	[SettingPropertyFloatingInteger("Respawn Min Distance Factor", 0.01f, 3f, Order = 231, RequireRestart = false, HintText = "Lower bound for how far a replacement horde spawns from where the old one died.")]
	[SettingPropertyGroup("AI", GroupOrder = 4)]
	public float RespawnMinDistanceFactor
	{
		get => ZombieBehaviorConfig.RespawnMinDistanceFactor;
		set => ZombieBehaviorConfig.RespawnMinDistanceFactor = value;
	}

	// --- Debug ---

	[SettingPropertyFloatingInteger("Farsight Seeing Range", 10f, 1000f, Order = 0, RequireRestart = false, HintText = "Map spotting range applied to the player's own party while zombie.toggle_farsight is enabled.")]
	[SettingPropertyGroup("Debug", GroupOrder = 5)]
	public float FarsightSeeingRange
	{
		get => ZombieBehaviorConfig.FarsightSeeingRange;
		set => ZombieBehaviorConfig.FarsightSeeingRange = value;
	}

	[SettingPropertyFloatingInteger("Debug Tick Interval (seconds)", 1f, 120f, Order = 1, RequireRestart = false, HintText = "How often (real-time seconds) the on-screen debug status dump prints - just log spam pacing, not a balance knob.")]
	[SettingPropertyGroup("Debug", GroupOrder = 5)]
	public float DebugTickIntervalSeconds
	{
		get => ZombieBehaviorConfig.DebugTickIntervalSeconds;
		set => ZombieBehaviorConfig.DebugTickIntervalSeconds = value;
	}

	[SettingPropertyFloatingInteger("Ambient Sound Interval (seconds)", 1f, 120f, Order = 2, RequireRestart = false, HintText = "How often (real-time seconds) a random zombie growl/moan plays during a battle the horde is in.")]
	[SettingPropertyGroup("Debug", GroupOrder = 5)]
	public float AmbientSoundIntervalSeconds
	{
		get => ZombieBehaviorConfig.AmbientSoundIntervalSeconds;
		set => ZombieBehaviorConfig.AmbientSoundIntervalSeconds = value;
	}

	[SettingPropertyBool("Debug Chat Messages", Order = 3, RequireRestart = false, HintText = "Also show warnings/errors from the mod's internal log as in-game chat messages, not just in the log file. Useful for spotting a caught exception without opening the log.")]
	[SettingPropertyGroup("Debug", GroupOrder = 5)]
	public bool DebugChatMessagesEnabled
	{
		get => ZombieBehaviorConfig.DebugChatMessagesEnabled;
		set => ZombieBehaviorConfig.DebugChatMessagesEnabled = value;
	}
}
