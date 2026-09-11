using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Settlements;
using ZombiePlague.Behaviors;
using ZombiePlague.Infrastructure;

namespace ZombiePlague.Patches;

[HarmonyPatch(typeof(DefaultVolunteerModel), nameof(DefaultVolunteerModel.GetDailyVolunteerProductionProbability))]
internal static class ZombieRecruitBlockPatch
{
	[HarmonyPrefix]
	private static bool Prefix(Hero hero, int index, Settlement settlement, ref float __result)
	{
		if (!ZombieBehaviorConfig.BlockRecruitmentEnabled || !(ZombieVillageRaidBehavior.Instance?.IsVillageDebuffed(settlement) ?? false))
		{
			return true;
		}

		__result = 0f;
		return false;
	}
}
