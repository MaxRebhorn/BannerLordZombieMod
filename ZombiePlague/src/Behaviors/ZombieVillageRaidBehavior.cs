using System;
using System.Collections.Generic;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.ObjectSystem;
using TaleWorlds.SaveSystem;
using ZombiePlague.Infrastructure;

namespace ZombiePlague.Behaviors;

public sealed class VillageDebuffRecord
{
	[SaveableField(1)] public CampaignTime ExpiryTime;
}

internal sealed class ZombieVillageRaidBehavior : CampaignBehaviorBase
{
	internal static ZombieVillageRaidBehavior Instance { get; private set; }

	private Dictionary<Settlement, VillageDebuffRecord> _debuffedVillages = new();

	public ZombieVillageRaidBehavior()
	{
		Instance = this;
	}

	public override void RegisterEvents()
	{
		CampaignEvents.DailyTickSettlementEvent.AddNonSerializedListener(this, OnDailyTickSettlement);
		CampaignEvents.VillageLooted.AddNonSerializedListener(this, OnVillageLooted);
	}

	private void OnVillageLooted(Village village)
	{
		MobileParty attacker = village?.Settlement?.LastAttackerParty;
		if (attacker == null || !ZombieClanUtil.IsZombieParty(attacker))
		{
			return;
		}

		OnZombieRaidCompleted(attacker, village);
	}

	public override void SyncData(IDataStore dataStore)
	{
		dataStore.SyncData("_debuffedVillages", ref _debuffedVillages);
	}

	internal bool IsVillageDebuffed(Settlement settlement)
	{
		return settlement != null && _debuffedVillages.ContainsKey(settlement);
	}

	private void OnZombieRaidCompleted(MobileParty zombieParty, Village village)
	{
		Settlement settlement = village?.Settlement;
		if (settlement == null)
		{
			return;
		}

		int gain = ComputeZombieGain(village.Hearth);
		if (gain > 0)
		{
			CharacterObject troop = MBObjectManager.Instance.GetObject<CharacterObject>(ZombieIds.TroopId(ZombieIds.MinTier));
			if (troop != null)
			{
				zombieParty.MemberRoster.AddToCounts(troop, gain);
			}
		}

		_debuffedVillages[settlement] = new VillageDebuffRecord
		{
			ExpiryTime = CampaignTime.DaysFromNow(ZombieBehaviorConfig.VillageDebuffDurationDays),
		};

		ZombieLog.Info("ZombieVillageRaidBehavior: " + settlement.StringId + " raided by " + zombieParty.StringId
			+ " - gained " + gain + " zombies, debuffed for " + ZombieBehaviorConfig.VillageDebuffDurationDays + " days.");
	}

	private void OnDailyTickSettlement(Settlement settlement)
	{
		if (!_debuffedVillages.TryGetValue(settlement, out VillageDebuffRecord record))
		{
			return;
		}

		if (CampaignTime.Now >= record.ExpiryTime)
		{
			_debuffedVillages.Remove(settlement);
			return;
		}

		Village village = settlement.Village;
		if (village == null)
		{
			return;
		}

		village.Hearth = (float)Math.Floor(village.Hearth - village.Hearth * ZombieBehaviorConfig.ProsperityPenaltyPercent / 100f);
	}

	private static int ComputeZombieGain(float hearth)
	{
		float baseGain = hearth / 100f * ZombieBehaviorConfig.ProsperityToZombieMultiplier;
		float variance = 1f + MBRandom.RandomFloatRanged(-1f, 1f) * ZombieBehaviorConfig.ZombieGainVariancePercent / 100f;
		int gain = (int)Math.Round(baseGain * variance, MidpointRounding.AwayFromZero);
		gain = Math.Max(gain, ZombieBehaviorConfig.MinZombiesGained);
		return Math.Min(gain, ZombieBehaviorConfig.MaxZombiesGained);
	}
}
