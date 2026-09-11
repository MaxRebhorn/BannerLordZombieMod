using System;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.SaveSystem;

namespace ZombiePlague.Infrastructure;

/// <summary>
/// A BanditPartyComponent that additionally supports a real Hero leader - for
/// a converted hero leading a zombie horde.
///
/// Deliberately a subclass rather than switching a converted party to
/// LordPartyComponent: MobileParty.IsBandit is computed live as
/// `_partyComponent is BanditPartyComponent` every time the component
/// changes (confirmed in decompiled MobileParty.cs), so swapping to
/// LordPartyComponent would silently flip that flag false and pull in every
/// vanilla system gated on IsBandit itself (wage exemptions, diplomacy,
/// encyclopedia visibility, bandit density tracking, ...) - none of which
/// this mod has audited. A subclass still satisfies `is BanditPartyComponent`,
/// so IsBandit (and everything that depends on it) stays exactly as it
/// already is for every other zombie party; only Leader/PartyOwner change.
///
/// This is a new saveable class with a new saveable field, so it needs its
/// own SaveableTypeDefiner registration - see ZombieSaveableTypeDefiner.cs.
/// This is the same pattern TaleWorlds' own NavalDLC module uses for its
/// FishingPartyComponent (also a PartyComponent subclass with new fields,
/// registered via SaveableNavalDLCTypeDefiner) - precedented, not a hack.
/// </summary>
internal sealed class ZombiePartyComponent : BanditPartyComponent
{
	[SaveableField(1)]
	private Hero _leader;

	public override Hero Leader => _leader;

	public override Hero PartyOwner => _leader ?? base.PartyOwner;

	private ZombiePartyComponent(Settlement relatedSettlement)
		: base(relatedSettlement, null)
	{
	}

	/// <summary>
	/// Swaps an already-existing zombie party (created the normal way via
	/// ZombieSpawner, so already a BanditPartyComponent) onto this component
	/// and assigns `leader` as both Leader and PartyOwner. Passing `args: null`
	/// to the base constructor means the base class's OnMobilePartySetOnCreation
	/// is a no-op for this swap - the party's roster, position and clan are
	/// untouched, only the component (and therefore Leader) changes.
	/// </summary>
	public static void ConvertPartyToZombieLeaderParty(MobileParty party, Hero leader)
	{
		try
		{
			ZombiePartyComponent component = new(party.HomeSettlement);
			party.SetPartyComponent(component);
			component._leader = leader;
		}
		catch (Exception ex)
		{
			ZombieLog.Error("ConvertPartyToZombieLeaderParty failed for " + party?.StringId, ex);
		}
	}

	/// <summary>
	/// TODO.md Phase 3 crash workaround: a hero-led ZombiePartyComponent
	/// (BanditPartyComponent-derived) still crashes (0xC0000005, confirmed via
	/// crash dump) the instant it's ordered to besiege a settlement - something
	/// vanilla's siege pipeline expects from a "real" lord party is still
	/// missing even with a genuine LeaderHero present, and the exact missing
	/// piece could not be pinned down without symbol-resolution tooling.
	///
	/// Workaround: swap to an actual LordPartyComponent only for the duration
	/// of the siege, then swap back to ZombiePartyComponent (restoring
	/// IsBandit and everything gated on it) once the party stops besieging.
	/// `owner` and `partyLeader` are both `leader` - a lord party's Owner and
	/// Leader are normally the same hero for a single-hero party like this one.
	/// </summary>
	public static void SwapToLordForSiege(MobileParty party, Hero leader)
	{
		try
		{
			if (party.PartyComponent is LordPartyComponent)
			{
				return;
			}

			LordPartyComponent.ConvertPartyToLordParty(party, leader, leader);
		}
		catch (Exception ex)
		{
			ZombieLog.Error("SwapToLordForSiege failed for " + party?.StringId, ex);
		}
	}

	/// <summary>Reverses SwapToLordForSiege once the party is no longer besieging - see that method's doc comment.</summary>
	public static void SwapBackFromSiege(MobileParty party, Hero leader)
	{
		try
		{
			if (party.PartyComponent is ZombiePartyComponent)
			{
				return;
			}

			ConvertPartyToZombieLeaderParty(party, leader);
		}
		catch (Exception ex)
		{
			ZombieLog.Error("SwapBackFromSiege failed for " + party?.StringId, ex);
		}
	}
}
