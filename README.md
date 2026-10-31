# BunoTunner

Portable (no installation) Windows utility with a Windows XP look: fan control, GPU overclocking (NVIDIA/AMD/Intel), sensors, benchmark, RAM timings, process affinity, uninstaller, storage health (S.M.A.R.T.), internet test and Windows optimizations.

Utilitario portatil (sem instalacao) para Windows, com visual estilo Windows XP: ventoinhas, overclock de GPU, sensores, benchmark, timings de RAM, afinidade de processos, desinstalador, saude de armazenamento (S.M.A.R.T.), teste de internet e otimizacoes do Windows.

## Download

**[Download the latest version / Baixar a versao mais recente (BunoTunner.exe)](https://github.com/Buno-o/BunoTunner/releases/latest/download/BunoTunner.exe)**

All versions: [Releases](https://github.com/Buno-o/BunoTunner/releases)

- Single portable `.exe`, Windows 10/11 x64, .NET Framework 4.x (already included in Windows).
- Runs as administrator (needed to read hardware sensors and control fans/clocks).
- The app has an **update check** button (only runs when you click it) that compares your version with the latest release here.

## Code signing policy

Free code signing provided by [SignPath.io](https://signpath.io), certificate by [SignPath Foundation](https://signpath.org).

- Releases are built from the source code in this repository by GitHub Actions (see `.github/workflows/build.yml`) and submitted to SignPath for signing.
- Team roles: author, reviewer and approver: [Buno-o](https://github.com/Buno-o).
- Only binaries built from this repository are signed.

> Status: the signing request to the SignPath Foundation is pending; releases are not signed yet. This notice will be updated once signing is active.

## Privacy policy

BunoTunner does not collect, store or send personal data and has no telemetry. It only accesses the network when you ask for it:

- **Check for updates** button: contacts `api.github.com` and downloads the new `BunoTunner.exe` from this repository's Releases.
- **Windows driver updater**: uses the Windows Update service of your own PC.
- **Internet test**: downloads/uploads test data to the speed-test server.
- Donation buttons open PayPal / LivePix in your browser.

## Building from source / Como compilar

1. Put the LibreHardwareMonitor DLLs (0.9.6) in the `libs` folder (see `libs/COLOQUE-AS-DLLS-AQUI.txt`).
2. Run `build.bat` (uses the `csc.exe` that ships with Windows).

Full details and version history in `LEIA-ME.txt`.

## License

[MIT](LICENSE). Bundled third-party libraries (LibreHardwareMonitor, MPL-2.0) keep their original licenses.
