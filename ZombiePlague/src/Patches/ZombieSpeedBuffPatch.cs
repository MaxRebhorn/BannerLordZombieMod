using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Localization;
using ZombiePlague.Infrastructure;

namespace ZombiePlague.Patches;

/// <summary>
/// All of DefaultPartySpeedCalculatingModel's zombie-specific handling:
/// TODO.md Feature 1's speed buffs (young horde, chasing, general), plus three
/// "race trait" exemptions - no night penalty, no wounded-troop penalty, and
/// (the big one in practice) no over-party-size penalty, since a heroless
/// tier-4 bandit clan's PartySizeLimit (~80) is nowhere near what the snowball
/// mechanic grows hordes to - without this a few-hundred-troop horde's speed
/// collapses to the game's global floor (1) and it can never catch anything or
/// reach a village. MobileParty.Speed has no settable field of its own - it is
/// recomputed every tick from this model - so everything here works the same
/// way vanilla perks/culture bonuses do, via Harmony patches on the model's
/// methods.
/// </summary>
[HarmonyPatch(typeof(DefaultPartySpeedCalculatingModel))]
internal static class ZombieSpeedBuffPatch
{
	private static readonly TextObject YoungHordeBuffText = new("{=zombieplague_young_horde_speed}Young horde");
	private static readonly TextObject HuntingBuffText = new("{=zombieplague_hunting_speed}Chasing prey");
	private static readonly TextObject GeneralBuffText = new("{=zombieplague_general_speed}Zombie horde");
	private static readonly TextObject NightImmunityText = new("{=zombieplague_night_speed}Doesn't need to see");
	private static readonly TextObject RaidApproachBuffText = new("{=zombieplague_raid_speed}Smells blood");

	[HarmonyPatch(nameof(DefaultPartySpeedCalculatingModel.CalculateFinalSpeed))]
	[HarmonyPostfix]
	private static void CalculateFinalSpeedPostfix(MobileParty mobileParty, ref ExplainedNumber __result)
	{
		if (!ZombieClanUtil.IsZombieParty(mobileParty))
		{
			return;
		}

		__result.Add(ZombieBehaviorConfig.GeneralSpeedBonus, GeneralBuffText);

		if (mobileParty.MemberRoster.TotalManCount < ZombieBehaviorConfig.YoungHordeMaxTroops)
		{
			__result.Add(ZombieBehaviorConfig.YoungHordeSpeedBonus, YoungHordeBuffText);
		}

		if (mobileParty.DefaultBehavior == AiBehavior.EngageParty && mobileParty.TargetParty != null)
		{
			__result.Add(ZombieBehaviorConfig.HuntingSpeedBonus, HuntingBuffText);
		}

		if (mobileParty.DefaultBehavior == AiBehavior.RaidSettlement)
		{
			// A genuine multiplier (not a flat add): +200% factor triples speed
			// regardless of what the base value already is.
			__result.AddFactor(ZombieBehaviorConfig.RaidApproachSpeedMultiplier - 1f, RaidApproachBuffText);
		}

		// Vanilla applies a flat -0.25 factor for night inside CalculateFinalSpeed
		// itself (Campaign.Current.IsNight check, MovingAtNightEffect constant) -
		// by the time this postfix runs it is already baked into __result's
		// SumOfFactors, so adding the exact same magnitude back cancels it
		// precisely regardless of what other factors are present.
		if (ZombieBehaviorConfig.ZombieNightImmune && Campaign.Current.IsNight)
		{
			__result.AddFactor(0.25f, NightImmunityText);
		}
	}

	/// <summary>
	/// GetWoundedModifier is the private helper CalculateBaseSpeed uses to turn
	/// a party's wounded-troop share into a negative speed factor - patched
	/// directly (rather than counter-adding in a postfix like the night penalty
	/// above) since its exact formula isn't a fixed constant.
	/// </summary>
	[HarmonyPatch("GetWoundedModifier")]
	[HarmonyPrefix]
	private static bool GetWoundedModifierPrefix(MobileParty party, ref float __result)
	{
		if (!ZombieBehaviorConfig.ZombieWoundedSpeedImmune || !ZombieClanUtil.IsZombieParty(party))
		{
			return true;
		}

		__result = 0f;
		return false;
	}

	private static readonly TextObject PartySizeImmunityText = new("{=zombieplague_partysize_speed}Doesn't need order or formation");

	/// <summary>
	/// The over-party-size penalty (GetOverPartySizeEffect, applied inside the
	/// private CalculateLandBaseSpeed) has no party parameter of its own to
	/// gate on directly, so instead of patching that helper this recomputes the
	/// exact same formula here and cancels it - same counter-add technique as
	/// the night penalty above, just computed rather than a fixed constant.
	/// </summary>
	[HarmonyPatch(nameof(DefaultPartySpeedCalculatingModel.CalculateBaseSpeed))]
	[HarmonyPostfix]
	private static void CalculateBaseSpeedPostfix(
		MobileParty mobileParty,
		int additionalTroopOnFootCount,
		int additionalTroopOnHorseCount,
		ref ExplainedNumber __result)
	{
		if (!ZombieBehaviorConfig.ZombiePartySizeSpeedImmune || !ZombieClanUtil.IsZombieParty(mobileParty))
		{
			return;
		}

		int totalMenCount = mobileParty.MemberRoster.TotalManCount + additionalTroopOnFootCount + additionalTroopOnHorseCount;
		int partySizeLimit = mobileParty.Party.PartySizeLimit;
		foreach (MobileParty attachedParty in mobileParty.AttachedParties)
		{
			totalMenCount += attachedParty.MemberRoster.TotalManCount;
			partySizeLimit += attachedParty.Party.PartySizeLimit;
		}

		if (totalMenCount <= partySizeLimit || partySizeLimit <= 0)
		{
			return;
		}

		float overPartySizeEffect = 1f / ((float)totalMenCount / partySizeLimit) - 1f;
		__result.AddFactor(-overPartySizeEffect, PartySizeImmunityText);
	}
}
