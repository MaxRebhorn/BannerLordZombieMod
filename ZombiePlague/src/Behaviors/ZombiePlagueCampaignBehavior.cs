using System;
using System.Collections.Generic;
using System.Linq;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.MountAndBlade;
using TaleWorlds.ObjectSystem;
using ZombiePlague.Infrastructure;

namespace ZombiePlague.Behaviors;

public sealed class ZombiePlagueCampaignBehavior : CampaignBehaviorBase
{
	private float _debugTimer;

	// Transient, battle-scoped state. Does not need to survive a save: a
	// MapEventStarted/MapEventEnded/MobilePartyDestroyed sequence always
	// completes within the same session it started in.
	private readonly Dictionary<MobileParty, Dictionary<CharacterObject, int>> _prisonSnapshots = new();
	private readonly Dictionary<string, Dictionary<CharacterObject, int>> _pendingRespawnTallies = new();

	// Also transient/session-only, same reasoning: just throttles how often a
	// wandering party picks a new random destination.
	private readonly Dictionary<MobileParty, CampaignTime> _nextWanderRoll = new();

	// TODO.md Feature 2 commitment: the hunt/raid target each zombie party
	// is currently committed to, so SelectBestAction can apply the stickiness
	// bonus and avoid flip-flopping between near-equal targets every hour.
	// Transient/session-only like the dictionaries above.
	private readonly Dictionary<MobileParty, ActionCandidate> _currentAction = new();

	// Stuck-party watchdog: position/streak tracked each hourly tick.
	private readonly Dictionary<MobileParty, CampaignVec2> _lastPosition = new();
	private readonly Dictionary<MobileParty, float> _stuckHours = new();

	// Siege debugging (TODO.md Phase 3 is still misbehaving post-crash-fix):
	// which settlement each zombie party was besieging as of the last hourly
	// tick, so LogSiegeStatus can print a start/end line on the transition
	// instead of just a wall of per-hour status spam. Transient/session-only.
	private readonly Dictionary<MobileParty, Settlement> _lastBesiegedSettlement = new();

	// Frustrated-raider tracking: timestamps of hunts this party lost (target
	// escaped rather than died) within the trailing window, and which village
	// it is currently pillaging opportunistically because of that, if any.
	private readonly Dictionary<MobileParty, List<CampaignTime>> _huntAbandonTimestamps = new();
	private readonly Dictionary<MobileParty, Settlement> _opportunisticRaidTarget = new();

	// Starting-horde spawn (see TrySpawnStartingHordes) and event-notification
	// "already fired" flags - all persisted via SyncData so a save/load doesn't
	// re-spawn or re-notify.
	private CampaignTime _newGameStartTime;
	private bool _startingHordeSpawned;
	private bool _firstSpawnNotified;
	private bool _mediumHordeNotified;
	private bool _largeHordeNotified;
	private bool _wipeoutNotified;

