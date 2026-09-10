using System.Collections.Generic;
using System.Linq;
using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.ObjectSystem;
using ZombiePlague.Infrastructure;

namespace ZombiePlague.Cheats;

/// <summary>
/// Developer console commands, reachable ingame with Alt+~ (same console as
/// blse.version). Pattern follows vanilla CampaignCheats.
/// </summary>
public static class ZombieCheats
{
	[CommandLineFunctionality.CommandLineArgumentFunction("spawn_near_player", "zombie")]
	public static string SpawnNearPlayer(List<string> strings)
	{
		if (Campaign.Current == null)
		{
			return "Keine laufende Kampagne.";
		}

		int troopCount = 10;
		if (strings != null && strings.Count > 0 && int.TryParse(strings[0], out int parsed) && parsed > 0)
		{
			troopCount = parsed;
		}

		MobileParty party = ZombieSpawner.SpawnNearPlayer(troopCount);
		if (party == null)
		{
			return "Spawn fehlgeschlagen - Details im Logfile (Configs/ModLogs/zombieplague_*.log).";
		}

		float distance = party.Position.Distance(MobileParty.MainParty.Position);
		return string.Format(
			"Zombie-Party '{0}' gespawnt: {1} Mann, {2:0} Einheiten entfernt (Sichtweite {3:0}).",
			party.Name, party.MemberRoster.TotalManCount, distance, MobileParty.MainParty.SeeingRange);
	}

	// --- Bisection ladder -------------------------------------------------------
	// Each rung changes exactly one thing versus the rung before it. Run them in
	// order; the first one that crashes names the culprit.
	//
	//   t0  vanilla clan   + vanilla template   (baseline, known to work)
	//   t1  ZOMBIE clan    + vanilla template   -> tests our runtime-created clan
	//   t2  zombie clan    + CLONE template     -> tests our XML pipeline, troop data
	//                                              identical to the vanilla looter
	//   t3  zombie clan    + ONEROSTER template -> adds "only one equipment roster"
	//   t4  zombie clan    + ZOMBIE template    -> adds our real troop customizations

	[CommandLineFunctionality.CommandLineArgumentFunction("t0_looter", "zombie")]
	public static string LadderT0(List<string> strings)
	{
		return RunLadder("t0_looter", useZombieClan: false, templateId: null);
	}

	[CommandLineFunctionality.CommandLineArgumentFunction("spawn_looter", "zombie")]
	public static string SpawnLooter(List<string> strings)
	{
		return RunLadder("t0_looter", useZombieClan: false, templateId: null);
	}

	[CommandLineFunctionality.CommandLineArgumentFunction("t1_clan", "zombie")]
	public static string LadderT1(List<string> strings)
	{
		return RunLadder("t1_clan", useZombieClan: true, templateId: null);
	}

	[CommandLineFunctionality.CommandLineArgumentFunction("t2_clone", "zombie")]
	public static string LadderT2(List<string> strings)
	{
		return RunLadder("t2_clone", useZombieClan: true, templateId: "zombie_debug_clone_template");
	}

	[CommandLineFunctionality.CommandLineArgumentFunction("t3_oneroster", "zombie")]
	public static string LadderT3(List<string> strings)
	{
		return RunLadder("t3_oneroster", useZombieClan: true, templateId: "zombie_debug_oneroster_template");
	}

	[CommandLineFunctionality.CommandLineArgumentFunction("t4_zombie", "zombie")]
	public static string LadderT4(List<string> strings)
	{
		return RunLadder("t4_zombie", useZombieClan: true, templateId: ZombieIds.PartyTemplateId);
	}

	[CommandLineFunctionality.CommandLineArgumentFunction("t5_battle", "zombie")]
	public static string LadderT5(List<string> strings)
	{
		if (Campaign.Current == null)
		{
			return "Keine laufende Kampagne.";
		}

		int looterCount = 10;
		if (strings != null && strings.Count > 0 && int.TryParse(strings[0], out int parsed) && parsed > 0)
		{
			looterCount = parsed;
		}

		if (!ZombieSpawner.SpawnBattleTest(looterCount))
		{
			return "t5_battle FEHLGESCHLAGEN - Details im Engine-Log.";
		}

		return string.Format(
			"OK t5_battle: {0} Looter vs {1} Zombies nebeneinander gespawnt. Vor/nach dem Kampf 'zombie.list' aufrufen, um das Wachstum zu pruefen.",
			looterCount, looterCount * 2);
	}

