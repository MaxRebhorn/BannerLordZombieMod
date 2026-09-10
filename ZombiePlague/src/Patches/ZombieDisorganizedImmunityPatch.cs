using HarmonyLib;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Party;
using ZombiePlague.Infrastructure;

namespace ZombiePlague.Patches;

/// <summary>
/// "Zombies don't get disorganized": patches the single gate
/// (DisorganizedStateCampaignBehavior checks this before setting the flag from
/// every trigger - breaking a raid/siege, being routed, etc.) rather than
/// mimicking the Foragers/Swift Regroup perks' partial duration reduction,
/// since neither of those (nor both stacked) ever reaches zero. A horde with
/// no real formation to break has nothing to reorganize in the first place.
/// </summary>
[HarmonyPatch(typeof(DefaultPartyImpairmentModel), nameof(DefaultPartyImpairmentModel.CanGetDisorganized))]
internal static class ZombieDisorganizedImmunityPatch
{
	private static bool Prefix(PartyBase party, ref bool __result)
	{
		if (!ZombieBehaviorConfig.ZombieDisorganizedImmune || !ZombieClanUtil.IsZombieParty(party?.MobileParty))
		{
			return true;
		}

		__result = false;
		return false;
	}
}
