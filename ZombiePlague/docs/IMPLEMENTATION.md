# Zombie Plague – Implementierungsdoku

Beschreibt, **wie** der Mod aktuell umgesetzt ist und **warum** die jeweiligen
Wege gewählt wurden. Für den Projektstand und offene Punkte siehe
[HANDOVER.md](HANDOVER.md).

Zielversion: **Bannerlord v1.4.8** (War Sails). Alle genannten Spiel-APIs wurden
gegen den dekompilierten v1.4.8-Source verifiziert (BannerlordSage MCP,
`api_exact_match: true`).

---

## 1. Aufbau

```
ZombiePlagueMod/
├─ tools/
│  ├─ Generate-ZombieTroops.ps1      erzeugt den Truppen-Katalog aus Vanilla-XML
│  └─ Generate-ZombieRace.ps1        erzeugt die grüne Race aus Vanilla-skins.xml
└─ ZombiePlague/
   ├─ SubModule.xml                  Modul-Manifest, registriert alle XML-Knoten
   ├─ ModuleData/
   │  ├─ project.mbproj              meldet skins.xml beim Engine-Loader an
   │  ├─ skins.xml                   GENERIERT – Race „zombie" mit grüner Palette
   │  ├─ zombie_monsters.xml         Monster „zombie" / „zombie_settlement"
   │  ├─ zombie_bodyproperties.xml   eigene Gesichts-Templates der Horde
   │  ├─ zombie_clans.xml            Zombie-Clan als XML-Faction
   │  ├─ zombie_troops.xml           zombie_tier_1 … zombie_tier_6 (Fallback)
   │  ├─ zombie_troops_generated.xml GENERIERT – 514 Klassen-Varianten
   │  ├─ zombie_party_templates.xml  Horde-Template + zwei Debug-Templates
   │  └─ zombie_debug_troops.xml     Debug-Truppen für die Bisektionsleiter
   └─ src/
      ├─ SubModule.cs                Einstieg, Harmony-Init, Behavior-Registrierung
      ├─ Behaviors/
      │  └─ ZombiePlagueCampaignBehavior.cs   Kern-Loop, Events, Konvertierung, Split, KI
      ├─ Infrastructure/
      │  ├─ ZombieIds.cs             IDs + Laufzeit-Schalter
      │  ├─ ZombieLog.cs             Logging
      │  ├─ ZombieClanUtil.cs        Clan-Zugriff
      │  ├─ ZombieSpawner.cs         Sämtliche Party-Erzeugung
      │  ├─ ZombieTroopSetup.cs      Race-Zuweisung + Fernkampf-Stripping
      │  ├─ ZombieConversion.cs      Opfer-Truppe → Zombie-Variante
      │  └─ ZombieBattleReport.cs    Bilanz der letzten Schlacht (Diagnose)
      ├─ Patches/
      │  ├─ BlockVanillaBanditSpawnPatch.cs   Unterdrückt Vanilla-Auto-Spawn
      │  └─ BlockDeserterSpawnPatch.cs        Unterdrückt Vanilla-Deserteure
      └─ Cheats/
         └─ ZombieCheats.cs          Konsolenbefehle
```

Die beiden `GENERIERT`-Dateien werden nie von Hand bearbeitet. Nach einem
Bannerlord-Update reicht ein erneuter Lauf der jeweiligen `tools/*.ps1`.

Der Ordner ist per Verzeichnis-Junction in
`…\Mount & Blade II Bannerlord\Modules\ZombiePlague` eingehängt. Jeder Build
landet damit sofort im Spiel, ohne Kopierschritt.

---

## 2. Der Zombie-Clan

**Datei:** `ModuleData/zombie_clans.xml`

Der Clan wird **als XML-Faction definiert**, nicht zur Laufzeit erzeugt. Das ist
die wichtigste Architekturentscheidung des Projekts und wurde teuer erkauft:

`Clan.CreateClan()` legt nur eine leere Hülle an. `Clan.Deserialize` setzt
darüber hinaus Banner-Farbwerte (`BannerBackgroundColorPrimary/Secondary`,
`BannerIconColor`), die Minor-Faction-Template-Liste, `Tier` und `Renown`. Ohne
diese Initialisierung stürzte der native Renderer beim Erzeugen des
Party-Karten-Icons mit `0xC0000005` ab. Nachgewiesen über die Debug-Leiter:
Vanilla-Clan spawnte sauber, unser Laufzeit-Clan nicht.

