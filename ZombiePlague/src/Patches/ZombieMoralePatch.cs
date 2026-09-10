using HarmonyLib;
using SandBox.GameComponents;
using TaleWorlds.MountAndBlade;
using ZombiePlague.Infrastructure;

namespace ZombiePlague.Patches;

/// <summary>
/// "Zombies don't get scared": patches the singleplayer-campaign battle morale
/// model (SandboxBattleMoraleModel, the one CampaignGameStarter registers) so a
/// zombie agent can never trigger the individual panic/flee reaction
/// (CommonAIComponent gates that entirely behind CanPanicDueToMorale), and
/// reports full morale to anything else that reads it.
/// </summary>
[HarmonyPatch(typeof(SandboxBattleMoraleModel))]
internal static class ZombieMoralePatch
{
	[HarmonyPatch(nameof(SandboxBattleMoraleModel.CanPanicDueToMorale))]
	[HarmonyPrefix]
	private static bool CanPanicDueToMoralePrefix(Agent agent, ref bool __result)
	{
		if (!ZombieBehaviorConfig.ZombiePanicImmune || !ZombieClanUtil.IsZombieAgent(agent))
		{
			return true;
		}

		__result = false;
		return false;
	}

	[HarmonyPatch(nameof(SandboxBattleMoraleModel.GetEffectiveInitialMorale))]
	[HarmonyPostfix]
	private static void GetEffectiveInitialMoralePostfix(Agent agent, ref float __result)
	{
		if (ZombieClanUtil.IsZombieAgent(agent))
		{
			__result = ZombieBehaviorConfig.ZombieInitialMorale;
		}
	}
}
