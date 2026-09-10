using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Siege;
using TaleWorlds.Core;
using ZombiePlague.Infrastructure;

namespace ZombiePlague.Patches;

/// <summary>
/// Temporary instrumentation for the still-unresolved siege crash (TODO.md
/// Phase 3): 0xC0000005 access violation, offset 0x18, no managed stack
/// trace available in this environment, and the SiegeCampPositionGuardPatch
/// fix did not stop it - so the crash is somewhere else in the vanilla
/// siege-initiation call chain between our own
/// SetPartyAiAction.GetActionForBesiegingSettlement call and an actual
/// SiegeEvent/BesiegerCamp being fully set up.
///
/// Every step below logs its own entry (and, where cheap, the values that
/// matter) so the next crash's zombieplague.log shows exactly which method
/// was entered last - i.e. which call it died inside - without needing a
/// symbol-resolved dump. Remove all of this once the crash is fixed.
/// </summary>
[HarmonyPatch]
internal static class SiegeDiagnosticsPatch
{
	[HarmonyPatch(typeof(MobileParty), "set_BesiegerCamp")]
	[HarmonyPrefix]
	private static void BeforeSetBesiegerCamp(MobileParty __instance, BesiegerCamp value)
	{
		ZombieLog.Info("SiegeDiag: MobileParty.BesiegerCamp setter ENTER, party=" + __instance.StringId + ", newValue=" + (value == null ? "NULL" : "non-null"));
	}

	[HarmonyPatch(typeof(SiegeEvent))]
	[HarmonyPatch(MethodType.Constructor, typeof(Settlement), typeof(MobileParty))]
	[HarmonyPrefix]
	private static void BeforeSiegeEventCtor(Settlement settlement, MobileParty besiegerParty)
	{
		ZombieLog.Info("SiegeDiag: SiegeEvent ctor ENTER, settlement=" + settlement.StringId + ", besiegerParty=" + besiegerParty.StringId
			+ ", LeaderHero=" + (besiegerParty.LeaderHero?.Name.ToString() ?? "NULL")
			+ ", OwnerClan=" + (settlement.OwnerClan?.StringId ?? "NULL")
			+ ", OwnerClanLeader=" + (settlement.OwnerClan?.Leader?.Name.ToString() ?? "NULL/no OwnerClan"));
	}

	[HarmonyPatch(typeof(BesiegerCamp))]
	[HarmonyPatch(MethodType.Constructor, typeof(SiegeEvent), typeof(IFaction))]
	[HarmonyPrefix]
	private static void BeforeBesiegerCampCtor()
	{
		ZombieLog.Info("SiegeDiag: BesiegerCamp ctor ENTER");
	}

	[HarmonyPatch(typeof(BesiegerCamp), "AddSiegePartyInternal")]
	[HarmonyPrefix]
	private static void BeforeAddSiegePartyInternal(BesiegerCamp __instance, MobileParty mobileParty)
	{
		ZombieLog.Info("SiegeDiag: BesiegerCamp.AddSiegePartyInternal ENTER, party=" + mobileParty.StringId);
	}

	[HarmonyPatch(typeof(DefaultEncounterModel), nameof(DefaultEncounterModel.GetLeaderOfSiegeEvent))]
	[HarmonyPostfix]
	private static void AfterGetLeaderOfSiegeEvent(BattleSideEnum side, Hero __result)
	{
		ZombieLog.Info("SiegeDiag: DefaultEncounterModel.GetLeaderOfSiegeEvent RETURNED for side=" + side
			+ ", result=" + (__result?.Name.ToString() ?? "NULL"));
	}

	[HarmonyPatch(typeof(BesiegerCamp), nameof(BesiegerCamp.InitializeSiegeEventSide))]
	[HarmonyPrefix]
	private static void BeforeBesiegerCampInitializeSiegeEventSide()
	{
		ZombieLog.Info("SiegeDiag: BesiegerCamp.InitializeSiegeEventSide ENTER");
	}

	[HarmonyPatch(typeof(Settlement), nameof(Settlement.InitializeSiegeEventSide))]
	[HarmonyPrefix]
	private static void BeforeSettlementInitializeSiegeEventSide(Settlement __instance)
	{
		ZombieLog.Info("SiegeDiag: Settlement.InitializeSiegeEventSide ENTER, settlement=" + __instance.StringId);
	}

	[HarmonyPatch(typeof(ChangeRelationAction), nameof(ChangeRelationAction.ApplyRelationChangeBetweenHeroes))]
	[HarmonyPrefix]
	private static void BeforeApplyRelationChangeBetweenHeroes(Hero hero, Hero gainedRelationWith, int relationChange)
	{
		ZombieLog.Info("SiegeDiag: ApplyRelationChangeBetweenHeroes ENTER hero=" + (hero?.Name.ToString() ?? "NULL")
			+ ", gainedRelationWith=" + (gainedRelationWith?.Name.ToString() ?? "NULL") + ", change=" + relationChange);
	}

	[HarmonyPatch(typeof(BesiegerCamp), "GetSiegeCampPartyPosition")]
	[HarmonyPostfix]
	private static void AfterGetSiegeCampPartyPosition(CampaignVec2 __result)
	{
		ZombieLog.Info("SiegeDiag: BesiegerCamp.GetSiegeCampPartyPosition RETURNED, result=" + __result.X + "," + __result.Y);
	}

	[HarmonyPatch(typeof(MobileParty), "set_Position")]
	[HarmonyPrefix]
	private static void BeforeSetPosition(MobileParty __instance, CampaignVec2 value)
	{
		ZombieLog.Info("SiegeDiag: MobileParty.Position setter ENTER, party=" + __instance.StringId
			+ ", value=" + value.X + "," + value.Y);
	}

	[HarmonyPatch(typeof(MobileParty), "OnPartyJoinedSiegeInternal")]
	[HarmonyPostfix]
	private static void AfterOnPartyJoinedSiegeInternal(MobileParty __instance)
	{
		ZombieLog.Info("SiegeDiag: MobileParty.OnPartyJoinedSiegeInternal RETURNED, party=" + __instance.StringId);
	}

	[HarmonyPatch(typeof(MobileParty), "set_BesiegerCamp")]
	[HarmonyPostfix]
	private static void AfterSetBesiegerCamp(MobileParty __instance)
	{
		ZombieLog.Info("SiegeDiag: MobileParty.BesiegerCamp setter RETURNED, party=" + __instance.StringId);
	}
}