	private static string RunLadder(string label, bool useZombieClan, string templateId)
	{
		if (Campaign.Current == null)
		{
			return "Keine laufende Kampagne.";
		}

		MobileParty party = ZombieSpawner.SpawnLadderTest(label, useZombieClan, templateId);
		if (party == null)
		{
			return label + " FEHLGESCHLAGEN - Details im Engine-Log.";
		}

		float distance = party.Position.Distance(MobileParty.MainParty.Position);
		return string.Format(
			"OK {0}: '{1}', {2} Mann, {3:0} Einheiten entfernt.",
			label, party.Name, party.MemberRoster.TotalManCount, distance);
	}

	// Type 1/2 pick a specific named variant instead of a generic tier troop -
	// legionary and cataphract are the flavour of zombie asked for on the console.
	private const string LegionaryVariantId = "zombie_imperial_legionary";
	private const string CavalryVariantId = "zombie_imperial_cataphract";

	[CommandLineFunctionality.CommandLineArgumentFunction("spawn_zombies", "zombie")]
	public static string SpawnZombies(List<string> strings)
	{
		if (Campaign.Current == null)
		{
			return "Keine laufende Kampagne.";
		}

		if (strings == null || strings.Count == 0 || !int.TryParse(strings[0], out int count) || count <= 0)
		{
			return "Format: zombie.spawn_zombies <anzahl> [tier 1-6 | 1=Legionaer 2=Kavallerie]";
		}

		string troopId;
		string label;
		if (strings.Count > 1 && strings[1] == "1")
		{
			troopId = LegionaryVariantId;
			label = "Legionaer";
		}
		else if (strings.Count > 1 && strings[1] == "2")
		{
			troopId = CavalryVariantId;
			label = "Kavallerie";
		}
		else
		{
			int tier = 1;
			if (strings.Count > 1 && int.TryParse(strings[1], out int parsedTier))
			{
				tier = parsedTier;
			}

			troopId = ZombieIds.TroopId(tier);
			label = "Tier-" + tier;
		}

		CharacterObject troop = MBObjectManager.Instance.GetObject<CharacterObject>(troopId);
		if (troop == null)
		{
			return "Zombie-Truppe '" + troopId + "' nicht gefunden.";
		}

		MobileParty player = MobileParty.MainParty;
		float sight = player.SeeingRange;
		MobileParty party = ZombieSpawner.SpawnNearPosition(
			player.Position,
			sight * 0.8f,
			sight * 0.4f,
			new Dictionary<CharacterObject, int> { { troop, count } },
			0);

		if (party == null)
		{
			return "Spawn fehlgeschlagen - Details im Engine-Log.";
		}

		ZombieLog.Info("SUCCESS spawn_zombies: " + count + " x " + troop.StringId + " -> " + party.StringId);
		return string.Format(
			"OK spawn_zombies: {0} x {1}-Zombies gespawnt ({2:0} Einheiten entfernt).",
			count, label, party.Position.Distance(player.Position));
	}

	/// <summary>
	/// Phase 1 test cheat for TODO.md's hero-conversion work: spawns a horde with
	/// a brand-new hero already born into the zombie clan (via HeroCreator's
	/// `faction` parameter) rather than reassigning an existing lord's clan -
	/// this is deliberately the lower-risk half of "hero in a zombie party"
	/// before the real conversion-of-a-defeated-lord path touches Hero.Clan on
	/// an existing hero. The template is any living lord's CharacterObject
	/// (falls back to the player's own) purely for stats/equipment/face shape -
	/// HeroCreator does not keep any link back to whoever was cloned from.
	/// </summary>
	[CommandLineFunctionality.CommandLineArgumentFunction("spawn_hero_horde", "zombie")]
	public static string SpawnHeroHorde(List<string> strings)
	{
		MobileParty party = SpawnHeroLedParty(20, out Hero hero, out string error);
		if (party == null)
		{
			return error;
		}

		return string.Format(
			"OK spawn_hero_horde: Held '{0}' (Anfuehrer) + 20 Zombies in Party '{1}' ({2:0} Einheiten entfernt).",
			hero.Name, party.Name, party.Position.Distance(MobileParty.MainParty.Position));
	}

