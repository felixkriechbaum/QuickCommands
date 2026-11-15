# QuickCommands

Eigene Terminal-Befehle per Oberfläche verwalten: Ein Wort wie `dev`, `mkcd` oder `open` führt ein Script aus,
in **PowerShell**, **cmd** oder **bash/zsh**. Speichern genügt, das Tool legt die Scripts an der richtigen Stelle ab.

![Hauptfenster](docs/screenshots/main-dark.png)

## Funktionen

- Liste aller Commands mit Suche, Shell-Filter und Badge pro Shell
- Code-Editor (AvaloniaEdit) mit Syntax-Highlighting für PowerShell, Batch und Shellscript
- Konfliktprüfung: warnt, wenn ein Name ein vorhandenes Programm, einen Alias oder einen Shell-Befehl überschreibt.
  Interne cmd-Befehle wie `mkdir` oder `cd` werden blockiert, weil cmd sie immer selbst ausführt.
- `qc` im Terminal listet deine Commands, `qc -a` die aller Shells, `qc reload` lädt neu
- Einrichtungsassistent beim ersten Start, Integration jederzeit wieder entfernbar
- Datenordner verlegbar (OneDrive, NAS, Git), um Commands zwischen Rechnern zu teilen
- Oberfläche auf Deutsch und Englisch, Hell/Dunkel nach System

## So funktioniert es

| Shell | Mechanismus | Verfügbar |
|---|---|---|
| PowerShell (pwsh 7, optional 5.1) | `~/.quickcommands/qc.ps1` definiert Funktionen. Eine markierte Zeile in `$PROFILE` lädt die Datei. | neues Terminal oder `qc reload` |
| cmd | eine `.cmd` pro Command in `~/.quickcommands/bin`. Der Ordner steht am Anfang des Benutzer-PATH. | sofort |
| bash / zsh | `~/.quickcommands/qc.sh` definiert Funktionen. Eine markierte Zeile in `.bashrc` bzw. `.zshrc` lädt die Datei. | neues Terminal oder `qc reload` |

- Maßgeblich ist `commands.json` im Datenordner (Standard: `~/.quickcommands`). Alle anderen Dateien werden daraus erzeugt.
- Weil es Funktionen bzw. Batch-Dateien im selben Prozess sind, funktioniert auch `cd` in einem Command.
- Ein Syntaxfehler in einem Command erzeugt nur eine Warnung. Die übrigen Commands laden trotzdem.
- Vor dem ersten Eingriff wird jedes Profil gesichert (`*.qc-backup`). Das Tool bearbeitet nur den Block
  zwischen `# >>> QuickCommands >>>` und `# <<< QuickCommands <<<`.
- Argumente schreibst du in der Syntax der jeweiligen Shell: `$args` (PowerShell), `%*` / `%1` (cmd), `"$@"` / `$1` (sh).

## Projektstruktur

```
src/QuickCommands.Core     Modelle, Speicherung, Generatoren, Profil-/PATH-Integration, Konfliktprüfung (ohne UI)
src/QuickCommands.App      Avalonia-Oberfläche (MVVM mit CommunityToolkit.Mvvm)
tests/QuickCommands.Tests  xUnit: Unit-Tests und Tests, die die erzeugten Scripts in pwsh, cmd und Git Bash ausführen
tools/QuickCommands.Screenshots  rendert die Oberfläche headless mit Beispieldaten nach docs/screenshots
```

## Entwickeln

```powershell
dotnet test                                    # alle Tests (Ausführungstests werden übersprungen, wenn eine Shell fehlt)
dotnet run --project src/QuickCommands.App     # App starten
dotnet run --project tools/QuickCommands.Screenshots -- docs/screenshots de
```

## Veröffentlichen

```powershell
dotnet publish src/QuickCommands.App -c Release -r win-x64 -o publish/win-x64
# später: -r osx-arm64 / -r linux-x64
```

Ergebnis ist eine einzelne, selbstständige `QuickCommands.exe` (ohne installiertes .NET lauffähig).
