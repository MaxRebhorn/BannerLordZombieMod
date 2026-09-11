using System.Collections.Generic;
using System.Linq;
using System.Text;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;
using TaleWorlds.ObjectSystem;
using ZombiePlague.Infrastructure;

namespace ZombiePlague.Cheats;

/// <summary>
/// Developer console commands, reachable ingame with Alt+~ (same console as
/// blse.version). Pattern follows vanilla CampaignCheats.
///
/// Every player-facing result string goes through TextObject with a
/// "{=token}fallback text" id, same pattern the rest of the mod uses (see
/// e.g. ZombieSpeedBuffPatch's buff texts) - the fallback is what's shown
/// unless ModuleData/Languages/std_module_strings_xml.xml has a translated
/// override for that token. The two read-only diagnostic dumps (Races,
/// SkinPalette) are the deliberate exception: they print raw engine data
/// tables, not a designed UI message, so localizing them would just be
/// busywork with no real translation value.
/// </summary>
public static class ZombieCheats
{
	private static readonly TextObject NoCampaignText = new("{=zombieplague_cheat_no_campaign}Keine laufende Kampagne.");
	private static readonly TextObject NoActivePartyText = new("{=zombieplague_cheat_no_active_party}Keine aktive Zombie-Party.");
	private static readonly TextObject SpawnFailedEngineLogText = new("{=zombieplague_cheat_spawn_failed}Spawn fehlgeschlagen - Details im Engine-Log.");
	private static readonly TextObject StateOnText = new("{=zombieplague_cheat_state_on}AN");
	private static readonly TextObject StateOffText = new("{=zombieplague_cheat_state_off}AUS");

	private static string OnOff(bool value) => (value ? StateOnText : StateOffText).ToString();

	[CommandLineFunctionality.CommandLineArgumentFunction("spawn_near_player", "zombie")]
	public static string SpawnNearPlayer(List<string> strings)
	{
		try
		{
			if (Campaign.Current == null)
			{
				return NoCampaignText.ToString();
			}

			int troopCount = 10;
			if (strings != null && strings.Count > 0 && int.TryParse(strings[0], out int parsed) && parsed > 0)
			{
				troopCount = parsed;
			}

			MobileParty party = ZombieSpawner.SpawnNearPlayer(troopCount);
			if (party == null)
			{
				return new TextObject("{=zombieplague_cheat_spawn_near_player_log_fail}Spawn fehlgeschlagen - Details im Logfile (Configs/ModLogs/zombieplague_*.log).").ToString();
			}

			float distance = party.Position.Distance(MobileParty.MainParty.Position);
			TextObject result = new("{=zombieplague_cheat_spawn_near_player_result}Zombie-Party '{PARTY}' gespawnt: {COUNT} Mann, {DIST} Einheiten entfernt (Sichtweite {SIGHT}).");
			result.SetTextVariable("PARTY", party.Name);
			result.SetTextVariable("COUNT", party.MemberRoster.TotalManCount);
			result.SetTextVariable("DIST", distance.ToString("0"));
			result.SetTextVariable("SIGHT", MobileParty.MainParty.SeeingRange.ToString("0"));
			return result.ToString();
		}
		catch (System.Exception exception)
		{
			ZombieLog.Error("spawn_near_player failed", exception);
			TextObject error = new("{=zombieplague_cheat_spawn_near_player_error}spawn_near_player fehlgeschlagen: {ERROR}");
			error.SetTextVariable("ERROR", exception.Message);
			return error.ToString();
		}
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
		try
		{
			if (Campaign.Current == null)
			{
				return NoCampaignText.ToString();
			}

			int looterCount = 10;
			if (strings != null && strings.Count > 0 && int.TryParse(strings[0], out int parsed) && parsed > 0)
			{
				looterCount = parsed;
			}

			if (!ZombieSpawner.SpawnBattleTest(looterCount))
			{
				return new TextObject("{=zombieplague_cheat_t5_failed}t5_battle FEHLGESCHLAGEN - Details im Engine-Log.").ToString();
			}

			TextObject result = new("{=zombieplague_cheat_t5_result}OK t5_battle: {LOOTERS} Looter vs {ZOMBIES} Zombies nebeneinander gespawnt. Vor/nach dem Kampf 'zombie.list' aufrufen, um das Wachstum zu pruefen.");
			result.SetTextVariable("LOOTERS", looterCount);
			result.SetTextVariable("ZOMBIES", looterCount * 2);
			return result.ToString();
		}
		catch (System.Exception exception)
		{
			ZombieLog.Error("t5_battle failed", exception);
			TextObject error = new("{=zombieplague_cheat_t5_error}t5_battle fehlgeschlagen: {ERROR}");
			error.SetTextVariable("ERROR", exception.Message);
			return error.ToString();
		}
	}

