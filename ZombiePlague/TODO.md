1. AI Phase System (Replaces Utility Scoring)
Goal: Replace the single SelectBestAction utility-score system with a size-tiered phase system (Small, Medium, Large, Gigantic). The phase governs target selection strategy, thresholds, and risk-taking.

Files affected:

Infrastructure/ZombieBehaviorConfig.cs – add thresholds.

Infrastructure/ZombiePhase.cs (new) – phase enum + evaluation logic.

Behaviors/ZombiePlagueCampaignBehavior.cs – use GetCurrentPhase() to branch behavior.

Implementation steps:

Add phase thresholds to ZombieBehaviorConfig:

csharp
public const int SmallHordeMax = 120;
public const int MediumHordeMax = 500;
public const int LargeHordeMax = 1000;
// (Gigantic is anything > 1000)
Create ZombiePhase enum:

csharp
public enum ZombiePhase { Small, Medium, Large, Gigantic }
Add a GetCurrentPhase(MobileParty) method that reads the troop count and returns the phase.

Behavior branching in UpdateZombieBehavior:

Small (0–120): Keep the current utility-scoring logic (exact as today).

Medium (121–500): Use a simplified scoring where distance dominates over reward. The GreedyHuntDistanceExponent (currently 2.0) is applied unconditionally.

Large (501–1000): Drop the distance penalty; just pick the nearest huntable target that meets a very low strength ratio (0.5). If a village is within SpawnRadius, raid it instead.

Gigantic (1001+): "Really dumb" – hunt the nearest hostile mobile party, otherwise wander. Raiding becomes the default if no hunt target is found.

Config entry/exit: All thresholds (SmallHordeMax, MediumHordeMax, LargeHordeMax) will be in ZombieBehaviorConfig for easy tuning.

2. Hero Conversion & Turn Mechanic
Goal: Any hero (lord, wanderer, companion) on the losing side of a battle against a zombie horde has a variable chance to be turned. If they are not turned, they escape (they do not simply die – they flee the battle). This lays the groundwork for sieges (Task 4).

Trigger points:

OnMapEventEnded – when the horde wins.

Heroes in the losing party that are WoundedInBattle, DiedInBattle, or RoutedInBattle are candidates.

Files affected:

Infrastructure/ZombieConversion.cs – add ConvertHero() logic.

Infrastructure/ZombieBehaviorConfig.cs – add HeroTurnBaseChance and HeroTurnTroopSizeModifier.

Behaviors/ZombiePlagueCampaignBehavior.cs – hook into OnMapEventEnded.

Implementation logic:

Identify heroes on the losing side:

csharp
foreach (MapEventParty enemy in mapEvent.PartiesOnSide(losingSide))
{
    foreach (TroopRosterElement element in enemy.DiedInBattle.Union(enemy.WoundedInBattle).Union(enemy.RoutedInBattle))
    {
        if (element.Character.IsHero)
        {
            ProcessHeroTurn((Hero)element.Character);
        }
    }
}
Turn/escape decision:

Base chance: HeroTurnBaseChance = 0.4 (40%).

Modify by horde size: smaller hordes have a higher turn chance to help them snowball.
finalChance = baseChance + (SmallHordeMax - currentTroops) / SmallHordeMax * HeroTurnTroopSizeModifier

If the roll succeeds → turn the hero.

If it fails → the hero "escapes" (they remain alive, are removed from the battle, and rejoin their original clan or become a prisoner somewhere else? For simplicity, they are just not turned and are handled by vanilla recovery).

Converting a hero:

Remove the hero from their current clan.

Change their culture to Culture.looters (or a custom zombie culture).

Add them to the Zombie Clan.

Assign them to the victorious zombie party (as a prisoner? Or a full member?).
For sieges to work, the hero must be a member of the party, not a prisoner. So we will add them directly to the MemberRoster as a hero troop (using AddElementToMemberRoster).

Set their face to the zombie face template (zombie_male/zombie_female).

Config values:

csharp
public const float HeroTurnBaseChance = 0.4f;
public const float HeroTurnTroopSizeModifier = 0.3f; // Max extra chance when horde is tiny
3. Percentage-Based Lethality (Wound → Kill)
Goal: Increase the number of kills (rather than wounds) in battles where the zombie horde is small, to help them snowball and avoid being wiped. This is a post-battle conversion that turns a percentage of the enemy's WoundedInBattle into DiedInBattle before the zombie growth tally is computed.

