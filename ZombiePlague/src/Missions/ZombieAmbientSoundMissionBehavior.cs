using System;
using System.Collections.Generic;
using TaleWorlds.Core;
using TaleWorlds.Engine;
using TaleWorlds.MountAndBlade;
using ZombiePlague.Infrastructure;

namespace ZombiePlague.Missions;

/// <summary>
/// TODO.md item 6: gives zombie troops an actual voice.
///
/// Two parts:
///  - Ambient bark: every AmbientSoundIntervalSeconds, plays a random clip at
///    a random living zombie agent's position - flavor for the whole battle,
///    not tied to any specific action.
///  - Real combat voice: ModuleData/voice_definitions.xml adds a
///    "zombieplague_zombie_01" voice_definition (Grunt/Pain/Death/Yell wired
///    to Sounds/attack_grunt_1.wav etc. via module_sounds.xml) to the
///    "human" skeleton class's voice pool. Merely being in the pool is not
///    enough - the engine assigns voice_definitions to agents at random with
///    no race/persona filter - so OnAgentBuild force-assigns it to every
///    zombie-race agent via Agent.AgentVisuals.SetVoiceDefinitionIndex,
///    which vanilla never calls for humans (only for mounts) but is public
///    and callable regardless.
///
/// Registered for every mission (see SubModule.OnMissionBehaviorInitialize);
/// both parts are no-ops whenever no zombie agent is present.
/// </summary>
public sealed class ZombieAmbientSoundMissionBehavior : MissionBehavior
{
	private const string ZombieVoiceDefinitionName = "zombieplague_zombie_01";
	private const string HumanCollisionClass = "human";

	private static readonly int AmbientSoundEventId = SoundEvent.GetEventIdFromString("zombieplague/voice/ambient_growl");

	private float _timer;
	private int _zombieVoiceDefinitionIndex = -1;
	private bool _voiceIndexResolved;
	private bool _tickErrorLogged;

	public override MissionBehaviorType BehaviorType => MissionBehaviorType.Other;

	public override void OnAgentBuild(Agent agent, Banner banner)
	{
		try
		{
			ApplyZombieVoiceIfNeeded(agent);
		}
		catch (Exception ex)
		{
			ZombieLog.Error("ZombieAmbientSoundMissionBehavior.OnAgentBuild failed", ex);
		}
	}

	public override void OnMissionTick(float dt)
	{
		try
		{
			OnMissionTickCore(dt);
		}
		catch (Exception ex)
		{
			// A single battle can tick hundreds of times - log the first
			// failure only, to avoid flooding the log for the rest of it.
			if (!_tickErrorLogged)
			{
				_tickErrorLogged = true;
				ZombieLog.Error("ZombieAmbientSoundMissionBehavior.OnMissionTick failed (further failures this mission are suppressed)", ex);
			}
		}
	}

	private void OnMissionTickCore(float dt)
	{
		_timer += dt;
		if (_timer < ZombieBehaviorConfig.AmbientSoundIntervalSeconds)
		{
			return;
		}

		_timer = 0f;

		List<Agent> zombieAgents = new();
		foreach (Agent agent in Mission.Agents)
		{
			if (!agent.IsActive() || !ZombieClanUtil.IsZombieAgent(agent))
			{
				continue;
			}

			zombieAgents.Add(agent);

			// Belt-and-suspenders: re-assert the override on every zombie agent
			// here too, not just at OnAgentBuild - if OnAgentBuild fires before
			// the engine's own native voice assignment for that agent (order is
			// not something the C# side controls), the native call would win and
			// silently undo ours. Cheap to repeat; SetVoiceDefinitionIndex has no
			// side effects beyond the assignment itself.
			ApplyZombieVoiceIfNeeded(agent);
		}

		if (zombieAgents.Count == 0)
		{
			return;
		}

		Agent chosen = zombieAgents[MBRandom.RandomInt(zombieAgents.Count)];
		Mission.MakeSound(AmbientSoundEventId, chosen.Position, false, false, -1, -1);
	}

	private void ApplyZombieVoiceIfNeeded(Agent agent)
	{
		if (!ZombieClanUtil.IsZombieAgent(agent))
		{
			return;
		}

		int voiceIndex = ResolveZombieVoiceDefinitionIndex();
		if (voiceIndex < 0)
		{
			return;
		}

		agent.AgentVisuals?.SetVoiceDefinitionIndex(voiceIndex, 1f);
	}

	/// <summary>
	/// TODO.md item 6: the C# voice API has no lookup-by-name, only a count +
	/// index array per skeleton class (SkinVoiceManager). Native's own 13
	/// human voice_definitions (male_01..08, female_01..05) are declared
	/// before ours (Native is a LoadBeforeThis dependency, and
	/// voice_definitions.xml merges same as any other same-filename
	/// ModuleData file), so this assumes "zombieplague_zombie_01" lands last
	/// in the merged array and takes indices[^1]. Logged once so this is easy
	/// to double check in zombieplague.log the first time a zombie agent
	/// spawns - if zombie troops end up with a plain human's default barks
	/// instead of the zombie sounds, this is the assumption to revisit first
	/// (the array itself would need dumping with the actual voice_definition
	/// names, which the engine does not expose - would need a WinDbg/log
	/// listen-test matching a known human voice against a known index).
	/// </summary>
	// Native ships exactly 13 "human" voice_definitions (male_01..08,
	// female_01..05) as of the currently indexed game version. Used purely to
	// tell "our XML never got merged" apart from "merged, but at the wrong
	// index" in the log below - not a correctness dependency anywhere else.
	private const int VanillaHumanVoiceDefinitionCount = 13;

	private int ResolveZombieVoiceDefinitionIndex()
	{
		if (_voiceIndexResolved)
		{
			return _zombieVoiceDefinitionIndex;
		}

		_voiceIndexResolved = true;

		int count = SkinVoiceManager.GetVoiceDefinitionCountWithMonsterSoundAndCollisionInfoClassName(HumanCollisionClass);
		if (count <= 0)
		{
			ZombieLog.Error("ResolveZombieVoiceDefinitionIndex: no voice definitions found for class '" + HumanCollisionClass + "'.");
			return -1;
		}

		if (count <= VanillaHumanVoiceDefinitionCount)
		{
			ZombieLog.Error("ResolveZombieVoiceDefinitionIndex: only " + count + " '" + HumanCollisionClass
				+ "' voice definitions found (vanilla ships " + VanillaHumanVoiceDefinitionCount
				+ ") - ModuleData/voice_definitions.xml was not merged in at all. Check it's actually being loaded "
				+ "(no XmlNode registration needed, same as module_sounds.xml, but double check the file is valid XML "
				+ "and sits directly in ModuleData/).");
			return -1;
		}

		int[] indices = new int[count];
		SkinVoiceManager.GetVoiceDefinitionListWithMonsterSoundAndCollisionInfoClassName(HumanCollisionClass, indices);

		_zombieVoiceDefinitionIndex = indices[indices.Length - 1];
		ZombieLog.Info("ResolveZombieVoiceDefinitionIndex: " + count + " '" + HumanCollisionClass
			+ "' voice definitions found (vanilla baseline " + VanillaHumanVoiceDefinitionCount
			+ ", so ours did merge in), assuming last one (index " + _zombieVoiceDefinitionIndex
			+ ") is '" + ZombieVoiceDefinitionName + "' - verify in-game that zombies actually sound different from human NPCs.");

		return _zombieVoiceDefinitionIndex;
	}
}
