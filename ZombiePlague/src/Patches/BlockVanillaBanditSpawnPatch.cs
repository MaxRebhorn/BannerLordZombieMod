using System;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using ZombiePlague.Infrastructure;

namespace ZombiePlague.Patches;

/// <summary>
/// The zombie clan is flagged as a bandit faction, which is the right archetype
/// (heroless, owns parties, hostile to all) but also puts it into vanilla's bandit
/// spawner. That spawner's looter budget is derived from the number of infested
/// hideouts in the whole world, not just the clan's own, so it happily spawned
/// zombie parties across the map every night.
///
/// These prefixes block the two methods that actually create a party, so every
/// caller path is covered - the nightly tick, the new-game seeding and the hideout
/// refills alike. The mod spawns its own hordes and wants full control over that.
/// </summary>
[HarmonyPatch(typeof(BanditSpawnCampaignBehavior), "SpawnLooterParty")]
internal static class BlockVanillaLooterSpawnPatch
{
	private static bool Prefix(Clan selectedFaction)
	{
		try
		{
			return !ZombieClanUtil.IsZombieClan(selectedFaction);
		}
		catch (Exception ex)
		{
			ZombieLog.Error("BlockVanillaLooterSpawnPatch.Prefix failed", ex);
			return true;
		}
	}
}

[HarmonyPatch(typeof(BanditSpawnCampaignBehavior), "SpawnBanditParty")]
internal static class BlockVanillaBanditSpawnPatch
{
	private static bool Prefix(Clan selectedFaction)
	{
		try
		{
			return !ZombieClanUtil.IsZombieClan(selectedFaction);
		}
		catch (Exception ex)
		{
			ZombieLog.Error("BlockVanillaBanditSpawnPatch.Prefix failed", ex);
			return true;
		}
	}
}
