# Zombie Plague

A Mount & Blade II: Bannerlord singleplayer mod. Turns the campaign map into a
slow-burning apocalypse: a single "Patient Zero" horde spawns near a random
settlement and grows by hunting parties, raiding villages, and converting
defeated heroes - splitting into new hordes once it gets large enough.

## Features

- Zombie hordes that hunt, raid, and (once hero-led) besiege settlements,
  with behavior that shifts as a horde grows from small to medium to large.
- Village raids convert a settlement's prosperity into fresh zombies and
  leave it debuffed (prosperity decay + blocked recruitment) for a while
  afterward.
- Zombies are immune to night/wounded/over-size speed penalties, battle
  panic, and hunger/wage morale penalties - a mindless horde doesn't get
  tired or scared.
- Defeated enemy heroes have a chance to turn instead of escaping,
  eventually letting a horde lead its own siege.
- Fully configurable through MCM (Mod Configuration Menu): difficulty
  presets, starting spawn options, horde-size thresholds, settlement
  debuffs, village raid balance, and dozens of AI/behavior knobs.

## Installation

- **Steam Workshop** (recommended): subscribe via
  [Zombie Plague on the Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3799576293).
- **Manual**: copy the `ZombiePlague` folder into your Bannerlord `Modules`
  directory, then enable it (and its dependencies below) in the launcher.

### Dependencies

Requires, in load order, all available on the Workshop:

1. [Harmony](https://steamcommunity.com/sharedfiles/filedetails/?id=2859188632)
2. [UIExtenderEx](https://steamcommunity.com/sharedfiles/filedetails/?id=2859222409)
3. [ButterLib](https://steamcommunity.com/sharedfiles/filedetails/?id=2859232415)
4. [MCM (Mod Configuration Menu)](https://steamcommunity.com/sharedfiles/filedetails/?id=2859238197)

## Known limitations

- **Sieging is disabled by default** (`SiegeDisabledPendingInvestigation` in
  MCM's AI settings, under "Siege Disabled (Safety Toggle)"). Every attempt
  to let a hero-led horde besiege a settlement has hit a different crash or
  soft-lock at the "leaving the siege" transition - see the comments around
  `ZombieBehaviorConfig.SettlementSiegeTroopThreshold` and
  `ZombiePartyComponent` for the history. Enable at your own risk; the
  `zombie.create_op_sieging_party` cheat exists specifically to test this.
- This is an actively developed mod - expect balance changes and bug fixes
  between updates. Please report crashes with your `zombieplague.log`
  (module folder, or the engine log under
  `%ProgramData%\Mount and Blade II Bannerlord\logs\`) and a description of
  what was happening.

## Building from source

```powershell
dotnet build ZombiePlague/src/ZombiePlague.csproj -c Release
```

No local Bannerlord installation is required to build: the project
references the game/library assemblies via NuGet (community-maintained
reference assemblies + the real MCM/ButterLib/UIExtenderEx packages), not a
hardcoded path into a Steam install - see `ZombiePlague/src/ZombiePlague.csproj`.
The output DLL is written to `ZombiePlague/bin/Win64_Shipping_Client/ZombiePlague.dll`,
matching `ZombiePlague/SubModule.xml`.

CI (`.github/workflows/build.yml`) restores and builds the project the same
way on every push/PR, so a broken reference or a compile error is caught
before it reaches a release.

### Regenerating XML data

`tools/Generate-ZombieRace.ps1` and `tools/Generate-ZombieTroops.ps1` produce
generated `ModuleData/*.xml` files from the installed game's own data (a
green zombie race/skin, and a zombie variant of every vanilla combat troop).
Both are documented via PowerShell comment-based help - run
`Get-Help tools\Generate-ZombieTroops.ps1 -Full` for details, or open the
script. Re-run them after a Bannerlord update; their output is generated,
never hand-edited.

## Project layout

- `ZombiePlague/src/` - C# source (Harmony patches, campaign behaviors,
  MCM settings, console cheats).
- `ZombiePlague/ModuleData/` - troop/party/faction/localization XML.
- `ZombiePlague/docs/` - additional design notes.
- `tools/` - data-generation scripts and Steam Workshop publishing config.
