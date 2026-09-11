using System;
using System.Collections.Generic;
using System.Linq;
using Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.ObjectSystem;

namespace ZombiePlague.Infrastructure;

/// <summary>
/// All zombie party creation goes through here. Deliberately mirrors vanilla's
/// BanditSpawnCampaignBehavior.SpawnLooterParty step for step - spawning at a raw
/// settlement gate position and skipping the post-create initialization left the
/// party half-built, which is the prime suspect for the native campaign-start crash.
/// </summary>
internal static class ZombieSpawner
{
	private const int DefaultStartingTroops = 8;

	/// <summary>Same radius vanilla uses for looter spawns around a settlement.</summary>
	public static float SpawnRadius =>
		0.5f * Campaign.Current.EstimatedAverageBanditPartySpeed * (float)CampaignTime.HoursInDay * 1.5f;

	/// <summary>
	/// Verifies that everything our XML is supposed to register actually resolved.
	/// A missing troop would leave spawned parties empty, so this runs at session
	/// start rather than being discovered later through a crash.
	/// </summary>
	public static void LogObjectDiagnostics()
	{
		ZombieLog.Info("--- object diagnostics ---");

		for (int tier = ZombieIds.MinTier; tier <= ZombieIds.MaxTier; tier++)
		{
			string id = ZombieIds.TroopId(tier);
			CharacterObject troop = MBObjectManager.Instance.GetObject<CharacterObject>(id);
			ZombieLog.Info("  " + id + " -> " + (troop == null ? "NULL" : "ok, tier=" + troop.Tier + ", level=" + troop.Level));
		}

		foreach (string debugTroopId in new[] { "zombie_debug_clone", "zombie_debug_oneroster" })
		{
			CharacterObject troop = MBObjectManager.Instance.GetObject<CharacterObject>(debugTroopId);
			ZombieLog.Info("  " + debugTroopId + " -> " + (troop == null ? "NULL" : "ok, tier=" + troop.Tier));
		}

		foreach (string templateId in new[]
			{ ZombieIds.PartyTemplateId, "zombie_debug_clone_template", "zombie_debug_oneroster_template" })
		{
			PartyTemplateObject template = MBObjectManager.Instance.GetObject<PartyTemplateObject>(templateId);
			ZombieLog.Info("  " + templateId + " -> " + (template == null ? "NULL" : "ok"));
		}

		CultureObject culture = MBObjectManager.Instance.GetObject<CultureObject>("looters");
		ZombieLog.Info("  culture looters -> " + (culture == null ? "NULL" : "ok"));

		Clan clan = Clan.All.FirstOrDefault(c => c.StringId == ZombieIds.ClanId);
		if (clan == null)
		{
			ZombieLog.Error("  clan " + ZombieIds.ClanId + " -> NULL (zombie_clans.xml not loaded?)");
		}
		else
		{
			ZombieLog.Info("  clan " + clan.StringId + " -> ok"
				+ ", culture=" + (clan.Culture == null ? "NULL" : clan.Culture.StringId)
				+ ", banner=" + (clan.Banner == null ? "NULL" : "ok")
				+ ", defaultTemplate=" + (clan.DefaultPartyTemplate == null ? "NULL" : clan.DefaultPartyTemplate.StringId)
				+ ", tier=" + clan.Tier
				+ ", minorFaction=" + clan.IsMinorFaction);
		}
	}

	public static MobileParty SpawnAtRandomSettlement()
	{
		ZombieLog.Info("SpawnAtRandomSettlement: start");

		Settlement anchor = PickRandomTownOrVillage();
		if (anchor == null)
		{
			ZombieLog.Error("SpawnAtRandomSettlement: no town or village available");
			return null;
		}

		ZombieLog.Info("  anchor=" + anchor.Name + " (" + anchor.StringId + ")");

		// Vanilla never spawns exactly on the gate - it picks a navmesh-validated
		// point around it. A position whose navmesh face is invalid for a land
		// party sends native pathfinding into invalid memory.
		CampaignVec2 position = NavigationHelper.FindPointAroundPosition(
			anchor.GatePosition,
			MobileParty.NavigationType.Default,
			SpawnRadius);
		LogPosition("  settlement spawn", position);

		// Keep whatever the template defines as the starting horde.
		return SpawnAt(anchor, position, null, 0);
	}