	private static string RunLadder(string label, bool useZombieClan, string templateId)
	{
		try
		{
			if (Campaign.Current == null)
			{
				return NoCampaignText.ToString();
			}

			MobileParty party = ZombieSpawner.SpawnLadderTest(label, useZombieClan, templateId);
			if (party == null)
			{
				TextObject failed = new("{=zombieplague_cheat_ladder_failed}{LABEL} FEHLGESCHLAGEN - Details im Engine-Log.");
				failed.SetTextVariable("LABEL", label);
				return failed.ToString();
			}

			float distance = party.Position.Distance(MobileParty.MainParty.Position);
			TextObject result = new("{=zombieplague_cheat_ladder_result}OK {LABEL}: '{PARTY}', {COUNT} Mann, {DIST} Einheiten entfernt.");
			result.SetTextVariable("LABEL", label);
			result.SetTextVariable("PARTY", party.Name);
			result.SetTextVariable("COUNT", party.MemberRoster.TotalManCount);
			result.SetTextVariable("DIST", distance.ToString("0"));
			return result.ToString();
		}
		catch (System.Exception exception)
		{
			ZombieLog.Error(label + " failed", exception);
			TextObject error = new("{=zombieplague_cheat_ladder_error}{LABEL} fehlgeschlagen: {ERROR}");
			error.SetTextVariable("LABEL", label);
			error.SetTextVariable("ERROR", exception.Message);
			return error.ToString();
		}
	}

	// Type 1/2 pick a specific named variant instead of a generic tier troop -
	// legionary and cataphract are the flavour of zombie asked for on the console.
	private const string LegionaryVariantId = "zombie_imperial_legionary";
	private const string CavalryVariantId = "zombie_imperial_cataphract";

	[CommandLineFunctionality.CommandLineArgumentFunction("spawn_zombies", "zombie")]
	public static string SpawnZombies(List<string> strings)
	{
		try
		{
			if (Campaign.Current == null)
			{
				return NoCampaignText.ToString();
			}

			if (strings == null || strings.Count == 0 || !int.TryParse(strings[0], out int count) || count <= 0)
			{
				return new TextObject("{=zombieplague_cheat_spawn_zombies_format}Format: zombie.spawn_zombies <anzahl> [tier 1-6 | 1=Legionaer 2=Kavallerie]").ToString();
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
				TextObject notFound = new("{=zombieplague_cheat_spawn_zombies_troop_not_found}Zombie-Truppe '{TROOP}' nicht gefunden.");
				notFound.SetTextVariable("TROOP", troopId);
				return notFound.ToString();
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
				return SpawnFailedEngineLogText.ToString();
			}

			ZombieLog.Info("SUCCESS spawn_zombies: " + count + " x " + troop.StringId + " -> " + party.StringId);
			TextObject result = new("{=zombieplague_cheat_spawn_zombies_result}OK spawn_zombies: {COUNT} x {LABEL}-Zombies gespawnt ({DIST} Einheiten entfernt).");
			result.SetTextVariable("COUNT", count);
			result.SetTextVariable("LABEL", label);
			result.SetTextVariable("DIST", party.Position.Distance(player.Position).ToString("0"));
			return result.ToString();
		}
		catch (System.Exception exception)
		{
			ZombieLog.Error("spawn_zombies failed", exception);
			TextObject error = new("{=zombieplague_cheat_spawn_zombies_error}spawn_zombies fehlgeschlagen: {ERROR}");
			error.SetTextVariable("ERROR", exception.Message);
			return error.ToString();
		}
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
		MobileParty party = SpawnHeroLedParty(20, ZombieIds.TroopId(1), strongLeader: false, out Hero hero, out string error);
		if (party == null)
		{
			return error;
		}

		TextObject result = new("{=zombieplague_cheat_spawn_hero_horde_result}OK spawn_hero_horde: Held '{HERO}' (Anfuehrer) + 20 Zombies in Party '{PARTY}' ({DIST} Einheiten entfernt).");
		result.SetTextVariable("HERO", hero.Name);
		result.SetTextVariable("PARTY", party.Name);
		result.SetTextVariable("DIST", party.Position.Distance(MobileParty.MainParty.Position).ToString("0"));
		return result.ToString();
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

		MobileParty party = SpawnHeroLedParty(troopCount, ZombieIds.TroopId(1), strongLeader: false, out Hero hero, out string error);
		if (party == null)
		{
			return error;
		}

		TextObject result = new("{=zombieplague_cheat_create_sieging_party_result}OK create_sieging_party: Held '{HERO}' (Anfuehrer) + {COUNT} Zombies in Party '{PARTY}' ({DIST} Einheiten entfernt).");
		result.SetTextVariable("HERO", hero.Name);
		result.SetTextVariable("COUNT", troopCount);
		result.SetTextVariable("PARTY", party.Name);
		result.SetTextVariable("DIST", party.Position.Distance(MobileParty.MainParty.Position).ToString("0"));
		return result.ToString();
	}

