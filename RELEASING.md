# Shipping Thermal

## Build

1. Update the three-part version in `VERSION` and the versioned example filenames in README/release notes.
2. Quit the copy you are rebuilding if it is running. The installer build uses `dist\release\Thermal` so it does not overwrite a development app in `dist\Thermal`.
3. Run `powershell -ExecutionPolicy Bypass -File scripts\build-installer.ps1`.
4. Run `Thermal.exe --self-test` and `Thermal.exe --render-test` from `dist\release\Thermal`. Confirm both reports begin with `PASS:`.

Outputs: `dist\Thermal-Setup-<version>.exe`, `dist\Thermal-windows-x64.zip`, and `dist\SHA256SUMS.txt`. Test data, diagnostics, build tools, and source are excluded.

The Inno Setup compiler and sensor downloads are version/hash pinned. Review their licensing before changing distribution or using the compiler commercially. Thermal itself is currently unsigned. Production code signing requires a signing certificate or a configured signing service; no signing credentials are stored in this repository. If you add signing, sign the application and launcher **before** packaging, then sign the finished installer and regenerate checksums.

## Test the user paths

- Clean Windows 10/11 x64 machine: installer checks .NET, installs all runtime files, offers the signed sensor driver, creates desktop/Start menu shortcuts, and launches.
- Existing driver: setup leaves the installed compatible driver alone.
- Driver declined/canceled: app still opens with clear limited-reading status; setup can be retried from Settings.
- Reopen shortcut while app is in tray: dashboard returns, with only one monitoring instance.
- Upgrade: quit the running app when prompted; history remains in LocalAppData.
- Uninstall: installed files and shortcuts go away; local data and shared driver remain.
- Portable ZIP: extract the whole ZIP, run `Start Thermal.exe`, accept the Windows prompt.
- Source ZIP without `vendor` or `dist`: extract it, double-click `Start Thermal.cmd`; first run downloads/builds, subsequent runs open the existing app.

For an isolated installer smoke test, build with `scripts\build-installer.ps1 -TestBuild`. This produces `Thermal-PackagingTest.exe` with a separate application ID and shortcut group. Do not distribute that test installer.

## Publish

Push the source changes, then push a version tag that exactly matches `VERSION`, prefixed with `v`. The Windows GitHub Actions workflow builds/tests and creates a **draft**, never an automatically published release. Review it and publish from GitHub. A manual workflow run only creates build artifacts.

Alternatively, attach the three local output files to a release manually. Keep the installer as the first download called out in the release notes. Include the unsigned-app note until signing is configured. Distribute licenses with the app and retain the upstream corresponding-source references.

Do not upload diagnostics, test histories, `vendor`, or the `Thermal-PackagingTest.exe` test installer.
