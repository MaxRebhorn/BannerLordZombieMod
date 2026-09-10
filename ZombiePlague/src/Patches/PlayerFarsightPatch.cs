using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Party;
using ZombiePlague.Infrastructure;

namespace ZombiePlague.Patches;

/// <summary>
/// Debug/testing QoL: while ZombieIds.FarsightEnabled is on (zombie.toggle_farsight),
/// the player's own map spotting range is replaced outright with
/// ZombieBehaviorConfig.FarsightSeeingRange - a fresh ExplainedNumber rather
/// than adjusting the computed one, since the original method already applies
/// its own hard cap (MaximumSeeingRange, normally 60) via LimitMax before
/// returning, and a fresh instance has no such limit set.
/// </summary>
[HarmonyPatch(typeof(DefaultMapVisibilityModel), nameof(DefaultMapVisibilityModel.GetPartySpottingRange))]
internal static class PlayerFarsightPatch
{
	private static void Postfix(MobileParty party, bool includeDescriptions, ref ExplainedNumber __result)
	{
		if (!ZombieIds.FarsightEnabled || party != MobileParty.MainParty)
		{
			return;
		}

		__result = new ExplainedNumber(ZombieBehaviorConfig.FarsightSeeingRange, includeDescriptions);
	}
}