	/// <summary>
	/// Every siege attempted so far has been broken by the defenders - this is
	/// the "just win the damn siege" cheat: the strongest troop in the mod
	/// (zombie_patient_zero, level 80 vs. tier_6's level 35 - see
	/// zombie_troops.xml) instead of the usual tier-1 filler, led by a hero
	/// built from the single strongest living lord's CharacterObject (highest
	/// Level, not just "any lord") with every combat/tactics skill immediately
	/// maxed out. Troop count still defaults well past
	/// SettlementSiegeTroopThreshold and is still overridable.
	/// </summary>
	[CommandLineFunctionality.CommandLineArgumentFunction("create_op_sieging_party", "zombie")]
	public static string CreateOpSiegingParty(List<string> strings)
	{
		int troopCount = 500;
		if (strings != null && strings.Count > 0 && int.TryParse(strings[0], out int parsed) && parsed > 0)
		{
			troopCount = parsed;
		}

		MobileParty party = SpawnHeroLedParty(troopCount, ZombieIds.PatientZeroTroopId, strongLeader: true, out Hero hero, out string error);
		if (party == null)
		{
			return error;
		}

		TextObject result = new("{=zombieplague_cheat_create_op_sieging_party_result}OK create_op_sieging_party: Held '{HERO}' (Anfuehrer, alle Kampf-/Fuehrungsskills maximiert) + {COUNT} Patient-Zero-Zombies in Party '{PARTY}' ({DIST} Einheiten entfernt).");
		result.SetTextVariable("HERO", hero.Name);
		result.SetTextVariable("COUNT", troopCount);
		result.SetTextVariable("PARTY", party.Name);
		result.SetTextVariable("DIST", party.Position.Distance(MobileParty.MainParty.Position).ToString("0"));
		return result.ToString();
	}

	/// <summary>
	/// Skills SpawnHeroLedParty maxes out for strongLeader: true - every skill
	/// with a direct combat or battle-simulation effect. Tactics/Leadership are
	/// included alongside the weapon skills since sieges you don't personally
	/// fight in are resolved by simulation, which reads party-level skills like
	/// these, not just individual troop stats.
	/// </summary>
	private static readonly TaleWorlds.Core.SkillObject[] StrongLeaderSkills =
	{
		TaleWorlds.Core.DefaultSkills.OneHanded,
		TaleWorlds.Core.DefaultSkills.TwoHanded,
		TaleWorlds.Core.DefaultSkills.Polearm,
		TaleWorlds.Core.DefaultSkills.Bow,
		TaleWorlds.Core.DefaultSkills.Crossbow,
		TaleWorlds.Core.DefaultSkills.Throwing,
		TaleWorlds.Core.DefaultSkills.Riding,
		TaleWorlds.Core.DefaultSkills.Athletics,
		TaleWorlds.Core.DefaultSkills.Tactics,
		TaleWorlds.Core.DefaultSkills.Leadership
	};