	/// <summary>
	/// Initial-horde spawn (see ZombiePlagueCampaignBehavior.TrySpawnStartingHordes):
	/// a random town/village, same as SpawnAtRandomSettlement, but with an
	/// explicit troop type/count instead of the template's fixed roster.
	/// </summary>
	public static MobileParty SpawnStartingHorde(string troopId, int troopCount)
	{
		ZombieLog.Info("SpawnStartingHorde: troopId=" + troopId + " troopCount=" + troopCount);

		Settlement anchor = PickRandomTownOrVillage();
		if (anchor == null)
		{
			ZombieLog.Error("SpawnStartingHorde: no town or village available");
			return null;
		}

		ZombieLog.Info("  anchor=" + anchor.Name + " (" + anchor.StringId + ")");

		CampaignVec2 position = NavigationHelper.FindPointAroundPosition(
			anchor.GatePosition,
			MobileParty.NavigationType.Default,
			SpawnRadius);
		LogPosition("  starting horde spawn", position);

		CharacterObject troop = MBObjectManager.Instance.GetObject<CharacterObject>(troopId);
		if (troop == null)
		{
			ZombieLog.Error("SpawnStartingHorde: troop '" + troopId + "' not found - falling back to template roster");
			return SpawnAt(anchor, position, null, 0);
		}

		return SpawnAt(anchor, position, new Dictionary<CharacterObject, int> { { troop, troopCount } }, 0);
	}

	/// <summary>
	/// Debug spawn: a navmesh-reachable ring around the player, between 40% and 80%
	/// of the player's spotting range - far enough to watch it come at you, close
	/// enough to stay visible. A strictly "in front" point is avoided on purpose,
	/// it can land in water or somewhere unreachable.
	/// </summary>
	public static MobileParty SpawnNearPlayer(int troopCount)
	{
		ZombieLog.Info("SpawnNearPlayer: troopCount=" + troopCount);

		MobileParty player = MobileParty.MainParty;
		float sight = player.SeeingRange;
		ZombieLog.Info(string.Format("  player sight range={0:0.00}", sight));

		return SpawnNearPosition(player.Position, sight * 0.8f, sight * 0.4f, null, troopCount);
	}

	/// <summary>
	/// Bisection ladder. Every rung uses the exact same code path, position logic and
	/// post-init - only the clan and the party template change, one variable at a
	/// time. Whichever rung is the first to crash names the culprit.
	/// </summary>
	/// <param name="label">Rung name, shows up in the log.</param>
	/// <param name="useZombieClan">false = vanilla looters clan, true = our zombie clan.</param>
	/// <param name="templateId">Template to spawn with, or null for the vanilla looters template.</param>
	public static MobileParty SpawnLadderTest(string label, bool useZombieClan, string templateId)
	{
		ZombieLog.Info("=== LADDER " + label + " === clan=" + (useZombieClan ? "zombie" : "vanilla looters")
			+ " template=" + (templateId ?? "vanilla looters default"));

		Clan clan;
		if (useZombieClan)
		{
			clan = ZombieClanUtil.GetZombieClan();
			if (clan == null)
			{
				ZombieLog.Error(label + ": zombie clan not available - aborting");
				return null;
			}
		}
		else
		{
			clan = Clan.All.FirstOrDefault(c => c.StringId == "looters");
			if (clan == null)
			{
				ZombieLog.Error(label + ": vanilla looters clan not found - aborting");
				return null;
			}
		}

		ZombieLog.Info("  clan -> " + clan.StringId);

		PartyTemplateObject template;
		if (templateId != null)
		{
			template = MBObjectManager.Instance.GetObject<PartyTemplateObject>(templateId);
		}
		else
		{
			Clan vanillaLooters = Clan.All.FirstOrDefault(c => c.StringId == "looters");
			template = vanillaLooters?.DefaultPartyTemplate;
		}

		ZombieLog.Info("  template -> " + (template == null ? "NULL" : template.StringId));
		if (template == null)
		{
			ZombieLog.Error(label + ": template could not be resolved - aborting");
			return null;
		}

		MobileParty player = MobileParty.MainParty;
		float sight = player.SeeingRange;
		CampaignVec2 position = NavigationHelper.FindReachablePointAroundPosition(
			player.Position,
			MobileParty.NavigationType.Default,
			sight * 0.8f,
			sight * 0.4f);
		LogPosition("  ring spawn", position);

		Settlement anchor = FindNearestTownOrVillage(position);
		ZombieLog.Info("  anchor -> " + (anchor == null ? "NULL" : anchor.StringId));
		if (anchor == null)
		{
			ZombieLog.Error(label + ": no town or village available as home settlement - aborting");
			return null;
		}

		ZombieLog.Info("  calling CreateLooterParty...");
		MobileParty party = BanditPartyComponent.CreateLooterParty(
			"zombieplague_ladder_" + label,
			clan,
			anchor,
			isBossParty: false,
			template,
			position);
		ZombieLog.Info("  party created: " + party.StringId + ", roster=" + party.MemberRoster.TotalManCount);

		InitializeZombieParty(party);

		ZombieLog.Info(string.Format(
			"SUCCESS {0}: {1} @ ({2:0.0},{3:0.0}) strength={4}",
			label, party.StringId, party.Position.X, party.Position.Y, party.MemberRoster.TotalManCount));
		return party;
	}

