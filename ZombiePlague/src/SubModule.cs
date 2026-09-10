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

		try
		{
			_harmony = new Harmony("ZombiePlague");
			_harmony.PatchAll();
			Infrastructure.ZombieLog.Info("Harmony patches applied");
		}
		catch (System.Exception exception)
		{
			Infrastructure.ZombieLog.Error("Harmony PatchAll failed", exception);
		}

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
