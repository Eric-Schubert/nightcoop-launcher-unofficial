# NightCoop Updater

Hält die Koop-Mod [NightCoop](https://nightcoop.net/download.html) für Cyberpunk 2077 aktuell.
Es gibt zwei Wege, die sich dieselbe Statusdatei im Spielordner teilen und sich daher mischen lassen:

- **Launcher** (`launcher/`): Fenster mit Status, Changelog, Update- und Play-Knopf und Spielstand-Sicherung.
- **Skript** (`Update-NightCoop.ps1`): ohne Oberfläche, für die geplante Aufgabe. Sichert keine Spielstände.

## Launcher

Die fertige Datei liegt nach dem Bauen unter `launcher\dist\NightCoopLauncher.exe` und braucht keine
installierte .NET-Laufzeit.

```powershell
cd launcher
dotnet test
dotnet publish src\NightCoopLauncher.App -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true -p:DebugType=none -o dist
```

| Projekt | Inhalt |
| --- | --- |
| `src/NightCoopLauncher.Core` | Release-Liste, Spielsuche, Installation, Spielstand-Sicherung; ohne Oberfläche |
| `src/NightCoopLauncher.App` | WPF-Oberfläche (MVVM) |
| `tests/NightCoopLauncher.Core.Tests` | xUnit-Tests der Logik |

**Spielstände:** Vor jedem Update wird `%USERPROFILE%\Saved Games\CD Projekt Red\Cyberpunk 2077` als ZIP nach
`%LOCALAPPDATA%\NightCoopLauncher\SaveBackups` gesichert (abschaltbar). Von den automatischen Sicherungen
bleiben die drei neuesten, manuelle werden nie gelöscht. Eine Wiederherstellung sichert zuerst den
aktuellen Stand und lässt sich so rückgängig machen.

## Skript

| Aufruf | Wirkung |
| --- | --- |
| `Update-NightCoop.cmd` | Prüft auf eine neue Version und installiert sie. |
| `Play-NightCoop.cmd` | Wie oben, startet danach das Spiel. |
| `Update-NightCoop.cmd -CheckOnly` | Meldet nur, ob ein Update vorliegt, und ändert nichts. |
| `Update-NightCoop.cmd -Force` | Installiert die aktuelle Version erneut. |
| `Update-NightCoop.cmd -RegisterTask` | Legt eine geplante Aufgabe an: bei der Anmeldung und täglich um 18:00 Uhr. |
| `Update-NightCoop.cmd -UnregisterTask` | Entfernt die geplante Aufgabe wieder. |

Das Spiel wird in Steam, GOG und Epic gesucht. Bei mehreren Installationen oder einem anderen
Ort hilft `-GamePath "D:\Spiele\Cyberpunk 2077"`. Die Spielversion wird aus der
`Cyberpunk2077.exe` gelesen und lässt sich mit `-GameVersion 2.5` überschreiben.

## Ablauf

1. `https://nightcoop.net/releases.json` laden und den neuesten, nicht zurückgezogenen Build wählen.
2. Das Archiv zur installierten Spielversion laden und seine SHA-256 gegen das Manifest prüfen.
3. In einen temporären Ordner entpacken, dann in den Spielordner kopieren.
4. Den Stand in `NightCoop.updater.json` im Spielordner festhalten.

Läuft das Spiel, wird nichts angefasst. Dateien, die ein neuer Build nicht mehr mitbringt, werden
nur in NightCoops eigenen Ordnern entfernt; RED4ext und die Dateien anderer Mods bleiben stehen.

## Rückgabewerte

| Code | Bedeutung |
| --- | --- |
| 0 | Aktuell oder erfolgreich aktualisiert |
| 1 | Fehler |
| 2 | Spiel läuft, Update übersprungen |
| 3 | Update verfügbar (nur mit `-CheckOnly`) |

Das Protokoll liegt unter `%LOCALAPPDATA%\NightCoopUpdater\updater.log`.