	/// <summary>
	/// Shared by spawn_hero_horde, create_sieging_party and
	/// create_op_sieging_party: a brand-new hero born directly into the zombie
	/// clan (HeroCreator's `faction` param, so no risky reassignment of an
	/// existing lord's clan), given the standard zombie green race/face,
	/// leading a fresh `troopCount`-strong `troopId` party spawned near the
	/// player. `strongLeader` picks the single strongest living lord as the
	/// stat/equipment template (instead of just "any lord") and maxes every
	/// skill in StrongLeaderSkills immediately after creation.
	/// </summary>
	private static MobileParty SpawnHeroLedParty(int troopCount, string troopId, bool strongLeader, out Hero hero, out string error)
	{
		hero = null;
		error = null;

		try
		{
			if (Campaign.Current == null)
			{
				error = NoCampaignText.ToString();
				return null;
			}

			Clan zombieClan = ZombieClanUtil.GetZombieClan();
			if (zombieClan == null)
			{
				error = new TextObject("{=zombieplague_cheat_clan_unavailable}Zombie-Klan nicht verfuegbar.").ToString();
				return null;
			}

			CharacterObject template = strongLeader
				? Hero.AllAliveHeroes.Where(h => h.IsLord).OrderByDescending(h => h.CharacterObject.Level).FirstOrDefault()?.CharacterObject
					?? Hero.MainHero.CharacterObject
				: Hero.AllAliveHeroes.FirstOrDefault(h => h.IsLord)?.CharacterObject ?? Hero.MainHero.CharacterObject;
			hero = HeroCreator.CreateSpecialHero(template, bornSettlement: null, faction: zombieClan, supporterOfClan: null, age: -1);
			if (hero == null)
			{
				error = new TextObject("{=zombieplague_cheat_hero_creation_failed}Hero-Erstellung fehlgeschlagen.").ToString();
				return null;
			}

			hero.Culture = zombieClan.Culture;
			ZombieConversion.ApplyZombieAppearance(hero);

			if (strongLeader)
			{
				foreach (TaleWorlds.Core.SkillObject skill in StrongLeaderSkills)
				{
					hero.HeroDeveloper.SetInitialSkillLevel(skill, 300);
				}
			}

			CharacterObject troop = MBObjectManager.Instance.GetObject<CharacterObject>(troopId);
			if (troop == null)
			{
				TextObject notFound = new("{=zombieplague_cheat_troop_not_found}Zombie-Truppe nicht gefunden: {TROOP}");
				notFound.SetTextVariable("TROOP", troopId);
				error = notFound.ToString();
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
				error = SpawnFailedEngineLogText.ToString();
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
				+ ", troops=" + party.MemberRoster.TotalManCount + ", LeaderHero=" + (party.LeaderHero?.Name.ToString() ?? "NULL")
				+ ", strongLeader=" + strongLeader);

			return party;
		}
		catch (System.Exception exception)
		{
			ZombieLog.Error("SpawnHeroLedParty failed", exception);
			TextObject errorText = new("{=zombieplague_cheat_spawn_hero_error}Spawn fehlgeschlagen: {ERROR}");
			errorText.SetTextVariable("ERROR", exception.Message);
			error = errorText.ToString();
			return null;
		}
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
	/// Deliberately not localized - see the class doc comment.
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
	/// Deliberately not localized - see the class doc comment.
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
		try
		{
			if (Campaign.Current == null)
			{
				return NoCampaignText.ToString();
			}

			List<MobileParty> parties = GetZombieParties();
			if (parties.Count == 0)
			{
				return NoActivePartyText.ToString();
			}

			StringBuilder builder = new();
			foreach (MobileParty party in parties)
			{
				TextObject lineText = new("{=zombieplague_cheat_list_line}{PARTY} | {COUNT} Mann | ({X},{Y}) | {BEHAVIOR}");
				lineText.SetTextVariable("PARTY", party.Name);
				lineText.SetTextVariable("COUNT", party.MemberRoster.TotalManCount);
				lineText.SetTextVariable("X", party.Position.X.ToString("0"));
				lineText.SetTextVariable("Y", party.Position.Y.ToString("0"));
				lineText.SetTextVariable("BEHAVIOR", party.GetBehaviorText()?.ToString() ?? "unbekannt");
				string line = lineText.ToString();
				builder.AppendLine(line);
				ZombieLog.Info("  list: " + line);
			}

			ZombieLog.Info("SUCCESS list: " + parties.Count + " parties");
			return builder.ToString();
		}
		catch (System.Exception exception)
		{
			ZombieLog.Error("list failed", exception);
			TextObject error = new("{=zombieplague_cheat_list_error}list fehlgeschlagen: {ERROR}");
			error.SetTextVariable("ERROR", exception.Message);
			return error.ToString();
		}
	}