	/// <summary>
	/// TODO.md Phase 3 test cheat: a dedicated command for a large, already
	/// siege-eligible hero-led horde (default 1000 troops, comfortably past
	/// SettlementSiegeTroopThreshold) - spawn_hero_horde's 20 troops need many
	/// minutes of zombie.grow calls to reach siege scale, this skips straight
	/// there. Optional first argument overrides the troop count.
	/// </summary>
	[CommandLineFunctionality.CommandLineArgumentFunction("create_sieging_party", "zombie")]
	public static string CreateSiegingParty(List<string> strings)
	{
		int troopCount = 1000;
		if (strings != null && strings.Count > 0 && int.TryParse(strings[0], out int parsed) && parsed > 0)
		{
			troopCount = parsed;
		}

		MobileParty party = SpawnHeroLedParty(troopCount, out Hero hero, out string error);
		if (party == null)
		{
			return error;
		}

		return string.Format(
			"OK create_sieging_party: Held '{0}' (Anfuehrer) + {1} Zombies in Party '{2}' ({3:0} Einheiten entfernt).",
			hero.Name, troopCount, party.Name, party.Position.Distance(MobileParty.MainParty.Position));
	}

	/// <summary>
	/// Shared by spawn_hero_horde and create_sieging_party: a brand-new hero
	/// born directly into the zombie clan (HeroCreator's `faction` param, so no
	/// risky reassignment of an existing lord's clan), given the standard zombie
	/// green race/face, leading a fresh `troopCount`-strong tier-1 zombie party
	/// spawned near the player.
	/// </summary>
	private static MobileParty SpawnHeroLedParty(int troopCount, out Hero hero, out string error)
	{
		hero = null;
		error = null;

		if (Campaign.Current == null)
		{
			error = "Keine laufende Kampagne.";
			return null;
		}

		Clan zombieClan = ZombieClanUtil.GetZombieClan();
		if (zombieClan == null)
		{
			error = "Zombie-Klan nicht verfuegbar.";
			return null;
		}

		CharacterObject template = Hero.AllAliveHeroes.FirstOrDefault(h => h.IsLord)?.CharacterObject ?? Hero.MainHero.CharacterObject;
		hero = HeroCreator.CreateSpecialHero(template, bornSettlement: null, faction: zombieClan, supporterOfClan: null, age: -1);
		if (hero == null)
		{
			error = "Hero-Erstellung fehlgeschlagen.";
			return null;
		}

		hero.Culture = zombieClan.Culture;
		ZombieConversion.ApplyZombieAppearance(hero);

		CharacterObject troop = MBObjectManager.Instance.GetObject<CharacterObject>(ZombieIds.TroopId(1));
		if (troop == null)
		{
			error = "Zombie-Truppe nicht gefunden.";
			return null;
		}

		MobileParty player = MobileParty.MainParty;
		MobileParty party = ZombieSpawner.SpawnNearPosition(
			player.Position,
			player.SeeingRange * 0.8f,
			player.SeeingRange * 0.4f,
			new Dictionary<CharacterObject, int> { { troop, troopCount } },
			0);

		if (party == null)
		{
			error = "Spawn fehlgeschlagen - Details im Engine-Log.";
			return null;
		}

		AddHeroToPartyAction.Apply(hero, party);

		// BanditPartyComponent (what ZombieSpawner builds every horde on) never
		// overrides PartyComponent.Leader - it is hardcoded to null there, so a
		// hero merely sitting in the roster can never become LeaderHero. Using
		// our own ZombiePartyComponent subclass instead of switching to
		// LordPartyComponent keeps MobileParty.IsBandit true (it's a live `is
		// BanditPartyComponent` check) while still giving the party a real
		// LeaderHero - which is what unlocks sieging (BesiegerCamp reads
		// LeaderHero directly).
		ZombiePartyComponent.ConvertPartyToZombieLeaderParty(party, hero);

		ZombieLog.Info("SUCCESS SpawnHeroLedParty: hero=" + hero.Name + " (" + hero.StringId + ") -> " + party.StringId
			+ ", troops=" + party.MemberRoster.TotalManCount + ", LeaderHero=" + (party.LeaderHero?.Name.ToString() ?? "NULL"));

		return party;
	}