	/// <summary>
	/// Battle test: a looter party and a zombie party side by side near the player,
	/// zombies outnumbering them two to one so the horde should win. Used to watch
	/// the growth loop end to end - check with zombie.list before and after.
	/// </summary>
	public static bool SpawnBattleTest(int looterCount)
	{
		int zombieCount = looterCount * 2;
		ZombieLog.Info("=== LADDER t5_battle === looters=" + looterCount + " zombies=" + zombieCount);

		Clan zombieClan = ZombieClanUtil.GetZombieClan();
		if (zombieClan == null)
		{
			return false;
		}

		Clan looterClan = Clan.All.FirstOrDefault(c => c.StringId == "looters");
		if (looterClan == null)
		{
			ZombieLog.Error("t5: vanilla looters clan not found - aborting");
			return false;
		}

		CharacterObject looterTroop = MBObjectManager.Instance.GetObject<CharacterObject>("looter");
		CharacterObject zombieTroop = MBObjectManager.Instance.GetObject<CharacterObject>(ZombieIds.TroopId(1));
		PartyTemplateObject looterTemplate = looterClan.DefaultPartyTemplate;
		PartyTemplateObject zombieTemplate = MBObjectManager.Instance.GetObject<PartyTemplateObject>(ZombieIds.PartyTemplateId);
		ZombieLog.Info("  looter troop -> " + (looterTroop == null ? "NULL" : "ok")
			+ ", zombie troop -> " + (zombieTroop == null ? "NULL" : "ok")
			+ ", looter template -> " + (looterTemplate == null ? "NULL" : looterTemplate.StringId)
			+ ", zombie template -> " + (zombieTemplate == null ? "NULL" : zombieTemplate.StringId));
		if (looterTroop == null || zombieTroop == null || looterTemplate == null || zombieTemplate == null)
		{
			ZombieLog.Error("t5: required object missing - aborting");
			return false;
		}

		MobileParty player = MobileParty.MainParty;
		float sight = player.SeeingRange;
		CampaignVec2 looterPosition = NavigationHelper.FindReachablePointAroundPosition(
			player.Position, MobileParty.NavigationType.Default, sight * 0.7f, sight * 0.4f);
		LogPosition("  looter position", looterPosition);

		// Close enough that they run into each other almost immediately.
		CampaignVec2 zombiePosition = NavigationHelper.FindReachablePointAroundPosition(
			looterPosition, MobileParty.NavigationType.Default, sight * 0.15f, sight * 0.05f);
		LogPosition("  zombie position", zombiePosition);

		Settlement anchor = FindNearestTownOrVillage(looterPosition);
		if (anchor == null)
		{
			ZombieLog.Error("t5: no town or village available as home settlement - aborting");
			return false;
		}

		MobileParty looterParty = CreatePartyWithComposition(
			"zombieplague_t5_looters", looterClan, looterTemplate, anchor, looterPosition,
			new Dictionary<CharacterObject, int> { { looterTroop, looterCount } });
		if (looterParty == null)
		{
			return false;
		}

		MobileParty zombieParty = CreatePartyWithComposition(
			"zombieplague_t5_zombies", zombieClan, zombieTemplate, anchor, zombiePosition,
			new Dictionary<CharacterObject, int> { { zombieTroop, zombieCount } });
		if (zombieParty == null)
		{
			return false;
		}

		if (!FactionManager.IsAtWarAgainstFaction(zombieClan, looterClan))
		{
			ZombieLog.Info("  declaring war between zombie clan and looters");
			FactionManager.DeclareWar(zombieClan, looterClan);
		}

		ZombieLog.Info(string.Format(
			"SUCCESS t5_battle: looters={0} ({1} men) vs zombies={2} ({3} men), distance={4:0.0}",
			looterParty.StringId, looterParty.MemberRoster.TotalManCount,
			zombieParty.StringId, zombieParty.MemberRoster.TotalManCount,
			looterParty.Position.Distance(zombieParty.Position)));
		return true;
	}

