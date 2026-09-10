using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.CampaignSystem.Siege;
using ZombiePlague.Infrastructure;

namespace ZombiePlague.Patches;

/// <summary>
/// TODO.md Phase 3 crash fix, take two: SiegeDiagnosticsPatch's logging
/// bracketed the crash to a two-statement window inside SiegeEvent's
/// constructor - it always dies between the BesiegerCamp setter returning
/// and either ApplyRelationChangeBetweenHeroes or InitializeSiegeEventSide
/// logging their own entry, and neither ever does:
///
///   if (besiegerParty.LeaderHero != null &amp;&amp; settlement.OwnerClan != null &amp;&amp; settlement.OwnerClan != Clan.PlayerClan)
///   {
///       ChangeRelationAction.ApplyRelationChangeBetweenHeroes(settlement.OwnerClan.Leader, besiegerParty.LeaderHero, -5, ...);
///   }
///
/// Since a Harmony Prefix always runs before the patched method's own body
/// (confirmed working for this exact method elsewhere in the same session's
/// log, for ordinary lord-vs-lord relation changes), the call itself - or
/// something evaluated only in this specific branch - is what's crashing,
/// and it's a hard native crash with no managed stack trace available here.
///
/// A Prefix/Postfix can only bracket whole method calls, not a mid-method
/// `if` block, so a transpiler is the only tool that can excise just this
/// one statement. Rather than deleting it outright (branch/stack surgery,
/// easy to get subtly wrong), this redirects the call itself to a safe
/// no-op with an identical signature - zero stack effect, so no other IL in
/// the constructor needs touching - and only for pairs involving a zombie
/// hero; a normal lord-vs-lord siege still gets the real relation change.
/// </summary>
[HarmonyPatch(typeof(SiegeEvent))]
[HarmonyPatch(MethodType.Constructor, typeof(Settlement), typeof(MobileParty))]
internal static class SiegeRelationChangeGuardPatch
{
	[HarmonyTranspiler]
	private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
	{
		MethodInfo original = AccessTools.Method(typeof(ChangeRelationAction), nameof(ChangeRelationAction.ApplyRelationChangeBetweenHeroes));
		MethodInfo replacement = AccessTools.Method(typeof(SiegeRelationChangeGuardPatch), nameof(GuardedApplyRelationChangeBetweenHeroes));

		foreach (CodeInstruction instruction in instructions)
		{
			if (instruction.Calls(original))
			{
				yield return new CodeInstruction(OpCodes.Call, replacement);
			}
			else
			{
				yield return instruction;
			}
		}
	}

	private static void GuardedApplyRelationChangeBetweenHeroes(Hero hero, Hero gainedRelationWith, int relationChange, bool showQuickNotification)
	{
		bool involvesZombie = ZombieClanUtil.IsZombieClan(hero?.Clan) || ZombieClanUtil.IsZombieClan(gainedRelationWith?.Clan);
		if (!involvesZombie)
		{
			ChangeRelationAction.ApplyRelationChangeBetweenHeroes(hero, gainedRelationWith, relationChange, showQuickNotification);
			return;
		}

		ZombieLog.Info("SiegeRelationChangeGuard: skipped relation change between "
			+ (hero?.Name.ToString() ?? "NULL") + " and " + (gainedRelationWith?.Name.ToString() ?? "NULL")
			+ " - a zombie hero is involved (confirmed crash site, see SiegeDiagnosticsPatch's log trail).");
	}
}