Files affected:

Behaviors/ZombiePlagueCampaignBehavior.cs – modify OnMapEventEnded.

Infrastructure/ZombieBehaviorConfig.cs – add scaling values.

Implementation logic:

In OnMapEventEnded, after identifying the losing side, extract the WoundedInBattle roster.

Calculate a kill-conversion rate based on the horde size:

Small horde (≤ 120): KillConversionRate = 0.6 (60% of wounded become dead).

Medium (120–500): 0.4

Large (500–1000): 0.25

Gigantic (>1000): 0.15

(All values go into ZombieBehaviorConfig)

For each wounded troop on the losing side:

Roll a die. If success, subtract them from WoundedInBattle and add them to DiedInBattle.

This directly feeds into the existing AccumulateRoster tally, so the horde grows from them as kills.

Why this works without flat stat boosts: It makes the battle result more lethal for the loser without making zombie agents individually stronger in combat simulation.

Config values:

csharp
public const float KillConversionRateSmall = 0.6f;
public const float KillConversionRateMedium = 0.4f;
public const float KillConversionRateLarge = 0.25f;
public const float KillConversionRateGigantic = 0.15f;
4. Siege Re-integration (Conditional on Hero Presence)
Goal: Re-enable sieges, but only for hordes that have at least one turned hero in their MemberRoster. Siege behavior should avoid the broken siege-engine logic (i.e., zombies do not build engines – they just attack the walls en masse, which the game can handle if the party has a hero).

Files affected:

Infrastructure/ZombieActionType – re-add SiegeSettlement.

Behaviors/ZombiePlagueCampaignBehavior.cs – modify SelectBestAction and IsCandidateValid to include siege candidates conditionally.

Cheats/ZombieCheats.cs – add testing cheat.

Implementation logic:

Check hero presence:

csharp
private bool HasTurnedHero(MobileParty party)
{
    return party.MemberRoster.GetTroopRoster().Any(e => e.Character.IsHero);
}
Re-add SiegeSettlement to ZombieActionType.

In GetCandidates, only yield SiegeSettlement candidates if HasTurnedHero(party) is true.

In IsCandidateValid, add the siege hard constraints:

Troop threshold (configurable): SiegeTroopThreshold = 400.

(Optionally scale with phase – big hordes siege sooner).

Siege behavior: Since the party now has a LeaderHero, the vanilla siege system (SetPartyAiAction.GetActionForBesiegingSettlement) should work without crashing. We will not force it to avoid siege engines – the game will handle that natively once the hero exists.

5. Testing Cheat: Hero + Horde Spawn
Goal: Spawn a test horde of 20 zombies plus one newly created hero, placed next to the player, to test siege/hero conversion.

Files affected:

Cheats/ZombieCheats.cs – add spawn_hero_horde command.

Implementation logic:

Create a new hero using HeroCreator.CreateSpecialHero or Hero.CreateHero with a default template (e.g., imperial_recruit modified).

Assign the hero to the Zombie Clan, set their face to the zombie template.

Spawn a party with 20 generic zombies (zombie_tier_1) plus this hero using ZombieSpawner.SpawnNearPosition.

Add the hero to the party's MemberRoster.

Log success and the hero's name to the console.

Command signature:
zombie.spawn_hero_horde – spawns the horde 20–30 units from the player.

6. Sound Replacement
Goal: Replace vanilla zombie/undead voice lines with custom sounds from the /Sounds folder.

Implementation:

This is best done via a SubModule.xml entry defining a sound bank override, or via a SoundManager API call in OnSessionLaunched.

Since the specific file names in /Sounds are not detailed in the provided context, this step requires you to specify the exact sound event mappings (e.g., event:/characters/human/voice/attack → zombie_attack.wav).

Summary of New Config Values (ZombieBehaviorConfig)
Name	Purpose
SmallHordeMax = 120	Phase boundary
MediumHordeMax = 500	Phase boundary
LargeHordeMax = 1000	Phase boundary
HeroTurnBaseChance = 0.4f	Base chance a hero is turned
HeroTurnTroopSizeModifier = 0.3f	Extra chance when horde is small
KillConversionRateSmall = 0.6f	Wound → Kill for small horde
KillConversionRateMedium = 0.4f	...
KillConversionRateLarge = 0.25f	...
KillConversionRateGigantic = 0.15f	...
SiegeTroopThreshold = 400	Minimum troops to siege (if a hero is present)
Implementation Order (Recommended)
Sound Replacement – trivial, low risk.

