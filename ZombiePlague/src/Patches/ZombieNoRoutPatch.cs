using System;
using System.Linq;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.MapEvents;
using ZombiePlague.Infrastructure;

namespace ZombiePlague.Patches;

/// <summary>
/// Per user request: zombie hordes should fight to the death or wounded,
/// never flee and never end up as prisoners. Two separate vanilla paths
/// needed patching for that, not one:
///
///  - MapEventSide.Route() is called unconditionally on the losing side
///    whenever that side's leader party is mobile and its morale collapses
///    to 0 (MapEvent.CalculateWinner) - it marks every currently-active
///    troop on that side as routed (RoutedInBattle - they flee and survive),
///    with no faction/trait check at all.
///
///  - Skipping Route() alone was not enough: MapEvent.CaptureDefeatedPartyMembers
///    only skips its capture logic when RetreatingSide != None (i.e. a side
///    actually routed). With routing skipped, a defeated zombie side falls
///    through to the normal capture path instead, which - since
///    DefaultBattleRewardModel.CanTroopBeTakenPrisoner always returns true
///    for every CharacterObject - would divert some defeated zombies into
///    the winner's prisoner roster. Forcing that false for zombie troops
///    means none of them get diverted there; the same method's unconditional
///    roster subtraction at the end still removes them from the losing
///    party's roster either way, so the only remaining fate is death.
///
/// The battle's win/loss outcome itself (MapEvent.BattleState) is decided
/// before either of these runs, so neither patch changes who wins - only
/// what happens to the losing zombies afterward.
/// </summary>
[HarmonyPatch]
internal static class ZombieNoRoutPatch
{
	[HarmonyPatch(typeof(MapEventSide), "Route")]
	[HarmonyPrefix]
	private static bool RoutePrefix(MapEventSide __instance)
	{
		try
		{
			bool isZombieSide = __instance.Parties.Any(party => party.Party.IsMobile && ZombieClanUtil.IsZombieParty(party.Party.MobileParty));
			if (!isZombieSide)
			{
				return true;
			}

			ZombieLog.Info("ZombieNoRoutPatch: skipped Route() for a zombie-clan side - zombies do not flee.");
			return false;
		}
		catch (Exception ex)
		{
			ZombieLog.Error("ZombieNoRoutPatch.RoutePrefix failed", ex);
			return true;
		}
	}

	[HarmonyPatch(typeof(DefaultBattleRewardModel), nameof(DefaultBattleRewardModel.CanTroopBeTakenPrisoner))]
	[HarmonyPrefix]
	private static bool CanTroopBeTakenPrisonerPrefix(CharacterObject troop, ref bool __result)
	{
		try
		{
			if (!ZombieClanUtil.IsZombieTroop(troop))
			{
				return true;
			}

			__result = false;
			return false;
		}
		catch (Exception ex)
		{
			ZombieLog.Error("ZombieNoRoutPatch.CanTroopBeTakenPrisonerPrefix failed", ex);
			return true;
		}
	}
}
