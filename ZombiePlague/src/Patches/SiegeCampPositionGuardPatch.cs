using System;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Siege;
using TaleWorlds.Library;
using ZombiePlague.Infrastructure;

namespace ZombiePlague.Patches;

/// <summary>
/// TODO.md Phase 3 crash fix: confirmed root cause of the repeated
/// 0xC0000005 siege crash, found by reading vanilla source rather than
/// guessing at the party's own PartyComponent/hero (a Lord-vs-Bandit swap
/// made zero difference, which was the first sign the party's identity was
/// never the problem).
///
/// The crash only ever happened once the horde physically arrived and
/// MobileParty.OnPartyJoinedSiegeInternal ran:
///   Town town = SiegeEvent.BesiegedSettlement.Town;
///   var pos = _besiegerCamp.GetSiegeCampPartyPosition(this, town.BesiegerCampPositions1, town.BesiegerCampPositions2);
/// GetSiegeCampPartyPosition immediately does
/// `MBRandom.RandomInt(siegeCamp1GlobalFrames.Length)` with no null/empty
/// check at all. BesiegerCampPositions1/2 are populated once per session
/// from Campaign.Current.MapSceneWrapper.GetSiegeCampFrames - a native scene
/// query for camp marker prefabs baked into that settlement's scene. Small,
/// out-of-the-way castles (exactly the kind our horde picks and vanilla
/// lords essentially never bother besieging) can come back with zero camp
/// frames, and vanilla has no fallback for that until the *caller* checks
/// IsValid() - the callee it's calling into crashes first.
///
/// Fix: short-circuit that call ourselves when the primary camp array is
/// missing/empty, returning CampaignVec2.Invalid - MobileParty's own caller
/// already falls back to the settlement's GatePosition whenever the result
/// fails IsValid(), so this reuses vanilla's existing safety net instead of
/// inventing a new one.
/// </summary>
[HarmonyPatch(typeof(BesiegerCamp), "GetSiegeCampPartyPosition")]
internal static class SiegeCampPositionGuardPatch
{
	private static bool Prefix(MobileParty mobileParty, MatrixFrame[] siegeCamp1GlobalFrames, ref CampaignVec2 __result)
	{
		try
		{
			if (siegeCamp1GlobalFrames != null && siegeCamp1GlobalFrames.Length > 0)
			{
				return true;
			}

			ZombieLog.Error("GetSiegeCampPartyPosition: " + (mobileParty?.StringId ?? "NULL")
				+ " -> target settlement has no primary siege camp scene positions (BesiegerCampPositions1 empty/null) - "
				+ "using GatePosition instead of letting vanilla index the empty array.");

			__result = CampaignVec2.Invalid;
			return false;
		}
		catch (Exception ex)
		{
			ZombieLog.Error("SiegeCampPositionGuardPatch.Prefix failed", ex);
			return true;
		}
	}
}
