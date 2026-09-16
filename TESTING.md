# Verification

Verified on the target Windows PC on 2026-09-15.

- Native x64 build completed using the Windows .NET Framework compiler without warnings.
- Automated history checks: seven-day expiration on startup, future-timestamp rejection, one-minute aggregation, preserved peaks, null handling, atomic persistence round trip, preference persistence, invalid temperature filtering, and SSD warning/critical-limit filtering.
- Native WPF render/interaction checks: overview, settings, mini monitor, always-on-top state, dashboard restore, invalid threshold rejection, history-range buttons, sensor navigation, and close-to-tray behavior.
- Real hardware probe: AMD Ryzen 5 7600X3D, AMD integrated graphics, NVIDIA RTX 3090, Lexar NM790 storage, and motherboard sensors.
- Installed the official signed PawnIO 2.1.0.0 prerequisite after approval. Signature status was Valid; installer exit code was 0.
- Elevated 25-second end-to-end app test: 223 sensors, CPU approximately 61°C, GPU 31°C, SSD 53°C, and persisted history with no storage errors. Dashboard and mini-window images were inspected.
- Standard-user probe correctly reports CPU/drive temperature unavailable on this PC. Administrator launch is necessary for these sensors.
- The history test simulates expired records; a seven-day real-time soak test has not been performed. Mixed-DPI multi-monitor placement, sign-in auto-launch, and UAC restart flow are implemented but were not manually exercised end to end.

Test artifacts are generated under `dist\Thermal` and are explicitly excluded from the portable ZIP. Production history uses `%LOCALAPPDATA%\Thermal`; tests use isolated folders. Hardware readings above are snapshots, not promised steady-state temperatures.

## Distribution update

- Built the elevated entry-point launcher and Windows installer with pinned Inno Setup 6.7.3; the compiler runs as a portable tool under `vendor`.
- Installed a separate `Thermal Packaging Test` copy to a unique workspace folder, verified its runtime files and Start menu shortcut target, ran its app logic tests, then uninstalled it. App files and shortcut were removed. The running user app and shared sensor driver were not changed.
- Copied only source files into a fresh folder with spaces in its name, without `vendor` or `dist`. Ran the source starter using stock Windows PowerShell 5.1 with `-BuildOnly`. It downloaded/verified dependencies, compiled both executables, and passed the app logic tests.
- Added and passed named-event activation tests, including signal delivery and handle cleanup. The dashboard restore method passed the native UI tests. Reopening an elevated app from the installed shortcut still needs a manual cross-process check on a clean PC.
- Updated desktop render/interaction checks passed after the distribution changes.
- GitHub Actions configuration is included but has not been run on GitHub. No release has been published. Code signing has not been configured; app and installer are unsigned.