	public override void RegisterEvents()
	{
		CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
		CampaignEvents.OnNewGameCreatedEvent.AddNonSerializedListener(this, OnNewGameCreated);
		CampaignEvents.MapEventStarted.AddNonSerializedListener(this, OnMapEventStarted);
		CampaignEvents.MapEventEnded.AddNonSerializedListener(this, OnMapEventEnded);
		CampaignEvents.MobilePartyDestroyed.AddNonSerializedListener(this, OnMobilePartyDestroyed);
		CampaignEvents.TickEvent.AddNonSerializedListener(this, OnTick);
		CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, OnHourlyTick);
		CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, OnDailyTick);
	}

	private void OnDailyTick()
	{
		TrySpawnStartingHordes();

		foreach (MobileParty party in MobileParty.AllBanditParties.ToList())
		{
			if (ZombieClanUtil.IsZombieParty(party))
			{
				TrySplitZombieParty(party);
				ReinforceIfWeak(party);
			}
		}
	}

	/// <summary>
	/// Balance: a straggler party at or under ZombieBehaviorConfig.
	/// WeakPartyReinforceMaxTroops gains one Patient Zero every day, so a tiny
	/// split-off or the survivors of a lost fight has a chance to claw back up
	/// instead of just wandering until something finishes it off.
	/// </summary>
	private static void ReinforceIfWeak(MobileParty party)
	{
		if (party.MemberRoster.TotalManCount > ZombieBehaviorConfig.WeakPartyReinforceMaxTroops)
		{
			return;
		}

		CharacterObject patientZero = MBObjectManager.Instance.GetObject<CharacterObject>(ZombieIds.PatientZeroTroopId);
		if (patientZero == null)
		{
			return;
		}

		party.MemberRoster.AddToCounts(patientZero, 1);
		ZombieLog.Info("ReinforceIfWeak: " + party.StringId + " was at " + (party.MemberRoster.TotalManCount - 1)
			+ " troops - added 1 Patient Zero (now " + party.MemberRoster.TotalManCount + ")");
	}

	/// <summary>
	/// Replaces the old "spawn immediately on new game" behavior: waits
	/// ZombieBehaviorConfig.SpawnDelayDays after the campaign starts, then
	/// spawns ZombieBehaviorConfig.NumberOfStartingParties hordes (each
	/// ZombieBehaviorConfig.StartingGroupSize troops of
	/// ZombieBehaviorConfig.StartingTroopTypeIndex) at random towns/villages.
	/// Runs once per campaign - see _startingHordeSpawned/SyncData.
	/// </summary>
	private void TrySpawnStartingHordes()
	{
		if (_startingHordeSpawned || !ZombieIds.AutoSpawnOnNewGame)
		{
			return;
		}

		if (CampaignTime.Now < _newGameStartTime + CampaignTime.Days(ZombieBehaviorConfig.SpawnDelayDays))
		{
			return;
		}

		_startingHordeSpawned = true;

		string troopId = ZombieIds.StartingTroopId(ZombieBehaviorConfig.StartingTroopTypeIndex);
		int partyCount = Math.Max(1, ZombieBehaviorConfig.NumberOfStartingParties);
		ZombieLog.Info("TrySpawnStartingHordes: spawning " + partyCount + " starting horde(s), troop=" + troopId
			+ ", size=" + ZombieBehaviorConfig.StartingGroupSize);

		for (int i = 0; i < partyCount; i++)
		{
			try
			{
				ZombieSpawner.SpawnStartingHorde(troopId, ZombieBehaviorConfig.StartingGroupSize);
			}
			catch (Exception exception)
			{
				ZombieLog.Error("TrySpawnStartingHordes: spawn failed", exception);
			}
		}
	}

	/// <summary>
	/// Four one-shot(ish) player-facing notifications, per the quality-of-life
	/// request: first spawn, first medium horde, first large horde, complete
	/// wipeout. "Medium"/"large" are evaluated per individual zombie party (the
	/// same per-party troop count GetStrategicWeight's brackets use), not the
	/// plague's total population. Wipeout re-arms if the plague ever comes back
	/// (respawn after total defeat, or a fresh manual spawn) so a second wipeout
	/// still gets its own notification.
	/// </summary>
	private void CheckHordeMilestones()
	{
		List<MobileParty> zombieParties = MobileParty.AllBanditParties
			.Where(ZombieClanUtil.IsZombieParty)
			.ToList();

		if (!_firstSpawnNotified && zombieParties.Count > 0)
		{
			_firstSpawnNotified = true;
			ShowZombieEventMessage("The dead are rising! A zombie horde has appeared.");
		}

		if (!_mediumHordeNotified && zombieParties.Any(p => p.MemberRoster.TotalManCount >= ZombieBehaviorConfig.MediumHordeThreshold))
		{
			_mediumHordeNotified = true;
			ShowZombieEventMessage("A zombie horde has grown to a fearsome size.");
		}

		if (!_largeHordeNotified && zombieParties.Any(p => p.MemberRoster.TotalManCount >= ZombieBehaviorConfig.LargeHordeThreshold))
		{
			_largeHordeNotified = true;
			ShowZombieEventMessage("A massive zombie horde now roams the land!");
		}

		if (zombieParties.Count == 0)
		{
			if (_firstSpawnNotified && !_wipeoutNotified)
			{
				_wipeoutNotified = true;
				ShowZombieEventMessage("The zombie plague has been wiped out. No zombie parties remain.");
			}
		}
		else
		{
			_wipeoutNotified = false;
		}
	}

	private static void ShowZombieEventMessage(string text)
	{
		InformationManager.DisplayMessage(new InformationMessage("[ZombiePlague] " + text));
	}

	/// <summary>
	/// Zombie parties have vanilla AI decision-making turned off entirely (see
	/// ZombieSpawner.InitializeZombieParty) because the stock bandit AI just
	/// re-anchors patrol to the party's home settlement every hour. This hourly
	/// tick is the replacement, checked every in-game hour.
	///
	/// TODO.md Feature 1: hordes under YoungHordeMaxTroops run a separate,
	/// survival-first branch (HandleYoungHorde) instead of the logic below.
	///
	/// TODO.md Feature 2: everyone else compares hunt/raid candidates
	/// against each other with a single utility score (SelectBestAction) instead
	/// of a fixed priority ladder, and wanders if nothing scores above zero.
	/// </summary>
	private void OnHourlyTick()
	{
		foreach (MobileParty party in MobileParty.AllBanditParties.ToList())
		{
			if (!ZombieClanUtil.IsZombieParty(party))
			{
				continue;
			}

			try
			{
				UpdateZombieBehavior(party);
			}
			catch (Exception exception)
			{
				ZombieLog.Error("OnHourlyTick: behavior update failed for " + party.StringId, exception);
			}

			try
			{
				LogSiegeStatus(party);
			}
			catch (Exception exception)
			{
				ZombieLog.Error("OnHourlyTick: LogSiegeStatus failed for " + party.StringId, exception);
			}
		}

		CheckHordeMilestones();
	}

	/// <summary>
	/// Siege debugging (TODO.md Phase 3): sieges still misbehave after the
	/// crash fix, so every hour a besieging zombie party gets a status line -
	/// troop count, defender strength, wall/assault state - plus explicit
	/// start/end lines on the transition, so a full siege's log makes it
	/// possible to see what actually happened without reproducing it live.
	/// Remove or gate behind a debug flag once the remaining bugs are found.
	/// </summary>
	private void LogSiegeStatus(MobileParty party)
	{
		Settlement current = party.BesiegedSettlement;
		_lastBesiegedSettlement.TryGetValue(party, out Settlement previous);

		if (current == previous)
		{
			if (current != null)
			{
				LogSiegeStatusLine(party, current, "SiegeStatus");
			}

			return;
		}

		if (previous != null)
		{
			ZombieLog.Info("SiegeStatus: " + party.StringId + " STOPPED besieging " + previous.StringId
				+ " (now: " + party.DefaultBehavior + ", troops=" + party.MemberRoster.TotalManCount + ")");
		}

		if (current != null)
		{
			LogSiegeStatusLine(party, current, "SiegeStatus: " + party.StringId + " STARTED besieging " + current.StringId + " -");
			_lastBesiegedSettlement[party] = current;
		}
		else
		{
			_lastBesiegedSettlement.Remove(party);
		}
	}

	private static void LogSiegeStatusLine(MobileParty party, Settlement settlement, string label)
	{
		int myTroops = party.MemberRoster.TotalManCount;
		int defenders = GetGarrisonAndMilitia(settlement);
		ZombieLog.Info(label + " " + party.StringId + " vs " + settlement.StringId
			+ " | zombies=" + myTroops
			+ " | defenders(garrison+militia)=" + defenders
			+ " | wallHP=" + settlement.SettlementHitPoints.ToString("0.00")
			+ " | inAssault=" + (party.MapEvent != null)
			+ " | defaultBehavior=" + party.DefaultBehavior
			+ " | shortTermBehavior=" + party.ShortTermBehavior
			+ " | leaderHero=" + (party.LeaderHero?.Name.ToString() ?? "NULL")
			+ " | partyComponent=" + party.PartyComponent.GetType().Name);
	}

	private void UpdateZombieBehavior(MobileParty party)
	{
		// Active in a battle or an ongoing raid map event - do not interrupt.
		if (party.MapEvent != null)
		{
			return;
		}

		if (CheckAndHandleStuckParty(party))
		{
			return;
		}

		int total = party.MemberRoster.TotalManCount;

		if (TryHandleActiveSiege(party, total))
		{
			return;
		}

		if (total < ZombieBehaviorConfig.YoungHordeMaxTroops)
		{
			HandleYoungHorde(party);
			return;
		}

		if (TryHandleFrustratedRaid(party, total))
		{
			return;
		}

		_currentAction.TryGetValue(party, out ActionCandidate current);
		RecordHuntAbandonmentIfAny(party, current);

		ActionCandidate best = SelectBestAction(party, current);
		if (best == null)
		{
			_currentAction.Remove(party);
			Wander(party);
			return;
		}

		_currentAction[party] = best;
		ExecuteAction(party, best);
	}

	/// <summary>
	/// Bug-prevention watchdog: if a party has been stuck within
	/// StuckMovementThreshold map units of itself for StuckThresholdHours in a
	/// row - a navmesh/order problem this file's Wander comment already flags as
	/// having happened once - force it to a fresh, distant point and clear its
	/// AI commitment so the behavior tree starts over cleanly from there.
	/// Returns true if it just issued that escape order (caller should not run
	/// any other logic this tick).
	/// </summary>
	private bool CheckAndHandleStuckParty(MobileParty party)
	{
		// A besieging party is *supposed* to sit at its camp position for as
		// long as the siege lasts - real sieges run well past
		// StuckThresholdHours, and without this exemption the watchdog was
		// force-escaping every siege that outlasted 10 hours, indistinguishable
		// from an actual navmesh-stuck party. Keep the counter zeroed the whole
		// time so leaving the siege later doesn't immediately look "stuck" from
		// hours banked while it was deliberately stationary.
		if (party.BesiegedSettlement != null)
		{
			_stuckHours[party] = 0f;
			_lastPosition[party] = party.Position;
			return false;
		}

		CampaignVec2 currentPosition = party.Position;

		if (_lastPosition.TryGetValue(party, out CampaignVec2 lastPosition)
			&& currentPosition.Distance(lastPosition) < ZombieBehaviorConfig.StuckMovementThreshold)
		{
			_stuckHours.TryGetValue(party, out float hours);
			_stuckHours[party] = hours + 1f;
		}
		else
		{
			_stuckHours[party] = 0f;
		}

		_lastPosition[party] = currentPosition;

		if (_stuckHours[party] < ZombieBehaviorConfig.StuckThresholdHours)
		{
			return false;
		}

		ZombieLog.Info("CheckAndHandleStuckParty: " + party.StringId + " stuck for " + _stuckHours[party]
			+ "h - forcing escape and restarting behavior");

		float radius = ZombieSpawner.SpawnRadius * ZombieBehaviorConfig.StuckEscapeDistanceFactor;
		CampaignVec2 escapePoint = ZombieSpawner.FindWanderPoint(party.Position, radius);
		party.SetMoveGoToPoint(escapePoint, MobileParty.NavigationType.Default);

		// Restart the behavior tree fresh from the new spot.
		_currentAction.Remove(party);
		_opportunisticRaidTarget.Remove(party);
		_nextWanderRoll.Remove(party);
		_stuckHours[party] = 0f;
		_lastPosition[party] = escapePoint;

		return true;
	}

	/// <summary>
	/// TODO.md Phase 3 bug fix: an actively-besieging party (BesiegedSettlement
	/// != null) used to go straight back through the generic hunt/raid/siege
	/// utility scoring every hour, same as any other party. Since hunts and
	/// sieges are scored on the same scale, any hostile party that happened to
	/// out-score the siege for a single hour would pull the horde off the
	/// siege to go fight it (real troop losses, confirmed in testing), and the
	/// moment that fight ended the horde had no memory of which settlement it
	/// had been besieging - it just re-rolled from scratch among every
	/// still-valid siege candidate, which is what caused the observed
	/// flip-flopping between adjacent settlements with near-identical scores.
	///
	/// This intercepts the decision entirely while a siege is actually under
	/// way: keep besieging unless the numbers have turned (matching the same
	/// threshold used to start the siege) or a genuinely dangerous force is
	/// closing in - never for merely opportunistic prey. Returns true if the
	/// tick's behavior was fully handled here.
	/// </summary>
	private bool TryHandleActiveSiege(MobileParty party, int total)
	{
		Settlement settlement = party.BesiegedSettlement;
		if (settlement == null)
		{
			return false;
		}

		int defenders = GetGarrisonAndMilitia(settlement);
		if (total < ZombieBehaviorConfig.SettlementSiegeTroopThreshold
			|| total < ZombieBehaviorConfig.SiegeStrengthMultiplier * defenders)
		{
			ZombieLog.Info("TryHandleActiveSiege: " + party.StringId + " abandoning siege of " + settlement.StringId
				+ " - no longer strong enough (" + total + " vs " + defenders + " defenders)");
			_currentAction.Remove(party);
			return false;
		}

		MobileParty threat = FindNearbyHostileParty(party, settlement.GatePosition, ZombieBehaviorConfig.InterceptionRadius);
		if (threat != null && threat.MemberRoster.TotalManCount >= total * ZombieBehaviorConfig.RaidBlockMinThreatRatio)
		{
			ZombieLog.Info("TryHandleActiveSiege: " + party.StringId + " -> threat " + threat.StringId
				+ " nearby, aborting siege of " + settlement.StringId + " and fleeing");
			_currentAction.Remove(party);
			FleeFrom(party, threat);
			return true;
		}

		if (party.TargetSettlement != settlement || party.DefaultBehavior != AiBehavior.BesiegeSettlement)
		{
			ZombieLog.Info("TryHandleActiveSiege: " + party.StringId + " re-affirming siege of " + settlement.StringId);
			SetPartyAiAction.GetActionForBesiegingSettlement(party, settlement, MobileParty.NavigationType.Default, isFromPort: false);
		}

		_currentAction[party] = new ActionCandidate { Type = ZombieActionType.SiegeSettlement, TargetSettlement = settlement };
		return true;
	}

	/// <summary>
	/// A horde big enough to matter (FrustrationTroopThreshold+) that keeps
	/// losing hunts rather than finishing them gives up chasing for a while and
	/// raids the nearest healthy village instead - as long as nothing hostile is
	/// nearby. Returns true if this tick's behavior was handled here (either
	/// continuing/starting an opportunistic raid, or fleeing one that was just
	/// interrupted by a threat).
	/// </summary>
	private bool TryHandleFrustratedRaid(MobileParty party, int total)
	{
		if (total < ZombieBehaviorConfig.FrustrationTroopThreshold)
		{
			_opportunisticRaidTarget.Remove(party);
			return false;
		}

		if (_opportunisticRaidTarget.TryGetValue(party, out Settlement target))
		{
			bool stillValid = target.IsVillage && target.Village.VillageState == Village.VillageStates.Normal;
			MobileParty threat = stillValid
				? FindNearbyHostileParty(party, party.Position, ZombieBehaviorConfig.FrustrationRaidCancelRadius)
				: null;

			if (stillValid && threat == null)
			{
				if (party.TargetSettlement != target || party.DefaultBehavior != AiBehavior.RaidSettlement)
				{
					ZombieLog.Info("TryHandleFrustratedRaid: " + party.StringId + " -> opportunistic raid " + target.StringId);
					SetPartyAiAction.GetActionForRaidingSettlement(party, target, MobileParty.NavigationType.Default, isFromPort: false, isTargetingPort: false);
				}

				return true;
			}

			_opportunisticRaidTarget.Remove(party);
			if (threat != null)
			{
				ZombieLog.Info("TryHandleFrustratedRaid: " + party.StringId + " -> threat " + threat.StringId + " nearby, aborting raid and fleeing");
				_huntAbandonTimestamps.Remove(party);
				FleeFrom(party, threat);
				return true;
			}

			return false;
		}

		PruneOldAbandonments(party);
		if (!_huntAbandonTimestamps.TryGetValue(party, out List<CampaignTime> timestamps)
			|| timestamps.Count <= ZombieBehaviorConfig.FrustrationAbandonThreshold)
		{
			return false;
		}

		Settlement candidate = FindNearestSettlement(party.Position, s => s.IsVillage && s.Village.VillageState == Village.VillageStates.Normal);
		if (candidate == null || FindNearbyHostileParty(party, party.Position, ZombieBehaviorConfig.FrustrationRaidCancelRadius) != null)
		{
			return false;
		}

		ZombieLog.Info("TryHandleFrustratedRaid: " + party.StringId + " frustrated (" + timestamps.Count
			+ " failed chases in " + ZombieBehaviorConfig.FrustrationWindowDays + "d) -> opportunistic raid on " + candidate.StringId);

		_opportunisticRaidTarget[party] = candidate;
		_currentAction.Remove(party);
		timestamps.Clear();
		SetPartyAiAction.GetActionForRaidingSettlement(party, candidate, MobileParty.NavigationType.Default, isFromPort: false, isTargetingPort: false);
		return true;
	}

	/// <summary>
	/// Records a "lost the chase" event when the party's previous commitment was
	/// a hunt whose target is still alive but no longer reachable/huntable (fled
	/// into a settlement, left the spawn radius, made peace, etc.) - as opposed
	/// to the target having simply died in battle, which is not a failure.
	/// </summary>
	private void RecordHuntAbandonmentIfAny(MobileParty party, ActionCandidate previous)
	{
		if (previous == null || previous.Type != ZombieActionType.Hunt || previous.TargetParty == null)
		{
			return;
		}

		MobileParty target = previous.TargetParty;
		bool targetAlive = target.MemberRoster.TotalManCount > 0;
		bool stillHuntable = targetAlive
			&& IsHuntableParty(party, target)
			&& target.Position.Distance(party.Position) <= ZombieSpawner.SpawnRadius;

		if (!targetAlive || stillHuntable)
		{
			return;
		}

		if (!_huntAbandonTimestamps.TryGetValue(party, out List<CampaignTime> timestamps))
		{
			timestamps = new List<CampaignTime>();
			_huntAbandonTimestamps[party] = timestamps;
		}

		timestamps.Add(CampaignTime.Now);
		ZombieLog.Info("RecordHuntAbandonmentIfAny: " + party.StringId + " lost " + target.StringId
			+ " (" + timestamps.Count + " in window)");
	}

	/// <summary>Drops abandonment timestamps older than FrustrationWindowDays - a sliding, not calendar, window.</summary>
	private void PruneOldAbandonments(MobileParty party)
	{
		if (!_huntAbandonTimestamps.TryGetValue(party, out List<CampaignTime> timestamps))
		{
			return;
		}

		CampaignTime cutoff = CampaignTime.DaysFromNow(-ZombieBehaviorConfig.FrustrationWindowDays);
		timestamps.RemoveAll(t => t < cutoff);
	}

	/// <summary>
	/// TODO.md Feature 1: a freshly spawned horde (under YoungHordeMaxTroops) is
	/// too weak to survive the adult scoring logic's assumptions - it needs to
	/// actively avoid overwhelming threats rather than just skip targets it can't
	/// beat. Priority: flee a nearby crushing threat, else hunt only very weak
	/// prey with a tightened search radius, else wander somewhere quiet.
	///
	/// The evasion/chase speed buff mentioned in TODO.md Feature 1 ("see Feature
	/// 8") is applied separately, unconditionally on troop count alone rather
	/// than through this method - see ZombieSpeedBuffPatch, which also adds a
	/// separate chase-speed bonus while any zombie party (young or adult) is
	/// actively engaging a hunt target.
	/// </summary>
	private void HandleYoungHorde(MobileParty party)
	{
		int myTroops = party.MemberRoster.TotalManCount;

		MobileParty threat = FindFleeThreat(party, myTroops);
		if (threat != null)
		{
			ZombieLog.Info("HandleYoungHorde: " + party.StringId + " (" + myTroops + ") -> flee " + threat.StringId
				+ " (" + threat.MemberRoster.TotalManCount + " Mann)");
			FleeFrom(party, threat);
			return;
		}

		MobileParty prey = FindYoungHordeHuntTarget(party, myTroops);
		if (prey != null)
		{
			EngageParty(party, prey, myTroops);
			return;
		}

		Wander(party, awayFrom: FindNearestSettlement(party.Position).GatePosition);
	}

	/// <summary>
	/// A hostile party close enough and strong enough to wipe out a young horde
	/// outright. Only close-range threats count (YoungFleeThreatRadius) - anything
	/// farther away is not an imminent threat this hour.
	/// </summary>
	private static MobileParty FindFleeThreat(MobileParty party, int myTroops)
	{
		MobileParty worst = null;
		int worstTroops = -1;

		foreach (MobileParty candidate in MobileParty.All)
		{
			if (candidate == party || candidate.MemberRoster.TotalManCount <= 0)
			{
				continue;
			}

			if (candidate.MapEvent != null || candidate.CurrentSettlement != null)
			{
				continue;
			}

			if (!FactionManager.IsAtWarAgainstFaction(party.MapFaction, candidate.MapFaction))
			{
				continue;
			}

			if (candidate.Position.Distance(party.Position) > ZombieBehaviorConfig.YoungFleeThreatRadius)
			{
				continue;
			}

			int candidateTroops = candidate.MemberRoster.TotalManCount;
			if (candidateTroops <= myTroops * ZombieBehaviorConfig.YoungHordeFleeThreatMultiplier)
			{
				continue;
			}

			if (candidateTroops > worstTroops)
			{
				worst = candidate;
				worstTroops = candidateTroops;
			}
		}

		return worst;
	}

	/// <summary>Same shape as adult hunting, but capped to very weak prey within a tightened radius.</summary>
	private static MobileParty FindYoungHordeHuntTarget(MobileParty party, int myTroops)
	{
		float radius = ZombieSpawner.SpawnRadius * ZombieBehaviorConfig.YoungHuntRadiusMultiplier;

		MobileParty best = null;
		float bestScore = -1f;

		foreach (MobileParty candidate in MobileParty.All)
		{
			if (!IsHuntableParty(party, candidate))
			{
				continue;
			}

			int targetTroops = candidate.MemberRoster.TotalManCount;
			if (targetTroops > ZombieBehaviorConfig.YoungHordeMaxHuntTroops)
			{
				continue;
			}

			float distance = candidate.Position.Distance(party.Position);
			if (distance > radius)
			{
				continue;
			}

			float ratio = myTroops / (float)targetTroops;
			if (ratio < ZombieBehaviorConfig.GetMinStrengthRatio(myTroops))
			{
				continue;
			}

			float score = targetTroops / (distance + ZombieBehaviorConfig.HuntScoreDistanceSmoothing);
			if (score > bestScore)
			{
				best = candidate;
				bestScore = score;
			}
		}

		return best;
	}

	/// <summary>
	/// A party is only worth engaging/hunting if it is actually out on the open
	/// map - something sitting inside a settlement (visiting a town, a lord's own
	/// garrison hero, etc.) cannot be "engaged" the way EngageParty expects, and
	/// something already fighting is someone else's business this hour.
	/// </summary>
	private static bool IsHuntableParty(MobileParty hunter, MobileParty candidate)
	{
		if (candidate == hunter || candidate.MemberRoster.TotalManCount <= 0)
		{
			return false;
		}

		if (candidate.MapEvent != null || candidate.CurrentSettlement != null)
		{
			return false;
		}

		if (ZombieClanUtil.IsZombieParty(candidate))
		{
			return false;
		}

		return FactionManager.IsAtWarAgainstFaction(hunter.MapFaction, candidate.MapFaction);
	}

	private static void EngageParty(MobileParty party, MobileParty prey, int myTroops)
	{
		if (party.DefaultBehavior != AiBehavior.EngageParty || party.TargetParty != prey)
		{
			ZombieLog.Info("UpdateZombieBehavior: " + party.StringId + " (" + myTroops + ") -> hunt " + prey.StringId
				+ " (" + prey.MemberRoster.TotalManCount + " Mann)");
			SetPartyAiAction.GetActionForEngagingParty(party, prey, MobileParty.NavigationType.Default, isFromPort: false);
		}
	}

	/// <summary>Moves a young horde directly away from an imminent threat, navmesh-validated near the flee point.</summary>
	private void FleeFrom(MobileParty party, MobileParty threat)
	{
		CampaignVec2 fleePoint = ZombieSpawner.FindPointAwayFrom(party.Position, threat.Position, ZombieBehaviorConfig.YoungHordeFleeDistance);
		party.SetMoveGoToPoint(fleePoint, MobileParty.NavigationType.Default);
		_nextWanderRoll[party] = CampaignTime.HoursFromNow(ZombieBehaviorConfig.WanderRerollIntervalHours);
	}

	/// <summary>
	/// TODO.md Feature 2: gathers every valid hunt/raid candidate within range,
	/// scores each with a single utility formula, and returns the best one - or
	/// null if nothing scores above zero, in which case the caller wanders
	/// instead. Replaces the old fixed hunt -> siege -> raid ladder (siege
	/// itself was later removed entirely - see the ZombieActionType doc comment).
	///
	/// Commitment: if `current` (the party's action from the previous tick) is
	/// still among the valid candidates, its score is boosted by
	/// CommitmentBonusMultiplier before ranking, and even a challenger that wins
	/// that boosted ranking must still beat the incumbent's raw score by
	/// CommitmentRequiredScoreIncrease to actually replace it. Both default to
	/// the same "50% better" bar but are independently tunable in
	/// ZombieBehaviorConfig - this is what stops a horde from flip-flopping
	/// between two near-equal targets every hour.
	/// </summary>
	private static ActionCandidate SelectBestAction(MobileParty party, ActionCandidate current)
	{
		ActionCandidate best = null;
		ActionCandidate currentMatch = null;
		float currentRawScore = 0f;

		foreach (ActionCandidate candidate in GetCandidates(party))
		{
			if (!IsCandidateValid(party, candidate))
			{
				continue;
			}

			float rawScore = CalculateScore(party, candidate);
			if (rawScore <= 0f)
			{
				continue;
			}

			candidate.Score = rawScore;

			if (current != null && IsSameTarget(candidate, current))
			{
				currentMatch = candidate;
				currentRawScore = rawScore;
				candidate.Score = rawScore * ZombieBehaviorConfig.CommitmentBonusMultiplier;
			}

			if (best == null || candidate.Score > best.Score)
			{
				best = candidate;
			}
		}

		if (currentMatch != null && best != currentMatch
			&& best.Score < currentRawScore * ZombieBehaviorConfig.CommitmentRequiredScoreIncrease)
		{
			return currentMatch;
		}

		return best;
	}

	/// <summary>Whether two candidates refer to the same hunt prey / raid village.</summary>
	private static bool IsSameTarget(ActionCandidate a, ActionCandidate b)
	{
		if (a.Type != b.Type)
		{
			return false;
		}

		return a.Type == ZombieActionType.Hunt ? a.TargetParty == b.TargetParty : a.TargetSettlement == b.TargetSettlement;
	}

	private static IEnumerable<ActionCandidate> GetCandidates(MobileParty party)
	{
		int myTroops = party.MemberRoster.TotalManCount;
		float radius = ZombieSpawner.SpawnRadius;
		if (myTroops >= ZombieBehaviorConfig.GreedyGrowthTroopThreshold)
		{
			radius *= ZombieBehaviorConfig.GreedySearchRadiusMultiplier;
		}

		foreach (MobileParty candidate in MobileParty.All)
		{
			if (!IsHuntableParty(party, candidate))
			{
				continue;
			}

			if (candidate.Position.Distance(party.Position) > radius)
			{
				continue;
			}

			yield return new ActionCandidate { Type = ZombieActionType.Hunt, TargetParty = candidate };
		}

		bool canSiege = !ZombieBehaviorConfig.SiegeDisabledPendingInvestigation && HasTurnedHero(party);
		foreach (Settlement settlement in Settlement.All)
		{
			if (settlement.IsVillage && settlement.Village.VillageState == Village.VillageStates.Normal)
			{
				yield return new ActionCandidate { Type = ZombieActionType.RaidVillage, TargetSettlement = settlement };
			}
			else if (canSiege && (settlement.IsTown || settlement.IsCastle) && !settlement.IsUnderSiege)
			{
				yield return new ActionCandidate { Type = ZombieActionType.SiegeSettlement, TargetSettlement = settlement };
			}
		}
	}

	/// <summary>
	/// TODO.md Phase 3: siege is only ever offered to a horde with a real
	/// LeaderHero - BesiegerCamp/the siege strategy system read LeaderHero
	/// directly at multiple points, and a heroless horde hitting one of those
	/// paths is what crashed/hung the game before siege was removed entirely.
	/// A hero merely present in the roster (not leading) does not count, since
	/// it is specifically LeaderHero those systems need.
	/// </summary>
	private static bool HasTurnedHero(MobileParty party)
	{
		return party.LeaderHero != null;
	}

	/// <summary>Hard constraints (TODO.md Features 3 and 5) applied before any candidate is scored.</summary>
	private static bool IsCandidateValid(MobileParty party, ActionCandidate candidate)
	{
		int myTroops = party.MemberRoster.TotalManCount;

		switch (candidate.Type)
		{
			case ZombieActionType.Hunt:
			{
				int targetTroops = candidate.TargetParty.MemberRoster.TotalManCount;
				float ratio = myTroops / (float)targetTroops;
				float minRatio = ZombieBehaviorConfig.GetMinStrengthRatio(myTroops);
				if (myTroops >= ZombieBehaviorConfig.GreedyGrowthTroopThreshold)
				{
					minRatio = Math.Min(minRatio, ZombieBehaviorConfig.GreedyMinStrengthRatio);
				}

				return ratio >= minRatio;
			}
			case ZombieActionType.RaidVillage:
			{
				int threshold = IsHighQualityRoster(party)
					? ZombieBehaviorConfig.VillageRaidTroopThresholdHighQuality
					: ZombieBehaviorConfig.VillageRaidTroopThreshold;
				if (myTroops >= ZombieBehaviorConfig.GreedyGrowthTroopThreshold)
				{
					threshold = Math.Min(threshold, ZombieBehaviorConfig.GreedyVillageRaidTroopThreshold);
				}

				if (myTroops < threshold)
				{
					return false;
				}

				return !HasNearbyThreateningHostileParty(party, candidate.TargetSettlement.GatePosition, myTroops);
			}
			case ZombieActionType.SiegeSettlement:
			{
				if (myTroops < ZombieBehaviorConfig.SettlementSiegeTroopThreshold)
				{
					return false;
				}

				int defenders = GetGarrisonAndMilitia(candidate.TargetSettlement);
				return myTroops >= ZombieBehaviorConfig.SiegeStrengthMultiplier * defenders;
			}
			default:
				return false;
		}
	}

	private static float CalculateScore(MobileParty party, ActionCandidate candidate)
	{
		int myTroops = party.MemberRoster.TotalManCount;
		float weight = ZombieBehaviorConfig.GetStrategicWeight(myTroops, candidate.Type);
		bool isGreedy = myTroops >= ZombieBehaviorConfig.GreedyGrowthTroopThreshold;

		float reward;
		float distanceFactor;
		float riskPenalty;

		if (candidate.Type == ZombieActionType.Hunt)
		{
			MobileParty target = candidate.TargetParty;
			int targetTroops = target.MemberRoster.TotalManCount;
			float distance = target.Position.Distance(party.Position);

			reward = targetTroops * (target.IsCaravan || target.IsVillager ? ZombieBehaviorConfig.PreyTypeBonusMultiplier : 1f);
			distanceFactor = distance * (target.Speed / Math.Max(party.Speed, 0.01f));
			if (isGreedy)
			{
				// Proximity dominates over reward while greedy: 8 looters right
				// next to the horde should beat 24 villagers much further away,
				// which a merely-linear distance penalty would not guarantee.
				distanceFactor = MathF.Pow(distanceFactor, ZombieBehaviorConfig.GreedyHuntDistanceExponent);
			}

			float ratio = myTroops / (float)targetTroops;
			riskPenalty = 1f / Math.Max(ratio, 0.01f);
			if (!isGreedy && HasNearbyHostileParty(party, target.Position, target))
			{
				reward *= ZombieBehaviorConfig.IsolationPenaltyMultiplier;
			}
		}
		else
		{
			Settlement settlement = candidate.TargetSettlement;
			float distance = settlement.GatePosition.Distance(party.Position);
			distanceFactor = distance;

			if (candidate.Type == ZombieActionType.RaidVillage)
			{
				Village village = settlement.Village;
				reward = village.Militia + ZombieBehaviorConfig.VillageHearthRewardWeight * village.Hearth;
				float villageRatio = myTroops / Math.Max(village.Militia, 1f);
				riskPenalty = 1f / Math.Max(villageRatio, 0.01f);
			}
			else
			{
				int defenders = GetGarrisonAndMilitia(settlement);
				reward = defenders;
				float siegeRatio = myTroops / (float)Math.Max(defenders, 1);
				riskPenalty = 1f / Math.Max(siegeRatio, 0.01f);
			}

			if (!isGreedy && HasNearbyHostileParty(party, settlement.GatePosition))
			{
				reward *= ZombieBehaviorConfig.IsolationPenaltyMultiplier;
			}
		}

		return (reward * weight) / (distanceFactor + riskPenalty);
	}

	/// <summary>
	/// Interception risk / isolation check: any other hostile party lurking near
	/// the target makes it riskier to commit to. `exclude` lets a hunt candidate
	/// skip counting itself.
	/// </summary>
	private static bool HasNearbyHostileParty(MobileParty party, CampaignVec2 position, MobileParty exclude = null)
	{
		return FindNearbyHostileParty(party, position, ZombieBehaviorConfig.InterceptionRadius, exclude) != null;
	}

	/// <summary>
	/// Raid hard-constraint version of the isolation check above: a nearby
	/// hostile only actually blocks a raid if it's strong enough to plausibly
	/// contest it (RaidBlockMinThreatRatio of the horde's own troops) - a lone
	/// weak straggler merely passing by should not veto raiding forever.
	/// </summary>
	private static bool HasNearbyThreateningHostileParty(MobileParty party, CampaignVec2 position, int myTroops)
	{
		MobileParty nearby = FindNearbyHostileParty(party, position, ZombieBehaviorConfig.InterceptionRadius);
		return nearby != null && nearby.MemberRoster.TotalManCount >= myTroops * ZombieBehaviorConfig.RaidBlockMinThreatRatio;
	}

	/// <summary>Nearest hostile party within `radius` of `position`, or null if none - used both for the isolation check above and the frustrated-raider threat check.</summary>
	private static MobileParty FindNearbyHostileParty(MobileParty party, CampaignVec2 position, float radius, MobileParty exclude = null)
	{
		MobileParty nearest = null;
		float nearestDistance = float.MaxValue;

		foreach (MobileParty candidate in MobileParty.All)
		{
			if (candidate == party || candidate == exclude || candidate.MemberRoster.TotalManCount <= 0)
			{
				continue;
			}

			if (!FactionManager.IsAtWarAgainstFaction(party.MapFaction, candidate.MapFaction))
			{
				continue;
			}

			float distance = candidate.Position.Distance(position);
			if (distance <= radius && distance < nearestDistance)
			{
				nearest = candidate;
				nearestDistance = distance;
			}
		}

		return nearest;
	}

	private static void ExecuteAction(MobileParty party, ActionCandidate candidate)
	{
		int total = party.MemberRoster.TotalManCount;

		switch (candidate.Type)
		{
			case ZombieActionType.Hunt:
				EngageParty(party, candidate.TargetParty, total);
				break;

			case ZombieActionType.RaidVillage:
				if (party.TargetSettlement != candidate.TargetSettlement || party.DefaultBehavior != AiBehavior.RaidSettlement)
				{
					ZombieLog.Info("UpdateZombieBehavior: " + party.StringId + " (" + total + ") -> raid " + candidate.TargetSettlement.StringId);
					SetPartyAiAction.GetActionForRaidingSettlement(party, candidate.TargetSettlement, MobileParty.NavigationType.Default, isFromPort: false, isTargetingPort: false);
				}
				break;

			case ZombieActionType.SiegeSettlement:
				if (party.TargetSettlement != candidate.TargetSettlement || party.DefaultBehavior != AiBehavior.BesiegeSettlement)
				{
					ZombieLog.Info("UpdateZombieBehavior: " + party.StringId + " (" + total + ") -> siege " + candidate.TargetSettlement.StringId);
					SetPartyAiAction.GetActionForBesiegingSettlement(party, candidate.TargetSettlement, MobileParty.NavigationType.Default, isFromPort: false);
				}
				break;
		}
	}

	private static int GetGarrisonAndMilitia(Settlement settlement)
	{
		int garrison = settlement.Town?.GarrisonParty?.MemberRoster.TotalManCount ?? 0;
		return garrison + (int)settlement.Militia;
	}

	/// <summary>Utility-scored target selection candidate - either a mobile party or a settlement, never both.</summary>
	private sealed class ActionCandidate
	{
		public ZombieActionType Type;
		public MobileParty TargetParty;
		public Settlement TargetSettlement;
		public float Score;
	}

	/// <summary>
	/// Nothing to raid or hunt nearby: drift to a fresh point instead of
	/// sitting still (what the vanilla bandit AI would do, and exactly the "sits
	/// at a village" behavior this replaces). Throttled so it does not re-roll
	/// mid-walk. When `awayFrom` is given (young hordes steering clear of a
	/// settlement), the point is biased in that direction instead of random.
	///
	/// Deliberately calls MobileParty.SetMovePatrolAroundPoint directly instead
	/// of going through SetPartyAiAction.GetActionForPatrollingAroundPoint: that
	/// helper only forwards to SetMovePatrolAroundPoint when DefaultBehavior
	/// isn't already PatrolAroundPoint, so once a zombie party is patrolling (it
	/// always is, starting at spawn) every later call to it was silently
	/// dropping the new point and leaving the party stuck circling wherever it
	/// first stopped - the "just stands there" bug.
	/// </summary>
	private void Wander(MobileParty party, CampaignVec2? awayFrom = null)
	{
		if (_nextWanderRoll.TryGetValue(party, out CampaignTime next) && next.IsFuture)
		{
			return;
		}

		float radius = ZombieSpawner.SpawnRadius * ZombieBehaviorConfig.WanderRadiusMultiplier;
		CampaignVec2 point = awayFrom.HasValue
			? ZombieSpawner.FindPointAwayFrom(party.Position, awayFrom.Value, radius)
			: ZombieSpawner.FindWanderPoint(party.Position, radius);

		ZombieLog.Info("UpdateZombieBehavior: " + party.StringId + " -> wander towards (" + point.X.ToString("0.0") + "," + point.Y.ToString("0.0") + ")");
		party.SetMovePatrolAroundPoint(point, MobileParty.NavigationType.Default);
		_nextWanderRoll[party] = CampaignTime.HoursFromNow(ZombieBehaviorConfig.WanderRerollIntervalHours);
	}

	/// <summary>
	/// A horde built mostly from converted regulars (tier 3+: line infantry,
	/// cavalry, etc.) rather than looters/recruits is more dangerous per head, so
	/// it is allowed to start raiding villages earlier.
	/// </summary>
	private static bool IsHighQualityRoster(MobileParty party)
	{
		int total = 0;
		int highTier = 0;
		foreach (TroopRosterElement element in party.MemberRoster.GetTroopRoster())
		{
			if (element.Character == null || element.Character.IsHero || element.Number <= 0)
			{
				continue;
			}

			total += element.Number;
			if (element.Character.Tier >= ZombieBehaviorConfig.HighQualityTierMin)
			{
				highTier += element.Number;
			}
		}

		return total > 0 && highTier / (float)total >= ZombieBehaviorConfig.HighQualityShareMin;
	}

	private void TrySplitZombieParty(MobileParty party)
	{
		int total = party.MemberRoster.TotalManCount;
		if (total < ZombieBehaviorConfig.SplitMinTroops)
		{
			return;
		}

		float chance = (total - ZombieBehaviorConfig.SplitMinTroops)
			/ (float)(ZombieBehaviorConfig.SplitMaxTroops - ZombieBehaviorConfig.SplitMinTroops);
		if (chance < 0f)
		{
			chance = 0f;
		}
		else if (chance > 1f)
		{
			chance = 1f;
		}

		if (MBRandom.RandomFloat > chance)
		{
			return;
		}

		ZombieLog.Info("TrySplitZombieParty: " + party.StringId + " at " + total + " men");

		// TODO.md Feature 4: split off a minority (30%) expedition party instead
		// of an even half, so the parent horde keeps growing instead of
		// perpetually re-halving itself.
		Dictionary<CharacterObject, int> splitOff = new();
		foreach (TroopRosterElement element in party.MemberRoster.GetTroopRoster().ToList())
		{
			int moving = (int)(element.Number * ZombieBehaviorConfig.SplitFraction);
			if (moving > 0)
			{
				splitOff[element.Character] = moving;
			}
		}

		if (splitOff.Count == 0)
		{
			return;
		}

		float radius = ZombieSpawner.SpawnRadius;
		MobileParty newParty = ZombieSpawner.SpawnNearPosition(
			party.Position,
			radius * ZombieBehaviorConfig.SplitSpawnMaxDistanceFactor,
			radius * ZombieBehaviorConfig.SplitSpawnMinDistanceFactor,
			splitOff,
			0);
		if (newParty == null)
		{
			return;
		}

		foreach (KeyValuePair<CharacterObject, int> entry in splitOff)
		{
			party.MemberRoster.AddToCounts(entry.Key, -entry.Value);
		}

		// Send the new expedition party far away from its parent rather than
		// leaving it to wander from the near-parent spawn point it just appeared at.
		CampaignVec2 awayPoint = ZombieSpawner.FindPointAwayFrom(
			newParty.Position, party.Position, radius * ZombieBehaviorConfig.SplitAwayDistanceFactor);
		newParty.SetMoveGoToPoint(awayPoint, MobileParty.NavigationType.Default);
	}

	public override void SyncData(IDataStore dataStore)
	{
		dataStore.SyncData("_newGameStartTime", ref _newGameStartTime);
		dataStore.SyncData("_startingHordeSpawned", ref _startingHordeSpawned);
		dataStore.SyncData("_firstSpawnNotified", ref _firstSpawnNotified);
		dataStore.SyncData("_mediumHordeNotified", ref _mediumHordeNotified);
		dataStore.SyncData("_largeHordeNotified", ref _largeHordeNotified);
		dataStore.SyncData("_wipeoutNotified", ref _wipeoutNotified);
	}

	private void OnTick(float dt)
	{
		_debugTimer += dt;
		if (_debugTimer < ZombieBehaviorConfig.DebugTickIntervalSeconds)
		{
			return;
		}

		_debugTimer = 0f;
		PrintZombieDebugInfo();
	}

	/// <summary>
	/// Debug-only status dump: for every zombie party, roughly where it is
	/// (nearest settlement + distance) and what it is currently doing.
	/// </summary>
	private static void PrintZombieDebugInfo()
	{
		List<MobileParty> zombieParties = MobileParty.AllBanditParties
			.Where(ZombieClanUtil.IsZombieParty)
			.ToList();

		if (zombieParties.Count == 0)
		{
			InformationManager.DisplayMessage(new InformationMessage("[ZombiePlague] Keine aktive Zombie-Party."));
			return;
		}

		foreach (MobileParty party in zombieParties)
		{
			Settlement nearest = FindNearestSettlement(party.Position);
			string locationText = nearest != null
				? string.Format("nahe {0} (~{1:0} Einheiten)", nearest.Name, nearest.GatePosition.Distance(party.Position))
				: "Position unbekannt";
			string behaviorText = party.GetBehaviorText()?.ToString() ?? "unbekannt";

			string message = string.Format(
				"[ZombiePlague] {0} | {1} Mann | ({2:0},{3:0}) {4} | {5}",
				party.Name,
				party.MemberRoster.TotalManCount,
				party.Position.X,
				party.Position.Y,
				locationText,
				behaviorText);

			InformationManager.DisplayMessage(new InformationMessage(message));
		}
	}

	private void OnSessionLaunched(CampaignGameStarter campaignGameStarter)
	{
		ZombieLog.Info("=== OnSessionLaunched ===");
		ZombieConversion.ClearCache();
		ZombieSpawner.LogObjectDiagnostics();
		ZombieTroopSetup.Apply();
		AddZombieDialog(campaignGameStarter);
		MakeZombiesHostileToEveryone();
		ReapplyZombieAppearanceToConvertedHeroes();
	}

	/// <summary>
	/// Bug fix: a converted hero's green skin was reverting after every save
	/// load. Root cause: BasicCharacterObject.InitializeHeroBasicCharacterOnAfterLoad
	/// (runs on every CharacterObject.AfterLoad) unconditionally resets Race
	/// (and Culture, DefaultCharacterSkills, BodyPropertyRange, ...) back to
	/// _originCharacter - the hero's ORIGINAL vanilla template - regardless of
	/// what ApplyZombieAppearance set at conversion time. StaticBodyProperties
	/// is a normal saved Hero field and survives fine; Race specifically does
	/// not. OnSessionLaunched fires on both new-game and load-save, so redoing
	/// this here re-fixes every already-converted hero every time a save loads.
	/// </summary>
	private static void ReapplyZombieAppearanceToConvertedHeroes()
	{
		int count = 0;
		foreach (Hero hero in Hero.AllAliveHeroes)
		{
			if (!ZombieClanUtil.IsZombieClan(hero.Clan))
			{
				continue;
			}

			ZombieConversion.ApplyZombieAppearance(hero);
			count++;
		}

		if (count > 0)
		{
			ZombieLog.Info("ReapplyZombieAppearanceToConvertedHeroes: re-applied to " + count + " already-converted hero(es).");
		}
	}

	/// <summary>
	/// Zombies do not negotiate - they groan and the encounter proceeds. Without
	/// this the game falls back to "default_conversation_for_wrongly_created_heroes",
	/// which is what showed up in the log when talking to a zombie party.
	/// </summary>
	private static void AddZombieDialog(CampaignGameStarter starter)
	{
		starter.AddDialogLine(
			"zombieplague_greeting",
			"start",
			"close_window",
			"{=zombieplague_greeting}Uuuuuuuhhh...",
			IsTalkingToZombies,
			PlayZombieGreetingSound,
			200);

		ZombieLog.Info("SUCCESS dialog: zombie greeting line registered");
	}

	private static bool IsTalkingToZombies()
	{
		return ZombieClanUtil.IsZombieParty(MobileParty.ConversationParty);
	}

	/// <summary>
	/// TODO.md item 6 test: talking to a hostile mobile party (not a
	/// settlement NPC) turned out NOT to run inside a Mission at all - it's a
	/// plain text encounter popup, confirmed by "no active Mission" showing
	/// up in zombieplague.log every time this fired. CampaignMissionComponent.
	/// PlayConversationSoundEvent's Mission.Current.Scene usage only applies
	/// to conversations that DO get a 3D scene (e.g. talking to a settlement
	/// notable). InformationManager's quick-info sound path is the
	/// Mission-independent route vanilla itself uses for campaign-map/UI
	/// sounds (see the MapNotificationType classes' SoundEventPath), so it
	/// works here regardless of whether a Mission exists - kept as a fallback
	/// for the (presumably rarer) case where the zombie encounter conversation
	/// does get a Mission.
	/// </summary>
	private const string GreetingSoundPath = "zombieplague/voice/dialogue_greeting";

	private static readonly int GreetingSoundEventId = SoundEvent.GetEventIdFromString(GreetingSoundPath);

	private static void PlayZombieGreetingSound()
	{
		if (Mission.Current != null)
		{
			Mission.Current.MakeSound(GreetingSoundEventId, Agent.Main.Position, false, false, -1, -1);
			return;
		}

		InformationManager.DisplayMessage(new InformationMessage(string.Empty, GreetingSoundPath));
	}

	/// <summary>
	/// The horde attacks anything. The bandit flag on the clan already makes it
	/// hostile by default; this declares it explicitly against every kingdom and
	/// clan so nothing stays neutral through some diplomacy path.
	/// </summary>
	private static void MakeZombiesHostileToEveryone()
	{
		Clan zombieClan = ZombieClanUtil.GetZombieClan();
		if (zombieClan == null)
		{
			return;
		}

		int declared = 0;

		foreach (Kingdom kingdom in Kingdom.All)
		{
			if (!FactionManager.IsAtWarAgainstFaction(zombieClan, kingdom))
			{
				FactionManager.DeclareWar(zombieClan, kingdom);
				declared++;
			}
		}

		foreach (Clan clan in Clan.All)
		{
			if (clan != zombieClan && !clan.IsEliminated && !FactionManager.IsAtWarAgainstFaction(zombieClan, clan))
			{
				FactionManager.DeclareWar(zombieClan, clan);
				declared++;
			}
		}

		ZombieLog.Info("SUCCESS hostility: declared war on " + declared + " additional factions");
	}

	private void OnNewGameCreated(CampaignGameStarter campaignGameStarter)
	{
		ZombieLog.Info("OnNewGameCreated: autoSpawn=" + ZombieIds.AutoSpawnOnNewGame
			+ ", spawnDelayDays=" + ZombieBehaviorConfig.SpawnDelayDays);

		ZombieClanUtil.GetZombieClan();
		_newGameStartTime = CampaignTime.Now;
		_startingHordeSpawned = false;

		if (!ZombieIds.AutoSpawnOnNewGame)
		{
			ZombieLog.Info("OnNewGameCreated: auto spawn disabled, use zombie.spawn_near_player");
		}
	}

	private void OnMapEventStarted(MapEvent mapEvent, PartyBase attackerParty, PartyBase defenderParty)
	{
		MobileParty zombieParty = FindZombiePartyInEvent(mapEvent, out BattleSideEnum _);
		if (zombieParty == null)
		{
			return;
		}

		_prisonSnapshots[zombieParty] = SnapshotRoster(zombieParty.PrisonRoster);
	}

	/// <summary>
	/// Everything the enemy side lost in this battle becomes a zombie, one for one.
	///
	/// Four sources, all read straight off the vanilla per-battle bookkeeping on
	/// MapEventParty - no Harmony hook needed, and it works for auto-resolved
	/// battles just as well as for fought ones:
	///
	///   died    troops killed outright, already gone from their roster
	///   routed  troops that fled, also already gone
	///   wounded troops still standing in their roster, marked wounded
	///   captured whatever the game handed us as prisoners
	///
	/// The only overlap is a troop that was wounded and then captured when its
	/// party was wiped out: it shows up in both "wounded" and "captured". Hence
	/// captured is counted only for the surplus beyond the wounded.
	/// </summary>
	private void OnMapEventEnded(MapEvent mapEvent)
	{
		MobileParty zombieParty = FindZombiePartyInEvent(mapEvent, out BattleSideEnum zombieSide);
		if (zombieParty == null)
		{
			return;
		}

		Dictionary<CharacterObject, int> died = new();
		Dictionary<CharacterObject, int> routed = new();
		Dictionary<CharacterObject, int> wounded = new();
		HashSet<Hero> defeatedHeroes = new();

		foreach (MapEventParty enemy in mapEvent.PartiesOnSide(mapEvent.GetOtherSide(zombieSide)))
		{
			AccumulateRoster(enemy.DiedInBattle, died);
			AccumulateRoster(enemy.RoutedInBattle, routed);
			AccumulateRoster(enemy.WoundedInBattle, wounded);

			CollectDefeatedHeroes(enemy.DiedInBattle, defeatedHeroes);
			CollectDefeatedHeroes(enemy.WoundedInBattle, defeatedHeroes);
			CollectDefeatedHeroes(enemy.RoutedInBattle, defeatedHeroes);

			// Scenario 2: the enemy pulls back with wounded men. They do not get to
			// heal - the horde takes them. Whoever is already gone (captured with a
			// destroyed party) is skipped inside.
			StripFreshlyWounded(enemy);
		}

		// TODO.md Phase 2 hero conversion: only when the horde actually won this
		// fight (not merely "the enemy lost some men", which can happen either
		// way) - a hero the horde lost against never gets a turn roll.
		if (defeatedHeroes.Count > 0 && mapEvent.HasWinner && mapEvent.DefeatedSide == mapEvent.GetOtherSide(zombieSide))
		{
			TryConvertDefeatedHeroes(defeatedHeroes, zombieParty);
		}

		Dictionary<CharacterObject, int> captured = ComputeCaptureDiff(zombieParty);
		_prisonSnapshots.Remove(zombieParty);

		// Zombies take no prisoners - they eat them. Handing the captives straight
		// back out of the prison roster is what turns "captured" into "converted".
		ReleaseCaptives(zombieParty, captured);

		Dictionary<CharacterObject, int> total = new();
		AddTally(total, died);
		AddTally(total, routed);
		AddTally(total, wounded);
		foreach (KeyValuePair<CharacterObject, int> entry in captured)
		{
			wounded.TryGetValue(entry.Key, out int alreadyCountedAsWounded);
			int surplus = entry.Value - alreadyCountedAsWounded;
			if (surplus > 0)
			{
				total.TryGetValue(entry.Key, out int existing);
				total[entry.Key] = existing + surplus;
			}
		}

		ZombieBattleReport.Record(zombieParty, died, routed, wounded, captured, total);

		if (total.Count == 0)
		{
			return;
		}

		if (zombieParty.MemberRoster.TotalManCount > 0)
		{
			ApplyGrowth(zombieParty, total);
		}
		else
		{
			// The zombie party did not survive this battle - MobilePartyDestroyed
			// will fire shortly after. Stash the tally so the respawn handler can
			// use it instead of the growth being lost.
			_pendingRespawnTallies[zombieParty.StringId] = total;
		}
	}

	/// <summary>
	/// Heroes are skipped for the troop tally - they are not troops, and (since
	/// TODO.md Phase 2) get their own separate conversion roll instead, see
	/// CollectDefeatedHeroes below.
	/// </summary>
	private static void AccumulateRoster(TroopRoster roster, Dictionary<CharacterObject, int> target)
	{
		if (roster == null)
		{
			return;
		}

		foreach (TroopRosterElement element in roster.GetTroopRoster())
		{
			if (element.Character == null || element.Character.IsHero || element.Number <= 0)
			{
				continue;
			}

			target.TryGetValue(element.Character, out int existing);
			target[element.Character] = existing + element.Number;
		}
	}

	/// <summary>Heroes found in a battle-tally roster, resolved via CharacterObject.HeroObject (null for regular troops).</summary>
	private static void CollectDefeatedHeroes(TroopRoster roster, HashSet<Hero> target)
	{
		if (roster == null)
		{
			return;
		}

		foreach (TroopRosterElement element in roster.GetTroopRoster())
		{
			Hero hero = element.Character?.HeroObject;
			if (hero != null)
			{
				target.Add(hero);
			}
		}
	}

	/// <summary>
	/// TODO.md Phase 2: each defeated hero gets an independent turn-or-escape
	/// roll, weighted so a small horde (closer to needing the boost to avoid
	/// being wiped) is more likely to succeed than one that has already
	/// snowballed. A failed roll does nothing special - the hero is handled by
	/// vanilla's own recovery, same as any other defeated lord.
	/// </summary>
	private static void TryConvertDefeatedHeroes(HashSet<Hero> heroes, MobileParty zombieParty)
	{
		int currentTroops = zombieParty.MemberRoster.TotalManCount;
		float sizeBonus = (ZombieBehaviorConfig.SmallHordeMax - currentTroops) / (float)ZombieBehaviorConfig.SmallHordeMax
			* ZombieBehaviorConfig.HeroTurnTroopSizeModifier;

		float turnChance = ZombieBehaviorConfig.HeroTurnBaseChance + sizeBonus;
		if (turnChance < 0f)
		{
			turnChance = 0f;
		}
		else if (turnChance > 1f)
		{
			turnChance = 1f;
		}

		foreach (Hero hero in heroes)
		{
			if (hero == Hero.MainHero || !hero.IsAlive || ZombieClanUtil.IsZombieClan(hero.Clan))
			{
				continue;
			}

			bool turned = MBRandom.RandomFloat < turnChance;
			ZombieLog.Info("TryConvertDefeatedHeroes: " + hero.Name + " roll " + (turned ? "SUCCESS" : "failed")
				+ " (chance " + turnChance.ToString("0.00") + ")");

			if (turned)
			{
				ZombieConversion.ConvertHero(hero, zombieParty);
			}
		}
	}

	/// <summary>
	/// Removes the men this battle wounded from the party they belong to. Capped at
	/// the wounded actually present so that men who limped in wounded from an
	/// earlier fight are left alone, and so that troops the game already moved into
	/// a prison roster are not removed twice.
	/// </summary>
	private static void StripFreshlyWounded(MapEventParty enemy)
	{
		TroopRoster roster = enemy.Party?.MemberRoster;
		if (roster == null)
		{
			return;
		}

		foreach (TroopRosterElement element in enemy.WoundedInBattle.GetTroopRoster())
		{
			if (element.Character == null || element.Character.IsHero || element.Number <= 0)
			{
				continue;
			}

			int index = roster.FindIndexOfTroop(element.Character);
			if (index < 0)
			{
				continue;
			}

			int removable = Math.Min(element.Number, roster.GetElementWoundedNumber(index));
			if (removable > 0)
			{
				roster.AddToCountsAtIndex(index, -removable, -removable, 0, removeDepleted: true);
			}
		}
	}

	private static void ReleaseCaptives(MobileParty zombieParty, Dictionary<CharacterObject, int> captured)
	{
		foreach (KeyValuePair<CharacterObject, int> entry in captured)
		{
			zombieParty.PrisonRoster.AddToCounts(entry.Key, -entry.Value);
		}
	}

	private static void AddTally(Dictionary<CharacterObject, int> target, Dictionary<CharacterObject, int> source)
	{
		foreach (KeyValuePair<CharacterObject, int> entry in source)
		{
			target.TryGetValue(entry.Key, out int existing);
			target[entry.Key] = existing + entry.Value;
		}
	}

	private void OnMobilePartyDestroyed(MobileParty party, PartyBase destroyerParty)
	{
		if (!ZombieClanUtil.IsZombieParty(party))
		{
			return;
		}

		_prisonSnapshots.Remove(party);
		_nextWanderRoll.Remove(party);
		_currentAction.Remove(party);
		_lastPosition.Remove(party);
		_stuckHours.Remove(party);
		_huntAbandonTimestamps.Remove(party);
		_opportunisticRaidTarget.Remove(party);
		_lastBesiegedSettlement.Remove(party);

		if (!_pendingRespawnTallies.TryGetValue(party.StringId, out Dictionary<CharacterObject, int> tally))
		{
			return;
		}

		_pendingRespawnTallies.Remove(party.StringId);
		if (tally.Count == 0)
		{
			return;
		}

		ZombieLog.Info("OnMobilePartyDestroyed: respawning from tally of " + tally.Count + " troop types");

		// Concept: the replacement horde appears at a hidden spot some way off from
		// where the old one died, carrying exactly what it killed in that battle.
		float radius = ZombieSpawner.SpawnRadius;
		ZombieSpawner.SpawnNearPosition(
			party.Position,
			radius * ZombieBehaviorConfig.RespawnMaxDistanceFactor,
			radius * ZombieBehaviorConfig.RespawnMinDistanceFactor,
			ZombieConversion.MapTally(tally),
			0);
	}

	/// <summary>
	/// v0.1 simplification carried over from the design notes: the first zombie
	/// party found is credited with everything the other side lost. That is exact
	/// for the one-horde-versus-one-party encounters this is built around, and
	/// generous when an allied lord happens to fight alongside the horde.
	/// </summary>
	private static MobileParty FindZombiePartyInEvent(MapEvent mapEvent, out BattleSideEnum side)
	{
		foreach (BattleSideEnum candidateSide in new[] { BattleSideEnum.Attacker, BattleSideEnum.Defender })
		{
			foreach (MapEventParty candidate in mapEvent.PartiesOnSide(candidateSide))
			{
				MobileParty mobileParty = candidate.Party?.MobileParty;
				if (ZombieClanUtil.IsZombieParty(mobileParty))
				{
					side = candidateSide;
					return mobileParty;
				}
			}
		}

		side = BattleSideEnum.None;
		return null;
	}

	private static Dictionary<CharacterObject, int> SnapshotRoster(TroopRoster roster)
	{
		Dictionary<CharacterObject, int> snapshot = new();
		foreach (TroopRosterElement element in roster.GetTroopRoster())
		{
			snapshot[element.Character] = element.Number;
		}

		return snapshot;
	}

	private Dictionary<CharacterObject, int> ComputeCaptureDiff(MobileParty zombieParty)
	{
		Dictionary<CharacterObject, int> result = new();
		if (!_prisonSnapshots.TryGetValue(zombieParty, out Dictionary<CharacterObject, int> before))
		{
			return result;
		}

		foreach (TroopRosterElement element in zombieParty.PrisonRoster.GetTroopRoster())
		{
			// Captured lords stay captured - converting heroes is a separate feature.
			if (element.Character == null || element.Character.IsHero)
			{
				continue;
			}

			before.TryGetValue(element.Character, out int beforeCount);
			int diff = element.Number - beforeCount;
			if (diff > 0)
			{
				result[element.Character] = diff;
			}
		}

		return result;
	}

	private static void ApplyGrowth(MobileParty zombieParty, Dictionary<CharacterObject, int> tally)
	{
		foreach (KeyValuePair<CharacterObject, int> entry in ZombieConversion.MapTally(tally))
		{
			zombieParty.MemberRoster.AddToCounts(entry.Key, entry.Value);
		}

		ZombieLog.Info("ApplyGrowth: " + zombieParty.StringId + " now at " + zombieParty.MemberRoster.TotalManCount);
	}

	private static Settlement FindNearestSettlement(CampaignVec2 position)
	{
		return FindNearestSettlement(position, null) ?? Settlement.All.First();
	}

	private static Settlement FindNearestSettlement(CampaignVec2 position, Func<Settlement, bool> filter)
	{
		Settlement nearest = null;
		float best = float.MaxValue;
		foreach (Settlement settlement in Settlement.All)
		{
			if (filter != null && !filter(settlement))
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
}