Lethality Conversion – modifies existing OnMapEventEnded, easiest to test.

Hero Conversion – unlocks sieges and is the most impactful new feature.

Cheat Command – needed to test hero conversion + sieges.

AI Phase System – replaces the core logic, do this last to avoid conflicts.

Let me know if you need help fleshing out any specific method signature or edge case (e.g., what happens to a turned hero's spouse/children, or how to handle a hero with an existing kingdom).

## 2026-09-11 -> 2026-09-20: Siege rework - corrected root cause, split into testable phases

Sieging is currently disabled (`ZombieBehaviorConfig.SiegeDisabledPendingInvestigation = true`).
2026-09-11's session tried three fixes that all failed at the "leaving a siege" transition (raw
crash, silent party vanish, party disbanded via a lost-battle hero-capture) - see
`ZombiePartyComponent.SwapToLordForSiege`'s doc comment for that blow-by-blow. That session's
write-up blamed `SetDoNotMakeNewDecisions(true)` for the horde never building siege equipment.

**2026-09-20 correction: that diagnosis was wrong.** Decompiled the actual vanilla classes
(`ilspycmd`) instead of guessing from method signatures. Confirmed: `SiegeEvent.Tick()` ->
`TickSiegeEventSide()` applies `DefaultSiegeEventModel.GetConstructionProgressPerHour(...)` directly
- siege engine construction runs entirely on `SiegeEvent`'s own tick, with no dependency on
`MobileParty.Ai`/party decision-making at all. `SetDoNotMakeNewDecisions` was never the cause.

**Real root cause, decompiler-verified:** `BesiegerCamp.IsReadyToBesiege` gates an assault on
`PreparationProgress >= 1f` - an *abstract* readiness percentage, not "do we have a working ram/
tower." Its speed (`GetConstructionProgressPerHour`) is driven mainly by `availableManDayPower`,
i.e. raw troop count. A 12001-troop horde's abstract progress races to 100% almost instantly, while
whether a real Ram/Siege Tower has actually finished building is tracked completely separately
(`StartingAssaultOnBesiegedSettlementIsLogical` only checks `IsConstructed` per engine to apply a
harsher 1.25x times 1.25x strength-ratio requirement if missing - it does not block the assault
outright). Against a small garrison, overwhelming troop count cleared even that harsher bar in
seconds, so vanilla greenlit an assault before a single physical siege engine existed. Attacking a
wall with nothing to breach it is a mechanically guaranteed 0-damage wipe, which is exactly what the
logs showed.

