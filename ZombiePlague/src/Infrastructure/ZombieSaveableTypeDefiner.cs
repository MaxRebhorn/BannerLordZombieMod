using TaleWorlds.SaveSystem;

namespace ZombiePlague.Infrastructure;

/// <summary>
/// Registers ZombiePartyComponent's new saveable class/field with the save
/// system - required for any new persisted type a mod adds (see e.g.
/// TaleWorlds' own NavalDLC.SaveableNavalDLCTypeDefiner, which does the exact
/// same thing for its FishingPartyComponent). Auto-discovered by the engine at
/// startup the same way CampaignBehaviorBase/MBSubModuleBase subclasses are -
/// no explicit registration call needed elsewhere in the mod.
/// </summary>
public class ZombieSaveableTypeDefiner : SaveableTypeDefiner
{
	// Arbitrary - just needs to not collide with other mods' definer base ids.
	// TaleWorlds' own NavalDLC uses 520000; picked well clear of that.
	private const int BaseId = 913270;

	public ZombieSaveableTypeDefiner()
		: base(BaseId)
	{
	}

	protected override void DefineClassTypes()
	{
		AddClassDefinition(typeof(ZombiePartyComponent), 1);
	}
}
