# winget

Die Dateien hier sind Vorlagen. Der Release-Workflow füllt Version, Download-URL und SHA256 ein und hängt das Ergebnis
als `winget-manifests-<version>.zip` an den Release.

## Erste Version einreichen (einmalig, von Hand)

1. Release-Entwurf auf GitHub veröffentlichen (der Installer muss öffentlich herunterladbar sein).
2. `winget-manifests-<version>.zip` entpacken und lokal prüfen:
   ```powershell
   winget validate --manifest manifests\f\FelixKriechbaum\QuickCommands\<version>
   winget install --manifest manifests\f\FelixKriechbaum\QuickCommands\<version>
   ```
3. Den Ordner `manifests/f/FelixKriechbaum/QuickCommands/<version>` per Pull Request zu
   [microsoft/winget-pkgs](https://github.com/microsoft/winget-pkgs) hinzufügen
   (oder `wingetcreate submit <ordner> --token <token>`).

## Weitere Versionen (automatisch)

Sobald die erste Version in winget ist: Repository-Secret `WINGET_TOKEN` anlegen (klassischer GitHub-Token mit
`public_repo`). Danach reicht `.github/workflows/winget.yml` jede veröffentlichte Version selbst ein.