**The fix found in vanilla's own code:** `DefaultSiegeEventModel.GetPrebuiltSiegeEnginesOfSiegeCamp
(BesiegerCamp)` already exists for exactly this purpose - today it just grants a free Ballista if the
leader has the "Battlements" perk. Same mechanism, applied to zombie-led sieges, closes the gap
between "vanilla thinks we're ready" and "we actually have equipment."

### Phase 1 - grant zombie sieges working equipment (fixes the 0-damage wipe on its own)

Goal: a zombie-led siege has a real Ram + Siege Tower (+ one ranged engine) from the moment it
starts, so `StartingAssaultOnBesiegedSettlementIsLogical`'s equipment flags are true immediately and
an assault is no longer guaranteed to be a walls-with-no-ladders wipe.

Files: new `src/Patches/ZombieSiegeEquipmentPatch.cs`.

Implementation:
- `[HarmonyPatch(typeof(DefaultSiegeEventModel), nameof(DefaultSiegeEventModel.GetPrebuiltSiegeEnginesOfSiegeCamp))]`,
  `[HarmonyPostfix]` - if `besiegerCamp.LeaderParty` is a zombie party (`ZombieClanUtil.IsZombieParty`),
  add `DefaultSiegeEngineTypes.Ram`, `DefaultSiegeEngineTypes.SiegeTower`, and one ranged engine
  (`DefaultSiegeEngineTypes.Trebuchet` or `.Onager`) to `__result` instead of returning vanilla's list.
- Confirmed via reflection: `TaleWorlds.Core.DefaultSiegeEngineTypes` is the right static holder
  (mirrors `DefaultSkills`/`DefaultPerks`), with `.Ram`, `.SiegeTower`, `.Trebuchet` all present
  exactly as named above - safe to wire in directly.

Test (independently, before touching anything else):
- `zombie.create_op_sieging_party` against a weak castle with `SiegeDisabledPendingInvestigation`
  temporarily flipped to `false` for the test.
- Watch `zombieplague.log` / engine log for the prebuilt engines actually being added to the camp.
- Confirm the resulting assault battle report is no longer "gains 0 across 0 troop types" - some
  defender casualties should be credited even if the zombies still ultimately lose that particular
  fight. This alone is the success signal for Phase 1, independent of everything below.

### Phase 2 - stop a lost assault from disbanding the whole party

Goal: even with real equipment, the horde can still lose an assault against a strong enough
garrison. Right now that death/capture of the `LordPartyComponent`'s leader hero disbands the entire
party (the exact 2026-09-11 failure #3). Needs a guard so a losing zombie siege degrades gracefully
(horde retreats/shrinks) instead of vanishing outright.

Files: likely `ZombieNoRoutPatch.cs` (extend to heroes, not just troops) or a new patch on whatever
vanilla hero-capture/death path a defeated `LordPartyComponent` leader goes through.

Implementation: not designed yet - first confirm via testing whether Phase 1 alone makes this rare
enough to not matter much in practice (a horde that can actually contest the walls should win most
sieges it starts, softening how often this edge case even fires) before investing in a dedicated fix.

Test: repeat the Phase 1 test against a garrison strong enough to actually win the assault; confirm
the horde survives in some reduced form afterward rather than fully disbanding + the still-unconfirmed
crash from 2026-09-11 recurring.

### Phase 3 - re-enable siege as a normal AI-chosen action

Goal: flip `SiegeDisabledPendingInvestigation` back to `false` by default once Phases 1-2 are
verified stable via repeated manual cheat testing (not just one lucky run).

Test: let a horde reach `SettlementSiegeTroopThreshold` naturally (no cheats) and siege on its own;
watch several full siege-to-resolution cycles for crashes/vanishes before calling this done.

### Phase 4 - live in-mission troop conversion during assaults (buff, not a bugfix)

Goal (user's original ask, still valid independently of the equipment fix above): "they fight, they
lose a bunch of troops, but they also gain troops whilst the enemy only loses troops" - a mission
behavior active during a zombie-attacking siege assault that converts defender kills into zombies
*during* the still-ongoing fight, not just via the existing post-battle `OnMapEventEnded`
tally-and-respawn-later path.

Files: new mission behavior alongside `src/Missions/ZombieAmbientSoundMissionBehavior.cs`.

Implementation sketch (unchanged from 2026-09-11, still open questions):
- Hook `MissionBehaviorBase.OnAgentRemoved`/`OnAgentDeleted`, filtered to defending-side non-zombie
  casualties during a siege-assault mission the zombie clan is attacking in.
- For each qualifying kill, roll/queue a new zombie agent (reusing `ZombieConversion.MapTally`'s
  tier-mapping-from-victim logic) and spawn it into the attacking formation via the mission's
  agent-spawn API - not yet verified against decompiled source which call that actually is.
- New `ZombieBehaviorConfig` field(s) for the live conversion rate, similar in spirit to the existing
  `KillConversionRate*` tiers, so it is tunable rather than hardcoded (100% conversion would trivialize
  every fight).
- Open question carried over: whether this should also apply to plain hunt/raid missions, or stay
  siege-assault-specific per the original framing.

Test: a controlled `zombie.create_op_sieging_party` assault with Phase 1's equipment fix in place;
count converted zombies visibly joining the attacking side mid-fight, and confirm the horde's
post-battle troop count reflects live gains beyond just what would have respawned afterward anyway.

### Suggested order

Phase 1 is the one most likely to fix the core problem outright and is fully independent of the
other three - do it first and re-test before deciding whether Phases 2 and 4 are still needed at
all. Phase 3 (re-enabling siege by default) should be last, after real confidence from repeated
testing, not just a single successful run.