	private static MobileParty CreatePartyWithComposition(
		string stringId,
		Clan clan,
		PartyTemplateObject template,
		Settlement homeSettlement,
		CampaignVec2 position,
		Dictionary<CharacterObject, int> troops)
	{
		ZombieLog.Info("  creating " + stringId + " for clan " + clan.StringId + "...");
		MobileParty party = BanditPartyComponent.CreateLooterParty(
			stringId, clan, homeSettlement, isBossParty: false, template, position);
		ZombieLog.Info("    created " + party.StringId + ", starter roster=" + party.MemberRoster.TotalManCount);

		ApplyRequestedComposition(party, troops, 0);

		if (party.MemberRoster.TotalManCount == 0)
		{
			ZombieLog.Error("    roster EMPTY - removing party");
			DestroyPartyAction.ApplyForDisbanding(party, homeSettlement);
			return null;
		}

		InitializeZombieParty(party);
		ZombieLog.Info("    ready: " + party.StringId + " with " + party.MemberRoster.TotalManCount + " men");
		return party;
	}

	public static MobileParty SpawnNearPosition(
		CampaignVec2 origin,
		float maxDistance,
		float minDistance,
		Dictionary<CharacterObject, int> troops,
		int fallbackTroopCount)
	{
		CampaignVec2 position = NavigationHelper.FindReachablePointAroundPosition(
			origin,
			MobileParty.NavigationType.Default,
			maxDistance,
			minDistance);
		LogPosition("  ring spawn", position);

		Settlement anchor = FindNearestTownOrVillage(position);
		if (anchor == null)
		{
			ZombieLog.Error("SpawnNearPosition: no town or village available as home settlement");
			return null;
		}

		return SpawnAt(anchor, position, troops, fallbackTroopCount);
	}

