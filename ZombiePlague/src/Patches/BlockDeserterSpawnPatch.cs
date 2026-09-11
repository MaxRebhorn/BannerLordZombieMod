using System;
using HarmonyLib;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using ZombiePlague.Infrastructure;

namespace ZombiePlague.Patches;

/// <summary>
/// Vanilla builds its deserter pool from exactly the two rosters the horde feeds
/// on - RoutedInBattle plus DiedInBattle of the losing side - and turns them into
/// its own parties with a 90% chance from 15 men up. Left alone, every man the
/// zombies took would walk the map twice.
///
/// Scoped to battles a zombie party was actually part of; every other battle keeps
/// its deserters.
/// </summary>
[HarmonyPatch(typeof(DesertersCampaignBehavior), "MapEventEnded")]
internal static class BlockDeserterSpawnPatch
{
	private static bool Prefix(MapEvent mapEvent)
	{
		try
		{
			return !InvolvesZombies(mapEvent);
		}
		catch (Exception ex)
		{
			ZombieLog.Error("BlockDeserterSpawnPatch.Prefix failed", ex);
			return true;
		}
	}

	private static bool InvolvesZombies(MapEvent mapEvent)
	{
		if (mapEvent == null)
		{
			return false;
		}

		foreach (BattleSideEnum side in new[] { BattleSideEnum.Attacker, BattleSideEnum.Defender })
		{
			foreach (MapEventParty party in mapEvent.PartiesOnSide(side))
			{
				MobileParty mobileParty = party.Party?.MobileParty;
				if (ZombieClanUtil.IsZombieParty(mobileParty))
				{
					return true;
				}
			}
		}

		return false;
	}
}
