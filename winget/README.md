# winget

DualPulse's package files for winget (`winget install elvisgraho.DualPulse`), in the same layout as
[microsoft/winget-pkgs](https://github.com/microsoft/winget-pkgs).

## First submission (once)

The first version goes in by hand, as a pull request to microsoft/winget-pkgs:

```powershell
winget install Microsoft.WingetCreate
wingetcreate submit --token <your GitHub token> winget/manifests/e/elvisgraho/DualPulse/0.3.0
```

`wingetcreate` forks winget-pkgs to your account and opens the pull request. A classic token with the `public_repo`
scope is enough. Microsoft's checks run on the pull request; a reviewer merges it, usually within a few days.

## Later versions

After the first version is in winget, add the repository secret `WINGET_TOKEN` (a classic token with `public_repo` and
`workflow` scopes). From then on, pushing a tag like `v0.4.0` also sends the new version to winget
(`.github/workflows/release.yml`, job `winget`). Pre-release tags (`v0.4.0-beta`) are skipped.