Vorlage ist die Vanilla-Söldnerfraktion `ghilman`. Bewusst übernommen wurden
`banner_key`, `settlement_banner_mesh` und `tier`.

**Wichtig – Clan-Flags:** Der Clan ist `is_bandit="true"` + `is_outlaw="true"`,
**nicht** `is_minor_faction`. Ein erster Versuch als Minor Faction stürzte im
Daily Tick ab: Minor Factions haben in Vanilla Lords, unserer hat keine, und die
tägliche Verwaltung lief ins Leere. Die Banditenfraktion ist der einzige
Vanilla-Archetyp, der heldenlos ist, Parties besitzt und gegen alle feindlich
ist. `IsBanditFaction` hält den Clan zusätzlich aus der gesamten Adels-Logik
(Finanzen, Diplomatie, Erbfolge) heraus, die einen Anführer voraussetzt.

Die Kultur ist `Culture.looters`, passend zu den Truppen. Helden-Templates sind
bewusst weggelassen — Vanilla-Banditenfraktionen machen das genauso, wodurch die
Liste leer statt `null` ist.

---

## 3. Truppen

### 3.1 Klassen-Varianten (der Regelfall)

**Datei:** `ModuleData/zombie_troops_generated.xml`, erzeugt von
`tools/Generate-ZombieTroops.ps1`.

Zu jeder kampfrelevanten Vanilla-Truppe existiert ein Klon `zombie_<original_id>` –
aus `imperial_legionary` wird `zombie_imperial_legionary` mit demselben Level,
denselben Skills und derselben Rüstung. 514 Einträge aus drei Quellen
(`spnpccharacters.xml`, `bandits.xml`, `naval_characters.xml`), gefiltert auf
`occupation` ∈ {Soldier, Bandit, CaravanGuard, Mercenary, Gangster, Townsfolk,
Villager}; Helden, `Special` und `Wanderer` fallen raus.

Der Generator ändert am Klon nur:

| | |
|---|---|
| `occupation` / `culture` | `Bandit` / `Culture.looters`, passend zum Zombie-Clan |
| `upgrade_targets` | entfernt – Zombies steigen nicht auf, sie vermehren sich |
| `face` | `BodyProperty.zombie_male` / `zombie_female` |
| `default_group` | `Ranged` → `Infantry`, `HorseArcher` → `Cavalry` |
| `is_basic_troop`, `upgrade_requires` | entfernt |

**Fernkampfwaffen entfernt der Generator bewusst nicht.** Gecraftete Waffen tragen
im XML keinen Typ, ein Filter über Item-IDs wäre also raterei. Stattdessen räumt
`ZombieTroopSetup` das zur Laufzeit auf, wo `WeaponComponentData.IsRangedWeapon`
und `.IsAmmo` die Frage eindeutig beantworten. Reittiere bleiben unangetastet:
Zombies reiten, sie schießen nur nicht.

### 3.2 Tier-Truppen (Fallback)

**Datei:** `ModuleData/zombie_troops.xml`

Sechs Einträge `zombie_tier_1` … `zombie_tier_6`. Das Tier ergibt sich in
Bannerlord aus dem Level:

```
Tier = clamp(ceil((Level - 5) / 5), 0, 6)      // DefaultCharacterStatsModel
```

Daraus die gewählten Level: 10, 15, 20, 25, 30, 35 → Tier 1–6.

