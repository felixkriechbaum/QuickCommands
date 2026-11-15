# winget

The files in here are templates. The release workflow fills in the version, download URL and SHA256 and attaches the
result to the release as `winget-manifests-<version>.zip`.

## Submitting the first version (once, by hand)

1. Publish the draft release on GitHub (the installer has to be publicly downloadable).
2. Unzip `winget-manifests-<version>.zip` and test it locally:
   ```powershell
   winget validate --manifest manifests\f\FelixKriechbaum\QuickCommands\<version>
   winget install --manifest manifests\f\FelixKriechbaum\QuickCommands\<version>
   ```
3. Add the folder `manifests/f/FelixKriechbaum/QuickCommands/<version>` to
   [microsoft/winget-pkgs](https://github.com/microsoft/winget-pkgs) with a pull request
   (or run `wingetcreate submit <folder> --token <token>`).

## Later versions (automatic)

Once the first version is in winget, add a repository secret `WINGET_TOKEN` (a classic GitHub token with
`public_repo`). From then on `.github/workflows/winget.yml` submits every published version by itself.