	/// <summary>
	/// Breakdown of the last zombie battle: how many of each enemy troop died,
	/// fled, were wounded or captured, and which zombie variant they became. Turns
	/// "the horde grew" into a number that can be checked.
	/// </summary>
	[CommandLineFunctionality.CommandLineArgumentFunction("last_battle", "zombie")]
	public static string LastBattle(List<string> strings)
	{
		return ZombieBattleReport.Last;
	}

	/// <summary>
	/// Read-only probe: which races the engine registered, and the skin colour
	/// palette behind each. Skin tone is an offset into that per-race gradient, not
	/// a free RGB value - this is how to tell whether the zombie race came through.
	/// </summary>
	[CommandLineFunctionality.CommandLineArgumentFunction("races", "zombie")]
	public static string Races(List<string> strings)
	{
		try
		{
			string[] names = TaleWorlds.Core.FaceGen.GetRaceNames() ?? new string[0];
			StringBuilder builder = new();
			builder.AppendLine("Rassen: " + string.Join(", ", names));

			for (int race = 0; race < names.Length; race++)
			{
				List<uint> colors = MBBodyProperties.GetSkinColorGradientPoints(race, 0, 30);
				bool hasMonster = TaleWorlds.Core.FaceGen.GetBaseMonsterFromRace(race) != null;
				string line = "[" + race + "] " + names[race] + ": " + colors.Count
					+ " Hautfarben, erste " + (colors.Count > 0 ? colors[0].ToString("X8") : "-")
					+ ", Monster " + (hasMonster ? "ok" : "FEHLT");
				builder.AppendLine(line);
				ZombieLog.Info("races " + line);
			}

			ZombieLog.Info("SUCCESS races");
			return builder.ToString();
		}
		catch (System.Exception exception)
		{
			ZombieLog.Error("races failed", exception);
			return "races fehlgeschlagen: " + exception.Message;
		}
	}

	/// <summary>
	/// Read-only probe: dumps the skin colour palette the engine offers for the
	/// human race. Skin tone is an index into this gradient, not a free RGB value,
	/// so this tells us whether anything green is reachable at all.
	/// </summary>
	[CommandLineFunctionality.CommandLineArgumentFunction("skin_palette", "zombie")]
	public static string SkinPalette(List<string> strings)
	{
		try
		{
			StringBuilder builder = new();
			for (int gender = 0; gender <= 1; gender++)
			{
				List<uint> colors = MBBodyProperties.GetSkinColorGradientPoints(0, gender, 30);
				string genderName = gender == 0 ? "male" : "female";
				builder.AppendLine(genderName + ": " + colors.Count + " Farben");
				ZombieLog.Info("skin palette " + genderName + ": " + colors.Count + " entries");

				for (int i = 0; i < colors.Count; i++)
				{
					string line = "  [" + i + "] " + colors[i].ToString("X8");
					builder.AppendLine(line);
					ZombieLog.Info("  " + genderName + line);
				}
			}

			ZombieLog.Info("SUCCESS skin_palette");
			return builder.ToString();
		}
		catch (System.Exception exception)
		{
			ZombieLog.Error("skin_palette failed", exception);
			return "skin_palette fehlgeschlagen: " + exception.Message;
		}
	}

	[CommandLineFunctionality.CommandLineArgumentFunction("list", "zombie")]
	public static string List(List<string> strings)
	{
		if (Campaign.Current == null)
		{
			return "Keine laufende Kampagne.";
		}

		List<MobileParty> parties = GetZombieParties();
		if (parties.Count == 0)
		{
			return "Keine aktive Zombie-Party.";
		}

		StringBuilder builder = new();
		foreach (MobileParty party in parties)
		{
			string line = string.Format(
				"{0} | {1} Mann | ({2:0},{3:0}) | {4}",
				party.Name, party.MemberRoster.TotalManCount, party.Position.X, party.Position.Y, party.GetBehaviorText());
			builder.AppendLine(line);
			ZombieLog.Info("  list: " + line);
		}

		ZombieLog.Info("SUCCESS list: " + parties.Count + " parties");
		return builder.ToString();
	}

