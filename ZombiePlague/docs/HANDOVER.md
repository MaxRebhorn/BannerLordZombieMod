# Zombie Plague – Handover

Stand: **6. September 2026**. Technische Details siehe
[IMPLEMENTATION.md](IMPLEMENTATION.md).

---

## Kurzfassung

Der Kern-Loop steht und ist im Spiel bestätigt: Zombie-Parties spawnen, kämpfen
und **wachsen nach gewonnenen Schlachten**. Der Mod kompiliert fehlerfrei und
lädt sauber. Die lange Kette von Abstürzen beim Spawnen ist behoben.

**Neu und noch nicht im Spiel getestet** (kompiliert, aber ungespielt):

- **514 Klassen-Varianten** – zu jeder Vanilla-Kampftruppe gibt es einen
  `zombie_*`-Klon. Die Horde wächst jetzt in echten Legionären und Kataphrakten
  statt in generischen Tier-Zombies.
- **Grüne Haut über eine eigene Race** `zombie`. Die frühere Aussage „über
  BodyProperties nachweislich unmöglich" war falsch: die Hautpalette steht als
  Klartext-XML in `skins.xml`. Details in IMPLEMENTATION.md §3.3.
- **Vollständige Konvertierung** – Tote, Geflohene, Verwundete und Gefangene der
  Gegenseite werden 1:1 zu Zombies. Zombies nehmen keine Gefangenen mehr, und
  Verwundete einer fliehenden Partei heilen nicht, sondern wechseln die Seite
  (auch beim Spieler).

Was jetzt zählt, ist ein Testdurchlauf: die drei Szenarien unten und ein
Stabilitätstest über mehrere Kampagnentage.

---

## Umgebung

| | |
|---|---|
| Spiel | Bannerlord **v1.4.8** (Build 119303), War Sails |
| Spielverzeichnis | `E:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord` |
| Modding Kit | `D:\SteamLibrary\steamapps\common\Mount & Blade II Bannerlord` |
| Mod-Quellcode | `E:\Projects\ZombiePlagueMod\ZombiePlague` |
| Einbindung | Verzeichnis-Junction nach `…\Modules\ZombiePlague` – Build landet sofort im Spiel |
| Loader | BLSE 1.7.2 + HarmonyX (beide über Vortex installiert) |
| Start | `bin\Win64_Shipping_Client\Bannerlord.BLSE.Launcher.exe` |

**Wichtig:** Nicht über Vortex' Play-Button starten. Vortex übergibt BLSE seine
eigene Modliste und kennt ZombiePlague nicht (der Mod hängt per Junction drin,
nicht über Vortex). Über den BLSE-Launcher erscheint er dagegen normal in der
Modul-Auswahl.

**Build:** Spiel vorher schließen, sonst ist die DLL gesperrt.
```bash
dotnet build "E:\Projects\ZombiePlagueMod\ZombiePlague\src\ZombiePlague.csproj" -c Release
```

**Logs:** `C:\ProgramData\Mount and Blade II Bannerlord\logs\rgl_log_*.txt`,
Zeilen mit `[ZombiePlague]`. Bei Abstürzen zusätzlich
`rgl_log_errors_*.txt`. Nicht in `Dokumente\…` suchen – Schreibzugriffe dorthin
scheitern auf diesem System stillschweigend.

---

## Was funktioniert

- **Spawn** – über Konsole, an navmesh-validierten Positionen, stabil
- **Wachstum nach Schlachten** – im Spiel verifiziert: 20 Zombies gegen 10
  Looter → 28 Zombies (damals noch mit Tier-Mapping und ohne Geflohene)
- **Zombie-Clan** als XML-Faction, korrekt initialisiert
- **Sechs Truppen-Tiers** `zombie_tier_1…6`, reine Nahkämpfer – jetzt nur noch
  Fallback für Truppen ohne eigene Variante
- **Feindseligkeit** gegen alle Fraktionen
- **Dialog** – „Uuuuuuuhhh…" statt der Fallback-Zeile
- **Vanilla-Auto-Spawn unterdrückt** – keine unkontrollierten Zombie-Parties mehr
- **Debug-Werkzeug** – 16 Konsolenbefehle inkl. Bisektionsleiter, Logging mit
  `SUCCESS`-Markern

