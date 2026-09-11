using HarmonyLib;
using TaleWorlds.Core;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace ZombiePlague;

public sealed class SubModule : MBSubModuleBase
{
	private Harmony _harmony;

	protected override void OnSubModuleLoad()
	{
		base.OnSubModuleLoad();
		Infrastructure.ZombieLog.Info("=== OnSubModuleLoad ===");

		// Patched class-by-class rather than one PatchAll(assembly) call: a
		// single bad patch (confirmed - see ZombieRecruitBlockPatch's parameter
		// name bug) throws out of PatchAll entirely, silently skipping every
		// class Harmony hadn't reached yet in its enumeration order. That
		// meant a typo in one unrelated patch could - and did - leave the
		// siege/position crash-guard patches unapplied for an entire session,
		// with no symptom besides those crashes still happening. Isolating
		// each class means one broken patch only loses that one patch.
		_harmony = new Harmony("ZombiePlague");
		int patched = 0;
		int failed = 0;
		foreach (System.Type type in typeof(SubModule).Assembly.GetTypes())
		{
			try
			{
				if (_harmony.CreateClassProcessor(type).Patch() != null)
				{
					patched++;
				}
			}
			catch (System.Exception exception)
			{
				failed++;
				Infrastructure.ZombieLog.Error("Harmony patch failed for " + type.FullName, exception);
			}
		}

		Infrastructure.ZombieLog.Info("Harmony patching complete: " + patched + " class(es) patched, " + failed + " failed");

		Debug.Print("ZombiePlague: OnSubModuleLoad");
	}

	protected override void OnGameStart(Game game, IGameStarter gameStarterObject)
	{
		base.OnGameStart(game, gameStarterObject);
		if (game.GameType is Campaign && gameStarterObject is CampaignGameStarter campaignGameStarter)
		{
			Infrastructure.ZombieLog.Info("OnGameStart: registering campaign behavior");
			campaignGameStarter.AddBehavior(new Behaviors.ZombiePlagueCampaignBehavior());
			campaignGameStarter.AddBehavior(new Behaviors.ZombieVillageRaidBehavior());
		}
	}

	public override void OnMissionBehaviorInitialize(Mission mission)
	{
		base.OnMissionBehaviorInitialize(mission);
		mission.AddMissionBehavior(new Missions.ZombieAmbientSoundMissionBehavior());
	}
}