	private static MobileParty SpawnAt(
		Settlement homeSettlement,
		CampaignVec2 position,
		Dictionary<CharacterObject, int> troops,
		int fallbackTroopCount)
	{
		Clan clan = ZombieClanUtil.GetZombieClan();
		if (clan == null)
		{
			ZombieLog.Error("  zombie clan not available - aborting spawn");
			return null;
		}

		// Always spawn through a real party template. With a null template the party
		// is created with an empty roster, and the engine builds the map visual for
		// it during creation - a party with no troops has no character to render,
		// which is where the native crash happened.
		PartyTemplateObject template = MBObjectManager.Instance.GetObject<PartyTemplateObject>(ZombieIds.PartyTemplateId);
		ZombieLog.Info("  template '" + ZombieIds.PartyTemplateId + "' -> " + (template == null ? "NULL" : template.StringId));
		if (template == null)
		{
			ZombieLog.Error("  party template missing - aborting spawn");
			return null;
		}

		ZombieLog.Info("  calling CreateLooterParty (home=" + homeSettlement.StringId + ")...");
		MobileParty party;
		try
		{
			party = BanditPartyComponent.CreateLooterParty(
				ZombieIds.PartyIdPrefix,
				clan,
				homeSettlement,
				isBossParty: false,
				template,
				position);
		}
		catch (Exception ex)
		{
			// The known native-crash trigger this class works around (see class
			// doc comment) - a bad position/template combo can still slip
			// through despite the checks above, so this is the last line of
			// defense before the actual creation call.
			ZombieLog.Error("  CreateLooterParty failed - aborting spawn", ex);
			return null;
		}

		ZombieLog.Info("  party created: " + party.StringId + ", starter roster=" + party.MemberRoster.TotalManCount);

		ApplyRequestedComposition(party, troops, fallbackTroopCount);

		if (party.MemberRoster.TotalManCount == 0)
		{
			ZombieLog.Error("  roster is EMPTY after fill - removing party");
			DestroyPartyAction.ApplyForDisbanding(party, homeSettlement);
			return null;
		}

		InitializeZombieParty(party);

		ZombieLog.Info(string.Format(
			"SUCCESS spawn: {0} @ ({1:0.0},{2:0.0}) strength={3}",
			party.StringId, party.Position.X, party.Position.Y, party.MemberRoster.TotalManCount));
		return party;
	}

	/// <summary>
	/// Replaces the template's starter troops with the composition the caller asked
	/// for. Order matters: the requested troops go in first and the starters are
	/// removed afterwards, so the roster never passes through zero.
	/// </summary>
	private static void ApplyRequestedComposition(MobileParty party, Dictionary<CharacterObject, int> troops, int fallbackTroopCount)
	{
		List<TroopRosterElement> starters = party.MemberRoster.GetTroopRoster().ToList();

		int added = 0;
		if (troops != null)
		{
			foreach (KeyValuePair<CharacterObject, int> entry in troops)
			{
				if (entry.Key != null && entry.Value > 0)
				{
					party.MemberRoster.AddToCounts(entry.Key, entry.Value);
					added += entry.Value;
				}
			}
		}
		else if (fallbackTroopCount > 0)
		{
			CharacterObject fallback = MBObjectManager.Instance.GetObject<CharacterObject>(ZombieIds.TroopId(1));
			ZombieLog.Info("  fallback troop lookup -> " + (fallback == null ? "NULL" : fallback.StringId));
			if (fallback != null)
			{
				party.MemberRoster.AddToCounts(fallback, fallbackTroopCount);
				added += fallbackTroopCount;
			}
		}

		if (added == 0)
		{
			// Nothing requested - keep the template's starter troops rather than
			// stripping the party bare.
			ZombieLog.Info("  no explicit composition, keeping starter roster");
			return;
		}

		foreach (TroopRosterElement element in starters)
		{
			party.MemberRoster.AddToCounts(element.Character, -element.Number);
		}

		ZombieLog.Info("  composition applied: added=" + added + ", final=" + party.MemberRoster.TotalManCount);
	}

	/// <summary>
	/// Mirrors BanditSpawnCampaignBehavior.InitializeBanditParty. Vanilla runs all of
	/// this immediately after creating a bandit party; skipping it leaves the party
	/// without a visual refresh, without trade data and without an AI order.
	/// </summary>
	private static void InitializeZombieParty(MobileParty party)
	{
		ZombieLog.Info("  init: SetVisualAsDirty");
		party.Party.SetVisualAsDirty();

		party.Aggressiveness = 1f - 0.2f * MBRandom.RandomFloat;
		party.InitializePartyTrade(10 * party.MemberRoster.TotalManCount);

		ZombieLog.Info("  init: patrol order");
		party.SetMovePatrolAroundPoint(party.Position, MobileParty.NavigationType.Default);

		// Vanilla bandit AI (AiLandBanditPatrollingBehavior) re-anchors an IsBandit
		// party's patrol to its HomeSettlement every in-game hour - that is why a
		// zombie horde would otherwise just sit at the town/village it spawned near
		// forever instead of roaming or following through on a raid/siege order.
		// Disabling vanilla decision-making hands movement entirely to
		// ZombiePlagueCampaignBehavior's own hourly tick.
		party.Ai.SetDoNotMakeNewDecisions(true);
	}

