# Homebrew

QuickCommands kommt über einen eigenen Tap, nicht über das offizielle `homebrew/cask`: Dort werden seit dem
1. September 2026 nur noch Apps aufgenommen, die Apples Gatekeeper-Prüfung bestehen (signiert mit einem bezahlten
Apple-Developer-Konto und notarisiert). Eigene Taps dürfen auch nicht notarisierte Apps anbieten.

Installieren (für Nutzer):

```sh
brew install --cask felixkriechbaum/tap/quickcommands
```

`quickcommands.rb` ist eine Vorlage. Der Release-Workflow füllt Version und SHA256 ein, testet die Installation auf
einem Mac und hängt die fertige Datei an den Release.

## Einmalig einrichten

1. Öffentliches Repo **`felixkriechbaum/homebrew-tap`** anlegen (der Name muss mit `homebrew-` beginnen).
2. Nach dem Veröffentlichen eines Releases dessen `quickcommands.rb` nach `Casks/quickcommands.rb` im Tap legen.
3. Optional automatisch: Fine-grained Token mit „Contents: Read and write“ nur für `homebrew-tap` anlegen und im
   QuickCommands-Repo als Secret **`HOMEBREW_TAP_TOKEN`** speichern. Danach aktualisiert `.github/workflows/homebrew.yml`
   den Tap bei jedem veröffentlichten Release.

## Gatekeeper

Die App ist nur ad-hoc signiert. macOS blockiert deshalb den ersten Start; einmal unter Systemeinstellungen →
Datenschutz & Sicherheit „Dennoch öffnen“ klicken. Der Cask entfernt die Quarantäne absichtlich nicht selbst.

Vor dem Deinstallieren die Integration in den Einstellungen entfernen („Integration komplett entfernen“). Ein
`uninstall`-Block im Cask würde das zwar automatisch machen, liefe aber auch bei jedem `brew upgrade` und würde die
Commands nach jedem Update abschalten.
Wer das komplett vermeiden will, braucht ein Apple-Developer-Konto (99 $/Jahr) für Signatur und Notarisierung.