## Was als Nächstes zu testen ist

Die neuen Sachen zuerst, weil sie zusammen den größten Umbau seit dem Kern-Loop
darstellen. Alles über `zombie.last_battle` nachrechenbar.

| # | Prüfung | Vorgehen | Erwartung |
|---|---|---|---|
| 1 | XML lädt | Kampagne starten, Log auf `[ZombiePlague]` | `troop setup: 514+ zombie troops`, keine `ERROR`-Zeile |
| 2 | Grüne Haut | `zombie.races` | `zombie` gelistet, Monster `ok`; danach Zombies ansehen |
| 3 | Fernkampf weg, Pferd da | Berittene Variante spawnen und ansehen | reitet, schießt nicht |
| 4 | **Szenario 1** | 50 Zombies vs. 15 Legionäre | Horde +15 Zombie-Legionäre, `zombie.list` zeigt 0 Gefangene |
| 5 | **Szenario 2** | Horde gegen überlegene KI-Party, die abdreht | Verwundete verschwinden aus der KI-Party, Horde wächst um genau diese Zahl |
| 6 | **Szenario 3** | Kleine Horde von großer Armee vernichten lassen | Ersatz-Party spawnt mit Toten + Verwundeten der Gegenseite |
| 7 | Keine Deserteure | Szenario 3 wiederholen | keine Deserteurs-Party in Kampfnähe |
| 8 | Spieler-Infektion | Selbst gegen Zombies kämpfen | eigene Verwundete sind nach der Schlacht weg |
| 9 | Kein Kollateralschaden | Zwei Vanilla-Lords kämpfen lassen | Deserteure und Gefangene normal, keine grünen Vanilla-NPCs |

Weiterhin offen aus dem vorigen Stand:

- **Split ab 300–400 Mann** – implementiert, nie ausgelöst. Testbar mit
  `zombie.grow 350`, dann einen Tag vergehen lassen
- **Dorf-Raid ab 100 / Belagerung ab 400** – implementiert, nie beobachtet
- **Auto-Spawn beim Kampagnenstart** – steht auf **AUS**
  (`ZombieIds.AutoSpawnOnNewGame`), war eine Debug-Maßnahme. Vor dem ersten
  echten Durchlauf wieder einschalten
- **Langzeitstabilität** über viele Kampagnentage

---

## Offene Punkte

### 1. Grüne Haut braucht die Bestätigung im Spiel
Umgesetzt über eine eigene Race `zombie` (IMPLEMENTATION.md §3.3), aber noch nicht
gespielt. Der Weg über die menschliche Palette wurde bewusst verworfen: der
Hautton-Offset ist auf 0..1 normiert, jede Änderung an der Palette würde alle
Vanilla-NPCs mitverschieben.

`zombie.races` beantwortet die Frage in einer Zeile. Steht `zombie` nicht in der
Liste, hat die Engine die Race nicht registriert — dann bleibt die Haut normal,
und der Rest des Mods läuft unverändert weiter (die Race wird in Code zugewiesen,
nicht im Truppen-XML, genau damit ein Fehlschlag nichts kaputt macht).

Grünton nachjustieren: `tools/Generate-ZombieRace.ps1 -Green 1.0 -Red 0.4`.

### 2. Balance kippt nach oben
Die Horde wächst jetzt in echten Elite-Varianten inklusive Kavallerie statt in
generischen Tier-Zombies. Die Schwellenwerte 100 / 300–400 / 400 für Raid, Split
und Belagerung stammen aus dem Konzept und waren schon vorher ungetestete
Setzwerte — sie werden vermutlich nach unten müssen.

### 3. Truppen ohne Variante fallen auf Tier zurück
Truppen aus anderen Mods und alles, was der Occupation-Filter des Generators
auslässt, wird weiterhin auf `zombie_tier_N` gemappt. Rekruten und Helden haben
rechnerisch Tier 0 und würden zu Tier-1-Zombies; Helden sind inzwischen überall
ausgenommen, für Rekruten ist es sinnvoll.

