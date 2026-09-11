using System;
using HarmonyLib;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Party;
using ZombiePlague.Infrastructure;

namespace ZombiePlague.Patches;

/// <summary>
/// "Zombies don't eat or need pay": zeroes out the two daily party-morale
/// penalties that only make sense for living, salaried troops. Patches
/// DefaultPartyMoraleModel directly (the model CampaignGameStarter registers)
/// rather than the abstract PartyMoraleModel, same reasoning as
/// ZombieMoralePatch targeting SandboxBattleMoraleModel.
/// </summary>
[HarmonyPatch(typeof(DefaultPartyMoraleModel))]
internal static class ZombieUpkeepImmunityPatch
{
	[HarmonyPatch(nameof(DefaultPartyMoraleModel.GetDailyStarvationMoralePenalty))]
	[HarmonyPrefix]
	private static bool GetDailyStarvationMoralePenaltyPrefix(PartyBase party, ref int __result)
	{
		try
		{
			if (!ZombieBehaviorConfig.ZombieHungerWageMoraleImmune || !ZombieClanUtil.IsZombieParty(party?.MobileParty))
			{
				return true;
			}

			__result = 0;
			return false;
		}
		catch (Exception ex)
		{
			ZombieLog.Error("ZombieUpkeepImmunityPatch.GetDailyStarvationMoralePenaltyPrefix failed", ex);
			return true;
		}
	}

	[HarmonyPatch(nameof(DefaultPartyMoraleModel.GetDailyNoWageMoralePenalty))]
	[HarmonyPrefix]
	private static bool GetDailyNoWageMoralePenaltyPrefix(MobileParty party, ref int __result)
	{
		try
		{
			if (!ZombieBehaviorConfig.ZombieHungerWageMoraleImmune || !ZombieClanUtil.IsZombieParty(party))
			{
				return true;
			}

			__result = 0;
			return false;
		}
		catch (Exception ex)
		{
			ZombieLog.Error("ZombieUpkeepImmunityPatch.GetDailyNoWageMoralePenaltyPrefix failed", ex);
			return true;
		}
	}
}
