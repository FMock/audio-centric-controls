# Packaging and verification

Two packages are produced:

| Package | Target | Contents |
|---|---|---|
| `AudioCentric.Controls.Core` | `net8.0` | Scales, display dynamics, state models, snapshots, plotting helpers; no UI dependency |
| `AudioCentric.Controls.Wpf` | `net8.0-windows` | The WPF controls; depends on Core at the same version |

Each package contains the library, its XML documentation (IntelliSense), a README and an icon. A `.snupkg` symbol package and Source Link let debuggers step into the sources.

## Build the packages

```
dotnet build Audio-Centric-Controls.sln -c Release
dotnet pack  Audio-Centric-Controls.sln -c Release --no-build
```

The packages go to `bin\x64\Release\packages\`. Only the two library projects are packable; tests and examples set `IsPackable=false`.

The libraries are pure IL and marked **AnyCPU**, so AnyCPU, x64 and ARM64 applications can all use them. The repository still builds only the x64 Debug and x64 Release solution configurations, and all output goes to `bin\x64\<Configuration>\`.

## Verify them: `scripts/verify-packages.ps1`

```
powershell -ExecutionPolicy Bypass -File scripts\verify-packages.ps1
```

The script:

1. Builds the solution (x64 Release, where warnings are errors) and runs all tests.
2. Packs both packages.
3. Checks that each package has its DLL, XML docs, README, icon and symbol package, and that Wpf depends on the matching Core version.
4. Builds `examples/PackageConsumer.Wpf` **from the packages only**. That app is not in the solution, does not import the repository's `Directory.Build.props`, references `AudioCentric.Controls.Wpf` with a `PackageReference`, and restores into its own `obj\packages` folder from the local feed in its `nuget.config`. A stale copy in the global NuGet cache can therefore never hide a problem.
5. Runs the consumer with `--smoke <png>`. It opens its window, lets the controls animate, checks that every expected control type is on screen, saves a screenshot and exits with code 0.

Use `-SkipTests` for a faster run. To use the consumer interactively after a run, start `bin\x64\Release\PackageConsumer\PackageConsumer.Wpf.exe`, or open `examples\PackageConsumer.Wpf\PackageConsumer.sln` in Visual Studio and press F5. The folder's `README.md` has the details.

## Versioning

The version is set once, as `<Version>` in `Directory.Build.props` (currently 0.1.0), and both packages share it. Follow semantic versioning. Until 1.0, minor versions may contain breaking API changes; list them in the release notes.

## Publishing (maintainers)

```
dotnet nuget push bin\x64\Release\packages\AudioCentric.Controls.Core.<version>.nupkg --api-key <key> --source https://api.nuget.org/v3/index.json
dotnet nuget push bin\x64\Release\packages\AudioCentric.Controls.Wpf.<version>.nupkg  --api-key <key> --source https://api.nuget.org/v3/index.json
```

Push Core first, because Wpf depends on it. `dotnet nuget push` uploads the matching `.snupkg` automatically. Build releases from a clean, committed tree, so the Source Link commit in the package points to code that exists on GitHub. On CI, set `CI=true` (GitHub Actions sets `GITHUB_ACTIONS`) to get deterministic paths in the PDBs.
