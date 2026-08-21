# 🐱 NyaLauncher

> Ein moderner, plattformübergreifender, leichtgewichtiger Minecraft-Launcher, geboren für die Freiheit.
<br>
![.NET](img/badges/dotnet.svg)
![Avalonia](img/badges/avalonia.svg)
![Platform](img/badges/platform.svg)
![License](img/badges/license.svg)

---

## ✨ Übersicht

**NyaLauncher** ist ein plattformübergreifender Minecraft-Launcher, der mit **Avalonia UI 12.1.1** und **.NET 10** entwickelt wurde.<br>
Er ist nicht nur leichtgewichtig und schnell, sondern legt auch großen Wert auf **Datenschutz** und **individuelle Benutzeroberflächen**, sodass du beim Spielen die volle Kontrolle behältst.<br>
NyaLauncher ist freie Software. Abgesehen von den bei Bedarf beibehaltenen Bibliotheksdateien unterliegt der gesamte Code der [Apache License 2.0](LICENSE).<br>
Der Launcher führt keinerlei Telemetrie ohne dein Wissen durch, verletzt in keiner Weise deine Privatsphäre und erzwingt keinerlei Funktionseinschränkungen.

---

## 📦 Technologie-Stack

| Komponente                     | Technologie                       |
|--------------------------------|-----------------------------------|
| UI-Framework                   | Avalonia UI 12.1.1                |
| Laufzeitumgebung               | .NET 10                           |
| Komponenten-Erweiterungsvertrag | .NET 10, ohne Avalonia-Abhängigkeit |

---

## 🔧 Projektstruktur

| Projekt                              | Verantwortlichkeiten                                                          |
|--------------------------------------|------------------------------------------------------------------------------|
| NyaLauncher.Core                     | 🐱 Kernfunktionen zum Starten des Launchers von NyaLauncher                  |
| NyaLauncher.Avalonia                 | Frontend-UI von NyaLauncher, basierend auf Avalonia-Technologie              |
| NyaLauncher.Avalonia.Animations      | Animationsbibliothek für die Frontend-UI von NyaLauncher                    |
| NyaLauncher.Plugin.Abstractions      | UI-frameworkunabhängige Komponentenverträge, Geometrie, Elemente, Laufzeitzustand und Validierung |
| NyaLauncher.MinecraftTokenCrypto     | (**Closed-Source, da der Algorithmus nicht für die öffentliche Veröffentlichung geeignet ist**) Verschlüsselungsalgorithmus/Speicherung für die Anmeldetokens von Minecraft-Premiumkonten |

---

## 🔃 Update-Plan

### 📝 Versionsbenennungsregeln
| Phase            | Bedeutung                                                                       |
|------------------|---------------------------------------------------------------------------------|
| beta             | Schreibphase des Launchers, vollständig unbrauchbar                             |
| preview          | Testphase, teilweise nutzbar, aber für den Alltag nicht empfohlen (aktuelle Phase von 0.1.0preview-3) |
| release          | Offizielle Version, vollständig nutzbar                                         |
| gp (speziell)    | Spezifische Versionsnummer des newgui-Zweigs, entspricht dem preview des Hauptzweigs |

### Geplante Funktionen
- Plugin-Funktion (erfolgreich im Downstream-Zweig testplug verifiziert)
- Benutzerdefinierte Themen (voraussichtlich in der nächsten Preview-Version)
- Mehrsprachigkeit (Zeitpunkt noch offen)
- KI-gestützte Übersetzung/Fehlerprüfung (unbekannt)
- Online-Mehrspieler (???)

---

## 🛠️ Schnellstart

### 🪟🍎🐧 Systemanforderungen

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) oder neuer
- Windows 10+, macOS Ventura+, Linux Kernel 5.0+
- Desktop-Laufzeitumgebung (Windows/macOS/Linux)
> Die Portierung auf HarmonyOS steht noch aus.

### 🔧 Klonen und Erstellen

```bash
git clone https://github.com/redstore-noob/NyaLauncher.git
cd NyaLauncher
dotnet restore
dotnet build -c Release
```

---

## 📈 Änderungsprotokoll
Aktuelle Änderungen

v0.1.0-preview3
- Die newgui-Funktionen wurden vervollständigt und wieder in den main-Zweig gemerged
- Eine große Anzahl von Komponenten hinzugefügt
- Kleine Umstrukturierung des Core-Moduls (Fortschritt: 25/100%)
- Minecraft-bezogene Download-Funktionen hinzugefügt
- Log-Funktion hinzugefügt, um die vom Launcher/Spiel erzeugten Laufzeitdateien zu speichern
- Hardcodierte Stilprobleme im Frontend behoben und andere Probleme leicht optimiert
- Das Plugin-System befindet sich in der Testphase; die erste nutzbare API erscheint bald
- Redundanter/toter Code im Backend wurde entfernt
- Teile des animations-Moduls entfernt, Umstrukturierung folgt in Kürze
- Benennungsregeln der Versionsausgaben geändert
- Herobrine entfernt
![Screenshot des Hauptfensters von v0.1.0-preview3](img/v0.1.0preview-3-mainwindow.png)
![Screenshot der Spielverwaltung von v0.1.0-preview3](img/v0.1.0preview-3-game.png)
![Screenshot der Kontoverwaltung von v0.1.0-preview3](img/v0.1.0preview-3-account.png)

v0.1.0-gp2 (newgui-Zweig)

> `v0.1.0-gp2` kennzeichnet lediglich die zweite UI-Entwicklung von v0.1.0 newgui und wird nicht in die Core-Version übernommen.<br>Diese Version steht nicht mit dem main-Zweig in Zusammenhang.

- Die GUI wurde (im newgui-Zweig) umstrukturiert; die Startseite wurde zu anpassbaren Komponentenblöcken, wodurch die Gestaltungsfreiheit zunimmt (noch nicht vollständig; die alte GUI ist im main-Zweig erhalten)
- Offline-Start und Premium-/Online-Start hinzugefügt
- Fehler in der readme.md behoben (?)
- Multi-Account-Verwaltung hinzugefügt
- Konfigurationsspeicherung hinzugefügt; nach einmaliger Konfiguration bleibt sie endlich erhalten 😭
- Java-Suche optimiert; das frühere Problem, dass Java startbar, aber nicht nutzbar war, wurde behoben
- Herobrine entfernt
![Screenshot des Hauptfensters von v0.1.0-gp2](img/v0.1.0pre2-mainwindow.png)
![Screenshot des Startbildschirms von v0.1.0-gp2](img/v0.1.0pre2.png)
![Screenshot der Einstellungen von v0.1.0-gp2](img/v0.1.0pre2-settings.png)
![Screenshot der Personalisierung von v0.1.0-gp2](img/v0.1.0pre2-settings2.png)

v0.1.0-pre1
- Die GUI aus der Benutzeroberfläche in eine separate Bibliothek ausgelagert (NyaLauncher.Avalonia.Animations)
- Einige aufgetretene Bildruckler verbessert
- Herobrine entfernt
![Screenshot des Hauptfensters von v0.1.0-pre1](img/v0.1.0pre1.png)