	[CommandLineFunctionality.CommandLineArgumentFunction("grow", "zombie")]
	public static string Grow(List<string> strings)
	{
		if (Campaign.Current == null)
		{
			return "Keine laufende Kampagne.";
		}

		if (strings == null || strings.Count == 0 || !int.TryParse(strings[0], out int count) || count <= 0)
		{
			return "Format: zombie.grow <anzahl> [tier 1-6]";
		}

		int tier = 1;
		if (strings.Count > 1 && int.TryParse(strings[1], out int parsedTier))
		{
			tier = parsedTier;
		}

		MobileParty target = GetZombiePartyNearestToPlayer();
		if (target == null)
		{
			return "Keine aktive Zombie-Party.";
		}

		CharacterObject troop = MBObjectManager.Instance.GetObject<CharacterObject>(ZombieIds.TroopId(tier));
		if (troop == null)
		{
			return "Zombie-Truppe fuer Tier " + tier + " nicht gefunden.";
		}

		target.MemberRoster.AddToCounts(troop, count);
		ZombieLog.Info("SUCCESS grow: +" + count + " x " + troop.StringId + " -> " + target.StringId
			+ " (now " + target.MemberRoster.TotalManCount + ")");
		return string.Format("{0}: +{1} x {2} (jetzt {3} Mann).",
			target.Name, count, troop.StringId, target.MemberRoster.TotalManCount);
	}

	[CommandLineFunctionality.CommandLineArgumentFunction("kill_all", "zombie")]
	public static string KillAll(List<string> strings)
	{
		if (Campaign.Current == null)
		{
			return "Keine laufende Kampagne.";
		}

		List<MobileParty> parties = GetZombieParties();
		foreach (MobileParty party in parties)
		{
			DestroyPartyAction.ApplyForDisbanding(party, party.HomeSettlement);
		}

		ZombieLog.Info("SUCCESS kill_all: removed " + parties.Count + " parties");
		return parties.Count + " Zombie-Party(s) entfernt.";
	}

	[CommandLineFunctionality.CommandLineArgumentFunction("toggle_autospawn", "zombie")]
	public static string ToggleAutoSpawn(List<string> strings)
	{
		ZombieIds.AutoSpawnOnNewGame = !ZombieIds.AutoSpawnOnNewGame;
		ZombieLog.Info("SUCCESS toggle_autospawn -> " + ZombieIds.AutoSpawnOnNewGame);
		return "Auto-Spawn bei Kampagnenstart: " + (ZombieIds.AutoSpawnOnNewGame ? "AN" : "AUS");
	}

	/// <summary>
	/// Debug/testing QoL: boosts the player's own map spotting range to
	/// ZombieBehaviorConfig.FarsightSeeingRange (see PlayerFarsightPatch) so
	/// zombie hordes - or anything else - stay visible from far away, for
	/// watching AI behavior without babysitting sight range.
	/// </summary>
	[CommandLineFunctionality.CommandLineArgumentFunction("toggle_farsight", "zombie")]
	public static string ToggleFarsight(List<string> strings)
	{
		ZombieIds.FarsightEnabled = !ZombieIds.FarsightEnabled;
		ZombieLog.Info("SUCCESS toggle_farsight -> " + ZombieIds.FarsightEnabled);
		return "Farsight (Sichtweite " + ZombieBehaviorConfig.FarsightSeeingRange.ToString("0") + "): "
			+ (ZombieIds.FarsightEnabled ? "AN" : "AUS");
	}

	private static List<MobileParty> GetZombieParties()
	{
		return MobileParty.AllBanditParties.Where(ZombieClanUtil.IsZombieParty).ToList();
	}

	private static MobileParty GetZombiePartyNearestToPlayer()
	{
		MobileParty nearest = null;
		float best = float.MaxValue;
		foreach (MobileParty party in GetZombieParties())
		{
			float distance = party.Position.Distance(MobileParty.MainParty.Position);
			if (distance < best)
			{
				best = distance;
				nearest = party;
			}
		}

		return nearest;
	}
}
