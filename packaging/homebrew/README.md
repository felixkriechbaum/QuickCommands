# Homebrew

QuickCommands is distributed through its own tap, not the official `homebrew/cask`. Since September 1, 2026 the
official repo only accepts apps that pass Apple's Gatekeeper check (signed with a paid Apple Developer account and
notarized). Third-party taps can still ship apps that aren't notarized.

To install:

```sh
brew install --cask felixkriechbaum/tap/quickcommands
```

`quickcommands.rb` is a template. The release workflow fills in the version and SHA256, tests the install on a Mac
and attaches the finished file to the release.

## One-time setup

1. Create a public repo **`felixkriechbaum/homebrew-tap`** (the name has to start with `homebrew-`).
2. After publishing a release, copy its `quickcommands.rb` to `Casks/quickcommands.rb` in the tap.
3. Optional, to automate step 2: create a fine-grained token with "Contents: Read and write" for `homebrew-tap`
   only and save it as the secret **`HOMEBREW_TAP_TOKEN`** in the QuickCommands repo. From then on
   `.github/workflows/homebrew.yml` updates the tap for every published release.

## Gatekeeper

The app only has an ad-hoc signature, so macOS blocks the first launch. Go to System Settings → Privacy & Security
once and click "Open Anyway". The cask deliberately doesn't strip the quarantine flag itself. Getting rid of this
step entirely needs an Apple Developer account ($99/year) for signing and notarization.

Before uninstalling, remove the shell integration in the app's settings ("Remove integration completely"). An
`uninstall` block in the cask could do that automatically, but it would also run on every `brew upgrade` and switch
your commands off after each update.
