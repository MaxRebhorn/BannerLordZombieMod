using System;
using System.Collections.Generic;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Siege;
using ZombiePlague.Infrastructure;

namespace ZombiePlague.Patches;

/// <summary>
/// Crash report 2026-09-09 16:07: the game died the instant a zombie party's
/// siege ended - zombieplague.log's last lines are
/// MobileParty.set_BesiegerCamp firing twice in a row (ENTER/RETURNED) for
/// "zombie_plague_party" with newValue=NULL, then nothing, then the crash
/// dump. No managed exception was ever logged by our own try/catch blocks
/// (OnHourlyTick's included), which means whatever threw did so somewhere
/// past that setter, inside vanilla code we don't wrap - most likely the
/// native/scene cleanup that expects the leaving party's Position to already
/// be a valid point outside the settlement, the way a normal lord's orderly
/// siege-lift leaves it. Our AI has SetDoNotMakeNewDecisions(true) permanently
/// on (see ZombieSpawner.InitializeZombieParty), so nothing ever runs that
/// step for us - the party's Position can still be sitting wherever the siege
/// camp scene last put it.
///
/// Mirrors the existing SiegeCampPositionGuardPatch fix (same crash family,
/// opposite end of the siege): force the party to a known-safe point - the
/// settlement's own gate, exactly what every spawn/relocate elsewhere in this
/// mod already uses - the moment BesiegerCamp clears, instead of trusting
/// whatever position it was left at.
/// </summary>
[HarmonyPatch(typeof(MobileParty), "set_BesiegerCamp")]
internal static class SiegeEndPositionGuardPatch
{
	// Bridges prefix -> postfix: BesiegedSettlement reads back NULL by the time
	// the postfix runs, so the settlement being left has to be captured before
	// the setter actually clears it.
	private static readonly Dictionary<MobileParty, Settlement> PendingLeftSettlement = new();

	[HarmonyPrefix]
	private static void Prefix(MobileParty __instance, BesiegerCamp value)
	{
		try
		{
			if (value != null || !ZombieClanUtil.IsZombieParty(__instance))
			{
				return;
			}

			Settlement settlement = __instance.BesiegedSettlement;
			if (settlement != null)
			{
				PendingLeftSettlement[__instance] = settlement;
			}
		}
		catch (Exception ex)
		{
			ZombieLog.Error("SiegeEndPositionGuardPatch.Prefix failed", ex);
		}
	}

	[HarmonyPostfix]
	private static void Postfix(MobileParty __instance)
	{
		if (!PendingLeftSettlement.TryGetValue(__instance, out Settlement settlement))
		{
			return;
		}

		// Removed up front, before the risky work below - so a failure here
		// never leaves a stale entry (leak) for this party.
		PendingLeftSettlement.Remove(__instance);

		try
		{
			ZombieLog.Info("SiegeEndPositionGuard: " + __instance.StringId + " left the siege of " + settlement.StringId
				+ " - forcing position to GatePosition before native siege-camp cleanup continues.");
			__instance.Position = settlement.GatePosition;
		}
		catch (Exception ex)
		{
			ZombieLog.Error("SiegeEndPositionGuardPatch.Postfix failed for " + __instance.StringId, ex);
		}
	}
}