### 4. Zuordnung bei Armeen
Stehen mehrere Parteien auf einer Seite, wird die erste gefundene Zombie-Party
gutgeschrieben, ohne saubere Verursacher-Zuordnung. Bewusste v0.1-Vereinfachung
aus der Recherche.

### 5. Debug-Ballast
`zombie_debug_troops.xml` und die Leiter-Befehle `t0`–`t4` sind reine
Diagnose-Werkzeuge. Vor einer Veröffentlichung entfernen oder hinter einem
Schalter verstecken. Ebenso die 10-Sekunden-Statusmeldung im Spiel.

---

## Bewusst zurückgestellt (laut Konzept „spätere Version")

- Infektions-Mechanik für verwundete Truppen
- Helden können zu Zombies überlaufen (40 % bei Gefangennahme)
- Reduzierter Fernkampfschaden gegen Zombies
- Reichsweiter Eskalationsmechanismus

Technische Ansätze dazu stehen in `zombie-mod-recherche.md` und sollten vor der
Umsetzung erneut gegen den dann aktuellen Source-Stand geprüft werden.

---

## Empfohlene nächste Schritte

1. Die Prüfungen 1–9 aus „Was als Nächstes zu testen ist" durchgehen – zuerst 1
   und 2, die entscheiden, ob überhaupt geladen und eingefärbt wurde
2. Split, Raid und Respawn gezielt durchtesten (`zombie.grow`, `kill_all`)
3. `AutoSpawnOnNewGame` wieder aktivieren und eine Kampagne über mehrere Tage
   laufen lassen
4. Balance nachziehen, sobald klar ist, wie schnell die Horde mit echten
   Elite-Varianten wächst

---

## Schmerzhaft gelernt – bitte nicht wiederholen

Diese Punkte haben je einen halben Abend gekostet:

- **Clans nie mit `Clan.CreateClan()` erzeugen.** Die Hülle ist unvollständig
  und der native Renderer stürzt beim Party-Icon ab. Immer über XML.
- **Parties nie mit `null`-Template erzeugen.** Leeres Roster + Icon-Erzeugung
  während `CreateLooterParty` = nativer Absturz.
- **Nie die rohe `Settlement.GatePosition` als Spawn-Position.** Immer über
  `NavigationHelper`, sonst landet man in ungültigen Navmesh-Flächen.
- **Kein `is_minor_faction` ohne Lords.** Vanillas tägliche Minor-Faction-Logik
  läuft ins Leere. Für heldenlose Fraktionen ist `is_bandit` der richtige
  Archetyp.
- **Nicht nach `Dokumente\…` loggen.** Schreibzugriffe scheitern dort still.
  Engine-Log via `Debug.Print` verwenden.
- **Bei nativen Abstürzen nicht raten.** `0xC0000005` ohne Symbole ist blind
  nicht lösbar. Der Weg, der funktioniert hat: Schritt-für-Schritt-Logging plus
  eine Bisektionsleiter, die je eine Variable ändert.
- **„Geht nicht" erst glauben, wenn der Ladepfad geprüft ist.** Die 24 Hauttöne
  aus `zombie.skin_palette` sahen nach einer Engine-Konstante aus. Sie stehen als
  Klartext in `Native/ModuleData/skins.xml`. Eine Runtime-Abfrage zeigt, was
  geladen wurde, nicht, woher es kommt.
- **Kein eigener Live-Hook, bevor man nach der Vanilla-Buchhaltung gesucht hat.**
  Der Harmony-Patch auf `OnTroopKilled` war überflüssig: `MapEventParty` führt
  `DiedInBattle`, `WoundedInBattle` und `RoutedInBattle` selbst mit, und sie leben
  noch bei `MapEventEnded`.
- **Nicht jedes ModuleData-XML läuft über `SubModule.xml`.** `skins.xml` wird über
  `ModuleData/project.mbproj` eingesammelt. Ein `<XmlNode>` dafür wird schlicht
  ignoriert – ohne Fehlermeldung.
- **In PowerShell-Werkzeugen `InvariantCulture` erzwingen.** Auf einem deutschen
  System liest `[double]"0.90"` als 90 und schreibt `0,9` zurück. Der erste Lauf
  von `Generate-ZombieRace.ps1` hat genau das produziert.
