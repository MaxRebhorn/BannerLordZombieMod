using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.ObjectSystem;

namespace ZombiePlague.Infrastructure;

/// <summary>
/// Maps a defeated enemy troop to the zombie troop that replaces it.
///
/// The catalogue in ModuleData/zombie_troops_generated.xml holds a
/// "zombie_&lt;original id&gt;" clone of every vanilla combat troop, so an Imperial
/// Legionary comes back as a Zombie Imperial Legionary with the same level, skills
/// and armour. Anything without a clone - a troop from another mod, or one the
/// generator's occupation filter skipped - falls back to the six generic
/// zombie_tier_N troops, which is what the whole mod used before the catalogue
/// existed.
/// </summary>
internal static class ZombieConversion
{
	private static readonly Dictionary<CharacterObject, CharacterObject> Cache = new();

	/// <summary>
	/// Resolves the zombie counterpart, or null if not even the tier fallback exists
	/// (which would mean the module XML failed to load).
	/// </summary>
	public static CharacterObject MapToZombie(CharacterObject victim)
	{
		if (victim == null || victim.IsHero)
		{
			return null;
		}

		if (Cache.TryGetValue(victim, out CharacterObject cached))
		{
			return cached;
		}

		CharacterObject zombie =
			MBObjectManager.Instance.GetObject<CharacterObject>(ZombieIds.VariantId(victim.StringId))
			?? MBObjectManager.Instance.GetObject<CharacterObject>(ZombieIds.TroopId(victim.Tier));

		if (zombie == null)
		{
			ZombieLog.Error("MapToZombie: no zombie troop for '" + victim.StringId + "' and no tier fallback");
		}

		Cache[victim] = zombie;
		return zombie;
	}

	/// <summary>
	/// Object identity survives a save/load cycle only within one session, so the
	/// cache has to go when a campaign is (re)started.
	/// </summary>
	public static void ClearCache()
	{
		Cache.Clear();
	}

	/// <summary>
	/// Turns a tally of defeated enemy troops into the zombie troops they become,
	/// strictly one for one.
	/// </summary>
	public static Dictionary<CharacterObject, int> MapTally(Dictionary<CharacterObject, int> tally)
	{
		Dictionary<CharacterObject, int> result = new();
		foreach (KeyValuePair<CharacterObject, int> entry in tally)
		{
			CharacterObject zombie = MapToZombie(entry.Key);
			if (zombie == null)
			{
				continue;
			}

			result.TryGetValue(zombie, out int existing);
			result[zombie] = existing + entry.Value;
		}

		return result;
	}

	/// <summary>
	/// Gives a hero the same green race/face regular zombie troops get.
	///
	/// A Hero carries its own unique CharacterObject (cloned by HeroCreator from
	/// whatever template it was born from), not one of the zombie_-prefixed troop
	/// templates ZombieTroopSetup.ApplyRace already handles, and its face is a
	/// baked-once StaticBodyProperties rather than a per-agent-random range - so
	/// both need to be set explicitly here rather than falling out of the normal
	/// troop pipeline. Mirrors CharacterObject's own face-randomization call
	/// (FaceGen.GetRandomBodyProperties against a BodyPropertyRange) using our
	/// zombie_male/zombie_female templates from ModuleData/zombie_bodyproperties.xml.
	/// </summary>
	public static void ApplyZombieAppearance(Hero hero)
	{
		if (hero == null)
		{
			return;
		}

		string[] races = FaceGen.GetRaceNames();
		int raceIndex = races == null ? -1 : Array.IndexOf(races, ZombieTroopSetup.RaceId);
		if (raceIndex < 0)
		{
			ZombieLog.Error("ApplyZombieAppearance: race '" + ZombieTroopSetup.RaceId + "' not registered - hero keeps human skin.");
			return;
		}

		hero.CharacterObject.Race = raceIndex;

		string bodyPropertyId = hero.IsFemale ? ZombieIds.BodyPropertyIds[1] : ZombieIds.BodyPropertyIds[0];
		MBBodyProperty bodyPropertyRange = MBObjectManager.Instance.GetObject<MBBodyProperty>(bodyPropertyId);
		if (bodyPropertyRange == null)
		{
			ZombieLog.Error("ApplyZombieAppearance: body property template '" + bodyPropertyId + "' not found.");
			return;
		}

		BodyProperties randomBodyProperties = FaceGen.GetRandomBodyProperties(
			raceIndex,
			hero.IsFemale,
			bodyPropertyRange.BodyPropertyMin,
			bodyPropertyRange.BodyPropertyMax,
			0,
			MBRandom.RandomInt(int.MaxValue),
			bodyPropertyRange.HairTags,
			bodyPropertyRange.BeardTags,
			bodyPropertyRange.TattooTags,
			0f);

		hero.StaticBodyProperties = randomBodyProperties.StaticProperties;

		ZombieLog.Info("ApplyZombieAppearance: " + hero.Name + " -> race " + raceIndex + ", face from " + bodyPropertyId);
	}

	/// <summary>
	/// TODO.md Phase 2: a defeated enemy hero that rolled successfully joins the
	/// horde outright - clan, culture and appearance all switch over via the
	/// same ApplyZombieAppearance used by the spawn_hero_horde test cheat, and
	/// AddHeroToPartyAction.Apply is the official way to move them into the
	/// party's MemberRoster (it also cleans up wherever they were before -
	/// their old party, a governed town, etc). If the party has no leader yet,
	/// this hero becomes it via ZombiePartyComponent - see that class for why a
	/// subclass is used instead of LordPartyComponent - which is also what
	/// unlocks siege eligibility later (BesiegerCamp reads LeaderHero directly).
	/// </summary>
	public static void ConvertHero(Hero hero, MobileParty zombieParty)
	{
		Clan zombieClan = ZombieClanUtil.GetZombieClan();
		if (zombieClan == null || hero == null || zombieParty == null)
		{
			return;
		}

		hero.Clan = zombieClan;
		hero.Culture = zombieClan.Culture;
		ApplyZombieAppearance(hero);
		AddHeroToPartyAction.Apply(hero, zombieParty);

		bool becameLeader = zombieParty.LeaderHero == null;
		if (becameLeader)
		{
			ZombiePartyComponent.ConvertPartyToZombieLeaderParty(zombieParty, hero);
		}

		ZombieLog.Info("ConvertHero: " + hero.Name + " turned, joined " + zombieParty.StringId
			+ (becameLeader ? " as leader" : " as member"));
	}
}