	/// <summary>
	/// A fresh reachable point to wander towards, centred on the party's current
	/// position rather than any settlement - this is what lets a horde drift across
	/// the map instead of orbiting its spawn point.
	///
	/// Validated before returning: FindReachablePointAroundPosition can come back
	/// with an invalid point when the search annulus around `origin` has no
	/// reachable navmesh face at all (map edge, deep water, steep terrain) - a
	/// crash this mod has fought before (see the class doc comment). Handing an
	/// invalid point straight to a movement call is a confirmed native-crash
	/// cause (see ZombiePlagueCampaignBehavior's TrySplitZombieParty crash), so
	/// this falls back to `origin` itself - a no-op move, never invalid - rather
	/// than ever return something a caller might not check.
	/// </summary>
	public static CampaignVec2 FindWanderPoint(CampaignVec2 origin, float maxDistance)
	{
		CampaignVec2 point = NavigationHelper.FindReachablePointAroundPosition(
			origin, MobileParty.NavigationType.Default, maxDistance, maxDistance * 0.2f);

		if (!point.IsValid())
		{
			ZombieLog.Error(string.Format(
				"FindWanderPoint: navmesh search around ({0:0.0},{1:0.0}) radius {2:0.0} returned an invalid point - falling back to origin",
				origin.X, origin.Y, maxDistance));
			return origin;
		}

		return point;
	}

	/// <summary>
	/// A reachable point roughly `distance` away from `origin`, in the direction
	/// pointing away from `subjectToAvoid` - used to flee a threat or to send a
	/// wandering/split-off party away from a settlement or its parent, instead of
	/// FindWanderPoint's undirected random ring. Validated the same way and for
	/// the same reason as FindWanderPoint above.
	/// </summary>
	public static CampaignVec2 FindPointAwayFrom(CampaignVec2 origin, CampaignVec2 subjectToAvoid, float distance)
	{
		CampaignVec2 direction = CampaignVec2.Normalized(origin - subjectToAvoid);
		if (!direction.IsNonZero())
		{
			// origin and subjectToAvoid coincide - any direction will do.
			direction = new CampaignVec2(new TaleWorlds.Library.Vec2(1f, 0f), true);
		}

		CampaignVec2 target = origin + direction * distance;
		CampaignVec2 point = NavigationHelper.FindReachablePointAroundPosition(
			target, MobileParty.NavigationType.Default, distance * 0.25f, 0f);

		if (!point.IsValid())
		{
			ZombieLog.Error(string.Format(
				"FindPointAwayFrom: navmesh search around ({0:0.0},{1:0.0}) returned an invalid point - falling back to origin",
				target.X, target.Y));
			return origin;
		}

		return point;
	}

	private static Settlement PickRandomTownOrVillage()
	{
		List<Settlement> candidates = Settlement.All.Where(s => s.IsTown || s.IsVillage).ToList();
		if (candidates.Count == 0)
		{
			return null;
		}

		return candidates[MBRandom.RandomInt(0, candidates.Count)];
	}

	public static Settlement FindNearestTownOrVillage(CampaignVec2 position)
	{
		Settlement nearest = null;
		float best = float.MaxValue;
		foreach (Settlement settlement in Settlement.All)
		{
			if (!settlement.IsTown && !settlement.IsVillage)
			{
				continue;
			}

			float distance = settlement.GatePosition.Distance(position);
			if (distance < best)
			{
				best = distance;
				nearest = settlement;
			}
		}

		return nearest;
	}

	private static void LogPosition(string label, CampaignVec2 position)
	{
		ZombieLog.Info(string.Format(
			"{0}: ({1:0.0},{2:0.0}) onLand={3} valid={4}",
			label,
			position.X,
			position.Y,
			position.IsOnLand,
			NavigationHelper.IsPositionValidForNavigationType(position, MobileParty.NavigationType.Default)));
	}
}