	[CommandLineFunctionality.CommandLineArgumentFunction("grow", "zombie")]
	public static string Grow(List<string> strings)
	{
		try
		{
			if (Campaign.Current == null)
			{
				return NoCampaignText.ToString();
			}

			if (strings == null || strings.Count == 0 || !int.TryParse(strings[0], out int count) || count <= 0)
			{
				return new TextObject("{=zombieplague_cheat_grow_format}Format: zombie.grow <anzahl> [tier 1-6]").ToString();
			}

			int tier = 1;
			if (strings.Count > 1 && int.TryParse(strings[1], out int parsedTier))
			{
				tier = parsedTier;
			}

			MobileParty target = GetZombiePartyNearestToPlayer();
			if (target == null)
			{
				return NoActivePartyText.ToString();
			}

			CharacterObject troop = MBObjectManager.Instance.GetObject<CharacterObject>(ZombieIds.TroopId(tier));
			if (troop == null)
			{
				TextObject notFound = new("{=zombieplague_cheat_grow_tier_not_found}Zombie-Truppe fuer Tier {TIER} nicht gefunden.");
				notFound.SetTextVariable("TIER", tier);
				return notFound.ToString();
			}

			target.MemberRoster.AddToCounts(troop, count);
			ZombieLog.Info("SUCCESS grow: +" + count + " x " + troop.StringId + " -> " + target.StringId
				+ " (now " + target.MemberRoster.TotalManCount + ")");
			TextObject result = new("{=zombieplague_cheat_grow_result}{PARTY}: +{COUNT} x {TROOP} (jetzt {TOTAL} Mann).");
			result.SetTextVariable("PARTY", target.Name);
			result.SetTextVariable("COUNT", count);
			result.SetTextVariable("TROOP", troop.StringId);
			result.SetTextVariable("TOTAL", target.MemberRoster.TotalManCount);
			return result.ToString();
		}
		catch (System.Exception exception)
		{
			ZombieLog.Error("grow failed", exception);
			TextObject error = new("{=zombieplague_cheat_grow_error}grow fehlgeschlagen: {ERROR}");
			error.SetTextVariable("ERROR", exception.Message);
			return error.ToString();
		}
	}

	[CommandLineFunctionality.CommandLineArgumentFunction("kill_all", "zombie")]
	public static string KillAll(List<string> strings)
	{
		try
		{
			if (Campaign.Current == null)
			{
				return NoCampaignText.ToString();
			}

			List<MobileParty> parties = GetZombieParties();
			foreach (MobileParty party in parties)
			{
				DestroyPartyAction.ApplyForDisbanding(party, party.HomeSettlement);
			}

			ZombieLog.Info("SUCCESS kill_all: removed " + parties.Count + " parties");
			TextObject result = new("{=zombieplague_cheat_kill_all_result}{COUNT} Zombie-Party(s) entfernt.");
			result.SetTextVariable("COUNT", parties.Count);
			return result.ToString();
		}
		catch (System.Exception exception)
		{
			ZombieLog.Error("kill_all failed", exception);
			TextObject error = new("{=zombieplague_cheat_kill_all_error}kill_all fehlgeschlagen: {ERROR}");
			error.SetTextVariable("ERROR", exception.Message);
			return error.ToString();
		}
	}

	[CommandLineFunctionality.CommandLineArgumentFunction("toggle_autospawn", "zombie")]
	public static string ToggleAutoSpawn(List<string> strings)
	{
		try
		{
			ZombieIds.AutoSpawnOnNewGame = !ZombieIds.AutoSpawnOnNewGame;
			ZombieLog.Info("SUCCESS toggle_autospawn -> " + ZombieIds.AutoSpawnOnNewGame);
			TextObject result = new("{=zombieplague_cheat_toggle_autospawn_result}Auto-Spawn bei Kampagnenstart: {STATE}");
			result.SetTextVariable("STATE", OnOff(ZombieIds.AutoSpawnOnNewGame));
			return result.ToString();
		}
		catch (System.Exception exception)
		{
			ZombieLog.Error("toggle_autospawn failed", exception);
			TextObject error = new("{=zombieplague_cheat_toggle_autospawn_error}toggle_autospawn fehlgeschlagen: {ERROR}");
			error.SetTextVariable("ERROR", exception.Message);
			return error.ToString();
		}
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
		try
		{
			ZombieIds.FarsightEnabled = !ZombieIds.FarsightEnabled;
			ZombieLog.Info("SUCCESS toggle_farsight -> " + ZombieIds.FarsightEnabled);
			TextObject result = new("{=zombieplague_cheat_toggle_farsight_result}Farsight (Sichtweite {RANGE}): {STATE}");
			result.SetTextVariable("RANGE", ZombieBehaviorConfig.FarsightSeeingRange.ToString("0"));
			result.SetTextVariable("STATE", OnOff(ZombieIds.FarsightEnabled));
			return result.ToString();
		}
		catch (System.Exception exception)
		{
			ZombieLog.Error("toggle_farsight failed", exception);
			TextObject error = new("{=zombieplague_cheat_toggle_farsight_error}toggle_farsight fehlgeschlagen: {ERROR}");
			error.SetTextVariable("ERROR", exception.Message);
			return error.ToString();
		}
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
