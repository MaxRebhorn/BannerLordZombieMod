using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;

namespace ZombiePlague.Infrastructure;

/// <summary>
/// Two adjustments to the zombie troop catalogue that are deliberately made in
/// code instead of in the generated XML:
///
/// * <b>Race</b> - writing race="zombie" into the troop XML would throw during load
///   if the race did not register (FaceGen.GetRaceOrDefault is a plain dictionary
///   lookup despite the name), and a broken campaign start is a bad way to find out.
///   Assigning it here means a missing race costs the green skin and nothing else.
/// * <b>Ranged weapons</b> - the generator cannot tell a bow from a sword, because
///   crafted weapons carry no type in the XML at all. Here the game's own item data
///   is loaded and IsRangedWeapon/IsAmmo answer the question outright.
///
/// Mounts are left alone on purpose: zombies ride, they just do not shoot.
/// </summary>
internal static class ZombieTroopSetup
{
	public const string RaceId = "zombie";

	// The bisection-ladder troops are controls: zombie_debug_clone is meant to stay
	// a byte-for-byte vanilla looter, so touching it would defeat its purpose.
	private const string DebugTroopPrefix = "zombie_debug_";

	public static void Apply()
	{
		List<CharacterObject> troops = CharacterObject.All
			.Where(c => c != null && c.StringId != null
				&& c.StringId.StartsWith(ZombieIds.TroopIdPrefix)
				&& !c.StringId.StartsWith(DebugTroopPrefix))
			.ToList();

		if (troops.Count == 0)
		{
			ZombieLog.Error("ZombieTroopSetup: no zombie troops found - did zombie_troops_generated.xml load?");
			return;
		}

		VerifyFaceTemplates(troops);
		ApplyRace(troops);
		int strippedSlots = StripRangedWeapons(troops);

		ZombieLog.Info("SUCCESS troop setup: " + troops.Count + " zombie troops, "
			+ strippedSlots + " ranged/ammo slots cleared");
	}

	/// <summary>
	/// A face template that failed to resolve only shows up as a null reference when
	/// the first agent spawns, which is a long way from the cause. Name the offenders
	/// here instead.
	/// </summary>
	private static void VerifyFaceTemplates(List<CharacterObject> troops)
	{
		List<string> broken = troops
			.Where(t => t.BodyPropertyRange == null)
			.Select(t => t.StringId)
			.Take(5)
			.ToList();

		if (broken.Count > 0)
		{
			ZombieLog.Error("ZombieTroopSetup: troops without a face template (first few: "
				+ string.Join(", ", broken) + ") - check ModuleData/zombie_bodyproperties.xml");
		}
	}

	private static void ApplyRace(List<CharacterObject> troops)
	{
		string[] races = FaceGen.GetRaceNames();
		int index = races == null ? -1 : System.Array.IndexOf(races, RaceId);

		if (index < 0)
		{
			ZombieLog.Error("ZombieTroopSetup: race '" + RaceId + "' not registered (known: "
				+ (races == null ? "<none>" : string.Join(", ", races))
				+ ") - zombies keep human skin. Check ModuleData/project.mbproj and skins.xml.");
			return;
		}

		// The race resolves its Monster by its own name, so a missing entry here
		// would only surface as a crash when the first agent spawns.
		if (FaceGen.GetBaseMonsterFromRace(index) == null)
		{
			ZombieLog.Error("ZombieTroopSetup: race '" + RaceId
				+ "' has no matching Monster - check ModuleData/zombie_monsters.xml");
			return;
		}

		foreach (CharacterObject troop in troops)
		{
			troop.Race = index;
		}

		ZombieLog.Info("SUCCESS race: '" + RaceId + "' is index " + index
			+ ", applied to " + troops.Count + " troops");
	}

	private static int StripRangedWeapons(List<CharacterObject> troops)
	{
		int cleared = 0;

		foreach (CharacterObject troop in troops)
		{
			foreach (Equipment equipment in troop.BattleEquipments.Concat(troop.CivilianEquipments))
			{
				for (EquipmentIndex slot = EquipmentIndex.WeaponItemBeginSlot;
					slot < EquipmentIndex.NumAllWeaponSlots;
					slot++)
				{
					if (!IsRangedOrAmmo(equipment[slot]))
					{
						continue;
					}

					equipment.AddEquipmentToSlotWithoutAgent(slot, default(EquipmentElement));
					cleared++;
				}
			}
		}

		return cleared;
	}

	private static bool IsRangedOrAmmo(EquipmentElement element)
	{
		WeaponComponentData weapon = element.Item?.PrimaryWeapon;
		return weapon != null && (weapon.IsRangedWeapon || weapon.IsAmmo);
	}
}
