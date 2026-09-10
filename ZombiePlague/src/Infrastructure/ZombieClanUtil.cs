using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace ZombiePlague.Infrastructure;

internal static class ZombieClanUtil
{
	/// <summary>
	/// The clan is defined in ModuleData/zombie_clans.xml and deserialized by the
	/// game like any vanilla faction, so it arrives fully initialized. It is
	/// deliberately never created at runtime: a Clan.CreateClan() clan is missing
	/// the initialization Clan.Deserialize does (banner colors, minor faction
	/// template list, tier, renown), which crashed the native party visual.
	/// </summary>
	public static Clan GetZombieClan()
	{
		Clan clan = Clan.All.FirstOrDefault(c => c.StringId == ZombieIds.ClanId);
		if (clan == null)
		{
			ZombieLog.Error("Zombie clan '" + ZombieIds.ClanId + "' not found - is zombie_clans.xml registered in SubModule.xml?");
		}

		return clan;
	}

	public static bool IsZombieClan(Clan clan)
	{
		return clan != null && clan.StringId == ZombieIds.ClanId;
	}

	public static bool IsZombieParty(MobileParty party)
	{
		return party != null && party.ActualClan != null && party.ActualClan.StringId == ZombieIds.ClanId;
	}

	/// <summary>
	/// Mission-side equivalent of IsZombieParty: an agent's owning party in a live
	/// battle is reached via Origin.BattleCombatant, which for a normal troop
	/// spawned from a MapEvent is the PartyBase it was recruited into.
	/// </summary>
	public static bool IsZombieAgent(Agent agent)
	{
		return IsZombieParty((agent?.Origin?.BattleCombatant as PartyBase)?.MobileParty);
	}

	/// <summary>
	/// Every regular zombie troop CharacterObject (zombie_tier_N, the
	/// per-original zombie_&lt;id&gt; variants, the debug troops) is registered
	/// with the ZombieIds.TroopIdPrefix prefix - see ZombieTroopSetup/ZombieIds.
	/// Converted heroes keep their original StringId and are handled through a
	/// separate code path (Hero.CanBecomePrisoner), so this deliberately does
	/// not need to cover them.
	/// </summary>
	public static bool IsZombieTroop(CharacterObject troop)
	{
		return troop != null && !troop.IsHero && troop.StringId != null && troop.StringId.StartsWith(ZombieIds.TroopIdPrefix);
	}
}