Sie greifen nur noch, wenn zu einer getöteten Truppe keine Variante existiert –
etwa bei Truppen aus anderen Mods. Ausrüstung ist rein Nahkampf. Ein früherer
Zwischenstand hatte eine Schleuder ohne Munition (Konzept: „sichtbar, aber
unbenutzbar"); das wurde verworfen, weil sie im Kampf trotzdem als Fernkampfwaffe
auftrat.

### 3.3 Grüne Haut: eine eigene Race

**Dateien:** `ModuleData/skins.xml` (generiert), `zombie_monsters.xml`,
`ModuleData/project.mbproj`

Hautfarbe ist in Bannerlord kein freier RGB-Wert, sondern ein Offset in einen
Farbverlauf **pro Race** – und dieser Verlauf steht als Klartext-XML in
`Native/ModuleData/skins.xml`:

```xml
<skin_color_gradient_point point="0.90, 0.80, 0.72" />
```

Es sind RGB-Multiplikatoren auf die Hauttextur; deshalb ist der erste Eintrag
`1.0, 1.0, 1.0`. Damit ist Grün ohne jedes neue Asset erreichbar — die
gegenteilige Aussage in einer früheren Fassung dieses Dokuments war falsch.

**Warum eine eigene Race und nicht die menschliche Palette:** Der in den
Body-Properties gespeicherte Offset ist auf 0..1 normiert. Punkte anzuhängen oder
zu ersetzen verschiebt damit den Hautton *jedes* Vanilla-NPCs. Eine eigene Race
bringt ihren eigenen Verlauf mit und lässt alles andere unberührt.

`Generate-ZombieRace.ps1` klont dafür die komplette `<race id="human">` nach
`<race id="zombie">` und rechnet jeden der 240 Farbpunkte auf Grün um; Meshes,
Deform-Keys, Haare, Bärte und Tattoos bleiben wörtlich stehen.

Drei Dinge mussten dafür stimmen:

1. **Der Merge.** `MBObjectManager.MergeElements` schlüsselt `<race>` über sein
   `id`-Attribut (`soln_skins.xsd`, `race_unique_attribute`) und hängt Elemente
   mit unbekannter ID einfach an. Eine neue Race wird also angefügt, nicht in die
   menschliche hineingemergt. Beleg für den Mechanismus ist NavalDLC: dessen
   `skins.xml` enthält eine Teil-`<race id="human">` mit ausschließlich eigenen
   Haar- und Tattoo-Meshes.
2. **Die Registrierung.** `skins.xml` läuft **nicht** über `<Xmls>` in
   `SubModule.xml`, sondern über `XmlResource.MbprojXmls`
   (`Module.CreateProcessedSkinsXMLForNative` → `GetMergedXmlForNative("soln_skins")`).
   Dafür gibt es `ModuleData/project.mbproj`.
3. **Das Monster.** `FaceGen.GetBaseMonsterFromRace` löst über den Race-*Namen*
   auf, die Race braucht also ein `Monster` mit exakt dieser ID. Beide Einträge in
   `zombie_monsters.xml` erben per `base_monster` von ihrem menschlichen Gegenstück.

Zugewiesen wird die Race in `ZombieTroopSetup`, nicht im Truppen-XML:
`FaceGen.GetRaceOrDefault` ist trotz des Namens ein simpler Dictionary-Zugriff und
würde beim Laden werfen, wenn die Race nicht ankommt. So kostet ein Fehlschlag nur
die grüne Haut und nicht den Kampagnenstart – `zombie.races` zeigt im Spiel, ob
sie registriert wurde.

Die Gesichts-Templates in `zombie_bodyproperties.xml` sind aus demselben Grund
eigene Objekte: `MBBodyProperty` ist ein `MBObjectBase`, also global geteilt.

---

## 4. Party-Erzeugung

**Datei:** `src/Infrastructure/ZombieSpawner.cs`

Zentraler Punkt: **jede** Zombie-Party entsteht hier. Der Ablauf spiegelt
bewusst Vanillas `BanditSpawnCampaignBehavior.SpawnLooterParty` Schritt für
Schritt, weil jede Abweichung davon zu nativen Abstürzen geführt hat:

1. **Anker-Siedlung** — nur Stadt oder Dorf, nie Versteck, Burg oder Hafen.
2. **Position** — `NavigationHelper.FindPointAroundPosition(...)` bzw.
   `FindReachablePointAroundPosition(...)`, also navmesh-validiert. Die rohe
   `Settlement.GatePosition` zu verwenden ist gefährlich: `CampaignVec2.Face`
   wird lazy über den nativen `MapSceneWrapper.GetFaceIndex()` aufgelöst, und
   eine für Landparties ungültige Fläche führt direkt in nativen Code.
3. **Erzeugung** — `BanditPartyComponent.CreateLooterParty(...)`, **immer** mit
   echtem Party-Template. Mit `null` als Template entsteht die Party mit leerem
   Roster, und die Engine baut das Karten-Icon bereits während der Erzeugung —
   ohne Truppe gibt es keine Figur zum Rendern.
4. **Zusammensetzung** — gewünschte Truppen werden **zuerst hinzugefügt**, erst
   danach die Starter-Truppen des Templates entfernt. So fällt das Roster nie
   auf null.
5. **Nachbereitung** (`InitializeZombieParty`) — entspricht Vanillas
   `InitializeBanditParty`: `Party.SetVisualAsDirty()`, `Aggressiveness`,
   `InitializePartyTrade(...)`, danach ein erster KI-Befehl
   (`SetMovePatrolAroundPoint`).

Party-IDs müssen nicht selbst eindeutig gemacht werden — `MobileParty.CreateParty`
ruft intern `FindNextUniqueStringId` auf.

---

## 5. Wachstum nach Schlachten

**Dateien:** `Behaviors/ZombiePlagueCampaignBehavior.cs`,
`Infrastructure/ZombieConversion.cs`, `Patches/BlockDeserterSpawnPatch.cs`

Regel: **strikt 1:1 pro Klasse.** Jeder Mann, den die Gegenseite in dieser
Schlacht verliert, kommt als sein eigener Zombie zurück – ein Imperial Legionary
als `zombie_imperial_legionary`, nicht als generischer Tier-5-Zombie.

### Vier Quellen, ein Hook

`MapEventParty` führt die Buchhaltung bereits selbst
(`MapEventParty.cs:65-69`) und die Roster leben noch bei `MapEventEnded` –
die Vanilla-`CampaignBattleRecoveryBehavior` liest sie dort ebenfalls aus:

| Quelle | Zustand nach der Schlacht |
|---|---|
| `DiedInBattle` | gefallen, bereits aus dem Roster entfernt |
| `RoutedInBattle` | geflohen, ebenfalls schon entfernt |
| `WoundedInBattle` | steht noch im Roster, als verwundet markiert |
| `PrisonRoster`-Differenz | von Bannerlord als Gefangene übergeben |

Gefüllt werden alle drei sowohl in der Live-Mission als auch im Auto-Resolve –
die Simulationsschleife ruft dieselben `MapEventSide.OnTroop*`-Methoden auf
(`MapEventSide.cs:982-1078`). Ein Harmony-Patch auf `OnTroopKilled` ist damit
überflüssig geworden und wurde entfernt.

### Die Formel

```
gewinn(c) = tot(c) + geflohen(c) + verwundet(c)
          + max(0, gefangen(c) - verwundet(c))
```

Die einzige Überschneidung ist ein Mann, der verwundet **und** danach gefangen
wurde, weil seine Party vernichtet wurde – er steht in beiden Töpfen. Deshalb
zählt „gefangen" nur den Überschuss über die Verwundeten hinaus:

- Gegner vernichtet: `gefangen = verwundet + gesund` → Überschuss = die Gesunden,
  Summe `tot + geflohen + verwundet + gesund`.
- Gegner zieht sich zurück: `gefangen = 0` (`MapEvent.CaptureDefeatedPartyMembers`
  bricht bei `RetreatingSide != None` sofort ab) → Summe `tot + geflohen + verwundet`.

Die Gefangenen-Differenz bleibt ein Vorher/Nachher-Vergleich mit einem Snapshot
bei `MapEventStarted`, damit früher erbeutete Gefangene nicht mitzählen.

### Drei Nebenwirkungen

1. **Verwundete werden entzogen.** Zieht sich der Gegner zurück, heilen seine
   frisch Verwundeten nicht – sie werden aus seinem Roster entfernt und die Horde
   bekommt sie. Gedeckelt auf `GetElementWoundedNumber`, damit Verwundete aus
   einer früheren Schlacht verschont bleiben und bereits gefangen genommene nicht
   doppelt abgezogen werden. Gilt auch für die Spieler-Party.
2. **Zombies nehmen keine Gefangenen.** Die eben gezählte Differenz wird direkt
   wieder aus der `PrisonRoster` der Horde entfernt. Aus „gefangen" wird damit
   „umgewandelt", und keine Zombie-Party schleppt je einen Gefangenen mit.
3. **Vanilla-Deserteure werden unterdrückt.** `DesertersCampaignBehavior` baut
   seinen Pool aus genau `RoutedInBattle + DiedInBattle` der Verliererseite und
   macht daraus ab 15 Mann mit 90 % eigene Parties. Ohne den Prefix in
   `BlockDeserterSpawnPatch` liefe jeder Gefallene zweimal über die Karte.

Helden bleiben in allen vier Töpfen außen vor: Ein gefangener Lord ist Sache des
Spiels, nicht der Horde. Überlaufende Lords sind ein eigenes, späteres Feature.

Das Wachstum wird erst **nach** Schlachtende angewendet, nie live.

**Niederlage:** Überlebt die Zombie-Party nicht, geht die Zählung nicht
verloren — sie wird zwischengespeichert und bei `MobilePartyDestroyed` für eine
Ersatz-Party an einem entfernten Ort verwendet.

**Mehrparteien-Schlachten** bleiben die bekannte v0.1-Vereinfachung: die erste
gefundene Zombie-Party bekommt alles gutgeschrieben, was die Gegenseite verloren
hat.

`zombie.last_battle` gibt die Bilanz der letzten Schlacht pro Truppentyp aus
(tot / geflohen / verwundet / gefangen → welche Variante), damit sich das
Ergebnis nachrechnen und nicht nur beobachten lässt.

---

## 6. Skalierung

**Split:** Täglicher Tick. Unter 300 Mann nichts, ab 400 garantiert, dazwischen
linear steigende Wahrscheinlichkeit. Das Roster wird hälftig auf eine neue Party
in der Nähe aufgeteilt.

**Dorf-Raid ab 100 / Belagerung ab 400 Mann:** Bewusst **nicht** über einen
Patch der internen KI-Bewertungslogik gelöst — das hätte alle Banditenparties im
Spiel betroffen. Stattdessen über die offiziellen Order-APIs
`SetPartyAiAction.GetActionForRaidingSettlement` bzw.
`GetActionForBesiegingSettlement`, die auch das Spiel selbst für erzwungene
Party-Befehle nutzt. Diese Methoden prüfen intern das aktuelle Verhalten und
sind damit idempotent — tägliches Aufrufen ist unschädlich.

---

## 7. Feindseligkeit und Dialog

Über das Banditen-Flag ist der Clan bereits gegen alle feindlich. Zusätzlich
wird bei `OnSessionLaunched` explizit Krieg gegen alle Königreiche und alle
übrigen Clans erklärt, damit niemand über einen Diplomatie-Pfad neutral bleibt.

Der Dialog ist eine einzelne `AddDialogLine` mit Priorität 200:
`„Uuuuuuuhhh…"`, Ausgang direkt `close_window`. Ohne diese Zeile griff die
Engine auf `default_conversation_for_wrongly_created_heroes` zurück.

---

## 8. Unterdrückung des Vanilla-Auto-Spawns

**Datei:** `src/Patches/BlockVanillaBanditSpawnPatch.cs`

Das Banditen-Flag hat einen Nebeneffekt: Vanillas Banditen-Spawner nimmt den
Clan mit auf. Sein Looter-Budget stammt aus

```csharp
Math.Min(Hideout.All.Count(x => x.IsInfested) * 7, …)
```

also aus **allen** verseuchten Verstecken der Welt, nicht nur denen des Clans.
Ergebnis waren nächtlich über die ganze Karte verstreute Zombie-Parties.

Zwei Harmony-Prefixes auf `SpawnLooterParty` und `SpawnBanditParty` blockieren
das ausschließlich für unseren Clan. Bewusst an den *Blattmethoden* angesetzt,
die tatsächlich Parties erzeugen — damit sind alle Aufrufpfade abgedeckt
(nächtlicher Tick, Neuspiel-Seeding, Hideout-Nachfüllung).

---

## 9. Logging und Diagnose

**Datei:** `src/Infrastructure/ZombieLog.cs`

Primäres Ziel ist der **Engine-Log** (`Debug.Print` →
`C:\ProgramData\Mount and Blade II Bannerlord\logs\rgl_log_*.txt`), zusätzlich
eine Kopie als `zombieplague.log` im Modulordner.

Wichtige Umgebungs-Erkenntnis: Schreibzugriffe des Spielprozesses in
`Dokumente\…` schlagen auf diesem System **stillschweigend fehl** (vermutlich
Windows-Ordnerschutz). Ein erster Logger schrieb dorthin und erzeugte nie eine
Datei — der Fehler verschwand im `try/catch`. Der Engine-Log funktioniert
nachweislich und wird laufend rausgeschrieben, überlebt also auch harte native
Abstürze. **Nicht auf Documents zurückwechseln.**

Jeder Konsolenbefehl schreibt bei Erfolg eine `SUCCESS`-Zeile, damit im Log
greppbar ist, was durchlief und was nicht. Bei `OnSessionLaunched` läuft
zusätzlich `LogObjectDiagnostics()` und protokolliert, ob alle Truppen,
Templates, die Kultur und der Clan aufgelöst werden konnten.

---

## 10. Konsolenbefehle

Erreichbar ingame über **Alt+~**, Gruppe `zombie`.

| Befehl | Zweck |
|---|---|
| `spawn_zombies <n> [tier]` | n Zombies des Tiers in Sichtweite |
| `spawn_near_player [n]` | Zombie-Party in Sichtweite |
| `t5_battle [n]` | n Looter vs. 2n Zombies nebeneinander – testet den Wachstums-Loop |
| `list` | alle Zombie-Parties mit Position, Stärke, Verhalten |
| `grow <n> [tier]` | Truppen zur nächsten Party – testet Schwellenwerte ohne Kampf |
| `kill_all` | alle Zombie-Parties entfernen |
| `toggle_autospawn` | Auto-Spawn bei Kampagnenstart umschalten |
| `last_battle` | Bilanz der letzten Zombie-Schlacht pro Truppentyp |
| `races` | registrierte Rassen + Palettengröße + Monster-Check |
| `skin_palette` | verfügbare Hautfarben der menschlichen Race ausgeben |
| `t0_looter` … `t4_zombie` | Bisektionsleiter, siehe unten |

### Bisektionsleiter

Jede Stufe ändert genau **eine** Variable gegenüber der vorherigen. Damit wurde
der Clan als Absturzursache eingekreist; das Werkzeug bleibt für künftige
Fehlersuche erhalten.

| Stufe | Clan | Truppe |
|---|---|---|
| `t0_looter` | Vanilla | Vanilla-Looter |
| `t1_clan` | Zombie | Vanilla-Looter |
| `t2_clone` | Zombie | 1:1-Klon des Looters aus eigener XML |
| `t3_oneroster` | Zombie | Klon mit nur einem Ausrüstungs-Set |
| `t4_zombie` | Zombie | echte `zombie_tier_1` |

---

## 11. Build

```bash
dotnet build "E:\Projects\ZombiePlagueMod\ZombiePlague\src\ZombiePlague.csproj" -c Release
```

Das Spiel muss dafür geschlossen sein — es sperrt sonst die DLL.
Referenzen zeigen relativ auf das Spielverzeichnis; `0Harmony.dll` kommt aus dem
`Bannerlord.Harmony`-Modul. Gestartet wird über
`Bannerlord.BLSE.Launcher.exe`, nicht über Vortex' Play-Button (Vortex kennt
diesen Mod nicht, da er per Junction eingebunden ist).

Die generierten XMLs sind eingecheckt und müssen nicht bei jedem Build neu
erzeugt werden. Nach einem Bannerlord-Update dagegen schon:

```bash
powershell -File "E:\Projects\ZombiePlagueMod\tools\Generate-ZombieTroops.ps1"
```

```bash
powershell -File "E:\Projects\ZombiePlagueMod\tools\Generate-ZombieRace.ps1"
```

Beide Skripte nehmen `-GameDir` entgegen, `Generate-ZombieRace.ps1` zusätzlich
`-Red`/`-Green`/`-Blue` zum Nachjustieren des Grüntons.
